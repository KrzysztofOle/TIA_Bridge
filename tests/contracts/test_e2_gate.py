"""E2-G: synthetic policy, binding, sealed-evidence and no-authority regressions."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("e2_gate", ROOT / "scripts/e2_gate.py")
gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)


def dossier():
    b = dict(project_key="11111111-1111-1111-1111-111111111111",
             project_path=r"C:\synthetic\T05_RecipeSim_V19_Testowy.ap19", tia_pid=1234,
             tia_start_ticks=123456789, executable_path=r"C:\synthetic\Portal.exe",
             session_sid="S-1-5-21-123-456-789-1001", tia_build="19.0.0.1",
             host_build="1.0+" + "a" * 64, worker_build="1.0+" + "b" * 64)
    return dict(schema_version="1.0", scope=copy.deepcopy(gate.SCOPE), read_scope=gate.READ_SCOPE.copy(),
                plan_sha256="c" * 64, expected_binding=b, observed_binding=copy.deepcopy(b),
                clean_test_copy_confirmed=True, ui_clean_confirmed=True, no_colliding_siemens_operations=True,
                monitoring_ready=True, safe_stop_ready=True, bootstrap_state="INACTIVE",
                is_modified_before_start="PENDING_AUTHORIZED_READ")


def policy():
    q = dict(rt={key: dict(status="PASS") for key in gate.RT},
             nf={key: dict(status="PASS") for key in gate.NF},
             sample_evidence_status="PASS", measurements=dict(idle=dict(duration_s=300)))
    q["nf"].update({"NF-08": dict(status="NOT_RUN", measured_idle_status="PASS"),
                    "NF-11": dict(status="NOT_RUN", completed_operations=1001)})
    return q, dict(test_execution_status="PASS")


class DiagnosticDossierTests(unittest.TestCase):
    def test_diagnostic_scope_is_explicit_and_never_weakens_legacy_B1(self):
        d = dossier(); d.update(schema_version="1.1", scope=copy.deepcopy(gate.DIAGNOSTIC_SCOPE),
                                phase_telemetry_ready=True, handle_type_analysis="NOT_AVAILABLE")
        builds = dict(host="a"*64, worker="b"*64)
        self.assertEqual([], gate.assess_dossier(d, builds, "c"*64, "HANDLE_DIAGNOSTICS"))
        self.assertTrue(gate.assess_dossier(d, builds, "c"*64))
        for key, bad in [("maximum_cycles",20),("purpose","QUALIFICATION"),("automatic_retry",True),("plan_id","E2-B1")]:
            changed = copy.deepcopy(d); changed["scope"][key] = bad
            self.assertTrue(gate.assess_dossier(changed, builds, "c"*64, "HANDLE_DIAGNOSTICS"))
        d["phase_telemetry_ready"] = False
        self.assertTrue(gate.assess_dossier(d, builds, "c"*64, "HANDLE_DIAGNOSTICS"))


def sealed_fixture(root):
    """Entirely synthetic run, including TRX, samples, process records and inert build bytes."""
    def fixture_module(name):
        spec = importlib.util.spec_from_file_location(name, ROOT / "tests/contracts" / (name + ".py"))
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module
    r = gate.reporter(); e = r.performance_evidence; run_id = "1" * 32
    f = fixture_module("test_performance_evidence"); f.evidence = e
    folder = f.fixtures.fixture(root); v = folder / "validation"; v.mkdir()
    for scenario in e.PROFILES:
        value = f.fixture(root, scenario); f.publish(root, value)
        if scenario != "api-performance":
            p = folder / (scenario + ".json"); record = json.loads(p.read_text())
            record["data"]["filesystem"] = "NTFS"
            if scenario == "workspace-replace":
                record["data"]["save_replace_1mib"] = value["metrics"][0]["distribution"]
            else:
                for metric, field in zip(value["metrics"], ("without_worker", "with_worker")):
                    record["data"][field] = dict(distribution=metric["distribution"], refused_samples=0, success=True)
            p.write_text(json.dumps(record))
    e.write_new_json(v / "environment.json", dict(run_id=run_id))
    identity = dict(run_id=run_id, started_at=f.START, git_sha="a" * 40,
                    qualification_profile="SHARED_WORKSTATION", profile_version="1.0.0",
                    profile_definition_sha256=e.PROFILE_SHA256, applied_test_harness_limits=e.TEST_HARNESS_LIMITS)
    e.write_new_json(v / "run-metadata.json", identity)
    builds = {}
    for key, relative in {"host": "src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll",
                          "worker": "src/TiaBridge.Worker.V19/bin/Release/net48/TiaBridge.Worker.V19.exe"}.items():
        p = root / relative; p.parent.mkdir(parents=True); p.write_bytes(b"synthetic, never executable")
        builds[key] = e.digest(p)
    plan = root / "docs/E2-B1_TEST_PLAN.md"; plan.parent.mkdir(); plan.write_text("synthetic plan")
    names = []
    def add(prefix, cases):
        for name, count in cases.items(): names.extend(prefix + name + str(i) for i in range(count))
    add("TiaBridge.Core.Tests.", {"Golden_models_paths_manifests_and_capabilities": 100,
        "Shared_shape_mutations_have_the_same_verdict": 19, "Every_required_parameter_result_and_nested_member": 19,
        "Method_specific_errors_round_trip": 19, "Every_lifecycle_edge_obeys": 147,
        "Every_method_has_valid_typed_request_and_response": 19, "Core_transitive_assembly_dependencies": 1,
        "Required_nullable_diagnostic_age_is_preserved": 1, "QualificationEvidenceTests.Synthetic": 1})
    add("TiaBridge.Host.Tests.StorageTests.", {"One_hundred_concurrent_identical_submits": 1,
        "Deterministic_process_exit_at_admission_boundary": 2, "Outbox_claim_is_compare_and_swap_across_independent_processes": 1,
        "Failed_upgrade_rolls_back": 1, "Missing_or_corrupt_content": 3, "Artifact_crash_boundaries": 2,
        "Sqlite_full_and_artifact_io_full": 1})
    add("TiaBridge.Host.Tests.SchedulerTests.", {"Real_worker_completes_a_durable_operation_once": 3,
        "Queued_cancel_is_atomic_and_never_dispatches": 1, "Hung_mutation_times_out_to_durable_quarantine_without_retry": 5,
        "Host_process_restart_quarantines_a_dispatched_mutation": 1,
        "Host_status_remains_responsive_while_a_real_fake_process_is_hung": 1,
        "Late_success_is_retained_without_changing_unknown_to_success": 1,
        "Wrong_fence_or_plan_from_a_real_worker_is_evidence_only": 2,
        "Shared_ipc_schema_examples_round_trip_through_actual_framing": 9})
    add("TiaBridge.Host.Tests.HttpApiTests.", {name: 1 for name in [
        "Real_host_cli_and_fake_complete_the_submit_status_events_result_cycle", "All_nineteen_valid_requests_have_an_explicit_http_outcome",
        "Metadata_post_uses_the_same_durable_idempotency_and_cancel_journal", "Host_restart_keeps_results_identity_and_history_and_rotates_transport_token",
        "Discovery_is_user_only_and_a_widened_acl_is_rejected_by_cli", "Worker_start_failure_becomes_a_durable_error_while_api_stays_available",
        "One_and_eight_clients_keep_status_and_workspace_responsive_during_real_worker_hang",
        "Queue_is_bounded_at_one_thousand_and_duplicate_health_and_status_still_work",
        "Unknown_mutation_stays_quarantined_after_api_cancel_and_host_restart"]})
    add("TiaBridge.Host.Tests.ResilienceTests.", {"RT03_admission_crash": 5, "RT03_running_crash": 6,
        "RT03_explicit_QUEUED_state": 1, "RT05_two_synthetic_writes": 1, "RT06_receipt_already": 1,
        "RT15_owned_child_lock": 1, "RT16_one_hundred": 1, "Cancel_dispatch_race": 2,
        "Real_process_pipe_attack": 8, "Damaged_final_result_WAL_frame": 2, "Corrupt_artifact_never": 3,
        "Five_minute_idle_resource_profile": 1, "One_thousand_and_one_operations": 1,
        "Independent_workspace_save_and_replace": 1, "Health_result_events_and_recovery": 1})
    add("TiaBridge.Host.Tests.", {suite + ".Synthetic": 1 for suite in gate.SUITES[:-1]})
    trx = folder / "test-results"; trx.mkdir()
    (trx / "synthetic.trx").write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>' +
        ''.join('<UnitTestResult outcome="Passed" testName="' + name + '"/>' for name in names) + '</Results></TestRun>')
    summary = gate.load("validate_offline").check_trx(trx, host_required=True, scheduler_required=True, api_required=True, resilience_required=True)
    fingerprint, _ = r.source_fingerprint(root)
    e.write_new_json(v / "build-metadata.json", dict(run_id=run_id, source_fingerprint_lf_sha256=fingerprint, builds_sha256=builds))
    q = r.qualification(root, run_id, f.START)
    e.write_new_json(v / "resilience.json", q)
    run = identity | dict(ended_at=f.START, test_execution_status="PASS", dotnet=summary,
        python=dict(total=1, passed=1, failed=0, skipped=0),
        steps=[dict(name=name, exit_code=0) for name in gate.STEPS],
        preflight=dict(status="PASS_WITH_LOAD", blockers=[], profile="SHARED_WORKSTATION", profile_version="1.0.0"))
    e.write_new_json(v / "run.json", run); e.seal_run(folder, run_id)
    return folder


class E2GateTests(unittest.TestCase):
    def binding_issues(self, d):
        return gate.assess_dossier(d, dict(host="a" * 64, worker="b" * 64), "c" * 64)

    def test_approved_shared_and_unchanged_strict(self):
        profile = gate.reporter().performance_evidence.PROFILE_DEFINITION
        self.assertEqual("STRICT", profile["default_profile"])
        self.assertEqual({"version": "1.0.0", "test_harness": {"fake_worker_phase_observation_s": 10}, "limits": {
            "nf01_status_p95_ms": 100, "nf01_status_p99_ms": 250, "nf02_submit_p95_ms": 250,
            "nf02_submit_p99_ms": 500, "nf03_replace_p95_ms": 100, "nf05_startup_p95_ms": 5000,
            "nf08_host_idle_cpu_mean_percent": 1, "nf08_host_ram_mib": 200, "nf08_worker_ram_mib": 250}}, profile["profiles"]["STRICT"])
        self.assertEqual("1.0.0", profile["profiles"]["SHARED_WORKSTATION"]["version"])
        self.assertEqual(28800, profile["unchanged_protective_limits"]["nf11_soak_minimum_h"] * 3600)

    def test_current_required_safety_pass(self):
        q, r = policy(); self.assertEqual([], gate.assess_qualification(q, r))

    def test_each_required_safety_and_nf_failure_blocks(self):
        for group, keys in (("rt", gate.RT), ("nf", gate.NF)):
            for key in keys:
                with self.subTest(key=key):
                    q, r = policy(); q[group][key]["status"] = "FAIL"
                    self.assertTrue(gate.assess_qualification(q, r))

    def test_failed_functional_execution_cannot_be_hidden_by_green_subsets(self):
        q, r = policy(); r["test_execution_status"] = "FAIL"
        self.assertEqual("SAFETY_TESTS_NOT_PASS", gate.assess_qualification(q, r)[0]["code"])

    def test_new_profile_kpi_failure_blocks(self):
        q, r = policy(); q["sample_evidence_status"] = "FAIL"
        self.assertTrue(gate.assess_qualification(q, r))

    def test_nf11_not_run_is_allowed_only_for_limited_gate(self):
        q, r = policy(); self.assertEqual([], gate.assess_qualification(q, r))
        self.assertEqual("NOT_RUN", q["nf"]["NF-11"]["status"])
        # Full qualification is independently calculated by report_resilience, tested by its soak regression.
        self.assertEqual("BLOCKED", gate.evaluate(ROOT, "bad")["full_offline_qualification"])

    def test_nf08_not_run_is_preserved_without_false_pass(self):
        q, r = policy(); before = copy.deepcopy(q)
        self.assertEqual([], gate.assess_qualification(q, r)); self.assertEqual(before, q)
        q["nf"]["NF-08"]["status"] = "FAIL"
        self.assertTrue(gate.assess_qualification(q, r))

    def test_inactive_bootstrap_is_preparable_without_operator_authorization(self):
        self.assertEqual([], self.binding_issues(dossier()))

    def test_invalid_project_key_and_each_process_or_build_drift(self):
        d = dossier(); d["expected_binding"]["project_key"] = "not-a-key"
        self.assertTrue(self.binding_issues(d))
        for field in sorted(gate.BINDING):
            with self.subTest(field=field):
                d = dossier(); b = d["observed_binding"]
                b[field] = b[field] + 1 if type(b[field]) is int else b[field] + "drift"
                self.assertTrue(self.binding_issues(d))

    def test_exact_read_scope_rejects_missing_extra_duplicate_getter(self):
        for values in (gate.READ_SCOPE[:-1], gate.READ_SCOPE + ["Project.Save"], gate.READ_SCOPE[:-1] + [gate.READ_SCOPE[0]]):
            d = dossier(); d["read_scope"] = values; self.assertTrue(self.binding_issues(d))

    def test_b2_activation_and_multitarget_are_rejected(self):
        for field, value in (("plan_id", "E2-B2"), ("project_name", "T04_T05-000_Testowy"), ("target_count", 2)):
            d = dossier(); d["scope"][field] = value; self.assertTrue(self.binding_issues(d))

    def test_dirty_copy_monitoring_stop_and_active_grant_require_review(self):
        for field, value in (("clean_test_copy_confirmed", False), ("ui_clean_confirmed", False),
                ("no_colliding_siemens_operations", False), ("monitoring_ready", False), ("safe_stop_ready", False),
                ("is_modified_before_start", True), ("is_modified_before_start", None), ("bootstrap_state", "ACTIVE")):
            d = dossier(); d[field] = value
            issues = self.binding_issues(d); self.assertTrue(issues)
            self.assertTrue(all(i["review_required"] for i in issues))

    def test_missing_dossier_and_changed_plan_block(self):
        self.assertTrue(self.binding_issues(None))
        d = dossier(); d["plan_sha256"] = "d" * 64; self.assertTrue(self.binding_issues(d))

    def test_malformed_types_cannot_be_accepted_as_true_or_identity(self):
        d = dossier(); d["scope"]["fresh"] = 1; self.assertTrue(self.binding_issues(d))
        d = dossier(); d["expected_binding"]["tia_pid"] = True; self.assertTrue(self.binding_issues(d))

    def test_ready_has_no_permission_activation_or_dispatch_side_effect(self):
        q, r = policy(); metadata = dict(git_sha="a" * 40, profile_definition_sha256="f" * 64,
                                        builds_sha256=dict(host="a" * 64, worker="b" * 64))
        with patch.object(gate, "verify_qualification", return_value=(q, r, metadata, "e" * 64, 10)):
            # The actual plan hash is supplied, never a synthetic permission or local bootstrap document.
            import hashlib
            d = dossier(); d["plan_sha256"] = hashlib.sha256((ROOT / "docs/E2-B1_TEST_PLAN.md").read_bytes()).hexdigest()
            result = gate.evaluate(ROOT, "1" * 32, d)
            self.assertEqual("READY_FOR_OPERATOR_AUTHORIZATION", result["status"])
            self.assertEqual("NOT_GRANTED", result["operator_authorization"])
            self.assertFalse(result["bootstrap_activation"]); self.assertFalse(result["siemens_attach_permitted"])
            self.assertEqual("NOT_RUN", result["engineering"])
            self.assertEqual("BLOCKED", result["full_offline_qualification"])
            d["ui_clean_confirmed"] = False
            self.assertEqual("REVIEW_REQUIRED", gate.evaluate(ROOT, "1" * 32, d)["status"])

    def test_missing_evidence_or_manifest_and_corrupt_hash_block(self):
        e = gate.reporter().performance_evidence
        for fault in ("missing", "manifest", "corrupt"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary); folder = root / ".tia-bridge/resilience" / ("1" * 32)
                folder.mkdir(parents=True); (folder / "synthetic.json").write_text('{}')
                if fault != "manifest": e.seal_run(folder, "1" * 32)
                if fault == "missing": (folder / "synthetic.json").unlink()
                if fault == "corrupt": (folder / "synthetic.json").write_text('{"tampered":true}')
                self.assertEqual("BLOCKED", gate.evaluate(root, "1" * 32)["status"])

    def test_profile_change_after_run_and_incomplete_metadata_are_rejected(self):
        e = gate.reporter().performance_evidence
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); folder = root / ".tia-bridge/resilience" / ("1" * 32); v = folder / "validation"; v.mkdir(parents=True)
            run = dict(run_id="1" * 32, started_at="2026-10-27T00:00:00+00:00", ended_at="2026-10-27T01:00:00+00:00",
                       qualification_profile="SHARED_WORKSTATION", profile_version="1.0.0", profile_definition_sha256="changed")
            (v / "run.json").write_text(json.dumps(run)); (v / "resilience.json").write_text(json.dumps(run))
            (v / "run-metadata.json").write_text(json.dumps(run | dict(git_sha="a" * 40)))
            e.seal_run(folder, "1" * 32)
            result = gate.evaluate(root, "1" * 32)
            self.assertEqual("BLOCKED", result["status"])
            self.assertIn("Profile changed", result["reasons"][0]["description"])

    def test_read_scope_agrees_with_existing_b0_document_and_worker(self):
        text = (ROOT / "src/TiaBridge.Host/Scheduling/V19InspectionProfile.cs").read_text()
        for read in gate.READ_SCOPE: self.assertIn('"' + read + '"', text)
        self.assertEqual(13, len(gate.READ_SCOPE))

    def test_cli_has_no_authorize_attach_or_execution_options(self):
        # A completed assessment cannot feed any executable/authorization path.
        import subprocess, sys
        help_result = subprocess.run([sys.executable, "-B", "scripts/e2_gate.py", "--help"], cwd=ROOT, capture_output=True, text=True)
        self.assertEqual(0, help_result.returncode)
        self.assertNotIn("--authorize", help_result.stdout); self.assertNotIn("--execute", help_result.stdout)
        for option in ("--authorize", "--attach", "--execute"):
            result = subprocess.run([sys.executable, "-B", "scripts/e2_gate.py", "--run-id", "1" * 32, option], cwd=ROOT, capture_output=True)
            self.assertEqual(2, result.returncode)

    def test_profile_selection_does_not_mutate_callers_environment(self):
        with patch.dict(os.environ, {"TIA_BRIDGE_QUALIFICATION_PROFILE": "STRICT"}):
            self.assertEqual("SHARED_WORKSTATION", gate.reporter().performance_evidence.PROFILE_NAME)
            self.assertEqual("STRICT", os.environ["TIA_BRIDGE_QUALIFICATION_PROFILE"])

    def test_complete_sealed_evidence_is_recomputed_and_build_or_source_drift_blocks(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); sealed_fixture(root)
            result = gate.evaluate(root, "1" * 32)
            self.assertEqual("PASS", result["evidence_validation"], result)
            self.assertEqual("PASS", result["limited_d11"])
            self.assertEqual("BLOCKED", result["status"])
            self.assertEqual(["B1_DOSSIER_MISSING"], [r["code"] for r in result["reasons"]])
            binary = root / "src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll"
            binary.write_bytes(b"changed build")
            self.assertEqual("BLOCKED", gate.evaluate(root, "1" * 32)["evidence_validation"])
            binary.write_bytes(b"synthetic, never executable")
            (root / "synthetic.cs").write_text("changed source")
            # The full source fingerprint includes the source trees; the bundle also binds this particular file.
            self.assertEqual("BLOCKED", gate.evaluate(root, "1" * 32)["evidence_validation"])
