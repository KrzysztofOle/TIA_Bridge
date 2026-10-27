# TiaBridge.Host — Storage, Scheduler i HTTP API B-02A–C

Proces ASP.NET Core .NET 10 z lokalnym API oraz współdzielonymi komponentami Storage/Scheduler. Wykorzystuje istniejące Identity, OperationLifecycle, RequestIdentity, ContractJson i DTO Core. Scheduler i supervisor wykonują jawne scenariusze Fake w osobnym procesie przez IPC; HTTP i osobny CLI udostępniają ten sam silnik; X-01 dodaje osobny eksperymentalny worker V19 read-only, bez ładowania Siemens w Host. Przechowanie żądania nie jest autoryzacją jego wykonania.

B-03A rozszerza projekcję `project.inspect` o immutable Discovery snapshot z ograniczonym grafem Device/DeviceItem i coverage. Szczegóły pozostają w artefakcie; publiczny Inventory nie otrzymuje fikcyjnych ObjectId. Nowa projekcja nie jest świeżą kwalifikacją Engineering. Zobacz [raport B-03A](../../docs/B03A_DISCOVERY_QUALIFICATION.md).

- `Operations/SqliteOperationStore.cs`: IOperationRepository i IOutboxRepository; przyjęcie z durable idempotency, dziennik, CAS rewizji, zachowanie wyników oraz jednokrotne przejęcie outbox. Metody są synchronicznym, ograniczonym interfejsem Storage; handler API korzysta z nich bez oczekiwania na wykonanie workera.
- `Storage/SQLite/SqliteDatabase.cs`: osobne połączenie per write i opcjonalne bounded readers metadanych B-02C, lokalny writer gate i SQLite BEGIN IMMEDIATE pomiędzy procesami, WAL, synchronous=FULL, foreign_keys=ON, private cache, pooling=false i timeout blokady 5 s. Bounded oczekiwanie SQLite na lock nie jest retry zlecenia ani efektu Siemens. Zasady transakcji i błędów: [Microsoft transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [database errors](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors).
- `Storage/StorageLocation.cs`: odmowa niezatwierdzonej lokalizacji, obcego katalogu, repozytorium, UNC/SMB, non-NTFS i reparse points.
- `Storage/Artifacts/FileArtifactStore.cs`: temp, Flush(true), SHA-256, publikacja bez overwrite, readonly, referencja DB; weryfikacja i retencja orphanów bez GC.
- `Storage/Migrations`: trzy wersjonowane migracje z ledgerem hashy i kontrolą integralności. [Szczegóły schematu, workflow i testów](../../docs/13_DURABLE_STORAGE.md).

Provider [Microsoft.Data.Sqlite 10.0.13](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.13) jest przypięty wraz z zależnościami w packages.lock.json. Native SQLite z NuGet pozostaje w cache/bin, nie w Git. Core nie otrzymał zależności SQLite ani Host. Baza i artefakty nie są zasobami osadzonymi biblioteki.

Domyślna lokalizacja to `%LOCALAPPDATA%\TIA_Bridge2\state`. Konstrukcja wymaga jawnego StorageLocation.Open; przykład poniżej opisuje warstwę Storage używaną przez Host i nie jest uruchamiany przez build/testy:

```csharp
var location = StorageLocation.Open(StorageLocation.DefaultPath, repositoryRoot);
IOperationRepository store = new SqliteOperationStore(location);
// RegisterProject only registers a Core key. It does not establish a live TIA binding or grant.
store.RegisterProject(projectKey);
Admission accepted = store.Accept(authenticatedPrincipal, requestUtf8);
```

Zatwierdzona konfiguracja musi pozostać pod lokalnym `%LOCALAPPDATA%\TIA_Bridge2`, poza repo i na stałym NTFS. Testy używają wyłącznie własnych UUID katalogów `dev\storage-tests`, nigdy domyślnego state ani istniejących baz. Repozytorium jest miejscem kodu i ignorowanych raportów, nie magazynem SQLite.

Metody Storage nie uwierzytelniają callerów ani nie weryfikują grantów. Principal musi pochodzić z zaufanej warstwy API Host, nie z JSON. IStorageFaults jest wyłącznie wstrzykiwanym interfejsem testowym; normalne użycie wybiera NoStorageFaults. Zakończenie testowego procesu nie implikuje możliwości przerwania Siemens.

Pełna walidacja: [tests/README.md](../../tests/README.md). OFFLINE jest odrębne od ENGINEERING/RUNTIME NOT_RUN. B-02B implementuje owner/epoch/fence, watchdog, retry/breaker, cancel i quarantine na Fake. [Scheduler/IPC/recovery](../../docs/14_SCHEDULER_IPC.md) opisuje zakres oraz testy; B-02C dodaje [HTTP API i CLI](../../docs/15_HTTP_API_CLI.md); wyniki B-02D/D-11 oraz osobny X-01 są raportowane niezależnie od Runtime i produkcji.

`Scheduling/OperationScheduler.cs` pracuje na tym samym store/outbox; `Operations/ExecutionStorage.cs` dodaje atomowy dispatch/cancel i trwałą historię prób. `Supervision/WorkerSupervisor.cs` obsługuje własny Fake Worker oraz jawnie skonfigurowany V19 read-only przez ten sam interfejs. Brak jawnego IExecutionProfile oznacza odmowę wykonania; profile syntetyczne nie nadają praw TIA. Start rozpoczyna pętlę w tle, a status pozostaje odczytem DB niezależnym od IPC. Shutdown, timeout lub heartbeat nie dowodzą zatrzymania Siemens.

`Api/HostApi.cs` binduje wyłącznie 127.0.0.1, publikuje token w chronionym discovery, wymaga bearer/SID ownership, sprawdza Origin/Host i limity. `Operations/ApiStorage.cs` korzysta z tego samego journalu/outbox także dla szybkich metod metadata. Token nie jest grantem TIA. Uruchomienie, przykład konfiguracji, pełny cykl CODEX i ograniczenia: [dokument 15](../../docs/15_HTTP_API_CLI.md).

## E1 Local Trusted v1

[Controlled Qualification Bootstrap](../../docs/19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md)
jest osobnym, jawnie skonfigurowanym trybem qualification-only dla B1. Domyślnie
wyłączony; lokalny operatorowy grant i trwały budżet 20 nie zastępują E1 w normalnym
trybie. Nie ma samodzielnego aktywowania go przez HTTP ani automatycznego enrollment.

Host korzysta z istniejącego providera E1 rozszerzonego o rzeczywiste Windows ACL, jawne SID operatora/Host i trwały journal aktywacji/odwołań w tym samym `qualification-evidence`. Każda ocena i rewalidacja przed dispatch sprawdza bieżącą aktywność, dokładny podpis/revision/build/project/scope oraz hashe artefaktów. Klucz prywatny i komendy enrollment należą wyłącznie do osobnego lokalnego [narzędzia operatora](../TiaBridge.Qualification/README.md); nie ma endpointów administracyjnych HTTP ani automatycznej naprawy ACL. [Model](../../docs/17_TRUSTED_QUALIFICATION_EVIDENCE.md), [procedura](../../docs/18_LOCAL_TRUSTED_ENROLLMENT.md), [raport](../../docs/B03A_E1_L_LOCAL_TRUSTED.md). Mechanizm pozostaje domyślnie wyłączony bez anchorów, a rzeczywiste dowody Engineering nie zostały zarejestrowane. Grant Siemens i B-03A/E2 wymagają osobnego zadania.

## Profil X-01

`v19_worker_exe` i `v19` są zaufaną konfiguracją operatora, nigdy parametrem HTTP. [Instrukcja workera](../TiaBridge.Worker.V19/README.md) opisuje exact binding, lokalny NTFS, assembly resolution i zakres deny. Profil jest rozłączny z Fake i syntetycznymi mutacjami. Native dispatch nie ma szablonu sukcesu; dopiero rzeczywista odpowiedź po Dispose jest walidowana względem bindingu i zamieniana na istniejący ProjectInspectResult. Raw observation i częściowe Coverage są dwoma immutable artefaktami z kontrolą SHA-256. Brak pełnego modelu obiektów jest jawny, a coverage_refs zawiera wyłącznie identyfikatory zweryfikowanych artefaktów. Native read nie podlega automatycznemu retry. [Kwalifikacja](../../docs/X01_TIA_V19_QUALIFICATION.md) nie rozszerza tokenu API o mutacje.
#
