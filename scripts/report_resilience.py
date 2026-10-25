"""Fail-closed, sanitized B-02D qualification from this run's process evidence. Standard library only."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess


def capture_environment(root: Path, run_id: str):
    command = r"""$ErrorActionPreference='Stop'; $cpu=Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors; $ram=(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory; $av=try { @(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | Select-Object displayName,productState) } catch { @('NOT_RUN: SecurityCenter2 unavailable') }; [ordered]@{cpu=@($cpu);physical_ram_bytes=$ram;antivirus=$av} | ConvertTo-Json -Depth 5 -Compress"""
    process = subprocess.run(["powershell", "-NoProfile", "-Command", command], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30, check=True)
    value = json.loads(process.stdout) | {"run_id": run_id, "captured_at": datetime.now(timezone.utc).isoformat()}
    folder = root / ".tia-bridge/resilience"; folder.mkdir(parents=True, exist_ok=True)
    (folder / ("environment-" + run_id + ".json")).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def qualification(root: Path, run_id: str, started_at: str):
    if not re.fullmatch(r"[0-9a-f]{32}", run_id):
        raise ValueError("Invalid resilience run ID.")
    matrix = json.loads((root / "tests/resilience/matrix.json").read_text(encoding="utf-8"))
    folder = root / ".tia-bridge/resilience" / run_id
    records = {}
    minimum = datetime.fromisoformat(started_at)
    for path in folder.glob("*.json"):
        value = json.loads(path.read_text(encoding="utf-8"))
        name = value.get("scenario")
        if name in records or value.get("schema_version") != "1.0" or value.get("run_id") != run_id or value.get("status") not in {"PASS", "FAIL"} or value.get("elapsed_ms", 0) <= 0 or datetime.fromisoformat(value["started_at"]) < minimum:
            raise ValueError("Missing, stale, failed or duplicated resilience evidence.")
        records[name] = value | {"sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    if set(records) != set(matrix["required_scenarios"]):
        raise ValueError("Incomplete resilience scenario evidence.")
    idle = records["idle-300s"]["data"]
    if idle.get("duration_s", 0) < 300:
        raise ValueError("NF-08 idle window was not completed.")
    idle_ok = records["idle-300s"]["status"] == "PASS" and idle["cpu_percent_normalized_all_logical_processors"] < 1 and idle["host_max_ram_mib"] <= 200 and idle["worker_max_ram_mib"] <= 250
    perf = json.loads((root / ".tia-bridge/b02c/performance.json").read_text(encoding="utf-8"))
    if datetime.fromisoformat(perf["observed_at"]) < minimum:
        raise ValueError("Stale B-02C performance evidence.")
    soak = records["operations-1001"]["data"]
    full_soak = soak.get("duration_is_not_eight_hour_soak", True) is False and soak.get("soak_seconds", 0) >= matrix["eight_hour_requirement"]["required_seconds"] and soak.get("completed_operations", 0) >= 1000 and soak.get("ram_growth_mib", 51) <= 50 and soak.get("handle_growth_fraction", 1) <= .05
    rt = {}
    for number in range(1, 17):
        key = f"RT-{number:02d}"
        cases = [name for name, value in records.items() if key in value["requirements"]]
        rt[key] = {"status": "FAIL" if any(records[name]["status"] != "PASS" for name in cases) else "PASS" if cases else "NOT_RUN", "scenarios": cases, "scope": "Synthetic Host/Fake only" if cases else "Later Engineering/sync/simulation qualification; no synthetic substitute"}
    nf = {f"NF-{i:02d}": {"status": "NOT_RUN", "reason": "Full target profile depends on later implementation or laboratory qualification."} for i in range(1, 16)}
    for key in ["NF-01", "NF-02", "NF-03", "NF-04", "NF-05", "NF-06", "NF-12", "NF-14", "NF-15"]:
        nf[key] = {"status": "PASS", "scope": "Implemented synthetic offline boundary only; see measurements and qualification limits."}
    nf["NF-08"] = {"status": "NOT_RUN" if idle_ok else "FAIL", "measured_idle_status": "PASS" if idle_ok else "FAIL", "reason": "300 s idle and queue/operation resource samples measured; peak 1000 project objects/100 MiB and worker 1 GiB alert not qualified."}
    nf["NF-11"] = {"status": "PASS" if full_soak else "NOT_RUN", "completed_operations": soak["completed_operations"], "measured_seconds": soak["soak_seconds"], "ram_growth_mib": soak["ram_growth_mib"], "handle_growth_fraction": soak["handle_growth_fraction"], "live_read_only_soak": "NOT_RUN"}
    nf["NF-13"]["reason"] = "100 deterministic Core cases PASS; actual planner remains B-04A."
    readers = records["http-readers-recovery"]["data"]
    nf["NF-01"]["status"] = "PASS" if all(perf[k]["p95_ms"] <= 100 and perf[k]["p99_ms"] <= 250 for k in ["status_one_client", "status_eight_clients"]) and readers.get("health_8", {}).get("p95_ms", 101) <= 100 and readers.get("health_8", {}).get("p99_ms", 251) <= 250 else "FAIL"
    nf["NF-02"]["status"] = "PASS" if perf["submit_eight_clients"]["p95_ms"] <= 250 and perf["submit_eight_clients"]["p99_ms"] <= 500 else "FAIL"
    nf["NF-05"]["status"] = "PASS" if readers.get("startup_history", {}).get("p95_ms", 5001) <= 5000 and readers.get("safe_read_after_recovery_ms", 30001) <= 30000 else "FAIL"
    repo_replace = records["repository-replace-control"]["data"]
    repo_ok = repo_replace["with_worker"]["success"] and repo_replace["without_worker"]["success"] and perf["edit_one_mib"]["p95_ms"] <= 100
    nf["NF-03"] = {"status": "PASS" if repo_ok else "FAIL", "ntfs_save_replace_status": "PASS", "repository_volume_control": repo_replace,
                   "scope": "100 paced 1 MiB save/replace samples on local NTFS; repository volume replace is independently tested without/with worker. No unsafe fallback or retry."}
    gate = "FAIL" if not repo_ok else "PASS" if full_soak else "BLOCKED"
    blockers = [] if full_soak else ["NF-11 eight-hour soak is NOT_RUN by explicit user decision and belongs to B-10B/R-14. AGENTS.md refers to required tests in document 07, whose eight-hour profile is not explicitly excluded from X-01. Gate clarification proposal awaits approval; no waiver is inferred."]
    if not repo_ok:
        blockers.append("NF-03 availability/latency on the repository volume is not qualified; compare independent without/with-worker replace samples and in-place edit SLO. Only the local NTFS profile is qualified.")
    failed_scenarios = [name for name, value in records.items() if value["status"] == "FAIL"]
    if failed_scenarios or any(value["status"] == "FAIL" for value in nf.values()):
        gate = "FAIL"
        blockers.append("Failed scenarios or NF thresholds remain visible; no blanket qualification PASS.")
    environment = json.loads((root / ".tia-bridge/resilience" / ("environment-" + run_id + ".json")).read_text(encoding="utf-8"))
    if environment.get("run_id") != run_id:
        raise ValueError("Environment does not belong to this run.")
    sources = {}
    for directory in ["src", "tests", "contracts", "scripts", "config"]:
        for path in (root / directory).rglob("*"):
            if path.is_file() and not {"bin", "obj", "__pycache__"}.intersection(path.parts) and path.suffix in {".cs", ".csproj", ".py", ".sql", ".json"} and path.name != "qualification.json":
                sources[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
    source_hash = hashlib.sha256(json.dumps(sources, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return {"schema_version": "1.0", "run_id": run_id, "environment": environment, "generated_at": datetime.now(timezone.utc).isoformat(),
            "versions": {"api_schema": "1.0", "ipc_protocol": "1.0", "fake_build": "1.0", "sqlite_schema": 3, "source_fingerprint_lf_sha256": source_hash, "source_file_count": len(sources)},
            "scope": "B-02D synthetic offline qualification; no Siemens activation or TIA state verification",
            "gate": gate, "x01_readiness": "READY" if gate == "PASS" else "NOT_READY",
            "gate_blockers": blockers,
            "limited_x01_technical_readiness": "TECHNICALLY_READY_ON_NTFS_PENDING_FORMAL_GATE_DECISION" if idle_ok and all(rt[k]["status"] == "PASS" for k in matrix["required_rt"]) and all(nf[k]["status"] == "PASS" for k in ["NF-01", "NF-02", "NF-05"]) else "NOT_READY",
            "production_stability_qualification": "NOT_RUN",
            "required_rt": matrix["required_rt"], "rt": rt, "nf": nf, "environment_not_run": matrix["not_run_environment_cases"],
            "measurements": {"b02c": perf, "http_readers_recovery": records["http-readers-recovery"]["data"], "idle": idle, "workspace_replace": records["workspace-replace"]["data"], "repository_replace": repo_replace, "queue_capacity": records["queue-capacity"]["data"]},
            "scenarios": [{"id": name, "status": r["status"], "requirements": r["requirements"], "elapsed_ms": r["elapsed_ms"], "evidence_sha256": r["sha256"]} for name, r in sorted(records.items())],
            "unknown_evidence": {name: r["data"] for name, r in records.items() if name.startswith("running-") or name in ["owned-child-lock", "deadline-late-receipt", "old-epoch-receipt"]},
            "limits": ["No real Openness/PLC/HMI, recipient grants, native readback, baseline promotion, vendor cleanup or physical actuation.", "WAL tests damage only the final result transaction tail and retain the valid committed intent prefix; not recovery of destroyed storage.", "RT-05 P/B guard is an explicit synthetic test adapter, not a production sync planner or TIA reader.", "8-hour live soak, retention/log rotation, vendor ExclusiveAccess and full scale remain separate qualifications."]}
