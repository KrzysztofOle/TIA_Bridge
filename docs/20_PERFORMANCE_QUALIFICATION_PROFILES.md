# 20 — Profile wydajnościowe kwalifikacji offline

Status: kandydat implementacyjny D-11-P, 2026-10-27. `STRICT` zachowuje dotychczasowe progi i jest domyślny. `SHARED_WORKSTATION` jest jawnym profilem dla normalnie używanego komputera developerskiego z lokalnym NTFS. Wersja `1.0.0-candidate` nie zatwierdza zmiany bramki E2-B1 ani nie nadaje uprawnień Siemens.

## Profile i progi

Progi są wersjonowane w [`performance-profiles.json`](../tests/resilience/performance-profiles.json). Kod pomiarowy Host i walidator Python czytają tę samą definicję. Każdy nowy run zapisuje nazwę i wersję profilu, hash definicji, Git SHA, run ID, czas UTC, środowisko, wszystkie próbki, agregaty, progi, ich przekroczenia i hashe artefaktów. Percentyle używają nearest rank, bez interpolacji. Żaden pomiar nie jest odrzucany ani zastępowany wynikiem z innego uruchomienia.

| Wymaganie i mierzona wielkość | STRICT | SHARED_WORKSTATION (kandydat) | Klasyfikacja i uzasadnienie |
|---|---:|---:|---|
| NF-01 status/health, 1 i 8 klientów, p95 | ≤100 ms | ≤250 ms | KPI responsywności. Status pozostaje dostępny przy zawieszeniu workera; 250 ms nadal daje interaktywną odpowiedź. |
| NF-01 status/health, p99 | ≤250 ms | ≤500 ms | KPI ogona; pełne p95/p99/max są raportowane, żaden wolny odczyt nie jest usuwany. |
| NF-02 durable submit, 8 klientów, p95 | ≤250 ms | ≤500 ms | KPI czasu potwierdzenia po trwałym commicie intentu. Wyższy limit uwzględnia contention CPU/SQLite, ale nie pozwala pominąć commit/fsync. |
| NF-02 durable submit, p99 | ≤500 ms | ≤1000 ms | KPI ogona; zaakceptowane zlecenie nadal musi być utrwalone przed 202. Nie zmienia to timeoutów operacji. |
| NF-03 replace pliku 1 MiB, p95 | ≤100 ms | ≤500 ms | KPI niezależnej edycji Workspace. Dotychczasowy pomiar na NTFS osiągnął max 256,3424 ms; 500 ms daje około 2× zapasu względem zaobserwowanego maksimum. Atomowość, SHA-256 i zachowanie oryginału przy odmowie są bezwarunkowe. |
| NF-05 startup/history, p95 | ≤5000 ms | ≤10000 ms | KPI czasu startu. Podwojenie daje zapas na cold start i współdzielenie zasobów. Bezpieczny read po recovery ≤30 s pozostaje niezmienionym warunkiem funkcjonalnym. |
| NF-08 średni CPU procesu Host przy pustej kolejce przez 300 s | <1% | <2% | Budżet zasobów, nie bezczynność całego komputera. 1,0288% zakończyło historyczny STRICT FAIL; kandydat 2% zostawia mały margines dla schedulingu. CPU innych aplikacji jest rejestrowany, nie doliczany do CPU Host. |
| NF-08 RAM Host / Fake Worker | ≤200 / ≤250 MiB | ≤200 / ≤250 MiB | Limity zasobów bez zmian. Szczyt Host 512 MiB, worker soft alert 1 GiB i zachowanie przy wzroście są niezmienione. |
| Tylko harness: oczekiwanie testu na fazę `EXECUTING` Fake Worker | 10 s | 30 s | Czas obserwacji w jednym teście synchronizującym proces, nie operation/queue/IPC/native timeout. Ostatni shared run wykazał, że 10 s bywa zbyt krótkie dla process scheduling; awaria asercji nie dowodzi złamania safety property. Pozostałe asercje i jeden dispatch pozostają identyczne. |

NF-03 p95 500 ms jest kandydatem, nie wnioskiem o bieżącym PASS. p99, maximum i liczba przekroczeń są zachowane, by wykryć długie ogony. Limit p95 nie jest dopuszczeniem do delete+copy, retry, osłabienia flush, wyłączenia AV ani nadpisania pliku przy braku bezpiecznego replace. Dla tych przypadków test nadal wymaga poprawności i zachowania starej wersji.

### Historyczne porównanie — bez przeliczania wyników

| Historyczny pomiar (osobny run) | Wynik źródłowy | STRICT | SHARED_WORKSTATION | Interpretacja |
|---|---|---|---|---|
| NF-01 status/1: trzy serie diagnostyczne | p95 7,0959–7,9199 ms; p99 9,2785–15,7809 ms; max 16,1287–65,7577 ms | historyczne PASS | spełnia liczbowo kandydat | Nie zastępuje przyszłego pełnego runu. |
| NF-01 status/8: trzy serie diagnostyczne | p95 16,3749–17,8459 ms; p99 22,7935–26,1906 ms; max 32,2510–48,5796 ms | historyczne PASS | spełnia liczbowo kandydat | Nie mieszać z innymi runami. |
| NF-02 submit/8, D-11 requalification | N=64; p50/p95/p99/max 125,5562/152,1215/194,5564/194,5564 ms | historyczne PASS | spełnia liczbowo kandydat | Pomiar pozostaje przypisany do STRICT i pierwotnego SHA. |
| NF-02 submit/8, diagnostyka | p95 211,9023 / 205,8176 / 232,9689 ms; p99 250,7162 / 262,2369 / 276,4433 ms | serie celowane PASS; wcześniejsze p95 298,0946 ms FAIL | wszystkie wymienione liczby poniżej kandydata | Zmienność nadal niewyjaśniona; poszerzony próg nie usuwa wcześniejszego FAIL. |
| NF-03 replace X:/NTFS, diagnostyka | N=100; p95 115,8212 ms, p99 200,9893 ms, max 256,3424 ms: FAIL STRICT | historyczny FAIL | spełniałby sam kandydacki p95, gdyby powstał w nowym runie | Dawnego FAIL nie zmieniać ani nie przepisywać. |
| NF-03 NTFS przy Fake Hang, D-11 requalification | N=100; p95 2,9710 ms, p99 15,5739 ms, max 35,4445 ms | historyczny PASS | spełnia liczbowo kandydat | Oddzielna lokalizacja/scenariusz; nie scalać z replace kontrolnym. |
| NF-08 idle CPU Host | 1,0288% przez 300 s: FAIL STRICT | historyczny FAIL | sam pomiar przekroczyłby <2% | Nie zmieniać statusu historycznego runu. |
| NF-05 startup/history | N=20, p95 935,5389 ms; safe read 850,4761 ms | historyczny PASS | spełnia liczbowo kandydata | Safe read ≤30 s pozostaje bez zmian. |

Źródła liczb: [`NF03_NF02_DIAGNOSTICS_2026-10-26.md`](../tests/resilience/NF03_NF02_DIAGNOSTICS_2026-10-26.md), [`X01_NTFS_REQUALIFICATION_2026-10-26.md`](../tests/resilience/X01_NTFS_REQUALIFICATION_2026-10-26.md) i [`D11_REQUALIFICATION_2026-10-26_31b2e401.md`](../tests/resilience/D11_REQUALIFICATION_2026-10-26_31b2e401.md). Raporty są archiwalne i pozostają niezmienione.

## Klasy wymagań

### A. Bezpieczeństwo, poprawność i integralność — zawsze PASS

Profile wydajnościowe nie zmieniają oczekiwań dla integralności plików/artefaktów, SHA-256, durable intent i historii, idempotencji, epoch/fence, restart/recovery, deny-by-default, bindingu PID/start/path, `IsModified`, autoryzacji, UNKNOWN/quarantine, braku nieuprawnionego dispatch oraz podpisu/hashy dowodów kwalifikacyjnych. Każdy obowiązkowy przypadek tych klas musi mieć PASS. Profile nie mogą przekształcić awarii integralności w odchylenie KPI.

### B. KPI i budżety zasobów — profilowe tylko w jawnie wskazanych miejscach

NF-01, NF-02, NF-03, startup NF-05 i średni idle CPU NF-08 mają różne progi dla dwóch profili. Limity RAM NF-08 pozostają takie same. Pozostałe NF mierzone czasem, I/O, rozmiarem lub liczbą rekordów należy klasyfikować w ich własnym kontrakcie; nowego profilu nie wolno interpretować jako automatycznego mnożnika. NF-11 minimum 8 h pozostaje poza D-11 i `NOT_RUN` nie staje się PASS.

### C. Limity ochronne i operacyjne — nie są wydłużane przez profil

Kolejka NF-06: domyślnie 300 s, brak oczekiwania nieskończonego; wait timeout, heartbeat 2 s / utrata NF-04 do 10 s, worker loss, IPC/native call deadline, lease 15 s, NF-07 budżety ExclusiveAccess, limit kolejki 1000, cancel i maksymalny czas izolacji pozostają niezmienione. `safe_read_after_recovery ≤30 s` też pozostaje niezmienione. Dłuższe oczekiwanie testu na `EXECUTING` w tabeli wyżej nie przechodzi do aplikacji ani kontraktu; runner zapisuje dokładną wersję/hash polityki. Timeout nie dowodzi zakończenia native call; UNKNOWN nadal wymaga quarantine/reconciliation. Każdą przyszłą zmianę ochronną trzeba uzasadnić analizą bezpieczeństwa i recovery, niezależnie od profilu KPI.

## Preflight i `PASS_WITH_LOAD`

Runner wykonuje jeden read-only preflight przed testami. Sprawdza stały lokalny NTFS repo i state, dostępność ścieżek, minimum 2 GiB wolnego miejsca, brak reparse point, .NET 10, kolidującego procesu TIA_Bridge oraz próbuje wykryć istniejące procesy Host/Worker/Fake. Kontrole integralności runnera i plików źródłowych, build SHA oraz przyrostowy manifest runu są wykonywane w pipeline. Nie uruchamia TIA ani nie dotyka otwartych projektów.

Przez 10 s zbierane są obserwacje obciążenia CPU i kolejki dysku. Średnie CPU ≥80% lub kolejka dysku ≥2 daje `PASS_WITH_LOAD`, jeżeli wymagania bezpieczeństwa i dostępności są spełnione. Typowa praca w VS Code, przeglądarce, aplikacjach biurowych, narzędziach inżynierskich oraz aktywność AV nie blokują runu; ich obserwacje są zapisywane. `BLOCKED` wymaga konkretnego problemu: nie-NTFS/ścieżka niebezpieczna, brak miejsca/runtime, kolidujący Host/Worker, albo utrzymujące się ≥98% CPU równocześnie z mniej niż 1 GiB dostępnego RAM. Ostatni warunek nie jest zwykłym obciążeniem CPU i opisuje ryzyko, że testy procesowe nie wykonają się wiarygodnie. Brak możliwości potwierdzenia istotnego warunku daje BLOCKED z przyczyną, nie PASS.

`PASS_WITH_LOAD` pozostaje etykietą warunków, nie PASS wydajności. Testy i KPI są oceniane normalnie według wybranego profilu. Zachowujemy wszystkie próbki, p50/p95/p99/max, N, przekroczenia i opis równoczesnych warunków; nie wybieramy najlepszego spośród uruchomień. Jeden kanoniczny run jest jednym dowodem. Powtórzenie wymaga nowego run ID i pozostawia poprzedni wynik.

Wykonanie:

```powershell
& "$env:LOCALAPPDATA\TIA_Bridge2\dev\qa-b01a-20261025\Scripts\python.exe" -B scripts/validate_offline.py --profile SHARED_WORKSTATION
```

Bez `--profile` wybierany jest `STRICT`. Profile nie wpływają na API/IPC, kontrakty, grant qualification-only, E1, capability ani rzeczywisty worker.

## E2-B1 — propozycja do zatwierdzenia, nieaktywna

Ta implementacja nie zmienia normatywnego warunku wejścia E2-B1. Do decyzji użytkownika przedstawiam następujący warunek: (1) PASS wszystkich obowiązkowych testów bezpieczeństwa/poprawności; (2) jeden świeży run `SHARED_WORKSTATION` z preflight `PASS` lub `PASS_WITH_LOAD` i PASS dla wszystkich wymaganych KPI; (3) zatwierdzony i dokładnie ograniczony bootstrap qualification-only; (4) exact binding projektu, PID/start procesu, executable/build, planu i okna; (5) osobna, jawna zgoda na live Engineering; (6) niezależny odczyt `IsModified=false` przed i po dozwolonym read-only cyklu. Każda zmiana bindingu lub dirty state blokuje wykonanie.

Kolejność projektów, po osobnym zatwierdzeniu bramki i live zakresu: `T05_RecipeSim_V19_Testowy`, następnie `T04_T05-000_Testowy`. Profil nie zapewnia identity, capability, permission, enrollment ani zgody na Attach. Dotychczas zatwierdzona ograniczona bramka D-11 i jej warunek idle pozostają obowiązujące do chwili wyraźnej decyzji użytkownika; żaden wynik SHARED nie uruchamia E2-B1 samoczynnie.

## Ponowna ocena

Przed kolejnym przeglądem progów zbierz minimum trzy niezależne, pełne runy rozłożone na zwykłe okresy pracy; zachowaj także FAIL/partial i profile/env per run. To rekomendacja do oceny stabilności, nie dodatkowy warunek bieżącego profilu ani zasada wybierania najlepszego wyniku. Przeanalizuj rozkłady, p99/max, proporcję przekroczeń, różne obciążenia, filesystem i bezpieczeństwo atomowego replace. Nie przypisuj przyczynowości AV/CPU bez pomiaru. Kandydat może zostać zatwierdzony lub zmieniony wyłącznie przez nową wersję definicji; stare manifesty, SHA i kwalifikacje pozostają takie, jak zapisano.
