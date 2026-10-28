# Testy offline B-01A–C, B-02A–D oraz Discovery B-03A — bez Siemens

QA-01 dodaje [uniwersalny runner](../docs/QA_RUNNER.md): `.\qa.ps1 run fast`,
`.\qa.ps1 run standard` i `.\qa.ps1 run qualification`. Ten ostatni deleguje
niezmieniony runner kanoniczny z jawnym SHARED_WORKSTATION; STRICT pozostaje
dostępny. Wynik formalnej kwalifikacji pozostaje osobny od wykonania testów.
Testy runnera: `py -3 -B -m unittest discover -s tests/qa -v`.

[E2-B1-I](../docs/B03A_E2_B1_IPC_HANDLE_INVESTIGATION.md) izoluje net48 baseline,
rzeczywistą pętlę Program Workera z syntetyczną granicą vendor, IPC/heartbeat,
normalny Host/FakeWorker oraz sampler/audit. Testowy
[InfrastructureProbe](integration/TiaBridge.Worker.InfrastructureProbe/README.md)
nie ma referencji do Siemens. Runner Python 3.12/std library:
`py -3 -B scripts/investigate_worker_handles.py` po Build Release TiaBridge.slnx.
Opcja --smoke jest wyłącznie kontrolą plumbing. Wyniki i niepowodzenia są zachowane
poza Git, bez nowej kwalifikacji Engineering lub zmiany kryteriów B1.

[Controlled Qualification Bootstrap B0](../docs/19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md)
jest częścią pełnego Host suite: SQLite/ACL/CLI, 20 syntetycznych cykli istniejącego
schedulera, drift/revoke/expiry/dirty/replay, prawdziwy exit StorageProbe po rezerwacji
i pakiet dowodowy bez auto enrollment. Nie uruchamia Workera V19 ani vendor DLL.

[E2-B1-R](../docs/B03A_E2_B1_RESOURCE_INVESTIGATION.md) dodaje testy tego samego
VendorReader skompilowanego z jawnymi syntetycznymi typami API: finally Dispose,
enumeratory przy wyjątku, null service, timeout obserwatora bez abort, 20 odczytów
bez korzeni syntetycznych sesji, kolejność i błędy/limity loggera. Weak-reference
test używa kontrolowanego GC wyłącznie w procesie testowym. Net48 FrameworkProbe
sprawdza 20 sekwencyjnych dispatch/heartbeat/receipt i identyfikatory trace, bez
Siemens DLL. To regresje własnego kodu, nie dowód usunięcia historycznego wzrostu.

E2-B1-D dodaje BootstrapDiagnosticTests: stały budżet 3 przed IPC, odmowę czwartego,
crossed kind/schema/plan/budget, crash/restart/revoke/expiry/dirty/worker loss,
kompletność faz i zakaz B1 PASS/review/enrollment. Dotychczasowe B1 20-cyklowe
testy i E1 pozostają obowiązkowe. Nowe testy również nie uruchamiają Siemens.

Rzeczywisty, osobno autoryzowany E2-B1-D może użyć
[e2_diagnostic_session.py](../scripts/e2_diagnostic_session.py) z prywatnym --run-root.
Wymaga Python 3.12 i opcjonalnego psutil (lokalnie 7.0.0), istniejącego deploymentu,
nowego grantu 1.1 i E2-G-D READY. Nie tworzy grantu ani nie woła Siemens bezpośrednio.
Kontrole start-host/go-01..03 rozdzielają review operatora; brak parametrów liczby
cykli i retry. Po niepewnym call zachowuje własne stdin/Host do jawnego review STOP.

Wymagania: .NET SDK 10.0.4xx (global.json dopuszcza najnowszy patch tego pasma), Python 3.12. Zależności .NET są przypięte w csproj/packages.lock.json; QA Python w [requirements-qa.txt](../scripts/requirements-qa.txt). Pierwszy restore/install wymaga dostępu do repozytoriów pakietów; testy B-02C łączą się wyłącznie z własnym lokalnym Host; nie łączą się z Siemens lub obcymi endpointami.

## Pełny przebieg offline jednym poleceniem

Aktualne dowody są niezmienne i przypięte do run ID w `.tia-bridge/resilience/<run-id>/`; szczegóły [próbek NF 1.1](resilience/README.md#dowody-nf-02nf-03-od-formatu-11). Testy przygotowania: [raport D-11 preparation](resilience/D11_PREPARATION_2026-10-26.md). Pełnego polecenia poniżej nie wykonuje się w zadaniu ograniczonym do przygotowania harnessu.

Po jednorazowym przygotowaniu zależności według instrukcji niżej uruchom z root repo, używając Python z izolowanego środowiska QA:

```powershell
& $b01Python -B scripts/validate_offline.py
```

`$b01Python` wskazuje ścieżkę do Scripts/python.exe w tym środowisku; można użyć istniejącego venv z requirements-qa.txt. W bieżącej walidacji użyto `%LOCALAPPDATA%/TIA_Bridge2/dev/qa-b01a-20261025/Scripts/python.exe`. Runner nie instaluje zależności. Wymaga SDK i wszystkich pakietów .NET już w lokalnym cache; locked restore używa pustego lokalnego feedu, z wyłączonym sieciowym NuGet audit. Brak pakietu/SDK/dependency daje FAIL; nie następuje fallback do publicznego feedu. Zwykły restore opisany niżej służy jednorazowemu przygotowaniu cache i może wymagać sieci.

Runner kolejno wykonuje locked restore, Build Release, cały zestaw .NET, cały unittest JSON Schema/OpenAPI i przykładów, oba generatory w trybie check, audit_quality.py, dotnet format whitespace verify i git diff --check. Przed build sprawdza statyczną granicę zależności Core; test .NET sprawdza także tranzytywną closure assembly. Projekty testowe są wykonywane kolejno (`-m:1`), aby masowe testy granic JSON w Core nie konkurowały z deadline startu procesów Host/Fake. Nie zmienia to deadline produkcyjnych ani równoczesnego profilu ośmiu klientów. Wyniki obu projektów testowych są agregowane z oddzielnych TRX w nowym katalogu danego przebiegu; raport Host nie nadpisuje Core ani nie miesza się ze starymi PASS. TRX muszą zawierać niepuste wyniki, zero FAIL/SKIP, dokładnie 100 deterministycznych przypadków oraz wymagane przypadki SQLite/restart/fault B-02A oraz procesów IPC/schedulera/quarantine B-02B i HTTP/CLI/auth/performance B-02C. Błąd infrastruktury/brak dowodów kończy przebieg. Fail testów .NET/Python jest zbierany bez promowania do PASS, a kolejne walidatory i raport kwalifikacji są wykonywane. Końcowy kod 1 zachowuje test lub NF failure; kod 0 wymaga pozytywnego wykonania, ale formalne BLOCKED X-01 pozostaje osobno jawne. Raport, quality i logi kroków powstają w `resilience/<run-id>/validation/`, a TRX w `resilience/<run-id>/test-results/`, pod ignorowanym `.tia-bridge/`. Run ID jest nowy, pliki nie zastępują poprzedniego przebiegu; końcowy manifest wiąże cały zestaw przez SHA-256.

[Macierz pokrycia](COVERAGE.md) podaje istniejące testy, wykryte luki, nazwy nowych testów per 19 metod, naprawione błędy i ograniczenia. B-01C dodaje 377 testów .NET: 57 macierzy kontraktów, 100 golden scenariuszy, 147 lifecycle edges, 38 idempotency/budget, 1 phase/status, 13 boundary i 21 adversarial/dependency. Razem 537 .NET oraz 17 unittest Python (7 nowych); wewnętrznych pętli mutacji nie liczymy jako dodatkowych testów. Modelowe testy path/reparse nie wykonują filesystem Workspace IO. Rzeczywiste deterministyczne planowanie jest odłożone do B-04A; nie ma atrapy planera.

## B-02A — rzeczywiste SQLite i kontrolowane procesy testowe

[Host.Tests](integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj) dodaje 48 testów: przyjęcie/idempotencja/journal/outbox, awarie/restarty, migracje/polityka i artefakty. Razem z Core: **585 testów .NET**, osobno **17 testów Python**. Wymagany jest Windows i lokalny stały NTFS w `%LOCALAPPDATA%\TIA_Bridge2`; brak tych warunków daje FAIL, nie SKIP. Testy tworzą własne UUID katalogi `dev\storage-tests`, nigdy produkcyjnego state ani DB w repo. Sprzątanie sprawdza containment i nie przechodzi przez junction/reparse points. Testy nie wymagają TIA, Openness, PLC/HMI ani symulatorów.

[Scenariusze i granice](../docs/13_DURABLE_STORAGE.md): 100 jednoczesnych nowych submitów → jeden OperationId; wyścigi niezależnych procesów o idempotencję i outbox; proces kończony w dokładnym checkpoint przed/po commit; wynik SUCCEEDED i UNKNOWN po restarcie; SQL failure przy event/outbox; migracje v1→v3 i rollback; kontrolowany SQLITE_FULL/IO disk-full bez zapełniania dysku; przerwany stream, uszkodzone/brakujące artefakty i orphan retention. Rzeczywisty test junction jest ograniczony do własnego syntetycznego katalogu. [StorageProbe](integration/TiaBridge.StorageProbe/Program.cs) jest helperem testowym; B-02B dodaje do niego kontrolowany crash hosta z Fake Worker, nadal bez API. Nie jest to pełny harness B-02D ani kwalifikacja power-loss.

Do samych testów Storage po przygotowaniu cache:

```powershell
dotnet test tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj -c Release --no-restore
```

Przebieg całego pakietu nadal uruchamia `scripts/validate_offline.py`. Nowe csproj/packages.lock.json przypinają Microsoft.Data.Sqlite i zależności poza Core. Pierwsze pobranie pakietów wymaga zwykłego restore z siecią; późniejsze pełne przebiegi offline używają cache. Testy badają warstwę trwałości; nie uruchamiają zadań ani nie potwierdzają Engineering/Runtime.

## B-02B — rzeczywiste procesy Fake i Named Pipes

Ten sam Host.Tests dodaje **76 testów** B-02B. Łącznie **661 .NET (537 Core +48 Storage +76 Scheduler/IPC)** oraz **20 Python** (3 nowe testy schematu IPC). Scenariusze obejmują normalne/opóźnione wykonanie, duplicate dispatch/effect counter, cancel queued/running/race/backoff, hang/no heartbeat/heartbeat bez postępu, deadline/loss/exit, UNKNOWN i trwałą quarantine z epoch/fence, host process restart mutation/read, late success, bounded retry i breaker, singleton/lease/fence oraz negatywny IPC. Dziewięć wspólnych przykładów przechodzi .NET framing i niezależny JSON Schema. Test 1000 odczytów metadata sprawdza p95≤100 ms przy wiszącym procesie. Nie zastępuje on HTTP/8-klientowego testu B-02C/D.

Testy budują i uruchamiają własny TiaBridge.FakeWorker jako proces .NET 10 przez CurrentUserOnly Named Pipes. StorageProbe kończy host deterministycznie przez Environment.Exit(76) po Started; Fake kończy własne oczekiwanie po utracie pipe. Nie ma Siemens, PLC/HMI, symulatorów lub serwera HTTP. Heartbeat Fake ma interwał 2 s; logika deadline/backoff/lease/breaker ma kontrolowany zegar i jitter. Testy mogą trwać około minuty zależnie od hosta. Pełny harness B-02D, obcy SID, sprzętowy power-loss i rzeczywisty worker pozostają NOT_RUN.

Po przygotowaniu cache można uruchomić samą grupę:

```powershell
dotnet test tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Scheduler|FullyQualifiedName~IpcAndPolicy"
```

[Dokumentacja Scheduler/IPC](../docs/14_SCHEDULER_IPC.md) opisuje ograniczenia Fake i brak automatycznej rekoncyliacji TIA. Przebieg akceptacyjny to nadal pełny validate_offline.py, bez pomijania Core/Storage/Python/generatorów/audytu/formatu.

## B-02C — rzeczywiste HTTP API i CLI

Nowy zakres dodaje 38 przypadków Host (HttpApiTests 13, ApiSecurityTests 15, ApiMetadataAndCliTests 8, ApiPerformanceTests 2) i 1 regresję Core required-nullable. Łącznie **700 .NET (538 Core +48 Storage +76 Scheduler/IPC +38 HTTP/CLI)** i **20 Python**. Runtime ASP.NET Core 10 jest wymagany obok .NET SDK; nie dodano NuGet do Kestrel/ACL. Każdy proces Host ma własny UUID dev state na lokalnym NTFS i chroniony discovery. CLI jest rzeczywistym osobnym procesem, a wykonanie przechodzi przez istniejący Fake Worker/IPC.

Testy obejmują pełny cykl i wszystkie 19 nazw requestów z jawnym dostępem/UNSUPPORTED, metadata POST idempotency, queued/running cancel, token/Origin/Host/principal/ACL, limity body także chunked, rate, restart z rotacją tokenu i zachowaną historią, CLI offline/exit/wait bez auto-cancel oraz UNKNOWN po cancel/restart. Błąd startu własnego procesu przed dispatch daje trwały FAILED/NOT_STARTED, bez utraty dostępności API lub wycieku stdout/stderr dziecka. Przykład host.fake.json jest walidowany przez produkcyjny parser przy podmienieniu wyłącznie ścieżek na własne testowe. Procesowe kolekcje HTTP są serializowane względem innych testów procesowych, zamiast rozluźniania deadline workera pod obciążeniem kilku równoczesnych startów.

Profil NF: 1000 kompletnych HTTP status reads przy 1 i 8 klientach; 64 submit przy 8; 100 edycji 1 MiB na bieżącym repo exFAT, z jawnym odstępem 100 ms między zapisami, oraz niezależne SHA podczas fake Hang. Wszystkie zmierzone próbki są uwzględniane. Limity asercji pozostają NF-01 p95≤100/p99≤250 ms, NF-02 p95≤250/p99≤500 ms i NF-03 p95≤100 ms. Test nie kwalifikuje nieograniczonego ciągłego zapisu na dysku wymiennym ani conditional replace exFAT. Osobny test wypełnia 999 miejsc i ściga 4 requesty o ostatnie: 1 acceptance, 3 odmowy; przy 1000 duplicate/status/health nadal działają.

Raport metryk i środowiska: `.tia-bridge/b02c/performance.json`; TRX i logi są ignorowane. Dla samych nowych testów, po build/cache:

```powershell
dotnet test tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~HttpApiTests|FullyQualifiedName~ApiSecurityTests|FullyQualifiedName~ApiMetadataAndCliTests|FullyQualifiedName~ApiPerformanceTests"
```

Akceptacja wymaga całego validate_offline.py. [API/CLI i przykład uruchomienia](../docs/15_HTTP_API_CLI.md), [macierz](COVERAGE.md). B-02D/soak, drugi rzeczywisty SID, power-loss i Siemens pozostają poza tym przebiegiem.

## B-02D — resilience i odrębna kwalifikacja

Runner zachowuje `STRICT` jako wybór domyślny. Kandydacki profil współdzielonego
stanowiska wymaga jawnego argumentu `--profile SHARED_WORKSTATION`; preflight może
zwrócić `PASS_WITH_LOAD` i kontynuuje, jeśli nie ma konkretnego blokera bezpieczeństwa
lub środowiska. Szczegóły progów i statusów w [D-11-P](../docs/20_PERFORMANCE_QUALIFICATION_PROFILES.md).

Historyczny przebieg B-02D obejmował 48 nowych przypadków .NET oraz 4 Python reportera: łącznie 748 .NET i 24 Python, bez wliczania requestów z pętli jako odrębnych testów. D-11 dodaje 6 testów zgodności dokumentacyjnej (obecnie 30 Python w discovery), bez nowych testów silnika .NET. [Harness, bariery i polecenia](resilience/README.md), [raport techniczny](../docs/16_RESILIENCE_RECOVERY.md), [snapshot kwalifikacji](resilience/QUALIFICATION.md). Zestaw wykonuje prawdziwe 300 s idle, 1001 operacji, 100 identycznych submitów/8 klientów, crashy Host, peer attacks na Named Pipe, WAL-tail/artifacts i kontrolę własnych child/lock. Całość trwa kilkanaście minut zależnie od komputera. Nie wykonuje 8 h soak — decyzja użytkownika: NF-11 NOT_RUN w B-02D, B-10B/R-14 pozostaje właścicielem.

Raport .tia-bridge/resilience/<run-id>/validation/resilience.json oddziela wyniki xUnit/unittest od kwalifikacji NF i formalnej bramki. Każdy run ma własny UUID i hashe dowodów; stare pliki nie mogą dać PASS. FAIL NF-03 na repo volume powoduje OFFLINE FAIL/exit 1 nawet przy samych zielonych testach kontroli; lokalny NTFS ma osobny profil. Reporter i matrix.json zachowują historyczny konserwatywny sposób agregacji B-02D. Aktualna [ocena ograniczonej bramki D-11](resilience/X01_LIMITED_GATE.md) jest oddzielnym raportem przypiętym do niezmienionego snapshot; nie zmienia reporterów ani dawnych wyników. NF-11 ≥8 h pozostaje NOT_RUN/B-10B, ale nie jest warunkiem ograniczonego X-01 read-only na NTFS. Nowe dane lub zmiana kodu/środowiska wymagają ponownej oceny.

Dokumentacyjną zgodność decyzji i źródłowych dowodów, bez Host/Worker/Siemens, sprawdza:

```powershell
py -3 -B -m unittest discover -s tests/contracts -p test_limited_gate_documentation.py -v
py -3 scripts/audit_quality.py
```

Testy te kontrolują zachowanie historycznych hashy i statusów, powiązanie warunków RT/NF z dowodami oraz rozdzielenie profili/poziomów kwalifikacji. Nie są ponownym wykonaniem pomiarów lub testów odporności.

## Build i testy .NET

Z root repo:

```powershell
dotnet restore TiaBridge.slnx --locked-mode
dotnet build TiaBridge.slnx -c Release --no-restore
dotnet test TiaBridge.slnx -c Release --no-build --no-restore
dotnet format whitespace TiaBridge.slnx --no-restore --verify-no-changes
```

[Testy kontraktów Core](unit/TiaBridge.Core.Tests/ContractTests.cs) obejmują wszystkie 19 wejść i wyników, round-trip, nieznane/missing pola, przykłady negatywne, uprawnienia, dane nieufne, limity/UTF-8/depth/numbers, redagowanie błędów i współbieżną walidację. [Testy domenowe](unit/TiaBridge.Core.Tests/IdentityAndLifecycleTests.cs) obejmują tożsamości, sesje i wersje, kanonikalizację, scope idempotencji, terminalne UNKNOWN, timeout oraz anulowanie przed dispatch. Sprawdzamy brak zależności od Host/Siemens/PLCSIM/WinCC.

[WorkspaceTests](unit/TiaBridge.Core.Tests/WorkspaceTests.cs) sprawdzają B-01B: oryginalne nazwy/NFC, percent UTF-8, reserved names, case/Unicode/truncation collisions, limity 100/240, traversal/absolute/UNC/ADS, kompletność obserwacji ścieżki i reparse points od korzenia woluminu, graf manifestu, współdzielone jednostki HMI, round-trip unknown vendor data, wersje i raw/model/unit hashes. [CapabilityTests](unit/TiaBridge.Core.Tests/CapabilityTests.cs) sprawdzają deny by default, każdy wymiar klucza, projekt/implementację/kwalifikację oddzielnie od sesji, luki coverage, niezgodne wersje i snapshot/hash registry. Testy czytają wyłącznie syntetyczne fixtures; nie sprawdzają rzeczywistych symlinków ani TOCTOU filesystemu. Weryfikacja przykładów manifestów, konfiguracji Workspace i Capabilities jest częścią tych testów. Nie powstaje pipeline B-01C ani testy planera.

## Niezależne JSON Schema i OpenAPI

Utwórz osobne środowisko QA na lokalnym NTFS; nie zmieniaj Python starego OPC/Siemens. Systemowy pip musi obsługiwać opcję --python (pip 22.3 lub nowszy). Użycie --without-pip pozwala wykonać instalację przez istniejący pip, bez modyfikowania go.

```powershell
$b01Qa = Join-Path $env:LOCALAPPDATA 'TIA_Bridge2/dev/qa-b01a'
py -3 -m venv --without-pip $b01Qa
$b01Python = Join-Path $b01Qa 'Scripts/python.exe'
py -3 -m pip --python $b01Python install -r scripts/requirements-qa.txt
& $b01Python -B -m unittest discover -s tests/contracts -p 'test_*.py' -v
```

[Testy Python](contracts/test_contracts.py) używają niezależnego jsonschema i openapi-spec-validator: wszystkie schematy, 19 metod, wymagane parametry/błędy, 38 poprawnych komunikatów, 21 negatywnych przykładów, lokalne refs OpenAPI i reprodukowalność generatora. Nie pobierają refs z sieci.

[Niezależne testy schematów B-01B](contracts/test_workspace_contracts.py) w tym samym poleceniu discovery sprawdzają oba manifesty, obie konfiguracje oraz negatywne pola/wersje/enumy. [Macierz B-01C](contracts/test_core_matrix.py) sprawdza wspólne mutacje przez JSON Schema i schematy danych OpenAPI, wszystkie wymagane wyniki/błędy oraz 100 golden raw/model hashes. [Testy runnera](contracts/test_offline_runner.py) potwierdzają odmowę fałszywego PASS po fail/skip/braku testów. JSON Schema nie zastępuje walidacji semantycznej Core. B-01B/B-01C nie dodają zależności .NET ani Python.

## Kontrola dokumentacji

```powershell
py -3 scripts/generate_contracts.py
py -3 scripts/audit_quality.py
git diff --check
```

Audyt dokumentacji pozostaje oddzielny od walidacji OpenAPI i artefaktów B-01B. Wszystkie dane testowe są syntetyczne. Host udostępnia Storage/ledger B-02A, wykonanie Fake B-02B i HTTP/CLI B-02C. Brak testów live, IO synchronizacji i adapterów Siemens. Harness B-02D pozostaje syntetyczny i nie kwalifikuje capabilities Siemens lub produkcyjnej stabilności. Wyniki: OFFLINE; ENGINEERING i RUNTIME NOT_RUN. Następne pakiety wymagają osobnego zlecenia zgodnie z [backlogiem](../docs/08_IMPLEMENTATION_PLAN.md).

## X-01 / B-03A — oddzielny Engineering lane

Standardowe `validate_offline.py` i TiaBridge.slnx nie ładują Siemens. `V19OfflineTests` sprawdzają exact configuration binding, wersjonowanie/no downgrade, read-only dispatch, rzeczywisty net48 proces IPC bez vendor, negatywne observations oraz trwały wynik i artefakt. B-03A dodaje syntetyczne testy hierarchii, Unicode/duplikatów nazw, HMI Unified/Classic, limitów, UNKNOWN i braku fikcyjnych ObjectId. Capability tests sprawdzają osobny PROJECT scope, dokładny profil DiscoveryV1/build/read scope i brak dziedziczenia do DEVICE/OBJECT lub operacji zapisu/eksportu; v1 pozostaje kompatybilne. Regresja raportowania uruchamia subprocess z `PYTHONIOENCODING=cp1252` i wymaga bezstratnego UTF-8 w raporcie. Dziewięć historycznych przykładów IPC 1.1 jest sprawdzanych przez Python. E2-G dopuszcza sam offline build Workera w jawnym profilu SHARED_WORKSTATION, bez uruchamiania Siemens; domyślna ścieżka STRICT pozostaje bez tej zależności. Testy live wymagają [jawnej procedury Engineering](engineering/README.md); ich wyniki są w [raporcie X-01](../docs/X01_TIA_V19_QUALIFICATION.md) oraz [raporcie B-03A](../docs/B03A_DISCOVERY_QUALIFICATION.md). Nowe testy Engineering nie uruchamiają się w discovery offline. Historyczny NF-03 exFAT FAIL i NF-11 NOT_RUN nie są zastępowane nową kwalifikacją odczytu na NTFS.
