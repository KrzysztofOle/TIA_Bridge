# LAB-03K — laboratoryjny eksport zdarzeń Unified V19

Data: 2026-10-29. Baza repo: `ded62b7`, `main`, początkowo czyste drzewo. Zlecenie obejmuje implementację i testy offline, bez Attach ani operacji TIA, commita i push. Laboratorium `X:\TESTY_TIA_Bridge` nie jest repozytorium Git; poprzednie źródło, EXE i README zachowano w `evidence\LAB-03K-before-dbb3a3bd7cdc40c4a74270168b60583b`.

## Polecenie i granica architektury

`X:\TESTY_TIA_Bridge\bin\LabExporter.exe export-events` jest osobnym, ręcznie uruchamianym poleceniem istniejącego narzędzia net48/x64. Nie korzysta z produkcyjnego Host/Worker/IPC, registry ani E1 i nie nadaje im nowej ścieżki wykonania. Bieżące zlecenie użytkownika jawnie dotyczy odrębnego laboratorium; zgoda na implementację nie uprawnia do jego uruchomienia z TIA.

Przeanalizowano [LAB-03I](LAB_03I_GLOBAL_DEFINITION_NAMING.md), [LAB-03J](LAB_03J_NATIVE_EXPORT_DIAGNOSTICS.md), [ADR 0003](decisions/0003-hmi-canonical-model-and-native-artifacts.md) i [reader offline](HMI_EVENT_READER_OFFLINE.md). Reader produkcyjny obejmuje sześć konkretnych par; nowy eksporter obejmuje wszystkie skonfigurowane handlery publicznych ekranów/elementów. Nie skopiowano readera, jego projekcji, zależności ani zakresu T05. Przeniesiono zasady semantyczne: niezależne odczyty, zachowanie raw text, jawne błędy i limity, brak inferred absence. Nowe interfejsy są wewnętrzne dla laboratorium.

Zmiana starego `LabExporter.cs` ogranicza się do `partial class` i pojedynczego dispatchu `export-events`. Dotychczasowe funkcje, parametry i kod FORCE EXPORT/IMPORT pozostają identyczne po pominięciu tych dwóch dopisków i reprezentacji EOL. Nowe pliki:

- `src\LabEventExport.cs`: bounded traversal, wynik wartościowy, hashe i publikacja artefaktów; bez zależności Siemens.
- `src\LabEventVendor.cs`: lifecycle sesji i jawny adapter typowany publicznego V19.

Nie zmieniono `SplitModules.ps1`, `RebuildImportPackages.ps1`, `Scripts`, starych eksportów, projektów ani `T05_TIA_Bridge.py`. Nie dodano bibliotek lub frameworka.

## Zakres publicznego odczytu

Trasa: `HmiSoftware.Screens` / `ScreenGroups` → `HmiScreenGroup.Screens` / `Groups` → `HmiScreen.ScreenItems` → `EventHandlers` → `EventType`, `Script` → `IHmiScript.ScriptCode`, `GlobalDefinitionAreaScriptCode`, `Async`.

Adapter zawiera jawną allowlistę **39 publicznych typów V19 z EventHandlers**, ustaloną z lokalnego PublicAPI `Siemens.Engineering.dll`. Wywołuje gettery przez statyczne typy; brak refleksyjnego dispatchu, `GetAttribute`, wyszukiwania dowolnych kompozycji lub wykonywania metod po nazwie. Typy pochodne sprawdzane są przed bazowymi, m.in. segmenty kształtów i slider. Obecność typu/metody to dowód metadanych i kompilacji, nie Engineering support.

Eksport enumeruje wszystkie skonfigurowane handlery każdego obsługiwanego obiektu; nie jest ograniczony do nazw ekranów, przycisków lub enumów z readera diagnostycznego. Nazwa zdarzenia pochodzi wyłącznie z odczytanego `EventType`, a wynik zawiera też publiczny typ tej wartości i runtime typ handlera. Nie enumeruje `PropertyEventHandlers`, `Dynamizations`, interfejsów kontrolek ani bibliotek faceplate.

**Granica zagnieżdżenia:** traversal obsługuje rekurencyjne relacje dzieci i testuje je syntetycznie. Zainstalowane publiczne API V19 udostępnia `ScreenItems` na ekranie; nie znaleziono takiej kompozycji na elementach. Faceplate/custom widget/custom web control mają `children_status=UNKNOWN`, obserwację `ENCAPSULATED_CHILDREN_NOT_PUBLIC` i wynik `PARTIAL`. Wszystkie ich dostępne publiczne handlery nadal są czytane. Wnętrza kontrolek nie są rekonstruowane lub uznawane za puste. Screen windows są obserwowanymi elementami; wszystkie publiczne ekrany są odczytywane niezależnie, bez podążania za cyklicznymi referencjami okien do ekranów.

Locator używa zakodowanej procentowo ścieżki urządzenia, nazw kompozycji i ordinali, np. `/HMI/Device/Runtime/ScreenGroups[0]/Groups[0]/Screens[0]/ScreenItems[2]/EventHandlers[1]`. Nazwa i typ każdego obiektu są osobnymi polami. Duplikaty nazw i eventów pozostają odrębnymi obserwacjami. Locator nie jest trwałym ObjectId; zmiana kolejności po kolejnym odczycie może go zmienić.

## Semantyka kompletności

| Przypadek | Wynik |
|---|---|
| Obecny handler, poprawny kod | `PRESENT`; tekst `READ` albo dokładnie `EMPTY` |
| ScriptCode dokładnie pusty | `PRESENT_EMPTY`, plik kodu zero bajtów i hash pustej wartości; GDA odczytane niezależnie, zgodnie z semantyką readera offline |
| Kompletna pusta kompozycja EventHandlers | `handlers_status=ABSENT` na obserwowanym obiekcie; brak wymyślonych handlerów |
| Brak gettera/skryptu, null lub błąd | `UNKNOWN` / `READ_FAILED`, osobna faza i typ błędu; nigdy domyślne false lub pusty kod |
| Nieznany enum | Odczytana wartość zachowana, `UNKNOWN_EVENT_VALUE`, wynik `PARTIAL` |
| Unsupported typ | Obiekt pozostaje w inventory; `UNSUPPORTED_OBJECT`, `PARTIAL` |
| Niepełny iterator/getter/Dispose/limit | `PARTIAL`, powód i locator; zachowanie wcześniej odczytanych wartości |
| Wszystkie wymagane publiczne odczyty kompletne | `COMPLETE_FOR_PUBLIC_SCOPE`; bez deklaracji pełnego modelu HMI lub SYNCED |

Zwykłe niezależne błędy getterów zachowują możliwość odczytu pozostałych właściwości dokładnie raz. Wyjątek typu `Siemens.*` zatrzymuje dalsze odczyty treści; błąd Dispose iteratora również zatrzymuje traversal. Pozostają końcowe kontrole lifecycle i zwolnienie własnego połączenia. Brak retry lub zastępowania bieżącego odczytu starszym eksportem. Wiadomości i stosy wyjątków nie trafiają do logów/raportów; tylko kontrolowana faza/kod, typ i HRESULT.

`engineering_qualification=NOT_RUN` pozostaje niezależne od wykonania. Syntetyczne wyniki mają `execution_evidence=SYNTHETIC_OFFLINE`; przyszły rzeczywisty odczyt będzie miał `ENGINEERING_OBSERVATION_ONLY`. Dostępność `RowSelected` w tym zadaniu: **UNKNOWN**. Wyłącznie rzeczywisty odczyt skonfigurowanego handlera może dać `OBSERVED_PRESENT` dla tej obserwacji, bez capability PASS i bez dowodu dostępności na innym obiekcie.

## Bezpieczeństwo i tożsamość

Wymagane są trzy jawne opcje: katalog projektu, katalog wyników i dokładna ścieżka urządzenia. Brak domyślnego HMI lub wyboru pierwszego dopasowania. W katalogu projektu musi znajdować się dokładnie jeden zwykły AP19. `GetProcesses` musi wskazać dokładnie jeden otwarty projekt o tej ścieżce. Weryfikowane są PID/start UTC, dokładna ścieżka pliku EXE Portal V19 i sesja Windows operatora, a następnie PID/ProjectPath aktualnego połączenia, liczba projektów i `Project.Path`. Dwa identyczne locatory HMI są odmową; wymagany jest rzeczywisty `HmiSoftware`. Discovery HMI nie odczytuje modułów `Scripts`.

Przed traversal wymagane `IsModified=False`. Po odczycie osobno sprawdzane są project/process identity, `IsModified` i ponowne jednoznaczne wskazanie HMI wraz z nazwą/typem. Nieudana kontrola daje `PARTIAL`, zachowując treść i niezależne wyniki pozostałych kontroli. W JSON są czas, operator SID, sesja, PID/start, AP19, HMI, identyfikacja/hash PublicAPI i zwrot Dispose. Są to obserwacje przed/po, nie transakcja ani blokada wszystkich równoległych edycji operatora.

Mutex dla PID/start blokuje równoległe nowe `export-events`; abandoned owner jest odmową bez ponownego Attach. Dotychczasowe polecenia FORCE nie korzystają z tego mutexu — operator nadal musi wykluczyć równoległe narzędzia/Attach. Ewentualny monit Openness zatwierdza ręcznie użytkownik. Nie zmieniono whitelist, kont, ACL ani automatyzacji GUI. Nie ma setterów, Import, Save, Archive, Compile, Download lub JS execution w nowej ścieżce. Zwalniany jest tylko własny `TiaPortal`, nigdy `TiaPortalProcess.Dispose`.

Pojedynczy getter/Attach/MoveNext/Dispose może blokować bezterminowo. Limity ilościowe nie przerywają Siemens call. Brak automatycznego timeoutu/retry/kill; brak odpowiedzi nie dowodzi ustania wywołania. Kolejna próba live wymaga osobnej decyzji po uzgodnieniu stanu.

## Artefakty i limity

```text
EventExports/
  <UTC>-<UUID>/
    events.json
    events-report.md
    manifest.json
    scripts/
      <SHA256-locatora-handlera>-ScriptCode.js
      <SHA256-locatora-handlera>-GlobalDefinitionAreaScriptCode.js
```

Każdy uruchomiony odczyt ma nowy run ID. `events.json` zawiera pełne dostępne wartości obu tekstów, niezależne statusy i ścieżki/hash każdego tekstu, metadata/inventory, błędy i tożsamość. Raport ma tabele ekranów/grup/elementów, handlerów i błędów oraz liczniki i coverage; nie zawiera treści JS. Nazwy plików bazują na pełnym SHA-256 locatora, a duplikat ścieżki blokuje publikację.

JS zapisuje się jako ścisłe UTF-8 bez dodawania BOM, nagłówka, separatora lub końcowego newline. Zachowane są zwrócone znaki, także istniejący U+FEFF, mieszane CR/LF, znaki spoza BMP i postać Unicode. To kodowanie stringa API, nie dowód tożsamości z niedostępnymi natywnymi bajtami Siemens. Niepoprawne samotne surrogate powodują `INVALID_UNICODE`, bez cichego replacement. Nie ma parsera lub wykonywania kodu JS.

Manifest obejmuje wszystkie JS, JSON i raport: ścieżki względne, długości bajtowe, SHA-256, wersję i hash EXE oraz identyfikację odczytu. Manifest wyłącza własny hash i marker `.incomplete`; ten wyjątek zapisano w samym manifeście. Po zapisie pliki są ponownie czytane i hashowane. Publikacja następuje przez rename nowego stagingu obok wyniku; istniejący eksport nigdy nie jest zastępowany. Ukończenie wymaga manifestu i braku `.incomplete`. Błąd zachowuje `.event-incomplete-<UUID>` lub wynik z markerem, bez czyszczenia/rollbacku Siemens.

Docelowy katalog musi być na lokalnym NTFS, poza repozytoriami Git (także worktrees), projektem TIA, instalacją Siemens, katalogami `src`/`bin` laboratorium oraz każdym segmentem `Scripts`. Reparse points w istniejących składowych ścieżki są odrzucane. Rodzic i katalog wynikowy mogą zostać utworzone. Zbyt długa ścieżka albo błąd dysku jest odmową/publikacją nieukończoną; poprzednie eksporty pozostają zachowane. Walidacja nie jest ochroną przed złośliwą równoległą zmianą filesystemu przez inny proces.

Stałe limity CLI: 256 elementów na kompozycję; 64 handlerów na obiekt; 8192 elementów i handlerów łącznie; 16 poziomów; 512 znaków metadanych; 262144 jednostki UTF-16 na kod/GDA, 4194304 łącznie; 64 MiB opublikowanych artefaktów z manifestem. Discovery urządzeń ma osobne 256 devices / 8192 odwiedzonych węzłów / 16 poziomów. Limity nie są opcjami klienta. Iterator może wykonać jedno dodatkowe `MoveNext`, lecz nie odczytuje nadmiarowego `Current`. Odrzucenia głębokości również zużywają globalny budżet.

Tekstu nie obcina się do limitu: wartość/hash/plik są nieobecne i oznaczone `LIMIT_EXCEEDED`. Gdy całkowita paczka przekracza limit artefaktów, kod/GDA są jawnie pomijane, zachowując inventory i `PARTIAL` w raporcie. Jeżeli nie mieści się nawet metadata, wynik pozostaje nieopublikowany; marker zawiera `PARTIAL/LIMIT_EXCEEDED` i liczniki pominięć. Awaria IO nie jest PASS. Exit 0 = kompletny zakres publiczny; 2 = opublikowany `PARTIAL`; 1 = parametry lub nieudana publikacja.

## Uruchomienie po osobnej zgodzie Engineering

W tej pracy **nie uruchomiono** poniższego polecenia z poprawnym odbiorcą:

```powershell
& 'X:\TESTY_TIA_Bridge\bin\LabExporter.exe' export-events `
  --project 'X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy' `
  --output 'X:\TESTY_TIA_Bridge\EventExports' `
  --hmi 'HMI_JSON_TEST/HMI_RT_1'
```

Ścieżka HMI to dokładne nazwy `Device/DeviceItem/...`, porównywane ordinalnie; segmenty zawierające `/` lub `%` trzeba zakodować procentowo (`%2F`, `%25`). Nie jest to ścieżka pliku. Przykład wskazuje historyczny locator, który musi zostać świeżo potwierdzony przed testem; nie jest bieżącą autoryzacją.

VS Code: otwórz projekt TIA_Bridge 2, wybierz Run and Debug → **TIA Bridge: EXPORT EVENTS — TIA → Folder** i uruchom dopiero po osobnej zgodzie live. W terminalu PowerShell podaj kolejno katalog AP19, katalog wyników i dokładny locator HMI. `Read-Host` przekazuje odpowiedzi jako dane; nie wkleja ich do kodu polecenia. Ręcznie obsłuż ewentualny monit Siemens. Dwie dotychczasowe konfiguracje FORCE pozostają bez zmian. F5 i UX terminala: **NOT_RUN**; zweryfikowano statycznie JSON/config i poprawne przekazanie argumentów z whitespace/apostrofem do syntetycznego programu.

## Walidacja offline

Zależności: istniejący .NET Framework x64 `csc.exe`, PublicAPI V19 dla builda eksportera/inspekcji metadata i Windows PowerShell 5.1 x64. Testy readera kompilują dokładny laboratoryjny `LabEventExport.cs` z syntetycznym `ILabEventAccess`; bez wywołań Siemens. IL adaptera i lifecycle jest czytany jako metadata, bez jego uruchomienia. Regresje używają istniejących harnessów.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03i.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab03j.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File X:\TESTY_TIA_Bridge\evidence\LAB-03H-publisher-tests.ps1
py -3 scripts/audit_quality.py
git diff --check
```

| Kontrola | Wynik | Dowód względem `X:\TESTY_TIA_Bridge` |
|---|---|---|
| Finalny build net48/x64 | PASS, 0 warnings/errors | `evidence\LAB-03K-build-93270ba42b4b49d1be564d82e7099879\build-manifest.json` |
| Nowy reader/publisher, IL i katalogi | 42/42 PASS | `evidence\LAB-03K-offline-551ff00c283c4ae6ad22d4a792ae6bdd\results.json` |
| Argumenty launch i odmowa niepełnego CLI | 2/2 PASS, bez Siemens calls | `launch-check.json`, `cli-check.json` w tym samym katalogu |
| FORCE EXPORT/IMPORT, LAB-03I | 18/18 PASS | `evidence\LAB-03I-offline-8ac0cc6dbfca4467858d08fcb4e1ebf2\results.json` |
| Natywna walidacja/backup, LAB-03J | 15/15 PASS | `evidence\LAB-03J-offline-202b802b07f44572a0ca231f4804c9cb\results.json` |
| Publisher LAB-03H | 10/10 PASS | osobny nowy `LAB-03H-offline-tests-*.json`, wskazany w zbiorczym raporcie |
| Dokumentacja/linki/schema i diff | PASS, 0 errors | `audit_quality.py`, `git diff --check` |
| Stare launch/config i implementacja FORCE | Bez zmian | porównanie obu pierwszych wpisów JSON i źródła sprzed zadania |

Łącznie **85/85 testów C#/regresji PASS** oraz dwie dodatkowe kontrole CLI/launch PASS. SHA-256 finalnego `bin\LabExporter.exe`: `8dfe559b54adcf10a7e4b079096b6cc15a9873e8dfb3541e079735ea1f216a2c`. Wszystkie trzy regresje uruchomiono ponownie na tym finalnym EXE.

Testy obejmują hierarchie, zagnieżdżone dzieci syntetyczne, wszystkie publiczne rodziny adaptera w IL, różne zdarzenia, duplikaty, code/GDA/Async, null/empty/absent/unsupported, awarie getterów i etapów iteratora, STOP po błędzie Siemens, limity, pełne bajty i hashe artefaktów, odmowę nadpisania i bezpieczeństwo katalogu. Test IL zabrania vendor write i odczytu dynamizations. Własne stałe kody odmowy są zachowane jako `detail_code`; wiadomości wyjątków Siemens pozostają pominięte. Zmapowane dyski sieciowe nie spełniają wymaganego typu lokalnego Fixed/NTFS.

Pierwszy przebieg LAB-03K zachowano: `evidence\LAB-03K-offline-144046aab7d248b2811b7a764285a444`, 33/39 PASS. Wykrył zbyt długą ścieżkę stagingu i zależność kontroli ścieżki EXE od AppDomain testu. Poprawiono krótszy staging obok wyniku i wiązanie katalogu z assembly eksportera; kolejny przebieg 39/39 PASS. Dalsze testy obejmują także zużycie globalnego budżetu przy odmowie głębokości. Nie usuwano wcześniejszych dowodów.

## Lista zmienionych plików

| Lokalizacja | Pliki |
|---|---|
| Laboratorium `X:\TESTY_TIA_Bridge` | `src\LabExporter.cs`, nowe `src\LabEventExport.cs`, nowe `src\LabEventVendor.cs`, `bin\LabExporter.exe`, `README.md` |
| Repo — konfiguracja | `.vscode/launch.json` |
| Repo — narzędzia/testy | nowe `scripts/build_lab_events.ps1`, `scripts/test_lab_events.ps1`, `scripts/lab_events_tests.cs`; `scripts/README.md` |
| Repo — dokumentacja | nowy `docs/LAB_03K_HMI_EVENT_EXPORT.md`; `docs/LAB_03I_GLOBAL_DEFINITION_NAMING.md`, `docs/LAB_03J_NATIVE_EXPORT_DIAGNOSTICS.md`, `README.md`, `Zmiany.md` |

Buildy, poprzednie EXE, dane syntetyczne, raporty i zbiorczy manifest pozostają poza repo w nowych katalogach `evidence` laboratorium. Index Git, HEAD i pozostałe worktrees nie były modyfikowane.

## Ocena wpływu i kwalifikacja

Przegląd sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git i dane/kontrakty; Instrukcja_ChatGPT — sekcje 1–10. Obie instrukcje pozostają bez zmian: jawne zlecenie dotyczy samodzielnego laboratorium i zachowuje ich rozdzielenie zgody na kod od uprawnień TIA oraz offline/Engineering/Runtime. ADR 0001–0003, specyfikacje 03–08/12, kontrakty publiczne, E1/B0 i registry pozostają bez zmian. Laboratoryjny format obserwacji nie jest nowym produkcyjnym CANONICAL/SOURCE/NATIVE ani native screen export/import.

Engineering export, Attach, tożsamość rzeczywistego odbiorcy, kompletny zasięg API i dostępność `RowSelected`: **NOT_RUN / UNKNOWN**. Offline IL dowodzi tylko dozwolonych wywołań w zbadanym buildzie; syntetyczne testy nie kwalifikują lifecycle Siemens ani kompletności wnętrz HMI. Runtime, Import, Save, Compile, Download i produkcyjna kwalifikacja: **NOT_RUN**. Nie wykonano commita/push. Prace zatrzymują się po implementacji, dokumentacji i walidacji offline.
