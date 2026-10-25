"""Generate B-01A wire models, result schemas, OpenAPI and synthetic examples.

Python 3.12 standard library only. Default: check reproducibility; --write: update.
Never loads Siemens, executes requests or resolves remote schemas.
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "contracts/v1"
UUID = {"type": "string", "format": "uuid"}
TOKEN = {"type": "string", "minLength": 1, "maxLength": 128,
         "pattern": "^[A-Za-z0-9][A-Za-z0-9_.:-]*$"}
HASH = {"type": "string", "pattern": "^[a-f0-9]{64}$"}
COUNT = {"type": "integer", "minimum": 0, "maximum": 9223372036854775807}
BOOL = {"type": "boolean"}
TIME = {"type": "string", "format": "date-time"}
TEXT = {"type": "string", "maxLength": 2048}
STATES = ["ACCEPTED", "QUEUED", "RUNNING", "SUCCEEDED", "FAILED", "CANCELLED", "RECONCILIATION_REQUIRED"]
OUTCOMES = ["NOT_STARTED", "CONFIRMED", "UNKNOWN"]
COMMON_ERRORS = "INVALID_ARGUMENT SCHEMA_VERSION UNAUTHENTICATED ACCESS_DENIED AUTHORIZATION_EXPIRED PHYSICAL_DOWNLOAD_DENIED NOT_FOUND CONFLICT STALE_PLAN IDEMPOTENCY_MISMATCH PROJECT_QUARANTINED BASELINE_RACE LIMIT_EXCEEDED UNSUPPORTED VALIDATION_FAILED QUEUE_FULL STORE_UNAVAILABLE WORKER_UNAVAILABLE CIRCUIT_OPEN TIMEOUT TIMED_OUT VENDOR_CALL_TIMEOUT WAIT_TIMEOUT RESULT_NOT_READY CURSOR_INVALID DEGRADED".split()
CATEGORIES = "VALIDATION AUTHORIZATION IDENTITY CONFLICT CAPABILITY STORAGE TRANSPORT EXECUTION INTERNAL".split()


def obj(properties, required=None):
    return {"type": "object", "additionalProperties": False,
            "required": list(properties) if required is None else required, "properties": properties}


def array(item, maximum=10000):
    return {"type": "array", "items": item, "maxItems": maximum}


def enum(*values):
    return {"type": "string", "enum": list(values)}


def ref(name):
    return {"$ref": f"#/$defs/{name}"}


def pascal(value):
    return "".join(part[:1].upper() + part[1:] for part in value.replace(".", "_").split("_"))


def document(name, **kwargs):
    return {"$schema": "https://json-schema.org/draft/2020-12/schema",
            "$id": f"urn:tia-bridge2:{name}:1", **kwargs}


def lifecycle_rules():
    # The same lifecycle constrains the target operation in status/result projections.
    return [
        {"if": {"properties": {"state": {"const": "SUCCEEDED"}}, "required": ["state"]}, "then": {"properties": {"outcome": {"const": "CONFIRMED"}}}},
        {"if": {"properties": {"state": {"const": "RECONCILIATION_REQUIRED"}}, "required": ["state"]}, "then": {"properties": {"outcome": {"const": "UNKNOWN"}}}},
        {"if": {"properties": {"state": {"enum": ["ACCEPTED", "QUEUED"]}}, "required": ["state"]}, "then": {"properties": {"outcome": {"const": "NOT_STARTED"}}}},
        {"if": {"properties": {"outcome": {"const": "UNKNOWN"}}, "required": ["outcome"]}, "then": {"properties": {"state": {"enum": ["RUNNING", "RECONCILIATION_REQUIRED"]}}}},
    ]


def build(request, registry):
    methods = [r["name"] for r in registry["operations"]]
    codes = sorted(set(COMMON_ERRORS).union(*(r["error_codes"] for r in registry["operations"])))
    defs = {
        "ArtifactRef": obj({"artifact_id": TOKEN, "sha256": HASH}),
        "ObjectStatus": obj({"object_id": UUID, "status": enum("SYNCED", "MODIFIED", "CONFLICT", "UNSUPPORTED", "ERROR", "UNKNOWN"), "reason_code": TOKEN}),
        "DeviceSummary": obj({"device_id": TOKEN, "family": TOKEN}),
        "InventoryItem": obj({"object_id": UUID, "kind": TOKEN, "capability": enum("UNSUPPORTED", "UNKNOWN", "QUALIFIED")}),
        "FileChange": obj({"object_id": UUID, "kind": enum("MODIFIED", "ADDED", "MISSING", "TOMBSTONE", "INVALID")}),
        "DimensionEvidence": obj({"dimension": enum("content", "config", "api", "structure", "dependencies", "inventory"), "status": enum("PASS", "FAIL", "UNKNOWN", "UNSUPPORTED"), "evidence_refs": array(TOKEN)}),
        "StepReceipt": obj({"step_id": TOKEN, "outcome": enum(*OUTCOMES), "evidence_refs": array(TOKEN)}),
        "CompilerDiagnostic": obj({"severity": enum("ERROR", "WARNING", "INFO"), "code": TOKEN, "message": TEXT, "object_id": UUID}, ["severity", "code", "message"]),
        "AssertionSummary": obj({"test_id": TOKEN, "status": enum("PASS", "FAIL", "ERROR", "BLOCKED", "NOT_RUN", "SKIP"), "evidence_refs": array(TOKEN)}),
        "OperationEvent": obj({"sequence": COUNT, "operation_id": UUID, "state": enum(*STATES), "phase": TOKEN, "occurred_at": TIME, "evidence_refs": array(TOKEN)}),
        "ComponentHealth": obj({"component": TOKEN, "status": enum("HEALTHY", "DEGRADED", "UNAVAILABLE", "UNKNOWN"), "age_ms": {"type": ["integer", "null"], "minimum": 0, "maximum": 9223372036854775807}}),
    }
    artifacts = array(ref("ArtifactRef"))
    defs.update({
        "ProjectInspectResult": obj({"project_key": UUID, "session_epoch": UUID, "api_build": TOKEN, "devices": array(ref("DeviceSummary")), "inventory": array(ref("InventoryItem")), "coverage_refs": array(TOKEN)}),
        "ProjectExportResult": obj({"snapshot_id": TOKEN, "manifest": ref("ArtifactRef"), "item_statuses": array(ref("ObjectStatus"))}),
        "ProjectStatusResult": obj({"observed_at": TIME, "session_epoch": UUID, "age_ms": COUNT, "quarantined": BOOL, "objects": array(ref("ObjectStatus")), "freshness_refs": array(TOKEN)}),
        "FilesChangesResult": obj({"workspace_id": UUID, "source_revision": HASH, "changes": array(ref("FileChange"))}),
        "SyncPlanResult": obj({"plan_id": TOKEN, "plan_sha256": HASH, "snapshot_id": TOKEN, "eligible": BOOL, "issues": array(TOKEN), "plan_artifact": ref("ArtifactRef")}),
        "SyncApplyResult": obj({"steps": array(ref("StepReceipt")), "workspace_advanced": BOOL, "evidence_refs": array(TOKEN), "verified_revision": TOKEN}, ["steps", "workspace_advanced", "evidence_refs"]),
        "SyncVerifyResult": obj({"objects": array(ref("ObjectStatus")), "dimensions": array(ref("DimensionEvidence")), "promoted": BOOL, "baseline_revision": TOKEN}, ["objects", "dimensions", "promoted"]),
    })
    compiler = obj({"status": enum("PASS", "FAIL", "ERROR", "NOT_RUN"), "error_count": COUNT, "warning_count": COUNT, "fingerprint": HASH, "diagnostics": array(ref("CompilerDiagnostic")), "elapsed_ms": COUNT})
    defs["CompilePlcResult"] = copy.deepcopy(compiler)
    defs["CompileHmiResult"] = copy.deepcopy(compiler)
    defs["SimulationPrepareResult"] = obj({"instance_id": TOKEN, "lease_id": TOKEN, "program_fingerprint": HASH, "isolation_evidence_refs": array(TOKEN)})
    defs["SimulationStartResult"] = obj({"instance_id": TOKEN, "lease_id": TOKEN, "state": enum("STARTING", "RUNNING", "PROGRAM_LOADED", "READY", "NOT_READY", "ERROR")})
    defs["SimulationStopResult"] = obj({"instance_id": TOKEN, "state": enum("STOPPED", "STOP_UNCONFIRMED", "ERROR"), "cleanup_refs": array(TOKEN)})
    test = obj({"status": enum("PASS", "FAIL", "ERROR", "BLOCKED", "NOT_RUN", "SKIP"), "assertions": array(ref("AssertionSummary")), "report": ref("ArtifactRef"), "environment_refs": array(TOKEN)})
    defs["TestPlcResult"] = obj({**copy.deepcopy(test["properties"]), "transport": enum("opcua", "snap7"), "mapping_sha256": HASH})
    defs["TestHmiResult"] = obj({**copy.deepcopy(test["properties"]), "browser_version": TOKEN, "runtime_version": TOKEN})
    defs["OperationStatusResult"] = obj({"operation_id": UUID, "state": enum(*STATES), "phase": TOKEN, "outcome": enum(*OUTCOMES), "cancel_requested": BOOL})
    defs["OperationResultResult"] = obj({"operation_id": UUID, "state": enum(*STATES), "outcome": enum(*OUTCOMES), "artifact_refs": artifacts})
    for name in ["OperationStatusResult", "OperationResultResult"]:
        defs[name]["allOf"] = lifecycle_rules()
    defs["OperationEventsResult"] = obj({"operation_id": UUID, "events": array(ref("OperationEvent"), 1000), "next_sequence": COUNT})
    defs["OperationCancelResult"] = obj({"operation_id": UUID, "disposition": enum("CANCELLED", "CANCEL_REQUESTED", "CANNOT_INTERRUPT", "ALREADY_TERMINAL"), "state": enum(*STATES)})
    defs["DiagnosticsHealthResult"] = obj({"components": array(ref("ComponentHealth")), "observed_at": TIME})
    result = document("operation-result", **{"oneOf": [obj({"method": {"const": m}, "result": ref(pascal(m) + "Result")}) for m in methods], "$defs": defs})
    error = document("error", **obj({"code": enum(*codes), "category": enum(*CATEGORIES), "message": TEXT, "retryable": BOOL, "outcome": enum(*OUTCOMES), "phase": TOKEN, "evidence_refs": array(TOKEN), "recovery_hint": TEXT}, ["code", "category", "message", "retryable", "outcome"]))
    error["allOf"] = [{"if": {"properties": {"retryable": {"const": True}}, "required": ["retryable"]}, "then": {"properties": {"outcome": {"const": "NOT_STARTED"}}}}]
    envelope = document("operation", **obj({"schema_version": {"const": "1.0"}, "operation_id": UUID,
        "method": enum(*methods), "state": enum(*STATES), "phase": TOKEN, "outcome": enum(*OUTCOMES),
        "cancel_requested": BOOL, "poll_after_ms": COUNT, "artifact_refs": artifacts,
        "workspace_advanced": BOOL, "error": {"oneOf": [{"type": "null"}, ref("OperationError")]}}
        , ["schema_version", "operation_id", "method", "state", "phase", "outcome", "cancel_requested", "poll_after_ms", "artifact_refs"]))
    envelope["properties"].update({"result": {}, "accepted_at": TIME, "started_at": TIME, "completed_at": TIME, "deadline_at": TIME,
                                   "elapsed_ms": COUNT, "completed_units": COUNT, "total_units": {"type": ["integer", "null"], "minimum": 0, "maximum": 9223372036854775807},
                                   "last_progress_at": TIME, "worker_heartbeat_age_ms": {"type": ["integer", "null"], "minimum": 0, "maximum": 9223372036854775807},
                                   "session_epoch": UUID, "status_url": {"type": "string", "pattern": "^/v1/operations/[0-9a-fA-F-]+$"},
                                   "result_url": {"type": "string", "pattern": "^/v1/operations/[0-9a-fA-F-]+/result$"}})
    base_rules = [
        {"if": {"properties": {"state": {"const": "SUCCEEDED"}}, "required": ["state"]}, "then": {"required": ["result"], "properties": {"outcome": {"const": "CONFIRMED"}, "error": {"type": "null"}}}},
        {"if": {"properties": {"state": {"const": "RECONCILIATION_REQUIRED"}}, "required": ["state"]}, "then": {"properties": {"outcome": {"const": "UNKNOWN"}}}},
        {"if": {"properties": {"state": {"enum": ["ACCEPTED", "QUEUED"]}}, "required": ["state"]}, "then": {"properties": {"outcome": {"const": "NOT_STARTED"}}}},
        {"if": {"properties": {"outcome": {"const": "UNKNOWN"}}, "required": ["outcome"]}, "then": {"properties": {"state": {"enum": ["RUNNING", "RECONCILIATION_REQUIRED"]}}}},
    ]
    envelope["allOf"] = base_rules + [{"if": {"properties": {"method": {"const": row["name"]}}, "required": ["method"]}, "then": {"properties": {"result": ref(pascal(row["name"]) + "Result"),
        "error": {"oneOf": [{"type": "null"}, {"allOf": [ref("OperationError"), {"properties": {"code": enum(*sorted(set(COMMON_ERRORS + row["error_codes"])) )}}]}]}}}} for row in registry["operations"]]
    envelope["$defs"] = {**defs, "OperationError": {k: v for k, v in error.items() if k not in {"$id", "$schema"}}}
    acceptance = document("acceptance", **obj({"operation_id": UUID, "state": enum("ACCEPTED", "QUEUED"), "poll_after_ms": COUNT, "status_url": envelope["properties"]["status_url"], "result_url": envelope["properties"]["result_url"], "accepted_at": TIME}))
    session = document("session-identity", **obj({"schema_version": {"const": "1.0"}, "project_key": UUID, "session_epoch": UUID,
        "process_id": {"type": "integer", "minimum": 1, "maximum": 2147483647}, "process_started_at": TIME,
        "handshake_instance": {**UUID, "not": {"const": "00000000-0000-0000-0000-000000000000"}}}))

    # Keep schemas local to each component, avoiding any remote runtime resolver.
    components = {"Request": copy.deepcopy(request), "Operation": envelope, "Acceptance": acceptance, "Error": error}
    def rebase(value, component):
        if isinstance(value, dict):
            return {k: (v.replace("#/$defs/", f"#/components/schemas/{component}/$defs/") if k == "$ref" else rebase(v, component)) for k, v in value.items() if k != "$id"}
        if isinstance(value, list):
            return [rebase(v, component) for v in value]
        return value
    schemas = {k: rebase(v, k) for k, v in components.items()}
    def response(name, description):
        return {"description": description, "content": {"application/json": {"schema": {"$ref": "#/components/schemas/" + name}}}}
    errors = {str(code): response("Error", "Request rejected before acceptance") for code in [400, 401, 403, 404, 409, 413, 422, 429, 503]}
    paths = {"/v1/operations": {"post": {"operationId": "submitOperation", "description": "Contract only; no host or operation handler is implemented. Acceptance requires durable intent in B-02.", "x-methods": methods,
             "requestBody": {"required": True, "content": {"application/json": {"schema": {"$ref": "#/components/schemas/Request"}}}},
             "responses": {"200": response("Operation", "Fast method result or existing idempotent operation"), "202": response("Acceptance", "Durably accepted work"), **errors}}}}
    id_param = {"name": "id", "in": "path", "required": True, "schema": UUID}
    for suffix, method in [("", "operation.status"), ("/result", "operation.result"), ("/events", "operation.events"), ("/cancel", "operation.cancel")]:
        branch = next(b for b in request["oneOf"] if b["properties"]["method"]["const"] == method)
        item = {"operationId": pascal(method), "x-methods": [method], "parameters": [id_param], "responses": {"200": response("Operation", "Operation report; HTTP success is not Engineering or Runtime success"), **errors}}
        if suffix == "/result":
            item["responses"]["202"] = {"description": "Result is not ready; use operation status"}
        if suffix == "/events":
            item["parameters"] += [{"name": k, "in": "query", "schema": v} for k, v in branch["properties"]["params"]["properties"].items() if k != "operation_id"]
        if suffix == "/cancel":
            item["requestBody"] = {"required": False, "content": {"application/json": {"schema": obj({"reason": branch["properties"]["params"]["properties"]["reason"]}, [])}}}
        paths["/v1/operations/{id}" + suffix] = {"post" if suffix == "/cancel" else "get": item}
    paths["/v1/diagnostics/health"] = {"get": {"operationId": "health", "x-methods": ["diagnostics.health"], "parameters": [{"name": k, "in": "query", "schema": v} for k, v in request["oneOf"][-1]["properties"]["params"]["properties"].items()], "responses": {"200": response("Operation", "Cached host health only"), **errors}}}
    openapi = {"openapi": "3.1.1", "info": {"title": "TIA_Bridge 2 contracts (no running service)", "version": "1.0.0"}, "security": [{"HostSession": []}], "paths": paths, "components": {"securitySchemes": {"HostSession": {"type": "http", "scheme": "bearer", "description": "Protected host token; never supplied in operation JSON. Does not grant TIA permissions."}}, "schemas": schemas}}

    generated = ["// <auto-generated/>\n#nullable enable\nusing System.Collections.Immutable;\nusing TiaBridge.Core.Identity;\nusing TiaBridge.Core.Operations;\n\nnamespace TiaBridge.Core.Transport.V1;\n"]
    enum_defs = {}
    def cs_type(schema, name):
        if "$ref" in schema:
            return schema["$ref"].split("/")[-1]
        if "enum" in schema:
            enum_defs[name] = schema["enum"]
            return name
        typ = schema.get("type", "string")
        if isinstance(typ, list):
            return cs_type({**schema, "type": next(t for t in typ if t != "null")}, name) + "?"
        if typ == "array":
            return "ImmutableArray<" + cs_type(schema["items"], name + "Item") + ">"
        if typ == "boolean":
            return "bool"
        if typ == "integer":
            return "long"
        if schema.get("format") == "uuid":
            return {"ProjectKey": "ProjectKey", "ObjectId": "ObjectId", "OperationId": "OperationId", "SessionEpoch": "SessionEpoch", "ExpectedSessionEpoch": "SessionEpoch"}.get(name.split("_")[-1], "Guid")
        if schema.get("format") == "date-time":
            return "DateTimeOffset"
        return "string"
    def record(name, schema, parent):
        required = schema.get("required", [])
        fields = []
        for key, value in schema["properties"].items():
            prop = pascal(key)
            typ = cs_type(value, name + "_" + prop)
            if key not in required and not typ.endswith("?"):
                typ += "?"
            fields.append((key in required, f"{typ} {prop}" + ("" if key in required else " = null")))
        fields.sort(key=lambda x: not x[0])
        generated.append(f"public sealed record {name}(" + ", ".join(f[1] for f in fields) + f") : {parent};\n")
    for branch in request["oneOf"]:
        record(pascal(branch["properties"]["method"]["const"]) + "Parameters", branch["properties"]["params"], "OperationParameters")
    for name, schema in defs.items():
        record(name, schema, "OperationResult" if name.endswith("Result") else "WireValue")
    # The wire enum spellings are exact (including lower-case protocol tokens).
    for name, values in enum_defs.items():
        generated.append("public enum " + name + " { " + ", ".join(values) + " }\n")
    generated.append("\npublic static class MethodContracts\n{\n    public static Type Parameters(OperationMethod method) => method switch\n    {\n")
    for method in methods:
        generated.append(f"        OperationMethod.{pascal(method)} => typeof({pascal(method)}Parameters),\n")
    generated.append("        _ => throw new ArgumentOutOfRangeException(nameof(method))\n    };\n    public static Type Result(OperationMethod method) => method switch\n    {\n")
    for method in methods:
        generated.append(f"        OperationMethod.{pascal(method)} => typeof({pascal(method)}Result),\n")
    generated.append("        _ => throw new ArgumentOutOfRangeException(nameof(method))\n    };\n}\n")
    domain = "// <auto-generated/>\nnamespace TiaBridge.Core.Operations;\n\npublic enum OperationMethod\n{\n" + "".join(f"    {pascal(m)},\n" for m in methods) + "}\n\npublic enum ErrorCode\n{\n" + "".join(f"    {c},\n" for c in codes) + "}\n\npublic static class OperationCatalog\n{\n    public static string Name(OperationMethod method) => method switch\n    {\n" + "".join(f'        OperationMethod.{pascal(m)} => "{m}",\n' for m in methods) + "        _ => throw new ArgumentOutOfRangeException(nameof(method))\n    };\n    public static (int Default, int Maximum) Timeouts(OperationMethod method) => method switch\n    {\n" + "".join(f'        OperationMethod.{pascal(r["name"])} => ({r["default_timeout_s"]}, {r["max_timeout_s"]}),\n' for r in registry["operations"]) + "        _ => throw new ArgumentOutOfRangeException(nameof(method))\n    };\n}\n"
    artifacts_out = {BASE / "operation-result.schema.json": result, BASE / "error.schema.json": error, BASE / "operation.schema.json": envelope, BASE / "acceptance.schema.json": acceptance, BASE / "openapi.json": openapi,
                     BASE / "session-identity.schema.json": session,
                     ROOT / "src/TiaBridge.Core/Transport/V1/MethodModels.g.cs": "".join(generated), ROOT / "src/TiaBridge.Core/Operations/OperationCatalog.g.cs": domain}
    # Synthetic examples are illustrative observations, never executable grants or TIA plans.
    uid = "11111111-1111-4111-8111-111111111111"
    def sample(schema):
        if "$ref" in schema:
            return sample(defs[schema["$ref"].split("/")[-1]])
        if "const" in schema:
            return schema["const"]
        if "enum" in schema:
            return schema["enum"][0]
        typ = schema.get("type", "string")
        if typ == "object":
            return {k: sample(v) for k, v in schema["properties"].items() if k in schema.get("required", [])}
        if typ == "array":
            return [sample(schema["items"])] if schema.get("minItems", 0) else []
        if typ == "boolean":
            return False
        if typ == "integer":
            return max(0, schema.get("minimum", 0))
        if schema.get("format") == "uuid":
            return uid
        if schema.get("format") == "date-time":
            return "2026-10-25T08:00:00Z"
        if schema.get("pattern") == HASH["pattern"]:
            return "0" * 64
        return "synthetic.1"
    for branch in request["oneOf"]:
        method = branch["properties"]["method"]["const"]
        req = sample(branch)
        req["idempotency_key"] = "synthetic-" + method
        res = {"schema_version": "1.0", "operation_id": uid, "method": method, "state": "SUCCEEDED", "phase": "COMPLETED", "outcome": "CONFIRMED", "cancel_requested": False, "poll_after_ms": 0, "artifact_refs": [], "result": sample(defs[pascal(method) + "Result"])}
        example_dir = ROOT / "examples/contracts/v1"
        artifacts_out[example_dir / f"request.{method}.json"] = req
        artifacts_out[example_dir / f"response.{method}.json"] = res
    return artifacts_out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    request = json.loads((BASE / "operation-request.schema.json").read_text(encoding="utf-8"))
    registry = json.loads((BASE / "operations.json").read_text(encoding="utf-8"))
    stale = []
    for path, value in build(request, registry).items():
        text = value if isinstance(value, str) else json.dumps(value, ensure_ascii=False, indent=2) + "\n"
        if args.write:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8", newline="\n")
        elif not path.exists() or path.read_text(encoding="utf-8") != text:
            stale.append(str(path.relative_to(ROOT)))
    if stale:
        raise SystemExit("Generated artifacts differ: " + ", ".join(stale))
    print("Generated contracts: " + ("updated" if args.write else "PASS"))


if __name__ == "__main__":
    main()
