# B-02D — harness odporności bez Siemens

Harness używa istniejącego projektu Host.Tests i StorageProbe, rzeczywistych procesów Host/CLI/Fake oraz plikowej SQLite na własnym lokalnym NTFS. Nie powstaje drugi silnik operacji. [Dokumentacja i macierz RT/NF](../../docs/16_RESILIENCE_RECOVERY.md), [ostatnia kwalifikacja](QUALIFICATION.md), [maszynowy snapshot](qualification.json).

## Uruchomienie

Wymagania i jednorazowy cache: [tests/README.md](../README.md). Windows, .NET SDK/ASP.NET Core 10, Windows PowerShell do odczytu CPU/RAM/AV oraz QA Python z przypiętymi zależnościami. Nie instalujemy Siemens, nowych pakietów ani kont testowych.

```powershell
& "$env:LOCALAPPDATA/TIA_Bridge2/dev/qa-b01a-20261025/Scripts/python.exe" -B scripts/validate_offline.py
```

To pełny zestaw, z rzeczywistym pięciominutowym idle i 1001 operacjami. Projekty wykonują się kolejno (-m:1), profile HTTP są izolowane od innych startów procesów; osiem klientów jest równoczesnych wewnątrz testu. Nie skraca się deadline produkcyjnych ani progu NF-01. Pełny 8 h soak NF-11 nie jest wykonywany: jawna decyzja użytkownika pozostawia go w B-10B/R-14.

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

Test drugiego SID i rzeczywistego power-loss: NOT_RUN, brak zatwierdzonego środowiska. Uszkodzenie końcówki ostatniej transakcji wyniku WAL nie dowodzi odzyskania całkowicie zniszczonej bazy. Brak testów Engineering/Runtime. Formalny X-01 pozostaje osobnym zadaniem i wymaga rozstrzygnięcia bramki opisanej w dokumencie 16.
