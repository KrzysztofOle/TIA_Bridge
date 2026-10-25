"""Independent JSON Schema/OpenAPI checks for B-01A. Synthetic data only."""
from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import OpenAPIV31SpecValidator

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / "contracts/v1"
EXAMPLES = ROOT / "examples/contracts/v1"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


class ContractTests(unittest.TestCase):
    def setUp(self):
        self.request = Draft202012Validator(read(BASE / "operation-request.schema.json"), format_checker=FormatChecker())
        self.report = Draft202012Validator(read(BASE / "operation.schema.json"), format_checker=FormatChecker())
        self.registry = read(BASE / "operations.json")["operations"]

    def test_all_schemas_are_valid_and_have_no_remote_refs(self):
        def walk(value):
            if isinstance(value, dict):
                if "$ref" in value:
                    self.assertTrue(value["$ref"].startswith("#/"), value["$ref"])
                for child in value.values():
                    walk(child)
            elif isinstance(value, list):
                for child in value:
                    walk(child)
        for path in BASE.glob("*.schema.json"):
            with self.subTest(schema=path.name):
                schema = read(path)
                Draft202012Validator.check_schema(schema)
                walk(schema)

    def test_all_19_methods_have_requests_typed_results_and_errors(self):
        self.assertEqual(19, len(self.registry))
        for operation in self.registry:
            method = operation["name"]
            with self.subTest(method=method):
                request = read(EXAMPLES / f"request.{method}.json")
                response = read(EXAMPLES / f"response.{method}.json")
                self.request.validate(request)
                self.report.validate(response)
                self.assertTrue(response["result"])
                for field in operation["required_params"]:
                    invalid = copy.deepcopy(request)
                    del invalid["params"][field]
                    self.assertFalse(self.request.is_valid(invalid), (method, field))
                for code in operation["error_codes"]:
                    failed = copy.deepcopy(response)
                    failed["state"] = "FAILED"
                    failed["outcome"] = "NOT_STARTED"
                    failed["error"] = {"code": code, "category": "EXECUTION", "message": "Synthetic failure", "retryable": False, "outcome": "NOT_STARTED"}
                    self.report.validate(failed)

    def test_negative_examples(self):
        cases = read(EXAMPLES / "invalid-requests.json")
        self.assertGreaterEqual(len(cases), 19)
        for case in cases:
            with self.subTest(case=case["name"]):
                self.assertFalse(self.request.is_valid(case["request"]))

    def test_openapi_and_all_local_references(self):
        spec = read(BASE / "openapi.json")
        OpenAPIV31SpecValidator(spec).validate()
        exposed = set()
        for item in spec["paths"].values():
            for operation in item.values():
                exposed.update(operation["x-methods"])
        self.assertEqual({r["name"] for r in self.registry}, exposed)
        def walk(value):
            if isinstance(value, dict):
                if "$ref" in value:
                    current = spec
                    self.assertTrue(value["$ref"].startswith("#/"))
                    for part in value["$ref"][2:].split("/"):
                        current = current[part.replace("~1", "/").replace("~0", "~")]
                for child in value.values():
                    walk(child)
            elif isinstance(value, list):
                for child in value:
                    walk(child)
        walk(spec)

    def test_generated_artifacts_are_reproducible(self):
        spec = importlib.util.spec_from_file_location("generate_contracts", ROOT / "scripts/generate_contracts.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        for path, value in module.build(read(BASE / "operation-request.schema.json"), read(BASE / "operations.json")).items():
            expected = value if isinstance(value, str) else json.dumps(value, ensure_ascii=False, indent=2) + "\n"
            self.assertEqual(expected, path.read_text(encoding="utf-8"), str(path.relative_to(ROOT)))

    def test_false_success_and_retry_after_unknown_are_rejected(self):
        response = read(EXAMPLES / "response.sync.apply.json")
        response["outcome"] = "UNKNOWN"
        self.assertFalse(self.report.is_valid(response))
        response["state"] = "RECONCILIATION_REQUIRED"
        self.report.validate(response)
        response["error"] = {"code": "TIMEOUT", "category": "EXECUTION", "message": "Synthetic", "outcome": "UNKNOWN", "retryable": True}
        self.assertFalse(self.report.is_valid(response))


if __name__ == "__main__":
    unittest.main()
