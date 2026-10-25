# Testy B-01A–B-01C i B-02A–C — bez Siemens

Wymagania: .NET SDK 10.0.4xx (global.json dopuszcza najnowszy patch tego pasma), Python 3.12. Zależności .NET są przypięte w csproj/packages.lock.json; QA Python w [requirements-qa.txt](../scripts/requirements-qa.txt). Pierwszy restore/install wymaga dostępu do repozytoriów pakietów; testy B-02C łączą się wyłącznie z własnym lokalnym Host; nie łączą się z Siemens lub obcymi endpointami.

## Pełny przebieg offline jednym poleceniem

Po jednorazowym przygotowaniu zależności według instrukcji niżej uruchom z root repo, używając Python z izolowanego środowiska QA:

```powershell
& $b01Python -B scripts/validate_offline.py
```

`$b01Python` wskazuje ścieżkę do Scripts/python.exe w tym środowisku; można użyć istniejącego venv z requirements-qa.txt. W bieżącej walidacji użyto `%LOCALAPPDATA%/TIA_Bridge2/dev/qa-b01a-20261025/Scripts/python.exe`. Runner nie instaluje zależności. Wymaga SDK i wszystkich pakietów .NET już w lokalnym cache; locked restore używa pustego lokalnego feedu, z wyłączonym sieciowym NuGet audit. Brak pakietu/SDK/dependency daje FAIL; nie następuje fallback do publicznego feedu. Zwykły restore opisany niżej służy jednorazowemu przygotowaniu cache i może wymagać sieci.

Runner kolejno wykonuje locked restore, Build Release, cały zestaw .NET, cały unittest JSON Schema/OpenAPI i przykładów, oba generatory w trybie check, audit_quality.py, dotnet format whitespace verify i git diff --check. Przed build sprawdza statyczną granicę zależności Core; test .NET sprawdza także tranzytywną closure assembly. Projekty testowe są wykonywane kolejno (`-m:1`), aby masowe testy granic JSON w Core nie konkurowały z deadline startu procesów Host/Fake. Nie zmienia to deadline produkcyjnych ani równoczesnego profilu ośmiu klientów. Wyniki obu projektów testowych są agregowane z oddzielnych TRX w nowym katalogu danego przebiegu; raport Host nie nadpisuje Core ani nie miesza się ze starymi PASS. TRX muszą zawierać niepuste wyniki, zero FAIL/SKIP, dokładnie 100 deterministycznych przypadków oraz wymagane przypadki SQLite/restart/fault B-02A oraz procesów IPC/schedulera/quarantine B-02B i HTTP/CLI/auth/performance B-02C. Pierwszy błąd kończy przebieg kodem 1; kod 0 oznacza wszystkie bramki PASS. Raport `.tia-bridge/offline/run.json`, quality.json i logi kroków są zastępowane przy kolejnym uruchomieniu; TRX mają osobny UUID katalog. Wszystko pozostaje ignorowane w Git.

[Macierz pokrycia](COVERAGE.md) podaje istniejące testy, wykryte luki, nazwy nowych testów per 19 metod, naprawione błędy i ograniczenia. B-01C dodaje 377 testów .NET: 57 macierzy kontraktów, 100 golden scenariuszy, 147 lifecycle edges, 38 idempotency/budget, 1 phase/status, 13 boundary i 21 adversarial/dependency. Razem 537 .NET oraz 17 unittest Python (7 nowych); wewnętrznych pętli mutacji nie liczymy jako dodatkowych testów. Modelowe testy path/reparse nie wykonują filesystem Workspace IO. Rzeczywiste deterministyczne planowanie jest odłożone do B-04A; nie ma atrapy planera.

## B-02A — rzeczywiste SQLite i kontrolowane procesy testowe

[Host.Tests](integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj) dodaje 48 testów: przyjęcie/idempotencja/journal/outbox, awarie/restarty, migracje/polityka i artefakty. Razem z Core: **585 testów .NET**, osobno **17 testów Python**. Wymagany jest Windows i lokalny stały NTFS w `%LOCALAPPDATA%\TIA_Bridge2`; brak tych warunków daje FAIL, nie SKIP. Testy tworzą własne UUID katalogi `dev\storage-tests`, nigdy produkcyjnego state ani DB w repo M:/exFAT. Sprzątanie sprawdza containment i nie przechodzi przez junction/reparse points. Testy nie wymagają TIA, Openness, PLC/HMI ani symulatorów.

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

Audyt dokumentacji pozostaje oddzielny od walidacji OpenAPI i artefaktów B-01B. Wszystkie dane testowe są syntetyczne. Host udostępnia Storage/ledger B-02A, wykonanie Fake B-02B i HTTP/CLI B-02C. Brak testów live, IO synchronizacji i adapterów Siemens. Testy offline nie zastępują pełnego harnessu B-02D ani kwalifikacji capabilities Siemens. Wyniki: OFFLINE; ENGINEERING i RUNTIME NOT_RUN. Następne pakiety wymagają osobnego zlecenia zgodnie z [backlogiem](../docs/08_IMPLEMENTATION_PLAN.md).
