# QA-02A — Unified Diagnostics Foundation

Scope: offline import and normalization of structured logs collected by QA Runner. No application logger, Host, Scheduler, Worker, CLI or IPC code changes. QA Runner imports only files explicitly selected by an approved task's `collect` configuration. Raw collected bytes remain under `artifacts/`; `diagnostics.jsonl` is an additional derived artifact covered by the run manifest.

## Existing logging audit

| Component | Format | Run ID | Operation ID | Worker epoch | Event ID | Gaps |
|---|---|---|---|---|---|---|
| Host | SQLite operation/event journal and versioned JSON reports; selected qualification evidence JSONL | Not in operation journal | Yes, journal key/report | In worker binding/receipts | Lifecycle/report phase, not a universal diagnostic event ID | No common diagnostic sink; stdout is startup/error contract JSON |
| Scheduler | Durable operation/step/attempt/receipt state; selected phase/resource evidence | Not generally present | Yes | Yes for worker attempt | Phase/receipt disposition where present | No standalone structured event stream |
| IPC 1.1 | Versioned JSON frames over transport | No | Dispatch/Started/Progress/Receipt/Cancel carry it; controls null | Yes | Message kind | Protocol records are transport evidence, not logger output |
| Worker V19 | Audit JSONL (`operation_id`, `worker_epoch`, UTC `at`, `call`); optional resource JSONL with `phase`, `sequence`, `elapsed_ticks`, frequency, IDs | No | Yes, when operation-scoped | Yes | Audit call/resource phase | Optional resource stream; no shared event envelope |
| CLI | JSON contract output on stdout; JSON errors on stderr | No | In returned operation report where applicable | No | Command/response shape, no diagnostic ID | Not a persistent event log |
| QA Runner | `runner.jsonl` JSONL; `QA_APPLICATION_LOG` wrappers from approved collected JSONL | Yes | Only when supplied by source | Only when supplied by source | Stable QA IDs; prior imported wrapper ID was generic | No normalized sidecar, structured JSON import or event summary before QA-02A |

Sources checked in clean `test/e2-b1-ipc-sender-lifetime` at `0a882091bc2908d8c4039a135a55f96bacb32541`: `TiaBridge.Host/Operations/SqliteOperationStore.cs`, `TiaBridge.Host/Api/HostApi.cs`, `TiaBridge.Host/Scheduling/OperationScheduler.cs`, `TiaBridge.Worker.V19/Program.cs`, `WorkerResourceTrace.cs`, `TiaBridge.Ipc/Protocol.cs`, and `TiaBridge.Cli/Program.cs`; QA Runner modules checked at QA-01 base `1d65568`. These formats provide evidence with different purposes; the normalizer does not claim they are one complete execution trace.

## DiagnosticEvent v1

Normalized rows use `schema_version: "1.0"`, `timestamp_utc`, `level`, `application`, `component`, `event_id`, `message`, and optional correlation/duration/error fields (`run_id`, `scenario_id`, `step_id`, `operation_id`, `worker_epoch`, `duration_ms`, `error_code`). The normalized row also has `absent_fields`, `unavailable_fields`, `original_values`, `log_reference`, and `record_number` provenance fields. All standard fields are represented; unavailable values are JSON null. A field absent in the source is listed only in `absent_fields`; a present null is listed only in `unavailable_fields`. The source schema version is retained in `original_values.source_schema_version`.

Timestamp aliases `utc`, `at`, and `occurred_at` are accepted. Existing UTC `+00:00` is rendered as `Z`; non-UTC or malformed timestamp strings are preserved as supplied and are not silently converted. Durations are copied only from explicit `duration_ms`. Monotonic counters such as `elapsed_ticks` and `stopwatch_frequency` remain in source evidence and are not guessed into milliseconds. QA Runner uses a monotonic clock for its own durations.

Stable event IDs are copied from explicit `event_id`, or the existing structured `event`, `phase`, or `call` field. No ID is made from message text. Sensitive-looking message values pass through the existing redactor; the original file remains byte-for-byte in collected artifacts. Only selected contract fields are copied to the normalized row, not arbitrary source keys.

## Normalization and validation

- Accept only JSONL or a JSON object/array. A line-oriented file with malformed lines records invalid entries. Free-form text is never interpreted as events.
- Normalize known aliases while preserving their selected original values. Different source records are not deduplicated.
- Invalid JSON, non-object records and unreadable sources appear in the report with source reference and record number; invalid rows do not become events.
- `diagnostics.jsonl` is created only when approved JSON/JSONL sources were collected. Source artifacts are never modified. The derived file and summary are included in the sealed QA run manifest.
- With no structured source, the report explicitly says diagnostics `NOT_RUN`; absence of data is not a PASS verdict and does not alter functional QA verdicts.
- No project paths, credentials, request bodies or arbitrary source properties are copied. The existing redactor handles recognizable token/password/Bearer patterns in message text; raw imported files are only suitable when the task's source is reviewed synthetic diagnostics.

## Correlation

Events are grouped independently by explicit `run_id`, `operation_id`, `worker_epoch`, `scenario_id`, and `step_id`. Missing IDs remain missing; there is no time/message similarity join. Event order is chronological by supplied timestamp. Duplicates remain distinct observations. The operation timeline displays recognized explicit components (`Host`, `Scheduler`, `IPC`, `Worker`, `Result`); unobserved components are `UNKNOWN`. Component names are not inferred from application names. A partial timeline is not proof of a missing production action.

## Example report section

```text
## Diagnostics
Events: 14; WARN/ERROR: 2; without correlation IDs: 4; invalid records: 1.
Normalized events: diagnostics.jsonl. Raw log references are retained in each normalized record.
Grouped errors:
- WORKER_FAILED: 1
Important events:
- 2026-10-28T10:00:00Z ERROR Worker/WORKER_FAILED: Read failed (artifacts/.../worker.jsonl)
Operation flows use only explicit operation IDs; absent stages are UNKNOWN.
```

## Limitations and QA-02B

The current application modules do not share a diagnostic event envelope, and some Host/Scheduler evidence is a durable business journal rather than a log. Correlation therefore works only across selected records that already share reliable IDs. The normalizer does not read SQLite databases, infer missing IDs, parse arbitrary text, redact all possible secrets from source evidence, or establish event causality from timestamps. QA-02B could define a separately reviewed Host journal export and application event mapping, then qualify redaction and identity provenance with synthetic fixtures; it must not alter IPC 1.1 or turn qualification evidence into a general logging API.
