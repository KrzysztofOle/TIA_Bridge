"""Run B-01A-C/B-02A-D offline tests. Qualification/readiness is reported separately. No eight-hour soak.

No network restore/feed, package installation or Siemens. Starts own loopback Host/CLI/Fake processes and edits synthetic files.
Run with the isolated QA Python from repo root; exit 0 only when every gate passes.
"""
from __future__ import annotations

import importlib.util
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / ".tia-bridge/offline"
FORBIDDEN = re.compile(r"Siemens|PLCSIM|WinCC|TiaBridge\.(?:Host|Worker|Adapters)", re.I)


def check_core_boundary(root=ROOT):
    project = root / "src/TiaBridge.Core/TiaBridge.Core.csproj"
    model = ET.parse(project).getroot()
    if list(model.iter("ProjectReference")):
        raise ValueError("Core must remain independent of application/adapter projects.")
    for element in model.iter():
        if element.tag in {"PackageReference", "Reference", "COMReference"}:
            if FORBIDDEN.search(element.attrib.get("Include", "")):
                raise ValueError("Forbidden Core reference.")
    lock = json.loads(project.with_name("packages.lock.json").read_text(encoding="utf-8"))
    for framework in lock["dependencies"].values():
        if any(FORBIDDEN.search(name) for name in framework):
            raise ValueError("Forbidden transitive Core package.")
    for source in project.parent.rglob("*.cs"):
        if "obj" in source.parts or "bin" in source.parts:
            continue
        if re.search(r"^\s*(?:using|extern alias)\s+Siemens\b|(?:DllImport|LibraryImport)\s*\(\s*\"[^\"]*(?:Siemens|WinCC|PLCSIM)", source.read_text(encoding="utf-8"), re.M | re.I):
            raise ValueError("Forbidden vendor source import.")
    return {"status": "PASS", "scope": "Core project, locked transitive package names and source imports; assembly closure is tested by .NET"}


def check_trx(path, host_required=False, scheduler_required=False, api_required=False, resilience_required=False, allow_failures=False):
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    paths = sorted(path.glob("*.trx")) if path.is_dir() else [path]
    results = [result for item in paths for result in ET.parse(item).getroot().findall(".//t:UnitTestResult", ns)]
    if not results or any(r.attrib.get("outcome") not in ({"Passed", "Failed"} if allow_failures else {"Passed"}) for r in results):
        raise ValueError("Missing, failed or skipped .NET results.")
    deterministic = [r for r in results if "Golden_models_paths_manifests_and_capabilities" in r.attrib.get("testName", "")]
    if len(deterministic) != 100 or len({r.attrib["testName"] for r in deterministic}) != 100:
        raise ValueError("Expected 100 distinct deterministic model cases.")
    for name, count in {"Shared_shape_mutations_have_the_same_verdict": 19,
                        "Every_required_parameter_result_and_nested_member": 19,
                        "Method_specific_errors_round_trip": 19,
                        "Every_lifecycle_edge_obeys": 147,
                        "Every_method_has_valid_typed_request_and_response": 19,
                        "Core_transitive_assembly_dependencies": 1}.items():
        if sum(name in r.attrib.get("testName", "") for r in results) != count:
            raise ValueError("Missing required API, lifecycle, regression or dependency cases.")
    host = [r for r in results if "TiaBridge.Host.Tests." in r.attrib.get("testName", "")]
    if host_required:
        for name, count in {"One_hundred_concurrent_identical_submits": 1,
                            "Deterministic_process_exit_at_admission_boundary": 2,
                            "Outbox_claim_is_compare_and_swap_across_independent_processes": 1,
                            "Failed_upgrade_rolls_back": 1,
                            "Missing_or_corrupt_content": 3,
                            "Artifact_crash_boundaries": 2,
                            "Sqlite_full_and_artifact_io_full": 1}.items():
            if sum(name in r.attrib.get("testName", "") for r in host) != count:
                raise ValueError("Missing required NTFS SQLite/restart/failure tests.")
    scheduler = [r for r in host if any(name in r.attrib.get("testName", "") for name in ["SchedulerTests.", "SchedulerSafetyTests.", "IpcAndPolicyTests."])]
    if scheduler_required:
        for name, count in {"Real_worker_completes_a_durable_operation_once": 3,
                            "Queued_cancel_is_atomic_and_never_dispatches": 1,
                            "Hung_mutation_times_out_to_durable_quarantine_without_retry": 5,
                            "Host_process_restart_quarantines_a_dispatched_mutation": 1,
                            "Host_status_remains_responsive_while_a_real_fake_process_is_hung": 1,
                            "Late_success_is_retained_without_changing_unknown_to_success": 1,
                            "Wrong_fence_or_plan_from_a_real_worker_is_evidence_only": 2,
                            "Shared_ipc_schema_examples_round_trip_through_actual_framing": 9}.items():
            if sum(name in r.attrib.get("testName", "") for r in scheduler) != count:
                raise ValueError("Missing required process IPC/scheduler/quarantine cases.")
    api = [r for r in host if any(name in r.attrib.get("testName", "") for name in ["HttpApiTests.", "ApiSecurityTests.", "ApiMetadataAndCliTests.", "ApiPerformanceTests."])]
    if api_required:
        for name in ["Real_host_cli_and_fake_complete_the_submit_status_events_result_cycle",
                     "All_nineteen_valid_requests_have_an_explicit_http_outcome",
                     "Metadata_post_uses_the_same_durable_idempotency_and_cancel_journal",
                     "Host_restart_keeps_results_identity_and_history_and_rotates_transport_token",
                     "Discovery_is_user_only_and_a_widened_acl_is_rejected_by_cli",
                     "Worker_start_failure_becomes_a_durable_error_while_api_stays_available",
                     "One_and_eight_clients_keep_status_and_workspace_responsive_during_real_worker_hang",
                     "Queue_is_bounded_at_one_thousand_and_duplicate_health_and_status_still_work",
                     "Unknown_mutation_stays_quarantined_after_api_cancel_and_host_restart"]:
            if sum(name in r.attrib.get("testName", "") for r in api) != 1:
                raise ValueError("Missing required HTTP/CLI/process/auth/performance cases.")
        if sum("Required_nullable_diagnostic_age_is_preserved" in r.attrib.get("testName", "") for r in results) != 1:
            raise ValueError("Missing required nullable wire regression.")
    resilience = [r for r in host if ".Resilience" in r.attrib.get("testName", "")]
    if resilience_required:
        for name, count in {"RT03_admission_crash": 5, "RT03_running_crash": 6, "RT03_explicit_QUEUED_state": 1, "RT05_two_synthetic_writes": 1,
                            "RT06_receipt_already": 1, "RT15_owned_child_lock": 1, "RT16_one_hundred": 1,
                            "Cancel_dispatch_race": 2, "Real_process_pipe_attack": 8,
                            "Damaged_final_result_WAL_frame": 2, "Corrupt_artifact_never": 3,
                            "Five_minute_idle_resource_profile": 1, "One_thousand_and_one_operations": 1,
                            "Independent_workspace_save_and_replace": 1, "Health_result_events_and_recovery": 1}.items():
            if sum(name in r.attrib.get("testName", "") for r in resilience) != count:
                raise ValueError("Missing required resilience/crash/resource/process cases.")
    return {"passed": sum(r.attrib["outcome"] == "Passed" for r in results), "failed": sum(r.attrib["outcome"] == "Failed" for r in results), "failed_tests": [r.attrib["testName"] for r in results if r.attrib["outcome"] == "Failed"], "skipped": 0, "deterministic_cases": 100, "host_storage_cases": len(host) - len(scheduler) - len(api) - len(resilience), "scheduler_ipc_cases": len(scheduler), "http_cli_cases": len(api), "resilience_cases": len(resilience)}


def run_step(name, command, steps, collect_test_failure=False):
    started = time.monotonic()
    environment = os.environ.copy()
    environment.update(DOTNET_CLI_UI_LANGUAGE="en", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONIOENCODING="utf-8")
    result = subprocess.run(command, cwd=ROOT, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="strict", check=False)
    (OUTPUT / (name + ".log")).write_text(result.stdout, encoding="utf-8")
    steps.append({"name": name, "exit_code": result.returncode, "elapsed_s": round(time.monotonic() - started, 3)})
    print(result.stdout, end="", flush=True)
    if result.returncode != 0 and not collect_test_failure:
        raise ValueError("Offline gate failed: " + name)
    return result


def configure_console_utf8():
    """Use strict UTF-8 for console reports while leaving report files explicitly UTF-8."""
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8", errors="strict")


def load_reporter(root):
    spec = importlib.util.spec_from_file_location("report_resilience", root / "scripts/report_resilience.py")
    reporter = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(reporter)
    return reporter


def run_preflight(profile, output_directory):
    result = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", str(ROOT / "scripts/d11_preflight.ps1"), "-OutputDirectory", str(output_directory), "-Profile", profile], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=90, check=False)
    if result.returncode != 0:
        details = (result.stderr or result.stdout or "No PowerShell diagnostic was returned.").strip()[-3000:]
        raise ValueError("Qualification preflight failed (exit " + str(result.returncode) + "): " + details)
    path = Path(result.stdout.strip().splitlines()[-1])
    return json.loads(path.read_text(encoding="utf-8-sig"))


def run_nf11_synthetic(mode, profile):
    """Run only the opt-in Host/Fake NF-11 phase; preserve partial evidence on controlled stop."""
    global OUTPUT
    run_id = uuid.uuid4().hex
    run_folder = ROOT / ".tia-bridge/resilience" / run_id
    lock_path = ROOT / ".tia-bridge/resilience/nf11-synthetic.active.json"
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    lock_value = json.dumps({"run_id": run_id, "pid": os.getpid(), "started_at": datetime.now(timezone.utc).isoformat()})
    try:
        with lock_path.open("x", encoding="utf-8") as lock:
            lock.write(lock_value + "\n"); lock.flush(); os.fsync(lock.fileno())
    except FileExistsError as error:
        print("NF11: BLOCKED - an active/stale NF-11 lock exists; inspect it, do not remove it automatically.", flush=True)
        return 2

    validation = run_folder / "validation"
    test_results = run_folder / "test-results"
    validation.mkdir(parents=True)
    test_results.mkdir()
    OUTPUT = validation
    stop_file = run_folder / "stop.request"
    log_path = validation / "nf11-dotnet-test.log"
    report = {"schema_version": "1.0", "run_id": run_id, "mode": mode, "scope": "NF11_SYNTHETIC Host/FakeWorker only",
              "status": "BLOCKED", "started_at": datetime.now(timezone.utc).isoformat(), "test_exit_code": None,
              "interrupted": False, "engineering": "NOT_RUN", "runtime": "NOT_RUN"}
    exit_code = 1
    output_offset = 0
    process = None
    try:
        os.environ["TIA_BRIDGE_QUALIFICATION_PROFILE"] = profile
        dependencies = ["dotnet", "git"]
        versions = {}
        for tool in dependencies:
            result = subprocess.run([tool, "--version"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=15, check=True)
            versions[tool] = result.stdout.strip().splitlines()[0]
        report["tools"] = versions
        # The existing preflight inspects this test-owned repository-volume root.
        # Create only its empty parent; the soak itself writes under its UUID run folder.
        (ROOT / ".tia-bridge/resilience/workspaces").mkdir(parents=True, exist_ok=True)
        (ROOT / ".tia-bridge/b02c/workspaces").mkdir(parents=True, exist_ok=True)
        preflight_dir = validation / "preflight"
        preflight = run_preflight(profile, preflight_dir)
        report["preflight"] = preflight
        if preflight.get("status") == "BLOCKED":
            raise ValueError("NF-11 preflight BLOCKED: " + "; ".join(preflight.get("blockers", [])))
        resilience_report = load_reporter(ROOT)
        resilience_report.capture_environment(ROOT, run_id)
        source_fingerprint, sources = resilience_report.source_fingerprint(ROOT)
        profile_meta = resilience_report.performance_evidence
        profile_meta.write_new_json(validation / "run-metadata.json", {
            "run_id": run_id, "started_at": report["started_at"], "git_sha": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
            "source_fingerprint_lf_sha256": source_fingerprint, "source_file_count": len(sources), "mode": mode,
            "profile": profile_meta.PROFILE_NAME, "profile_version": profile_meta.PROFILE_VERSION,
            "profile_definition_sha256": profile_meta.PROFILE_SHA256,
            "protected_criteria": json.loads((ROOT / "tests/resilience/matrix.json").read_text(encoding="utf-8"))["eight_hour_requirement"]})
        empty_feed = validation / "empty-feed"
        empty_feed.mkdir()
        restore_command = ["dotnet", "restore", "TiaBridge.slnx", "--locked-mode", "--source", str(empty_feed), "-p:NuGetAudit=false"]
        restore_steps = []
        run_step("01-restore", restore_command, restore_steps)
        build_command = ["dotnet", "build", "TiaBridge.slnx", "-c", "Release", "--no-restore"]
        run_step("02-build", build_command, restore_steps)
        report["steps"] = restore_steps
        binary_paths = {
            "host_tests": "tests/integration/TiaBridge.Host.Tests/bin/Release/net10.0/TiaBridge.Host.Tests.dll",
            "host": "src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll",
            "fake_worker": "src/TiaBridge.FakeWorker/bin/Release/net10.0/TiaBridge.FakeWorker.dll",
            "core": "src/TiaBridge.Core/bin/Release/net10.0/TiaBridge.Core.dll",
            "ipc": "src/TiaBridge.Ipc/bin/Release/net10.0/TiaBridge.Ipc.dll",
        }
        profile_meta.write_new_json(validation / "build-metadata.json", {
            "run_id": run_id, "git_sha": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
            "source_fingerprint_lf_sha256": source_fingerprint,
            "binaries": {key: {"path": relative, "bytes": (ROOT / relative).stat().st_size, "sha256": profile_meta.digest(ROOT / relative)} for key, relative in binary_paths.items()}})
        command = ["dotnet", "test", "tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj", "-c", "Release", "--no-build", "--no-restore",
                   "--filter", "Category=NF11Soak", "--logger", "trx", "--results-directory", str(test_results)]
        environment = os.environ.copy()
        environment.update({"DOTNET_CLI_UI_LANGUAGE": "en", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                            "TIA_BRIDGE_RESILIENCE_RUN_ID": run_id, "TIA_BRIDGE_NF11_RUN_ID": run_id,
                            "TIA_BRIDGE_NF11_MODE": mode, "TIA_BRIDGE_NF11_OUTPUT_DIRECTORY": str(run_folder),
                            "TIA_BRIDGE_NF11_STOP_FILE": str(stop_file), "PYTHONIOENCODING": "utf-8"})
        creationflags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
        with log_path.open("x", encoding="utf-8", newline="\n") as log:
            process = subprocess.Popen(command, cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT, creationflags=creationflags)
            report["command"] = command
            report["child_pid"] = process.pid
            print(f"NF11 {mode}: run_id={run_id}; evidence={run_folder}", flush=True)
            try:
                while process.poll() is None:
                    time.sleep(1)
                    log.flush()
                    with log_path.open("r", encoding="utf-8", errors="replace") as reader:
                        reader.seek(output_offset)
                        chunk = reader.read()
                        output_offset = reader.tell()
                    if chunk:
                        print(chunk, end="", flush=True)
            except KeyboardInterrupt:
                report["interrupted"] = True
                try:
                    request_nf11_stop(stop_file, run_id, "keyboard_interrupt")
                except FileExistsError:
                    pass
                print("NF11: controlled stop requested; waiting for the owned test process to flush partial evidence and clean up.", flush=True)
                process.wait()
        report["test_exit_code"] = process.returncode
        nf11 = resilience_report.nf11_synthetic_report(ROOT, run_id)
        profile_meta.write_new_json(validation / "nf11-report.json", nf11)
        report["nf11"] = nf11
        report["ended_at"] = datetime.now(timezone.utc).isoformat()
        report["status"] = "PASS" if nf11["NF11_SYNTHETIC"] == "PASS" else "BLOCKED" if nf11["NF11_SYNTHETIC"] in {"BLOCKED", "NOT_RUN"} else "FAIL"
        if report["interrupted"]:
            report["status"] = "BLOCKED"
            exit_code = 130
        elif process.returncode != 0 and not (mode == "DIAGNOSTIC" and nf11["NF11_SYNTHETIC"] == "BLOCKED"):
            report["status"] = "FAIL" if nf11["NF11_SYNTHETIC"] == "FAIL" else "BLOCKED"
            exit_code = 1
        else:
            exit_code = 0 if report["status"] in {"PASS", "BLOCKED"} else 1
    except (OSError, ValueError, subprocess.SubprocessError, KeyError, TypeError) as error:
        report["error"] = str(error)
        report["status"] = "BLOCKED"
        report["ended_at"] = datetime.now(timezone.utc).isoformat()
        try:
            nf11 = load_reporter(ROOT).nf11_synthetic_report(ROOT, run_id)
            report["nf11"] = nf11
            profile_meta = load_reporter(ROOT).performance_evidence
            profile_meta.write_new_json(validation / "nf11-report.json", nf11)
        except (OSError, ValueError):
            pass
        exit_code = 1
    finally:
        report.setdefault("ended_at", datetime.now(timezone.utc).isoformat())
        if process is not None and process.poll() is None:
            try:
                request_nf11_stop(stop_file, run_id, "runner_cleanup")
            except FileExistsError:
                pass
            process.wait()
        with (validation / "run.json").open("x", encoding="utf-8") as stream:
            json.dump(report, stream, indent=2); stream.write("\n"); stream.flush(); os.fsync(stream.fileno())
        load_reporter(ROOT).performance_evidence.seal_run(run_folder, run_id)
        if lock_path.exists() and lock_path.read_text(encoding="utf-8").strip() == lock_value:
            lock_path.unlink()
        print(f"NF11 {mode}: {report['status']}; synthetic={report.get('nf11', {}).get('NF11_SYNTHETIC', 'NOT_RUN')}; live={report.get('nf11', {}).get('NF11_LIVE', 'NOT_RUN')}; overall={report.get('nf11', {}).get('NF11_OVERALL', 'BLOCKED')}", flush=True)
    return exit_code


def request_nf11_stop(path, run_id, reason):
    """Create the one-way stop request consumed by the active test; never overwrite it."""
    with Path(path).open("x", encoding="utf-8", newline="\n") as request:
        request.write(json.dumps({"run_id": run_id, "reason": reason, "requested_at": datetime.now(timezone.utc).isoformat()}) + "\n")
        request.flush(); os.fsync(request.fileno())


def main(argv=None):
    global OUTPUT
    configure_console_utf8()
    parser = argparse.ArgumentParser(description="Run the offline suite using an explicit qualification profile.")
    parser.add_argument("--profile", choices=["STRICT", "SHARED_WORKSTATION"], default="STRICT")
    parser.add_argument("--nf11-synthetic", action="store_true", help="Run only the opt-in Host/FakeWorker NF-11 synthetic phase.")
    parser.add_argument("--nf11-mode", choices=["FULL", "DIAGNOSTIC"], default="FULL")
    arguments = parser.parse_args(argv or [])
    profile = arguments.profile
    if arguments.nf11_synthetic:
        return run_nf11_synthetic(arguments.nf11_mode, profile)
    if arguments.nf11_mode != "FULL":
        parser.error("--nf11-mode requires --nf11-synthetic")
    os.environ["TIA_BRIDGE_QUALIFICATION_PROFILE"] = profile
    run_id = uuid.uuid4().hex
    run_folder = ROOT / ".tia-bridge/resilience" / run_id
    run_folder.mkdir(parents=True, exist_ok=False)
    OUTPUT = run_folder / "validation"
    OUTPUT.mkdir()
    report = {"schema_version": "1.1", "scope": "B-01A-C/B-02A-D OFFLINE", "status": "FAIL", "qualification_status": "NOT_RUN", "performance_status": "NOT_RUN", "qualification_profile": profile, "profile_version": "1.0.0", "started_at": datetime.now(timezone.utc).isoformat(), "steps": [], "engineering": "NOT_RUN", "runtime": "NOT_RUN"}
    try:
        for dependency in ["jsonschema", "openapi_spec_validator"]:
            if importlib.util.find_spec(dependency) is None:
                raise ValueError("Missing QA dependency; see scripts/requirements-qa.txt and tests/README.md.")
        report["core_boundary"] = check_core_boundary()
        preflight_dir = OUTPUT / "preflight"
        try:
            report["preflight"] = run_preflight(profile, preflight_dir)
        except (OSError, ValueError, subprocess.SubprocessError) as preflight_error:
            report["qualification_status"] = "BLOCKED"; report["performance_status"] = "NOT_RUN"; report["status"] = "BLOCKED"
            report["preflight_error"] = str(preflight_error)
            raise
        if report["preflight"]["status"] == "BLOCKED":
            report["qualification_status"] = "BLOCKED"; report["performance_status"] = "NOT_RUN"; report["status"] = "BLOCKED"
            raise ValueError("Qualification preflight BLOCKED: " + "; ".join(report["preflight"]["blockers"]))
        # An empty local feed and existing global cache keep restore offline. No package install or audit HTTP.
        feed = OUTPUT / "empty-feed"
        feed.mkdir(exist_ok=True)
        test_results = run_folder / "test-results"
        os.environ["TIA_BRIDGE_RESILIENCE_RUN_ID"] = run_id
        resilience_report = load_reporter(ROOT)
        resilience_report.capture_environment(ROOT, run_id)
        resilience_report.performance_evidence.write_new_json(OUTPUT / "run-metadata.json", {"run_id": run_id, "started_at": report["started_at"], "git_sha": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(), "python": sys.version, "qualification_profile": profile, "profile_version": resilience_report.performance_evidence.PROFILE_VERSION, "profile_definition_sha256": resilience_report.performance_evidence.PROFILE_SHA256, "applied_test_harness_limits": resilience_report.performance_evidence.TEST_HARNESS_LIMITS, "preflight_status": report["preflight"]["status"], "scope": report["scope"]})
        source_fingerprint, _ = resilience_report.source_fingerprint(ROOT)
        report["profile_definition_sha256"] = resilience_report.performance_evidence.PROFILE_SHA256
        steps = [
            ("01-restore", ["dotnet", "restore", "TiaBridge.slnx", "--locked-mode", "--source", str(feed), "-p:NuGetAudit=false"]),
            *([("01b-worker-restore", ["dotnet", "restore", "src/TiaBridge.Worker.V19/TiaBridge.Worker.V19.csproj", "--locked-mode", "--source", str(feed), "-p:NuGetAudit=false"])] if profile == "SHARED_WORKSTATION" else []),
            ("02-build", ["dotnet", "build", "TiaBridge.slnx", "-c", "Release", "--no-restore"]),
            *([("02b-worker-build", ["dotnet", "build", "src/TiaBridge.Worker.V19/TiaBridge.Worker.V19.csproj", "-c", "Release", "--no-restore"])] if profile == "SHARED_WORKSTATION" else []),
            ("03-dotnet", ["dotnet", "test", "TiaBridge.slnx", "-c", "Release", "--no-build", "--no-restore", "-m:1", "--filter", "FullyQualifiedName!~NF11_synthetic_soak", "--logger", "trx", "--results-directory", str(test_results)]),
            ("04-json-schema-openapi", [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tests/contracts", "-p", "test_*.py", "-v"]),
            ("05-contract-generator", [sys.executable, "-B", "scripts/generate_contracts.py"]),
            ("06-test-vectors", [sys.executable, "-B", "scripts/generate_test_vectors.py"]),
            ("07-documentation", [sys.executable, "-B", "scripts/audit_quality.py", "--report", str(OUTPUT / "quality.json")]),
            ("08-format", ["dotnet", "format", "whitespace", "TiaBridge.slnx", "--no-restore", "--verify-no-changes"]),
            ("09-diff", ["git", "diff", "--check"]),
        ]
        for name, command in steps:
            result = run_step(name, command, report["steps"], collect_test_failure=name in {"03-dotnet", "04-json-schema-openapi"})
            if name == "02b-worker-build":
                # This metadata is finalized once, before any test executes; never overwrite a run artifact.
                resilience_report.performance_evidence.write_new_json(OUTPUT / "build-metadata.json", {
                    "run_id": run_id, "source_fingerprint_lf_sha256": source_fingerprint,
                    "builds_sha256": {key: resilience_report.performance_evidence.digest(ROOT / relative) for key, relative in {
                        "host": "src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll",
                        "worker": "src/TiaBridge.Worker.V19/bin/Release/net48/TiaBridge.Worker.V19.exe"}.items()}})
            if name == "03-dotnet":
                report["dotnet"] = check_trx(test_results, host_required=True, scheduler_required=True, api_required=True, resilience_required=True, allow_failures=True)
                report["test_results"] = str(test_results.relative_to(ROOT))
            if name == "04-json-schema-openapi":
                found = re.search(r"Ran (\d+) tests", result.stdout)
                if not found:
                    raise ValueError("Missing Python test execution summary.")
                total = int(found.group(1)); failed = sum(int(n) for n in re.findall(r"(?:failures|errors)=(\d+)", result.stdout)); skipped = sum(int(n) for n in re.findall(r"skipped=(\d+)", result.stdout))
                report["python"] = {"total": total, "passed": total - failed - skipped, "failed": failed, "skipped": skipped}
        qualification = resilience_report.qualification(ROOT, run_id, report["started_at"])
        test_execution_ok = all(step["exit_code"] == 0 for step in report["steps"]) and report["dotnet"]["failed"] == 0 and report["python"]["skipped"] == 0
        if not test_execution_ok:
            qualification["gate"] = "FAIL"; qualification["x01_readiness"] = "NOT_READY"
            qualification["qualification_status"] = "FAIL"; report["qualification_status"] = "FAIL"
            if report["python"]["failed"] or any("ApiPerformanceTests.One_and_eight_clients_keep_status_and_workspace_responsive_during_real_worker_hang" not in name for name in report["dotnet"]["failed_tests"]):
                qualification["limited_x01_technical_readiness"] = "NOT_READY_UNTIL_FAILED_TESTS_RESOLVED"
            qualification["gate_blockers"].append("A .NET or Python test failed/skipped; passing RT subsets cannot hide this execution failure.")
        qualification["test_results"] = {"dotnet": report["dotnet"], "python": report["python"]}
        qualification["preflight_status"] = report["preflight"]["status"]
        qualification["preflight_load_observations"] = report["preflight"]["load_policy"]
        performance_keys = ["NF-01", "NF-02", "NF-03", "NF-05", "NF-08"]
        performance_values = [qualification["nf"][key]["status"] for key in performance_keys[:-1]] + [qualification["nf"]["NF-08"]["measured_idle_status"]]
        report["performance_status"] = "FAIL" if "FAIL" in performance_values else "PASS" if all(value == "PASS" for value in performance_values) else "BLOCKED"
        report["qualification_status"] = "FAIL" if qualification["gate"] == "FAIL" else "PASS" if qualification["gate"] == "PASS" else "BLOCKED"
        resilience_report.performance_evidence.write_new_json(OUTPUT / "resilience.json", qualification)
        report["resilience_gate"] = qualification["gate"]; report["x01_readiness"] = qualification["x01_readiness"]
        report["test_execution_status"] = "PASS" if test_execution_ok else "FAIL"
        report["status"] = "FAIL" if qualification["gate"] == "FAIL" else "PASS"
        if report["status"] == "FAIL":
            report["failure"] = "Resilience qualification failed; see this run's validation/resilience.json. Passing test execution does not hide NF failures."
    except (OSError, ValueError, ET.ParseError, subprocess.SubprocessError, KeyError, TypeError) as error:
        report["failure"] = str(error)
        print("OFFLINE: FAIL - " + str(error), flush=True)
    finally:
        report.update(run_id=run_id, ended_at=datetime.now(timezone.utc).isoformat())
        with (OUTPUT / "run.json").open("x", encoding="utf-8") as stream:
            json.dump(report, stream, indent=2); stream.write("\n")
        evidence_spec = importlib.util.spec_from_file_location("performance_evidence", ROOT / "scripts/performance_evidence.py")
        evidence = importlib.util.module_from_spec(evidence_spec); evidence_spec.loader.exec_module(evidence)
        evidence.seal_run(run_folder, run_id)
    print("RUN ID: " + run_id + "; EVIDENCE: " + str(run_folder), flush=True)
    print("OFFLINE: " + report["status"] + "; ENGINEERING: NOT_RUN; RUNTIME: NOT_RUN", flush=True)
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
