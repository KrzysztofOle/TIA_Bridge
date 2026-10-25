# Testy B-01A — wyłącznie offline

Wymagania: .NET SDK 10.0.4xx (global.json dopuszcza najnowszy patch tego pasma), Python 3.12. Zależności .NET są przypięte w csproj/packages.lock.json; QA Python w [requirements-qa.txt](../scripts/requirements-qa.txt). Pierwszy restore/install wymaga dostępu do repozytoriów pakietów; same testy nie łączą się z Siemens ani endpointami aplikacji.

## Build i testy .NET

Z root repo:

```powershell
dotnet restore TiaBridge.slnx --locked-mode
dotnet build TiaBridge.slnx --no-restore
dotnet test TiaBridge.slnx --no-build --no-restore
dotnet format whitespace TiaBridge.slnx --no-restore --verify-no-changes
```

[Testy kontraktów Core](unit/TiaBridge.Core.Tests/ContractTests.cs) obejmują wszystkie 19 wejść i wyników, round-trip, nieznane/missing pola, przykłady negatywne, uprawnienia, dane nieufne, limity/UTF-8/depth/numbers, redagowanie błędów i współbieżną walidację. [Testy domenowe](unit/TiaBridge.Core.Tests/IdentityAndLifecycleTests.cs) obejmują tożsamości, sesje i wersje, kanonikalizację, scope idempotencji, terminalne UNKNOWN, timeout oraz anulowanie przed dispatch. Sprawdzamy brak zależności od Host/Siemens/PLCSIM/WinCC.

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

## Kontrola dokumentacji

```powershell
py -3 scripts/generate_contracts.py
py -3 scripts/audit_quality.py
git diff --check
```

Audyt dokumentacji pozostaje oddzielny od walidacji OpenAPI. Wszystkie dane testowe są syntetyczne. Nie ma testów live, hosta, ledger, fake workera, IO synchronizacji ani adapterów. B-01A nie kwalifikuje odporności B-02 ani capabilities Siemens. Wyniki: OFFLINE; ENGINEERING i RUNTIME NOT_RUN. Następne pakiety wymagają osobnego zlecenia zgodnie z [backlogiem](../docs/08_IMPLEMENTATION_PLAN.md).
