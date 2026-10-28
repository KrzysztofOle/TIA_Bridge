# B-02D — harness odporności bez Siemens

Harness używa istniejącego projektu Host.Tests i StorageProbe, rzeczywistych procesów Host/CLI/Fake oraz plikowej SQLite na własnym lokalnym NTFS. Nie powstaje drugi silnik operacji. [Dokumentacja i macierz RT/NF](../../docs/16_RESILIENCE_RECOVERY.md), [ostatnia kwalifikacja](QUALIFICATION.md), [maszynowy snapshot](qualification.json).

Decyzja D-11 (2026-10-26): [odrębna ocena ograniczonej bramki X-01](X01_LIMITED_GATE.md), [JSON decyzji](x01-limited-gate.json). Historyczne QUALIFICATION.md i qualification.json pozostają niezmienione, włącznie z OFFLINE/GATE FAIL, NF-03 exFAT FAIL i NF-11 NOT_RUN. Aktualna ograniczona bramka NTFS ma PASS na podstawie wskazanych dowodów; nie oznacza produkcji, kwalifikacji exFAT ani zgody na uruchomienie X-01. NF-11 ≥8 h pozostaje obowiązkowy w B-10B/R-14.

## Dowody NF-02/NF-03 od formatu 1.1

Nowe przebiegi używają wyłącznie `.tia-bridge/resilience/<run-id>/`: `performance/<test>.json` zawiera pełne próbki, ich ID, ms/wall_clock, N, odtwarzalne agregaty/SLO, UTC, Git SHA/dirty, SHA źródeł i binariów, runtime, filesystem i hash konfiguracji. Osobny `<test>.manifest.json` wiąże bajty przez SHA-256. Kolejność ID oznacza snapshot kolekcji; przy ośmiu klientach nie jest chronologią zakończeń żądań. Wszystkie wartości, także najwolniejsze i częściowe po wyjątku, pozostają zachowane. Progi, stoper, warm-up, interwały i N pozostają bez zmian. Status ciała testu w artefakcie uzupełnia pełny wynik TRX, obejmujący również teardown.

`validation/` zawiera metadata, środowisko, logi kroków, `run.json`, `resilience.json` i quality; `test-results/` zawiera TRX. Runner tworzy nowy UUID/root, a na końcu także przy FAIL zapisuje `evidence-manifest.json` z pełnym spisem plików i hashami. Zapisy dowodów używają create-new; powtórzenie ID nie nadpisuje danych. Nie tworzy się nowego wspólnego `b02c/performance.json`, `offline/run.json` ani `resilience/run.json`. Dawne pliki pozostają niezmienione.

[Walidator](../../scripts/performance_evidence.py) odrzuca brak/duplikaty, złe liczby i jednostki, NaN/Infinity/ujemne czasy, niezgodne percentyle/statusy, run/build/hash i uszkodzony JSON. Reporter porównuje też agregaty scenariuszy z pełnymi próbkami i nie pobiera współdzielonego performance.json. Historyczny format bez próbek jest dostępny wyłącznie przez jawne `qualification(..., allow_legacy=True)` do analizy archiwalnej: `sample_evidence_status=INSUFFICIENT`, bez nowego gate PASS. Odczyt nie uzupełnia starych dowodów. Pełne próbki z FAIL pozostają FAIL, a częściowe są INSUFFICIENT. `NF-03.ntfs_save_replace_status` odzwierciedla teraz wynik właściwego testu zamiast stałej PASS; jego asercje nie zmieniły się.

Przygotowanie i aktualny preflight: [raport D-11 preparation](D11_PREPARATION_2026-10-26.md). Jego testy celowane nie są nową kwalifikacją D-11. Semantyka zatwierdzonej ograniczonej bramki i historyczna konserwatywna agregacja NF-08/NF-11 reportera pozostają rozdzielone jak w dokumencie 16.

## Uruchomienie

Wymagania i jednorazowy cache: [tests/README.md](../README.md). Windows, .NET SDK/ASP.NET Core 10, Windows PowerShell do odczytu CPU/RAM/AV oraz QA Python z przypiętymi zależnościami. Nie instalujemy Siemens, nowych pakietów ani kont testowych.

```powershell
& "$env:LOCALAPPDATA/TIA_Bridge2/dev/qa-b01a-20261025/Scripts/python.exe" -B scripts/validate_offline.py
```

Domyślny profil to `STRICT`. Aby wykonać jawnie nową kwalifikację offline na
współdzielonym stanowisku, dopisz `--profile SHARED_WORKSTATION`. Nie wybieraj
profilu na podstawie wyników poprzedniego runu. Preflight `PASS_WITH_LOAD`
kontynuuje pomiary, a wszystkie obserwacje obciążenia pozostają w nowym UUID.
Profile i ich wersjonowane progi opisuje [D-11-P](../../docs/20_PERFORMANCE_QUALIFICATION_PROFILES.md).

To pełny zestaw, z rzeczywistym pięciominutowym idle i 1001 operacjami. Projekty wykonują się kolejno (-m:1), profile HTTP są izolowane od innych startów procesów; osiem klientów jest równoczesnych wewnątrz testu. Nie skraca się deadline produkcyjnych ani progu NF-01. Pełny 8 h soak NF-11 nie jest wykonywany: jawna decyzja użytkownika pozostawia go w B-10B/R-14.

Syntetyczny harness NF-11 z pakietu B-10B-A1 jest oddzielnym, opt-in testem Host/FakeWorker; użyj [jego runbooka](../../docs/B10B_NF11_SYNTHETIC.md). Standardowy runner wyklucza kategorię `NF11Soak`. `DIAGNOSTIC` nie kwalifikuje fazy, a `FULL` wymaga realnych 28 800 s. Nie uruchamiaj `FULL` w FAST/STANDARD. Osobne 100 operacji read-only live pozostaje NOT_RUN, więc syntetyczny PASS daje `NF11_OVERALL: BLOCKED`.

Celowany przebieg po build:

```powershell
dotnet test tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Resilience
```

Przebieg celowany nie zastępuje pełnego runnera: nie ma wszystkich regresji, limitu kolejki i raportu kwalifikacji. Kod 0 pojedynczego testu kontroli filesystemu nie oznacza NF-03 PASS na niekwalifikowanym woluminie. Pełny runner raportuje osobno wykonanie testów, kwalifikację NF i formalną bramkę. NF failure powoduje exit 1, nawet przy samych zielonych xUnit; BLOCKED bramki pozostaje jawne w JSON.

## Dowody i kontrola awarii

Kod: [crash](../integration/TiaBridge.Host.Tests/Resilience/ResilienceCrashTests.cs), [safety](../integration/TiaBridge.Host.Tests/Resilience/ResilienceSafetyTests.cs), [IPC](../integration/TiaBridge.Host.Tests/Resilience/ResilienceSecurityTests.cs), [artefakty/WAL](../integration/TiaBridge.Host.Tests/Resilience/ResilienceArtifactTests.cs), [pomiary](../integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs). Wersjonowany [manifest przypadków](matrix.json) wymusza komplet dowodów; [reporter](../../scripts/report_resilience.py) odrzuca brakujące, stare, zduplikowane lub pominięte dane; jawne FAIL zachowuje w raporcie i blokuje bramkę i skrócony profil idle.

StorageProbe uruchamia dokładnie HostApi z wewnętrznym composition seam; produkcyjny Host nie przyjmuje fault flags z HTTP/config/argv. Bariery profile/before-dispatch/dispatch-intent/dispatch-sent/receipt-received mają reached/release i deadline. Environment.Exit(86) pomija finally. Faulty journal/outbox/disk-full są wstrzykiwane w istniejący IStorageFaults. WorkerReceiptCommitted dodaje obserwację po atomowym zapisie receipt; nie zmienia transakcji i nie wykonuje retry.

Fake zapisuje mały, flushowany audit syntetycznych efektów po jednym wpisie na rzeczywisty efekt i przed odpowiedzią. Audit nie jest ledgerem operacji i nie steruje recovery; źródłem lifecycle pozostaje SQLite. RT-05 porównuje jawne syntetyczne P/B po pierwszym potwierdzonym efekcie i odrzuca drugi zapis STALE_PLAN/0 effects. Nie jest to implementacja planera, readbacku TIA ani zgoda na rzeczywiste Apply.

RT-15 pozostawia własny testowy child/lock, wykrywa anomaly i quarantine, a harness zwalnia wyłącznie ten zasób po PID/start i własnym root. Drugi sentinel z tą samą nazwą procesu pozostaje żywy do teardown jego właściciela. Nie ma enumerowania i zabijania obcych procesów.

Raporty .tia-bridge/resilience/<run-id> zawierają scenario IDs, RT/NF, status, czas, PID/epoch, journal i hashe. Pełny run.json, środowisko CPU/RAM/AV, metryki i TRX są ignorowane przez Git. [Snapshot](qualification.json) zawiera tylko syntetyczne, zredagowane podsumowanie i hashe dowodów. Nie drukuj chronionego discovery, tokenów ani bootstrapu IPC. Surowe logi i DB nie trafiają do repo.

Test drugiego SID i rzeczywistego power-loss: NOT_RUN, brak zatwierdzonego środowiska. Uszkodzenie końcówki ostatniej transakcji wyniku WAL nie dowodzi odzyskania całkowicie zniszczonej bazy. Brak testów Engineering/Runtime. X-01 pozostaje osobnym zadaniem z odrębną autoryzacją i weryfikacją środowiska. Konserwatywne pola gate/x01_readiness reportera i missing_blocks_x01 z matrix.json opisują dawną agregację całego B-02D; aktualną interpretację zatwierdzonej D-11 zawiera osobny raport. Nie należy przepisywać dawnych pomiarów ani wnioskować PASS dla nowego przebiegu z tej oceny archiwalnych dowodów. [Kontrola zgodności dokumentacyjnej](../README.md#kontrola-dokumentacji) nie uruchamia procesów testowych.

## B-03A/E2-G — ograniczona bramka B1

Zatwierdzony SHARED_WORKSTATION 1.0.0 i [walidator E2-G](../../docs/21_E2_B1_READINESS_GATE.md)
oddzielają safety, ograniczoną D-11 i gotowość do autoryzacji operatora od pełnej
kwalifikacji BLOCKED/NF-11 NOT_RUN. READY nie aktywuje bootstrapu ani Siemens Attach.
Nowe shared runy wiążą także exact offline build Host/Worker; historyczne raporty
powyżej pozostają bez zmian. Pełny NF-08 NOT_RUN nie jest PASS zmierzonego scale.
