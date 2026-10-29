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


def lab03k_fixture():
    """Synthetic values with the actual LAB-03K array binding and direct Screens edge."""
    data = fixture()
    data["identity"]["device_path"] = ["HMI_JSON_TEST", "HMI_RT_1"]
    data["objects"] = [r for r in data["objects"] if r["kind"] != "GROUP"]
    for row in data["objects"] + data["handlers"]:
        for prop in ("locator", "object_locator"):
            if prop in row:
                row[prop] = row[prop].replace("/HMI/Device/Runtime", "/HMI/HMI_JSON_TEST/HMI_RT_1").replace("/ScreenGroups[0]", "")
    return data


def full_lab03k_fixture():
    """211 synthetic objects / 48 handlers; no source copied from the real exports."""
    data = lab03k_fixture()
    software, screen, item = data["objects"]
    handler = data["handlers"][0]
    data["objects"], data["handlers"] = [software], []
    for number in range(7):
        row = dict(screen, locator=software["locator"] + f"/Screens[{number}]", name=f"Screen_{number}")
        data["objects"].append(row)
        for index in range(29):
            obj = dict(item, locator=row["locator"] + f"/ScreenItems[{index}]", name=f"Item_{index}", handlers_status="ABSENT")
            data["objects"].append(obj)
            if len(data["handlers"]) < 48:
                obj["handlers_status"] = "ENUMERATED"
                data["handlers"].append(dict(handler, locator=obj["locator"] + "/EventHandlers[0]", object_locator=obj["locator"], object_name=obj["name"]))
    data["status"] = "PARTIAL"
    for obj in [r for r in data["objects"] if r["kind"] == "ITEM"][-3:]:
        obj["children_status"] = "UNKNOWN"
        data["observations"].append(dict(locator=obj["locator"], phase="children", code="ENCAPSULATED_CHILDREN_NOT_PUBLIC"))
    return data


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

    def test_lab03k_array_device_and_direct_screens_match_all_211_48(self):
        self.before = full_lab03k_fixture()
        self.after = copy.deepcopy(self.before)
        report = self.report()
        self.assertEqual(dict(object=211, handler=48), report["matched_counts"])
        self.assertEqual(208, report["counts"]["object"]["UNCHANGED"])
        self.assertEqual(3, report["counts"]["object"]["PARTIAL"])
        self.assertEqual(48, report["counts"]["handler"]["UNCHANGED"])
        self.assertEqual("PARTIAL", report["status"])
        self.assertFalse(any(e["status"] == "UNKNOWN" for e in report["entries"]))

    def test_lab03k_reorder_screens_and_items_independently(self):
        self.before = full_lab03k_fixture()
        for field in ("Screens", "ScreenItems"):
            with self.subTest(field=field):
                self.after = copy.deepcopy(self.before)
                for row in self.after["objects"] + self.after["handlers"] + self.after["observations"]:
                    for prop in ("locator", "object_locator"):
                        if prop in row:
                            row[prop] = row[prop].replace(field + "[0]", field + "[999]").replace(field + "[1]", field + "[0]").replace(field + "[999]", field + "[1]")
                self.after["objects"].reverse()
                self.after["handlers"].reverse()
                self.assertEqual(dict(object=211, handler=48), self.report()["matched_counts"])
                self.assertEqual(0, self.report()["counts"]["handler"]["MODIFIED"])
                self.assertEqual(48, self.report()["counts"]["handler"]["UNCHANGED"])

    def test_lab03k_code_global_and_async_changes_are_detected(self):
        self.before = lab03k_fixture()
        for prop, value in (("ScriptCode", "changed\r\n"), ("GlobalDefinitionAreaScriptCode", "import * as m from 'Changed';\n"), ("async", True)):
            self.after = copy.deepcopy(self.before)
            self.after["handlers"][0][prop] = value
            entry = self.handlers()[0]
            self.assertEqual("MODIFIED", entry["status"])
            self.assertEqual(value, entry["changed_properties"][prop]["after"])

    def test_lab03k_array_binding_and_encoded_string_compatibility(self):
        self.before = lab03k_fixture()
        self.after = copy.deepcopy(self.before)
        self.after["identity"]["device_path"] = "HMI_JSON_TEST/HMI_RT_1"
        self.assertEqual("UNCHANGED", self.report()["status"])

    def test_device_segments_with_encoded_slashes_percent_and_unicode(self):
        self.before = lab03k_fixture()
        self.before["identity"]["device_path"] = ["Device/A%", "Runtime_日本"]
        old_root = "/HMI/HMI_JSON_TEST/HMI_RT_1"
        new_root = "/HMI/Device%2FA%25/Runtime_%E6%97%A5%E6%9C%AC"
        for row in self.before["objects"] + self.before["handlers"]:
            for prop in ("locator", "object_locator"):
                if prop in row:
                    row[prop] = row[prop].replace(old_root, new_root)
        self.after = copy.deepcopy(self.before)
        self.after["identity"]["device_path"] = new_root[len("/HMI/"):]
        self.assertEqual("UNCHANGED", self.report()["status"])
        self.after["identity"]["device_path"] = ["Device", "A%/Runtime_日本"]
        self.assertEqual("UNKNOWN", self.report()["status"])

    def test_invalid_array_or_device_escape_never_confirms_binding(self):
        self.before = lab03k_fixture()
        for device in ([], ["one"], ["Device", None], [["Device"], "Runtime"], ["Device", " "], {}, "Device%zz/Runtime", "Device%ff/Runtime"):
            with self.subTest(device=device):
                self.after = copy.deepcopy(self.before)
                self.after["identity"]["device_path"] = device
                self.assertEqual("UNKNOWN", self.report()["status"])
                self.assertEqual(dict(object=0, handler=0), self.report()["matched_counts"])

    def test_root_locator_must_agree_with_device_segments(self):
        self.before = lab03k_fixture()
        self.after = copy.deepcopy(self.before)
        for row in self.after["objects"] + self.after["handlers"]:
            for prop in ("locator", "object_locator"):
                if prop in row:
                    row[prop] = row[prop].replace("/HMI/HMI_JSON_TEST/", "/HMI/Other/")
        report = self.report()
        self.assertEqual(dict(object=0, handler=0), report["matched_counts"])
        self.assertIn("ROOT_DEVICE_BINDING_MISMATCH", {e.get("identity_issue") for e in report["entries"]})

    def test_lab03k_group_subgroup_nested_items_and_duplicate_names(self):
        self.before = lab03k_fixture()
        root, screen, item = self.before["objects"]
        group_loc = root["locator"] + "/ScreenGroups[0]"
        subgroup_loc = group_loc + "/Groups[0]"
        for row in (screen, item, self.before["handlers"][0]):
            for prop in ("locator", "object_locator"):
                if prop in row:
                    row[prop] = row[prop].replace(root["locator"], subgroup_loc, 1)
        self.before["objects"].extend([dict(screen, kind="GROUP", locator=group_loc, name="Area"), dict(screen, kind="GROUP", locator=subgroup_loc, name="Cell")])
        self.before["objects"].append(dict(item, locator=item["locator"] + "/ScreenItems[0]", name="Nested"))
        self.after = copy.deepcopy(self.before)
        self.assertEqual("UNCHANGED", self.report()["status"])
        self.after["objects"].append(dict(item, locator=item["locator"].replace("ScreenItems[0]", "ScreenItems[1]")))
        self.assertEqual("UNKNOWN", self.handlers()[0]["status"])
        self.assertEqual({}, self.handlers()[0]["changed_properties"])

    def test_same_indices_do_not_match_a_renamed_item(self):
        self.before = lab03k_fixture()
        self.after = copy.deepcopy(self.before)
        self.after["objects"][-1]["name"] = "Renamed"
        report = self.report()
        self.assertEqual(dict(object=2, handler=0), report["matched_counts"])
        self.assertEqual({"ADDED", "REMOVED"}, {e["status"] for e in report["entries"] if e["kind"] == "handler"})

    def test_lab03k_missing_item_requires_complete_parent_scope(self):
        self.before = lab03k_fixture()
        self.after = copy.deepcopy(self.before)
        self.after["objects"].pop()
        self.after["handlers"] = []
        self.assertEqual("REMOVED", self.handlers()[0]["status"])
        self.after["objects"][-1]["children_status"] = "UNKNOWN"
        self.after["status"] = "PARTIAL"
        self.assertEqual("PARTIAL", self.handlers()[0]["status"])
        self.assertEqual(0, self.report()["counts"]["object"]["REMOVED"])

    def test_invalid_collection_locators_are_unknown_not_guessed(self):
        self.before = lab03k_fixture()
        original = self.before["objects"][-1]["locator"]
        for invalid in (original + "/", original.replace("[0]", "[-1]"), original.replace("ScreenItems[0]", "ScreenItems[x]"),
                        original.replace("ScreenItems", "Items"), original.replace("ScreenItems", "Screens"),
                        original.replace("ScreenItems[0]", "ScreenItems[01]"), original.replace("ScreenItems[0]", "ScreenItems[2147483648]")):
            with self.subTest(locator=invalid):
                self.after = copy.deepcopy(self.before)
                self.after["objects"][-1]["locator"] = invalid
                self.after["handlers"][0]["object_locator"] = invalid
                self.after["handlers"][0]["locator"] = invalid + "/EventHandlers[0]"
                report = self.report()
                self.assertEqual("UNKNOWN", report["status"])
                self.assertEqual(0, report["matched_counts"]["handler"])
                self.assertFalse(any(e["status"] == "REMOVED" for e in report["entries"]))

    def test_handler_locator_cannot_claim_another_owner(self):
        self.before = lab03k_fixture()
        self.after = copy.deepcopy(self.before)
        self.after["handlers"][0]["locator"] = self.after["objects"][-2]["locator"] + "/EventHandlers[0]"
        report = self.report()
        self.assertEqual(0, report["matched_counts"]["handler"])
        self.assertIn("HANDLER_LOCATOR_INVALID", {e.get("identity_issue") for e in report["entries"]})
        for owner in (None, ["not a locator"], 12):
            with self.subTest(owner=owner):
                self.after = copy.deepcopy(self.before)
                self.after["handlers"][0]["object_locator"] = owner
                report = self.report()
                self.assertEqual("UNKNOWN", report["status"])
                self.assertFalse(any(e["status"] == "REMOVED" for e in report["entries"]))

    def test_ambiguous_examples_bound_locators_but_keep_detailed_json(self):
        self.before = lab03k_fixture()
        item = self.before["objects"][-1]
        for index in range(1, 100):
            self.before["objects"].append(dict(item, locator=item["locator"].replace("ScreenItems[0]", f"ScreenItems[{index}]")))
        self.after = copy.deepcopy(self.before)
        report = self.report()
        entry = next(e for e in report["entries"] if len(e["before_locators"]) == 100)
        example = next(ex for g in report["diagnostic_summary"] for ex in g["examples"] if ex["before_locator_count"] == 100)
        self.assertEqual(100, len(entry["after_locators"]))
        self.assertEqual(3, len(example["before_locators"]))
        self.assertEqual(3, len(example["after_locators"]))
        self.assertIn("before(100)", tool.markdown(report).decode("utf-8"))

    def test_compact_unknown_summary_keeps_all_json_records_and_manifest(self):
        self.before = full_lab03k_fixture()
        for row in self.before["objects"]:
            row["name"] = None
        self.after = copy.deepcopy(self.before)
        report = self.report()
        self.assertEqual(518, len(report["entries"]))
        self.assertEqual(518, sum(g["count"] for g in report["diagnostic_summary"]))
        self.assertTrue(all(len(g["examples"]) <= 3 for g in report["diagnostic_summary"]))
        with tempfile.TemporaryDirectory() as tmp:
            target = tool.publish(report, tmp)
            saved = json.loads((target / "comparison.json").read_bytes())
            self.assertEqual(report["entries"], saved["entries"])
            text = (target / "comparison-report.md").read_text(encoding="utf-8")
            self.assertLess(len(text), 10000)
            self.assertLess(text.count("INCOMPLETE"), 20)
            self.assertIn("UNKNOWN means", text)
            self.assertIn("PARTIAL retains incomplete coverage", text)
            for group in saved["diagnostic_summary"]:
                for example in group["examples"]:
                    entry = saved["entries"][int(example["entry_ref"].rsplit("/", 1)[1])]
                    self.assertEqual(entry["before_locators"], example["before_locators"])
            manifest = json.loads((target / "manifest.json").read_bytes())
            self.assertEqual({"comparison.json", "comparison-report.md"}, {f["path"] for f in manifest["artifacts"]})
            for artifact in manifest["artifacts"]:
                raw = (target / artifact["path"]).read_bytes()
                self.assertEqual(len(raw), artifact["length"])
                self.assertEqual(tool.digest(raw), artifact["sha256"])
            self.assertFalse((target / ".incomplete").exists())

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
