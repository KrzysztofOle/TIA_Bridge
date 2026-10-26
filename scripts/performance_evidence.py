"""NF sample evidence 1.1: strict bounded loading, reproducible nearest-rank aggregates, immutable artifacts.

Python 3 standard library only. Used by report_resilience/validate_offline; no Siemens or test execution.
"""
from datetime import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import re

PROFILES = {
    "api-performance": {"nf02-submit-eight": (64, 250, 500), "nf03-inplace-repository": (100, 100, None)},
    "workspace-replace": {"nf03-ntfs-save-replace": (100, 100, None)},
    "repository-replace-control": {"nf03-repository-without-worker": (100, 100, None), "nf03-repository-with-worker": (100, 100, None)},
}


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key.")
        result[key] = value
    return result


def read_json(path):
    try:
        if path.is_symlink() or path.is_junction() or path.stat().st_size > 4 * 1024 * 1024:
            raise ValueError("Unsafe/oversize evidence.")
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_pairs,
                           parse_constant=lambda value: (_ for _ in ()).throw(ValueError("Nonfinite JSON number.")))
        if not isinstance(value, dict):
            raise ValueError("Evidence root must be an object.")
        return value
    except (OSError, UnicodeError, RecursionError, json.JSONDecodeError) as error:
        raise ValueError("Missing or damaged evidence.") from error


def write_new_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")
        stream.flush(); os.fsync(stream.fileno())


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def timestamp(value):
    try:
        stamp = datetime.fromisoformat(value)
        if stamp.tzinfo is None or stamp.utcoffset().total_seconds() != 0:
            raise ValueError("Evidence timestamp must be UTC.")
        return stamp
    except (TypeError, AttributeError) as error:
        raise ValueError("Invalid timestamp.") from error


def distribution(values):
    ordered = sorted(values)
    if not ordered:
        return None
    return {"samples": len(ordered), **{key: ordered[math.ceil(len(ordered) * quantile) - 1]
            for key, quantile in [("p50_ms", .5), ("p95_ms", .95), ("p99_ms", .99), ("maximum_ms", 1)]}}


def checked_path(root, relative):
    if not isinstance(relative, str) or "\\" in relative or ":" in relative or relative.startswith("/") or ".." in Path(relative).parts:
        raise ValueError("Unsafe evidence path.")
    path = root / relative
    if any(p.is_symlink() or p.is_junction() for p in [path, *path.parents] if p != root.parent):
        raise ValueError("Reparse evidence path.")
    if path.resolve() != root.resolve() / relative:
        raise ValueError("Evidence escapes its root.")
    return path


def load_bundle(root, run_id, scenario, started_at, verify_build=True):
    """Return validated data including explicit INSUFFICIENT for incomplete/legacy samples; never infer PASS."""
    if not re.fullmatch(r"[0-9a-f]{32}", run_id) or scenario not in PROFILES:
        raise ValueError("Invalid run/profile.")
    folder = root / ".tia-bridge/resilience" / run_id / "performance"
    manifest = read_json(checked_path(root, ".tia-bridge/resilience/" + run_id + "/performance/" + scenario + ".manifest.json"))
    if manifest.get("schema_version") != "1.1" or manifest.get("kind") != "nf-performance-manifest" or manifest.get("run_id") != run_id or manifest.get("artifact") != scenario + ".json":
        raise ValueError("Manifest identity mismatch.")
    path = checked_path(folder, manifest["artifact"])
    try:
        if path.stat().st_size != manifest.get("bytes") or digest(path) != manifest.get("sha256"):
            raise ValueError("Artifact hash mismatch.")
    except OSError as error:
        raise ValueError("Artifact missing.") from error
    value = read_json(path)
    if value.get("schema_version") != "1.1":
        return {"status": "INSUFFICIENT", "reason": "Historical aggregate format has no full samples."}
    try:
        if value["kind"] != "nf-performance" or value["run_id"] != run_id or value["scenario"] != scenario:
            raise ValueError("Sample evidence identity mismatch.")
        if timestamp(value["started_at"]) < timestamp(started_at) or timestamp(value["ended_at"]) < timestamp(value["started_at"]):
            raise ValueError("Stale or reversed sample timestamps.")
        if not re.fullmatch(r"[0-9a-f]{40}", value["git_sha"]) or not isinstance(value["git_dirty"], bool):
            raise ValueError("Missing Git identity.")
        for key in ["test_name", "runtime", "os", "state_path", "state_filesystem"]:
            if not isinstance(value[key], str) or not value[key]:
                raise ValueError("Missing environment/test metadata.")
        if value["fake_scenario"] != "Hang" or value["test_status"] not in {"PASS", "FAIL"}:
            raise ValueError("Wrong test configuration/status.")
        for key in ["binaries_sha256", "sources_sha256"]:
            if not value[key]:
                raise ValueError("Missing build/source hashes.")
            for relative, sha in value[key].items():
                if not re.fullmatch(r"[0-9a-f]{64}", sha):
                    raise ValueError("Invalid build hash.")
                if verify_build and digest(checked_path(root, relative)) != sha:
                    raise ValueError("Build/source hash mismatch.")
        metrics = value["metrics"]
        ids = [metric["id"] for metric in metrics]
        if len(ids) != len(set(ids)) or set(ids) - set(PROFILES[scenario]):
            raise ValueError("Duplicate or mixed metric profile.")
        insufficient = set(ids) != set(PROFILES[scenario])
        failed = value["test_status"] == "FAIL"
        for metric in metrics:
            expected, p95, p99 = PROFILES[scenario][metric["id"]]
            if (metric["unit"], metric["time_kind"], metric["expected_samples"], metric["p95_limit_ms"], metric["p99_limit_ms"]) != ("ms", "wall_clock", expected, p95, p99):
                raise ValueError("Metric semantics/threshold mismatch.")
            samples = metric["samples"]
            if [sample["id"] for sample in samples] != list(range(len(samples))) or metric["sample_count"] != len(samples) or len(samples) > expected:
                raise ValueError("Sample identity/count mismatch.")
            values = [sample["value"] for sample in samples]
            if any(type(v) not in {int, float} or not math.isfinite(v) or v < 0 for v in values):
                raise ValueError("Invalid sample time.")
            measured = distribution(values)
            if metric["distribution"] != measured:
                raise ValueError("Aggregate does not reproduce from samples.")
            refused = metric["refused_samples"]
            if type(refused) is not int or not 0 <= refused <= len(samples):
                raise ValueError("Invalid refusal count.")
            status = "INCOMPLETE" if len(samples) != expected else "FAIL" if refused or measured["p95_ms"] > p95 or p99 is not None and measured["p99_ms"] > p99 else "PASS"
            if metric["status"] != status:
                raise ValueError("Sample SLO status mismatch.")
            insufficient |= status == "INCOMPLETE"
            failed |= status == "FAIL"
        if not re.fullmatch(r"[0-9a-f]{64}", value.get("config_sha256") or ""):
            insufficient = True
        summary = value.get("summary")
        if scenario == "api-performance" and not insufficient:
            if not summary or timestamp(summary["observed_at"]) < timestamp(value["started_at"]) or timestamp(summary["observed_at"]) > timestamp(value["ended_at"]):
                raise ValueError("Missing/stale API summary.")
            by_id = {m["id"]: m for m in metrics}
            for field, metric in [("submit_eight_clients", "nf02-submit-eight"), ("edit_one_mib", "nf03-inplace-repository")]:
                if summary[field] != by_id[metric]["distribution"]:
                    raise ValueError("API summary differs from samples.")
        return {"status": "INSUFFICIENT" if insufficient else "FAIL" if failed else "PASS", "bundle": value, "sha256": digest(path)}
    except (KeyError, TypeError, AttributeError, OSError) as error:
        raise ValueError("Incomplete or malformed sample evidence.") from error


def seal_run(folder, run_id):
    """Immutable complete inventory; includes failed/partial artifacts and reports, excludes this manifest itself."""
    files = {p.relative_to(folder).as_posix(): {"sha256": digest(p), "bytes": p.stat().st_size}
             for p in sorted(folder.rglob("*")) if p.is_file() and p != folder / "evidence-manifest.json"}
    write_new_json(folder / "evidence-manifest.json", {"schema_version": "1.1", "run_id": run_id, "files": files})


def verify_run(folder, run_id):
    manifest = read_json(folder / "evidence-manifest.json")
    if manifest.get("run_id") != run_id or manifest.get("schema_version") != "1.1" or not manifest.get("files"):
        raise ValueError("Invalid run manifest.")
    actual = {p.relative_to(folder).as_posix() for p in folder.rglob("*") if p.is_file() and p != folder / "evidence-manifest.json"}
    if actual != set(manifest["files"]):
        raise ValueError("Missing/extra run artifact.")
    for relative, expected in manifest["files"].items():
        path = checked_path(folder, relative)
        if digest(path) != expected["sha256"] or path.stat().st_size != expected["bytes"]:
            raise ValueError("Run artifact integrity mismatch.")
    return manifest
