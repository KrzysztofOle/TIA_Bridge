"""Independent schema checks for the B-01B synthetic artifacts; no live IO."""
import copy
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8"))


class WorkspaceContractTests(unittest.TestCase):
    def validator(self, name):
        schema = read(f"contracts/v1/{name}.schema.json")
        Draft202012Validator.check_schema(schema)
        return Draft202012Validator(schema, format_checker=FormatChecker())

    def test_workspace_configuration_and_both_manifests(self):
        config = read("config/examples/workspace.synthetic.json")
        self.validator("workspace-config").validate(config)
        for name in ["plc", "hmi"]:
            manifest = read(f"examples/workspace/{name}.manifest.json")
            self.validator("workspace-manifest").validate(manifest)
            self.assertEqual(config["workspace_id"], manifest["workspace_id"])
            self.assertIn(manifest["project_key"], config["projects"])

    def test_capability_configuration(self):
        config = read("config/examples/capabilities.synthetic.json")
        self.validator("capability-registry").validate(config)
        self.assertTrue(any(not e["adapter_implemented"] for e in config["entries"]))
        self.assertTrue(any(d["state"] == "PARTIAL" for e in config["entries"] for d in e["coverage"]["dimensions"]))
        self.assertTrue(all(not e["write_scope"] for e in config["entries"]))

    def test_project_scoped_capability_v2_is_distinct_from_device_family_and_read_only(self):
        config = read("config/examples/capabilities.project.synthetic.json")
        schema = read("contracts/v2/capability-registry.schema.json")
        Draft202012Validator.check_schema(schema)
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        validator.validate(config)
        key = config["entries"][0]["key"]
        self.assertEqual("PROJECT", key["scope"])
        self.assertIsNone(key["device_family"])
        self.assertEqual("project.inspect", key["operation"])
        self.assertEqual("v19.discovery.v1", key["profile"])
        self.assertFalse(config["entries"][0]["write_scope"])
        for mutation in [lambda n: n["entries"][0]["key"].update(device_family="PLC"),
                         lambda n: n["entries"][0]["key"].update(scope="DEVICE"),
                         lambda n: n["entries"][0].update(write_scope=["project.write"]),
                         lambda n: n["entries"][0].update(unreviewed=True),
                         lambda n: n.update(schema_version="1.0")]:
            invalid = copy.deepcopy(config)
            mutation(invalid)
            self.assertFalse(validator.is_valid(invalid))

    def test_manifest_missing_fields_unknown_fields_and_versions(self):
        manifest = read("examples/workspace/plc.manifest.json")
        validator = self.validator("workspace-manifest")
        for key in manifest:
            invalid = copy.deepcopy(manifest)
            del invalid[key]
            self.assertFalse(validator.is_valid(invalid), key)
        for mutate in [lambda n: n.update(schema_version="2.0"),
                       lambda n: n.update(unrecognized=True),
                       lambda n: n["inventory"][0].update(status="SYNCED"),
                       lambda n: n["inventory"][0].update(model_sha256="short"),
                       lambda n: n["inventory"][0]["vendor_locator"].update(unrecognized=True)]:
            invalid = copy.deepcopy(manifest)
            mutate(invalid)
            self.assertFalse(validator.is_valid(invalid))
        manifest["inventory"][0]["vendor_fields"]["UnknownVendorData"] = {"Keep": [1.25, "x", False]}
        validator.validate(manifest)

    def test_capabilities_reject_unknown_keys_and_session_grants(self):
        config = read("config/examples/capabilities.synthetic.json")
        validator = self.validator("capability-registry")
        for mutate in [lambda n: n.update(schema_version="1.1"),
                       lambda n: n.update(session_grant="not-authorization"),
                       lambda n: n["entries"][0]["key"].update(operation="InvokeSiemens"),
                       lambda n: n["entries"][0]["key"].update(device_family="HMI_ANY")]:
            invalid = copy.deepcopy(config)
            mutate(invalid)
            self.assertFalse(validator.is_valid(invalid))


if __name__ == "__main__":
    unittest.main()
