"""Run approved unittest discovery with structured, bounded diagnostics (no dependencies)."""
import json
import os
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from qa_runner.diagnostics import redact

if __name__ == "__main__":
    root, start, pattern, destination = sys.argv[1:]
    os.chdir(root)
    sys.path.insert(0, root)
    suite = unittest.defaultTestLoader.discover(start, pattern=pattern)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    failures = [{"test": test.id(), "message": redact(trace.strip().splitlines()[-1]), "stack_trace": redact(trace[:16000])} for test, trace in result.failures + result.errors]
    value = {"total": result.testsRun, "passed": max(0, result.testsRun - len(failures) - len(result.skipped) - len(result.expectedFailures) - len(result.unexpectedSuccesses)),
             "failed": len(failures) + len(result.unexpectedSuccesses), "skipped": len(result.skipped) + len(result.expectedFailures), "failures": failures[:200]}
    with Path(destination).open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)
    raise SystemExit(0 if result.wasSuccessful() else 1)
