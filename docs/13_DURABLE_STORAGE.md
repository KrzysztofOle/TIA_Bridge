# 13 — Trwały magazyn B-02A

Stan 2026-10-25: biblioteka Host Storage, SQLite i plikowe artefakty zaimplementowane offline. B-02A sam nie uruchamia zadań; rozszerzenie [B-02B](14_SCHEDULER_IPC.md) dodaje wykonanie Fake i migrację 003. Granice: [architektura 03](03_ARCHITECTURE.md), [synchronizacja 04](04_SYNCHRONIZATION.md), [interfejs 05](05_CODEX_INTERFACE.md), [Host README](../src/TiaBridge.Host/README.md). Żaden zapis SQLite nie dowodzi wykonania TIA, Compile lub Runtime.

## Lokalizacja i inicjalizacja

Domyślne state: `%LOCALAPPDATA%\TIA_Bridge2\state`. Jawny root musi leżeć pod lokalnym katalogiem aplikacji TIA_Bridge2 na stałym NTFS, poza repo i jego przodkami. Polityka jest konserwatywna: brak UNC/SMB, device paths, exFAT/removable, traversal i dowolnego C:\foreign. Sprawdzane są istniejące komponenty ścieżki, także przodkowie, plik DB/WAL/SHM i ścieżki artefaktów; reparse points są odrzucane. To nie jest ochrona przed administratorem lub nieograniczonym wrogim procesem tego samego użytkownika zmieniającym ścieżki w wyścigu. Pełna kwalifikacja storage/power-loss i ACL deployment pozostaje późniejszym zakresem; nie przechowuje się tu sekretów.

Empty root może zostać jawnie zainicjalizowany. Istniejący niepusty root wymaga dokładnego markera `.tia-bridge2-storage-v1`; obcy katalog/baza nie są adoptowane, przenoszone ani naprawiane. Marker `.tia-bridge2-database-initialized` powstaje po prawidłowych migracjach i kontroli integralności. Jego obecność przy braku DB blokuje otwarcie, zamiast tworzyć świeży pusty ledger. Po inicjalizacji zwykłe połączenia używają ReadWrite, bez Create. Brak wszystkich plików/root po utracie dysku nie jest odzyskiwany z samego kodu — wymaga zewnętrznej kopii i osobnej procedury.

Init mutex serializuje inicjalizację w sesji Windows; poprawność transakcyjna między procesami wynika z SQLite i ograniczeń DB. WAL, synchronous=FULL i foreign_keys=ON są konfigurowane i testowane. Każde wywołanie używa własnego połączenia, private cache, pooling=false, timeout lock 5 s. Jeden writer na root w procesie oraz BEGIN IMMEDIATE pomiędzy procesami. Długie hashowanie plików odbywa się poza transakcją. Błąd/busy nie uruchamia ponownie operacji domenowej. Nie ma automatycznego recovery/replay zleceń po starcie; standardowe checkpointy WAL SQLite nie zmieniają ich lifecycle.

## Schemat i migracje

PRAGMA application_id ma wartość 0x54494232. Aktualny schemat po B-02B ma user_version=3; poniższe dwie migracje opisują zachowany zakres B-02A. PRAGMA user_version i `schema_migrations(version, sha256, applied_at)` muszą się zgadzać z osadzonymi migracjami. SHA-256 jest liczony nad UTF-8 SQL z EOL LF, niezależnie od CRLF checkoutu. Unknown version, drift hasha, obcy schemat, błąd integrity_check lub foreign_key_check blokują użycie. Nie ma down migration, resetu ani automatycznej zmiany dawnych hashy.

- `001_initial.sql`: 18 tabel z kluczami i FK, w tym encje docelowe 03 oraz outbox, operation_results, operation_artifacts i ledger migracji.
- `002_guards.sql`: indeksy i triggery retencji/niemutowalności, zakaz requeue outbox i terminalnego stanu bez trwałego raportu.

Wszystkie brakujące migracje wykonują się w jednej transakcji. Upgrade v1→v2 zachowuje zaakceptowane operacje i deduplikację; błąd upgrade zostawia poprzednią wersję. Początkowa awaria przed ukończeniem inicjalizacji może pozostawić zarządzany pusty DB/temp, bez zaakceptowanych operacji; niczego nie usuwa się automatycznie.

| Tabela | Zakres B-02A |
|---|---|
| projects | rejestracja ProjectKey, bez discovery/bindingu Siemens |
| sessions | struktura binding_json i FK do projektu, bez session ownership |
| operations | request wire, canonical body/SHA, principal/project/method/accepted_at, bieżący raport/hash, lifecycle/outcome i rewizja CAS |
| idempotency_keys | UNIQUE(principal, project_scope, key); FK wiąże operację, principal i scope |
| events | immutable raporty z UNIQUE(operation_id,event_sequence), czasem i SHA |
| operation_results | jeden niezmienny terminal report/hash per operacja |
| outbox | jeden rekord per przyjęcie, stan i trwały claim UUID |
| artifacts / operation_artifacts | hash, size, zarządzana ścieżka, domyślne pinned=1 i relacje operacji |
| quarantines | minimalna blokada projektu po jawnym RECONCILIATION_REQUIRED; bez zwalniania lub automatycznego recovery |
| steps / leases | struktury przyszłego journalu kroków/ownership; bez wykonania, lease expiry lub fences |
| snapshots / plans / baseline_revisions | struktury referencji do projektu/artefaktów; bez capture, planowania i CAS baseline |
| capabilities / authorizations | struktury odwołań/metadanych; bez kwalifikacji i nadawania praw |
| schema_migrations | wersje SQL i hashe, kontrolowane przy otwarciu |

`project_scope` jest generowane jako coalesce(project_key,''). Dzięki temu globalne operacje z NULL project nie omijają UNIQUE jak w zwykłej nullable kolumnie SQLite. Nil UUID pozostaje osobnym rzeczywistym kluczem zgodnie z Core. Wszystkie tekstowe scope/key są porównywane dokładnie; normalizacja nazw Workspace nie dotyczy idempotencji. Zarejestrowany klucz nie dowodzi praw do AP19.

## Przyjmowanie i idempotencja

`Accept(authenticatedPrincipal, requestUtf8)` sprawdza limit przed kopiowaniem, wykonuje ContractJson.ReadRequest i zachowuje własny snapshot. W krótkiej transakcji rozstrzyga trwały klucz, sprawdza zarejestrowany projekt, tworzy istniejący typ Core OperationId, zapisuje request/canonical body/SHA, ACCEPTED/NOT_STARTED, pierwszy event i PENDING outbox, po czym COMMIT. Dopiero powrót metody stanowi acceptance. Nie ma HTTP 202 w tym pakiecie.

Istniejący klucz porównuje RequestIdentity z Core oraz dokładne canonical bytes. Identyczny request zwraca ten sam OperationId i historię, bez nowego eventu/outbox. Zmieniony body daje IDEMPOTENCY_MISMATCH. Principal/projekt/key są osobnymi wymiarami; zmiana tylko key nie zmienia canonical fingerprintu. Deduplikacja nie zależy od writer gate w RAM: UNIQUE i BEGIN IMMEDIATE obowiązują także przy kilku procesach.

Awaria przed commit wycofuje cały intent. Awaria podczas/po próbie commit może oznaczać, że commit nastąpił mimo braku potwierdzenia: StorageException.CommitMayHaveSucceeded pozostaje true. Należy sprawdzić lub powtórzyć wyłącznie ten sam klucz/request; nie jest to ponowienie wykonania. Storage nie planuje żadnego automatycznego retry. Diagnostyka StorageException używa stałego kodu Core, bez SQL, principal, payloadu i ścieżki w wyjątku.

## Journal i wynik

`Record(id, expectedRevision, report, claimId?)` waliduje istniejący wire report i OperationLifecycle. Sprawdza identyfikator, method, accepted_at i CAS rewizji. Report o tym samym stanie może aktualizować phase/postęp, bez zmiany lifecycle. Wejście w RUNNING wymaga istniejącego DISPATCH_RECORDED z odpowiednim claim, lecz samo to nadal nie jest grantem wykonania.

Stan/revision/report/hash, następny event oraz ewentualny wynik terminalny, zamknięcie outbox i minimalna quarantine zapisują się atomowo. Odroczony composite FK operacji do `(operation_id, sequence, report_hash)` zdarzenia wymaga spójnego eventu przy commit. Wynik terminalny jest niezmienny; SUCCEEDED wymaga prawidłowego typed result. Raport jest dowodem zakończenia w ledgerze, a nie weryfikatorem prawdziwości danych zwróconych przez przyszłego wykonawcę. Wyniki Compile/sync/Runtime pozostają odrębnymi DTO Core.

`Events` zwraca historię uporządkowaną sequence, z paginacją i sprawdzeniem hashy. `ReadStatus` jest wyłącznie odczytem metadanych dziennika i nie dotyka artefaktów/TIA/wykonawcy. Status historyczny SUCCEEDED po późniejszym uszkodzeniu artefaktu nie jest świeżym integrity PASS. `Read`/`ReadResult` oraz duplicate acceptance weryfikują zarządzane referencje plików; uszkodzenie daje odmowę zwrotu poprawnego wyniku. Historia i dawny UNKNOWN pozostają dostępne. B-02A nie zamienia po restarcie RUNNING na inną historię ani nie wznawia go — dopiero B-02B/D musi rozstrzygnąć unknown effect przed dispatch.

## Outbox bez wykonawcy

Stany infrastrukturalne: PENDING → CLAIMED → DISPATCH_RECORDED → CLOSED; dopuszczone wcześniejsze zamknięcie przy terminalnym wyniku/anulowaniu. To nie jest drugi lifecycle operacji. `Pending` odczytuje nieprzejęte ACCEPTED/QUEUED bez quarantine. `TryClaim` wykonuje CAS w DB i nadaje dokładnie jeden claim; przegrany wyścig dostaje null. `RecordDispatch` wymaga właściwego tokenu, aktualnego stanu i braku quarantine. Nie wysyła niczego do procesu.

Przejęty wpis nigdy nie wraca sam do PENDING, również po restarcie, utracie procesu czy lease. Nie ma expiry/release/retry API. RECONCILIATION_REQUIRED zamyka jego outbox i utrwala minimalną quarantine projektu, także dla nowych/przejętych wcześniej wpisów. Prawidłowe recovery, one-owner/epoch/fence, quiescence i walidacja odbiorcy są wymaganiami B-02B/D; pojedynczy claim nie gwarantuje exactly-once efektu Siemens.

## Artefakty i awarie

Identyfikator `artifact-<32 lowercase hex>` i ścieżka `artifacts/<id>.blob` są generowane przez magazyn. Input nie wybiera ścieżki. Limit strumienia wynosi 1 GiB; plik nie jest w całości buforowany w RAM. Zapis: exclusive create w spool → zapis → Flush(true) → SHA-256 dokładnych bajtów → move bez overwrite na tym samym NTFS → readonly → weryfikacja pod uchwytem odmawiającym write/delete → referencja i relacja SQLite. Operacja plikowa i DB nie są jedną transakcją.

Niepowodzenie zapisu/flush/reference lub przerwanie procesu może pozostawić temp lub niepowiązany opublikowany plik. `RetainedOrphans` raportuje je, zachowując oba rodzaje; nie odtwarza z nich sukcesu, nie adoptuje i nie usuwa. Brak GC/retencji. Zarejestrowane artefakty mają immutable hash/size/path i pinned=1. Referencje ArtifactRef, także zagnieżdżone, oraz listy evidence/coverage/environment refs muszą wskazywać istniejące zarządzane pliki. Hash/rozmiar i ścieżka są sprawdzane przy użyciu; potrzebne uchwyty są zachowane przez commit wyniku. Uszkodzony lub brakujący plik blokuje wynik. Readonly jest zabezpieczeniem przed przypadkowym zapisem, nie ochroną kryptograficzną przed administratorem.

Fault injection obejmuje realne transakcje i błędy SQL przy events/outbox, kontrolowany SQLITE_FULL i Windows disk-full IO error, przerwany input stream oraz testowy proces kończony deterministycznie przez Environment.Exit(73) w zadanym checkpoint. Nie zapełnia się fizycznego dysku i nie używa losowego kill; power-loss sprzętu/WAL durability nie zostały zakwalifikowane. Flush/FULL i test procesu nie dają bezwarunkowej gwarancji wobec utraty sprzętu.

## Testy i kolejna bramka

Nowe [testy Host](../tests/integration/TiaBridge.Host.Tests/OperationStorageTests.cs), [awarii/restartu](../tests/integration/TiaBridge.Host.Tests/FailureAndRestartTests.cs), [migracji/polityki](../tests/integration/TiaBridge.Host.Tests/MigrationAndPolicyTests.cs) i [artefaktów](../tests/integration/TiaBridge.Host.Tests/ArtifactStorageTests.cs) używają plikowych SQLite na C:/lokalnym NTFS w własnych UUID katalogach `%LOCALAPPDATA%\TIA_Bridge2\dev\storage-tests`. Sprzątanie ograniczono do tej ścieżki i nie przechodzi przez reparse points. Test junction powstaje tylko we własnym katalogu testu. [StorageProbe](../tests/integration/TiaBridge.StorageProbe/Program.cs) jest krótkim procesem testowym, nie workerem/fake workerem ani CLI aplikacji.

Pełne polecenie i raporty: [instrukcja testów](../tests/README.md). Runner zbiera oddzielne TRX obu projektów, nie pozwala nadpisać regresji wynikiem Host i wymaga również określonych testów trwałości. B-02A kończy wyłącznie tę warstwę. ENGINEERING/RUNTIME NOT_RUN; rozszerzenie B-02B opisuje dokument 14; B-02C/D nadal nie są wykonane i bramka Siemens pozostaje zamknięta.

## Rozszerzenie B-02C

[Host API/CLI](15_HTTP_API_CLI.md) dodaje atomic admission limit 1000 aktywnych operacji po lookup idempotencji oraz metadata/cancel bez drugiego ledgeru. Schemat pozostaje v3, migracje 001–003 bez zmian. Host utrzymuje uchwyt lifetime WAL i osiem krótkich reader connections do statusu/health, bez współbieżnego użycia connection/command. Pooling providera pozostaje false; readers należą wyłącznie do Host lifetime. Nadal WAL/FULL/FK i lokalny NTFS. Szybki metadata read sprawdza principal oraz bieżący report/hash; pełny result zachowuje SHA verification artefaktów.
