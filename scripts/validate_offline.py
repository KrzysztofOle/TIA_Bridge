"""Run B-01A-C/B-02A-B offline gates. QA Python, cached .NET SDK/packages and local NTFS.

No network restore/feed, package installation, Siemens, public server or project IO. Starts own Fake Workers.
Run with the isolated QA Python from repo root; exit 0 only when every gate passes.
"""
from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET

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


def check_trx(path, host_required=False, scheduler_required=False):
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    paths = sorted(path.glob("*.trx")) if path.is_dir() else [path]
    results = [result for item in paths for result in ET.parse(item).getroot().findall(".//t:UnitTestResult", ns)]
    if not results or any(r.attrib.get("outcome") != "Passed" for r in results):
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
    return {"passed": len(results), "failed": 0, "skipped": 0, "deterministic_cases": 100, "host_storage_cases": len(host) - len(scheduler), "scheduler_ipc_cases": len(scheduler)}


def run_step(name, command, steps):
    started = time.monotonic()
    environment = os.environ.copy()
    environment.update(DOTNET_CLI_UI_LANGUAGE="en", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONIOENCODING="utf-8")
    result = subprocess.run(command, cwd=ROOT, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", check=False)
    (OUTPUT / (name + ".log")).write_text(result.stdout, encoding="utf-8")
    steps.append({"name": name, "exit_code": result.returncode, "elapsed_s": round(time.monotonic() - started, 3)})
    print(result.stdout, end="", flush=True)
    if result.returncode != 0:
        raise ValueError("Offline gate failed: " + name)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {"schema_version": "1.0", "scope": "B-01A-C/B-02A-B OFFLINE", "status": "FAIL", "steps": [], "engineering": "NOT_RUN", "runtime": "NOT_RUN"}
    try:
        for dependency in ["jsonschema", "openapi_spec_validator"]:
            if importlib.util.find_spec(dependency) is None:
                raise ValueError("Missing QA dependency; see scripts/requirements-qa.txt and tests/README.md.")
        report["core_boundary"] = check_core_boundary()
        # An empty local feed and existing global cache keep restore offline. No package install or audit HTTP.
        feed = OUTPUT / "empty-feed"
        feed.mkdir(exist_ok=True)
        test_results = OUTPUT / "test-results" / uuid.uuid4().hex
        steps = [
            ("01-restore", ["dotnet", "restore", "TiaBridge.slnx", "--locked-mode", "--source", str(feed), "-p:NuGetAudit=false"]),
            ("02-build", ["dotnet", "build", "TiaBridge.slnx", "-c", "Release", "--no-restore"]),
            ("03-dotnet", ["dotnet", "test", "TiaBridge.slnx", "-c", "Release", "--no-build", "--no-restore", "--logger", "trx", "--results-directory", str(test_results)]),
            ("04-json-schema-openapi", [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tests/contracts", "-p", "test_*.py", "-v"]),
            ("05-contract-generator", [sys.executable, "-B", "scripts/generate_contracts.py"]),
            ("06-test-vectors", [sys.executable, "-B", "scripts/generate_test_vectors.py"]),
            ("07-documentation", [sys.executable, "-B", "scripts/audit_quality.py", "--report", ".tia-bridge/offline/quality.json"]),
            ("08-format", ["dotnet", "format", "whitespace", "TiaBridge.slnx", "--no-restore", "--verify-no-changes"]),
            ("09-diff", ["git", "diff", "--check"]),
        ]
        for name, command in steps:
            run_step(name, command, report["steps"])
            if name == "03-dotnet":
                report["dotnet"] = check_trx(test_results, host_required=True, scheduler_required=True)
                report["test_results"] = str(test_results.relative_to(ROOT))
        report["status"] = "PASS"
    except (OSError, ValueError, ET.ParseError) as error:
        report["failure"] = str(error)
        print("OFFLINE: FAIL - " + str(error), flush=True)
    finally:
        (OUTPUT / "run.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("OFFLINE: " + report["status"] + "; ENGINEERING: NOT_RUN; RUNTIME: NOT_RUN", flush=True)
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
