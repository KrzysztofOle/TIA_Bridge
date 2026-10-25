# TiaBridge.Ipc — kontrakt 1.0

Biblioteka wire-only .NET 10, bez Core, SQLite lub Siemens. [Specyfikacja](../../docs/14_SCHEDULER_IPC.md), [JSON Schema](../../contracts/ipc/v1/message.schema.json) i [przykłady](../../examples/ipc/v1/dispatch.json) opisują length-prefix/HMAC, limity, epoch/fence/sequence, handshake i artifact references.

FramedConnection ma jednego czytelnika i serializowane zapisy. Protokół odrzuca nieobsługiwane wersje, duplicate/unknown/missing pola, zły HMAC, sequence, identity i rozmiar przed użyciem treści. HMAC nie szyfruje. PipePeer sprawdza PID Windows. Core pozostaje niezależny; przyszły net48 użyje wersjonowanego JSON, nie tej binarnej biblioteki net10.
