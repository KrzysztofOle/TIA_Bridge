"""Qualification must never promote stale/missing evidence or conflate short tests with an eight-hour soak."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("report_resilience", ROOT / "scripts/report_resilience.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
RUN = "1" * 32
START = "2026-10-25T00:00:00+00:00"


def fixture(root):
    matrix = json.loads((ROOT / "tests/resilience/matrix.json").read_text(encoding="utf-8"))
    (root / "tests/resilience").mkdir(parents=True)
    (root / "tests/resilience/matrix.json").write_text(json.dumps(matrix), encoding="utf-8")
    folder = root / ".tia-bridge/resilience" / RUN
    folder.mkdir(parents=True)
    for name in matrix["required_scenarios"]:
        data = {}
        if name == "idle-300s":
            data = dict(duration_s=300, cpu_percent_normalized_all_logical_processors=.1, host_max_ram_mib=100, worker_max_ram_mib=50)
        if name == "operations-1001":
            data = dict(completed_operations=1001, soak_seconds=400, ram_growth_mib=1, handle_growth_fraction=0)
        if name == "http-readers-recovery":
            data = dict(health_8=dict(p95_ms=10, p99_ms=20), startup_history=dict(p95_ms=1000), safe_read_after_recovery_ms=1000)
        if name == "repository-replace-control":
            data = {"with_worker": {"success": True}, "without_worker": {"success": True}}
        (folder / (name + ".json")).write_text(json.dumps(dict(schema_version="1.0", run_id=RUN, scenario=name, requirements=matrix["required_rt"], status="PASS", started_at=START, elapsed_ms=1, data=data)), encoding="utf-8")
    (root / ".tia-bridge/b02c").mkdir()
    (root / ".tia-bridge/b02c/performance.json").write_text(json.dumps({"observed_at": START, "status_one_client": dict(p95_ms=10, p99_ms=20), "status_eight_clients": dict(p95_ms=10, p99_ms=20), "submit_eight_clients": dict(p95_ms=50, p99_ms=70), "edit_one_mib": dict(p95_ms=20)}), encoding="utf-8")
    (folder.parent / ("environment-" + RUN + ".json")).write_text(json.dumps({"run_id": RUN}), encoding="utf-8")
    return folder


class ResilienceReportTests(unittest.TestCase):
    def test_missing_failed_stale_and_wrong_run_evidence_is_rejected(self):
        for fault in ["missing", "skipped", "stale", "run", "duplicate", "short_idle"]:
            with self.subTest(fault=fault), tempfile.TemporaryDirectory(prefix="tia-bridge-resilience-report-") as directory:
                root = Path(directory)
                folder = fixture(root)
                path = folder / "idle-300s.json"
                value = json.loads(path.read_text(encoding="utf-8"))
                if fault == "missing":
                    path.unlink()
                elif fault == "duplicate":
                    (folder / "duplicate.json").write_text(json.dumps(value), encoding="utf-8")
                else:
                    if fault == "skipped": value["status"] = "SKIP"
                    if fault == "stale": value["started_at"] = "2020-01-01T00:00:00+00:00"
                    if fault == "run": value["run_id"] = "2" * 32
                    if fault == "short_idle": value["data"]["duration_s"] = 299
                    path.write_text(json.dumps(value), encoding="utf-8")
                with self.assertRaises(ValueError):
                    module.qualification(root, RUN, START)

    def test_short_synthetic_pass_cannot_claim_full_stability_or_formal_x01_readiness(self):
        with tempfile.TemporaryDirectory(prefix="tia-bridge-resilience-report-") as directory:
            root = Path(directory)
            fixture(root)
            result = module.qualification(root, RUN, START, allow_legacy=True)
            self.assertEqual("BLOCKED", result["gate"])
            self.assertEqual("NOT_READY", result["x01_readiness"])
            self.assertEqual("NOT_RUN", result["nf"]["NF-11"]["status"])
            self.assertTrue(all(result["rt"][key]["status"] == "PASS" for key in result["required_rt"]))
            path = root / ".tia-bridge/resilience" / RUN / "operations-1001.json"
            value = json.loads(path.read_text(encoding="utf-8"))
            value["data"]["soak_seconds"] = 28801
            value["data"]["duration_is_not_eight_hour_soak"] = True
            path.write_text(json.dumps(value), encoding="utf-8")
            self.assertEqual("NOT_RUN", module.qualification(root, RUN, START, allow_legacy=True)["nf"]["NF-11"]["status"])

    def test_repository_replace_failure_is_not_hidden_by_successful_ntfs_tests(self):
        with tempfile.TemporaryDirectory(prefix="tia-bridge-resilience-report-") as directory:
            root = Path(directory)
            folder = fixture(root)
            path = folder / "repository-replace-control.json"
            value = json.loads(path.read_text(encoding="utf-8"))
            value["data"] = {"with_worker": {"success": False}, "without_worker": {"success": False}}
            path.write_text(json.dumps(value), encoding="utf-8")
            result = module.qualification(root, RUN, START, allow_legacy=True)
            self.assertEqual("FAIL", result["gate"])
            self.assertEqual("FAIL", result["nf"]["NF-03"]["status"])
            self.assertEqual("NOT_READY", result["x01_readiness"])

    def test_failed_resource_window_is_preserved_instead_of_aborting_the_qualification_report(self):
        with tempfile.TemporaryDirectory(prefix="tia-bridge-resilience-report-") as directory:
            root = Path(directory)
            folder = fixture(root)
            path = folder / "idle-300s.json"
            value = json.loads(path.read_text(encoding="utf-8"))
            value["status"] = "FAIL"
            value["data"]["cpu_percent_normalized_all_logical_processors"] = 3.7
            path.write_text(json.dumps(value), encoding="utf-8")
            result = module.qualification(root, RUN, START, allow_legacy=True)
            self.assertEqual("FAIL", result["gate"])
            self.assertEqual("FAIL", result["nf"]["NF-08"]["measured_idle_status"])
            self.assertEqual("FAIL", next(row for row in result["scenarios"] if row["id"] == "idle-300s")["status"])
