# NF-03/NF-02 — diagnostyka X:/NTFS i ponowna ocena D-11

Data: 2026-10-26. Zakres: wyłącznie offline, lokalny filesystem i własne procesy Host/Fake. **Trzy celowane serie przeszły NF-03 i NF-02; nie odtworzono wcześniejszego przekroczenia p95. Zarejestrowano jednak długie ogony opóźnień bez workera. Przyczyna dawnych FAIL pozostaje nierozstrzygnięta, a nowa ograniczona bramka D-11 jest BLOCKED.** Nie zmieniono kodu produkcyjnego, testów, progów, reportera ani konfiguracji Windows. Commit obejmuje tylko ten raport i `Zmiany.md`.

## Wejście i niezmienność dowodów

Warunek zakończenia Zadania A sprawdzono przed eksperymentem: commit `ea3d4ff061340d5569290bbe247cbfc050b64a7b` zawiera zakończone capabilities PROJECT/Host/Unicode i zapis wyników w [raporcie B-03A](../../docs/B03A_DISCOVERY_QUALIFICATION.md#walidacja-implementacji-offline) oraz `Zmiany.md`. Po `git fetch origin main` HEAD i `origin/main` wskazywały ten commit, worktree był czysty. A jest zakończone z wynikiem częściowym: test execution 801/801 .NET i 33/33 Python PASS, cały runner FAIL; nie jest to pozytywna kwalifikacja B-03A.

Przeczytano AGENTS, instrukcję ChatGPT, wymagane 07/08/16, historyczne raporty [D-11](X01_LIMITED_GATE.md), [X:/NTFS](X01_NTFS_REQUALIFICATION_2026-10-26.md), B-03A, wymagania 02, granice architektury/synchronizacji/interfejsu/integracji, właściwe ADR oraz kod NF, fixture, storage i runnerów. Historyczne pliki pozostają niezmienione. Przed pomiarami zachowano kopie zastanych `.tia-bridge/b02c/performance.json`, `.tia-bridge/resilience/run.json` i `.tia-bridge/offline/run.json` w ignorowanym katalogu eksperymentu.

Oddzielne źródła historyczne:

| Dowód | Wynik; nie jest wynikiem obecnego eksperymentu |
|---|---|
| X:/NTFS, run `f116dd23c7aa480995b5a69077f4db35` | NF-03 kontrola bez workera p95 4,5592 ms; kontrola przy Hang 6,5635 ms. NF-08 idle CPU 1,0288%: FAIL przy wymaganiu **<1%**. D-11 limited: PASS |
| Zadanie A, run `2948c6a3246c45888a60faeda51a303b` | NF-03 kontrola X: bez workera: N100, p50/p95/p99/max 3,0333/115,8212/200,9893/256,3424 ms, refused=0: FAIL; przy Hang 2,9254/6,6432/17,7749/56,3370 ms: PASS |
| Ten sam runner A, osadzony pomiar NF-02 | N64/8 klientów, p50/p95/p99/max 136,3178/156,9138/204,7255/204,7255 ms: PASS, `observed_at=2026-10-26T12:44:45.6912503Z` |
| Wcześniejszy pełny standalone .NET z dokumentacji A | NF-02 p95 298,0946 ms: FAIL; brak odnalezionego surowego raportu tej próby i telemetrycznego bindingu |
| Dawny exFAT | NF-03 FAIL, zachowany; NTFS nie kwalifikuje exFAT |

Nie łączono próbek, nie wybierano najlepszego przebiegu. Zastany współdzielony `performance.json` miał już późniejszą obserwację `2026-10-26T13:11:34.6393913Z`, p95 submit 198,1176 ms. To **inny pomiar**, nie zamiennik 156,9138 ms. Do porównania użyto osadzonej kopii w `resilience/run.json` A; kolejne nowe pomiary od razu kopiowano do katalogów ich run ID.

## Metryki i granice stopera

Kod: [ResiliencePerformanceTests](../integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs), [ApiPerformanceTests](../integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs), [ApiFixture](../integration/TiaBridge.Host.Tests/ApiFixture.cs), [StoreFixture](../integration/TiaBridge.Host.Tests/StoreFixture.cs). Wszystkie percentyle to **nearest rank**: po sortowaniu indeks `ceil(N*q)-1`, bez interpolacji i bez odrzucania próbek. Dla N100 p95 to 95. próbka, p99 to 99.; dla N64 p95 to 61., p99 to maksimum. Mała liczba obserwacji ogona NF-02 zwiększa wrażliwość percentyla na kilka wolnych żądań; nie zmieniono N.

| Profil | N, lokalizacja i dokładna metryka | Kryterium |
|---|---|---|
| R, repository control | N100 bez Host, potem N100 przy Fake Hang. Nowe pary plików w `.tia-bridge/resilience/workspaces/<UUID>` na X:. Pierwotny target 1 MiB tworzony przed stoperem. Stoper: `WriteAllBytes(source)` → `File.Move(source,target,overwrite:true)` → `ReadAllBytes(target)` → `Assert.Equal` | obie fazy bez odmów i każda p95 ≤100 ms; xUnit PASS oznacza poprawne wykonanie kontroli, pole `success` decyduje o SLO |
| N, independent NTFS | N100 przy Fake Hang, **C:/NTFS**, w `%LOCALAPPDATA%/TIA_Bridge2/dev/storage-tests/<UUID>/workspaces/<UUID>`. Ten sam target, dane zmieniane co próbkę. Stoper: `WriteAllBytes(tmp)` → `File.Move(...overwrite:true)`; SHA odczytu po zatrzymaniu stopera | p95 ≤100 ms i zgodność SHA |
| E, editor in-place | N100 przy Fake Hang na X:, `.tia-bridge/b02c/workspaces/<UUID>`; stoper obejmuje wyłącznie `WriteAllBytes(editable)`; późniejszy odczyt/hash poza stoperem | p95 ≤100 ms; to inny profil niż R/replace |
| NF-02 | N64, 8 klientów ×8 kolejnych POST; podczas Hang po status N1000/1 i N1000/8. Request z nowym key przygotowany przed stoperem. Od `SendAsync` do odczytu pełnego body 202; walidacja acceptance po pomiarze | p95 ≤250 ms **oraz** p99 ≤500 ms |

Każda para source/target leży w tym samym katalogu i na tym samym woluminie. Nie użyto systemowego `%TEMP%` do tych zapisów. NF-03 R/E kwalifikuje X:, a N mierzy osobny workspace C:; samo słowo „NTFS” nie upoważnia do zamiany tych wyników. State SQLite/journal/outbox/artifacts Host/Fake jest na C:/NTFS poza repo. `TEMP/TMP` wskazywały `C:\Users\kolejnik\AppData\Local\Temp`; narzędzia .NET mogą używać własnych temp, ale nie są one targetem NF-03.

W NF-03 nie ma jawnego `Flush(true)`, fsync ani `FileOptions.WriteThrough`; pomiar zawiera czas zamknięcia zapisu i zastąpienia nazwy, a w R również odczyt/porównanie. **To nie test trwałości po utracie zasilania ani pomiar samego fizycznego dysku.** Cache OS/nośnika nie był czyszczony ani kontrolowany. R zawsze mierzy bez workera najpierw, potem z workerem: kolejność, inicjalizacja/JIT i ogrzanie cache są czynnikami zakłócającymi, więc szybsza druga faza nie dowodzi przyspieszającego wpływu workera. R/N/E nie mają osobnego warm-up filesystemu i uwzględniają pierwszą próbkę. NF-02 ma 8 warm-up status GET i wcześniejszy startowy submit, ale nie osobny warm-up serii submit. Po każdej próbie plikowej jest `Delay(100)` poza stoperem; okres wynosi czas pracy + co najmniej 100 ms, nie sztywne 10 Hz.

NF-02 mierzy również walidację/serializację, kolejkę wykonania HTTP i trwały admission. [SQLite](../../src/TiaBridge.Host/Storage/SQLite/SqliteDatabase.cs) serializuje write przez `writerGate`, używa WAL, transakcji immediate i `synchronous=FULL`; nie jest to pomiar samej sieci ani vendor call. Koszt CPU, oczekiwanie na lock i zapis na C: są wiarygodnymi hipotezami zmienności, lecz nie zmierzono ich udziałów w dawnych dwóch próbach.

Ograniczenie asercji E: `Assert.Equal(32, SHA256.HashData(File.ReadAllBytes(editable)).Length)` sprawdza możliwość odczytu i długość digestu, ale **nie zgodność odczytanej treści z payloadem**; SHA-256 daje 32 bajty także dla innej treści. Nie jest to dowód poprawności zapisu bajtów. R porównuje całą tablicę, N porównuje oczekiwany i odczytany SHA. Słaba asercja E nie wyjaśnia przekroczeń latencji; w tym eksperymencie zachowano niezmieniony test i nie rozszerzano na jego podstawie kwalifikacji integralności. Poprawa oracle treści jest jednoznacznie uzasadnionym kandydatem osobnej, małej korekty testu, bez zmiany stopera, N i progów.

## Środowisko i plan eksperymentu

Windows 11 Pro, build 10.0.22000; .NET 10.0.13 x64, SDK wybrany przez `global.json` 10.0.400/latestPatch. Xeon E3-1240 v5, 4 rdzenie/8 logicznych, około 31,93 GiB RAM. X: Fixed/NTFS, disk 1, Micron 1100 SATA 256GB, SATA, Healthy/Online, writable, rozmiar 255303086080 B, wolne 162857512960 B przy pierwszym odczycie. C: Fixed/NTFS, Healthy/OK, rozmiar 499352883200 B, wolne 10135343104 B. Nie zmieniano lokalizacji state ani konfiguracji dysków.

SecurityCenter2 raportował ESET Security oraz Windows Defender; `ekrn.exe` wykonywał I/O. `Get-MpComputerStatus` raportował wyłączone komponenty Defendera, co **nie dowodzi braku aktywnej ochrony ESET**. `fltmc filters` odmówił dostępu (0x80070005); nie eskalowano praw. Nie uzyskano śladu per-file operacji AV, więc jego wpływ pozostaje UNKNOWN. System log, ograniczony do storage IDs 7/11/51/55/129/153/157 z ostatniej doby, zawierał jeden disk ID11 z 2026-10-25 22:47:58 +01:00 na `Harddisk6/DR6`, **nie disk 1/X:**. Brak odpowiadającego zdarzenia dla X: i Healthy nie są pełnym badaniem sprzętu/SMART. Odczytane chwilowe counters dysków miały queue=0 i latencje=0; ich rozdzielczość nie wyjaśnia opóźnień API plikowego.

Ustalono z góry **3 serie**, każda kolejno R → N → API (NF-01/02/E), nowy własny Host/Fake i run ID na test. Bez powtórzeń „aż przejdzie”, równoległego builda, benchmarku obciążającego i soak. Każdy niezmieniony test uruchomiono `dotnet test tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~<method> --logger trx --results-directory <własny-run-dir>`, z `TIA_BRIDGE_RESILIENCE_RUN_ID=<UUID>`. Nazwy metod i pełne argv są w lokalnym manifeście. Ograniczenie diagnostycznego procesu testowego: 180 s na test; żaden timeout nie wystąpił. Użyto istniejących binariów Release po Zadaniu A, bez przebudowy w trakcie serii. Wersję identyfikują HEAD, hashe źródeł i DLL poniżej.

Okno trzech serii: **2026-10-26 13:40:30–13:44:21 UTC** (14:40:30–14:44:21 Europe/Warsaw). Daty i okna tabel poniżej są UTC, z dokładnymi timestampami w manifeście.

| Seria / test | Run ID | Start–koniec UTC | CPU śr./max % | Min. available RAM MiB | Obserwacje zasobów |
|---|---|---|---|---|---|
| 1 / repo | `a7ec1f30b6fa4b5bafaf326e68c5997d` | 13:40:30–13:41:02 | 52.67/64.4 | 7576 | 4 |
| 1 / ntfs | `60204e9a60ae4b4bb2068cdf500b32db` | 13:41:02–13:41:22 | 55.20/61.4 | 7691 | 3 |
| 1 / api | `6dd8e5fe91d7421e82fe68567f6e3bcc` | 13:41:22–13:41:49 | 60.35/69.8 | 7638 | 4 |
| 2 / repo | `e38821728a354fb9b3bf056bf4aa41f1` | 13:41:49–13:42:17 | 57.92/64.6 | 7380 | 4 |
| 2 / ntfs | `59068ea51b0e4941a57cc7c935adece8` | 13:42:17–13:42:37 | 52.47/60.3 | 7485 | 3 |
| 2 / api | `6e1eb2ddc4034f978737ea6420a7926f` | 13:42:37–13:43:04 | 61.38/68.4 | 7468 | 4 |
| 3 / repo | `c6d3ca84e6004169ba09eac923d7cbe0` | 13:43:04–13:43:35 | 55.20/70.0 | 7443 | 5 |
| 3 / ntfs | `e7e2aa1bb1c94e22ba77f839bc105a2c` | 13:43:35–13:43:54 | 50.47/63.2 | 7443 | 3 |
| 3 / api | `3dfe9db8677d4f529ed3cf694794e226` | 13:43:54–13:44:21 | 63.15/71.4 | 7354 | 4 |

Monitor Python 3 + już dostępny psutil rejestrował CPU, available RAM, liczniki fizycznych dysków oraz nazwy/PID i I/O/CPU procesów bez odczytu command line lub danych projektów. Rzeczywisty odstęp pełnych obserwacji wynosił **5,77–7,49 s**, mimo zadanej pauzy 1 s: enumeracja procesów kosztuje czas. Średnie CPU w tabeli są arytmetycznymi średnimi tych rzadkich obserwacji, nie pełną analizą przebiegu; nie wolno przypisać nimi pojedynczego opóźnienia 100–700 ms. Monitoring sam wprowadza narzut i nie był obecny w dawnej kwalifikacji. Process I/O może obejmować cache i inne transfery, nie dowodzi dostępu do konkretnego pliku/woluminu.

Wykryto równoległe I/O `Code.exe`, przeglądarek/WebView, `codex.exe`, `svchost.exe`, `ekrn.exe` oraz istniejących `Siemens.Automation.Portal.exe` i `Siemens.Automation.ObjectFrame.FileStorage.Server.exe`. To wyłącznie obserwacja liczników OS; nie wykonano Attach ani odczytu projektów i nie zatrzymywano procesów. Próby nie odbywały się na dedykowanym idle hoście. CPU systemowe z tabeli nie jest CPU samego Host w NF-08. Fizyczne liczniki C:/X: są w telemetry JSON; zerowe lub małe transfery podczas dużej liczby logicznych zapisów są zgodne z wpływem cache i nie są dowodem szybkiego trwałego flush.

## Wyniki wszystkich niezmienionych testów

Każda dystrybucja jest oddzielna; wartości w ms. N100 w każdym wierszu R/N/E, N64 dla NF-02 i N1000 dla każdego status profile. PASS poniżej dotyczy wskazanego SLO, nie całej D-11.

| Seria | Profil | p50 | p95 | p99 | max | SLO |
|---|---|---|---|---|---|---|
| 1 | R X: without_worker | 3.2287 | 4.9672 | 5.5603 | 16.5883 | PASS |
| 1 | R X: with_worker | 3.3340 | 16.2142 | 32.3736 | 40.3583 | PASS |
| 1 | N C: save/replace Hang | 2.3045 | 4.2739 | 9.4297 | 11.8080 | PASS |
| 1 | NF-02 submit/8 | 158.8210 | 211.9023 | 250.7162 | 250.7162 | PASS |
| 1 | E X: in-place Hang | 1.0889 | 1.9390 | 6.9734 | 23.8678 | PASS |
| 1 | NF-01 status/1 | 5.4387 | 7.8957 | 15.7809 | 65.7577 | PASS |
| 1 | NF-01 status/8 | 9.7220 | 17.4180 | 22.7935 | 32.2510 | PASS |
| 2 | R X: without_worker | 3.3897 | 7.9545 | 19.6222 | 94.9801 | PASS |
| 2 | R X: with_worker | 3.3203 | 9.3706 | 19.0182 | 25.6168 | PASS |
| 2 | N C: save/replace Hang | 2.3110 | 6.3076 | 12.5127 | 32.3826 | PASS |
| 2 | NF-02 submit/8 | 138.8921 | 205.8176 | 262.2369 | 262.2369 | PASS |
| 2 | E X: in-place Hang | 1.1030 | 2.3358 | 20.9900 | 21.8159 | PASS |
| 2 | NF-01 status/1 | 5.2801 | 7.0959 | 9.2785 | 16.1287 | PASS |
| 2 | NF-01 status/8 | 10.1844 | 17.8459 | 24.9113 | 32.8669 | PASS |
| 3 | R X: without_worker | 3.5240 | 30.3084 | 65.8036 | 252.4374 | PASS |
| 3 | R X: with_worker | 3.0664 | 5.4440 | 26.4773 | 26.5758 | PASS |
| 3 | N C: save/replace Hang | 2.2997 | 3.6744 | 15.3002 | 16.2437 | PASS |
| 3 | NF-02 submit/8 | 152.9506 | 232.9689 | 276.4433 | 276.4433 | PASS |
| 3 | E X: in-place Hang | 1.0992 | 2.6121 | 15.4000 | 27.3382 | PASS |
| 3 | NF-01 status/1 | 5.4803 | 7.9199 | 13.6744 | 41.2065 | PASS |
| 3 | NF-01 status/8 | 9.6522 | 16.3749 | 26.1906 | 48.5796 | PASS |

Wszystkie 9 xUnit testów: PASS, bez SKIP, błędów wykonania, odmów replace lub timeoutu. Kontrole treści R/N przeszły; E ma ograniczenie opisane powyżej. W R wszystkie 600 prób replace zakończyły się powodzeniem. Rozrzut p95 bez workera wyniósł 4,9672 → 7,9545 → 30,3084 ms przy p50 około 3,2–3,5 ms; największa próba serii 3 wyniosła 252,4374 ms. Kryterium jest na p95, więc to maksimum nie daje samo w sobie FAIL. Testy zapisują agregaty, nie sekwencję surowych próbek; dla tych przebiegów nie da się odzyskać indeksów ani liczby przekroczeń 100 ms. Nie dorabiano brakujących próbek z percentyli.

## Pomiar pomocniczy etapów filesystemu

Po zakończeniu serii wykonano **jeden diagnostyczny run**, ID `126b9c1c0f5c479e9e3a70a36efd6c6c`, 13:44:44–13:45:14 UTC. Oddzielny, lokalny program .NET 10 bez Host/Fake odwzorował sekwencję R, z tym samym xUnit `Assert.Equal`, 100 nowymi parami plików 1 MiB na X:, następnie 100 na C:. Zachowano 100 ms pauzy i wszystkie próbki; dodatkowe timestampy rozdzielają etapy. Jest to instrumentowany eksperyment pomocniczy, **nie zmieniony test normatywny ani zamiennik NF-03 przy Hang**. Źródło, projekt i surowe próbki zachowano w ignorowanym katalogu `components`; build offline bez instalacji pakietów: 0 warnings/errors. Każde źródło i cel na tym samym NTFS, tylko własne UUID pod `.tia-bridge/diagnostics/.../components/workspaces` lub istniejącym dev/storage-tests; usunięto wyłącznie własne utworzone pliki i puste katalogi, bez recursive delete.

| Wolumin / etap | p50 | p95 | p99 | max (ms) |
|---|---|---|---|---|
| X: write_ms | 0.8103 | 1.5291 | 11.0268 | 32.2941 |
| X: move_ms | 1.1636 | 12.4382 | 54.9127 | 592.3570 |
| X: read_ms | 1.0259 | 3.0854 | 13.4564 | 94.9598 |
| X: compare_ms | 0.1340 | 0.2577 | 0.3754 | 0.3871 |
| X: total_ms | 3.2916 | 16.5630 | 59.2445 | 719.8091 |
| C: write_ms | 0.9989 | 1.5156 | 1.6781 | 26.2902 |
| C: move_ms | 1.4276 | 10.9098 | 20.4986 | 83.2351 |
| C: read_ms | 1.0910 | 2.6359 | 19.7701 | 35.9821 |
| C: compare_ms | 0.1190 | 0.1974 | 0.4909 | 1.9565 |
| C: total_ms | 3.8057 | 23.0200 | 40.4152 | 86.3390 |

X:, próbka index 25, koniec `13:44:48.2760914Z`: total **719,8091 ms**, write **32,2941**, move **592,3570**, read **94,9598**, compare **0,1982**. Pozostałe największe totals X: 59,2445; 24,9567; 23,5142; 18,3270 ms. Długi ogon w tym pomiarze leży głównie w odcinku wywołania replace; to czas ścienny obejmujący również możliwość wywłaszczenia wątku, nie sam czas pracy dysku. Na C: maksimum 86,3390 ms, w tym move 83,2351 ms. Nie sumuje się percentyli etapów, bo nie muszą dotyczyć tych samych próbek. Pomocniczy total p95 spełnił 100 ms na obu woluminach, lecz nie nadaje im nowej kwalifikacji.

Telemetria pomocnicza: tylko 3 pełne obserwacje, 13:44:58–13:45:14 UTC, CPU sample mean/max 67,1/69,0%, minimum available RAM 6663 MiB. **Pierwsza pełna obserwacja była po odstającej próbce na X:** nie ma podstaw do korelacji tego outliera z konkretnym procesem. Nie wykonywano ETW/Procmon ani kontrolnego wyłączania ochrony. Nie było błędów programu (exit 0) i nie wykonywano dalszych powtórzeń.

## NF-02: porównywalność i ocena przyczyny

Źródło `ApiPerformanceTests.cs` nie zmieniało metody od commita `51d859f5b361b614672ccf9abb59a05606c461c5`. Intencja obu dawnych pomiarów według raportu A to ten sam Hang, admission HTTP, N64/8 i próg 250/500 ms. Dla 156,9138 ms potwierdzono rzeczywiste N64, p99 204,7255 ms, NTFS state/workspace, OS/runtime i timestamp w osadzonym raporcie run A oraz zgodność czasu z testem TRX. Dla 298,0946 ms dokumentacja potwierdza FAIL wcześniejszego pełnego zestawu; w dostępnych lokalnych TRX/logach nie odnaleziono jego surowego wyniku. Dokładny hash użytego wtedy binary, liczba rzeczywiście zebranych próbek, p99, timestamp i obciążenie pozostają niepotwierdzone. Nie można udowodnić identycznych warunków obu wykonań na podstawie niezmienionego źródła.

Nie ma dowodu zmiany definicji, scenariusza, progu lub celowej liczby próbek; nie ma też dowodu porównywalnego obciążenia systemu. Nie jest uzasadnione przypisanie spadku 298,0946 → 156,9138 ms poprawce kodu, cache lub AV. Nowe p95 211,9023 / 205,8176 / 232,9689 ms (p99 250,7162 / 262,2369 / 276,4433) potwierdzają trzy aktualne PASS w profilu celowanym, z ograniczonym zapasem do 250 ms, **nie usunięcie niestabilności**. `NF-02 VARIABILITY: UNEXPLAINED`.

## Rozpoznane czynniki i możliwe poprawki

| Kategoria | Wniosek i poziom dowodu |
|---|---|
| Wada funkcjonalna Host/Worker | Nie wykryto jednoznacznego błędu. Długie opóźnienie pojawiło się także w programie bez Host/Fake; to wyklucza konieczność udziału ich blokady w tej konkretnej próbce, nie dowodzi braku innych wad |
| Filesystem/replace | Zmierzono długi odcinek `File.Move` oraz ogon bez workera. Nie rozdzielono filesystemu, filtra, cache i czasu deschedulingu |
| Obciążenie Windows/interferencja | Potwierdzone współdzielone CPU i równoległe I/O. Związek przyczynowy z dawnym NF-03/NF-02 FAIL niepotwierdzony |
| Antywirus | ESET i jego I/O zaobserwowane; brak per-file trace. Udział w latencji UNKNOWN; nie zmieniono ochrony |
| Metoda pomiaru | R, N i E mierzą różne granice i lokalizacje; fixed order, niekontrolowany warm/cache i mały ogon N64 ograniczają porównania. To nie jest jednoznaczny błąd pozwalający usunąć odczyt/asercję z R lub podnieść próg |
| Tożsamość dowodów | `performance.json` jest współdzielony i nadpisywany, bez własnego run ID; reporter sprawdza freshness timestamp. Kopia w raporcie A zachowuje właściwe 156,9138 ms, ale dzisiejszego luźnego pliku nie można uznać za tamten wynik |
| Warunki środowiska | Lokalny Fixed/NTFS potwierdzony; brak kwalifikowanego cold/idle i kontroli obcych obciążeń. C: ma około 10,1 GB wolnego miejsca; nie stwierdzono storage full. Nie przeprowadzono pełnej kwalifikacji sprzętu |

**Nie wykazano błędu testu lub implementacji, który wyjaśniałby przekroczenia latencji; kod pozostawiono bez zmian.** Potencjalne kolejne, osobno ograniczone prace: wzmocnić asercję treści E opisaną powyżej; dodać run-bound, niezmienny output NF-02 i per-sample czasy do harnessu bez zmiany kryteriów; osobno zmierzyć cold/warm i kolejność faz jako eksperyment, zachowując wynik oryginalnego testu; przy ponownym outlierze zebrać ograniczony ślad filesystem/scheduling/AV z uprawnieniami i kontrolą narzutu. Potrzebna jest telemetria o znacznie lepszej rozdzielczości niż obecna enumeracja procesów. Nie proponuje się retry, delete+copy, usuwania najwolniejszych próbek, większego progu, wyłączenia AV lub automatycznego zatrzymania cudzych procesów. Zmiana trwałego admission na słabszy flush nie jest dopuszczalną optymalizacją NF-02.

## D-11: cztery oddzielne oceny

1. **Historyczna kwalifikacja X-01:** D-11 limited PASS pozostaje niezmienione, z historycznym X-01 Engineering 93/93 PASS. Nie jest dowodem nowych getterów B-03A ani nowego profilu.
2. **Nowa kwalifikacja X:/NTFS:** trzy celowane serie NF-03/NF-02 PASS, powtarzalność wyjaśniająca historyczną zmienność INCONCLUSIVE. Ostatnia pełna kwalifikacja run A miała NF-03 FAIL; obecne testy nie nadpisują tego wyniku. Nowa kompletna ograniczona bramka **BLOCKED**, ponieważ nie wykonano świeżej, pełnej oceny wszystkich warunków w spójnym pakiecie; nie zbudowano pozytywnego wyniku przez wybranie korzystnych pomiarów z różnych runów. Niewyjaśniona zmienność jest ograniczeniem dowodowym, nie nowym progiem lub dodatkowym kryterium normatywnym D-11. Nie twierdzimy, że RT z A nagle otrzymały FAIL; nie wpisujemy im nowego PASS na podstawie NF-03. Gdy wymagany NF znów da FAIL, nowa bramka ma FAIL zgodnie z 07, bez odwołania do starego PASS.
3. **Rozszerzenie Engineering B-03A:** **BLOCKED**; nowe gettery/artefakty/Dispose i zasoby nadal NOT_RUN, Worker Handle Risk UNKNOWN. Osobnym blockerem drogi produkcyjnego Host jest opisany w raporcie B-03A brak zaufanego provider evidence IDs; syntetyczne registry nie nadaje uprawnień. Nie rozwiązywano tego tutaj. Nie rekomenduje się teraz rozpoczęcia Engineering. Można przygotować osobny zakres autoryzacji, ale wykonanie powinno czekać na pozytywną aktualną ocenę offline i jawne rozstrzygnięcie ścieżki kwalifikacji.
4. **Pełna kwalifikacja offline/produkcyjna:** ostatni pełny offline profil **FAIL**, NF-08 historyczny CPU FAIL zachowany; dziś nie uruchamiano ponownie NF-08/full runner. Produkcja niezakwalifikowana, NF-11 **NOT_RUN**, pełny ≥8 h i inne wymagane zakresy nadal B-10B/R-14. NF-11 nie jest automatycznym blockerem ograniczonej D-11. B-03B **NOT_READY**.

Przed nowym pozytywnym rozstrzygnięciem D-11 należy wykonać i ocenić komplet według [07](../../docs/07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only) / [08](../../docs/08_IMPLEMENTATION_PLAN.md#etap-2--silnik-operacji-procesy-i-recovery-mvp): RT-03/05/06/15/16 (restart admission/dispatch/receipt/result, serializacja, hang/timeout/late/loss/cancel/idempotency, UNKNOWN/quarantine/stale epoch i izolacja); NF-01/02/03/04/05/06/12/14/15 na jawnych C:/NTFS state oraz X:/NTFS workspace; rzeczywisty idle ≥300 s i ≥1000 operacji. Najprostsza istniejąca ścieżka zebrania tych dowodów to pełny `scripts/validate_offline.py` z kwalifikowanego QA Python, oddzielny UUID i zachowane wcześniejsze wyniki. To rekomendowany następny przebieg, **NOT_RUN w tej diagnostyce**, nie dopisany PASS. Ocenę ograniczoną należy potem wykonać literalnie według D-11, oddzielając historyczną agregację NF-11 reportera i pełny NF-08 od wymaganego zakresu. Nie ma nowej normatywnej liczby powtórzeń; trzy tutaj są ograniczonym wyborem diagnostycznym. Ponowny pełny run sam w sobie nie wyjaśni przyczyny zmienności.

## Dowody lokalne, walidacja i granice

Surowe wyniki, źródło pomocniczego eksperymentu, TRX, telemetry i niezmienne kopie per-run pozostają pod `.tia-bridge/diagnostics/nf-20261026/`; nie trafiają do Git. Manifest zawiera wszystkie 9 run IDs, pełne argv, UTC start/end, source/DLL hashes, agregaty oraz SHA-256 każdego result/log/TRX/telemetry. Manifest i podsumowanie nie używają losowych ID jako dowodu Siemens. Dodatkowe surowe `ResilienceEvidence` leżą w `.tia-bridge/resilience/<run-id>`.

| Artefakt | SHA-256 bajtów |
|---|---|
| Lokalny `manifest.json` | `d6d6aa70ba7a23ae042488e0cabc93a2fbff98600a8d45923bf574554d952394` |
| Lokalny `environment.json` | `b4035c9ee4f3dd7e3d440d79a455371e33f3c069c3bf57227e4dd17ccae6d729` |
| Lokalny runner `experiment.py` | `7fe6bb9758c29a921e1eee6ad537d2403b1236e8a6554b7eeb14c700ec34d111` |
| Test `ResiliencePerformanceTests.cs` | `000647726062ea7184a891f8d1c986dbe259a1250981edb56f1fbb3740017321` |
| Test `ApiPerformanceTests.cs` | `993693a5417bec4650828a79c71f542209433cac4697ac338693d6b56e637263` |
| Użyty `TiaBridge.Host.Tests.dll` Release | `5a913c40956678a4c236082303e21f7a61ac9e7ed978b00c74d4f25cbc0ae760` |
| Pomocniczy `components/run.json` | `a710a019dcc7c4f2361731d3b3c83092f70bd5ac1766f2887f799a991a06e3a5` |
| Pomocniczy `components/results.json` (200 pełnych próbek) | `92e7bedae4025653e36138c9d9677b4837cf893fd1302873bacc1e550e3c53ce` |
| Pomocniczy `components/Program.cs` | `107f0871cc7cf2504d8c73c9a9f7af3a2798ba0a2b5ea7d400fd8d305194c725` |
| Pomocniczy `components/telemetry.json` | `4e4dabfcf716eb09afdc5cbb8c0220596240f7b7899b4b5901718b112ed551b2` |

Walidacja: 9/9 celowanych niezmienionych testów .NET PASS; pomocniczy program exit 0; `py -3 scripts/audit_quality.py` PASS (50 Markdown, 432 lokalne odnośniki, bez błędów); `py -3 -B -m unittest discover -s tests/contracts -p test_limited_gate_documentation.py -v` PASS 6/6, w tym kontrola niezmienności historycznych hashy. `git diff --check`, kontrola linków oraz ręczna kontrola ograniczonego diffu: PASS. Po uzupełnieniu raportu ponowiono audyt i kontrolę staged diffu przed commitem. Nie wykonywano pełnego runnera ani testów nowego kodu, bo kod śledzony nie uległ zmianie.

Nie uruchomiono Siemens Attach, odczytów projektów, Save/Import/Export/Compile/Download w TIA, RUN/STOP ani Runtime. Nie zmieniono ochrony/usług Windows, dysków, uprawnień lub danych użytkownika; istniejące procesy Siemens pozostały nietknięte. Nie było Computer Use, NF-11 ani Engineering. Zakres oceny obu instrukcji, ADR i kontraktów oraz decyzję pozostawienia ich bez zmian zapisano w `Zmiany.md`.

```text
TASK: NF-03/NF-02 DIAGNOSTICS
NF-03 CURRENT NTFS: PASS (three targeted series only)
NF-03 REPEATABILITY: INCONCLUSIVE (historical variation not explained)
NF-02 CURRENT: PASS (three targeted series only)
NF-02 VARIABILITY: UNEXPLAINED
D-11 HISTORICAL: PASS
D-11 CURRENT READ-ONLY GATE: BLOCKED
FULL OFFLINE QUALIFICATION: FAIL (last complete profile; not rerun here)
NF-11: NOT_RUN
B-03A ENGINEERING READINESS: BLOCKED
B-03B READINESS: NOT_READY
```
