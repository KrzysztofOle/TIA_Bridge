"""Runner must fail closed on missing/skipped tests, dependencies or failed tools."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import shutil
import unittest
from unittest.mock import patch
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("validate_offline", ROOT / "scripts/validate_offline.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class OfflineRunnerTests(unittest.TestCase):
    def test_orchestration_keeps_runs_separate_and_seals_failed_results_without_executing_tests(self):
        # Every execution seam is mocked: this does not launch the full profile, processes or restore/build.
        evidence_spec = importlib.util.spec_from_file_location("performance_evidence", ROOT / "scripts/performance_evidence.py")
        evidence = importlib.util.module_from_spec(evidence_spec); evidence_spec.loader.exec_module(evidence)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root / "scripts").mkdir()
            shutil.copyfile(ROOT / "scripts/performance_evidence.py", root / "scripts/performance_evidence.py")
            (root / "tests/resilience").mkdir(parents=True)
            shutil.copyfile(ROOT / "tests/resilience/performance-profiles.json", root / "tests/resilience/performance-profiles.json")
            shared = root / ".tia-bridge/resilience/run.json"; shared.parent.mkdir(parents=True); shared.write_bytes(b"historical unchanged")
            fake_reporter = SimpleNamespace(performance_evidence=evidence, capture_environment=lambda *args: None,
                qualification=lambda *args: dict(gate="FAIL", x01_readiness="NOT_READY", gate_blockers=[], limited_x01_technical_readiness="NOT_READY", nf={f"NF-{n:02d}": {"status": "PASS"} for n in [1, 2, 3, 5, 8]}))
            def fake_step(name, command, steps, **kwargs):
                steps.append(dict(name=name, exit_code=0, elapsed_s=0))
                return SimpleNamespace(returncode=0, stdout="Ran 1 tests\nOK\n")
            first_output = None
            with patch.object(runner, "ROOT", root), patch.object(runner, "OUTPUT", root / "unused"), patch.object(runner, "load_reporter", return_value=fake_reporter), patch.object(runner, "check_core_boundary", return_value={"status": "PASS"}), patch.object(runner, "check_trx", return_value=dict(passed=1, failed=0, failed_tests=[])), patch.object(runner, "run_preflight", return_value={"status": "PASS_WITH_LOAD", "load_policy": {}}), patch.object(runner, "run_step", side_effect=fake_step), patch.object(runner.subprocess, "check_output", return_value="a" * 40), patch("builtins.print"), patch.dict(os.environ):
                for _ in range(2):
                    self.assertEqual(1, runner.main())
                    result = json.loads((runner.OUTPUT / "run.json").read_text())
                    self.assertEqual("FAIL", result["status"])
                    evidence.verify_run(runner.OUTPUT.parent, result["run_id"])
                    self.assertEqual(b"historical unchanged", shared.read_bytes())
                    if first_output is None: first_output = runner.OUTPUT
                    else: self.assertNotEqual(first_output, runner.OUTPUT)
                self.assertTrue((first_output / "run.json").is_file())

    def test_core_static_dependency_boundary(self):
        self.assertEqual("PASS", runner.check_core_boundary()["status"])

    def test_trx_gate_rejects_empty_skipped_failed_and_missing_determinism(self):
        prefix = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
        suffix = '</Results></TestRun>'
        with tempfile.TemporaryDirectory(prefix="tia-bridge2-runner-") as directory:
            path = Path(directory) / "synthetic.trx"
            for results in ['', '<UnitTestResult outcome="NotExecuted" testName="skipped"/>', '<UnitTestResult outcome="Failed" testName="failed"/>', '<UnitTestResult outcome="Passed" testName="ordinary"/>']:
                path.write_text(prefix + results + suffix, encoding="utf-8")
                with self.assertRaises(ValueError):
                    runner.check_trx(path)
            results = ''.join(f'<UnitTestResult outcome="Passed" testName="Golden_models_paths_manifests_and_capabilities({n})"/>' for n in range(100))
            path.write_text(prefix + results + suffix, encoding="utf-8")
            with self.assertRaises(ValueError):
                runner.check_trx(path)
            for name, count in {"Shared_shape_mutations_have_the_same_verdict": 19, "Every_required_parameter_result_and_nested_member": 19, "Method_specific_errors_round_trip": 19, "Every_lifecycle_edge_obeys": 147, "Every_method_has_valid_typed_request_and_response": 19, "Core_transitive_assembly_dependencies": 1}.items():
                results += ''.join(f'<UnitTestResult outcome="Passed" testName="{name}({n})"/>' for n in range(count))
            path.write_text(prefix + results + suffix, encoding="utf-8")
            self.assertEqual(100, runner.check_trx(path)["deterministic_cases"])
            with self.assertRaises(ValueError):
                runner.check_trx(path, host_required=True)
            with self.assertRaises(ValueError):
                runner.check_trx(path, scheduler_required=True)
            with self.assertRaises(ValueError):
                runner.check_trx(path, api_required=True)
            with self.assertRaises(ValueError):
                runner.check_trx(path, resilience_required=True)
            failing = results.replace('outcome="Passed"', 'outcome="Failed"', 1)
            path.write_text(prefix + failing + suffix, encoding="utf-8")
            with self.assertRaises(ValueError):
                runner.check_trx(path)
            collected = runner.check_trx(path, allow_failures=True)
            self.assertEqual(1, collected["failed"])
            self.assertEqual(1, len(collected["failed_tests"]))

    def test_failed_command_is_never_reported_as_pass(self):
        with tempfile.TemporaryDirectory(prefix="tia-bridge2-runner-") as directory:
            with patch.object(runner, "OUTPUT", Path(directory)), patch.object(runner.subprocess, "run", return_value=SimpleNamespace(returncode=9, stdout="Synthetic failure\n")), patch("builtins.print"):
                steps = []
                with self.assertRaises(ValueError):
                    runner.run_step("synthetic", ["synthetic-no-execution"], steps)
                self.assertEqual(9, steps[0]["exit_code"])

    def test_unicode_console_reporting_works_when_windows_python_starts_in_charmap(self):
        environment = os.environ.copy()
        environment.update(PYTHONIOENCODING="cp1252", PYTHONUTF8="0", PYTHONDONTWRITEBYTECODE="1")
        code = (
            "import sys; sys.path.insert(0, 'scripts'); import validate_offline; "
            "assert sys.stdout.encoding.lower() == 'cp1252'; "
            "validate_offline.configure_console_utf8(); print('Réacteur → żółć')"
        )
        result = subprocess.run([sys.executable, "-c", code], cwd=ROOT, env=environment,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
        self.assertEqual(0, result.returncode, result.stderr.decode("utf-8", errors="strict"))
        self.assertIn("Réacteur → żółć", result.stdout.decode("utf-8", errors="strict"))


if __name__ == "__main__":
    unittest.main()
