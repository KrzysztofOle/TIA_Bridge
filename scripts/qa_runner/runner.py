"""Sequential offline orchestrator. Project-specific behavior stays in configuration."""
from __future__ import annotations

import os
from pathlib import Path
import shutil
import signal
import subprocess
import threading
import time
import traceback
import uuid
import xml.etree.ElementTree as ET

from . import adapters
from .diagnostics import Logger, correlate, digest, redact, utc, verify_manifest, write_json
from .evaluator import STATUSES, aggregate, failure, group_failures, test_results
from .loader import Invalid, contained, load_scenario, read_json, require
from .reports import comparison, finalize


def copy_new(source, destination):
    with source.open("rb") as reader, destination.open("xb") as writer:
        shutil.copyfileobj(reader, writer)
    require(destination.is_file(), "Snapshot is not a regular file")


def git_state(root):
    try:
        def git(*args):
            return subprocess.check_output(["git", *args], cwd=root, text=True, stderr=subprocess.DEVNULL, timeout=15).strip()
        status = git("status", "--porcelain", "--untracked-files=all")
        return {"sha": git("rev-parse", "HEAD"), "state": "DIRTY" if status else "CLEAN", "status": status}
    except (OSError, subprocess.SubprocessError):
        return {"sha": None, "state": "UNAVAILABLE", "status": None}


def previous_run(results, project_id, profile, performance, selected=None):
    paths = sorted((p for p in results.glob("*/summary.json") if p.parent.name != selected), key=lambda p: p.parent.name, reverse=True)
    for path in paths:
        try:
            contained(results, path.parent.name)
            item = read_json(path, 16 * 1024 * 1024)
            if item["project_id"] == project_id and item["profile"] == profile and item.get("performance_profile") == performance:
                verify_manifest(path.parent)
                return item
        except (OSError, ValueError, KeyError):
            continue
    return None


def matches(root, pattern):
    # A wildcard is configuration-only. Resolve every match before opening it.
    paths = set()
    for path in root.glob(pattern):
        if path.is_file():
            contained(root, path.relative_to(root).as_posix())
            paths.add(path)
    require(len(paths) <= 5000, "Artifact discovery limit exceeded")
    return paths


def collect(root, patterns, before, output, logger, scenario, step):
    for pattern in patterns:
        for source in sorted(matches(root, pattern) - before):
            require(source.stat().st_size <= 128 * 1024 * 1024, "Collected artifact exceeds 128 MiB")
            destination = output / "collected" / source.relative_to(root)
            destination.parent.mkdir(parents=True, exist_ok=True)
            if destination.exists():
                continue
            # Preserve authoritative evidence byte-for-byte. Only approved synthetic sources.
            copy_new(source, destination)
            logger.event("QA_ARTIFACT_COLLECTED", "Collected approved new artifact", level="TRACE", scenario_id=scenario, step_id=step, log_reference=source.relative_to(root).as_posix())
            if source.suffix == ".jsonl":
                correlate(destination, logger, scenario, step)


def receipt(root, task, before, output):
    spec = task["receipt"]
    fresh = matches(root, spec["pattern"]) - before
    require(len(fresh) == 1, "Expected exactly one new receipt; missing/ambiguous execution evidence")
    path = fresh.pop()
    value = read_json(path, 16 * 1024 * 1024)
    status = value.get(spec["status_field"])
    require(status in STATUSES, "Missing/unknown receipt status")
    layers = {name: value.get(field, "NOT_RUN") for name, field in spec["layers"].items()}
    require(all(status in STATUSES for status in layers.values()), "Missing/unknown evidence layer")
    if spec.get("bundle"):
        parent = path.parent
        while parent != root and not (parent / "evidence-manifest.json").is_file():
            parent = parent.parent
        require(parent != root, "Receipt bundle is not sealed")
        manifest = verify_manifest(parent)
        require(manifest["run_id"] == value["run_id"], "Receipt/manifest Run ID mismatch")
        destination = output / "receipt-bundle"
        shutil.copytree(parent, destination, symlinks=False)
        verify_manifest(destination)
    else:
        copy_new(path, output / "receipt.json")
    counts = dict(passed=0, failed=0, skipped=0)
    for field in spec.get("counts", {}).values():
        source = value.get(field)
        if source is None and status in {"BLOCKED", "FAIL", "NOT_RUN"}:
            continue
        require(isinstance(source, dict), "Missing receipt test counts")
        for key in counts:
            require(type(source.get(key)) is int and source[key] >= 0, "Invalid receipt test count")
            counts[key] += source[key]
    require(not spec.get("counts") or status != "PASS" or sum(counts.values()) > 0, "Receipt has no executed tests")
    messages = [value.get("failure") or value.get("preflight_error")]
    if "diagnostics" in spec:
        detail_path = contained(path.parent, spec["diagnostics"]["path"])
        if detail_path.exists():
            detail = read_json(detail_path, 16 * 1024 * 1024)
            for field in spec["diagnostics"]["fields"]:
                content = detail.get(field)
                if isinstance(content, list):
                    messages += [str(item) for item in content[:10]]
                elif isinstance(content, str):
                    messages.append(content)
    return status, layers, counts, "; ".join(str(item) for item in messages if item)[:2000]


def run(root, project, config_path, profile, scenario_path=None, performance=None, level="INFO", cancelled=None):
    started = time.monotonic()
    run_id = utc().replace("-", "").replace(":", "").replace(".", "").replace("+0000", "Z") + "-" + uuid.uuid4().hex
    results = contained(root, project["results_root"])
    results.mkdir(parents=True, exist_ok=True)
    lock = results / ".runner.lock"
    # Fail closed on concurrent/stale ownership. No automatic adoption or deletion.
    with lock.open("x", encoding="utf-8") as stream:
        stream.write(str(os.getpid()))
    folder = results / run_id
    cancelled = cancelled or threading.Event()
    old_signals = {}
    logger = None
    try:
        folder.mkdir(exist_ok=False)
        (folder / "logs").mkdir()
        (folder / "artifacts").mkdir()
        (folder / "artifacts/scenarios").mkdir()
        logger = Logger(folder / "logs/runner.jsonl", run_id, level)
        logger.event("QA_RUN_STARTED", "Offline run started")
        summary = {"schema_version": "1.0", "project_id": project["project_id"], "profile": profile,
                   "run_id": run_id, "started_at": utc(), "status": "NOT_RUN", "git": git_state(root),
                   "performance_profile": performance, "performance_definition_sha256": None,
                   "config_sha256": digest(config_path), "steps": [], "tests": dict(passed=0, failed=0, skipped=0),
                   "layers": {"functional": "NOT_RUN", "environment_qualification": "NOT_RUN", "engineering": "NOT_RUN", "runtime": "NOT_RUN"},
                   "report_path": str(folder / "report.md")}
        execution = {"schema_version": "1.0", "run_id": run_id, "runtime_paths": {}, "scenarios": [], "commands": []}
        copy_new(config_path, folder / "artifacts/qa.project.json")
        if threading.current_thread() is threading.main_thread():
            for sig in [signal.SIGINT, signal.SIGTERM]:
                old_signals[sig] = signal.signal(sig, lambda *_: cancelled.set())
        try:
            if "performance_profiles" in project:
                spec = project["performance_profiles"]
                performance = performance or spec["default"]
                require(performance in spec["allowed"], "Unapproved performance profile")
                summary["performance_profile"] = performance
                definition = contained(root, spec["definition"])
                summary["performance_definition_sha256"] = digest(definition)
                copy_new(definition, folder / "artifacts/performance-profiles.json")
            scenarios = []
            if scenario_path:
                paths = [contained(root, scenario_path)]
            else:
                require(profile in project["profiles"], "Unknown profile: " + profile)
                paths = [contained(contained(root, project["scenario_root"]), entry) for entry in project["profiles"][profile]]
            for path in paths:
                scenario = load_scenario(path, root, project)
                require(scenario["id"].casefold() not in {s["id"].casefold() for s in scenarios}, "Duplicate scenario id (case-insensitive)")
                scenarios.append(scenario)
                execution["scenarios"].append({"path": path.relative_to(root).as_posix(), "sha256": digest(path)})
                copy_new(path, folder / "artifacts/scenarios" / (scenario["id"] + ".json"))
                for step in scenario["steps"]:
                    summary["steps"].append({"scenario_id": scenario["id"], "step_id": step["id"], "task": step["task"], "required": step["required"], "status": "NOT_RUN", "duration_ms": 0, "failures": []})
            for scenario in scenarios:
                deadline = time.monotonic() + scenario["timeout_seconds"]
                halted = False
                for definition in scenario["steps"]:
                    step = next(s for s in summary["steps"] if s["scenario_id"] == scenario["id"] and s["step_id"] == definition["id"])
                    if halted or cancelled.is_set():
                        continue
                    task = project["tasks"][definition["task"]]
                    key = scenario["id"] + "--" + definition["id"]
                    output = folder / "artifacts" / key
                    output.mkdir()
                    log_path = folder / "logs" / (key + ".log")
                    step["log_reference"] = log_path.relative_to(folder).as_posix()
                    clock = time.monotonic()
                    logger.event("QA_STEP_STARTED", "Starting approved task", scenario_id=scenario["id"], step_id=definition["id"])
                    try:
                        require(time.monotonic() < deadline, "Scenario deadline exhausted before step")
                        for name in {*scenario["requires"], task["runtime"]}:
                            if name not in execution["runtime_paths"]:
                                execution["runtime_paths"][name] = adapters.runtime(project["runtimes"][name])
                        argv = adapters.command(task, execution["runtime_paths"][task["runtime"]], root, output, performance)
                        logger.event("QA_COMMAND_PREPARED", "Approved argv prepared", level="DEBUG", scenario_id=scenario["id"], step_id=definition["id"], argv=[redact(arg) for arg in argv])
                        execution["commands"].append({"scenario_id": scenario["id"], "step_id": definition["id"], "argv": [redact(arg) for arg in argv]})
                        environment = os.environ.copy()
                        # Project policy controls inherited switches; the engine has no vendor keys.
                        for key_env in list(environment):
                            if any(key_env.startswith(prefix) for prefix in project.get("environment", {}).get("clear_prefixes", [])):
                                del environment[key_env]
                        environment.update(QA_RUN_ID=run_id, PYTHONIOENCODING="utf-8", PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_UI_LANGUAGE="en", DOTNET_CLI_TELEMETRY_OPTOUT="1")
                        environment.update({key: adapters.expand(item, output, performance) for key, item in project.get("environment", {}).get("values", {}).items()})
                        environment.update({key: adapters.expand(item, output, performance) for key, item in task.get("env", {}).items()})
                        patterns = task.get("collect", [])
                        before = set().union(*(matches(root, pattern) for pattern in patterns))
                        receipt_before = matches(root, task["receipt"]["pattern"]) if "receipt" in task else set()
                        exit_code, reason, elapsed = adapters.execute(argv, root, environment, log_path, min(definition["timeout_seconds"], deadline - time.monotonic()), cancelled)
                        step.update(exit_code=exit_code, duration_ms=elapsed, status="PASS")
                        if reason:
                            step["status"] = "BLOCKED" if reason == "INTERRUPTED" else "FAIL"
                            step["failures"].append(failure("ENVIRONMENT" if reason == "INTERRUPTED" else reason, reason))
                        elif task["type"] in {"dotnet_test", "python_test"}:
                            try:
                                counts, errors = test_results(task, output)
                                step["tests"] = counts
                                step["failures"] += errors
                                if counts["failed"]:
                                    step["status"] = "FAIL"
                                elif counts["skipped"]:
                                    step["status"] = "BLOCKED"
                                    step["failures"].append(failure("ENVIRONMENT", "Skipped tests prevent complete execution"))
                            except Exception as error:
                                if exit_code == 0:
                                    raise
                                step["status"] = "FAIL"
                                step["failures"].append(failure("PROCESS_FAILURE", "Test process exit " + str(exit_code) + "; " + str(error)))
                        if "receipt" in task and not reason:
                            status, layers, counts, message = receipt(root, task, receipt_before, output)
                            step["receipt_status"], step["layers"], step["tests"] = status, layers, counts
                            summary["layers"].update(layers)
                            verdicts = [{"status": v, "required": True} for v in [status, *layers.values()]]
                            step["status"] = aggregate(verdicts)
                            if step["status"] != "PASS":
                                step["failures"].append(failure("UNKNOWN", message or "Authoritative receipt/layer is " + step["status"] + "; inspect receipt-bundle"))
                        if exit_code != 0 and not reason:
                            step["status"] = "FAIL" if step["status"] == "PASS" else step["status"]
                            if not step["failures"]:
                                category = "BUILD_FAILURE" if task["type"] in {"dotnet_restore", "dotnet_build"} else "PROCESS_FAILURE"
                                step["failures"].append(failure(category, "Process exit code " + str(exit_code)))
                        collect(root, patterns, before, output, logger, scenario["id"], definition["id"])
                    except FileNotFoundError as error:
                        step["status"] = "BLOCKED"
                        step["failures"].append(failure("ENVIRONMENT", str(error)))
                    except Exception as error:
                        step["status"] = "BLOCKED"
                        category = "FILESYSTEM" if isinstance(error, OSError) else "INTEGRITY_FAILURE" if isinstance(error, (Invalid, ET.ParseError, UnicodeError)) else "UNKNOWN"
                        with (output / "exception.log").open("x", encoding="utf-8") as stream:
                            stream.write(redact(traceback.format_exc()))
                        step["failures"].append(failure(category, str(error), stack=traceback.format_exc()))
                    step["duration_ms"] = round((time.monotonic() - clock) * 1000, 3)
                    for item in step["failures"]:
                        item["log_reference"] = step["log_reference"]
                    logger.event("QA_STEP_COMPLETED", step["status"], level="INFO" if step["status"] == "PASS" else "ERROR",
                                 scenario_id=scenario["id"], step_id=definition["id"], duration_ms=step["duration_ms"],
                                 error_code=step["failures"][0]["category"] if step["failures"] else None)
                    print(f"{scenario['id']}/{definition['id']}: {step['status']}", flush=True)
                    halted = step["status"] != "PASS" and scenario["on_failure"] == "stop"
                if any(s["status"] != "PASS" and s["required"] for s in summary["steps"] if s["scenario_id"] == scenario["id"]) and scenario["on_failure"] == "stop":
                    break
        except Exception as error:
            with (folder / "logs/input-exception.log").open("x", encoding="utf-8") as stream:
                stream.write(redact(traceback.format_exc()))
            summary["steps"].append({"scenario_id": "validation", "step_id": "input", "required": True, "status": "BLOCKED", "duration_ms": 0,
                                     "failures": [failure("AUTHORIZATION" if "denied" in str(error).lower() or "approved" in str(error).lower() else "ENVIRONMENT", str(error), stack=traceback.format_exc())]})
            logger.event("QA_INPUT_REJECTED", str(error), level="ERROR", exception_type=type(error).__name__)
        if cancelled.is_set():
            summary["steps"].append({"scenario_id": "runner", "step_id": "interruption", "required": True, "status": "BLOCKED", "duration_ms": 0, "failures": [failure("ENVIRONMENT", "User interrupted; remaining steps NOT_RUN")]})
        summary["status"] = aggregate(summary["steps"])
        for step in summary["steps"]:
            for key, count in step.get("tests", {}).items():
                summary["tests"][key] += count
        if summary["layers"]["functional"] == "NOT_RUN" and not any("functional" in s.get("layers", {}) for s in summary["steps"]):
            summary["layers"]["functional"] = summary["status"]
        summary.update(ended_at=utc(), duration_ms=round((time.monotonic() - started) * 1000, 3), failures=group_failures(summary["steps"]))
        summary["comparison"] = comparison(summary, previous_run(results, project["project_id"], profile, summary["performance_profile"], run_id))
        logger.event("QA_RUN_COMPLETED", summary["status"], duration_ms=summary["duration_ms"])
        logger.close()
        logger = None
        finalize(folder, summary, execution)
        verify_manifest(folder)
        return summary
    finally:
        if logger:
            logger.close()
        for sig, handler in old_signals.items():
            signal.signal(sig, handler)
        lock.unlink()
