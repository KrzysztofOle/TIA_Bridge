"""QA-01 acceptance: real child processes, isolated projects and hostile scenarios."""
from __future__ import annotations

import contextlib
import copy
import io
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"
sys.path.insert(0, str(SCRIPTS))
from qa_runner import adapters
from qa_runner.cli import main
from qa_runner.diagnostics import Logger, correlate, digest, inventory, redact, verify_manifest, write_json
from qa_runner.evaluator import aggregate, failure, group_failures, test_results
from qa_runner.loader import Invalid, contained, discover, load_project, load_scenario, read_json
from qa_runner.runner import collect, matches, receipt, run


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="qa-acceptance-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.config = self.root / "qa.project.json"
        self.scenario = self.root / "scenarios/smoke.json"
        self.scenario.parent.mkdir()
        (self.root / "fixture.py").write_text("import sys,time\nprint('fixture-started',flush=True)\nif 'wait' in sys.argv: time.sleep(30)\nif 'exit' in sys.argv: sys.exit(7)\n", encoding="utf-8")
        self.project = {"schema_version": "1.0", "project_id": "independent", "default_profile": "fast", "scenario_root": "scenarios", "results_root": "results",
                        "runtimes": {"python": {"candidates": ["{python}"], "modules": []}},
                        "tasks": {"smoke": {"type": "python_script", "runtime": "python", "offline": True, "timeout_seconds": 30, "path": "fixture.py"}},
                        "profiles": {"fast": ["smoke.json"]}}
        self.value = {"schema_version": "1.0", "id": "smoke", "description": "Synthetic independent smoke", "tags": ["offline"], "mode": "offline", "timeout_seconds": 30,
                      "requires": [], "on_failure": "stop", "steps": [{"id": "one", "type": "python_script", "task": "smoke", "timeout_seconds": 30, "required": True}]}
        self.save()

    def save(self):
        self.config.write_text(json.dumps(self.project), encoding="utf-8")
        self.scenario.write_text(json.dumps(self.value), encoding="utf-8")

    def execute(self, **kwargs):
        self.save()
        with contextlib.redirect_stdout(io.StringIO()):
            return run(self.root, load_project(self.config)[1], self.config, "fast", **kwargs)

    def folder(self, result):
        return self.root / "results" / result["run_id"]

    def rejected(self):
        self.save()
        with self.assertRaises(Invalid):
            load_scenario(self.scenario, self.root, self.project)

    def test_one_command_fast_and_complete_report(self):
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(main(["--config", str(self.config), "run", "fast"]), 0)
        folder = next((self.root / "results").glob("*/summary.json")).parent
        self.assertIn("Next action", (folder / "report.md").read_text())
        self.assertEqual(read_json(folder / "summary.json")["status"], "PASS")

    def test_new_ids_and_no_report_overwrite(self):
        first = self.execute()
        original = (self.folder(first) / "summary.json").read_bytes()
        second = self.execute()
        self.assertNotEqual(first["run_id"], second["run_id"])
        self.assertRegex(first["run_id"], r"^[0-9TZ]+-[0-9a-f]{32}$")
        self.assertEqual(original, (self.folder(first) / "summary.json").read_bytes())
        self.assertEqual(second["comparison"]["previous_run_id"], first["run_id"])

    def test_manifest_complete_and_tampering_rejected(self):
        result = self.execute()
        folder = self.folder(result)
        self.assertEqual(verify_manifest(folder)["files"], inventory(folder))
        (folder / "report.md").write_text("tamper")
        with self.assertRaises(Invalid):
            verify_manifest(folder)

    def test_jsonl_utc_and_correlation_fields(self):
        result = self.execute()
        events = [json.loads(line) for line in (self.folder(result) / "logs/runner.jsonl").read_text().splitlines()]
        self.assertEqual(events[0]["event_id"], "QA_RUN_STARTED")
        for event in events:
            self.assertTrue(event["timestamp_utc"].endswith("+00:00"))
            self.assertEqual(event["run_id"], result["run_id"])
            self.assertIn("worker_epoch", event)

    def test_nonzero_exit_classified(self):
        self.project["tasks"]["smoke"]["args"] = ["exit"]
        result = self.execute()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["failures"][0]["category"], "PROCESS_FAILURE")

    def test_timeout_and_partial_report_remaining_not_run(self):
        self.project["tasks"]["smoke"]["args"] = ["wait"]
        self.value["steps"][0]["timeout_seconds"] = .2
        self.value["steps"].append({**self.value["steps"][0], "id": "two"})
        result = self.execute()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["steps"][1]["status"], "NOT_RUN")
        self.assertEqual(result["failures"][0]["category"], "TEST_TIMEOUT")
        verify_manifest(self.folder(result))

    def test_missing_runtime_blocked(self):
        self.project["runtimes"]["python"]["candidates"] = ["qa-nonexistent-runtime-01"]
        result = self.execute()
        self.assertEqual(result["status"], "BLOCKED")
        self.assertEqual(result["failures"][0]["category"], "ENVIRONMENT")

    def test_user_interruption_preserves_partial_reports(self):
        self.project["tasks"]["smoke"]["args"] = ["wait"]
        event = threading.Event()
        timer = threading.Timer(.5, event.set)
        timer.start()
        try:
            result = self.execute(cancelled=event)
        finally:
            timer.cancel()
        self.assertEqual(result["status"], "BLOCKED")
        verify_manifest(self.folder(result))
        self.assertIn("User interrupted", (self.folder(result) / "report.md").read_text())

    def test_real_cli_interrupt_signal(self):
        self.project["tasks"]["smoke"]["args"] = ["wait"]
        self.save()
        # raise_signal delivers the same Python SIGINT handler on Windows and POSIX.
        wrapper = ("import signal,sys,threading; "
                   "sys.path.insert(0,sys.argv[1]); from qa_runner.cli import main; "
                   "timer=threading.Timer(1,lambda:signal.raise_signal(signal.SIGINT)); timer.start(); "
                   "sys.exit(main(['--config',sys.argv[2],'run','fast']))")
        child = subprocess.run([sys.executable, '-B', '-c', wrapper, str(SCRIPTS), str(self.config)],
                               capture_output=True, text=True, timeout=25)
        self.assertEqual(child.returncode, 2, child.stdout + child.stderr)
        folder = next((self.root / 'results').glob('*/summary.json')).parent
        self.assertEqual(read_json(folder / 'summary.json')['status'], 'BLOCKED')
        verify_manifest(folder)

    def test_invalid_scenario_creates_blocked_report_without_execution(self):
        self.value["steps"][0]["command"] = "forbidden"
        result = self.execute()
        self.assertEqual(result["status"], "BLOCKED")
        self.assertFalse(list(self.folder(result).glob("logs/smoke--*.log")))

    def test_unknown_type_denied(self):
        self.value["steps"][0]["type"] = "shell"
        self.rejected()

    def test_unapproved_task_denied(self):
        self.value["steps"][0]["task"] = "evil.exe"
        self.rejected()

    def test_untrusted_scenario_cannot_override_argv(self):
        self.value["steps"][0]["args"] = ["-c", "raise RuntimeError('EXECUTED')"]
        self.rejected()

    def test_scenario_cannot_change_success_exit_code(self):
        self.value["steps"][0]["expected_exit_code"] = 7
        self.rejected()

    def test_live_mode_and_attach_denied(self):
        self.value["mode"] = "live"
        self.rejected()
        self.value["mode"] = "offline"
        self.value["steps"][0]["type"] = "siemens_attach"
        self.rejected()

    def test_live_task_config_denied(self):
        self.project["tasks"]["smoke"]["offline"] = False
        self.save()
        with self.assertRaises(Invalid):
            load_project(self.config)

    def test_duplicate_ids_and_wrong_schema_denied(self):
        self.value["steps"].append(copy.deepcopy(self.value["steps"][0]))
        self.rejected()
        self.value["steps"].pop()
        self.value["schema_version"] = "2.0"
        self.rejected()

    def test_json_duplicate_keys_depth_size_nan_denied(self):
        for text in ['{"a":1,"a":2}', '[' * 25 + '0' + ']' * 25, '{"x":NaN}', 'x' * (1024 * 1024 + 1)]:
            self.scenario.write_text(text)
            with self.assertRaises(Invalid):
                read_json(self.scenario)

    def test_path_traversal_absolute_ads_denied(self):
        for value in ['../escape', '/tmp/escape', 'C:/escape', 'test:ads', 'dir\\escape']:
            with self.assertRaises(Invalid):
                contained(self.root, value)

    def test_symlink_escape_denied_when_supported(self):
        with tempfile.TemporaryDirectory() as outside:
            try:
                (self.root / "escape").symlink_to(outside, target_is_directory=True)
            except OSError:
                # Creating symlinks may require rights. Junction containment is the same resolve check.
                return
            with self.assertRaises(Invalid):
                contained(self.root, 'escape/file')

    def test_timeout_limits_and_boolean_validation(self):
        for field, value in [('timeout_seconds', 90000), ('timeout_seconds', True), ('required', 'yes')]:
            before = self.value['steps'][0][field]
            self.value['steps'][0][field] = value
            self.rejected()
            self.value['steps'][0][field] = before

    def test_missing_required_step_never_pass(self):
        for status in ['NOT_RUN', 'SKIPPED', 'BLOCKED']:
            self.assertEqual(aggregate([{'required': True, 'status': status}]), 'BLOCKED')

    def test_failure_groups_keep_first_stack(self):
        item = failure('TEST_ASSERTION', 'same', 'Example.Test', 'first stack')
        groups = group_failures([{'scenario_id': 's', 'step_id': 'one', 'failures': [item, item]}])
        self.assertEqual(len(groups), 1)
        self.assertEqual(groups[0]['occurrences'], 2)
        self.assertEqual(groups[0]['stack_trace'], 'first stack')

    def test_python_adapter_actual_assertion_and_source(self):
        tests = self.root / 'tests'; tests.mkdir()
        (tests / 'test_example.py').write_text('import unittest\nclass Example(unittest.TestCase):\n def test_fail(self): self.assertEqual(1,2)\n')
        self.project['tasks']['smoke'].update(type='python_test', path='tests')
        self.value['steps'][0]['type'] = 'python_test'
        result = self.execute()
        self.assertEqual(result['tests']['failed'], 1)
        self.assertEqual(result['failures'][0]['category'], 'TEST_ASSERTION')
        self.assertIn('test_example.py:', result['failures'][0]['source_location'])

    def test_python_zero_tests_blocked(self):
        (self.root / 'tests').mkdir()
        self.project['tasks']['smoke'].update(type='python_test', path='tests')
        self.value['steps'][0]['type'] = 'python_test'
        self.assertEqual(self.execute()['status'], 'BLOCKED')

    def test_dotnet_trx_failed_and_missing_results(self):
        output = self.root / 'output'; output.mkdir()
        task = {'type': 'dotnet_test'}
        with self.assertRaises(Invalid):
            test_results(task, output)
        (output / 'test.trx').write_text('<TestRun><Results><UnitTestResult testName="Core.Fails" outcome="Failed"><Output><ErrorInfo><Message>expected 1</Message><StackTrace>in code.cs:line 42</StackTrace></ErrorInfo></Output></UnitTestResult></Results><ResultSummary outcome="Failed"><Counters total="1"/></ResultSummary></TestRun>')
        counts, errors = test_results(task, output)
        self.assertEqual(counts['failed'], 1)
        self.assertEqual(errors[0]['source_location'], 'code.cs:42')
        (output / 'test.trx').write_text('<!DOCTYPE x [<!ENTITY a "bad">]><TestRun/>')
        with self.assertRaises(Invalid):
            test_results(task, output)

    def test_report_selected_run_and_failures_only(self):
        first = self.execute()
        self.execute()
        with contextlib.redirect_stdout(io.StringIO()) as stream:
            code = main(['--config', str(self.config), 'report', '--run-id', first['run_id'], '--failures-only'])
        self.assertEqual(code, 0)
        self.assertIn(first['run_id'], stream.getvalue())
        self.assertNotIn('smoke/one', stream.getvalue())

    def test_previous_comparison_resolved_failure(self):
        self.project['tasks']['smoke']['args'] = ['exit']
        first = self.execute()
        self.project['tasks']['smoke']['args'] = []
        second = self.execute()
        self.assertEqual(second['comparison']['previous_run_id'], first['run_id'])
        self.assertTrue(second['comparison']['resolved_failures'])

    def test_cross_project_copy_no_tia_or_repo_dependencies(self):
        tool = self.root / 'tools'; tool.mkdir()
        shutil.copyfile(SCRIPTS / 'qa.py', tool / 'qa.py')
        shutil.copytree(SCRIPTS / 'qa_runner', tool / 'qa_runner', ignore=shutil.ignore_patterns('__pycache__'))
        result = subprocess.run([sys.executable, '-B', str(tool / 'qa.py'), 'run', 'fast'], cwd=self.root, capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('PROJECT: independent', result.stdout)

    def test_doctor_list_and_config_discovery(self):
        nested = self.root / 'nested'; nested.mkdir()
        self.assertEqual(discover(nested), self.config)
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(main(['--config', str(self.config), 'doctor']), 0)
            self.assertEqual(main(['--config', str(self.config), 'list']), 0)

    def test_redaction_and_existing_jsonl_correlation(self):
        self.assertNotIn('abc123', redact('token=abc123 password=abc123 Bearer abc123'))
        source = self.root / 'application.jsonl'
        source.write_text(json.dumps({'phase': 'worker.idle', 'operation_id': 'op-01', 'worker_epoch': 'epoch-01', 'worker_pid': 123}) + '\n')
        logger = Logger(self.root / 'runner.jsonl', 'run')
        correlate(source, logger, 'scenario', 'step')
        logger.close()
        event = json.loads((self.root / 'runner.jsonl').read_text())
        self.assertEqual(event['operation_id'], 'op-01')
        self.assertEqual(event['worker_epoch'], 'epoch-01')

    def test_lock_prevents_concurrent_run_and_is_not_removed(self):
        (self.root / 'results').mkdir()
        lock = self.root / 'results/.runner.lock'; lock.write_text('owner')
        with self.assertRaises(FileExistsError):
            self.execute()
        self.assertEqual(lock.read_text(), 'owner')

    def test_receipt_blocked_qualification_not_functional_pass(self):
        script = self.root / 'fixture.py'
        script.write_text("import json,pathlib\np=pathlib.Path('receipts');p.mkdir(exist_ok=True)\n(p/'new.json').write_text(json.dumps({'status':'PASS','qualification':'BLOCKED','functional':'PASS'}))\n")
        self.project['tasks']['smoke']['receipt'] = {'pattern': 'receipts/*.json', 'status_field': 'status', 'layers': {'environment_qualification': 'qualification', 'functional': 'functional'}}
        result = self.execute()
        self.assertEqual(result['status'], 'BLOCKED')
        self.assertEqual(result['layers']['functional'], 'PASS')
        self.assertEqual(result['layers']['engineering'], 'NOT_RUN')

    def test_stale_receipt_not_adopted(self):
        directory = self.root / 'receipts'; directory.mkdir()
        (directory / 'old.json').write_text('{"status":"PASS"}')
        self.project['tasks']['smoke']['receipt'] = {'pattern': 'receipts/*.json', 'status_field': 'status', 'layers': {}}
        self.assertEqual(self.execute()['status'], 'BLOCKED')

    def test_trusted_task_only_builds_fixed_argv(self):
        task = {'type': 'dotnet_test', 'project': 'tests/Example.csproj', 'filter': 'FullyQualifiedName~Example'}
        argv = adapters.command(task, 'dotnet', self.root, self.root / 'output', None)
        self.assertIn('--no-build', argv)
        self.assertNotIn('shell', argv)
        self.assertIn('FullyQualifiedName~Example', argv)

    def test_continue_retains_failure_and_executes_next_step(self):
        self.value['on_failure'] = 'continue'
        self.project['tasks']['okay'] = copy.deepcopy(self.project['tasks']['smoke'])
        self.project['tasks']['smoke']['args'] = ['exit']
        self.value['steps'].append({**self.value['steps'][0], 'id': 'two', 'task': 'okay'})
        result = self.execute()
        self.assertEqual(result['status'], 'FAIL')
        self.assertEqual(result['steps'][1]['status'], 'PASS')

    def test_sealed_receipt_bundle_copied_and_verified(self):
        bundle = self.root / 'bundles/run-01'; (bundle / 'validation').mkdir(parents=True)
        write_json(bundle / 'validation/run.json', {'run_id': 'run-01', 'status': 'PASS'})
        write_json(bundle / 'evidence-manifest.json', {'run_id': 'run-01', 'files': inventory(bundle)})
        output = self.root / 'output'; output.mkdir()
        task = {'receipt': {'pattern': 'bundles/*/validation/run.json', 'status_field': 'status', 'layers': {}, 'bundle': True}}
        status, _, _, _ = receipt(self.root, task, set(), output)
        self.assertEqual(status, 'PASS')
        verify_manifest(output / 'receipt-bundle')
        (bundle / 'validation/run.json').write_text('{}')
        with self.assertRaises(Invalid):
            receipt(self.root, task, set(), self.root / 'different-output')

    def test_receipt_required_not_run_layer_blocks(self):
        (self.root / 'fixture.py').write_text("import pathlib\np=pathlib.Path('receipts');p.mkdir();(p/'new.json').write_text('{\"status\":\"PASS\",\"qualification\":\"NOT_RUN\"}')\n")
        self.project['tasks']['smoke']['receipt'] = {'pattern': 'receipts/*.json', 'status_field': 'status', 'layers': {'environment_qualification': 'qualification'}}
        self.assertEqual(self.execute()['status'], 'BLOCKED')

    def test_preflight_blocked_receipt_keeps_explicit_reason(self):
        (self.root / 'fixture.py').write_text("import pathlib\np=pathlib.Path('receipts');p.mkdir();(p/'new.json').write_text('{\"status\":\"BLOCKED\",\"preflight_error\":\"NTFS required\"}')\n")
        self.project['tasks']['smoke']['receipt'] = {'pattern': 'receipts/*.json', 'status_field': 'status', 'layers': {'functional': 'execution'}, 'counts': {'tests': 'tests'}}
        result = self.execute()
        self.assertEqual(result['status'], 'BLOCKED')
        self.assertIn('NTFS required', result['failures'][0]['message'])
        self.assertEqual(result['tests']['passed'], 0)
        self.assertEqual(result['layers']['functional'], 'NOT_RUN')

    def test_receipt_diagnostic_blockers_are_readable_without_log(self):
        folder = self.root / 'receipts'; folder.mkdir()
        write_json(folder / 'new.json', {'status': 'BLOCKED'})
        write_json(folder / 'details.json', {'blockers': ['NF-11 NOT_RUN']})
        task = {'receipt': {'pattern': 'receipts/new.json', 'status_field': 'status', 'layers': {}, 'diagnostics': {'path': 'details.json', 'fields': ['blockers']}}}
        output = self.root / 'output'; output.mkdir()
        self.assertIn('NF-11 NOT_RUN', receipt(self.root, task, set(), output)[3])

    def test_collector_only_new_approved_logs_and_source_timestamp(self):
        logs = self.root / 'app'; logs.mkdir()
        (logs / 'old.jsonl').write_text('{}\n')
        before = matches(self.root, 'app/*.jsonl')
        (logs / 'new.jsonl').write_text('{"utc":"2026-01-01T00:00:00Z","operation_id":"op"}\n')
        output = self.root / 'output'; output.mkdir()
        logger = Logger(self.root / 'correlated.jsonl', 'run')
        collect(self.root, ['app/*.jsonl'], before, output, logger, 's', 'one')
        logger.close()
        self.assertTrue((output / 'collected/app/new.jsonl').exists())
        self.assertFalse((output / 'collected/app/old.jsonl').exists())
        self.assertEqual(json.loads((self.root / 'correlated.jsonl').read_text())['source_timestamp_utc'], '2026-01-01T00:00:00Z')

    def test_child_log_redacts_common_credentials(self):
        (self.root / 'fixture.py').write_text("print('password=top-secret token=top-secret')\n")
        result = self.execute()
        text = next((self.folder(result) / 'logs').glob('*.log')).read_text()
        self.assertNotIn('top-secret', text)
        self.assertIn('[REDACTED]', text)

    def test_runtime_candidate_fallback(self):
        path = adapters.runtime({'candidates': ['qa-missing-runtime-99', '{python}'], 'modules': []})
        self.assertEqual(Path(path), Path(sys.executable).resolve())

    def test_contradictory_and_aborted_trx_cannot_pass(self):
        output = self.root / 'output'; output.mkdir()
        for status, counters in [('Failed', 'total="1" passed="1" failed="0"'), ('Completed', 'total="1" passed="0" failed="1"'), ('Aborted', 'total="1"')]:
            (output / 'test.trx').write_text(f'<TestRun><Results><UnitTestResult outcome="Passed"/></Results><ResultSummary outcome="{status}"><Counters {counters}/></ResultSummary></TestRun>')
            with self.assertRaises(Invalid):
                test_results({'type': 'dotnet_test'}, output)

    def test_project_environment_policy_without_vendor_dependency(self):
        self.project['environment'] = {'clear_prefixes': ['QA_SAMPLE_'], 'values': {'QA_SAMPLE_APPROVED': 'yes'}}
        (self.root / 'fixture.py').write_text("import os\nassert 'QA_SAMPLE_LIVE' not in os.environ\nassert os.environ['QA_SAMPLE_APPROVED']=='yes'\n")
        with patch.dict(os.environ, {'QA_SAMPLE_LIVE': 'inherited'}):
            self.assertEqual(self.execute()['status'], 'PASS')

    def test_tia_integration_prepares_fresh_layout_without_overwrite(self):
        from qa_prepare_tia_bridge import prepare
        paths = prepare(self.root)
        sentinel = self.root / paths[0] / 'sentinel.txt'
        sentinel.write_text('keep')
        self.assertEqual(prepare(self.root), paths)
        self.assertEqual(sentinel.read_text(), 'keep')
        self.assertTrue((self.root / paths[1]).is_dir())

    def test_tia_integration_layout_collision_fails_closed(self):
        from qa_prepare_tia_bridge import prepare
        (self.root / '.tia-bridge').write_text('existing file')
        with self.assertRaises(OSError):
            prepare(self.root)
        self.assertEqual((self.root / '.tia-bridge').read_text(), 'existing file')

    def test_reserved_scenario_names_denied(self):
        for name in ['NUL', 'con', 'AUX.json', 'COM1', 'LPT9']:
            self.value['id'] = name
            self.rejected()

    def test_scenario_snapshot_cannot_overwrite_project_config(self):
        self.value['id'] = 'qa.project'
        result = self.execute()
        folder = self.folder(result)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(digest(folder / 'artifacts/qa.project.json'), result['config_sha256'])
        self.assertEqual(read_json(folder / 'artifacts/scenarios/qa.project.json')['id'], 'qa.project')

    def test_step_case_aliases_denied_before_execution(self):
        self.value['steps'].append({**self.value['steps'][0], 'id': 'ONE'})
        self.rejected()

    def test_report_rejects_manifest_run_id_mismatch(self):
        result = self.execute()
        path = self.folder(result) / 'evidence-manifest.json'
        value = read_json(path); value['run_id'] = 'different-run'
        path.write_text(json.dumps(value))
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(main(['--config', str(self.config), 'report', '--run-id', result['run_id']]), 2)


if __name__ == '__main__':
    unittest.main()
