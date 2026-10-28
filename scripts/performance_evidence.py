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
import os
from uuid import UUID

PROFILE_PATH = Path(__file__).resolve().parents[1] / "tests/resilience/performance-profiles.json"
PROFILE_BYTES = PROFILE_PATH.read_bytes()
PROFILE_SHA256 = hashlib.sha256(PROFILE_BYTES).hexdigest()
PROFILE_DEFINITION = json.loads(PROFILE_BYTES)
PROFILE_NAME = os.environ.get("TIA_BRIDGE_QUALIFICATION_PROFILE", PROFILE_DEFINITION["default_profile"])
if PROFILE_NAME not in PROFILE_DEFINITION["profiles"]:
    raise ValueError("Unknown performance qualification profile.")
PROFILE_VERSION = PROFILE_DEFINITION["profiles"][PROFILE_NAME]["version"]
TEST_HARNESS_LIMITS = PROFILE_DEFINITION["profiles"][PROFILE_NAME]["test_harness"]
_LIMITS = PROFILE_DEFINITION["profiles"][PROFILE_NAME]["limits"]
PROFILES = {
    "api-performance": {
        "nf02-submit-eight": (64, _LIMITS["nf02_submit_p95_ms"], _LIMITS["nf02_submit_p99_ms"]),
        "nf03-inplace-repository": (100, _LIMITS["nf03_replace_p95_ms"], None),
        "nf01-status-one": (1000, _LIMITS["nf01_status_p95_ms"], _LIMITS["nf01_status_p99_ms"]),
        "nf01-status-eight": (1000, _LIMITS["nf01_status_p95_ms"], _LIMITS["nf01_status_p99_ms"]),
    },
    "workspace-replace": {"nf03-ntfs-save-replace": (100, _LIMITS["nf03_replace_p95_ms"], None)},
    "repository-replace-control": {
        "nf03-repository-without-worker": (100, _LIMITS["nf03_replace_p95_ms"], None),
        "nf03-repository-with-worker": (100, _LIMITS["nf03_replace_p95_ms"], None),
    },
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
        if value["qualification_profile"] != PROFILE_NAME or value["profile_version"] != PROFILE_VERSION or value["profile_definition_sha256"] != PROFILE_SHA256:
            raise ValueError("Qualification profile identity mismatch.")
        if value.get("applied_thresholds") != _LIMITS:
            raise ValueError("Applied thresholds differ from the versioned profile.")
        if value.get("applied_test_harness_limits") != TEST_HARNESS_LIMITS:
            raise ValueError("Applied test harness limits differ from the versioned profile.")
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
            p95_exceedances = sum(value > p95 for value in values)
            p99_exceedances = None if p99 is None else sum(value > p99 for value in values)
            status = "INCOMPLETE" if len(samples) != expected else "FAIL" if refused or measured["p95_ms"] > p95 or p99 is not None and measured["p99_ms"] > p99 else "PASS"
            if metric.get("p95_exceedance_count") != p95_exceedances or metric.get("p99_exceedance_count") != p99_exceedances:
                raise ValueError("Threshold exceedance count mismatch.")
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
            for field, metric in [("submit_eight_clients", "nf02-submit-eight"), ("edit_one_mib", "nf03-inplace-repository"), ("status_one_client", "nf01-status-one"), ("status_eight_clients", "nf01-status-eight")]:
                if summary[field] != by_id[metric]["distribution"]:
                    raise ValueError("API summary differs from samples.")
        return {"status": "INSUFFICIENT" if insufficient else "FAIL" if failed else "PASS", "bundle": value, "sha256": digest(path), "profile": PROFILE_NAME, "profile_version": PROFILE_VERSION}
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


def load_nf11_synthetic(root, run_id):
    """Validate run-bound synthetic NF-11 phase evidence without inferring status from a short smoke."""
    if not re.fullmatch(r"[0-9a-f]{32}", run_id):
        raise ValueError("Invalid NF-11 run ID.")
    folder = checked_path(root, ".tia-bridge/resilience/" + run_id)
    manifest = read_json(checked_path(folder, "nf11-synthetic-manifest.json"))
    if manifest.get("schema_version") != "1.0" or manifest.get("run_id") != run_id:
        raise ValueError("NF-11 synthetic manifest identity mismatch.")
    required = {"nf11-synthetic-samples.jsonl", "nf11-synthetic-operations.jsonl", "nf11-synthetic-result.json"}
    if set(manifest.get("files", {})) != required:
        raise ValueError("NF-11 synthetic manifest inventory mismatch.")
    for name in sorted(required):
        path = checked_path(folder, name)
        identity = manifest["files"][name]
        if type(identity.get("bytes")) is not int or identity["bytes"] != path.stat().st_size or not re.fullmatch(r"[0-9a-f]{64}", identity.get("sha256", "")) or digest(path) != identity["sha256"]:
            raise ValueError("NF-11 synthetic evidence hash mismatch: " + name)

    result_path = checked_path(folder, "nf11-synthetic-result.json")
    result = read_json(result_path)
    if result.get("schema_version") != "1.0" or result.get("run_id") != run_id or result.get("phase") != "NF11_SYNTHETIC":
        raise ValueError("NF-11 synthetic result identity mismatch.")
    if result.get("mode") not in {"FULL", "DIAGNOSTIC"} or result.get("execution_status") not in {"COMPLETE", "INTERRUPTED", "FAIL", "DIAGNOSTIC"}:
        raise ValueError("NF-11 synthetic execution state is invalid.")
    started = timestamp(result.get("measurement_started_at"))
    ended = timestamp(result.get("ended_at"))
    if ended < started:
        raise ValueError("NF-11 synthetic time window is reversed.")
    measured = result.get("measured_seconds")
    if type(measured) not in {int, float} or not math.isfinite(measured) or measured < 0:
        raise ValueError("NF-11 measured duration is invalid.")

    def rows(filename):
        path = checked_path(folder, filename)
        if path.stat().st_size > 128 * 1024 * 1024:
            raise ValueError("NF-11 evidence file exceeds the bounded size.")
        output = []
        try:
            with path.open("r", encoding="utf-8", newline="") as stream:
                for line_number, line in enumerate(stream, 1):
                    if len(line) > 16 * 1024 or not line.endswith("\n"):
                        raise ValueError("Truncated or oversized NF-11 evidence row.")
                    output.append(json.loads(line, object_pairs_hook=_pairs,
                                             parse_constant=lambda value: (_ for _ in ()).throw(ValueError("Nonfinite JSON number."))))
        except (OSError, UnicodeError, RecursionError, json.JSONDecodeError) as error:
            raise ValueError("Missing or damaged NF-11 evidence rows.") from error
        return output

    sample_rows = rows("nf11-synthetic-samples.jsonl")
    if len(sample_rows) != result.get("samples_count") or not sample_rows:
        raise ValueError("NF-11 resource samples are missing or incomplete.")
    elapsed_samples = []
    host_working_sets = []
    host_handles = []
    for expected, row in enumerate(sample_rows):
        value = row.get("value", {})
        elapsed = value.get("elapsed_seconds")
        host = value.get("host", {})
        if row.get("sequence") != expected or type(elapsed) not in {int, float} or not math.isfinite(elapsed) or elapsed < 0 or elapsed > measured + 1:
            raise ValueError("NF-11 resource sample sequence/time is invalid.")
        if type(host.get("working_set_bytes")) is not int or host["working_set_bytes"] < 0 or type(host.get("handles")) is not int or host["handles"] <= 0:
            raise ValueError("NF-11 resource sample is incomplete.")
        for field in ["threads", "cpu_ms"]:
            if type(host.get(field)) not in {int, float} or not math.isfinite(host[field]) or host[field] < 0:
                raise ValueError("NF-11 CPU/thread sample is invalid.")
        if type(value.get("process_counts", {}).get("machine")) is not int or type(value.get("process_counts", {}).get("tracked_owned_total")) is not int:
            raise ValueError("NF-11 process sample is incomplete.")
        elapsed_samples.append(elapsed)
        host_working_sets.append(host["working_set_bytes"])
        host_handles.append(host["handles"])
    if any(right <= left for left, right in zip(elapsed_samples, elapsed_samples[1:])):
        raise ValueError("NF-11 resource sample times are not strictly increasing.")

    operations = rows("nf11-synthetic-operations.jsonl")
    accepted = set()
    completed = set()
    for row in operations:
        operation_id = row.get("operation_id")
        if not isinstance(operation_id, str) or not re.fullmatch(r"[0-9a-fA-F-]{32,36}", operation_id):
            raise ValueError("Invalid NF-11 operation identity.")
        try:
            operation_id = UUID(operation_id).hex
        except (ValueError, AttributeError) as error:
            raise ValueError("Invalid NF-11 operation identity.") from error
        if row.get("kind") == "accepted":
            if operation_id in accepted:
                raise ValueError("Duplicate accepted NF-11 operation ID.")
            accepted.add(operation_id)
        elif row.get("kind") == "completed":
            if operation_id not in accepted or operation_id in completed or row.get("state") != "SUCCEEDED":
                raise ValueError("Unexpected or failed NF-11 completion.")
            completed.add(operation_id)
        else:
            raise ValueError("Unknown NF-11 operation evidence event.")
    lost = sorted(accepted - completed)
    if result.get("accepted_operations") != len(accepted) or result.get("completed_operations") != len(completed):
        raise ValueError("NF-11 operation totals differ from event evidence.")
    try:
        summary_lost = sorted(UUID(value).hex for value in result.get("lost_operation_ids", []))
    except (ValueError, TypeError) as error:
        raise ValueError("Invalid NF-11 lost Operation ID summary.") from error
    if summary_lost != lost:
        raise ValueError("NF-11 lost Operation ID summary differs from evidence.")

    baseline_ram = result.get("warmup_host_working_set_bytes")
    baseline_handles = result.get("warmup_host_handles")
    if type(baseline_ram) is not int or type(baseline_handles) is not int or baseline_handles <= 0:
        raise ValueError("NF-11 warmup resource baseline is invalid.")
    ram_growth = max(0, (max(host_working_sets) - baseline_ram) / 1048576)
    handle_growth = max(0, (max(host_handles) - baseline_handles) / baseline_handles)
    if not math.isclose(result.get("ram_growth_mib", -1), ram_growth, rel_tol=1e-9, abs_tol=1e-6) or not math.isclose(result.get("handle_growth_fraction", -1), handle_growth, rel_tol=1e-9, abs_tol=1e-9):
        raise ValueError("NF-11 resource aggregates differ from samples.")
    matrix = read_json(root / "tests/resilience/matrix.json")
    synthetic = matrix.get("nf11_synthetic_phase", {})
    requirement = matrix.get("eight_hour_requirement", {})
    live = matrix.get("nf11_live_phase", {})
    if not synthetic or requirement.get("required_seconds") != 28_800 or requirement.get("minimum_operations") != 1000 or live.get("minimum_read_only_operations") != 100:
        raise ValueError("NF-11 qualification matrix is missing or inconsistent.")
    if result.get("sampling_interval_seconds") != synthetic.get("sampling_interval_seconds") or result.get("required_seconds") != requirement["required_seconds"] or result.get("minimum_operations") != requirement["minimum_operations"]:
        raise ValueError("NF-11 criteria or sample interval changed.")
    thresholds = result.get("thresholds", {})
    if thresholds.get("maximum_ram_growth_mib") != synthetic.get("host_ram_growth_mib_max") or thresholds.get("maximum_handle_growth_fraction") != synthetic.get("host_handle_growth_fraction_max"):
        raise ValueError("NF-11 stability thresholds differ from the matrix.")
    unmanaged = result.get("unmanaged_child_processes")
    if type(unmanaged) is not int or unmanaged < 0 or result.get("cleanup_confirmed") != (unmanaged == 0):
        raise ValueError("NF-11 child-process cleanup evidence is invalid.")
    run_metadata = read_json(checked_path(folder, "validation/run-metadata.json"))
    build_metadata_path = checked_path(folder, "validation/build-metadata.json")
    build_metadata = read_json(build_metadata_path)
    git_sha = run_metadata.get("git_sha")
    source_sha = run_metadata.get("source_fingerprint_lf_sha256")
    if run_metadata.get("run_id") != run_id or build_metadata.get("run_id") != run_id or build_metadata.get("git_sha") != git_sha or build_metadata.get("source_fingerprint_lf_sha256") != source_sha:
        raise ValueError("NF-11 source/build metadata identity mismatch.")
    if not re.fullmatch(r"[0-9a-f]{40}", git_sha or "") or not re.fullmatch(r"[0-9a-f]{64}", source_sha or ""):
        raise ValueError("NF-11 source/build fingerprint is invalid.")
    binaries = build_metadata.get("binaries")
    if not isinstance(binaries, dict) or not binaries:
        raise ValueError("NF-11 build hashes are missing.")
    for identity in binaries.values():
        checked_path(root, identity.get("path"))
        if type(identity.get("bytes")) is not int or identity["bytes"] <= 0 or not re.fullmatch(r"[0-9a-f]{64}", identity.get("sha256", "")):
            raise ValueError("NF-11 build artifact identity is invalid.")
    return {"result": result, "samples": sample_rows, "accepted_ids": accepted, "completed_ids": completed,
            "ram_growth_mib": ram_growth, "handle_growth_fraction": handle_growth,
            "required_seconds": requirement["required_seconds"], "minimum_operations": requirement["minimum_operations"],
            "sample_interval_seconds": synthetic["sampling_interval_seconds"],
            "maximum_ram_growth_mib": synthetic["host_ram_growth_mib_max"],
            "maximum_handle_growth_fraction": synthetic["host_handle_growth_fraction_max"],
            "live_minimum_operations": live["minimum_read_only_operations"],
            "build_metadata": build_metadata,
            "build_metadata_sha256": digest(build_metadata_path),
            "manifest_sha256": digest(checked_path(folder, "nf11-synthetic-manifest.json")),
            "result_sha256": digest(result_path)}
