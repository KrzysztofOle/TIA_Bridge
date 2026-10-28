"""UTC correlation, bounded diagnostic summaries and complete redacted log files."""
from __future__ import annotations

import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

LEVELS = {"TRACE": 0, "DEBUG": 1, "INFO": 2, "WARN": 3, "ERROR": 4}


def utc():
    return datetime.now(timezone.utc).isoformat()


def redact(text):
    text = re.sub(r"(?i)(bearer\s+)[^\s\"']+", r"\1[REDACTED]", text)
    text = re.sub(r"(?i)((?:token|password|secret|authorization|credential|api_key)[\"']?\s*[:=]\s*[\"']?)[^\s,\"'}]+", r"\1[REDACTED]", text)
    return re.sub(r"(https?://)[^\s/@]+:[^\s/@]+@", r"\1[REDACTED]@", text)


def write_json(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write("\n")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def inventory(folder):
    result = {}
    for path in sorted(folder.rglob("*")):
        if path.is_file():
            if not path.resolve().is_relative_to(folder.resolve()):
                raise ValueError("Evidence path escaped run directory")
            if path != folder / "evidence-manifest.json":
                result[path.relative_to(folder).as_posix()] = {"bytes": path.stat().st_size, "sha256": digest(path)}
    return result


def verify_manifest(folder):
    from .loader import read_json, require
    manifest = read_json(folder / "evidence-manifest.json", 16 * 1024 * 1024)
    require(manifest["files"] == inventory(folder), "Evidence manifest integrity failure")
    return manifest


class Logger:
    def __init__(self, path, run_id, level="INFO"):
        self.stream = path.open("x", encoding="utf-8", newline="\n")
        self.run_id, self.level = run_id, level

    def event(self, event_id, message, level="INFO", **context):
        if LEVELS[level] < LEVELS[self.level]:
            return
        entry = dict(timestamp_utc=utc(), level=level, application="qa", component="runner",
                     event_id=event_id, message=redact(message), run_id=self.run_id,
                     scenario_id=None, step_id=None, operation_id=None, worker_pid=None,
                     worker_epoch=None, duration_ms=None, exception_type=None, error_code=None)
        entry.update(context)
        self.stream.write(json.dumps(entry, ensure_ascii=False, allow_nan=False) + "\n")
        self.stream.flush()

    def close(self):
        self.stream.close()


def correlate(source, logger, scenario_id, step_id):
    """Import existing JSONL identities, never infer missing operation/epoch values."""
    with source.open(encoding="utf-8", errors="replace") as stream:
        for index, line in enumerate(stream):
            if index >= 4096:
                logger.event("QA_LOG_LIMIT", "Correlation limited to 4096 entries; source log retained", level="WARN")
                break
            try:
                value = json.loads(line)
                if not isinstance(value, dict):
                    continue
                context = {key: value[key] for key in ["operation_id", "worker_pid", "worker_epoch", "duration_ms", "exception_type", "error_code"] if key in value and isinstance(value[key], (str, int, float, type(None)))}
                logger.event("QA_APPLICATION_LOG", str(value.get("message", value.get("phase", "Existing application event")))[:2000],
                             scenario_id=scenario_id, step_id=step_id, source_timestamp_utc=value.get("timestamp_utc", value.get("utc")),
                             log_reference=str(source.name), **context)
            except (ValueError, TypeError):
                continue
