# 02 — Wymagania TIA_Bridge 2

Data projektu: 2026-10-24. Wszystkie wymagania w tym dokumencie są **docelowe**. Dowody obecnych możliwości są w [06](06_TIA_INTEGRATION.md) i [10](10_EVIDENCE_AND_QUALITY.md). `MVP` oznacza pierwszy użyteczny przepływ PLC, `REQUIRED` — wymagane do kompletnej wersji opisanej przez użytkownika, `OPTIONAL` — rozszerzenie. REQUIRED nie oznacza dostępnego dziś API Siemens.

## Wymagania funkcjonalne i sześć etapów workflow

| ID | Etap / klasa | Wymagane zachowanie | Weryfikowalne kryterium |
|---|---|---|---|
| FR-01 | 1 / MVP | Inspect środowiska, dokładnego produktu, DLL, projektu i urządzenia | brak attach do niejednoznacznego PID; raport oddziela metadane offline od izolacji fizycznej |
| FR-02 | 1 / MVP | Eksport PLC z hierarchią i registry capability | każdy odkryty obiekt ma pliki albo powód UNSUPPORTED/ERROR; brak cichego pominięcia |
| FR-03 | 1 / REQUIRED | DB, UDT, FC, FB, OB, tag tables, inne rodziny wyłącznie po kwalifikacji | macierz per kind/language/CPU/version/operation; test round-trip przed włączeniem importu |
| FR-04 | 1 / REQUIRED | Strukturalne HMI: ekrany, elementy, zdarzenia, funkcje, tagi, powiązania | read-only projekcja 1:1 identyfikowalnych obiektów; write tylko dla potwierdzonych pól i metod |
| FR-05 | 1 / MVP | Stabilna tożsamość oddzielona od ścieżki | deterministyczne escaping, kolizje Windows/Unicode, rename bez automatycznego delete/create |
| FR-06 | 2 / MVP | CODEX edytuje i tworzy pliki podczas operacji | zmiana W podczas importu S pozostaje; brak blokady pliku roboczego przez wywołanie TIA |
| FR-07 | 2 / MVP | Lokalna walidacja bez TIA | składnia kontraktów, nazwy, hashe, containment, limity XML/JSON; nie udaje kompilatora Siemens |
| FR-08 | 2 / REQUIRED | Usunięcia i refaktoryzacja | tombstone i analiza referencji; zmiana ścieżki nie usuwa obiektu; oddzielna zgoda na delete |
| FR-09 | 3 / MVP | Przyrostowy plan CODEX→TIA na SHA-256 i trzech stanach | znany baseline B, workspace W, świeże P; zmienione tylko kwalifikowane jednostki i zależności |
| FR-10 | 3 / MVP | Snapshot wejść i trwała kolejka | zatwierdzony hash planu wiąże pliki, wersję adaptera, projekt, uprawnienie i bazę; wykonawca czyta S, nie W |
| FR-11 | 3 / MVP | Konflikty bez utraty wersji | obie wersje i B zachowane; brak overwrite nieznanej zmiany TIA; nowy plan po resolution |
| FR-12 | 3 / MVP | Zależności i grupy importu | graf typów/bloków, cykle i nierozwiązane referencje blokują niekwalifikowaną jednostkę; import/compile/save/readback jako saga |
| FR-13 | 3 / MVP | Bezpieczne ponowienie żądania | ten sam idempotency key + canonical request zwraca tę samą operację; UNKNOWN nigdy nie uruchamia ponownie mutacji |
| FR-14 | 4 / MVP | Niezależna weryfikacja po Apply | świeże proxy/eksport, treść + konfiguracja + struktura + inventory; odmowa SYNCED przy niepokrytym wymaganym wymiarze |
| FR-15 | 4 / MVP | Statusy SYNCED, MODIFIED, CONFLICT, UNSUPPORTED, ERROR, UNKNOWN | formalna klasyfikacja z [04](04_SYNCHRONIZATION.md); nie myli się z lifecycle operacji |
| FR-16 | 4 / REQUIRED | Raport transformacji TIA | raw hash i porównanie adapterowe osobno; jawna lista dopuszczonych różnic kosmetycznych |
| FR-17 | 5 / MVP | Compile PLC ze zagnieżdżonymi diagnostykami | ErrorCount, WarningCount, scope, build fingerprint, komunikaty i wersja CPU; błąd nie daje Engineering PASS |
| FR-18 | 5 / REQUIRED | Compile HMI | dobór usługi ICompilable dla rodziny urządzenia, komunikaty z ścieżkami; Compile nie potwierdza natywnego API ani Runtime |
| FR-19 | 5 / REQUIRED | Symulatory i Runtime jako osobne adaptery | INSTALLED, STARTING, RUNNING, PROGRAM_LOADED i READY osobno; kontrola własności i zamknięcia wyłącznie własnej sesji |
| FR-20 | 5 / REQUIRED | Przygotowanie i load wyłącznie symulacji | oddzielne uprawnienie `simulation.prepare`, tożsamość instancji i izolacja od sieci maszyny; brak automatycznego fizycznego downloadu |
| FR-21 | 6 / REQUIRED | Testy HMI Playwright | udokumentowany URL/certyfikat/rola, wykryte selektory, nawigacja, wejścia, alarmy, screenshot/trace i raport; brak API/DOM → jawny adapter alternatywny |
| FR-22 | 6 / REQUIRED | Testy PLC OPC UA | jawny TEST/SECURE, mapowanie namespace URI, odczyt/subskrypcje/warunki/timeout; zapisy tylko allowlist testowa z lease symulacji |
| FR-23 | przekrojowe / MVP | Asynchroniczne API + CLI | zlecenie, status, wynik, zdarzenia, anulowanie; brak blokującego domyślnego wait |
| FR-24 | przekrojowe / MVP | Diagnostyka i recovery | osobny heartbeat hosta/workera/postępu; watchdog, kwarantanna, bounded retry odczytów, circuit breaker |
| FR-25 | przekrojowe / MVP | Backup i historia | jeden zweryfikowany backup przed pierwszą mutacją sesji, kontrola storage/temp, trwały intent i Save receipt, zachowanie FAILED/UNKNOWN |
| FR-26 | przekrojowe / OPTIONAL | MCP | fasada tego samego API, negocjacja capabilities; brak uzależnienia trwałych operacji od połączenia MCP |
| FR-27 | przekrojowe / REQUIRED | Migracja i wersje | schematy major/minor, eksport starych dowodów read-only, świeży baseline; brak automatycznej migracji starej nieznanej sesji |

## Model odbiorców i uprawnień

Domyślny profil R pozwala na inspect/export/compare. E pozwala na ograniczone zmiany Engineering zatwierdzone planem lub uprawnieniem sesji; S pozwala na własne, niezależnie zweryfikowane symulatory i testowe zmienne. Uprawnienie wiąże SID operatora, `project_key`, session epoch, zakres, operacje, plan/politykę, termin ważności i nonce. Token transportowy API nie jest uprawnieniem TIA. Obiekt nazwy `SIM` nie jest dowodem izolacji.

Automatyzacja ma być autonomiczna w raz zatwierdzonym zakresie, bez potwierdzania każdego helpera i kroku. Zmiana projektu, nieznany konflikt, destructive action lub rozszerzenie zakresu wymagają osobnego rozstrzygnięcia. Fizyczny download i machine actuation są poza MVP, domyślnie zabronione. Ten projekt dokumentacyjny nie uruchamia sesji E/S.

## Mierzalne wymagania niefunkcjonalne

Wartości są **proponowanymi celami**, nie wynikami pomiarów TIA_Bridge 2. Pomiar na docelowym komputerze przy 1 i 8 klientach, 1 000 obiektach/100 MiB, a test skali 10 000/1 GiB; testy live na małym zatwierdzonym projekcie. Raport podaje CPU/RAM/dyski/AV/wersje, p50/p95/p99 i liczbę próbek. Czas vendor call raportować oddzielnie.

| ID | Metryka i cel początkowy | Metoda i zachowanie po przekroczeniu |
|---|---|---|
| NF-01 | STRICT: health/status p95 ≤100 ms, p99 ≤250 ms; SHARED_WORKSTATION candidate: p95 ≤250 ms, p99 ≤500 ms; 1 000 odczytów/profile | odczyt z hosta/DB bez wywołania TIA; oddzielne progi KPI, ten sam kontrakt dostępności |
| NF-02 | STRICT: durable submit p95 ≤250 ms, p99 ≤500 ms; SHARED_WORKSTATION candidate: p95 ≤500 ms, p99 ≤1000 ms | commit intent przed 202; capture dużego workspace jest osobną fazą; progi nie zmieniają wymogu trwałości |
| NF-03 | STRICT: edit/replace pojedynczego 1 MiB pliku p95 ≤100 ms; SHARED_WORKSTATION candidate: p95 ≤500 ms podczas wiszącego workera | brak uchwytów do W przez czas vendor call; editor save/replace pozostaje możliwe; atomowość i integralność nie są KPI |
| NF-04 | informacja o braku heartbeat ≤10 s (interwał 2 s, próg 3 + budżet 4 s) | LOST/SUSPECT, nie twierdzenie o utracie TIA; TIA responsiveness weryfikowane osobnym, szeregowanym probe |
| NF-05 | startup/history p95: STRICT ≤5 s, SHARED_WORKSTATION candidate ≤10 s; dispatch read-only po recovery ≤30 s | nie ma SLO automatycznego odblokowania niepewnego zapisu; 30 s pozostaje niezmienionym warunkiem recovery |
| NF-06 | kolejka czeka na własność ≤300 s domyślnie; zero oczekiwania nieskończonego | WAIT_TIMEOUT bez mutacji; użytkownik może jawnie wybrać mniejszy limit |
| NF-07 | soft budget ExclusiveAccess: import grupy ≤120 s, compile ≤300 s, Archive ≤600 s | admission estimate i małe grupy; przekroczenie = DEGRADED/timeout. Nie obiecujemy twardego zwolnienia blokady w wiszącym Siemens; brak następnej mutacji |
| NF-08 | Host idle CPU <1% STRICT / <2% SHARED_WORKSTATION candidate średnio/5 min; RAM ≤200 MiB, worker idle ≤250 MiB; peak host ≤512 MiB przy 1 000 obiektów | limit CPU dotyczy procesu Host przy pustej kolejce, nie bezczynności całego komputera; pamięć bez zmian; worker 1 GiB soft alert, bez zabijania mutacji przez memory cap |
| NF-09 | JSON log rotation 20 MiB ×10 na komponent; logi 14 dni, trace 7 dni lub 2 GiB | kasowanie tylko terminalnych, niechronionych artefaktów; FAILED/UNKNOWN i aktywne backupy przypięte do resolution |
| NF-10 | audit i receipty ≥180 dni, limity workspace configurable | quota alarm i blokada nowych mutacji przed wyczerpaniem; nie kasować dowodów automatycznie |
| NF-11 | sesja 8 h / 1 000 operacji syntetycznych, 100 operacji read-only live | zero zaginionych operation_id, zero niezarządzanych dzieci; wzrost RAM po warmup ≤50 MiB, uchwytów ≤5% |
| NF-12 | 100% zaakceptowanych intentów po kontrolowanym crash/restart; zero fałszywych SYNCED | fault injection na każdym journal boundary; rzeczywista utrata zasilania wymaga osobnego testu sprzętowego/storage |
| NF-13 | identyczne wejścia → identyczny plan/hash; 100 powtórzeń offline | zegar/UUID poza częścią canonical plan; stabilne sortowanie; retry nie powiela efektów |
| NF-14 | 100% prób fizycznego downloadu i write poza allowlist zablokowane przed adapterem | negatywne testy policy i recipients; brak credentiali w bundle testowym |
| NF-15 | manifest/plan ≤10 MiB, żądanie API ≤1 MiB, event ≤64 KiB, maks. kolejka 1 000 | 413/429 przed przyjęciem; źródła przez artifact IDs, nie body HTTP; przerwanie parserów nad limitami |

### Etapy kwalifikacji i profile filesystemu

Decyzja [D-11](09_RISKS_AND_DECISIONS.md#decyzje) z 2026-10-26 rozdziela ograniczony eksperyment X-01 read-only od pełnej kwalifikacji stabilności produkcyjnej. Wartości i semantyka NF pozostają bez zmian. Przed X-01 obowiązują NF-01/02/03/04/05/06/12/14/15 w zaimplementowanym zakresie Fake/Host, w jawnym profilu lokalnego NTFS, rzeczywisty pomiar idle ≥5 min oraz krótki przebieg ≥1000 operacji, obok wymaganych RT i niezmienników [bramki 07](07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only). Syntetyczny PASS nie kwalifikuje odpowiednich wywołań Siemens, odbiorców ani przyszłych adapterów.

Progi NF-01/02/03/05 i CPU NF-08 są wersjonowane per profil w [D-11-P](20_PERFORMANCE_QUALIFICATION_PROFILES.md). `STRICT` zachowuje powyższe wartości i jest domyślny. `SHARED_WORKSTATION` jest kandydatem dla przyszłych kwalifikacji offline na normalnie używanym stanowisku; nie zmienia istniejącej ograniczonej bramki D-11/X-01 ani E2-B1 bez odrębnego zatwierdzenia.

NF-11 nadal wymaga minimum 8 h, 1000 operacji syntetycznych i osobnego read-only live soak 100 operacji. Właścicielem pełnej kwalifikacji jest B-10B/R-14. NF-11: NOT_RUN nie blokuje ograniczonego X-01 read-only, ale nie staje się PASS i pozostaje wymaganiem późniejszej kwalifikacji stabilności produkcyjnej. Krótki przebieg i pomiar idle nie zaliczają pełnego NF-08 ani NF-11.

NF-03 raportuje odrębnie NTFS i exFAT. SQLite, journal, outbox, artefakty oraz operacje plikowe wymagające kwalifikowanego zapisu/replace korzystają z lokalnego NTFS. Historyczny NF-03 exFAT: FAIL pozostaje ograniczeniem badanego profilu; NTFS PASS nie kwalifikuje exFAT. Nie dopuszcza się delete+copy jako obejścia replace ani automatycznego przenoszenia repozytorium. Typ filesystemu każdej nowej lokalizacji wymaga weryfikacji przed użyciem w profilu NTFS.

## Kryterium zakończenia MVP

Na syntetycznym, czystym projekcie V19: zlecenie eksportu, edycja jednej FC SCL, trzystronny plan, jeden backup sesji, Apply z kwalifikowanym importem, kompilacja PLC, Save i niezależny odczyt dają SYNCED dla dokładnej rewizji. Powtórzenie żądania nie importuje ponownie. W czasie zablokowanego fake adaptera CODEX edytuje drugi plik i otrzymuje status w NF-01. Awaria po pierwszym imporcie pozostawia RECONCILIATION_REQUIRED, a nie sukces lub samoczynny retry. Runtime może nadal być NOT_RUN.
