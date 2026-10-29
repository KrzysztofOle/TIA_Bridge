"""Synthetic offline behavior tests; Python 3.12 stdlib. No Siemens calls."""
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import compare_events as tool


def fixture():
    root = "/HMI/Device/Runtime"
    screen = root + "/ScreenGroups[0]/Screens[0]"
    item = screen + "/ScreenItems[0]"

    def obj(locator, name, kind, events="ABSENT"):
        return dict(locator=locator, name=name, kind=kind, object_type="Synthetic." + kind,
                    handlers_status=events, children_status="COMPLETE_FOR_PUBLIC_SCOPE")

    return dict(schema_version="lab-03k-events/1.0", status="COMPLETE_FOR_PUBLIC_SCOPE",
                identity=dict(project_path="Synthetic.ap19", device_path="Device/Runtime", run_id="synthetic"),
                coverage_scope="Synthetic public test scope", observations=[],
                objects=[obj(root, "Runtime", "SOFTWARE", "NOT_APPLICABLE"),
                         obj(root + "/ScreenGroups[0]", "Group", "GROUP", "NOT_APPLICABLE"),
                         obj(screen, "Main", "SCREEN"), obj(item, "Button", "ITEM", "ENUMERATED")],
                handlers=[dict(locator=item + "/EventHandlers[0]", object_locator=item,
                               object_name="Button", object_type="Synthetic.ITEM", handler_type="Synthetic.Handler",
                               event_name="Tapped", event_type="Tapped", event_type_type="Synthetic.Event",
                               presence="PRESENT", status="READ", async_status="READ", **{"async": False},
                               ScriptCode="let x = 'Zażółć 😀';\r\n", ScriptCode_status="READ",
                               GlobalDefinitionAreaScriptCode="import * as m from 'Module';\r\n",
                               GlobalDefinitionAreaScriptCode_status="READ")])


def snapshot(data):
    return tool.Snapshot(data, dict(path="synthetic/events.json", identity=data["identity"],
                                    status=data["status"], observations=data["observations"]))


class CompareTests(unittest.TestCase):
    def setUp(self):
        self.before = fixture()
        self.after = copy.deepcopy(self.before)

    def report(self):
        return tool.compare(snapshot(self.before), snapshot(self.after))

    def handlers(self):
        return [r for r in self.report()["entries"] if r["kind"] == "handler"]

    def test_identical(self):
        self.assertEqual("UNCHANGED", self.report()["status"])
        self.assertEqual(1, self.report()["counts"]["handler"]["UNCHANGED"])

    def test_code_imports_async_and_types(self):
        for prop, value in (("ScriptCode", "throw 'do not execute';"),
                            ("GlobalDefinitionAreaScriptCode", "import * as x from 'Other';"),
                            ("async", True), ("handler_type", "Other.Handler"),
                            ("event_type_type", "Other.Event"), ("object_type", "Other.Item")):
            with self.subTest(prop=prop):
                self.after = copy.deepcopy(self.before)
                self.after["handlers"][0][prop] = value
                row = self.handlers()[0]
                self.assertEqual("MODIFIED", row["status"])
                self.assertEqual(value, row["changed_properties"][prop]["after"])

    def test_object_type(self):
        self.after["objects"][-1]["object_type"] = "Other.Item"
        self.assertEqual(1, self.report()["counts"]["object"]["MODIFIED"])

    def test_event_added_removed(self):
        self.after["handlers"] = []
        self.after["objects"][-1]["handlers_status"] = "ABSENT"
        self.assertEqual("REMOVED", self.handlers()[0]["status"])
        self.before, self.after = self.after, self.before
        self.assertEqual("ADDED", self.handlers()[0]["status"])

    def test_screen_added_removed(self):
        self.after["objects"] = self.after["objects"][:2]
        self.after["handlers"] = []
        self.assertEqual(2, self.report()["counts"]["object"]["REMOVED"])
        self.assertEqual("REMOVED", self.handlers()[0]["status"])
        self.before, self.after = self.after, self.before
        self.assertEqual(2, self.report()["counts"]["object"]["ADDED"])
        self.assertEqual("ADDED", self.handlers()[0]["status"])

    def test_reordered_objects_and_handlers(self):
        for row in self.after["objects"] + self.after["handlers"]:
            for prop in ("locator", "object_locator"):
                if prop in row:
                    row[prop] = row[prop].replace("[0]", "[7]")
        self.after["objects"].reverse()
        self.assertEqual("UNCHANGED", self.report()["status"])

    def test_duplicate_names_and_descendants_unknown(self):
        row = copy.deepcopy(self.after["objects"][-2])
        row["locator"] = row["locator"].replace("Screens[0]", "Screens[1]")
        self.after["objects"].append(row)
        self.assertEqual("UNKNOWN", self.handlers()[0]["status"])
        self.assertGreater(self.report()["counts"]["object"]["UNKNOWN"], 0)

    def test_duplicate_event_unknown(self):
        self.after["handlers"].append(copy.deepcopy(self.after["handlers"][0]))
        self.assertEqual("UNKNOWN", self.handlers()[0]["status"])

    def test_ambiguous_handlers_do_not_get_speculative_diffs(self):
        duplicate = copy.deepcopy(self.after["objects"][-1])
        duplicate["locator"] = duplicate["locator"].replace("ScreenItems[0]", "ScreenItems[1]")
        self.after["objects"].append(duplicate)
        self.after["handlers"][0]["ScriptCode"] = "unreliable match"
        self.assertEqual("UNKNOWN", self.handlers()[0]["status"])
        self.assertEqual({}, self.handlers()[0]["changed_properties"])

    def test_unknown_enum_preserves_partial_status(self):
        self.after["status"] = "PARTIAL"
        self.after["observations"] = [dict(locator=self.after["handlers"][0]["locator"], code="UNKNOWN_EVENT_VALUE")]
        self.assertEqual("PARTIAL", self.handlers()[0]["status"])

    def test_multiple_screens_items_groups_reordered(self):
        for data in (self.before, self.after):
            for old, new, name in (("ScreenItems[0]", "ScreenItems[1]", "SecondButton"),
                                   ("Screens[0]", "Screens[1]", "SecondScreen"),
                                   ("ScreenGroups[0]", "ScreenGroups[1]", "SecondGroup")):
                originals = copy.deepcopy(data["objects"])
                selected = [r for r in originals if old in r["locator"]]
                for row in selected:
                    row["locator"] = row["locator"].replace(old, new)
                    if row["locator"].endswith(new):
                        row["name"] = name
                    data["objects"].append(row)
        # Reverse traversal order and all composition ordinals together.
        for row in self.after["objects"] + self.after["handlers"]:
            for prop in ("locator", "object_locator"):
                if prop in row:
                    row[prop] = row[prop].replace("[0]", "[9]").replace("[1]", "[0]").replace("[9]", "[1]")
        self.after["objects"].reverse()
        self.assertEqual("UNCHANGED", self.report()["status"])

    def test_empty_handler(self):
        for data in (self.before, self.after):
            h = data["handlers"][0]
            h.update(ScriptCode="", ScriptCode_status="EMPTY", presence="PRESENT_EMPTY")
        self.assertEqual("UNCHANGED", self.handlers()[0]["status"])

    def test_global_partial_keeps_readable_handler(self):
        self.after["status"] = "PARTIAL"
        self.after["objects"][-1]["children_status"] = "UNKNOWN"
        self.after["observations"] = [dict(locator=self.after["objects"][-1]["locator"],
                                           phase="children", code="ENCAPSULATED_CHILDREN_NOT_PUBLIC")]
        self.assertEqual("UNCHANGED", self.handlers()[0]["status"])
        self.assertEqual("PARTIAL", self.report()["status"])
        self.after["handlers"][0]["ScriptCode"] = "changed"
        self.assertEqual("MODIFIED", self.handlers()[0]["status"])

    def test_partial_handlers_never_removed(self):
        self.after["status"] = "PARTIAL"
        self.after["handlers"] = []
        self.after["objects"][-1]["handlers_status"] = "PARTIAL"
        self.assertEqual("PARTIAL", self.handlers()[0]["status"])

    def test_partial_inventory_never_removed(self):
        self.after["objects"] = self.after["objects"][:2]
        self.after["objects"][-1]["children_status"] = "PARTIAL"
        self.after["handlers"] = []
        self.after["status"] = "PARTIAL"
        self.assertEqual("PARTIAL", self.handlers()[0]["status"])
        self.assertEqual(0, self.report()["counts"]["object"]["REMOVED"])

    def test_partial_elsewhere_allows_proven_removal(self):
        self.after["status"] = "PARTIAL"
        self.after["objects"][-1]["children_status"] = "UNKNOWN"
        self.after["objects"][-1]["handlers_status"] = "ABSENT"
        self.after["handlers"] = []
        self.assertEqual("REMOVED", self.handlers()[0]["status"])

    def test_unread_code_is_not_empty(self):
        h = self.after["handlers"][0]
        h.update(ScriptCode=None, ScriptCode_status="READ_FAILED", status="PARTIAL")
        row = self.handlers()[0]
        self.assertEqual("PARTIAL", row["status"])
        self.assertIn("ScriptCode_status", row["changed_properties"])
        self.assertEqual("READ_FAILED", row["read_statuses"]["after"][0]["ScriptCode_status"])

    def test_unknown_event_cannot_be_matched_as_absent(self):
        self.after["handlers"][0]["event_name"] = None
        self.assertEqual({"PARTIAL", "UNKNOWN"}, {r["status"] for r in self.handlers()})

    def test_missing_ancestor_and_name(self):
        for change in ("ancestor", "name", "type"):
            with self.subTest(change=change):
                self.after = copy.deepcopy(self.before)
                if change == "ancestor":
                    self.after["objects"].pop(1)
                else:
                    self.after["objects"][-1]["name" if change == "name" else "object_type"] = None
                self.assertEqual("UNKNOWN", self.report()["status"])
                self.assertNotIn("REMOVED", {r["status"] for r in self.handlers()})

    def test_project_device_binding(self):
        for prop, value in (("project_path", "Other.ap19"), ("device_path", "Other"), ("device_path", None)):
            self.after = copy.deepcopy(self.before)
            self.after["identity"][prop] = value
            self.assertEqual("UNKNOWN", self.report()["status"])
            self.assertNotIn("REMOVED", {r["status"] for r in self.handlers()})

    def test_unicode_crlf_lf_and_whitespace_exact(self):
        original = self.after["handlers"][0]["ScriptCode"]
        for value in (original.replace("\r\n", "\n"), original + " ", original.replace("ż", "z\u0307")):
            self.after["handlers"][0]["ScriptCode"] = value
            self.assertEqual("MODIFIED", self.handlers()[0]["status"])

    def test_unknown_extensions_not_discarded(self):
        self.after["handlers"][0]["vendor_extra"] = dict(flag=1)
        self.assertIn("vendor_extra", self.handlers()[0]["changed_properties"])

    def test_repeat_is_deterministic(self):
        self.assertEqual(self.report(), self.report())

    def test_duplicate_locators_unknown(self):
        self.after["objects"].append(copy.deepcopy(self.after["objects"][-1]))
        self.assertEqual("UNKNOWN", self.report()["status"])

    def test_manifest_and_immutable_runs(self):
        self.after["handlers"][0]["ScriptCode"] = "// 😀\n"
        with tempfile.TemporaryDirectory() as tmp:
            first = tool.publish(self.report(), tmp)
            saved = {p.relative_to(first).as_posix(): p.read_bytes() for p in first.rglob("*") if p.is_file()}
            second = tool.publish(self.report(), tmp)
            self.assertNotEqual(first, second)
            self.assertFalse((first / ".incomplete").exists())
            manifest = json.loads(saved["manifest.json"])
            self.assertEqual(set(saved) - {"manifest.json"}, {r["path"] for r in manifest["artifacts"]})
            for row in manifest["artifacts"]:
                raw = (first / row["path"]).read_bytes()
                self.assertEqual(row["length"], len(raw))
                self.assertEqual(row["sha256"], tool.digest(raw))
                self.assertEqual(saved[row["path"]], raw)
            diff = next((first / "diffs").glob("*.diff")).read_text(encoding="utf-8")
            self.assertIn("\\r\\n", diff)
            self.assertIn("😀", diff)
            self.assertIn("ScriptCode", (first / "comparison-report.md").read_text(encoding="utf-8"))

    def test_failed_publication_keeps_incomplete(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(tool, "markdown", side_effect=OSError("synthetic disk failure")):
                with self.assertRaises(OSError):
                    tool.publish(self.report(), tmp)
            self.assertEqual(1, len(list(Path(tmp).rglob(".incomplete"))))

    def test_cli_paths_and_invalid_inputs(self):
        with tempfile.TemporaryDirectory(prefix="compare ' Unicode ą ") as tmp:
            root = Path(tmp)
            b, a = root / "before events.json", root / "after events.json"
            b.write_bytes(tool.encode(self.before))
            a.write_bytes(tool.encode(self.after))
            command = [sys.executable, "-B", tool.__file__, "compare-events", "--before", str(b),
                       "--after", str(a), "--output", str(root / "reports")]
            result = subprocess.run(command, capture_output=True)
            self.assertEqual(0, result.returncode, result.stderr)
            a.write_bytes(b'{"schema_version":"bad"}')
            self.assertEqual(1, subprocess.run(command, capture_output=True).returncode)
            self.assertEqual(1, len(list((root / "reports" / "EventComparisons").iterdir())))

    def test_cli_partial_exit(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.after["status"] = "PARTIAL"
            for name, data in (("before", self.before), ("after", self.after)):
                (root / (name + ".json")).write_bytes(tool.encode(data))
            self.assertEqual(2, tool.main(["compare-events", "--before", str(root / "before.json"),
                                          "--after", str(root / "after.json"), "--output", tmp]))

    def test_reject_bad_json_duplicate_depth_unicode_size(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "events.json"
            for raw in (b"{}", b'{"a":1,"a":2}', b"[" * 65 + b"]" * 65,
                        tool.encode(self.before).replace(b"Tapped", b"\\ud800"), b"not json", b"\xff"):
                with self.subTest(raw=raw[:15]):
                    path.write_bytes(raw)
                    with self.assertRaises((ValueError, UnicodeError)):
                        tool.load_export(path)
            path.write_bytes(tool.encode(self.before))
            with patch.object(tool, "MAX_BYTES", 8):
                with self.assertRaises(ValueError):
                    tool.load_export(path)

    def test_legacy_launch_configurations_unchanged(self):
        root = Path(__file__).resolve().parents[1]
        old = json.loads(subprocess.check_output(["git", "show", "HEAD:.vscode/launch.json"], cwd=root))
        new = json.loads((root / ".vscode/launch.json").read_text(encoding="utf-8"))
        self.assertEqual(old["configurations"], new["configurations"][:len(old["configurations"])])
        self.assertEqual(1, sum("COMPARE EVENTS" in c["name"] for c in new["configurations"]))


if __name__ == "__main__":
    unittest.main(verbosity=2)
