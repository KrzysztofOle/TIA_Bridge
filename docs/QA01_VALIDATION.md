# QA-01 — validation record, 2026-10-28

Scope: isolated Windows worktree `TIA_Bridge_QA01`, base commit `a509a65`.
Python 3.12.2, .NET SDK 10.0.402. No Siemens Attach or live operation.
Implementation, scenario contract and limitations: [QA_RUNNER](QA_RUNNER.md).

The acceptance suite exercises real child processes, timeout, nonzero exit,
missing runtime, SIGINT/cancellation, sealed partial reports, hostile JSON,
fixed approved commands, log correlation, manifest tampering, grouping,
comparison, stale receipts, qualification layers and a copied independent project.
Tests use synthetic fixtures. Windows is the validated execution platform;
POSIX process handling is implemented but not exercised on this workstation.

No test counts here imply Engineering, Runtime or full qualification PASS.

## Executed profiles and examples

| Execution | Run ID | Verdict | Tests PASS / FAIL / SKIPPED |
|---|---|---|---|
| Final FAST | 20261028T074736647988Z-d629ea212fb047f9933eab845feaef38 | PASS | 615 / 0 / 0 (564 Core + 51 runner) |
| Initial STANDARD | 20261028T070642179732Z-d5f19918f6804322aba8637df73e4d3b | PASS | 1083 / 0 / 0 |
| Final STANDARD | 20261028T074810955584Z-604755600a6e4f5a98b0a2a0cdad92e8 | PASS | 1090 / 0 / 0 (564 Core + 404 Host + 71 contracts Python + 51 runner) |
| QUALIFICATION offline | 20261028T072320318652Z-02cca0eba2cc458c87b2abd7c1bbe2ac | BLOCKED qualification; PASS functional/performance | 1044 / 0 / 0 (973 .NET + 71 Python) |
| Explicit worker-handles.json | 20261028T071733944206Z-3cd51c4554da45509440f4f00986d728 | PASS | 23 / 0 / 0 |
| Independent example | 20261028T071342691143Z-7f5564b6b39d4ff1aa2eb7a188100734 | PASS | 2 / 0 / 0 |

Runner acceptance: 51/51 in final FAST and STANDARD, including copied standalone engine,
actual SIGINT and cancellation, snapshot namespace collision, reserved Windows
names, case aliases and manifest Run ID mismatch. `report --run-id` and
`--failures-only` were also exercised against actual repository runs. Each run
retains its own immutable evidence and recorded source/configuration hashes.

## Canonical evidence and qualification limits

Canonical ID: `355442f4d6f7460f8d3021ac60d0df1b`, SHARED_WORKSTATION 1.0.0,
profile definition SHA-256
`855e5c952aeece7bfa165676fbe9f15eb61151fd1ded00c89abd0c4228becdd2`.
Preflight PASS; Release builds PASS with zero warnings/errors; required
RT-03/05/06/15/16 PASS. The existing canonical runner and profile definitions
were not modified.

Actual idle: 308.0234649 seconds, Host CPU 0.8915209602%, peak Host/Fake RAM
90.6171875/37.37109375 MiB, Host handles 462 initially and finally (maximum 475).
1001 operations completed; measured operation series 482.2513608 seconds.
Measured performance PASS. Full NF-08 and eight-hour NF-11 remain NOT_RUN;
NF-11 explicitly blocks formal qualification. Engineering and Runtime NOT_RUN.
This record does not activate E2, B0, Attach, enrollment or a current-source live grant.

Verified inventories: canonical manifest 78 files, copied original bundle 78,
QA wrapper manifest 89. Every file hash and byte length matched; originals were
retained. Evidence is under `.tia-bridge/qa-results/<run-id>/`; the original
canonical bundle is under `.tia-bridge/resilience/<canonical-id>/`. No raw logs,
TRX, binaries, system metadata, vendor project or credentials are committed.

The canonical run binds its recorded source hashes. Afterwards, only QA snapshot/
name validation and report Run ID validation were hardened; final FAST/STANDARD
validate the final runner. Production C#, IPC, thresholds and canonical code are unchanged.

## First-run preparation regression

Initial QA run `20261028T071945245578Z-8ba512df54a145cfa476687cc8a04982`,
canonical `726583b4195c4827a90b10a9d560d3cb`: BLOCKED before test execution because
the fresh worktree lacked `.tia-bridge/resilience/workspaces`. Its sealed report
remains intact; no tests were promoted to PASS. The integration layout helper
now prepares both expected QA workspace directories before unchanged preflight.
Tests verify idempotent preparation, preservation of existing files and fail-closed
collision handling. The subsequent canonical run passed preflight.

## Instruction and delivery review

Both instruction files were reviewed section by section and remain unchanged;
scope and reasoning are recorded in [Zmiany](../Zmiany.md). Architecture/ADR,
Host/Worker authorization and IPC contracts remain unchanged. Quality audit,
local link verification and staged diff checks passed for final delivery.
The implementation is delivered on the isolated `feat/qa-01-runner` branch,
without merge/rebase or edits in the original E2-B1-I checkout.
