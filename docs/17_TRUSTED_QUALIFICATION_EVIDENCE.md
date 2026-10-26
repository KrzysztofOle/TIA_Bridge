# Trusted Qualification Evidence Provider

Status: offline implementation introduced by B-03A/E1. No real Engineering evidence, signer key, or enabled production qualification was registered in this change.

## Trust boundary

An `evidence_id` in `capability-registry.v2.json` is only a selector. Host reads signed envelopes only from the fixed `<state_path>/qualification-evidence/` directory; HTTP/CLI requests cannot select the directory, provide evidence, set a trust anchor, or assert a PASS. A Host operator supplies signer public keys through the startup `qualification_trust.trusted_signer_keys` configuration field (`signerId=base64-SPKI`). With no anchors, missing store, unreadable store, malformed envelope, invalid signature, or invalid artifact hash, Host denies `project.inspect`.

Each `*.signed.json` envelope contains a signer ID, the base64-encoded exact JSON payload, and an ECDSA P-256/SHA-256 signature over the payload bytes. The payload binds the evidence ID and revision; kind, source and provenance; exact TIA, adapter and Worker builds; operation and scope; device family/object kind; profile and read scope; coverage and result; run ID; optional exact ProjectKey or project class; artifact names and SHA-256; and UTC creation time. Host validates semantic fields, signature against an operator-pinned key, and each artifact under `<store>/<evidence_id>/`. Artifact names must be leaf names. SHA-256 proves byte integrity only; the signature and trusted signer configuration establish origin. A signed PASS is still qualification evidence, not an execution grant.

Core performs deterministic exact matching. Project evidence is bound to the exact registered ProjectKey when used by `project.inspect`; it does not qualify another project. The current Host binding does not infer a project class. PROJECT does not qualify DEVICE/OBJECT; READ does not qualify WRITE/IMPORT/EXPORT. PASS is required; FAIL, NOT_RUN and BLOCKED deny.

## Admission, dispatch and recovery

Host evaluates the immutable operation request against the current exact capability key and resolves every required evidence ID. It appends a decision record in `<state_path>/qualification-decisions.jsonl`; failure to record a decision denies dispatch. Before preparing a durable dispatch and immediately before IPC send, Host re-reads the evidence envelope and artifacts and compares the decision snapshot hash. Retry dispatch is re-evaluated too. A changed or unavailable trust snapshot fails closed and is not replayed under a previous admission.

After Host restart, pending operations are resolved from the current Host configuration, current registry and current evidence store. There is no durable admission grant. Existing operation idempotency, owner fencing, worker epoch and quarantine rules remain in force. The Worker receives no trust store or trust anchors and IPC 1.1 is unchanged. Worker attach continues to validate exact PID/start binding; a stale binding fails in the Worker and cannot silently choose another project.

## Offline tests and current activation state

Tests generate ephemeral P-256 keys and synthetic payloads/artifacts in temporary directories. These keys are not checked in and are not production anchors. The normal configuration contains no qualification trust anchors and no evidence records. Real Engineering evidence is `NOT_REGISTERED`.

Before registering real qualification evidence, an operator decision and deployment review are still required:

1. Select an isolated signing procedure and authorized approver identity; keep private keys outside Host and repository.
2. Protect the Host startup configuration, state directory, evidence store and decision journal with Windows ACLs appropriate to the Host service identity. The current implementation rejects reparse points but does not certify ACL ownership or prevent an administrator from changing these files.
3. Independently review the run ID, source/provenance, exact builds, scope, project binding, raw artifact hashes and PASS decision before pinning the signer public key and placing a signed envelope.
4. Define key rotation, revocation and evidence expiry. These lifecycle controls are not implemented; until decided, trust anchors must remain empty for production use.

Recommended activation variant: an administrator-controlled Host configuration pins a dedicated qualification signer public key; an offline, dual-reviewed enrollment utility signs canonical qualification packages; Host consumes immutable signed packages from the fixed local-NTFS store, and deployment ACLs restrict configuration/store writes to the operator identity. Do not enable this path until ACL, approval, rotation/revocation and expiry policy are accepted and implemented or explicitly verified. This E1 change does not run the enrollment utility or add a real evidence record.

## Compatibility and limits

B-03A/E1-R diagnosed three failing resilience artifact cases as a test-probe binding defect exposed by the additional profile resolutions during revalidation. The probe now preserves the synthetic admitted proof/template per operation and checks it against the actual Worker receipt before corruption. This does not cache qualification decisions or change production revalidation, trust anchors, artifact verification, HTTP codes, or recovery. See the [repair report](B03A_E1_R_ARTIFACT_REPAIR.md) for reproduction, regression coverage and separately reported validation results. Historical qualification reports retain their original results.

No HTTP/API v1, capability registry v1/v2, IPC 1.1, or Worker DTO shape changes. Registry v1 remains unchanged. The current Host retains the X-01 `all_supported`, fresh, read-only PROJECT DiscoveryV1 bound to registered ProjectKey/PID/start/path. Exact TIA API FileVersion and Worker SHA-256 are required. This mechanism does not qualify newly added Siemens getters, confer session permissions, qualify production, remove `WORKER HANDLE RISK: UNKNOWN`, or authorize Siemens Engineering work. D-11 status and historical evidence are unchanged.
