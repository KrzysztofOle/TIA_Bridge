"""E2-B1 offline readiness assessment. Python 3.12 stdlib; never authorizes or starts Siemens.

Inputs are local evidence and operator review claims, not permissions. Host B0 independently
checks ACL, window, live Windows binding and project state at execution. No age-based expiry.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path, PureWindowsPath
import re
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RT = ["RT-03", "RT-05", "RT-06", "RT-15", "RT-16"]
NF = [f"NF-{n:02d}" for n in (1, 2, 3, 4, 5, 6, 12, 14, 15)]
READ_SCOPE = ["TiaPortal.GetProcesses", "TiaPortalProcess.Attach", "TiaPortal.Projects",
              "Project.Path", "Project.Name", "Project.IsModified", "Project.Devices",
              "Device.Name", "Device.DeviceItems", "DeviceItem.Name", "DeviceItem.DeviceItems",
              "DeviceItem.GetService<SoftwareContainer>", "SoftwareContainer.Software"]
SCOPE = dict(project_name="T05_RecipeSim_V19_Testowy", plan_id="E2-B1",
             operation_method="project.inspect", operation_scope="PROJECT", profile="v19.discovery.v1",
             inventory_scope="all_supported", fresh=True, maximum_cycles=20, maximum_devices=64,
             maximum_nodes=256, maximum_depth=8, timeout_seconds=120, queue_timeout_seconds=300,
             apartment="STA", max_in_flight=1, target_count=1)
DIAGNOSTIC_SCOPE = dict(SCOPE, plan_id="E2-B1-D", maximum_cycles=3,
                        purpose="HANDLE_DIAGNOSTICS", automatic_retry=False)
BINDING = {"project_key", "project_path", "tia_pid", "tia_start_ticks", "executable_path",
           "session_sid", "tia_build", "worker_build", "host_build"}
STEPS = ["01-restore", "01b-worker-restore", "02-build", "02b-worker-build", "03-dotnet",
         "04-json-schema-openapi", "05-contract-generator", "06-test-vectors", "07-documentation",
         "08-format", "09-diff"]
SUITES = ["BootstrapTests", "LocalTrustedTests", "LocalTrustedDispatchTests", "V19OfflineTests",
          "QualificationEvidenceTests"]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def reporter():
    previous = os.environ.get("TIA_BRIDGE_QUALIFICATION_PROFILE")
    try:
        os.environ["TIA_BRIDGE_QUALIFICATION_PROFILE"] = "SHARED_WORKSTATION"
        return load("report_resilience")
    finally:
        if previous is None:
            os.environ.pop("TIA_BRIDGE_QUALIFICATION_PROFILE", None)
        else:
            os.environ["TIA_BRIDGE_QUALIFICATION_PROFILE"] = previous


def assess_qualification(q, run):
    """Pure policy evaluation, also used by synthetic boundary tests. No side effects."""
    reasons = []
    def deny(code, message):
        reasons.append(dict(code=code, description=message))
    if run.get("test_execution_status") != "PASS":
        deny("SAFETY_TESTS_NOT_PASS", "Complete current offline test execution must PASS, without skips.")
    for key in RT:
        if q.get("rt", {}).get(key, {}).get("status") != "PASS":
            deny("SAFETY_" + key, "Required safety evidence is not PASS: " + key)
    for key in NF:
        if q.get("nf", {}).get(key, {}).get("status") != "PASS":
            deny("D11_" + key, "Required limited D-11 result is not PASS: " + key)
    if q.get("sample_evidence_status") != "PASS":
        deny("PERFORMANCE_SAMPLES_NOT_PASS", "Full samples must reproduce the approved profile KPIs.")
    if q.get("measurements", {}).get("idle", {}).get("duration_s", 0) < 300:
        deny("D11_IDLE_INCOMPLETE", "At least 300 seconds of Host idle observation is required.")
    if q.get("nf", {}).get("NF-11", {}).get("completed_operations", 0) < 1000:
        deny("D11_OPERATIONS_INCOMPLETE", "At least 1000 completed operations are required, independently of NF-11.")
    # Full NF-08 and NF-11 remain explicit NOT_RUN. Measured idle is retained, never promoted.
    if q.get("nf", {}).get("NF-08", {}).get("status") == "FAIL":
        deny("RESOURCE_SUBTEST_FAILED", "A measured resource subtest failed; full NOT_RUN cannot hide it.")
    return reasons


def verify_qualification(root, run_id):
    """Verify sealed bytes, rebuild verdicts, compare current sources/binaries and full TRX."""
    if not re.fullmatch(r"[0-9a-f]{32}", run_id):
        raise ValueError("Invalid run ID.")
    r = reporter(); e = r.performance_evidence
    folder = e.checked_path(root, ".tia-bridge/resilience/" + run_id)
    manifest = e.verify_run(folder, run_id)
    run = e.read_json(folder / "validation/run.json")
    metadata = e.read_json(folder / "validation/run-metadata.json")
    saved = e.read_json(folder / "validation/resilience.json")
    if run.get("run_id") != run_id or saved.get("run_id") != run_id:
        raise ValueError("Run identity mismatch.")
    e.timestamp(run["ended_at"])
    if e.timestamp(run["ended_at"]) < e.timestamp(run["started_at"]):
        raise ValueError("Run timestamp order mismatch.")
    if not re.fullmatch(r"[0-9a-f]{40}", metadata.get("git_sha", "")):
        raise ValueError("Missing source Git SHA.")
    for item in (run, metadata, saved):
        if (item.get("qualification_profile"), item.get("profile_version"), item.get("profile_definition_sha256")) != ("SHARED_WORKSTATION", "1.0.0", e.PROFILE_SHA256):
            raise ValueError("Profile changed or wrong qualification profile/version/hash.")
    preflight = run["preflight"]
    if preflight.get("status") not in {"PASS", "PASS_WITH_LOAD"} or preflight.get("blockers"):
        raise ValueError("Preflight is not acceptable.")
    if (preflight.get("profile"), preflight.get("profile_version")) != ("SHARED_WORKSTATION", "1.0.0"):
        raise ValueError("Preflight profile mismatch.")
    if {step["name"] for step in run["steps"]} != set(STEPS) or len(run["steps"]) != len(STEPS) or any(step["exit_code"] != 0 for step in run["steps"] if step["name"] not in {"03-dotnet", "04-json-schema-openapi"}):
        raise ValueError("Required validation steps missing or failed.")
    tests = load("validate_offline").check_trx(folder / "test-results", host_required=True,
                    scheduler_required=True, api_required=True, resilience_required=True, allow_failures=True)
    if tests != run["dotnet"]:
        raise ValueError("TRX summary mismatch.")
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    names = [n.attrib["testName"] for p in (folder / "test-results").glob("*.trx")
             for n in ET.parse(p).getroot().findall(".//t:UnitTestResult", ns)]
    if any(not any("." + suite + "." in name for name in names) for suite in SUITES):
        raise ValueError("Required B0/E1/identity safety suite missing.")
    python = run["python"]
    if python["skipped"] or python["total"] != python["passed"] + python["failed"] or python["total"] <= 0:
        raise ValueError("Python suite evidence incomplete.")
    if (tests["failed"] or python["failed"]) and run.get("test_execution_status") == "PASS":
        raise ValueError("Failed tests falsely reported PASS.")
    q = r.qualification(root, run_id, run["started_at"])
    if q["measurements"]["workspace_replace"].get("filesystem") != "NTFS" or q["measurements"]["repository_replace"].get("filesystem") != "NTFS":
        raise ValueError("Qualification filesystem must be local NTFS.")
    for scenario in r.performance_evidence.PROFILES:
        bundle = r.performance_evidence.load_bundle(root, run_id, scenario, run["started_at"])["bundle"]
        if bundle["state_filesystem"] != "NTFS" or any(m["filesystem"] != "NTFS" for m in bundle["metrics"]):
            raise ValueError("Performance state/workspace filesystem is not NTFS.")
    if any(q[key] != saved[key] for key in ("rt", "nf", "scenarios", "measurements", "versions", "sample_evidence_status")):
        raise ValueError("Stored qualification does not reproduce from evidence/current sources.")
    source_hash, _ = r.source_fingerprint(root)
    build_metadata = e.read_json(folder / "validation/build-metadata.json")
    if build_metadata.get("source_fingerprint_lf_sha256") != source_hash:
        raise ValueError("Sources changed since qualification.")
    if build_metadata.get("run_id") != run_id:
        raise ValueError("Build manifest run identity mismatch.")
    metadata = metadata | build_metadata
    builds = metadata["builds_sha256"]
    expected = {"host": "src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll",
                "worker": "src/TiaBridge.Worker.V19/bin/Release/net48/TiaBridge.Worker.V19.exe"}
    if set(builds) != set(expected):
        raise ValueError("Host/Worker build manifest incomplete.")
    for key, relative in expected.items():
        if builds[key] != e.digest(e.checked_path(root, relative)):
            raise ValueError("Current Host/Worker build mismatch.")
    return q, run, metadata, e.digest(folder / "evidence-manifest.json"), len(manifest["files"])


def assess_dossier(d, builds, plan_hash, experiment="B1"):
    """Review inputs only, not live binding proof or bootstrap activation."""
    issues = []
    def issue(code, message, review=False):
        issues.append(dict(code=code, description=message, review_required=review))
    if not d:
        issue("B1_DOSSIER_MISSING", "Exact reviewed B1 binding, clean copy and monitoring dossier is required.")
        return issues
    allowed = {"schema_version", "scope", "read_scope", "plan_sha256", "expected_binding", "observed_binding",
               "clean_test_copy_confirmed", "ui_clean_confirmed", "no_colliding_siemens_operations",
               "monitoring_ready", "safe_stop_ready", "bootstrap_state", "is_modified_before_start"}
    diagnostic = experiment == "HANDLE_DIAGNOSTICS"
    expected_scope = DIAGNOSTIC_SCOPE if diagnostic else SCOPE
    if diagnostic:
        allowed |= {"phase_telemetry_ready", "handle_type_analysis"}
    if experiment not in {"B1", "HANDLE_DIAGNOSTICS"} or set(d) != allowed or d.get("schema_version") != ("1.1" if diagnostic else "1.0"):
        issue("DOSSIER_SCHEMA", "Missing or unknown dossier fields/version.")
    if d.get("scope") != expected_scope or any(type(d.get("scope", {}).get(k)) is not type(v) for k, v in expected_scope.items()):
        issue("B1_SCOPE_MISMATCH", "Only the exact B1 operation, single target and bounded profile are allowed; B2 is excluded.")
    reads = d.get("read_scope")
    if not isinstance(reads, list) or len(reads) != 13 or any(not isinstance(v, str) for v in reads) or set(reads) != set(READ_SCOPE):
        issue("READ_SCOPE_MISMATCH", "Exactly the approved 13 unique getters are required.")
    if d.get("plan_sha256") != plan_hash:
        issue("PLAN_HASH_MISMATCH", "Reviewed plan bytes differ from the current B1 plan.")
    b = d.get("expected_binding", {}); observed = d.get("observed_binding", {})
    if not isinstance(b, dict) or set(b) != BINDING or not isinstance(observed, dict) or set(observed) != BINDING:
        issue("BINDING_INCOMPLETE", "Exact ProjectKey/AP19/PID/start/EXE/SID/API/Worker/Host builds are required.")
        b = {}
    else:
        try:
            key = uuid.UUID(b["project_key"])
            if key.int == 0 or str(key) != b["project_key"]:
                raise ValueError("Noncanonical ProjectKey.")
        except (ValueError, TypeError, AttributeError):
            issue("PROJECT_KEY_INVALID", "ProjectKey must be a nonempty canonical UUID.")
        for field in sorted(BINDING):
            if type(b[field]) != type(observed[field]) or b[field] != observed[field]:
                issue("BINDING_DRIFT_" + field.upper(), "Expected and independently reviewed observed binding differ: " + field)
        for field in ("tia_pid", "tia_start_ticks"):
            if type(b[field]) is not int or b[field] <= 0:
                issue("PROCESS_IDENTITY_INVALID", "PID and UTC StartTime ticks must be positive integers.")
        for field in ("project_path", "executable_path"):
            value = b[field]
            if not isinstance(value, str) or not PureWindowsPath(value).is_absolute() or not re.match(r"^[A-Za-z]:\\", value) or ".." in PureWindowsPath(value).parts:
                issue("PATH_INVALID", "An exact absolute local Windows path is required: " + field)
        if PureWindowsPath(str(b["project_path"])).name != SCOPE["project_name"] + ".ap19":
            issue("PROJECT_PATH_INVALID", "AP19 must identify the first test project.")
        if not isinstance(b["session_sid"], str) or not re.fullmatch(r"S-1-(?:\d+-)+\d+", b["session_sid"]):
            issue("SID_INVALID", "Exact Windows session SID is required.")
        if not isinstance(b["tia_build"], str) or not re.fullmatch(r"\d+(?:\.\d+){3}", b["tia_build"]):
            issue("TIA_BUILD_INVALID", "Exact TIA API FileVersion is required.")
        for field, key in (("host_build", "host"), ("worker_build", "worker")):
            if b[field] != "1.0+" + builds[key]:
                issue("BUILD_MISMATCH", "Binding build differs from qualified bytes: " + field)
    for field in ("clean_test_copy_confirmed", "ui_clean_confirmed", "no_colliding_siemens_operations", "monitoring_ready", "safe_stop_ready"):
        if d.get(field) is not True:
            issue("REVIEW_" + field.upper(), "Operator review must confirm: " + field, review=True)
    if d.get("bootstrap_state") != "INACTIVE":
        issue("BOOTSTRAP_STATE_REVIEW", "This assessment prepares an inactive B0 draft; active/unknown grant requires review.", review=True)
    if d.get("is_modified_before_start") is not False and d.get("is_modified_before_start") != "PENDING_AUTHORIZED_READ":
        issue("DIRTY_OR_UNKNOWN_PROJECT", "IsModified must be false in an authorized read; no read may be performed to obtain READY.", review=True)
    if diagnostic and (d.get("phase_telemetry_ready") is not True or d.get("handle_type_analysis") not in {"AVAILABLE", "NOT_AVAILABLE"}):
        issue("DIAGNOSTIC_MONITORING_REQUIRED", "Phase trace must be ready; handle type availability must be explicitly recorded.")
    return issues


def evaluate(root, run_id, dossier=None, experiment="B1"):
    result = dict(schema_version="1.0", task="E2-G", run_id=run_id, status="BLOCKED", reasons=[],
                  safety_gate="BLOCKED", performance_gate="BLOCKED", evidence_validation="BLOCKED",
                  bootstrap_readiness="BLOCKED", limited_d11="BLOCKED", operator_authorization="NOT_GRANTED",
                  bootstrap_activation=False, siemens_attach_permitted=False, engineering="NOT_RUN",
                  full_offline_qualification="BLOCKED", production_qualification="NOT_RUN", nf11="NOT_RUN")
    try:
        if experiment not in {"B1", "HANDLE_DIAGNOSTICS"}:
            raise ValueError("Unsupported local experiment.")
        q, run, metadata, manifest_hash, count = verify_qualification(root, run_id)
        if experiment == "HANDLE_DIAGNOSTICS":
            ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
            names = [n.attrib["testName"] for p in (root / ".tia-bridge/resilience" / run_id / "test-results").glob("*.trx")
                     for n in ET.parse(p).getroot().findall(".//t:UnitTestResult", ns)]
            if any(not any("." + suite + "." in name for name in names) for suite in ("BootstrapDiagnosticTests", "WorkerResourceTests")):
                raise ValueError("Diagnostic budget/phase safety suites missing.")
            result.update(task="E2-G-D", purpose=experiment, maximum_cycles=3, b1_qualification="NOT_RUN")
        result.update(evidence_validation="PASS", manifest_sha256=manifest_hash, artifact_count=count,
                      git_sha=metadata["git_sha"], profile_version="1.0.0", qualification_profile="SHARED_WORKSTATION",
                      profile_definition_sha256=metadata["profile_definition_sha256"], nf08=q["nf"]["NF-08"], nf11=q["nf"]["NF-11"]["status"])
        failures = assess_qualification(q, run)
        result["reasons"].extend(failures)
        result["safety_gate"] = "FAIL" if any(f["code"].startswith("SAFETY_") for f in failures) else "PASS"
        result["performance_gate"] = "FAIL" if any(f["code"].startswith(("D11_NF-", "PERFORMANCE_", "RESOURCE_")) for f in failures) else "PASS"
        result["limited_d11"] = "BLOCKED" if failures else "PASS"
        plan = "E2-B1-D_TEST_PLAN.md" if experiment == "HANDLE_DIAGNOSTICS" else "E2-B1_TEST_PLAN.md"
        issues = assess_dossier(dossier, metadata["builds_sha256"], reporter().performance_evidence.digest(root / "docs" / plan), experiment)
        result["reasons"].extend(issues)
        result["bootstrap_readiness"] = "BLOCKED" if issues else "PASS"
        result["status"] = "BLOCKED" if failures or any(not i.get("review_required") for i in issues) else "REVIEW_REQUIRED" if issues else "READY_FOR_OPERATOR_AUTHORIZATION"
    except (OSError, ValueError, KeyError, TypeError, AttributeError, ET.ParseError) as error:
        result["reasons"].append(dict(code="QUALIFICATION_EVIDENCE_INVALID", description=str(error)))
        if not dossier:
            result["reasons"].append(dict(code="B1_DOSSIER_MISSING", description="No exact operator-reviewed B1 dossier was provided."))
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--dossier", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--experiment", choices=["B1", "HANDLE_DIAGNOSTICS"], default="B1")
    args = parser.parse_args(argv)
    try:
        d = reporter().performance_evidence.read_json(args.dossier) if args.dossier else None
        result = evaluate(ROOT, args.run_id, d, args.experiment)
    except (OSError, ValueError) as error:
        result = dict(status="BLOCKED", reasons=[dict(code="DOSSIER_INVALID", description=str(error))],
                      operator_authorization="NOT_GRANTED", siemens_attach_permitted=False, bootstrap_activation=False)
    if args.output:
        reporter().performance_evidence.write_new_json(args.output, result)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == "READY_FOR_OPERATOR_AUTHORIZATION" else 1


if __name__ == "__main__":
    raise SystemExit(main())
