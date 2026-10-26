"""Versioned read-only IPC examples; never starts Siemens or imports vendor libraries."""
import json
from pathlib import Path
import unittest
from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]


class V19IpcContractTests(unittest.TestCase):
    def test_all_readonly_examples_validate_and_synthetic_payloads_are_rejected(self):
        schema = json.loads((ROOT / "contracts/ipc/v1.1/message.schema.json").read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(schema)
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        examples = list((ROOT / "examples/ipc/v1.1").glob("*.json"))
        self.assertEqual(len(examples), 9)
        for path in examples:
            value = json.loads(path.read_text(encoding="utf-8"))
            with self.subTest(file=path.name):
                validator.validate(value)
                old = json.loads((ROOT / "examples/ipc/v1" / path.name).read_text(encoding="utf-8"))
                self.assertTrue(list(validator.iter_errors(old)))
                if value["kind"] in {"Dispatch", "Heartbeat", "Stop"}:
                    old["protocol_version"] = "1.1"
                    self.assertTrue(list(validator.iter_errors(old)))


if __name__ == "__main__":
    unittest.main()
