# Kontrakty v1 — B-01A i B-01B

Zaimplementowane są modele i walidacja offline w [Core](../src/TiaBridge.Core/README.md). Semantykę 19 metod określa [interfejs CODEX](../docs/05_CODEX_INTERFACE.md). Host Storage/ledger i syntetyczny scheduler/Fake istnieją jako biblioteki; publiczne handlery i rzeczywiste operacje TIA nie istnieją. Poprawny JSON nie potwierdza odbiorcy ani uprawnień.

## Artefakty

- [operation-request.schema.json](v1/operation-request.schema.json): wejścia 19 metod, zamknięte envelope/params, wymagane pola i warunkowe odwołania do grantu.
- [operation.schema.json](v1/operation.schema.json): status/raport z wynikiem zależnym od metody. SUCCEEDED wymaga typowanego result i CONFIRMED; RECONCILIATION_REQUIRED wymaga UNKNOWN. Wynik nieudanej operacji może opisywać pracę częściową.
- [operation-result.schema.json](v1/operation-result.schema.json): DTO podsumowań i odwołań do dowodów. Szczegółowy manifest jest osobnym artefaktem B-01B; plany, baseline i scenariusze pozostają artefaktami następnych pakietów. Nie rozszerzono wyników 19 metod.
- [error.schema.json](v1/error.schema.json): kody z registry/dokumentu 05, kategorie, redagowany message, outcome i evidence refs. Retryable wymaga NOT_STARTED; sam nie uprawnia do retry.
- [acceptance.schema.json](v1/acceptance.schema.json): ACCEPTED/QUEUED, ID, polling, względne URL i accepted_at. Trwałość implementuje B-02A, a wykonanie syntetyczne B-02B.
- [session-identity.schema.json](v1/session-identity.schema.json): binding projektu/epoch/PID/start/handshake, bez odczytu Siemens.
- [operations.json](v1/operations.json): nazwy, wymagane params, limity i wymagane prawa; DESIGN_ONLY oznacza brak handlerów, nie brak walidatora.
- [openapi.json](v1/openapi.json): OpenAPI 3.1.1, wspólny POST z 19 wariantami oraz status/result/events/cancel i health. Opis endpointów nie oznacza ich implementacji. Token transportowy jest poza body i nie nadaje praw TIA.
- [workspace-manifest.schema.json](v1/workspace-manifest.schema.json), [workspace-config.schema.json](v1/workspace-config.schema.json), [capability-registry.schema.json](v1/capability-registry.schema.json): artefakty B-01B walidowane przez WorkspaceJson/CapabilityJson. Osobne schematy 1.0 nie zmieniają wire operacji; OperationMethod ma te same nazwy, np. project.export. Extension bag vendor_fields zachowuje unknown vendor JSON, pozostałe pola są zamknięte. Schematy sprawdzają kształt; Core dodatkowo sprawdza graf, kolizje, membership i unit hash. [Dokumentacja B-01B](../docs/12_WORKSPACE_CAPABILITIES.md) definiuje format i odrębny profil canonical hashes artefaktów, w tym dokładne liczby dziesiętne. Hash manifestu nie nadaje statusu SYNCED.

Wire schema_version ma dokładnie wartość `1.0`; inne wersje są odrzucane. Numer OpenAPI 3.1.1 i info.version 1.0.0 są niezależne. Schematy Draft 2020-12 mają wyłącznie lokalne refs. Zmiany semantyki wymagają jawnego wersjonowania zgodnie z dokumentem 04; parser nie zgaduje zgodności przyszłych wersji.

## Macierz modeli

Każdy wiersz ma schemat wejścia/wyjścia, klasę parametrów, klasę wyniku oraz testy pozytywne i negatywne. W raporcie akceptowane są błędy wspólne dokumentu 05 i błędy szczególne danej metody z registry.

| Metoda | DTO wejścia | DTO wyniku |
|---|---|---|
| project.inspect | ProjectInspectParameters | ProjectInspectResult |
| project.export | ProjectExportParameters | ProjectExportResult |
| project.status | ProjectStatusParameters | ProjectStatusResult |
| files.changes | FilesChangesParameters | FilesChangesResult |
| sync.plan | SyncPlanParameters | SyncPlanResult |
| sync.apply | SyncApplyParameters | SyncApplyResult |
| sync.verify | SyncVerifyParameters | SyncVerifyResult |
| compile.plc | CompilePlcParameters | CompilePlcResult |
| compile.hmi | CompileHmiParameters | CompileHmiResult |
| simulation.prepare | SimulationPrepareParameters | SimulationPrepareResult |
| simulation.start | SimulationStartParameters | SimulationStartResult |
| simulation.stop | SimulationStopParameters | SimulationStopResult |
| test.plc | TestPlcParameters | TestPlcResult |
| test.hmi | TestHmiParameters | TestHmiResult |
| operation.status | OperationStatusParameters | OperationStatusResult |
| operation.result | OperationResultParameters | OperationResultResult |
| operation.events | OperationEventsParameters | OperationEventsResult |
| operation.cancel | OperationCancelParameters | OperationCancelResult |
| diagnostics.health | DiagnosticsHealthParameters | DiagnosticsHealthResult |

DTO operation.status/result są projekcjami statusu i odwołań do raportu wskazanej operacji. GET result zwraca raport operacji docelowej albo 202 przy braku wyniku. Events/cancel mają osobne wyniki zdarzeń/disposition. Udany odczyt metadanych nie oznacza sukcesu operacji docelowej.

Regresja B-01C: zagnieżdżone state/outcome w operation.status/result podlegają tym samym regułom lifecycle co envelope (np. SUCCEEDED wymaga CONFIRMED). Generator, oba schematy wyników i OpenAPI sprawdzają te istniejące reguły także w projekcji docelowej; poprawny odczyt statusu nie maskuje niespójnego wyniku. Nie dodano stanu ani operacji i nie zmieniono wersji 1.0.

## Walidacja

Granica `ContractJson` odrzuca nieznane pola na każdym poziomie, duplicate keys także po odkodowaniu escape, błędny UTF-8, niepoprawny JSON, komentarze, trailing commas, nieznane metody/enumy/wersje. Limity: request 1 MiB, raport 10 MiB, pojedynczy event 64 KiB, głębokość 32, leksykalna liczba do 128 znaków i tablice według schematów. Wszystkie liczby wire v1 są całkowite Int64, z dodatkowymi limitami pól; after_sequence ma jawną granicę Int64. Nie zaokrąglamy przez double ani nie zamieniamy ułamka podprogowego na zero. Daty są RFC 3339; start procesu sesji wymaga UTC. Core dodatkowo sprawdza zgodność error/outcome, completed_units/total_units i binding sesji.

Klasyfikacja wejścia: INVALID_ARGUMENT, SCHEMA_VERSION, VALIDATION_FAILED, LIMIT_EXCEEDED. Wyjątki mają stały komunikat bez wartości, nazw nieznanych pól, pełnego JSON i wyjątków parsera. Nie logujemy payloadów. Producenci raportów muszą redagować message/diagnostics przed użyciem DTO; biblioteka nie wykrywa automatycznie sekretów w dowolnym tekście.

Typowany zapis wire i konstrukcja IdempotencyKey odrzucają również niepoprawne pary UTF-16 surrogate; nie zastępują ich po cichu U+FFFD. Testy graniczne i wspólny korpus .NET/JSON Schema/OpenAPI opisuje [macierz B-01C](../tests/COVERAGE.md). Limity bajtów i dodatkowe zależności semantyczne Core nie są zastępowane samą walidacją JSON Schema.

Authorization_ref jest referencją, nie grantem. Nie przyjmujemy obiektów uprawnień, pól permissions, arbitralnych komend, skryptów, ścieżek projektów ani PID do mutacji. Faktyczna rejestracja i weryfikacja polityki pozostają przyszłemu hostowi/workerowi.

## Kanonikalizacja i idempotencja

Po walidacji canonical body jest UTF-8 bez BOM, odstępów i końcowego LF. Klucze są sortowane ordinal według UTF-16. Stringi są odkodowane i ponownie kodowane domyślnym encoderem System.Text.Json: równoważne escape dają te same bajty. Zadeklarowane pola UUID mają małe litery i format D. Liczby całkowite mają zapis dziesiętny bez wykładnika, zbędnych zer i ujemnego zera. Zachowujemy kolejność tablic, treść zwykłych stringów, Unicode, EOL i wielkość liter artifact IDs. Nie uzupełniamy opcji: brak pola i jawna wartość domyślna to różne body.

Wyłączony jest wyłącznie root idempotency_key. SHA-256 canonical body jest zapisany małymi literami hex. [Testy](../tests/unit/TiaBridge.Core.Tests/IdentityAndLifecycleTests.cs) obejmują stabilność, escape, równoważne liczby/UUID i ochronę kopii bajtów. Ta para daje ten sam hash:

```json
{"schema_version":"1.0","method":"operation.events","idempotency_key":"synthetic","params":{"operation_id":"abcdefab-1111-4111-8111-111111111111","limit":1.0}}
```

```json
{"params":{"limit":1e0,"operation_id":"ABCDEFAB-1111-4111-8111-111111111111"},"idempotency_key":"synthetic","method":"operation.events","schema_version":"1.0"}
```

RequestIdentity porównuje `(authenticated principal, project_key, idempotency_key)` i hash body. Principal pochodzi z uwierzytelnionego kontekstu, nie JSON. Ten sam scope i hash to SameRequest, zmieniony hash to Mismatch (przyszły host: 409 IDEMPOTENCY_MISMATCH), inny principal/projekt/key to DifferentScope. B-01A nie zawiera ledger, przydzielania trwałych ID, deduplikacji efektów ani replay. UNKNOWN nie staje się zgodą na ponowienie.

## Lifecycle i generowanie

OperationLifecycle sprawdza legalne przejścia; stany terminalne nie wracają do kolejki. Timeout możliwej mutacji daje RECONCILIATION_REQUIRED/UNKNOWN, bez efektu FAILED/NOT_STARTED. Żaden nie dowodzi zatrzymania procesu lub wywołania. Anulowanie przed dispatch nie deklaruje efektu. Status synchronizacji, kompilacji, symulacji i Runtime ma osobne typy.

[Generator](../scripts/generate_contracts.py) zawiera definicje wyników i czyta schemat wejścia oraz registry. Generuje schematy, OpenAPI, modele C# i [38 przykładów](../examples/README.md). Nie edytuj ręcznie oznaczonego kodu wygenerowanego. Transport jest oddzielony od modeli domenowych; przyszły worker net48 nie musi współdzielić DLL Core.

```powershell
py -3 scripts/generate_contracts.py
py -3 scripts/generate_contracts.py --write
```

[Instrukcja testów](../tests/README.md) opisuje build, testy .NET, niezależne JSON Schema/OpenAPI i zależności QA. Wyniki są wyłącznie OFFLINE; ENGINEERING i RUNTIME pozostają NOT_RUN.

## IPC B-02B

[Odrębny schemat IPC 1.0](ipc/v1/message.schema.json) i [opis transportu](../docs/14_SCHEDULER_IPC.md) definiują dziewięć typów komunikatów, epoch/fence/sequence i bounded artifact references. To kontrakt Host ↔ Fake Worker, bez zmian semantyki 19 operacji API. Shared fixtures są walidowane przez .NET i JSON Schema; hash/HMAC, limity bajtów i stan połączenia są dodatkowo sprawdzane w kodzie.
