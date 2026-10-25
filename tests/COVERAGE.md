# Macierz pokrycia B-01C

Audyt wejścia: commit `5fe5bda6eb288e7aa8b13c58fd859ea84a8e01d9`, 160 testów .NET i 10 Python ponownie wykonanych PASS. Ocena niżej wynika z odczytu testów, schematów, Core i uruchomień. To macierz zachowań, nie deklaracja 100% branch/line coverage ani kwalifikacja TIA. Instrukcja pełnego przebiegu: [README](README.md).

## Każda z 19 operacji

Wspólne symbole oznaczają konkretne testy parametryczne, uruchamiane oddzielnie dla każdej metody:

- A-R: `ContractTests.Every_method_has_valid_typed_request_and_response_round_trip` (B-01A).
- A-N: `ContractTests.Every_method_rejects_missing_required_data_and_unknown_fields` (B-01A); sprawdzał dodatkowy parametr, brak idempotency key, dodatkowe pole wyniku i brak całego result.
- C-M: `ContractMatrixTests.Shared_shape_mutations_have_the_same_verdict_as_independent_schema_tests`; wspólny korpus request/report/error, wersje, typy, nieznane required features, UUID, limity Unicode i tekstu.
- C-R: `ContractMatrixTests.Every_required_parameter_result_and_nested_member_is_individually_checked`; każdy wymagany parametr i pole wyniku, błędny typ oraz wymagane pola obecnych zagnieżdżonych obiektów/przykładów.
- C-E: `ContractMatrixTests.Method_specific_errors_round_trip_and_reject_bad_shapes`; wszystkie error_codes z registry w standalone error i raporcie, brakujące wymagane pola, UNKNOWN+retry oraz obcy kod błędu poza macierzą metody.
- C-I: `LifecycleMatrixTests.Idempotency_binds_method_project_principal_and_canonical_body_without_a_ledger` i `Execution_budgets_match_each_registry_boundary`.

Źródła testów: [ContractTests](unit/TiaBridge.Core.Tests/ContractTests.cs), [ContractMatrixTests](unit/TiaBridge.Core.Tests/ContractMatrixTests.cs), [LifecycleMatrixTests](unit/TiaBridge.Core.Tests/LifecycleMatrixTests.cs). Niezależny Python [CoreMatrixTests](contracts/test_core_matrix.py) wykonuje ten sam korpus przez JSON Schema i schematy danych z OpenAPI; istniejący [ContractTests Python](contracts/test_contracts.py) nadal sprawdza wszystkie metody, błędy, OpenAPI i generator.

| Operacja | Pokrycie wejściowe | Luka z audytu | Uzupełnienie B-01C |
|---|---|---|---|
| project.inspect | A-R, A-N; Python required/error | .NET wymagane pola inventory_scope/wyniku i błędy per metoda | C-M, C-R, C-E, C-I |
| project.export | A-R, A-N; Python required/error | scope, nested manifest artifact, błędy eksportu | C-M, C-R, C-E, C-I |
| project.status | A-R, A-N; Python error | wynik freshness/status, błędne typy/wersje | C-M, C-R, C-E, C-I |
| files.changes | A-R, A-N; Python required/error | workspace_id, wynik changes, PATH_UNSAFE i WORKSPACE_CHANGED | C-M, C-R, C-E, C-I |
| sync.plan | A-R, A-N; Python required/error | wymagane pola plan DTO i błędy; brak rzeczywistego planera | C-M, C-R, C-E, C-I; sam kontrakt |
| sync.apply | A-R, A-N; unknown mutation test | brak wszystkich required params/results i kodów błędów | C-M, C-R, C-E, C-I; bez Apply |
| sync.verify | A-R, A-N; Python required/error | scope, dimensions, promotion DTO i błędy | C-M, C-R, C-E, C-I |
| compile.plc | A-R, A-N; enum mismatch test | pola diagnostics i wszystkie błędy kompilacji | C-M, C-R, C-E, C-I |
| compile.hmi | A-R, A-N; Python required/error | analogiczne granice HMI, bez compile live | C-M, C-R, C-E, C-I |
| simulation.prepare | A-R, A-N; Python required/error | pola izolacji/artefaktu i błędy | C-M, C-R, C-E, C-I |
| simulation.start | A-R, A-N; Python required/error | pola instance/lease/state i błędy | C-M, C-R, C-E, C-I |
| simulation.stop | A-R, A-N; Python required/error | instance/cleanup, NOT_OWNER/STOP_UNCONFIRMED | C-M, C-R, C-E, C-I |
| test.plc | A-R, A-N; allow_writes test | wynik transport/mapping/assertions i błędy | C-M, C-R, C-E, C-I |
| test.hmi | A-R, A-N; Python required/error | wynik browser/runtime/assertions i błędy | C-M, C-R, C-E, C-I |
| operation.status | A-R, A-N; Python required/error | niespójny lifecycle operacji docelowej był akceptowany | C-M, C-R, C-E, C-I; BoundaryTests i regresja Python |
| operation.result | A-R, A-N; Python required/error | niespójny lifecycle operacji docelowej był akceptowany | C-M, C-R, C-E, C-I; BoundaryTests i regresja Python |
| operation.events | A-R, A-N; oversized event test | dokładne 64 KiB i +1, pola eventu i błędy cursor | C-M, C-R, C-E, C-I; BoundaryTests |
| operation.cancel | A-R, A-N; Python required/error | typowany reason z uszkodzonym Unicode, disposition DTO | C-M, C-R, C-E, C-I; BoundaryTests |
| diagnostics.health | A-R, A-N; Python error | pola components i błędy per metoda | C-M, C-R, C-E, C-I |

## Moduły i granice

| Obszar | Istniejące testy i ograniczenie | Nowe testy / rozstrzygnięcie |
|---|---|---|
| Identity | IdentityAndLifecycleTests: rozróżnialne UUID, sesja, niezależność nazwy; niewiele kombinacji | DeterminismTests: 100 różnych ObjectId i reprezentacji; WorkspaceAdversarialTests: poprawny rename/move po pełnej walidacji; BoundaryTests: wersje, binding, unknown features |
| Lifecycle | Kilka poprawnych i błędnych krawędzi, terminal UNKNOWN | LifecycleMatrixTests: wszystkie 147 kombinacji current/next/outcome, terminalność, osobne phase/cancel/sync status; tabela Core bez zmian |
| Idempotency | Kanonikalizacja i principal/project/key, głównie operation.events | C-I per 19 metod: kolejność kluczy, zmieniony body pod tym samym key, principal/key/project, zapis/odczyt; bez deduplikacji efektów |
| Workspace i hierarchia | WorkspaceTests: podstawowe cykle/locatory i dwa manifesty | WorkspaceAdversarialTests: 14 niezależnych defektów grafu, rehash tam gdzie potrzebny, poprawny move/rename, unknown vendor fields; brak materializacji plików |
| Path codec | Syntetyczne reserved/Unicode/traversal/case/length/reparse cases | DeterminismTests: 10 rodzin nazw, 100 golden prezentacji, NFC, Rune/percent truncation, suffix collisions i dwa culture settings; WorkspaceAdversarialTests: brak poprawnego rodzica i odwrócone obserwacje |
| Manifest / hashe | Dwa round-trip, znany raw SHA, jeden golden model hash, unit reorder | 100 niezależnych raw/model golden SHA-256 (Python hashlib), 100 różnych manifestów/unit hash, permutacje plików/członków/coverage, wersje profilu i ochrona wejścia; vendor arrays pozostają znaczące |
| Capabilities | CapabilityTests: deny, exact key, wersje, snapshot, częściowy coverage | 100 wariantów qualified/gap/no evidence/not implemented/missing kind, culture/order stability; wszystkie PARTIAL/UNKNOWN/UNSUPPORTED odmawiają; SessionPermission nadal NOT_EVALUATED |
| Wersjonowanie | Wybrane envelope i artefakty, unknown fields | Korpus C-M: major/minor i unknown required features per 19 metod; dodatkowe wersje sesji/Identity, manifest required feature; brak zgadywania migracji |
| Rozmiary/kodowanie | Przekroczenie request, wybrany UTF-8, jeden duży event | BoundaryTests: dokładne 1 MiB/10 MiB i +1, raw event 64 KiB i +1, 4 klasy błędnego UTF-8 na 7 wire granicach, duplicate/escaped keys, uszkodzony surrogate i utrata Unicode przy typowanym zapisie |
| Zależności modułów | Tylko bezpośrednie assembly refs Core | Pełna załadowana closure referencji assembly w .NET; runner dodatkowo sprawdza csproj, nazwy wszystkich locked transitive packages i source imports; bez ładowania vendor DLL |
| Powtarzalny przebieg | Oddzielne ręczne polecenia | validate_offline.py: fail-fast, lokalny pusty feed/cache, brak HTTP audit NuGet, Release/.NET/Python/generatory/audyt/format/diff, raport TRX i 100 przypadków wymaganych; testy runnera odrzucają brak testów, skip, fail i zły exit code |

## Rozdział walidacji i wynik audytu

Wspólny korpus [contract-mutations.v1.json](fixtures/contract-mutations.v1.json) zawiera 39 reguł × 19 metod = 741 ewaluacji w .NET, a niezależnie 741 JSON Schema i 741 OpenAPI. Testy C-R/C-E wykonują dodatkowe mutacje wymaganych pól i kodów. Nie sumujemy tych pętli jako osobnych testów xUnit: są 57 oddzielnych wierszy C-M/C-R/C-E.

[core-determinism.v1.json](fixtures/core-determinism.v1.json) ma dokładnie 100 osobnych xUnit cases o ID 0–99: 10 rodzin nazw × 5 wariantów capability/coverage × 2 wersje normalizatora; każdy ma osobny UUID, wejście modelu i golden SHA-256. Test obejmuje hash/raw/model, manifest i unit membership, mapping/decode, kolizję, permutacje znanych zbiorów, dwa ustawienia kultury, round-trip i zachowanie wejścia. Python hashlib generuje oczekiwane raw/model hashes bez korzystania z Core; literalne wzorce prezentacji nie są kopią produkcyjnego codec. Generator sprawdza drift fixture. Nie są to plany synchronizacji.

JSON Schema sprawdza kształt i wyrażalne reguły kontraktu. Core dodatkowo sprawdza limity bajtów/UTF-8/depth, completed_units ≤ total_units, zgodność error/outcome i graf manifestu/hash/path containment. Nie deklarujemy, że surowy JSON Schema potwierdza filesystem, oblicza hash lub porównuje dowolne dwa pola liczbowe. Różne warstwy mają jawne, uzupełniające odpowiedzialności. Dla wspólnego korpusu nie występują rozbieżne werdykty.

Wykryte i poprawione błędy: B-01A nie odrzucał uszkodzonego UTF-16 przy konstrukcji IdempotencyKey i zapisie typowanych stringów (możliwa cicha zamiana na replacement character); schematy zagnieżdżonych OperationStatusResult/OperationResultResult nie stosowały istniejących reguł lifecycle. Regresje znajdują się w [BoundaryTests](unit/TiaBridge.Core.Tests/BoundaryTests.cs) i Python CoreMatrixTests. Schematy i OpenAPI są odtworzone przez generator; semantyka stanów, operacji, uprawnień i wersja 1.0 pozostają bez zmian. Nie wykryto nierozwiązanych błędów krytycznych w zbadanym zakresie; nie jest to dowód braku wszystkich możliwych defektów.

## Świadomie późniejsze etapy

- B-02B–D: pełna kolejka/dispatch/cancel races, restart/crash/late result, owner/epoch/fence, recovery quarantine i fake worker. Poniższe B-02A bada wyłącznie warstwę trwałości, nie cały ten przepływ.
- B-03 / B-05B: rzeczywiste IO, symlinki/reparse points, TOCTOU, native format parsers/normalizers i golden vendor fixtures. Obecne testy ścieżek używają danych obserwacji, bez filesystemu Workspace.
- B-04A: co najmniej 100 rzeczywistych deterministycznych planów, W/P/B, DAG i konflikty. Obecny task jawnie przenosi tę część pierwotnego skrótu backlogu do implementacji planera; nie usuwa jej kryterium.
- X-01 i dalsza integracja Siemens: dopiero po wymaganej bramce B-02A–D, na osobno zatwierdzonym zakresie. ENGINEERING/RUNTIME NOT_RUN.

## Rozszerzenie B-02A — Storage, 48 testów NTFS

Regresyjne 537 .NET i 17 Python pozostają bez zmiany liczby; Core i wire v1 nie zmieniono. Nowe testy wykorzystują rzeczywisty plikowy SQLite na lokalnym NTFS, bez TIA/Siemens i bez :memory: jako dowodu trwałości. [Szczegóły implementacji i ograniczeń](../docs/13_DURABLE_STORAGE.md), [polecenia](README.md).

| Klasa / liczba | Rzeczywiście wykonywany zakres |
|---|---|
| OperationStorageTests / 9 | atomowe przyjęcie i reopen; 100 równoczesnych nowych zgłoszeń (1 nowe, 99 duplikatów); principal/key/body/global scope; odrzucenie niezarejestrowanego projektu; wynik i eventy po nowym procesie; invalid lifecycle/CAS; terminal UNKNOWN i quarantine; pojedynczy claim; blokada dispatch po quarantine |
| FailureAndRestartTests / 14 | cztery granice rollbacku intentu; awaria po commit z CommitMayHaveSucceeded; realne błędy SQL events/outbox; kontrolowane SQLITE_FULL i disk-full IO; dwa deterministyczne process exits przed/po COMMIT; wyścig procesów o admission i claim; rollback state/result/quarantine po błędzie eventu; odczyt podczas otwartej transakcji writera |
| MigrationAndPolicyTests / 13 | pięć niedozwolonych lokalizacji; foreign directory/repository/junction; v1→v2 i WAL/FULL/FK; rollback upgrade; future version/hash drift; obca baza/ochrona historii SQL; korupcja pliku i brak wcześniej zainicjalizowanej bazy bez resetu ledgeru |
| ArtifactStorageTests / 12 | known SHA-256, size, readonly i reopen; brak/truncation/tamper; odrzucenie złego hasha/wyniku po korupcji także dla duplicate submit; nested refs; crash po flush/publish i zachowanie orphanów; trzy unsafe IDs; niekompletny input stream |

Źródła: [operacje](integration/TiaBridge.Host.Tests/OperationStorageTests.cs), [awarie](integration/TiaBridge.Host.Tests/FailureAndRestartTests.cs), [migracje](integration/TiaBridge.Host.Tests/MigrationAndPolicyTests.cs), [artefakty](integration/TiaBridge.Host.Tests/ArtifactStorageTests.cs). Interfejs IStorageFaults wstrzykuje błąd w określony checkpoint; proces StorageProbe kończy się przez Environment.Exit(73), a nie losowy kill. Test pełnego dysku nie zapełnia fizycznego dysku. RetainedOrphans niczego nie usuwa i nie adoptuje.

Runner agreguje dwa TRX z katalogu danego przebiegu i wymaga wskazanych testów Host; same stare PASS Core nie zamkną B-02A. Nie jest to kwalifikacja power-loss/ACL deployment, schedulera B-02B, pełnego harnessu B-02D, jakiegokolwiek efektu Siemens lub Runtime.
