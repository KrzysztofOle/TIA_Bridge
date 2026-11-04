# LabExporter.exe — możliwości i uruchamianie

Dokument opisuje laboratoryjny program `X:\TESTY_TIA_Bridge\bin\LabExporter.exe`: jego główne polecenia, argumenty oraz przykłady odpowiadające konfiguracjom z `C:\RMA\t05_plc\.vscode\launch.json`.

## Czym jest LabExporter

`LabExporter.exe` to oddzielne narzędzie laboratoryjne .NET Framework 4.8, x64, korzystające z Siemens TIA Portal V19 PublicAPI. Źródła programu znajdują się w `X:\TESTY_TIA_Bridge\src`. Nie jest to produkcyjny TIA_Bridge Host ani Worker i jego możliwości nie są automatycznie capability produkcyjnego Bridge.

Skrypt repozytorium [build_lab_events.ps1](../scripts/build_lab_events.ps1) buduje narzędzie laboratoryjne ze źródeł w `X:\TESTY_TIA_Bridge\src` przy użyciu Windows PowerShell 5.1 x64, .NET Framework `csc.exe` i zainstalowanego Siemens PublicAPI V19. Aktualny zestaw zawiera `LabExporter.cs`, `LabEventExport.cs`, `LabEventVendor.cs`, `LabEventView.cs`, `LabEventConsole.cs`, `LabEventSync.cs`, `LabForceSync.cs`, `LabNewModules.cs`, `LabUdtSync.cs`, `LabPlcBlocksSync.cs` i `LabPlcFcImport.cs`. Wymagane są również `src\ValidateNewModule.ps1` i `bin\Esprima.dll`; kompilator odwołuje się do systemowych bibliotek i PublicAPI, nie kopiuje Siemens DLL. Skrypt zachowuje poprzedni EXE w `evidence\LAB-03K-build-<UUID>`, publikuje nowy `bin\LabExporter.exe` po udanej kompilacji i zapisuje `build-manifest.json` z rozmiarami i SHA-256 źródeł, EXE oraz zależności pomocniczych. To wyłącznie build; nie wywołuje Siemens Openness.

W samym laboratorium aktualnym pełnym builderem jest `X:\TESTY_TIA_Bridge\scripts\build_udt.ps1`. Kompiluje bieżący zestaw jedenastu plików C# w trybie x64, kopiuje konfigurację obok kandydata, zachowuje poprzedni EXE i manifest źródeł w `evidence\UDT-build-<UUID>`. Domyślnie zastępuje `bin\LabExporter.exe`; `-CandidateOnly` buduje kandydata bez podmiany. Tożsamość builda ustala manifest/hash, nie deklarowana wersja narzędzia. Oba skrypty wymagają lokalnie zainstalowanych zależności wskazanych w ich treści i nie przenoszą źródeł ani vendor DLL do produkcyjnego TIA_Bridge.

## Główne możliwości

Nowy odrębny eksport czytelnych źródeł PLC V19: [plc-source-export](LAB_PLC_SOURCES.md). Natywne `.scl/.db/.udt` wraz z XML, manifestem i ochroną lokalnych edycji; import źródeł pozostaje zablokowany. Zakres Engineering i ograniczenia opisuje raport laboratorium.

| Polecenie | Zastosowanie | Istotny skutek |
|---|---|---|
| `force-export` | Eksport skryptów HMI z otwartego projektu do folderu | Publikuje przygotowaną paczkę; nadpisuje zawartość wskazanego katalogu po walidacji i stagingu. |
| `force-import` | Import paczki skryptów do HMI | Operacja Engineering; może importować, zapisać projekt i zweryfikować wynik readbackiem. Wymaga spełnienia kontroli projektu i paczki. |
| `force-sync` | Porównanie paczki z HMI; NO_OP albo synchronizacja | Przy różnicach wykonuje pełną ścieżkę importu, kompilacji, Save i readbacku. Zgodny stan może zakończyć się NO_OP. |
| `export-events` | Odczyt publicznych handlerów zdarzeń ekranów Unified | Eksport read-only danych i raportów; wynik może być `PARTIAL`, gdy API nie udostępnia całego wnętrza obiektów. |
| `udt-sync` | Eksport, porównanie lub kontrolowany workflow PLC UDT | Kierunek wybiera `--direction`; zapis repozytorium albo próba repo→TIA zależy od kierunku i bramek. Aktualny import repo→TIA ze zmianami jest blokowany przez niezakwalifikowaną ochronę metadanych V19. |
| `plc-blocks-sync` | Natywny eksport i porównanie bloków PLC OB/FC/FB/GlobalDB/InstanceDB | Eksport TIA→repo i compare mają readback Engineering; `repo-to-tia` kończy się `NO_OP` przy zgodności, a ogólny import pozostaje zablokowany. Nowy offline-tested tryb kwalifikacji jawnej grupy FC SCL opisuje [kontrakt PLC](LAB_PLC_BLOCKS_SYNC.md); CREATE/UPDATE/restore wymagają osobnych dowodów i zgód na plan. Brak adaptera Download/Runtime. |

Szczegółowe kontrakty: [LAB-03K eksport zdarzeń](LAB_03K_HMI_EVENT_EXPORT.md), [LAB-03P force-sync](LAB_03P_FORCE_SYNC.md), [LAB-03Q tworzenie modułów](LAB_03Q_CREATE_MODULES.md), [workflow UDT](LAB_UDT_SYNC.md) oraz dokumentacja laboratoryjna `PLC_DATA_TYPES.md` w `X:\TESTY_TIA_Bridge`.

Kontrakt polecenia bloków PLC, statusy, zakres Engineering i ograniczenia importu opisuje [LAB PLC blocks sync](LAB_PLC_BLOCKS_SYNC.md), a szczegółowy raport i dowody znajdują się w `X:\TESTY_TIA_Bridge\PLC_BLOCKS_REPORT.md`.

## Parametry poleceń

Ścieżki z przykładów są wartościami z konfiguracji VS Code użytkownika. Przed użyciem sprawdź, czy wskazują właściwy projekt i katalog. Projekt TIA musi być otwarty zgodnie z wymaganiami danego polecenia; ewentualny monit Siemens Openness zatwierdza operator.

### HMI Scripts

| Argument | Polecenia | Znaczenie |
|---|---|---|
| `--project PROJECT_DIRECTORY` | `force-export`, `force-import`, `force-sync` | Katalog projektu TIA, nie plik `.ap19`. Narzędzie dopasowuje go do otwartego projektu. |
| `--output OUTPUT_DIRECTORY` | `force-export` | Bezwzględna ścieżka katalogu docelowego paczki eksportu. |
| `--input SCRIPTS_DIRECTORY` | `force-import`, `force-sync` | Katalog wejściowej paczki skryptów. |
| `--overwrite-tia` | `force-import` | Jawnie dopuszcza zastąpienie historycznie rozbieżnego baseline TIA; nadal obowiązują świeże kontrole, backup i readback. Nie dotyczy `force-sync`. |
| `--create-modules` | `force-sync` | Jawnie dopuszcza plan CREATE dla nowych modułów spełniających ograniczony format LAB-03Q; nie umożliwia usuwania modułów. |
| `--validate-only` | `force-sync` | Waliduje wejściową paczkę i buduje lokalny plan bez GetProcesses/Attach; nie dowodzi stanu TIA. |
| `--verbose` | HMI Scripts | Włącza dodatkowe komunikaty przebiegu. |

### Eksport zdarzeń HMI

| Argument | Wymagany | Znaczenie |
|---|---:|---|
| `--project PROJECT_DIRECTORY` | Tak | Katalog projektu, w którym znajduje się jednoznaczny plik AP19. |
| `--output EVENT_EXPORT_ROOT` | Tak | Katalog publikacji wyniku. Domyślny tryb synchronizuje wcześniej zarządzane pliki z kontrolą własności. |
| `--hmi ENCODED_DEVICE_ITEM_PATH` | Tak | Dokładna ścieżka urządzenia HMI; segmenty rozdziela `/`, np. `HMI_JSON_TEST/HMI_RT_1`. |
| `--mode overwrite\|snapshot` | Nie | `overwrite` jest trybem domyślnym; `snapshot` tworzy osobny historyczny eksport. |
| `--history VERIFIED_EVENT_EXPORT` | Nie | Opcjonalny historyczny eksport do weryfikacji retencji; używany wyłącznie w trybie overwrite. Wartość musi wskazywać weryfikowany eksport zgodny z kontraktem LAB-03O. |

### PLC UDT

| Argument | Wymagany | Znaczenie |
|---|---:|---|
| `--direction tia-to-repo\|repo-to-tia\|compare` | Tak | Eksport z TIA, kierunek repo→TIA albo porównanie. |
| `--project EXACT_AP19` | Tak | Pełna ścieżka do konkretnego pliku `.ap19`. |
| `--plc NAME_OR_DEVICE_PATH` | Tak | Nazwa PLC albo dokładna ścieżka urządzenia pozwalająca jednoznacznie wskazać `PlcSoftware`. |
| `--source DIRECTORY` | Tak | Katalog źródłowy/pakietu UDT. |
| `--types NAME[,NAME]\|*` | Tak | Jedna lub lista nazw UDT rozdzielona przecinkami, albo `*` dla wszystkich. |
| `--dry-run` | Nie | Przygotowuje plan bez publikacji/zapisu. Dla `repo-to-tia` nie wykonuje importu, Compile ani Save. |
| `--approve-plan SHA256` | Warunkowo | Hash planu uprzednio przejrzanego przez operatora; musi odpowiadać świeżemu planowi. Nie omija innych bramek. |
| `--overwrite-local` | Warunkowo | Wyłącznie `tia-to-repo`; jawne nadpisanie lokalnych różnic po dry-run i zatwierdzeniu planu. |
| `--accept-structural-impact` | Warunkowo | Jawne potwierdzenie strukturalnego wpływu dla kierunku repo→TIA. Nie omija blokady ochrony metadanych V19. |

W aktualnym stanie repo→TIA ze zmianami jest blokowane przed mutacją przez `UDT_METADATA_PRESERVATION_V19_NOT_QUALIFIED`, również w dry-run. Eksport, compare i zgodny NO_OP zachowują odrębną semantykę. Zob. raport metadanych `X:\TESTY_TIA_Bridge\UDT_METADATA_REPORT.md` oraz [LAB_UDT_SYNC](LAB_UDT_SYNC.md).

### Bloki PLC

| Argument | Wymagany | Znaczenie |
|---|---:|---|
| `--direction tia-to-repo\|compare\|repo-to-tia` | Tak | Kierunek odczytu/publikacji, porównania albo planu repo→TIA. |
| `--project EXACT_AP19` | Tak | Istniejący, dokładny plik projektu `.ap19`; projekt musi być już otwarty w dokładnie jednym procesie TIA. |
| `--plc NAME_OR_DEVICE_PATH` | Tak | Nazwa PLC software albo dokładny locator urządzenia z raportu. |
| `--source DIRECTORY` | Tak | Katalog pakietu natywnych XML i manifestu `.plc-blocks-sync.json`. |
| `--blocks NAME[,NAME]\|*` | Tak | Jawna lista nazw bloków lub `*` dla wszystkich; nazwa tożsamości obejmuje rodzaj w dokładnym PLC. |
| `--dry-run` | Nie | Tworzy plan i dowody bez publikowania plików ani mutacji TIA. |
| `--approve-plan SHA256` | Przy imporcie | Hash bindingu konkretnego planu; drift blokuje. Nie rozszerza zakresu kwalifikacji. |
| `--qualification-only` | Przy imporcie FC | Jawny eksperyment grupy 1–64 root FC SCL w dokładnym projekcie testowym; osobny plan i backup, bez capability produkcyjnej. |
| `--prepare-backup` | Nie | Tylko repo-to-tia z dry-run; przygotowuje backup zmienionego planu na X: przed zgodą. |
| `--backup-receipt PATH` | Przy imporcie FC | Zweryfikowany receipt backupu związany z tym planem/procesem; przed importem także świeży backup. |

`compare` i dry-run pozostawiają XML źródłowe bez zmian; zapisują raporty i plan w nowym katalogu evidence laboratorium. `tia-to-repo` wykonuje ponowny eksport/readback, po czym publikuje wyłącznie zarządzane artefakty objęte wyborem, z ochroną lokalnych zmian. Oczekuj statusu Engineering `PASS_READ_ONLY_CAPTURE` dla odczytu zakończonego z niezależnym readbackiem; statusy `NOT_RUN`, `BLOCKED` i `REVIEW_REQUIRED` nie są PASS. Kod wyjścia 0 obejmuje ukończone odczyty również przy statusie `DIFFERENCES`; kod 2 oznacza blokadę/przegląd. Sprawdzaj `report.json` i nie wnioskuj o pełnej kwalifikacji rodzajów nieobecnych w testowanym projekcie.

`NO_OP` oznacza zgodność po niezależnym odczycie, bez Archive/Import/Compile/Save. Eksperymentalny profil `v19-fc-scalar-static-calls/2` przyjmuje jawną grupę root FC SCL o skalarnych interfejsach i statycznych, domkniętych zależnościach FC, w dokładnym projekcie testowym, z manifestem wejścia, zgodnym planem i backupem na X:. Dla konkretnego Q1 FC910–912 potwierdzono Engineering PASS: pierwotne CREATE/Compile 0/0, ręczny Save operatora, następnie exact read-only rekoncyliacja, nowy backup i Q1b IDENTICAL/NO_OP. Nie powtórzono importu/Compile/Save; nie jest to ogólny PASS dowolnych grup. Raport końcowy: X:\TESTY_TIA_Bridge\FC_MULTI_ENGINEERING_Q1_REPORT.md. Historyczny FC900 ma osobne CREATE/UPDATE/restore PASS i nie kwalifikuje nowego profilu. Zmiana interfejsu/metadanych, OB/FB/DB i nieobsługiwane zależności są BLOCKED. Niepewność pozostawia trwałą kwarantannę bez retry/rollbacku. [Kontrakt PLC](LAB_PLC_BLOCKS_SYNC.md) oraz FC_MULTI_REPORT.md przy laboratorium zawierają zakres i dowody. Bez Download/Runtime.

## Przykładowe uruchomienia z `C:\RMA\t05_plc\.vscode\launch.json`

Poniższe komendy odpowiadają konfiguracjom launch.json z 2026-11-04. Polecenia korzystające z otwartego TIA wykonują rzeczywiste operacje Engineering. Samo skopiowanie przykładu lub przygotowanie tego dokumentu ich nie uruchamia.

### FORCE EXPORT — TIA → Folder

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-export `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --output "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts"
```

### FORCE IMPORT — Folder → TIA

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-import `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts" `
  --overwrite-tia
```

### FORCE SYNC — Folder ↔ TIA

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-sync `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts"
```

### EXPORT EVENTS — TIA → Folder

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" export-events `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --output "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Events" `
  --hmi "HMI_JSON_TEST/HMI_RT_1"
```

### UDT EXPORT — TIA → repo

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" udt-sync `
  --direction tia-to-repo `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19" `
  --plc T05_PLC_SIM `
  --source "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\PLC data types" `
  --types '*'
```

### UDT COMPARE — TIA ↔ repo

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" udt-sync `
  --direction compare `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19" `
  --plc T05_PLC_SIM `
  --source "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\PLC data types" `
  --types '*'
```

### UDT DRY RUN — repo → TIA

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" udt-sync `
  --direction repo-to-tia `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19" `
  --plc T05_PLC_SIM `
  --source "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\PLC data types" `
  --types '*' `
  --dry-run
```

### UDT IMPORT — repo → TIA (konfiguracja z promptem hash planu)

Launch.json pobiera `udtReviewedPlanHash` z okna promptu i przekazuje go jako `--approve-plan`. Hash ma pochodzić z przejrzanego `plan.json`/`report.json` dla świeżego dry-run. Konfiguracja zawiera też `--accept-structural-impact`; żadna z tych flag nie omija bieżącej blokady ochrony metadanych V19.

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" udt-sync `
  --direction repo-to-tia `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19" `
  --plc T05_PLC_SIM `
  --source "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\PLC data types" `
  --types '*' `
  --approve-plan '<SHA256_Z_PRZEJRZANEGO_PLANU>' `
  --accept-structural-impact
```

### PLC BLOCKS COMPARE — TIA ↔ repo

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" plc-blocks-sync `
  --direction compare `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19" `
  --plc T05_PLC_SIM `
  --source "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\Program blocks" `
  --blocks '*'
```

### PLC BLOCKS EXPORT — TIA → repo

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" plc-blocks-sync `
  --direction tia-to-repo `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19" `
  --plc T05_PLC_SIM `
  --source "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\Program blocks" `
  --blocks '*' `
  --dry-run
```

Usuń `--dry-run` po przejrzeniu planu, jeśli zamierzasz opublikować eksport do wskazanego folderu. Początkowa kwalifikacja Engineering dla `T05_RecipeSim_V19` objęła 11 bloków OB/FC/GlobalDB, odczyt i eksport z niezależnym readbackiem oraz powtórne NO_OP. FB, InstanceDB i podfoldery nie wystąpiły w projekcie; ich Engineering pozostaje `NOT_RUN`. W tej historycznej sesji import zmian był NOT_RUN/BLOCKED. Późniejsza kwalifikacja jednego FC SCL w dokładnym T05_RecipeSim_V19_Testowy uzyskała CREATE/UPDATE/restore PASS; nie rozszerza starych konfiguracji launch.json ani kwalifikacji innych bloków. Download i Runtime pozostają NOT_RUN.

### PLC BLOCKS REPO → TIA — tylko dry-run diagnostyczny

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" plc-blocks-sync `
  --direction repo-to-tia `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19" `
  --plc T05_PLC_SIM `
  --source "C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\Program blocks" `
  --blocks '*' `
  --dry-run
```

Ten przykład nie importuje bloków. Zgodny pakiet może zakończyć się `NO_OP`; zmieniony zakres pozostaje zablokowany poza jawną grupą obsługiwanych root FC SCL w trybie qualification-only. Hash planu sam nie otwiera importu. Szczegóły zakresu, statusów i ograniczeń: [LAB PLC blocks sync](LAB_PLC_BLOCKS_SYNC.md) oraz raport `X:\TESTY_TIA_Bridge\PLC_BLOCKS_REPORT.md`.

## Ważne granice

- `force-import`, `force-sync` i `udt-sync` mogą wejść w ścieżkę modyfikującą projekt TIA; każda wymaga właściwego uprawnienia, dokładnej tożsamości projektu i wymaganych kontroli/backupów. Przykład launch.json sam w sobie nie stanowi dowodu, że projekt lub zakres są zatwierdzone.
- Po nieznanym wyniku mutacji nie ponawiaj polecenia automatycznie. Obowiązuje uzgodnienie stanu i `RECONCILIATION_REQUIRED` zgodnie z kontraktami danego workflow.
- `export-events` odczytuje publicznie dostępne API. `PARTIAL` i niepubliczne wnętrza kontrolek pozostają jawne; eksport nie dowodzi kompletnego modelu ekranów ani Runtime.
- PASS testów offline, build lub konfiguracji VS Code nie jest dowodem wykonania w Engineering ani Runtime.
- Wyniki, hashe i kwalifikacja są zależne od konkretnej wersji programu i danego przebiegu; przed operacją sprawdź aktualny raport właściwego workflow.
