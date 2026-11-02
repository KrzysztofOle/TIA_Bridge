# LAB-03Q — nowe moduły Unified w force-sync

2026-11-01. Zatwierdzony zakres: rozbudowa samodzielnego narzędzia laboratoryjnego `X:\TESTY_TIA_Bridge`, build i testy offline. **IMPLEMENTED / OFFLINE_TESTED; Siemens Engineering tworzenia modułów i Runtime: NOT_RUN.** Nie jest to capability produkcyjnego Host/Worker ani potwierdzenie `SYNCED` z [04](04_SYNCHRONIZATION.md). Rozszerza [LAB-03P](LAB_03P_FORCE_SYNC.md).

## Zachowanie i polecenia

Dotychczasowa walidacja odrzucała każdy katalog niewymieniony w głównym manifeście. Opcja `--create-modules` pozwala potraktować dodatkowy katalog zawierający wyłącznie źródła JS jako kandydat `CREATE`. Istniejące wpisy manifestu są `UPDATE`; uszkodzonego lub brakującego baseline nie przeklasyfikowuje się na nowy moduł. Manifesty wejścia i `.native` pozostają bez ręcznych zmian.

Sprawdzenie źródeł, bez GetProcesses, Attach lub innej operacji Siemens:

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-sync `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\T05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts" `
  --create-modules --validate-only
```

`--validate-only` sprawdza lokalne ścieżki i obecność AP19, tworzy kopię W i przygotowane wejście pod laboratoryjnym `evidence`. Wyświetla plan źródeł oraz `PASS_INPUT_READY`, z `tia_preconditions=NOT_CHECKED` i `tia_operations=NOT_RUN`. Nie potwierdza nieobecności nowego modułu w otwartym projekcie, stanu czystości ani działania importera V19. Nie zmienia W lub projektu.

Rzeczywisty cykl Engineering, do osobnego wykonania w zatwierdzonym zakresie:

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-sync `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\T05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts" `
  --create-modules
```

Obie nowe flagi są opcjonalne, występują najwyżej raz i dotyczą wyłącznie parametrycznego `force-sync`. `--verbose` pozostaje dostępne. Bez `--create-modules` dodatkowa zawartość nadal jest odrzucana, teraz z nazwami wpisów i wskazówką. `force-import`, `--overwrite-tia`, eksport i składnia pozycyjna nie uzyskują praw do tworzenia modułów.

## Kontrakt wejścia CREATE

Wejściem pozostaje pełny pakiet istniejących modułów w układzie LAB-03I oraz dodatkowe katalogi:

```text
Scripts/
  manifest.json
  ExistingModule/
    .native/
    _Global_definition_area.js
    existingFunction.js
  NewModule/
    _Global_definition_area.js
    alpha.js
```

- Nazwa nowego modułu: ASCII `[A-Za-z_][A-Za-z0-9_]{0,127}`, bez zastrzeżonych nazw Windows. Jest to ograniczony profil generowanego deskryptora YAML, niezależny od szerszych nazw obsługiwanych przy aktualizacji autentycznych eksportów.
- Nowy katalog zawiera jeden `_Global_definition_area.js` i od 1 do 128 plików funkcji `.js`. Nie może zawierać `.native`, manifestu, innych plików lub podkatalogów. Reparse points są odrzucane przed kopiowaniem. Łączny zestaw UPDATE + CREATE ma maksymalnie 256 modułów.
- Global definition zawiera dokładnie jeden blok z markerem Siemens `***End of Global definition area***`; po bloku dozwolone jest wyłącznie whitespace. Nie dopisuje się ani nie normalizuje markerów. Global definition może zawierać importy, zmienne i funkcje pomocnicze, bez publicznych eksportów.
- Każdy plik funkcji ma dokładnie jeden nazwany `export function` lub `export async function`; nazwa funkcji musi odpowiadać nazwie pliku. Parametry są prostymi identyfikatorami, z zachowaniem kolejności. Generatory, parametry rest/default/destructuring, export default, re-exporty i wiele eksportów w jednym pliku są odrzucane w tym profilu.
- Źródła muszą być poprawnym UTF-8. Limit jednego pliku i całego złożonego modułu wynosi 1 MiB. Kolejność: global definition, następnie funkcje według `StringComparer.Ordinal`. Bajty źródeł, BOM, EOL, async i argumenty pozostają zachowane; parser sprawdza także granice po konkatenacji. Nazwy funkcji nie mogą kolidować z uwzględnieniem wielkości liter.
- Osobny proces PowerShell 7 z istniejącym Esprima 3.0.6 wyłącznie parsuje JS. Nie wykonuje kodu. Ma limit 30 s, kontrolę SHA-256 złożonego pliku i diagnostykę bez treści źródeł. Przerwanie tego własnego parsera nie dotyczy procesu Siemens ani jego wywołań.

Powstaje przygotowany `.hmi.js` oraz ograniczony deskryptor importu `#Version: 2.0` / `ScriptModules` / `ScriptFile`. Powstają wyłącznie w stagingu, nigdy jako rzekomy eksport w `.native` wejścia. Wewnętrzny `input-manifest.json` zapisuje `action=CREATE`, `input_origin=GENERATED_IMPORT_INPUT_NOT_NATIVE_EXPORT`, profil `lab-03q-unified-v19-scriptmodules-v2/1.0`, funkcje i hashe źródeł/wejścia, z oboma hashami baseline równymi null. CREATE nie może deklarować historycznego baseline. Autentyczne `.native` i manifesty wynikowe powstają z niezależnego eksportu TIA po pomyślnym cyklu.

## Plan i wykonanie Engineering

1. Zamrożenie W, pełna dotychczasowa walidacja UPDATE, przygotowanie i parsowanie CREATE. Brak zależności lub wadliwe źródła blokują wykonanie przed discovery Siemens.
2. Dotychczasowy binding AP19/PID/start/HMI i wymóg `IsModified=False`. Dokładny cel pozostaje `HMI_JSON_TEST/HMI_RT_1`.
3. Plan TIA wymaga, aby wszystkie UPDATE istniały, wszystkie CREATE były nieobecne, a w TIA nie było żadnego dodatkowego modułu. Także kolizja case-only daje odmowę. Nie ma usuwania, rename, adopcji obcego modułu ani automatycznego overwrite CREATE, nawet w force-sync.
4. Pod jednym ExclusiveAccess ponowne sprawdzenie nazw i planu; kontrola kompilatora dokładnego HMI. Eksport i porównanie historycznych baseline dotyczą UPDATE. Dotychczasowy zweryfikowany natywny backup AP19 poprzedza każdą mutację. Backup receipt i import intent zachowują plan CREATE/UPDATE.
5. Po backupie ponowne sprawdzenie frozen input, zestawu nazw TIA, czystości i cancellation oraz świeżych natywnych hashy UPDATE. Pojawienie się CREATE w międzyczasie blokuje zapis. `--overwrite-tia` nie wyłącza tych kontroli.
6. Jeden publiczny `HmiScriptModuleComposition.Import` przekazuje komplet przygotowanych modułów. Natychmiast po powrocie sprawdzany jest pełny zestaw nazw, przed Compile i Save. Następnie kompilacja HMI według aktualnej polityki LAB-03P, Save i `IsModified=False`.
7. Świeża enumeracja i niezależny eksport każdej pary JS/YAML muszą odpowiadać wejściu byte-exact. Splitter publikuje pakiet z autentycznym baseline nowych modułów. Ochrona edycji W, zachowanie poprzedniego folderu i recovery publikacji pozostają jak w LAB-03P. Kolejny cykl traktuje utworzony moduł jako UPDATE.

Publiczne sygnatury Import/Export sprawdzono w lokalnym `PublicAPI\V19\Siemens.Engineering.xml`; kolekcja nie udostępnia osobnego publicznego Create dla modułów ani funkcji. [Dokumentacja Siemens opisuje Import](https://docs.tia.siemens.cloud/r/en-us/v21/functions-for-accessing-the-data-of-an-hmi-unified-device/hmisoftware/global-script/import-of-global-script), ale ta strona dotyczy V21 i nie kwalifikuje zachowania V19. Profil deskryptora oparto na odczytanej strukturze istniejącego eksportu V19. **Utworzenie obiektów przez Import, zgodność re-eksportu nowego modułu i zachowanie jego funkcji wymagają rzeczywistego eksperymentu V19.** Dostępność metody, build i testy offline nie stanowią tego dowodu. Zmiana formatu przez importer/kompilator nie jest ignorowana: niezgodny readback kończy cykl odmową, bez promowania baseline.

## Awarie i kwalifikacja

Przed importem odmowa oznacza brak mutacji. Niedozwolony plan zwraca exit 3; wadliwe źródła/parametry i pozostałe błędy zwracają 2. Po rozpoczęciu Import każda awaria, także niepoprawny zestaw nowo utworzonych modułów, Compile/Save/readback lub Dispose, daje `RECONCILIATION_REQUIRED`. Nie ma automatycznego usuwania częściowo utworzonych modułów, rollbacku, reimportu, ponowienia ani ratunkowego Save. Raport zachowuje plan i informację, czy import rozpoczęto. Błąd publikacji folderu nadal rozdziela wcześniejszy wynik Engineering od wyniku publikacji. Nie zmienia się polityka warningów, downloadu, Runtime, symulacji lub PLC.

Implementacja laboratoryjna: `src/LabExporter.cs`, nowy `src/LabNewModules.cs`, nowy `src/ValidateNewModule.ps1`, `bin/LabExporter.exe`; źródła i binaria pozostają poza repo produkcyjnym. Repo zawiera [build](../scripts/build_lab_events.ps1), [testy zachowania](../scripts/lab_new_modules_tests.cs) i [runner](../scripts/test_lab_new_modules.ps1). Zależności: Windows PowerShell 5.1 x64, PowerShell 7, systemowy .NET Framework csc, zainstalowane publiczne assembly V19 i istniejący Esprima; bez nowych pakietów.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_new_modules.ps1
```

Testy wykorzystują wyłącznie syntetyczne dane i delegaty; obejmują plan CREATE/UPDATE, parser i limity, kolizje, drift przed importem, niewłaściwy zestaw po imporcie, źródła bez native baseline, ochronę lokalnych edycji, dwa cykle publikacji i zmieniony readback. Runner sprawdza prawdziwy CLI `--validate-only` na syntetycznym pliku AP19, brak zmiany W, odmowy parametrów i junction. Syntetyczny readback nie dowodzi działania Siemens. Wyniki, regresje, walidację rzeczywistego katalogu bez TIA i ocenę instrukcji zapisano w [Zmiany.md](../Zmiany.md).
