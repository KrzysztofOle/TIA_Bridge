"""B1-J offline adapter; Python 3.12 stdlib, Windows x64, Release solution.

Called by QA Runner; its artifact directory and manifest own all reporting/logging.
No Siemens, external tools, live switches, production GC or handle thresholds.
"""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from qa_runner.diagnostics import write_json


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def analyze(folder):
    samples = [json.loads(line) for line in (folder / "metrics.jsonl").read_text().splitlines()]
    snapshots = [read(p) for p in folder.glob("types-*.json")]
    if not {"cold", "warm", "idle-enter", "idle", "shutdown"}.issubset({s["phase"] for s in samples}):
        raise ValueError("Required phase evidence missing")
    if not {"cold", "warm", "idle-enter", "idle", "shutdown"}.issubset({s["phase"] for s in snapshots}):
        raise ValueError("Required handle snapshots missing")
    if any(b["elapsed_ticks"] < a["elapsed_ticks"] for a, b in zip(samples, samples[1:])):
        raise ValueError("Monotonic timestamps regressed")
    warm = next(s for s in samples if s["phase"] == "warm")
    idle = next(s for s in reversed(samples) if s["phase"] == "idle")
    pre = next((s for s in samples if s["step_id"] == "pre-gc"), None)
    post = next((s for s in samples if s["step_id"] == "post-gc"), None)
    if (pre is None) != (post is None):
        raise ValueError("Incomplete GC pair")
    types = []
    for item in sorted(snapshots, key=lambda x: x["sequence"]):
        snap = item["snapshot"]
        types.append(dict(phase=item["phase"], sequence=item["sequence"], status=snap["status"],
                          counts=snap.get("counts", "NOT_AVAILABLE"), ownership=item["ownership"],
                          thread_handles=[h for h in snap.get("handles", []) if h["type"] == "Thread"]))
    ended = read(folder / "ended.json")
    controller = read(folder / "controller-result.json")
    spec = read(folder / "spec.json")
    if ended["cleanup"] != "DISPOSED" or controller["exit_code"] != 0 or samples[-1]["phase"] != "shutdown":
        raise ValueError("Cleanup evidence missing")
    if ended["errors"] or bool(controller["heartbeats"]) != spec["heartbeat"]:
        raise ValueError("Sender errors or heartbeat mismatch")
    if ended["sends"] != controller["dispatches"] + controller["heartbeats"] + 1:
        raise ValueError("Completed sends do not match received authenticated messages")
    if any(s["worker_epoch"] != controller["epoch"] or s["worker_pid"] != controller["pid"] for s in samples):
        raise ValueError("Process binding changed")
    checks = read(folder / "cleanup-checks.json") if (folder / "cleanup-checks.json").exists() else []
    gc_release = "NOT_AVAILABLE"
    if pre:
        before = next(t for t in types if t["sequence"] == pre["sequence"])
        after = next(t for t in types if t["sequence"] == post["sequence"])
        removed = [h for h in before["thread_handles"] if not any(h["value"] == a["value"] and h.get("detail") == a.get("detail") for a in after["thread_handles"])]
        known = {t["native_id"]: t for t in before["ownership"].get("threads", [])}
        gc_release = dict(type_deltas={name: after["counts"].get(name, 0) - before["counts"].get(name, 0) if isinstance(before["counts"], dict) and isinstance(after["counts"], dict) and name in before["counts"] and name in after["counts"] else "NOT_AVAILABLE" for name in ["Thread", "Event"]},
                          removed_thread_handles=[dict(handle=h, tracked_owner=known.get((h.get("detail") or {}).get("thread_id"), "NOT_AVAILABLE")) for h in removed],
                          ownership_before=before["ownership"], ownership_after=after["ownership"])
    last_ownership = types[-1]["ownership"]
    apm_status = "NOT_AVAILABLE"
    if last_ownership.get("apm_ended", 0):
        apm_status = "CONFIRMED_CLOSED_AT_END" if last_ownership["apm_closed"] == last_ownership["apm_ended"] else "PARTIAL"
    trends = {}
    for phase in ["cold", "warm", "active", "idle-enter", "idle", "shutdown"]:
        window = [s for s in samples if s["phase"] == phase]
        if len(window) < 2:
            trends[phase] = "NOT_AVAILABLE"
            continue
        times = [s["elapsed_ticks"] / s["frequency"] for s in window]
        mean = statistics.mean(times)
        variance = sum((t - mean) ** 2 for t in times)
        trends[phase] = dict(samples=len(window), seconds=times[-1] - times[0],
                             handles_per_second=sum((t - mean) * (s["handles"] - statistics.mean(r["handles"] for r in window)) for t, s in zip(times, window)) / variance if variance else "NOT_AVAILABLE",
                             gc_collections=[window[-1][f"gc{i}"] - window[0][f"gc{i}"] for i in range(3)])
    # No one-value stability assertions. Windows scheduling/GC require interpretation.
    return dict(id=warm["scenario_id"], functional="PASS", resource="REVIEW_REQUIRED",
                diagnostic_completeness="PASS" if all(t["status"] == "PASS" for t in types) else "PARTIAL",
                warm_handles=warm["handles"], idle_handles=idle["handles"], warm_idle_delta=idle["handles"] - warm["handles"],
                pre_gc_handles=pre["handles"] if pre else "NOT_AVAILABLE", post_gc_handles=post["handles"] if post else "NOT_AVAILABLE",
                gc_delta=post["handles"] - pre["handles"] if pre else "NOT_AVAILABLE",
                warm_threads=warm["threads"], idle_threads=idle["threads"], messages=ended["sends"], errors=ended["errors"],
                heartbeat_count=controller["heartbeats"], termination=ended["termination"], cleanup=ended["cleanup"], checks=checks, types=types,
                sample_count=len(samples), worker_pid=warm["worker_pid"], worker_epoch=warm["worker_epoch"], gc_release=gc_release, phase_trends=trends,
                apm_lifetime=apm_status)


def comparisons(results):
    def select(prefix):
        return [r for r in results if r["id"].startswith(prefix + "-")]
    pairs = [("S1-vs-S2", "S1", "S2"), ("S2-vs-S3", "S2", "S3"), ("natural-vs-controlled", "S3", "S4"),
             ("observed-vs-inline-flush", "M-scheduled", "M-inline")]
    return [dict(name=name, left=[dict(id=r["id"], delta=r["warm_idle_delta"], gc_delta=r["gc_delta"]) for r in select(left)],
                 right=[dict(id=r["id"], delta=r["warm_idle_delta"], gc_delta=r["gc_delta"]) for r in select(right)],
                 interpretation="REVIEW_REQUIRED: S1 burst versus S2 paced; no causal inference from unmatched workload" if name == "S1-vs-S2" else "REVIEW_REQUIRED")
            for name, left, right in pairs]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mechanism-only", action="store_true", help="New allocation hypothesis: observe existing APM wait fields before/after EndRead/EndWrite")
    args = parser.parse_args()
    output = args.output.resolve()
    if "QA_RUN_ID" not in os.environ:
        raise ValueError("Run via QA Runner")
    root = Path(os.environ["LOCALAPPDATA"]) / "TIA_Bridge2/dev/e2-b1-j" / uuid.uuid4().hex
    root.mkdir(parents=True, exist_ok=False)
    binary = ROOT / "tests/integration/TiaBridge.Worker.InfrastructureProbe/bin/Release/net48/TiaBridge.Worker.InfrastructureProbe.exe"
    controller = ROOT / "tests/integration/TiaBridge.StorageProbe/bin/Release/net10.0/TiaBridge.StorageProbe.dll"
    builds = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in [binary, controller, ROOT / "src/TiaBridge.Ipc/bin/Release/net48/TiaBridge.Ipc.dll"]}
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    write_json(output / "provenance.json", dict(run_id=os.environ["QA_RUN_ID"], git_sha=sha, builds=builds, external_evidence=str(root),
               source_sha256={str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(ROOT.glob("**/*.cs")) if not {"bin", "obj", ".tia-bridge"}.intersection(p.parts)}))
    matrix = []
    for rep in range(1, 4):
        for name, heartbeat, gc, operations, delay in [("S1", False, False, 500, 0), ("S2", False, False, 60, 100), ("S3", True, False, 60, 100), ("S4", True, True, 60, 100)]:
            matrix.append(dict(id=f"{name}-{rep}", heartbeat=heartbeat, controlled_gc=gc, operations=operations, delay_ms=delay, idle_seconds=35, observe_flush=False, inline_flush=False, recovery=False, disconnect=False))
    for name, inline in [("M-scheduled", False), ("M-inline", True)]:
        matrix.append(dict(id=name + "-1", heartbeat=True, controlled_gc=True, operations=60, delay_ms=100, idle_seconds=35, observe_flush=True, inline_flush=inline, recovery=False, disconnect=False))
    for name, disconnect in [("S5-stop", False), ("S5-loss", True), ("S5-fresh", False)]:
        matrix.append(dict(id=name + "-1", heartbeat=True, controlled_gc=False, operations=2, delay_ms=0, idle_seconds=3, observe_flush=False, inline_flush=False, recovery=True, disconnect=disconnect))
    if args.mechanism_only:
        matrix = [dict(id="M-apm-1", heartbeat=True, controlled_gc=True, operations=60, delay_ms=100, idle_seconds=35, observe_flush=True, inline_flush=False, observe_apm=True, recovery=False, disconnect=False)]
    results = []
    failures = []
    for case in matrix:
        folder = root / case["id"]; folder.mkdir()
        case.update(git_sha=sha, build_sha=builds[str(binary.relative_to(ROOT))])
        write_json(folder / "spec.json", case)
        try:
            if any(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() != value for name, value in builds.items()):
                raise ValueError("Build drift")
            with (folder / "controller.log").open("x", encoding="utf-8") as log:
                process = subprocess.run(["dotnet", str(controller), str(folder), str(ROOT), "sender-lifetime", str(folder / "spec.json")], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=120)
            if process.returncode:
                raise ValueError("Controller exit " + str(process.returncode))
            result = analyze(folder)
            results.append(result)
            print(json.dumps({k: v for k, v in result.items() if k not in {"types", "checks", "gc_release", "phase_trends"}}), flush=True)
        except (OSError, ValueError, subprocess.SubprocessError) as error:
            failures.append(dict(id=case["id"], error=str(error)))
            break  # No retry or adoption; preserve all observations.
        finally:
            shutil.copytree(folder, output / "evidence" / case["id"])
    summary = dict(functional="FAIL" if failures else "PASS", resource="REVIEW_REQUIRED", diagnostic_completeness="PASS" if not failures and all(r["diagnostic_completeness"] == "PASS" for r in results) else "PARTIAL",
                   root_cause="REVIEW_REQUIRED", before_after="NOT_APPLICABLE", results=results, comparisons=comparisons(results), failures=failures)
    write_json(output / "lifetime-analysis.json", summary)
    with (output / "metrics.csv").open("x", encoding="utf-8", newline="") as stream:
        writer = None
        for path in sorted((output / "evidence").glob("*/metrics.jsonl")):
            for line in path.read_text().splitlines():
                row = json.loads(line)
                if writer is None:
                    writer = csv.DictWriter(stream, fieldnames=list(row)); writer.writeheader()
                writer.writerow(row)
    lines = ["# IPC sender lifetime — offline", "", f"Functional: {summary['functional']}; resources: REVIEW_REQUIRED; diagnostic completeness: {summary['diagnostic_completeness']}", "", "| Case | Warm handles | Idle handles | Delta | GC delta | Messages | Heartbeats |", "|---|---:|---:|---:|---:|---:|---:|"]
    lines += [f"| {r['id']} | {r['warm_handles']} | {r['idle_handles']} | {r['warm_idle_delta']} | {r['gc_delta']} | {r['messages']} | {r['heartbeat_count']} |" for r in results]
    lines += ["", "Resource status remains REVIEW_REQUIRED. Completed execution is not stability qualification. S1 is a burst control; S2/S3 are matched. No production fix assumed."]
    (output / "lifetime-analysis.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    # Simple standalone SVG; no plotting dependencies or resource threshold line.
    polylines = []
    palette = ["#2563eb", "#dc2626", "#059669", "#7c3aed"]
    for i, path in enumerate(sorted((output / "evidence").glob("*/metrics.jsonl"))):
        rows = [json.loads(line) for line in path.read_text().splitlines()]
        first = rows[0]
        points = " ".join(f"{40 + r['elapsed_ticks'] / r['frequency'] * 15:.1f},{200 - (r['handles'] - first['handles']) * 2:.1f}" for r in rows)
        polylines.append(f'<polyline fill="none" stroke="{palette[i % 4]}" points="{points}"><title>{path.parent.name}</title></polyline>')
    (output / "trends.svg").write_text('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 900 500"><rect width="900" height="500" fill="white"/><text x="20" y="20">Handle delta from cold; x=seconds (15 px/s), y=handles (2 px/handle)</text>' + "".join(polylines) + '</svg>', encoding="utf-8")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
