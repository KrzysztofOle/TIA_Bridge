# E2-B1-D — zatwierdzony zakres HANDLE_DIAGNOSTICS

Decyzja użytkownika: 2026-10-27, osobno od kwalifikacji E2-B1.
Jedyny target: T05_RecipeSim_V19_Testowy. Drugi projekt i E2-B2 są zabronione.
Plan jest wejściem hash bindingu; wyniki trafiają do osobnego
[raportu diagnostycznego](B03A_E2_B1_D_HANDLE_DIAGNOSTICS.md).

## Stały zakres

Maksymalnie **3 sekwencyjne** rzeczywiste cykle na jednym Workerze STA i jednym
TIA PID/start/EXE/SID/AP19/ProjectKey; max_in_flight=1. project.inspect/PROJECT,
v19.discovery.v1, all_supported, fresh=true, timeout 120 s, queue 300 s,
64 Device, 256 nodes, MaxDepth 8. Automatic retry=false. Exact 13 getterów
z [B0](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md) bez rozszerzeń.
Każdy cykl: Attach → IsModifiedBefore → DiscoveryV1 → IsModifiedAfter → Dispose.
Wymagane false/false, Dispose.Returned i quiescent receipt.

## Wejście i autoryzacja

1. Nowy lokalny run/state na fixed NTFS, chronione katalogi/config i actual SID.
   Nie zmieniać historycznego state lub ACL projektu. Nie generować klucza E1.
2. Świeże Windows/UI i kontrola kopii, praw read-only, AP19, procesu i wersji API.
   Bez historycznego bindingu. ProjectKey jest lokalnym UUID Bridge, nie natywnym ID Siemens.
3. Ograniczona D-11 na SHARED_WORKSTATION 1.0.0 dla exact aktualnych źródeł/buildów:
   pełne Core/Host/Python, safety E1/B0 + diagnostic, ≥1000 Fake operacji, idle ≥300 s.
   Normalne obciążenie stanowiska i PASS_WITH_LOAD dozwolone. NF-11 nie uruchamiać.
4. Dossier 1.1 i istniejący e2_gate.py --experiment HANDLE_DIAGNOSTICS muszą zwrócić
   READY_FOR_OPERATOR_AUTHORIZATION. To nie jest grant. IsModified może być
   PENDING_AUTHORIZED_READ; pierwszy autoryzowany Attach potwierdza false przed inventory.
5. Nowy dokument bootstrap schema 1.1, purpose HANDLE_DIAGNOSTICS, plan_id E2-B1-D,
   maximum_cycles=3, exact hash tego planu, buildy/binding i ograniczone okno.
   bootstrap-inspect i jawne bootstrap-authorize z exact document hash oraz SID.
   Stary grant 652cf95d-be6e-4e88-838b-826f7f314a2f pozostaje zakończony, bez reuse.

## Pomiary i przebieg

Włączyć TIA_BRIDGE_V19_RESOURCE_TRACE=1 tylko dla własnego Host/Workera.
Każdy cykl ma osobny Operation ID i zachowane request/acceptance/status/health/
events/raw/Discovery/receipts, pełną telemetry i audit. Istniejący scheduler/IPC/owner
jest jedyną ścieżką Attach. Bez osobnego harnessu dwu-projektowego.

Trace Windows/CLR: worker.start, cycle.enter, enumeration.returned, attach.enter/
returned, project.checked, devices.enter/returned, software.returned.null/present,
device_items.traversed, device.classified, inventory.returned, dispose.enter/
returned, cycle.returned i worker.idle z ostatnim Operation ID. Software marker
obejmuje GetService + Software, bez dodatkowych getterów. Każda próbka zachowuje
PID/start/epoch/Operation ID, handles/WS/PB/managed bytes/threads/GC0–2 i zegar
monotoniczny. Nie wymuszać GC. Sampler zewnętrzny 1 s obejmuje własny Host/Worker/TIA.
Trace jest ograniczony do 4096 rekordów/1 MiB; błąd/limit oznacza STOP, bez rotacji.

Po każdym potwierdzonym cyklu 10 s idle i kontrola kompletności faz przed kolejnym
submit. Po trzecim co najmniej 30 s idle, kontrolowany stop własnego Host i release
własnego Workera w 10 s. Przy niepewnym call nie zabijać procesów. TIA pozostaje otwarta.
Zachować wszystkie próbki, także outliery, braki, ujemne delty i naturalne GC.

Klasyfikacja Event/File/Thread/Mutant/Section/Semaphore/Key/ALPC/Other wyłącznie
przy dostępnej bezpiecznej obserwacji bez injection/debug attach/CloseHandle obcych
uchwytów, zmiany zabezpieczeń lub podnoszenia praw. W przeciwnym razie NOT_AVAILABLE
z konkretnym powodem, bez obejść. Typ nie dowodzi właściciela.

## STOP i wyjście

STOP kolejnych cykli: drift binding/build, IsModified=true, brak wymaganej telemetry,
błąd read/trace/integrity, timeout/worker loss/niepewny native call, niepotwierdzony
Dispose, nieoczekiwany dialog, nieautoryzowana zmiana lub niekontrolowany wzrost
zasobów. Zachować dowody, bez retry, Save, napraw, resetu TIA lub odnowienia grantu.
Nigdy Compile/Download/Import/Export/Archive/RUN/STOP PLC, symulacja lub Runtime.

Budżet rezerwowany trwale przed IPC; crash zużywa rezerwację, restart/STOP nie
odnawia sesji. Czwarty dispatch zabroniony. Pełna seria daje co najwyżej
DIAGNOSTIC_COMPLETE po release/integrity; bootstrap-review nie tworzy B1 PASS.
Wynik analizy: ROOT_CAUSE_CONFIRMED / PROBABLE / UNEXPLAINED, z porównaniem B1/R.
Kryteria 20-cyklowego B1 pozostają bez zmian. B1 historical BLOCKED, nowa kwalifikacja
B1/B2/E1 enrollment/produkcja NOT_RUN. Brak rozstrzygnięcia wymaga nowej osobnej decyzji.
