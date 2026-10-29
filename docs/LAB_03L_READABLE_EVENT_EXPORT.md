# LAB-03L — czytelny eksport zdarzeń HMI Unified V19

[LAB-03M](LAB_03M_EVENT_CONSOLE_DIAGNOSTICS.md) dodaje czytelną diagnostykę konsoli bez zmiany tego widoku, JSON, statusów i kodów wyjścia. Poniższe wyniki i hashe zachowują historyczny build LAB-03L.

Data: 2026-10-29. Baza `4fa6887`, `main`, początkowo czyste drzewo. Implementacja laboratoryjna i testy **OFFLINE PASS**; Attach, rzeczywisty eksport Engineering i F5 **NOT_RUN**. Bez Import, Save, Compile, Download, zmian projektów TIA, commita i push.

## Użycie i źródło danych

Istniejące `X:\TESTY_TIA_Bridge\bin\LabExporter.exe export-events` automatycznie tworzy dodatkowy widok w każdym nowym eksporcie. Argumenty i konfiguracja **TIA Bridge: EXPORT EVENTS — TIA → Folder** pozostają bez zmian. Po osobno autoryzowanym eksporcie otwórz katalog wyniku w VS Code, wybierz główny `README.md`, ekran, następnie JS lub GDA. Można też bezpośrednio przeglądać `screens/`.

Źródłem danych pozostaje [LAB-03K](LAB_03K_HMI_EVENT_EXPORT.md): `events.json`, schemat **`lab-03k-events/1.0`**. Widok wykorzystuje te same odczytane wartości, bez dodatkowych Siemens calls, parsera/wykonania/formatowania JS lub łączenia kodu i GDA. Edycja widoku nie zmienia JSON i nie jest wejściem importu. Przy rozbieżności obowiązuje źródłowy JSON.

Przykład **syntetyczny z testów**, nie obserwacja projektu TIA:

```text
<run-id>/
├── events.json
├── events-report.md
├── events-index.json
├── README.md
├── manifest.json
├── scripts/
│   └── <SHA256-locatora>-<właściwość>.js
└── screens/
    └── Area__Cell__T05_Recipes_Main/
        ├── README.md
        ├── Loaded.js
        ├── Loaded.global.js
        ├── Empty.js
        ├── Empty.global.js
        ├── ACTION_0.Tapped.js
        ├── ACTION_0.Tapped.global.js
        └── Broken.Tapped.global.js
```

`Empty` ma poprawnie odczytane puste stringi: status EMPTY i pliki zero bajtów. `Broken` ma READ_FAILED dla kodu, więc nie ma `Broken.Tapped.js`; poprawnie odczytane GDA pozostaje dostępne. Obiekt `NoHandlers` jest w inventory z ABSENT, bez plików JS. Nie powstał fikcyjny `Unloaded`.

## Nazwy i dokładność tekstów

Ekran: `<Zdarzenie>.js` i `<Zdarzenie>.global.js`. Element: `<Obiekt>.<Zdarzenie>.*`; nesting: np. `Panel.Button.Tapped.js`. Grupy/podgrupy są widoczne w nazwie katalogu, np. `Group__Sub__Main`, oraz jako rekordy z pełnymi locatorami w indeksie. Ekrany są płasko pod `screens/`, aby ograniczyć długość ścieżek.

Znaki niedozwolone w Windows i sterujące zastępuje `_`; zarezerwowane nazwy otrzymują prefiks; końcowe kropki/spacje są usuwane. NFC dotyczy wyłącznie nazw plików. Oryginalne nazwy i kod pozostają nietknięte. Kolizje po NFC, zamianie znaków, składaniu hierarchii, różnicy case lub skróceniu otrzymują `~<16 hex SHA256 pełnego locatora>`. Sprawdzane są całe pary plików, także `Loaded.global.js` jako GDA Loaded i kod zdarzenia Loaded.global. Nierozstrzygnięta kolizja sufiksów blokuje publikację. Locator LAB-03K zawiera indeksy odczytu: sufiks jest stabilny dla tego samego pełnego locatora, nie pomiędzy zmianami kolejności w TIA.

Nowe ścieżki mają maks. 240 znaków w stagingu i wyniku. Segment katalogu ma maks. 64 jednostki UTF-16, stem pliku 90; przy długim root limity maleją. Skracanie nie rozcina pary surrogate. Zbyt długi root blokuje publikację z markerem. Zachowano dotychczasową politykę ścieżek LAB-03K i nazwy `scripts/`.

Teksty są kodowane jako ścisłe UTF-8 zgodnie z LAB-03K, bez dodawania BOM/newline/nagłówka. Zachowane Unicode, importy, komentarze, wcięcia i CR/LF. To dokładne kodowanie stringów API, nie dowód dostępu do natywnych bajtów Siemens. READ/EMPTY muszą zgadzać się z tekstem, hashem i bajtami artefaktu `scripts/`; rozbieżność blokuje publikację. Inne statusy, w tym UNKNOWN/READ_FAILED/LIMIT_EXCEEDED/INVALID_UNICODE, mają `path=null`, `sha256=null`, `length=null` i brak pliku zastępczego.

## Kontrakt indeksu i README

`events-index.json`: **`lab-03l-events-index/1.0`**, czytelne wcięcia, bez treści JS.

| Pole | Znaczenie |
|---|---|
| `run_id`, `identity` | identyfikacja publikacji oraz źródłowe metadane projektu/HMI/czasu |
| `source` | events.json, jego schemat, dokładny SHA-256 i długość |
| `status`, `source_status` | COMPLETE/PARTIAL oraz oryginalny COMPLETE_FOR_PUBLIC_SCOPE/PARTIAL |
| `counts` | ekrany, elementy ITEM/UNKNOWN, właściciele handlerów (również ekrany), handlery |
| `coverage_scope`, `limits`, `observations` | oryginalne ograniczenia i kontrolowane błędy, bez tekstów wyjątków |
| `engineering_qualification`, `execution_evidence` | osobne poziomy dowodów, bez awansu do Engineering PASS |
| `screens[]` | ekran, katalog, grupy, inventory obiektów i handlery |
| obiekt | source_ref, locator, nazwa, kind, typ, handlers_status, children_status |
| handler | source_ref, locator, object_locator, event_name, async/async_status, status/presence |
| `code`, `global` | osobne statusy, ścieżki względem root eksportu, SHA-256 i długości |

`source_ref=/objects/<indeks>` lub `/handlers/<indeks>` jest JSON Pointer do rekordu tego events.json, związanego hashem. To nie trwałe ObjectId. Obiekty bez handlerów pozostają w inventory. Dla syntetycznego obiektu bez ekranu istnieje koszyk z `screen=null`, oznaczony UNKNOWN w README; nie zwiększa licznika ekranów. Handler bez znanego właściciela blokuje publikację.

Główny README zawiera projekt/lokalizację, HMI, datę UTC, liczniki, COMPLETE/PARTIAL, listę ekranów i błędy/ograniczenia. README ekranu ma tabelę Obiekt/Zdarzenie/Async/Kod/GDA, inventory z coverage i lokalne błędy. Linki są względne i kodowane procentowo, w tym nawiasy i Unicode. Metadane Markdown są escapowane, bez pełnego kodu JS. Nieobsługiwane wnętrza kontenerów nadal UNKNOWN/PARTIAL; widok nie poszerza coverage V19.

## Integralność i zgodność LAB-03K

`LabEventView.Add` rozszerza mapę artefaktów wewnątrz istniejącego `LabEventArtifacts.Write`, przed obliczeniem manifestu i limitu paczki. Manifest zachowuje schemat `lab-03k-events-manifest/1.0`; tool_version to LAB-03L/1.0.0 z hashem nowego EXE. Nadal wyłącza siebie i `.incomplete`.

Nowy staging obok wyniku, CreateNew, flush, ponowny odczyt/hash wszystkich plików i rename pozostają wspólne. Marker jest usuwany dopiero po weryfikacji i rename. Błąd widoku zachowuje `.incomplete` z fazą readable-view, kodem VIEW_GENERATION_FAILED i typem wyjątku. Błąd IO/weryfikacji także nie przechodzi do usunięcia markera. Istniejące eksporty pozostają nietknięte. Tożsamość projektu/HMI, lokalny NTFS, zakazy Git/Scripts/reparse paths i Siemens calls pozostają w niezmienionym LabEventVendor.cs.

Widok zwiększa rozmiar paczki. Limit 64 MiB i jawne pomijanie całych tekstów przy przekroczeniu budżetu pozostają aktywne; indeks pokazuje wtedy LIMIT_EXCEEDED. Jeśli nie mieszczą się metadata, wynik pozostaje nieopublikowany. COMPLETE dotyczy wyłącznie publicznego zakresu LAB-03K, bez kwalifikacji całego modelu HMI/native round-trip/SYNCED.

events.json, events-report.md i scripts/ zachowują format i semantykę LAB-03K. [compare-events](LAB_EVENT_COMPARISON.md) nadal czyta wyłącznie lab-03k-events/1.0, bez indeksu lub kopii screens/. Kod porównywarki, FORCE EXPORT/IMPORT, adapter vendor i obie konfiguracje launch.json są bajtowo niezmienione.

## Historyczne eksporty — rekomendacja render-events

Nie dodano polecenia i nie zmodyfikowano historycznych katalogów. Renderer może ponownie wykorzystać wartościowy LabEventResult, ale publiczne render-events wymaga osobnego bounded readera: walidacji schematu, duplicate keys, głębokości, liczników, Unicode, status–tekst–hash, locatorów, relacji właścicieli i limitów. Prosta deserializacja nieufnego JSON i zaufanie jego ścieżkom nie wystarczają.

Rekomendowany osobny pakiet: `render-events --input <events.json> --output <parent>` do nowego unikalnego katalogu, z zachowaniem dokładnych bajtów źródła i bindingu SHA-256, bez zmian historii/Siemens. Potrzebne są osobne testy parsera i publikacji. W tym zadaniu wdrożono automatyczny widok nowych eksportów.

## Testy offline

Zależności: Windows PowerShell 5.1 x64 i systemowy .NET Framework csc; build EXE wymaga zainstalowanego PublicAPI V19. LAB-03I używa istniejącego PowerShell 7 i Esprima.dll. Porównywarka: Python 3.12 stdlib. Bez nowych pakietów i wywołań Siemens.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_view.ps1 `
  -BaselineSource 'X:\TESTY_TIA_Bridge\evidence\LAB-03L-before-e0d00e4b42694ab3bca6cf8539c84720\LabEventExport.cs'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03i.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03j.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File X:\TESTY_TIA_Bridge\evidence\LAB-03H-publisher-tests.ps1
py -3 -B scripts/test_compare_events.py
py -3 scripts/audit_quality.py
git diff --check
```

| Zestaw | Wynik | Dowód w X:\TESTY_TIA_Bridge\evidence |
|---|---|---|
| Finalny build net48/x64 | PASS, 0 warnings/errors | LAB-03K-build-33f5deddfdb6416f8027b6e89113d927/build-manifest.json |
| Widok LAB-03L | 16/16 PASS | LAB-03L-offline-4d063a4707984e6a9105ccf3b6df6c62/results.json |
| Identyczność oryginalnych artefaktów | 9/9 plików identycznych SHA-256 | ten sam katalog, lab03k-byte-regression.json |
| Reader/publisher LAB-03K | 42/42 + CLI/launch 2/2 PASS | LAB-03K-offline-86ba4fdc6e2a40a58130fe9c52f96d0b |
| LAB-03I | 18/18 PASS | LAB-03I-offline-346371ef79af4566b94cfea198a82589/results.json |
| LAB-03J | 15/15 PASS | LAB-03J-offline-8105ce5f36d84e93a657bb80d6e24b4e/results.json |
| LAB-03H | 10/10 PASS | LAB-03H-offline-tests-92ab004cb9a04443a69895eaa8a04408.json |
| compare-events | 30/30 PASS | scripts/test_compare_events.py, zapis w raporcie końcowym |

Łącznie **131/131** testów zachowania/regresji PASS; dodatkowo kontrole CLI/launch i porównanie 9 plików. Nowe testy obejmują wszystkie 16 wymaganych tematów: multi-event, kod/GDA, empty/error/absence, grupy/nesting, kolizje/Windows/Unicode/EOL/path limits, PARTIAL, manifest/hash, niezmieniony JSON, zgodność LAB-03K i awarię widoku/marker. Porównano również bazowy i nowy syntetyczny eksport przez compare-events: 7 obiektów UNCHANGED, 3 handlery UNCHANGED, 1 PARTIAL (kontrolowany błąd odczytu), zero ADDED/REMOVED/MODIFIED/UNKNOWN. Ogólny PARTIAL pozostaje PARTIAL.

Wczesne nieudane kompilacje harnessu i przebieg LAB-03L-offline-d32311dde4d64b598ff359cc9ed63895 zachowano. Wykrył niedostateczne kodowanie nawiasów w linkach, błędne oczekiwanie raw underscore w escapowanym Markdown testu i zastosowanie nowego limitu 240 do historycznych nazw scripts/. Poprawiono linki, asercję po HTML decode i ograniczono dodatkową walidację do nowych ścieżek; finalny przebieg PASS.

Finalny EXE SHA-256: `b680050a3a16ef5916e403f00aa26336d0ac6984868c84ab00710abea8177771`. Poprzednie źródła/EXE/README i lista chronionych hashy: evidence/LAB-03L-before-e0d00e4b42694ab3bca6cf8539c84720. Zestawienie: `X:\TESTY_TIA_Bridge\evidence\LAB-03L-final-report.json`. Testy nie dowodzą obecności konkretnych zdarzeń w TIA.

## Pliki i instrukcje

Laboratorium: nowy src/LabEventView.cs, ograniczona zmiana src/LabEventExport.cs, przebudowany bin/LabExporter.exe i README. Repo: build/test LAB-03K z dodatkowym źródłem, nowe scripts/lab_event_view_tests.cs i scripts/test_lab_event_view.ps1, LAB-03L, odnośnik LAB-03K, README repo/scripts oraz Zmiany. Worker/Host/API/IPC, registry, compare-events, launch i pozostałe worktrees bez zmian. Ocena obu instrukcji i ADR/specyfikacji w [Zmiany.md](../Zmiany.md).
