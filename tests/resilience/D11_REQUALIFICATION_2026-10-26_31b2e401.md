# D-11 — formalny preflight i ponowna kwalifikacja offline, 2026-10-26

```text
TASK: D-11 CONDITIONAL REQUALIFICATION
BASE SHA: ff20b79f04e8b62463cd10f7bb681ffbae15aaab
PREFLIGHT ID: 31b2e401a9224ce2ace3d5239205b6d1
RUN ID: 2535d5e5e0584f4b95d7ed5794913c47
PREFLIGHT: PASS
D-11 CURRENT LIMITED GATE: PASS
FUNCTIONAL TEST EXECUTION: PASS
FULL OFFLINE QUALIFICATION: NOT_RUN (runner resilience gate BLOCKED by NF-11)
PRODUCTION QUALIFICATION: NOT_RUN
NF-11: NOT_RUN
WORKER HANDLE RISK: UNKNOWN
B-03A IMPLEMENTATION: COMPLETE_OFFLINE
B-03A ENGINEERING READINESS: READY TO PREPARE A SEPARATE AUTHORIZATION
B-03A ENGINEERING EXECUTION: BLOCKED/NOT_RUN
B-03B READINESS: NOT_READY
```

To jest nowa ocena i nowy pojedynczy run. Wcześniejsze raporty [blokady preflight](D11_REQUALIFICATION_2026-10-26.md), [przygotowania harnessu](D11_PREPARATION_2026-10-26.md), [diagnostyki NF-02/NF-03](NF03_NF02_DIAGNOSTICS_2026-10-26.md), [historycznej bramki D-11](X01_LIMITED_GATE.md) oraz wcześniejszy pełny profil pozostają niezmienione.

## Preflight

Lokalny `main`, `HEAD` i `origin/main` wskazywały `ff20b79f04e8b62463cd10f7bb681ffbae15aaab`; ahead/behind `0/0`, worktree czysty, brak merge/rebase/cherry-pick. Repo, `.git`, katalogi wynikowe i przyszły workspace runu są na lokalnym woluminie X: Fixed/NTFS. Trwały state i katalog `dev\storage-tests` są poza repo na lokalnym C:/NTFS. Zweryfikowane łańcuchy katalogów nie zawierały reparse points; ścieżki były dostępne. Wolne miejsce w obserwacji: X: 162854952960 B, C: 18856202240 B.

Dostępne narzędzia: Python 3.12.2 x64 z istniejącego zewnętrznego QA venv, `jsonschema 4.26.0`, `openapi-spec-validator 0.7.2`, .NET SDK 10.0.402/runtime 10.0.13 x64 i Git 2.50.1. Sprawdzono aktualne Release binaries; build właściwy dla tego runu wykonał runner. Wymagane źródła, runner i dotychczasowe wyniki pozostały identyfikowalne przez hashe zapisane w ignorowanym `preflight.json`.

Obserwacja CPU/RAM/dysków trwała 17,6 s i miała cztery próbki: CPU 8/35/3/2%, dostępny RAM 14108/14055/14074/14074 MiB. Kolejka dysków wynosiła 0; aktywność C: była zerowa poza krótką próbką 64988 B/s, X: pozostał bez obserwowanego transferu. Nie wystąpiło utrzymujące się wysokie obciążenie ani presja pamięci. Siedem procesów Git sklasyfikowano jako `fsmonitor--daemon`; nie widać było procesów Host/Worker/FakeWorker/testhost pozostawionych przez inne testy. Nie stwierdzono głównej instancji TIA Portal. Aktywne usługi WCCIL Runtime pozostawiono bez zmian; brak ich mierzalnej konkurencyjnej aktywności dyskowej w preflight. Ich możliwy wpływ na ogon pomiarów zapisano jako ograniczone ryzyko środowiskowe, a nie automatyczny FAIL.

Wszystkie wymagane warunki wejścia potwierdzono; **PREFLIGHT: PASS**. Pełny zrzut preflightu oraz hashe źródeł i binariów: `.tia-bridge/d11-requalification/31b2e401a9224ce2ace3d5239205b6d1/preflight.json` (SHA-256 `764d6a5643f1514db04978a81db2f31a29d01bd979d4d02e7f99b6dccea6b960`). Nie zmieniano lokalizacji, ustawień Windows, usług ani procesów.

## Wykonanie i identyfikowalność

Uruchomiono dokładnie jeden kanoniczny runner:

```powershell
& "$env:LOCALAPPDATA\TIA_Bridge2\dev\qa-b01a-20261025\Scripts\python.exe" -B scripts/validate_offline.py
```

Start `2026-10-26T18:01:21.427538Z`, koniec `2026-10-26T18:20:27.912Z` (wartości UTC z metadanych runu). Repo SHA `ff20b79f04e8b62463cd10f7bb681ffbae15aaab`; Python `3.12.2`, .NET SDK `10.0.402`, runtime `.NET 10.0.13`, Windows `10.0.22000`, x64, 8 logical processors. Restore locked z pustym lokalnym feedem, build Release: **0 warnings, 0 errors**.

Testy: Host 257/257, Core 546/546, razem **803/803 .NET PASS**, 0 FAIL/0 SKIP; Python **42/42 PASS**, 0 FAIL/0 SKIP. Generatory, audit dokumentacji, `dotnet format --verify-no-changes` i krok `git diff --check` runnera: PASS. Run JSON podaje `status=PASS`, `test_execution_status=PASS`, `resilience_gate=BLOCKED`, `x01_readiness=NOT_READY`. Runner zakończył się kodem 0. `resilience_gate` raportera obejmuje szerszy pełny profil i wskazuje wyłącznie `NF-11 eight-hour soak is NOT_RUN`; nie przeniesiono go na ograniczoną D-11.

Surowe, wyłączne dla tego runu dowody znajdują się w ignorowanym `.tia-bridge/resilience/2535d5e5e0584f4b95d7ed5794913c47/`. `evidence-manifest.json` obejmuje 73 artefakty i ma SHA-256 `1685e00870cbef4e593215cd0273175e09b18172438c23361d0a845a4708c72a`. Walidator manifestu oraz loader próbek potwierdziły integralność, binding run/source/build i odtwarzalność agregatów. SHA-256 kluczowych plików:

| Artefakt | SHA-256 |
|---|---|
| `validation/run.json` | `a2681f3951928be9de2f681a87fad9e87d91dfd1b9e006a505d7571fa9587e27` |
| `validation/resilience.json` | `1e57776229071ff552fd553e02971dba58e36e849d0f77e89630d9ca5612cbe8` |
| Host tests TRX | `1816917f4c3e699b6844f842043bc355bc42579bfe84abaf48d966614de8fae5` |
| Core tests TRX | `b4d8d584a6642b068877b6ad4fa0b1911f4fe2d19e3ecd67dda530644a2ec619` |
| `TiaBridge.Core.dll` | `c1a0283aa96f2f90954adeec104361d2c513d161ff956f86b1cf0ba1e46ff33c` |
| `TiaBridge.Host.dll` | `852b8dddadd1eed0cdec62834344910f444c5ef9061acb56471f263db709a3b7` |
| `TiaBridge.Ipc.dll` | `ba406e50f1238e83f2d40e661bcf67032f6cf55051832a7ea7704a763f619367` |
| `TiaBridge.FakeWorker.dll` | `c7f222b46eed043159d2b5b5d9ccb7f9203a2319e73d36b67a0b15e2f6e14eff` |
| `TiaBridge.Host.Tests.dll` | `c385e5bbf564b6deb1d38c53d233913d02a440dd05cccebc76e281a2084255c9` |
| `TiaBridge.Core.Tests.dll` | `a121d4ef1320f4e86c7f6093802bd4e7068c5636de0c4a1773c3b6e326f432f8` |

## Macierz D-11

| Wymaganie | Wynik | Zakres dowodu |
|---|---|---|
| RT-03 | PASS | restart Host, trwałe granice admission/dispatch/receipt/result, queued/running recovery i UNKNOWN |
| RT-05 | PASS | dwa zapisy syntetyczne, serializacja, stale P/B, cancel race i fencing |
| RT-06 | PASS | timeout, late receipt, worker loss, stary epoch bez promocji UNKNOWN |
| RT-15 | PASS | owned child/lock, cleanup, izolacja obcego procesu; bez terminacji obcych procesów |
| RT-16 | PASS | 100 identycznych żądań, idempotency, jedno ID/efekt i zachowana historia |
| NF-01 | PASS | status/health, 1 i 8 klientów, Host podczas Hang i recovery |
| NF-02 | PASS | N=64 submit przy 8 klientach; p95 152.1215 ms ≤250, p99 194.5564 ms ≤500 |
| NF-03 | PASS | N=100 in-place oraz save/replace na NTFS; niezależne replace bez i z workerem |
| NF-04 | PASS | wykrycie braku heartbeat w wymaganym zakresie, bez uznania timeoutu za zakończenie call |
| NF-05 | PASS | N=20 startup/history p95 935.5389 ms ≤5000; safe read 850.4761 ms ≤30000 |
| NF-06 | PASS | deadline kolejki, brak dispatch po wygaśnięciu i obsługa granic recovery |
| NF-12 | PASS | durable intent/result, crash boundaries, WAL/artifact i brak fałszywego sukcesu |
| NF-14 | PASS | default deny w zaimplementowanym zakresie syntetycznym |
| NF-15 | PASS | limit 1000 aktywnych, odmowa kolejnego, health/status/duplicate przy pełnej kolejce |

Wyniki RT dotyczą Host/Fake i nie kwalifikują API Siemens. Wszystkie wymagane scenariusze RT/NF przeszły bez błędu krytycznego wykonania lub izolacji. Pełną listę scenariuszy i hash każdego pliku zawiera run manifest.

## NF-02/NF-03 — pełne próbki i agregaty

Łącznie zachowano **464/464** rzeczywistych pomiarów: 64 submit NF-02 oraz cztery kolekcje NF-03 po 100. Dane są w trzech run-bound bundles; `performance_evidence.load_bundle` zweryfikował liczności, p50/p95/p99/max, statusy progów i zgodność agregatów z próbkami.

| Profil | N | p50 ms | p95 ms | p99 ms | max ms | Kryterium | Wynik |
|---|---:|---:|---:|---:|---:|---|---|
| NF-02 submit, 8 klientów | 64 | 125.5562 | 152.1215 | 194.5564 | 194.5564 | p95 ≤250; p99 ≤500 | PASS |
| NF-03 in-place 1 MiB, podczas Hang | 100 | 1.0101 | 1.2894 | 1.5345 | 1.5413 | p95 ≤100 | PASS |
| NF-03 workspace save/replace 1 MiB, X:/NTFS | 100 | 2.2841 | 2.9710 | 15.5739 | 35.4445 | p95 ≤100 | PASS |
| NF-03 repository replace, bez worker | 100 | 2.7490 | 3.4237 | 3.7805 | 10.7632 | 100/100, brak refusal, p95 ≤100 | PASS |
| NF-03 repository replace, z worker | 100 | 2.7049 | 3.4316 | 3.9688 | 7.8575 | 100/100, brak refusal, p95 ≤100 | PASS |

Save/replace używało interwału 100 ms. W obu profilach kontrolnych `success=true`, `refused_samples=0`, bez HRESULT, a oryginał jest zachowywany przy odmowie. NF-03 exFAT pozostaje historycznym FAIL; ten run kwalifikuje wyłącznie obserwowany profil lokalnego NTFS.

## Idle, operacje i oddzielne statusy

- D-11 idle: **306.2499262 s PASS** (wymagane ≥300 s); CPU Host znormalizowane do wszystkich logicznych procesorów 0.7883%, Host peak 90.09 MiB, Fake Worker peak 37.96 MiB.
- Krótki przebieg: **1001 operacji PASS**; wszystkie zaakceptowane ID i wyniki zachowane, jeden efekt na ID. Zmierzono 431.2093668 s. To nie jest ośmiogodzinny soak.
- `NF-08: NOT_RUN` dla pełnego profilu skali/obiektów. Podprzypadek pięciominutowego idle przeszedł; nie zmienia to historycznego NF-08 FAIL ani nie tworzy pełnego NF-08 PASS.
- `NF-11: NOT_RUN`; nie uruchamiano soak ≥8 h zgodnie z zakresem.

| Warstwa | Bieżący status | Uzasadnienie |
|---|---|---|
| Functional test execution | PASS | .NET 803/803, Python 42/42, 0 fail/skip; build i kroki walidacyjne PASS |
| D-11 current limited read-only gate | PASS | wszystkie wymagane RT/NF, lokalny NTFS, idle ≥300 s, ≥1000 operacji, próbki i izolacja PASS |
| Full offline qualification | NOT_RUN | run testów pełnego runnera przeszedł, ale jego szerszy `resilience_gate` jest BLOCKED; NF-11 i pełny profil NF-08 nie są zaliczone |
| Production qualification | NOT_RUN | poza zakresem; nie przeprowadzono Engineering/Runtime |

W ten sposób nie zmieniono historycznego `FULL OFFLINE QUALIFICATION: FAIL` ostatniego kompletnego profilu. Nie utożsamiono runnerowego exit code 0 ani test execution PASS z kwalifikacją pełnego profilu. NF-11 pozostaje `NOT_RUN` i nie blokuje D-11. NF-08 nie jest warunkiem D-11 według 07.

## Wniosek dla B-03A i granice

D-11 PASS pozwala **przygotować osobne zadanie i autoryzację** bounded Engineering B-03A read-only. Nie jest zgodą na Attach/odczyty Siemens ani rozpoczęciem Engineering. `B-03A IMPLEMENTATION: COMPLETE_OFFLINE`; `B-03A ENGINEERING EXECUTION: BLOCKED/NOT_RUN`; `B-03B: NOT_READY`.

Pozostaje otwarty problem zaufanego provider’a weryfikującego qualification evidence IDs w produkcyjnym Host: obecny Host przekazuje pusty kontekst, a syntetyczne testy wstrzykują evidence tylko testowo. Ścieżka pozostaje fail-closed. Przed wykonaniem nowego Engineering należy w osobnym, ograniczonym zadaniu rozstrzygnąć tę granicę lub zapewnić kwalifikowany sposób uprawnień, zatwierdzić dokładny zakres i ponownie sprawdzić tożsamość projektu, DLL, capability i własność procesów. Worker Handle Risk dla rzeczywistego V19 pozostaje `UNKNOWN`; testy Fake tego nie kwalifikują.

Nie uruchamiano Siemens Openness, TIA Portal, Attach, nowych odczytów Engineering, Save/Import/Export/Compile/Download w TIA, RUN/STOP, Runtime, NF-11 ani B-03B. Nie zmieniano kodu produkcyjnego, testów, progów, Windows, procesów ani historycznych raportów. Nie wystąpił konflikt normatywny: pełny gate reportera i ograniczona D-11 są odrębnymi statusami zgodnie z 07 oraz jawnym zakresem tego zadania.

## Kontrola dostawy

Plik preflight i surowe wyniki pozostają w ignorowanym `.tia-bridge/`; do Git trafi wyłącznie niniejszy raport i wpis `Zmiany.md`. Oceniono wpływ na `AGENTS.md`, `Instrukcja_ChatGPT.md`, ADR/specyfikacje i kontrakty. Bez zmiany zasad, architektury, semantyki lub progów dokumenty normatywne pozostają bez edycji; decyzję uzasadniono w `Zmiany.md`. `py -3 scripts/audit_quality.py`: PASS, 53 Markdown, 465 lokalnych linków, 0 błędów. Manifest ma 73/73 pliki zgodne z SHA, wszystkie trzy bundle pomiarowe przeszły walidację próbek i odtwarzalności agregatów; kluczowe SHA są podane wyżej. Staged diff zawiera wyłącznie ten raport i `Zmiany.md`; `git diff --cached --check`: PASS. Fetch przed commitem potwierdził `HEAD=origin/main`.
