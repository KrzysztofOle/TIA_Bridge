# Scripts

## HMI JavaScript native artifact packager

[`hmi_js_sync.py`](hmi_js_sync.py) uses Python 3.12 standard library only. It packages an explicit inventory JSON and supplied native files without Siemens access, parsing, normalization, overwrite, or import. `pack <inventory.json> <new-or-empty-destination>` writes opaque native artifacts and an always-incomplete manifest; `compare <previous-manifest> <current-manifest>` emits observation-only statuses. Review sensitive script content before staging it in Git. Format, evidence limits, and future live scope are documented in [HMI_JS_SYNC_MVP](../docs/HMI_JS_SYNC_MVP.md).

## HMI JavaScript native artifact packager

[`hmi_js_sync.py`](hmi_js_sync.py) uses Python 3.12 standard library only. It packages an explicit inventory JSON and supplied native files without Siemens access, parsing, normalization, overwrite, or import. `pack <inventory.json> <new-or-empty-destination>` writes opaque native artifacts and an always-incomplete manifest; `compare <previous-manifest> <current-manifest>` emits observation-only statuses. Review sensitive script content before staging it in Git. Format, evidence limits, and future live scope are documented in [HMI_JS_SYNC_MVP](../docs/HMI_JS_SYNC_MVP.md).

Place repository maintenance and development scripts here. Scripts must not connect to or modify a TIA Portal project unless the command and its safeguards are explicit, documented, and separately reviewed.

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
