# TiaBridge.Core — B-01A

Biblioteka .NET 10, niezależna od Host i Siemens. [Kontrakty v1](../../contracts/README.md) są zaimplementowane jako walidacja offline i typowane DTO, bez wykonywania operacji.

- `Identity`: różne niemutowalne typy UUID ProjectKey/ObjectId/OperationId/SessionEpoch; ObjectIdentity bez nazwy/ścieżki; SessionIdentity z bindingiem PID/start UTC/handshake; ContractVersion 1.0. Poprawny UUID, także nil, nie dowodzi rejestracji ani autoryzacji; handshake sesji nie może być nil.
- `Operations`: żądanie z niezmiennymi bajtami canonical body/SHA-256, IdempotencyKey/RequestIdentity, typowane metody/błędy/stany/faza, ExecutionBudget i metadane. Porównanie idempotencji nie jest trwałym ledger ani retry.
- `Transport/V1`: parametry i wyniki metod, report/acceptance/error, ContractJson. Przypięty JsonSchema.Net 7.3.3 używa tylko wbudowanych schematów i lokalnych refs. Klient nie podaje schematu ani resolvera. Ewaluacja pojedynczego schematu jest serializowana; test obejmuje współbieżne wejścia poprawne i błędne.

Publiczną granicą danych nieufnych są metody ContractJson.Read*/Write*. Samo DTO nie potwierdza poprawności ani uprawnień. Nie omijaj walidatora i przyszłej niezależnej polityki hosta. ValidatedRequest i domenowy OperationRequest tworzy tylko walidator. Tożsamości są serializowane jako UUID, sesja jako wersjonowany binding, wersja jako string 1.0.

Nie zaimplementowano Workspace/Capabilities, synchronizacji, IO projektu, HTTP, SQLite, kolejki, recovery, fake workera ani Siemens. To następne osobno zlecane pakiety. Timeout zachowuje niepewność, bez sterowania procesami i deklaracji przerwania vendor call.

Build i [testy](../../tests/README.md), z root repo:

```powershell
dotnet restore TiaBridge.slnx --locked-mode
dotnet build TiaBridge.slnx --no-restore
dotnet test TiaBridge.slnx --no-build --no-restore
```
