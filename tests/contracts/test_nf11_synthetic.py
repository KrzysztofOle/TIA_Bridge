"""DIAGNOSTIC validation of NF-11 evidence; no Siemens calls or soak execution."""
import hashlib
import importlib.util
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
import tempfile
import unittest
import shutil

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("nf11_report_resilience", ROOT / "scripts/report_resilience.py")
REPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(REPORTER)
RUNNER_SPEC = importlib.util.spec_from_file_location("validate_offline_nf11", ROOT / "scripts/validate_offline.py")
RUNNER = importlib.util.module_from_spec(RUNNER_SPEC)
RUNNER_SPEC.loader.exec_module(RUNNER)
RUN = "a" * 32
START = datetime(2026, 10, 28, tzinfo=timezone.utc)


def _write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, separators=(",", ":")) + "\n", encoding="utf-8")


def _seal(folder, run_id):
    files = {}
    for name in ["nf11-synthetic-samples.jsonl", "nf11-synthetic-operations.jsonl", "nf11-synthetic-result.json"]:
        path = folder / name
        payload = path.read_bytes()
        files[name] = {"bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest()}
    _write_json(folder / "nf11-synthetic-manifest.json", {"schema_version": "1.0", "run_id": run_id, "files": files})


def _metadata(root):
    validation = root / ".tia-bridge/resilience" / RUN / "validation"
    git_sha = "b" * 40
    source_sha = "c" * 64
    _write_json(validation / "run-metadata.json", {"run_id": RUN, "git_sha": git_sha, "source_fingerprint_lf_sha256": source_sha})
    _write_json(validation / "build-metadata.json", {"run_id": RUN, "git_sha": git_sha, "source_fingerprint_lf_sha256": source_sha,
        "binaries": {"host_tests": {"path": "tests/integration/TiaBridge.Host.Tests/bin/Release/net10.0/TiaBridge.Host.Tests.dll", "bytes": 10, "sha256": "d" * 64}}})


def _fixture(root, *, mode="DIAGNOSTIC", execution="DIAGNOSTIC", seconds=5, operation_count=5,
             sample_count=1, ram_growth=0, handle_growth=0, missing_operation=False, cleanup=True):
    (root / "tests/resilience").mkdir(parents=True, exist_ok=True)
    shutil.copyfile(ROOT / "tests/resilience/matrix.json", root / "tests/resilience/matrix.json")
    folder = root / ".tia-bridge/resilience" / RUN
    folder.mkdir(parents=True)
    sample_lines = []
    for index in range(sample_count):
        elapsed = seconds if sample_count == 1 else index + 1
        sample_lines.append(json.dumps({"sequence": index, "value": {"elapsed_seconds": elapsed,
            "host": {"working_set_bytes": 100 * 1048576 + int(ram_growth * 1048576), "handles": 100 + int(handle_growth * 100), "threads": 8, "cpu_ms": index * 1000},
            "process_counts": {"machine": 100, "tracked_owned_total": 2}}}, separators=(",", ":")))
    (folder / "nf11-synthetic-samples.jsonl").write_text("\n".join(sample_lines) + ("\n" if sample_lines else ""), encoding="utf-8")
    events = []
    ids = [f"{index + 1:032x}" for index in range(operation_count)]
    for operation_id in ids:
        events.append({"kind": "accepted", "operation_id": operation_id, "at_utc": START.isoformat()})
        if not (missing_operation and operation_id == ids[-1]):
            events.append({"kind": "completed", "operation_id": operation_id, "state": "SUCCEEDED", "at_utc": START.isoformat()})
    (folder / "nf11-synthetic-operations.jsonl").write_text("".join(json.dumps(event, separators=(",", ":")) + "\n" for event in events), encoding="utf-8")
    ended = START + timedelta(seconds=seconds)
    lost = [ids[-1]] if missing_operation else []
    result = {"schema_version": "1.0", "run_id": RUN, "phase": "NF11_SYNTHETIC", "mode": mode,
        "execution_status": execution, "started_at": START.isoformat(), "measurement_started_at": START.isoformat(), "ended_at": ended.isoformat(),
        "measured_seconds": seconds, "required_seconds": 28800, "minimum_operations": 1000,
        "warmup_operations": 50, "accepted_operations": operation_count, "completed_operations": operation_count - len(lost),
        "lost_operation_ids": lost, "warmup_host_working_set_bytes": 100 * 1048576, "warmup_host_handles": 100,
        "ram_growth_mib": ram_growth, "handle_growth_fraction": handle_growth, "samples_count": sample_count,
        "sampling_interval_seconds": 1, "cleanup_confirmed": cleanup, "unmanaged_child_processes": 0 if cleanup else 1,
        "thresholds": {"maximum_ram_growth_mib": 50, "maximum_handle_growth_fraction": .05}, "diagnostic_must_not_qualify": mode == "DIAGNOSTIC"}
    _write_json(folder / "nf11-synthetic-result.json", result)
    _seal(folder, RUN)
    _metadata(root)
    return folder


class NF11DiagnosticReportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.valid_root = tempfile.TemporaryDirectory(prefix="nf11-valid-diagnostic-")
        root = Path(cls.valid_root.name)
        (root / "tests/resilience").mkdir(parents=True)
        shutil.copyfile(ROOT / "tests/resilience/matrix.json", root / "tests/resilience/matrix.json")
        folder = root / ".tia-bridge/resilience" / RUN
        folder.mkdir(parents=True)
        sample_path = folder / "nf11-synthetic-samples.jsonl"
        with sample_path.open("x", encoding="utf-8") as stream:
            for sequence in range(1, 28_801):
                row = {"sequence": sequence - 1, "value": {"elapsed_seconds": sequence,
                    "host": {"working_set_bytes": 100 * 1048576, "handles": 100, "threads": 8, "cpu_ms": sequence * 1000},
                    "process_counts": {"machine": 100, "tracked_owned_total": 2}}}
                stream.write(json.dumps(row, separators=(",", ":")) + "\n")
        events_path = folder / "nf11-synthetic-operations.jsonl"
        with events_path.open("x", encoding="utf-8") as stream:
            for index in range(1000):
                operation_id = f"{index + 1:032x}"
                stream.write(json.dumps({"kind": "accepted", "operation_id": operation_id, "at_utc": START.isoformat()}, separators=(",", ":")) + "\n")
                stream.write(json.dumps({"kind": "completed", "operation_id": operation_id, "state": "SUCCEEDED", "at_utc": START.isoformat()}, separators=(",", ":")) + "\n")
        _write_json(folder / "nf11-synthetic-result.json", {"schema_version": "1.0", "run_id": RUN, "phase": "NF11_SYNTHETIC", "mode": "FULL",
            "execution_status": "COMPLETE", "started_at": START.isoformat(), "measurement_started_at": START.isoformat(),
            "ended_at": (START + timedelta(seconds=28800)).isoformat(), "measured_seconds": 28800,
            "required_seconds": 28800, "minimum_operations": 1000, "warmup_operations": 50,
            "accepted_operations": 1000, "completed_operations": 1000, "lost_operation_ids": [],
            "warmup_host_working_set_bytes": 100 * 1048576, "warmup_host_handles": 100,
            "ram_growth_mib": 0, "handle_growth_fraction": 0, "samples_count": 28800,
            "sampling_interval_seconds": 1, "cleanup_confirmed": True, "unmanaged_child_processes": 0,
            "thresholds": {"maximum_ram_growth_mib": 50, "maximum_handle_growth_fraction": .05}, "diagnostic_must_not_qualify": False})
        _seal(folder, RUN)
        _metadata(root)

    @classmethod
    def tearDownClass(cls):
        cls.valid_root.cleanup()

    def test_DIAGNOSTIC_complete_synthetic_evidence_passes_phase_but_not_overall(self):
        report = REPORTER.nf11_synthetic_report(Path(self.valid_root.name), RUN)
        self.assertEqual("PASS", report["NF11_SYNTHETIC"])
        self.assertEqual("NOT_RUN", report["NF11_LIVE"])
        self.assertEqual("BLOCKED", report["NF11_OVERALL"])
        self.assertEqual(28800, report["sample_count"])

    def test_DIAGNOSTIC_short_full_window_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            _fixture(Path(directory), mode="FULL", execution="COMPLETE", seconds=28799, operation_count=1000, sample_count=1)
            report = REPORTER.nf11_synthetic_report(Path(directory), RUN)
            self.assertEqual("FAIL", report["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_too_few_operations_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            _fixture(Path(directory), mode="FULL", execution="COMPLETE", seconds=28800, operation_count=999, sample_count=1)
            report = REPORTER.nf11_synthetic_report(Path(directory), RUN)
            self.assertEqual("FAIL", report["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_RAM_limit_exceeded_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            _fixture(Path(directory), ram_growth=51)
            self.assertEqual("FAIL", REPORTER.nf11_synthetic_report(Path(directory), RUN)["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_handle_limit_exceeded_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            _fixture(Path(directory), handle_growth=.06)
            self.assertEqual("FAIL", REPORTER.nf11_synthetic_report(Path(directory), RUN)["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_lost_operation_id_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            _fixture(Path(directory), missing_operation=True)
            self.assertEqual("FAIL", REPORTER.nf11_synthetic_report(Path(directory), RUN)["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_unmanaged_child_cleanup_failure_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            _fixture(Path(directory), cleanup=False)
            self.assertEqual("FAIL", REPORTER.nf11_synthetic_report(Path(directory), RUN)["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_interruption_is_blocked(self):
        with tempfile.TemporaryDirectory() as directory:
            _fixture(Path(directory), mode="FULL", execution="INTERRUPTED", seconds=10)
            self.assertEqual("BLOCKED", REPORTER.nf11_synthetic_report(Path(directory), RUN)["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_missing_sample_is_blocked(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = _fixture(Path(directory), sample_count=0)
            report = REPORTER.nf11_synthetic_report(Path(directory), RUN)
            self.assertEqual("BLOCKED", report["NF11_SYNTHETIC"])
            self.assertTrue(any("sample" in item.lower() for item in report["blockers"]))

    def test_DIAGNOSTIC_corrupt_manifest_is_blocked(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = _fixture(Path(directory))
            manifest_path = folder / "nf11-synthetic-manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            manifest["files"]["nf11-synthetic-samples.jsonl"]["sha256"] = "0" * 64
            _write_json(manifest_path, manifest)
            self.assertEqual("BLOCKED", REPORTER.nf11_synthetic_report(Path(directory), RUN)["NF11_SYNTHETIC"])

    def test_DIAGNOSTIC_missing_live_phase_never_passes_overall(self):
        report = REPORTER.nf11_synthetic_report(Path(self.valid_root.name), RUN)
        self.assertEqual("BLOCKED", report["NF11_OVERALL"])

    def test_DIAGNOSTIC_overall_pass_requires_both_phases(self):
        self.assertEqual("BLOCKED", REPORTER.nf11_overall("PASS", "NOT_RUN")["NF11_OVERALL"])

    def test_DIAGNOSTIC_interruption_writes_one_way_run_bound_stop_request(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "stop.request"
            RUNNER.request_nf11_stop(path, RUN, "keyboard_interrupt")
            value = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(RUN, value["run_id"])
            self.assertEqual("keyboard_interrupt", value["reason"])
            with self.assertRaises(FileExistsError):
                RUNNER.request_nf11_stop(path, RUN, "runner_cleanup")


if __name__ == "__main__":
    unittest.main()
