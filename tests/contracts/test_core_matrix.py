"""B-01C differential corpus: independent JSON Schema/OpenAPI validation."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8"))


class CoreMatrixTests(unittest.TestCase):
    def test_shared_shape_matrix_matches_schema_and_openapi(self):
        spec = read("contracts/v1/openapi.json")
        schemas = {"request": "operation-request", "report": "operation", "error": "error"}
        validators = {k: Draft202012Validator(read(f"contracts/v1/{v}.schema.json"), format_checker=FormatChecker()) for k, v in schemas.items()}
        api_names = {"request": "Request", "report": "Operation", "error": "Error"}
        api = {k: Draft202012Validator({**spec, "$ref": f"#/components/schemas/{v}"}, format_checker=FormatChecker()) for k, v in api_names.items()}
        rules = read("tests/fixtures/contract-mutations.v1.json")
        methods = read("contracts/v1/operations.json")["operations"]
        self.assertEqual(19, len(methods))
        for operation in methods:
            method = operation["name"]
            for rule in rules:
                with self.subTest(method=method, case=rule["name"], target=rule["target"]):
                    target = rule["target"]
                    message = ({"code": "TIMEOUT", "category": "EXECUTION", "message": "Synthetic failure", "retryable": False, "outcome": "NOT_STARTED"}
                               if target == "error" else read(f"examples/contracts/v1/{'request' if target == 'request' else 'response'}.{method}.json"))
                    parent = message
                    for part in rule["path"][:-1]:
                        parent = parent[part]
                    if rule["path"]:
                        if rule["remove"]:
                            del parent[rule["path"][-1]]
                        else:
                            parent[rule["path"][-1]] = copy.deepcopy(rule["value"])
                    self.assertEqual(rule["valid"], validators[target].is_valid(message))
                    self.assertEqual(rule["valid"], api[target].is_valid(message))

    def test_required_results_and_all_method_error_codes(self):
        schema = read("contracts/v1/operation.schema.json")
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        for operation in read("contracts/v1/operations.json")["operations"]:
            method = operation["name"]
            report = read(f"examples/contracts/v1/response.{method}.json")
            shape = schema["$defs"]["".join(p.title() for p in method.split(".")) + "Result"]
            for field in shape["required"]:
                with self.subTest(method=method, missing=field):
                    bad = copy.deepcopy(report)
                    del bad["result"][field]
                    self.assertFalse(validator.is_valid(bad))
            for code in operation["error_codes"]:
                failed = copy.deepcopy(report)
                failed.update(state="FAILED", outcome="NOT_STARTED", error=dict(code=code, category="EXECUTION", message="Synthetic failure", retryable=False, outcome="NOT_STARTED"))
                validator.validate(failed)
            branch = next(r for r in schema["allOf"] if r.get("if", {}).get("properties", {}).get("method", {}).get("const") == method)
            allowed = branch["then"]["properties"]["error"]["oneOf"][1]["allOf"][1]["properties"]["code"]["enum"]
            codes = read("contracts/v1/error.schema.json")["properties"]["code"]["enum"]
            foreign = next(c for c in codes if c not in allowed)
            failed["error"]["code"] = foreign
            self.assertFalse(validator.is_valid(failed))

    def test_100_golden_vectors_have_distinct_inputs_and_independent_hashes(self):
        vectors = read("tests/fixtures/core-determinism.v1.json")
        self.assertEqual(list(range(100)), [v["case_id"] for v in vectors])
        self.assertEqual(100, len({v["object_id"] for v in vectors}))
        self.assertEqual(100, len({v["model_sha256"] for v in vectors}))
        for v in vectors:
            canonical = json.dumps(dict(normalizer_version=v["normalizer"], comparison_profile=v["profile"], model=v["model"]), sort_keys=True, separators=(",", ":")).encode()
            self.assertEqual(v["model_sha256"], hashlib.sha256(canonical).hexdigest())
            self.assertEqual(v["raw_sha256"], hashlib.sha256(f"synthetic-{v['case_id']}\r\n".encode()).hexdigest())
        spec = importlib.util.spec_from_file_location("generate_test_vectors", ROOT / "scripts/generate_test_vectors.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        for name, value in module.build().items():
            self.assertEqual(value, read("tests/fixtures/" + name))

    def test_nested_lifecycle_cannot_hide_unknown_behind_a_successful_status_read(self):
        schema = read("contracts/v1/operation.schema.json")
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        for method in ["operation.status", "operation.result"]:
            for state, outcome in [("SUCCEEDED", "UNKNOWN"), ("QUEUED", "CONFIRMED"), ("RECONCILIATION_REQUIRED", "CONFIRMED")]:
                report = read(f"examples/contracts/v1/response.{method}.json")
                report["result"].update(state=state, outcome=outcome)
                self.assertFalse(validator.is_valid(report), (method, state, outcome))


if __name__ == "__main__":
    unittest.main()
