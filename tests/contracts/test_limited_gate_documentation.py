"""Check D-11 documentation against immutable B-02D evidence; starts no Host or worker."""
import hashlib
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "tests/resilience"


class LimitedGateDocumentationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = json.loads((FOLDER / "qualification.json").read_text(encoding="utf-8"))
        cls.assessment = json.loads((FOLDER / "x01-limited-gate.json").read_text(encoding="utf-8"))

    def test_historical_evidence_is_unchanged_and_bound_to_the_assessment(self):
        self.assertEqual(self.assessment["source_run_id"], self.source["run_id"])
        for name, expected in self.assessment["source_files_sha256"].items():
            with self.subTest(file=name):
                data = (ROOT / name).read_bytes()
                git_lf = self.assessment["source_git_lf_sha256"][name]
                self.assertIn(hashlib.sha256(data).hexdigest(), {expected, git_lf})
                self.assertEqual(hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest(), git_lf)
        self.assertEqual(self.source["gate"], "FAIL")
        self.assertEqual(self.source["x01_readiness"], "NOT_READY")
        self.assertEqual(self.source["nf"]["NF-03"]["status"], "FAIL")
        self.assertEqual(self.source["nf"]["NF-11"]["status"], "NOT_RUN")

    def test_required_rt_nf_and_safety_criteria_have_positive_evidence(self):
        self.assertEqual(set(self.assessment["required_rt"]), {"RT-03", "RT-05", "RT-06", "RT-15", "RT-16"})
        for key in self.assessment["required_rt"]:
            self.assertEqual(self.source["rt"][key]["status"], "PASS")
            self.assertTrue(self.source["rt"][key]["scenarios"])
        pointers = self.assessment["required_nf_source_pointers"]
        self.assertEqual(set(pointers), {f"NF-{n:02d}" for n in (1, 2, 3, 4, 5, 6, 12, 14, 15)})
        for key, pointer in pointers.items():
            value = self.source
            for part in pointer.strip("/").split("/"):
                value = value[part]
            self.assertEqual(value, "PASS", key)
        scenarios = {item["id"]: item for item in self.source["scenarios"]}
        matrix = json.loads((FOLDER / "matrix.json").read_text(encoding="utf-8"))
        self.assertEqual(set(scenarios), set(matrix["required_scenarios"]))
        self.assertTrue(all(s["status"] == "PASS" for s in scenarios.values()))
        for criterion, names in self.assessment["additional_criteria_scenarios"].items():
            self.assertTrue(names, criterion)
            for name in names:
                self.assertEqual(scenarios[name]["status"], "PASS", criterion)
        self.assertIn("old-epoch-receipt", self.assessment["additional_criteria_scenarios"]["stale_epoch_rejected"])
        self.assertIn("lease-expiry-live-call", self.assessment["additional_criteria_scenarios"]["unknown_quarantine_no_replay"])

    def test_scoped_slos_follow_the_measured_ntfs_profile(self):
        measured = self.source["measurements"]
        perf = measured["b02c"]
        self.assertEqual(perf["state_filesystem"], "NTFS")
        for key in ("status_one_client", "status_eight_clients"):
            self.assertGreaterEqual(perf[key]["samples"], 1000)
            self.assertLessEqual(perf[key]["p95_ms"], 100)
            self.assertLessEqual(perf[key]["p99_ms"], 250)
        health = measured["http_readers_recovery"]["health_8"]
        self.assertLessEqual(health["p95_ms"], 100)
        self.assertLessEqual(health["p99_ms"], 250)
        self.assertLessEqual(perf["submit_eight_clients"]["p95_ms"], 250)
        self.assertLessEqual(perf["submit_eight_clients"]["p99_ms"], 500)
        self.assertLessEqual(measured["http_readers_recovery"]["startup_history"]["p95_ms"], 5000)
        self.assertLessEqual(measured["http_readers_recovery"]["safe_read_after_recovery_ms"], 30000)
        workspace = measured["workspace_replace"]
        self.assertEqual(workspace["filesystem"], "NTFS")
        self.assertEqual(workspace["save_replace_1mib"]["samples"], 100)
        self.assertLessEqual(workspace["save_replace_1mib"]["p95_ms"], 100)
        self.assertEqual(measured["queue_capacity"]["active_operations"], 1000)

    def test_real_idle_and_short_run_are_not_promoted_to_full_soak(self):
        idle = self.source["measurements"]["idle"]
        self.assertGreaterEqual(idle["duration_s"], 300)
        self.assertLess(idle["cpu_percent_normalized_all_logical_processors"], 1)
        self.assertLessEqual(idle["host_max_ram_mib"], 200)
        self.assertLessEqual(idle["worker_max_ram_mib"], 250)
        short = self.source["nf"]["NF-11"]
        self.assertGreaterEqual(short["completed_operations"], 1000)
        self.assertLess(short["measured_seconds"], 28800)
        self.assertEqual(short["status"], "NOT_RUN")
        limits = self.assessment["preserved_limits"]
        self.assertEqual(limits["nf11"], "NOT_RUN")
        self.assertEqual(limits["nf11_required_seconds_in_b10b"], 28800)
        self.assertEqual(limits["r14"], "ACTIVE")

    def test_exfat_failure_and_future_environment_are_not_qualified_by_ntfs(self):
        repo = self.source["measurements"]["repository_replace"]
        self.assertEqual(repo["filesystem"], "exFAT")
        self.assertFalse(repo["without_worker"]["success"])
        self.assertGreater(repo["without_worker"]["distribution"]["p95_ms"], 100)
        self.assertEqual(self.assessment["preserved_limits"]["nf03_exfat"], "FAIL")
        future = self.assessment["prepared_test_folder"]
        self.assertEqual(future["filesystem"], "UNVERIFIED")
        self.assertFalse(future["project_access_in_this_task"])
        self.assertFalse(future["physical_download_authorized"])
        self.assertEqual(future["x01_mode"], "read-only")

    def test_distinct_levels_preserve_offline_failure_and_unrun_layers(self):
        status = self.assessment["statuses"]
        delivery = self.source["delivery_validation"]
        self.assertEqual(status["b02_offline_test_execution"], delivery["test_execution_status"])
        self.assertEqual(status["b02_offline_qualification"], delivery["offline"])
        self.assertEqual(status["x01_limited_read_only_gate"], "PASS")
        for key in ("engineering", "runtime"):
            self.assertEqual(status[key], delivery[key])
            self.assertEqual(status[key], "NOT_RUN")
        self.assertEqual(status["production_qualification"], self.source["production_stability_qualification"])
        self.assertEqual(status["production_qualification"], "NOT_RUN")
        self.assertEqual(self.assessment["critical_defects_review"]["unresolved_critical_execution_or_process_isolation_defects"], 0)


if __name__ == "__main__":
    unittest.main()
