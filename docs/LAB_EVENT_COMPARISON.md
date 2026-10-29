# Offline comparison of LAB-03K event exports

## 2026-10-29 — naprawa INCOMPLETE_LOGICAL_IDENTITY

Zakres: wyłącznie porównywarka offline, jej testy i dokumentacja. Baza repo `4fa6887`, main, z zachowanymi wcześniejszymi zmianami LAB-03L/M. LAB-03K/L/M, EXE, projekty TIA, eksporty historyczne i ich JSON pozostały bez zmian. Bez Siemens Openness, importu/synchronizacji, commita i push.

### Przyczyna i reguły parsowania

Problem odtworzono na wskazanych rzeczywistych eksportach A/B: 422 obiekty UNKNOWN, 96 handlerów UNKNOWN, wszystkie z INCOMPLETE_LOGICAL_IDENTITY. LAB-03K zapisuje `identity.device_path` jako **tablicę segmentów**, np. `["HMI_JSON_TEST", "HMI_RT_1"]`. Porównywarka wymagała stringa zarówno przy budowaniu klucza SOFTWARE, jak i ocenie bindingu. Poprawny korzeń nie otrzymywał klucza, a niepełna tożsamość propagowała się na wszystkie dzieci i handlery. Dotychczasowe fixtures używały wyłącznie stringa `Device/Runtime`, dlatego nie wykryły błędu. To nie był brak metadanych w eksporcie ani problem publicznego coverage.

Naprawa akceptuje natywną tablicę nazw i zachowuje obsługę wcześniejszego stringa ścieżki z kodowaniem procentowym. Binding porównuje uporządkowane segmenty, zachowując granice nazw: `["A/B", "C"]` różni się od `["A", "B/C"]`. Nie normalizuje Unicode, case lub nazw. Nieprawidłowe/niepełne segmenty i błędne escape procentowe pozostają UNKNOWN. Surowe identity wejścia pozostaje w raporcie bez zmian.

Poprawny korzeń SOFTWARE ma locator `/HMI/<zakodowane-segmenty-urządzenia>` zgodny z device_path. Każdy kolejny rekord musi mieć dokładnie jednego obserwowanego rodzica oraz poprawną krawędź:

| Krawędź w locatorze | Kind dziecka | Kind rodzica |
|---|---|---|
| ScreenGroups[n] | GROUP | SOFTWARE |
| Groups[n] | GROUP | GROUP |
| Screens[n] | SCREEN | SOFTWARE lub GROUP |
| ScreenItems[n] | ITEM | SCREEN lub ITEM |
| EventHandlers[n] | handler | dokładny obiekt wskazany przez object_locator |

Bezpośrednie `/HMI/HMI_JSON_TEST/HMI_RT_1/Screens[0]/ScreenItems[0]` jest prawidłowe; grupa nie jest wymagana. Wspierane pozostają grupy/podgrupy i zagnieżdżenia elementów. Ordinal ma format nieujemnej liczby Int32 bez dodatkowych zer wiodących. Nieznane kompozycje, zły kind, nieprawidłowy suffix lub brak/duplikat rodzica nie otrzymują zgadywanego klucza. Dodano kontrolowane `identity_issue`, np. DEVICE_PATH_INVALID, ROOT_DEVICE_BINDING_MISMATCH, OBJECT_LOCATOR_INVALID, PARENT_MISSING_OR_AMBIGUOUS, ANCESTOR_IDENTITY_INCOMPLETE i HANDLER_LOCATOR_INVALID. Główny reason INCOMPLETE_LOGICAL_IDENTITY pozostaje zachowany dla takich rekordów.

Locatory służą wyłącznie do rekonstrukcji relacji **wewnątrz jednego eksportu**. Klucz porównania nadal składa się z urządzenia i kolejnych par kind/nazwa; handler dodaje nazwę zdarzenia. Indeksy i pełne locatory nie stają się zastępczym dopasowaniem między eksportami. Duplikaty nazw i niejednoznaczność przodków nadal blokują dopasowanie/diff. Potwierdzanie braku nadal wymaga kompletnego publicznego zakresu; niepełne lub nieidentyfikowalne inventory nie dowodzi usunięcia.

### Czytelność raportu

comparison.json zachowuje wszystkie szczegółowe entries. Do lokalnego kontraktu raportu lab-events-comparison/1.0 dodano pola `matched_counts` i `diagnostic_summary`; dla niepełnej tożsamości także `entries[].identity_issue`. Pierwszy licznik oznacza pary z jednoznaczną tożsamością, w tym pary PARTIAL; nie jest dodatkowym statusem ani sumą tylko UNCHANGED.

Markdown grupuje diagnostykę według kind/status/reason/identity_issue i podaje liczby oraz maks. trzy przykłady z JSON Pointer do entries. Każdy przykład pokazuje maks. trzy locatory na stronę i pełną liczbę locatorów; pełne listy pozostają w entries. Nie generuje setek jednakowych wierszy UNKNOWN. Pozostałe sparowane rekordy, zmiany konfiguracji i linki do diffów pozostają w tabelach ekranów/grup. Raport jawnie odróżnia UNKNOWN tożsamości od PARTIAL pokrycia lub odczytu właściwości; globalny PARTIAL nie oznacza błędnej identyfikacji.

### Wynik rzeczywistego porównania A/B

Wejścia, bez modyfikacji:

- A: `X:\TESTY_TIA_Bridge\EventExports\20261029T111051984Z-a2e5858d75d44a659edf6bbb2791e92d\events.json` — 135658 bajtów, SHA-256 `acc02440cb4c1aff9784b7d73640f7d6f386f01d9724bee1bc3954c408dc7118`.
- B: `X:\TESTY_TIA_Bridge\EventExports\20261029T114706775Z-fdaba0c94f3c481292800b0280d1db39\events.json` — 135658 bajtów, SHA-256 `4dbe974f9a64ee30b3364285e91c94a36aa9cab2a20ce6e32f49b0c43046b257`.

Oba mają kompletne metadane identyfikacyjne 211 obiektów: SOFTWARE 1, SCREEN 7, ITEM 203; po 48 handlerów i po 3 obserwacje ENCAPSULATED_CHILDREN_NOT_PUBLIC. **Dopasowano 211 obiektów i 48 handlerów.**

| Rekordy | UNCHANGED | MODIFIED | UNKNOWN | PARTIAL | ADDED | REMOVED |
|---|---|---|---|---|---|---|
| Obiekty | 208 | 0 | 0 | 3 | 0 | 0 |
| Handlery | 48 | 0 | 0 | 0 | 0 | 0 |

Ogólny wynik **PARTIAL**, exit **2**, zachowany wyłącznie z powodu niepełnego pokrycia trzech kontenerów. Brak INCOMPLETE_LOGICAL_IDENTITY. Nie wymuszano UNCHANGED: niezależne porównanie surowych ScriptCode, GlobalDefinitionAreaScriptCode i async dla wszystkich 48 sparowanych handlerów potwierdziło zero różnic. Testy syntetyczne dowodzą, że zmiany każdej z tych właściwości nadal dają MODIFIED.

Dowody w ignorowanym katalogu `.tia-bridge/compare-identity-fix-fde0af11b46c4198ae74169530190e66/`:

- Odtworzenie starego błędu: EventComparisons/20261029T115117Z-1a6327264acb4358a4d51aa4c272f0e1/.
- Finalne porównanie: final/EventComparisons/20261029T120243Z-c57ef0dbd70e41abaa7387cb1d0965c1/ — comparison.json, comparison-report.md i manifest.
- real-comparison-verification-final.json: exit 2, liczniki, hashe wejść i kodu porównywarki, niezależne porównanie właściwości i weryfikacja całego zestawu plików/manifestu; real-comparison-cli-final.txt zachowuje CLI. Poprzednie przebiegi pozostawiono obok.
- tests-final.txt: **44/44 PASS**, w tym 30 istniejących i 14 nowych testów. Fixture powtarza format LAB-03K i przykładowy locator urządzenia z zadania; pozostałe dane i kod JS są syntetyczne, bez kopiowania kodu z rzeczywistych eksportów.
- final-report.json: zbiorcze wyniki, ścieżki dowodów i kontrola niezmienności LAB-03K/L/M oraz historii eksportów.

Nowe testy obejmują model 211/48 z array device_path i bezpośrednimi Screens, niezależne przestawianie ekranów/elementów, code/GDA/Async, granice segmentów z procentami/slash/Unicode, błędny binding, grupy/nesting, duplikaty, rename przy tych samych indeksach, brak obiektu przy COMPLETE/PARTIAL, nieprawidłowe locatory i związek handler–owner. Raport testowy 518 UNKNOWN zachowuje wszystkie entries w JSON, a Markdown ma mniej niż 10000 znaków. Osobny przypadek stu zduplikowanych nazw sprawdza ograniczenie liczby locatorów w przykładach. Manifest, marker, diff i zachowanie historii pozostają sprawdzane.

Regresja laboratoryjna bez przebudowy lub edycji LAB: LAB-03K 42/42, LAB-03L 16/16, LAB-03M 14/14, LAB-03I 18/18, LAB-03J 15/15, LAB-03H 10/10 PASS. Razem z porównywarką **159/159 PASS**, dodatkowo kontrole CLI/launch i byte regression. Zależności i polecenia porównywarki poniżej pozostają aktualne; regresje L/M uruchamiane istniejącymi test_lab_event_view.ps1 i test_lab_event_console.ps1 z zachowanymi baseline z ich dokumentacji.

Poniżej zachowano opis pierwotnego wdrożenia oraz jego historyczne wyniki 30/30; powyższe wyniki dotyczą bieżącej naprawy.

Data: 2026-10-29. Baza: `421958c`, `main`, początkowo czyste drzewo. Narzędzie laboratoryjne offline; bez Siemens Openness, vendor DLL, uruchamiania LabExporter lub JavaScript. Nie rozszerza produkcyjnego Host/API/CLI, synchronizacji ani uprawnień. Wejście to rzeczywisty format [LAB-03K](LAB_03K_HMI_EVENT_EXPORT.md), `lab-03k-events/1.0`; zasady niepełnego odczytu pozostają zgodne z [ADR 0003](decisions/0003-hmi-canonical-model-and-native-artifacts.md).

## CLI i zależności

Python 3.12, wyłącznie biblioteka standardowa. Launcher wymaga Windows PowerShell 5.1 i istniejącego `py -3`; nie instaluje zależności.

```powershell
py -3 -B scripts/compare_events.py compare-events `
  --before 'X:\EventExports\before\events.json' `
  --after 'X:\EventExports\after\events.json' `
  --output 'X:\EventReports'
```

Alternatywa PowerShell:

```powershell
& .\scripts\compare-events.ps1 -Before 'X:\EventExports\before\events.json' `
  -After 'X:\EventExports\after\events.json' -Output 'X:\EventReports'
```

`--output` jest katalogiem nadrzędnym: wynik zawsze powstaje w `EventComparisons/<UTC-UUID>/`. Można użyć ignorowanego `.tia-bridge/` lub własnego katalogu poza repo. Nie wskazuj katalogu projektu TIA jako miejsca raportów. Wejścia pozostają tylko do odczytu; żadne pliki JS/manifesty obok wejść nie są otwierane. Nie jest to kontrola autentyczności eksportu lub podpisu: raport wiąże dokładne bajty wejściowych JSON przez SHA-256 i długość.

Exit 0: ukończone porównanie, także z wykrytymi zmianami. Exit 2: opublikowane `UNKNOWN` lub `PARTIAL`. Exit 1: błąd wejścia/publikacji. Błędny JSON, obcy schemat, zduplikowany klucz JSON i niepoprawne Unicode są odrzucane przed utworzeniem raportu. Brakujące metadane obiektów/handlerów nie stają się wartościami domyślnymi; powodują `UNKNOWN` lub `PARTIAL`.

## Tożsamość i reguły dopasowania

Oba eksporty muszą mieć dokładnie taki sam, niepusty `identity.project_path` i zgodne uporządkowane segmenty `identity.device_path` (natywna tablica LAB-03K albo wcześniejszy kodowany string). To binding deklaracji eksportu, bez niezależnego potwierdzenia projektu. Różnica lub brak daje `UNKNOWN`, bez zgadywania zgodności kopii projektu. PID, czas, numer odczytu i kolejność nie są tożsamością obiektu.

Locator służy wyłącznie do odtworzenia relacji rodzic–dziecko wewnątrz pojedynczego eksportu oraz do odwołań w raporcie. Klucz logiczny to urządzenie oraz kolejne pary kind/nazwa grupy, ekranu i elementu; handler dodaje nazwę zdarzenia. Typ obiektu, handlera i zdarzenia jest porównywaną konfiguracją. Zmiana typu przy jednoznacznej hierarchii daje `MODIFIED`; zduplikowane nazwy w tej samej hierarchii dają `UNKNOWN`, również gdy typ mógłby sugerować wybór. Niejednoznaczność przodka propaguje się do potomków. Takie wpisy nie otrzymują spekulacyjnego diffu kodu.

Zmiana kolejności ekranów, grup, elementów lub handlerów nie zmienia klucza. Jednoznaczne identyczne nazwy w różnych grupach są rozdzielone. Brak rodzica, brak nazwy/typu, duplikat locatora, nieznany kind lub nieodczytane dane identyfikujące zdarzenie powodują `UNKNOWN`. Rename/przeniesienie nie jest rozpoznawane jako trwała tożsamość: przy potwierdzonej kompletności może dać `REMOVED` i `ADDED`.

## Rozłączne statusy i kompletność

Każdy wpis ma dokładnie jeden status. Osobne liczniki dla obiektów i handlerów zapobiegają liczeniu zmiany ekranu jako zmiany wszystkich jego handlerów.

| Status | Reguła |
|---|---|
| `UNCHANGED` | Jednoznaczne dopasowanie, wymagane odczyty kompletne i wszystkie porównywane właściwości identyczne |
| `ADDED` | Wpis obecny tylko po zmianie, a brak przed zmianą potwierdzony w odpowiednim publicznym zakresie |
| `REMOVED` | Wpis obecny tylko przed zmianą, a brak po zmianie potwierdzony w odpowiednim publicznym zakresie |
| `MODIFIED` | Jednoznaczne dopasowanie i kompletne odczyty; różnica kodu, konfiguracji, typu lub statusu |
| `UNKNOWN` | Brak bindingu lub niejednoznaczna/niepełna tożsamość; brak zgadywania dopasowania |
| `PARTIAL` | Dopasowanie możliwe, ale odczyt właściwości/pokrycie niepełne albo nie można dowieść braku wpisu |

Pierwszeństwo wpisu: binding/tożsamość `UNKNOWN`, następnie brak z dowodem lub bez, następnie niepełny odczyt `PARTIAL`, następnie dokładna różnica. Dla sparowanego niepełnego handlera zachowane są ustalone różnice innych właściwości, ale jego jedynym statusem pozostaje `PARTIAL`. `null`, `READ_FAILED`, nieznane `Async` i nieznana wartość enum nie stają się pustym kodem, false lub `UNCHANGED`. Poprawny pusty kod (`EMPTY`, `PRESENT_EMPTY`) jest porównywalny; GDA i Async są oceniane niezależnie.

Brak handlera wymaga kompletnego `handlers_status=ENUMERATED/ABSENT` właściciela i braku nieidentyfikowalnych handlerów na nim. Brak obiektu wymaga kompletnego `children_status=COMPLETE_FOR_PUBLIC_SCOPE` najbliższego jednoznacznego zaobserwowanego przodka. Te same reguły działają symetrycznie dla `ADDED`. Nieidentyfikowalne obiekty lub osierocone handlery konserwatywnie blokują dowodzenie braku w eksporcie. Niepełne `children_status` kontenera nie blokuje porównania jego poprawnie odczytanych handlerów ani dowodu braku w jego kompletnej kompozycji EventHandlers.

Globalne `PARTIAL` jest zachowane, lecz nie zmienia automatycznie lokalnych `UNCHANGED/MODIFIED/ADDED/REMOVED`. Wynik ogólny: `UNKNOWN` przy niepewnej tożsamości, następnie `PARTIAL` przy niepełnym eksporcie/wpisach, następnie `MODIFIED` przy dowolnej ustalonej zmianie, w przeciwnym razie `UNCHANGED`. Ogólny status nie jest dodatkowym handlerem i nie zwiększa liczników. Brak obiektu w pustym lub niekompletnym inventory nie dowodzi usunięcia.

## Dokładność, limity i raport

`ScriptCode` i `GlobalDefinitionAreaScriptCode` porównuje się znak po znaku po zdekodowaniu JSON. Nie normalizuje się Unicode, białych znaków ani CRLF/LF; importy są częścią surowego GDA/kodu, bez parsera i wykonania JS. Zmiana sposobu escapowania tego samego stringa w JSON nie jest zmianą kodu, ale zmienia hash źródłowego pliku. Nie ma dodatkowej klasyfikacji „tylko formatowanie”.

Porównywane są także `async`, typy, statusy/presence i dodatkowe właściwości wierszy. Techniczne locatory, nazwa właściciela handlera oraz pochodne ścieżki/hashe JS są wyłączone z porównania treści; nazwa właściciela pochodzi z inventory. Nieznane właściwości wierszy pozostają w porównaniu; dodatkowe metadane eksportów i obserwacje są zachowane w identyfikacji źródeł. Plik `events.json` jest źródłem wartości kodu, bez fallbacku do plików skryptów.

Stałe limity: 64 MiB na wejście, głębokość JSON i hierarchii 64, maks. 16384 wiersze w każdej z trzech list, 256 MiB łącznie na raportowane artefakty bez manifestu. Diff ma liniowy koszt tworzenia i pokazuje pełne usunięte/dodane teksty jako JSON-quoted lines z oryginalnymi zakończeniami (`\r\n`, `\n`, brak końcowego newline). Jest przeznaczony do czytania, nie do automatycznego patch/apply. Pliki diff powstają dla jednoznacznie sparowanych handlerów ze zmianą kodu/GDA, również z jawnym oznaczeniem nieodczytanej strony w `PARTIAL`. Async i pozostała konfiguracja są pokazane w JSON i Markdown.

Każdy przebieg zapisuje:

- `comparison.json`: wersja, run-id, identyfikacja wejść, raw SHA-256/długości, coverage/obserwacje, klucze logiczne, pełne locatory obu stron, lokalne statusy odczytu, zmienione właściwości i liczniki. matched_counts i diagnostic_summary opisują dopasowania oraz powody niepewności; entries zachowują wszystkie szczegóły, w tym identity_issue.
- `comparison-report.md`: liczniki wszystkich sześciu statusów, zakres wiarygodności, zbiorcze przyczyny UNKNOWN/PARTIAL z przykładami, lista sparowanych wpisów według ekranów/grup, zmiany konfiguracji i linki do diffów. Nazwy/ścieżki są escapowane przeciw interpretacji jako HTML, obrazy lub linki Markdown.
- `diffs/*.diff`: dokładne zmiany kodu/GDA, bez nazw użytkownika w nazwach plików.
- `manifest.json`: ścieżki względne, długości i SHA-256 wszystkich trzech powyższych rodzajów artefaktów. Wyłącza siebie i marker `.incomplete`, co zapisuje jawnie.

Nowy katalog jest rezerwowany wyłącznie przez `mkdir`, pliki przez exclusive create. Istniejący wynik nigdy nie jest otwierany do zapisu. `.incomplete` znika dopiero po ponownym odczycie i weryfikacji wszystkich artefaktów oraz manifestu. Awaria pozostawia nowy katalog z markerem; konsumenci wymagają manifestu i braku markera. Nie ma czyszczenia, retry ani nadpisywania starszych wyników. To raport laboratoryjny, bez gwarancji trwałości przy utracie zasilania ani ochrony przed równoległą złośliwą zmianą filesystemu.

## VS Code

W [.vscode/launch.json](../.vscode/launch.json) dodano wyłącznie czwarty wpis:

```json
{
  "name": "TIA Bridge: COMPARE EVENTS — Export ↔ Export",
  "type": "node-terminal",
  "request": "launch",
  "command": "powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/compare-events.ps1",
  "cwd": "${workspaceFolder}"
}
```

Run and Debug → wybierz powyższą nazwę → F5. Terminal pyta wyłącznie o dwa istniejące `events.json` i katalog nadrzędny raportów. Odpowiedzi Read-Host są przekazywane jako argumenty, bez interpretowania ich jako PowerShell. Trzy wcześniejsze konfiguracje FORCE EXPORT, FORCE IMPORT i EXPORT EVENTS są identyczne względem HEAD; nie uruchamiano ich z odbiorcą TIA. Sprawdzono rzeczywisty wrapper z parametrami zawierającymi whitespace, apostrof i Unicode. GUI F5/interaktywna obsługa terminala: **NOT_RUN**.

## Testy i przykład

```powershell
py -3 -B scripts/test_compare_events.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03i.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03j.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File X:\TESTY_TIA_Bridge\evidence\LAB-03H-publisher-tests.ps1
py -3 scripts/audit_quality.py
git diff --check
```

Testy porównywarki: **30/30 PASS**. Obejmują identyczność, kod/importy/Async/typy, dodanie i usunięcie zdarzeń/ekranów, zmianę kolejności grup/ekranów/elementów, duplikaty i przodków, puste i nieodczytane handlery, globalny i lokalny PARTIAL, brak dopasowania/bindingu, Unicode/CRLF/LF, nowe pola, deterministyczność, limity, błędny JSON, integralność i niezmienność wyników oraz awarię zapisu z zachowaniem markera. Regresje: LAB-03K **42/42**, LAB-03I **18/18**, LAB-03J **15/15**, LAB-03H **10/10 PASS**. Razem **115/115** testów offline; dodatkowo wrapper CLI PASS i dwie istniejące kontrole LAB-03K CLI/launch PASS.

Przykład syntetyczny, nie dane projektu klienta: `.tia-bridge/compare-events-9a1d7ad622dc4fba8aead35673a86d49/EventComparisons/20261029T104650Z-fd548ad6a79544699fe09278218f4b5f/comparison-report.md`. W tym samym katalogu są JSON, diffy i manifest. Globalny `UNKNOWN`, zachowany globalny `PARTIAL` eksportu; ekran `Group/Main` zawiera:

| Handler | Status | Ustalona zmiana |
|---|---|---|
| Tapped | MODIFIED | ScriptCode, import w GDA, Async false → true |
| Up | REMOVED | Brak potwierdzony kompletną kompozycją EventHandlers |
| Down | UNCHANGED | Dokładnie te same wartości |
| Loaded | PARTIAL | ScriptCode READ_FAILED, bez podstawienia pustego tekstu |
| Unloaded | UNKNOWN | Zduplikowana nazwa zdarzenia |
| Pressed | ADDED | Nowy handler w kompletnym zakresie |

Po jednym handlerze w każdym statusie. Dowody porównywarki: `results.json`, `tests.txt`, `wrapper-cli.txt` w powyższym katalogu nadrzędnym. Nowe dowody regresji laboratorium: `evidence/LAB-03K-offline-c43ca4e12d5248ccb2fcba88471920fe`, `evidence/LAB-03I-offline-adb171f79adf4196bbabb4942f630bb3`, `evidence/LAB-03J-offline-33b702ba03e8476caf374ffef21cdde1`; LAB-03H: `evidence/LAB-03H-offline-tests-1bc89a0bbf1241fea9820f0149223225.json`. Historycznych dowodów nie modyfikowano.

Pierwsza wersja nie rozpoznaje rename/move po vendor ObjectId, nie udowadnia nieobserwowalnych wnętrz kontenerów, nie czyta innych wersji schematu i nie weryfikuje sąsiedniego manifestu wejściowego. Exact binding ścieżek może dać UNKNOWN dla przeniesionej kopii tego samego projektu. Nie jest to CANONICAL HMI, pełny model ekranów, native round-trip, capability PASS lub SYNCED. Engineering i Runtime: **NOT_RUN**. Nie wykonano Attach, Import, Save, Compile, Download, commita ani push.
