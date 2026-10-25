# 05 — Interfejs CODEX: HTTP, CLI i opcjonalne MCP

Status: kontrakty v1 i walidacja Core zaimplementowane w B-01A; B-02C dostarcza [lokalne API i CLI](15_HTTP_API_CLI.md) nad Storage/Scheduler/Fake. Nie ma wykonawców Siemens; metody bez dostępnego profilu są odrzucane. Jedno źródło nazw i deadline w [registry](../contracts/v1/operations.json), wersjonowane [JSON Schema](../contracts/v1/operation-request.schema.json), [OpenAPI 3.1.1](../contracts/v1/openapi.json) i [przykłady](../examples/README.md). [Dokumentacja kontraktów](../contracts/README.md) opisuje modele, kanonikalizację i granice walidacji. Schema/check nie zastępuje rejestracji projektu, autoryzacji ani kwalifikacji Siemens.

## Wybór interfejsu

Oceny są oceną projektową dla tego przypadku, nie benchmarkiem protokołów.

| Model | Stabilność i obsługa CODEX | Async / restart | Diagnostyka i testowanie | Bezpieczeństwo / utrzymanie |
|---|---|---|---|---|
| tylko CLI | proste JSON i exit codes; ryzyko powrotu do blokującego procesu na każde call | bez osobnego serwera trudno o trwały status i wspólne ownership | łatwy smoke test, rozproszone logi | mało portów, lecz równoległe CLI muszą współdzielić scheduler |
| tylko MCP | wygodne tools, zależność od klienta i transportu | reconnect/MCP restart nie może posiadać kolejki; długie tool call jest złym domyślnym kontraktem | JSON Schema tools; trzeba badać zachowanie klienta | dodatkowa powierzchnia i wersje protokołu; nie daje samodzielnie uprawnień TIA |
| lokalne HTTP | stabilne request/status/result, zwykłe narzędzia HTTP i OpenAPI docelowo | 202, polling, trwałe IDs; rozłączenie klienta nie gubi operacji | łatwy fake client, replay i correlation | loopback, uwierzytelnienie, Origin/Host checks i limity obowiązkowe |
| **hybryda HTTP + CLI, MCP opcjonalnie** | jeden silnik i semantyka, mały CLI dla CODEX | trwałość w hoście, klienci wymienialni | wspólny registry i contract tests | umiarkowany koszt; izolowane fasady bez duplikacji schedulerów |

Rekomendacja: ostatni model. MCP Tasks z rewizji `2025-11-25` ma stan eksperymentalny i wymaga negocjacji capabilities; nie uzależniamy od niego działania. Zwykłe tool submit/status/result działa także bez Tasks. Jeśli później wspieramy Tasks, mapujemy task ID na `operation_id`, zachowując kwarantannę i semantykę cancel. [Specyfikacja MCP Tasks](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/tasks).

## Transport, kontrakt wspólny i status

HTTP bind wyłącznie loopback (IPv 4/IPv 6 jawnie), port przydzielany i publikowany w discovery file z ACL użytkownika na NTFS. Losowy token bieżącej sesji hosta przekazywany przez chroniony pipe/file, nie w URL/CLI/history. CLI czyta go wewnętrznie. Bez CORS wildcard; sprawdzanie Origin i Host, odrzucenie nieznanych browser origins chroni przed lokalnym CSRF/DNS rebinding. Aplikacja nie binduje LAN. MCP nie staje się publicznym serwerem TIA.

Długie operacje: `POST /v1/operations`, body `{schema_version, method, project_key?, idempotency_key, params}`. Wymagane `schema_version="1.0"`, nazwa z registry, niepusty klucz do 128 znaków. Projekt musi być wcześniej zarejestrowany przez operatora; klient nie wskazuje arbitralnej ścieżki AP19 ani PID do zapisu. Token transportowy identyfikuje SID; `authorization_ref` w params wskazuje odrębne, zweryfikowane uprawnienie E/S. Sekretów ani komend shell nie ma w body.

202 zawiera `operation_id` UUID, lifecycle `ACCEPTED|QUEUED`, `poll_after_ms`, `status_url`, `result_url`, `accepted_at`. Zwracany dopiero po trwałym intent/outbox. Ten sam `(principal,project_key,idempotency_key)` z identycznym canonical body (bez samego key) zwraca to samo ID i wynik; inne body daje `IDEMPOTENCY_MISMATCH` 409. Klucze dla mutacji przechowujemy z audit do jawnej archiwizacji, bez ponownego wykorzystania po krótkim TTL.

`GET /v1/operations/{id}` nie wywołuje TIA. Zwraca lifecycle, phase, `outcome=NOT_STARTED|CONFIRMED|UNKNOWN`, completed/total units, timestamps/elapsed/deadline, last progress, heartbeat ages, cancel state i error. Unknown total → null, nigdy fikcyjny procent. `GET /v1/operations/{id}/result`: 202 jeśli trwa; 200 terminal report; artefakty pobierane po ACL i SHA, stronicowane. `GET .../events?after_sequence=N` jest wznawialny; retencja nie usuwa aktywnych/pinned dowodów.

`POST /v1/operations/{id}/cancel` zapisuje żądanie i zwraca `CANCELLED`, `CANCEL_REQUESTED`, `CANNOT_INTERRUPT` albo `ALREADY_TERMINAL`; nie ukrywa trwającego vendor call. Anulowanie nie jest forced kill. Operacja administracyjna worker termination jest oddzielna od publicznego cancel i nie należy do MVP CODEX.

Szybkie metody `project.status`, `files.changes` (cached small scope), `operation.status/result/events/cancel`, `diagnostics.health` mapowane są na GET/POST opisane wyżej lub odpowiednie `/v1/projects/...` i `/v1/diagnostics/health`. Duży rescan `files.changes` przekracza budżet 100 ms → 202, a nie blokujący request. Każda akceptowana praca w tle używa tego samego ledger.

## Typy wejść

- `project_key`, `operation_id`, `plan_id`, `observation_id`, `snapshot_id`, `instance_id`, `baseline_revision` — nieprzezroczyste UUID/artifact IDs, nie ścieżki klienta.
- `scope` — niepusta lista bridge object IDs albo zarejestrowany scope ID; brak `*` dla mutacji. Inspect może jawnie prosić o pełny inventory z limitami.
- `authorization_ref` — ID istniejącego grantu, nigdy token samodzielnie wystawiony przez klienta.
- `expected_*` — SHA-256/revision/epoch; mismatch unieważnia plan, nie jest poradą do automatycznego overwrite.
- `timeout_s` — dodatnia liczba całkowita w dopuszczonym przedziale metody; nie rozszerza deadline grantu. `queue_timeout_s` domyślnie 300, max 3600.
- `fresh` — live snapshot albo cache; każde cache ma `observed_at`, `age_ms`, `session_epoch`, `coverage`. Cache nie spełnia samo warunku SYNCED po restarcie.
- `test_profile_id`, `scenario_id` — zarejestrowana konfiguracja testu, nie dowolny kod z żądania. Test runner dostaje immutable pakiet scenariusza.

## Operacje: wejście, wynik, uprawnienia i deadline

Wszystkie poniższe wyniki zawierają wspólny operation envelope i references do artefaktów. R=read-only, E=Engineering session/plan, S=isolated simulation/test, A=odrębne usunięcie. Uprawnienia są sprawdzane przed dispatch i przed efektem; R może zapisywać metadane Bridge, nigdy projekt TIA. Czasy to proponowane domyślne/max **sekundy fazy wykonania**, poza czasem kolejki; limit pojedynczego vendor call nie oznacza jego bezpiecznego przerwania.

| Metoda | Wymagane params i istotne opcje | Wynik merytoryczny | Prawa; default/max; błędy szczególne |
|---|---|---|---|
| `project.inspect` | `inventory_scope`; optional `fresh=true` | tożsamość API/build, project/session, devices, object inventory, coverage/capability | R; 30/120; IDENTITY_MISMATCH, ATTACH_DENIED, TIA_UNAVAILABLE |
| `project.export` | `scope`; optional `include_native=true`, `observation_id` | immutable export snapshot, manifest/hash, unsupported/error items; destination staging, bez nadpisania W | R; 120/600; EXPORT_FAILED, COVERAGE_GAP, STORAGE_FULL |
| `project.status` | optional `scope`, `fresh=false` | cached sync vector, B/C/E/T freshness, quarantine; fresh=true tworzy operację inspect/verify | R; 1/5 cached, live 30/120; STALE_OBSERVATION |
| `files.changes` | `workspace_id`; optional `scope`, `rescan=false` | content changes, added/missing/tombstone/invalid; digest source revision | R; 10/120 background scan; PATH_UNSAFE, WORKSPACE_CHANGED |
| `sync.plan` | `workspace_id`, `scope`; optional `baseline_revision`, `observation_id`, `delete_objects=[]`, `resolution_ref` | immutable plan+hash, S, expected P/B, DAG, issues, eligibility; zapis tylko metadata | R; 60/300; CONFLICT, UNKNOWN_INITIAL, DEPENDENCY_UNRESOLVED, UNSUPPORTED |
| `sync.apply` | `plan_id`, `expected_plan_sha256`, `authorization_ref`, `expected_session_epoch` | step receipts, partial scope, backup/compile/save/verify refs; verified revision i workspace_advanced | E (+A na delete); 300/1800 całej sagi; STALE_PLAN, BACKUP_FAILED, IMPORT_PARTIAL, READBACK_MISMATCH |
| `sync.verify` | `scope`; optional `plan_id`, `snapshot_id`, `promote_baseline=false`, `expected_baseline_revision` | fresh P vs S/W, per-dimension evidence i status; CAS promotion tylko dla kompletnej grupy | R, promotion wg polityki sesji/initial adopt; 120/600; COVERAGE_GAP, VERIFY_FAILED, BASELINE_RACE |
| `compile.plc` | `scope`, `authorization_ref`; optional `warnings_policy_id` | compiler state, błędy/ostrzeżenia z hierarchy/path, fingerprint C, elapsed | E; 300/1200; COMPILER_UNAVAILABLE, COMPILE_ERRORS, WARNINGS_REJECTED |
| `compile.hmi` | `scope`, `authorization_ref`; optional `warnings_policy_id` | analogiczny raport dla dokładnego HMI device/family | E; 300/1200; COMPILER_UNAVAILABLE, COMPILE_ERRORS, WARNINGS_REJECTED |
| `simulation.prepare` | `test_profile_id`, `compiled_artifact_id`, `authorization_ref` | potwierdzenie izolacji, lease i fingerprint programu; ewentualny load do symulacji jest osobno zapisany | S; 300/900; RECIPIENT_UNVERIFIED, LICENSE_UNVERIFIED, LOAD_FAILED |
| `simulation.start` | `test_profile_id`, `authorization_ref`; optional `instance_id` | instance lease, STARTING/RUNNING; READY tylko po wszystkich readiness checks | S; 120/600; SIM_UNAVAILABLE, LICENSE_UNVERIFIED, NOT_READY |
| `simulation.stop` | `instance_id`, `authorization_ref`; optional `release_owned_resources=false` | stop receipt, final state i cleanup results; nie zamyka cudzej sesji | S; 60/180; NOT_OWNER, STOP_UNCONFIRMED |
| `test.plc` | `test_profile_id`, `scenario_id`, `transport=opcua\|snap7`; optional `authorization_ref`, `allow_writes=false` | assertions, samples/quality/timestamps, mapping hash, latency i raport; diagnostyka to nie PASS logiki PLC | R dla zatwierdzonego odczytu; S+grant na write; 120/1800; SECURITY_PROFILE_REQUIRED, ENDPOINT_MISMATCH, BAD_QUALITY, CONNECTION_LOST, TEST_FAILED |
| `test.hmi` | `test_profile_id`, `scenario_id`, `authorization_ref` | test IDs, screen/navigation/value/alarm results, screenshot/trace refs, browser/runtime versions | S; 180/1800; TLS_UNTRUSTED, LOGIN_REQUIRED, SELECTOR_UNSUPPORTED, RUNTIME_NOT_READY |
| `operation.status` | `operation_id` | trwały lifecycle, phase, outcome, heartbeat ages i error | właściciel/authorized reader; 1/5; NOT_FOUND, ACCESS_DENIED |
| `operation.result` | `operation_id` | terminal report lub 202; immutable artifact IDs/hashes, result schema | jw.; 1/5 metadata; NOT_FOUND, RESULT_NOT_READY |
| `operation.events` | `operation_id`; optional `after_sequence=0`, `limit=100` (max 1000) | zdarzenia z sequence i continuation, bez źródeł/sekretów | jw.; 1/5; NOT_FOUND, CURSOR_INVALID |
| `operation.cancel` | `operation_id`; optional `reason` do 512 znaków | cancel disposition i observed lifecycle; może zwrócić CANNOT_INTERRUPT | właściciel/operator; 1/5; NOT_FOUND, ACCESS_DENIED, ALREADY_TERMINAL |
| `diagnostics.health` | optional `project_key`, `include_cached_components=true` | host/store/queue/worker/known TIA health, circuit state i ages, bez live Attach | R; 1/5; STORE_UNAVAILABLE, DEGRADED |

`simulation.start` nigdy nie implikuje load/RUN, a `simulation.prepare` bez oddzielnej kwalifikacji load kończy UNSUPPORTED. Test Snap7 podlega [11](11_SNAP7_PLC_TESTING.md); parametr transport nie omija bramek OPC ani nie włącza PUT/GET.

## Błędy i polityka timeout

Stały envelope: `{code, category, message, retryable, outcome, phase, evidence_refs, recovery_hint}`. `message` jest redagowany, bez credentiali/pełnych source bodies. `retryable=true` tylko gdy wiadomo, że nie rozpoczęto efektu i adapter kwalifikuje retry. HTTP 200 dla terminalnego raportu FAILED nie jest sukcesem projektu; klient sprawdza lifecycle/outcome.

400 INVALID_ARGUMENT/SCHEMA_VERSION; 401 UNAUTHENTICATED; 403 ACCESS_DENIED/AUTHORIZATION_EXPIRED/PHYSICAL_DOWNLOAD_DENIED; 404 NOT_FOUND; 409 CONFLICT/STALE_PLAN/IDEMPOTENCY_MISMATCH/PROJECT_QUARANTINED/BASELINE_RACE; 413 LIMIT_EXCEEDED; 422 UNSUPPORTED/VALIDATION_FAILED; 429 QUEUE_FULL; 503 STORE_UNAVAILABLE/WORKER_UNAVAILABLE/CIRCUIT_OPEN. Po zaakceptowaniu operation_id błąd wykonania jest w operation report, nie ginie jako samo HTTP timeout.

Każda metoda ma registry default/max. Queue timeout, execution deadline, heartbeat threshold i krótszy timeout klienta to różne zegary. Host po deadline publikuje TIMEOUT i, przy możliwym efekcie, `outcome=UNKNOWN`/RECONCILIATION_REQUIRED. Wykonawca nie ma gwarantowanej możliwości przerwania vendor call. Klient timeoutu HTTP ponawia **ten sam klucz**, nie tworzy nowego Apply.

## CLI i korzystanie przez CODEX

Docelowa składnia; B-02C implementuje submit METHOD --request FILE oraz operation status/result/events/cancel/wait i health. Skróty --scope/--workspace pozostają przyszłym zakresem; aktualne przykłady są w dokumencie 15:

```text
tiabridge submit sync.plan --project <key> --workspace <id> --scope <id> --key <request-key> --json
tiabridge submit sync.apply --request <immutable-request.json> --json
tiabridge operation status <operation_id> --json
tiabridge operation result <operation_id> --json
tiabridge operation cancel <operation_id> --reason <text> --json
tiabridge operation wait <operation_id> --max-wait 30 --json
```

Exit codes projektowe: 0 przyjęte/odczyt udany; 2 parametry/kontrakt; 3 odmowa policy/conflict; 4 transport/host; 5 jawny bounded wait zakończony przed terminalnym wynikiem; 6 terminal FAILED/RECONCILIATION_REQUIRED w wait/result. JSON zawiera acceptance/result, aby kod 0 submit nie był mylony z Apply PASS. Brak automatycznego cancel po zakończeniu `--max-wait`.

CODEX po submit zapisuje ID w kontekście, wykonuje niezależne prace i odpytuje status w krótkich krokach. Nie trzyma narzędzia wykonawczego otwartego przez 20 minut. Restart MCP/CLI nie przerywa operacji; restart hosta uruchamia recovery według [03](03_ARCHITECTURE.md). Nadal nie ma gwarancji, że CODEX może wykonywać zależną operację TIA na zablokowanym projekcie.
