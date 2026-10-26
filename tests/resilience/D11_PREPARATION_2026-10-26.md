# D-11 PREPARATION — pełne próbki i preflight, 2026-10-26

**Harness gotowy; ENVIRONMENT READINESS: BLOCKED dla kontrolowanego pomiaru przy końcowym obciążeniu hosta.** To wynik przygotowania i testów celowanych, nie D-11 PASS. Pełna kwalifikacja i Engineering nie zostały uruchomione. Historyczne D-11 PASS, ostatnia bieżąca ocena BLOCKED, pełny offline FAIL, NF-11 NOT_RUN i Worker Handle Risk UNKNOWN pozostają niezmienione.

Base SHA: `d114d9eca1a49879dbdbe0d9d500080a3fc6709a`. Na wejściu po fetch HEAD = origin/main, worktree clean. Przeczytano AGENTS, instrukcję ChatGPT, architekturę/synchronizację/interfejs/integrację, 07/08/16, historyczną D-11 oraz oba raporty [diagnostyki NF](NF03_NF02_DIAGNOSTICS_2026-10-26.md) i [blokady preflight](D11_REQUALIFICATION_2026-10-26.md), runner i testy. Żaden wynik historyczny nie został uzupełniony nowymi próbkami.

## Analiza i zmiany harnessu

Pierwotny przepływ: **Stopwatch (czas ścienny) → List/ConcurrentBag → nearest-rank p50/p95/p99/max → JSON agregatu → reporter → kwalifikacja**. N64 submit/8 klientów i N100 na każdy profil plikowy były mierzone w całości, ale surowe listy znikały. API nadpisywało wspólne `.tia-bridge/b02c/performance.json`; reporter kontrolował jedynie jego timestamp względem startu, a nie tożsamość runu. Scenario evidence miało UUID, lecz również używało zapisu pozwalającego nadpisać ten sam plik.

Nowy przepływ: **te same pomiary/kolekcje → pełny snapshot rzeczywistych próbek i agregaty → create-new i SHA w dotychczasowym run root → odtworzenie agregatów/binding → reporter**. Zmieniono wyłącznie testy i narzędzia QA, bez funkcjonalności B-03A, kontraktów API/IPC ani vendor API.

- [PerformanceEvidence.cs](../integration/TiaBridge.Host.Tests/Resilience/PerformanceEvidence.cs) zachowuje próbki także po asercji/wyjątku przez Dispose. Każda ma ID i wartość ms; ID jest indeksem snapshotu kolekcji, nie chronologią zakończeń ośmiu klientów. Zapis obejmuje test/profile, N oczekiwane/faktyczne, wall_clock, agregaty/limity/status, UTC start/end, Git SHA/dirty, hashe źródeł i 5 DLL, runtime/OS/CPU count, state/workspace/filesystem, Fake Hang i hash konfiguracji. Dane konfiguracji ani tokeny nie trafiają do Git.
- Zapis próbek i metadanych jest poza przedziałami Stopwatch. Dodaje koszt przygotowania/eksportu i może ogrzać cache, więc sam wymaga nowego pełnego przebiegu; nie przelicza wcześniej zmierzonych wartości. Nie zmieniono pętli N64/N100, warm-up, pauz, operacji plikowych, granic stopera, asercji i progów. Całe kolekcje są zachowane bez selekcji/outlier filtering. Częściowe dane nie są uzupełniane sztucznie. Dotychczasowa słaba asercja długości SHA w in-place pozostaje otwartym ograniczeniem integralności treści; nie zmienia oceny czasu ani nie została naprawiona przy okazji.
- Formaty performance i manifest mają wersję **1.1**, generic ResilienceEvidence zachowuje 1.0. Nowe pliki są w `.tia-bridge/resilience/<run-id>/performance/`; generic scenario i performance zapisują przez CreateNew, ze flush dowodów po pomiarach. Próba nadpisania kończy się błędem i zachowuje stare bajty.
- [validate_offline.py](../../scripts/validate_offline.py) rezerwuje nowy UUID/root, umieszcza TRX w `test-results/`, a metadata/środowisko/logi/raporty w `validation/`. Na końcu także przy FAIL tworzy `evidence-manifest.json` ze spisem plików i SHA. Stare wspólne working reports pozostają nietknięte. W tym zadaniu prawdziwego `main()` pełnego runnera nie uruchamiano; test orkiestracji używa wyłącznie mocków wszystkich wywołań wykonawczych.
- [performance_evidence.py](../../scripts/performance_evidence.py) kontroluje strukturę, kompletność/ID/N, ms/wall_clock, finite/nonnegative, niezmienne limity, odtwarzalność agregatów/statusów, run/build/source/hash, UTC, jednostki, manifest i artefakty. Reporter porównuje jeszcze agregaty starego scenario envelope z pełnymi próbkami i metadata runu. Nie czyta już wspólnego performance.json dla nowych dowodów.
- Historyczne agregaty można analizować tylko jawnie przez `allow_legacy=True`; wynik pełności to **INSUFFICIENT**, bez nowego gate PASS. Nowy strict reader odmawia brakujących dowodów. Naprawiono też spójność pola `NF-03.ntfs_save_replace_status`: wcześniej było stałą PASS, teraz odzwierciedla wynik istniejącego testu C:/NTFS. Asercja tego testu oraz warunek p95 ≤100 ms nie uległy zmianie. Historia na dysku nie została przeliczona/przepisana.

## Weryfikacja i pełne próbki

**Build Release PASS**, 0 warnings/errors; **5/5 celowanych .NET PASS**, 0 skipped; **42/42 Python PASS**. Początkowy build nowego testu pomocniczego ujawnił CS0619 dla niejednoznacznej lambdy `Assert.Throws`; skorygowano rzutowanie Action przed finalnym build/testami. Nie był to przebieg kwalifikacji.

Celowany run **`6813bd8866824ed18a89b7a08dd820ce`**, UTC **14:32:50.7626763–14:33:53.6961979** (15:32:50–15:33:53 Europe/Warsaw), scope jawnie `D-11 PREPARATION`. Zawiera trzy dotychczasowe testy NF i dwa nowe testy helpera. Wersja źródeł: base SHA powyżej + `git_dirty=true` i rzeczywiste source hashes; nie przedstawiamy tego jako pomiaru późniejszego czystego commita. SHA testowanego `TiaBridge.Host.Tests.dll`: `6a32c63c2de6a88266ff50b6580b38e77a2c5399810ed785d0d9c017e66fb2c1`; pozostałe DLL i źródła są jawnie wymienione w każdym bundle.

| Profil | N | p50 ms | p95 ms | p99 ms | max ms | Wynik tych próbek |
|---|---|---|---|---|---|---|
| NF-02 submit/8, C: state | 64 | 136.6857 | 163.0215 | 209.9525 | 209.9525 | PASS, ≤250/500 |
| NF-03 X: in-place przy Hang | 100 | 0.9795 | 1.5964 | 3.8061 | 5.6614 | PASS, p95 ≤100 |
| NF-03 C: save/replace przy Hang | 100 | 2.0231 | 2.4721 | 3.3341 | 7.4202 | PASS, p95 ≤100 |
| NF-03 X: replace bez workera | 100 | 2.8629 | 4.2831 | 12.8475 | 16.2100 | PASS, p95 ≤100, refused=0 |
| NF-03 X: replace podczas Hang | 100 | 2.8435 | 3.5231 | 3.8380 | 7.6055 | PASS, p95 ≤100, refused=0 |

**464 rzeczywiste wartości** zapisano i niezależnie odtworzono ich agregaty. Nie dodawano ponowień NF w celu poprawy SLO. Test helpera dodatkowo zachowuje jedną syntetyczną próbkę po kontrolowanym wyjątku jako INCOMPLETE/FAIL, oddzielnie od powyższych wyników; nie jest częścią N100. Próba drugiego zapisu zachowała oryginalne bajty i zwróciła błąd.

Python obejmuje wszystkie 15 wymaganych klas uszkodzeń: brak/duplikat/nieprawidłowe N, NaN/Infinity/ujemny czas, złe percentyle, run ID, hash, brak artefaktu, overwrite, stale evidence, mix NF-02/NF-03, jednostka, uszkodzony JSON i historyczny format bez próbek. Dodatkowo bada source/build/metadata mismatch, niezgodne statusy/progi, zachowanie SLO FAIL i częściowych wyników oraz orkiestrację dwóch rozdzielonych przebiegów bez uruchomienia prawdziwego profilu. Testy Python używają danych syntetycznych, nie uzupełniają pomiarów runtime.

## Git — siedem procesów

Nowy odczyt CIM udostępnił CommandLine/ExecutablePath: wszystkie siedem to **`git fsmonitor--daemon run --detach --ipc-threads=8`**, executable `C:\Program Files\Git\mingw64\libexec\git-core\git.exe`, SessionId=1, procesy żywe. Rodzice o podanych PID nie byli już dostępni; nie przypisano ich automatycznie VS Code/CODEX. `--detach` i rola fsmonitor wyjaśniają długie życie procesu, nie dowodzą pozostawionego zadania ani aktywnego commita.

| PID | Parent PID | Start UTC | Klasyfikacja / aktywność |
|---|---|---|---|
| 14520 | 1996 | 2026-10-23 07:47:52 | fsmonitor; bez zaobserwowanego CPU/I/O |
| 15108 | 28680 | 2026-10-23 07:47:52 | fsmonitor; bez zaobserwowanego CPU/I/O |
| 42700 | 2012 | 2026-10-23 11:45:48 | fsmonitor; bez zaobserwowanego CPU/I/O |
| 36420 | 42400 | 2026-10-23 11:45:49 | fsmonitor; bez zaobserwowanego CPU/I/O |
| 60632 | 69200 | 2026-10-24 14:56:19 | fsmonitor; bez zaobserwowanego CPU/I/O |
| 9204 | 77280 | 2026-10-26 05:22:09 | fsmonitor; bez zaobserwowanego CPU/I/O |
| 40944 | 30952 | 2026-10-26 10:24:42 | fsmonitor; bez zaobserwowanego CPU/I/O |

Ograniczone okna pięciu obserwacji (pauzy 2 s, około 9 s obserwacji Git) zachowano w surowych JSON. Dla tych PID delty kernel/user CPU i read/write transfer wyniosły 0; to nie dowód bezczynności poza oknem. `git fsmonitor--daemon status` potwierdził watcher dla `X:/Projects/TIA_Bridge`, a read-only `git config --show-origin --get core.fsmonitor` wskazał istniejące `true` z konfiguracji użytkownika. Nie ustalono, który konkretny PID obsługuje ten repozytorium; CWD wskazywało katalog użytkownika. Pochodzenie pozostałych watcherów pozostaje niepotwierdzone, bez dowodu szkodliwej operacji.

W pierwszym odczycie wykonanym podczas własnego formatowania pojawiło się również sześć krótkotrwałych Git wpisów bez dostępnej CommandLine; nie przetrwały do okna obserwacji i nie przypisano im pochodzenia. Cztery późniejsze odczyty pokazały tylko siedem powyższych daemonów. Wszystkie obserwacje pozostają zachowane. **GIT PROCESS DIAGNOSTICS: PASS** dla identyfikacji siedmiu zgłoszonych procesów; nie jest to twierdzenie o absolutnym braku aktywności każdego Git w systemie. Nie zabijano procesów i nie zmieniano konfiguracji edytora/Git.

## Siemens i NTFS

W pięciu snapshotach **brak `Siemens.Automation.Portal.exe`: TIA PORTAL MAIN INSTANCES: NONE**. Pozostałe procesy rozdzielono w [d11_preflight.ps1](../../scripts/d11_preflight.ps1): usługi Session 0 (tracing, communication, telemetry), Automation License Manager, PLCSIM Advanced Runtime Manager / V19 VplcHost oraz `siemens-lsp.exe` z rozszerzenia VS Code. Nie są dowodem otwartego projektu. Dla części usług nie było dostępu do pełnej ścieżki/command line; oznaczono to w danych. Nie odpytano projektów ani interfejsu Siemens i nie zatrzymano usług, helperów lub symulacji.

Ostatni bounded preflight: **2026-10-26 14:53:32.4954448–14:53:51.1528007 UTC**, plik `environment-97db9b54ae3e4a4da6b60c363eb50a9f.json`. Repo/workspace X: i root state C: są lokalne Fixed/NTFS, Healthy. Sprawdzono **20 katalogów/ancestorów**, w tym `.git`, obie lokalizacje workspace, root `%LOCALAPPDATA%/TIA_Bridge2/dev/storage-tests`, QA i katalog wyniku: brak reparse/junction. X: wolne **162855239680 B** z 255303086080 B; C: wolne **16145014784 B** z 499352883200 B. State SQLite/journal/outbox pozostaje poza repo. Krytyczne pliki testów tworzone są w własnych UUID; nie dotykano projektów ani backupów.

Windows 11 Pro 10.0.22000, Xeon E3-1240 v5 (8 logicznych), .NET SDK 10.0.402/runtime 10.0.13 x64, Python 3.12.2 i istniejący QA venv. Import wymaganych bibliotek i build potwierdziły gotowość runtime. W ostatnim oknie globalne CPU: **100/91/99/98/100%**, osobny snapshot CPU 91%, dostępny RAM **13051884 KiB**. Chwilowy C: queue=0, około 49,6 MB/s; X: queue=0 i 0 B/s. Process I/O obejmowało ESET `ekrn` około 25,8 MB/s read i 9,8 MB/s write oraz Code. Format verify był już zakończony; nie trwał żaden test NF. Process CPU counter ESET 354 jest sumowany po logicznych CPU, nie jest globalnym 354% hosta; nie przypisano ESET całości obciążenia.

Wcześniejsze snapshoty również zachowano: CPU 29%, a potem 88%; trzecia seria globalnego CPU wyniosła 20–44%, przy ESET około 20 MB/s read. Wysoki drugi odczyt uzasadnił krótką serię globalnego CPU, czwarty snapshot zweryfikował dodanie nazw procesów (CPU 24–86%, część okna obejmowała własny format verify), a piąty końcową redakcję credentiali command line po jej syntetycznych testach (3/3 PASS). Nie są to ponowienia NF ani wybór najlepszego wyniku. Host nie jest dedykowanym idle środowiskiem; AV/I/O mogą wpływać na ogon NF-02/NF-03. Nie ma zatwierdzonego progu globalnego CPU lub zakazu fsmonitor/Code/AV w D-11, więc nie ustanowiono nowego FAIL za ich obecność. **NTFS PREFLIGHT: PASS; ENVIRONMENT READINESS: BLOCKED dla kontrolowanego pomiaru teraz**: pięć końcowych obserwacji blisko pełnego wykorzystania CPU i intensywne I/O to konkretne ryzyko zakłócenia NF-01/NF-02/NF-03 i deadline testów procesowych. To operacyjna rekomendacja przygotowania pomiaru, nie dodatkowy warunek normatywny D-11 ani nowy FAIL RT/NF. Nie użyto wcześniejszych lżejszych snapshotów do nadania bieżącego READY. Nie wyłączano ochrony ani usług. Sam ten wynik nie wyjaśnia historycznych opóźnień ani nie usuwa Worker Handle Risk UNKNOWN.

## Artefakty i kontrola jakości

Run celowany: `.tia-bridge/resilience/6813bd8866824ed18a89b7a08dd820ce/`, ze wszystkimi próbkami, manifestami, TRX, konfiguracją pomiaru i odczytem walidatora. Surowe preflighty i snapshot wcześniejszych hashy: `.tia-bridge/preparation/d11-20261026/`. Te pliki pozostają ignorowane i nie są commitowane.

| Artefakt lokalny | SHA-256 |
|---|---|
| Run `evidence-manifest.json` | `fe53825d41ec0407737bb95dbeb261f76f140b67da6a7030ef9d1d27734e205e` |
| Run `validation/sample-check.json` | `d5c861982b895a2c97c83e7a0b0de8a86e0e6118b28a01b114491719d7591a3a` |
| Ostatni preflight JSON | `82d48bf0c00f813d241baefb535377c08fedc244eccac1d6a08253c4e5cc9ec2` |
| Wykonany `d11_preflight.ps1` | `96869d786d23a29dc3eeae47e4cec8c970dbbcfca893098ac3d15161c16baebb` |

Manifest runu zweryfikowano ponownie po zapisaniu; wszystkie trzy bundle mają PASS pełności i zgodności agregatów/hashów. Kontrola snapshotu: **454 wcześniejsze artefakty/raporty bez zmiany bajtów**; jedynym planowo zmienionym plikiem ze spisu 455 jest instrukcyjny `tests/resilience/README.md`, nie raport historyczny. Build Release, 5 celowanych .NET, 42 Python i format verify PASS. Audyt dokumentacji/linków PASS: 52 Markdown, 460 lokalnych odnośników, bez błędów; diff check PASS. Po finalnym uzupełnieniu raportu ponowiono audyt i sprawdzono staged diff przed commitem. Własny wygenerowany cache Pythona po odrzuconym przez automatyczną kontrolę usunięciu zachowano przez przeniesienie do ignorowanego katalogu preparation; nie trafił do indeksu.

Obie instrukcje i ADR oceniono sekcja po sekcji; bez zmian architektury, uprawnień, kontraktów i kryteriów kwalifikacji nie ma potrzeby ich edycji. Uzasadnienie i zakres oceny są w `Zmiany.md`. Zaktualizowano instrukcje testowe, scripts README i opis dowodów w 16.

## Następny krok — polecenie, którego nie wykonano

Minimalne działanie: poczekać na zakończenie przejściowego obciążenia w normalnej pracy Windows, bez wyłączania AV lub zabijania procesów, i wykonać nowy krótki `d11_preflight.ps1`. Nie czekać automatycznie w pętli aż pojawi się korzystny wynik. Dopiero po rozstrzygnięciu tej gotowości i sprawdzeniu HEAD/worktree osobny kompletny przebieg offline może użyć:

```powershell
& "$env:LOCALAPPDATA\TIA_Bridge2\dev\qa-b01a-20261025\Scripts\python.exe" -B scripts/validate_offline.py
```

Powstanie nowy run ID; potrzebne będą wszystkie RT/NF D-11, rzeczywisty idle ≥300 s i ≥1000 operacji oraz odrębna ocena ograniczonej bramki według 07/08. Kod wyjścia i historyczne pola agregacji NF-08/NF-11 nie są samodzielnym PASS tej bramki. Zachować każdy FAIL, bez wyboru korzystnego ponowienia. Dopiero pozytywna pełna ocena D-11 pozwoli przygotować osobną autoryzację bounded Engineering B-03A.

**W tym zadaniu: D-11 CURRENT LIMITED GATE NOT_RUN; FULL OFFLINE QUALIFICATION NOT_RUN; ENGINEERING LIVE NOT_RUN; B-03A ENGINEERING BLOCKED; B-03B NOT_READY.** Ostatnie oceny BLOCKED/FAIL pozostają historycznie zapisane. Nie wykonano Openness, Attach, rzeczywistego Discovery, Save/Import/Export/Compile/Download w TIA, RUN/STOP, NF-11, zmian B-03A/provider lub sterowania procesami Git/Siemens.
