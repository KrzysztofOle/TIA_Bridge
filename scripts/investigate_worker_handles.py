"""Offline A-E process investigation; Python 3.12 standard library, Windows/.NET SDK.

Build Release TiaBridge.slnx first. Runs only test InfrastructureProbe and normal
Host configured with FakeWorker. No Siemens binary, grant, enumeration or Attach.
Full windows: 30 s initial idle, 20 activity slots at >=1 s, 120 s post idle.
--smoke uses 1/2/1 solely to verify plumbing, never as stability evidence.
--follow-up runs fixed timer/poll/send controls and a separate synthetic GC-worker
experiment with explicit collection after its ordinary window, followed by 30 s idle.
All output is create-new under local per-user NTFS, outside Git. Failures retained.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
SCENARIOS = ("A", "B-trace", "B-no-trace", "C", "D-on", "D-off", "E-sampler", "E-audit")


def write_new(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, indent=2); stream.flush(); os.fsync(stream.fileno())


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    choice = parser.add_mutually_exclusive_group()
    choice.add_argument("--smoke", action="store_true")
    choice.add_argument("--follow-up", action="store_true", help="Fixed timer/poll/send controls and separate GC-worker experiment; no Siemens")
    choice.add_argument("--flush-control", action="store_true", help="One sender with inline pipe flush; no production change or GC")
    args = parser.parse_args()
    if os.name != "nt": raise RuntimeError("Windows required")
    run_id = uuid.uuid4().hex
    root = Path(os.environ["LOCALAPPDATA"])/"TIA_Bridge2/dev/e2-b1-i"/run_id
    root.mkdir(parents=True, exist_ok=False)
    binaries = {name: ROOT/path for name, path in {
        "infrastructure": "tests/integration/TiaBridge.Worker.InfrastructureProbe/bin/Release/net48/TiaBridge.Worker.InfrastructureProbe.exe",
        "controller": "tests/integration/TiaBridge.StorageProbe/bin/Release/net10.0/TiaBridge.StorageProbe.dll",
        "host": "src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll",
        "fake": "src/TiaBridge.FakeWorker/bin/Release/net10.0/TiaBridge.FakeWorker.dll",
        "ipc_net48": "src/TiaBridge.Ipc/bin/Release/net48/TiaBridge.Ipc.dll",
    }.items()}
    versions = {name: digest(path) for name, path in binaries.items()}
    spec = importlib.util.spec_from_file_location("reporter", ROOT/"scripts/report_resilience.py")
    reporter = importlib.util.module_from_spec(spec); spec.loader.exec_module(reporter)
    fingerprint, sources = reporter.source_fingerprint(ROOT)
    write_new(root/"source-hashes.json", sources)
    write_new(root/"metadata.json", dict(run_id=run_id, purpose="FLUSH_CONTROL" if args.flush_control else "FOLLOW_UP_CONTROLS_AND_SEPARATE_GC" if args.follow_up else "SMOKE" if args.smoke else "IPC_HANDLE_INVESTIGATION",
        utc=datetime.now(timezone.utc).isoformat(), base_sha=subprocess.check_output(["git","rev-parse","HEAD"],cwd=ROOT,text=True).strip(),
        source_fingerprint=fingerprint, builds=versions, forced_gc="ONLY_GC-worker" if args.follow_up else False, siemens="FORBIDDEN", window=dict(initial_idle_s=1 if args.smoke else 30, activity_slots=1 if args.flush_control else "F=1,GC=20" if args.follow_up else 2 if args.smoke else 20, post_idle_s=1 if args.smoke else 120)))
    pointer=ROOT/".tia-bridge"/("e2-b1-i-flush-control-current.json" if args.flush_control else "e2-b1-i-follow-up-current.json" if args.follow_up else "e2-b1-i-smoke-"+run_id+".json" if args.smoke else "e2-b1-i-current.json")
    write_new(pointer, dict(run_id=run_id,root=str(root)))
    print(json.dumps(dict(run_id=run_id,root=str(root))),flush=True)
    results=[]
    for name in (("F-no-flush",) if args.flush_control else ("F-poll-only", "F-send-only", "F-timer-only", "GC-worker") if args.follow_up else SCENARIOS):
        folder=root/name;folder.mkdir()
        specfile=folder/"spec.json"
        write_new(specfile,dict(scenario=name,initial_idle_seconds=1 if args.smoke else 30,operations=1 if name.startswith("F-") else 2 if args.smoke else 20,post_idle_seconds=1 if args.smoke else 120,controlled_gc=name=="GC-worker"))
        if {key:digest(path) for key,path in binaries.items()} != versions: raise RuntimeError("Build drift; no next scenario")
        started=time.monotonic()
        print(json.dumps(dict(scenario=name,status="STARTED",utc=datetime.now(timezone.utc).isoformat())),flush=True)
        # No retry. Independent later cases still run after a failed case; every result is retained.
        with (folder/"controller.stdout").open("xb") as stdout, (folder/"controller.stderr").open("xb") as stderr:
            result=subprocess.run(["dotnet",str(binaries["controller"]),str(folder),str(ROOT),"handle-investigation",str(specfile)],cwd=ROOT,stdout=stdout,stderr=stderr)
        row=dict(scenario=name,exit_code=result.returncode,seconds=time.monotonic()-started)
        write_new(folder/"execution.json",row);results.append(row);print(json.dumps(row),flush=True)
    write_new(root/"execution.json",dict(status="PASS" if all(r["exit_code"]==0 for r in results) else "FAIL",results=results))
    return 0 if all(r["exit_code"]==0 for r in results) else 1


if __name__ == "__main__": raise SystemExit(main())
