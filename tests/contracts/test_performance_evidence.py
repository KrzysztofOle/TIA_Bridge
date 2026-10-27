"""Corruption and historical evidence must not become a new qualification PASS. Synthetic samples only."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("performance_evidence", ROOT / "scripts/performance_evidence.py")
evidence = importlib.util.module_from_spec(spec); spec.loader.exec_module(evidence)
report_spec = importlib.util.spec_from_file_location("report_resilience", ROOT / "scripts/report_resilience.py")
reporter = importlib.util.module_from_spec(report_spec); report_spec.loader.exec_module(reporter)
fixture_spec = importlib.util.spec_from_file_location("report_fixtures", ROOT / "tests/contracts/test_resilience_report.py")
fixtures = importlib.util.module_from_spec(fixture_spec); fixture_spec.loader.exec_module(fixtures)
RUN = "1" * 32
START = "2026-10-25T00:00:00+00:00"


def fixture(root, scenario="api-performance"):
    binary = root / "synthetic.dll"; binary.write_bytes(b"synthetic fixture, never executable")
    code = root / "synthetic.cs"; code.write_text("// synthetic")
    metrics = []
    for name, (n, p95, p99) in evidence.PROFILES[scenario].items():
        values = [1] * n
        metrics.append(dict(id=name, unit="ms", time_kind="wall_clock", order="collection_snapshot_not_concurrent_completion_order", expected_samples=n, sample_count=n,
                            samples=[dict(id=i, value=v) for i, v in enumerate(values)], distribution=evidence.distribution(values),
                            p95_limit_ms=p95, p99_limit_ms=p99, p95_exceedance_count=0, p99_exceedance_count=0 if p99 is not None else None, refused_samples=0, status="PASS", workspace="synthetic-ntfs", filesystem="NTFS"))
    value = dict(schema_version="1.1", kind="nf-performance", run_id=RUN, scenario=scenario, test_name="SyntheticEvidenceTest", started_at=START, ended_at=START,
                 qualification_profile=evidence.PROFILE_NAME, profile_version=evidence.PROFILE_VERSION, profile_definition_sha256=evidence.PROFILE_SHA256,
                 applied_test_harness_limits=evidence.TEST_HARNESS_LIMITS,
                 applied_thresholds=evidence.PROFILE_DEFINITION["profiles"][evidence.PROFILE_NAME]["limits"],
                 git_sha="a" * 40, git_dirty=False, binaries_sha256={"synthetic.dll": evidence.digest(binary)}, sources_sha256={"synthetic.cs": evidence.digest(code)},
                 runtime="synthetic", os="synthetic", processors=8, state_path="synthetic-state", state_filesystem="NTFS", fake_scenario="Hang", config_sha256="b" * 64,
                 test_status="PASS", metrics=metrics, summary=None)
    if scenario == "api-performance":
        by_id = {metric["id"]: metric for metric in metrics}
        value["summary"] = {"observed_at": START, "submit_eight_clients": by_id["nf02-submit-eight"]["distribution"], "edit_one_mib": by_id["nf03-inplace-repository"]["distribution"], "status_one_client": by_id["nf01-status-one"]["distribution"], "status_eight_clients": by_id["nf01-status-eight"]["distribution"]}
    return value


def publish(root, value):
    folder = root / ".tia-bridge/resilience" / RUN / "performance"; folder.mkdir(parents=True, exist_ok=True)
    path = folder / (value["scenario"] + ".json")
    path.write_text(json.dumps(value), encoding="utf-8")
    manifest = dict(schema_version="1.1", kind="nf-performance-manifest", run_id=RUN, artifact=path.name, bytes=path.stat().st_size, sha256=evidence.digest(path))
    (folder / (value["scenario"] + ".manifest.json")).write_text(json.dumps(manifest), encoding="utf-8")
    return path


class PerformanceEvidenceTests(unittest.TestCase):
    def test_reproduces_actual_nearest_rank_including_the_slowest_sample(self):
        self.assertEqual(dict(samples=4, p50_ms=2, p95_ms=900, p99_ms=900, maximum_ms=900), evidence.distribution([900, 2, 1, 3]))
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); value = fixture(root); publish(root, value)
            self.assertEqual("PASS", evidence.load_bundle(root, RUN, "api-performance", START)["status"])

    def test_strict_profile_is_default_and_shared_profile_has_versioned_individual_thresholds(self):
        self.assertEqual("STRICT", evidence.PROFILE_DEFINITION["default_profile"])
        shared = evidence.PROFILE_DEFINITION["profiles"]["SHARED_WORKSTATION"]["limits"]
        strict = evidence.PROFILE_DEFINITION["profiles"]["STRICT"]["limits"]
        self.assertEqual(100, strict["nf03_replace_p95_ms"])
        self.assertEqual(500, shared["nf03_replace_p95_ms"])
        self.assertEqual(250, shared["nf01_status_p95_ms"])
        self.assertEqual(500, shared["nf02_submit_p95_ms"])
        self.assertEqual(10000, shared["nf05_startup_p95_ms"])
        self.assertEqual(1, strict["nf08_host_idle_cpu_mean_percent"])
        self.assertEqual(2, shared["nf08_host_idle_cpu_mean_percent"])
        self.assertEqual(strict["nf08_host_ram_mib"], shared["nf08_host_ram_mib"])

    def test_shared_profile_is_explicitly_selectable_in_the_same_validator(self):
        environment = os.environ.copy(); environment["TIA_BRIDGE_QUALIFICATION_PROFILE"] = "SHARED_WORKSTATION"
        probe = "import json,sys; sys.path.insert(0,'scripts'); import performance_evidence as e; print(json.dumps({'name':e.PROFILE_NAME,'version':e.PROFILE_VERSION,'nf03':e.PROFILES['workspace-replace']['nf03-ntfs-save-replace']}))"
        result = subprocess.run([sys.executable, "-B", "-c", probe], cwd=ROOT, env=environment, capture_output=True, text=True, check=True)
        self.assertEqual({"name": "SHARED_WORKSTATION", "version": "1.0.0-candidate", "nf03": [100, 500, None]}, json.loads(result.stdout))

    def test_performance_threshold_failure_and_integrity_failure_remain_distinct(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); value = fixture(root); metric = next(m for m in value["metrics"] if m["id"] == "nf03-inplace-repository")
            failed_value = metric["p95_limit_ms"] + 1
            for sample in metric["samples"]: sample["value"] = failed_value
            metric["distribution"] = evidence.distribution([failed_value] * 100); metric["p95_exceedance_count"] = 100; metric["status"] = "FAIL"; value["test_status"] = "FAIL"
            value["summary"]["edit_one_mib"] = metric["distribution"]
            publish(root, value)
            self.assertEqual("FAIL", evidence.load_bundle(root, RUN, "api-performance", START)["status"])
            artifact = root / "synthetic.dll"; artifact.write_bytes(b"tampered")
            with self.assertRaises(ValueError): evidence.load_bundle(root, RUN, "api-performance", START)

    def test_rejects_missing_duplicate_wrong_count_nonfinite_negative_and_mixed_samples(self):
        for fault in ["missing", "duplicate", "count", "nan", "inf", "negative", "aggregate", "run", "mixed", "unit", "clock", "threshold", "status", "stale", "build"]:
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); value = fixture(root); metric = value["metrics"][0]
                if fault == "missing": metric["samples"].pop()
                if fault == "duplicate": metric["samples"][-1]["id"] = 0
                if fault == "count": metric["sample_count"] = 63
                if fault == "nan": metric["samples"][0]["value"] = float("nan")
                if fault == "inf": metric["samples"][0]["value"] = float("inf")
                if fault == "negative": metric["samples"][0]["value"] = -1
                if fault == "aggregate": metric["distribution"]["p95_ms"] = .1
                if fault == "run": value["run_id"] = "2" * 32
                if fault == "mixed": metric["id"] = "nf03-ntfs-save-replace"
                if fault == "unit": metric["unit"] = "seconds"
                if fault == "clock": metric["time_kind"] = "cpu"
                if fault == "threshold": metric["p95_limit_ms"] = 999
                if fault == "status": metric["status"] = "FAIL"
                if fault == "stale": value["started_at"] = "2020-01-01T00:00:00Z"
                if fault == "build": value["binaries_sha256"]["synthetic.dll"] = "0" * 64
                publish(root, value)
                with self.assertRaises(ValueError): evidence.load_bundle(root, RUN, "api-performance", START)

    def test_hash_missing_artifact_and_broken_or_duplicate_json_are_rejected(self):
        for fault in ["hash", "missing", "json", "duplicate_json", "manifest_run"]:
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); value = fixture(root); path = publish(root, value)
                if fault == "hash": path.write_text(path.read_text() + " ")
                if fault == "missing": path.unlink()
                if fault in {"json", "duplicate_json"}:
                    path.write_text("{" if fault == "json" else '{"schema_version":"1.1","schema_version":"1.1"}')
                    manifest_path = path.with_name("api-performance.manifest.json"); manifest = json.loads(manifest_path.read_text()); manifest.update(bytes=path.stat().st_size, sha256=evidence.digest(path)); manifest_path.write_text(json.dumps(manifest))
                if fault == "manifest_run":
                    manifest_path = path.with_name("api-performance.manifest.json"); manifest = json.loads(manifest_path.read_text()); manifest["run_id"] = "2" * 32; manifest_path.write_text(json.dumps(manifest))
                with self.assertRaises(ValueError): evidence.load_bundle(root, RUN, "api-performance", START)

    def test_partial_and_legacy_are_explicitly_insufficient_while_a_real_slo_failure_is_retained(self):
        for fault in ["partial", "legacy", "slo"]:
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); value = fixture(root); metric = value["metrics"][0]
                if fault == "partial":
                    metric["samples"] = metric["samples"][:3]; metric["sample_count"] = 3; metric["distribution"] = evidence.distribution([1, 1, 1]); metric["status"] = "INCOMPLETE"; value["test_status"] = "FAIL"
                if fault == "legacy": value["schema_version"] = "1.0"; value.pop("metrics")
                if fault == "slo":
                    for sample in metric["samples"]: sample["value"] = 600
                    metric["distribution"] = evidence.distribution([600] * 64); metric["p95_exceedance_count"] = 64; metric["p99_exceedance_count"] = None if metric["p99_limit_ms"] is None else sum(sample["value"] > metric["p99_limit_ms"] for sample in metric["samples"]); metric["status"] = "FAIL"; value["test_status"] = "FAIL"; value["summary"]["submit_eight_clients"] = metric["distribution"]
                publish(root, value)
                self.assertEqual("FAIL" if fault == "slo" else "INSUFFICIENT", evidence.load_bundle(root, RUN, "api-performance", START)["status"])

    def test_run_manifest_prevents_overwrite_and_detects_missing_modified_and_extra_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory); path = folder / "proof.json"; evidence.write_new_json(path, {"value": 1}); original = path.read_bytes()
            with self.assertRaises(FileExistsError): evidence.write_new_json(path, {"value": 2})
            self.assertEqual(original, path.read_bytes()); evidence.seal_run(folder, RUN); evidence.verify_run(folder, RUN)
            with self.assertRaises(FileExistsError): evidence.seal_run(folder, RUN)
            path.write_bytes(b"corrupt")
            with self.assertRaises(ValueError): evidence.verify_run(folder, RUN)
            path.write_bytes(original); path.unlink()
            with self.assertRaises(ValueError): evidence.verify_run(folder, RUN)
            path.write_bytes(original); (folder / "extra").write_text("unexpected")
            with self.assertRaises(ValueError): evidence.verify_run(folder, RUN)

    def test_reporter_never_qualifies_historical_aggregates_as_new_full_sample_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); fixtures.fixture(root)
            with self.assertRaises(ValueError): reporter.qualification(root, RUN, START)
            result = reporter.qualification(root, RUN, START, allow_legacy=True)
            self.assertEqual("INSUFFICIENT", result["sample_evidence_status"])
            self.assertNotEqual("PASS", result["gate"])

    def test_reporter_consumes_only_run_bound_samples_and_rejects_summary_mixing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); folder = fixtures.fixture(root)
            for scenario in evidence.PROFILES:
                value = fixture(root, scenario); publish(root, value)
                if scenario != "api-performance":
                    path = folder / (scenario + ".json"); record = json.loads(path.read_text())
                    if scenario == "workspace-replace": record["data"]["save_replace_1mib"] = value["metrics"][0]["distribution"]
                    else:
                        for phase, metric in zip(["without_worker", "with_worker"], value["metrics"]): record["data"][phase] = dict(success=True, refused_samples=0, distribution=metric["distribution"])
                    path.write_text(json.dumps(record))
            validation = folder / "validation"; validation.mkdir(); (validation / "environment.json").write_text(json.dumps({"run_id": RUN}))
            metadata = validation / "run-metadata.json"; metadata.write_text(json.dumps(dict(run_id=RUN, git_sha="a" * 40, started_at=START, qualification_profile=evidence.PROFILE_NAME, profile_version=evidence.PROFILE_VERSION, profile_definition_sha256=evidence.PROFILE_SHA256, applied_test_harness_limits=evidence.TEST_HARNESS_LIMITS, preflight_status="PASS_WITH_LOAD")))
            result = reporter.qualification(root, RUN, START); self.assertEqual("PASS", result["sample_evidence_status"]); self.assertEqual("PASS_WITH_LOAD", result["preflight_status"])
            # The shared legacy file may be corrupt; it is never consumed by current qualification.
            (root / ".tia-bridge/b02c/performance.json").write_text("broken")
            self.assertEqual("PASS", reporter.qualification(root, RUN, START)["sample_evidence_status"])
            metadata.write_text(json.dumps(dict(run_id=RUN, git_sha="0" * 40, started_at=START, qualification_profile=evidence.PROFILE_NAME, profile_version=evidence.PROFILE_VERSION, profile_definition_sha256=evidence.PROFILE_SHA256, applied_test_harness_limits=evidence.TEST_HARNESS_LIMITS, preflight_status="PASS_WITH_LOAD")))
            with self.assertRaises(ValueError): reporter.qualification(root, RUN, START)
            metadata.write_text(json.dumps(dict(run_id=RUN, git_sha="a" * 40, started_at=START, qualification_profile=evidence.PROFILE_NAME, profile_version=evidence.PROFILE_VERSION, profile_definition_sha256=evidence.PROFILE_SHA256, applied_test_harness_limits=evidence.TEST_HARNESS_LIMITS, preflight_status="PASS_WITH_LOAD")))
            path = folder / "workspace-replace.json"; record = json.loads(path.read_text()); record["data"]["save_replace_1mib"]["p95_ms"] = 999; path.write_text(json.dumps(record))
            with self.assertRaises(ValueError): reporter.qualification(root, RUN, START)

    def test_failed_ntfs_workspace_cannot_be_labeled_pass_by_the_reporter(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); folder = fixtures.fixture(root)
            path = folder / "workspace-replace.json"; value = json.loads(path.read_text()); value["status"] = "FAIL"; path.write_text(json.dumps(value))
            result = reporter.qualification(root, RUN, START, allow_legacy=True)
            self.assertEqual("FAIL", result["nf"]["NF-03"]["status"])
            self.assertEqual("FAIL", result["nf"]["NF-03"]["ntfs_save_replace_status"])


if __name__ == "__main__": unittest.main()
