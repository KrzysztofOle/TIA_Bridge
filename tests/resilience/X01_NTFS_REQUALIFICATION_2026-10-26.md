# X-01 — końcowa kwalifikacja offline profilu NTFS

Data: 2026-10-26. Repozytorium: `X:\Projects\TIA_Bridge`, filesystem `X:` = lokalny, stały NTFS. Checkout wejściowy był czysty: `HEAD=origin/main=e6b659708c6141ad89be4ea109a64a8e76109a3d`; commit implementacji X-01: `9e8b7bfadb1f4e7b0fc3a793d01cbb82f54e5c07`. Runner: `.tia-bridge/offline/run.json`, run `f116dd23c7aa480995b5a69077f4db35`; fingerprint źródeł LF `f4bd013d908d7f77a777f5741c77f402046cd9c8b459be07d13d795d9cb55ea7` (139 plików). Pełne, surowe wyniki pozostają lokalnie w ignorowanym `.tia-bridge/`; ich wybrane SHA-256 są w [raporcie maszynowym](x01-ntfs-requalification-2026-10-26.json).

```text
BUILD RELEASE: PASS
B-02 OFFLINE TEST EXECUTION: FAIL
B-02 OFFLINE QUALIFICATION — CURRENT NTFS: FAIL
HISTORICAL exFAT: FAIL (retained)
X-01 LIMITED D-11: PASS
X-01 ENGINEERING: PASS (93 historical PASS; not rerun)
RUNTIME: NOT_RUN
NF-11: NOT_RUN
WORKER HANDLE RISK: UNKNOWN
B-03A READINESS: READY (bounded read-only package only)
```

## Pełny runner i bieżący profil NTFS

Wykonano pełne `scripts/validate_offline.py` z odizolowanego QA Python, nie zestaw ukierunkowany. Runner objął locked restore, build Release, wszystkie testy .NET i Python, audyt granicy Core, resilience, oba generatory, audyt jakości dokumentacji, dotnet format oraz `git diff --check`. Wszystkie kroki poza .NET zakończyły się kodem 0. Build Release: 0 ostrzeżeń, 0 błędów.

| Zakres | Wynik |
|---|---|
| .NET | 772 PASS / 1 FAIL / 0 SKIP (773); 73 storage, 76 scheduler/IPC, 38 HTTP/CLI, 48 resilience, 538 Core |
| Python | 31/31 PASS / 0 SKIP |
| Generatory kontraktów i wektorów | PASS |
| Audyt jakości, format, diff | PASS |
| FAIL | `ResiliencePerformanceTests.Five_minute_idle_resource_profile_is_measured_without_shortening_the_NF08_window`: idle CPU `1.028798887705612%`, próg asercji NF-08 `1.0%` |

To nie jest PASS pełnej kwalifikacji bieżącego profilu. Nie zmieniono progu. Błąd nie jest błędem wykonania lub izolacji procesów; pomiar trwał `306.4101049 s`, Host/Fake osiągnął maksymalnie odpowiednio `92.13/37.87 MiB`, a uchwyty Fake wyniosły `467` na początku, `458` na końcu i `476` maksimum. NF-08 pozostaje FAIL, a jego docelowy zakres skali nadal nie jest zakwalifikowany.

NF-03 wykonano na rzeczywistym Workspace/repozytorium leżącym na X:/NTFS. Kontrolny profil repo bez workera: 100 próbek, p95 `4.5592 ms`; podczas Fake Hang: 100 próbek po 1 MiB, co 100 ms, p95 `6.5635 ms`, p99 `87.0439 ms`, maksimum `163.6967 ms`. Wszystkie próby zapisu/replace zakończyły się powodzeniem; odmówione próbki: 0. NF-01/02/03/04/05/06/12/14/15 mają PASS w zaimplementowanym zakresie syntetycznym. State i Workspace raportu wskazują NTFS. To nie kwalifikuje exFAT.

## Ograniczona bramka D-11

D-11 ocenia osobną, ograniczoną bramkę z 07/08; nie jest agregatem całego B-02. Wymagane RT-03/05/06/15/16 mają PASS na Fake. Każdy z wymaganych NF-01/02/03/04/05/06/12/14/15 ma pozytywny dowód na NTFS. Idle rzeczywiście trwał ponad 300 s, a scenariusz zakończył 1001 operacji PASS w 479.22 s. W wymaganych scenariuszach nie znaleziono krytycznego błędu wykonania lub izolacji.

Ograniczona D-11 ma PASS mimo FAIL pełnego B-02 offline: jedyny błąd testu dotyczy progu CPU NF-08, którego nie ma w zestawie NF wymaganym przez ograniczoną bramkę; sam wymagany czas idle został zmierzony. Nie pomniejsza to NF-08 FAIL ani ogólnego `x01_readiness=NOT_READY` zapisanego przez istniejący konserwatywny reporter, który propaguje każdy FAIL testu do readiness. Reporter i jego reguły nie zostały zmienione. Ta ocena stosuje literalny zakres zatwierdzonej D-11 i jest osobnym dowodem. NF-11 pozostaje NOT_RUN; D-11 nie zamienia go w PASS ani nie zamyka R-14.

Historyczny NF-03 exFAT FAIL, historyczne raporty oraz ich hashe pozostają niezmienione. Obecny PASS NF-03 dotyczy wyłącznie rzeczywistego workspace X:/NTFS. Pełna kwalifikacja produkcyjna jest nieukończona.

## Engineering V19 i zasoby Workera

Status Engineering pozostaje PASS na podstawie istniejącego raportu X-01: 93 PASS, 0 FAIL, 0 SKIP; oba projekty V19 miały rzeczywiste Attach/read/Dispose PASS. Nie wykonywano nowych testów Engineering ani żadnych operacji na TIA w tym zadaniu. Runtime pozostaje NOT_RUN.

Istniejące pomiary Worker V19 pokazały uchwyty STA `462→702` i MTA `456→602` w krótkich przebiegach odczytu. Dispose się powiódł, a wszystkie własne procesy zakończyły się; nie pozostały niekontrolowane Workery. Dowody nie rozstrzygają, czy wzrost wynika z odroczonego zwalniania GC/vendor, czy trwałej retencji. Nowy pełny run Fake pokazał stabilne uchwyty syntetycznego procesu, ale nie mówi nic o uchwytach Siemens. Ryzyko klasyfikuję jako `UNKNOWN`; nie deklaruję usunięcia wycieku.

B-03A można przygotować jako ograniczony pakiet read-only z jedną sesją i właścicielem wywołań, próbkowaniem uchwytów/pamięci przed i po Dispose oraz potwierdzeniem końca własnego procesu. Każdy wzrost bez plateau, nieudany Dispose, brak końca własnego procesu albo nieoczekiwany stan TIA zatrzymuje dalsze wywołania. To rekomendacja gotowości pakietu, nie rozpoczęcie go ani zezwolenie na test Engineering. Bezterminowa praca, reuse na dużą skalę i produkcja nadal są niekwalifikowane.

### Dalsza kwalifikacja zasobów przed rozszerzeniem zakresu

Jeżeli B-03A będzie utrzymywał Worker przez serię wywołań, osobne zadanie powinno objąć dwa zatwierdzone projekty testowe, po 20 read-only cykli na projekt, z pomiarem przed wywołaniem, po Dispose oraz po 30 s idle; oddzielnie raportować STA/MTA i pierwsze/ostatnie 10 cykli. Mierzyć uchwyty, private bytes, working set, wątki, CPU, PID/start, wynik Dispose i czas zakończenia własnego procesu. Bez wymuszonego GC, równoległych vendor calls, Compile, Save lub innych operacji TIA.

Proponowane kryterium eksperymentu: wszystkie Dispose i odłączenia własnych procesów potwierdzone; brak osieroconego procesu; końcowe uchwyty/pamięć stabilizują się bez dodatniego trendu w ostatnich 10 cyklach. Utrzymujący się wzrost oznacza BLOCKING dla ponownego użycia Worker i wymaga odrębnej analizy; nie należy go maskować recyklingiem albo GC przed zebraniem próbek. Kryterium jest propozycją kwalifikacji ograniczonego przebiegu, nie zmianą NF-08/NF-11 ani zatwierdzeniem testu live.

## Zakres i granice

Nie wykonano Import, Save, Compile w TIA, Download, RUN/STOP, Runtime ani modyfikacji projektu. Nie zamykano instancji TIA Portal, nie uruchamiano NF-11 ani długiego/nieograniczonego obciążenia. Nie zmieniano produkcyjnego Workera V19, kodu testów, progów ani reporterów. Nie rozpoczęto B-03A.

Porównano `AGENTS.md` i `Instrukcja_ChatGPT.md` sekcja po sekcji oraz właściwe ADR/specyfikacje. Dokumentacja tej oceny nie zmienia architektury, kontraktów, zakresu zgód ani progów; oba dokumenty instrukcji i ADR pozostają bez zmian. Szczegóły decyzji zapisano w `Zmiany.md`.
