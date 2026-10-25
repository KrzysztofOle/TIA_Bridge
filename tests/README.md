# Testy B-01A–B-01C — wyłącznie offline

Wymagania: .NET SDK 10.0.4xx (global.json dopuszcza najnowszy patch tego pasma), Python 3.12. Zależności .NET są przypięte w csproj/packages.lock.json; QA Python w [requirements-qa.txt](../scripts/requirements-qa.txt). Pierwszy restore/install wymaga dostępu do repozytoriów pakietów; same testy nie łączą się z Siemens ani endpointami aplikacji.

## Pełny przebieg B-01C jednym poleceniem

Po jednorazowym przygotowaniu zależności według instrukcji niżej uruchom z root repo, używając Python z izolowanego środowiska QA:

```powershell
& $b01Python -B scripts/validate_offline.py
```

`$b01Python` wskazuje ścieżkę do Scripts/python.exe w tym środowisku; można użyć istniejącego venv z requirements-qa.txt. W bieżącej walidacji użyto `%LOCALAPPDATA%/TIA_Bridge2/dev/qa-b01a-20261025/Scripts/python.exe`. Runner nie instaluje zależności. Wymaga SDK i wszystkich pakietów .NET już w lokalnym cache; locked restore używa pustego lokalnego feedu, z wyłączonym sieciowym NuGet audit. Brak pakietu/SDK/dependency daje FAIL; nie następuje fallback do publicznego feedu. Zwykły restore opisany niżej służy jednorazowemu przygotowaniu cache i może wymagać sieci.

Runner kolejno wykonuje locked restore, Build Release, cały zestaw .NET, cały unittest JSON Schema/OpenAPI i przykładów, oba generatory w trybie check, audit_quality.py, dotnet format whitespace verify i git diff --check. Przed build sprawdza statyczną granicę zależności Core; test .NET sprawdza także tranzytywną closure assembly. TRX musi zawierać niepuste wyniki, zero FAIL/SKIP i dokładnie 100 osobnych deterministycznych przypadków. Pierwszy błąd kończy przebieg kodem 1; kod 0 oznacza wszystkie bramki PASS. Raport `.tia-bridge/offline/run.json`, TRX, quality.json i logi kroków są ignorowanymi wynikami roboczymi; kolejne uruchomienie zastępuje te raporty. Nie trafiają do Git.

[Macierz pokrycia](COVERAGE.md) podaje istniejące testy, wykryte luki, nazwy nowych testów per 19 metod, naprawione błędy i ograniczenia. B-01C dodaje 377 testów .NET: 57 macierzy kontraktów, 100 golden scenariuszy, 147 lifecycle edges, 38 idempotency/budget, 1 phase/status, 13 boundary i 21 adversarial/dependency. Razem 537 .NET oraz 17 unittest Python (7 nowych); wewnętrznych pętli mutacji nie liczymy jako dodatkowych testów. Modelowe testy path/reparse nie wykonują filesystem Workspace IO. Rzeczywiste deterministyczne planowanie jest odłożone do B-04A; nie ma atrapy planera.

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

Audyt dokumentacji pozostaje oddzielny od walidacji OpenAPI i artefaktów B-01B. Wszystkie dane testowe są syntetyczne. Nie ma testów live, hosta, ledger, fake workera, IO synchronizacji ani adapterów. B-01A/B-01B nie kwalifikują odporności B-02 ani capabilities Siemens. Wyniki: OFFLINE; ENGINEERING i RUNTIME NOT_RUN. Następne pakiety wymagają osobnego zlecenia zgodnie z [backlogiem](../docs/08_IMPLEMENTATION_PLAN.md).
