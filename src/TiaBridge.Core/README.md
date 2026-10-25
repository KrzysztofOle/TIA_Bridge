# TiaBridge.Core — B-01A i B-01B

Biblioteka .NET 10, niezależna od Host i Siemens. [Kontrakty v1](../../contracts/README.md) są zaimplementowane jako walidacja offline i typowane DTO, bez wykonywania operacji.

- `Identity`: różne niemutowalne typy UUID ProjectKey/ObjectId/OperationId/SessionEpoch; ObjectIdentity bez nazwy/ścieżki; SessionIdentity z bindingiem PID/start UTC/handshake; ContractVersion 1.0. Poprawny UUID, także nil, nie dowodzi rejestracji ani autoryzacji; handshake sesji nie może być nil.
- `Operations`: żądanie z niezmiennymi bajtami canonical body/SHA-256, IdempotencyKey/RequestIdentity, typowane metody/błędy/stany/faza, ExecutionBudget i metadane. Porównanie idempotencji nie jest trwałym ledger ani retry.
- `Transport/V1`: parametry i wyniki metod, report/acceptance/error, ContractJson. Przypięty JsonSchema.Net 7.3.3 używa tylko wbudowanych schematów i lokalnych refs. Klient nie podaje schematu ani resolvera. Ewaluacja pojedynczego schematu jest serializowana; test obejmuje współbieżne wejścia poprawne i błędne.
- `Workspace`: projekt/urządzenia/grupy/obiekty i locatory, jednostki wymiany z wieloma członkami, źródła/native/metadane, rewizje, coverage, manifest i konfiguracja. WorkspaceNameCodec, WorkspaceLayout i WorkspacePath wykonują deterministyczne mapowanie i walidację bez IO. WorkspaceHashing rozdziela raw/model/unit; WorkspaceJson waliduje wersjonowane artefakty i referencje.
- `Capabilities`: CapabilityConfiguration/Key/Entry/Query, CapabilityJson i snapshot CapabilityRegistry. Dokładne wersje, rodziny i zakresy, brak capability/kwalifikacji oznacza deny. Wynik nie nadaje uprawnień sesji. Registry nie ma domyślnych wpisów Siemens.

Publiczną granicą danych nieufnych są metody ContractJson.Read*/Write*. Samo DTO nie potwierdza poprawności ani uprawnień. Nie omijaj walidatora i przyszłej niezależnej polityki hosta. ValidatedRequest i domenowy OperationRequest tworzy tylko walidator. Tożsamości są serializowane jako UUID, sesja jako wersjonowany binding, wersja jako string 1.0.

Granice B-01B to WorkspaceJson i CapabilityJson; surowe konstruktory DTO również nie stanowią walidacji. [Opis formatu i algorytmów](../../docs/12_WORKSPACE_CAPABILITIES.md) obejmuje limity, metadata-assisted decoding, kolizje, konserwatywne odrzucanie reparse points, canonical hashes oraz zewnętrzną weryfikację evidence IDs. Modele używają tożsamości i OperationMethod z B-01A. Nie mają pola potwierdzającego SYNCED.

B-01C dostarcza [macierz pokrycia](../../tests/COVERAGE.md) i powtarzalny przebieg offline; naprawia utratę uszkodzonego Unicode w typowanym wire oraz walidację istniejącego lifecycle w projekcjach operation.status/result. Nie dodaje nowych modeli operacji ani planera.

Core nie implementuje synchronizacji, IO projektu/Workspace, normalizatorów Siemens, HTTP, SQLite ani procesów. Storage, scheduler i Fake Worker są w osobnych projektach [Host](../TiaBridge.Host/README.md); Core nadal nie zależy od tej infrastruktury. Timeout zachowuje niepewność, bez sterowania procesami i deklaracji przerwania vendor call. B-01C nie jest częścią B-01B.

Build i [testy](../../tests/README.md), z root repo:

```powershell
dotnet restore TiaBridge.slnx --locked-mode
dotnet build TiaBridge.slnx -c Release --no-restore
dotnet test TiaBridge.slnx -c Release --no-build --no-restore
```
