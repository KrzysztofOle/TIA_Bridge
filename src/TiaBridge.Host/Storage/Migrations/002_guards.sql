CREATE INDEX outbox_pending ON outbox(state,operation_id);
CREATE INDEX operations_project ON operations(project_key,state);
CREATE TRIGGER immutable_request BEFORE UPDATE OF operation_id,principal,project_key,method,request_json,canonical_body,body_sha256,accepted_at ON operations BEGIN SELECT RAISE(ABORT,'immutable request'); END;
CREATE TRIGGER immutable_terminal BEFORE UPDATE ON operations WHEN OLD.state IN ('SUCCEEDED','FAILED','CANCELLED','RECONCILIATION_REQUIRED') BEGIN SELECT RAISE(ABORT,'terminal history'); END;
CREATE TRIGGER terminal_requires_result BEFORE UPDATE ON operations WHEN NEW.state IN ('SUCCEEDED','FAILED','CANCELLED','RECONCILIATION_REQUIRED') AND NOT EXISTS(SELECT 1 FROM operation_results r WHERE r.operation_id=NEW.operation_id AND r.sha256=NEW.report_sha256) BEGIN SELECT RAISE(ABORT,'missing terminal proof'); END;
CREATE TRIGGER no_operation_delete BEFORE DELETE ON operations BEGIN SELECT RAISE(ABORT,'retained operation'); END;
CREATE TRIGGER no_event_update BEFORE UPDATE ON events BEGIN SELECT RAISE(ABORT,'immutable event'); END;
CREATE TRIGGER no_event_delete BEFORE DELETE ON events BEGIN SELECT RAISE(ABORT,'retained event'); END;
CREATE TRIGGER no_result_update BEFORE UPDATE ON operation_results BEGIN SELECT RAISE(ABORT,'immutable result'); END;
CREATE TRIGGER no_result_delete BEFORE DELETE ON operation_results BEGIN SELECT RAISE(ABORT,'retained result'); END;
CREATE TRIGGER no_key_update BEFORE UPDATE ON idempotency_keys BEGIN SELECT RAISE(ABORT,'immutable idempotency'); END;
CREATE TRIGGER no_key_delete BEFORE DELETE ON idempotency_keys BEGIN SELECT RAISE(ABORT,'retained idempotency'); END;
CREATE TRIGGER no_artifact_update BEFORE UPDATE ON artifacts BEGIN SELECT RAISE(ABORT,'immutable artifact'); END;
CREATE TRIGGER no_artifact_delete BEFORE DELETE ON artifacts BEGIN SELECT RAISE(ABORT,'retained artifact'); END;
CREATE TRIGGER no_outbox_delete BEFORE DELETE ON outbox BEGIN SELECT RAISE(ABORT,'retained outbox'); END;
CREATE TRIGGER no_outbox_requeue BEFORE UPDATE ON outbox WHEN NOT (
 (OLD.state='PENDING' AND NEW.state IN ('CLAIMED','CLOSED')) OR
 (OLD.state='CLAIMED' AND NEW.state IN ('DISPATCH_RECORDED','CLOSED') AND NEW.claim_id IS OLD.claim_id) OR
 (OLD.state='DISPATCH_RECORDED' AND NEW.state='CLOSED' AND NEW.claim_id IS OLD.claim_id))
 BEGIN SELECT RAISE(ABORT,'outbox cannot replay'); END;
