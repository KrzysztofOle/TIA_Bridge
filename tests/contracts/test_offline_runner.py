"""Runner must fail closed on missing/skipped tests, dependencies or failed tools."""
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("validate_offline", ROOT / "scripts/validate_offline.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class OfflineRunnerTests(unittest.TestCase):
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
