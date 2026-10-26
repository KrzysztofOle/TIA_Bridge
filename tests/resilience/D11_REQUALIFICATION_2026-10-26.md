# D-11 — ponowna kwalifikacja offline, 2026-10-26

**D-11 CURRENT LIMITED GATE: BLOCKED na preflight. Runnera kwalifikacyjnego nie uruchomiono.** Istniejący harness nie zapisuje pełnych próbek NF-02/NF-03 wymaganych w punkcie 9 bieżącego zadania. Zastosowano polecenie z punktu 7: „Jeśli runner ma ograniczenie uniemożliwiające wiarygodny wynik, opisz je i wstrzymaj kwalifikację zamiast tworzyć niezweryfikowany PASS”. Punkt 12 wyklucza poprawkę kodu i ponowienie kwalifikacji bez odrębnej decyzji. Dodatkowo nie udało się potwierdzić braku aktywnych operacji siedmiu widocznych procesów Git. Nie jest to nowe wykonanie RT/NF zakończone FAIL ani odmowa autoryzowanych operacji TIA; żadnych operacji TIA nie zlecono ani nie wykonano.

## Tożsamość i zakres

- Repo: `X:\Projects\TIA_Bridge`, gałąź `main`.
- Base SHA: `de9f20ffd4057e8afe28a25b4d4067d086a852a8`.
- Po `git fetch origin main`: HEAD = main = origin/main; worktree clean przed zapisaniem raportu.
- **Preflight ID:** `bab2cef275194d4583894a569ead636c`.
- **Qualification run ID:** `NOT_STARTED`; powyższy UUID identyfikuje tylko próbę wejścia/preflight, nie uruchomienie `validate_offline.py`.
- Zapis środowiska: `2026-10-26T14:07:18.3475106Z`; zakończenie analizy wejścia w manifeście: `2026-10-26T14:09:19.553031Z` (15:07:18–15:09:19 Europe/Warsaw). Kontrole dokumentacji wykonano później, oddzielnie od RT/NF.
- Profil zamierzony: jeden pełny offline Host/Core/IPC/Fake, state C:/NTFS poza repo, workspace X:/NTFS, bez Siemens. Nie utworzono nowej konfiguracji sesji Host/Fake ani testowego state, ponieważ zatrzymano się przed wykonaniem.

Przeanalizowano obowiązujące `AGENTS.md`, `Instrukcja_ChatGPT.md`, dokumenty 07/08/16, [historyczną D-11](X01_LIMITED_GATE.md), [rekwalifikację NTFS](X01_NTFS_REQUALIFICATION_2026-10-26.md), [diagnostykę NF-03/NF-02](NF03_NF02_DIAGNOSTICS_2026-10-26.md) oraz [B-03A](../../docs/B03A_DISCOVERY_QUALIFICATION.md), runner, reporter, matrix, fixture i właściwe testy. Nie zmieniono normatywnych progów ani zatwierdzonej semantyki D-11.

## Kontrola środowiska

| Kontrola | Obserwacja | Ocena |
|---|---|---|
| Repo i workspace X: | Fixed/NTFS, Healthy/OK; disk 1 Micron 1100 SATA 256GB, SATA, Healthy/Online | PASS kontroli lokalizacji |
| State/journal/outbox | przewidziany root `%LOCALAPPDATA%/TIA_Bridge2/dev/storage-tests` na C:/Fixed/NTFS, Healthy/OK, poza repo; nowego state i jego zapisu nie uruchomiono | PASS kontroli root; nowa sesja NOT_RUN |
| Reparse/symlink | sprawdzono 16 istniejących katalogów/ancestorów repo, `.git`, `.tia-bridge/resilience/workspaces`, root state i QA Python; wszystkie bez `ReparsePoint`, bez LinkType | PASS dla sprawdzonych ścieżek |
| Wolne miejsce | X: 162855952384 B z 255303086080 B; C: 10122846208 B z 499352883200 B | odczyt PASS; bez zapełniania/zmiany dysków |
| .NET | SDK 10.0.402, runtime Microsoft.NETCore.App i ASP.NET Core 10.0.13, x64, Windows 10.0.22000 | PASS dostępności |
| Python/QA | Python 3.12.2 x64; istniejący `%LOCALAPPDATA%/TIA_Bridge2/dev/qa-b01a-20261025/Scripts/python.exe`; import jsonschema i openapi_spec_validator PASS | PASS dostępności; bez instalacji |
| Git w repo | brak index/HEAD/config/packed-refs/shallow lock, MERGE/CHERRY_PICK/REVERT_HEAD, rebase-apply/rebase-merge; worktree clean | brak oznak operacji zmieniającej repo w obserwacji |
| Inne procesy Git | 7 żywych `git.exe`, PID 14520, 15108, 42700, 36420, 60632, 9204, 40944; `HasExited=false`, brak dostępnej klasyfikacji polecenia/rodzica; przez 1 s delta CPU i I/O = 0 | **BLOCKED potwierdzenia braku aktywnych operacji**; nie twierdzimy, że wykonują zapis w tym repo |
| CPU/RAM | Xeon E3-1240 v5, 4 rdzenie/8 logicznych; snapshot LoadPercentage 36%; RAM widoczny 33484448 KiB, wolny 6538724 KiB | zarejestrowano; nie jest to idle NF-08 |
| Dyski i równoległe I/O | w chwilowym odczycie C:/X: queue=0 i liczniki transferów/latencji=0; top process I/O: Code, Brave, ChatGPT | snapshot, bez dowodu stabilnego idle; process I/O nie wskazuje konkretnego pliku |
| AV i konfiguracja | SecurityCenter2 raportuje ESET Security i Windows Defender; nie wyłączano ochrony, usług ani obcych procesów | zachowano stan zastany |

**ENVIRONMENT: BLOCKED** dla całego zestawu warunków wejścia, mimo pozytywnych kontroli NTFS/ścieżek. Zerowa aktywność przez sekundę i brak locków nie dowodzą bezczynności siedmiu niesklasyfikowanych procesów Git. Nie zabijano ich i nie zmieniano uprawnień ani konfiguracji Windows. Ten punkt jest niezależny od brakującego zapisu próbek w harnessie.

## Audyt runnera przed uruchomieniem

| Właściwość | Stan w aktualnym kodzie; analiza statyczna, nie wynik testu |
|---|---|
| Pełna ścieżka offline | [validate_offline.py](../../scripts/validate_offline.py) wykonuje locked restore z pustego feedu/cache, Release build, pełne .NET/TRX, Python, generatory, audyt, format i diff; solution offline bez Siemens |
| Wymagane recovery | `check_trx` sprawdza obecność wymaganych Storage/Scheduler/API/Resilience cases; [matrix.json](matrix.json) wymaga 49 scenario IDs, w tym wszystkich RT-03/05/06/15/16 |
| Idle i liczba operacji | [ResiliencePerformanceTests.cs](../integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs): linie 113–132 zawierają rzeczywiste 300 × Delay(1000) i asercję ≥5 min; linie 134–156 wykonują 1001 operacji i sprawdzają historię/efekty. Nie skrócono ich i nie uruchomiono |
| Run binding | runner generuje UUID dla katalogu TRX i `TIA_BRIDGE_RESILIENCE_RUN_ID`; ResilienceEvidence zapisuje scenario/run ID, start i elapsed; reporter odrzuca niekompletne/stare/zdublowane scenariusze |
| Test execution vs qualification | runner zapisuje osobne `test_execution_status`, wyniki .NET/Python i `resilience_gate`; nie wolno ich utożsamiać z aktualną ograniczoną D-11 |
| NF-08/NF-11 a D-11 | [report_resilience.py](../../scripts/report_resilience.py), linie 68–83, zachowuje historyczną agregację: pełny soak wpływa na `gate`, idle_ok na pomocniczą readiness. Nie jest samodzielnym ewaluatorem obecnej D-11. Dokumenty 07/16 przewidują oddzielną ocenę ograniczonej bramki; sam ten znany sposób agregacji nie uzasadnia zmiany progów |
| Historia wyników | unikalne TRX i scenario JSON są per-run, ale `.tia-bridge/offline/run.json`, `.tia-bridge/resilience/run.json`, logi kroków oraz `.tia-bridge/b02c/performance.json` są współdzielone/nadpisywane. Przed ewentualnym nowym wykonaniem wymagają niezmiennego archiwum; tutaj ich nie zmieniono, zapisano i porównano SHA |
| Pełne próbki NF-02/NF-03 | **brak — blocker bieżącego zadania**, szczegóły poniżej |

### Konkretny blocker dowodowy

1. [ApiPerformanceTests.cs](../integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs), linie 47–57, gromadzi N64 czasów admission w `ConcurrentBag<double>`. Linie 73–90 zapisują wyłącznie `Distribution(admissions)` i `Distribution(edits)`: liczebność, p50/p95/p99/max; listy NF-02 oraz N100 in-place NF-03 nie są serializowane.
2. `ResiliencePerformanceTests.cs`, linie 29–38, zapisuje `Distribution(samples)` zamiast N100 próbek save/replace. Linie 55–66 zapisują `distribution` dla N100 bez workera i N100 podczas Hang, bez tablic `times`. Dotyczy to także właściwego workspace X:.
3. [ResilienceEvidence.cs](../integration/TiaBridge.Host.Tests/Resilience/ResilienceEvidence.cs), linie 18–25, serializuje wyłącznie jawnie przekazane `Data`. Nie przechwytuje lokalnych list testu. Wyjątkowo NF-05 zapisuje pełne `startup_samples_ms`, co nie zachowuje próbek NF-02/NF-03.
4. Runner i reporter nie mają drugiego zapisu tych tablic do stdout/TRX lub innego pliku. Z agregatu nie da się odtworzyć pełnej sekwencji ani wszystkich najwolniejszych próbek. Samo kopiowanie JSON po teście lub nadanie katalogowi UUID nie uzupełnia brakujących danych.

To **ograniczenie kompletności dowodów wymaganych teraz przez użytkownika**, nie wykazana wada algorytmu percentyla ani zmiana historycznego kryterium D-11. Nearest-rank, N64/8 dla NF-02, N100 dla profili NF-03 i progi 250/500 oraz 100 ms pozostają zgodne z istniejącymi testami. Nie twierdzimy, że historyczne agregaty były sfałszowane. Nie można jednak przedstawić nowej próby jako spełniającej żądanie zachowania pełnego zestawu próbek. Zgodnie z punktem 7 nie uruchomiono kosztownego przebiegu, o którym już wiadomo, że nie wytworzy wymaganych dowodów. Nie zastosowano zastępczego pomiaru ani wybranych ponowień.

## Macierz bieżącego wykonania

Wszystkie pozycje poniżej dotyczą wyłącznie **tej próby**, nie wyników historycznych. `NOT_RUN` nie oznacza FAIL konkretnej funkcji.

| ID | Wymagany zakres / kryterium z 02/07/16 | Bieżące wykonanie |
|---|---|---|
| RT-03 | restart i granice trwałego intent/dispatch/receipt/result, zachowanie ID, epoch i quarantine | NOT_RUN |
| RT-05 | serializacja syntetycznych mutacji, stale P/B, cancel/lease/fencing bez overlap | NOT_RUN |
| RT-06 | deadline, timeout i late receipt bez promocji UNKNOWN do sukcesu | NOT_RUN |
| RT-15 | własność child/lock, cleanup anomaly i quarantine bez zabijania obcych procesów | NOT_RUN |
| RT-16 | identyczne request/key → to samo ID i jeden efekt; konflikt 409, restart, brak replay UNKNOWN | NOT_RUN |
| NF-01 | status/health N1000 przy 1/8 klientach, p95 ≤100/p99 ≤250 ms | NOT_RUN |
| NF-02 | submit N64/8, trwały intent przed 202, p95 ≤250/p99 ≤500 ms | NOT_RUN |
| NF-03 | N100 ×1 MiB, paced ≥100 ms, podczas Hang p95 ≤100 ms; osobne profile C:/X:, replace kontrolne bez/z workerem, brak odmów i unsafe fallback | NOT_RUN |
| NF-04 | informacja o braku heartbeat ≤10 s; brak utożsamienia timeoutu z zakończeniem call | NOT_RUN |
| NF-05 | startup/history N20 p95 ≤5000 ms, safe read po recovery ≤30000 ms | NOT_RUN |
| NF-06 | queue deadline domyślnie ≤300 s, brak dispatch przy wygaśnięciu | NOT_RUN |
| NF-12 | zaakceptowane intenty zachowane po kontrolowanych crash, brak fałszywego sukcesu; syntetyczne WAL/artifacts | NOT_RUN |
| NF-14 | default deny i 100% niedozwolonych write/download przed adapterem w zaimplementowanym zakresie | NOT_RUN |
| NF-15 | limity API/manifest/event i kolejka 1000, health/status/duplicate dostępne przy pełnej kolejce | NOT_RUN |
| Idle D-11 | rzeczywisty pomiar ≥300 s | NOT_RUN; 0 s pomiaru kwalifikacyjnego |
| Krótki przebieg | ≥1000 operacji i kontrola wyników/efektów | NOT_RUN; 0 nowych operacji |
| Recovery / brak błędów krytycznych | dodatnie dowody z całego nowego przebiegu | NOT_RUN; brak wykonania nie jest dowodem braku błędów |

## Cztery odrębne oceny i historia

| Poziom | Bieżący wynik | Znaczenie |
|---|---|---|
| A. Functional test execution | **NOT_RUN** | pełne .NET/Python/IPC/Fake nie zostały uruchomione; kontrole dokumentacji poniżej są osobne |
| B. D-11 current limited gate | **BLOCKED** | brak możliwości spełnienia wymaganej kompletności dowodów w niezmienionym harnessie oraz niepotwierdzony warunek Git |
| C. Full offline qualification | **NOT_RUN** | nie jest nowym FAIL ani PASS; ostatnia pełna kwalifikacja pozostaje historycznym **FAIL** |
| D. Production qualification | **NOT_RUN** | poza zakresem; nie kwalifikuje jej preflight ani ewentualne przyszłe D-11 PASS |

Wzór końcowego raportu przewiduje dla A/C jedynie PASS/FAIL, ale wykonanie nie nastąpiło po wymaganym zatrzymaniu preflight. Użyto jawnego NOT_RUN zamiast zmyślenia wyniku testów. Historyczne X-01 ENGINEERING 93/93 PASS, D-11 LIMITED PASS, NF-03 exFAT FAIL, NF-08 NTFS FAIL, NF-11 NOT_RUN i Worker Handle Risk UNKNOWN zachowano bez zmian. B-03A IMPLEMENTATION pozostaje COMPLETE_OFFLINE; ENGINEERING BLOCKED/NOT_RUN; B-03B NOT_READY. Dawne trzy celowane PASS NF-02/NF-03 i pomocnicze File.Move 592,36 ms nie są wynikami obecnej próby.

NF-11 nie jest przyczyną BLOCKED ograniczonej D-11. Nie dodano pełnego NF-08 do jej kryteriów. Nie kwalifikowano nowego builda ani cleanup/handles Siemens. **B-03A ENGINEERING READINESS: BLOCKED** — ten wynik nie pozwala przejść do wykonania Engineering. Przyszły kompletny D-11 PASS mógłby uzasadniać przygotowanie osobnej autoryzacji bounded read-only B-03A, nadal z wymaganymi identity/build/capability i monitoringiem zasobów; nie nadałby sam praw TIA ani nie implementował providera evidence.

## Osobne zadanie konieczne przed następną próbą

Nie wprowadzono poprawki kodu. Proponowany wąski zakres następnego zadania implementacyjnego:

- Zapisywać pełne tablice faktycznie mierzonych admission NF-02 i wszystkich profili NF-03 wraz z N i agregatami, w niezmiennym katalogu run ID; zachowywać również częściowe dane przy FAIL/wyjątku. Bez dodatkowego warm-up, retry, odrzucania outlierów, zmiany liczby próbek, stopera lub progów.
- Powiązać output API z run ID i manifestem commit/runtime/binary/config; odrzucać stale/cross-run evidence. Archiwizować wspólne working reports przed nowym wykonaniem, zachowując dawne hashe.
- Zweryfikować testami zachowanie dowodów zarówno przy PASS, jak i przekroczeniu SLO oraz brak zmiany obliczeń percentyli; nie osłabiać flush SQLite ani operacji replace.
- Wyjaśnić własność i stan siedmiu istniejących Git procesów lub wiarygodnie potwierdzić brak aktywnych operacji; bez zabijania procesów i zmiany Windows w tym celu.

Po odrębnej decyzji i zakończeniu takiej korekty potrzebny jest **nowy pojedynczy pełny run**, obejmujący cały wymagany RT/NF, rzeczywisty idle ≥300 s i ≥1000 operacji, bez wyboru najlepszego powtórzenia. Wyniki tej próby preflight pozostają BLOCKED, a nie zostają później przepisane.

## Artefakty i hashe

Lokalny katalog: `.tia-bridge/d11-requalification/bab2cef275194d4583894a569ead636c/` (ignorowany). `preflight.json` zawiera 144 hashe źródeł/kontraktów/skryptów, 12 hashy historycznych raportów/JSON, hashe zastanych working reports i 6 binariów Release. Binariów nie przebudowano ani nie użyto do testu: poniższe hashe identyfikują zastany stan, nie kwalifikowany nowy build. Surowe dane środowiska i obserwacje procesów pozostają poza Git; nie zapisano command line ani credentiali.

| Binarne DLL Release | SHA-256 |
|---|---|
| Core | `56d09f4539bdf506800e5fe7cdd65688731c980d3a6a539fc30713a96f36c141` |
| Host | `53ab10d0ba771ee740489f670188aae0f17c923b9407f12e42f6aa98a3269ce7` |
| IPC | `fe155f8755bac7a453c2f1df1b94adb70d3d842e0e131d34f4cdbb2f3fc56653` |
| FakeWorker | `7eb94423ba94c14dbb599722214ec80955f5fcf40650b1a76c1b307782f6b832` |
| Host.Tests | `5a913c40956678a4c236082303e21f7a61ac9e7ed978b00c74d4f25cbc0ae760` |
| Core.Tests | `f3e29634f7138d2958c07be515bc0bf26ca81df2f514e6675d1740d681cb3cdd` |

| Lokalny artefakt / źródło | SHA-256 bajtów |
|---|---|
| `preflight.json` | `ec424ac50bc31894edad53a23ea46c6ce1f738a1a4f315d5a7e1e3e7f31f5746` |
| `environment.json` | `2184a4e4d7b748e08743bae4c71101ab3db7aa2175af89cb71c13ab191c1c217` |
| `git-process-observation.json` | `47579aa4777b4f7a23b8d880f7b5f26524d5d4e1cdb4ba728ac4305f53320a03` |
| `git-lifecycle.json` | `29f0837b7220d91f54faa0547676bb3c751bf03d3db5c57bbba930e5f902a761` |
| `dotnet-info.txt` | `c7078014d415f9ec8c195dec12aa83a4a7ba578bae5683da4ff4f7d63ba42ba3` |
| `scripts/validate_offline.py` | `708aebead528bbdd992d0304b44d043ba1f717ccabd7369aa26180049ece87c8` |
| `scripts/report_resilience.py` | `270f3c8934234ac1f5b33acf7c63bc62f963b9d46e1f0b761e5c3cf56467a119` |
| `ApiPerformanceTests.cs` | `993693a5417bec4650828a79c71f542209433cac4697ac338693d6b56e637263` |
| `ResiliencePerformanceTests.cs` | `000647726062ea7184a891f8d1c986dbe259a1250981edb56f1fbb3740017321` |
| `ResilienceEvidence.cs` | `5db4aef154594c61b3ad6c46ee9211121a19ecc1c737ae4b8757781db204fb4b` |

## Walidacja i granice dostawy

Audyt `py -3 scripts/audit_quality.py`: **PASS**, 51 Markdown, 443 lokalne odnośniki, zero błędów. `py -3 -B -m unittest discover -s tests/contracts -p test_limited_gate_documentation.py -v`: **6/6 PASS**, w tym kontrola historycznych hashy. To kontrole dokumentacji, nie functional test execution tej kwalifikacji. Sprawdzenie SHA potwierdziło niezmienność 144 źródeł/kontraktów/skryptów, 12 raportów historycznych, 3 zastanych working reports i 6 DLL oraz zgodność hashy artefaktów z tabelą. `git diff --check`: PASS; po uzupełnieniu wyników ponowiono audyt i skontrolowano staged diff. Commit obejmuje wyłącznie ten raport i `Zmiany.md`; bez kodu, kontraktów, surowych logów, TRX, baz lub zmian historycznych raportów. Obie instrukcje i właściwe ADR/specyfikacje/kontrakty oceniono sekcja po sekcji; pozostają bez zmian, uzasadnienie w `Zmiany.md`.

Nie uruchomiono `validate_offline.py`, testów RT/NF, idle/1000 operacji, Siemens Openness, Attach/Discovery, Save/Import/Export/Compile/Download w TIA, RUN/STOP, Runtime, B-03B ani live evidence providera. Nie wykonano automatycznej naprawy, ponowienia lub wyboru korzystnego wyniku.
