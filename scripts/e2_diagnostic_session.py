"""E2-B1-D local orchestration. Python 3.12 + psutil (optional live dependency).

No Siemens library, grant creation or retry. Uses deployed Host/Scheduler/CLI only.
Prepare reviewed private run-root/binding/config, new authorized B0 1.1 and gate first.
Control files start-host and go-01..03 are explicit local operator checkpoints.
Outputs contain private project evidence: keep outside Git. --help performs no work.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import threading
import time
import traceback


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def new(path, value):
    with Path(path).open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, indent=2, ensure_ascii=False); stream.flush(); os.fsync(stream.fileno())


def check_entry(gate, auth, config, binding):
    if (gate.get("status"), gate.get("task"), gate.get("purpose"), gate.get("maximum_cycles")) != ("READY_FOR_OPERATOR_AUTHORIZATION", "E2-G-D", "HANDLE_DIAGNOSTICS", 3):
        raise RuntimeError("DIAGNOSTIC_GATE_REQUIRED")
    if (auth.get("schema_version"), auth.get("purpose"), auth.get("plan_id"), auth.get("maximum_cycles"), auth.get("project_name")) != ("1.1", "HANDLE_DIAGNOSTICS", "E2-B1-D", 3, "T05_RecipeSim_V19_Testowy"):
        raise RuntimeError("DIAGNOSTIC_AUTHORIZATION_REQUIRED")
    if config.get("qualification_bootstrap", {}).get("authorization_id") != auth["authorization_id"]:
        raise RuntimeError("AUTHORIZATION_ID_MISMATCH")
    if any(auth.get(key) != value for key, value in binding.items()):
        raise RuntimeError("AUTHORIZATION_BINDING_MISMATCH")


def check_phases(records, operation, epoch, pid, start):
    own = [r for r in records if r["operation_id"] == operation]
    if any((r["worker_epoch"], r["worker_pid"], r["worker_start_ticks"]) != (epoch, pid, start) for r in records):
        raise RuntimeError("PHASE_BINDING")
    if [r["sequence"] for r in records] != list(range(1, len(records) + 1)):
        raise RuntimeError("PHASE_SEQUENCE")
    phases = [r["phase"] for r in own]
    required = ["cycle.enter", "enumeration.returned", "attach.enter", "attach.returned", "project.checked", "devices.enter", "devices.returned",
                "inventory.returned", "dispose.enter", "dispose.returned", "cycle.returned"]
    positions = [phases.index(p) if phases.count(p) == 1 else -1 for p in required]
    if -1 in positions or positions != sorted(positions):
        raise RuntimeError("PHASE_ORDER_OR_MISSING")
    if not {"device_items.traversed", "device.classified", "worker.idle"}.issubset(phases) or not any(p in phases for p in ("software.returned.null", "software.returned.present")):
        raise RuntimeError("PHASE_COVERAGE")
    if phases.index("worker.idle") <= phases.index("cycle.returned"):
        raise RuntimeError("PHASE_IDLE")


def check_project_artifacts(raw, discovery, binding):
    # Worker wire DTO uses a UUID string; Core Discovery serializes ProjectKey.Value.
    # Accept these exact contracts, never arbitrary nested objects or fallback names.
    if raw.get("project_key") != binding["project_key"] or discovery.get("project_key") != {"value": binding["project_key"]}:
        raise RuntimeError("PROJECT_BINDING")
    for artifact in (raw, discovery):
        if artifact.get("is_modified_before") is not False or artifact.get("is_modified_after") is not False:
            raise RuntimeError("DIRTY_PROJECT")
        if artifact.get("project_path") != binding["project_path"] or artifact.get("project_name") != "T05_RecipeSim_V19_Testowy":
            raise RuntimeError("PROJECT_BINDING")


class Session:
    def __init__(self, root):
        import psutil
        self.psutil = psutil
        self.root = Path(root).resolve(strict=True)
        self.root.relative_to(Path(os.environ["LOCALAPPDATA"]) / "TIA_Bridge2" / "dev" / "e2-b1-d")
        self.binding = read(self.root / "binding.json")
        self.config = read(self.root / "host-config.json")
        self.state = Path(self.config["state_path"])
        if self.state != self.root / "state": raise RuntimeError("STATE_PATH")
        self.control = self.root / "control"; self.control.mkdir()
        self.started = time.monotonic(); self.lock = threading.Lock()
        self.end_monitor = threading.Event(); self.monitor_failed = threading.Event()
        self.host = None; self.worker = None; self.cycle = 0; self.operation = None
        self.shutting_down = False; self.safe_to_stop = True; self.graph = None
        self.thread = None
        self.lines = (self.root / "telemetry.jsonl").open("x", encoding="utf-8")

    def emit(self, value): print(json.dumps(value), flush=True)

    def sample(self, point):
        ps = self.psutil
        tia = ps.Process(self.binding["tia_pid"])
        if abs(tia.create_time() - (self.binding["tia_start_ticks"] - 621355968000000000) / 1e7) > .001 or tia.exe().casefold() != self.binding["executable_path"].casefold():
            raise RuntimeError("TIA_PROCESS_DRIFT")
        processes = [("tia", tia)]
        if self.host is not None and self.host.poll() is None:
            hp = ps.Process(self.host.pid); processes.append(("host", hp))
            workers = [p for p in hp.children(recursive=True) if p.name().casefold() == "tiabridge.worker.v19.exe"]
            if len(workers) > 1: raise RuntimeError("MULTIPLE_WORKERS")
            if workers:
                wp = workers[0]; identity = (wp.pid, wp.create_time())
                if self.worker is not None and self.worker != identity: raise RuntimeError("WORKER_RESTART")
                if Path(wp.exe()) != self.root / "worker" / "TiaBridge.Worker.V19.exe": raise RuntimeError("WORKER_PATH")
                self.worker = identity; processes.append(("worker", wp))
            elif self.worker is not None and not self.shutting_down: raise RuntimeError("WORKER_LOSS")
        elif self.host is not None and not self.shutting_down: raise RuntimeError("HOST_LOSS")
        row = dict(utc=datetime.now(timezone.utc).isoformat(), elapsed_ms=(time.monotonic()-self.started)*1000,
                   point=point, cycle=self.cycle, operation_id=self.operation, processes=[])
        for role, p in processes:
            with p.oneshot():
                mem = p.memory_info(); cpu = p.cpu_times()
                row["processes"].append(dict(role=role, pid=p.pid, start_unix=p.create_time(), handles=p.num_handles(),
                    working_set_bytes=mem.rss, private_bytes=mem.private, cpu_ms=(cpu.user+cpu.system)*1000))
        with self.lock:
            self.lines.write(json.dumps(row)+"\n"); self.lines.flush()

    def monitor(self):
        while not self.end_monitor.is_set():
            try: self.sample("interval")
            except Exception as error:
                new(self.root / "monitor-error.json", dict(error=repr(error))); self.monitor_failed.set(); return
            self.end_monitor.wait(1)

    def guard(self):
        if self.monitor_failed.is_set() or (self.control / "stop").exists(): raise RuntimeError("MONITOR_OR_OPERATOR_STOP")

    def wait(self, name):
        while not (self.control / name).exists(): self.guard(); time.sleep(.2)

    def cli(self, args, path, allow_failure=False):
        result = subprocess.run(["dotnet", str(self.root/"cli/TiaBridge.Cli.dll"), *args, "--discovery", str(self.state/"api/host.json"), "--json"], capture_output=True, timeout=30)
        with Path(path).open("xb") as stream: stream.write(result.stdout)
        if result.returncode and not allow_failure: raise RuntimeError("CLI_FAILED_"+str(result.returncode))
        if result.stderr: raise RuntimeError("CLI_STDERR")
        return json.loads(result.stdout)

    def snapshot(self, name):
        folder = Path(self.binding["project_path"]).parent; rows = []
        for path in sorted(folder.rglob("*")):
            if path.is_symlink() or path.is_junction(): raise RuntimeError("PROJECT_REPARSE")
            if path.is_file():
                stat = path.stat(); row = dict(path=str(path.relative_to(folder)), bytes=stat.st_size, mtime_ns=stat.st_mtime_ns)
                if path.suffix.casefold() == ".ap19": row["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
                rows.append(row)
        new(self.root/name, dict(utc=datetime.now(timezone.utc).isoformat(), files=rows, hash_scope="AP19 only; other files metadata; no Siemens DB parsing"))

    def run_cycle(self, number):
        if number not in (1, 2, 3): raise RuntimeError("THREE_CYCLE_BOUNDARY")
        self.guard(); self.cycle = number; self.operation = None; self.sample("before-cycle")
        folder = self.root/"operations"/f"cycle-{number:02d}"; folder.mkdir(parents=True)
        request = dict(schema_version="1.0", method="project.inspect", project_key=self.binding["project_key"],
            idempotency_key=self.root.name+f"-diagnostic-{number:02d}", params=dict(inventory_scope="all_supported", fresh=True, timeout_s=120, queue_timeout_s=300))
        new(folder/"request.json", request); self.safe_to_stop = False
        accepted = self.cli(["submit", "project.inspect", "--request", str(folder/"request.json")], folder/"acceptance.json")
        self.operation = accepted["operation_id"]; self.emit(dict(status="ACCEPTED", cycle=number, operation_id=self.operation))
        deadline = time.monotonic()+150; poll = 0
        while True:
            self.guard()
            status = self.cli(["operation", "status", self.operation], folder/f"status-{poll:03d}.json")
            self.cli(["health"], folder/f"health-{poll:03d}.json")
            if status["state"] in ("SUCCEEDED", "FAILED", "CANCELLED", "RECONCILIATION_REQUIRED"): break
            if time.monotonic() > deadline: raise RuntimeError("OBSERVATION_TIMEOUT_NATIVE_STATE_UNKNOWN")
            time.sleep(max(.1, min(5, status.get("poll_after_ms", 1000)/1000))); poll += 1
        result = self.cli(["operation", "result", self.operation], folder/"result.json", True)
        self.cli(["operation", "events", self.operation, "--limit", "1000"], folder/"events.json")
        if (result["state"], result["outcome"], len(result["artifact_refs"])) != ("SUCCEEDED", "CONFIRMED", 2): raise RuntimeError("CYCLE_NOT_CONFIRMED")
        artifacts = []
        for ref in result["artifact_refs"]:
            aid = ref["artifact_id"]
            if not re.fullmatch(r"artifact-[0-9a-f]{32}", aid): raise RuntimeError("ARTIFACT_ID")
            path = self.state/"artifacts"/(aid+".blob")
            if path.is_symlink() or path.is_junction(): raise RuntimeError("ARTIFACT_REPARSE")
            data = path.read_bytes()
            if hashlib.sha256(data).hexdigest() != ref["sha256"]: raise RuntimeError("ARTIFACT_HASH")
            with (folder/(aid+".json")).open("xb") as stream: stream.write(data)
            artifacts.append(json.loads(data))
        raw = next(a for a in artifacts if "tia_pid" in a); discovery = next(a for a in artifacts if "profile_id" in a)
        check_project_artifacts(raw, discovery, self.binding)
        if (raw["tia_pid"], raw["tia_start_ticks"], raw["api_build"], raw["apartment"], raw["portal_alive_after_dispose"]) != (self.binding["tia_pid"], self.binding["tia_start_ticks"], self.binding["tia_build"], "STA", True): raise RuntimeError("RAW_BINDING")
        audit_files = list(self.state.glob("v19-audit-*.jsonl")); trace_files = list(self.state.glob("v19-resources-*.jsonl"))
        if len(audit_files) != 1 or len(trace_files) != 1: raise RuntimeError("EPOCH_OR_TRACE_COUNT")
        audit = [json.loads(s) for s in audit_files[0].read_text().splitlines()]
        epoch = audit[0]["worker_epoch"]
        if not any(a["operation_id"] == self.operation and a["call"] == "TiaPortal.DisposeAttachedSession.Returned" for a in audit): raise RuntimeError("DISPOSE_UNCONFIRMED")
        self.safe_to_stop = True; self.sample("after-cycle")
        graph = {k:discovery[k] for k in ("devices", "nodes", "coverage")}
        if self.graph is not None and graph != self.graph: raise RuntimeError("DISCOVERY_DRIFT")
        self.graph = graph
        self.sample("before-short-idle")
        for _ in range(10): self.guard(); time.sleep(1)
        self.sample("after-short-idle")
        trace = [json.loads(s) for s in trace_files[0].read_text().splitlines()]
        wp = next(r for r in trace if r["phase"] == "worker.start")
        check_phases(trace, self.operation, epoch, wp["worker_pid"], wp["worker_start_ticks"])
        new(folder/"verified.json", dict(cycle=number, operation_id=self.operation, raw=raw, discovery=discovery, phase_records=len(trace)))
        self.emit(dict(status="VERIFIED", cycle=number, operation_id=self.operation, handles=trace[-1]["handles"], attach_ms=raw["attach_ms"], dispose_ms=raw["dispose_ms"]))

    def shutdown(self):
        self.shutting_down = True
        if self.host is not None:
            if self.host.poll() is None:
                self.host.stdin.write(b"stop\n"); self.host.stdin.flush(); self.host.wait(timeout=10)
            alive = self.worker is not None and self.psutil.pid_exists(self.worker[0]) and self.psutil.Process(self.worker[0]).create_time() == self.worker[1]
            new(self.root/"release.json", dict(host_pid=self.host.pid, host_exit=self.host.returncode, worker_identity=self.worker, worker_ended=not alive))
            if self.host.returncode != 0 or alive: raise RuntimeError("RELEASE_UNCONFIRMED")
        self.end_monitor.set()
        if self.thread is not None:
            self.thread.join(3)
            if self.thread.is_alive(): raise RuntimeError("MONITOR_RELEASE_UNCONFIRMED")
        self.lines.close(); self.snapshot("project-files-after.json")

    def run(self):
        self.snapshot("project-files-before.json")
        self.thread = threading.Thread(target=self.monitor, daemon=True); self.thread.start()
        new(self.root/"monitor-ready.json", dict(pid=os.getpid(), psutil=self.psutil.__version__, interval_s=1))
        self.emit(dict(status="MONITOR_READY")); self.wait("start-host")
        auth = read(self.state/"qualification-bootstrap/authorization.json")
        check_entry(read(self.root/"gate.json"), auth, self.config, self.binding)
        env = os.environ.copy(); env["TIA_BRIDGE_V19_RESOURCE_TRACE"] = "1"
        self.host = subprocess.Popen(["dotnet", str(self.root/"host/TiaBridge.Host.dll"), "--config", str(self.root/"host-config.json"), "--stdio"], env=env,
            stdin=subprocess.PIPE, stdout=(self.root/"host.stdout").open("xb"), stderr=(self.root/"host.stderr").open("xb"))
        new(self.root/"host-process.json", dict(pid=self.host.pid, start_unix=self.psutil.Process(self.host.pid).create_time()))
        for _ in range(100):
            if (self.state/"api/host.json").exists(): break
            if self.host.poll() is not None: raise RuntimeError("HOST_START_FAILED")
            time.sleep(.1)
        self.cli(["health"], self.root/"health-initial.json"); self.emit(dict(status="HOST_READY", pid=self.host.pid))
        for number in (1, 2, 3): self.wait(f"go-{number:02d}"); self.run_cycle(number)
        self.sample("before-final-idle")
        for _ in range(30): self.guard(); time.sleep(1)
        self.sample("after-final-idle"); self.cli(["health"], self.root/"health-final.json")
        self.shutdown(); self.emit(dict(status="RELEASED", cycles=3))


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("--run-root", required=True)
    args = parser.parse_args(); session = Session(args.run_root)
    try: session.run()
    except Exception as error:
        new(session.root/"session-stop.json", dict(cycle=session.cycle, operation_id=session.operation, error=repr(error), traceback=traceback.format_exc(), safe_to_stop=session.safe_to_stop, retry=False))
        session.emit(dict(status="STOP", error=repr(error), safe_to_stop=session.safe_to_stop))
        if session.safe_to_stop: session.shutdown(); return 1
        # Preserve stdin and the unknown native call. No timeout/exit is proof of release.
        while not (session.control/"reviewed-safe-stop").exists(): time.sleep(1)
        session.shutdown(); return 1
    return 0


if __name__ == "__main__": raise SystemExit(main())
