# Examples

Only synthetic, non-customer examples belong here. Add example files only after a workspace format is defined and label their schema and adapter versions clearly.

## Przykłady kontraktów TIA_Bridge 2

W `contracts/` są wyłącznie syntetyczne JSON: [Apply request](contracts/request.sync-apply.json), [Snap7 read request](contracts/request.test.plc.json) i [unknown outcome](contracts/operation.unknown.json). Zerowy SHA-256 i identyfikatory nie wskazują istniejących obiektów. Przykłady nie nadają autoryzacji i nie uruchamiają działań.

Weryfikacja przez `py -3 scripts/audit_quality.py`. Semantyka: [interfejs](../docs/05_CODEX_INTERFACE.md) i [synchronizacja](../docs/04_SYNCHRONIZATION.md).
