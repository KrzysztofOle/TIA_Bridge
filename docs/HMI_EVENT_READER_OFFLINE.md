# Diagnostyczny reader zdarzeń HMI V19 — implementacja offline

Data: 2026-10-29. Baza: `088e72303be824eb08b8611269a4c0d87e15f9a0`, gałąź `main`.
Zakres zatwierdzony przez użytkownika: implementacja i testy offline. Engineering, Attach i Runtime: **NOT_RUN**. Bez commita/push, wdrożenia lub zmiany rzeczywistego projektu.

## Architektura i granica wykonania

[HmiEventDiagnosticReader](../src/TiaBridge.Worker.V19/HmiEventDiagnosticReader.cs) jest wewnętrznym komponentem istniejącego Workera net48/x64, kompilowanym przeciw zainstalowanemu publicznemu `Siemens.Engineering.dll` V19. Nie zmienia Core, Host, IPC, grantu, registry ani capability. Nie tworzy procesu, schedulera, sesji Siemens lub innego kanału odczytu. Używa istniejącej [projekcji](../src/TiaBridge.Worker.V19/HmiEventDiagnosticProjection.cs), rozszerzonej o niezależne błędy kodu i globalnych deklaracji.

Reader otrzymuje istniejące `HmiSoftware`, zweryfikowany locator urządzenia/software oraz ID obecnego owner thread. Odrzuca niewłaściwy wątek i pusty/nieprawidłowy locator przed pierwszym getterem. ID wątku jest asercją ownership, a locator argumentem wejściowym; żaden z nich nie jest dowodem autoryzacji lub tożsamości projektu. Reader nie wykonuje Attach, nie wyszukuje projektu/procesu i nie sprawdza `IsModified`. Te kontrole pozostają obowiązkiem przyszłej zatwierdzonej integracji w istniejącym lifecycle `VendorReader`, przed i po odczycie. Nie przenosi proxy przez IPC; wynik zawiera tylko wewnętrzne rekordy wartości.

**Obecnie nie istnieje żaden caller readera w produkcyjnym Workerze.** `VendorReader.cs` i `Program.cs` pozostają bez zmian, normalny dispatch nadal wykonuje wyłącznie DiscoveryV1 przez `project.inspect`. Nie dodano argumentu CLI, testowego endpointu, publicznego DTO ani alternatywnej ścieżki autoryzacji. Ten pakiet jest komponentem do przyszłej integracji, a nie uruchamialnym eksperymentem Engineering.

Wywołanie readera z obecnego `VendorReader.Inspect` rozszerzyłoby zatwierdzony zakres DiscoveryV1 o HMI content bez właściwego kontraktu/profilu/uprawnienia. Ten zależny zakres integracji pozostawiono zatrzymany; nie zastąpiono go innym entrypointem. Osobny przyszły pakiet musi uzgodnić wywołanie wewnątrz istniejącej sesji i ownera oraz wymagane bramki, bez obchodzenia architektury.

## Typowany zakres

Publiczne ścieżki: `HmiSoftware.Screens` / `ScreenGroups` → `HmiScreenGroup.Screens` / `Groups` → `HmiScreen.ScreenItems` → typowane `EventHandlers` → `Script` (`IHmiScript`). Nie enumeruje dowolnych kompozycji refleksją ani `PropertyEventHandlers`/dynamizations; nie utożsamia ich ze zdarzeniami ekranu/elementu.

| Obiekt | Event | Obsługiwany publiczny typ |
|---|---|---|
| `T05_Recipes_Main` | `Loaded`, `Unloaded` | `HmiScreen` |
| `ACTION_0` | `Tapped` | `HmiButton` lub allowlistowany custom container |
| `ACTION_1` | `Tapped` | `HmiButton` lub allowlistowany custom container |
| `RecipeTable_Main` | `RowSelected` | `HmiCustomWidgetContainer` / `HmiCustomWebControlContainer`, jeśli `GetValidEnumValues()` potwierdzi zdarzenie |
| `NAV_1` | `Up` | `HmiButton` lub allowlistowany custom container |

Element o innej klasie pozostaje `UNSUPPORTED_OBJECT`. Standardowe typy korzystają z jawnych enumów V19. Custom containers wymagają bounded `GetValidEnumValues()`; nazwa `RecipeTable_Main` nie dowodzi typu ani dostępności `RowSelected`. `SupportStatus=SUPPORTED` oznacza wyłącznie rozpoznany typ/model odczytu w tym readerze, bez kwalifikacji Engineering lub capability PASS.

Odczytywane wartości: nazwa i dokładny runtime typ obiektu, locator kompozycji/ordinal/nazwa, vendor `EventType`, `ScriptCode`, `GlobalDefinitionAreaScriptCode`, `IHmiScript.Async`. Publiczny V19 `IHmiScript` deklaruje `Async`, więc nie jest potrzebna refleksja do wyszukiwania opcjonalnej właściwości. Błąd tego gettera daje `Async=null` i `READ_FAILED`, nie domyślne `false`.

Wynik zawsze zawiera sześć żądanych par i osobne obserwacje inventory dla kompozycji/getterów. Locator jest lokalizatorem obserwacji, nie trwałym ObjectId. Przy brakującym/niejednoznacznym obiekcie ścieżka jest żądaną ścieżką diagnostyczną, `MetadataStatus=UNKNOWN`; nie udaje zaobserwowanej tożsamości. Duplikaty ekranów, nazw elementów lub handlerów kończą się `AMBIGUOUS_*`, bez wyboru pierwszego dopasowania.

## Semantyka wyniku i limity

| Przypadek | Wynik |
|---|---|
| Obecny handler, skuteczny niepusty getter | `PRESENT`, kod `READ` |
| Obecny handler, dokładnie pusty string | `PRESENT_EMPTY`, kod `EMPTY`, hash pustego stringa |
| Istniejący jednoznaczny obiekt, znany event kind, kompletne ancestry/handlers, brak handlera | `ABSENT`, kod `NOT_APPLICABLE`; vendor `EventType=null`, bo nie wykonano gettera nieistniejącego handlera |
| Brak obiektu/ekranu | inventory `OBJECT_ABSENT`, handler `UNKNOWN` |
| Getter rzuca / zwraca null | niezależne `READ_FAILED` / `UNKNOWN`, jawna przyczyna; nigdy `ABSENT` lub pusty string |
| Niepełna enumeracja / null collection, iterator lub element / błąd Dispose | `PARTIAL`; nie dowodzi braku handlera |
| Nieobsługiwany obiekt/event | jawne `UNSUPPORTED_*`; nieznana odczytana wartość enum zachowana jako `ObservedValue` inventory |

Zachowane są niezależne statusy kodu, GDA i Async. Sukces jednej właściwości nie ukrywa błędu pozostałej. Odczytane wartości mogą pozostać w częściowym wyniku po późniejszym błędzie enumeracji. `COMPLETE_FOR_SCOPE` wymaga kompletnego inventory oraz wymaganych odczytów wszystkich sześciu par; nie oznacza kompletnego modelu HMI ani `SYNCED`.

Stałe ograniczenia: 256 elementów na kompozycję ekranów/grup/items, 64 handlerów lub valid events, 2048 wyliczonych elementów łącznie, 8 poziomów grup, 512 znaków metadanych, locator wejściowy 1–16 segmentów. Maksymalnie 262144 jednostki UTF-16 na string kodu/GDA i 1048576 łącznie. Limity są stałe, klient nie może ich wybrać. Dopuszczalne jest jedno dodatkowe `MoveNext`, aby odróżnić dokładny limit od nadmiaru; nadmiarowy element nie jest odczytywany przez `Current`. Iteratory są zwalniane także po błędzie/limicie. Przekroczony tekst nie jest obcinany w pozornie poprawny skrypt — wartość/hash są nieobecne i zapisany jest `TEXT_LIMIT`.

Stringi pozostają dokładnie takie, jakie zwróci getter; brak normalizacji Unicode, CRLF/LF, importów, komentarzy lub markerów. Hash jest SHA-256 bajtów `Encoding.UTF8.GetBytes` bez BOM i bez zmiany EOL. Nie powstaje hipotetyczny natywny eksport eventów. Wiadomości/stack trace wyjątków nie są kopiowane do wyniku; zapisywana jest faza, typ błędu i jawne statusy, bez treści skryptów w zwykłych logach.

Granice ilościowe nie przerywają wiszącego pojedynczego getter/MoveNext. Reader nie dodaje timeoutu, retry ani nowego watchdog/schedulera. W trybie komponentu zbiera niezależne błędy i może kontynuować ograniczony odczyt; nie realizuje jeszcze natychmiastowego STOP całego przyszłego eksperymentu. Integracja live musi osobno zapewnić tę politykę, identity/IsModified, Dispose/quiescence i zachowanie historycznych wyników, bez automatycznego retry.

## Walidacja offline

Testy [typed traversal](../tests/integration/TiaBridge.Host.Tests/HmiEventDiagnosticReaderTests.cs) kompilują dokładny plik readera z [syntetycznymi typami](../tests/integration/TiaBridge.Host.Tests/HmiEventSyntheticVendorApi.cs), bez vendor reference i bez rzeczywistych wywołań Siemens. Obejmują poprawną enumerację, sześć par, ekran w grupie, raw code/GDA/Async, pusty/brakujący handler, brak obiektu/ekranu, błędy getterów i każdego etapu iteratora, null, partial ancestry, unsupported/duplikaty, per-collection/global/depth/text limits, Unicode/EOL i brak odczytu skryptów spoza zakresu. Testy [dispatch i IL](../tests/integration/TiaBridge.Host.Tests/HmiEventDiagnosticDispatchTests.cs) sprawdzają odrzucenie nieznanych metod i prób dodania diagnostyki do params `project.inspect`, brak admission/outbox oraz dozwolone wyłącznie vendor gettery, `GetEnumerator` i `GetValidEnumValues`.

Zwykły test IL korzysta z linked synthetic compilation i nie wymaga instalacji Siemens ani zbudowanego Workera. Opcjonalna zmienna **wyłącznie testowa** `TIA_BRIDGE_HMI_READER_BINARY` wskazuje osobno zbudowany net48 EXE do odczytu PE metadata/IL. Ten tryb dodatkowo sprawdza brak jakiegokolwiek callera readera poza jego implementacją. Nie ładuje ani nie uruchamia EXE/vendor DLL; produkcyjny kod nie czyta tej zmiennej.

Polecenia odtwarzające (wyniki kierować do nowego unikalnego katalogu):

```powershell
dotnet build src/TiaBridge.Worker.V19/TiaBridge.Worker.V19.csproj -c Release --no-restore
$env:TIA_BRIDGE_HMI_READER_BINARY = (Resolve-Path 'src/TiaBridge.Worker.V19/bin/Release/net48/TiaBridge.Worker.V19.exe').Path
dotnet test tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~HmiEventDiagnostic|FullyQualifiedName~V19OfflineTests"
py -3 scripts/audit_quality.py
git diff --check
```

Build wymaga lokalnego publicznego V19 API i istniejącego cache locked dependencies; nie wymaga TIA Portal. Standardowe testy bez zmiennej PE korzystają wyłącznie z .NET i syntetycznych typów. Kompilacja C# nie jest operacją Siemens Compile.

Wyniki finalnego zakresu:

| Kontrola | Wynik |
|---|---|
| Worker V19 Release net48/x64 | PASS; 0 warnings, 0 errors |
| Typed traversal (Debug, syntetyczne API) | 50/50 PASS |
| Projekcja | 11/11 PASS |
| Default-deny HTTP i IL linked readera oraz finalnego Workera Release | 2/2 PASS |
| Regresja `V19OfflineTests` | 47/47 PASS |
| Końcowy połączony przebieg ukierunkowany | 110/110 PASS; 0 failed, 0 skipped |
| Szersza regresja Host Release, wcześniejsza kompilacja opisana niżej | 469/469 PASS; 0 failed, 0 skipped |
| Format obu rozwiązań, wskazane pliki C# | PASS (`--verify-no-changes`) |
| `audit_quality.py` / kontrola lokalnych linków / diff check | PASS; 0 errors |

Finalny TRX: `.tia-bridge/hmi-event-reader/0e3aa1fb92734438900689624af793d6/hmi-event-reader-final.trx`. SHA-256 Workera Release poddanego kontroli IL: `ead52f9261b1d94351ca05f472377ff4e9a8160d0e1dca5f612ab0c9e73d1c58`.

Szerszy TRX: `.tia-bridge/hmi-event-reader/d1a25682f170403b9e6554d50ab60f2f/host-offline.trx`, 22 min 52 s. Ten przebieg uruchomiono przed końcowym doprecyzowaniem raportowania vendor `EventType`/unknown enum oraz dodaniem trzech testów projekcji; tych zmian dotyczy końcowe 110/110 na finalnych źródłach. Nie przedstawiamy 469/469 jako pełnego przebiegu późniejszego zestawu 472 testów. Szersza regresja obejmuje istniejące scenariusze awarii/recovery, 1001 operacji Fake i idle NF-08: 306,433 s, CPU 0,829224%, PASS. Jest to wykonanie testów, bez nowej kwalifikacji/enrollment i bez nadpisania historycznych dowodów; NF-11 ≥8 h pozostaje NOT_RUN. Surowe syntetyczne dowody tego przebiegu: `.tia-bridge/resilience/ae87baf9bc0546629eab73b744c9405d/`.

Zachowano wcześniejszy nieudany przebieg `.tia-bridge/hmi-event-reader/838184c6eee941abbdb9432abf65c3f6/hmi-event-reader.trx`: 106/107 PASS, 1 FAIL w nowym walidatorze IL. Walidator rozpoznawał vendor members tylko jako metadata `MemberReference`, pomijając syntetyczne `MethodDefinition` z tej samej assembly. Poprawiono rozpoznawanie, nie regułę read-only; kolejny przebieg `075d7acfde5f433390f5018bf8c57bd6` przeszedł 107/107. Końcowe 110 testów obejmuje jeszcze doprecyzowanie braku sztucznego vendor `EventType` dla absence, zachowania nieznanych enumów i trzy dodatkowe testy projekcji. Pierwsza kompilacja nowych testów wymagała poprawienia błędnej przestrzeni nazw typu Host w teście izolacji.

Próba równoległej kompilacji testów Release trafiła na blokadę DLL przez trwający pełny przebieg Host (`MSB3027/MSB3021`); testów z tej próby nie uruchomiono. Nie przerwano właściciela pliku ani nie usuwano outputów. Finalny zestaw uruchomiono w osobnej konfiguracji Debug, z kontrolą IL finalnego Workera Release. Nie jest to retry operacji Siemens. Żaden test offline nie podnosi statusów Engineering, Runtime, NF-11 ani kwalifikacji produkcyjnej.

## Pliki i ocena wpływu

Pliki dodane lub zmienione w tym zadaniu:

- `src/TiaBridge.Worker.V19/HmiEventDiagnosticReader.cs` — nowy typed traversal i wewnętrzny wynik/inventory.
- `src/TiaBridge.Worker.V19/HmiEventDiagnosticProjection.cs` — niezależne błędy getterów i znany event kind bez sztucznego vendor `EventType` dla absence.
- `tests/integration/TiaBridge.Host.Tests/HmiEventSyntheticVendorApi.cs` — nowe compile-time API doubles.
- `tests/integration/TiaBridge.Host.Tests/HmiEventDiagnosticReaderTests.cs` — nowe testy traversal i awarii.
- `tests/integration/TiaBridge.Host.Tests/HmiEventDiagnosticDispatchTests.cs` — nowe testy default-deny i IL.
- `tests/integration/TiaBridge.Host.Tests/HmiEventDiagnosticProjectionTests.cs` — nowe testy niezależnych błędów/absence.
- `tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj` — linked source readera, bez Siemens reference.
- `docs/HMI_EVENT_READER_OFFLINE.md` — nowy raport, wewnętrzna semantyka i polecenia walidacji.
- [docs/HMI_EVENT_EXPERIMENT_READINESS.md](HMI_EVENT_EXPERIMENT_READINESS.md) — bieżący status i pozostała integracja przed live.
- [src/TiaBridge.Worker.V19/README.md](../src/TiaBridge.Worker.V19/README.md) — opis wewnętrznego komponentu.
- [Zmiany.md](../Zmiany.md) — zakres, walidacja i ocena wpływu.

Zastano już niezatwierdzone przygotowanie projekcji, jej testów i raportu gotowości, zmianę projektu testowego/`Zmiany.md` oraz raport analizy. Zachowano te materiały; [HMI_EVENT_SCRIPT_EXPORT_ANALYSIS.md](HMI_EVENT_SCRIPT_EXPORT_ANALYSIS.md) pozostaje bez zmian. Index Git i inne worktrees pozostają bez zmian.

Ocena sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git i dane/kontrakty; Instrukcja_ChatGPT — sekcje 1–10. Obie instrukcje pozostają bez zmian: wewnętrzny komponent realizuje zatwierdzone zasady typowanego, bounded, read-only odczytu i nie zmienia workflow/uprawnień. ADR 0003, architektura 03, synchronizacja 04, interfejs 05, integracja 06, strategia 07, backlog 08, specyfikacje E1/B0 i publiczne kontrakty pozostają bez zmian; nie ustanowiono nowego formatu CANONICAL/SOURCE/NATIVE, capability, kryterium kwalifikacji lub dowodu `SYNCED`. Bieżące syntetyczne kryteria tego pakietu i ograniczenia zapisano w niniejszym raporcie.

## Rekomendacja przygotowania Engineering

Przygotować osobne zadanie z przeglądem minimalnego podłączenia do obecnego `VendorReader` i ownera, testami identity/IsModified/STOP/Dispose oraz zatwierdzonym testowym raportem/artifactem. Brak rozszerzenia Host/IPC/capability w tym pakiecie jest celowy; nie uruchamiać funkcji przez zastępczy proces, reflection dispatch lub obejście autoryzacji.

Przed pierwszym eksperymentem potrzebne są świeże build hashes, kwalifikowane wejściowe gate evidence, zatwierdzona ścieżka uprawnienia/E1 dla dokładnego nowego zakresu, izolowana i czysta kopia projektu, exact PID/start/SID/AP19/device binding oraz osobna zgoda operatora na pojedynczy odczyt. Zweryfikować rzeczywiste typy wymaganych obiektów, dostępność `RowSelected`, semantykę absent/empty i zachowanie surowego tekstu na kontrolowanych próbkach. Plan i warunki STOP pozostają w [dokumencie gotowości](HMI_EVENT_EXPERIMENT_READINESS.md). Bieżące zadanie kończy się raportem offline, bez live, enrollment lub produkcyjnego wdrożenia.
