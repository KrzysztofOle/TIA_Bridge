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

Baza jest wyłącznie na własnym lokalnym NTFS poza repo. Nie zmieniono schematu/migracji v1–v3 ani kontraktów v1. Journal/outbox/disk-full injection nie zapełnia dysku i nie dotyka obcych baz. Walidacja B-02A nadal sprawdza niedozwolone lokalizacje, migracje, constraints, reparse i integrity.

WAL fault obejmuje bit flip ostatniej ramki i ucięcie jej końca po commit wyniku, z zachowanym prawidłowym prefixem intent/dispatch/receipt. Żadne połączenie SQLite nie jest otwierane pomiędzy crash a uszkodzeniem własnego WAL, aby nie wywołać checkpoint. Recovery musi zachować operation_id i sklasyfikować niepotwierdzoną mutację UNKNOWN, nie odtworzyć zniszczony sukces. To nie test utraty całego storage ani zasilania.

Artifact tests obejmują publish przed DB reference (orphan retained), uszkodzenie/truncation/missing przed terminalnym commit i uszkodzenie po sukcesie. Status zachowuje historyczny raport; odczyt result musi sprawdzić SHA i odmówić potwierdzenia uszkodzonego dowodu. Nie ma agresywnego GC.

## Pomiary i błędy wykryte przez harness

NF-01/02: 1000 status/health odczytów przy 1/8 klientach, 64 submit/8 klientów; osobno result/events i startup/history recovery. NF-08 idle jest rzeczywistymi 300 sekundami z CPU/RAM/handles/threads/processes. 1001 operacji sprawdza granicę dwóch epoch i brak utraty wyników, bez nazywania tego 8 h soak. NF-15 ponownie sprawdza wyścig o miejsce 1000 i dostęp odczytów przy pełnej kolejce. Środowisko zawiera CPU, fizyczny RAM, OS/runtime, filesystems i odczyt stanu AV; nie zmienia konfiguracji ochrony.

Regresja 1001 wykryła zatrzymanie schedulera po wyczerpaniu per-worker historii 1000. Poprawka recyklinguje tylko bezczynny, potwierdzony własny worker, z nowym epoch; SQLite i dawne receipty pozostają. UNKNOWN/blocked nie wchodzi na tę ścieżkę. Początkowy health p95 116,61 ms przekroczył NF-01; hot GET health używa teraz raz zwalidowanego envelope i wewnętrznych typowanych danych, bez wielokrotnej walidacji całego schematu raportu. Inputy, durable reports i klient nadal używają pełnej walidacji Core. Nie zmieniono DTO ani uprawnień. Pierwszy pełny pomiar idle wykazał 3,6966% CPU przez polling pustej kolejki co 20 ms; pętla IDLE używa teraz 200 ms, aktywne wywołania nadal 20 ms. Obserwacja aktywnego wywołania korzysta z wewnętrznej projekcji report/hash/event z tego samego metadata readera, bez ponownej walidacji zamrożonego requestu w każdej iteracji. Admission, dispatch i zapis stanu nadal wykonują pełną walidację Core; hash, operation ID, schema/state/outcome/method i powiązanie journal są sprawdzane także w projekcji. Pełne 300 s pomiaru jest powtarzane, bez obniżania progu CPU.

NF-03 ma dwa jawne profile. Lokalny NTFS: 100 zapisów i replace 1 MiB co 100 ms podczas fake Hang, z kontrolą SHA. Bieżący repo M:/exFAT: 100 niezależnych prób replace 1 MiB bez Host i 100 podczas zawieszenia; odmowa musi pozostawić oryginał. Wynik negatywnego testu izolacji nie oznacza kwalifikacji NF-03 PASS. Nie stosujemy delete+copy, retry ani usuwania ochrony jako pozoru safe replace. Dokładny błąd i wpływ na bramkę są w raporcie.

NF-07, pełny NF-08 scale/worker alert, NF-09/10 retention, rzeczywisty planer NF-13, live soak oraz testy sprzętowe nie są pozornie zaliczone. NF-14 dotyczy obecnego default deny i braku fizycznego wykonawcy; przyszły adapter wymaga własnej kwalifikacji.

## Pozostawiony katalog B-02C

Zweryfikowano dokładnie dev/storage-tests/a71bad688cf24e0eb6e04f85d6474ab9: marker TIA_BRIDGE2_STORAGE_V1, zgodne state_path i synthetic project 111…, własny niepoprawny Fake DLL, brak reparse, brak aktywnego procesu. Siedem plików można otworzyć wyłącznie do odczytu z FileShare.None. Nie ma obecnie blokady uchwytu; wcześniejsze blocked by policy było odmową automatycznej kontroli narzędzia, nie błędem SQLite/NTFS. Katalog pozostawiono bez obchodzenia tej polityki. Nowa regresja potwierdza cleanup i zwolnienie Process również po oczekiwanym nieudanym starcie; Stop używa finally do zwolnienia własnego procesu także po nieudanej asercji.

## NF-11 i formalna bramka — rozbieżność do decyzji

Dokument [07, Wydajność, stabilność i dostarczanie dowodów](07_TEST_STRATEGY.md#wydajność-stabilność-i-dostarczanie-dowodów) mówi „1000 operacji syntetycznych i minimum 8 h”. [02, NF-11](02_REQUIREMENTS.md#mierzalne-wymagania-niefunkcjonalne) opisuje sesję 8 h/1000. Jednocześnie [09, R-14](09_RISKS_AND_DECISIONS.md#rejestr-ryzyk) przypisuje zamknięcie CPU/RAM/handles creep do B-10B, a [08, B-02D](08_IMPLEMENTATION_PLAN.md#etap-2--silnik-operacji-procesy-i-recovery-mvp) wymienia RT-03/05/06/15/16 i zero podwójnego efektu. AGENTS.md i Instrukcja_ChatGPT.md wymagają pozytywnych obowiązkowych testów z 07/08 przed X-01, ale nie definiują tu wyjątku NF-11. Nie ma jednoznacznego zdania, że minimum 8 h jest albo nie jest wejściem X-01. To rozbieżność zakresu kwalifikacji, nie zgoda na jej pominięcie.

Jawna decyzja użytkownika w tym zadaniu: nie wykonywać teraz 8 h; NF-11 NOT_RUN, pełna stabilność w B-10B/R-14, dalsze testy B-02D bez przestoju. Nie zmieniono normatywnych kryteriów samodzielnie. Formalne X-01 READINESS pozostaje NOT_READY do rozstrzygnięcia bramki; techniczną gotowość ograniczonego, osobno zlecanego read-only na NTFS raportujemy oddzielnie od produkcyjnej stabilności. Ewentualny FAIL NF-03 na M: również pozostaje widoczny.

**Minimalna propozycja do zatwierdzenia, niezastosowana:** dopisać w bramce etapu 2 i sekcji testów 07, że przed wyłącznie ograniczonym X-01 read-only obowiązują pozytywne RT-03/05/06/15/16, restart/hang/deadline/cancel/late/duplicate/loss/UNKNOWN/quarantine, kwalifikowane NF-01/02/03/04/05/06/12/14/15 w jawnym profilu lokalnego NTFS, rzeczywisty 5 min idle i krótki ≥1000-operations run. Pełny NF-11 minimum 8 h oraz skala/retention/production qualification pozostają obowiązkowe w B-10B, przed gotowością produkcyjną. M:/exFAT replace nie jest automatycznie dopuszczone tym wyjątkiem. Zmiana wymaga jawnej decyzji i oceny obu instrukcji/ADR; obecny task nie wprowadza tego wyjątku.

Bezpieczna alternatywa: zachować obecną konserwatywną bramkę i poczekać na B-10B/8 h, bez jakiegokolwiek X-01. Rekomendacja: doprecyzowanie pierwszego wariantu dla osobnego read-only eksperymentu na NTFS; nie rozluźniać zasad UNKNOWN, ownership, auth, backup lub mutacji. Po dostawie B-02D praca kończy się bez X-01.
