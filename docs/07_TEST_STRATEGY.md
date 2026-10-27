# 07 — Strategia testowania i dowody

Plan docelowy, nie wynik testów nowej aplikacji. Aktualnie wykonane kontrole opisuje [10](10_EVIDENCE_AND_QUALITY.md). Każdy wynik ma warstwy `OFFLINE`, `ENGINEERING`, `SIMULATION`, `RUNTIME` z `PASS|FAIL|ERROR|BLOCKED|NOT_RUN|SKIP`. SKIP/NOT_RUN nie agreguje się do pełnego PASS. SYNCED jest odrębnym statusem obiektu, a nie wynikiem testów działania.

B-01C realizuje ograniczoną walidację offline istniejących kontraktów/Core: [macierz pokrycia i odroczone testy](../tests/COVERAGE.md), [jedno polecenie uruchomienia](../tests/README.md). 100 deterministycznych przypadków modeli nie stanowi testu planera B-04A, trwałości B-02 ani zachowania Siemens.

## Testy jednostkowe i kontraktowe

| Obszar | Wymagane przypadki i kryterium | Dowody |
|---|---|---|
| parsowanie/serializacja | golden XML/SCL/JS, UTF-8 BOM/EOL, polskie znaki, unknown fields, duplicate JSON keys, limit depth, XXE i traversal; zero niejawnej utraty pól | fixture provenance/hash, expected typed model, negatywne wyniki |
| manifest/path | deterministyczne UUID binding, case/Unicode collisions, reserved devices, rename, MLC file pairs | porównanie 100 powtórzeń, manifest/hash/path mapping |
| compare/conflicts | pełna tabela W/P/B, nowe/usunięte/missing, converged, non-overlap edits, unknown coverage | status/reason per object, oba artifacts preserved |
| plan/dependencies | DAG, cykle, unknown dynamic refs, S zmienia się podczas capture, B CAS race | immutable plan+input hash, brak niekwalifikowanej mutacji |
| operacje/kolejka | transaction intent/outbox, queue capacity/deadline, ownership, queued cancel race, retry budget | DB przed/po fault point, monotonic event sequence |
| timeout/breaker | fake monotonic clock, worker żywy bez postępu, late result, half-open probe | deadline i heartbeat osobno, zero retry możliwego write |
| idempotencja | identyczne submit 100 razy→1 ID; ten sam key/inna treść→409 | unique constraint i side-effect counter |
| recovery | crash między każdą parą intent/dispatch/receipt/Save/B commit | zero fałszywych SUCCEEDED/SYNCED; quarantine po unknown |
| policy | utrata grantu, inne SID/project/epoch, delete bez zakresu, fizyczny download, obcy simulator | odmowa przed wywołaniem vendor/transport |
| kontrakt API/CLI/MCP | registry 19 metod, wymagane params/error/deadline, JSON schema, lifecycle mapping | contract tests bez Siemens; MCP Tasks opcjonalna negocjacja |
| OPC/Snap7 codecs | jakości/statusy OPC, namespace remap, offset/endian/string/bit/layout Snap7 | golden vectors, brak sztucznego Good dla Snap7 |

Fake adapter wykonuje skrypty awarii w izolowanym procesie. Pozwala zablokować call, zabić worker lub utracić IPC bez ryzyka dla TIA. Dopiero odrębne, ograniczone testy na kopii syntetycznej potwierdzają, jak zachowuje się Siemens; fake nie dowodzi cleanup vendor resources.

## Testy integracyjne Engineering

Przygotowanie B-03A/E2 ma osobny [pakiet E2-A](B03A_E2_A_PREPARATION.md):
[E2-B1](E2-B1_TEST_PLAN.md) `T05_RecipeSim_V19_Testowy` przed
[E2-B2](E2-B2_TEST_PLAN.md) `T04_T05-000_Testowy`. Jeden projekt naraz, B2 dopiero po
udokumentowanym B1 qualification PASS, release/trend review i nowym preflight/zgodzie.
Plany dotyczą bounded read-only Discovery, bez operacji round-trip opisanych poniżej;
przygotowanie offline nie uruchamia Engineering ani nie zmienia wymagań RT/NF.
Bootstrap exact rzeczywistych dowodów pozostaje osobną decyzją, bez fikcyjnego PASS.

Pierwszy projekt: czysta syntetyczna kopia AP19 na NTFS, jeden SCL FC bez Safety/hardware side effects. Fresh identity + backup → export → edycja → plan → Apply → Compile → Save → nowy Attach/eksport → compare W/P/B. Zweryfikuj także niezmieniony blok kontrolny i inventory. Pierwszy round-trip musi wykazać zachowanie komentarzy, atrybutów, version metadata i referencji, nie tylko ciała FC.

Następnie grupy UDT→GlobalDB→FB→IDB i zależne FC/OB osobno, XML LAD początkowo read-only; wszystkie operacje macierzy testowane per kind/version/CPU/language. HMI: oddzielne rodziny klasyczne/Unified, natywne JS+YAML i publiczne struktury; brak readbacku required dimension blokuje capability. Import błędnego pliku, half-import, compile errors/warnings i utrata Save reply mają testy z receiptami.

Operacje live mają jawny test-run grant i owner session. Nie wykonujemy testów awarii na bieżącym dirty T05 ani na projekcie produkcyjnym. Nie odtwarzamy crash-prone SyntaxCheck/AutomaticTags w aktywnym projekcie. Archive/retrieve/restore testuje się do innej ścieżki z zachowanym oryginałem.

## PLC przez OPC UA

Runner w odrębnym venv/procesie, przypięta wersja biblioteki i lockfile. Istniejący read-only engine jest źródłem wymagań i doświadczeń, nie kopiowanym automatycznie backendem. Połączenie wymaga jawnego profilu TEST (odizolowane, anonymous/None tylko zgodnie z grantem) albo SECURE z weryfikacją certyfikatów/URI/SAN i chronionym kluczem. Nie ma downgrade lub automatycznego trust unknown cert.

Resolve tagów: namespace URI + identifier/konfiguracja, nie zakodowany na stałe namespace index. Read/Browse ograniczone depth/nodes/deadline, Good quality i freshness sprawdzane przy każdej wartości. Subskrypcja negocjuje sampling/publishing, monitoruje sequence/overflow/keepalive; reconnect po ustaniu starej sesji odtwarza mapowanie i monitored items. Nie ciągnie przez przerwę pozornie ciągłej sekwencji testowej.

Scenariusz PLC: inputs z allowlist testowej → command RequestId → oczekiwanie AckId i stanu → asercje output/error → kontrolowany cleanup, tylko jeśli bezpieczny i w tym samym lease. Write do procesowych tagów, Activate/sterowania maszyną lub nieznanej CPU blokowany. Nie utożsamiać wartości w WinCC cache ze stanem CPU; profil wskazuje, czy endpoint to CPU czy HMI i jak potwierdza loaded build. Snapshot wielu tagów nie jest atomowy; sekwencja/testowy licznik wykrywa zmieszane cykle.

Testujemy connect/disconnect, bad quality, stale data, source/server clock skew, timeout stanu, duplicate ack, reconnect i utratę odpowiedzi na write. Zapis o nieznanym efekcie wymaga odczytu/reconciliation, nie retry. Wynik ma timestamps klienta i serwera, quality, mapping/build/scenario hashes, trace assertion i liczbę write. Lokalny status WinCC PASS w audycie nie potwierdza tych scenariuszy.

## PLC przez Snap7

Niezależny, OPTIONAL adapter opisany w [11](11_SNAP7_PLC_TESTING.md). Obsługuje classic S7 na kwalifikowanym testowym DB/CPU. Wymaga potwierdzonych offsetów i layout fingerprint; nie ma natywnego odpowiednika OPC UA subscriptions/quality. Polling/timeout/rate są ograniczone; write tylko do DB mailbox z grantem symulacji. Brak dostępu do optimized DB nie może skutkować automatyczną zmianą konfiguracji CPU.

Unit testy codecs i mock server nie dowodzą wykonania programu PLC. X-08 wymaga rzeczywistej izolowanej instancji PLCSIM Advanced i porównania wyników scenariusza z OPC UA, jeśli oba transporty są dostępne. Nie używamy Snap7 do start/stop CPU, force, block download lub obchodzenia policy SECURE. Test awarii Snap7 ma kończyć się ERROR/inconclusive, z czynnym API hosta.

## HMI przez Playwright

Warunki: zweryfikowany **testowy** URL i Runtime project/build, izolowany PLC partner, certyfikat zaufany w profilu testowym, konto/rola testowa UMC/local, zgodny browser i WinCC Runtime license. Sam HTTP 200/IIS lub aktywny WCCIL proces nie daje READY. Nie przekazujemy credentiali w CLI, projekcie Git, Playwright storageState lub trace do publicznego repo; artifacts z sesją logowania wymagają redakcji/ACL/retencji.

Dla Unified web: najpierw read-only discovery DOM/a11y, potem page-object fixtures. Preferuj role/label/stabilne kontrakty i frame locators dla CWC; opieranie się wyłącznie na koordynatach lub generowanych CSS IDs wymaga osobnego uzasadnienia. Playwright wspiera [locators](https://playwright.dev/docs/locators) i [frames](https://playwright.dev/docs/frames); to nie dowodzi dostępności selektorów dla konkretnej kontrolki Unified. Canvas/custom drawing może nie eksponować tekstowych kontrolek. Wtedy: publiczny Runtime API adapter do danych plus ograniczone porównanie obrazu jako osobny poziom dowodu; brak domyślnego pełnego PASS UI.

Scenariusze: login i expected role → ekran startowy → nawigacja i powrót → odczyt wartości z quality → wprowadzenie wartości tylko do testowego pola → oczekiwanie ack ze źródła → alarm testowy, widoczność/severity/time i acknowledge tylko dla przygotowanego alarmu symulacji → screenshot/trace/console/network/error report. Widoczny tekst potwierdza UI, a test logiki wymaga powiązanego PLC evidence.

Dla natywnego Runtime Advanced bez web UI Playwright nie jest właściwym adapterem. Alternatywa: udokumentowany Runtime API do danych oraz oddzielny Windows UI Automation/Computer Use adapter dla UI, po zgodzie A/B. WebNavigator/Professional/panele mają własną macierz i licencje; nie zakładamy ich zgodności z Unified. Lokalny błąd TLS w audycie pozostawia testy browser NOT_RUN, bez wyłączenia weryfikacji certyfikatów.

## Obowiązkowa macierz odporności

Wszystkie testy wymagają sprawdzenia responsywności `operation.status` i edycji niezależnego W podczas awarii. Dowody wspólne: operation/step IDs, immutable request/plan/snapshot hashes, event sequence, fake/vendor version, timestamps i kontrola braku nieautoryzowanego efektu.

| ID | Wyzwolenie awarii | Oczekiwany rezultat / kryterium sukcesu | Dodatkowe dowody |
|---|---|---|---|
| RT-01 | TIA nie odpowiada: fake call wstrzymany; live tylko zatwierdzony test diagnostyczny | host działa w NF-01, worker heartbeat nie udaje postępu; po deadline FAILED read albo RECONCILIATION_REQUIRED write; brak następnego dispatch | histogram API, phase age, vendor call active flag, quarantine |
| RT-02 | zamknięcie TIA w trakcie operacji na kopii | zerwanie IPC/API → identity invalid, wszystkie proxy porzucone; możliwa mutacja UNKNOWN; żadnego fallback do innego PID | PID/start przed/po, exception chain, inwentarz po ponownym otwarciu |
| RT-03 | restart hosta podczas sync między intent i receipt | zaakceptowane ID pozostaje; RUNNING write przechodzi do reconciliation; queued bez dispatch może ruszyć po recovery; brak powtórzonego importu | SQLite/outbox, restart epoch, side-effect counter |
| RT-04 | CODEX zmienia W po rozpoczęciu importu S | importuje wyłącznie S; Wnew zachowane; operation report może potwierdzić S, bieżący object MODIFIED, workspace_advancedtrue | hashe W przed/po, S, native readback i B |
| RT-05 | dwa write żądania tego samego projektu | jedno w wykonaniu, drugie queued; stale expected P/B blokuje drugie po zmianie pierwszego; zero overlap | scheduler/lease/fence timeline, expected/actual fingerprints |
| RT-06 | przekroczenie execution deadline | w terminie pojawia się TIMEOUT, bez obietnicy kill call; late result zachowany, nie zmienia starego UNKNOWN w sukces bez verify | deadline/clock, late receipt, brak retry write |
| RT-07 | drugi z trzech importów zwraca błąd | pierwszy z dowodem pozostaje w raporcie; grupa nie promuje B, partial state zachowany; bez rollbacku/retry | step journal, exports obu stanów, backup, recovery plan |
| RT-08 | edycja TIA i W tego samego obiektu różnie | CONFLICT przed mutacją; obie treści i B zachowane; resolution na starych hashach odrzucone | diff 3/coverage, mutation counter 0 |
| RT-09 | symulator przestaje odpowiadać | NOT_READY/ERROR, testy przerywają writes i sekwencję; host i inne zadania działają; brak stop cudzej instancji | lease, simulator events, program fingerprint, test timeline |
| RT-10 | OPC UA zrywa połączenie podczas subskrypcji/write | test ERROR/inconclusive, bad/stale samples nie przechodzą; reconnect ograniczony, write UNKNOWN nieponawiany | session ID, sequence gaps, quality, write ack/outcome |
| RT-11 | HMI Runtime nie startuje lub TLS/UMC odmawia | start nie daje READY; test.hmi BLOCKED/ERROR, brak ślepych kliknięć i trust bypass | Runtime Manager exit/status, HTTPS/login diagnostics, browser trace bez sekretów |
| RT-12 | niepoprawny plik repo / traversal / JSON duplicate / broken XML | validation fail przed backup/import; nie zmienia P ani B | validation findings z path/line, adapter calls 0 |
| RT-13 | compiler ErrorCount>0 | Engineering FAIL; brak końcowego SYNCED po Apply; partial dirty state opisany, bez automatycznego Save/rollbacku | rekurencyjne compiler messages i fingerprint C |
| RT-14 | compiler warnings | raport zachowuje ostrzeżenia; default reject niezaakceptowanych warnings; jawna polityka może dopuścić wyłącznie wskazany fingerprint | warnings policy hash, counts i outcome obu wariantów |
| RT-15 | worker zostawia uchwyt/child/lock | wykryte ownership i cleanup anomaly; nie uruchamia drugiego write bez quiescence; po testach brak własnych orphan children; TIA użytkownika nieubity | PID/start tree, handle counts, dispose result, quarantine persistence |
| RT-16 | CODEX ponawia żądanie po utracie response | ten sam key/body→ten sam ID i raport, effect counter 1; inny body→409; UNKNOWN nie jest replay | unique ledger, request hashes, disconnect/retry timeline |

Dodatkowe REQUIRED: queue cancel vs dispatch race; śmierć workera przy żywym host; wygaśnięcie lease przy nadal aktywnym TIA call; disk full podczas journalingu/Archive; corrupt/truncated WAL/artifact; utrata zasilania (osobny laboratoryjny test storage); exFAT workspace safe staging; zmiana version normalizatora; brak license; odmowa obcego SID. Snap7: layout drift, złe offsety/bit/endian, read-after-write timeout i sąsiedni bajt niezmieniony. Wszystkie destructive/fault injection najpierw fake, potem wyłącznie osobno autoryzowane kopie.

## Wydajność, stabilność i dostarczanie dowodów

NF KPI w trybie offline mają dwa jawne profile w [D-11-P](20_PERFORMANCE_QUALIFICATION_PROFILES.md). `STRICT` pozostaje domyślny i zachowuje wszystkie dotychczasowe progi. Kandydacki `SHARED_WORKSTATION` jest wybierany jawnie przez runner; nie wymaga bezczynności systemu, a `PASS_WITH_LOAD` jest akceptowanym stanem preflightu, po którym progi nadal oceniają pełne próbki. Wymagania bezpieczeństwa/poprawności zawsze wymagają PASS. Timeouty operacji, kolejki, heartbeat, IPC/native call, recovery i limity liczby operacji nie są KPI i nie ulegają zmianie. Szczegółowe progi, obciążenie, próbkowanie i historia są w dokumencie 20.

Pełna kwalifikacja stabilności w B-10B/R-14 obejmuje profil NF-01–NF-15 z [02](02_REQUIREMENTS.md), minimum 1000 operacji syntetycznych i minimum 8 h NF-11 oraz osobny read-only live soak 100 operacji. Minimum 8 h nie jest warunkiem ograniczonego eksperymentu X-01 read-only; pozostaje obowiązkowe przed odpowiednią kwalifikacją stabilności produkcyjnej. Mierz warm/cold Attach, Export per kind/size, compile duration, CPU/RAM/handles/process count i API latency podczas wiszącego call. Nie porównuj czasu OPC i Snap7 z różnymi buildami DB lub częstotliwością. Wartości celów należy skorygować pomiarami, nie przedstawiać jako już osiągnięte.

Pakiet dowodów: machine-readable `run.json`, test IDs, sanitized event journal, plan/source/capability/normalizer versions, compiler results, raw artifact hashes i wybrane screenshots/trace. Duże/zawierające projekt artifacts pozostają chronione poza Git; repo zawiera syntetyczne fixtures i podsumowania z hashami. Redakcja nie może usuwać informacji o UNKNOWN lub niespełnionym kryterium. CI offline nie potrzebuje Siemens; Windows integration lane jest opt-in z bezpiecznym profilem, a E2E nigdy nie uruchamia się automatycznie na fizycznym PLC.

## Ograniczona bramka X-01 read-only

Zatwierdzona decyzja [D-11](09_RISKS_AND_DECISIONS.md#decyzje), 2026-10-26, określa warunki przed osobno zleconym X-01 read-only:

- RT-03, RT-05, RT-06, RT-15 i RT-16 mają PASS na Fake Workerze, w oryginalnym zakresie kryteriów, bez udawania importu, Save lub readbacku TIA.
- Dowody potwierdzają trwałość zaakceptowanych operacji po restarcie, hang, timeout, late response, utratę procesu, idempotencję i bezpieczeństwo anulowania. UNKNOWN i kwarantanna zachowują semantykę po restarcie; stary epoch nie zatwierdza wyniku; niepewne mutacje nie są automatycznie ponawiane.
- NF-01/02/03/04/05/06/12/14/15 mają pozytywne dowody we właściwym zaimplementowanym zakresie, w jednoznacznie opisanym profilu lokalnego NTFS. State/journal/outbox/artifacts i operacje zależne od kwalifikowanego zapisu/replace są na NTFS. NF-14 potwierdza obecny default deny, nie uprawnienia przyszłych odbiorców PLC/HMI.
- Wykonano rzeczywisty pomiar idle ≥5 min i krótki przebieg ≥1000 operacji. Nie są one zaliczeniem pełnego NF-08 scale ani NF-11 soak.
- Nie ma nierozwiązanych błędów krytycznych bezpieczeństwa wykonania lub izolacji procesów. Brak dowodu wymaganego warunku daje BLOCKED; negatywny wynik wymaganego warunku w wybranym profilu daje FAIL.

NF-11 może pozostać NOT_RUN przed tym eksperymentem; B-10B/R-14 pozostaje obowiązkowe. Historyczny NF-03 exFAT: FAIL nie jest zmieniany ani przenoszony na NTFS. Pozytywny NTFS nie dopuszcza operacji zależnych od safe replace na niezakwalifikowanym exFAT i nie uprawnia do relokacji danych.

PASS tej bramki pozwala wyłącznie przygotować osobne zadanie X-01 read-only, z odrębną autoryzacją i weryfikacją środowiska, tożsamości projektu, exact DLL/rights oraz własności procesów. Nie uruchamia X-01 i nie nadaje zgody na mutacje, download ani sterowanie PLC/HMI. Fake nie dowodzi poprawności Siemens.

Profil `SHARED_WORKSTATION` nie zastępuje ani nie modyfikuje tej zatwierdzonej bramki. Propozycja wejścia E2-B1 na podstawie nowego profilu wymaga osobnego zatwierdzenia użytkownika; do tego czasu wymaganie D-11 powyżej pozostaje aktywne.

## Kolejne poziomy kwalifikacji i dowody B-02D

| Poziom | Obowiązkowy zakres i znaczenie |
|---|---|
| B-02 OFFLINE TEST EXECUTION | rzeczywiste testy regresji i odporności; zielone testy nie zastępują kwalifikacji NF/filesystemu |
| B-02 OFFLINE QUALIFICATION | wynik pełnego badanego profilu; historyczny FAIL exFAT pozostaje nawet przy PASS ograniczonej bramki NTFS |
| X-01 LIMITED READ-ONLY GATE | powyższe warunki na Fake/NTFS; PASS/FAIL/BLOCKED, bez potwierdzenia Siemens |
| Późniejsza integracja Siemens | osobny X-01 i kolejne pakiety kwalifikują attach/dispose, adaptery, RT Engineering/sync/simulation i live read-only; brak automatycznego zaliczenia przez Fake |
| PRODUCTION QUALIFICATION | pełna stabilność B-10B (NF-11 ≥8 h), skala, retencja i wymagane RT/NF dla release oraz testy odbiorców, izolacji i uprawnień; samo X-01 nie wystarcza |

[Harness i raport](16_RESILIENCE_RECOVERY.md) dokumentują syntetyczny Host/CLI/Fake flow. [Historyczne wyniki](../tests/resilience/QUALIFICATION.md) i [JSON](../tests/resilience/qualification.json) są niezmienne. [Odrębna ocena D-11](../tests/resilience/X01_LIMITED_GATE.md) stosuje zatwierdzone kryteria do tych dowodów; nie zamienia NOT_RUN w PASS i nie zmienia oryginalnej macierzy RT.
