# Gotowość pierwszego eksperymentu odczytu eventów HMI V19

Data: 2026-10-29
Baza kodu: `088e72303be824eb08b8611269a4c0d87e15f9a0` (`main`, zgodny z `origin/main` przed tą zmianą).
Zakres: przygotowanie planu oraz późniejsza implementacja readera i kontrola offline. Nie wykonano Siemens Attach ani odczytu Engineering.

## Decyzja gotowości

**Status: READER IMPLEMENTED OFFLINE; LIVE: NOT_RUN; ENGINEERING INTEGRATION: NOT IMPLEMENTED.**

Po osobnym zleceniu implementacyjnym dodano wewnętrzny `HmiEventDiagnosticReader` w istniejącym `TiaBridge.Worker.V19`, reużywający `HmiEventDiagnosticProjection`. Typowany kod traversal jest kompilowany z PublicAPI V19 oraz testowany na syntetycznych typach bez Siemens DLL. Nie ma wywołania readera z `VendorReader`, Program, IPC ani Host dispatch, nowej capability lub ścieżki autoryzacji. Szczegóły, limity, wyniki i ograniczenia zawiera [raport implementacji offline](HMI_EVENT_READER_OFFLINE.md). Brakuje integracji sesyjnej Engineering, zatwierdzonego kontraktu raportu i świeżego E1 dla nowego zakresu/builda. Offline PASS nie kwalifikuje rzeczywistych getterów V19.

## 1. Stan wejściowy i dokumenty

- Stan przed zmianą dokumentacyjną: czysty index/worktree poza raportem analizy z poprzedniego zadania; `HEAD == origin/main`, ahead/behind `0/0`.
- Sprawdzono `AGENTS.md`, `Instrukcja_ChatGPT.md`, `README.md`, `Zmiany.md`, `docs/03_ARCHITECTURE.md`, `docs/06_TIA_INTEGRATION.md`, `docs/07_TEST_STRATEGY.md`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/decisions/0003-hmi-canonical-model-and-native-artifacts.md`, dokumentację X-01/B-03A/E1 oraz poprzedni `HMI_EVENT_SCRIPT_EXPORT_ANALYSIS.md`.
- Zgodnie z ADR 0003 prototyp używa wyłącznie publicznego, wersjonowanego Workera V19; nie tworzy drugiego adaptera, nie zmienia CANONICAL/SOURCE/NATIVE ani nie deklaruje `SYNCED`.
- Istniejący IPC 1.1 obsługuje ograniczone `project.inspect`; HMI event read nie ma publicznego DTO, metody operacji, capability key ani profilem zaakceptowanego scope.

## 2. Bramki i uprawnienia

| Bramka / dowód | Stan odczytany z repo | Znaczenie dla tego eksperymentu |
|---|---|---|
| B-02A–B-02D / Fake | `B-02 OFFLINE TEST EXECUTION: PASS`; D-11 raport dokumentuje wymagane RT-03/05/06/15/16 i syntetyczne recovery PASS. | Spełnia historyczną bramkę wejścia dla ograniczonego X-01. Nie kwalifikuje nowego HMI readera. |
| Ograniczona D-11 / NTFS | `X-01 LIMITED READ-ONLY GATE: PASS` w `tests/resilience/X01_LIMITED_GATE.md` dla wcześniejszego zakresu i buildów. | Zmiana Workera/scope wymaga ponownej oceny; wynik nie przenosi się automatycznie. |
| Pełna offline kwalifikacja | Ostatni opisany pełny profil ma FAIL (NF-08); NF-11 ≥8 h pozostaje NOT_RUN, R-14 aktywne. Historyczny NF-03 exFAT FAIL zachowany. | Nie jest PASS pełnej kwalifikacji ani produkcji. Nie wolno zmieniać progów ani przepisywać statusu. |
| Historyczne X-01 Engineering | Ograniczony wcześniejszy Attach/read/Dispose 93/93 PASS; `WORKER HANDLE RISK: UNKNOWN`; nie obejmuje HMI screen/event getters. | Dowód historyczny, nie kwalifikacja bieżącego builda readera ani sześciu zdarzeń. |
| E1/trust | B-03A wskazuje brak zarejestrowanego real Engineering evidence, trust anchors/config i świeżej kwalifikacji nowego zakresu. | Przed dispatch wymagane osobne, podpisane E1 dokładnie wiążące nowy build, metodę, profil, projekt, device i coverage. Offline test go nie tworzy. |
| Operator authorization | To zadanie wyraźnie zabrania rzeczywistego odczytu. | Potrzebna osobna decyzja użytkownika po przeglądzie tego raportu oraz osobne uprawnienie operatora do dokładnego live zakresu. |

Nie zmieniono capability registry, bindingów, trust anchorów, operacji Host ani uprawnień. Nie wykonano żadnego Attach.

## 3. Minimalny projekt readera diagnostycznego

`HmiEventDiagnosticProjection` pozostaje czystą projekcją; `HmiEventDiagnosticReader` implementuje typowany bounded traversal istniejącego `HmiSoftware`. Oba typy są wewnętrzne. Reader nie wykonuje Attach, nie wybiera projektu ani urządzenia i nie stanowi publicznego DTO. Przyszła integracja musi użyć istniejącego `VendorReader` i ownera po zatwierdzeniu testowego harness/operation scope. Poniższe warunki dotyczą tej przyszłej integracji; nie została wykonana w zadaniu offline. Nie tworzyć oddzielnego procesu, PowerShell adaptera, endpointu publicznego ani produkcyjnej capability.

1. Reużyj istniejącego dokładnego bindingu TIA process start/PID/SID/AP19, `TiaPortal.Attach`, jednego owner thread i lifecycle/dispose Workera.
2. Po sprawdzeniu `project.IsModified == false` odnajdź exact software HMI o oczekiwanej rodzinie Unified i device locator. W pierwszym przebiegu ogranicz się do jednego ekranu `T05_Recipes_Main` oraz jawnie wymienionych elementów: `ACTION_0`, `ACTION_1`, `RecipeTable_Main`, `NAV_1`.
3. Enumeruj ekrany, elementy i kompozycje zdarzeń przez publiczny model V19 z jawnie dozwolonymi typami/węzłami. Zbieraj `Name`, runtime type, ścieżkę locator, vendor `EventType`, `ScriptCode`, `GlobalDefinitionAreaScriptCode`, `Async` i błędy getterów. Nie wykonuj ogólnego, nieograniczonego reflection sweep.
4. Dla każdego węzła raportuj niezależnie `inventory_status`, `handler_status`, `code_status` i `error`. Brak handlera jest `ABSENT` tylko na istniejącym, jednoznacznym obiekcie po kompletnej enumeracji nadrzędnych kompozycji i handlerów, przy znanym dozwolonym event kind. Brak obiektu pozostawia handler `UNKNOWN`. Pusty `ScriptCode` jest `PRESENT_EMPTY` tylko jeśli handler obiekt został rzeczywiście wyliczony i getter zwrócił pusty string. Getter null, wyjątek, brak kompozycji lub niepełne coverage pozostają `UNKNOWN`/`READ_FAILED`.
5. Po odczycie ponownie sprawdź exact identity i `IsModified`; wynik do zaakceptowania wymaga `false` przed i po. Nie wywołuj Save, Import, Compile, Download, Runtime ani setterów. Zapisz wynik poza repo/Workspace przez create-new artifact.
6. Nie podejmuj automatycznego retry po timeoutie, late response lub utracie Workera. Zachowaj oryginalny wynik, nie uruchamiaj następnego odczytu do czasu potwierdzenia quiescence/reconciliation.

Testy syntetyczne obejmują obecnie ten sam kod typed traversal na atrapach vendor boundary, błędy getterów/iteratorów, limity, absence/empty/null, Unicode/EOL oraz default-deny Host. Nie udowadniają, że taka struktura i semantyka są zwracane przez rzeczywiste Openness. Projection DTO nie może wejść do IPC/Host/capability bez osobnej decyzji kontraktowej.

## 4. Historyczne testy przygotowania projekcji

Uruchomiono istniejące, offline testy V19/Host:

```powershell
dotnet test tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~V19OfflineTests|FullyQualifiedName~HmiEventDiagnosticProjectionTests"
```

**Wyniki: PASS — 47/47 istniejących `V19OfflineTests` oraz 8/8 `HmiEventDiagnosticProjectionTests`; połączony przebieg: 55/55, 0 failed, 0 skipped.** Projection tests sprawdzają zachowanie kodu/GDA, hash UTF-8, event/object metadata, handler absent/present-empty/null/error, niepełne inventory, brak event type i zakaz fałszywego ABSENT po błędzie enumeracji. Zbudowano też `TiaBridge.Worker.V19` net48/x64 offline: PASS, 0 warning/error.

Ograniczenie historycznego wyniku 55/55: w tamtym przygotowaniu nie istniał HMI event reader ani fake typed vendor tree. Osiem testów sprawdzało wyłącznie projekcję, a 47 istniejących — regresję Host/Worker. Późniejsze wyniki implementacji znajdują się w [raporcie offline](HMI_EVENT_READER_OFFLINE.md). Żaden z tych testów nie wywołuje rzeczywistych getterów Openness ani nie potwierdza, że Siemens zwraca expected data/empty/absence w projekcie. Dla rzeczywistego odczytu status pozostaje `NOT_RUN`/`UNKNOWN`.

## 5. Plan pojedynczego odczytu Engineering

**Obiekt testu:** izolowana, zweryfikowana kopia projektu `T05_RecipeSim_V19` (nie źródłowy projekt roboczy). Kopię, AP19 path/hash, stan czysty, build TIA/HSP i dowód izolacji dostarcza/akceptuje operator; nie wywnioskowano jej istnienia ani stanu z plików w zadaniu.

**Zakres jednego operation ID:** jeden Attach do dokładnie zbindowanej kopii; jeden device HMI `HMI_JSON_TEST / HMI_RT_1`; ekran `T05_Recipes_Main`; sześć oczekiwanych locator/event par z analizy:

| Obiekt | Event |
|---|---|
| `T05_Recipes_Main` | `Loaded` |
| `T05_Recipes_Main` | `Unloaded` |
| `ACTION_0` | `Tapped` |
| `ACTION_1` | `Tapped` |
| `RecipeTable_Main` | `RowSelected` |
| `NAV_1` | `Up` |

Każda enumeracja nadrzędnej listy/węzła w tym zamkniętym zakresie musi jawnie zapisać complete/partial/error. Jeśli tabela jest widgetem z inną hierarchią/handler API albo jakikolwiek getter/kompozycja nie jest wspierana, nie rozszerzać zakresu ad hoc — zakończyć `PARTIAL` i pozostawić nieodczytane pozycje `UNKNOWN`.

Przed i po Attach odczytać (wyłącznie przez istniejący worker i dozwolone publiczne gettery) project key/path/name, PID/start/SID, TIA build, worker hash, device identity oraz `IsModified`. Wynik pojedynczego odczytu zachować w unikalnym immutable artifact, bez publikacji do Workspace. Operator powinien ręcznie sprawdzić zgodność wybranej izolowanej kopii, wskazanego procesu i dokładnych six locators przed uruchomieniem.

## 6. Format raportu eksperymentu

Raport z przyszłego live przebiegu powinien zawierać:

- `schema_version`, `operation_id`, observation ID, UTC start/end, wynik końcowy i niezmienne ścieżki artifactów.
- TIA version/build, worker version/build SHA-256, PublicAPI assembly identity/hash, profil/apartment oraz exact project key/name/AP19 hash, PID/start/SID.
- HMI device name/type/observed locator, screen/item name/type/observed locator i oryginalną wartość vendor `EventType`.
- `handler_status`, `code_status`, raw `ScriptCode`, raw `GlobalDefinitionAreaScriptCode`, `Async`, SHA-256 osobno dla code i GDA (deklarowana UTF-8 encoding policy), długości bajtowe oraz błędy getterów. Brak wartości nie może być reprezentowany jako pusty string.
- `coverage` dla ekranów, elementów i event handlers (`COMPLETE|PARTIAL|UNKNOWN`) z listą enumerowanych węzłów, limitów i przyczyn pominięcia.
- `is_modified_before/after`, status weryfikacji identity przed/po, Attach/Dispose/quiescence status, brak wywołań mutujących oraz liczba wywołań/efektów (oczekiwane zero).
- Podpisany manifest/artifact hashes z istniejących mechanizmów. Treść skryptów nie powinna trafiać do zwykłych logów diagnostycznych.

Format na tym etapie jest raportem diagnostycznym candidate, nie API v1, canonical model ani format Workspace zatwierdzony przez ADR 0003.

## 7. Kryteria oceny

| Kryterium | PASS dla ograniczonego eksperymentu | FAIL | UNKNOWN / PARTIAL |
|---|---|---|---|
| Odczyt wartości | Każdy handler obecny zwraca dokładne, niepuste/empty stringi ScriptCode i GDA; hash powtarza się z zapisanego UTF-8 payloadu; znana próbka zgadza się z UI/kopią. | Błędna wartość/hash, identity drift, `IsModified` zmienia się, TIA state jest dotknięty. | Getter null/wyjątek, API value nieporównywalne, zakres odczytany tylko częściowo. |
| Locator/type/event | Każdy odczytany rekord wiąże się z dokładnym screen/item locator, object type i event name/type, bez kolizji w bieżącym inventory. | Błędny/dwuznaczny locator lub event przypisany do innego obiektu. | Brak stabilnego identity poza nazwą/ścieżką w tym observation albo nierozpoznana enum wartość. |
| Absence vs empty | Brak handlera wyłącznie po kompletnym inventory kompozycji; pusty tylko po wyliczeniu handlera i skutecznym pustym string getterze. Wynik potwierdzony na izolowanej kontrolowanej kopii. | Reader zamienia wyjątek/partial/null w ABSENT lub EMPTY. | API/reprezentacja nie pozwala udowodnić którejś ze ścieżek; raportuje UNKNOWN. |
| Read-only | Exact identity, `IsModified=False` przed i po, zero setter/import/save/compile/download/runtime, prawidłowy Dispose/quiescence i artefakt. | Jakikolwiek write/dirty-state drift, błędny target, utracona kontrola nad call. | Timeout, hang, niepotwierdzone quiescence/Dispose lub brak telemetry. Bez retry. |
| Coverage | Pełna enumeracja tylko jawnego pierwszego scope, nie całego HMI. | Brak z wymaganych obiektów pomimo dowiedzionej kompletności i niezgodności ze znanym baseline w izolowanej kopii. | Nieobsługiwany typ/kompozycja/limity: no MISSING conclusion. |

PASS dotyczy wyłącznie dokładnego tuple: V19 build + Worker hash + Unified device/build + object/event kinds + operation/scope + coverage. Nie oznacza kompletnego eksportu HMI, round-trip, produkcyjnej gotowości, mutacji ani `SYNCED`.

## 8. Uprawnienia i warunki STOP

Przed live wymagane będą: (1) osobna jawna zgoda użytkownika na dokładnie jeden read-only eksperyment, (2) zatwierdzona i zweryfikowana izolowana kopia i dowód odbiorcy/projektu, (3) aktualne wejściowe gate evidence dla bieżącego kodu/NTFS, (4) osobno zatwierdzony E1 dla `HMI_EVENT_DIAGNOSTIC_READ`/exact worker build/project/device/object-event scope/coverage/okno oraz zaufany enrollment przez uprawnionego operatora, (5) nowy exact PID/start/SID/AP19 binding i świeży preflight. Dawne X-01 binding/grant, B1 20-cycle grant, D-11 PASS, LAB-03J i offline PASS nie nadają tego uprawnienia. Zgoda na tę analizę nie jest zgodą na Attach.

**Natychmiast STOP, bez automatycznego retry/Attach:** project/process/HMI identity mismatch; source rather than approved copy; `IsModified=True` na wejściu; zmiana `IsModified`; nieoczekiwany dirty state; niepełne/ambiguous identity; nieznana enum/composition; limit przekroczony; wyjątek getter/API; timeout/hang; brak Dispose/quiescence; projekt lub TIA nieoczekiwanie zamknięty; otrzymany inny HMI family/build; jakikolwiek sygnał próby setter/write. Zachować istniejące artefakty/logi i zgłosić `FAIL`, `UNKNOWN` lub `PARTIAL`; timeout nie dowodzi zatrzymania wywołania.

## 9. Otwarte kwestie

1. Czy V19 pozwala odróżnić handler nieistniejący od istniejącego pustego handlera dla każdej z sześciu par obiekt/event?
2. Czy `RowSelected` na docelowym typie RecipeTable jest dostępny tak samo jak typowane event handlers standardowych screen items?
3. Czy `ScriptCode` i global declaration getter zwracają dokładne wszystkie znaki, kodowanie/line ending i importy bez ukrytych zależności?
4. Czy ekran i wszystkie wymagane elementy mają kompletne, bezpieczne locatory/typy oraz jakie inne UI object classes znajdują się w ich poddrzewach?
5. Jakie dokładne buildy V19/HSP/TIA i Worker będą związane z E1 oraz czy lokalny trust enrollment jest dostępny/aktualny?
6. Jaki testowy HMI w izolowanej kopii jawnie reprezentuje event nieobecny i event obecny z pustym kodem bez mutowania chronionego źródła? Jeśli kopia nie zawiera obu stanów, eksperyment rozstrzygnie tylko odczyt kodu, nie absence semantics.
7. Brak zatwierdzonego DTO/IPC/capability i kontraktu raportu: przed produkcyjnym eksportem wymagane osobne zatwierdzenie formatu zgodne z ADR 0003 oraz pakiet implementacyjny/testowy.

## Rekomendacja następnego pojedynczego kroku

Implementacja offline została wykonana osobnym zleceniem, bez rejestracji live operation/capability. Następnie przygotować do osobnego przeglądu minimalną integrację testową w istniejącym lifecycle `VendorReader`: exact project/device binding, owner, identity/IsModified przed i po, przerwanie dalszego traversal według warunków STOP, immutable artifact oraz zatwierdzone bramki/E1. Obecny komponent zbiera niezależne błędy i może kontynuować ograniczone odczyty diagnostyczne; nie jest jeszcze harnessem realizującym natychmiastowy STOP eksperymentu. Nie uruchamiać go live bez tej integracji i osobnego uprawnienia operatora. Dopiero po ich zatwierdzeniu rozważyć jeden odczyt Engineering z sekcji 5.

## Dowody

- Aktualny stan architektury/API: `src/TiaBridge.Worker.V19/VendorReader.cs`, `src/TiaBridge.Worker.V19/README.md`, `contracts/ipc/v1.1/message.schema.json`.
- Wejście i limity: `docs/03_ARCHITECTURE.md`, `docs/06_TIA_INTEGRATION.md`, `docs/07_TEST_STRATEGY.md`, `docs/08_IMPLEMENTATION_PLAN.md`.
- ADR: `docs/decisions/0003-hmi-canonical-model-and-native-artifacts.md`.
- Bramka ograniczona: `tests/resilience/X01_LIMITED_GATE.md`; pełna kwalifikacja i historyczny X-01: `docs/X01_TIA_V19_QUALIFICATION.md`, `docs/B03A_DISCOVERY_QUALIFICATION.md`.
- Historyczne testy przygotowania: 47/47 `V19OfflineTests` oraz 8/8 `HmiEventDiagnosticProjectionTests`, 55/55 combined PASS; Worker net48 build PASS, zero warnings/errors. Bieżące wyniki typed readera: [raport implementacji](HMI_EVENT_READER_OFFLINE.md). Testy są offline i nie wywołują rzeczywistych getterów Siemens.
- Wcześniejsze API metadata i historyczne event handler code paths: `docs/HMI_EVENT_SCRIPT_EXPORT_ANALYSIS.md`; nie są nowym Engineering evidence.
