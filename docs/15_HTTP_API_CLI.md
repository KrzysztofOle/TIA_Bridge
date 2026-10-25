# 15 — Lokalne HTTP API i CLI B-02C

Stan 2026-10-25: działający proces Host ASP.NET Core Minimal API i klient CLI, na istniejących [Storage](13_DURABLE_STORAGE.md), [schedulerze/Fake Worker/IPC](14_SCHEDULER_IPC.md) i [kontraktach v1](05_CODEX_INTERFACE.md). Bez Siemens, MCP lub pełnego harnessu B-02D. Dostępność endpointu i synthetic SUCCEEDED nie potwierdzają Engineering, Runtime lub SYNCED.

## Proces i konfiguracja

[TiaBridge.Host](../src/TiaBridge.Host/README.md) jest teraz procesem .NET 10, nie tylko biblioteką Storage. Wymaga Windows, lokalnego stałego NTFS, runtime ASP.NET Core 10 i jawnego pliku konfiguracji. [Syntetyczny przykład](../config/examples/host.fake.json) używa osobnego dev state i UUID 111…, bez projektów AP19 lub sekretów. Uruchamiaj go z root repo; StatePath rozwija zmienne środowiskowe, względne repo/worker paths są rozwiązywane względem katalogu uruchomienia. Parser ma limit 256 KiB, głębokość 32, dokładną wersję 1.0, zamknięte pola i odmowę duplikatów. Nowa aplikacja nie adoptuje obcego katalogu ani bazy.

```powershell
dotnet build TiaBridge.slnx -c Release
dotnet src/TiaBridge.Host/bin/Release/net10.0/TiaBridge.Host.dll --config config/examples/host.fake.json
```

Host publikuje tylko stałe READY na stdout. Nie drukuje tokenu. Ctrl+C kończy aplikację. Opcja `--stdio` umożliwia nadzorcy własnego procesu zakończenie przez dokładne `stop` na odziedziczonym stdin; nie jest endpointem HTTP i nie wykonuje komend systemowych. W testach umożliwia potwierdzony shutdown bez zabijania procesów. Niepewne RUNNING jest klasyfikowane przy recovery, bez fałszywego sukcesu podczas shutdown.

Projekt rejestruje operator przez `registered_projects` w konfiguracji przed publikacją API. Nie istnieje endpoint wskazujący AP19/PID ani rejestrujący odbiorcę na żądanie klienta. Domyślnie brak FakeProfiles oznacza brak wykonawcy. W B-02C tylko jawne Fake profile `project.inspect` i `sync.apply` mogą uruchamiać proces Fake; ten drugi wymaga dodatkowo `enable_synthetic_mutations=true`. Nie nadaje to żadnych praw Siemens. Sukces pochodzi z jawnego syntetycznego wzorca raportu; konfiguracja, scenario lub worker path nie są przyjmowane z HTTP. Fake uruchamia supervisor po trwałym dispatch, przez istniejący IPC. Nie uruchamiaj go ręcznie z tokenem w argv.

## Endpointy i trwałość

| Endpoint | Odpowiedź |
|---|---|
| POST /v1/operations | istniejący request 19 metod; 202 Acceptance dopiero po commit dla nowego zlecenia; 200 raport istniejącego klucza lub szybkiej metody metadata |
| GET /v1/operations/{id} | 200 istniejący OperationReport z journalu, bez hashowania plików i bez IPC; Last-Modified pochodzi z ostatniego trwałego eventu |
| GET /v1/operations/{id}/result | 202 bieżący report + Retry-After/Location, jeśli wynik niegotowy; 200 terminal report po sprawdzeniu zarządzanych referencji i SHA |
| GET /v1/operations/{id}/events | 200 OperationReport metody operation.events; `after_sequence>=0`, `limit=1..1000`, uporządkowane event IDs i next_sequence |
| POST /v1/operations/{id}/cancel | 200 OperationReport metody operation.cancel, opcjonalne zamknięte body `{reason}` do 512 znaków; disposition i observed state |
| GET /v1/diagnostics/health | 200 OperationReport metody diagnostics.health; opcjonalny project_key i include_cached_components; bez probe do TIA |

Status GET zwraca raport wskazanej operacji. Events/cancel/health są raportami sukcesu odczytu lub zapisu metadata, a ich `result` opisuje operację docelową; SUCCEEDED tego envelope nie potwierdza sukcesu targetu. Historyczny terminalny status nie jest nowym dowodem integralności plików. GET result weryfikuje pliki; nie ma nowego endpointu pobierania artefaktów poza zatwierdzonym OpenAPI. SSE nie zostało zaimplementowane; polling jest samodzielny.

API używa schema_version=1.0 i istniejących DTO Core. Acceptance ma dokładnie obecne pola, bez dodawania niedozwolonego schema_version do zamkniętego schematu; wersję transportu potwierdza nagłówek X-TiaBridge-Contract-Version: 1.0. GET status zawiera accepted/started/completed/deadline, phase/outcome, cancel, last_progress i aktualny worker heartbeat age tylko przy zgodnym epoch. Last-Modified ma precyzję standardowego HTTP do sekundy; dokładny czas zmiany daje journal. Diagnostics rozdziela host/store/queue/scheduler/worker/worker_progress/tia_probe/quarantine. Brak postępu nie jest maskowany żywym heartbeat, a tia_probe pozostaje UNKNOWN/null.

Pięć szybkich metod POST — operation.status/result/events/cancel i diagnostics.health — korzysta z tego samego ledgeru/idempotencji/outbox. Ich przyjęcie, odczyt/cancel, komplet zdarzeń i terminalny raport zapisują się w jednej krótkiej transakcji bez wywołania workera. Powtórzony key zwraca tę samą zapisaną obserwację; świeży odczyt wymaga GET lub nowego klucza. POST operation.result jest typowaną projekcją metadata, GET result zwraca właściwy raport z kontrolą integralności. Nie powstaje drugi lifecycle ani kolejka. Pozostałe metody wymagają wykonawcy; jego brak daje 422 UNSUPPORTED bez acceptance. Test przechodzi wszystkie 19 poprawnych requestów i sprawdza ich rzeczywisty dostępny wynik/odmowę.

Długie zlecenie nie wykonuje w handlerze snapshotu, hashowania, skanowania ani Fake call. SQLite ogranicza liczbę aktywnych ACCEPTED/QUEUED/RUNNING łącznie do 1000; quota jest sprawdzana atomowo po trwałym lookup idempotencji. Wyścig o ostatnie miejsce przyjmuje dokładnie jedno zlecenie. Pełna kolejka nie blokuje duplikatu ani odczytu statusu/health. Niepewna mutacja zachowuje UNKNOWN/quarantine także po cancel i restarcie; nowe przyjęte zlecenia jej projektu nie są dispatchowane. Nie ma clear quarantine ani retry mutacji przez API.

## Uwierzytelnienie i limity

Kestrel binduje wyłącznie 127.0.0.1 na przydzielonym porcie. IPv6/LAN nie są w tym profilu włączane; ASPNETCORE_URLS i konfiguracja endpointów z env nie zmieniają bindu. Request wymaga zgodnego numerycznego Host i loopback peer, dokładnie jednego Authorization: Bearer oraz tokenu bieżącej instancji. Token identyfikuje SID operatora; każdy operation resource dodatkowo sprawdza principal zapisany w SQLite. JSON ani query nie mogą nadpisać principal. Posiadanie tokenu nie nadaje praw importu/Save/PLC/HMI.

Losowe 32 bajty tokenu, SID, PID/start i instance UUID są publikowane w `state/api/host.json`. Directory/file ACL przyznają dostęp wyłącznie bieżącemu SID; owner i DACL są sprawdzane przed publikacją oraz przez CLI. Nie poszerza się ani nie naprawia ACL obcego katalogu. Discovery wymaga lokalnego NTFS pod LOCALAPPDATA/TIA_Bridge2, bez UNC, reparse points i ADS. Klient odrzuca obcą wersję/owner/ACL, duplicate/unknown pola, limit 16 KiB i niezgodny PID/start. Token jest czytany wewnętrznie, bez argv/URL/history/logów. Restart zmienia token; stary nie działa. Nie jest to ochrona przed administratorem lub wrogim procesem tego samego użytkownika, a test z rzeczywistym drugim kontem pozostaje osobną kwalifikacją.

Brak interfejsu przeglądarkowego: każde Origin, także null, Sec-Fetch-* i forwarded host jest odrzucane; brak CORS wildcard. Klient wyłącza proxy, redirects i cookies. Odpowiedzi mają no-store/nosniff. Nie ma endpointu shell, zmiany konfiguracji lub administracyjnego kill. Host wymusza środowisko Production niezależnie od ASPNETCORE_ENVIRONMENT (bez developer error pages). Logowanie HTTP/header/body zostało wyłączone, a błędy są stałym DTO bez payloadu i sekretów.

Request ≤1 MiB, cancel body ≤4 KiB i reason ≤512 znaków; JSON UTF-8 i zamknięte query/params. Nagłówki ≤16 KiB, body/header timeout 5 s. Domyślnie max 2048 requests/s, 256 POST/s i 64 in-flight/connections, z zarezerwowaną połową concurrency dla odczytów. Konfiguracja może tylko zmniejszać limity. Nie ma nieograniczonej kolejki oczekujących HTTP; przeciążenie daje 429/Retry-After. Parsery i wynik zachowują limity kontraktów, raport ≤10 MiB i event ≤64 KiB. Wynik niegotowy to 202, nie fałszywy sukces projektu.

## CLI i pełny cykl CODEX

[CLI](../src/TiaBridge.Cli/README.md) jest cienkim klientem [TiaBridge.Client](../src/TiaBridge.Client/README.md), bez magazynu, schedulera i wykonawcy. W drugim terminalu, z root repo po uruchomieniu powyższego przykładu:

```powershell
$cli = 'src/TiaBridge.Cli/bin/Release/net10.0/TiaBridge.Cli.dll'
$discovery = Join-Path $env:LOCALAPPDATA 'TIA_Bridge2/dev/api-example/api/host.json'
$accepted = dotnet $cli submit project.inspect --request examples/contracts/v1/request.project.inspect.json --discovery $discovery --json | ConvertFrom-Json
$operationId = $accepted.operation_id
dotnet $cli operation status $operationId --discovery $discovery --json
dotnet $cli operation events $operationId --after-sequence 0 --limit 100 --discovery $discovery --json
dotnet $cli operation result $operationId --discovery $discovery --json
```

Domyślny submit kończy się po acceptance, nie po wyniku. CODEX zachowuje operation_id i może wykonywać niezależne czynności. Przykład ma stały syntetyczny key — ponowienie zwraca tę samą operację, a nowa praca wymaga nowego klucza w request JSON. Wiersz result może jeszcze zwrócić RUNNING/202. Jawne, ograniczone oczekiwanie i anulowanie:

```powershell
dotnet $cli operation wait $operationId --max-wait 10 --discovery $discovery --json
dotnet $cli operation result $operationId --discovery $discovery --json
dotnet $cli operation cancel $operationId --reason 'synthetic request' --discovery $discovery --json
dotnet $cli health --discovery $discovery --json
```

Obsługiwane: submit METHOD --request FILE, operation status/result/events/cancel/wait ID, health, --json i --discovery PATH. `submit ... --wait --max-wait N` jest jawny; brak N jest błędem. Max wait 1..3600 s, każdy HTTP call ma własny limit 5 s; Ctrl+C lub wyjście CLI nie anuluje zaakceptowanego intentu. Brak flag token/url/force i brak automatycznego retry submit. Skrótów składania requestu z --scope/--workspace jeszcze nie ma; wersjonowany plik JSON jest wejściem. Normalny profil używa domyślnego discovery LOCALAPPDATA/TIA_Bridge2/state/api/host.json.

Kody: 0 acceptance/udany odczyt, 2 argumenty/kontrakt, 3 odmowa policy/auth/conflict/unsupported, 4 transport/Host, 5 bounded wait bez terminalnego wyniku, 6 terminal FAILED/RECONCILIATION_REQUIRED w result/wait. Status nie zmienia kodu na 6 za sam odczyt FAILED. Każde wyjście jest JSON, bez interaktywnego promptu; kod 0 submit nie jest Engineering PASS.

## Wydajność, testy i granice

Host utrzymuje jeden pusty uchwyt lifetime SQLite, aby nie zamykać ostatniego połączenia WAL po każdym call, oraz osiem wyłącznych, krótkich reader connections do metadata. Nie współdzieli się aktywnego command/transaction i nie ma równoległych writerów. Metadata nie jest drugim ledgerem/cache: każdy odczyt sprawdza aktualny DB report/hash i principal. Nadal WAL/FULL/FK, NTFS i kontrola ścieżek. Readers są zamykane po Kestrel/scheduler shutdown. Scheduler przy skanowaniu kolejki odczytuje zamrożony queue deadline, a pełną walidację wykonuje przed zmianą wygasłego zlecenia.

[Testy integracyjne](../tests/README.md) uruchamiają prawdziwe Host, CLI i Fake Worker, bez Openness. Profil NF: 1000 status reads przy 1 i 8 klientach, 64 submit przy 8, 100 edycji 1 MiB z odstępem 100 ms i niezależne SHA podczas fake Hang; state NTFS, syntetyczny workspace na bieżącym repo exFAT. Test edycji to zapis pliku w miejscu, bez deklaracji kwalifikacji conditional replace na exFAT. Pomiary p50/p95/p99, środowisko i liczba próbek trafiają do ignorowanego .tia-bridge/b02c/performance.json. Każdy zmierzony sample pozostaje w percentylach; nie usuwa się outlierów. Błąd progu powoduje FAIL.

To ograniczone pomiary B-02C na obecnym komputerze, nie 8-godzinny soak/B-10 lub pełne B-02D. Po testach sprawdzane jest zakończenie własnych procesów i katalogów testowych. Nie implementowano autoryzacji operacji Siemens, fizycznego downloadu, MCP, reconciliation lub pełnego retention/GC. OFFLINE jest odrębne od ENGINEERING/RUNTIME NOT_RUN; bramka przed X-01 nadal wymaga B-02D.
