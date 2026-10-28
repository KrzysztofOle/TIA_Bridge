"""Independent manifest/schema/CLI checks using synthetic source only; never Siemens."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "src/TiaBridge.Cli/bin/Release/net10.0/TiaBridge.Cli.dll"
BINDING = json.loads((ROOT / "examples/hmi-js/export-binding.synthetic.json").read_text())


class HmiJsExportContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest_schema = Draft202012Validator(json.loads((ROOT / "contracts/hmi-js/manifest.schema.json").read_text()), format_checker=FormatChecker())
        cls.binding_schema = Draft202012Validator(json.loads((ROOT / "contracts/hmi-js/export-binding.schema.json").read_text()), format_checker=FormatChecker())

    def invoke(self, *args):
        return subprocess.run(["dotnet", str(CLI), "hmi-js", *map(str, args)], capture_output=True, text=True, encoding="utf-8", timeout=30, check=False)

    def test_actual_cli_ten_files_schema_repeat_hashes_and_one_function_diff(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            native = root / "input"
            native.mkdir()
            names = BINDING["expected_functions"]
            global_code = "\ufeffconst synthetic = 'Zażółć 😀';\r\n"
            functions = {name: f"export function {name}() {{\r\n  /* braces {{}} */ return synthetic;\r\n}}\r\n" for name in names}
            original = (global_code + "".join(functions.values())).encode("utf-8")
            source = native / "T05RecipeCatalog.hmi.js"
            source.write_bytes(original)
            (native / "T05RecipeCatalog.hmi.yml").write_bytes(b"# synthetic opaque metadata\r\nunknown: 19\r\n")
            first = self.invoke("split", native, root / "first", "T05RecipeCatalog", "synthetic-bound-scope")
            self.assertEqual(0, first.returncode, first.stdout + first.stderr)
            manifest = json.loads(first.stdout)
            self.manifest_schema.validate(manifest)
            self.assertIsNone(manifest["object_id"])
            self.assertEqual(10, len(manifest["parts"]))
            for row in manifest["parts"] + manifest["native_artifacts"]:
                data = (root / "first" / row["path"]).read_bytes()
                self.assertEqual(row["sha256"], hashlib.sha256(data).hexdigest())
                self.assertEqual(row["bytes"], len(data))
            self.assertEqual(global_code.encode("utf-8"), (root / "first/T05RecipeCatalog/Global definition area.js").read_bytes())
            for name in names:
                self.assertEqual(functions[name].encode("utf-8"), (root / "first/T05RecipeCatalog" / (name + ".js")).read_bytes())
            repeat = self.invoke("split", native, root / "repeat", "T05RecipeCatalog", "synthetic-bound-scope")
            self.assertEqual(0, repeat.returncode, repeat.stdout + repeat.stderr)
            self.assertEqual(manifest, json.loads(repeat.stdout))
            source.write_bytes(original.replace(b"return synthetic;", b"return 'changed';", 1))
            changed = self.invoke("split", native, root / "changed", "T05RecipeCatalog", "synthetic-bound-scope")
            self.assertEqual(0, changed.returncode, changed.stdout + changed.stderr)
            compared = self.invoke("compare", root / "first/manifest.json", root / "changed/manifest.json")
            self.assertEqual(0, compared.returncode, compared.stdout + compared.stderr)
            changes = json.loads(compared.stdout)
            self.assertEqual(["EXPORTED_FUNCTION:createCatalog"], [row["comparison_key"] for row in changes if row["status"] == "MODIFIED"])
            self.assertEqual(9, sum(row["status"] == "UNCHANGED" for row in changes))

    def test_blocked_cli_preserves_native_with_no_source_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            native = root / "input"
            native.mkdir()
            (native / "opaque.bin").write_bytes(b"\x00\xff")
            result = self.invoke("split", native, root / "output", "T05RecipeCatalog", "synthetic")
            self.assertEqual(3, result.returncode, result.stdout + result.stderr)
            manifest = json.loads(result.stdout)
            self.manifest_schema.validate(manifest)
            self.assertEqual("BLOCKED", manifest["split_status"])
            self.assertFalse((root / "output/T05RecipeCatalog").exists())
            self.assertEqual(b"\x00\xff", (root / "output/native/opaque.bin").read_bytes())

    def test_binding_and_request_examples_validate_without_granting_authorization(self):
        self.binding_schema.validate(BINDING)
        request = json.loads((ROOT / "examples/hmi-js/request.project.export.synthetic.json").read_text())
        schema = json.loads((ROOT / "contracts/v1/operation-request.schema.json").read_text())
        Draft202012Validator(schema, format_checker=FormatChecker()).validate(request)
        self.assertEqual("2000-01-01T00:05:00Z", BINDING["expires_utc"])
        bad = copy.deepcopy(BINDING)
        bad["destination"] = "client-selected-path"
        self.assertFalse(self.binding_schema.is_valid(bad))


if __name__ == "__main__":
    unittest.main()
