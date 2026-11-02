"""Synthetic file-only unit, schema and real CLI tests; no Siemens or network."""
import ast
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/source_package_preflight.py"
spec = importlib.util.spec_from_file_location("source_package_preflight", SCRIPT)
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


class SourcePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.root = self.base / "package"
        self.root.mkdir()
        self.manifest = self.root / "manifest.json"
        self.data = dict(schema_version=p.VERSION, required_checks=list(p.CHECKS), files=[])
        self.add_file("module.js", b"export function run() {}\r\n")
        self.save()
        self.report_schema = Draft202012Validator(json.loads((p.SCHEMAS / "report.schema.json").read_text()))

    def add_file(self, path, raw, binary=False):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
        row = dict(path=path, role="native", type="binary" if binary else "text", sha256=hashlib.sha256(raw).hexdigest())
        if not binary:
            row["text"] = dict(encoding="utf-8", bom="forbidden", eol="crlf", final_newline="required")
        self.data["files"].append(row)
        return row

    def save(self):
        self.manifest.write_bytes(p.encode(self.data))

    def result(self, code=0, issue=None):
        self.save()
        result = p.validate(self.manifest, self.root)
        self.assertEqual(code, result["exit_code"], result)
        self.report_schema.validate(result)
        if issue:
            self.assertIn(issue, [i["code"] for i in result["issues"]])
        return result

    def cli(self, *args):
        return subprocess.run([sys.executable, "-B", str(SCRIPT), *map(str, args)], capture_output=True, text=True, timeout=15)

    def default_args(self):
        return ["--root", self.root, "--manifest", self.manifest, "--json"]

    def test_valid_package(self):
        report = self.result()
        self.assertTrue(all(c["status"] == "PASS" for c in report["checks"]))

    def test_hash_mismatch(self):
        self.data["files"][0]["sha256"] = "0" * 64
        report = self.result(1, "SHA256_MISMATCH")
        self.assertEqual("module.js", report["issues"][0]["path"])
        self.assertEqual("0" * 64, report["issues"][0]["expected"])

    def test_missing_file(self):
        (self.root / "module.js").unlink()
        self.result(1, "FILE_MISSING")

    def test_extra_file(self):
        (self.root / "extra.txt").write_bytes(b"extra")
        self.result(1, "FILE_EXTRA")

    def test_duplicate_path(self):
        self.data["files"].append(copy.deepcopy(self.data["files"][0]))
        self.result(1, "PATH_DUPLICATE")

    def test_case_collision(self):
        row = copy.deepcopy(self.data["files"][0]); row["path"] = "MODULE.js"
        self.data["files"].append(row)
        self.result(1, "PATH_DUPLICATE")

    def test_file_directory_collision(self):
        row = copy.deepcopy(self.data["files"][0]); row["path"] = "module.js/child.txt"
        self.data["files"].append(row)
        self.result(1, "PATH_COLLISION")

    def test_invalid_paths(self):
        for path in ("../outside", "/absolute", "C:/absolute", "C:relative", "\\\\host\\share", "a\\b", "a//b", "a/./b", "CON.txt", "LPT1", "a.", "a ", "a:ads", "a\x00b", "e\u0301.js", "a?b"):
            with self.subTest(path=path):
                self.data["files"][0]["path"] = path
                self.result(1, "PATH_INVALID")

    def test_invalid_manifest_json(self):
        for raw in (b"{", b'[]', b'{"schema_version":NaN}', b'{"x":1,"x":2}', b'{"x":"\\ud800"}', b'\xff'):
            with self.subTest(raw=raw):
                self.manifest.write_bytes(raw)
                report = p.validate(self.manifest, self.root)
                self.assertEqual(2, report["exit_code"])
                self.report_schema.validate(report)

    def test_unknown_fields_fail_closed(self):
        self.data["unknown_vendor_field"] = "must not be ignored"
        self.result(2, "MANIFEST_SCHEMA_INVALID")

    def test_missing_schema_version(self):
        del self.data["schema_version"]
        self.result(2, "MANIFEST_SCHEMA_INVALID")

    def test_unsupported_version(self):
        self.data["schema_version"] = "tia-bridge.source-package/2.0"
        self.result(3, "MANIFEST_VERSION_UNSUPPORTED")

    def test_unsupported_required_check(self):
        self.data["required_checks"].append("events.routes")
        report = self.result(3, "REQUIRED_CHECK_UNSUPPORTED")
        self.assertTrue(any(c["status"] == "NOT_RUN" for c in report["checks"]))

    def test_invalid_encoding_hashed_before_decode(self):
        self.add_file("invalid.txt", b"\xff\xfe\x00", False)
        report = self.result(1, "ENCODING_INVALID")
        self.assertEqual(2, next(c for c in report["checks"] if c["id"] == "files.sha256")["count"])
        self.assertNotIn("SHA256_MISMATCH", [i["code"] for i in report["issues"]])

    def test_lf_violates_crlf_even_with_correct_hash(self):
        self.add_file("wrong.js", b"one\ntwo\n")
        self.result(1, "EOL_MISMATCH")

    def test_lf_policy(self):
        row = self.add_file("lf.js", b"one\ntwo\n"); row["text"]["eol"] = "lf"
        self.result()

    def test_mixed_policy(self):
        row = self.add_file("mixed.txt", b"one\r\ntwo\n")
        self.result(1, "EOL_MISMATCH")
        row["text"]["eol"] = "any"
        self.result()

    def test_lone_cr_rejected_even_with_any(self):
        row = self.add_file("cr.txt", b"one\rtwo\n"); row["text"]["eol"] = "any"
        self.result(1, "EOL_MISMATCH")

    def test_binary_has_no_text_policy(self):
        self.add_file("opaque.bin", b"\xff\x00\r\n\n", True)
        self.result()
        self.data["files"][-1]["text"] = self.data["files"][0]["text"]
        self.result(2, "MANIFEST_SCHEMA_INVALID")

    def test_text_policy_is_required(self):
        del self.data["files"][0]["text"]
        self.result(2, "MANIFEST_SCHEMA_INVALID")

    def test_unsupported_type(self):
        self.data["files"][0]["type"] = "execute"
        self.result(2, "MANIFEST_SCHEMA_INVALID")

    def test_final_newline_and_no_newlines(self):
        row = self.add_file("single.txt", b"single")
        self.result(1, "FINAL_NEWLINE_MISMATCH")
        row["text"]["final_newline"] = "optional"
        self.result()
        row["text"]["final_newline"] = "forbidden"
        self.result()
        self.data["files"][0]["text"]["final_newline"] = "forbidden"
        self.result(1, "FINAL_NEWLINE_MISMATCH")

    def test_empty_text(self):
        row = self.add_file("empty.txt", b""); row["text"]["final_newline"] = "optional"
        self.result()

    def test_bom_policies(self):
        row = self.add_file("bom.txt", b"\xef\xbb\xbfx\r\n")
        self.result(1, "BOM_MISMATCH")
        row["text"]["bom"] = "required"
        self.result()
        row["text"]["bom"] = "optional"
        self.result()
        self.data["files"][0]["text"]["bom"] = "required"
        self.result(1, "BOM_MISMATCH")

    def test_manifest_depth_and_size_limits(self):
        self.manifest.write_bytes(b"[" * 17)
        self.assertEqual("MANIFEST_LIMIT", p.validate(self.manifest, self.root)["issues"][0]["code"])
        with patch.object(p, "MAX_MANIFEST", 10):
            self.result(2, "MANIFEST_LIMIT")

    def test_file_total_and_inventory_limits(self):
        with patch.object(p, "MAX_FILE", 2):
            self.result(1, "INPUT_LIMIT")
        with patch.object(p, "MAX_TOTAL", 2):
            self.result(1, "INPUT_LIMIT")
        with patch.object(p, "MAX_ENTRIES", 1):
            self.result(1, "INVENTORY_LIMIT")

    def test_manifest_self_reference(self):
        row = copy.deepcopy(self.data["files"][0]); row["path"] = "manifest.json"
        self.data["files"].append(row)
        self.result(2, "MANIFEST_SELF_REFERENCE")

    def test_external_manifest(self):
        self.manifest.unlink()
        self.manifest = self.base / "external.json"
        self.result()

    def test_auxiliary_files_must_be_listed(self):
        self.add_file("README.txt", b"synthetic\r\n")["role"] = "auxiliary"
        self.result()

    def test_determinism_and_input_immutability(self):
        before = {f.name: f.read_bytes() for f in self.root.iterdir()}
        first = p.validate(self.manifest, self.root)
        self.assertEqual(p.encode(first), p.encode(p.validate(self.manifest, self.root)))
        self.assertEqual(before, {f.name: f.read_bytes() for f in self.root.iterdir()})

    def test_output_never_overwrites_sources_or_existing_reports(self):
        before = {f.name: f.read_bytes() for f in self.root.iterdir()}
        for target in (self.manifest, self.root / "module.js", self.root / "report.json"):
            with self.subTest(target=target):
                result = self.cli(*self.default_args(), "--output", target)
                self.assertEqual(2, result.returncode)
        self.assertEqual(before, {f.name: f.read_bytes() for f in self.root.iterdir()})
        output = self.base / "report.json"
        result = self.cli(*self.default_args(), "--output", output)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(json.loads(result.stdout), json.loads(output.read_text()))
        saved = output.read_bytes()
        self.assertEqual(2, self.cli(*self.default_args(), "--output", output).returncode)
        self.assertEqual(saved, output.read_bytes())

    def test_external_manifest_output_protected(self):
        self.manifest.unlink(); self.manifest = self.base / "external.json"; self.save()
        self.assertEqual(2, self.cli(*self.default_args(), "--output", self.manifest).returncode)

    def test_cli_exit_codes(self):
        self.assertEqual(0, self.cli(*self.default_args()).returncode)
        self.data["files"][0]["sha256"] = "0" * 64; self.save()
        self.assertEqual(1, self.cli(*self.default_args()).returncode)
        self.assertEqual(2, self.cli("--root", self.root).returncode)
        self.data["required_checks"] = ["future.check"]; self.save()
        self.assertEqual(3, self.cli(*self.default_args()).returncode)
        self.data["required_checks"] = list(p.CHECKS); self.save()
        # Without site-packages, the real command must report missing jsonschema as internal failure.
        result = subprocess.run([sys.executable, "-S", "-B", str(SCRIPT), *map(str, self.default_args())],
                                capture_output=True, text=True, timeout=15, env={**os.environ, "PYTHONPATH": ""})
        self.assertEqual(4, result.returncode, result.stderr)
        self.report_schema.validate(json.loads(result.stdout))

    def test_internal_failure_exit_four_and_schema(self):
        with patch.object(p, "inventory", side_effect=RuntimeError("sensitive data")):
            report = self.result(4, "VALIDATOR_INTERNAL_ERROR")
        self.assertNotIn("sensitive data", p.encode(report).decode())
        with patch.object(p, "inventory", side_effect=RuntimeError()), patch.object(p.sys.stdout, "write"):
            self.assertEqual(4, p.main(list(map(str, self.default_args()))))

    def test_read_failure_cannot_pass(self):
        original = p.read_bytes
        def read(path, limit):
            if path.name == "module.js":
                raise PermissionError()
            return original(path, limit)
        with patch.object(p, "read_bytes", side_effect=read):
            self.result(1, "FILE_UNREADABLE")

    def test_input_change_cannot_pass(self):
        original = os.fstat
        calls = 0
        def stat_with_mutation(fd):
            nonlocal calls
            calls += 1
            if calls == 2:
                with (self.root / "module.js").open("ab") as stream:
                    stream.write(b"changed")
            return original(fd)
        with patch.object(p.os, "fstat", side_effect=stat_with_mutation):
            with self.assertRaises(p.Rejected) as error:
                p.read_bytes(self.root / "module.js", p.MAX_FILE)
        self.assertEqual("INPUT_CHANGED", error.exception.code)

    def test_binary_only_package_has_no_eol_requirement(self):
        (self.root / "module.js").unlink()
        self.data["files"] = []
        self.add_file("opaque.bin", b"\xff\x00\r\n\n", True)
        report = self.result()
        self.assertEqual([0, 0], [c["count"] for c in report["checks"][-2:]])

    def test_hardlink_report_cannot_overwrite_source(self):
        output = self.base / "hardlink.json"
        os.link(self.root / "module.js", output)
        before = output.read_bytes()
        self.assertEqual(2, self.cli(*self.default_args(), "--output", output).returncode)
        self.assertEqual(before, output.read_bytes())

    def test_demo_packages(self):
        for name, code, issue in (("pass", 0, None), ("bad-hash", 1, "SHA256_MISMATCH"), ("bad-eol", 1, "EOL_MISMATCH")):
            root = ROOT / "examples/source-package" / name
            report = p.validate(root / "manifest.json", root)
            self.assertEqual(code, report["exit_code"], report)
            self.report_schema.validate(report)
            if issue:
                self.assertEqual([issue], [i["code"] for i in report["issues"]])

    def test_invalid_root_missing_manifest_and_network_paths(self):
        for manifest, root in ((self.manifest, self.root / "missing"), (self.root / "missing.json", self.root), (self.manifest, self.manifest), ("\\\\server\\share\\x.json", self.root), (self.manifest, "//server/share"), ("bad\x00path", self.root)):
            with self.subTest(root=root, manifest=manifest):
                report = p.validate(manifest, root)
                self.assertEqual(2, report["exit_code"])
                self.report_schema.validate(report)

    def test_real_junctions_rejected_without_following(self):
        if os.name != "nt":
            outside = self.base / "outside"; outside.mkdir()
            (self.root / "redirect").symlink_to(outside, target_is_directory=True)
            self.result(1, "REPARSE_POINT_FORBIDDEN")
            return
        outside = self.base / "outside"; outside.mkdir()
        (outside / "untouched.txt").write_bytes(b"outside")
        for destination in (outside, self.root):
            link = self.root / "redirect"
            # Native Windows junction requires no symlink privilege; the process creates only this synthetic link.
            result = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(destination)], capture_output=True, timeout=10)
            self.assertEqual(0, result.returncode, result.stderr)
            try:
                self.result(1, "REPARSE_POINT_FORBIDDEN")
            finally:
                os.rmdir(link)
        self.assertEqual(b"outside", (outside / "untouched.txt").read_bytes())

    def test_output_junction_rejected(self):
        link = self.base / "output-link"
        if os.name == "nt":
            result = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(self.root)], capture_output=True, timeout=10)
            self.assertEqual(0, result.returncode)
        else:
            link.symlink_to(self.root, target_is_directory=True)
        try:
            result = self.cli(*self.default_args(), "--output", link / "report.json")
            self.assertEqual(2, result.returncode)
            self.assertFalse((self.root / "report.json").exists())
        finally:
            if os.name == "nt":
                os.rmdir(link)
            else:
                link.unlink()

    def test_no_process_network_or_vendor_dependencies(self):
        tree = ast.parse(SCRIPT.read_text())
        imports = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imports.update(n.name.split(".")[0] for n in node.names)
            if isinstance(node, ast.ImportFrom):
                imports.add(node.module.split(".")[0])
        self.assertEqual(set(), imports - {"__future__", "argparse", "hashlib", "json", "os", "pathlib", "re", "stat", "sys", "unicodedata", "ctypes", "jsonschema"})
        with patch("subprocess.Popen", side_effect=AssertionError("process forbidden")), patch("socket.socket", side_effect=AssertionError("network forbidden")):
            self.result()

    def test_schema_self_validation_and_pass_invariants(self):
        for name in ("manifest", "report"):
            Draft202012Validator.check_schema(json.loads((p.SCHEMAS / (name + ".schema.json")).read_text()))
        report = self.result()
        report["checks"][1]["status"] = "NOT_RUN"
        self.assertFalse(self.report_schema.is_valid(report))


if __name__ == "__main__":
    unittest.main()
