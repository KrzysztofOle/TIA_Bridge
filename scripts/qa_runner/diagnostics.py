"""UTC correlation, bounded diagnostic summaries and complete redacted log files."""
from __future__ import annotations

import hashlib
import json
import re
from collections import defaultdict
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


ALIASES = {
    "schema_version": ("schema_version",), "timestamp_utc": ("timestamp_utc", "utc", "at", "occurred_at"),
    "level": ("level", "severity"), "application": ("application",), "component": ("component",),
    "event_id": ("event_id", "event", "phase", "call"), "message": ("message",),
    "run_id": ("run_id",), "scenario_id": ("scenario_id",), "step_id": ("step_id",),
    "operation_id": ("operation_id",), "worker_epoch": ("worker_epoch",), "duration_ms": ("duration_ms",),
    "error_code": ("error_code", "hresult"),
}
REQUIRED = ("schema_version", "timestamp_utc", "level", "application", "component", "event_id", "message")


def _records(path):
    """Read JSONL or a JSON object/array; free-form text is never parsed."""
    content = path.read_text(encoding="utf-8-sig", errors="strict")
    try:
        value = json.loads(content)
    except json.JSONDecodeError:
        for number, line in enumerate(content.splitlines(), 1):
            if not line.strip():
                continue
            try:
                yield number, json.loads(line)
            except json.JSONDecodeError as error:
                yield number, error
        return
    for number, record in enumerate(value if isinstance(value, list) else [value], 1):
        yield number, record


def normalize_logs(sources, destination):
    events, invalid = [], []
    for source, reference in sources:
        try:
            records = _records(source)
            for number, record in records:
                if isinstance(record, Exception):
                    invalid.append({"log_reference": reference, "record": number, "reason": "INVALID_JSON"})
                    continue
                if not isinstance(record, dict):
                    invalid.append({"log_reference": reference, "record": number, "reason": "RECORD_NOT_OBJECT"})
                    continue
                event = {"schema_version": "1.0"}
                originals = {}
                absent, unavailable = [], []
                for target, aliases in ALIASES.items():
                    key = next((name for name in aliases if name in record), None)
                    if key is None:
                        if target not in {"schema_version"}:
                            absent.append(target)
                        continue
                    value = record[key]
                    originals[target] = redact(value) if isinstance(value, str) else value
                    if value is None:
                        unavailable.append(target)
                    if target != "schema_version":
                        event[target] = (str(value) if target == "error_code" and type(value) is int
                                         else redact(value) if isinstance(value, str) else value)
                originals["source_schema_version"] = originals.pop("schema_version", None)
                for field in ALIASES:
                    if field != "schema_version":
                        event.setdefault(field, None)
                event["absent_fields"] = sorted(set(absent))
                event["unavailable_fields"] = sorted(set(unavailable))
                event["original_values"] = originals
                event["log_reference"] = reference
                event["record_number"] = number
                event["message"] = redact(str(event.get("message", "")))[:2000]
                if any(event.get(field) is not None and not isinstance(event.get(field), str) for field in REQUIRED):
                    invalid.append({"log_reference": reference, "record": number, "reason": "REQUIRED_FIELD_TYPE"})
                    continue
                optional_types = {"run_id": str, "scenario_id": str, "step_id": str, "operation_id": str,
                                  "worker_epoch": str, "error_code": str}
                malformed = any(event.get(field) is not None and not isinstance(event[field], kind)
                                for field, kind in optional_types.items())
                malformed |= event.get("duration_ms") is not None and (type(event["duration_ms"]) not in (int, float))
                if malformed:
                    invalid.append({"log_reference": reference, "record": number, "reason": "OPTIONAL_FIELD_TYPE"})
                    continue
                if isinstance(event.get("timestamp_utc"), str) and event["timestamp_utc"].endswith("+00:00"):
                    event["timestamp_utc"] = event["timestamp_utc"][:-6] + "Z"
                events.append(event)
        except (OSError, UnicodeError):
            invalid.append({"log_reference": reference, "record": 0, "reason": "UNREADABLE_SOURCE"})
    with destination.open("x", encoding="utf-8", newline="\n") as stream:
        for event in events:
            stream.write(json.dumps(event, ensure_ascii=False, allow_nan=False) + "\n")
    return summarize_events(events, invalid)


def summarize_events(events, invalid):
    ids = ("run_id", "operation_id", "worker_epoch", "scenario_id", "step_id")
    groups = {key: defaultdict(list) for key in ids}
    errors = defaultdict(int)
    for event in events:
        for key in ids:
            value = event.get(key)
            if value is not None and value != "":
                groups[key][str(value)].append(event)
        if event.get("level") in {"WARN", "ERROR"}:
            errors[str(event.get("error_code") or event.get("event_id") or "UNKNOWN")] += 1
    def ordered(rows):
        return sorted(rows, key=lambda item: str(item.get("timestamp_utc", "")))
    correlations = {}
    for key, values in groups.items():
        correlations[key] = {identity: [
            {"timestamp_utc": item.get("timestamp_utc"), "component": item.get("component", "UNKNOWN"),
             "event_id": item.get("event_id", "UNKNOWN"), "log_reference": item["log_reference"]}
            for item in ordered(rows)] for identity, rows in values.items()}
    operation_flows = {}
    stages = {"Host": "UNKNOWN", "Scheduler": "UNKNOWN", "IPC": "UNKNOWN", "Worker": "UNKNOWN", "Result": "UNKNOWN"}
    for identity, rows in groups["operation_id"].items():
        flow = dict(stages)
        for item in ordered(rows):
            component = item.get("component")
            if component in flow:
                flow[component] = {"event_id": item.get("event_id"), "timestamp_utc": item.get("timestamp_utc"),
                                   "log_reference": item["log_reference"]}
        operation_flows[identity] = flow
    return {"count": len(events), "warn_error": sum(x.get("level") in {"WARN", "ERROR"} for x in events),
            "without_correlation_ids": sum(not any(x.get(key) for key in ids) for x in events),
            "invalid_records": invalid, "error_groups": dict(sorted(errors.items())), "correlations": correlations,
            "operation_flows": operation_flows,
            "top_events": [{"timestamp_utc": x.get("timestamp_utc"), "level": x.get("level"), "component": x.get("component"),
                            "event_id": x.get("event_id"), "message": x.get("message"), "log_reference": x["log_reference"]}
                           for x in sorted((e for e in events if e.get("event_id")),
                                           key=lambda e: (e.get("level") not in {"ERROR", "WARN"}, str(e.get("timestamp_utc", ""))))[:20]]}
