# Scripts

Place repository maintenance and development scripts here. Scripts must not connect to or modify a TIA Portal project unless the command and its safeguards are explicit, documented, and separately reviewed.

## LAB-03O — synchronizacja overwrite i snapshot, offline

[test_lab_event_sync.ps1](test_lab_event_sync.ps1) kompiluje [lab_event_sync_tests.cs](lab_event_sync_tests.cs) z dokładnymi źródłami laboratoryjnymi: 40 testów filesystemu/odczytanych wartości oraz integracja opublikowanego events.json z compare-events. Wymaga Windows PowerShell 5.1 x64, systemowego .NET Framework csc, Python 3 przez `py -3`, zapisywalnego udziału localhost X$ i NTFS TEMP dla junction. Bez pakietów, nowych udziałów/mapowań ani wywołań Siemens. Polecenie: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_sync.ps1`. Nowe dane w X:\TESTY_TIA_Bridge\evidence\LAB-03O-offline-UUID, ignorowanym .tia-bridge/ i TEMP. Test subprocess celowo kończy wyłącznie własny syntetyczny EXE przez Exit 77; nie dotyka Siemens. Bez recursive delete, zachowuje dowody. Exit 0 = wszystkie kontrole PASS. Build oraz pełna regresja K/L/M/N według poniższych instrukcji; dodatkowo `py -3 -m unittest discover -s scripts -p test_compare_events.py`. [Kontrakt, recovery, wyniki i gotowe polecenie live](../docs/LAB_03O_EVENT_OVERWRITE.md).

## LAB-03N — lokalizacja eksportu zdarzeń, offline

[test_lab_event_output.ps1](test_lab_event_output.ps1) kompiluje [lab_event_output_tests.cs](lab_event_output_tests.cs) z dokładnym laboratoryjnym walidatorem i publisherem. Wymaga Windows PowerShell 5.1 x64, systemowego .NET Framework csc, dostępnego zapisywalnego udziału `\\localhost\X$` i NTFS w TEMP dla ACL/junction. Bez nowych pakietów i wywołań Siemens. Polecenie: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_output.ps1`. 18 testów używa nowych syntetycznych katalogów w ignorowanym `.tia-bridge/`, `X:\TESTY_TIA_Bridge\evidence\LAB-03N-offline-UUID` i TEMP. Nie tworzy udziałów ani mapowań; tymczasowy deny ACL dotyczy wyłącznie nowego własnego katalogu i jest przywracany w finally. Zachowuje dowody, junction i dane testowe, bez usuwania plików. Exit 0 oznacza wszystkie testy PASS; niedostępność infrastruktury testowej nie jest PASS. [Kontrakt, wyniki i ograniczenia](../docs/LAB_03N_EVENT_OUTPUT_LOCATION.md).

## LAB-03M — diagnostyka konsoli, offline

[test_lab_event_console.ps1](test_lab_event_console.ps1) kompiluje [lab_event_console_tests.cs](lab_event_console_tests.cs): 14 syntetycznych przypadków konsoli oraz kompatybilność artefaktów i lifecycle względem zachowanego LAB-03L. Wymaga Windows PowerShell 5.1 x64, systemowego .NET Framework csc i zbudowanego EXE, bez nowych pakietów i Siemens calls. Argument `-BaselineDirectory` wskazuje katalog zachowanych źródeł LAB-03L sprzed zmian. Testuje niepełne CLI z exit 1 przed Attach; nie podaje poprawnych argumentów live. Dane w nowym X:\TESTY_TIA_Bridge\evidence\LAB-03M-offline-UUID, exit 0 oznacza wszystkie kontrole PASS. Polecenie, klasyfikacja i ograniczenia: [LAB-03M](../docs/LAB_03M_EVENT_CONSOLE_DIAGNOSTICS.md).

## LAB-03L — czytelny widok zdarzeń, offline

[test_lab_event_view.ps1](test_lab_event_view.ps1) kompiluje [lab_event_view_tests.cs](lab_event_view_tests.cs) i wykonuje 16 syntetycznych testów oraz byte regression 9 artefaktów względem zachowanego źródła LAB-03K. Wymaga Windows PowerShell 5.1 x64 i systemowego .NET Framework csc, bez nowych pakietów/Siemens. Argument `-BaselineSource` wskazuje zachowany LabEventExport.cs sprzed zmian; nie wskazuj bieżącego źródła jako baseline. Przykład i kryteria: [LAB-03L](../docs/LAB_03L_READABLE_EVENT_EXPORT.md). Wyniki w nowym X:\TESTY_TIA_Bridge\evidence\LAB-03L-offline-UUID, exit 0 = PASS. Nie modyfikuje historycznych eksportów.

## LAB-03K — eksport zdarzeń, wyłącznie build/test offline

[build_lab_events.ps1](build_lab_events.ps1) buduje laboratoryjny EXE z `LabExporter.cs`, `LabEventExport.cs`, `LabEventVendor.cs`, `LabEventView.cs`, `LabEventConsole.cs` i `LabEventSync.cs` pod `X:\TESTY_TIA_Bridge\src`, zachowując poprzedni EXE i manifest builda w nowym `evidence\LAB-03K-build-<UUID>`. Nie wywołuje żadnego polecenia eksportera ani API Siemens. Wymaga Windows PowerShell 5.1 x64, istniejącego frameworkowego `csc.exe`, systemowych assembly .NET Framework i lokalnego PublicAPI V19; bez nowych pakietów i kopiowania vendor DLL. Polecenie: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_lab_events.ps1`. Błąd kompilacji zachowuje dotychczasowy EXE; błąd zapisu pliku jest odmową, bez omijania blokady działającego eksportera.

[test_lab_events.ps1](test_lab_events.ps1) kompiluje dokładny laboratoryjny reader/publisher wraz z [lab_events_tests.cs](lab_events_tests.cs). Uruchamia 42 syntetycznych testów oraz kontrolę argumentów nowej konfiguracji launch przez syntetyczny program i odmowę CLI przy brakujących parametrach. Test IL ładuje assembly jako metadata i sprawdza publiczną allowlistę 39 typów, brak vendor mutacji i dynamizations w nowej ścieżce. Wymaga tego samego `csc.exe`, .NET Framework i zbudowanego EXE; inspekcja metadata wymaga zainstalowanego V19. Bez Attach, GetProcesses lub innych wywołań Siemens. Polecenie: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_events.ps1`. Dane, program testowy i raporty: nowe `X:\TESTY_TIA_Bridge\evidence\LAB-03K-offline-<UUID>`; exit 0 = wszystkie kontrole PASS. Testy nie zastępują Engineering ani rzeczywistego F5. [Kontrakt laboratoryjny i ograniczenia](../docs/LAB_03K_HMI_EVENT_EXPORT.md).

## LAB-03I — nazwa pliku definicji globalnych

[test_lab03i.ps1](test_lab03i.ps1) uruchamia wyłącznie offline istniejący laboratoryjny splitter, walidator, assembler i publisher. Wymaga Windows PowerShell 5.1 x64, `pwsh.exe` (PowerShell 7), zbudowanego `X:\TESTY_TIA_Bridge\bin\LabExporter.exe` oraz sąsiedniego `Esprima.dll`; bez nowych pakietów. Polecenie: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03i.ps1`. Nie wywołuje Attach ani API Siemens. Syntetyczne wejścia i wyniki pozostają w nowych katalogach UUID pod laboratoryjnymi `exports` i `evidence`, a kopia między woluminami w `%TEMP%\LAB-03I-stage-<UUID>`. Exit 0 oznacza PASS wszystkich przypadków; exit 1 oznacza FAIL. Raport i granice: [LAB-03I](../docs/LAB_03I_GLOBAL_DEFINITION_NAMING.md).

## LAB-03J — diagnostyka natywnego eksportu

[test_lab03j.ps1](test_lab03j.ps1) wymaga Windows PowerShell 5.1 x64, aktualnego laboratoryjnego EXE oraz systemowych bibliotek .NET Framework `System.IO.Compression` / `System.IO.Compression.FileSystem`; bez nowych pakietów. Polecenie: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03j.ps1`. Wykonuje 15 syntetycznych przypadków walidacji/diagnostyki natywnej pary i weryfikacji backupu. Nie wykonuje Attach ani żadnego wywołania Siemens; używa istniejącego wzorca refleksji i raportu. Wyniki pozostają w nowych `X:\TESTY_TIA_Bridge\evidence\LAB-03J-offline-<UUID>`, diagnostyka w osobnych `LAB-03J-native-<UUID>.json`. Exit 0 = wszystkie PASS, exit 1 = FAIL. [Raport LAB-03J](../docs/LAB_03J_NATIVE_EXPORT_DIAGNOSTICS.md) rozdziela testy od nieodtworzonej historycznej przyczyny.

## D-11 preparation — próbki i read-only preflight

[performance_evidence.py](performance_evidence.py) wymaga wyłącznie Python 3.12/std library. Waliduje pełne próbki 1.1, niezmienne manifesty i SHA oraz odtwarzalność percentyli, bez uruchamiania RT/NF. Używają go reporter i runner; `allow_legacy=True` służy tylko jawnemu odczytowi archiwalnych agregatów z INSUFFICIENT, bez nowej kwalifikacji PASS. Nowe raporty runnera są w `.tia-bridge/resilience/<run-id>/validation/`, TRX w `test-results/`, próbki w `performance/`; na końcu powstaje `evidence-manifest.json`. Stare wspólne ścieżki opisane w historii B-01/B-02 poniżej pozostają archiwalne i nie są nadpisywane.

[d11_preflight.ps1](d11_preflight.ps1) wymaga Windows PowerShell 5.1+, CIM/.NET, Git i .NET CLI, bez nowych pakietów lub elevacji. Przykład: `powershell -NoProfile -File scripts/d11_preflight.ps1 -OutputDirectory X:\Projects\TIA_Bridge\.tia-bridge\preparation\d11-20261026`. Odczytuje metadata procesów Git/Siemens i usług, 5 obserwacji Git z pauzami 2 s, NTFS/reparse, CPU/RAM/dyski/AV i runtime; zapisuje nowy UUID JSON przez CreateNew w podanym własnym katalogu `.tia-bridge`. Redaguje typowe credentiale command line; surowego raportu nie commitować. Nie wykonuje Attach, odczytu projektu, start/stop procesów, zmiany konfiguracji ani RT/NF. Obecność fsmonitor/edytora/usługi nie jest sama w sobie FAIL; główny Portal jest raportowany oddzielnie. Brak danych oznacza ograniczenie obserwacji, a nie zgadywanie stanu.

Instrukcja profilu i formatu: [resilience README](../tests/resilience/README.md), wynik przygotowania: [raport](../tests/resilience/D11_PREPARATION_2026-10-26.md).

## Kontrola pakietu projektowego

`audit_quality.py` jest lokalnym walidatorem dokumentacji i projektowych kontraktów, nie implementacją Bridge. Nie ładuje vendor DLL, nie łączy się z TIA/OPC/HTTP i nie wykonuje kodu znalezionego w dokumentach.

Wymagania: Python 3.12, pakiet `jsonschema` (w analizowanym środowisku dostępny). Jeśli go brakuje, użyj odrębnego środowiska narzędziowego i zainstaluj `jsonschema` zgodnie z lokalną polityką; nie zmieniaj Python używanego przez Siemens lub stare OPC. Pakiet nie jest dependency aplikacji.

Z root repo:

```powershell
py -3 scripts/audit_quality.py
py -3 scripts/audit_quality.py --report .tia-bridge/quality.json
```

Exit 0 = PASS, exit 1 = wykryte niespójności, exit 2 = niepoprawny argument. Raport zapisuje się tylko do ignorowanego `.tia-bridge/`; stdout pokazuje liczby i błędy. Walidator sprawdza lokalne linki/fences i poprawność kolumn tabel, 12 wymaganych dokumentów, 19 metod w registry/schema/docs, deadline/required fields, Draft2020-12, pierwotne syntetyczne przykłady i 8 negatywnych kontraktów, 24 audit rows, 16 resilience cases i 12 etapów. Zewnętrzne odnośniki są liczone, lecz nieodpytywane sieciowo. Nie sprawdza producenta, renderingu Mermaid, semantyki PLC ani pełnego OpenAPI; niezależne testy schematów/OpenAPI B-01A opisuje [instrukcja testów](../tests/README.md).

## Generator kontraktów B-01A

`generate_contracts.py` wymaga wyłącznie standardowej biblioteki Python 3.12. Czyta operation-request.schema.json i operations.json oraz jawne definicje wyników w skrypcie; generuje lokalne schematy/DTO/OpenAPI/przykłady, bez wywołań sieci lub Siemens. Domyślnie tylko sprawdza zgodność wygenerowanych plików, a jawne --write zapisuje zmiany. Exit 0 oznacza zgodność, nie gotowość API. Nie generuje handlerów, hosta ani adapterów.

```powershell
py -3 scripts/generate_contracts.py
py -3 scripts/generate_contracts.py --write
```

Zależności niezależnej walidacji QA określa [requirements-qa.txt](requirements-qa.txt). Instaluj je w osobnym venv na NTFS zgodnie z [instrukcją](../tests/README.md), bez globalnej zmiany Python. Wygenerowane DTO C# kompilują się w Core .NET 10; nie wymagają Siemens. Pliki robocze pozostają w ignorowanym `.tia-bridge/` lub lokalnym środowisku QA poza repo.

## Walidacja offline B-01C

[validate_offline.py](validate_offline.py) korzysta z istniejących dotnet/unittest/generator/audit narzędzi. Wymagania: Python 3.12 z requirements-qa.txt w izolowanym venv, .NET SDK z global.json, Git i wypełniony lokalny cache pakietów z lockfiles. Polecenie domyślne: `& $b01Python -B scripts/validate_offline.py` (`STRICT`). Zatwierdzony profil SHARED_WORKSTATION 1.0.0 wybiera się jawnie `--profile SHARED_WORKSTATION`. Wersja, hash definicji i zastosowane progi trafiają do run-bound evidence. Nie instaluje pakietów i nie uruchamia API/Siemens; locked restore ma wyłącznie pusty lokalny feed oraz NuGetAudit=false, bez sieciowego fallbacku. Przechodzi wszystkie bramki z [instrukcji testów](../tests/README.md), odrzuca niepełny TRX/skip/fail i zapisuje ignorowany `.tia-bridge/offline/run.json`. Sprawdzenie zależności obejmuje Core csproj, locked transitive packages, source imports i testowane assembly refs; nie jest skanerem dowolnej przyszłej refleksji/pluginów.

B-02A rozszerza ten sam przebieg o Host/Storage i lokalne testy SQLite na Windows NTFS. Oddzielne TRX w katalogu UUID przebiegu są agregowane, z obowiązkowymi przypadkami restartu, awarii, idempotencji i artefaktów. StorageProbe jest tylko procesem testowym; B-02A sam nie uruchamia serwera ani schedulera. Baza testowa powstaje poza repo w lokalnym katalogu aplikacji, a raporty pozostają w `.tia-bridge/offline/`.

[generate_test_vectors.py](generate_test_vectors.py) wymaga tylko standardowej biblioteki Python. Domyślnie sprawdza dwa syntetyczne pliki w tests/fixtures; `--write` odtwarza je deterministycznie. Literalne prezentacje nazw oraz Python hashlib wyznaczają 100 różnych wektorów modeli/raw SHA-256 i wspólny korpus mutacji request/report/error dla .NET/Python. Skrypt nie odwołuje się do Core ani planera, nie materializuje Workspace i nie łączy się z siecią. Zmiana oczekiwań wymaga przeglądu kontraktu i testów, nie automatycznego przyjmowania wyjścia Core.

```powershell
py -3 scripts/generate_test_vectors.py
py -3 scripts/generate_test_vectors.py --write
```

B-02B rozszerza ten sam validate_offline.py o procesy Fake Worker/Named Pipes i wymagane przypadki schedulera, timeout, restartu, quarantine oraz dziewięć wspólnych fixtures IPC. Całość: 661 .NET i 20 Python; żaden test nie wymaga Siemens. Procesy testowe uruchamiają wyłącznie własny zbudowany Fake Worker; brak publicznego API/CLI i pełnego harnessu B-02D.

B-02C rozszerza wspólny validate_offline.py o obowiązkowe procesy Host/CLI/Fake, security, metadane, kolejkę 1000 i rzeczywiste pomiary NF-01/02/03. Przebieg jest nadal bez Siemens i bez sieciowego restore, ale testy uruchamiają własny serwer wyłącznie loopback oraz edytują własny syntetyczny plik. Aktualny zestaw: 700 .NET, 20 Python. Raporty HTTP performance w .tia-bridge/b02c; szczegóły profilu i komend w tests/README.md.

B-02D: validate_offline.py rozszerza ten sam pipeline o wymagane process/crash/security/resource cases, wspólny UUID dowodów i report_resilience.py. Nowy reporter wymaga tylko biblioteki standardowej Python; odczyt CPU/RAM/AV używa wbudowanego Windows PowerShell/Get-CimInstance, bez zmiany ochrony. Odrzuca brak/stare/skip dowody, zachowuje failed cases jako FAIL bramki i wylicza osobno bramkę i X-01 readiness. Wyniki w .tia-bridge/resilience, sanitized snapshot w tests/resilience. [Uruchomienie](../tests/resilience/README.md). Exit 1 może oznaczać failure kwalifikacji NF mimo samych PASS testów wykonania. Pełny 8 h soak nie jest uruchamiany przez B-02D.

## Audyt środowiska V19 przed X-01

[audit_v19_environment.ps1](audit_v19_environment.ps1) wymaga Windows PowerShell/PowerShell na Windows, standardowych Win32/CIM/.NET i uprawnienia do odczytu lokalnej instalacji. Nie instaluje pakietów, nie zmienia grup/ACL/rejestru Siemens, nie wykonuje Attach i nie czyta kluczy licencyjnych. Parametry `-OutputDirectory` i `-ProjectRoot` wskazują uprzednio utworzony własny root StorageLocation pod LOCALAPPDATA/TIA_Bridge2/dev/x01 na NTFS oraz zatwierdzony katalog projektów. Wynik environment.json powstaje z CreateNew poza repo; powtórzenie nie nadpisuje dowodu. Skrypt zbiera wersje Windows/Framework/SDK, publiczne pliki i SHA, PID/start/SID, typy dysków i metadata AP19. Nie commituj prywatnego raportu. Gdy execution policy odmawia uruchomienia z exFAT, nie zmieniaj polityki: użyj zatwierdzonej kopii tego samego skryptu na własnym lokalnym NTFS z kontrolą SHA. Szczegóły i ograniczenia: [Engineering](../tests/engineering/README.md).

## Bramka E2-G

[e2_gate.py](e2_gate.py) wymaga Python 3.12/std library. Polecenie:
`py -3 -B scripts/e2_gate.py --run-id <run-id> --dossier <reviewed-local-json> --output .tia-bridge/e2-g-reviewed.json`.
Bez dossier ocenia dowody offline i raportuje brak bindingu. Nie uruchamia procesów
Siemens ani nie aktywuje bootstrapu. Format, kody i znaczenie trzech statusów są w
[21](../docs/21_E2_B1_READINESS_GATE.md). Runner z jawnym SHARED_WORKSTATION buduje także Worker V19 Release
z lokalnych referencji i cache (locked offline restore), bez uruchamiania Workera;
CreateNew build-metadata wiąże exact Host/Worker z dowodem i źródłami.

## Porównywanie zdarzeń HMI offline

[compare_events.py](compare_events.py) wymaga Python 3.12 i tylko stdlib. CLI:
`py -3 -B scripts/compare_events.py compare-events --before <events.json> --after <events.json> --output <parent>`.
[compare-events.ps1](compare-events.ps1) wymaga Windows PowerShell 5.1 i py launcher;
przy braku parametrów pyta wyłącznie o trzy ścieżki. Jest launcherem konfiguracji VS Code COMPARE EVENTS.
Nie ładuje Siemens ani nie wykonuje JS. Wyniki w nowych `EventComparisons/<run-id>/`, np. pod ignorowanym `.tia-bridge/`.
Exit 0 = ukończone porównanie, 2 = opublikowane PARTIAL/UNKNOWN, 1 = błąd.
Testy: `py -3 -B scripts/test_compare_events.py` (stdlib, syntetyczne dane, bez TIA).
Format raportu, semantyka coverage, limity i ograniczenia: [dokumentacja](../docs/LAB_EVENT_COMPARISON.md).
