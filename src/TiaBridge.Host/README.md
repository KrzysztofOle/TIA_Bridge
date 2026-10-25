# TiaBridge.Host — Storage, Scheduler i HTTP API B-02A–C

Proces ASP.NET Core .NET 10 z lokalnym API oraz współdzielonymi komponentami Storage/Scheduler. Wykorzystuje istniejące Identity, OperationLifecycle, RequestIdentity, ContractJson i DTO Core. Scheduler i supervisor wykonują jawne scenariusze Fake w osobnym procesie przez IPC; HTTP i osobny CLI udostępniają ten sam silnik; nie ma workera Siemens ani rzeczywistych operacji TIA. Przechowanie żądania nie jest autoryzacją jego wykonania.

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

Zatwierdzona konfiguracja musi pozostać pod lokalnym `%LOCALAPPDATA%\TIA_Bridge2`, poza repo i na stałym NTFS. Testy używają wyłącznie własnych UUID katalogów `dev\storage-tests`, nigdy domyślnego state ani istniejących baz. Repo na M:/exFAT jest miejscem kodu i ignorowanych raportów, nie magazynem SQLite.

Metody Storage nie uwierzytelniają callerów ani nie weryfikują grantów. Principal musi pochodzić z zaufanej warstwy API Host, nie z JSON. IStorageFaults jest wyłącznie wstrzykiwanym interfejsem testowym; normalne użycie wybiera NoStorageFaults. Zakończenie testowego procesu nie implikuje możliwości przerwania Siemens.

Pełna walidacja: [tests/README.md](../../tests/README.md). OFFLINE jest odrębne od ENGINEERING/RUNTIME NOT_RUN. B-02B implementuje owner/epoch/fence, watchdog, retry/breaker, cancel i quarantine na Fake. [Scheduler/IPC/recovery](../../docs/14_SCHEDULER_IPC.md) opisuje zakres oraz testy; B-02C dodaje [HTTP API i CLI](../../docs/15_HTTP_API_CLI.md); pełna bramka B-02D i X-01 nadal nie jest zamknięta.

`Scheduling/OperationScheduler.cs` pracuje na tym samym store/outbox; `Operations/ExecutionStorage.cs` dodaje atomowy dispatch/cancel i trwałą historię prób. `Supervision/WorkerSupervisor.cs` jest implementacją wyłącznie dla własnego Fake Worker. Brak jawnego IExecutionProfile oznacza odmowę wykonania; profile syntetyczne nie nadają praw TIA. Start rozpoczyna pętlę w tle, a status pozostaje odczytem DB niezależnym od IPC. Shutdown, timeout lub heartbeat nie dowodzą zatrzymania Siemens.

`Api/HostApi.cs` binduje wyłącznie 127.0.0.1, publikuje token w chronionym discovery, wymaga bearer/SID ownership, sprawdza Origin/Host i limity. `Operations/ApiStorage.cs` korzysta z tego samego journalu/outbox także dla szybkich metod metadata. Token nie jest grantem TIA. Uruchomienie, przykład konfiguracji, pełny cykl CODEX i ograniczenia: [dokument 15](../../docs/15_HTTP_API_CLI.md).
