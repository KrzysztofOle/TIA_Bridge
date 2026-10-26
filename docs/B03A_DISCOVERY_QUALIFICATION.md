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
B-03A ENGINEERING QUALIFICATION: BLOCKED/NOT_RUN
B-03A IMPLEMENTATION: INCOMPLETE_OFFLINE — CapabilityRegistry project-level semantics unresolved
B-03A OVERALL: BLOCKED
B-03B READINESS: NOT_READY
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

Core `CapabilityRegistry` pozostaje domyślnie deny i nie ma kwalifikowanego wpisu Siemens dla builda nowego B-03A. Bieżący Host utrzymuje historyczny, wąski X-01 allowlist dla `project.inspect`, ale nie ocenia jeszcze tego nowego Discovery snapshot przez Core registry. Nie deklarujemy WRITE/IMPORT/EXPORT. Przed rozszerzeniem dopuszczenia trzeba dodać dokładne, zaufane kwalifikacje związane z TIA build, Worker build, rodzajem/familią i profilem; tej luki nie maskuje status `STRUCTURE COMPLETE`.

Core registry `CapabilityKey` wymaga konkretnego `DeviceFamily`, natomiast wejściowa operacja inwentaryzuje projekt przed poznaniem rodzin, włącznie z nieznanym/mieszanym urządzeniem. Rozszerzenie registry/kontraktu lub niezależny model capability dla project-level Discovery wymaga osobnej, jawnej decyzji o semantyce. W tej dostawie stan ten pozostaje otwarty; jest to powód braku pełnego B-03A PASS.

## Wyniki i ograniczenia

- Nowe testy używają syntetycznych DTO. Przykładowa fixture testowa zawiera 2 urządzenia i 3 węzły; nie jest pomiarem projektu Siemens.
- Historyczne X-01 obserwowało 2 urządzenia / 58 nodes dla A oraz 2 / 14 dla B, w tym PLC i Unified HMI. Te liczby dotyczą historycznego DTO sprzed B-03A, nie nowej hierarchii z root Device.
- W nowym profilu żadna rodzina nie otrzymała świeżej kwalifikacji Engineering; liczby obiektów B-03A dla obu projektów są `NOT_RUN`.
- Nie zmieniono IPC DTO field-set, message envelope, schematów API v1, ProjectInspectResult ani wymagań IPC 1.1. Hierarchia Discovery jest zakodowana w wersjonowanym opaque `Locator`; szczegóły snapshotu trafiają do immutable artifact. Inventory pozostaje puste, dopóki nie istnieje stabilny ObjectId.
- Brak świeżego pomiaru Attach/read/Dispose, Handles, Private Bytes, Working Set i `IsModified` dla nowego builda. Nie deklaruje się stabilizacji zasobów.

## Walidacja implementacji offline

```text
BUILD RELEASE: PASS (TiaBridge.slnx oraz TiaBridge.Engineering.slnx; 0 warnings/errors)
V19OfflineTests: PASS (44/44)
FULL .NET: Core PASS (538/538); Host FAIL (253/254)
FLAKY HOST TEST RETRY: PASS (1/1; original suite failure remains recorded)
PYTHON CONTRACTS: PASS (31/31, isolated QA environment)
QUALITY AUDIT: PASS
FORMAT VERIFY: PASS
git diff --check: PASS
ENGINEERING LIVE: BLOCKED/NOT_RUN
RUNTIME: NOT_RUN
```

Jedyny pełnego zestawu Host failure: `ApiMetadataAndCliTests.Explicit_wait_is_bounded_and_cli_exit_does_not_cancel_the_operation` oczekiwał `RUNNING`, otrzymał `ACCEPTED`; pojedyncze ponowienie przeszło. Wynik traktujemy jako niestabilność testu/schedulera wymagającą osobnego triage, nie jako dowód PASS całej regresji. Pełny `scripts/validate_offline.py` poprzedzający końcowe zmiany zaliczył Host 251/251, Core 538/538, Python 31/31 oraz audyt/generatory/format, ale zakończył `FAIL` na etapie raportu: Windows `charmap` nie zakodował znaku Unicode. Historyczne NF-08 FAIL i progi nie zmieniono.

## Ocena wpływu dokumentacji i następny etap

`AGENTS.md` sekcja Zakres i stan była nieaktualna (stwierdzała brak istniejącego workera/hosta); poprawiono wyłącznie fakt bieżącego stanu. `Instrukcja_ChatGPT.md` sprawdzono sekcja po sekcji: pozostaje zgodna, nie zmieniono jej, bo określa role i granice, nie zaprzecza B-03A. ADR 0001/0002 zachowują granice Core/Worker, serializację i async Host. ADR 0003 pozostaje bez zmian i wyklucza rekonstrukcję HMI. Kontrakty HTTP/API nie zmieniły się; rozszerzono tylko wewnętrzny DTO payloadu raportu IPC 1.1, bez zmiany frame/envelope. Szczegóły wpływu są w `Zmiany.md`.

Następny krok: rozstrzygnąć model capability dla project-level discovery; następnie osobno autoryzować dokładne Engineering getter set, ponownie potwierdzić oba PID/start/path i workspace NTFS, wykonać świeże odczyty z monitoringiem zasobów i porównać strukturę, coverage oraz `IsModified`. Nie rozpoczynać B-03B.
