# 21 — E2-G: deterministyczna gotowość E2-B1

Status: implementacja OFFLINE. `SHARED_WORKSTATION 1.0.0` zatwierdzony 2026-10-27.
Stan przygotowania E2-G: Engineering B1/B2 NOT_RUN, autoryzacja NOT_GRANTED.
Późniejsze osobno autoryzowane [wykonanie B1](../tests/engineering/E2_B1_2026-10-27_06cbfec6.md)
uzyskało READY, wykonało 20/20 odczytów i zakończyło grant; kwalifikacja BLOCKED
przez zasoby, B2 NOT_RUN. Profil, D-11 i pełna kwalifikacja to osobne wyniki.
Historyczne raporty X-01 pozostają niezmienione.

## Jedno narzędzie i trzy wyniki

[`e2_gate.py`](../scripts/e2_gate.py), Python 3.12/std library, odczytuje lokalne
dowody; nie uruchamia Host/Worker/TIA, nie wykonuje enumeration/Attach, nie otwiera
SQLite, nie tworzy grantu i nie zmienia ACL/kluczy. Wykorzystuje istniejące
performance_evidence, reporter RT/NF i kontrolę TRX runnera. Nie przebudowuje E1/B0.

```powershell
py -3 -B scripts/e2_gate.py --run-id <canonical-run-id> --output .tia-bridge/e2-g-assessment.json
py -3 -B scripts/e2_gate.py --run-id <canonical-run-id> --dossier <reviewed-dossier.json> --output .tia-bridge/e2-g-reviewed.json
```

Wynik jest JSON, zapis CreateNew (istniejący plik nie jest nadpisywany):

| Status | Znaczenie |
|---|---|
| READY_FOR_OPERATOR_AUTHORIZATION | PASS safety, wymaganych KPI/D-11, integralności i kompletne dossier B1; można rozpatrzyć osobną decyzję operatora |
| BLOCKED | Brak/niezgodność dowodu, profilu, źródeł, buildu, scope, bindingu lub wymagany negatywny test |
| REVIEW_REQUIRED | Dowody offline poprawne, ale stan kopii/UI, monitoring, bezpieczny STOP, kolizja lub stan bootstrapu wymagają review |

BLOCKED ma pierwszeństwo przed REVIEW_REQUIRED. Każda przyczyna ma stały `code`
i `description`. Exit 0 tylko dla READY; exit 1 BLOCKED/REVIEW_REQUIRED, exit 2
niepoprawne argumenty. Wszystkie wyniki mają `operator_authorization=NOT_GRANTED`,
`bootstrap_activation=false`, `siemens_attach_permitted=false`. Brak aktywnej
autoryzacji jest prawidłowy przed decyzją; nie jest fikcyjnym aktywnym grantem.

## Warunki dowodu i świeżość

Jeden run `.tia-bridge/resilience/<run-id>` musi zawierać kompletny zamknięty
`evidence-manifest.json`: wszystkie relative paths, bytes i SHA-256, bez braków,
nadmiarowych plików lub reparse. Uszkodzenie dowolnego artefaktu blokuje ocenę.

Wymagane są: run ID, Git SHA, UTC start/end, profil i wersja 1.0.0, hash dokładnych
bajtów wspólnej definicji, zastosowane progi, build Host DLL i Worker EXE, źródła,
OS/runtime/filesystem, preflight, pełne próbki i wyniki RT/NF, TRX/Python oraz
końcowy status. `validation/build-metadata.json` utrwala buildy i fingerprint źródeł
przed testami; jawny SHARED_WORKSTATION buduje Worker offline, bez uruchamiania vendor API. Domyślna ścieżka STRICT nie dodaje zależności od instalacji Siemens.

Walidator ponownie liczy hashe i próbki, odtwarza raport RT/NF, kontroluje komplet
kroków runnera i porównuje źródła/binary z bieżącymi. Fingerprint obejmuje src/tests/
contracts/scripts/config (bez bin/obj/cache) oraz pliki konfiguracji buildu root.
Git SHA identyfikuje bazę runu; nie wymaga równości z późniejszym commitem tego samego
sprawdzonego drzewa. Dopuszczony dirty run jest związany z dokładnym fingerprintem
i buildami; sam SHA nie dowodzi niezmienności lokalnych zmian. Hash planu jest
sprawdzany niezależnie w dossier i ponownie przez B0. Nie ma arbitralnego TTL.
Zmiana profilu/źródeł/binariów wymaga nowego dowodu; zmiana planu nowego review.
Proces i odbiorca są ponownie sprawdzane tuż przed rzeczywistym wykonaniem przez B0.

Preflight PASS/PASS_WITH_LOAD jest wymagany. Zwykłe obciążenie CPU/dysku nie
blokuje oceny; zasady istotnej presji zasobów określa [profil](20_PERFORMANCE_QUALIFICATION_PROFILES.md).
PASS funkcjonalny, PASS ograniczonej D-11, BLOCKED pełnej kwalifikacji i NOT_RUN
Engineering są odrębnymi polami. Exit 0 runnera nie jest pełną kwalifikacją.

RT-03/05/06/15/16 i NF-01/02/03/04/05/06/12/14/15 wymagają PASS. Obowiązkowe są
pełne testy Core/Host/Python, w tym B0/E1/identity, bez skip i krytycznych błędów,
idle ≥300 s i ≥1000 operacji. NF-08 pełne scale może pozostać NOT_RUN; pomiar Host
idle i jego negatywny wynik pozostają widoczne. NF-11 NOT_RUN nie blokuje ograniczonej
D-11, ale blokuje pełną kwalifikację; nie uruchamiamy 8 h soak. Produkcja NOT_RUN.

## Dossier B1 — review claims, nie uprawnienia

JSON ma dokładnie poniższe pola (unknown/duplicate/NaN/oversize odrzucane).
Przykład jest **nieaktywnym szablonem**, placeholdery uniemożliwiają READY:

```json
{
  "schema_version": "1.0",
  "scope": {
    "project_name": "T05_RecipeSim_V19_Testowy", "plan_id": "E2-B1",
    "operation_method": "project.inspect", "operation_scope": "PROJECT",
    "profile": "v19.discovery.v1", "inventory_scope": "all_supported", "fresh": true,
    "maximum_cycles": 20, "maximum_devices": 64, "maximum_nodes": 256, "maximum_depth": 8,
    "timeout_seconds": 120, "queue_timeout_seconds": 300,
    "apartment": "STA", "max_in_flight": 1, "target_count": 1
  },
  "read_scope": [
    "TiaPortal.GetProcesses", "TiaPortalProcess.Attach", "TiaPortal.Projects",
    "Project.Path", "Project.Name", "Project.IsModified", "Project.Devices",
    "Device.Name", "Device.DeviceItems", "DeviceItem.Name", "DeviceItem.DeviceItems",
    "DeviceItem.GetService<SoftwareContainer>", "SoftwareContainer.Software"
  ],
  "plan_sha256": "<exact-current-B1-plan-hash>",
  "expected_binding": {
    "project_key": "<exact-reviewed-UUID>", "project_path": "<exact-local-AP19>",
    "tia_pid": 0, "tia_start_ticks": 0, "executable_path": "<exact-Portal-EXE>",
    "session_sid": "<actual-SID>", "tia_build": "<API-FileVersion>",
    "worker_build": "1.0+<Worker-EXE-SHA256>", "host_build": "1.0+<Host-DLL-SHA256>"
  },
  "observed_binding": {
    "project_key": "<independently-reviewed-UUID>", "project_path": "<confirmed-AP19>",
    "tia_pid": 0, "tia_start_ticks": 0, "executable_path": "<confirmed-Portal-EXE>",
    "session_sid": "<confirmed-SID>", "tia_build": "<confirmed-API-FileVersion>",
    "worker_build": "1.0+<qualified-Worker-SHA256>", "host_build": "1.0+<qualified-Host-SHA256>"
  },
  "clean_test_copy_confirmed": false, "ui_clean_confirmed": false,
  "no_colliding_siemens_operations": false, "monitoring_ready": false,
  "safe_stop_ready": false, "bootstrap_state": "INACTIVE",
  "is_modified_before_start": "PENDING_AUTHORIZED_READ"
}
```

Operator porównuje expected z niezależnie przejrzanymi Windows/UI/build observations.
Sama zgodność dwóch twierdzeń JSON nie dowodzi rzeczywistego stanu procesu/projektu.
B0 nadal sprawdza chronioną konfigurację, ACL, SID, exact żywy PID/start/EXE i buildy;
Worker po osobno autoryzowanym Attach potwierdza exact projekt oraz IsModified=false
przed inventory i false/false w każdym cyklu. Nie wolno pozyskiwać tej wartości
nieautoryzowanym odczytem na potrzeby READY. Dossier nie jest authorization.json
i nie jest wejściem bootstrap-authorize. Tylko istniejący [B0](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md)
może po osobnej decyzji operatora aktywować ograniczony grant z oknem i 20 cyklami.

Kody: QUALIFICATION_EVIDENCE_INVALID, SAFETY_TESTS_NOT_PASS, SAFETY_RT-xx,
D11_NF-xx, PERFORMANCE_SAMPLES_NOT_PASS, D11_IDLE_INCOMPLETE,
D11_OPERATIONS_INCOMPLETE, RESOURCE_SUBTEST_FAILED; B1_DOSSIER_MISSING,
DOSSIER_SCHEMA, B1_SCOPE_MISMATCH, READ_SCOPE_MISMATCH, PLAN_HASH_MISMATCH,
BINDING_INCOMPLETE, PROJECT_KEY_INVALID, BINDING_DRIFT_&lt;FIELD&gt;,
PROCESS_IDENTITY_INVALID, PATH_INVALID, PROJECT_PATH_INVALID, SID_INVALID,
TIA_BUILD_INVALID, BUILD_MISMATCH, REVIEW_&lt;FIELD&gt;, BOOTSTRAP_STATE_REVIEW,
DIRTY_OR_UNKNOWN_PROJECT i DOSSIER_INVALID. Opis wskazuje konkretną rozbieżność.

E2-B2 jest odrzucane. Dopiero B1 qualification PASS, pełny manifest/release/trend review
i oddzielna decyzja mogą otworzyć nowe przygotowanie B2. Instrukcja działań operatora:
[E2_OPERATOR](E2_OPERATOR.md). Timeout/hang/unknown → STOP bez retry/Save/kill.
