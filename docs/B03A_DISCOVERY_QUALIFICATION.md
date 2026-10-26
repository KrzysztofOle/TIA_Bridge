# B-03A — Discovery and Inventory V19

Data implementacji offline: 2026-10-26. Baza: `ea7a616d554805bf38860e4b8261507b4d5f046c`; worktree i `origin/main` były zgodne przed zmianami. Raport jest nowy i nie zmienia historycznego [X-01](X01_TIA_V19_QUALIFICATION.md), [D-11](../tests/resilience/X01_LIMITED_GATE.md), NF-08 ani NF-03 exFAT.

## Status

```text
X-01 ENGINEERING READ-ONLY: PASS (historyczne, ściśle ograniczone)
X-01 ENGINEERING TESTS: 93/93 PASS (historyczne; nowe testy nieuruchomione)
D-11 LIMITED GATE: PASS (historyczne)
OFFLINE CURRENT NTFS: FAIL — NF-08, CPU idle 1,0288% > 1,0%
HISTORICAL exFAT: FAIL — NF-03
NF-11: NOT_RUN
WORKER HANDLE RISK: UNKNOWN
B-03A READINESS: READY — bounded read-only only
B-03A OFFLINE: FAIL — test execution PASS; full runner blocked by resilience qualification
B-03A ENGINEERING: BLOCKED/NOT_RUN
B-03A IMPLEMENTATION: COMPLETE_OFFLINE
B-03A OVERALL: BLOCKED
B-03B READINESS: NOT_READY
FULL OFFLINE QUALIFICATION: FAIL
```

Nie otwierano TIA, nie wywoływano nowych getterów live i nie uruchamiano harnessu Engineering. Polecenie autoryzuje implementację oraz testy offline; nowe odczyty wymagają osobnej zgody i świeżego sprawdzenia obu bindingów. Historyczne X-01/A-B nie jest dowodem nowej kwalifikacji B-03A.

## Macierz X-01 → B-03A

| Funkcja | Stan X-01 | Brakujący zakres / zmiana B-03A | Dowód/test |
|---|---|---|---|
| Worker net48/x64 | Zbudowany, osobny proces x64 | Zachowano istniejącego workera | Build Engineering; istniejące testy startu |
| Exact DLL resolver | Allowlist V19, exact assembly identity/hash, shadow odrzucony | Bez zmian | X-01 historyczne loader checks; nie powtarzano |
| IPC 1.1 | HMAC, sequence, epoch/fence, bounded message i receipt | Zachowano pole-set DTO; metadane hierarchii są wersjonowane wewnątrz dotychczasowego opaque `Locator` | test round-trip i field-set; .NET 4.8 IPC probe |
| PID/start binding | PID, start, executable, SID i exact AP19 weryfikowane | Bez zmiany mechanizmu | istniejące testy i X-01 historyczne |
| Project identity | Bridge ProjectKey + path/name; brak trwałego Siemens ID | Snapshot zapisuje ProjectKey, exact path/name, PID/start, API build; stan `EXACT_REGISTERED_BINDING` | syntetyczne projekcje i negatywne binding tests |
| Device inventory | Bounded Device + DeviceItem metadata | Jawne nazwy, observation-local relacje, HMI kind; brak ID oznaczony `UNAVAILABLE` | testy duplikatów, Unicode i relacji |
| PLC software | Klasyfikacja exact `Siemens.Engineering.SW.PlcSoftware` | Jawna rodzina `PLC`, inne nazwy UNKNOWN | exact family tests; brak nowego live testu |
| HMI software | X-01 rozpoznawał Unified, w wyniku ogólnym HMI | Rozdzielenie exact Unified/Classic typów; klasyfikacja nie oznacza obsługi API ekranów | testy exact names, mixed/untrusted types; bez live Classic |
| Hierarchy | Locator ścieżkowy z nazw | Indeksy obserwacji i jawny parent/device link; indeks nie jest ID | testy parent/cross-device/cycle/depth |
| Object locator | Nie był stabilnym ObjectId | Oryginalna nazwa i typ; public ID i validity scope tylko gdy istnieje; obecnie null/UNAVAILABLE | test powtarzanych nazw i brak fikcyjnego ObjectId |
| Coverage | Ogólne PARTIAL/UNSUPPORTED | W snapshot: CONTENT UNSUPPORTED, CONFIG PARTIAL, API PARTIAL, STRUCTURE COMPLETE tylko w profilu, DEPENDENCIES UNSUPPORTED, INVENTORY PARTIAL | test snapshotu i brak fałszywego sukcesu |
| Timeout/recovery | Istniejący scheduler, owner, cancellation/deadline, late receipt, quarantine i brak retry | Zachowano bez drugiego Attach/IPC/schedulera | pełna regresja offline; Engineering NOT_RUN |
| Resource monitoring | Historyczny trend handles wzrastał | Nie uruchomiono odczytu; nie ma nowego pomiaru ani tezy o stabilizacji | `WORKER HANDLE RISK: UNKNOWN` |

## Profil i rzeczywiste gettery

DiscoveryV1 jest stałym profilem, nie polem requestu: maks. 64 Device, 256 węzłów łącznie z root Device, głębokość DeviceItem do 8. Odczyt zatrzymuje się błędem `LIMIT_EXCEEDED` po przekroczeniu limitu. Klient wciąż nie może wskazać dowolnego projektu: Host wybiera wyłącznie wcześniej zarejestrowane ProjectKey/PID/start/path; scope pozostaje `all_supported`, fresh. Artefakt Discovery przechowuje dokładny WorkerBuild `1.0+<SHA-256>` zweryfikowany przez handshake Host oraz TIA API FileVersion.

Worker używa: `TiaPortal.GetProcesses`, `TiaPortalProcess.Attach`, `TiaPortal.Projects`, `Project.Path`, `Project.Name`, `Project.IsModified`, `Project.Devices`, `Device.Name`, `Device.DeviceItems`, `DeviceItem.Name`, `DeviceItem.DeviceItems`, `DeviceItem.GetService<SoftwareContainer>()`, `SoftwareContainer.Software` oraz `GetType().FullName` dla exact runtime type classification. Dodatkowo kontroluje Windows `Process.StartTime`, `MainModule.FileName`, token SID i project path. Nie odpytuje nieograniczonej listy getterów, nie używa reflection do properties i nie wywołuje setterów.

`GetService` bez wartości nie dowodzi braku software; jego stan jest `UNKNOWN`. Typy nieznane i rodzice mieszani nie są zgadywane. ProjectKey jest tożsamością Bridge z rejestru, a nie natywnym Siemens ID. Dla Device/DeviceItem nie potwierdzono trwałego ID. Observation ordinals służą wyłącznie do relacji w artefakcie; rename/move nie jest automatycznie dopasowywany. `IsModified` musi mieć tę samą wartość przed i po odczycie.

HMI Discovery ogranicza się do klasyfikacji publicznego runtime typu `HmiSoftware`: `HmiUnified` lub `Hmi` (Classic). Nie odczytuje ekranów, elementów, dynamizacji, zdarzeń ani skryptów. Nie tworzy obiektu CANONICAL lub NATIVE z częściowego odczytu, zgodnie z ADR 0003.

## Capabilities i otwarty zakres

Registry v1 i jego `CapabilityKey` pozostały bez zmian. Dodano zamknięty, oddzielny registry v2, który modeluje `OperationScope` niezależnie od `DeviceFamily`; `project.inspect` jest PROJECT, `device_family` musi być null, a kwalifikacja DEVICE/OBJECT wymaga jawnej rodziny. Klucz jest exact-match po TIA FileVersion, Worker SHA-256 build, adapter id/build, operacji, profilu i ograniczonym getter read scope. Nie ma dziedziczenia PROJECT→DEVICE/OBJECT, ani READ→EXPORT/IMPORT/WRITE. Host odmawia dispatchu przed uruchomieniem Workera, gdy capability lub exact profile/build/kwalifikacja nie pasują.

**Aktualizacja B-03A/E1:** Host posiada provider podpisanych qualification evidence, dokładne dopasowanie w Core oraz ponowną ocenę przed dispatch i wysłaniem IPC. `evidence_id` z registry jest wyłącznie selektorem; Host weryfikuje podpis względem operatorowych trust anchors, semantic scope/build/project i SHA-256 artefaktów. Konfiguracja deploymentu nadal nie zawiera anchorów ani rekordów; real Engineering evidence pozostaje `NOT_REGISTERED`, Engineering pozostaje `BLOCKED/NOT_RUN`. Testy używają wyłącznie efemerycznych kluczy i syntetycznych artefaktów. Szczegóły formatu, bootstrapu, journalu i ograniczeń ACL są w [dokumencie 17](17_TRUSTED_QUALIFICATION_EVIDENCE.md). HTTP/API v1, registry v1/v2 i IPC 1.1 nie zmieniły kształtu.

## Wyniki i ograniczenia

- Nowe testy używają syntetycznych DTO. Przykładowa fixture testowa zawiera 2 urządzenia i 3 węzły; nie jest pomiarem projektu Siemens.
- Historyczne X-01 obserwowało 2 urządzenia / 58 nodes dla A oraz 2 / 14 dla B, w tym PLC i Unified HMI. Te liczby dotyczą historycznego DTO sprzed B-03A, nie nowej hierarchii z root Device.
- W nowym profilu żadna rodzina nie otrzymała świeżej kwalifikacji Engineering; liczby obiektów B-03A dla obu projektów są `NOT_RUN`.
- Nie zmieniono IPC DTO field-set, message envelope, schematów API v1, ProjectInspectResult ani wymagań IPC 1.1. Hierarchia Discovery jest zakodowana w wersjonowanym opaque `Locator`; szczegóły snapshotu trafiają do immutable artifact. Inventory pozostaje puste, dopóki nie istnieje stabilny ObjectId.
- Brak świeżego pomiaru Attach/read/Dispose, Handles, Private Bytes, Working Set i `IsModified` dla nowego builda. Nie deklaruje się stabilizacji zasobów.

## Walidacja implementacji offline

```text
BUILD RELEASE: PASS (locked restore/build; 0 warnings/errors)
FULL .NET: PASS (Host 255/255, Core 546/546; total 801/801, 0 skipped)
PYTHON CONTRACTS / IPC / CAPABILITIES / SCHEDULER: PASS (33/33)
V19OfflineTests: PASS (45/45)
HOST CLI BOUNDED-WAIT REGRESSION: PASS (1/1)
NF-02 PERFORMANCE REGRESSION: PASS in final runner (submit p95 156.9138 ms); an earlier standalone full-suite run measured 298.0946 ms and failed that assertion, while an isolated rerun passed
UNICODE CP1252 CONSOLE REGRESSION: PASS
GENERATORS / QUALITY AUDIT / FORMAT / DIFF CHECK: PASS
FULL OFFLINE RUNNER: FAIL — test execution PASS; resilience gate FAIL
ENGINEERING LIVE: NOT_RUN
RUNTIME: NOT_RUN
```

Finalny pojedynczy runner działał z `PYTHONIOENCODING=cp1252`, `PYTHONUTF8=0`, po czym skrypt jawnie ustawił UTF-8 strict. Test execution: .NET 801/801, Python 33/33, zero pominiętych. Runner zakończył kodem 1 z powodu istniejącej resilience qualification, a nie błędu testów/builda/raportowania: NF-03 dla woluminu repozytorium X: nie jest kwalifikowany (niezależny replace bez workera p95 115.8212 ms); NF-11 ośmiogodzinny soak pozostaje NOT_RUN. Gate blockers i wyniki zachowano w ignorowanym `.tia-bridge/resilience/run.json`. To zadanie nie zmienia NF-08 historycznego FAIL 1,0288% > 1,0%, NF-03 exFAT FAIL ani NF-11 NOT_RUN. Wcześniejszy oddzielny pełny test .NET zgłosił przejściowy NF-02 submit p95 298.0946 ms; powtórzony izolowany test i końcowy runner przeszły (156.9138 ms). Nie zmieniano progu NF-02 ani testu.

## Ocena wpływu dokumentacji i następny etap

`AGENTS.md` i `Instrukcja_ChatGPT.md` sprawdzono sekcja po sekcji: pozostają zgodne, nie zmieniono ich, bo zasady scope/capability pozostają w zatwierdzonym kontrakcie, a workflow/autoryzacja nie uległy zmianie. ADR 0001–0003 pozostają bez zmian: V2 jest wersjonowanym dodatkiem dla capability registry, nie zmienia Workspace/SYNCED, granic Core/Worker ani semantyki HMI. HTTP/API v1, IPC envelope i registry v1 pozostały kompatybilne. Szczegóły wpływu są w `Zmiany.md`.

Następny krok: usunąć lub rozstrzygnąć istniejące blockers pełnej offline resilience qualification według obowiązujących decyzji (bez zmiany progów), potem osobno zatwierdzić dokładny zakres testów Engineering, ponownie potwierdzić oba PID/start/path i workspace NTFS, wykonać świeże odczyty z monitoringiem zasobów i porównać strukturę, coverage oraz `IsModified`. Nie rozpoczynać B-03B.
