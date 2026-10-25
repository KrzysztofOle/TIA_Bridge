CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, sha256 TEXT NOT NULL, applied_at TEXT NOT NULL) STRICT;
CREATE TABLE projects(project_key TEXT PRIMARY KEY NOT NULL, registered_at TEXT NOT NULL) STRICT;
CREATE TABLE sessions(session_epoch TEXT PRIMARY KEY NOT NULL, project_key TEXT NOT NULL REFERENCES projects(project_key), binding_json BLOB NOT NULL) STRICT;
CREATE TABLE operations(
 operation_id TEXT PRIMARY KEY NOT NULL, principal TEXT NOT NULL,
 project_key TEXT REFERENCES projects(project_key), project_scope TEXT GENERATED ALWAYS AS (coalesce(project_key,'')) STORED,
 method TEXT NOT NULL, request_json BLOB NOT NULL, canonical_body BLOB NOT NULL, body_sha256 TEXT NOT NULL,
 accepted_at TEXT NOT NULL, state TEXT NOT NULL, outcome TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision>=0),
 report_json BLOB NOT NULL, report_sha256 TEXT NOT NULL, last_sequence INTEGER NOT NULL CHECK(last_sequence>0),
 UNIQUE(operation_id,principal,project_scope),
 FOREIGN KEY(operation_id,last_sequence,report_sha256) REFERENCES events(operation_id,event_sequence,report_sha256) DEFERRABLE INITIALLY DEFERRED
) STRICT;
CREATE TABLE idempotency_keys(
 principal TEXT NOT NULL, project_key TEXT REFERENCES projects(project_key), project_scope TEXT GENERATED ALWAYS AS (coalesce(project_key,'')) STORED,
 idempotency_key TEXT NOT NULL, operation_id TEXT NOT NULL UNIQUE,
 UNIQUE(principal,project_scope,idempotency_key),
 FOREIGN KEY(operation_id,principal,project_scope) REFERENCES operations(operation_id,principal,project_scope)
) STRICT;
CREATE TABLE events(
 operation_id TEXT NOT NULL REFERENCES operations(operation_id), event_sequence INTEGER NOT NULL CHECK(event_sequence>0),
 occurred_at TEXT NOT NULL, report_json BLOB NOT NULL, report_sha256 TEXT NOT NULL,
 PRIMARY KEY(operation_id,event_sequence), UNIQUE(operation_id,event_sequence,report_sha256)
) STRICT;
CREATE TABLE operation_results(operation_id TEXT PRIMARY KEY NOT NULL REFERENCES operations(operation_id), report_json BLOB NOT NULL, sha256 TEXT NOT NULL) STRICT;
CREATE TABLE outbox(operation_id TEXT PRIMARY KEY NOT NULL REFERENCES operations(operation_id), state TEXT NOT NULL CHECK(state IN ('PENDING','CLAIMED','DISPATCH_RECORDED','CLOSED')), claim_id TEXT,
 CHECK((state='PENDING' AND claim_id IS NULL) OR state='CLOSED' OR (state IN ('CLAIMED','DISPATCH_RECORDED') AND claim_id IS NOT NULL))) STRICT;
CREATE TABLE artifacts(artifact_id TEXT PRIMARY KEY NOT NULL, sha256 TEXT NOT NULL, size INTEGER NOT NULL CHECK(size>=0), relative_path TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL, pinned INTEGER NOT NULL DEFAULT 1 CHECK(pinned IN (0,1))) STRICT;
CREATE TABLE operation_artifacts(operation_id TEXT NOT NULL REFERENCES operations(operation_id), artifact_id TEXT NOT NULL REFERENCES artifacts(artifact_id), PRIMARY KEY(operation_id,artifact_id)) STRICT;
CREATE TABLE steps(step_id TEXT PRIMARY KEY NOT NULL, operation_id TEXT NOT NULL REFERENCES operations(operation_id), ordinal INTEGER NOT NULL, intent_json BLOB NOT NULL, receipt_json BLOB, UNIQUE(operation_id,ordinal)) STRICT;
CREATE TABLE leases(lease_id TEXT PRIMARY KEY NOT NULL, project_key TEXT NOT NULL REFERENCES projects(project_key), session_epoch TEXT REFERENCES sessions(session_epoch), metadata_json BLOB NOT NULL) STRICT;
CREATE TABLE quarantines(project_key TEXT PRIMARY KEY NOT NULL REFERENCES projects(project_key), operation_id TEXT NOT NULL REFERENCES operations(operation_id), reason TEXT NOT NULL, created_at TEXT NOT NULL) STRICT;
CREATE TABLE snapshots(snapshot_id TEXT PRIMARY KEY NOT NULL, project_key TEXT NOT NULL REFERENCES projects(project_key), artifact_id TEXT NOT NULL REFERENCES artifacts(artifact_id)) STRICT;
CREATE TABLE plans(plan_id TEXT PRIMARY KEY NOT NULL, project_key TEXT NOT NULL REFERENCES projects(project_key), snapshot_id TEXT NOT NULL REFERENCES snapshots(snapshot_id), artifact_id TEXT NOT NULL REFERENCES artifacts(artifact_id)) STRICT;
CREATE TABLE baseline_revisions(baseline_revision TEXT PRIMARY KEY NOT NULL, project_key TEXT NOT NULL REFERENCES projects(project_key), artifact_id TEXT NOT NULL REFERENCES artifacts(artifact_id)) STRICT;
CREATE TABLE capabilities(capability_set_hash TEXT PRIMARY KEY NOT NULL, artifact_id TEXT NOT NULL REFERENCES artifacts(artifact_id)) STRICT;
CREATE TABLE authorizations(authorization_ref TEXT PRIMARY KEY NOT NULL, project_key TEXT NOT NULL REFERENCES projects(project_key), session_epoch TEXT NOT NULL REFERENCES sessions(session_epoch), metadata_json BLOB NOT NULL) STRICT;
