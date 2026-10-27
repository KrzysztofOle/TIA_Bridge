# 16 — B-02D: odporność, crash recovery i kwalifikacja

Stan 2026-10-25: zaimplementowany harness syntetyczny na istniejących [Storage](13_DURABLE_STORAGE.md), [schedulerze/IPC](14_SCHEDULER_IPC.md) i [HTTP/CLI](15_HTTP_API_CLI.md). [Uruchomienie i kod](../tests/resilience/README.md), [snapshot wyników](../tests/resilience/QUALIFICATION.md), [maszynowy raport](../tests/resilience/qualification.json). Nie uruchamia Siemens ani X-01. Wykonanie testów, kwalifikacja filesystemu/NF, formalna bramka i stabilność produkcyjna mają osobne wyniki.

## Macierz RT i dokładny zakres

Oryginalna [macierz RT](07_TEST_STRATEGY.md#obowiązkowa-macierz-odporności) pozostaje bez zmian. Harness nie zastępuje jej krótszą tabelą.

| RT | Dowód w B-02D | Ograniczenie |
|---|---|---|
| RT-03 | crash przed/po admission commit; dispatch intent/sent; receipt przed/po zapisie; result przed/po commit; restart epoch, journal, outbox, efekty i quarantine | sync jest wyłącznie syntetyczny; nie ma importu/Save/B promotion |
| RT-05 | dwa write tego samego projektu, drugi czeka; syntetyczny adapter zmienia P/B dopiero po potwierdzonym pierwszym efekcie, drugi STALE_PLAN z 0 effects; cancel race i lease fencing | rzeczywista rewalidacja planu/P/B i ExclusiveAccess należą do B-04C/X-01; guard nie udaje readbacku TIA |
| RT-06 | receipt już odebrany z procesu wstrzymany za deadline, UNKNOWN/TIMEOUT przed użyciem odpowiedzi; late proof i stary epoch nie promują wyniku; cancel/restart nie zwalniają quarantine | timeout nie potwierdza zatrzymania vendor call |
| RT-15 | własny child utrzymuje lock; anomaly jest wykryta, kolejne write zablokowane; PID/start potwierdzone, child zwalniany jawnie; unrelated sentinel nietknięty | nie kwalifikuje Job Object, Dispose, COM lub child TIA |
| RT-16 | 100 identycznych HTTP/8 klientów, duplicate dispatch, effect counter 1, 409 dla zmiany, ponowienie CLI po restarcie z tym samym ID; UNKNOWN bez replay | nie obiecuje exactly-once Siemens |
| RT-01 | wiszący worker, HB bez postępu, host/status/health/result/events i niezależne W | syntetyczny call |
| pozostałe RT | pełne kryteria Engineering/sync/simulation pozostają NOT_RUN | nie są zastąpione mockiem kompilatora, readbacku, OPC lub HMI |

Wszystkie wyniki i czasy po rzeczywistym uruchomieniu są w snapshot, nie w deklaracji macierzy. Historia UNKNOWN pozostaje terminalna, a nowe obserwacje nie cofają starego wyniku. Kwarantanna ma własny dowód projektu/operacji/epoch i nie wygasa razem z lease.

## NTFS, SQLite, WAL i artefakty

Aktualizacja B-03A/E2-B0 dodaje [bootstrap qualification-only](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md)
i migrację 004 z autoryzacją/cyklami/release, zachowując bajty i hashe 001–003.
Crash po rezerwacji nie odnawia budżetu; restart zamyka grant. To rozszerzenie offline
tej samej ścieżki recovery, bez zmiany RT/NF, progów, UNKNOWN/quarantine i historii.

Baza jest wyłącznie na własnym lokalnym NTFS poza repo. Nie zmieniono schematu/migracji v1–v3 ani kontraktów v1. Journal/outbox/disk-full injection nie zapełnia dysku i nie dotyka obcych baz. Walidacja B-02A nadal sprawdza niedozwolone lokalizacje, migracje, constraints, reparse i integrity.

WAL fault obejmuje bit flip ostatniej ramki i ucięcie jej końca po commit wyniku, z zachowanym prawidłowym prefixem intent/dispatch/receipt. Żadne połączenie SQLite nie jest otwierane pomiędzy crash a uszkodzeniem własnego WAL, aby nie wywołać checkpoint. Recovery musi zachować operation_id i sklasyfikować niepotwierdzoną mutację UNKNOWN, nie odtworzyć zniszczony sukces. To nie test utraty całego storage ani zasilania.

Artifact tests obejmują publish przed DB reference (orphan retained), uszkodzenie/truncation/missing przed terminalnym commit i uszkodzenie po sukcesie. Status zachowuje historyczny raport; odczyt result musi sprawdzić SHA i odmówić potwierdzenia uszkodzonego dowodu. Nie ma agresywnego GC.

## Pomiary i błędy wykryte przez harness

Regresja B-03A/E1-R wykazała błąd testowego `HttpResilienceProbe.Profile`: rewalidacja E1 tworzyła kolejne proof/template, a marker wskazywał ostatni zamiast faktycznie wysłanego pierwszego dowodu. Naprawa zachowuje jedną syntetyczną parę na OperationId, nadal odczytując bieżący profil przy rewalidacji. Test przed ingerencją sprawdza ID/SHA względem rzeczywistej odpowiedzi workera. Asercje 422/DEGRADED i limit 10 s pozostają niezmienione; [nowy raport i macierz regresji](B03A_E1_R_ARTIFACT_REPAIR.md) oddzielają tę naprawę od historycznej kwalifikacji.

Przygotowanie 2026-10-26 dodaje [dowody próbek 1.1](../tests/resilience/README.md#dowody-nf-02nf-03-od-formatu-11) oraz [raport gotowości](../tests/resilience/D11_PREPARATION_2026-10-26.md). Przepływ: niezmieniony Stopwatch → te same kolekcje → pełne próbki i agregaty → create-new/SHA w dotychczasowym katalogu run ID → walidacja odtwarzalności i bindingu → reporter. Metadane i zapis są poza mierzonymi przedziałami; nie dodano próbek, warm-up, retry ani nowych progów. Brak pełnych danych jest INSUFFICIENT dla nowej kwalifikacji; archiwalny odczyt historycznych agregatów nie tworzy nowego PASS. To rozszerzenie harnessu, bez nowej kwalifikacji D-11 lub Siemens i bez zmiany kryteriów z 07/08.

NF-01/02: 1000 status/health odczytów przy 1/8 klientach, 64 submit/8 klientów; osobno result/events i startup/history recovery. NF-08 idle jest rzeczywistymi 300 sekundami z CPU/RAM/handles/threads/processes. 1001 operacji sprawdza granicę dwóch epoch i brak utraty wyników, bez nazywania tego 8 h soak. NF-15 ponownie sprawdza wyścig o miejsce 1000 i dostęp odczytów przy pełnej kolejce. Środowisko zawiera CPU, fizyczny RAM, OS/runtime, filesystems i odczyt stanu AV; nie zmienia konfiguracji ochrony.

D-11-P parametryzuje wyłącznie progi NF KPI w jednym źródle [`performance-profiles.json`](../tests/resilience/performance-profiles.json), z odczytem przez te same testy i reporter. Każdy nowy raport wiąże profile name/version/hash, thresholds, próbki, p50/p95/p99/max i liczniki exceedance z run ID, git SHA i SHA artefaktów. Domyślny wybór to STRICT. Zmiana profilu nie ponownie interpretuje historycznych raportów.

Regresja 1001 wykryła zatrzymanie schedulera po wyczerpaniu per-worker historii 1000. Poprawka recyklinguje tylko bezczynny, potwierdzony własny worker, z nowym epoch; SQLite i dawne receipty pozostają. UNKNOWN/blocked nie wchodzi na tę ścieżkę. Początkowy health p95 116,61 ms przekroczył NF-01; hot GET health używa teraz raz zwalidowanego envelope i wewnętrznych typowanych danych, bez wielokrotnej walidacji całego schematu raportu. Inputy, durable reports i klient nadal używają pełnej walidacji Core. Nie zmieniono DTO ani uprawnień. Pierwszy pełny pomiar idle wykazał 3,6966% CPU przez polling pustej kolejki co 20 ms; pętla IDLE używa teraz 200 ms, aktywne wywołania nadal 20 ms. Obserwacja aktywnego wywołania korzysta z wewnętrznej projekcji report/hash/event z tego samego metadata readera, bez ponownej walidacji zamrożonego requestu w każdej iteracji. Admission, dispatch i zapis stanu nadal wykonują pełną walidację Core; hash, operation ID, schema/state/outcome/method i powiązanie journal są sprawdzane także w projekcji. Pełne 300 s pomiaru jest powtarzane, bez obniżania progu CPU.

NF-03 ma dwa jawne profile. Lokalny NTFS: 100 zapisów i replace 1 MiB co 100 ms podczas fake Hang, z kontrolą SHA. Historyczny profil repo na exFAT: 100 niezależnych prób replace 1 MiB bez Host i 100 podczas zawieszenia; odmowa musi pozostawić oryginał. Wynik negatywnego testu izolacji nie oznacza kwalifikacji NF-03 PASS. Nie stosujemy delete+copy, retry ani usuwania ochrony jako pozoru safe replace. Dokładny błąd i wpływ na bramkę są w raporcie.

Profil wydajnościowy NF-03 jest osobnym wymiarem od NTFS/exFAT. STRICT zachowuje p95 ≤100 ms; zatwierdzony SHARED_WORKSTATION 1.0.0 używa p95 ≤500 ms tylko dla kolejnej jawnie wybranej kwalifikacji. Lokalizacja filesystemu, odmowy, integralność i historia exFAT FAIL nie są zmieniane.

NF-07, pełny NF-08 scale/worker alert, NF-09/10 retention, rzeczywisty planer NF-13, live soak oraz testy sprzętowe nie są pozornie zaliczone. NF-14 dotyczy obecnego default deny i braku fizycznego wykonawcy; przyszły adapter wymaga własnej kwalifikacji.

## Pozostawiony katalog B-02C

Zweryfikowano dokładnie dev/storage-tests/a71bad688cf24e0eb6e04f85d6474ab9: marker TIA_BRIDGE2_STORAGE_V1, zgodne state_path i synthetic project 111…, własny niepoprawny Fake DLL, brak reparse, brak aktywnego procesu. Siedem plików można otworzyć wyłącznie do odczytu z FileShare.None. Nie ma obecnie blokady uchwytu; wcześniejsze blocked by policy było odmową automatycznej kontroli narzędzia, nie błędem SQLite/NTFS. Katalog pozostawiono bez obchodzenia tej polityki. Nowa regresja potwierdza cleanup i zwolnienie Process również po oczekiwanym nieudanym starcie; Stop używa finally do zwolnienia własnego procesu także po nieudanej asercji.

## NF-11 i formalna bramka — rozbieżność do decyzji

### Zatwierdzone rozstrzygnięcie — 2026-10-26

Użytkownik zatwierdził [D-11](09_RISKS_AND_DECISIONS.md#decyzje): rozdzielenie ograniczonej bramki X-01 read-only od pełnej kwalifikacji stabilności produkcyjnej. Warunki normatywne są w [07](07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only) i [08](08_IMPLEMENTATION_PLAN.md#etap-2--silnik-operacji-procesy-i-recovery-mvp). Obejmują PASS RT-03/05/06/15/16 na Fake, recovery/hang/timeout/cancel/late/duplicate/loss/UNKNOWN/quarantine i stale epoch, NF-01/02/03/04/05/06/12/14/15 w kwalifikowanym lokalnym NTFS, rzeczywisty idle ≥5 min, krótki przebieg ≥1000 operacji i brak krytycznych błędów wykonania/izolacji.

NF-11 minimum 8 h pozostaje obowiązkowe w B-10B/R-14; NOT_RUN przed ograniczonym X-01 jest dopuszczone, bez uznania testu za PASS. NF-03 exFAT: FAIL pozostaje widoczne. Pozytywna kwalifikacja NTFS dotyczy wyłącznie tego profilu; SQLite/journal/outbox/artifacts i operacje zależne od kwalifikowanego zapisu/replace muszą pozostać na lokalnym NTFS. Nie kwalifikuje to badanego exFAT ani nie pozwala na delete+copy lub relokację danych.

[Odrębny raport decyzji i oceny dowodów](../tests/resilience/X01_LIMITED_GATE.md) oraz [JSON oceny](../tests/resilience/x01-limited-gate.json) wiążą nową interpretację z niezmienionym przebiegiem B-02D. Nie są nowym pomiarem. Wyniki: B-02 OFFLINE TEST EXECUTION: PASS; B-02 OFFLINE QUALIFICATION: FAIL dla pełnego badanego profilu z exFAT; X-01 LIMITED READ-ONLY GATE: PASS wyłącznie w udokumentowanym profilu NTFS; PRODUCTION QUALIFICATION: NOT_RUN. ENGINEERING i RUNTIME pozostają NOT_RUN.

### Historia rozbieżności i odnośnik archiwalny

Zachowano tytuł nadrzędny tej sekcji jako cel odnośnika w niezmiennym historycznym raporcie. Na etapie B-02D dokument 07 łączył „1000 operacji syntetycznych i minimum 8 h”, 02 definiował pełny NF-11, a R-14 wskazywał B-10B. Jednocześnie ogólna bramka 08 i obu instrukcji odsyłała do obowiązkowych testów 07/08 bez rozdzielenia wejścia read-only od produkcji. Miejsca sugerujące bezwarunkowy pełny soak przed X-01: 07 „Wydajność…” i „Dowody B-02D…”, 08 „Kolejność…” i „Etap 2”, AGENTS „Autoryzacja i testy”, Instrukcja_ChatGPT §10. Opisy aktualnego stanu w README, tests/README, tests/resilience/README, 09 i niniejszej sekcji podtrzymywały stan oczekiwania na decyzję. Dokument 03 i ADR 0001/0002 nie nakładały dodatkowej bramki 8 h; granice procesów i autoryzacji pozostają bez zmian.

Użytkownik najpierw odłożył wykonanie 8 h do B-10B, żądając raportu rozbieżności. Dlatego [snapshot MD](../tests/resilience/QUALIFICATION.md) i [JSON](../tests/resilience/qualification.json) słusznie zachowują ówczesne OFFLINE/GATE FAIL i X-01 NOT_READY; nie są przepisane po decyzji. Propozycja z tej sekcji została następnie zatwierdzona jako D-11 w osobnym zadaniu dokumentacyjnym. Historyczne wyniki NF-03 exFAT FAIL, NF-11 NOT_RUN oraz ENGINEERING/RUNTIME NOT_RUN nie uległy zmianie.

Reporter i matrix.json zachowują konserwatywną, historyczną ocenę całego przebiegu B-02D. Nie należy odczytywać ich dawnych pól gate/x01_readiness lub missing_blocks_x01 jako aktualnej interpretacji D-11; oddzielna ocena wskazana powyżej obowiązuje tylko dla przypiętych dowodów i profilu NTFS. Nowe wykonanie testów lub zmiana kodu/środowiska wymaga ponownej oceny dowodów; PASS nie przenosi się automatycznie.

Przygotowany przez użytkownika folder przyszłych testów `X:\TESTY_TIA_Bridge` i granice zgody opisano w [09](09_RISKS_AND_DECISIONS.md#przygotowane-środowisko-przyszłego-x-01). Filesystem X: nie został zweryfikowany. PASS ograniczonej bramki nie oznacza sprawdzenia tej lokalizacji, autoryzacji mutacji ani zgody na uruchomienie X-01 teraz. X-01 pozostaje osobnym zadaniem read-only. UNKNOWN, ownership, auth, backup i zasady mutacji pozostają bez zmian.
