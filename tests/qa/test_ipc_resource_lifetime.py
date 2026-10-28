"""Behavioral checks for the B1-J adapter's interpretation of phase evidence."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
from ipc_resource_lifetime import analyze, comparisons


class LifetimeAnalysisTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.samples = [dict(phase=phase, step_id="sample", sequence=i, elapsed_ticks=i, handles=handles,
                             threads=5, scenario_id="S2-1", worker_pid=123, worker_epoch="epoch")
                        for i, (phase, handles) in enumerate([("cold", 10), ("warm", 20), ("idle-enter", 22), ("idle", 24), ("shutdown", 23)])]
        for sample in self.samples:
            self.write(f"types-{sample['sequence']}.json", dict(phase=sample["phase"], sequence=sample["sequence"], ownership={}, snapshot=dict(status="PARTIAL", counts={"Event": 3, "Thread": 2, "UNAVAILABLE": 1}, handles=[])))
        self.write("ended.json", dict(cleanup="DISPOSED", sends=71, errors=0, termination="STOP"))
        self.write("controller-result.json", dict(exit_code=0, dispatches=70, heartbeats=0, epoch="epoch", pid=123))
        self.write("spec.json", dict(heartbeat=False))
        self.metrics()

    def write(self, name, value):
        (self.root / name).write_text(json.dumps(value), encoding="utf-8")

    def metrics(self):
        (self.root / "metrics.jsonl").write_text("\n".join(json.dumps(s) for s in self.samples), encoding="utf-8")

    def test_completed_execution_is_not_a_stability_pass(self):
        result = analyze(self.root)
        self.assertEqual(result["functional"], "PASS")
        self.assertEqual(result["resource"], "REVIEW_REQUIRED")
        self.assertEqual(result["warm_idle_delta"], 4)  # cold and shutdown excluded
        self.assertEqual(result["gc_delta"], "NOT_AVAILABLE")
        self.assertEqual(result["diagnostic_completeness"], "PARTIAL")

    def test_missing_shutdown_is_not_success(self):
        self.samples.pop(); self.metrics()
        with self.assertRaisesRegex(ValueError, "phase evidence"):
            analyze(self.root)

    def test_incomplete_message_delivery_is_rejected(self):
        self.write("ended.json", dict(cleanup="DISPOSED", sends=70, errors=0, termination="STOP"))
        with self.assertRaisesRegex(ValueError, "Completed sends"):
            analyze(self.root)

    def test_missing_handle_evidence_is_not_complete(self):
        (self.root / "types-1.json").unlink()
        with self.assertRaisesRegex(ValueError, "snapshots"):
            analyze(self.root)

    def test_gc_pair_uses_its_own_two_samples(self):
        for step, count in [("pre-gc", 60), ("post-gc", 30)]:
            self.samples.insert(-1, dict(self.samples[-2], step_id=step, phase="controlled-gc", handles=count))
        self.metrics()
        self.assertEqual(analyze(self.root)["gc_delta"], -30)

    def test_comparison_keeps_unmatched_workloads_explicit(self):
        data = [dict(id="S1-1", warm_idle_delta=100, gc_delta="NOT_AVAILABLE"), dict(id="S2-1", warm_idle_delta=2, gc_delta="NOT_AVAILABLE")]
        result = comparisons(data)
        self.assertIn("unmatched", result[0]["interpretation"])
        self.assertEqual(result[0]["left"][0]["delta"], 100)


if __name__ == "__main__":
    unittest.main()
