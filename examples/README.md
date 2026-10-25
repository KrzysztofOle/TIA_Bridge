# Examples

Only synthetic, non-customer examples belong here. Add example files only after a workspace format is defined and label their schema and adapter versions clearly.

## Przykłady kontraktów TIA_Bridge 2

W `contracts/` są wyłącznie syntetyczne JSON: [Apply request](contracts/request.sync-apply.json), [Snap7 read request](contracts/request.test.plc.json) i [unknown outcome](contracts/operation.unknown.json). Zerowy SHA-256 i identyfikatory nie wskazują istniejących obiektów. Przykłady nie nadają autoryzacji i nie uruchamiają działań.

Weryfikacja przez `py -3 scripts/audit_quality.py`. Semantyka: [interfejs](../docs/05_CODEX_INTERFACE.md) i [synchronizacja](../docs/04_SYNCHRONIZATION.md).

## B-01A: pełna macierz v1

Katalog `examples/contracts/v1/` zawiera wygenerowane request.<method>.json i response.<method>.json dla wszystkich 19 metod (38 poprawnych komunikatów) oraz [invalid-requests.json](contracts/v1/invalid-requests.json) z 21 przykładami odrzucenia. Przykładowe [inspect request](contracts/v1/request.project.inspect.json), [inspect result](contracts/v1/response.project.inspect.json), [Apply result](contracts/v1/response.sync.apply.json) i [health result](contracts/v1/response.diagnostics.health.json) ilustrują typowane DTO. Macierz tworzy generator z scripts/generate_contracts.py; przykłady negatywne są niezależnym zestawem testowym.

Wyniki są syntetycznymi obserwacjami kontraktu, nie dowodami wykonania aplikacji. Status SUCCEEDED w fixture nie potwierdza TIA ani Runtime, a przykładowy grant nie istnieje. Pełną walidację zapewniają [testy B-01A](../tests/README.md); pierwotne trzy przykłady wyżej nadal pozostają kontrolami historycznej semantyki.

## B-01B: Workspace i Capabilities

[PLC manifest](workspace/plc.manifest.json) zawiera zagnieżdżone grupy, nazwę CON, Pump/pump, równoważne prezentacje Unicode i nieobsługiwany FB. [HMI manifest](workspace/hmi.manifest.json) rozdziela Unified/Classic oraz pokazuje moduł i funkcję w jednej jednostce hmi.js/hmi.yml z niepełnym coverage. [Konfiguracje](../config/examples/README.md) wiążą projekty i registry. Wszystko jest syntetyczne; zerowe raw/model hashes są placeholderami bez plików Siemens, a unit/capability hashes dotyczą tylko deklarowanych metadanych. [Format](../docs/12_WORKSPACE_CAPABILITIES.md), [walidacja](../tests/README.md).
