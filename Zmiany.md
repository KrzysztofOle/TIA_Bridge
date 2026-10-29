# Zmiany — TIA_Bridge 2

## 2026-10-29 — compare-events: naprawa INCOMPLETE_LOGICAL_IDENTITY

Odtworzono błąd na dwóch wskazanych rzeczywistych eksportach: 422 obiekty UNKNOWN i 96 handlerów UNKNOWN. Przyczyna: LAB-03K zapisuje identity.device_path jako tablicę segmentów, a porównywarka wymagała stringa, więc brak klucza SOFTWARE propagował się na cały model. Dodano binding segmentowy z zachowaniem granic nazw/kodowania i zgodności wcześniejszego stringa; korzeń locatora musi odpowiadać urządzeniu. Walidacja znanych krawędzi/rodziców obsługuje bezpośrednie Screens, grupy/podgrupy, nesting i EventHandlers z dokładnym owner. Klucze nadal oparte na nazwach/hierarchii, bez fallbacku po indeksie lub pełnym locatorze. Duplikaty, niejednoznaczność, braki i PARTIAL zachowują dotychczasową semantykę. Markdown agreguje przyczyny UNKNOWN/PARTIAL i reprezentatywne przykłady; wszystkie entries/full locators pozostają w JSON, z dodatkowymi matched_counts, diagnostic_summary i identity_issue. [Dokumentacja, przyczyna i dowody](docs/LAB_EVENT_COMPARISON.md).

Finalne porównanie A/B: dopasowano 211 obiektów i 48 handlerów; obiekty UNCHANGED 208, MODIFIED 0, UNKNOWN 0, PARTIAL 3; handlery UNCHANGED 48, MODIFIED/UNKNOWN/PARTIAL 0; ADDED/REMOVED 0. Globalny PARTIAL i exit 2 zachowane z powodu trzech ograniczeń kontenerów. Niezależne porównanie surowego ScriptCode/GDA/async potwierdziło brak różnic, bez wymuszenia UNCHANGED. Testy porównywarki 44/44 (30 istniejących + 14 nowych); regresje LAB-03K/L/M/I/J/H odpowiednio 42/16/14/18/15/10 — 159/159 PASS. Sprawdzono nowy manifest, wszystkie pliki raportu i hashe wejść. Wyniki oraz odtworzenie błędu zachowano w osobnych katalogach .tia-bridge/compare-identity-fix-fde0af11b46c4198ae74169530190e66. Bez edycji historycznych eksportów/events.json lub LAB-03K/L/M, Siemens Openness, importu/synchronizacji, commita i push. Wcześniejsze niecommitowane zmiany pozostają zachowane.

Ocena sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git, dane/kontrakty; Instrukcja_ChatGPT — 1 cel, 2 odpowiedzialność, 3 źródła, 4 architektura, 5 zadania, 6 wyniki, 7 bezpieczeństwo/recovery, 8 decyzje/spójność, 9 GitHub/dokumentacja, 10 styl. Obie instrukcje bez zmian: naprawa rekonstrukcji istniejącego formatu offline nie nadaje praw TIA ani nowego workflow synchronizacji. ADR 0001–0003, specyfikacje 03–08/12 i kontrakty produkcyjne bez zmian: nie utworzono trwałego ObjectId, CANONICAL, capability lub SYNCED. Lokalny kontrakt raportu, parser locatorów i nowe kryteria regresji opisano w dokumentacji porównywarki; semantyka statusów i exit codes bez zmian. Kontrola dokumentacji/odnośników i diffu po zmianach.

## 2026-10-29 — LAB-03M: czytelna diagnostyka konsoli EXPORT EVENTS

Dodano niezależny od Siemens LabEventConsole: projekt/HMI, oryginalny status, liczniki ekranów/elementów/handlerów/obserwacji/WARN/ERROR/nieklasyfikowanych i katalog wyniku. Każda obserwacja zachowuje kod i kolejność; ENCAPSULATED_CHILDREN_NOT_PUBLIC jest WARN, znane nieudane odczyty/kontrole ERROR, nieznane kody WARN z classification=UNKNOWN. Liczba błędów nie zależy od PARTIAL. Nazwy z dokładnego inventory/hierarchii; brak lub niejednoznaczność daje pełny locator, bez zgadywania. Unicode i ścieżki bez obcinania, kontrolki terminala escapowane. Cztery zmiany prezentacji w laboratoryjnym vendor lifecycle, bez zmian Siemens calls, retry, publikacji, statusów lub return branches. Build uwzględnia nowy plik; dokumentacja repo/scripts/laboratorium i [raport LAB-03M](docs/LAB_03M_EVENT_CONSOLE_DIAGNOSTICS.md) zaktualizowane.

OFFLINE: 14/14 nowych testów, LAB-03K 42/42, LAB-03L 16/16, compare-events 30/30 (wymagane 102/102); dodatkowo LAB-03I 18/18, LAB-03J 15/15 i LAB-03H 10/10 — 145/145 PASS. Kontrole CLI/launch PASS; niepełne CLI faktycznie kończy się exit 1 bez Attach, gałęzie exit 0/2 i lifecycle zachowane poza czterema zmianami konsoli. Dziewięć artefaktów bazowego/nowego syntetycznego eksportu ma identyczne bajty, w tym events.json i events-index.json; manifest różni wyłącznie tool_sha256. Reader/publisher/widok, FORCE/compare-events i launch bez zmian. Historycznych dowodów nie nadpisano; pierwszy FAIL dodatkowego guardu źródła (kodowanie literalnego polskiego tekstu przez Windows PowerShell 5.1) zachowano i poprawiono guard ASCII. Attach/Engineering/F5/Runtime NOT_RUN. Bez Import, Save, Compile, Download, zmian TIA/T05_plc, commita i push.

Ocena sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git, dane/kontrakty; Instrukcja_ChatGPT — 1 cel, 2 odpowiedzialność, 3 źródła, 4 architektura, 5 zadania, 6 wyniki, 7 bezpieczeństwo/recovery, 8 decyzje/spójność, 9 GitHub/dokumentacja, 10 styl. Obie instrukcje bez zmian: prezentacja istniejących wyników nie nadaje uprawnień ani nie zmienia workflow lub kwalifikacji. ADR 0001–0003, specyfikacje 03–08/12, produkcyjne kontrakty i schematy LAB-03K/L bez zmian: nowa mapa severity jest wyłącznie lokalną polityką konsoli, nie nowym DTO/CANONICAL/capability/SYNCED. Kryteria testów i ograniczenia mapy zapisano w LAB-03M; odnośnik LAB-03L wskazuje nowy etap z zachowaniem historycznego raportu. Kontrola jakości dokumentacji/odnośników i diffu po zmianach.

## 2026-10-29 — LAB-03L: czytelny widok zdarzeń HMI, offline

export-events laboratoryjnego LabExporter tworzy pochodne screens/, osobne dokładne pliki JS/GDA, README i indeks lab-03l-events-index/1.0 z już odczytanych danych. Zachowane teksty/statusy; EMPTY ma zero bajtów, błąd odczytu nie tworzy pliku. Nazwy uwzględniają grupy/nesting, Windows, case/NFC/sanitization/truncation collisions, hash pełnego locatora i limity ścieżek. Linki względne i kodowane. Nowe pliki objęto wspólnym limitem, manifestem/hashami i weryfikacją; awaria widoku zachowuje marker. events.json nadal lab-03k-events/1.0, compare-events/FORCE/vendor lifecycle/launch bez zmian. Historyczne eksporty nietknięte; render-events wymaga osobnego bounded readera i pozostaje rekomendacją.

[Raport LAB-03L](docs/LAB_03L_READABLE_EVENT_EXPORT.md): build PASS, nowe testy 16/16, LAB-03K 42/42, LAB-03I 18/18, LAB-03J 15/15, LAB-03H 10/10 i compare-events 30/30 — razem 131/131 PASS; dodatkowo CLI/launch 2/2 i identyczne bajty 9 oryginalnych artefaktów bazowego/nowego eksportera. Porównanie syntetycznych JSON: 7 obiektów UNCHANGED, 3 handlery UNCHANGED i 1 PARTIAL, bez pozornych zmian. Wcześniejsze nieudane testy zachowano w evidence. Engineering/Attach/F5/Runtime NOT_RUN; bez Import, Save, Compile, Download i zmian projektów TIA/T05_plc, commita i push.

Przegląd sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git, dane/kontrakty; Instrukcja_ChatGPT — 1 cel, 2 odpowiedzialność, 3 źródła, 4 architektura, 5 zadania, 6 wyniki, 7 bezpieczeństwo/recovery, 8 decyzje/spójność, 9 GitHub/dokumentacja, 10 styl. Obie instrukcje bez zmian: reprezentacja realizuje jawnie zlecone laboratorium i nie poszerza praw TIA, workflow Apply ani kwalifikacji. Oceniono ADR 0001–0003, specyfikacje 03–08/12, LAB-03K, porównywarkę i kontrakty produkcyjne; bez zmian poza odnośnikiem LAB-03K, ponieważ nie ustanowiono CANONICAL HMI, natywnej wymiany ekranów, DTO/IPC, capability lub nowej definicji SYNCED. Lokalny kontrakt i kryteria testów zapisano w LAB-03L. README repo/laboratorium/scripts zaktualizowano. Kontrola jakości dokumentacji/odnośników i diffu po zmianach.

## 2026-10-29 — porównywanie eksportów zdarzeń HMI, wyłącznie offline

Dodano `scripts/compare_events.py compare-events`, wrapper PowerShell i czwartą konfigurację VS Code **TIA Bridge: COMPARE EVENTS — Export ↔ Export**. Porównanie LAB-03K używa hierarchii nazw zamiast indeksów; niejednoznaczne nazwy/tożsamość pozostają UNKNOWN. Dokładne ScriptCode/GDA/Async/typy/statusy, lokalne dowodzenie braku przy PARTIAL, inventory ekranów/elementów, niepowtarzalne raporty JSON/Markdown/diffy/manifest i marker nieukończonej publikacji. Nie wykonuje JS ani Siemens. Trzy istniejące konfiguracje pozostają identyczne. [Kontrakt, użycie i dowody](docs/LAB_EVENT_COMPARISON.md).

Testy porównywarki 30/30 PASS, regresje LAB-03K 42/42, LAB-03I 18/18, LAB-03J 15/15 i LAB-03H 10/10 PASS — łącznie 115/115 offline; dodatkowo wrapper CLI z whitespace/apostrofem/Unicode PASS i istniejące dwie kontrole CLI/launch LAB-03K PASS. Przykład syntetyczny zawiera wszystkie sześć statusów, po jednym handlerze; artefakty i dowody w nowym ignorowanym `.tia-bridge/compare-events-9a1d7ad622dc4fba8aead35673a86d49/`. GUI F5, Engineering i Runtime NOT_RUN. Bez operacji TIA, commita/push; poza repo powstały wyłącznie nowe syntetyczne dowody istniejących regresji.

Ocena wpływu sekcja po sekcji: AGENTS — zakres/stan, role/hierarchia, niezmienne granice, autoryzacja/testy, dyscyplina, Git, dane i kontrakty; Instrukcja_ChatGPT — 1 cel, 2 odpowiedzialność, 3 źródła, 4 architektura, 5 zadanie, 6 wyniki, 7 bezpieczeństwo/recovery, 8 decyzje/spójność, 9 GitHub/dokumentacja, 10 styl. Obie instrukcje pozostają bez zmian: nowy workflow jest samodzielnym narzędziem offline w jawnie zleconym zakresie, z dokumentacją zależności i testów; nie zmienia autoryzacji ani definicji SYNCED. Oceniono ADR 0001–0003, specyfikacje 03–08/12, LAB-03K i kontrakty produkcyjne: bez zmian poza odnośnikiem LAB-03K, ponieważ porównywarka czyta istniejący format laboratoryjny i tworzy wyłącznie osobny raport. Nie tworzy CANONICAL HMI, adaptera produkcyjnego, synchronizacji ani nowych praw. Nowy lokalny kontrakt raportu i kryteria offline zapisano w dokumentacji porównywarki. README i scripts/README zaktualizowano; historia istniejących raportów pozostaje zachowana.

## 2026-10-29 — LAB-03K: laboratoryjny eksport zdarzeń Unified, offline

Dodano ręczne, niezależne `LabExporter.exe export-events` w `X:\TESTY_TIA_Bridge`: wszystkie publiczne ekrany/grupy, elementy i skonfigurowane EventHandlers poprzez typowaną allowlistę 39 typów V19; code/GDA/Async, inventory, błędy, limity i coverage. Osobne niepowtarzalne wyniki poza repo i `Scripts` zawierają JSON, raport, dokładne teksty UTF-8 i manifest długości/SHA-256. Tożsamość procesu/projektu/HMI i IsModified sprawdzane przed/po; brak mutacji, JS execution lub retry. Niedostępne wnętrza kontenerów mają UNKNOWN/PARTIAL; RowSelected UNKNOWN. Stare funkcje FORCE zachowane. Dodano konfigurację VS Code z Read-Host i bez zmian dotychczasowych wpisów, skrypty build/test oraz [kontrakt i raport LAB-03K](docs/LAB_03K_HMI_EVENT_EXPORT.md).

Przegląd sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git i dane/kontrakty; Instrukcja_ChatGPT — 1 cel, 2 odpowiedzialność, 3 źródła, 4 architektura, 5 zadania, 6 wyniki, 7 bezpieczeństwo/recovery, 8 decyzje/spójność, 9 GitHub/dokumentacja, 10 styl. Obie instrukcje bez zmian: samodzielne laboratorium zostało jawnie zlecone i zachowuje rozdzielenie kodu, uprawnień TIA i poziomów dowodów. ADR 0001–0003, specyfikacje 03–08/12, produkcyjny Host/Worker/IPC, E1/B0, registry i publiczne kontrakty bez zmian; nie ustanowiono produkcyjnej synchronizacji, native eksportu ekranów ani nowych uprawnień. Nowy lokalny kontrakt i kryteria offline zapisano w LAB-03K; oceniono zgodność z zasadami partial reads i niezmienionego tekstu ADR 0003.

Finalny build PASS, testy 42/42, regresje LAB-03I 18/18, LAB-03J 15/15 i LAB-03H 10/10 — łącznie 85/85 PASS, ponadto kontrola argumentów launch i odmowy niepełnego CLI 2/2 PASS. Audit dokumentacji/linków/schema i diff PASS. Engineering/Attach/Import/Save/Compile/Download oraz Runtime NOT_RUN. Źródła i wcześniejszy EXE laboratorium zachowano poza Git, testy i artefakty w nowych katalogach dowodowych. Nie modyfikowano projektów, źródłowego Scripts ani starego Bridge. Bez commita/push. Dokładne wyniki końcowe i hashe podaje raport LAB-03K.

## 2026-10-29 — implementacja offline diagnostycznego readera HMI V19

Dodano wewnętrzny `HmiEventDiagnosticReader` w istniejącym Workerze net48/x64: typed bounded traversal ekranów/grup/items/event handlers dla sześciu par na `T05_Recipes_Main`, z allowlistą `HmiButton` i custom widget/web containers oraz weryfikacją dynamicznych valid events. Reużyto projekcji, dodając niezależne błędy kodu/GDA i absence bez sztucznego odczytanego vendor `EventType`. Odczyt obejmuje code/GDA, `Async`, event/object metadata i locatory; null, getter/iterator/Dispose failures, partial ancestry, unsupported, duplikaty i limity nie stają się `ABSENT`. Unicode/EOL i hashe UTF-8 zachowane bez normalizacji. [Raport implementacji](docs/HMI_EVENT_READER_OFFLINE.md) opisuje zakres, semantykę, limity, pliki, dowody i ograniczenia.

Worker Release build PASS, 0 warnings/errors. Finalne testy offline Debug z kontrolą IL Workera Release: typed traversal 50/50, projekcja 11/11, Host default-deny/IL 2/2 i V19 regression 47/47 — łącznie 110/110 PASS, 0 failed/skipped. Szersza regresja Host Release 469/469 PASS na wcześniejszej kompilacji, przed końcowym doprecyzowaniem raportowania EventType i trzema dodatkowymi testami projekcji; końcowe zmiany sprawdzone w 110/110. Ten szerszy przebieg nie jest pełnym przebiegiem późniejszego zestawu 472 testów. Zachowano pierwotny FAIL walidatora IL, poprawkę rozpoznawania syntetycznych MethodDefinition oraz nieuruchomioną próbę Release zablokowaną DLL trwającej regresji; szczegóły i TRX w raporcie. Walidacja dokumentacji/linków, scoped format verify obu rozwiązań i diff check PASS, 0 errors.

Brak callera readera w produkcyjnym Workerze; `VendorReader`, Program, Host/IPC/Core, kontrakty i capability bez zmian. Komponent nie tworzy sesji i wymaga już zweryfikowanego software/locator na istniejącym ownerze. Nie podłączono go do dispatch, nie dodano grantu/ścieżki autoryzacji ani schedulera. Przed live pozostaje osobno zatwierdzona integracja obecnego lifecycle z identity/IsModified, STOP i artifactem oraz aktualne bramki/E1 i uprawnienie operatora. Nie wykonano Siemens Attach, rzeczywistego Engineering odczytu, Import, Save, Compile, Download, Runtime ani PLC write; bez wdrożenia, commita i push. Historyczna kwalifikacja i NF-11 nie zmieniają statusu.

Ocena sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git i dane/kontrakty; Instrukcja_ChatGPT — sekcje 1–10. Obie instrukcje pozostawiono bez zmian: ograniczony komponent realizuje istniejące granice, bez nowych uprawnień/workflow. ADR 0003, specyfikacje 03–08 i E1/B0 oraz publiczne kontrakty pozostają bez zmian: brak nowego canonical modelu, capability, kryterium kwalifikacji lub deklaracji `SYNCED`. Zaktualizowano raport gotowości i README Workera; wcześniejszy raport analizy oraz zastane przygotowanie zachowano, inne worktrees i index bez zmian.

## 2026-10-29 — przygotowanie offline diagnostyki HMI event reader

Dodano `HmiEventDiagnosticProjection` jako wewnętrzną, offline-only projekcję przyszłego odczytu HMI w Worker V19 oraz syntetyczne testy statusów/metadanych/hashy. Helper nie wykonuje Siemens API, nie jest podłączony do `VendorReader`, IPC, Host dispatch ani capability; nie ustanawia kontraktu publicznego. Raport [gotowości eksperymentu](docs/HMI_EVENT_EXPERIMENT_READINESS.md) określa bounded scope, plan izolowanej kopii, gate statuses, format raportu, STOP i kryteria oceny. Nie wykonano Attach/live Engineering; Import, Save, Compile, Download, Runtime i PLC nieuruchomione.

Walidacja offline: Worker net48/x64 build 0 warning/error; 47 istniejących `V19OfflineTests` + 8 `HmiEventDiagnosticProjectionTests` = 55/55 PASS; `audit_quality.py` PASS/0 errors; diff check PASS. Testy projekcji nie dowodzą zachowania getterów Openness. Brak nowego read operation, public DTO/IPC, capability, trust enrollment lub produkcyjnego eksportu. Pełna kwalifikacja zachowuje NF-08 FAIL/NF-11 NOT_RUN; bieżący live event read NOT_RUN.

Ocena sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina zmian, Git i dane/kontrakty; Instrukcja_ChatGPT — sekcje 1–10. Obie instrukcje pozostają bez zmian: nie zmieniono workflow, praw, progów ani test acceptance/gate; helper jest wyłącznie prywatną projekcją raportu syntetycznych danych. ADR 0003, specyfikacje, publiczne kontrakty, IPC i registry pozostają bez zmian; live scope nadal wymaga osobnej decyzji operatora i aktualnego exact E1. Nie wykonano commita ani push.

## 2026-10-29 — LAB-03J: diagnostyka natywnego eksportu i test importu

Laboratoryjny eksporter zapisuje osobne raporty stanu przed/po wywołaniu API, zwróconych plików, rzeczywistego drzewa, oczekiwanej pary, konkretnej odmowy i kodów błędów. Nie loguje treści JS/YAML. Pusty katalog jest warunkiem przed wywołaniem; po nim wymagane są dokładna para i zgodność ścieżek API/dysku, bez dodatkowych plików, podkatalogów, duplikatów lub reparse points. Awaria natywnego eksportu zachowuje surowe dane. Nazwa `_Global_definition_area.js`, marker Siemens, manifesty LAB-03I, splitter i publisher LAB-03H bez zmian. Uzupełniono laboratoryjny import o baseline hashes, świeży odczyt pod ExclusiveAccess, natywny backup z weryfikacją AP19 i odczytem/hashami zawartości, recheck frozen input oraz intent. Nie wdrażano produkcyjnego adaptera ani sagi Host.

[Raport LAB-03J](docs/LAB_03J_NATIVE_EXPORT_DIAGNOSTICS.md): 15/15 nowych testów, 18/18 LAB-03I i 10/10 LAB-03H PASS na finalnym buildzie. Pełny rzeczywisty eksport, kontrolowany Import/Save i readback PASS dla 6 modułów / 22 funkcji; końcowe `Scripts` mają 47 plików i są zgodne z readbackiem. Zmieniono tylko odwracalny komentarz w laboratoryjnym `T05MoveTest.RunTest`; 21 innych funkcji, 6 globalnych obszarów i 6 YAML bez zmian bajtowych. Projekt zapisany i czysty w readbacku. T05_PLC bez zmian; Compile, download, Runtime i PLC write NOT_RUN. Bez commita/push.

Historyczny błąd `NATIVE_FILE_COUNT_OR_LAYOUT_INVALID:JSON_Test_screen` nie został odtworzony. Brak dawnych surowych danych i połączony warunek błędu uniemożliwiają potwierdzenie konkretnej historycznej przyczyny; nie przedstawiamy wzmocnienia walidatora jako jej udowodnionej naprawy. Gotowość T05_PLC NO, wymaganie diagnozy pozostaje otwarte. Zachowano FAIL autoryzacji i pierwszej próby backupu: API Archive używa literalnej nazwy, dlatego poprawiono jawne `.zap19`; przed tym błędem nie było Import/Save. Wznowienie nastąpiło po potwierdzeniu końca poprzedniego eksportera z `import_started=false`.

Ocena instrukcji sekcja po sekcji: AGENTS — zakres/stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git, dane/kontrakty; Instrukcja_ChatGPT — 1 cel, 2 odpowiedzialność, 3 źródła, 4 architektura, 5 zadania, 6 wyniki, 7 bezpieczeństwo/recovery, 8 decyzje/spójność, 9 GitHub/dokumentacja, 10 styl. Oba dokumenty pozostawiono bez zmian: ograniczony grant użytkownika obejmował laboratoryjny import i Save, a dodane zabezpieczenia realizują istniejące wymagania backupu, świeżych preconditions i readbacku. Autoryzację Siemens/UAC wykonywał operator, bez zmiany zabezpieczeń przez agenta. ADR 0001–0003, specyfikacje 03–08/12, Core/HTTP/IPC i bramki kwalifikacyjne bez zmian; laboratoryjny sukces nie nadaje capability, SYNCED ani kwalifikacji produkcyjnej. Zaktualizowano właściwe instrukcje narzędzia, testy i raporty; kontrola jakości dokumentacji, linków i diffu po zmianach.

## 2026-10-28 — LAB-03I: lokalna nazwa definicji globalnych Unified

W laboratoryjnym `X:\TESTY_TIA_Bridge` wdrożono `_Global_definition_area.js`, manifesty LAB-03I i ścisłe odrzucenie starego formatu/obu nazw. Zmieniono splitter, walidator/publisher paczki i assembler FORCE IMPORT; stary helper rekonstrukcji używa teraz tego samego assemblera. Oryginalne bajty JS, markery Siemens, nazwy funkcji, katalogi modułów i układ `.native` pozostają zachowane. Mechanizm publikacji LAB-03H, operacje Siemens oraz konfiguracje VS Code bez zmian. Źródła i poprzedni EXE zachowano poza Git; nie modyfikowano `C:\RMA\t05_plc` i nie wykonano commit/push.

[Raport i lokalny kontrakt LAB-03I](docs/LAB_03I_GLOBAL_DEFINITION_NAMING.md): finalny build PASS; offline 18/18 i istniejąca regresja staging/rollback 10/10 PASS. Live FORCE EXPORT FAIL po czterech modułach na istniejącej kontroli `NATIVE_FILE_COUNT_OR_LAYOUT_INVALID:JSON_Test_screen`, przed splitterem/publikacją; Attach potwierdzony po ręcznej autoryzacji. Live Import/Save/readback NOT_RUN, bez zastępowania bieżącej obserwacji historycznym eksportem. Brak deklaracji gotowości T05_PLC lub produkcyjnej kwalifikacji.

Ocena sekcja po sekcji: AGENTS — zakres i stan, hierarchia, niezmienniki, autoryzacja/testy, dyscyplina, Git, dane/kontrakty; Instrukcja_ChatGPT — 1 cel, 2 role, 3 źródła, 4 architektura, 5 zadanie, 6 kontrola, 7 recovery, 8 decyzje, 9 Git, 10 styl. Obie instrukcje pozostawiono bez zmian: lokalna konwencja laboratoryjna nie zmienia ich zasad. Jawny zakres zadania uprawniał do testów laboratoryjnych i Computer Use; monit uprawnień zatwierdził operator. ADR 0001–0003, specyfikacje 03–08/12, kontrakty Core/HTTP/IPC i bramki kwalifikacji pozostają bez zmian: LAB-03I nie wdraża produkcyjnego adaptera lub nowej sagi. Docelowy przykład `GlobalDefinitions.js` w dokumencie 04 pozostaje przykładem odrębnego produkcyjnego modelu workspace; nowy format laboratoryjny opisuje wskazany raport. Zaktualizowano właściwą instrukcję laboratorium oraz README/testy/raport repo; kontrola jakości dokumentacji i diffu wykonana po zmianach.

## 2026-10-28 — LAB-03E: VS Code launch dla eksportera laboratoryjnego

Dodano `.vscode/launch.json` z konfiguracjami **TIA Bridge: FORCE EXPORT — TIA → Folder** i **TIA Bridge: FORCE IMPORT — Folder → TIA**. Obie uruchamiają istniejący `X:\TESTY_TIA_Bridge\bin\LabExporter.exe` przez typ `node-terminal`, z argumentami LAB-03C/03D, widocznym terminalem i katalogiem roboczym laboratorium. Nie uruchamiano rzeczywistego eksportu ani importu; F5 pozostaje NOT_RUN, aby test konfiguracji nie zmieniał danych laboratorium/TIA. Zaktualizowano README o wybór konfiguracji, F5 i edycję ścieżek.

`.vscode/` jest ignorowane przez `.gitignore`; sam `launch.json` dodano jawnie do indeksu Git (`git add -f`). Kontrola: składnia JSON, dwie nazwy i typy konfiguracji, ścieżka programu, operacje i argumenty projektu/katalogów oraz istnienie celów PASS. `py -3 scripts/audit_quality.py` i `git diff --check` wykonano po zmianach.

Ocena wpływu: zmiana dotyczy wyłącznie lokalnego launchera laboratoryjnego i instrukcji README. Nie zmienia workflow CODEX, architektury, synchronizacji, autoryzacji ani bezpieczeństwa PLC/HMI; `AGENTS.md`, `Instrukcja_ChatGPT.md`, ADR, specyfikacje i kontrakty pozostają bez zmian.

## 2026-10-28 — B-03A/E2-B1-I: izolacja IPC, heartbeat i finalizacji net48

Baza 2c5b142 na sprawdzonej gałęzi diagnostycznej, clean; origin/main=16916d4,
bez merge/rebase/cherry-pick. Nowy testowy InfrastructureProbe net48/x64 kompiluje
dokładny Program Workera ze stubem granicy vendor, bez Siemens assembly reference.
Nie zmienia produkcyjnego Main, heartbeat, IPC, schedulera, grantów lub limitów.
Normalny Host/Fake jest osobnym punktem porównania. Runner Python/std library
utrwala source/build hashes i wszystkie próbki poza Git.

Seria b076e39c: A baseline, B trace on/off, C normalny Host/Fake, D heartbeat on/off,
E sampler/audit; 30 s initial-idle, 20 slotów activity, 120 s post-idle, osobne procesy.
Δ względem initial: A −3, B +13/+23, C −1, D +17/+2, E −8/−8. Kontrole e9131998
oddzieliły timer/poll/send: post-idle −8/−13/+22. Osobny syntetyczny GC-worker
373→338, Gen2 0→2, PSS 5 zakończonych Thread i 20 Event mniej; 30 s później 348.
Kontrola e6f04a9e z inline pipe flush nadal +7, więc nie wprowadzono jej do produkcji.
[Raport](docs/B03A_E2_B1_IPC_HANDLE_INVESTIGATION.md) rozdziela fakty, PROBABLE
odroczonego cleanup net48 oraz brak dowodu stosów/pełnej przyczyny historycznego B1.

Windows PSS tylko self, metadata bez object names/memory/thread context/drivera.
Klasyfikacja PARTIAL; remote próba na własnym Fake w pierwszym smoke zakończyła
kontroler 0xC0000005. Zachowano błąd, zweryfikowano i zakończono osierocony własny
Fake-only Host; dziecko zwolniło się po utracie pipe. Bez dotknięcia TIA, eskalacji
lub retry remote PSS. Kolejny smoke 8/8 po odmowie remote. Naprawiono nowe helpery:
health URL, read-only SQLite Pooling=false/exclusive open oraz adnotację liczby
slotów flush-control (spec/receipt zawsze 1). Pierwsze błędy zachowane, bez wyboru
korzystnych próbek lub zmiany progów. Produkcyjny kod bez zmian.

Ocena instrukcji sekcja po sekcji: AGENTS zakres/hierarchia/niezmienniki/autoryzacja/
testy/dyscyplina/Git/dane oraz Instrukcja 1–10 cel/role/źródła/architektura/zadania/
kontrola/recovery/decyzje/Git/styl. Oba dokumenty pozostawiono bez zmian: nowy
testowy harness realizuje ich istniejące zasady offline i nie nadaje uprawnień.
ADR 0001–0003, specyfikacje 03–08/14/16/18–21 i publiczne Core/HTTP/IPC/E1/B0
bez zmian semantyki; nie dodano drugiego schedulera lub adaptera. Aktualizacje
README/testów/raportu są opisem diagnostyki. B1/R/D i oryginalne dowody zachowane;
ponownie zweryfikowano 1069 B1 i 243 B1-D pliki. Brak nowego live, E1 enrollment,
B2 i NF-11. Build Release obu rozwiązań PASS, 0 warnings/errors; Core 564/564,
końcowy Host 409/409, Python 71/71. Format verify obu rozwiązań, audit_quality
i git diff --check PASS. Pierwsza pełna
regresja: Core 564/564, Host 408/409; zachowano timeout wejścia syntetycznego
ownera (5 s). Test ma teraz dedykowany LongRunning owner, asynchroniczny sygnał
i await cleanup w finally; bez zmiany budżetu lub produkcji. Targeted 23/23,
ponowna pełna regresja Host ma oddzielny run ddd7c2f0, bez nowego D-11/NF-11.
Pełnego runnera nie uruchamiano: bezpośrednie pełne Core/Host/Python obejmują
wymagane regresje IPC/resilience/E1/B0 dla tego zakresu zmian testowych.

## 2026-10-28 — B-03A/E2-B1-D: profil diagnostyczny i bezpieczny STOP po 1 cyklu

Baza 28eb6ea na fix/e2-b1-worker-resource-investigation, clean; po fetch main=16916d4,
raport B1=30011c9, bez merge/rebase/cherry-pick. Jawne zlecenie użytkownika dopuszcza
nowy, osobny eksperyment HANDLE_DIAGNOSTICS do 3 sekwencyjnych odczytów na B1.
Stary grant pozostaje zakończony. Historyczne plany, dowody i B1 BLOCKED zachowane.

Minimalne rozszerzenie lokalnego B0: document 1.1/purpose HANDLE_DIAGNOSTICS,
plan E2-B1-D/max3; stary 1.0/E2-B1/max20 bez zmiany zakresu. Budżet bierze się
z niezmiennego, weryfikowanego dokumentu w transakcji istniejącego ledgeru v4;
bez nowej migracji, resetu lub retry. Rezerwacja, completion i resolution używają
tego samego ograniczenia. Diagnostic capture wymaga faz, daje co najwyżej
DIAGNOSTIC_COMPLETE po release; review jako B1 PASS jest zabroniony.

Instrumentacja dodaje devices.enter i Operation ID ostatniego ukończonego cyklu
w idle; nie dodaje getterów Siemens/forced GC. E2-G-D zachowuje safety/D-11/KPI
SHARED_WORKSTATION, sprawdza nowe suite i dossier 1.1. HTTP/IPC/Core/E1/registry
bez rozszerzeń; nadal jeden scheduler/Worker/owner. Klasyfikacja typów wymaga
bezpiecznej dostępności; brak narzędzia jest NOT_AVAILABLE bez obejść.

Ocena obu instrukcji sekcja po sekcji: AGENTS zakres/hierarchia/niezmienniki/
autoryzacja/testy/dyscyplina/Git/dane oraz Instrukcja 1–10 cel/role/źródła/
architektura/zadania/kontrola/recovery/decyzje/Git/styl. Dodano wyłącznie jawny
wyjątek E2-B1-D w autoryzacji/recovery, pozostałe sekcje bez zmian. ADR 0001–0003,
specyfikacje 03–08/16/18, publiczne kontrakty i kryteria B1 pozostają bez zmian:
brak drugiego schedulera, mutacji, nowego canonical modelu, E1 fallbacku lub
produkcji. Specyfikacje 19/21, osobny plan, README i testy rozwijają autoryzowany
lokalny profil.

[Raport wykonania](docs/B03A_E2_B1_D_HANDLE_DIAGNOSTICS.md): run 09c962a0,
nowy grant 4ad6efe6, aktualna D-11 051565cc/E2-G-D READY, exact build/binding.
1/3 rzeczywistych cykli zakończony CONFIRMED, false/false i Dispose.Returned.
Kontroler błędnie porównał Discovery ProjectKey.Value z tekstem raw DTO; STOP
zachowany, niezależny review artefaktów/binding/release, revoke i kontrolowany
Host exit=0/Worker ended. Nie było retry ani cykli 2–3. Naprawiono porównanie
w pomocniczym Python i dodano syntetyczną regresję; bez ponownego grantu/live.
Po poprawce fingerprint źródeł zmieniony, następne wejście wymaga nowej D-11.

126 fazowych próbek: cold Attach 418→455, inventory 455→455, Dispose 456→446,
idle ostatnie 474 po 191,761 s. Typy handles NOT_AVAILABLE, warm NOT_RUN,
ROOT_CAUSE_UNEXPLAINED. Framework kontrola samego trace bez Siemens nie odtworzyła
wzrostu; nie dowodzi to naprawy. Plan następnej kontroli offline i osobnego live
w raporcie. Bez zmian historycznych dowodów B1, progów lub wyniku BLOCKED.
Oryginalne 1069 plików/manifest zweryfikowane ponownie. Prywatny pakiet poza Git.

Pełny canonical runner przed live: Release 0 warnings/errors, Core 564/564,
Host 399/399, Python 70/70, resilience/E1/B0/Framework PASS; D-11 limited PASS,
pełna kwalifikacja BLOCKED (NF-11/full NF-08 NOT_RUN). Po fix kontrolera Python
71/71; format obu rozwiązań, audit i diff check PASS. NF-11 nie uruchamiano.

## 2026-10-27 — B-03A/E2-B1-R: analiza uchwytów i instrumentacja offline

Baza `30011c9774cda09c3d8d2ffbb3ae1be7e0506678` na gałęzi raportu B1;
po fetch `origin/main=16916d4`. Czyste worktree, osobna gałąź diagnostyczna bez
merge/rebase/cherry-pick. Ponownie zweryfikowano 1069 oryginalnych plików B1,
manifest/bytes/SHA/count PASS, bez modyfikacji dowodów lub odczytu starej SQLite.
Nowy lokalny run analizy `a0509f55e2c94f94a5bbfcd8613024ec` przechowuje wyniki.

[Raport dochodzenia](docs/B03A_E2_B1_RESOURCE_INVESTIGATION.md) zawiera wszystkie
20 delt, cold/warm, pamięć, idle, korelację UTC z audit i wykres. 15/20 okien Attach
ma dodatnie delty rzeczywistych próbek, przed traversal; w Dispose brak próbek,
inventory nie rozdziela GetService i klasyfikacji. Nie interpolowano braków.
Zweryfikowano SHA obu historycznych qualification.json X-01 (STA 462→702,
MTA 456→602); inne buildy/profile nie są ilościową próbą kontrolną.
Wynik ROOT_CAUSE_UNEXPLAINED, nie potwierdzono naprawy. Historyczny raport B1
i jego BLOCKED pozostają niezmienione.

Metadane API odczytano PEReader bez ładowania Siemens. TiaPortalProcess.Dispose
zamyka Portal, więc nie jest poprawką cleanup. Nie znaleziono podstaw do traktowania
Device/DeviceItem/SoftwareContainer jako RCW lub stosowania ReleaseComObject.
Własne using/finally, event callback, procesy/tokeny, enumeratory, mutex, audit,
IPC i bounded replay dictionaries przejrzano; nie przypisano im potwierdzonej winy.

Dodano WorkerResourceTrace: domyślnie off, lokalny jawny switch startowy,
create-new we własnym NTFS ArtifactRoot, limit 4096 / 1 MiB, Operation ID/epoch,
handles/PB/WS/threads/managed bytes/GC0–2 i fazy istniejącego odczytu.
Bez nowych getterów, forced GC, DTO lub grantu; koszt diagnostyki jawny,
błąd trace nie omija finally Dispose. Bootstrap-report nie dołącza go automatycznie;
plan wymaga osobnego seal. Produkcyjny owner zachowuje CheckProcess; wewnętrzny
seam pozwala kompilować tę samą metodę z syntetycznymi typami API w testach.
Nowe testy obejmują wyjątki/dispose/enumeratory/dirty/null, timeout bez abort,
20 odczytów/weak references, log/limits/Operation ID i net48 IPC 20 cykli.
Kontrolowany GC występuje tylko w teście syntetycznej retencji.

**Ocena instrukcji sekcja po sekcji:** AGENTS: zakres/stan, hierarchia, niezmienniki,
autoryzacja/testy, dyscyplina zmian, Git, dane nieufne; Instrukcja_ChatGPT: cel,
odpowiedzialność, źródła, architektura, zadania, kontrola wyników, bezpieczeństwo,
decyzje, GitHub i styl. Obie bez zmian: nie poszerzono prawa Engineering, nie
zmieniono kryteriów PASS/STOP, modelu async/owner/timeout lub synchronizacji.
ADR 0001–0003 i kontrakty API/IPC/Core/E1/B0 bez zmian. Specyfikacje 03–08/16/19/21
sprawdzono; aktualizacja dotyczy raportu, README Workera/testów i indeksu README.
Oryginalne plany B1/E2-A pozostają byte-identical. Nowy krótszy plan live nie jest
automatycznie dopuszczony przez B0: raport jawnie opisuje sztywny plan/budżet 20
i wymagane osobne rozstrzygnięcie proceduralnego 3/20 lub trwałego limitu 3.

Nowe Siemens Attach/Discovery, grant, B2, E1 enrollment, Runtime i NF-11 NOT_RUN.
Worker Handle Risk UNKNOWN. Nowy build nie może używać historycznej kwalifikacji
jako dowodu aktualnego. Pełny runner SHARED_WORKSTATION uruchomiono ze względu na
zmiany Worker/net48/procesowych regresji; zapisano oddzielne wyniki wszystkich
prób, bez dobierania PASS z podzbiorów i bez wymazywania prób nieudanych.

Pierwszy run `415d726f40294e7eb8391fb3dccc1c76` zachowano jako FAIL:
wszystkie kroki i 941/941 .NET + 67/67 Python PASS, ale nowy plik próbnika
trafił do zamkniętego zbioru root scenariuszy i reporter odmówił agregacji.
Potwierdzony błąd integracji dowodu naprawiono przenosząc pomocnicze obserwacje
do resource-diagnostics/framework-ipc.json, nadal pod rekurencyjnym manifestem.
Nie zmieniono reportera, zbioru wymaganych scenariuszy ani progów; wykonano nowe
pełne przebiegi dla zmienionych źródeł, bez przepisywania pierwszego wyniku.

Celowane 15/15 PASS po poprawce. Run `11e41648380f4b3bbbe8c51448b19e06`
pozostał BLOCKED przed testami: chwilowy brak C: w Get-Volume. Trzy niezależne
odczyty systemowe ponownie potwierdziły lokalne NTFS C:/X:, bez zmian konfiguracji.
Nowy run `4c2126f5b218444ea4323c653e2a1d11` używa niezmienionego preflightu.
Zakończył się FAIL: Core 564/564, Host 376/377, Python 67/67, performance PASS.
Jeden test API odebrał reset TCP przy równoczesnym uploadzie oversize body
i wczesnej odmowie Content-Length. Nie uznano resetu za PASS. Test deklarowanej
długości stosuje teraz Expect: 100-continue; chunked nadal wysyła body. Oba nadal
wymagają 413/LIMIT_EXCEEDED i zero operacji. Host/limity/kontrakt bez zmian,
bez retry lub akceptowania wyjątku transportu. Celowane ApiSecurityTests 15/15 PASS;
pełna regresja dla zmienionych źródeł otrzymała nowy run, stare wyniki są zachowane.

Końcowy `93610f4b329a48878e636641d6957708`: OFFLINE/test execution/performance
PASS; Core 564/564, Host 377/377, Python 67/67, bez skip. Resilience 52/52,
B0 49/49, Local Trusted + dispatch 47/47, Core evidence 18/18,
V19Offline 47/47 i WorkerResource 13/13. Build Release 0 warnings/errors,
format obu solution, dokumentacja i diff PASS; self-test net48 x64 bez Siemens.
Verifier odtworzył manifest 78 plików, pełne TRX, fingerprint i hashe buildów.
Idle 305,930 s / CPU 0,953164% PASS; pełny NF-08 NOT_RUN, pełny NF-11 NOT_RUN
i qualification_status BLOCKED. Nie nadano nowej autoryzacji. Dodatkowy PE odczyt
HW.Software/PLC/Unified nie wykazał IDisposable/Import; cztery vendor DLL są
byte-identical z B1. ROOT_CAUSE_UNEXPLAINED i Worker Handle Risk UNKNOWN.

## 2026-10-27 — B-03A/E2-B1: 20 odczytów, kwalifikacja BLOCKED

Baza `16916d4f392b639a7936494a1c85f899fa4f3e0a`, po fetch HEAD=origin/main,
worktree czyste. Osobne jawne zlecenie użytkownika objęło autonomiczne read-only B1,
nowy lokalny deployment/ACL, B0, monitoring i Computer Use A. Nie rozszerzono zakresu
o drugi projekt, mutacje Siemens lub E1 enrollment.

[Raport B1](tests/engineering/E2_B1_2026-10-27_06cbfec6.md) rozdziela funkcjonalne
20/20 PASS od qualification BLOCKED / RESOURCE_REVIEW_REQUIRED. Run
`06cbfec6107d4627b015a0c5e5f433bd`: Windows/UI/NTFS/build binding, dossier,
E2-G READY na ponownie zweryfikowanej D-11 `bdca23b132664a2b84bf2d225d0e707d`,
bootstrap-inspect i jawne authorize poprzedziły Attach. 20 sekwencyjnych cykli STA
na jednym Workerze, bez retry/restart; wszystkie IsModified false/false,
graf i coverage zgodne, wszystkie Dispose.Returned. Host i Worker zakończone
kontrolowanie, TIA nadal żywa. Grant FINISHED/BUDGET_COMPLETED, used 20, released true.

Worker handles after cold 456 → warm 20 620; slope warm +9,589474/cykl,
mediany okien 489,5/557/616. Idle 30,032 s nie potwierdził powrotu do wczesnego
poziomu warm. Przyczyna nierozstrzygnięta; brak automatycznego PASS, nowego progu
lub tezy o potwierdzonym leak. Bootstrap review jawnie odmówione
(plan_checks_passed=false, GROWING). 112 metadanych plików i hash AP19 bez zmian;
brak deklaracji pełnego porównania zawartości pozostałych plików/pamięci TIA.
684 rekordy samplera i wszystkie outliery zachowane. Pakiet 1069 plików,
manifest `a16d14f9e88deefd60b434e7e74e9a99ff3b8c566530135bcc8b955c844a0200`,
niezależny PowerShell Get-FileHash/bytes/count PASS. Prywatne surowe dowody, bindingi,
projekty, oryginalna SQLite, konfiguracja i lokalne skrypty pozostają poza Git.

**Ocena spójności sekcja po sekcji:** AGENTS: zakres/stan (historyczna data pozostaje),
hierarchia, niezmienniki, autoryzacja/testy, dyscyplina zmian, Git i dane nieufne;
Instrukcja_ChatGPT: cel, odpowiedzialność, źródła, architektura, zadania, kontrola,
bezpieczeństwo/recovery, decyzje, GitHub i styl. Obie instrukcje bez zmian:
wykonano już zdefiniowany, osobno autoryzowany eksperyment; nie zmieniono zasad,
kryteriów lub praw i nie stwierdzono konfliktu normatywnego.
ADR 0001–0003, specyfikacje 03–07/17–20 i kontrakty API/IPC/Core/E1/B0 bez zmian:
brak nowego adaptera, gettera, security workflow, SYNCED lub kwalifikowanej capability.
Dokładne bajty zatwierdzonego planu B1 i E2-A zachowano. README, bieżące odniesienia
08/21 i nowy raport aktualizują wyłącznie stan wykonania; historyczne dowody X-01,
STRICT FAIL, NF-03 exFAT FAIL i wcześniejsze E2 przygotowanie pozostają zachowane.

Engineering B2, real E1 enrollment, Runtime, NF-11 i produkcja NOT_RUN;
pełna kwalifikacja offline BLOCKED przez NF-11, Worker Handle Risk UNKNOWN.
Zmieniono wyłącznie dokumentację wynikową; build Host/Worker i źródła kodu bez zmian.
Walidacja dokumentacji PASS: audit_quality.py sprawdził 66 Markdown i 604 lokalne
odnośniki, errors=[]; git diff --check PASS. Nie uruchamiano kolejnej serii Siemens
ani testów tworzących nowy binding. Zewnętrznych 28 linków nie sprawdzano live.

## 2026-10-27 — B-03A/E2-G: SHARED_WORKSTATION i bramka E2-B1

Baza: `3e0785556902c0ef10d6983f17ea0743997e8e6c`, HEAD=origin/main po fetch,
worktree czyste. Zatwierdzono SHARED_WORKSTATION 1.0.0 dla ograniczonej D-11/E2-B1;
STRICT, progi bezpieczeństwa i timeouty ochronne bez zmian. Stanowisko współdzielone:
normalne obciążenie i PASS_WITH_LOAD dopuszczone. NF-08 dotyczy procesu Host.

Dodano [deterministyczną bramkę](docs/21_E2_B1_READINESS_GATE.md) opartą o istniejące
manifesty/próbki, reporter RT/NF i kontrolę TRX. Rozdziela safety, performance,
integralność i readiness B0. READY_FOR_OPERATOR_AUTHORIZATION nie jest grantem,
nie uruchamia Siemens i nie zapisuje autoryzacji. Dossier wymaga exact B1/scope/
ProjectKey/AP19/PID/start/EXE/SID/API/Host/Worker, czystej kopii/UI, monitoringu,
bezpiecznego STOP i braku kolizji. B2 odrzucane. IsModified dopiero w autoryzowanym
odczycie; false/false w każdym cyklu pozostaje bez zmian.

Aktualność dowodu: run ID/Git SHA/profile version/hash, fingerprint źródeł,
Release Host/Worker SHA-256, środowisko/NTFS/preflight, RT/NF, komplet artefaktów i
końcowy status. Runner dodaje offline build Worker i niezmienny build manifest.
Nie wprowadzono TTL, dodatkowego soak ani nowej kryptografii. NF-11 NOT_RUN blokuje
pełną kwalifikację, nie ograniczoną D-11. Pełny NF-08 pozostaje NOT_RUN bez fałszywego
PASS; wykonane negatywne podtesty nie są ukrywane. Historyczne raporty bez zmian.

**Ocena spójności sekcja po sekcji:** AGENTS: zakres/stan, hierarchia, niezmienniki,
autoryzacja/testy, dyscyplina zmian, Git i dane nieufne; Instrukcja_ChatGPT: cel,
odpowiedzialność, źródła, architektura, zadania, kontrola, bezpieczeństwo, decyzje,
GitHub i styl. Zaktualizowano tylko autoryzację/testy i kontrolę wyników o decyzję
E2-G; reszta zachowana. ADR 0001–0003 i API/IPC/Core/E1/B0 pozostawiono bez zmian:
brak nowego wykonawcy, capability, grantu, semantyki synchronizacji lub HMI.
Właściwe specyfikacje 02/07/08/16/19/20, plan B1, operator i odniesienie E2-A uaktualnione.

Pierwszy pełny run `0877675091694b60ad2c246b8b254052` zachowano jako FAIL:
Core 564/564, Host 362/363, Python 67/67, wymagane próbki KPI PASS. Jeden przypadek
Missing_heartbeat... nie zaobserwował EXECUTING przed QUARANTINED; pierwotny komunikat
nie zachował kodu przyczyny, więc nie przypisujemy potwierdzonej przyczynowości CPU
lub IPC. Bramka poprawnie zwróciła SAFETY_TESTS_NOT_PASS i B1_DOSSIER_MISSING.
Nie uznano zielonych podzbiorów za aktualną D-11 PASS.

Korekta harnessu: SchedulerTests i SchedulerSafetyTests używają istniejącej wyłącznej
kolekcji procesowej, ograniczając równoczesny load własnych testów. Normalna praca
użytkownika pozostaje dozwolona. Running fail-fast zachowuje terminalny state/phase/
error zamiast maskować go oczekiwaniem; test heartbeat sprawdza RUNNING przed progiem,
quarantine po progu i jeden attempt. Brak zmiany watchdogu, IPC lub timeoutów ochronnych,
brak retry Siemens i brak wymazywania pierwszego FAIL. Nowy pełny run jest wymagany
dla zmienionych źródeł; celowany test nie zastępuje D-11.

**Końcowa walidacja OFFLINE:** nowy kanoniczny run
`bdca23b132664a2b84bf2d225d0e707d`, SHARED_WORKSTATION 1.0.0, preflight PASS,
build Release rozwiązania i Worker PASS (0 warningów/błędów), Core 564/564,
Host 363/363, Python 67/67; bez FAIL/SKIP. Resilience 52/52, bootstrap 49/49,
E1 LocalTrusted 44/44 + dispatch 3/3 + Core evidence 18/18, V19Offline 46/46.
Regresja celowana schedulera 42/42 przed tym runem. API/IPC/schema/OpenAPI,
generatory kontraktów/wektorów, audyt linków/dokumentacji, format i diff check PASS.

Bramka E2-G niezależnie odtworzyła wyniki i sprawdziła 77 artefaktów. Manifest SHA-256:
`9ae079b04d0ecaf5bce553154052fc7ae55843c44f8e19396edc321cbdb2bf02`.
Hash definicji profilu:
`855e5c952aeece7bfa165676fbe9f15eb61151fd1ded00c89abd0c4228becdd2`.
Git SHA runu identyfikuje bazę powyżej; dokładne zmienione źródła wiąże fingerprint
`86b92b4d237a62f9faedc93219925db0c7dca7190fbc88f1b46e315260e9d15b`.
D-11 ograniczona PASS, safety/performance/evidence PASS. Idle 307,955 s,
CPU procesu Host 0,872692%, RAM Host 94,504 MiB / Fake 37,965 MiB;
1001 operacji PASS. NF-03 repo bez/z Workera p95 51,0899/11,643 ms, Workspace
2,5704 ms. Wszystkie próbki i wcześniejszy FAIL zachowane; bez dobierania podzbiorów.

E2-G IMPLEMENTATION COMPLETE_OFFLINE. Rzeczywista E2-B1 READINESS BLOCKED:
B1_DOSSIER_MISSING — brak exact aktualnego ProjectKey/AP19/PID/start/EXE/SID/API
bindingu, review czystej kopii/UI i gotowości monitoringu/STOP. Nie pozyskiwano ich
przez Openness. Operator musi uzupełnić dossier według dokumentu 21, zweryfikować
READY, potem osobno zatwierdzić live B1, okno i lokalny dokument B0, sprawdzić ACL,
bootstrap-inspect i jawnie bootstrap-authorize. READY nie jest zgodą ani grantem.
ENGINEERING B1/B2 NOT_RUN, OPERATOR AUTHORIZATION NOT_GRANTED. Pełna kwalifikacja
offline BLOCKED przez NF-11 NOT_RUN; pełny NF-08 NOT_RUN (idle podtest PASS),
produkcja/Runtime NOT_RUN, Worker Handle Risk UNKNOWN. Brak klucza produkcyjnego,
zmian rzeczywistych ACL, TIA start/Attach/enumeration lub rzeczywistej aktywacji B0.

## 2026-10-27 — D-11-P: SHARED_WORKSTATION performance profile

Dodano jedną wersjonowaną definicję progów z domyślnym `STRICT` i kandydackim
`SHARED_WORKSTATION`, jawny wybór profilu runnera oraz wspólną ocenę C#/Python.
Raport próbek zawiera profil/version/hash, Git SHA, run ID, surowe próbki, progi,
p50/p95/p99/max, liczności przekroczeń i SHA artefaktów. Preflight dopuszcza
`PASS_WITH_LOAD`; zwykłe obciążenie, AV, VS Code i kolejki dyskowe nie tworzą
samodzielnego BLOCKED. Timeouty ochronne i limity RAM nie zostały podniesione.

Zmieniono [07](docs/07_TEST_STRATEGY.md), [08](docs/08_IMPLEMENTATION_PLAN.md),
[16](docs/16_RESILIENCE_RECOVERY.md), [B03A/E2-A](docs/B03A_E2_A_PREPARATION.md),
[E2-B1](docs/E2-B1_TEST_PLAN.md) oraz [wymagania](docs/02_REQUIREMENTS.md);
szczegółowa polityka, analiza progów i tabela historyczna są w
[20_PERFORMANCE_QUALIFICATION_PROFILES.md](docs/20_PERFORMANCE_QUALIFICATION_PROFILES.md).
Historyczne raporty i snapshoty nie zostały zmienione ani przeliczone. Warunek
E2-B1 pozostaje bez zmian i wymaga osobnej decyzji. Oceniono `AGENTS.md`,
`Instrukcja_ChatGPT.md`, ADR 0001–0003 i kontrakty; pozostają bez zmian, ponieważ
bieżąca zatwierdzona bramka X-01 i granty są zachowane, a kandydat nie zmienia API,
IPC ani bezpieczeństwa.

**Walidacja D-11-P:** kanoniczny offline run `239339c7003345d8a758a331d6c9a5ad`
zakończył runner `PASS`, preflight `PASS`, profil `SHARED_WORKSTATION`
`1.0.0-candidate` i ocenę KPI `PASS`; pełna kwalifikacja pozostaje `BLOCKED`,
ponieważ NF-11 (8 h) celowo `NOT_RUN`, a istniejąca bramka X-01 nie wyklucza go
jednoznacznie. Nie uznano tego za zmianę bramki ani za E2-B1 PASS. Build Release
PASS (0 warningów/błędów), Core 564/564, Host 363/363, Python 46/46; audyt jakości,
format i `git diff --check` PASS. NF-08 pełne kryterium pozostaje `NOT_RUN`; osobny
zmierzony 300 s idle podtest PASS. NF-03 workspace N=100: p50/p95/p99/max
2,1666/4,8333/8,5523/67,5971 ms; repo bez/z Fake N=100: p95 11,4893/5,0122 ms,
max 37,844/16,1403 ms, zero przekroczeń limitu 500 ms. NF-02 N=64 p50/p95/p99/max
132,3369/158,6116/208,8081/208,8081 ms. Surowe wyniki i manifesty pozostają
w ignorowanym `.tia-bridge/`; wcześniejsze próby zablokowane/błędne zachowano i nie
wybrano najlepszego wyniku spośród runów. Engineering, Runtime, E2-B1/B2 i NF-11
nie zostały uruchomione.

## 2026-10-27 — B-03A/E2-B0: Controlled Qualification Bootstrap

**Wejście:** czyste `HEAD=main=origin/main=5d999023a799e91c90e54902504c5fa7108494ef`
po fetch. Zakres: zatwierdzona implementacja i pełne testy offline, bez TIA/Openness,
vendor enumeration/Attach, produkcyjnego klucza, ACL deploymentu, realnych dowodów,
aktywnego grantu rzeczywistego projektu, E2-B1/B2 lub B-03B. Testowe lokalne granty,
ACL i dokumenty istnieją wyłącznie we własnych syntetycznych UUID fixtures.

**Implementacja:** jawna lokalna konfiguracja qualification-only wybiera profil B0
na istniejącej ścieżce Host API → Durable Operations → Scheduler → Worker V19.
Nie jest fallbackiem po E1 denial, nie tworzy capability PASS i nie zmienia Core
policy/registry v1/v2, HTTP API v1 ani IPC 1.1. Normalne wykonanie nadal wymaga E1.
Local operator CLI z exact reviewed document SHA i flagą confirmation zapisuje
autoryzację raz w istniejącej SQLite; bez klucza CNG i bez enrollment. Chronione
config/root/plik i SID są weryfikowane przez istniejące ACL helpery, bez auto-fix.

**Autoryzacja i bezpieczeństwo:** tylko B1 `T05_RecipeSim_V19_Testowy`, jeden exact
ProjectKey/PID/start/AP19/root/EXE/SID, Host/Worker/API build, hash planu E2-B1,
okno i 13 getterów v19.discovery.v1. STA, all_supported/fresh, 120/300 s, 64/256/8
i 20 cykli, bez native retry. Profil ponownie weryfikuje binding/grant/config przed
PrepareDispatch i IPC; Worker po Attach potwierdza projekt. B2/WRITE/IMPORT/EXPORT
nie mogą użyć grantu. HostBuild i WorkerBuild są SHA-256 odpowiednio Host DLL i
Worker EXE; pozostałe zależności wymagają nadal build manifestu i preflightu B1.

**Durability:** migracja 004 dodaje bootstrap_authorizations/cycles/release, bez
zmiany migracji 001–003. Zużycie cyklu jest w transakcji intentu przed IPC; nie ma
refund po commit, replay lub restarcie. ACTIVE wiąże się z jedną epoką Host,
restart zamyka grant i oznacza RESERVED jako UNCERTAIN_AFTER_RESTART. Revoke,
expiry, STOP, błąd walidacji i koniec budżetu blokują następne dispatch. Zachowano
active-slot/quarantine/fencing i nie uznano timeoutu lub śmierci procesu za koniec
Siemens call. Końcowa rewalidacja nie jest atomową transakcją z wysłaniem IPC.

**IsModified i dowody:** Worker pomija inventory przy dirty wejściu i zwraca raw
obserwację po Dispose; nie dodano gettera. Host odrzuca true/true i false/true;
raw receipts zapisuje przed interpretacją bootstrapu, a poprawnie ukształtowaną
raw observation przed walidacją projekcji. Telemetria przed IPC i przy receipt,
epoch/PID/start/cycle/authorization/operation, czasy monotonic i faz V19, zasoby,
wyniki/STOP/cleanup/release pozostają trwale skorelowane. Raport/bundle create-new
zawiera pełne cykle, surowe odpowiedzi i manifest SHA-256 z niezależnym odczytem.
20 poprawnych odczytów daje REVIEW_REQUIRED do release i jawnego review operatora
stanu projektu, pełnego planu i trendu. GROWING/INCONCLUSIVE nie daje PASS. Brak
nowego progu handles/NF, usuwania outlierów lub restartu dla spłaszczenia trendu.
PASS to kandydat do późniejszego inspect/approve/enroll, nigdy auto enrollment.

**Walidacja celowana przed końcowym przeglądem:** Bootstrap/V19Offline 89/89 PASS, zero skip;
w tym 43 Bootstrap oraz 46 V19Offline. Wcześniejszy rozszerzony zestaw z migracjami
97/97 PASS. Własny proces StorageProbe faktycznie kończy się po commit rezerwacji,
przed dispatch; restart zachowuje użyty cykl i blokuje replay. Syntetyczny adapter
korzysta z tego samego schedulera i DTO IPC, bez uruchamiania Workera V19 lub DLL
Siemens. Początkowe błędy kompilacji brakującego using i nullable helpera testowego
usunięto przed regresją. Pełne wyniki końcowego runnera i ograniczenia poniżej.
Końcowy przegląd dodał regresję starego fenced receipt podczas kolejnego cyklu:
STOP dotyczy bieżącej operacji, wcześniejszy terminalny wynik pozostaje bajtowo
niezmieniony. Pakiet weryfikuje i dołącza audit Workera, Dispose.Returned per cykl,
strukturę/binding telemetrii oraz zgodność cycle report z trwałym wynikiem operacji.
Dodano negatywne przypadki missing audit i corrupt telemetry/initial/report.
Utrata Workera na idle również zamyka grant zamiast uruchomić zastępczy proces;
pełna seria wymaga jednej tożsamości Workera, aby nie maskować trendu zasobów.

**Ocena instrukcji sekcja po sekcji:** AGENTS: zakres/stan, role/hierarchia,
niezmienne granice, autoryzacja/testy/D-11/Computer Use, dyscyplina, Git, dane nieufne.
Instrukcja_ChatGPT: cel/role, źródła/hierarchia, architektura, standard zadania,
kontrola wyników, bezpieczeństwo/recovery, decyzje/spójność, Git/dokumentacja, styl.
Obie zaktualizowano wyłącznie w autoryzacji: jawne qualification-only dla B1 nie jest
normalną capability i wymaga osobnego live grantu; restart nie odnawia budżetu.
Pozostałe sekcje pozostają zgodne i bez zmian. ADR 0001–0003 nie wymagają zmiany:
ten sam Host/Worker/owner/IPC, bez mutacji, nowego canonical HMI, IDs lub SYNCED.
Dokumenty 03–06/12, kontrakty i RT/NF pozostają normatywnie zgodne; 08/16/17/18,
plany E2 i instrukcje wskazują zatwierdzony dodatek [19](docs/19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md).
Historyczne raporty X-01/D-11/E1-L i ich bajty/wyniki pozostają nietknięte.

**Granice:** implementacja OFFLINE, rzeczywisty Engineering B1/B2 NOT_RUN, evidence
NOT_REGISTERED, produkcja/Runtime/NF-11 NOT_RUN, Worker Handle Risk UNKNOWN.
Bezpieczeństwo rzeczywistej kopii, UI, isolated receiver i pełna analiza zasobów
wymagają operatora i dowodów B1; kod nie potwierdza prawdziwości arbitralnego review.
Wspólny SID/administrator pozostaje granicą zaufania, bez wielokontowej usługi,
testu power-loss lub nowej kwalifikacji vendor cleanup. B0 nie uruchamia eksperymentu.

**Końcowa walidacja implementacji:** celowany zestaw po końcowym przeglądzie
95/95 PASS (Bootstrap 49, Discovery/V19Offline 46). Pełny kanoniczny runner
`21ae0844cc95404e869ce79ef45f42dc`: Release build 0 warnings/errors, Core 564/564,
Host 361/361, łącznie .NET 925/925, Python 42/42, zero FAIL/SKIP. IPC 34/34,
scheduler/IPC 76/76, E1 LocalTrusted 47/47 i zachowany pozytywny E1 ProjectInspect,
resilience test execution 52/52. Generatory, format, audit (63 Markdown / 559 linków)
i diff check PASS; wszystkie dziewięć kroków wykonania exit 0. Osobny Release build
Workera net48/x64 PASS, bez uruchomienia EXE/Openness i bez kopiowania vendor DLL;
kompilator użył wyłącznie referencji/metadanych zainstalowanego API.

**Odrębna kwalifikacja FAIL:** runner exit 1, `test_execution_status=PASS`,
`resilience_gate=FAIL`. NF-03 X:/NTFS replace podczas Fake Hang: p95 148,9617 ms
przy niezmienionym limicie 100 ms (N=100), p99 312,0495 ms, max 661,6204 ms,
zero odmów. Kontrola bez Workera p95 4,8431 ms; zapis/replace na state NTFS PASS,
API NF-01/NF-02 PASS. Nie ustalono przyczyny i nie przypisano jej bootstrapowi,
NTFS lub AV; bootstrap pozostaje wyłączony w tym profilu Fake. Nie ponawiano NF
dla uzyskania PASS. NF-08 pełna skala NOT_RUN (idle 306,1636139 s, CPU 0,8752469%
w tym oknie PASS); 1001 operacji w 471,158264 s nie zastępuje NF-11 ≥8 h.
NF-11 NOT_RUN i produkcja NOT_RUN. **B0 COMPLETE_OFFLINE, wejście B1 BLOCKED**
do właściwej pozytywnej bramki D-11/preflightu i osobnej zgody/grantu.

**Integralność i metadane:** niezależnie zweryfikowano 73/73 pliki manifestu,
trzy bundles i 464/464 próbek z aktualnym binary/source binding; zachowano wynik
FAIL bundle repozytorium. SHA manifestu:
`b3a29fda3188b4f05dd93cc2c41fd962b1c3e225981320a4baa3de0540f93a30`.
20 hashy źródeł C#/SQL nie zmieniło się podczas finalnej regresji. Ledger hashes
migracji 001–003 pozostają identyczne (zastane CRLF pozostawiono), historyczne
raporty X-01/E1-L/D-11 mają identyczne bajty. Pierwszy runner
`e6c2b34d87934fcc8d34b40e4ea69dde` dotyczył kodu sprzed końcowego przeglądu:
919/919 .NET i 42/42 Python PASS, aggregate BLOCKED; zachowano go jako etap
pośredni, bez użycia zamiast finalnego FAIL kwalifikacji.

Reporter miał stałą metadaną SQLite schema=3 po dodaniu migracji 004. Poprawiono
wyłącznie tę metadaną na 4 i dodano regresję zgodności z wersją rzeczywistego
store. Finalne Python 43/43 PASS po tej korekcie; C#/SQL i binaria testowanego
builda nie zmieniły się, więc nie powtarzano ich pełnego przebiegu. Zamkniętego
raportu i manifestu z wartością 3 nie przepisano; korekta jest osobnym review note.
Nie zmieniono metryk, scenariuszy, progów RT/NF lub interpretacji FAIL.

Dokumentacja wykonawcza: [bootstrap 19](docs/19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md).
Surowe dowody tylko w ignorowanym `.tia-bridge/resilience/<run-id>/` oraz
`.tia-bridge/e2-bootstrap/`; żadnego rzeczywistego grantu, source Engineering lub
prywatnego materiału w Git. Przed dostawą wykonano selektywny staging, kontrolę
całego diffu i normalny push bez force.

**Lista 37 plików dostawy B0:**

```text
AGENTS.md
Instrukcja_ChatGPT.md
Zmiany.md
docs/08_IMPLEMENTATION_PLAN.md
docs/16_RESILIENCE_RECOVERY.md
docs/17_TRUSTED_QUALIFICATION_EVIDENCE.md
docs/18_LOCAL_TRUSTED_ENROLLMENT.md
docs/19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md
docs/B03A_E2_A_PREPARATION.md
docs/E2-B1_TEST_PLAN.md
docs/E2-B2_TEST_PLAN.md
docs/E2_OPERATOR.md
scripts/report_resilience.py
src/TiaBridge.Host/Api/HostApi.cs
src/TiaBridge.Host/Api/HostConfiguration.cs
src/TiaBridge.Host/Operations/BootstrapStorage.cs
src/TiaBridge.Host/Operations/ExecutionStorage.cs
src/TiaBridge.Host/Qualification/BootstrapAuthorization.cs
src/TiaBridge.Host/Qualification/BootstrapEvidence.cs
src/TiaBridge.Host/Qualification/BootstrapLocal.cs
src/TiaBridge.Host/README.md
src/TiaBridge.Host/Scheduling/BootstrapInspectionProfile.cs
src/TiaBridge.Host/Scheduling/ExecutionModels.cs
src/TiaBridge.Host/Scheduling/OperationScheduler.cs
src/TiaBridge.Host/Scheduling/V19InspectionProfile.cs
src/TiaBridge.Host/Storage/Migrations/004_qualification_bootstrap.sql
src/TiaBridge.Host/Storage/SQLite/SqliteDatabase.cs
src/TiaBridge.Host/Supervision/WorkerSupervisor.cs
src/TiaBridge.Qualification/Program.cs
src/TiaBridge.Qualification/README.md
src/TiaBridge.Worker.V19/VendorReader.cs
tests/README.md
tests/contracts/test_resilience_report.py
tests/integration/TiaBridge.Host.Tests/BootstrapTests.cs
tests/integration/TiaBridge.Host.Tests/MigrationAndPolicyTests.cs
tests/integration/TiaBridge.Host.Tests/V19OfflineTests.cs
tests/integration/TiaBridge.StorageProbe/Program.cs
```

## 2026-10-27 — B-03A/E2-A: przygotowanie sekwencyjnej kwalifikacji V19

**Wejście i zakres:** baza `97e9f803a583d727e53fb366d13a316baaaaaab7`, po fetch
`HEAD=main=origin/main`, czyste worktree. Wyłącznie przygotowanie offline i odczyt
środowiska Windows/plików dokumentacji API; bez uruchamiania TIA/Openness/Attach,
testów Siemens, NF-11, nowego D-11, klucza produkcyjnego, ACL deploymentu lub real
enrollment. Snapshot Windows zastał cztery już działające procesy Portal V19; nie
identyfikowano ich projektów i nie sterowano nimi. C:/X: Fixed/NTFS; domyślny state
Host nie istnieje. To nie jest potwierdzony deployment ani preflight Engineering.

**Pakiet:** [E2-A](docs/B03A_E2_A_PREPARATION.md), osobne
[B1](docs/E2-B1_TEST_PLAN.md) `T05_RecipeSim_V19_Testowy` i
[B2](docs/E2-B2_TEST_PLAN.md) `T04_T05-000_Testowy` oraz
[operator](docs/E2_OPERATOR.md). Jeden target naraz, 20 sekwencyjnych cykli STA,
exact PID/start/executable/SID/AP19/ProjectKey/build/read scope, limity DiscoveryV1
64/256/MaxDepth=8 z wyjaśnieniem depth=0 pierwszego DeviceItem. B1 qualification PASS,
czysty stan, pełne hashe, release i review trendu są warunkiem osobnej decyzji oraz
nowego preflightu B2. Brak sztucznego progu handles; pełne serie cold/warm/idle,
delty/nachylenie/mediany i jawna ocena GROWING/INCONCLUSIVE zamiast dowodu stabilności.

**Weryfikacja E1 i ograniczenia:** deny-by-default registry v2/PROJECT/project.inspect,
podpis/ACL/lifecycle/artefact hash i exact matching oraz ponowne oceny przed dispatch
pozostają zgodne z kodem. Mechanizm Local Trusted PASS offline, real evidence
NOT_REGISTERED. Procedura ACL odpowiada wspólnemu SID desktop Host/operator;
usługa z innym kontem niekwalifikowana. Plan wymaga false/false IsModified; Worker
odczytuje bit przed/po, ale projekcja równości dopuszcza true/true — sam SUCCEEDED
nie zalicza E2. Audit zawiera fazy, nie każdy getter. Publiczny artifact download
nie jest zaimplementowany; plany wskazują odczyt exact lokalnych blobów po ID/SHA.
Stary harness wymaga dwóch projektów, przeplata A/B i nie provisionuje E1, więc jest
jawnie wyłączony dla E2. Plany używają istniejącego Host/API/CLI, bez nowej infrastruktury.

**Raport rozbieżności/bootstrapu:** dokument 18 wymaga real source evidence przed
live dispatch; obecny profil także wymaga takiego exact PASS przed pierwszym call,
a brak nowej kwalifikacji B-03A. Pakiet opisuje źródła, skutki, warianty i rekomendację
osobno zatwierdzonego projektu ograniczonego eksperymentu, gdy brak źródłowych dowodów.
Nie wybrano wyjątku/bypassu i nie podpisano fikcyjnego PASS. E2-A przygotowanie COMPLETE;
wejście live nadal BLOCKED do decyzji/bootstrapu, rzeczywistego deploymentu i preflightu.
E2-B1/E2-B2 Engineering NOT_RUN, produkcja/Runtime NOT_RUN, B-03B NOT_READY,
NF-11 NOT_RUN i historyczny Worker Handle Risk UNKNOWN bez zmiany.

**Walidacja:** Build Release PASS, 0 warnings/errors; celowana regresja Host
LocalTrusted/V19Offline 92/92 oraz Core Capability/QualificationEvidence 39/39 PASS,
zero FAIL/SKIP. Python contracts/schema/IPC/gate/performance-reporter 42/42 PASS;
generatory kontraktów i 100 wektorów PASS. Osobno porównano 13 getterów planu z kodem,
sprawdzono oba requesty (placeholdery odrzucone, podstawienia syntetyczne zgodne ze
schematem) i niezmienność SHA-256 11 źródeł produkcyjnych. Testy używają własnych syntetycznych UUID
fixtures i efemerycznych kluczy, nie produkcyjnego CNG; dotychczasowe ACL testy
modyfikują wyłącznie nowo utworzone własne fixtures, nie deployment/Windows/projekty.
Nie uruchomiono pełnego runnera/NF, bo kod produkcyjny i wymagania nie uległy zmianie.
Audyt dokumentów i linków PASS (62 Markdown, 537 lokalnych linków, 0 błędów), diff
check PASS; przejrzano selektywny staged diff. Surowe dowody przygotowania wyłącznie
w `.tia-bridge/e2-preparation/2d9c1300a6eb42869a5f1ad2d1fa4a15/`, z manifestem SHA-256.
Przy edycji skorygowano lokalne EOL plików do stylu bazowego, bez masowego diffu.

**Ocena obu instrukcji sekcja po sekcji:** AGENTS — zakres/stan, role/hierarchia,
niezmienne granice, autoryzacja/testy/D-11/Computer Use, dyscyplina, Git i dane nieufne;
Instrukcja_ChatGPT — cel/odpowiedzialność, źródła/hierarchia, architektura, zadanie,
kontrola wyników, bezpieczeństwo/recovery, decyzje/spójność, Git/dokumentacja i styl.
Obie pozostają bez edycji: plany konkretyzują nowe jawne zlecenie przygotowania,
nie nadają grantów i zachowują obowiązek wstrzymania zależnej pracy przy bootstrapie.
ADR 0001–0003, dokumenty 03–06/12/17, Core/HTTP/IPC/registry i semantyka SYNCED
bez zmian; nie ma nowego Attach/owner/schedulera ani gettera. Strategia 07, backlog 08,
procedura 18 i indeksy README/Engineering wskazują nową kolejność i warunki wejścia.
Nie zmieniono RT/NF, kwalifikacji lub historycznych raportów X-01/D-11/E1-L.

## 2026-10-27 — B-03A/E1-L: Local Trusted v1

**Zakres i implementacja:** baza `8aa01e01306650d18dcc834868e68aaf66ee5b48=origin/main`, worktree clean po fetch. Zatwierdzony model: jeden lokalny operator/klucz CNG P-256, ACL, jawne approve/enroll, trwałe manual revoke, ręczna rotacja i brak auto expiry; bez PKI. Nowy lokalny TiaBridge.Qualification wykonuje inspect/approve/enroll/list/revoke/verify. Ten sam qualification-evidence otrzymuje create-new approval i records per ID/revision oraz append/flush lifecycle; provider E1 wymaga aktywacji i ponownie sprawdza revocation, podpisy, hashe, project/build/read scope oraz rzeczywiste ACL/Host SID. Chronione pliki nie wystarczają bez bezpiecznych przodków — dodano kontrolę owner i DeleteChild/ownership/write rodzica. Narzędzie nie zmienia ACL i nie generuje kluczy; testy ustawiają ACL wyłącznie własnych syntetycznych UUID fixtures.

**Granice:** Core/HTTP/API v1/IPC 1.1/registry v1-v2/Worker/scheduler/durable operations i UNKNOWN/quarantine pozostają bez zmian kontraktowych. Nie ma TIA, live evidence, key provisioning, auto enrollment lub nowego grantu. Domyślne konfiguracje nadal nie mają anchorów. Osobne narzędzie operatora nie jest zależnością Host i jako jedyne otwiera istniejący prywatny CNG key. Wspólny SID w przetestowanym per-user deploymentcie nie daje izolacji od złośliwego procesu tego SID; nie kwalifikowano oddzielnych kont/usługi. Odwołanie nie jest rollbackiem call, który przekroczył granicę dispatch.

**Testy i dokumentacja:** dodano 47 przypadków LocalTrusted, wraz z utrzymanym dotychczasowym testem E1 **48/48 PASS** w finalnym przebiegu celowanym. Macierz 24 wymagań, powiązania kodu, diagnozy iteracyjne i ryzyka w [raporcie E1-L](docs/B03A_E1_L_LOCAL_TRUSTED.md). [Dokument 17](docs/17_TRUSTED_QUALIFICATION_EVIDENCE.md), [instrukcja operatora 18](docs/18_LOCAL_TRUSTED_ENROLLMENT.md), README Host/narzędzia opisują klucze/ACL/approval/revoke/restart/disable. Pierwszy pełny runner `80d1564aebc344a18d92d096ace17981`: .NET 871/871, Python 42/42, build/generatory/audyt PASS, ale runner FAIL na formatowaniu dwóch miejsc nowego inicjalizatora. Wynik zachowano. Dodatkowe 4 testy odtworzyły niezgodne limity payload/envelope, brak startup walidacji dwóch błędnych public keys i append ponad limit odczytu journalu; po poprawkach PASS. Payload 1 MiB, envelope 2 MiB, journal 8 MiB bez przepełnienia/zmiany wcześniejszych bajtów; bez zmiany NF/RT lub integralności. Pełny format verify PASS. Końcowy pełny runner na finalnym kodzie zakończył się PASS funkcjonalnym; szczegóły i odrębny wynik kwalifikacji poniżej. Preflight C:/X: Fixed/NTFS, bez Host/Fake/TIA Portal; początkowy snapshot CPU 96% zachowany jako ryzyko. Pierwszy przebieg nie uzyskał qualification PASS. Surowe dane wyłącznie `.tia-bridge/` lub własne dev fixtures poza repo.

**Walidacja końcowa:** kanoniczny runner `b92e24a5659f45e5b58d1eedbbeb5b8a` zakończył się 2026-10-26 o 22:36:54 UTC, wszystkie 9 kroków exit 0. Build Release 0 warnings/errors, Core 564/564, Host 311/311, razem .NET 875/875, Python 42/42, zero FAIL/SKIP. E1 66/66, IPC 34/34 (scheduler/IPC 76/76), resilience 52/52 i 49/49 wymaganych records PASS. Generatory, audyt (58 Markdown/489 lokalnych linków/0 błędów), format i diff PASS. Niezależna kontrola 73/73 plików manifestu, trzech bundles i 464/464 próbek z aktualnym source/binary binding PASS; SHA w raporcie. Functional execution PASS; nowa ograniczona D-11 PASS na C:/X: Fixed/NTFS, wymagane RT/NF PASS, idle 306.0543894 s, CPU 0.8493946%, 1001 operacji w 551.6663539 s, bez krytycznych błędów. Full offline qualification BLOCKED (NF-11 NOT_RUN, pełna skala NF-08 NOT_RUN); Production NOT_RUN. Wynik początkowego runnera FAIL i historyczne ograniczenia zachowano. Weryfikacja dostawy 2026-10-27, bez ponawiania testów na niezmienionym kodzie.

**Ocena instrukcji sekcja po sekcji:** AGENTS — zakres/stan, role/hierarchia, granice, autoryzacja/testy/D-11/Computer Use, dyscyplina, Git, nieufne dane; Instrukcja_ChatGPT — cel/odpowiedzialność, źródła/hierarchia, architektura, standard zadania, kontrola wyników, bezpieczeństwo/recovery, decyzje/spójność, Git/dokumentacja i styl. Obie bez edycji: implementacja realizuje bieżącą jawną decyzję użytkownika w istniejącej architekturze; nie zmienia sync, mutacji, prawa TIA lub progów. ADR 0001–0003, specyfikacje 03–09 i kontrakty Core/API/IPC/registry pozostają zgodne i bez zmian. Dokument 17 aktualizuje wcześniejsze otwarte warianty do zatwierdzonej Local Trusted v1; nie wprowadza nowego workflow Siemens. Historyczne raporty X-01/D-11 i ich wyniki pozostają nietknięte. E1-L to przygotowanie offline, real Engineering evidence NOT_REGISTERED, Engineering BLOCKED/NOT_RUN, B-03B NOT_READY, NF-11 NOT_RUN i Worker Handle Risk UNKNOWN; E2 nie rozpoczęto.

## 2026-10-26 — B-03A/E1-R: resilience artifact binding

**Przyczyna i naprawa:** na czystym `956df6c69922aa73776f1afd6f2f378ff79f375f=origin/main` odtworzono każdy z trzech FAIL osobno oraz dwukrotnie jako grupę. Testowy wrapper Resolve tworzył kolejne proof/template przy rewalidacji E1, nadpisując marker, podczas gdy dispatch używał pierwszego szablonu. Instrumentacja rzeczywistej odpowiedzi potwierdziła różne ArtifactId; uszkadzano niewykorzystany plik. Wrapper zachowuje teraz jedną syntetyczną parę na OperationId, nadal rozwiązując bieżący profil wewnętrzny. Test wiąże marker ID/SHA z odpowiedzią workera. Kod produkcyjny Host, provider E1, HTTP 422, timeout 10 s i kwalifikacyjne progi pozostają bez zmian.

**Regresje:** zachowano pierwotne trzy przypadki i scenario IDs. Dodano cztery procesowe warianty modified/truncated/missing/replacement z poprawnym odczytem przed ingerencją, ośmioma klientami, odrębnymi operacjami i restartem; trzy Storage przypadki błędnego hasha/hash innego pliku/ID innego pliku. Asercje obejmują brak fałszywego 200, rzeczywisty DEGRADED przed completion, trwały receipt, brak terminalnego sukcesu po nieudanej weryfikacji, UNKNOWN/quarantine po restarcie, niezmienioną historię i brak retry. [Raport E1-R](docs/B03A_E1_R_ARTIFACT_REPAIR.md) zawiera przyczynę, pełny przepływ i macierz 15 wymagań. Surowe dowody wyłącznie w ignorowanym `.tia-bridge/`.

**Walidacja:** trzy pierwotne testy po naprawie 3/3 PASS; rozszerzony zestaw artefaktów/Storage i integracji E1 26/26 PASS. Jeden kanoniczny runner `b18c8af2274240ebb714d72c3563a3e0`: Build Release 0 warnings/errors, Core 564/564, Host 264/264, wszystkie .NET 828/828, Python 42/42, zero FAIL/SKIP. E1 19/19, IPC 34/34 (scheduler/IPC 76/76), resilience 52/52 i 49/49 wymaganych records PASS. Generatory, audyt (55 Markdown/474 linki/0 błędów), format i diff check PASS. Niezależna weryfikacja 73/73 plików manifestu i trzech bundle 464/464 próbek PASS. W raporcie zapisano SHA-256 dowodów i przebiegi diagnostyczne wraz z zachowanymi FAIL.

**Oddzielne kwalifikacje:** Functional execution PASS; nowa ograniczona D-11 PASS na C:/X: Fixed/NTFS: wymagane RT/NF PASS, idle 306.1528 s, CPU 0.7821%, 1001 operacji w 442.7659 s, bez błędów krytycznych. Full offline qualification BLOCKED (runner exit 0, resilience gate BLOCKED, NF-11 NOT_RUN; pełny profil skali NF-08 NOT_RUN). Production NOT_RUN. Historycznych raportów, NF-03 exFAT FAIL i NF-08 FAIL nie zmieniano. NF-11 NOT_RUN; Worker Handle Risk UNKNOWN; E1 COMPLETE_OFFLINE; Engineering BLOCKED/NOT_RUN, real evidence NOT_REGISTERED, B-03B NOT_READY. E2 nie rozpoczęto.

**Ocena instrukcji sekcja po sekcji:** AGENTS: zakres/stan, role/hierarchia, niezmienne granice, autoryzacja/testy/D-11/Computer Use, dyscyplina zmian, Git i dane nieufne; Instrukcja_ChatGPT: cel/odpowiedzialność, źródła/hierarchia, projektowanie architektury, standard zadania, kontrola wyników, bezpieczeństwo/recovery, decyzje/spójność, Git/dokumentacja i styl. Obie instrukcje pozostają bez edycji: naprawiono testowy binding w zatwierdzonym offline workflow, bez zmiany architektury, uprawnień, kontraktów, sagi synchronizacji i kryteriów/progów. ADR 0001–0003 oraz specyfikacje 02–09 i kontrakty API/IPC/registry/SQLite pozostają zgodne i bez zmian. Dokumenty Host/artifacts 13, resilience 16 i provider E1 17 opisują granice wykrywania, odrębność historycznego wyniku od integralności i odsyłają do nowego raportu. Nie rozpoczęto E2, B-03B ani Siemens.

## 2026-10-26 — D-11 requalification on current NTFS checkout

**Wejście i preflight:** świeży preflight `31b2e401a9224ce2ace3d5239205b6d1` zakończył się PASS na czystym `main`, `HEAD=origin/main=ff20b79f04e8b62463cd10f7bb681ffbae15aaab`. Zweryfikowano lokalne C:/NTFS dla trwałego stanu, X:/NTFS dla workspace i testów, łańcuchy ścieżek bez reparse points, narzędzia oraz brak testowych Host/Worker/TIA Portal. Krótka obserwacja nie wykazała utrzymującego się wysokiego obciążenia, presji pamięci ani kolejki dyskowej. WCCIL Runtime i Git fsmonitor pozostały uruchomione i zostały zapisane jako ograniczone ryzyko środowiskowe. Surowy preflight i hashe są w ignorowanym `.tia-bridge/d11-requalification/31b2e401a9224ce2ace3d5239205b6d1/`.

**Pojedynczy run:** [raport D-11](tests/resilience/D11_REQUALIFICATION_2026-10-26_31b2e401.md), run `2535d5e5e0584f4b95d7ed5794913c47`, dokładnie jeden kanoniczny `scripts/validate_offline.py`. Build Release 0 warnings/errors; .NET 803/803, Python 42/42, zero FAIL/SKIP; wszystkie kroki runnera PASS. Pełny run manifest obejmuje 73 artefakty; SHA-256 i weryfikacja integralności w raporcie i `.tia-bridge/resilience/<run-id>/`.

**Ocena:** RT-03/05/06/15/16 oraz NF-01/02/03/04/05/06/12/14/15 PASS. Zachowano i zweryfikowano wszystkie 464 próbki NF-02/NF-03, z odtwarzalnymi agregatami, run/source/build binding i hashami. Rzeczywisty idle 306.25 s PASS; krótki przebieg 1001 operacji PASS. Bieżąca ograniczona D-11: **PASS**. Runnerowy `resilience_gate` pozostaje **BLOCKED**, ponieważ NF-11 8 h pozostał NOT_RUN; to odrębne od D-11. Bieżąca pełna kwalifikacja: **NOT_RUN**, a historyczny ostatni pełny FAIL/NF-08, NF-03 exFAT FAIL, NF-11 NOT_RUN i Worker Handle Risk UNKNOWN pozostają zachowane. Produkcja, Engineering i Runtime NOT_RUN.

**B-03A i dokumenty:** D-11 PASS pozwala przygotować osobne, ograniczone zlecenie/autoryzację Engineering B-03A read-only, lecz nie rozpoczyna testów. Produkcyjny Host nadal nie ma zaufanego providera weryfikowanych qualification evidence IDs; Engineering pozostaje BLOCKED/NOT_RUN do osobnego rozstrzygnięcia uprawnień i świeżego preflightu konkretnego projektu. B-03A implementation COMPLETE_OFFLINE, B-03B NOT_READY. Oceniono `AGENTS.md`, `Instrukcja_ChatGPT.md`, właściwe ADR/specyfikacje i kontrakty; pozostają bez zmian, bo ten wpis i nowy raport utrwalają wyniki bez zmiany norm, architektury, uprawnień lub progów. Nie zmodyfikowano kodu, testów ani wcześniejszych raportów. Kontrole po zmianie: audit jakości PASS (53 Markdown, 465 lokalnych linków, 0 błędów), 73/73 manifestowane artefakty i próbki odtworzone, `git diff --check` PASS; staged diff ograniczony do tych dwóch dokumentów. Fetch potwierdził `HEAD=origin/main` przed commitem.

## 2026-10-26 — D-11 preparation: pełne próbki, niezmienne runy i preflight

**Wejście:** `HEAD=origin/main=d114d9eca1a49879dbdbe0d9d500080a3fc6709a`, worktree clean po fetch. Zlecono poprawę harnessu i ograniczoną diagnostykę, bez pełnej kwalifikacji i Siemens. [Raport przygotowania](tests/resilience/D11_PREPARATION_2026-10-26.md) zawiera przepływ pomiarów, zmiany, próbki/agregaty, konfigurację i hashe, diagnostykę oraz dokładną komendę następnego osobnego przebiegu.

**Harness:** PerformanceEvidence zapisuje pełne rzeczywiste kolekcje NF-02/NF-03 i częściowe wyniki po błędzie w formacie 1.1, z ms/wall_clock, N, ID, UTC, Git/dirty, hashami źródeł/DLL/konfiguracji, runtime i profilem NTFS. Stoper, pętle N64/N100, warm-up, pauzy i asercje/progi bez zmian. Wszystkie dowody mają run root `.tia-bridge/resilience/<run-id>/`; CreateNew blokuje overwrite, osobne manifesty i końcowy inventory wiążą pliki przez SHA. Runner zapisuje także logi/TRX/metadata/raporty per-run, bez nadpisywania starych wspólnych ścieżek. Python odtwarza agregaty, sprawdza integralność/binding i odmawia nowych kwalifikacji z brakujących danych. Jawny odczyt legacy zachowuje historyczne agregaty jako INSUFFICIENT bez nowego gate PASS. Reporter nie używa globalnego performance.json; pole ntfs_save_replace_status odzwierciedla istniejący test zamiast stałej PASS, bez zmiany jego warunku zaliczenia.

**Weryfikacja:** final Build Release 0 warnings/errors; run celowany `6813bd8866824ed18a89b7a08dd820ce`: 5/5 .NET PASS (trzy profile pomiarowe i dwa testy helpera), 464 rzeczywiste próbki, niezależna rekonstrukcja agregatów/SHA PASS. Python 42/42 PASS, w tym wszystkie 15 wymaganych klas braków/uszkodzeń, historical compatibility oraz mock orkiestracji, który nie wykonuje żadnego RT/NF. Format verify PASS. Początkowy błąd kompilacji nowego testu CS0619 skorygowano przed finalnym buildem. Nie ponawiano NF dla uzyskania SLO PASS i nie wykonano pełnego runnera. Duże/surowe dowody wyłącznie w ignorowanym `.tia-bridge`.

**Środowisko:** siedem zgłoszonych Git to `fsmonitor--daemon run --detach`, bez zaobserwowanego CPU/I/O w ograniczonych oknach; rodzice już niedostępni, pochodzenia VS Code/CODEX nie zgadywano. Read-only status potwierdza watcher TIA_Bridge, bez przypisania konkretnego PID. Dodatkowe przejściowe wpisy w pierwszym snapshot zachowano, nie zabijano procesów. Pięć snapshotów bez głównego Siemens.Automation.Portal.exe; usługi, ALM, PLCSIM i language server rozdzielone. X:/C: Fixed/NTFS, 20 ścieżek/ancestorów bez reparse, runtime i miejsce PASS. ENVIRONMENT BLOCKED dla kontrolowanego pomiaru przy końcowym nasyceniu CPU 91–100% i intensywnym I/O na C: (m.in. ESET); wcześniejsze lżejsze i cięższe snapshoty zachowano. To ocena konkretnego ryzyka zakłócenia, nie nowy próg NF, wymóg wyłączenia AV ani FAIL D-11. Minimalne działanie: poczekać na ustanie przejściowego obciążenia bez sterowania procesami i ponowić wyłącznie krótki preflight w osobnym kroku. Nie zmieniano Windows/ochrony/usług.

**Historia i granice:** 454 wcześniejsze raporty/artefakty ze snapshotu bez zmiany bajtów; z 455 pozycji planowo zmieniono tylko instrukcyjny resilience README. Historyczne D-11 PASS, ostatnia bieżąca ocena BLOCKED, pełny offline FAIL, NF-03 exFAT FAIL, NF-08 NTFS FAIL, NF-11 NOT_RUN, X-01 93/93 PASS i Worker Handle Risk UNKNOWN zachowano. Dla tego zadania D-11/full qualification/Engineering NOT_RUN; B-03A ENGINEERING BLOCKED, B-03B NOT_READY. Żadnych zmian funkcjonalności B-03A lub live qualification providera, API/IPC/capabilities, TIA, projektów, backupów i fizycznych odbiorców.

**Ocena wpływu sekcja po sekcji:** AGENTS — zakres/stan, role/hierarchia, granice, autoryzacja/testy/D-11, dyscyplina, Git i dane nieufne; Instrukcja_ChatGPT — role/stan, źródła/hierarchia, architektura, standard zadania, kontrola wyników, bezpieczeństwo/recovery, spójność, Git/dokumentacja i styl. Pozostawiono obie bez zmian: rozszerzono dowody w istniejącym workflow, nie zmieniono architektury, autoryzacji, progów lub semantyki D-11. ADR 0001–0003, 02–09 i kontrakty pozostają bez zmian; dokument 16 i instrukcje tests/scripts uzupełniono o aktualne ścieżki/format/legacy. Zmiany nie nadają nowego PASS ani zgód TIA. Audyt PASS (52 Markdown, 460 lokalnych odnośników, zero błędów), diff check i staged review wykonano przed selektywnym commitem/pushem bez force.

## 2026-10-26 — ponowna kwalifikacja D-11, zatrzymanie na preflight

**Wejście:** po fetch `HEAD=main=origin/main=de9f20ffd4057e8afe28a25b4d4067d086a852a8`, worktree clean. Preflight `bab2cef275194d4583894a569ead636c`, UTC 14:07:18–14:09:19; nie nadano qualification run ID i nie uruchomiono runnera.

**Wynik:** D-11 CURRENT LIMITED GATE **BLOCKED**. Punkty 6/9 bieżącego zadania wymagają kompletnych, zachowanych próbek NF-02/NF-03; aktualne ApiPerformanceTests i ResiliencePerformanceTests serializują tylko N/percentyle/max, nie tablice czasów. Zastosowano jawne polecenie z punktu 7, aby wstrzymać kwalifikację przy ograniczeniu harnessu, oraz punkt 12 zakazujący poprawki kodu i ponowienia bez odrębnej decyzji. Nie uruchamiano testu, który z góry nie wytworzy wymaganych dowodów. Nie zmieniono progów, liczby próbek, flush/replace ani reguł D-11.

**Środowisko:** X:/C: lokalne Fixed/NTFS, dostępna przestrzeń i runtime .NET/Python potwierdzone; 16 sprawdzonych katalogów/ancestorów bez reparse. Brak locków/operacji merge/rebase w repo. Widocznych 7 żywych git.exe bez dostępnej klasyfikacji polecenia; CPU/I/O delta 0 przez 1 s nie wystarcza do potwierdzenia braku aktywnych operacji Git. ENVIRONMENT BLOCKED dla tego warunku, bez twierdzenia, że procesy zmieniają repo. Nie zabijano ich i nie zmieniano Windows/AV. Wykonano tylko odczyty środowiska i analizę kodu, zachowano hashe zastanych binariów/źródeł oraz historii.

**Dowody i statusy:** [nowy raport](tests/resilience/D11_REQUALIFICATION_2026-10-26.md) zawiera audyt runnera z odwołaniami do kodu, macierz RT/NF NOT_RUN, konfigurację, dokładne blockers, hashe i propozycję osobnego zadania harnessu. Wszystkie wymagane RT/NF, idle i 1000 operacji NOT_RUN; functional i full offline qualification NOT_RUN w tej próbie, ostatni pełny FAIL zachowany. NF-11 NOT_RUN nie jest powodem blokady ograniczonej D-11. Historyczne X-01 93/93 PASS, D-11 PASS, NF-03 exFAT FAIL, NF-08 NTFS FAIL, B-03A COMPLETE_OFFLINE/Engineering BLOCKED i Worker Handle Risk UNKNOWN bez zmian. B-03A Engineering readiness BLOCKED, B-03B NOT_READY. Lokalny manifest i środowisko wyłącznie w ignorowanym `.tia-bridge/d11-requalification/bab2cef275194d4583894a569ead636c/`.

**Ocena obu instrukcji i specyfikacji:** AGENTS: zakres/stan, role/hierarchia, granice, autoryzacja/testy/D-11, dyscyplina, Git i dane nieufne; Instrukcja_ChatGPT: role/stan, hierarchia/źródła, architektura, standard zadania, kontrola wyników, bezpieczeństwo/recovery, spójność, Git/dokumentacja i styl. Pozostają bez zmian: raport dokumentuje zatrzymanie wymagane bieżącym zadaniem i brak dowodów, nie ustanawia nowego normatywnego progu D-11. ADR 0001–0003, wymagania 02, architektura/sync/interface/integracja 03–06, strategia 07, backlog 08, D-11/ryzyka 09, resilience 16 i kontrakty API/IPC/capabilities pozostają bez zmian. Brak implementacji B-03A/provider, Engineering, Runtime, TIA i poprawki harnessu.

**Kontrola jakości:** audyt PASS (51 Markdown, 443 lokalne odnośniki, zero błędów); dokumentacyjne testy D-11 PASS 6/6; hashe 144 źródeł/kontraktów/skryptów, 12 historycznych raportów, 3 zastanych working reports i 6 DLL bez zmian; `git diff --check` PASS. Kontrole dokumentacji nie są functional execution kwalifikacji. Dostawa dokumentacyjna: tylko nowy raport i ten wpis, selektywny staging, kontrola diffu i normalny push bez force.

## 2026-10-26 — diagnostyka NF-03/NF-02 na X:/NTFS

**Wejście i zakres:** Zadanie A zakończone i zapisane w commicie `ea3d4ff061340d5569290bbe247cbfc050b64a7b`, raporcie B-03A i poprzednim wpisie tego rejestru. Po fetch `HEAD=origin/main`, worktree clean. Wykonano tylko offline filesystem/Host/Fake, bez Siemens Attach, projektów, Engineering i Runtime. Nie zmieniono kodu funkcjonalnego B-03A, testów, progów, reportera ani ustawień Windows/AV/dysku.

**Pomiary:** z góry ustalone 3 serie R (X: replace bez/z Fake Hang), N (C: save/replace przy Hang) i API (NF-01/NF-02 oraz X: in-place), bez odrzucania próbek; każde N100 filesystem, N64/8 submit i N1000 na profil status. Wszystkie 9 niezmienionych testów PASS, bez odmów, błędów i timeoutów. X: replace bez workera p95 4,9672/7,9545/30,3084 ms, maksimum 252,4374 ms w trzeciej serii. NF-02 p95 211,9023/205,8176/232,9689 ms, p99 250,7162/262,2369/276,4433 ms: PASS tylko tych prób. Dodatkowy pojedynczy eksperyment bez Host/Fake, N100 na X: i N100 na C:, rozdzielił write/move/read/compare; na X: uchwycił total 719,8091 ms, z czego File.Move 592,3570 ms. To wskazuje granicę opóźnienia, nie dowodzi winy filesystemu, dysku, AV albo planisty Windows.

**Wnioski i dowody:** [osobny raport diagnostyki](tests/resilience/NF03_NF02_DIAGNOSTICS_2026-10-26.md) zawiera wszystkie agregaty, run IDs, UTC start/end, definicje percentyli, granice pomiarów, środowisko, hashe i ograniczenia. X:/C: potwierdzone Fixed/NTFS, host obciążony przez obce procesy; monitoring zasobów był rzadki i nie koreluje pojedynczych outlierów. Historyczne 298,0946 ms NF-02 nie ma odnalezionego raw bindingu/telemetrii; 156,9138 ms potwierdzono w osadzonym raporcie runnera A, nie w później nadpisanym performance.json. Przyczyna różnicy UNEXPLAINED. Nie wykazano błędu odpowiedzialnego za latencję; testy pozostawiono niezmienione. Raport jawnie wskazuje słabą asercję in-place (długość SHA zamiast porównania treści), która nie wyjaśnia latencji, oraz kandydatów poprawy harnessu bez zmiany kryteriów. Surowe logi/TRX, lokalny runner, program pomocniczy i próbki wyłącznie w ignorowanym `.tia-bridge/diagnostics/nf-20261026/` oraz osobnych resilience UUID; nie są częścią commita.

**Kwalifikacja:** NF-03 CURRENT NTFS PASS w trzech celowanych seriach, powtarzalność względem historycznej zmienności INCONCLUSIVE; NF-02 CURRENT PASS, VARIABILITY UNEXPLAINED. Historyczne D-11 PASS, NF-03 exFAT FAIL, NF-08 CPU 1,0288% FAIL, NF-11 NOT_RUN i Worker Handle Risk UNKNOWN zachowano. Nowa D-11 BLOCKED — brak kompletnej nowej oceny wymaganych RT/NF/idle/1000 operacji; targeted PASS nie zastępuje FAIL ostatniej pełnej kwalifikacji. NF-11 nie jest blockerem ograniczonej D-11. B-03A Engineering readiness BLOCKED, B-03B NOT_READY. Raport przedstawia zakres następnego pełnego zbierania dowodów; nie uruchomiono go ani Engineering i nie nadano nowej zgody.

**Ocena instrukcji sekcja po sekcji:** AGENTS: zakres/stan; role/hierarchia; niezmienne granice; autoryzacja/testy; dyscyplina zmian; Conventional Commits; dane nieufne. Instrukcja_ChatGPT: role i stan; hierarchia/źródła; architektura; standard zadania; kontrola wyników/D-11; bezpieczeństwo/recovery; decyzje/spójność; GitHub/dokumentacja; styl. Obie pozostawiono bez zmian: raport stosuje istniejące zasady do nowych dowodów, nie zmienia workflow, progów, architektury ani uprawnień. ADR 0001–0003, wymagania 02, architektura 03, synchronizacja 04, interfejs 05, integracja 06, strategia 07, backlog 08, decyzja D-11/ryzyka 09, resilience 16 oraz kontrakty API/IPC/capabilities pozostają bez zmian. Brak nowego konfliktu normatywnego; niezmienność historii i osobna ocena bramki zachowane. Historyczny raport B-03A nie jest przepisywany; bieżącą rekomendację zawiera nowy raport.

**Walidacja dokumentacji:** audyt jakości PASS (50 Markdown, 432 lokalne odnośniki, zero błędów), testy dokumentacyjne D-11 PASS 6/6, w tym hashe niezmienionej historii, `git diff --check` PASS. Wyniki i ograniczenia są w raporcie. Commit obejmuje wyłącznie raport i ten wpis; staging po nazwach, kontrola diffu i normalny push, bez force.

## 2026-10-26 — B-03A capabilities PROJECT, regresja Host i raportowanie Unicode

**Wejście:** HEAD=`115c3cd1cc30fef21a84967bd324dc593499b55b`, `origin/main` zgodny, worktree clean. Zachowano NF-08 (FAIL przy 1,0288% CPU idle, próg 1,0%), NF-03 exFAT FAIL, NF-11 NOT_RUN, X-01 historyczne PASS i Worker Handle Risk UNKNOWN.

**Capability compatibility:** registry/Core/schema v1 pozostają niezmienione. Dodano kontrakt v2 tylko dla capability registry: `OperationScope` PROJECT/DEVICE/OBJECT jest osobny od `DeviceFamily`; `project.inspect` jest PROJECT, wymaga null family i pustego write scope. V2 wiąże dokładną wersję TIA, SHA builda Worker, adapter/build, operację, profil i listę getterów. Dokładne dopasowanie nie dziedziczy PROJECT do DEVICE/OBJECT ani READ do EXPORT/IMPORT/WRITE. HTTP API v1, IPC envelope, Workspace, SYNCED i ADR 0003 pozostają bez zmian. Host ładuje registry v2 ze stałej ścieżki w state; brak registry lub dowodu kwalifikacji oznacza odmowę przed uruchomieniem workera. Provider zaufanych evidence IDs nie jest zaimplementowany, więc konfiguracja syntetyczna nie przyznaje live dostępu.

**Host flaky:** `ACCEPTED` po wyczerpaniu krótkiego wait CLI jest prawidłowe: operation intent jest trwały, dispatch jest asynchroniczny, a stan RUNNING pojawia się po jego przygotowaniu. Test zachowuje brak cancel i akceptowane stany po bounded wait, a następnie czeka na RUNNING przez deadline związany z budżetem uruchomienia workera; nie wydłużano opóźnienia arbitralnie i nie usunięto gwarancji.

**Unicode:** `validate_offline.py` przełącza stdout/stderr na UTF-8 strict przed raportowaniem, logi nadal są jawnie UTF-8 strict. Dodano test uruchamiający Python z `PYTHONIOENCODING=cp1252`, `PYTHONUTF8=0`; raport zawiera znaki Unicode bez zastępowania. Nie tłumimy wyjątku kodowania.

**Wpływ dokumentów:** przejrzano `AGENTS.md`, `Instrukcja_ChatGPT.md`, ADR 0001–0003, kontrakty v1/v2, plan, capabilities i test strategy. Obie instrukcje i ADR pozostawiono bez zmian; wersjonowany dodatek v2 jest kompatybilny z ich granicami. Aktualizacje capabilities, B-03A, test README, backlog i tego rejestru zapisują zasadę PROJECT, deny-by-default i otwarty provider kwalifikacji.

**Walidacja:** finalny pojedynczy `scripts/validate_offline.py` uruchomiono z początkową konsolą CP1252. Locked restore/Build Release 0 warning/error; .NET 801/801 (Host 255/255, Core 546/546), Python 33/33, generatory, audyt jakości, format, diff-check PASS; `test_execution_status=PASS`. Unicode raportowanie PASS. `resilience_gate=FAIL`, więc pełny runner zwrócił kod 1: NF-03 repo volume X: fail (without-worker replace p95 115.8212 ms), NF-11 NOT_RUN; blockers formal qualification zachowano bez waivera. NF-08 historyczne FAIL 1,0288%/1,0%, NF-03 exFAT FAIL i NF-11 NOT_RUN bez zmian. Wcześniejszy jednorazowy pełny .NET test zmierzył NF-02 p95 298,0946 ms; izolowane powtórzenie i finalny runner przeszły NF-02 (156,9138 ms), bez zmiany progu. BUILD/CORE/Host offline zachowują pełne wyniki; FULL OFFLINE RUNNER/B-03A OFFLINE oznaczone FAIL z powodu bramki resilience. Nie uruchamiano TIA/Siemens, nowych testów Engineering ani Runtime.

## 2026-10-26 — B-03A Discovery/Inventory, implementacja offline

**Wejście:** `X:\Projects\TIA_Bridge`, HEAD=`ea7a616d554805bf38860e4b8261507b4d5f046c`, `origin/main` zgodny, worktree clean. `git fsck --full --no-reflogs` wykazał jeden dangling blob bez referencji; nie zmieniano historii ani obiektów Git. X-01 bazowy status zachowano.

**Zakres:** rozbudowano istniejące IPC DTO/worker/projection bez drugiego Attach, sesji, schedulera ani IPC. DiscoveryV1 dodaje Device jako root, jawne parent/device observation ordinals, oryginalne nazwy, exact PLC/HMI Unified/HMI Classic classification, UNKNOWN dla braku Software, limity 64 urządzeń/256 węzłów/głębokości 8, walidację relacji i typed Core snapshot z coverage. Ordinals są lokalne dla jednego artefaktu, nie stanowią ID. Brak publicznych stabilnych ID pozostawia ObjectId niezdefiniowany. Nie zmieniano HTTP v1, ProjectInspectResult, schema API, mutacji ani X-01 historycznych fixtures/raportu.

**Ocena instrukcji/ADR/kontraktów:** sekcja Zakres i stan `AGENTS.md` niezgodnie twierdziła, że Host/Worker jeszcze nie istnieją; skorygowano do bieżącego X-01/B-03A. `Instrukcja_ChatGPT.md` przejrzano sekcja po sekcji i pozostawiono bez zmian: role, autoryzacja i oddzielenie Engineering/Runtime pozostają zgodne. ADR 0001/0002 bez zmian; architektura Core/Worker, exact binding, serializacja i durable engine zachowane. ADR 0003 bez zmian; HMI ograniczono do klasyfikacji. Kontrakty HTTP i IPC 1.1 field-set bez zmian; metadane Discovery kodowane są w wersjonowanym istniejącym `Locator`, a nie w dodatkowych polach IPC. Otwarte pozostaje podłączenie `CapabilityRegistry`: jego obecny klucz zakłada znaną DeviceFamily, podczas gdy project-level Discovery musi obsłużyć UNKNOWN przed rozpoznaniem rodziny. Raport B-03A opisuje rekomendację i nie ukrywa braku kwalifikowanego wpisu Siemens.

**Walidacja:** final Release build `TiaBridge.slnx` oraz `TiaBridge.Engineering.slnx` PASS (0 warnings/errors), `V19OfflineTests` 44/44 PASS, Python contracts 31/31 w isolated QA environment, `audit_quality.py`, format verify oraz `git diff --check` PASS. Pełny .NET: Core 538/538 PASS; Host 253/254, jeden test `Explicit_wait_is_bounded_and_cli_exit_does_not_cancel_the_operation` otrzymał ACCEPTED zamiast RUNNING. Natychmiastowe ponowienie tego testu przeszło 1/1; pierwsze niepowodzenie pozostaje w raporcie jako niestabilność, cała regresja nie jest PASS. Poprzedni full offline runner zaliczył Host 251/251, Core 538/538, Python 31/31 i audyt/generatory/format, lecz zakończył `FAIL` z powodu Windows `charmap` w końcowym raporcie. Engineering dla nowych getterów `BLOCKED/NOT_RUN`; nie potwierdzono nowych procesów/projektów i nie rozszerzano autoryzacji X-01. Runtime `NOT_RUN`. Kod próbkuje uchwyty, Private Bytes i Working Set workera przed i po operacji, ale dla nowego builda nie wykonano odczytu Engineering ani testu stabilizacji; `WORKER HANDLE RISK: UNKNOWN`.

Szczegóły, macierz X-01 → B-03A, listę getterów i ograniczenia zawiera [raport kwalifikacji B-03A](docs/B03A_DISCOVERY_QUALIFICATION.md).

## 2026-10-26 — końcowa kwalifikacja X-01 na X:/NTFS

**Wejście:** aktywny checkout `X:\Projects\TIA_Bridge`; przed zmianą czysty `main`, `HEAD=origin/main=e6b659708c6141ad89be4ea109a64a8e76109a3d`; implementacja X-01 `9e8b7bfadb1f4e7b0fc3a793d01cbb82f54e5c07`. `Get-Volume` potwierdził X: jako lokalny Fixed/NTFS. State oraz Workspace w pomiarach również wskazały NTFS. Nie otwierano TIA ani projektów.

**Pełny runner:** odizolowany QA Python uruchomił `scripts/validate_offline.py` z aktualnego checkoutu. Run `f116dd23c7aa480995b5a69077f4db35`, fingerprint LF źródeł `f4bd013d908d7f77a777f5741c77f402046cd9c8b459be07d13d795d9cb55ea7`. Locked restore/build PASS; Build Release 0 warning/error. .NET 772 PASS / 1 FAIL / 0 SKIP z 773; Python 31/31 PASS; generatory, audyt jakości, format i diff PASS. Jedyny FAIL: pięciominutowy idle NF-08 zmierzył CPU 1,0287988877% przy istniejącym progu 1,0%. Próg i test nie zmieniano. NF-03 workspace/repo X:/NTFS: 100 kontrolnych próbek bez workera p95 4,5592 ms; 100 paced 1 MiB samples podczas Fake Hang p95 6,5635 ms, p99 87,0439 ms; refused=0. Historyczny exFAT FAIL zachowano.

**Oddzielne oceny:** aktualna pełna kwalifikacja offline NTFS = FAIL; limited D-11 = PASS według jawnego zakresu 07/08: RT-03/05/06/15/16 i NF-01/02/03/04/05/06/12/14/15 PASS, idle 306,410 s oraz 1001 operacji PASS. NF-08 CPU failure pozostaje widoczny jako pełny profil, ale NF-08 nie należy do zestawu NF ograniczonej D-11; nie wykryto krytycznego błędu wykonania/izolacji. Istniejący ogólny reporter zachował `x01_readiness=NOT_READY`, bo konserwatywnie propaguje dowolny FAIL testu oraz nadal ma historyczną regułę agregacji NF-11. Reporter/thresholds nie modyfikowano; osobna ocena uzasadnia D-11 z jego zatwierdzonym zakresem. Engineering 93/93 PASS jest wcześniejszym dowodem, nie był powtarzany. Runtime NOT_RUN, NF-11 NOT_RUN, produkcyjna kwalifikacja nieukończona.

**Zasoby i rekomendacja:** historyczny Worker V19 miał STA handles 462→702 i MTA 456→602. Dispose i zakończenie własnych procesów przeszły, lecz przyczyna wzrostu pozostaje nierozstrzygnięta. Status ryzyka `UNKNOWN`; nie deklarujemy naprawy ani zamknięcia R-14. Rekomendacja B-03A `READY` wyłącznie dla ograniczonego, obserwowanego read-only; surowy/ciągły zakres i produkcja nie są gotowe. Dodano propozycję osobnego pomiaru cykli i kryteriów przed rozszerzeniem reuse Workera. Nie rozpoczęto B-03A.

**Dowody:** nowy, oddzielny raport [NTFS requalification](tests/resilience/X01_NTFS_REQUALIFICATION_2026-10-26.md) i JSON (źródłowy SHA, profil, statusy oraz hashe lokalnych surowych artefaktów); historyczne qualification snapshots pozostają niezmienione. Bieżące `.tia-bridge/offline` oraz `.tia-bridge/resilience` przed uruchomieniem skopiowano do ignorowanego archiwum `pre-run-5b99356133a24fe5a84ccf893fcb9bfe`; runner pracował z osobnym UUID. Brak surowych logów, danych projektu, DLL lub credentiali w Git.

**Ocena instrukcji i decyzji:** porównano sekcja po sekcji AGENTS: zakres/stan, hierarchia, niezmienne granice, autoryzacja/testy, dyscyplina zmian, Conventional Commits i dane nieufne; oraz Instrukcja_ChatGPT: stan/role, hierarchia, workflow, standard zadania, bezpieczeństwo, bramki, dowody i zamknięcie. Pozostawiono oba dokumenty bez zmian: to raport wyniku istniejących kryteriów, nie zmiana workflow, autoryzacji, architektury, kontraktu ani progu. ADR 0001/0002, 07/08/09/16 pozostają semantycznie bez zmian; dopisano wyłącznie nowy odrębny wynik i linki, zachowując historyczne raporty. Nierozstrzygnięte kwestie handle growth pozostają jawnie UNKNOWN; nie są sprzecznością architektury/bezpieczeństwa wymagającą decyzji przed ograniczonym B-03A.

**Walidacja po dokumentacji:** pełny runner powyżej poprzedzał dodanie raportu i edycje dokumentów. Po zmianach wykonano lokalny JSON parse, test dokumentacyjnej zgodności D-11, `py -3 scripts/audit_quality.py`, diff/link review; wyniki zapisano po walidacji. Brak nowych testów Engineering, TIA/Runtime, zmian produkcyjnego Workera, testów progów i NF-11.

```text
REPOSITORY: X:\Projects\TIA_Bridge
FILESYSTEM: NTFS
BUILD RELEASE: PASS
OFFLINE CURRENT NTFS: FAIL (1 NF-08 idle CPU threshold)
HISTORICAL exFAT: FAIL
X-01 LIMITED D-11: PASS
X-01 ENGINEERING: PASS (93 historical; not rerun)
NF-11: NOT_RUN
WORKER HANDLE RISK: UNKNOWN
B-03A READINESS: READY (bounded read-only only)
```

## 2026-10-26 — przenośna Instrukcja_ChatGPT i zgodność z ADR 0003

- Skrócono `Instrukcja_ChatGPT.md` z 17 477 do 7 818 znaków Unicode (łącznie ze spacjami i nowymi liniami; limit 8 000: PASS). Usunięto zależność od lokalnej ścieżki i opisy dawnych stanów jako bieżących.
- Zmieniono warunkowe odniesienie HMI na zatwierdzony ADR 0003 / D-12; wyjaśniono, że ogólne warunki `SYNCED` określa dokument 04, także dla stanu osiągniętego bez Apply; wymagane uprawnienie TIA wskazano osobno od planu lub grantu.
- Zachowano wszystkie reguły bezpieczeństwa, D-11, historyczne wyniki X-01 i treść ADR 0003. `AGENTS.md`, kod, Worker, kontrakty, testy i dane kwalifikacyjne bez zmian.
- Sprawdzono odnośniki lokalne w zmienionych dokumentach. `scripts/audit_quality.py` oraz testy lokalne/Siemens: NOT_RUN (operacje wyłącznie przez GitHub).

## 2026-10-26 — synchronizacja podsumowania X-01 po scaleniu PR

Po scaleniu PR #1/#2 doprecyzowano README.md do zgodności z raportem NTFS: pełny offline NTFS FAIL (NF-08), historyczny exFAT NF-03 FAIL, D-11 LIMITED GATE PASS, Engineering 93/93 PASS, NF-11 NOT_RUN, ryzyko handles UNKNOWN i B-03A READY wyłącznie dla ograniczonego read-only. Nie zmieniano testów, progów, historycznych raportów ani danych kwalifikacyjnych.

## 2026-10-26 — D-12 / ADR 0003: kanoniczny model HMI i granica artefaktów Siemens

Zatwierdzono dokumentacyjnie sześć reguł HMI: niezależny kanoniczny model, nienaruszalne NATIVE w ExchangeUnit, zakaz zastępowania nieznanych pól z częściowego odczytu, precyzyjne capabilities, kwalifikacja ryzykownego importu i rozwój na podstawie dowodów. Zmieniono wyłącznie ADR 0003, 03/04/06/08/09, README i ten rejestr. D-11, PLC MVP, X-01, istniejące kontrakty, kod, testy i historyczne dowody bez zmian.

Ocena instrukcji sekcja po sekcji: AGENTS.md (zakres/role/granice/autoryzacja/testy/dyscyplina/kontrakty) i Instrukcja_ChatGPT.md (stan/role/hierarchia/workflow/zadania/bezpieczeństwo/ocena wyników) nie wymagają modyfikacji: nowy ADR konkretyzuje już wymagane deny-by-default, coverage, izolację vendor i zakaz nieuprawnionej rekonstrukcji, bez nowych uprawnień i bez zmiany ich semantyki. ADR 0001/0002, dokument 12 i contracts/v1 zachowane. Ryzyka częściowego HMI i częściowego importu są już ujęte w R-04/R-02, bez dublowania. Nie wykonywano testów TIA, Engineering/Runtime; audyt skryptem lokalnym pozostaje NOT_RUN (brak repo/terminala lokalnego).

## 2026-10-26 — weryfikacja klonu X: i audyt odwołań

**Stan repozytorium:** checkout `X:\Projects\TIA_Bridge`; przed fetch i po nim HEAD `9e8b7bfadb1f4e7b0fc3a793d01cbb82f54e5c07`, `origin/main` ten sam SHA; `main` śledzi `origin/main`, różnica 0/0. Remote fetch/push: `git@github.com:KrzysztofOle/TIA_Bridge.git`, ten sam projekt co zadany URL HTTPS. `git fetch origin` zakończył się bez aktualizacji; brak lokalnych commitów do wysłania, zmian wejściowych, dodatkowych gałęzi/worktree, konfliktów i stanów operacji Git. Submoduły nie są używane; Git LFS jest zainstalowany, ale repo nie ma plików LFS.

**Wolumin X:** `X:` = NTFS, Fixed, partycja Basic nr 3 fizycznego dysku Micron 1100 SATA 256 GB; nie jest mapowaniem SMB ani urządzeniem USB/wymiennym. Wolumin online/healthy, ok. 163 GB wolne w chwili kontroli. ACL katalogu repo ma właściciela bieżącego użytkownika; tymczasowy zapis, `os.replace` w tym samym woluminie i odczyt kontrolny PASS, pliki próbne usunięto. `fsutil fsinfo volumeinfo X:` zwrócił odmowę dostępu; typ NTFS potwierdzono niezależnie przez `Get-Volume`.

**Audyt repozytorium:** aktywne opisy starej lokalizacji w `README.md`, `Instrukcja_ChatGPT.md`, `src/TiaBridge.Host/README.md`, `docs/03_ARCHITECTURE.md`, `docs/09_RISKS_AND_DECISIONS.md`, `docs/16_RESILIENCE_RECOVERY.md`, `docs/02_REQUIREMENTS.md` i `tests/README.md` uogólniono, usuwając założenie, że repo/workspace nadal leży na M:/exFAT. Ścieżki do trwałego state na NTFS pozostają poza repo.

| Pozostawione odwołanie | Kategoria | Powód |
|---|---|---|
| `docs/10_EVIDENCE_AND_QUALITY.md`, wcześniejsze wpisy `Zmiany.md`, `docs/X01_TIA_V19_QUALIFICATION.md`, historyczny `tests/engineering/x01-qualification.json` | C — historyczne dane | Opisują checkout, pomiary, konfigurację M:/exFAT i dowody z czasu wykonania; nie wolno ich przepisywać jako bieżącego środowiska. |
| `tests/integration/TiaBridge.Host.Tests/MigrationAndPolicyTests.cs` — syntetyczny `M:\\_Moje_Programy\\TIA_Bridge\\state` | C — fixture testowy | Ujemny przypadek testuje odmowę niezatwierdzonej lokalizacji; nie wskazuje aktywnego workspace ani magazynu. |
| VS Code `globalStorage/storage.json` i `workspaceStorage` zawierają X: jako bieżący workspace oraz M: jako wcześniejszy wpis | E — lokalna historia IDE | Nie jest to konfiguracja uruchomieniowa projektu. Nie zmieniano ustawień użytkownika ani historii VS Code. `settings.json`, tasks/launch, dostępne zmienne środowiskowe, usługi i zadania planowane nie ujawniły aktywnej konfiguracji TIA_Bridge ze starą ścieżką. |
| `bin/`, `obj/`, `.vs/`, `.tia-bridge/` | D — generowane/lokalne | Pominięte w ręcznym audycie edycyjnym; nowe buildy i dowody pozostały ignorowane. Nie są wymagane czyszczenie ani odtwarzanie konfiguracji. |

W aktywnych instrukcjach nie pozostało pełne odwołanie `M:\\_Moje_Programy\\TIA_Bridge`. Żadnych danych historycznych, testowych TIA, SQLite, instalacji Siemens ani DLL nie przenoszono. Nie znaleziono innych aktywnych bezwzględnych ścieżek do starego repo. Stare wpisy M: w historii workspace VS Code można usunąć ręcznie z listy ostatnich folderów, jeśli użytkownik nie chce ich zachować.

**Kontrola architektury i instrukcji:** `docs/03_ARCHITECTURE.md`, `docs/04_SYNCHRONIZATION.md`, `docs/07_TEST_STRATEGY.md`, `docs/09_RISKS_AND_DECISIONS.md` i raport X-01 sprawdzono pod kątem separacji repo/state/workspace, profili NTFS/exFAT oraz granic read-only. D-11, progi, architektura, worker i historyczny X-01 nie uległy zmianie.

| Dokument | Ocena spójności |
|---|---|
| AGENTS: Zakres i stan; role i hierarchia | Nowy checkout jest celem, stary checkout pozostał wyłącznie materiałem historycznym; brak kopiowania danych. Zasady spójne, bez zmian. |
| AGENTS: Niezmienne granice; Autoryzacja i testy | Uruchomiono wyłącznie OFFLINE na Fake i syntetycznych danych; TIA/Engineering/Runtime bez zmian. Granice zachowane. |
| AGENTS: Dyscyplina zmian; standard commitów | Zmiany ograniczone do dokumentacji i wpisu audytowego; zakres Git sprawdzony, obowiązuje Conventional Commit po angielsku. |
| AGENTS: Dane nieufne i stabilność kontraktów | Kontrakty i parsery niezmienione. |
| Instrukcja_ChatGPT §1 — cel i stan | Nagłówek lokalizacji zmieniono na katalog bieżącego checkoutu; nie przypina instrukcji do litery dysku. |
| Instrukcja_ChatGPT §2–5 — odpowiedzialność, źródła, workflow i zadania | Bez zmian semantycznych; hierarchia oraz workflow pozostają zgodne z AGENTS. |
| Instrukcja_ChatGPT §6–8 — architektura, pakiety, autoryzacja | Granice Core/Worker i uprawnienia Siemens bez zmian; trwałe dane opisano niezależnie od M:. |
| Instrukcja_ChatGPT §9–12 — raporty, sekwencja, styl i kontrola spójności | Kryteria statusów zachowano; ten wpis dokumentuje ocenę wpływu i wyniki. |

**Weryfikacja:** .NET SDK 10.0.402 i Python 3.12.2 dostępne. Locked restore oraz Build Release PASS (0 warnings/errors). OFFLINE: 773/773 testów .NET PASS (235 Host, 538 Core), 31/31 testów Python PASS; generatory PASS; `audit_quality.py` PASS; NF-03 `repository-replace-control` na X:/NTFS PASS: 100 próbek 1 MiB bez worker (p95 6,1634 ms) i 100 podczas Fake Hang (p95 4,4434 ms), bez odmów. Idle-300s test PASS; nie zmienia to statusu historycznych testów X-01/D-11 ani nie jest kwalifikacją Engineering/Runtime. Początkowy `dotnet format` wykazał wyłącznie CRLF wygenerowane przez globalne `core.autocrlf=true`, sprzeczne z `.editorconfig` LF. Dla tego klonu ustawiono lokalnie `core.autocrlf=false`, odtworzono z indexu niezmienione pliki C# i ujednolicono LF w zmienionych dokumentach; ponowny `dotnet format whitespace --verify-no-changes` oraz `git diff --check` PASS. Pełny runner zgłosił FAIL na kroku formatowania przed korektą lokalnego checkoutu; nie zmieniono progów ani źródeł C#.

**Ocena wpływu:** zaktualizowano tylko opisy lokalizacji i statusu profilu exFAT. Architektura, synchronizacja, autoryzacja, test strategy, ADR, D-11 i historyczne evidence bez zmian. PASS dotyczy wyłącznie opisanych testów OFFLINE na X:/NTFS; Engineering i Runtime pozostają NOT_RUN.

## 2026-10-26 — X-01: izolowany Siemens V19 read-only

Stan wejściowy: czysty main/origin main `eca8a079b04001c792c0669b991edfbeb6faaca6`. Osobne jawne zadanie użytkownika autoryzuje odczyt dwóch otwartych projektów testowych V19 pod X:\TESTY_TIA_Bridge. D-11 ogranicza profil do lokalnego NTFS; nie zmieniono historycznych QUALIFICATION/qualification.json lub ograniczonej bramki. NF-03 exFAT FAIL, NF-11 NOT_RUN/B-10B i RUNTIME NOT_RUN pozostają odrębne.

- Dodano Worker.V19 net48/x64, resolver czterech dokładnych assembly z chronionej instalacji Siemens, exact PID/start/SID/AP19 binding, jednego właściciela STA/MTA, audit przed wywołaniami i bounded metadata/inventory. Nie dodano żadnego mutatora ani wywołania TiaPortalProcess.Dispose. Zwolnienie własnego TiaPortal po Attach nie zamyka TIA.
- Ten sam Host/Core/API/CLI/SQLite/outbox/scheduler obsługuje realny odczyt. Wspólne IPC ma wariant net48 i jawne 1.1 z immutable input artifact, bez Fake success template. Brak downgrade, retry native call i automatycznego sukcesu po timeout/late/old epoch. Realne metadane i częściowe Coverage są zweryfikowanymi artefaktami; brak eksportera lub pełnego modelu PLC/HMI jest jawny.
- Naprawiono również klasyfikację mieszanego native Device: zamiast nadpisywać PLC przez ostatnio odwiedzony HMI, rodzic ma UNKNOWN, a oba typy software pozostają w bounded inventory. Syntetyczna regresja obejmuje kolejność oraz nieznane typy. Naprawiono wykryte błędy guardu AP19 (separator końcowy po ValidateRoot) i coverage_refs (referencje muszą wskazywać trwałe artefakty, nie opisy). Dodano 25 regresji offline oraz niezależną walidację dziewięciu komunikatów IPC 1.1. Core i kontrakty publicznych 19 metod nie zmieniły się. Framework reference assemblies pobrano standardowym NuGet, bez instalacji systemowej; nie zmieniano grup lub zabezpieczeń Siemens.
- Osobny harness Engineering używa rzeczywistych procesów Host/CLI/V19 oraz istniejących barier StorageProbe dla utraty IPC/rodzica, starego epoch, late response i cancel. Naprawiono jego oczekiwanie 202 przy idempotentnym HTTP 200 oraz współdzielenie audit file podczas kontrolowanego odczytu. Cleanup zwalnia wyłącznie własne bariery; nie zabija TIA ani nie zakłada przerwania vendor call.
- Użytkownik wybrał B — samodzielną obsługę dialogu Siemens. Nie stosowano Computer Use lub globalnej whitelisty. Pierwszy Attach z timeoutem pozostaje FAILED w historii; późniejsze powodzenie nie zmienia pierwotnego wyniku. [Raport X-01](docs/X01_TIA_V19_QUALIFICATION.md) zawiera osobne dowody obu instancji, próby nieudane, pomiary i końcowy zakres kwalifikacji. Surowe logi/projekty/vendor DLL/discovery/tokeny i DB pozostają poza Git.

Wyniki końcowe X-01: oba projekty mają rzeczywiste Attach/read/Dispose PASS; 80 cykli STA/MTA + 7 scenariuszy awarii + 4 testy startup/loader + 2 odczyty po końcowym guardzie = 93 przypadki Engineering PASS. Metadane plików i IsModified przed/po bez zmian. Własne procesy kończą się, portale pozostają otwarte. Zarejestrowano wzrost uchwytów workera (STA 462→702, MTA 456→602); R-14/8 h soak nie został zamknięty. Runtime NOT_RUN.

Build Release offline/Engineering: PASS, 0 warning/error. Pełny validate_offline.py run a901583fdab74745bebadad476794a39: 763/764 .NET PASS, 1 FAIL (NF-03 M:/exFAT edit p95 178,5496 ms), 31/31 Python PASS; wymagane RT PASS. Po końcowych zmianach 25/25 V19OfflineTests PASS, w tym 9 przypadków nieobecnych we wcześniejszym pełnym buildzie. Unikalnie 772 PASS / 1 FAIL / 0 SKIP z 773 .NET; nie udajemy pojedynczego pełnego uruchomienia 773. Ponowne Python/audit/format/diff PASS. Historyczne snapshoty B-02D/D-11 niezmienione. Pełny OFFLINE i X-01 OVERALL: FAIL z powodu kryterium pełnej regresji; ENGINEERING w ograniczonym NTFS: PASS, B-03A READINESS: NOT_READY. Jawny zakres częściowy jest dozwolony przez zadanie użytkownika; nie zadeklarowano pełnego X-01 PASS ani nie rozpoczęto B-03A.

Ocena wpływu instrukcji i semantyki — obie instrukcje pozostawiono bez zmian:

| Sekcje | Ocena i uzasadnienie |
|---|---|
| AGENTS: Zakres i stan; Instrukcja §1–2 | Historyczny stan odniesienia nie jest deklaracją bieżącej implementacji; aktualny stan i granice X-01 zapisano w README/raporcie. Nie zmieniono podziału ról. |
| AGENTS: Role/hierarchia; Instrukcja §3–5 | Wykonano jedno jawnie zlecone X-01 po zatwierdzonej D-11; nie rozszerzano architektury ani autoryzacji. |
| AGENTS: Niezmienne granice; Instrukcja §6–7 | Vendor tylko net48/x64, jeden owner, wersjonowane DTO, trwały intent i nieblokujące API zachowano; cały B-03A nie jest ogłoszony jako wykonany. |
| AGENTS: Autoryzacja i testy; Instrukcja §8–10 | Read-only i osobne Engineering/Runtime, UNKNOWN/quarantine, default deny, brak replay, brak PLC/download pozostały obowiązkowe. GUI obsługiwał użytkownik zgodnie z B. |
| AGENTS: Dyscyplina/Conventional Commits; Instrukcja §11–12 | Nowe skrypty i live lane mają instrukcje; audyt/linki/format/diff i named-path staging są wymagane; raport zawiera ograniczenia zamiast pozornych PASS. |
| AGENTS: Dane nieufne; ADR 0002 i specyfikacje 03/04/05 | Zachowano rozmiary/głębokość, HMAC/epoch/fence i tożsamość Core; 1.1 jest jawnym rozszerzeniem wewnętrznego IPC. Brak zmian lifecycle, synchronizacji, grantów lub definicji SYNCED. |

## 2026-10-26 — D-11: ograniczona bramka X-01 read-only i pełna kwalifikacja

Stan wejściowy: czysty main/origin main `51d859f5b361b614672ccf9abb59a05606c461c5`. Użytkownik zatwierdził propozycję z dokumentu 16: ograniczony X-01 read-only i pełna kwalifikacja stabilności mają oddzielne kryteria. Zadanie wyłącznie dokumentacyjne; bez X-01, uruchamiania TIA/Openness, procesów Host/Fake, zmian kodu produkcyjnego, dysków, przenoszenia baz, cleanup B-02C lub dostępu do projektów na X:.

- Przeczytano i porównano obie instrukcje, README, dokumenty 02/03/07/08/09/16, oba historyczne snapshot, instrukcję harnessu i historię. Niejednoznaczność dotyczyła ogólnego minimum 8 h w 07, pełnej bramki w 08, odesłań obu instrukcji do 07/08 i oczekującej decyzji w 16/09/README/test instructions. R-14 już przypisywał soak do B-10B. [Dokument 16](docs/16_RESILIENCE_RECOVERY.md#historia-rozbieżności-i-odnośnik-archiwalny) zawiera wykaz miejsc i historię rozbieżności.
- D-11 rozdziela B-02 OFFLINE TEST EXECUTION, B-02 OFFLINE QUALIFICATION, X-01 LIMITED READ-ONLY GATE i PRODUCTION QUALIFICATION. Wymagane RT-03/05/06/15/16, recovery/UNKNOWN/quarantine/epoch, brak replay niepewnej mutacji i właściwe NF-01/02/03/04/05/06/12/14/15 obowiązują nadal. Idle ≥5 min i ≥1000 operacji są osobnymi dowodami, bez pozornego zaliczenia pełnego NF-08/NF-11. NF-11 ≥8 h pozostaje obowiązkowy w B-10B; R-14 aktywne.
- Historyczne QUALIFICATION.md i qualification.json zachowano bajtowo. [Nowa ocena](tests/resilience/X01_LIMITED_GATE.md) i JSON decyzji przypinają ich SHA-256 i źródłowy run. Faktyczne stare wyniki: 748 .NET/24 Python PASS, OFFLINE/GATE FAIL, X-01 NOT_READY, NF-03 exFAT FAIL, NF-11/ENGINEERING/RUNTIME NOT_RUN nie są zmieniane. Aktualna ocena ograniczonej bramki: PASS tylko dla udokumentowanego profilu lokalnego NTFS; pełna kwalifikacja offline badanego profilu: FAIL; produkcja: NOT_RUN. Reporter/matrix pozostają historyczne; nie są aktualnym interpretatorem D-11 ani przepustką dla nowego kodu/środowiska.
- Zweryfikowano istniejące dowody: wymagane RT PASS, 306,2474483 s idle i 1001 operacji/513,2943582 s. NTFS save/replace 1 MiB/N100/co 100 ms podczas Hang p95 3,2648 ms; repo exFAT kontrolne p95 205,5911 ms pozostaje FAIL. Bez delete+copy, relokacji lub osłabienia ACL. Dodatkowo sprawdzono wskazane regresje watchdog, queue deadlines i default deny, nie tylko ogólną deklarację reportera.
- Odnotowano deklarację użytkownika o dwóch projektach V19 w X:\TESTY_TIA_Bridge i zgodzie na ich przyszłe zmiany/utratę w osobnych eksperymentach. Filesystem X: NIEZWERYFIKOWANY; brak dostępu do projektów w tym zadaniu. Nie jest to izolacja PLC/HMI lub zgoda na download. Pierwszy X-01 read-only wymaga odrębnego zadania/autoryzacji i weryfikacji środowiska; późniejsze mutacje mają własne bramki.
- Dodano sześć testów wyłącznie zgodności dokumentacyjnej decyzji z niezmiennymi dowodami, bez uruchamiania silnika. Nie zmieniono narzędzi agregujących kwalifikację ani implementacji Host/Scheduler/Worker. Dotychczasowe wyniki pomiarów nie zostały ponownie wykonane w tym zadaniu.

Ocena instrukcji sekcja po sekcji i dokumentów normatywnych:

| AGENTS.md / Instrukcja_ChatGPT.md | Decyzja i uzasadnienie |
|---|---|
| Zakres i stan / §1–2 | Bez zmian: zadanie ograniczone do zatwierdzonej decyzji dokumentacyjnej; stan implementacji/dowodów opisuje README. |
| Role, hierarchia / §3 | Bez zmian: jawna decyzja użytkownika rozstrzyga zgłoszony konflikt; nie rozstrzygano samodzielnie nowej semantyki. |
| Niezmienne granice / §6 | Bez zmian: Core/vendor boundary, trwałość, UNKNOWN, quarantine, brak replay i SYNCED pozostają obowiązkowe. |
| Autoryzacja i testy / §4–5, §7–8 | AGENTS otrzymuje jeden punkt doprecyzowujący ograniczoną bramkę i lokalny NTFS. Dotychczasowe obowiązkowe scenariusze zachowano. Workflow, granice zadań, read-only, uprawnienia, backup, download/PLC/HMI i Computer Use bez zmian. |
| Bramka / §10 | Instrukcja §10 otrzymuje jedno doprecyzowanie D-11 zgodne z AGENTS i 07/08; NF-11 w B-10B, exFAT nadal niezakwalifikowany. PASS Fake nie dowodzi Siemens; osobne zadanie i autoryzacja X-01. |
| Dyscyplina, dane nieufne / §9, §11–12 | Bez zmian: ocena wpływu zapisana tutaj, źródła historyczne przypięte hashami, niewykonane testy pozostają NOT_RUN. |
| Git / §5, §11 | Bez zmian: jeden angielski Conventional Commit, jawne ścieżki/staged diff i normalny fast-forward push. |
| Architektura 03, ADR 0001/0002, kontrakty | Bez zmian: decyzja określa etap kwalifikacji, nie zmienia process isolation, DTO/lifecycle, UNKNOWN/quarantine lub autoryzacji; te dokumenty nie nakładają dodatkowej bramki 8 h. |

Zmienione pliki: AGENTS.md, Instrukcja_ChatGPT.md, README.md, Zmiany.md; docs/02_REQUIREMENTS.md, docs/07_TEST_STRATEGY.md, docs/08_IMPLEMENTATION_PLAN.md, docs/09_RISKS_AND_DECISIONS.md, docs/16_RESILIENCE_RECOVERY.md; tests/README.md, tests/resilience/README.md, tests/resilience/X01_LIMITED_GATE.md, tests/resilience/x01-limited-gate.json, tests/contracts/test_limited_gate_documentation.py. Historyczne wpisy poniżej pozostają bez zmian.

Walidacja: sześć testów zgodności dokumentacyjnej PASS; pełne discovery Python w istniejącym QA venv: 30 PASS/0 FAIL/0 SKIP. Próba pełnego discovery systemowym Python zgłosiła brak openapi_spec_validator (24 testy PASS i 1 błąd importu); nie instalowano pakietów, powtórzono we właściwym udokumentowanym środowisku. Kontrola hashy obu historycznych raportów PASS. Pierwszy audyt wykrył jeden błędny nowy anchor instrukcji testów, poprawiony; końcowy audit_quality.py PASS: 43 Markdown, 355 lokalnych odnośników, 0 błędów (23 zastane linki zewnętrzne rejestrowane, bez ponownego sprawdzania sieciowego). Pełny build/testy odporności, Engineering i Runtime nie są ponownie uruchamiane w zadaniu dokumentacyjnym. Diff i sekrety podlegają końcowej kontroli przed commitem. Praca kończy się po commicie/push bez X-01.

## 2026-10-25 — B-02D: resilience, crash recovery i izolacja procesów

Stan wejściowy: czysty main/origin main 11eda18ae43d90c09affeb3bb2cebdd1624ee423. Przeczytano aktualne instrukcje, dokumentację 02–09/13–15, kontrakty, Core/Host i testy B-01A–B-02C. Implementacja wykorzystuje ten sam SQLite/outbox/journal, lifecycle, scheduler i IPC v1. Brak Siemens, X-01, PLC/HMI, symulatorów, OPC/Snap7, Playwright lub migracji rzeczywistych projektów.

- Dodano harness w istniejącym Host.Tests/Resilience i rozszerzono StorageProbe o ten sam HostApi w osobnym procesie z wewnętrznym composition seam. Fault flags/barriers nie są dostępne w produkcyjnym HTTP, config lub argv. 48 nowych .NET i 4 Python reportera; plan pełnego przebiegu: 748 .NET/24 Python. Końcowy pełny run e46332b7aee247c28cc9ca3910e69c48: 748/748 .NET +24/24 Python PASS, FAIL 0/SKIP 0; wszystkie kroki Release/generatorów/audytu/formatu/diffu exit 0. Kwalifikacja NF-03 repo FAIL daje OFFLINE FAIL/exit 1 i GATE FAIL mimo test_execution_status PASS. X-01 formalnie NOT_READY; wariant techniczny read-only/NTFS oddzielony od zgody. [Snapshot](tests/resilience/QUALIFICATION.md).
- Bariery i kontrolowane Environment.Exit obejmują admission przed/po commit, dispatch intent/sent, receipt odebrany/utrwalony oraz result przed/po commit. IStorageFaults obejmuje journal/outbox i symulowane SQLITE_FULL bez zapełniania dysku. WorkerReceiptCommitted jest obserwacją po zapisaniu receipt, bez zmiany dotychczasowej atomowości.
- RT-03/05/06/15/16 używają rzeczywistych Host/CLI/Fake/Named Pipes i plikowej bazy NTFS. Syntetyczny P/B guard działa na jawnym stanie testowego adaptera po potwierdzonym pierwszym efekcie; nie jest planerem, readerem TIA ani kwalifikacją rzeczywistego Apply. Fake audit effect counter jest flushowany przed odpowiedzią; nie jest drugim ledgerem i nie steruje recovery.
- Testy obejmują dwa porządki cancel/dispatch, 100 identycznych submitów/8 klientów i 409, no heartbeat/HB bez postępu, stale epoch, lease expiry, UNKNOWN/quarantine po restarcie, own child/lock anomaly, unrelated same-name sentinel, osiem procesowych ataków IPC, corrupt/missing/truncated proof oraz publish orphan i uszkodzoną końcówkę result WAL. Hardware power loss i foreign SID pozostają NOT_RUN bez zatwierdzonego środowiska.
- Regresja 1001 operacji najpierw zakończyła się FAIL: limit per-worker history 1000 zatrzymywał kolejny dispatch. Naprawiono idle recycling wyłącznie potwierdzonego, własnego, bezczynnego workera. Nowy epoch i SQLite history pozostają; UNKNOWN/blocked nie jest recyklingowane ani ponawiane. Ponowny 1001 run PASS, każdy ID z jednym efektem i trwałym wynikiem.
- Health/8 klientów początkowo p95 116,6084 ms FAIL. GET health używa teraz raz zwalidowanego stałego envelope i typowanych wewnętrznych wartości zamiast ponownego pełnego ReadReport na każdym odczycie. Nie zmieniono wejściowej walidacji, formatu DTO ani autoryzacji; klient i durable reports nadal używają Core. Ponowny profil health PASS.
- Pierwszy profil startup/history N=6 miał p95/max 5660,20 ms FAIL (pozostałe odczyty health/result/events były responsywne). Profil NF-05 rozszerzono do stałych 20 startów, aby p95 miał rzeczywistą próbkę ogona; każdy pomiar, w tym cold-process outliers, jest zapisywany. Próg 5000 ms pozostaje bez zmian; starego FAIL nie usunięto. Nie kwalifikuje to zimnego restartu całego systemu operacyjnego.
- Pierwszy rzeczywisty 306 s idle: CPU 3,6966% FAIL przy RAM 89,82 MiB host/37,38 MiB worker. Przyczyną jest polling pustej kolejki co 20 ms; tylko IDLE zmieniono na 200 ms, aktywne wywołania pozostają 20 ms. Obserwacja aktywnego wywołania korzysta z wewnętrznej projekcji report/hash/event z tego samego metadata readera, bez ponownej walidacji zamrożonego requestu w każdej iteracji. Admission, dispatch i zapis stanu nadal wykonują pełną walidację Core; hash, operation ID, schema/state/outcome/method i powiązanie journal są sprawdzane także w projekcji. Nie zmieniono deadline ani progu NF-08; pełny pomiar jest powtarzany.
- Stop testowej ApiFixture zwalnia Process przez finally także po oczekiwanym nieudanym startup/assertion. Regresja potwierdza usunięcie własnego UUID bez opuszczenia procesu. Historyczny a71bad688cf24e0eb6e04f85d6474ab9 potwierdzono przez marker/config/syntetyczny UUID/own invalid Fake DLL; 0 aktywnych procesów/reparse i 7 poprawnych exclusive read opens. Poprzednia odmowa była polityką narzędzia, nie aktualnym lockiem NTFS. Katalog pozostawiono, bez obejścia kontroli.
- NF-03: NTFS save/replace i repo volume mają odrębne próby. W finalnym 100-próbkowym repo control p95 bez workera 205,5911 ms FAIL, podczas Hang 28,5801 ms PASS; zmienność występuje niezależnie od obecności Host, nie kwalifikuje repo globalnie. Nie było odmów w tym finalnym profilu, ale wcześniejsze AccessDenied pozostają w historii. Bezpieczny test kontrolny repo zachowuje oryginał przy odmowie; brak delete+copy/retry lub rozluźnienia ochrony. Failure repo exFAT nie staje się PASS dzięki zielonemu testowi izolacji lub pozytywnemu NTFS. Formalna kwalifikacja i OFFLINE uwzględniają rzeczywisty wynik.

Jawne doprecyzowanie użytkownika: nie wykonywać teraz 8 h soak; NF-11 NOT_RUN, pełna kwalifikacja długotrwałej stabilności w B-10B/R-14. Dokument 07 mówi minimum 8 h, a AGENTS odsyła do jego obowiązkowych testów przed X-01; R-14 przypisuje długi profil B-10B. Nie przyjęto samodzielnie wyjątku. [Raport](docs/16_RESILIENCE_RECOVERY.md) zawiera dokładne odwołania, skutki, warianty i minimalną propozycję do zatwierdzenia: ograniczone X-01 read-only wyłącznie na kwalifikowanym lokalnym NTFS po obowiązkowych RT/NF, bez uznawania tego za produkcyjną stabilność. Formalne readiness pozostaje NOT_READY, dopóki bramka nie zostanie rozstrzygnięta; X-01 nie jest uruchamiane.

Ostateczne pomiary: idle 306.25 s, CPU 0.9075%, RAM Host 89.33/Fake 37.34 MiB; status/8 p95 14.5978 ms, health/8 25.6193, submit/8 236.3253, startup/history N20 1087.9415; NTFS save/replace 3.2648 ms. Wszystkie percentyle i RT/NF w snapshot JSON/MD. Audyt końcowy: 42 Markdown/323 lokalne odnośniki/0 błędów; format, diff i kontrola 38 ścieżek pod kątem sekretów/danych/binariów PASS. 1001 operacji, 1 efekt na ID, nowy epoch po idle recycle, zero zgubionych wyników.

Lista zmienionych plików (38):

```text
README.md
Zmiany.md
docs/07_TEST_STRATEGY.md
docs/08_IMPLEMENTATION_PLAN.md
docs/09_RISKS_AND_DECISIONS.md
docs/16_RESILIENCE_RECOVERY.md
scripts/README.md
scripts/report_resilience.py
scripts/validate_offline.py
src/TiaBridge.FakeWorker/Program.cs
src/TiaBridge.FakeWorker/README.md
src/TiaBridge.Host/Api/ApiResponses.cs
src/TiaBridge.Host/Api/HostApi.cs
src/TiaBridge.Host/Api/HostRuntimeHooks.cs
src/TiaBridge.Host/Operations/ApiStorage.cs
src/TiaBridge.Host/Operations/ExecutionStorage.cs
src/TiaBridge.Host/Scheduling/OperationScheduler.cs
src/TiaBridge.Host/Storage/StorageContracts.cs
src/TiaBridge.Host/TiaBridge.Host.csproj
tests/COVERAGE.md
tests/README.md
tests/contracts/test_offline_runner.py
tests/contracts/test_resilience_report.py
tests/integration/TiaBridge.Host.Tests/ApiFixture.cs
tests/integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceArtifactTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceCrashTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceEvidence.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceSafetyTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceSecurityTests.cs
tests/integration/TiaBridge.StorageProbe/HttpResilienceProbe.cs
tests/integration/TiaBridge.StorageProbe/IpcAdversary.cs
tests/integration/TiaBridge.StorageProbe/Program.cs
tests/resilience/QUALIFICATION.md
tests/resilience/README.md
tests/resilience/matrix.json
tests/resilience/qualification.json
```

Ocena obu instrukcji sekcja po sekcji:

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres, stan / 1–2 | Nowy ograniczony B-02D jest jawnie zlecony. Historyczny scaffold nie nadpisuje rzeczywistego stanu README; brak Siemens. |
| Hierarchia / 3 | Zachowano kontrakty/ADR. Rozbieżność NF-11/bramki opisana do decyzji, bez samowolnej zmiany normy. |
| Niezmienne granice / 6 | Core bez zmian/dependency Host; durable intent, brak replay UNKNOWN, niezmienne terminal history i odrębne heartbeat/progress. |
| Autoryzacja / 4–5, 8 | Tylko własne syntetyczne procesy/pliki. Nie zmieniono ACL, tokenów, grants, receiver policy ani TIA; foreign SID NOT_RUN. |
| Testy i bramki / 7, 10 | Rzeczywiste crash/IPC/WAL/artifact tests i pomiary; NF failure/NOT_RUN nie ukryte. Technical readiness NTFS oddzielne od formalnej decyzji i produkcji. |
| Dyscyplina, niezaufane dane / 9, 11–12 | Dowody w ignorowanym root, sanitized snapshots i hashe; stare/failed/duplicate dowody odrzucane. Brak surowych tokenów/logów/DB w Git. |
| Git / 5, 11 | Jeden krótki angielski Conventional Commit test(host), explicit staging/staged review, normalny fast-forward push bez przepisywania historii. |

AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002 i normatywne kontrakty v1 pozostają bez zmian; dokumenty 07/08/09 otrzymały wyłącznie odwołania do dowodów/otwartej decyzji, nie wyjątek bramki. R-08/R-14 nie są zamykane bez właściwej kwalifikacji. Po raporcie, commicie i push zadanie kończy się; bez automatycznego X-01 lub B-10B.

## 2026-10-25 — B-02C: lokalne HTTP API, CLI i komunikacja nieblokująca

**Stan wejściowy:** czysty main/origin main `9bcf216eee753ba2844eea346be1adb80cf4fcea`. Przeczytano aktualne instrukcje, README, dokumenty 02–09/13/14, Core/Host, contracts/v1, testy i historię. Wykorzystano B-01A–B-02B, bez drugiego magazynu, lifecycle, schedulera lub IPC. Zrealizowano wyłącznie Host/CLI i syntetyczne wykonanie; bez B-02D, Siemens, PLC/HMI, symulatorów, OPC, Playwright lub MCP.

- Host jest procesem ASP.NET Core Minimal API .NET 10. Udostępnia POST /v1/operations, GET status/result/events, POST cancel i GET diagnostics/health. Dokładne DTO i OpenAPI pozostają źródłem semantyki. Nowe długie zlecenie zwraca 202 dopiero po istniejącym commit intent/journal/outbox; handler nie czeka na workera i nie wykonuje snapshot/hash/scan. Duplicate daje 200 z istniejącym ID, inny canonical body 409. Wyłączenie profilu po restarcie nie ukrywa istniejącego idempotentnego wyniku.
- Pięć szybkich metod metadata POST korzysta z tego samego trwałego ledgeru, dziennika i outbox; akcja/cancel oraz terminalny raport są atomowe. Nie uruchamiają Fake. Wszystkie 19 nazw ma jawny HTTP outcome: wykonawca syntetyczny wyłącznie dla skonfigurowanych project.inspect/sync.apply, pięć metod metadata i UNSUPPORTED dla pozostałych. Nie ma fałszywego SUCCEEDED brakującego wykonawcy.
- Startup config jest wersjonowany, bounded i zamknięty; operator rejestruje ProjectKey przed API. Fake scenario/worker path/synthetic template nie pochodzą z HTTP. Domyślnie brak profilu oznacza deny; fake mutation wymaga osobnego jawnego enable w lokalnym config, bez grantów Siemens. host.fake.json jest syntetyczny, używa dev NTFS state i nie otwiera AP19. Względne ścieżki wymagają uruchomienia z root repo.
- Bind wyłącznie 127.0.0.1 i przydzielony port, niezależnie od ASPNETCORE_URLS. Wymuszone Production nie pozwala włączyć developer error pages przez env; testowy proces startuje z env Development i niezaufanym URL. Token bieżącej instancji ma 32 losowe bajty, SID/PID/start/instance binding i jest publikowany do user-only ACL discovery na lokalnym NTFS, poza repo. CLI sprawdza owner/DACL, containment/reparse/ADS, wersję, bounded/duplicate/unknown JSON i PID/start; sekret nie trafia do argv/URL/logów. Restart rotuje token. Każdy zasób operacji sprawdza principal w SQLite; request nie nadaje uprawnień innemu SID/projektowi.
- API sprawdza Host/loopback/Origin/Sec-Fetch/forwarded host, odmawia żądań przeglądarkowych, nie dodaje CORS wildcard. Brak endpointu dowolnej komendy, konfiguracji lub kill. Limity: 1 MiB request, bounded header/body time, 2048 requests/s, 256 POST/s i 64 concurrency/connections, z rezerwą dla metadata. Zamknięte query/JSON i stałe błędy bez payloadu. Kolejka ma atomowy limit 1000 aktywnych zleceń po lookup idempotencji; przy 999 wyścig 4 requestów daje 1 acceptance i 3 odmowy. Pełna kolejka pozostawia duplicate/status/health dostępne.
- Status jest odczytem aktualnego report/hash/journal bez request-schema revalidation, artifact IO lub blokady wykonawczej. Last-Modified i journal rozróżniają czas zmiany; worker heartbeat age jest związany z epoch. Health oddziela host/store/queue/scheduler/worker/progress/tia_probe/quarantine, bez live probe. Result 202 zwraca status; terminalny result weryfikuje zarządzane artefakty. Events zachowuje sequence, cursor i references. Cancel queued jest atomowy bez dispatch; running zapisuje żądanie i nie deklaruje przerwania. UNKNOWN/quarantine pozostaje po cancel/restart, bez clear/retry.
- CLI i TiaBridge.Client są klientami tego samego HTTP, bez własnej kolejki/SQLite/workera. Obsługują submit METHOD --request FILE, operation status/result/events/cancel/wait i health, JSON, stabilne kody 0/2/3/4/5/6, protected discovery i bounded HTTP. --wait wymaga --max-wait 1..3600; zakończenie CLI/Ctrl+C nie anuluje zaakceptowanej operacji. Brak token/url/force flags i automatycznego retry. Skróty składania requestu z --workspace/--scope pozostają niezaimplementowanym zakresem docelowym, nie brakującym wykonawcą CLI.
- Utrzymano SQLite WAL/FULL/FK, jeden writer i niezmienione migracje v1–v3. Host lifetime handle usuwa koszt checkpointu po zamknięciu ostatniego połączenia; osiem wyłącznych reader connections obsługuje krótkie metadata queries bez współdzielenia aktywnych command/transaction. Nie jest to drugi ledger/cache. Pełna walidacja kolejki następuje przed zmianą wygasłego zlecenia; zwykły skan deadline nie waliduje stale tych samych 1000 requestów. Default storage API bez Host lifetime zachowuje wcześniejsze reguły i testy awarii.

**Naprawa istniejącego błędu Core:** rzeczywisty cykl HTTP/CLI wykrył, że typed serialization pomija wymagane nullable ComponentHealth.age_ms=null. Najpierw dodano i uruchomiono regresję FAIL; potem generator oznaczył wymagane nullable property JsonIgnoreCondition.Never. Regresja i health z UNKNOWN/null przechodzą. Nie zmieniono schema 1.0, stanów, błędów lub wersji kontraktu. OpenAPI otrzymało aktualny opis serwisu i typowany Operation report dla GET result/202, bez nowych endpointów lub semantyki.

**Regresja startu workera:** końcowy przegląd wykrył, że błąd uruchomienia procesu przed dispatch mógł zatrzymać scheduler bez trwałej klasyfikacji. Test rzeczywistego Host z własnym niepoprawnym plikiem Fake DLL potwierdza teraz FAILED/WORKER_UNAVAILABLE/NOT_STARTED i dalszą dostępność API. Supervisor rozpoznaje exit przed handshake, ograniczonymi buforami odprowadza stdout/stderr własnego procesu i nie ujawnia nieufnego tekstu w konsoli Host. Nie było dispatch ani efektu, więc brak kwarantanny; błędy po dispatch nadal podlegają dotychczasowym regułom UNKNOWN.

**Pomiary i wcześniejsze FAIL:** początkowy NF-02 submit/8 klientów miał p95 402,3 ms i nie był deklarowany jako PASS. Lifetime SQLite/readers oraz ograniczenie ponownej walidacji poprawiły ten wynik. Pełny przebieg wykrył presję wielu równoległych startów Host na deadline workera; kolekcje procesowe HTTP są serializowane względem innych kolekcji, a projekty Core/Host runner wykonuje kolejno (-m:1), bez zwiększania timeoutów produkcyjnych i bez pomijania przypadków. Jeden z pełnych przebiegów miał 699 PASS/1 FAIL: startup Fake w istniejącym teście Completed_quarantined_operation konkurował z masowymi testami Core. Ten sam przypadek wykonany oddzielnie przeszedł w 1 s; runner izoluje teraz wykonanie projektów, zachowując wszystkie scenariusze i pomiary. Niepulsowany burst zapisu 1 MiB na wymiennym exFAT przekroczył NF-03 (p95 188,4 ms); taki profil i conditional replace exFAT nie są kwalifikowane. Próba rename napotkała także odmowę filesystemu; NF-03 sprawdza zapis pliku w miejscu, nie kwalifikację replace. Ostateczny profil edytora jawnie stosuje odstęp 100 ms, zachowuje wszystkie 100 próbek i te same progi. Jest to udokumentowane ograniczenie pomiaru, nie ogólna gwarancja opóźnienia dysku wymiennego.

**Ostateczny profil NF:** Windows 10.0.22000, .NET 10.0.13 x64, 8 procesorów logicznych; state NTFS, syntetyczny workspace exFAT; rzeczywisty Fake Hang. Pomiar 2026-10-25T17:06:58.8244419+00:00. Status 1 klient/1000 próbek: p50/p95/p99 4,9455 / 6,4832 / 8,1994 ms. Status 8 klientów/1000: 8,9800 / 14,9753 / 24,0917 ms. Submit 8 klientów/64: 92,3584 / 104,7887 / 115,3513 ms. Edycja 1 MiB/100 próbek/co ≥100 ms i niezależne SHA: 16,8563 / 32,8080 / 103,8299 ms. Progi NF-01/02/03 w tym profilu: PASS. Raporty metryk/TRX/logi są ignorowane w .tia-bridge/b02c i .tia-bridge/offline, nie w Git.

**Testy i wynik:** dodano 39 przypadków .NET — 38 Host (13 HTTP, 15 security, 8 metadata/CLI, 2 performance) i 1 Core regression. Łącznie 700/700 .NET (538 Core +48 Storage +76 Scheduler/IPC +38 HTTP/CLI), 20/20 Python, FAIL 0, SKIP 0. Wszystkie wcześniejsze regresje wykonano rzeczywiście. validate_offline.py sprawdza obowiązkowe nowe procesowe cases, pełny Release build, kontrakty/JSON Schema/OpenAPI/przykłady, generatory, Core dependency boundary, audyt, format i diff. Build Release 0 błędów/0 ostrzeżeń. Końcowy pełny runner: PASS, TRX 63f5fa663e68414faa4c6ea17cd8eaee. Audyt: 39 dokumentów Markdown/281 lokalnych odnośników, 0 błędów. Formatowanie, diff i kontrola 52 ścieżek pod kątem sekretów, danych projektowych i niedozwolonych binariów: PASS. Automatyczna kontrola odrzuciła cleanup jednego własnego UUID po wcześniejszym nieudanym teście (blocked by policy); syntetyczny katalog a71bad688cf24e0eb6e04f85d6474ab9 pozostaje poza repo w dev/storage-tests. Nie jest częścią dostawy ani działającego Host. Pliki testowe/sekrety/DB/binaries nie trafiają do indeksu.

**Lista zmienionych plików (52):**

```text
README.md
TiaBridge.slnx
Zmiany.md
config/examples/README.md
config/examples/host.fake.json
contracts/README.md
contracts/v1/openapi.json
docs/05_CODEX_INTERFACE.md
docs/08_IMPLEMENTATION_PLAN.md
docs/13_DURABLE_STORAGE.md
docs/14_SCHEDULER_IPC.md
docs/15_HTTP_API_CLI.md
examples/README.md
scripts/README.md
scripts/generate_contracts.py
scripts/validate_offline.py
src/TiaBridge.Cli/Program.cs
src/TiaBridge.Cli/README.md
src/TiaBridge.Cli/TiaBridge.Cli.csproj
src/TiaBridge.Cli/packages.lock.json
src/TiaBridge.Client/BridgeClient.cs
src/TiaBridge.Client/HostDiscovery.cs
src/TiaBridge.Client/README.md
src/TiaBridge.Client/TiaBridge.Client.csproj
src/TiaBridge.Client/packages.lock.json
src/TiaBridge.Core/Transport/V1/MethodModels.g.cs
src/TiaBridge.Host/Api/ApiResponses.cs
src/TiaBridge.Host/Api/HostApi.cs
src/TiaBridge.Host/Api/HostConfiguration.cs
src/TiaBridge.Host/Api/RequestGate.cs
src/TiaBridge.Host/Operations/ApiStorage.cs
src/TiaBridge.Host/Operations/ExecutionStorage.cs
src/TiaBridge.Host/Operations/SqliteOperationStore.cs
src/TiaBridge.Host/Program.cs
src/TiaBridge.Host/README.md
src/TiaBridge.Host/Scheduling/OperationScheduler.cs
src/TiaBridge.Host/Storage/SQLite/SqliteDatabase.cs
src/TiaBridge.Host/Supervision/WorkerSupervisor.cs
src/TiaBridge.Host/TiaBridge.Host.csproj
src/TiaBridge.Host/packages.lock.json
tests/COVERAGE.md
tests/README.md
tests/contracts/test_offline_runner.py
tests/integration/TiaBridge.Host.Tests/ApiFixture.cs
tests/integration/TiaBridge.Host.Tests/ApiMetadataAndCliTests.cs
tests/integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs
tests/integration/TiaBridge.Host.Tests/ApiSecurityTests.cs
tests/integration/TiaBridge.Host.Tests/HttpApiTests.cs
tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj
tests/integration/TiaBridge.Host.Tests/packages.lock.json
tests/integration/TiaBridge.StorageProbe/packages.lock.json
tests/unit/TiaBridge.Core.Tests/RequiredNullableTests.cs
```

**Ocena wpływu instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres/stan i role / 1–2 | Jawne zadanie B-02C implementuje API/CLI. Pierwotny opis scaffold w instrukcjach pozostaje historyczny; stan rzeczywisty jest w README/15, bez modyfikacji instrukcji. |
| Hierarchia / 3 | Użyto istniejącej architektury, v1 DTO, ledgeru i schedulera. Brak konfliktu wymagającego nowej decyzji; required-nullable to naprawa zgodności z istniejącym schematem. |
| Niezmienne granice / 6 | Core bez Host/vendor, durable acceptance przed 202, brak inline workera/TIA, UNKNOWN bez replay, niezależne heartbeat/progress i status. |
| Autoryzacja / 4–5, 8 | Token/ACL daje wyłącznie lokalny transport i SID ownership. Brak sesji/grantów Siemens, GUI lub obcych procesów; własne testowe Host/CLI/Fake i pliki objęte zadaniem. |
| Testy/bramki / 7, 10 | Rzeczywisty pełny przepływ i pomiary z jawnym profilem. B-02D, soak, rzeczywisty drugi SID, power-loss i X-01 nadal odrębne. |
| Dyscyplina / 9, 11–12 | Dokumentacja, config/example, generator, testy i runner aktualne. Wyniki robocze i protected discovery poza Git; bez uruchamiania znalezionych skryptów. |
| Git / 5, 11 | Jeden angielski Conventional Commit feat(host), named-path staging, staged review i normalny push z HEAD check, bez force. |
| Dane nieufne / 6, 9 | Zamknięte bounded JSON/query/config/discovery, user-only ACL i principal checks, brak arbitralnego endpointu/komendy; result zachowuje integrity verification. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002, wymagania 02, architektura 03, synchronizacja 04, testy 07 i ryzyka 09 bez zmiany zatwierdzonej semantyki. Dokument 05/08 ma aktualny stan i odsyła do ograniczonej implementacji 15; dokumenty 13/14 rozróżniają starszy zakres i rozszerzenie B-02C. Core nie otrzymał zależności infrastrukturalnych. Brak nierozwiązanych błędów krytycznych w zakresie; burst exFAT/conditional replace, drugi SID, B-02D/soak/power-loss i Siemens pozostają poza dowodem. Gotowość do osobnego B-02D nie otwiera bramki X-01. Po potwierdzonym commit/push praca zostaje zatrzymana.

## 2026-10-25 — B-02B: scheduler, nadzorowany Fake Worker i IPC

**Wejście i zakres:** czysty main/origin main `216c550d87c03507a641903a6e3d546308848ccf`, B-01A–C/B-02A. Przeczytano aktualne instrukcje, README, dokumenty 02–09/13, ADR 0002, Core/Host, kontrakty i testy. Zrealizowano wyłącznie wykonanie syntetyczne offline, bez HTTP/CLI B-02C, pełnego harnessu B-02D, workera V19 lub uruchamiania Siemens/PLC/HMI/symulatorów. Nie kopiowano starego Bridge lub danych klienta.

- Dodano OperationScheduler z pętlą w tle i kontrolowanym PumpOnceAsync, IExecutionProfile domyślnie deny, istniejącym outbox i tym samym ledgerem. Transakcja dispatch obejmuje QUEUED/RUNNING, journal, trwały claim, attempt/step intent i lease metadata przed wysłaniem IPC. Refaktoryzacja RecordCore współdzieli istniejące reguły lifecycle i zapis atomowy zamiast drugiego kontraktu. Queue deadline działa także przy wiszącym call; cancelled queued nie jest wysyłane. Running cancel jest intentem do najbliższej potwierdzonej granicy, bez twierdzenia o przerwaniu.
- Wyłączny plik scheduler.lock, host PID/start/SID/epoch, rosnący fence i lease 15 s; jeden aktywny call na zatwierdzony state root. Worker ma osobny PID/start/epoch i portal/capability key. Stary owner jest odrzucany po zmianie fence lub wygaśnięciu lease. Utrata lease nie daje prawa nowej mutacji. Profile syntetyczne nie nadają capability/grantów Siemens.
- FakeWorkerSupervisor uruchamia wyłącznie własny wskazany Fake Worker, bez shell/okna/sekretów argv. Sprawdza PID peer pipe, start, nonce, ID/build; handshake 10 s, send 1 s. Odczyt IPC i heartbeat są niezależne od wykonania. Dispose nie killuje trwającego wykonawcy ani obcych procesów; Fake sam kończy swoje oczekiwanie po utracie pipe. Startup failure może zakończyć wyłącznie własne dziecko przed dispatch. Job Object/parent-loss/Dispose rzeczywistego V19 pozostają X-01, bez przenoszenia gwarancji Fake na Siemens.
- Nowa biblioteka TiaBridge.Ipc nie zależy od Core/SQLite/Siemens. IPC 1.0 ma dziewięć rodzajów DTO, oddzielny schema i wspólne przykłady .NET/Python. Named Pipe: CurrentUserOnly, FirstPipeInstance, losowa nazwa, nonce i sekret przez stdin. Length prefix, limit 1 MiB przed alokacją, HMAC-SHA256 constant-time, epoch/fence/sequence, shape/unknown/duplicate/depth/UTF-8 validation. To uwierzytelnienie/integralność, bez szyfrowania. Inline receipt/report ≤64 KiB; duże wejścia przez managed artifact ID/SHA z kontrolą containment/reparse/hash. Nie ma proxy Siemens.
- Oddzielny TiaBridge.FakeWorker realizuje sukces/opóźnienie, błędy przed/po rozpoczęciu, hang, brak heartbeat, heartbeat bez postępu, timeout, utratę IPC, exit, late success, niepewną mutację, safe cancel, duplicate dispatch, bounded retry i negatywne fence/planhash. Jeden serializowany call, bounded dedupe cache i synthetic effect counter. Nie wykonuje znalezionych skryptów. LateSuccess jest zwalniany jawnie przez identyczny duplikat dla powtarzalnego testu już oczekującej odpowiedzi po deadline, bez drugiej próby/efektu.
- Watchdog rozdziela host/worker heartbeat, last_progress_at i tia_probe_at=null. Heartbeat 2 s, suspect po 6 s, lease 15 s i ExecutionBudget z Core. Timeout nie dowodzi końca call. Read timeout pozostaje FAILED bez równoległego ponowienia do potwierdzonej quiescence; możliwa mutacja daje terminalne UNKNOWN/RECONCILIATION_REQUIRED i trwałą quarantine powiązaną z worker epoch/fence. Late/fenced receipts pozostają append-only dowodami i nie promują UNKNOWN do SUCCEEDED.
- Retry tylko dla jawnie kwalifikowanych bezefektowych odczytów po Rejected/0 effect/quiescent, maksymalnie 3 próby, backoff 1/2 s +0–25% jitter i wspólny deadline. Brak retry identity/schema/auth/conflict lub możliwej mutacji. Circuit breaker utrwalany per portal/capability: 3 awarie/60 s, cooldown 60 s, jeden read half-open. Quarantine ma pierwszeństwo i nie ma automatycznego clear/replay.
- Migracja 003 dodaje 5 tabel: scheduler_ownership, worker_processes, dispatch_attempts, worker_receipts, circuit_breakers i rozszerza quarantine o epoch/fence; łącznie 23 tabele, user_version=3. SQL 001/002 oraz ich hashe bez zmian. Regresja sprawdza v1→v3 i rzeczywisty baseline v2→v3, zachowanie requestów i idempotencji. Core i contracts/v1 nie zmieniły semantyki ani zależności; aktualizowano jedynie indeks README Core/kontraktów.
- Recovery nowego hosta obserwuje stare PID/start, bez adopcji/kill. RUNNING możliwej mutacji od razu UNKNOWN/quarantine; alive/unverifiable stary proces blokuje nowe calls. Nowy read może ruszyć po potwierdzonym końcu starego read procesu i braku UNKNOWN/quarantine. Stary wynik pozostaje w historii. Nie implementowano uzgadniania projektu TIA ani automatycznego readback/Save/rollback.

**Testy:** 76 nowych przypadków .NET (SchedulerTests 29, SchedulerSafetyTests 13, IpcAndPolicyTests 34), 3 nowe Python IPC. Pełny zestaw ma 661 .NET (537 Core +48 Storage +76 nowych) oraz 20 Python. Testy używają własnych plikowych SQLite i artefaktów na lokalnym NTFS, rzeczywistych Fake Worker/Named Pipes oraz deterministycznego crash procesu hosta Environment.Exit(76) po Started. Obejmują między innymi 1000 metadata status reads/p95≤100 ms przy fake hang, cancel race, duplicate effect counter=1, brak overlap, retry/backoff/breaker, heartbeat bez postępu, restart read/mutation, late/fenced reply i lease expiry bez zwolnienia quarantine. Wspólny validate_offline.py wymaga nowych przypadków procesowych i dziewięciu fixtures IPC, bez pomijania starych bramek.

**Wykryte i poprawione w trakcie B-02B:** zbyt szeroki kod diagnostyki Fake nie mieścił się w error subset konkretnej metody — zachowano surowy kod w receipt i mapowano niedopuszczalny publiczny kod na istniejący WORKER_UNAVAILABLE, bez zmiany kontraktu. Pełny przebieg wykrył też wyścig receipt/deadline: już oczekujący sukces mógł wyprzedzić watchdog. Host sprawdza deadline przed użyciem wyniku; deterministyczna regresja wymusza obecność ramki przed krokiem hosta i potwierdza UNKNOWN z zachowanym late receipt. Pierwszy pełny przebieg z tym błędem był FAIL; ostateczne dowody są podane niżej po ponownej walidacji. Lokalny błąd kodowania skryptu dokumentacji poprawiono przez odtworzenie wyłącznie własnych edycji i jawny UTF-8; skrypt roboczy pozostaje ignorowany.

**Ocena wpływu instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / 1–2 | Jawnie zlecony B-02B to syntetyczny scheduler, nie serwer/adapter TIA. Stan w README/14; obie instrukcje bez zmian. |
| Hierarchia / 3 | Zastosowano istniejące ADR 0002, lifecycle i SQLite; brak nowej semantyki lub konfliktu architektonicznego. |
| Niezmienne granice / 6 | Core bez infrastruktury/vendor, intent przed dispatch, jeden call, UNKNOWN bez replay, epoch/fence/lease bez domniemania zatrzymania TIA. Brak nowego SYNCED/baseline. |
| Autoryzacja / 4–5, 8 | Własne testowe procesy/DB/artifacts i Git objęte zadaniem. Profile Fake nie nadają sesji/grantów. Bez Computer Use i obcych procesów. |
| Testy i bramki / 7, 10 | Realne procesy Fake i IPC, nadal wymagane B-02C/D przed X-01. Brak deklaracji pełnego resilience gate, Engineering/Runtime lub power-loss. |
| Dyscyplina / 9, 11–12 | Nowy schemat/przykłady/testy/docs, wspólny runner i jawne wyniki. Robocze logi/TRX/bazy/binaries poza Git. |
| Git / 5, 11 | Jeden krótki angielski Conventional Commit feat(host), named-path staging, staged review, normalny push i HEAD check. |
| Dane nieufne / 6, 9 | Bounded IPC, HMAC/identity/sequence, shape i artifact/hash/reparse checks. Brak wykonywania treści lub dowolnych nazw metod. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002 i normatywne dokumenty 02–07/09 bez zmiany semantyki; B-02B implementuje ich ograniczony zakres. Backlog 08 ma wyłącznie nowy stan; dokumenty 13/14 i indeksy rozróżniają historię Storage od obecnego wykonania Fake. Obcy SID/ACL deployment, wrogi proces tego samego użytkownika, pełny harness, sprzętowy power-loss i worker V19 pozostają poza dowodem. Brak publicznego HTTP/CLI, uzgadniania TIA i automatycznego clear quarantine. Gotowość do osobno zlecanego B-02C nie otwiera bramki Siemens. Po zweryfikowanym commit/push praca zostaje zatrzymana.

**Pliki (52):** `README.md`, `TiaBridge.slnx`, `Zmiany.md`, `contracts/README.md`, `contracts/ipc/v1/message.schema.json`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/13_DURABLE_STORAGE.md`, `docs/14_SCHEDULER_IPC.md`, `examples/ipc/v1/cancel.json`, `examples/ipc/v1/dispatch.json`, `examples/ipc/v1/heartbeat.json`, `examples/ipc/v1/hello.json`, `examples/ipc/v1/progress.json`, `examples/ipc/v1/ready.json`, `examples/ipc/v1/receipt.json`, `examples/ipc/v1/started.json`, `examples/ipc/v1/stop.json`, `scripts/README.md`, `scripts/validate_offline.py`, `src/TiaBridge.Core/README.md`, `src/TiaBridge.FakeWorker/Program.cs`, `src/TiaBridge.FakeWorker/README.md`, `src/TiaBridge.FakeWorker/TiaBridge.FakeWorker.csproj`, `src/TiaBridge.FakeWorker/packages.lock.json`, `src/TiaBridge.Host/Operations/ExecutionStorage.cs`, `src/TiaBridge.Host/Operations/SqliteOperationStore.cs`, `src/TiaBridge.Host/README.md`, `src/TiaBridge.Host/Scheduling/CircuitBreaker.cs`, `src/TiaBridge.Host/Scheduling/ExecutionModels.cs`, `src/TiaBridge.Host/Scheduling/OperationScheduler.cs`, `src/TiaBridge.Host/Storage/Migrations/003_execution.sql`, `src/TiaBridge.Host/Storage/SQLite/SqliteDatabase.cs`, `src/TiaBridge.Host/Supervision/WorkerSupervisor.cs`, `src/TiaBridge.Host/TiaBridge.Host.csproj`, `src/TiaBridge.Host/packages.lock.json`, `src/TiaBridge.Ipc/PipePeer.cs`, `src/TiaBridge.Ipc/Protocol.cs`, `src/TiaBridge.Ipc/README.md`, `src/TiaBridge.Ipc/TiaBridge.Ipc.csproj`, `src/TiaBridge.Ipc/packages.lock.json`, `tests/COVERAGE.md`, `tests/README.md`, `tests/contracts/test_ipc_contracts.py`, `tests/contracts/test_offline_runner.py`, `tests/integration/TiaBridge.Host.Tests/IpcAndPolicyTests.cs`, `tests/integration/TiaBridge.Host.Tests/MigrationAndPolicyTests.cs`, `tests/integration/TiaBridge.Host.Tests/SchedulerSafetyTests.cs`, `tests/integration/TiaBridge.Host.Tests/SchedulerTests.cs`, `tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj`, `tests/integration/TiaBridge.Host.Tests/packages.lock.json`, `tests/integration/TiaBridge.StorageProbe/Program.cs`, `tests/integration/TiaBridge.StorageProbe/packages.lock.json`.

**Walidacja końcowa:** pełny validate_offline.py: PASS; Build Release 0 błędów/0 ostrzeżeń; 661/661 .NET (FAIL 0, SKIP 0), 20/20 Python. Wszystkie 19 operacji/API schema/OpenAPI, 100 deterministycznych modeli, syntetyczne przykłady Workspace/Capabilities i dziewięć IPC nadal przechodzą. Testy procesowe/watchdog/quarantine/restart i regresje B-01A–C/B-02A wykonano rzeczywiście; brak mock-only deklaracji. audit_quality.py: PASS, 37 Markdown i 249 lokalnych odnośników. Format/diff i kontrola sekretów/binariów: PASS. Raporty/logi/TRX w ignorowanym .tia-bridge/offline oraz .tia-bridge/b02b; bazy i artefakty poza repo na NTFS. OFFLINE PASS, ENGINEERING NOT_RUN, RUNTIME NOT_RUN.

## 2026-10-25 — B-02A: trwały magazyn operacji SQLite

**Zakres:** biblioteka Host Storage/Operations w .NET 10, migracje SQLite, durable admission/idempotency/journal/outbox/artifacts i syntetyczne testy na lokalnym NTFS. Wejściowy czysty main/origin main: `cfe2e20d1504d8fd25a54753149ebbb74e1c7dfd`. Zweryfikowano rozwiązanie .NET, aktualne instrukcje, README, dokumenty 02–09, Core, kontrakty i testy B-01A–C oraz rejestr zmian. Nie kopiowano starego Bridge. Potwierdzono C: jako Fixed/NTFS, M: jako Removable/exFAT. Nowe testy używają wyłącznie własnych UUID katalogów `%LOCALAPPDATA%\TIA_Bridge2\dev\storage-tests`, bez otwierania produkcyjnego state i bez SQLite w repo.

- Dodano TiaBridge.Host jako bibliotekę, bez entrypointu serwera, HTTP/CLI, schedulera, supervisora, workera lub wykonywania zleceń. Core i wire v1 pozostają bez zmian. Host używa istniejących ProjectKey/OperationId, ContractJson, RequestIdentity, OperationLifecycle i DTO; nie tworzy alternatywnego modelu domenowego.
- SQLite: Microsoft.Data.Sqlite 10.0.13, lockfile, osobne private-cache połączenia per wywołanie, pooling=false, WAL, synchronous=FULL, FK, lokalny writer gate i BEGIN IMMEDIATE między procesami, bounded timeout 5 s. Brak retry efektu operacji. Domyślny root state pod LOCALAPPDATA/TIA_Bridge2; jawne inne root tylko w lokalnym katalogu aplikacji, na stałym NTFS, poza repo. Obce katalogi/bazy, UNC/SMB/device paths, exFAT i reparse points są odrzucane. Marker inicjalizacji DB zapobiega cichej rekreacji pustego ledgeru po usunięciu znanego pliku.
- Dwie migracje: 001 tworzy 18 tabel (14 docelowych encji oraz schema_migrations, operation_results, outbox, operation_artifacts), 002 dodaje indeksy i triggery ochronne. Ledger hashy SQL po normalizacji CRLF→LF, application_id/user_version, integrity_check i foreign_key_check blokują przyszły/obcy/uszkodzony schemat. Upgrade jest transakcyjny, bez down migration/resetu; v1→v2 zachowuje dane. Sessions/steps/leases/snapshots/plans/baselines/capabilities/authorizations mają tylko struktury potrzebne dalszym etapom, bez ich logiki biznesowej.
- Admission: walidacja i snapshot wejścia Core, krótka transakcja obejmująca trwały klucz, UUID operacji, wire/canonical body/SHA, ACCEPTED/NOT_STARTED, pierwszy event i PENDING outbox; acceptance dopiero po COMMIT. UNIQUE(principal,project_scope,key) działa także dla globalnego NULL project dzięki wygenerowanemu scope. Identyczne żądanie zwraca to samo ID, inne daje IDEMPOTENCY_MISMATCH. Nie oparto poprawności wyłącznie na pamięci procesu.
- Journal/result: CAS rewizji i istniejący lifecycle Core, atomowe state+event+terminal proof, immutable history i zakaz nadpisywania wyniku. Deferred composite FK wiąże bieżący report/hash z eventem. Terminalny UNKNOWN pozostaje w historii, zamyka outbox i minimalnie kwarantannuje projekt. ReadStatus czyta wyłącznie journal metadata; Read/ReadResult i duplicate acceptance sprawdzają integralność wymaganych artefaktów. Historyczny status nie jest świeżym dowodem zgodności TIA/Runtime.
- Outbox: trwały rekord per admission, PENDING→CLAIMED→DISPATCH_RECORDED→CLOSED; CAS nadaje jeden claim UUID, również przy wyścigu procesów. Nie ma expiry/release/requeue, automatycznego wznowienia po restarcie ani konsumenta wykonującego pracę. Quarantine blokuje Pending/claim/dispatch record także dla wcześniej przejętego wpisu. Claim nie jest grantem, lease ani gwarancją exactly-once Siemens.
- Artifacts: managed UUID IDs/paths, exclusive temp, stream limit 1 GiB, Flush(true), SHA-256 dokładnych bajtów, move bez overwrite na tym samym NTFS, readonly i dopiero potem referencja SQLite. Weryfikacja hash/size/path i uchwyt odmawiający write/delete chronią referencję przez commit wyniku. Uszkodzony/brakujący/niekompletny artifact blokuje poprawny wynik. Temp i niepowiązane opublikowane pliki są raportowane i zachowywane bez adopcji, GC i agresywnego usuwania; registered artifacts są pinned. Nie ma transakcji atomowej obejmującej zarazem filesystem i SQLite.
- Obsługa błędów nie ujawnia SQL, principal, request ani ścieżek. Po próbie commit StorageException.CommitMayHaveSucceeded sygnalizuje możliwość trwałego zapisu mimo braku potwierdzenia; jedynym bezpiecznym ponowieniem admission jest ten sam key/request, bez ponawiania wykonania. Generic IO, SQLITE_FULL i Windows disk-full mają kontrolowane kody. Fault seam domyślnie jest NoStorageFaults; nie pochodzi z JSON ani CLI aplikacji.

**Testy i dowody:** dodano 48 testów Host — 9 OperationStorageTests, 14 FailureAndRestartTests, 13 MigrationAndPolicyTests, 12 ArtifactStorageTests. Obejmują plikowy SQLite, 100 równoczesnych nowych zgłoszeń (1 nowe +99 duplicate), cztery niezależne procesy ścigające admission i outbox claim, deterministyczny Environment.Exit(73) przed/po COMMIT i po artifact flush/publish, odczyt wyników SUCCEEDED/UNKNOWN w nowym procesie, awarie SQL event/outbox, rollback terminal proof/quarantine, migracje i ich błędy, WAL/FULL/FK, korupcję/brak bazy i artefaktu, przerwany input stream, NTFS/repository/foreign/junction policy. Kontrolowane SQLITE_FULL i Windows disk-full IO injection nie zapełniają realnego dysku; nie kwalifikują sprzętowego power-loss. StorageProbe jest krótkim procesem testowym, bez fake workera/IPC/API. Sprzątanie sprawdza własny UUID root i nie przechodzi przez reparse points; po przebiegu brak pozostawionych katalogów testowych.

Rozszerzono istniejący validate_offline.py: build całego solution, oba projekty testowe, osobne TRX w katalogu UUID bieżącego przebiegu, agregacja i obowiązkowe przypadki Storage. Nie można nadpisać raportu Core przez Host lub przyjąć starego TRX jako wyniku nowego przebiegu. Wszystkie 537 regresji .NET i 17 Python B-01A–C nadal są wykonywane. Dodano ignorowanie lokalnych plików SQLite/WAL/SHM; nie jest to zgoda na umieszczanie bazy w repo. Pipeline offline nadal nie instaluje pakietów i używa pustego feedu/cache; nowe zależności pobrano raz zwykłym restore. Sprawdzenie podatności Host z transitive packages przez aktualnie dostępne źródła NuGet nie zgłosiło podatności w chwili kontroli; nie jest to trwała gwarancja bezpieczeństwa.

**Ocena wpływu instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / 1–2 | Zrealizowano jawnie zlecony B-02A jako bibliotekę Storage. Serwer i adapter nie istnieją; README/Host/13 opisują stan. Instrukcje pozostawiono bez zmian. |
| Hierarchia / 3 | Zastosowano zatwierdzone SQLite/outbox i granicę Core/Host. Brak nowej semantyki operacji, architektury lub konfliktu wymagającego rozstrzygnięcia. |
| Niezmienne granice / 6 | Trwały ID/intent przed acceptance, brak inline TIA, UNKNOWN bez replay, state poza exFAT/repo, osobne lifecycle/sync/Compile/Runtime. Brak nowych baseline lub SYNCED. |
| Autoryzacja i Computer Use / 4–5, 8 | Zapis własnych testowych SQLite/artifacts i Git jest objęty zadaniem. Nie nadano grantów, nie uruchomiono TIA/GUI ani obcych sesji. RegisterProject nie potwierdza bindingu Siemens. |
| Testy i bramki / 7, 10 | B-02A sprawdza trwałość, nie cały async engine. One-owner/epoch/fence, lost worker/lease, late result, scheduler i fake gate pozostają B-02B–D przed X-01. |
| Dyscyplina i raporty / 9, 11–12 | Dokumentacja, schemat SQL, macierz, runner i testy uzupełnione; wyniki robocze ignorowane. Kod danych wejściowych/artefaktów nie jest wykonywany. |
| Standard Git / 5, 11 | Jeden angielski Conventional Commit feat(host), named-path staging, staged review, normalny push i porównanie HEAD. Bez force i bez zmiany historii. |
| Dane nieufne / 6, 9 | Core waliduje wire, SQL ma parametry/FK/constraints, pliki mają limity/managed paths/hash, obcy schema/root i reparse points blokują. Brak parserów/wykonywania vendor formatów. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002, wymagania 02, architektura 03, synchronizacja 04, interfejs 05, strategia 07 i decyzje 09 pozostają normatywnie bez zmian: B-02A je implementuje w ograniczonym zakresie, nie ustanawia uprawnień ani nowej sagi. Backlog 08 otrzymał tylko stan realizacji; dokument 13, Host README, tests README/COVERAGE i scripts README zawierają konkretną implementację i granice. Core oraz contracts/v1 bez zmian. Kwalifikacja ACL deployment, odporność na wrogi proces tego samego użytkownika i realny power-loss pozostają poza dowodem B-02A.

**Pliki (31):** `.gitignore`, `README.md`, `TiaBridge.slnx`, `Zmiany.md`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/13_DURABLE_STORAGE.md`, `scripts/README.md`, `scripts/validate_offline.py`, `tests/README.md`, `tests/COVERAGE.md`, `tests/contracts/test_offline_runner.py`; `src/TiaBridge.Host/TiaBridge.Host.csproj`, `packages.lock.json`, `README.md`, `Operations/SqliteOperationStore.cs`, `Storage/StorageContracts.cs`, `Storage/StorageLocation.cs`, `Storage/SQLite/SqliteDatabase.cs`, `Storage/Artifacts/FileArtifactStore.cs`, `Storage/Migrations/001_initial.sql`, `002_guards.sql`; `tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj`, `packages.lock.json`, `StoreFixture.cs`, `OperationStorageTests.cs`, `FailureAndRestartTests.cs`, `MigrationAndPolicyTests.cs`, `ArtifactStorageTests.cs`; `tests/integration/TiaBridge.StorageProbe/TiaBridge.StorageProbe.csproj`, `packages.lock.json`, `Program.cs`. DLL, bin/obj, bazy, raw logs i artifacts nie trafiają do Git.

**Walidacja OFFLINE PASS:** locked restore offline, Build Release 0 błędów/0 ostrzeżeń, **585/585 .NET** (537 regresyjnych +48 Storage), **17/17 Python**, FAIL 0, SKIP 0. Testy restartu/idempotencji/concurrency/migracji/transakcji/artefaktów rzeczywiście wykonano na lokalnym NTFS, nie :memory:. Pełny validate_offline.py, oba generatory, format, diff, kontrola sekretów/ścieżek i audit_quality.py: PASS; audyt 34 Markdown, 214 lokalnych odnośników, 19 metod. TRX/run/quality/logi w `.tia-bridge/offline/`, dodatkowe robocze wyniki w `.tia-bridge/b02a/`, poza indeksem.

**Granice i zakończenie:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. To warstwa trwałości, nie gotowy async engine. Nie uruchamiano zadań, Siemens, PLC/HMI, kompilacji lub symulacji. Brak nierozwiązanych problemów blokujących zadany zakres; B-02B jest gotowy do osobnego zlecenia na tym fundamencie, ale nie został rozpoczęty. Po commicie i zweryfikowanym push praca nad B-02A zostaje zatrzymana.

## 2026-10-25 — B-01C: walidacja kontraktów i Core

**Zakres i wejście:** czysty main/origin main `5fe5bda6eb288e7aa8b13c58fd859ea84a8e01d9`. Ponownie wykonano 160 testów .NET i 10 Python B-01A/B-01B: PASS. Przeczytano aktualne instrukcje, README, dokumenty 02–09 i 12, contracts/v1, Core, testy i dotychczasowy rejestr. Audyt testów wykazał brak pełnej macierzy wymaganych params/results/errors per metoda w .NET, tylko wybrane lifecycle edges, ograniczone przypadki kodowania/rozmiarów oraz brak 100 różnych deterministycznych scenariuszy. Macierz zachowań z konkretnymi testami, lukami i uzupełnieniami znajduje się w `tests/COVERAGE.md`; nie deklaruje procentowego branch/line coverage.

- Dodano wspólny korpus 39 mutacji × 19 metod = 741 ewaluacji request/report/error, odtwarzany w .NET oraz niezależnie przez JSON Schema i schematy danych OpenAPI. Wymagane params/results i obecne zagnieżdżone struktury są mutowane pojedynczo; wszystkie error_codes per metoda mają test poprawnego raportu/standalone error i round-trip, brakujących pól, obcego kodu i UNKNOWN+retry. Nowe required features, wersje major/minor, błędne typy/UUID i granice Unicode odrzucane zgodnie z v1.
- Dodano 100 osobnych, odtwarzalnych xUnit cases: 10 rodzin nazw × 5 wariantów capability/coverage × 2 normalizatory. Każdy ma inny ObjectId, model i golden raw/model SHA-256 wyznaczony przez Python hashlib. Testuje mapowanie/decode, NFC/case/truncation/suffix collisions, manifest/unit hashes, znane zbiory w innej kolejności, wersje, pl-PL/tr-TR, round-trip i niezmienność wejść. Nie powstał silnik planowania.
- Dodano wszystkie 147 kombinacji lifecycle current/next/outcome, testy idempotency i budget dla każdej metody, odrębność phase/state/cancel/sync status, dokładne granice 1 MiB request/acceptance, 10 MiB raportu i raw event 64 KiB oraz +1, cztery klasy błędnego UTF-8 na wszystkich wire granicach. Dodano adversarial graphs/metadata/coverage i poprawny rename/move po pełnej walidacji oraz kontrolę tranzytywnej closure assembly Core. Testy ścieżek/reparse points są modelowe; nie wykonują IO Workspace ani nie dowodzą TOCTOU safety.

**Dwa wykryte i poprawione błędy (najpierw regresja FAIL, potem PASS):**

1. B-01A `IdempotencyKey` akceptował niepoprawny UTF-16, a typowany zapis wire mógł zastępować uszkodzony surrogate znakiem U+FFFD. Dodano strict UTF-8 validation w konstruktorze i string converterze ContractJson; niepoprawny input jest odrzucany bez echo payloadu. Poprawny Unicode i dotychczasowe canonical hashes pozostają zgodne.
2. `OperationStatusResult` / `OperationResultResult` miały enumy state/outcome, lecz brakowało wspólnej walidacji lifecycle. Odczyt statusu mógł więc przenosić docelowe SUCCEEDED/UNKNOWN. Generator stosuje istniejące reguły lifecycle także do tych projekcji; zaktualizowano operation-result.schema.json, operation.schema.json i OpenAPI. Nie dodano nowych stanów/operacji, nie zmieniono lifecycle table ani zatwierdzonej semantyki i wersji 1.0. Reguła envelope SUCCEEDED dotyczy sukcesu odczytu, a reguła projekcji — osobno operacji docelowej.

**Powtarzalny przebieg:** `scripts/validate_offline.py`, uruchamiany Python z izolowanego venv QA, wykonuje preflight Core boundaries, locked restore z pustego lokalnego feedu i cache, Build Release, .NET/TRX, unittest JSON Schema/OpenAPI/przykłady, oba generatory w trybie check, audyt dokumentacji, format i diff. Brak dependency/testu, fail, skip lub niepełna wymagana macierz daje FAIL; nie instaluje pakietów ani nie przechodzi do publicznego feedu. NuGetAudit=false dotyczy wyłącznie offline restore, nie jest raportem aktualnych podatności. Pakiety i lockfiles bez zmian. Raporty/logi wyłącznie w ignorowanym `.tia-bridge/offline/`; testy helpera runnera używają izolowanych synthetic temp fixtures, bez projektów klienta.

**Rozdzielenie backlogu zgodnie z bieżącym zleceniem:** B-01C sprawdza 100 istniejących modeli/wejść. Kryterium co najmniej 100 rzeczywistych deterministycznych planów zachowano i przeniesiono do B-04A, gdzie powstanie planer W/P/B/DAG. Zaktualizowano wyłącznie odpowiednie wiersze/stany backlogu i odnośnik w strategii testów. Nie przedstawiono modeli jako wykonanego sync.plan. B-02, X-01 i Siemens pozostają poza zakresem.

**Ocena instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / 1–2 | Implementacja nadal ograniczona do Core i narzędzi offline; brak serwera/adapterów. Stan B-01C uaktualniono w README, instrukcje pozostawiono zgodnie ze zleceniem. |
| Hierarchia / 3 | Naprawy egzekwują istniejące reguły Unicode i lifecycle; brak nowej semantyki. Rozdzielenie 100 modeli/planów wynika z jawnej decyzji w tym zadaniu, nie samodzielnego osłabienia backlogu. |
| Niezmienne granice / 6 | Core bez Siemens; brak ledgeru, schedulera, baseline/receipt/SYNCED i operacji TIA. Stan, phase, outcome oraz sync/Runtime pozostają osobne. |
| Autoryzacja i Computer Use / 4–5, 8 | Zadanie offline i Git, bez grantów ani GUI; wpis capability nadal nie nadaje SessionPermission. Nie używano Computer Use. |
| Testy i bramki / 7, 10 | Nowe testy i pipeline nie zastępują B-02A–D na fake ani X-01. Bramka przed każdym rzeczywistym Siemens, także read-only, pozostaje bez zmian. |
| Dyscyplina zmian i raporty / 9, 11–12 | Schematy/OpenAPI odtworzono generatorem, dodano macierz i jedno polecenie testów, wyniki podano jako OFFLINE. Dane wyłącznie syntetyczne, raporty poza Git. |
| Standard Git / 5, 11 | Jeden angielski Conventional Commit typu test(core), named-path staging, staged review, normalny push i porównanie HEAD; historia bez zmian. |
| Dane nieufne / 6, 9 | Więcej testów limitów, strict Unicode, malformed JSON, wersji i grafu. Brak parsera native XML/YAML; surowy schema check nie jest dowodem IO ani zgodności TIA. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md pozostają bez zmian. ADR 0001/0002, wymagania 02, architektura 03, synchronizacja 04, interfejs 05, integracja 06 i decyzje 09 nie wymagają zmiany: architektura, saga, prawa i lifecycle są zachowane. Strategia 07 wskazuje aktualne dowody offline; backlog 08 uwzględnia zlecone rozdzielenie modeli i przyszłego planowania. Nie ma nierozstrzygniętego konfliktu architektury/kontraktu blokującego ten pakiet. Brak znanych nierozwiązanych krytycznych błędów w zbadanym Core nie oznacza dowodu pełnego pokrycia wszystkich możliwych wejść.

**Zmienione pliki (27):** `README.md`, `Zmiany.md`, `contracts/README.md`; `contracts/v1/openapi.json`, `operation-result.schema.json`, `operation.schema.json`; `docs/07_TEST_STRATEGY.md`, `docs/08_IMPLEMENTATION_PLAN.md`; `scripts/README.md`, `scripts/generate_contracts.py`, `scripts/generate_test_vectors.py`, `scripts/validate_offline.py`; `src/TiaBridge.Core/README.md`, `src/TiaBridge.Core/Operations/OperationModels.cs`, `src/TiaBridge.Core/Transport/V1/ContractJson.cs`; `tests/README.md`, `tests/COVERAGE.md`, `tests/contracts/test_core_matrix.py`, `tests/contracts/test_offline_runner.py`, `tests/fixtures/contract-mutations.v1.json`, `tests/fixtures/core-determinism.v1.json`; `tests/unit/TiaBridge.Core.Tests/TiaBridge.Core.Tests.csproj`, `BoundaryTests.cs`, `ContractMatrixTests.cs`, `DeterminismTests.cs`, `LifecycleMatrixTests.cs`, `WorkspaceAdversarialTests.cs`. Starsze testy/fixtures B-01A/B-01B zachowano.

**Walidacja OFFLINE PASS:** 537/537 .NET (377 nowych + 160 regresyjnych), 17/17 Python (7 nowych + 10 regresyjnych), FAIL 0, SKIP 0. Potwierdzono 100 osobnych deterministycznych model cases, 19 metod request/result/error, niezależne golden SHA-256, wszystkie syntetyczne przykłady, zgodność generatorów i Core bez vendor refs. Pełny pipeline uruchomiono ponownie po poprawkach; locked restore offline i Build Release: 0 błędów/0 ostrzeżeń. Audyt: 32 Markdown, 190 lokalnych odnośników, 19 metod, bez błędów; formatowanie i diff PASS. Kontrola nazw/rozszerzeń i sygnatur sekretów oraz ręczny diff objęły tylko wskazane pliki; raporty, binaria, projekty Siemens i dane klienta pozostają poza indeksem. Wyniki TRX/QA/run.json w `.tia-bridge/offline/` są lokalnym dowodem, nie artefaktem produkcyjnym.

**Zakończenie i granice:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. Testy rzeczywistego IO/native round-trip i 100 planów pozostają odpowiednio w B-03/B-05B i B-04A, trwałość/kolejka/awarie procesu w B-02A–D. Fundament i walidacja są gotowe do osobno zleconego B-02A; B-02A ani integracja Siemens nie zostały rozpoczęte. Po commicie i zweryfikowanym push praca nad B-01C zostaje zatrzymana.

## 2026-10-25 — B-01B: Workspace i Capabilities

**Zakres:** implementacja offline w Core .NET 10, syntetyczne schematy/konfiguracje/manifesty, testy i dokumentacja. Wejściowy main/origin main: `c1590b7afd02c8edb423ec96c6a50197af619e95`, czysty worktree. Potwierdzono rzeczywistą implementację i wejściowe 91/91 testów B-01A; przeczytano obie instrukcje, README, dokumenty 02–09, ADR 0001/0002, contracts/v1, Core i rejestr zmian. Nie kopiowano kodu starego Bridge ani danych klienta.

- Workspace: konfiguracja wielu projektów, manifest pojedynczego eksportu, PLC/Unified/Classic, hierarchia grup, niezależne ObjectId z B-01A, locatory/identity evidence, relacje i rewizje source/native. Jednostka wymiany może obejmować wiele obiektów i plików; nie powstał importer pojedynczego logicznego obiektu.
- Path codec konkretyzuje dokument 04: NFC, percent UTF-8, prefiks `_` dla reserved/trailing names, sufiks 12 cyfr UUID, deterministyczne skracanie segmentu do 100 UTF-16 bez cięcia runy/escape, oryginalna nazwa w metadanych. Decode weryfikuje te metadane. Kolizje case/Unicode/skrócenia/sufiksu są rozróżnione lub odrzucane. Limit root+relative 240 UTF-16, bez spłaszczania hierarchii. Traversal/rooted/UNC/ADS i unsafe Windows paths są odrzucane.
- Obserwacje filesystemu są tylko modelem: wymagany pełny łańcuch od woluminu przez przodków workspace do celu, każdy reparse point blokuje. Nie wykonano IO Workspace ani sprawdzania rzeczywistych symlinków; przyszły adapter nadal musi obsłużyć freshness/TOCTOU i bezpieczne uchwyty.
- Trzy nowe schematy 1.0, typowana walidacja grafu/referencji/membership, coverage z jawnymi brakami, zamknięte pola i vendor_fields zachowujące unknown JSON. Native XML/YAML pozostają nieprzetwarzanymi referencjami do plików. Raw/model/unit hashes są osobne; unit hash wiąże wszystkie deklarowane pliki i metadane członków, profile, coverage, rewizje i zależności. Nie potwierdza faktycznych bajtów dysku ani projektu TIA. Nie powstaje baseline, receipt ani status SYNCED.
- Canonical JSON ma sortowanie ordinal, dokładne liczby dziesiętne bez zaokrągleń, zachowaną semantyczną kolejność tablic i limity 10 MiB wejścia/wyniku, depth 32, token number 128 i exponent ±1000. Odrzucane są duplicate keys, invalid UTF-8/surrogates, comments, NaN, trailing commas i przyszłe wersje. Bez resolvera sieciowego i parserów vendor.
- Capabilities: wersjonowany snapshot registry, hash, dokładny TIA/adapter/build/family/kind/language/OperationMethod, zakresy read/write, coverage i normalizer/profile. Rozdzielone designed/implemented/test-qualified, domyślna odmowa, brak fallbacków/wildcard. Kwalifikacje są niezależnym wejściem zweryfikowanym przez przyszłego zaufanego callera; ich deklaracja w JSON nie wystarcza. SessionPermission zawsze NOT_EVALUATED. Brak domyślnych wpisów Siemens i wykonywania grantów.
- Przykłady syntetyczne: dwa projekty, PLC, obie rodziny HMI, zagnieżdżone grupy, CON, Pump/pump, postacie Unicode, jawne zależności, nieobsługiwane typy, częściowy coverage i współdzielona jednostka hmi.js/hmi.yml. Zerowe raw/model hashes to placeholdery bez artefaktów; unit i capability hashes policzone nad zadeklarowaną treścią. synthetic.offline nie jest istniejącym adapterem.

**Zmienione pliki (27):**

- Core: `src/TiaBridge.Core/Workspace/WorkspaceModels.cs`, `WorkspacePaths.cs`, `WorkspaceValidation.cs`, `ArtifactJson.cs` oraz `src/TiaBridge.Core/Capabilities/CapabilityRegistry.cs`.
- Schematy: `contracts/v1/workspace-manifest.schema.json`, `workspace-config.schema.json`, `capability-registry.schema.json`; opis `contracts/README.md`. Istniejące DTO, schematy operacji, generator B-01A, identity i katalog 19 metod pozostają bez zmian.
- Konfiguracje: `config/examples/workspace.synthetic.json`, `capabilities.synthetic.json`, `README.md`; manifesty `examples/workspace/plc.manifest.json`, `hmi.manifest.json`; indeks `examples/README.md`.
- Testy: `tests/unit/TiaBridge.Core.Tests/WorkspaceTests.cs`, `CapabilityTests.cs`, `TiaBridge.Core.Tests.csproj`, `tests/contracts/test_workspace_contracts.py`, instrukcja `tests/README.md`.
- Dokumentacja: `README.md`, `src/TiaBridge.Core/README.md`, `docs/03_ARCHITECTURE.md`, `docs/04_SYNCHRONIZATION.md`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/12_WORKSPACE_CAPABILITIES.md`, `Zmiany.md`.

**Ocena wpływu i zgodność instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / sekcje 1–2 | Core B-01B jest implementacją offline; adapter i serwer nadal nie istnieją. Stan przyrostu opisany w README, bez zmiany instrukcji zgodnie z jawnym zadaniem. |
| Hierarchia / sekcja 3 | Zachowano zatwierdzone reguły 04 i D-06. Rozmieszczenie czystego registry w Core wynika z B-01B i zlecenia; 03 doprecyzowano organizacyjnie, Adapters będzie konsumentem/dostawcą deklaracji. Nie ustanowiono konkurencyjnego registry ani zmiany semantyki. |
| Niezmienne granice / sekcja 6 | Brak Siemens/host/dispatch. Manifest/hash nie nadaje SYNCED ani dowodu native HMI. W/S/B i outcome nie są utożsamiane. |
| Autoryzacja / sekcje 4–5, 8 | Kod nie tworzy grantów i nie wykonuje sesji. Zgodnie z zadaniem uprawnienia są NOT_EVALUATED. Nie użyto Computer Use ani operacji API TIA. |
| Testy i bramki / sekcje 7, 10 | Wyłącznie syntetyczne B-01B i regresje B-01A. Nie rozpoczęto B-01C, B-02, X-01 ani live testów. Pełna bramka fake przed Siemens pozostaje. |
| Dyscyplina zmian / sekcje 9, 11–12 | Zaktualizowano właściwe schematy/przykłady, opis formatu, build/test instrukcję i ten raport; dowody robocze w ignorowanym .tia-bridge. |
| Standard Git / sekcje 5, 11 | Jeden angielski Conventional Commit, wyłącznie nazwane ścieżki, staged review, normalny push i porównanie HEAD. |
| Dane nieufne / sekcje 6, 9 | Bounded JSON, unknown preservation albo odmowa, deterministyczne ścieżki/hashe, capability per exact version/scope. Nie wykonuje się znalezionych skryptów. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md pozostają bez zmian; pakiet realizuje ich reguły, nie ustanawia nowego workflow/uprawnień. ADR 0001/0002 oraz wymagania 02, interfejs 05, integracja 06, test strategy 07 i decyzje 09 pozostają bez zmian: architektura procesów, saga, semantyka 19 operacji i kwalifikacja Siemens nie zostały zmienione. Dokument 03 precyzuje miejsce czystych modeli wobec przyszłych adapterów, 04 odsyła do zrealizowanego formatu, 08 rejestruje stan pakietu. Brak nierozstrzygniętych konfliktów architektury lub kontraktu blokujących B-01B.

**Walidacja OFFLINE:** 160/160 testów .NET (91 B-01A + 69 B-01B, 0 pominiętych), 10/10 testów niezależnych JSON Schema/OpenAPI (6 B-01A + 4 B-01B). Oba manifesty oraz obie konfiguracje przeszły schema i walidację Core, wraz z bindingiem Workspace/CapabilitySetHash. Locked restore i build Release: 0 błędów, 0 ostrzeżeń. Formatowanie dotnet, reprodukowalność generatora B-01A, audit_quality.py (31 Markdown, 169 lokalnych odnośników, 19 metod, 8 pierwotnych negatywnych przypadków), diff --check i kontrola ścieżek/sekretów/danych projektowych: PASS. Brak nowych zależności i zmian lockfile. Testy Python uruchomiono w istniejącym osobnym venv qa-b01a-20261025 na lokalnym NTFS; systemowy Python nie posiada openapi-spec-validator, więc jego niepełny przebieg nie jest traktowany jako PASS. Wymagane testy wykonano ponownie kompletnie w venv. Raporty TRX i QA pozostają w ignorowanym .tia-bridge/.

**Granice:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. Nie uruchamiano TIA/Openness/workera, symulatorów, PLC/HMI ani pełnej synchronizacji. Nie weryfikowano prawdziwych plików Workspace; surowe hashe i coverage są deklaracjami modelu, a SessionPermission nie jest grantem. Gotowość do osobno zleconego B-01C: modele/kontrakty i syntetyczne fixtures są dostępne; B-01C nie rozpoczęty. Po commicie i push B-01B praca zostaje zatrzymana.

## 2026-10-25 — B-01A: kontrakty i rdzeń operacji

**Zakres wykonany:** biblioteka Core .NET 10 i walidacja offline; bez hosta HTTP, SQLite, trwałego ledger, workera, fake workera i wykonywania operacji. Stan wejściowy: czysty main, HEAD/origin main `5a249d0`; obie instrukcje zawierają aktualne Conventional Commits. Przeczytano wymagane dokumenty, schematy, przykłady, registry i integrację 06. Nie kopiowano starego Bridge ani danych klienta.

- Zaimplementowano niemutowalne, rozróżnialne tożsamości ProjectKey/ObjectId/OperationId/SessionEpoch, binding projektu i sesji oraz ContractVersion. Tożsamość obiektu nie zawiera nazwy/ścieżki; UUID nie jest dowodem tożsamości Siemens ani uprawnień.
- Zaimplementowano OperationRequest, IdempotencyKey/RequestIdentity, ExecutionBudget/metadane, typy błędów, stany i fazę. Reguły lifecycle nie przywracają terminalnego UNKNOWN do kolejki i nie uznają timeoutu za przerwanie wywołania. Status synchronizacji, kompilacji, symulacji i testów Runtime pozostaje oddzielny.
- Uzupełniono 19 kontraktów o zamknięte DTO wyników, kody/kategorie błędów, acceptance, binding sesji i OpenAPI 3.1.1. Wyniki są podsumowaniami i odwołaniami do dowodów; szczegółowe schematy manifestów/planów/baseline należą do następnych pakietów. Zachowano wersję wire 1.0, nazwy, required params, timeouty i uprawnienia. Doprecyzowano górny zakres after_sequence do Int64 i walidację stan/outcome/wynik; nie dodano operacji ani grantów.
- Walidacja odrzuca nieznane pola, duplicate keys, błędny UTF-8/JSON, wersje/enumy, przekroczenia rozmiaru/głębokości i błędne liczby. Kanonikalizacja sortuje klucze ordinal, normalizuje zadeklarowane UUID i dokładne liczby całkowite, zachowując treść i kolejność tablic. Idempotencja porównuje uwierzytelniony principal/projekt/key i SHA-256 body bez samego key; nie realizuje trwałej deduplikacji efektów.
- Diagnostyka błędnego wejścia nie ujawnia payloadu, nazw nieznanych pól ani wyjątków parsera. Referencja grantu jest wyłącznie formatem; faktyczna polityka i odbiorca pozostają do niezależnej weryfikacji. Brak vendor DLL, shell executor, klienta sieciowego i dostępu do projektów.
- Dodano deterministyczny generator schematów/OpenAPI/DTO C# i 38 poprawnych przykładów oraz niezależny zestaw 21 niepoprawnych żądań. Testy sprawdzają także zgodność artefaktów z generatorem.
- Skorygowano wyłącznie skrót kolejności backlogu: pełne B-02A–B-02D na fake przed Openness i X-01. Pozostałe zależności bez zmian; historyczny zapis rozbieżności zachowano.

**Pliki:** utworzono `TiaBridge.slnx`, `global.json`, projekt Core z packages.lock.json, `Identity/Identities.cs`, `Operations/OperationModels.cs`, `OperationLifecycle.cs`, `OperationCatalog.g.cs`, `Transport/V1/ContractJson.cs`, `WireModels.cs`, `MethodModels.g.cs`; nowe schematy acceptance/error/operation-result/session-identity i openapi.json w contracts/v1; 39 plików przykładów w examples/contracts/v1; scripts/generate_contracts.py i requirements-qa.txt; projekt testowy z lockfile i dwiema klasami w tests/unit/TiaBridge.Core.Tests oraz tests/contracts/test_contracts.py. Zaktualizowano operation-request.schema.json, operation.schema.json, README główne/Core/contracts/examples/tests/scripts, dokument 05, wskazany akapit backlogu i ten rejestr. Registry i pierwotne przykłady zachowano.

**Ocena wpływu instrukcji:** porównano zakres z AGENTS.md i Instrukcja_ChatGPT.md; pozostawiono je bez zmian zgodnie z zadaniem, ponieważ reguły architektury, autoryzacji i workflow nie uległy zmianie. README i dokumentacja kontraktów opisują aktualny fragment implementacji. Brak nierozstrzygniętych konfliktów architektury lub semantyki blokujących B-01A. Canonical profile, limity i granice walidacji opisano w contracts/README.md.

Kontrola Git: reguła ignorowania `*.ap*` obejmuje także nazwy request.sync.apply.json i response.sync.apply.json. Po kontroli syntetycznej treści te dwa dokładne pliki dodano przez named-path `git add -f --`, bez zmiany .gitignore ani dodawania projektów Siemens. Pozostałe ścieżki dodano zwykłym selektywnym add; raporty i binaria pozostały poza indeksem.

**Walidacja OFFLINE PASS:** locked restore, build Release (0 błędów, 0 ostrzeżeń), 91/91 testów .NET (0 pominiętych), 6/6 niezależnych testów JSON Schema/OpenAPI, wszystkie 19 kontraktów i 21 przykładów negatywnych, domyślne sprawdzenie generatora, formatowanie dotnet, audit_quality.py (29 Markdown, 136 lokalnych odnośników, 19 metod i 8 pierwotnych negatywnych kontroli), diff --check. Test regresyjny obejmuje równoległą walidację i chroni przed współdzieleniem mutowalnych constraintów ewaluatora. NuGet vulnerability scan dla obu projektów i zależności przechodnich: brak zgłoszonych podatności w użytych źródłach w chwili kontroli. Zależności .NET są przypięte lockfile; narzędzia QA Python zainstalowano w osobnym venv na lokalnym NTFS. Raporty robocze pozostają w ignorowanym .tia-bridge/.

**Granice:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. Nie uruchamiano TIA/Openness/symulatorów ani operacji PLC/HMI. Gotowość do osobno zleconego B-01B: fundamenty dostępne; B-01B, B-01C i B-02A–D nie rozpoczęte. Testy B-01A są pomocniczym warunkiem jego odbioru, nie realizacją przyszłego pipeline B-01C ani kwalifikacją fake/Siemens. Commit techniczny i normalny push zamykają wyłącznie ten pakiet.

## 2026-10-25 — standard komunikatów commitów Git

- W `AGENTS.md` wprowadzono Conventional Commits: angielskie, zwięzłe tematy `type(scope): short description`, opcjonalny scope, typ zgodny ze zmianą i opis rozszerzony tylko przy potrzebie technicznej; niekompatybilność oznaczana przez `!` i `BREAKING CHANGE:`.
- Ocena wpływu workflow: zaktualizowano również `Instrukcja_ChatGPT.md`, aby zadania i ocena wyników stosowały ten sam standard, z jawnym wyjątkiem od polskiego języka odpowiedzi. Komunikaty Git nie służą sprawozdawczości pracy ani celom podatkowym/księgowym; szczegóły pozostają w dokumentacji i raportach.
- Przejrzano istniejącą dokumentację pod kątem sprzecznych zaleceń: nie znaleziono konfliktów poza tymi plikami. Pozostałe dokumenty, kod, kontrakty i historia Git bez zmian; nie rozpoczęto B-01A ani operacji Siemens.

## 2026-10-25 — standaryzacja instrukcji ChatGPT i CODEX (P1–P6)

Zakres: wyłącznie `AGENTS.md`, `Instrukcja_ChatGPT.md` i ten rejestr; kontrola dokumentacji, commit i push. Czysty lokalny `main` zaktualizowano fast-forward z `759fbad` do `70c287d`, uwzględniając trzy istniejące commity instrukcji ChatGPT i jej rejestracji. Nie rozpoczęto B-01A, implementacji aplikacji ani operacji Siemens/PLC/HMI.

Przeczytano obie instrukcje, README, wymagania 02, architekturę 03, synchronizację 04, testy 07, backlog 08, ryzyka i decyzje 09 oraz dotychczasowy rejestr zmian. Dodatkowo sprawdzono ADR 0002 i zakres walidatora QA. Zachowano zatwierdzone decyzje, kontrakty JSON/API oraz podział Core/Host/Worker; dokumentacja nadal opisuje projekt, nie działającą aplikację.

| Problem | Status | Zmieniony dokument | Sposób rozwiązania |
|---|---|---|---|
| P1 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Oddzielono świeże warunki wejścia i zweryfikowany backup od importu, Compile, Save, niezależnego readbacku i promocji B przez CAS po weryfikacji całej grupy. Zachowano sagę 04, grant ADR 0002 i MODIFIED przy W różnym od S. |
| P2 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Ujednolicono hierarchię; jej kolejność nie uprawnia do zmiany architektury, bezpieczeństwa ani semantyki kontraktu. Konflikt wymaga raportu, rekomendacji i decyzji użytkownika; zależna praca czeka. |
| P3 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Wymagane nieblokujące przyjęcie, trwały ID i historia po restarcie, dostępny status przy zawieszeniu, idempotencja i zakaz ponowienia UNKNOWN. Progi i szczegółowe scenariusze pozostają w 02/07/08. |
| P4 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Jawna bramka B-02A–B-02D na fake obejmuje wszystkie osiem wymaganych scenariuszy przed rzeczywistym Openness, także read-only X-01; X-01 kwalifikuje worker przed B-03A. |
| P5 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Dodano wzajemne odnośniki i komplementarny podział: analiza/planowanie/zadania/ocena ChatGPT oraz wykonanie CODEX. |
| P6 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Obowiązkowa ocena wpływu i kontrola obu instrukcji oraz właściwych specyfikacji po zmianie zasad; decyzja z uzasadnieniem w Zmiany.md, bez automatycznego wymogu edycji obu instrukcji. |

**Ocena wpływu i porównanie sekcja po sekcji:**

- Sekcje 1–2 ChatGPT i „Zakres i stan” AGENTS: zachowano stan projektowy, role i granicę starego repo; doprecyzowano relację instrukcji (P5).
- Sekcja 3 ChatGPT i nowa hierarchia AGENTS: zgodna procedura konfliktów (P2); sekcje 4–5 ChatGPT i „Dyscyplina zmian” AGENTS zachowują ograniczone zadania, autoryzację i dowody.
- Sekcja 6 ChatGPT i „Niezmienne granice” / „Autoryzacja i testy” AGENTS: uzgodniono P1/P3 z 02/03/04 i ADR 0002. Pamięć P, zapis D, W/S/B i rewizje C/E/T pozostają oddzielne.
- Sekcje 7 i 10 ChatGPT oraz bramka testów AGENTS: zgodne zależności B-02A–D → X-01 → B-03A, bez przedwczesnej integracji (P4).
- Sekcje 8–9 ChatGPT i „Autoryzacja i testy” / „Dane nieufne i stabilność kontraktów” AGENTS: zachowano bezpieczeństwo PLC/HMI, osobne uprawnienia, wymagany coverage i rozdział OFFLINE/ENGINEERING/RUNTIME.
- Sekcje 11–12 ChatGPT i „Dyscyplina zmian” AGENTS: zgodne raportowanie, kontrola wpływu i obowiązek zapisania decyzji (P6).

Decyzja: zmienić obie instrukcje, ponieważ P1–P6 dotyczą ich wspólnego workflow i jego oceny. README pozostawić bez zmian: ma już odnośniki do obu instrukcji i aktualny stan projektu. ADR, wymagania, architektura, synchronizacja, strategia testów, backlog, kontrakty i przykłady pozostawić bez zmian: instrukcje precyzują ich stosowanie, nie ustanawiają nowej architektury, API ani progów NF.

**Dodatkowa rozbieżność do przeglądu:** skrót „Najkrótsza droga” w [backlogu](docs/08_IMPLEMENTATION_PLAN.md) wymienia tylko 2A/2B przed 3A/3B, podczas gdy zależności etapu 3 wskazują 2A–2D, a bramka etapu 2 wymaga testu przed rzeczywistym Siemens. Skrót pozostawiono bez edycji jako poza zakresem P1–P6; rekomendacja: przy osobnej aktualizacji backlogu uzgodnić go z pełną bramką. Nie jest to zgoda na pominięcie B-02C/D; obie instrukcje odsyłają do pełnych zależności.

Walidacja dokumentacyjna: **OFFLINE PASS** — porównanie sekcja po sekcji, zgodność P1–P6 z architekturą i synchronizacją, `py -3 scripts/audit_quality.py` (29 plików Markdown, 108 lokalnych odnośników, 19 metod, 3 przykłady i 8 negatywnych przypadków kontraktowych, bez błędów), `git diff --check` oraz kontrola struktury Markdown, UTF-8 i zachowania CRLF. Potwierdzono dokładnie trzy zatwierdzone ścieżki, bez zmian kodu, schematów i innych dokumentów; raport roboczy QA w ignorowanym `.tia-bridge/`. Linków zewnętrznych nie odpytywano; nie dodano nowych adresów zewnętrznych.

Gotowość: dokumentacyjne przygotowanie do osobno zleconego B-01A; testy silnika/fake, X-01, Engineering i Runtime pozostają NOT_RUN. Ta aktualizacja nie otwiera sesji TIA ani nie rozpoczyna implementacji.

## 2026-10-25 — instrukcja współpracy ChatGPT z CODEX

- Utworzono `Instrukcja_ChatGPT.md` zgodną z `AGENTS.md`, architekturą, planem implementacji i kontraktem synchronizacji.
- Opisano podział odpowiedzialności, źródła prawdy, workflow, standard zadań CODEX, bezpieczeństwo TIA/PLC/HMI, kryteria akceptacji oraz raportowanie.
- Zaktualizowano indeks dokumentacji w README. Zmiana wyłącznie dokumentacyjna; nie testowano TIA Engineering ani Runtime.


## 2026-10-24–2026-10-25 — analiza i projekt następcy Bridge

Zakres: ANALIZA + ARCHITEKTURA + PLAN IMPLEMENTACJI + DOKUMENTACJA + COMMIT + PUSH. Nie implementowano aplikacji ani nie zmieniano kodu starego Bridge. Polecenie uzupełniono o analizę Snap7 do testowania PLC.

- Przeczytano materiały wejściowe nowego repo i rzeczywistą implementację Python→PowerShell→Openness, sesje, backup, preservation, komparatory i OPC engine w `C:\RMA\t05_plc`. Utrwalono SHA-25632 wybranych źródeł/dowodów i oddzielono dirty pliki od HEAD.
- Zidentyfikowano 24 problemy/ograniczenia z priorytetami, lokalizacjami, mechanizmami, objawami, wpływem, rozwiązaniem i dowodami. Rozdzielono historyczne awarie/naprawy CRLF/identity od bieżącego synchronicznego wykonania, kosztów snapshotów i ostatniego readback mismatch.
- Potwierdzono read-only lokalne API V19 i dokładny projekt/urządzenia; projekt był dirty. Zinwentaryzowano PLCSIM/AdvancedV 6/WinCC oraz lokalne kontrakty producenta; nie wykonywano nowego importu, Archive, Compile, Save, load/RUN/STOP ani zmiany maszyny.
- Zaprojektowano host net10, oddzielny worker net48/x64, durable queue/outbox, epoch/ownership/quarantine, watchdog i bounded retry. Rozdzielono queued cancel, running cancel, termination i reconciliation. Długie wywołanie Siemens nie blokuje statusu hosta ani edycji CODEX.
- Zdefiniowano W/S/P/D/B/C/E/T, trzystronny plan, immutable snapshoty SHA-256, dependency groups, konflikt/delete/rename policy i formalne SYNCED dopiero po niezależnej weryfikacji.
- Zaprojektowano 19 operacji HTTP/CLI z opcjonalnym MCP, params/results/errors/deadlines/permissions; dodano projektowe schematy JSON i syntetyczne przykłady, bez serwera aplikacji.
- Rozdzielono compile PLC/HMI, simulator install/start/load/RUN/ready, Runtime Manager i browser tests. Ograniczenia licencji/publicznych API i TLS są jawne; wsparcie future adaptera nie wynika z instalacji.
- Odczyt OPC UA status w istniejącym venv: PASS dla WinCC Unified, writes 0 i zamknięta sesja; podstawowy Python miał brak zależności. Nie potwierdzono CPU endpointu ani sekwencji PLC. HTTPSlocalhost miał błąd TLS; HMI E2E NOT_RUN.
- Przeanalizowano natywny Snap7 i python-snap7, classic S7/PUT-GET, optimized DB/offsety, PLCSIM Advanced TCP, allowlist, codec/polling i unknown write. Adapter pozostaje OPTIONAL po osobnej kwalifikacji; niczego nie instalowano i nie łączono z pamięcią PLC przez S7.
- Przygotowano test strategy (unit/integration/E2E/8h resilience),16 obowiązkowych awarii, mierzalne NF cele, backlog etapów 0–11 z małymi commitami, zależnościami, ryzykami i bramkami; ryzyka R-01–20, decyzje D-01–10 i eksperymenty X-01–08.
- Zachowano i powiązano initial concept docs, zaktualizowano README/AGENTS i ADR 0002. Dodano skrypt kontroli spójności dokumentacji i kontraktów.

Wyniki wykonane: **94 ukierunkowane testy offline starego Bridge PASS**. Kontrola dokumentacji/kontraktów: **PASS**, 28 plików Markdown, 89 lokalnych odnośników, 19 metod, 3 przykłady i 8 negatywnych przypadków. `git diff --check`: PASS; wszystkie 32 źródła bez driftu względem indeksu w chwili sprawdzenia. Przejrzano staged diff 33 jawnie wskazanych plików; brak wykrytych sekretów, projektów i raw logów; `git diff --cached --check`: PASS. Normalny push jest weryfikowany po commicie. Szczegóły i ograniczenia: [dowody](docs/10_EVIDENCE_AND_QUALITY.md). Ten zapis nie stwierdza nowej integracji Engineering ani Runtime PASS.

Docelowy pierwszy etap implementacji: kontrakty + durable async engine na fake workerze, następnie read-only worker V19 i mały kwalifikowany FC SCL round-trip. Obecny commit ma zamknąć wyłącznie fazę projektową; nie rozpoczyna tego wdrożenia.

## 2026-10-26 — B-03A/E1 Trusted Qualification Evidence Provider

Na `f3c7f202d08d59d2d0d54b32a923cda130c9c925` dodano model evidence w Core i Host provider dla `project.inspect`: podpis ECDSA P-256/SHA-256 względem kluczy operatorowych z konfiguracji Host, weryfikacja manifestu/artefaktów, exact TIA/adapter/worker build, operation/profile/read scope/coverage/ProjectKey, deny dla wyników innych niż PASS, oraz decision journal. `evidence_id` jest selektorem, nie poświadczeniem. Host odczytuje wyłącznie stałą ścieżkę pod StatePath, nie przyjmuje dowodów/ścieżek/kluczy od klienta; brak trust anchor/store/dowodu odmawia. Scheduler sprawdza aktualny snapshot przed PrepareDispatch, bezpośrednio przed IPC oraz przed retry; restart rozwiązuje stare kolejki ponownie. IPC 1.1, HTTP/API v1, registry v1/v2 i Worker pozostały bez zmian.

Testy używają wyłącznie kluczy efemerycznych, syntetycznego payloadu i artefaktów. Nie dodano żadnych real Engineering evidence, publicznych kluczy ani wpisów w zaufanym magazynie. Zaktualizowano dokumenty capabilities, Host, B-03A i plan; nowy [dokument 17](docs/17_TRUSTED_QUALIFICATION_EVIDENCE.md) opisuje format, źródło zaufania, TOCTOU i procedurę enrollment. Przed real enrollment pozostaje decyzja operatora o izolowanym podpisywaniu, zatwierdzającym, ACL, rotacji/odwołaniu/wygasaniu; implementacja nie weryfikuje ACL i nie wykonuje enrollment. D-11 PASS/historyczny latest, NF-11 NOT_RUN, Worker handle risk UNKNOWN, Engineering BLOCKED/NOT_RUN, B-03B NOT_READY i produkcja NOT_RUN zachowano.

Wyniki: Release build całego rozwiązania PASS (0 ostrzeżeń). Core 564/564 PASS; E1 Host/V19 45/45 PASS; pełny Host 254/257, trzy FAIL w istniejącym `ResilienceArtifactTests` (po completion wynik GET `/result` zwrócił 200 zamiast oczekiwanego 422 dla modified artefaktu; przed completion przypadki truncated/missing nie osiągnęły oczekiwanego DEGRADED w 10 s). Testy i kod artefaktów nie należą do E1 zmian; nie zmieniano ich. Pierwsze Host suite było związane ze starym output path; pełny suite w konfiguracji E1 użył świeżych binariów i powtórzył te same trzy FAIL. Python kontrakty 42/42 PASS; audit_quality PASS (54 Markdown, 470 lokalnych odnośników); dotnet format whitespace PASS; git diff --check PASS. Nie uruchamiano Siemens ani validate_offline pełnego harnessu/D-11. E1 provider i testy specyficzne przechodzą; pełny Host suite pozostaje FAIL, więc przed integracją gałęzi należy osobno rozstrzygnąć awarie resilience.

Ocena wpływu: AGENTS i Instrukcja_ChatGPT sprawdzone sekcja po sekcji; pozostają bez zmian, bo nie zmieniono autoryzacji TIA, workflow CODEX ani zasad bezpieczeństwa. ADR 0001–0003 i kontrakty pozostają bez zmian; rozwiązanie realizuje obecny adapter/trust boundary bez nowego scheduler/grant/API/IPC. D-11 i historyczne raporty bez zmian. Production qualification NOT_RUN; D-11 current limited PASS zachowany; NF-11 NOT_RUN; Worker handle risk UNKNOWN; B-03A Engineering BLOCKED/NOT_RUN; B-03B NOT_READY.
