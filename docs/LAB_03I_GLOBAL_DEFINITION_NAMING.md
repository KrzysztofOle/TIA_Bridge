# LAB-03I — plik definicji globalnych WinCC Unified

Data: 2026-10-28. Zakres: niezależne narzędzie laboratoryjne w `X:\TESTY_TIA_Bridge`, baza repo `067223c`. Bez commita/push. Produkcyjny Core/Host/Worker, kontrakty HTTP/IPC, projekt i repozytorium `C:\RMA\t05_plc` pozostają bez zmian.

## Konwencja i kontrakt paczki

FORCE EXPORT zapisuje globalny obszar modułu do `_Global_definition_area.js`. Jest to specjalna, zarezerwowana nazwa SOURCE, nigdy publiczna funkcja. Katalogi modułów, nazwy funkcji i układ `.native` pozostają zachowane:

```text
Scripts/
  manifest.json
  ModuleA/
    _Global_definition_area.js
    functionA.js
    functionB.js
    .native/
      ModuleA.hmi.js
      ModuleA.hmi.yml
      manifest.json
```

Nowe identyfikatory formatu to `lab-03i-scripts-manifest/1.0` i `lab-03i-module-manifest/1.0`. W `parts[0]` zmienia się wyłącznie lokalne `path` na `_Global_definition_area.js`; logiczne `name="Global definition area"`, `kind="GLOBAL_PREFIX"`, kolejność, offsety, rozmiary i SHA-256 opisują te same oryginalne bajty. Kolejne wpisy pozostają `EXPORTED_FUNCTION` i mają dotychczasowe nazwy. Manifesty obu poziomów muszą podawać identyczne wpisy segmentów w identycznej kolejności. Walidacja sprawdza także ciągłość historycznych offsetów, unikalność ścieżek, format SHA-256 i liczbę segmentów.

Natywny marker `***End of Global definition area***`, cały blok `auto-generated`, `.hmi.js` i `.hmi.yml` nie są przemianowywane ani normalizowane. Maskowanie komentarza służy wyłącznie analizie AST; zapisywane segmenty są wycinane z oryginalnych bajtów. Plik globalny zawiera również natywny separator i odstęp przed pierwszą funkcją. Pusty kod definicji globalnych nadal wymaga tego pliku z zachowanym separatorem.

## FORCE EXPORT i FORCE IMPORT

Eksport używa istniejącego `SplitModules.ps1`, sprawdza rekonstrukcję bajtową i hashe, następnie kopiuje kompletną paczkę do staging na woluminie docelowym, weryfikuje kopię i publikuje ją istniejącym mechanizmem rename/backup/rollback. Udana publikacja usuwa z aktualnego katalogu poprzednią nazwę wraz z pozostałymi nieaktualnymi plikami. Odrzucony eksport nie publikuje niekompletnej paczki.

Importer odczytuje nowy plik jako pierwszy segment `GLOBAL_PREFIX` i składa wszystkie aktualne pliki SOURCE w kolejności manifestu. Bieżące bajty edytowalnych plików mogą różnić się od historycznych hashy eksportu; hashe nie wymagają ręcznej aktualizacji po edycji funkcji. Oba manifesty muszą nadal zgodnie opisywać eksportowany baseline. Natywne artefakty pozostają kontrolowane według ich SHA-256; metadane YAML są kopiowane bez zmian. Importer wymaga kompletnego zestawu plików i modułów, czystego projektu o potwierdzonej tożsamości, Save z `IsModified=False` i istniejącego readbacku wszystkich natywnych par. LAB-03I nie zmienia tej ścieżki Siemens ani nie dodaje Compile.

Polityka zgodności jest celowo ścisła:

- Nowy eksport zawsze generuje nową nazwę i oba manifesty LAB-03I.
- Import odrzuca wcześniejsze manifesty LAB-03B/LAB-03G oraz stary plik `Global definition area.js`; komunikat wskazuje ponowny FORCE EXPORT. Nie wykonuje migracji ani zmiany źródeł wejściowych.
- Równoczesna obecność obu nazw jest błędem `GLOBAL_DEFINITION_NAMES_CONFLICT` w nowym formacie. Starszy manifest również blokuje taką paczkę, na wcześniejszej kontroli wersji.
- Nazwa funkcji `_Global_definition_area`, także różniąca się tylko wielkością liter, jest odrzucana przez splitter jako kolizja. Importer również odrzuca przypisanie specjalnego pliku do funkcji lub przestawienie globalnego segmentu.
- Częściowo zaktualizowany lub niespójny manifest nie jest obsługiwanym sposobem migracji. Wykonaj świeży FORCE EXPORT po zachowaniu swoich edycji poza nadpisywanym katalogiem.

Offline helper `src\RebuildImportPackages.ps1` używa teraz bezpośrednio tego samego walidatora i assemblera co FORCE IMPORT w `LabExporter.exe`, zamiast historycznej implementacji LAB-03B z `native\` i osobnym parserem. Wymaga Windows PowerShell 5.1 x64; nie wykonuje Attach. Historyczne opcje testowe `OmitModule`/`AddTestComment` są jawnie odrzucane. Parametr `EsprimaAssembly` pozostaje akceptowany dla zgodności wywołania, lecz nie jest używany przez assembler.

## Analiza zależności

| Miejsce | Znaczenie i decyzja |
|---|---|
| `src\SplitModules.ps1` | Specjalna ścieżka SOURCE, rezerwacja nazwy, manifesty obu poziomów. Algorytm AST, maska markera, granice bajtowe i natywne artefakty zachowane. |
| `src\LabExporter.cs`, `PrepareForceImportInput` | Nowy format, rozpoznanie globalnego segmentu, odmowa legacy/konfliktu, wspólna walidacja układu i zgodności manifestów. Aktualne SOURCE pozostaje autorytatywne. |
| `src\LabExporter.cs`, `ValidatePackage` | Nowy format i ta sama kontrola kolejności/specjalnego pliku przed publikacją oraz po kopiowaniu do staging. Istniejąca kontrola hashy zachowana. |
| `src\RebuildImportPackages.ps1` | Zastąpienie nieużywanego przez bieżące FORCE historycznego assemblera wywołaniem aktualnej implementacji offline. |
| Publisher LAB-03H | Nie zależy od nazwy globalnej; kopiuje/weryfikuje pełne drzewa. Implementacja bez zmian, istniejąca regresja 10/10 PASS. |
| Natywny parser i Siemens readback | Nazwa markera oraz natywne nazwy plików pozostają bez zmian. Readback porównuje `.hmi.js`/`.hmi.yml`, nie lokalną nazwę segmentu. |
| VS Code launch i konfiguracja EXE | Przekazują katalog całej paczki; bez zależności od nazwy segmentu i bez zmian. |
| Istniejące `Scripts`, `exports`, `evidence` | Historyczne paczki i dowody mogą nadal zawierać starą nazwę. Bez masowej zamiany, ręcznej migracji lub nadpisywania historii. |
| Produkcyjne repo / HMI-JS parser | FORCE korzysta z lokalnego splittera i `Esprima.dll`, nie z CLI HMI-JS. Nie zmieniono produkcyjnego modelu workspace ani parserów innych pakietów. |

## Wyniki i dowody

```text
TASK: LAB-03I GLOBAL DEFINITION FILE NAMING

IMPLEMENTATION: PASS
FORCE EXPORT OFFLINE: PASS
FORCE IMPORT OFFLINE: PASS
BYTE IDENTITY: PASS
MANIFESTS: PASS
SHA-256: PASS
LEGACY FORMAT HANDLING: PASS
STAGING/ROLLBACK REGRESSION: PASS

LIVE FORCE EXPORT: FAIL
LIVE FORCE IMPORT: NOT_RUN
READBACK: NOT_RUN
```

Finalny build: .NET Framework 4.8 x64, kompilacja `csc.exe` z lokalnym PublicAPI V19 i istniejącym configiem. SHA-256 finalnego EXE oraz źródeł podaje `X:\TESTY_TIA_Bridge\evidence\LAB-03I-final-report.json`. Nie kopiowano vendor DLL.

Testy [test_lab03i.ps1](../scripts/test_lab03i.ps1): **18/18 PASS**, syntetyczne moduły, bez połączenia z Siemens. Dowód: `X:\TESTY_TIA_Bridge\evidence\LAB-03I-offline-35f1c690eb8f491faed67bef6a9f8e78\results.json`. Wcześniejsze nieudane uruchomienia harnessu zachowano; dotyczyły kodowania/wiązania obiektów PowerShell w nowym teście i zostały poprawione przed końcową walidacją.

| Wymagania testowe | Pokrycie |
|---|---|
| Nowa nazwa, brak starej, manifesty | Oba poziomy, kolejność globalny/alpha/beta, walidacja paczki, odmowa nieaktualnego manifestu. |
| SHA-256 i bajty | Bezpośrednie porównanie tablic bajtów oraz SHA-256 po rekonstrukcji JS i YAML. |
| Pusty globalny, UTF-8, EOL | Brak deklaracji przed markerem; polskie i japońskie znaki, znak spoza BMP; BOM/bez BOM; CRLF/LF. |
| Dane niekompletne i konflikt nazw | Brak funkcji/globalnego pliku, oba globalne pliki, stara nazwa, stary schema, błędne role segmentów, kolizja nazwy funkcji. |
| Edycja jednej funkcji | Zmiana syntetycznego `alpha`; dokładnie oczekiwany cały native JS, niezmieniony `beta`, pozostałe moduły i YAML. |
| Integralność artefaktów | Odmowa zmienionego native JS/YAML i błędnego SHA-256 SOURCE przy walidacji eksportu. |
| Helper i publikacja | Ten sam assembler offline, usunięcie starej nazwy przy publikacji, backup poprzedniego katalogu, weryfikacja kopii X: → C:. |

Istniejące testy `X:\TESTY_TIA_Bridge\evidence\LAB-03H-publisher-tests.ps1`: **10/10 PASS** na finalnym EXE. Dowód: `X:\TESTY_TIA_Bridge\evidence\LAB-03H-offline-tests-0bac14ec92d343cdab5197d18f2c5392.json`. Obejmują rzeczywiste kopiowanie między woluminami, usuwanie stale files, błędy SHA-256, przerwane kopiowanie, rollback po odłożeniu poprzedniego katalogu, retry blokady, trwałą odmowę dostępu, zablokowany katalog docelowy i reparse point.

Rzeczywisty test A: projekt `X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy\T05_RecipeSim_V19_Testowy.ap19`, PID 39136, HMI `HMI_JSON_TEST / HMI_RT_1`. Operator potwierdził jednorazowe Yes w natywnym monicie; faktyczny Attach powrócił. Projekt przeszedł kontrolę tożsamości i czystego stanu. Eksport natywny czterech modułów zakończył się OK, następnie istniejąca kontrola zwróciła `NATIVE_FILE_COUNT_OR_LAYOUT_INVALID:JSON_Test_screen`. Dispose powrócił, proces eksportera zakończył się. Wynik: **FAIL przed splitterem i publikacją**, bez nowego katalogu docelowego. Sam kod błędu nie rozstrzyga liczby zwróconych plików ani obecności podkatalogu. Nie osłabiano kontroli i nie naprawiano modułu HMI w ramach zmiany nazewnictwa.

Konsola i exit code: `X:\TESTY_TIA_Bridge\evidence\LAB-03I-live-d1c708d7bd324a5d966d217b887974f1\force-export-console.txt` i `force-export-exit.txt`. Live dotyczył wcześniejszego builda z tego zadania, wskazanego hashem w raporcie końcowym. Po nim usunięto z nowej walidacji niezamierzone ograniczenie długości nazwy zwykłych plików funkcji do limitu katalogów modułów; finalny build ponownie przeszedł oba zestawy offline, bez kolejnego live.

Test B i readback: **NOT_RUN**, ponieważ test A nie dostarczył kompletnej świeżej paczki wszystkich modułów. Nie wykonywano Import, Save ani zmiany funkcji w rzeczywistym projekcie. Nie zastąpiono bieżącego P historyczną paczką. Kompilacja HMI, Runtime, download i sterowanie PLC również NOT_RUN.

## Zmienione pliki i ograniczenia

Odrębne polecenie [LAB-03K `export-events`](LAB_03K_HMI_EVENT_EXPORT.md) zapisuje obserwacje zdarzeń poza `Scripts` i nie używa ani nie zmienia formatu modułów LAB-03I. Jego testy offline nie rozszerzają poniższych historycznych dowodów Engineering.

W laboratorium: `src\LabExporter.cs`, `src\SplitModules.ps1`, `src\RebuildImportPackages.ps1`, `bin\LabExporter.exe`, `README.md`. W repo: `scripts\test_lab03i.ps1`, `scripts\README.md`, `README.md`, ten raport i `Zmiany.md`. Nowe syntetyczne fixtures, logi, raporty i diffy znajdują się poza Git pod `X:\TESTY_TIA_Bridge`; wcześniejsze źródła/binary zachowano w `evidence\LAB-03I-before-a344a799e577417e8765d01e2f72cd26`.

`LabExporter.exe` jest zbudowany i gotowy do testów nowego formatu offline. **Nie potwierdzono gotowości do użycia w T05_PLC**: live eksport całej paczki zakończył się FAIL, a import/readback nie zostały wykonane. Błąd natywnego eksportu wymaga osobnej diagnozy przed deklaracją pełnego live PASS. Istniejące laboratoryjne `Scripts` zachowano bez ręcznej migracji; nowy importer wymaga ponownego udanego FORCE EXPORT. Repozytorium/projekt `C:\RMA\t05_plc` pozostawiono bez zmian.

Jest to zmiana lokalnego formatu laboratorium, bez produkcyjnego capability, bez deklaracji `SYNCED`, Engineering qualification lub Runtime PASS. Regresja publishera nie jest rollbackiem mutacji Siemens. Rzeczywista autoryzacja nie została wykonana automatycznie: zastosowano umiejętność `computer-use` do wykrycia okna, a decyzję podjął operator.
