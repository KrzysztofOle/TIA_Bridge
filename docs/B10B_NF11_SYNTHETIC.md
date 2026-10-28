# B-10B-A1 — syntetyczna faza NF-11

Status: harness implementacyjny offline, 2026-10-28. Ten dokument opisuje wyłącznie syntetyczną część NF-11 opartą na istniejącym Host/FakeWorker. Nie wykonuje Siemens Attach i nie implementuje wymaganej osobno fazy 100 operacji live.

## Architektura i uruchamianie

Test `NF11_synthetic_soak_is_opt_in_and_persists_full_or_partial_evidence` w [ResiliencePerformanceTests.cs](../tests/integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs) używa istniejącego `ApiFixture`, Host, SQLite oraz FakeWorker. Nie dodaje drugiego schedulera ani silnika operacji. Kategoria `NF11Soak` wyłącza ten długi test ze standardowego zestawu.

Runner v1 ma jawny, pojedynczy tryb NF-11 syntetycznego:

```powershell
py -3 -B scripts/validate_offline.py --nf11-synthetic --nf11-mode DIAGNOSTIC
py -3 -B scripts/validate_offline.py --nf11-synthetic --nf11-mode FULL
```

`DIAGNOSTIC` wykonuje krótką próbę plumbing i nigdy nie przyznaje `NF11_SYNTHETIC: PASS`. `FULL` uruchamia co najmniej 28 800 sekund rzeczywistego pomiaru i co najmniej 1000 zmierzonych operacji. Żadne z tych poleceń nie uruchamia Siemens. Profil preflightu domyślnie pozostaje `STRICT`; wybór profilu nie zmienia kryteriów NF-11.

Standardowy `scripts/validate_offline.py` wyklucza kategorię `NF11Soak`, dlatego FAST/STANDARD nie uruchamiają ośmiogodzinnego przebiegu. Runner tworzy run ID, wykonuje istniejący read-only preflight, restore z pustego lokalnego feedu i build Release, a następnie uruchamia wyłącznie opt-in scenariusz. Lock `.tia-bridge/resilience/nf11-synthetic.active.json` zapobiega równoległemu uruchomieniu. Pozostawiony lock blokuje start i wymaga ręcznego sprawdzenia; nie jest automatycznie usuwany.

## Okno, obciążenie i próbki

- 50 zakończonych syntetycznych operacji stanowi warmup, zgodnie z istniejącym scenariuszem 1001 operacji. Pomiar 8 h rozpoczyna się po warmupie.
- 1000 zmierzonych operacji jest równomiernie rozmieszczonych na całym mierzonym oknie; każda operacja przechodzi przez normalny submit/status flow Host/FakeWorker i musi zakończyć się `SUCCEEDED`. Łącznie run ma co najmniej 1050 operacji.
- Co sekundę próbnik zapisuje do JSONL próbkę CPU, RAM, uchwytów, wątków i liczników procesów. Interwał wynika z istniejącego 1-sekundowego próbkowania NF-08. Każdy rekord i operacja jest dopisywana oraz flushowana, aby przerwanie zachowało pełne rekordy poprzedzające przerwanie.
- Zachowywane są surowe CPU w ms dla Host/FakeWorker i znormalizowany CPU Host; nie istnieje dodatkowy próg CPU NF-11. Liczba procesów komputera jest obserwacją środowiska, a dokładne PID/start-time Host i zarejestrowanych FakeWorker wiążą cleanup z procesami tego testu.

Kryteria pozostają zgodne z [NF-11](02_REQUIREMENTS.md#mierzalne-wymagania-niefunkcjonalne): minimum 28 800 s, minimum 1000 operacji, zero utraconych `Operation ID`, zero niezarządzanych dzieci, wzrost RAM Host po warmupie ≤50 MiB i wzrost uchwytów Host ≤5%. Każda akceptacja i zakończenie ma osobny rekord; walidator porównuje ID z trwałą historią operacji oraz audit FakeWorker. Cleanup potwierdza zakończenie Host i każdego zarejestrowanego worker PID/start-time. Nie zamyka innych procesów.

## Dowody i raport

Pliki trafiają do `.tia-bridge/resilience/<run-id>/`:

- `nf11-synthetic-samples.jsonl` — wszystkie próbki 1-sekundowe.
- `nf11-synthetic-operations.jsonl` — osobne zdarzenia zaakceptowania i zakończenia każdej operacji.
- `nf11-synthetic-result.json` — czas, liczby, ID, baseline, wzrost zasobów, cleanup i stan wykonania.
- `nf11-synthetic-manifest.json` — rozmiar i SHA-256 powyższych plików.
- `validation/run-metadata.json`, `validation/build-metadata.json`, TRX i pełny log — run ID, Git/source fingerprint, SHA-256 buildów i wynik testu.
- `evidence-manifest.json` — końcowy SHA-256 całego katalogu runu, także raportu i artefaktów częściowych.

`report_resilience.py` i `performance_evidence.py` walidują integralność, kolejność próbek, unikalność/kompletność ID, kryteria i powiązanie run/build. Statusy są odrębne:

```text
NF11_SYNTHETIC: PASS | FAIL | BLOCKED | NOT_RUN
NF11_LIVE:      PASS | FAIL | BLOCKED | NOT_RUN
NF11_OVERALL:   PASS | FAIL | BLOCKED | NOT_RUN
```

Sam syntetyczny PASS nie daje PASS całości. Bez poprawnych dowodów wymaganych 100 operacji read-only live `NF11_LIVE=NOT_RUN` i `NF11_OVERALL=BLOCKED`. Istniejący B-02D `nf.NF-11` oraz bramka D-11 pozostają `NOT_RUN`; nowe pola `nf11` nie zmieniają ich semantyki ani wcześniejszych raportów.

DIAGNOSTIC, kontrolowane przerwanie lub niekompletne dane nie mogą uzyskać syntetycznego PASS. Ctrl+C runnera tworzy jednorazowy `stop.request`; test kończy bezpieczną granicę, zamyka wyłącznie własny Host/FakeWorker i zapisuje częściowe dowody. Przerwanie daje `BLOCKED`; naruszenie zmierzonego limitu daje `FAIL`. Awaria procesu może pozostawić tylko strumienie JSONL i końcowy manifest runu; brak kompletnego manifestu fazy daje BLOCKED.

## Ograniczenia i przyszła faza live

Syntetyczny Host/FakeWorker nie kwalifikuje Siemens, nie wykonuje Attach i nie jest pełnym NF-11. Osobny etap musi dostarczyć 100 operacji read-only live z własną autoryzacją i dowodami izolacji/recipient/build. Dopiero niezależna walidacja obu faz może zwrócić `NF11_OVERALL: PASS`. W tym pakiecie nie dodano launchera VS Code ani mechanizmu tworzącego uprawnienia live.
