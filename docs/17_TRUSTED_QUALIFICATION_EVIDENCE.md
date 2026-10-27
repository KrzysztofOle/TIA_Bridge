# Trusted Qualification Evidence Provider — E1 Local Trusted v1

Status: B-03A/E1-L implements the user-approved local model offline, extending B-03A/E1. One local operator, one ECDSA P-256 public trust anchor, Windows ACLs, manual enrollment/revocation/rotation, no automatic expiry. No PKI, CA, external service or multiple-approver workflow. No production private key or real Engineering evidence is registered by this change. [Operator procedure](18_LOCAL_TRUSTED_ENROLLMENT.md); [implementation/test report](B03A_E1_L_LOCAL_TRUSTED.md).

## Trust boundary and identities

An `evidence_id` in Capability Registry v2 remains a selector, not a grant. Host reads only `<state_path>/qualification-evidence/`, on the existing per-user local fixed NTFS application storage outside the repository. HTTP and the ordinary Bridge CLI cannot enroll, choose an evidence directory, supply a trust anchor, or assert qualification. The separate `TiaBridge.Qualification` executable performs local operator commands and is not a dependency of Host. Only it opens an existing user-scoped, nonexportable CNG signing key. Host receives only an operator-pinned public SPKI through startup configuration.

`qualification_trust` adds optional `operator_sid` and `host_sid` beside the existing `trusted_signer_keys`. With trust enabled, both explicit SIDs and exactly one `signerId=base64-SPKI` P-256 key are required. Empty anchors disable qualification. Old configurations/envelopes without a valid local policy and activation journal do not grant qualification; no automatic migration or adoption occurs. HTTP/API v1, Core models, Capability Registry v1/v2 and IPC 1.1 are unchanged.

The ACL validator reads actual Windows security descriptors, checks the effective current Host SID during assessment, checks the operator SID before writes, and verifies the protected store root/configuration file and descendant files/directories. It requires direct read permissions for the declared identities, an approved owner, operator write access at protected roots, and only operator/Host/SYSTEM/Administrators allow entries; a distinct Host SID may have read access only. Deny entries or wider grants fail closed. Enabled Host token groups cannot supply hidden write access in the distinct-SID case. Ancestor owners and replacement/ownership permissions are checked too; an immediate parent writable by an untrusted principal is rejected. Windows TrustedInstaller is recognized only as a trusted ancestor identity.

The existing per-user StorageLocation policy remains in force. The tested deployment uses the same Windows account for the operator and Host; separate executables and HTTP roles do not isolate malicious processes running under that same SID. The validator supports checking declared distinct identities, but this task does not qualify a multi-account service deployment or change the per-user state-root restriction. SYSTEM/Administrators remain trusted administrators. The validator never changes ACLs, Windows configuration, UAC or antivirus; the operator must provision/review deployment permissions explicitly.

## Fixed local format

```text
qualification-evidence/
  lifecycle.jsonl                         explicitly provisioned, initially empty
  operator.lock                           serializes local operator writes
  inbox/<evidence_id>/<revision>/          operator-prepared package
    evidence.json                         existing QualificationEvidence payload
    <artifact leaf names>
  approvals/<evidence_id>/<revision>/approval.json
  records/<evidence_id>/<revision>/        published immutable revision
    evidence.signed.json                   existing signed-envelope fields
    <artifact leaf names>
  staging/<uuid>/                          retained on interrupted publication
```

This is one store, not a parallel registry. Names are safe ASCII leaves, Windows reserved/ambiguous names and case-colliding artifacts are rejected, and the path chain rejects reparse points. JSON is strict UTF-8 with duplicate/unknown fields rejected and maximum depth 24. Payload/approval JSON is limited to 1 MiB, the signed envelope to 2 MiB to account for base64 overhead; the decoded payload still cannot exceed 1 MiB. Packages allow at most 64 artifacts, 64 MiB each and 128 MiB total. The lifecycle read/write limit is 8 MiB: an append that would exceed it is refused before changing bytes. Exceeding limits denies qualification; there is no automatic truncation, retention, reset or recovery of lost history. The operator must review history capacity and may disable anchors instead of attempting to erase history to force a revoke.

The existing envelope has signer ID, base64 exact payload bytes and an ECDSA P-256/SHA-256 signature over those bytes. Payload fields bind evidence ID/revision, provenance/kind, exact TIA/adapter/Worker builds, operation/scope, object family/kind, Discovery profile/read scope/coverage, result/run ID, exact ProjectKey and artifact names/SHA-256. Host verifies signature, activation's envelope/package hashes, exact payload identity, all artifact bytes and the existing Core exact-match policy. Revision selection never falls back to an older qualification when the latest enrollment is revoked.

Local Trusted v1 enrollment is bounded to PASS, registered exact ProjectKey, PROJECT `ProjectInspect`, the current DiscoveryV1 profile/read scope, exact adapter/Worker build and required STRUCTURE coverage. READ does not qualify WRITE/IMPORT/EXPORT, DEVICE or OBJECT operations. Missing fields, mismatched builds/project/scope, missing store/history, inaccessible files, unsafe ACLs, invalid signatures or hashes all deny.

## Approval, publication and revocation

`inspect` prints the complete evidence claim and a package SHA-256 binding the exact manifest bytes and ordered artifact descriptors after verifying actual artifact hashes. `approve` requires both that reviewed hash and `--confirm-operator-approval`; it writes a create-new approval bound to operator SID, ID/revision and UTC. A changed package invalidates approval. There is no automatic approval based on a PASS field and no implicit expiry.

`enroll` checks that approval, the current operator and pinned public key, signs locally, copies artifact bytes under read handles that deny write/delete, verifies copied hashes, flushes files, and publishes a new directory by same-volume move without overwrite. Only an appended/flushed ENROLLED lifecycle entry activates the revision. Interrupted staging/published directories remain retained and untrusted without that entry; they are not adopted or retried automatically. Existing revisions and approvals cannot be overwritten by the tool.

`revoke` appends/flushed a REVOKED record for exact ID/revision, operator SID, reason and UTC. It does not need the private key, delete evidence or change historical operation/test results. Sequence, revision progression, enrollment/hash binding and duplicate revocations are validated on read. Missing or torn journal denies qualification instead of creating an empty history. A failed/uncertain operator command requires read-only `list`/`verify` and review of retained files/history before any new revision; no automatic replay.

## Admission, dispatch, restart and disabling

The existing asynchronous admission/ledger does not create an enduring qualification grant. Host resolves the current active evidence, appends qualification decisions to the existing decision journal and compares decision hashes before preparing durable dispatch and again immediately before IPC send. Each assessment re-reads ACLs, lifecycle, signed payload and artifacts; lifecycle is read again after artifact hashing. Revocation after admission therefore denies a subsequent dispatch check. Retry uses the same revalidation; no second scheduler or operation engine was introduced.

Revocation is not cancellation or rollback. A call that has crossed the final dispatch validation boundary may already be in flight; there is no atomic transaction between the operator journal and Siemens IPC. Do not claim that revocation stopped a Worker call. Existing deadline, epoch/fencing, UNKNOWN/quarantine and recovery rules remain authoritative.

On restart, Host uses current pinned configuration and re-reads durable lifecycle/evidence for pending operations; an old admission snapshot cannot override revocation. Historical terminal results remain history. Clearing `trusted_signer_keys` and restarting Host disables future qualification; stopping/restarting Host is not evidence that an earlier Siemens call ended. Manual key rotation requires operator review, revocation of old active qualifications, replacement of the single pinned public key, Host restart and explicit enrollment of higher revisions; no automatic renewal/rotation or expiry is implemented.

## Evidence and limits

Offline tests use synthetic packages, ephemeral in-memory P-256 keys and UUID-owned NTFS fixtures. Tests exercise the real local CLI, Windows ACLs, provider, scheduler dispatch boundaries, HTTP/Fake execution and Host restart. They never create a production CNG key, register real Engineering evidence, load Siemens DLLs or call Openness. The existing [E1-R artifact repair](B03A_E1_R_ARTIFACT_REPAIR.md) remains covered; its history is unchanged.

This local mechanism establishes operator-controlled origin and byte integrity, not the truth of arbitrary signed PASS claims. Engineering enrollment requires independent review of the actual run/build/project/scope and separate authorization. Real Engineering evidence remains NOT_REGISTERED; B-03A Engineering BLOCKED/NOT_RUN, B-03B NOT_READY, NF-11 NOT_RUN and Worker Handle Risk UNKNOWN. Physical actuation, export/import and B-03A/E2 execution are not enabled by this task.
