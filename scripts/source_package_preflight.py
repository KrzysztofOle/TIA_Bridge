"""Read-only source-package preflight. Python 3.12 + existing jsonschema QA dependency.

No package code is imported or executed. See docs/OFFLINE_SOURCE_PREFLIGHT.md.
"""
from __future__ import annotations

import sys

# The command's only filesystem write is an explicitly requested report.
sys.dont_write_bytecode = True

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import unicodedata

SCHEMAS = Path(__file__).resolve().parents[1] / "contracts/source-package/v1"
VERSION = "tia-bridge.source-package/1.0"
CHECKS = ("manifest.schema", "manifest.files", "files.sha256", "files.encoding", "files.eol")
MAX_MANIFEST = 1024 * 1024
MAX_FILE = 16 * 1024 * 1024
MAX_TOTAL = 256 * 1024 * 1024
MAX_ENTRIES = 4096
MAX_DEPTH = 16


class Rejected(Exception):
    def __init__(self, code, path=None, exit_code=2):
        self.code, self.path, self.exit_code = code, path, exit_code


def issue(code, path=None, expected=None, actual=None):
    return dict(code=code, path=path, expected=expected, actual=actual,
                remedy="Correct the declared package or policy and rerun offline preflight.")


def encode(value):
    return (json.dumps(value, ensure_ascii=True, indent=2, allow_nan=False) + "\n").encode("utf-8")


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key")
        result[key] = value
    return result


def parse_manifest(raw):
    # Bound recursion before invoking the JSON parser (also counts unknown fields).
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
                raise Rejected("MANIFEST_LIMIT")
        elif ch in (93, 125):
            depth -= 1
    try:
        data = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=unique_pairs,
                          parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))
        # Reject escaped unpaired surrogates, including in unknown properties.
        json.dumps(data, ensure_ascii=False).encode("utf-8")
    except (ValueError, UnicodeError, RecursionError):
        raise Rejected("MANIFEST_INVALID") from None
    if not isinstance(data, dict):
        raise Rejected("MANIFEST_INVALID")
    if isinstance(data.get("schema_version"), str) and data["schema_version"] != VERSION:
        raise Rejected("MANIFEST_VERSION_UNSUPPORTED", exit_code=3)
    return data


def local_absolute(value):
    """Reject network/device syntax before filesystem access, including mapped SMB drives."""
    text = os.fspath(value)
    if not text or "\x00" in text or text.startswith(("\\\\", "//")):
        raise Rejected("PATH_INVALID")
    path = Path(os.path.abspath(text))
    if os.name == "nt":
        import ctypes
        if ctypes.windll.kernel32.GetDriveTypeW(str(path.anchor)) == 4:
            raise Rejected("NETWORK_PATH_FORBIDDEN")
        if ":" in str(path)[2:]:
            raise Rejected("PATH_INVALID")
    return path


def no_reparse(path):
    for node in (*reversed(path.parents), path):
        info = node.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise Rejected("REPARSE_POINT_FORBIDDEN", exit_code=1)


def read_bytes(path, limit):
    no_reparse(path)
    if not stat.S_ISREG(path.lstat().st_mode):
        raise Rejected("FILE_TYPE_UNSUPPORTED", exit_code=1)
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    fd = os.open(path, flags)
    with os.fdopen(fd, "rb") as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode):
            raise Rejected("FILE_TYPE_UNSUPPORTED", exit_code=1)
        if before.st_size > limit:
            raise Rejected("INPUT_LIMIT", exit_code=1)
        raw = stream.read(limit + 1)
        after = os.fstat(stream.fileno())
    no_reparse(path)
    current = path.stat()
    if len(raw) > limit:
        raise Rejected("INPUT_LIMIT", exit_code=1)
    # Python 3.12 Windows stat/fstat expose different legacy st_ctime semantics.
    # Identity, size and last-write time are comparable across both APIs.
    fingerprint = lambda s: (s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns)
    if fingerprint(before) != fingerprint(after) or fingerprint(after) != fingerprint(current):
        raise Rejected("INPUT_CHANGED", exit_code=1)
    return raw


def valid_relative(value):
    if not isinstance(value, str) or not value or len(value) > 240:
        return False
    if unicodedata.normalize("NFC", value) != value:
        return False
    parts = value.split("/")
    if len(parts) > MAX_DEPTH:
        return False
    for part in parts:
        if not part or part in (".", "..") or part.endswith((".", " ")):
            return False
        if any(ord(ch) < 32 or ord(ch) == 127 or ch in '<>:"\\|?*' for ch in part):
            return False
        if re.fullmatch(r"(?i:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])", part.split(".")[0]):
            return False
    return True


def inventory(root):
    """Iterative bounded traversal, never follow a reparse point, including internal ones."""
    found, pending, count = set(), [(root, 0)], 0
    while pending:
        directory, depth = pending.pop()
        no_reparse(directory)
        with os.scandir(directory) as entries:
            batch = []
            for entry in entries:
                count += 1
                if count > MAX_ENTRIES or depth > MAX_DEPTH:
                    raise Rejected("INVENTORY_LIMIT", exit_code=1)
                batch.append(entry)
            for entry in sorted(batch, key=lambda e: e.name):
                relative = Path(entry.path).relative_to(root).as_posix()
                info = entry.stat(follow_symlinks=False)
                if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                    raise Rejected("REPARSE_POINT_FORBIDDEN", relative, 1)
                if not valid_relative(relative):
                    raise Rejected("PATH_INVALID", relative, 1)
                if stat.S_ISDIR(info.st_mode):
                    pending.append((Path(entry.path), depth + 1))
                elif stat.S_ISREG(info.st_mode):
                    found.add(relative)
                else:
                    raise Rejected("FILE_TYPE_UNSUPPORTED", relative, 1)
    return found


def validate_text(raw, row):
    problems = []
    policy = row["text"]
    bom = raw.startswith(b"\xef\xbb\xbf")
    if (policy["bom"] == "required" and not bom) or (policy["bom"] == "forbidden" and bom):
        problems.append(("files.encoding", issue("BOM_MISMATCH", row["path"], policy["bom"], "present" if bom else "absent")))
    try:
        text = raw.decode("utf-8-sig", errors="strict")
    except UnicodeError:
        problems.append(("files.encoding", issue("ENCODING_INVALID", row["path"], "utf-8", "invalid")))
        return problems, False
    crlf = text.count("\r\n")
    lf = text.count("\n") - crlf
    cr = text.count("\r") - crlf
    actual = "mixed" if sum(n > 0 for n in (crlf, lf, cr)) > 1 else "crlf" if crlf else "lf" if lf else "cr" if cr else "none"
    # 'any' accepts mixed CRLF/LF, but never lone CR. No-newline files are valid for either style.
    if cr or (policy["eol"] != "any" and actual not in (policy["eol"], "none")):
        problems.append(("files.eol", issue("EOL_MISMATCH", row["path"], policy["eol"], actual)))
    final = text.endswith("\n")
    if (policy["final_newline"] == "required" and not final) or (policy["final_newline"] == "forbidden" and final):
        problems.append(("files.eol", issue("FINAL_NEWLINE_MISMATCH", row["path"], policy["final_newline"], "present" if final else "absent")))
    return problems, True


def validate(manifest, root):
    report = dict(schema_version="tia-bridge.preflight/1.0", status="FAIL", exit_code=2,
                  scope=dict(root=None, manifest=None, manifest_sha256=None),
                  checks=[dict(id=name, status="NOT_RUN", count=0) for name in CHECKS], issues=[])
    checks = {item["id"]: item for item in report["checks"]}
    active = "manifest.schema"
    try:
        root, manifest = local_absolute(root), local_absolute(manifest)
        report["scope"].update(root=str(root), manifest=str(manifest))
        no_reparse(root)
        if not root.is_dir():
            raise Rejected("ROOT_INVALID")
        try:
            raw = read_bytes(manifest, MAX_MANIFEST)
        except Rejected as error:
            if error.code == "INPUT_LIMIT":
                raise Rejected("MANIFEST_LIMIT") from None
            raise
        report["scope"]["manifest_sha256"] = hashlib.sha256(raw).hexdigest()
        data = parse_manifest(raw)
        from jsonschema import Draft202012Validator
        try:
            schema = json.loads((SCHEMAS / "manifest.schema.json").read_text(encoding="utf-8"))
        except (OSError, ValueError):
            raise RuntimeError("Local validator schema unavailable") from None
        if not Draft202012Validator(schema).is_valid(data):
            raise Rejected("MANIFEST_SCHEMA_INVALID")
        checks[active].update(status="PASS", count=1)
        unsupported = sorted(set(data["required_checks"]) - set(CHECKS))
        if unsupported:
            raise Rejected("REQUIRED_CHECK_UNSUPPORTED", exit_code=3)
        active = "manifest.files"
        paths = [row["path"] for row in data["files"]]
        for path in paths:
            if not valid_relative(path):
                raise Rejected("PATH_INVALID", path, 1)
        if len({p.casefold() for p in paths}) != len(paths):
            raise Rejected("PATH_DUPLICATE", exit_code=1)
        # File/directory prefix collisions are invalid on every supported platform.
        folded = {p.casefold() for p in paths}
        for path in paths:
            if any("/".join(path.casefold().split("/")[:i]) in folded for i in range(1, len(path.split("/")))):
                raise Rejected("PATH_COLLISION", path, 1)
        exempt = set()
        if manifest.is_relative_to(root):
            relative = manifest.relative_to(root).as_posix()
            if relative.casefold() in folded:
                raise Rejected("MANIFEST_SELF_REFERENCE", relative)
            exempt.add(relative)
        found = inventory(root) - exempt
        for path in sorted(set(paths) - found):
            report["issues"].append(issue("FILE_MISSING", path))
        for path in sorted(found - set(paths)):
            report["issues"].append(issue("FILE_EXTRA", path))
        checks[active].update(status="FAIL" if report["issues"] else "PASS", count=len(found))
        for name in CHECKS[2:]:
            checks[name]["status"] = "PASS"
        total = 0
        for row in sorted(data["files"], key=lambda r: r["path"]):
            path = row["path"]
            if path not in found:
                for name in CHECKS[2:]:
                    checks[name]["status"] = "FAIL"
                continue
            active = "files.sha256"
            try:
                raw = read_bytes(root / path, min(MAX_FILE, MAX_TOTAL - total))
            except (Rejected, OSError) as error:
                report["issues"].append(issue(error.code if isinstance(error, Rejected) else "FILE_UNREADABLE", path))
                for name in CHECKS[2:]:
                    checks[name]["status"] = "FAIL"
                continue
            total += len(raw)
            # Hash original bytes BEFORE decoding or inspecting EOL/BOM.
            actual = hashlib.sha256(raw).hexdigest()
            checks[active]["count"] += 1
            if actual != row["sha256"]:
                checks[active]["status"] = "FAIL"
                report["issues"].append(issue("SHA256_MISMATCH", path, row["sha256"], actual))
            if row["type"] == "text":
                problems, decoded = validate_text(raw, row)
                checks["files.encoding"]["count"] += 1
                if decoded:
                    checks["files.eol"]["count"] += 1
                else:
                    checks["files.eol"]["status"] = "FAIL"
                for check, problem in problems:
                    checks[check]["status"] = "FAIL"
                    report["issues"].append(problem)
        report["exit_code"] = 1 if report["issues"] else 0
    except Rejected as error:
        checks[active]["status"] = "FAIL"
        report["issues"].append(issue(error.code, error.path))
        report["exit_code"] = error.exit_code
    except (OSError, ValueError):
        checks[active]["status"] = "FAIL"
        report["issues"].append(issue("INPUT_UNREADABLE"))
        report["exit_code"] = 2
    except Exception:
        checks[active]["status"] = "FAIL"
        report["issues"].append(issue("VALIDATOR_INTERNAL_ERROR"))
        report["exit_code"] = 4
    report["issues"].sort(key=lambda i: (i["path"] or "", i["code"]))
    report["status"] = "PASS" if report["exit_code"] == 0 else "FAIL"
    return report


def write_report(output, report, root, manifest):
    output, root, manifest = local_absolute(output), local_absolute(root), local_absolute(manifest)
    no_reparse(root)
    no_reparse(manifest)
    # All parents must already exist; never create directories or replace an existing file.
    no_reparse(output.parent)
    if not output.parent.is_dir():
        raise Rejected("REPORT_PATH_UNSAFE")
    # Resolve existing nodes only AFTER reparse rejection, covering alternate drive aliases.
    root, manifest = root.resolve(strict=True), manifest.resolve(strict=True)
    output = output.parent.resolve(strict=True) / output.name
    if output.is_relative_to(root) or root.is_relative_to(output) or output == manifest:
        raise Rejected("REPORT_PATH_UNSAFE")
    with output.open("xb") as stream:
        stream.write(encode(report))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--root", required=True)
    parser.add_argument("--json", action="store_true", help="JSON is also the default output")
    parser.add_argument("--output", help="New report file outside package; parent must exist")
    args = parser.parse_args(argv)
    report = validate(args.manifest, args.root)
    if args.output:
        try:
            write_report(args.output, report, args.root, args.manifest)
        except (Rejected, OSError, ValueError) as error:
            report.update(status="FAIL", exit_code=2)
            report["issues"].append(issue(error.code if isinstance(error, Rejected) else "REPORT_WRITE_FAILED"))
        except Exception:
            report.update(status="FAIL", exit_code=4)
            report["issues"].append(issue("VALIDATOR_INTERNAL_ERROR"))
    sys.stdout.write(encode(report).decode("utf-8"))
    return report["exit_code"]


if __name__ == "__main__":
    sys.exit(main())
