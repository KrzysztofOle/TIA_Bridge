"""Offline LAB-03K comparison. Python 3.12 standard library; never executes JS.

CLI: py -3 scripts/compare_events.py compare-events --before events.json
     --after other/events.json --output .tia-bridge
"""

import argparse
from collections import defaultdict
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
import uuid


STATUSES = ("UNCHANGED", "ADDED", "REMOVED", "MODIFIED", "UNKNOWN", "PARTIAL")
MAX_BYTES = 64 * 1024 * 1024
MAX_DEPTH = 64
MAX_ROWS = 16384
TEXT_FIELDS = ("ScriptCode", "GlobalDefinitionAreaScriptCode")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON property")
        result[key] = value
    return result


def load_export(path):
    path = Path(path).resolve(strict=True)
    with path.open("rb") as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError("Input exceeds 64 MiB")
    # Bound nesting before the JSON decoder allocates recursive structures.
    depth, quoted, escaped = 0, False, False
    for ch in raw:
        if quoted:
            if escaped:
                escaped = False
            elif ch == 92:
                escaped = True
            elif ch == 34:
                quoted = False
        elif ch == 34:
            quoted = True
        elif ch in (91, 123):
            depth += 1
            if depth > MAX_DEPTH:
                raise ValueError("Input exceeds JSON depth 64")
        elif ch in (93, 125):
            depth -= 1
    data = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique_pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Non-finite JSON")))
    if not isinstance(data, dict) or data.get("schema_version") != "lab-03k-events/1.0":
        raise ValueError("Unsupported LAB-03K schema")
    if data.get("status") not in ("PARTIAL", "COMPLETE_FOR_PUBLIC_SCOPE"):
        raise ValueError("Invalid export status")
    if not isinstance(data.get("identity"), dict):
        raise ValueError("Missing export identity")
    for field in ("objects", "handlers", "observations"):
        rows = data.get(field)
        if not isinstance(rows, list) or len(rows) > MAX_ROWS or any(not isinstance(r, dict) for r in rows):
            raise ValueError("Invalid or oversized " + field)
    # Also reject escaped lone surrogates, including in unknown extension fields.
    encode(data)
    return Snapshot(data, {"path": str(path), "length": len(raw), "sha256": digest(raw),
                           "identity": data["identity"], "status": data["status"],
                           "coverage_scope": data.get("coverage_scope"),
                           "metadata": {k: v for k, v in data.items() if k not in ("objects", "handlers", "observations")},
                           "observations": data["observations"]})


def nonempty(value):
    return isinstance(value, str) and bool(value)


class Snapshot:
    def __init__(self, data, source):
        self.data, self.source = data, source
        self.locators = defaultdict(list)
        self.objects, self.handlers = defaultdict(list), defaultdict(list)
        self.keys, self.bad = {}, []
        for row in data["objects"]:
            if nonempty(row.get("locator")):
                self.locators[row["locator"]].append(row)
        for row in data["objects"]:
            key = self.object_key(row, set())
            if key is None:
                self.bad.append(("object", row))
            else:
                self.objects[key].append(row)
        for row in data["handlers"]:
            owners = self.locators.get(row.get("object_locator"), [])
            key = self.keys.get(id(owners[0])) if len(owners) == 1 else None
            if key is None or not all(nonempty(row.get(p)) for p in ("event_name", "event_type_type")):
                self.bad.append(("handler", row))
            else:
                self.handlers[key + (("EVENT", row["event_name"]),)].append(row)

    def object_key(self, row, visiting):
        if id(row) in self.keys:
            return self.keys[id(row)]
        locator = row.get("locator")
        if (not nonempty(locator) or len(self.locators[locator]) != 1 or locator in visiting
                or len(visiting) >= MAX_DEPTH or not nonempty(row.get("name"))
                or not nonempty(row.get("object_type")) or row.get("kind") not in ("SOFTWARE", "GROUP", "SCREEN", "ITEM")):
            return None
        parent_locator = locator.rsplit("/", 1)[0]
        parents = self.locators.get(parent_locator, [])
        if row["kind"] == "SOFTWARE":
            device = self.data["identity"].get("device_path")
            key = (("DEVICE", device),) if nonempty(device) and not parents else None
        elif len(parents) == 1:
            parent = self.object_key(parents[0], visiting | {locator})
            key = parent + ((row["kind"], row["name"]),) if parent else None
        else:
            key = None
        self.keys[id(row)] = key
        return key

    def ambiguous(self, key):
        return any(len(self.objects.get(key[:i], [])) > 1 for i in range(1, len(key) + 1))

    def absence_known(self, key, handler):
        """A complete nearest ancestor proves absence only in its observed subtree."""
        object_key = key[:-1] if handler else key
        if any(kind == "object" for kind, _ in self.bad):
            return False
        if any(kind == "handler" and row.get("object_locator") not in self.locators
               for kind, row in self.bad):
            return False
        if handler and object_key in self.objects:
            owners = self.objects[object_key]
            if len(owners) != 1 or self.ambiguous(object_key):
                return False
            owner = owners[0]
            if any(kind == "handler" and row.get("object_locator") == owner.get("locator")
                   for kind, row in self.bad):
                return False
            return owner.get("handlers_status") in ("ENUMERATED", "ABSENT")
        for size in range(len(object_key) - 1, 0, -1):
            ancestors = self.objects.get(object_key[:size], [])
            if ancestors:
                return (len(ancestors) == 1 and not self.ambiguous(object_key[:size])
                        and ancestors[0].get("children_status") == "COMPLETE_FOR_PUBLIC_SCOPE")
        return False


def changed_properties(before, after):
    excluded = {"locator", "object_locator", "object_name"}
    excluded |= {p + suffix for p in TEXT_FIELDS for suffix in ("_path", "_sha256")}
    changes = {}
    for prop in sorted((before.keys() | after.keys()) - excluded):
        if (prop in before) != (prop in after) or before.get(prop) != after.get(prop):
            changes[prop] = {"before_present": prop in before, "after_present": prop in after,
                             "before": before.get(prop), "after": after.get(prop)}
    return changes


def readable(row):
    return (row.get("status") == "READ" and row.get("presence") in ("PRESENT", "PRESENT_EMPTY")
            and row.get("async_status") == "READ" and type(row.get("async")) is bool
            and all(row.get(p + "_status") in ("READ", "EMPTY") and isinstance(row.get(p), str)
                    and (row[p] == "") == (row[p + "_status"] == "EMPTY") for p in TEXT_FIELDS)
            and all(nonempty(row.get(p)) for p in ("object_type", "handler_type", "event_type", "event_type_type")))


def compare(before, after):
    result = []
    binding = all(nonempty(s.data["identity"].get(p)) for s in (before, after)
                  for p in ("project_path", "device_path")) and all(
        before.data["identity"][p] == after.data["identity"][p] for p in ("project_path", "device_path"))
    for handler in (False, True):
        left, right = (before.handlers, after.handlers) if handler else (before.objects, after.objects)
        for key in sorted(left.keys() | right.keys()):
            b, a = left.get(key, []), right.get(key, [])
            changes = changed_properties(b[0], a[0]) if len(b) == len(a) == 1 else {}
            reason = "EXACT_OBSERVED_PROPERTIES"
            if not binding:
                status, reason = "UNKNOWN", "PROJECT_OR_DEVICE_BINDING_UNCONFIRMED"
                changes = {}
            elif len(b) > 1 or len(a) > 1 or before.ambiguous(key) or after.ambiguous(key):
                status, reason = "UNKNOWN", "AMBIGUOUS_LOGICAL_IDENTITY"
                changes = {}
            elif not b or not a:
                missing = before if not b else after
                if missing.absence_known(key, handler):
                    status, reason = ("ADDED" if not b else "REMOVED"), "ABSENCE_PROVEN_IN_PUBLIC_SCOPE"
                else:
                    status, reason = "PARTIAL", "ABSENCE_NOT_PROVEN"
            elif handler and (not (readable(b[0]) and readable(a[0])) or any(
                    o.get("locator") == rows[0].get("locator") and o.get("code") == "UNKNOWN_EVENT_VALUE"
                    for snap, rows in ((before, b), (after, a)) for o in snap.data["observations"])):
                status, reason = "PARTIAL", "HANDLER_PROPERTIES_UNREAD"
            elif not handler and any(r.get("children_status") != "COMPLETE_FOR_PUBLIC_SCOPE"
                                      or r.get("handlers_status") not in ("ABSENT", "ENUMERATED", "NOT_APPLICABLE") for r in (b[0], a[0])):
                status, reason = "PARTIAL", "OBJECT_COVERAGE_INCOMPLETE"
            else:
                status = "MODIFIED" if changes else "UNCHANGED"
            result.append({"kind": "handler" if handler else "object", "identity": key,
                           "status": status, "reason": reason,
                           "before_locators": [r.get("locator") for r in b],
                           "after_locators": [r.get("locator") for r in a],
                           "read_statuses": {side: [{p: v for p, v in row.items()
                                                    if p.endswith("_status") or p in ("status", "presence")}
                                                   for row in rows]
                                             for side, rows in (("before", b), ("after", a))},
                           "changed_properties": changes})
    for side, snapshot in (("before", before), ("after", after)):
        for kind, row in snapshot.bad:
            result.append({"kind": kind, "identity": None, "status": "UNKNOWN",
                           "reason": "INCOMPLETE_LOGICAL_IDENTITY", "side": side,
                           "before_locators": [row.get("locator")] if side == "before" else [],
                           "after_locators": [row.get("locator")] if side == "after" else [],
                           "changed_properties": {}})
    counts = {kind: {s: sum(r["kind"] == kind and r["status"] == s for r in result) for s in STATUSES}
              for kind in ("object", "handler")}
    coverage = any(s.data["status"] == "PARTIAL" for s in (before, after))
    overall = ("UNKNOWN" if any(r["status"] == "UNKNOWN" for r in result) or not binding else
               "PARTIAL" if coverage or any(r["status"] == "PARTIAL" for r in result) else
               "MODIFIED" if any(r["status"] != "UNCHANGED" for r in result) else "UNCHANGED")
    return {"schema_version": "lab-events-comparison/1.0", "status": overall,
            "execution_evidence": "OFFLINE", "engineering_qualification": "NOT_RUN",
            "before": before.source, "after": after.source, "counts": counts,
            "coverage_partial": coverage, "entries": result,
            "reliable_scope": "Unique logical identities and independently readable properties; absence only under complete observed owners/ancestors. Public LAB-03K scope only."}


def md(value):
    # Prevent names/paths from becoming Markdown links, images, HTML or table cells.
    entities = {ord(ch): "&#" + str(ord(ch)) + ";" for ch in "&<>|`\\[]!*_#()"}
    return str(value).translate(entities).replace("\r", "\\r").replace("\n", "\\n")


def markdown(report):
    lines = ["# HMI event comparison", "", "Status: **" + report["status"] + "**. OFFLINE; Engineering NOT_RUN.", "",
             "Before: " + md(report["before"]["path"]), "", "After: " + md(report["after"]["path"]), "",
             "| Kind | " + " | ".join(STATUSES) + " |", "|---|" + "---|" * len(STATUSES)]
    for kind, counts in report["counts"].items():
        lines.append("| " + kind + " | " + " | ".join(str(counts[s]) for s in STATUSES) + " |")
    lines += ["", "Coverage partial: " + str(report["coverage_partial"]), "", report["reliable_scope"], "",
              "Export limitations and observations are retained in comparison.json."]
    groups = defaultdict(list)
    for entry in report["entries"]:
        key = entry["identity"] or []
        screen = " / ".join(name for kind, name in key if kind in ("DEVICE", "GROUP", "SCREEN")) or "Unresolved identity"
        groups[screen].append(entry)
    for screen, entries in sorted(groups.items()):
        lines += ["", "## " + md(screen), "", "| Kind / logical path | Status | Changed properties | Reason |", "|---|---|---|---|"]
        for e in entries:
            logical = " / ".join(name for _, name in (e["identity"] or []))
            lines.append("| " + md(e["kind"] + " / " + logical) + " | " + e["status"] + " | "
                         + md(", ".join(e["changed_properties"])) + " | " + e["reason"] + " |")
        for e in entries:
            if e["changed_properties"]:
                lines += ["", "Changes at " + md(", ".join(e["after_locators"] or e["before_locators"])) + ":", ""]
                for prop, change in e["changed_properties"].items():
                    if prop not in TEXT_FIELDS:
                        lines.append("- " + md(prop) + ": " + md(json.dumps(change["before"], ensure_ascii=False))
                                     + " → " + md(json.dumps(change["after"], ensure_ascii=False)))
            if e.get("diff"):
                lines += ["", "Diff: [" + e["diff"] + "](" + e["diff"] + ")", ""]
    return ("\n".join(lines) + "\n").encode("utf-8")


def code_diff(entry):
    # A full replacement hunk is deliberate: bounded linear time even for hostile JS.
    # JSON-quoted lines expose CR/LF, tabs and missing final newlines without normalization.
    lines = ["Exact text diff; each +/- line is a JSON string including original EOL."]
    for prop in TEXT_FIELDS:
        change = entry["changed_properties"].get(prop)
        if not change:
            continue
        lines += ["--- before/" + prop, "+++ after/" + prop]
        for side, prefix in (("before", "-"), ("after", "+")):
            value = change[side]
            if isinstance(value, str):
                lines += [prefix + json.dumps(part, ensure_ascii=False) for part in value.splitlines(keepends=True)]
                if value == "":
                    lines.append(prefix + '""')
            else:
                lines.append(prefix + "<UNREAD_OR_ABSENT>")
    return ("\n".join(lines) + "\n").encode("utf-8")


def publish(report, output):
    root = Path(output).resolve() / "EventComparisons"
    root.mkdir(parents=True, exist_ok=True)
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex
    target = root / run_id
    target.mkdir()  # Exclusive claim; never reuse an existing run.
    marker = target / ".incomplete"
    marker.write_bytes(b"INCOMPLETE\n")
    (target / "diffs").mkdir()
    report["run_id"] = run_id
    artifacts = {}

    def write(name, content):
        if sum(a["length"] for a in artifacts.values()) + len(content) > 256 * 1024 * 1024:
            raise ValueError("Report exceeds 256 MiB")
        with (target / name).open("xb") as stream:
            stream.write(content)
        artifacts[name] = {"path": name, "length": len(content), "sha256": digest(content)}

    for index, entry in enumerate(report["entries"]):
        if entry["kind"] == "handler" and any(p in entry["changed_properties"] for p in TEXT_FIELDS):
            name = "diffs/" + str(index).zfill(5) + ".diff"
            entry["diff"] = name
            write(name, code_diff(entry))
    write("comparison.json", encode(report))
    write("comparison-report.md", markdown(report))
    manifest = {"schema_version": "lab-events-comparison-manifest/1.0", "run_id": run_id,
                "excluded": ["manifest.json", ".incomplete"], "artifacts": list(artifacts.values())}
    with (target / "manifest.json").open("xb") as stream:
        stream.write(encode(manifest))
    for row in artifacts.values():
        raw = (target / row["path"]).read_bytes()
        if len(raw) != row["length"] or digest(raw) != row["sha256"]:
            raise OSError("Artifact verification failed")
    if json.loads((target / "manifest.json").read_bytes()) != manifest:
        raise OSError("Manifest verification failed")
    marker.unlink()
    return target


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    command = commands.add_parser("compare-events", help="Compare existing LAB-03K JSON files offline")
    for name in ("before", "after", "output"):
        command.add_argument("--" + name, required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        before, after = load_export(args.before), load_export(args.after)
        report = compare(before, after)
        target = publish(report, args.output)
        print(str(target))
        print(report["status"])
        return 2 if report["status"] in ("PARTIAL", "UNKNOWN") else 0
    except (OSError, ValueError, TypeError, RecursionError) as error:
        print("compare-events: " + type(error).__name__ + ": " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
