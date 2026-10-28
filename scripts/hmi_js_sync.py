#!/usr/bin/env python3
"""Package existing WinCC Unified script export files for Git comparison.

This tool never connects to TIA and never interprets Siemens JavaScript/YAML.
Input is an operator-provided inventory JSON; exports are copied byte-for-byte.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import unicodedata

SCHEMA = "tia-bridge-hmi-js-manifest/1.0"
STATUSES = {"UNCHANGED", "MODIFIED", "ADDED", "MISSING", "UNSUPPORTED", "UNKNOWN"}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def safe_segment(name: str, identity: str) -> str:
    if not name or name in {".", ".."} or "\x00" in name:
        raise ValueError("empty or unsafe object name")
    normalized = unicodedata.normalize("NFC", name)
    escaped = "".join(c if (c.isalnum() and c != "%") or c in "._-" else "".join("%%%02X" % value for value in c.encode("utf-8"))
                      for c in normalized)
    escaped = escaped.rstrip(" .") or "_"
    if escaped.split(".")[0].upper() in {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}:
        escaped = "_" + escaped
    identity_tag = hashlib.sha256(identity.encode("utf-8")).hexdigest()[:12]
    return f"{escaped[:72]}--{identity_tag}"


def _relative(root: Path, raw: str) -> Path:
    candidate = Path(raw)
    if candidate.is_absolute():
        raise ValueError("inventory artifact paths must be relative to inventory root")
    resolved = (root / candidate).resolve(strict=True)
    base = root.resolve(strict=True)
    if os.path.commonpath((str(base), str(resolved))) != str(base) or not resolved.is_file():
        raise ValueError("artifact escapes input root or is not a file")
    return resolved


def load_inventory(path: Path) -> dict:
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict) or set(raw) != {"project_identity", "hmi_device_identity", "modules"}:
        raise ValueError("inventory requires exactly project_identity, hmi_device_identity, modules")
    if not isinstance(raw["modules"], list):
        raise ValueError("modules must be an array")
    return raw


def package(inventory_path: Path, destination: Path) -> dict:
    inventory = load_inventory(inventory_path)
    root = inventory_path.parent
    destination = destination.resolve()
    if destination.exists() and any(destination.iterdir()):
        raise FileExistsError("destination must be absent or empty; existing data is never overwritten")
    destination.mkdir(parents=True, exist_ok=True)
    entries, seen_ids, seen_paths = [], set(), set()
    for item in inventory["modules"]:
        if not isinstance(item, dict) or "identity" not in item or "name" not in item:
            raise ValueError("each module needs identity and name")
        identity, name = str(item["identity"]), str(item["name"])
        if not identity or identity in seen_ids:
            raise ValueError("module identity is empty or duplicated")
        seen_ids.add(identity)
        native_members = item.get("native_artifacts", [])
        unsupported_reason = item.get("unsupported_reason")
        if not isinstance(native_members, list) or (not native_members and not unsupported_reason):
            raise ValueError("module without native artifacts requires unsupported_reason")
        entry = {"identity": identity, "name": name, "locator": item.get("locator"), "kind": item.get("kind", "GLOBAL_SCRIPT_MODULE"),
                 "functions": item.get("functions", []), "dependencies": item.get("dependencies", []),
                 "status": "UNSUPPORTED" if unsupported_reason else "EXPORTED",
                 "unsupported_reason": unsupported_reason, "native_artifacts": [],
                 "source": None, "source_sha256": None, "coverage": "PARTIAL"}
        base = f"Scripts/modules/{safe_segment(name, identity)}"
        for member in native_members:
            source = _relative(root, member)
            rel = f"Scripts/native/{safe_segment(name, identity)}/{source.name}"
            collision_key = rel.casefold()
            if collision_key in seen_paths:
                raise ValueError("case-insensitive output path collision")
            seen_paths.add(collision_key)
            out = destination / rel
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, out)
            entry["native_artifacts"].append({"path": rel, "sha256": sha256(out), "bytes": out.stat().st_size,
                                              "role": "NATIVE", "original_name": source.name})
        # SOURCE is a separate explicit input; never derive it from NATIVE.
        if item.get("source_artifact") is not None:
            source = _relative(root, item["source_artifact"])
            source_rel = f"{base}/source.js"
            out_source = destination / source_rel
            out_source.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, out_source)
            entry["source"] = source_rel
            entry["source_sha256"] = sha256(out_source)
        for key in ("functions", "dependencies"):
            if not isinstance(entry[key], list):
                raise ValueError(f"{key} must be an array")
        entries.append(entry)
    manifest = {"schema_version": SCHEMA, "project_identity": inventory["project_identity"],
                "hmi_device_identity": inventory["hmi_device_identity"], "source_status": "NOT_ENGINEERING_VERIFIED",
                "coverage": "PARTIAL", "complete": False, "modules": sorted(entries, key=lambda x: x["identity"])}
    path = destination / "Scripts/manifest.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8", newline="\n")
    return manifest


def compare(previous: dict, current: dict) -> list[dict]:
    left = {x["identity"]: x for x in previous.get("modules", [])}
    right = {x["identity"]: x for x in current.get("modules", [])}
    result = []
    for identity in sorted(left.keys() | right.keys()):
        a, b = left.get(identity), right.get(identity)
        if b is None:
            status = "MISSING"  # observation only; never a TIA delete instruction
        elif b.get("unsupported_reason") or b.get("status") == "UNSUPPORTED":
            status = "UNSUPPORTED"
        elif a is None:
            status = "ADDED"
        elif any(a.get(field) != b.get(field) for field in ("name", "locator", "kind", "native_artifacts", "source_sha256", "functions", "dependencies")):
            status = "MODIFIED"
        elif a.get("coverage") != "COMPLETE" or b.get("coverage") != "COMPLETE":
            status = "UNKNOWN"
        else:
            status = "UNCHANGED"
        result.append({"identity": identity, "status": status, "action": "OBSERVATION_ONLY"})
    return result


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    pack_parser = sub.add_parser("pack")
    pack_parser.add_argument("inventory", type=Path)
    pack_parser.add_argument("destination", type=Path)
    diff_parser = sub.add_parser("compare")
    diff_parser.add_argument("previous", type=Path)
    diff_parser.add_argument("current", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "pack":
            package(args.inventory, args.destination)
        else:
            old = json.loads(args.previous.read_text(encoding="utf-8"))
            new = json.loads(args.current.read_text(encoding="utf-8"))
            print(json.dumps(compare(old, new), ensure_ascii=False, sort_keys=True, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f"hmi-js-sync: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
