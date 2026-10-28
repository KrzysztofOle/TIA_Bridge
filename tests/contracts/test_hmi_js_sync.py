import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "hmi_js_sync.py"
SPEC = importlib.util.spec_from_file_location("hmi_js_sync", SCRIPT)
HMI = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HMI)


class HmiJsSyncTests(unittest.TestCase):
    def test_safe_names_preserve_unicode_and_disambiguate_identity(self):
        first = HMI.safe_segment("Récipe/JS", "0123456789abcdef")
        second = HMI.safe_segment("Récipe/JS", "fedcba9876543210")
        self.assertNotEqual(first, second)
        self.assertIn("%2F", first)
        self.assertIn("Récipe", first)

    def test_package_is_byte_exact_idempotent_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            raw = b"// native\r\nfunction f(){ return 'x'; }\r\n"
            (root / "unit.hmi.js").write_bytes(raw)
            (root / "unit.hmi.yml").write_bytes(b"version: 1\r\n")
            (root / "curated.js").write_bytes(b"// reviewed source\n")
            inventory = {"project_identity": "p", "hmi_device_identity": "d", "modules": [{
                "identity": "module:1", "name": "Módül", "native_artifacts": ["unit.hmi.js", "unit.hmi.yml"],
                "source_artifact": "curated.js", "functions": [], "dependencies": [], "coverage": "PARTIAL"}]}
            index = root / "inventory.json"
            index.write_text(json.dumps(inventory), encoding="utf-8")
            destination = root / "out"
            manifest = HMI.package(index, destination)
            repeat = HMI.package(index, root / "out-repeat")
            self.assertEqual(manifest, repeat)
            native = destination / manifest["modules"][0]["native_artifacts"][0]["path"]
            self.assertEqual(raw, native.read_bytes())
            self.assertEqual(HMI.sha256(native), manifest["modules"][0]["native_artifacts"][0]["sha256"])
            self.assertNotEqual(native.read_bytes(), (destination / manifest["modules"][0]["source"]).read_bytes())
            self.assertFalse(manifest["complete"])
            self.assertEqual("NOT_ENGINEERING_VERIFIED", manifest["source_status"])
            with self.assertRaises(FileExistsError):
                HMI.package(index, destination)

    def test_compare_tracks_content_identity_and_missing_is_observation(self):
        old = {"modules": [{"identity": "a", "name": "one", "native_artifacts": [], "source_sha256": "1", "coverage": "COMPLETE"},
                           {"identity": "b", "name": "gone", "native_artifacts": [], "coverage": "COMPLETE"}]}
        new = {"modules": [{"identity": "a", "name": "one", "native_artifacts": [], "source_sha256": "2", "coverage": "COMPLETE"},
                           {"identity": "c", "name": "new", "native_artifacts": [], "coverage": "COMPLETE"}]}
        result = {x["identity"]: x for x in HMI.compare(old, new)}
        self.assertEqual("MODIFIED", result["a"]["status"])
        self.assertEqual("MISSING", result["b"]["status"])
        self.assertEqual("OBSERVATION_ONLY", result["b"]["action"])
        self.assertEqual("ADDED", result["c"]["status"])

    def test_unsupported_and_incomplete_coverage_fail_closed(self):
        old = {"modules": []}
        current = {"modules": [{"identity": "a", "unsupported_reason": "screen event API unknown", "status": "UNSUPPORTED"},
                               {"identity": "b", "name": "b", "coverage": "PARTIAL"}]}
        statuses = {x["identity"]: x["status"] for x in HMI.compare(old, current)}
        self.assertEqual("UNSUPPORTED", statuses["a"])
        self.assertEqual("ADDED", statuses["b"])
        self.assertEqual({"UNCHANGED", "MODIFIED", "ADDED", "MISSING", "UNSUPPORTED", "UNKNOWN"}, HMI.STATUSES)

    def test_incomplete_matching_object_is_unknown_and_metadata_drift_is_modified(self):
        old = {"modules": [{"identity": "a", "name": "m", "native_artifacts": [], "functions": ["f"], "dependencies": [], "coverage": "PARTIAL"}]}
        same = {"modules": [{"identity": "a", "name": "m", "native_artifacts": [], "functions": ["f"], "dependencies": [], "coverage": "PARTIAL"}]}
        changed = {"modules": [{"identity": "a", "name": "m", "native_artifacts": [], "functions": ["f", "g"], "dependencies": [], "coverage": "PARTIAL"}]}
        self.assertEqual("UNKNOWN", HMI.compare(old, same)[0]["status"])
        self.assertEqual("MODIFIED", HMI.compare(old, changed)[0]["status"])

    def test_duplicate_output_path_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "same.js").write_bytes(b"x")
            inventory = {"project_identity": "p", "hmi_device_identity": "d", "modules": [
                {"identity": "a", "name": "One", "native_artifacts": ["same.js"]},
                {"identity": "b", "name": "Two", "native_artifacts": ["same.js"]}]}
            index = root / "inventory.json"
            index.write_text(json.dumps(inventory), encoding="utf-8")
            # Duplicate members in one exchange unit collide even on case-insensitive filesystems.
            inventory["modules"] = [{"identity": "a", "name": "One", "native_artifacts": ["same.js", "same.js"]}]
            index.write_text(json.dumps(inventory), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "collision"):
                HMI.package(index, root / "out")


if __name__ == "__main__":
    unittest.main()
