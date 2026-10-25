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

## B-02B — scheduler, rzeczywisty Fake Worker i IPC

76 nowych przypadków .NET: SchedulerTests (29), SchedulerSafetyTests (13) i IpcAndPolicyTests (34). Łącznie 661 .NET, w tym 537 Core i 48 B-02A; Python 20, w tym 3 nowe testy IPC. Liczymy przypadki xUnit/unittest, bez mnożenia wewnętrznych pętli i prób. Migracyjna regresja obejmuje teraz v1→v3 oraz rzeczywisty baseline B-02A v2→v3 z zachowaniem danych i deduplikacji.

| Obszar / źródło | Rzeczywiste zachowanie i dowód |
|---|---|
| [SchedulerTests](integration/TiaBridge.Host.Tests/SchedulerTests.cs) | osobny proces, success/delay/duplicate i synthetic effect counter=1, trwały wynik; queued i running cancel; pięć scenariuszy hang/watchdog, heartbeat bez postępu, missing heartbeat, loss/exit/partial; timeout read bez overlap, late UNKNOWN receipt, retry tylko po no-effect i backoff, trzy próby/breaker, singleton/fence i host process crash mutation |
| [SchedulerSafetyTests](integration/TiaBridge.Host.Tests/SchedulerSafetyTests.cs) | różne projekty na tym samym portalu bez overlap; cancel vs dispatch race i cancel backoff; hung cancel pozostaje niepotwierdzony; brak profilu, queue timeout, pending restart, lease expiry, rosnący fence/UNKNOWN po drugim owner; rzeczywisty proces ze złym fence/planhash; host crash read i nowy odczyt dopiero po potwierdzeniu końca starego procesu |
| [IpcAndPolicyTests](integration/TiaBridge.Host.Tests/IpcAndPolicyTests.cs) | dziewięć współdzielonych przykładów i framing; HMAC/key/version/epoch/sequence/replay/unknown/missing/duplicate/UTF-8/depth/length/truncation; deterministic backoff/jitter, deny policy/identity retry, cooldown/half-open i niezależne zegary watchdog |
| [Python IPC](contracts/test_ipc_contracts.py) | niezależny JSON Schema: dziewięć poprawnych rodzajów komunikatów, missing/unknown/type/version/identity, unsafe artifact references i bounded inline result |

Pełny runner wymaga konkretnych testów procesowych i nowych dziewięciu przypadków framing. 1000 odczytów metadata statusu podczas realnego fake hang sprawdza p95≤100 ms; nie deklaruje HTTP SLO ani testu ośmiu klientów. Wszystkie DB/artifacts są syntetyczne na lokalnym NTFS. Process exit hosta jest deterministyczny po Started, nie losowy kill.

Braki B-02B pozostawiono właściwym pakietom: publiczne API/CLI/auth/SLO realizuje opisane niżej B-02C; pełny harness — B-02D; rzeczywisty worker, Dispose/apartment/Job Object parent-loss — X-01. Obcy SID i sprzętowy power-loss wymagają osobnego środowiska. Nie ma reconciliacji TIA ani automatycznego clear quarantine. PASS Fake nie potwierdza Siemens, SYNCED, Engineering lub Runtime.

## B-02C — HTTP API, CLI i nieblokowanie

39 nowych przypadków .NET: 38 w Host i 1 regresja Core. Razem 700 .NET (538 Core +48 Storage +76 Scheduler/IPC +38 HTTP/CLI) i 20 Python. Każda liczba oznacza odrębny przypadek xUnit/unittest, nie liczbę requestów w pętli.

| Źródło / liczba | Zachowanie i dowód |
|---|---|
| [HttpApiTests](integration/TiaBridge.Host.Tests/HttpApiTests.cs) / 13 | rzeczywisty Host+CLI+Fake, pełny submit/status/events/result, durable duplicate/409, 202 przed wynikiem, cursor/Last-Modified, queued/running cancel, restart i token rotation, unsupported bez acceptance, invalid JSON/version/query/body |
| [ApiSecurityTests](integration/TiaBridge.Host.Tests/ApiSecurityTests.cs) / 15 | token absent/wrong/query, Origin/null/Sec-Fetch/Host/proxy denial, rzeczywisty owner/ACL i celowe poszerzenie ACL własnego pliku z odmową CLI, foreign principal, declared/chunked body limit, rate window i reserved concurrency, redaction/CLI offline |
| [ApiMetadataAndCliTests](integration/TiaBridge.Host.Tests/ApiMetadataAndCliTests.cs) / 8 | 19 poprawnych nazw requestów i właściwy available/unsupported outcome, wspólny ledger szybkich POST/cancel, bounded wait bez auto-cancel, wynik FAILED/exit 6 vs status/exit 0, UNKNOWN i quarantine po cancel/restart, walidacja syntetycznego host.fake.json; rzeczywisty błąd startu workera daje trwały FAILED/NOT_STARTED przy dostępnym API |
| [ApiPerformanceTests](integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs) / 2 | 1000 HTTP status reads przy 1/8 klientach, 64 submit/8 klientów, 100 zapisów 1 MiB co ≥100 ms i niezależny SHA przy wiszącym procesie; twarde progi NF-01/02/03; rzeczywista kolejka 999 i wyścig 4 requestów o ostatnie miejsce, duplicate/status/health przy 1000 |
| [RequiredNullableTests](unit/TiaBridge.Core.Tests/RequiredNullableTests.cs) / 1 | najpierw rzeczywisty FAIL, potem zachowanie wymaganego age_ms=null przez generator/typowaną serializację, zgodnie z niezmienionym schema 1.0 |

Dotychczasowe regresje nadal są wykonywane. Procesowe kolekcje HTTP są izolowane od innych startów procesów, a ośmiu równoczesnych klientów działa wewnątrz tego samego profilu Host. Nie zwiększono timeout workera ani progów SLO. Percentyle obejmują wszystkie próbki; metryki/środowisko w ignorowanym .tia-bridge/b02c/performance.json. Wstępny profil burst edit bez odstępów na wymiennym exFAT przekraczał NF-03; nie deklarujemy jego kwalifikacji ani conditional replace na exFAT.

Odrębne pozostają B-02D i długotrwały soak B-10, rzeczywisty drugi SID, sprzętowy power-loss, native adapter/session grants i wszystkie testy Siemens/Engineering/Runtime. Dokument 15 opisuje dostępny podzbiór CLI i zakres API bez obietnicy wykonawców 19 metod.

## B-02D — pełny syntetyczny przepływ i jawne luki kwalifikacji

48 nowych .NET: ResilienceCrashTests 16, ResilienceSafetyTests 11, ResilienceSecurityTests 10, ResilienceArtifactTests 6, ResiliencePerformanceTests 5. Łącznie 748 .NET i 24 Python (4 nowe testy fail-closed reportera). Dodatkowy dowód queue-capacity pochodzi z istniejącego ApiPerformanceTests; nie zwiększa liczby testów. [Macierz przypadków](resilience/matrix.json), [RT/NF i ograniczenia](../docs/16_RESILIENCE_RECOVERY.md), [aktualny snapshot](resilience/QUALIFICATION.md).

| Źródło | Luka B-02A–C i nowe sprawdzenie |
|---|---|
| ResilienceCrashTests | prawdziwe Host HTTP+CLI i commit barriers, 5 granic admission i 6 granic dispatch/receipt/result; trzy storage faults i 100 requests/8 klientów; hash/sequence/epoch, po restarcie bez replay UNKNOWN |
| ResilienceSafetyTests | pełne fake RT-05 ze zmianą syntetycznego P/B i STALE_PLAN/0 effects; late receipt już w transporcie; stale epoch; obie kontrolowane kolejności cancel race; loss/exit/HB/no progress; expired lease; own leaked child/lock anomaly i quarantine |
| ResilienceSecurityTests | osobny proces wysyła HMAC/epoch/sequence/version/length/JSON/truncated ramki i błędny peer PID; ACL CurrentUserOnly pozostaje; unrelated sentinel przeżywa, failed-start cleanup zwalnia własne Process i UUID |
| ResilienceArtifactTests | orphan po publish przed reference pozostaje; corrupt/truncated/missing proof blokuje wynik; historyczny sukces nie zastępuje ponownej integrity kontroli; zniszczona końcówka ostatniego result WAL nie promuje utraconego sukcesu |
| ResiliencePerformanceTests | health/result/events 1000/8 klientów, 20 startów/history i safe read po recovery; rzeczywiste 300 s CPU/RAM/handles; 1001 efektów z recyclingiem wyłącznie idle worker; NTFS save/replace 1 MiB/100 i niezależna kontrola repo volume bez/ze zwieszonym workerem |
| test_resilience_report.py | nieaktualny/brakujący/failed/duplikowany dowód lub krótki idle odrzucone; krótki run nie udaje 8 h; zielone testy NTFS nie ukrywają failure replace repo |

Wykryty błąd limitu 1000 i początkowe przekroczenie health SLO mają rzeczywiste FAIL przed poprawką. Zależności Core i DTO pozostały niezmienione. NF-11 minimum 8 h NOT_RUN zgodnie z decyzją użytkownika/B-10B; formalna bramka nie jest automatycznie zwalniana. Obcy SID, power-loss i wszystkie pełne RT Engineering/sync/simulation są osobno NOT_RUN. Użycie syntetycznego P/B nie dowodzi readbacku ani planera TIA. Test kontroli repo filesystemu raportuje failure NF-03 jako failure kwalifikacji, nawet gdy test izolacji poprawnie potwierdza taką samą odmowę bez Host i podczas fake Hang.
