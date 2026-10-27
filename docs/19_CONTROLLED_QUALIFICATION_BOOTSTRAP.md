# 19 — Controlled Qualification Bootstrap, B-03A/E2-B0

Stan: implementacja i testy **OFFLINE**, 2026-10-27. Baza
`5d999023a799e91c90e54902504c5fa7108494ef`. Bieżące zlecenie zatwierdza implementację
ograniczonego eksperymentu; **nie autoryzuje rzeczywistego Engineering**. Nie utworzono
aktywnego grantu dla rzeczywistego projektu, klucza CNG ani realnego dowodu.

## Architektura i granice

Host API → istniejący Durable Operations/outbox → OperationScheduler → istniejący
Worker V19/owner/IPC 1.1 → immutable artifacts i receipts. Nie ma drugiego schedulera,
Attach lub Workera. Jedyny dodany profil [BootstrapInspectionProfile](../src/TiaBridge.Host/Scheduling/BootstrapInspectionProfile.cs)
wybierany jest przez **lokalną chronioną konfigurację startową**, zamiast profilu E1,
wyłącznie w trybie `qualification-only`. Nie jest fallbackiem po odmowie E1.

Domyślne `qualification_bootstrap=null` oznacza dotychczasowy profil E1 z wymaganym
aktywnym zaufanym dowodem. Klient HTTP/CLI operacji nie może wybrać trybu, dostarczyć
autoryzacji ani zmienić targetu. Nowe pole w request API v1 jest odrzucane.
Registry v1/v2 i Core qualification policy pozostają niezmienione. W eksperymencie
registry_version artefaktu ma `qualification-only`, capability key schedulera
`qualification-only.v19.discovery.v1`, a publiczny phase po odczycie
`QUALIFICATION_ONLY_COMPLETED`. SUCCEEDED oznacza potwierdzony odczyt, **nie kwalifikację**.
Nie zapisuje się fikcyjnego PASS ani dowodu E1. READ nie daje WRITE/IMPORT/EXPORT.

Zakres jest stały: `T05_RecipeSim_V19_Testowy`, plan `E2-B1`, dokładnie jeden
ProjectKey/PID/start/path/SID; STA, max_in_flight=1; project.inspect/PROJECT,
v19.discovery.v1, all_supported/fresh=true, timeout 120 s, queue timeout 300 s,
20 cykli, 64 Device / 256 nodes / MaxDepth 8 (pierwszy DeviceItem depth=0).
Exact 13-elementowy [read_scope E2-A](B03A_E2_A_PREPARATION.md#exact-profil-discoveryv1)
jest sprawdzany jako zbiór bez duplikatów. Nie dodano getterów ani pól IPC.
`T04_T05-000_Testowy` i E2-B2 są odrzucane; wymagają osobnego przyszłego zakresu.

## Lokalna autoryzacja

[BootstrapAuthorization](../src/TiaBridge.Host/Qualification/BootstrapAuthorization.cs)
jest zamkniętym JSON snake_case (64 KiB, duplicate/unknown fields i błędny JSON
odrzucane). Operator przygotowuje plik wyłącznie pod
`<state_path>/qualification-bootstrap/authorization.json`. To nie jest dokument HTTP.
Poniższe wartości są **placeholderami**, nie grantem:

```json
{
  "schema_version": "1.0",
  "authorization_id": "<new-UUID>",
  "operator_sid": "<actual-operator-SID>",
  "authorized_at_utc": "<authorization-UTC>",
  "not_before_utc": "<session-start-UTC>",
  "expires_at_utc": "<session-end-UTC>",
  "project_name": "T05_RecipeSim_V19_Testowy",
  "project_key": "<exact-reviewed-UUID>",
  "project_path": "<absolute-test-copy-AP19>",
  "project_root": "<authorized-project-root>",
  "tia_pid": 0,
  "tia_start_ticks": 0,
  "executable_path": "<exact-Portal-V19-EXE>",
  "session_sid": "<actual-Host-and-TIA-SID>",
  "tia_build": "<exact-API-FileVersion>",
  "worker_build": "1.0+<sha256-Worker-EXE>",
  "host_build": "1.0+<sha256-Host-DLL>",
  "operation_method": "project.inspect",
  "operation_scope": "PROJECT",
  "profile": "v19.discovery.v1",
  "read_scope": [
    "TiaPortal.GetProcesses", "TiaPortalProcess.Attach", "TiaPortal.Projects",
    "Project.Path", "Project.Name", "Project.IsModified", "Project.Devices",
    "Device.Name", "Device.DeviceItems", "DeviceItem.Name", "DeviceItem.DeviceItems",
    "DeviceItem.GetService<SoftwareContainer>", "SoftwareContainer.Software"
  ],
  "inventory_scope": "all_supported",
  "fresh": true,
  "timeout_seconds": 120,
  "queue_timeout_seconds": 300,
  "maximum_cycles": 20,
  "maximum_devices": 64,
  "maximum_nodes": 256,
  "maximum_depth": 8,
  "plan_id": "E2-B1",
  "plan_sha256": "<reviewed-B1-plan-sha256>",
  "git_sha": "<source-Git-SHA>",
  "stop_conditions": [
    "IDENTITY_DRIFT", "BUILD_DRIFT", "DIRTY_PROJECT", "READ_FAILURE", "TIMEOUT",
    "WORKER_LOSS", "RESOURCE_REVIEW", "EVIDENCE_FAILURE", "RELEASE_UNCONFIRMED"
  ]
}
```

W config Host operator jawnie dodaje `qualification_bootstrap` z authorization_id,
operator_sid i host_sid; oba SID muszą być identyczne. V19 targets i registered_projects
mają jeden zgodny ProjectKey. Config i root qualification-bootstrap wymagają chronionej
DACL/owner oraz bezpiecznych przodków według [Local Trusted ACL](18_LOCAL_TRUSTED_ENROLLMENT.md#tożsamość-i-acl).
Walidator **niczego nie naprawia**. Pliki operatora mają sprawdzane ACL i reparse safety.
State/SQLite/artifacts i konfiguracja pozostają pod per-user LOCALAPPDATA/TIA_Bridge2
na fixed NTFS, poza repo. Najpierw inicjalizuje się pusty state istniejącym Host bez
zleceń i bez bootstrapu; nie fabrykuje się markerów ani nie adoptuje obcych katalogów.

Fragment config (placeholdery; nie jest aktywną konfiguracją):

```json
"qualification_bootstrap": {
  "authorization_id": "<new-UUID>",
  "operator_sid": "<actual-SID>",
  "host_sid": "<same-actual-SID>"
}
```

Nie potrzeba prywatnego klucza lub anchora E1 do samej autoryzacji eksperymentu.
Granicą jest jawna lokalna decyzja operatora i Windows ACL wspólnego SID. Nie jest to
izolacja od administratora lub wrogiego procesu tego samego konta. Wielokontowa usługa
pozostaje niekwalifikowana. Dokument/hash/plan są provenance decyzji operatora,
nie dowodem, że wskazana kopia naprawdę jest bezpieczna; to wymaga preflight/review.

## Komendy operatora — dopiero po osobnym zatwierdzeniu E2-B1

Istniejący `TiaBridge.Qualification.dll`, .NET 10/Windows, bez nowych pakietów:

```powershell
dotnet $qualificationTool --config $qualificationConfig bootstrap-inspect
dotnet $qualificationTool --config $qualificationConfig bootstrap-authorize --document-sha256 $reviewedAuthorizationHash --confirm-operator-approval
dotnet $qualificationTool --config $qualificationConfig bootstrap-revoke
dotnet $qualificationTool --config $qualificationConfig bootstrap-report
dotnet $qualificationTool --config $qualificationConfig bootstrap-review --confirm-operator-review
```

`bootstrap-inspect` zwraca pełny dokument i SHA-256 dokładnych bajtów. Operator
przegląda binding/buildy/plan/okno; authorize wymaga exact reviewed hash, flagi i SID.
To create-new zapis dokumentu i hasha w istniejącej SQLite, nie wpis capability.
Bez potwierdzenia/brak dokumentu/drift → exit 3 LOCAL_QUALIFICATION_DENIED.
Komendy bootstrapu nie otwierają CNG i nie dotykają E1 lifecycle. HTTP nie ma ich endpointów.
Nie ponawiaj komendy o niepewnym wyniku — sprawdź zachowany stan i raport.

Przed live: oddzielna zgoda użytkownika, kopia/izolacja/prawo read-only, aktualna
ograniczona D-11 dla exact buildu/profilu, świadomy operator, czyste UI, świeże Windows
PID/start/EXE/SID/AP19/ProjectKey i dopiero autoryzowana enumeracja vendor. Nie używaj
historycznych PID. CODEX GUI podlega nadal bramce A/B w AGENTS. Te warunki pozostają
[w planie B1](E2-B1_TEST_PLAN.md); B0 ich nie wykonuje i nie nadaje zgody live.

## Trwały budżet, restart i replay

Migracja SQLite **004** dodaje bootstrap_authorizations, bootstrap_cycles i
bootstrap_release. Migracje 001–003 zachowują bajty i hashe. Document/hash/operator
są niezmienne; historia i cykle nie mogą zostać usunięte przez aplikację, licznik
nie może maleć. Jeden Authorization ID można zarejestrować raz. Kolejne reviewed
sesje wymagają nowego ID i nowej jawnej decyzji, nigdy resetu starego licznika.

READY → ACTIVE wiąże grant z jedną epoką schedulera. `PrepareDispatch` w transakcji
BEGIN IMMEDIATE/FULL rezerwuje kolejny numer 1..20 razem z operation intent, worker
epoch/PID/start i dispatch attempt, **przed IPC**. Operation ID i (authorization,cycle)
są unikalne. Idempotentny submit zwraca tę samą operację; nie przywraca cyklu.
Revoke pomiędzy rezerwacją i ostatnią rewalidacją zatrzymuje wysłanie, ale cykl jest
już zużyty. Crash po commit również go zużywa. Rollback niezatwierdzonej transakcji
nie daje dispatchu ani potwierdzenia rezerwacji.

Restart Host bezwarunkowo zamyka wcześniej ACTIVE authorization i oznacza RESERVED
cykle UNCERTAIN_AFTER_RESTART. Nie adoptuje Workera i nie odblokowuje starych zleceń.
Dispose/shutdown zamyka sesję, nawet gdy nie wykorzystano całego budżetu. Wygaśnięcie
okna, revoke i błąd walidacji blokują kolejne dispatch. Po 20 zakończonych cyklach
stan FINISHED; nie ma trwałego prawa do cyklu 21. Native retry jest zawsze wyłączone.

Rewalidacja odbywa się przy resolution, przed PrepareDispatch i przed IPC. Sprawdza
konfigurację/ACL i jej niezmieniony hash, document/hash/stan/epokę/budżet/okno, exact
request i binding. Windows preflight nie ładuje Siemens: rzeczywisty PID/start/EXE/
token SID, API FileVersion oraz hashe Host DLL i Worker EXE. Po handshake sprawdza
także Build/WorkerId faktycznego połączenia. Dodatkowe zależności Worker/Host i DLL
identities/hashes należą do build manifestu/preflightu E2-B1, nie do samego hasha EXE.
Host nie ma bez-Openness gettera current project path: config/document path sprawdza
przed dispatch, a istniejący Worker publiczną ścieżkę przed i po Attach, dokładnie
jeden projekt po Attach, PID/start/EXE/SID oraz właściwy project binding.

## IsModified, STOP i niepewność

Worker przy `IsModifiedBefore=true` zachowuje surową obserwację dirty state i zwalnia
własny Attach, pomijając inventory. Nie dodano gettera. Host odrzuca każdy true
przed lub po odczycie; true/true nie jest już SUCCEEDED również w normalnej projekcji.
False→true jest błędem eksperymentu. Nigdy Save/naprawa. Surowe receipts są trwale
zapisywane przed interpretacją bootstrapu, także przy błędnym DTO lub epoch; raw
observation trafia do artefaktu przed walidacją, jeśli ma poprawny format V19Observation.

Timeout, worker loss, stale epoch, błędny binding/build/profile/getter/limit,
niepotwierdzony Dispose lub brak dowodów zatrzymują dalszy eksperyment. Reserved cykl
pozostaje zużyty. Dotychczasowy active slot/UNKNOWN/quarantine/fencing nie są obchodzone;
timeout, śmierć procesu i lease nie dowodzą zatrzymania Siemens call. Revoke ma tę
samą granicę co E1: ostatnia walidacja nie jest atomową transakcją z wysłaniem IPC.
Nie twierdź, że revoke zatrzymał już wysłany call. Nie zabijaj TIA lub cudzych procesów.

## Telemetria, pakiet i review

SQLite zachowuje authorization ID/hash, cycle, operation ID, Host/Worker epoch,
Worker PID/start, reserved UTC i stan. Przed IPC zapisuje pierwszą próbkę Handles/
PrivateBytes/WorkingSet, a przy receipt drugą oraz czas całkowity, disposition/error
i quiescent receipt. Raw V19Observation zawiera enumeration/Attach/read/Dispose ms,
TIA PID/start, IsModified, graf Discovery, owner/apartment i PortalAliveAfterDispose.
Pełny release potwierdza dopiero powrót DisposeAsync własnego połączenia/procesu przy
braku active call; błąd nie jest potwierdzeniem. Nie restartuje się automatycznie
Workera dla spłaszczenia trendu; 20 cykli nie osiąga limitu recycling 1000 schedulera.
Utrata Workera także pomiędzy cyklami zamyka grant przed próbą zastąpienia procesu.
Kompletny pakiet wymaga jednej tożsamości Worker epoch/PID/start dla całej serii.

`bootstrap-report` tworzy nowy UUID katalog `qualification-bootstrap/reports/<uuid>`
bez overwrite: authorization bytes/hash, cycles/telemetry/results, request/events
(wszystkie strony), wszystkie surowe receipts i Worker responses, zweryfikowane
artefakty, report.json oraz manifest.json z rozmiarem i SHA-256 dokładnych bajtów.
Pakiet automatycznie dołącza exact `v19-audit-<worker-epoch>.jsonl`, weryfikuje epokę
i Dispose.Returned każdego poprawnego cyklu. Sprawdza JSON i binding telemetrii,
nieujemne próbki, komplet initial sample oraz zgodność cycle report z hashowanym
raportem trwałej operacji. Stara fenced odpowiedź zatrzymuje bieżący cykl, bez
próby przepisania wcześniejszego terminalnego wyniku.
Raport zapisuje się przed manifestem, manifest nie hashuje siebie. Zewnętrzny hash
manifestu jest wynikiem komendy; Verify ponownie sprawdza kompletny zbiór plików
i hashe. Nie kopiować tokenów discovery, DB na żywo ani surowych projektów do Git.
Niepoprawny lub częściowy pakiet pozostaje zachowany, bez automatycznej naprawy.

Statusy: FAIL dla potwierdzonych błędnych cykli; BLOCKED dla brakujących/uszkodzonych
dowodów; INCOMPLETE przy niepełnej serii; REVIEW_REQUIRED po poprawnej serii bez
pełnego release/review; PASS dopiero po 20 potwierdzonych cyklach i ręcznym review.
Sam report **nigdy nie przyznaje automatycznie PASS po 20 odczytach**.

Reviewer przygotowuje chroniony `qualification-bootstrap/review.json`:
authorization_sha256, operator_sid, reviewed_at_utc, project_unchanged=true,
plan_checks_passed=true, resource_trend oraz supporting_files (3..16 safe leaf names).
Rzeczywiste załączniki pod review-data muszą obejmować niezależne before/after projektu,
pełny przegląd kryteriów B1 oraz analizę trendu/idle/cleanup. To jawne twierdzenia
operatora poparte bajtami w pakiecie, nie automatyczny dowód prawdziwości UI/projektu.
`bootstrap-review --confirm-operator-review` sprawdza SID, hash autoryzacji, release,
komplet cykli i kopiuje/hashuje exact aktualne review bytes i załączniki.
GROWING lub INCONCLUSIVE pozostawia REVIEW_REQUIRED; tylko uzasadnione
STABLE_WITHIN_OBSERVATION może dopuścić ograniczony PASS. Nie ma nowego progu NF
ani automatycznej diagnozy leak. Cold 1/warm 2..20, wszystkie outliery, nachylenia,
mediany okien i 30 s idle ocenia się według E2-A. Brak wystarczających próbek blokuje review.

Kod nie odczytuje stanu UI i nie zastępuje zewnętrznego samplera 1 s oraz before/after
operatora z planu. Bez tych dowodów operator nie może potwierdzić plan_checks_passed.
To ograniczona kwalifikacja odczytu, bez NF-11 lub tezy o długiej stabilności.
Worker Handle Risk pozostaje UNKNOWN. Późniejszy review/enrollment używa manifestu
i zamkniętego bundle zgodnie z limitami [17](17_TRUSTED_QUALIFICATION_EVIDENCE.md).

## Wyłączenie i późniejsze E1

Operator wykonuje bootstrap-revoke i kontrolowany stop własnego Host. Usunięcie
qualification_bootstrap z chronionej konfiguracji plus restart przywraca normalny
tryb wymagający E1; stary grant nie zostaje adoptowany. Nie kasuj SQLite, cycle rows,
autoryzacji ani pakietów, aby uzyskać nowy limit. Stare STOP/FAIL/UNKNOWN pozostają.

Bootstrap PASS jest tylko **kandydatem**. Niezależny review source run, exact
build/project/read scope i integralności, osobna decyzja, a następnie istniejące
`inspect → approve → enroll → verify` mogą aktywować E1. B0 nie wywołuje enrollment,
nie tworzy anchora i nie przepisuje registry na PASS. B2 wymaga B1 PASS i własnej
autoryzacji/preflightu; obecna implementacja nie dopuszcza bootstrapu B2.

## Testy i granice dowodu

[BootstrapTests](../tests/integration/TiaBridge.Host.Tests/BootstrapTests.cs) używają
realnej SQLite/ACL/CLI i schedulera, ale syntetycznego IV19WorkerConnection z DTO
IPC 1.1; nie uruchamiają V19 ani Siemens DLL. StorageProbe rzeczywiście kończy własny
proces po commit rezerwacji przed dispatch. To test recovery Host, nie vendor cleanup.
Rzeczywisty VerifyLiveBinding ma negatywny test z nieuruchamialnym syntetycznym EXE.

| Wymagania zadania | Pokrycie |
|---|---|
| 1–2 | Missing_invalid_or_unapproved_local_authorization_never_dispatches; lokalny CLI confirmation/hash/SID |
| 3–15 | Exact_authorization_rejects_each_scope_or_binding_drift; Valid_bootstrap_authorization_does_not_allow_other_request_methods |
| 16–17 | Twenty_cycles_are_durable_idempotent_and_do_not_enroll_or_grant_normal_E1 |
| 18–19 | Abrupt_process_exit_after_reservation_consumes_cycle_and_restart_cannot_replay; Host_restart_closes_authorization_and_keeps_reserved_cycle_uncertain_without_replay |
| 20–22 | Queue_revalidation_stops_revoked_expired_or_changed_grant; Revoke_at_either_dispatch_boundary_sends_no_native_call |
| 23–25 | Dirty_project_and_uncertain_worker_results_stop_remaining_budget_and_retain_raw; Stale_receipt_from_previous_cycle_stops_current_cycle_without_rewriting_history; V19Offline false/true i true/true |
| 26–28 | Missing_corrupt_and_partial_evidence_never_produces_PASS: artefakty/audit/telemetry/initial/report; bundle Verify tamper i review GROWING |
| 29–30 | Twenty_cycles...; Normal_configuration_has_no_bootstrap_and_client_cannot_supply_one |
| 31–32 | Pełne istniejące E1 LocalTrusted/V19/Core/Python/API/registry/IPC/resilience suites, bez zmian pól API/IPC |

ADR 0001–0003 pozostają bez zmian: brak mutacji, drugiego wykonawcy, canonical HMI,
nowych publicznych IDs lub SYNCED. Nowa ścieżka realizuje bieżącą jawną decyzję
użytkownika o qualification-only; nie uznaje niekwalifikowanej capability za enabled.
Końcowe liczby i ograniczenia walidacji są w [Zmiany.md](../Zmiany.md).

## E2-G — gotowość przed lokalną autoryzacją

[Walidator E2-G](21_E2_B1_READINESS_GATE.md) ocenia aktualne safety/ograniczoną D-11
na zatwierdzonym SHARED_WORKSTATION 1.0.0 i dossier odbiorcy. Jego READY_FOR_OPERATOR_AUTHORIZATION
nie jest BootstrapPermit, wpisem SQLite, aktywną konfiguracją ani zgodą na Attach.
W tej fazie B0 pozostaje INACTIVE; operator osobno sprawdza i zatwierdza dokument,
exact hash/plan/window/binding/buildy. Mechanizmy Validate/VerifyLiveBinding, chronione
ACL i trwałe 20 cykli pozostają bez zmian. Dossier to review claims, nie live proof.
IsModified=false jest sprawdzane dopiero w autoryzowanym odczycie i false/false
w każdym cyklu. Nie aktywowano rzeczywistego B0, nie dodano auto enrollment ani kluczy.
