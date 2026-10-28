# B-03A/E2-B1-J — IPC sender resource lifetime

Badanie OFFLINE z 2026-10-28. Historyczny B1 pozostaje BLOCKED;
Engineering live, B2 i NF-11 NOT_RUN. Nie uruchamiano Siemens enumeration,
Attach, grantu, enrollment ani operacji projektu. Nie zmieniono IPC 1.1.

## Integracja i zakres

Sprawdzono czyste worktree `TIA_Bridge` (`a509a650ef6276d2b152cb2452856d6973f6b1f8`)
i `TIA_Bridge_QA01` (`1d65568c82c46fcf73427791adc95433b55d990c`), gałęzie i origin
po fetch. Origin/main: `16916d4f392b639a7936494a1c85f899fa4f3e0a`.
Merge-base B1-I/QA-01 to dokładnie B1-I; QA-01 dodaje jeden commit, 32 pliki,
2333 wiersze. Utworzono `test/e2-b1-ipc-sender-lifetime` i osobny worktree
`X:\Projects\TIA_Bridge_B1J` od QA-01, zachowując pełną historię obu etapów.
Oryginalnych checkoutów, artefaktów i historycznych raportów nie modyfikowano.

Przeczytano rzeczywisty [worker-handles.json](../tests/qa/scenarios/worker-handles.json):
restore/build i filtr WorkerResourceTests/InfrastructureHandleTests. Uruchamia
syntetyczny net48 InfrastructureProbe i w jednym teście rzeczywisty Host/Fake;
sprawdza fences/epochs, heartbeat, cleanup, timeout i brak vendor references.
TRX dostarcza wynik funkcjonalny; nie jest macierzą fazowych eksperymentów
sendera, wariantów GC ani automatycznym raportem własności. Pozostaje bez zmian.

Nowy [scenariusz](../tests/qa/scenarios/ipc-resource-lifetime.json) korzysta
z niezmienionego QA Runner v1, jego adapterów build/test/python, logowania,
raportów summary.json/report.md/failures.json i manifestu SHA-256. Logika
specyficzna dla badania jest w [adapterze](../scripts/ipc_resource_lifetime.py)
i istniejących projektach testowych. Nie powstał drugi runner/scheduler/logger.

## Architektura i hipotezy

Rzeczywisty sender `FramedConnection` (net48) → NamedPipeClientStream → osobny
kontroler StorageProbe (net10), używający tego samego FramedConnection IPC 1.1.
Kontroler ponownie wykorzystuje ProbeSession B1-I, exact PID/start/nonce/epoch,
SID peer verification, HMAC, sequence, operation ID i fence. Polecenia harnessu
są w hash-verified syntetycznych artefaktach InspectDispatch, bez zmiany DTO.
Nie jest to kwalifikacja normalnego supervisora; jego zachowanie i scheduler/
fencing są osobno pokryte STANDARD. Harness nie zawiera vendor assemblies.

Hipotezy przed wykonaniem:

1. Natywne Eventy APM zapisu mogą przeżyć zakończony SendAsync.
2. Dziedziczony FlushAsync zleca zbędną dla pipe walidację Flush do ThreadPool;
   przyrost może wiązać się z inicjalizacją/retencją i finalizacją Thread CLR.
3. Heartbeat powtarza tę ścieżkę, lecz nie musi być jedynym źródłem wzrostu.
4. Sam normalny cleanup nie zastępuje finalizacji wewnętrznych obiektów CLR.

Zainstalowane metadane potwierdzają `System.IO.Stream` jako DeclaringType obu
metod WriteAsync(byte[],int,int,CancellationToken) i FlushAsync(CancellationToken).
To ważniejsze od założenia o nowoczesnym PipeStream. Źródło referencyjne Microsoft:
[Stream](https://github.com/microsoft/referencesource/blob/main/mscorlib/system/io/stream.cs)
pokazuje WriteAsync → BeginEndWriteAsync → BeginWrite/EndWrite oraz FlushAsync →
Task.Factory.StartNew(Flush). [PipeStream](https://github.com/microsoft/referencesource/blob/main/System.Core/System/IO/Pipes/PipeStream.cs)
alokuje ManualResetEvent w BeginReadCore/BeginWriteCore i zamyka w EndRead/EndWrite;
Flush jedynie sprawdza warunki zapisu, nie wykonuje FlushFileBuffers.
Źródło referencyjne nie zastępuje dowodu z uruchomionego builda.

[Thread](https://github.com/microsoft/referencesource/blob/main/mscorlib/system/threading/thread.cs)
dziedziczy CriticalFinalizerObject, a finalizer deleguje do InternalFinalize.
Dokumentacja [ThreadPool dla net48](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool?view=netframework-4.8)
potwierdza adaptacyjne tworzenie i wycofywanie wątków. Nie przypisujemy jednak
wszystkich Eventów do tej implementacji bez natywnych stacków alokacji.

## Mapa zasobów

| Zasób | Alokacja | Właściciel i lifetime | Cleanup | Ocena ryzyka |
|---|---|---|---|---|
| NamedPipeClientStream/SafePipeHandle | Worker Run, raz na sesję | framed + zewnętrzny using pipe | DisposeAsync → net48 extension → Dispose; using zabezpiecza wyjątki | Prawidłowy lifetime; zamknięcie pipe nie dowodzi quiescence Siemens |
| sendGate SemaphoreSlim | FramedConnection, raz | całe połączenie, WaitAsync per send | Release w finally; brak Dispose | Nie pobiera AvailableWaitHandle; nie wykazano natywnego uchwytu ani wycieku, bez spekulacyjnej poprawki |
| Prefix/JSON/MAC/Task | SendAsync, per ramka | aktywny send/continuations | HMAC w using; pozostałe GC | Prawidłowe alokacje managed; nie są dowodem Event leak |
| APM result/Overlapped/ManualResetEvent | runtime BeginReadCore/BeginWriteCore | jedno IO; reader ma też aktywne oczekiwanie | EndRead/EndWrite: wait/close i Overlapped.Free | Oddzielić aktywne IO od zakończonego; obserwacja pola bez AsyncWaitHandle |
| Flush task | Stream.FlushAsync, per ramka | kolejka ThreadPool do zakończenia Flush | ukończenie task; obiekt managed przez GC | Źródło dodatkowego scheduling; brak dowodu zasobu wymagającego Task.Dispose |
| Thread/ThreadPool i wewnętrzne Eventy CLR | runtime scheduling/IO/oczekiwanie | runtime i obiekty managed Thread | retirement/finalizacja według CLR | Partial ownership; histogram Event nie daje właściciela |
| Heartbeat Task.Delay | jeden loop 2 s, token sesji | sesja, jeden delay na iterację | stop.Cancel i await heartbeat | Nie tworzy jawnego wątku per heartbeat |
| CTS | Worker stop; Host deadline; harness fault case | sesja lub ograniczone wywołanie | using/Dispose, cancellation i await | Cancellation nie dowodzi zatrzymania vendor call; net48 bazowe IO nie gwarantuje mid-flight cancellation |
| Owner Thread/BlockingCollection | Worker Run, raz | sesja, wywołania serializowane | CompleteAdding/Join/Dispose | Thread obiekt CLR może finalizować później; S1–S4 nie mają ownera Siemens |
| TaskCompletionSource | Worker per przyjęty dispatch | owner → execution continuation | TrySetResult, await | Brak jawnego WaitHandle; completed/commands trzymane do końca sesji, max 1000 |
| Task.Run execution | Worker per dispatch | oczekuje done.Task i wysyła receipt | await w shutdown | Nie jest nowym dedykowanym wątkiem per operacja |
| Process/ProcessThread/token | peer verification, telemetry, parent check | ograniczony pomiar/sesja | using/finally/CloseHandle | Obserwator ma koszt; wrappery wątków Dispose |
| PSS snapshot/marker/buffer | wyłącznie harness, na checkpointach | pojedynczy self snapshot | finally: buffer/marker/snapshot free | PARTIAL dla niektórych typów; remote PSS zabroniony |
| WeakReference diagnostyczne | wyłącznie M controls | testowy proces | koniec procesu | Nie utrzymują Thread/Task; Thread.CurrentThread i wrapper zmieniają obserwowaną ścieżkę |
| Klucz/sekret, host reader/drains | sesja IPC | połączenie | klucz Array.Clear; reader/drains await; Process.Dispose | Brak zmian security/fencing/handshake |

W produkcyjnej ścieżce sendera nie znaleziono `EventWaitHandle`, jawnego
`WaitHandle`, dedykowanego Thread per send ani Timer per send. Task.Delay używa
infrastruktury runtime; nie zakładamy jednego natywnego timera na wiadomość.
`GetAwaiter().GetResult()` występuje w Main i na ownerze przy Started, a nie
w samej implementacji SendAsync. Nie dodano Dispose do SemaphoreSlim jedynie
na podstawie obecności IDisposable.

## Scenariusze i metryki

| Scenariusz | Przebieg |
|---|---|
| S1 ×3 | sender bez heartbeat, 5 warm-up + 500 wysłań burst, 35 s idle, Dispose/STOP |
| S2 ×3 | sender bez heartbeat, 5 warm-up + 60 wysłań w odstępach co najmniej 100 ms, 35 s idle |
| S3 ×3 | identyczne obciążenie jak S2, heartbeat co 2 s również podczas idle |
| S4 ×3 | identycznie jak S3, po zwykłym idle osobna para pre/post GC.Collect/WaitForPendingFinalizers/GC.Collect |
| S5 | pięć deterministycznych przypadków: sukces, write exception, token timeout, pre-cancel, cancel oczekującego send; ponadto real pipe loss, STOP i nowy proces/epoch |
| M-scheduled/M-inline | testowy forwarder do tego samego pipe, obserwacja wątków Flush; wariant inline jest wyłącznie kontrolą eksperymentalną |
| M-apm | nowa, osobna hipoteza: wrapper dziedziczy Stream.WriteAsync/ReadAsync i forwarduje Begin/End; odczyt już istniejącego pola _waitHandle, sprawdzenie close po End |

S1/S2 różnią pacingiem i wolumenem, więc ich porównanie nie izoluje jednego
czynnika. S2/S3 to para dopasowana; S3/S4 mają identyczne naturalne okno.
Kontrolowane GC wyłącznie w test assembly. Brak produkcyjnego forced GC,
restartów, zmiany timeoutów, trust, schedulera lub sposobu używania Siemens.

JSONL/CSV zawierają Run/Scenario/Step ID, Git/build SHA, PID/epoch, UTC i
Stopwatch ticks/frequency, HandleCount, ThreadCount, PrivateBytes, WorkingSet,
managed heap, GC0/1/2, attempted/completed sends, błędy, heartbeat i cleanup.
Warianty naturalne nie wywołują wymuszonego GC. Próbki cold/warm/active/idle/
shutdown są jawnie oznaczone. Idle telemetry jest lokalna: nie dodaje polling IPC.
Cold jest pierwszym checkpointem po Hello/Ready, a nie pomiarem przed startem
CLR lub Connect. Wczesny koszt uruchomienia nie jest osobno zmierzony.
Nie porównujemy różnych faz jako jednej stabilnej serii. PSS jest oddzielnym,
nieatomowym checkpointem związanym sequence; typy pomiędzy checkpointami
NOT_AVAILABLE. Nieznane typy pozostają UNAVAILABLE, brak metryki NOT_AVAILABLE,
a nie zero. Nie użyto arbitralnego progu uchwytów ani pojedynczej asercji GC.

Pełne dane są create-new w `%LOCALAPPDATA%\TIA_Bridge2\dev\e2-b1-j\<id>`
i kopiowane do artefaktów QA przed jego SHA-256 seal. Binarne SHA są sprawdzane
przed każdym scenariuszem. Wersja źródeł i dirty fingerprint są osobnym provenance.
Po STOP pozostają próbki oraz ended.json, a plik metryk da się otworzyć exclusive.

## Wyniki i interpretacja

Automatyczna macierz: **17/17 funkcjonalnie PASS**, 2507 zakonczonych
wyslan, 0 bledow; nowa hipoteza M-apm: 1/1 PASS, 91 wyslan, 0 bledow.
Lacznie 18 przebiegow i 2598 wyslan. S5: 15/15 deterministycznych przypadkow
cleanup w trzech procesach; real pipe loss i dwa STOP zakonczone Dispose.
Kazdy proces ma osobny PID/epoch, bez retry/restartu tego samego przypadku.

| Wariant | Delta warm -> idle w powtorzeniach | Mediana | Kontrolowane GC |
|---|---|---:|---|
| S1 | +20, +20, +15 | 20 | NOT_AVAILABLE, NOT_AVAILABLE, NOT_AVAILABLE |
| S2 | +20, +15, +20 | 20 | NOT_AVAILABLE, NOT_AVAILABLE, NOT_AVAILABLE |
| S3 | +10, +14, +14 | 14 | NOT_AVAILABLE, NOT_AVAILABLE, NOT_AVAILABLE |
| S4 | +15, +19, +25 | 19 | -10, -10, -10 |

| Kontrola | Warm | Idle | Pre GC | Post GC | Thread / Event GC delta |
|---|---:|---:|---:|---:|---|
| M-scheduled-1 | 329 | 344 | 344 | 334 | -2 / -8 |
| M-inline-1 | 324 | 354 | 354 | 344 | -2 / -8 |
| M-apm-1 | 324 | 338 | 339 | 334 | -1 / -4 |

W S4 spadki wyniosly 349 -> 339, 348 -> 338 i 354 -> 344: zawsze
-2 Thread i -8 Event. PSS wskazal exit_status=0 obu zwolnionych watkow.
We wszystkich pieciu kontrolach GC starego builda ten sam wzorzec -2/-8.
To potwierdza odroczone zwolnienie konkretnego podzbioru zasobow, nie dowodzi
stabilnosci calego procesu ani nie wyjasnia wzrostu historycznego B1.

**APM lifetime: CONFIRMED_CLOSED_AT_END.** M-apm zaobserwowal **489/489**
istniejacych wait handles zamknietych po EndRead/EndWrite; brakujace pole: 0.
Przyklady zawieraja typ PipeStreamAsyncResult, wartosc natywnego uchwytu,
kierunek i closed_after_end. Nie pobierano AsyncWaitHandle i nie alokowano
zdarzen przez obserwatora. Nie sa to 489 rownoczesne uchwyty: licznik sumuje
zakonczone IO, a jedna ramka moze wymagac kilku odczytow. Ten tor nie
potwierdza zaleznosci zakonczonych Eventow APM od finalizera.

W M-scheduled: 90 flush tasks przed GC, 2 slabe referencje jeszcze zywe, po GC 0.
Jednoczesny ThreadCount procesu wynosil 14 zarowno warm, jak i idle, podczas
gdy histogram uchwytow Thread wzrosl z 14 do 17. Uchwyty zakonczonych watkow
nie sa liczba aktualnie wykonujacych sie watkow. W M-apm naturalne okno active
mialo GC0/1/2 = 3/1/0 kolekcji, a idle-enter 1/0/0: wystapienie naturalnego GC
nie oznacza, ze wszystkie zasoby z finalizerami zostaly juz zwolnione.
PSS zwolnil watki 43904 i 37204; 37204 byl obserwowany jako ThreadPool/Receipt,
a jego slaba referencja Thread przeszla True -> False. W M-inline zwolnione
14872 i 39024 byly oba w mapie ThreadPool/Receipt i obie referencje przeszly
True -> False. Wlasnosc **3/4** zwolnionych Thread w tych dwoch kontrolach
powiazano z wykonujacymi sender/continuation watkami CLR. Nie wszystkie
zwolnione watki byly widziane przez forwarder. W M-apm GC zwolnil jeszcze
1 Thread i 4 Event (339 -> 334), lecz native ID 23748 nie byl w mapie forwardera.

Wariant inline nie usuwa przyrostu (+30 wobec +15 scheduled w pojedynczej
kontroli). Hipoteza, ze dziedziczony FlushAsync jest jedyna przyczyna, zostala
odrzucona. Nie ma podstaw do produkcyjnej zmiany flush. Zblizone przyrosty
S1/S2 i mniejsza mediana S3 nie dowodza liniowego leak-per-message ani
pogorszenia powodowanego samym heartbeat. Warm nie jest dowodem steady state.

**Ocena:** IPC FUNCTIONAL PASS; RESOURCE REVIEW_REQUIRED; DIAGNOSTIC
COMPLETENESS PARTIAL; THREAD OWNERSHIP PARTIAL; EVENT OWNERSHIP PARTIAL;
GC LIFETIME CONFIRMED dla zmierzonego podzbioru; ROOT CAUSE PROBABLE
dla retencji/finalizacji zasobow CLR, UNEXPLAINED dla pozostalej czesci wzrostu.
PRODUCTION FIX NO; BEFORE/AFTER NOT_APPLICABLE. Brak potwierdzonego bledu
w kodzie produkcyjnym wymagajacego poprawki. Brak zmian w vendor lifetime.

APM wait handles maja potwierdzonego wlasciciela i jawny close. Pozostale
Eventy nie maja stackow alokacji: histogram i proporcja czterech Eventow na
zwolniony Thread nie dowodza wspolnego wlasciciela. Najblizszy zidentyfikowany
tor to scheduling/continuations/Thread CLR oraz jego natywny InternalFinalize.
Ograniczenie PSS PARTIAL wynika z 84 uchwytow UNAVAILABLE w checkpointach;
Event i Thread byly rozpoznawane. Nie zmieniono ich statusu na pelne PASS.

**Rekomendacja:** zachowac produkcje i B1 BLOCKED. Jesli potrzebne bedzie
domkniecie wlasnosci pozostalych Eventow, brakujacym dowodem jest stack
alokacji i zamkniecia konkretnych wartosci Event wylacznie w syntetycznym PID,
skorelowany z retirement/finalizacja wskazanego native thread ID. Hipoteza
do sprawdzenia to natywny pakiet Eventow CLR towarzyszacy wycofanemu watkowi,
a nie kolejny ogolny pomiar HandleCount. Nie instalowano profilera/drivera,
nie podnoszono uprawnien ani nie wykonywano debug attach. Nie zaleca sie
forced GC, automatycznych restartow ani Task.Dispose bez dowodu wlasciciela.

Ograniczenia: trzy krotkie powtorzenia i 35 s idle nie kwalifikuja dlugiego
soak; kontrola wrappera zmienia instrumentation; GC/Windows sa niedeterministyczne;
wspolny sampler ma wlasny koszt ThreadPool. Nie ma dowodu udzialu mechanizmu
w calosci historycznego B1. Zgodnosc schedulera/fencing ocenia osobno STANDARD.

Dokladne artefakty (ignorowane, lokalne, objete manifestem):

- S1-S5: `.tia-bridge/qa-results/20261028T081653881943Z-7c5d57d86605496eb805a5d40f4b8cdd/report.md`.
- Analiza: `.tia-bridge/qa-results/20261028T081653881943Z-7c5d57d86605496eb805a5d40f4b8cdd/artifacts/ipc-resource-lifetime--ipc-resource-lifetime/lifetime-analysis.json`.
- APM: `.tia-bridge/qa-results/20261028T082759077634Z-2bdde3d036994b3eb7c794f0616adbc9/report.md`.
- Analiza APM: `.tia-bridge/qa-results/20261028T082759077634Z-2bdde3d036994b3eb7c794f0616adbc9/artifacts/ipc-allocation-ownership--ipc-allocation-ownership/lifetime-analysis.json`.

W kazdym run sa summary.json, failures.json, logi JSONL i pelny manifest SHA-256.
W kroku diagnostycznym sa tez lifetime-analysis.md, metrics.csv, trends.svg,
provenance.json oraz surowe evidence/<case>/metrics.jsonl i types-*.json.
Najpierw czytaj summary/failures i lifetime-analysis, potem wybrany checkpoint.
Oba przebiegi uruchomiono z Git HEAD 1d65568 i DIRTY; provenance zapisuje
dokladne hashe zrodel/buildow. Zmiana builda przed M-apm rozszerza tylko
testowa obserwacje APM; nie jest poprawka produkcyjna ani porownaniem fixu.

| Build | SHA-256 |
|---|---|
| InfrastructureProbe, macierz S1-S5 | `950b520b0e90660d983bbc83c989200167c48fc7f51384edb9d25fc6ef41d735` |
| InfrastructureProbe, kontrola APM | `5f1fc5db30bdeb818ec64dc3ae1dd9877b8a40cc9250d9e771b70cecc35020b4` |
| InfrastructureProbe, po formatterze | `800d81df5c7cadee0ce40c6512a3a8e8784ff2941cd8180e8b71e0f636fc41db` |
| StorageProbe, wszystkie trzy buildy | `a212d7246fff0762faa9d588e120813150ecc374e8ac19054fd84f07953e6a47` |
| IPC net48, wszystkie trzy buildy | `9f21308e6d0f6bda6961e8919af792ddd8b8ffe9941510cf59c423ca37838e90` |

Sprawdzono identyczny binarny SHA IPC przed i po dodaniu diagnostyki/formattera.
Brak diffu src, contracts i examples/ipc względem B1-I. Formatter zmienia hash
testowego EXE (metadane/source mapping), bez zmiany tokenów wykonawczych kodu.

## Uruchomienie i regresja

- Pierwszy FAST `20261028T081426613069Z-f58b4ecb4254444ba2390e752135d9f1`:
  build FAIL CS0518 (testowy record w net48 wymagal IsExternalInit).
  Zastapiono go zwykla klasa w harnessie; dowod niepowodzenia zachowany.
- FAST po tej korekcie: 615 PASS. Koncowy FAST po dodaniu szesciu testow
  analizy wynikow: **621 PASS / 0 FAIL / 0 SKIP**,
  `20261028T082927264610Z-db46d53eb7924633b42759024b16c660`.
- Scenariusz S1-S5: 4/4 regresje net48 i 17/17 eksperymentow PASS.
- Scenariusz nowej hipotezy APM: 4/4 rozszerzone regresje net48 i 1/1
  eksperyment PASS. Ten przebieg sprawdza nowa obserwacje, bez powtarzania
  calej macierzy 17 scenariuszy.
- `doctor` i `list`: PASS; CLI potwierdzone lokalnie. Pełna kwalifikacja
  i NF-11 nie byly uruchamiane.
- Pierwszy STANDARD `20261028T083221440314Z-7472a31236f64fd8a2789aaded932dec`:
  **1100 testow PASS** (564 Core, 408 Host, 71 contracts, 57 runner), audyt,
  generatory i diff PASS; profil **FAIL** przez wymagane formatowanie
  inicjalizatorow w nowym SenderLifetimeProbe.cs. Zastosowano formatter tylko
  do tego pliku, bez zmiany tokenow kodu. Historyczny wynik pozostaje FAIL.
  Macierzy zasobowej nie powtarzano po samej zmianie whitespace.
- Wąska kontrola po formatterze: format/quality/diff PASS,
  `20261028T084329964426Z-4a9f0a74f62f4c9da95844006ff8ebd8`.
  Osobna wcześniejsza próba umieszczenia jej scenariusza poza scenario_root
  została poprawnie odrzucona przed wykonaniem (BLOCKED, zero testów):
  `20261028T084259789831Z-0999a4bdfbc842199632b3b327e6e28b`.
  Zachowano dowód; zatwierdzony scenariusz jest teraz w tests/qa/scenarios.
  Kosztowna macierz i kontrola APM sprawdzają format przed eksperymentem.
- Końcowy STANDARD **PASS: 1100/1100**, zero FAIL/SKIP; Core 564, Host 408,
  contracts Python 71, runner/analiza 57. Build, generatory, quality, format
  i diff PASS: `20261028T084351254855Z-c548e8afdddd40338827f8ec282253f9`.
  Raport: `.tia-bridge/qa-results/20261028T084351254855Z-c548e8afdddd40338827f8ec282253f9/report.md`.
  Kwalifikacja środowiska, Engineering i Runtime pozostają NOT_RUN.

```powershell
.\qa.ps1 doctor
.\qa.ps1 list
.\qa.ps1 run fast
.\qa.ps1 run --scenario tests/qa/scenarios/ipc-resource-lifetime.json
.\qa.ps1 run --scenario tests/qa/scenarios/ipc-allocation-ownership.json
.\qa.ps1 run standard
.\qa.ps1 report --latest --failures-only
```

FAST służy implementacji; STANDARD obejmuje rzeczywiste offline testy
schedulera, restart/timeout/fencing i kontrakty IPC. QUALIFICATION nie jest
potrzebne do tego pakietu i nie uruchamia NF-11. Formalny qualification BLOCKED
nie jest awarią QA Runnera. PASS uruchomienia scenariusza oznacza funkcjonalne
wykonanie, nie stabilność zasobów, pełną kwalifikację lub Engineering.
