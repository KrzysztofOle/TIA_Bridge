# B-03A/E2-A — pakiet przygotowania Engineering V19

Data: 2026-10-27. Baza: `97e9f803a583d727e53fb366d13a316baaaaaab7`.
Zakres wykonany: analiza kodu i konfiguracji, dokumentacja, syntetyczna regresja offline.
Aktualizacja B-03A/E2-B0: [Controlled Qualification Bootstrap](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md)
implementuje zatwierdzoną późniejszym zleceniem ścieżkę qualification-only. E2-A
pozostaje historycznym pakietem przygotowania; bieżące wejście B1 może użyć lokalnego
grantu B0 zamiast nieistniejącego wcześniejszego PASS. Nie jest to zgoda na live.
Nie wykonano Openness, enumeracji vendor, Attach, uruchamiania TIA, rejestracji rzeczywistych
dowodów, generowania klucza ani zmiany ACL deploymentu. Ten pakiet jest materiałem
do zatwierdzenia, **nie grantem Engineering**.

Kolejność obowiązująca od E2-A: [E2-B1](E2-B1_TEST_PLAN.md) —
`T05_RecipeSim_V19_Testowy`, następnie [E2-B2](E2-B2_TEST_PLAN.md) —
`T04_T05-000_Testowy`. Jedna aktywna konfiguracja, jeden target i jeden odczyt naraz.
[Instrukcja operatora](E2_OPERATOR.md). Historyczne oznaczenia A/B z X-01 nie określają
tej kolejności i nie są kwalifikacją nowych getterów ani artefaktów.

## E1 — wynik przeglądu implementacji

| Mechanizm | Źródło i ustalenie |
|---|---|
| Konfiguracja | [HostConfiguration](../src/TiaBridge.Host/Api/HostConfiguration.cs): opcjonalny trust, najwyżej jeden publiczny SPKI P-256, jawne SID-y przy aktywnym trust; ścisły JSON; Fake i V19 rozłączne |
| Registry v2 | [V19InspectionProfile](../src/TiaBridge.Host/Scheduling/V19InspectionProfile.cs): stały `state_path/capability-registry.v2.json`, brak → DenyAllV2; exact `project.inspect`, PROJECT, null family, `project`, null language, profil `v19.discovery.v1`, write scope pusty, STRUCTURE COMPLETE |
| Dowody | [Provider](../src/TiaBridge.Host/Scheduling/QualificationEvidenceProvider.cs), [store](../src/TiaBridge.Host/Qualification/LocalTrustStore.cs): brak anchora/store/aktywnej rewizji → deny; podpis, lifecycle, pakiet i rzeczywiste hashe artefaktów, exact ProjectKey/build/profile/getters; brak fallback po revoke |
| Przyjęcie | Durable acceptance nie jest grantem. `Resolve` działa w schedulerze, nie wykonuje Attach w HTTP. Przy braku kwalifikacji operacja może być przyjęta, a potem FAILED/UNSUPPORTED bez wysłania call |
| Dispatch | [Scheduler](../src/TiaBridge.Host/Scheduling/OperationScheduler.cs): Resolve przed startem workera, Revalidate przed PrepareDispatch i bezpośrednio przed IPC; brak trwałego uprawnienia z wcześniejszego admission |
| Enrollment | [LocalEnrollment](../src/TiaBridge.Qualification/LocalEnrollment.cs): inspect → jawny approve exact package hash/SID → enroll istniejącym CNG → verify/list; create-new, flush, immutable revisions, ręczne revoke/rotation; bez endpointu HTTP |
| ACL | [Walidator](../src/TiaBridge.Host/Qualification/LocalTrustAcl.cs) tylko odczytuje owner/DACL, przodków i prawa tokenu. [Procedura 18](18_LOCAL_TRUSTED_ENROLLMENT.md) odpowiada przetestowanemu Host/operator na tym samym SID; oddzielne konto usługowe nie jest kwalifikowane |

**E1 LOCAL TRUSTED: PASS oznacza gotowość mechanizmu offline**, nie aktywowany deployment.
W read-only snapshot środowiska domyślne `%LOCALAPPDATA%/TIA_Bridge2/state` nie istniało;
nie ma tam realnej konfiguracji ani store do zatwierdzenia. C: i X: były Fixed/NTFS.
Widać cztery już działające procesy Portal V19; ich projektów nie odczytywano. Brak
widocznego procesu Host/Worker nie dowodzi braku innych klientów Openness.
Nie przeszukiwano prywatnych kluczy ani historycznych dev fixtures w celu ich adopcji.
Rzeczywisty SID przyszłego Host trzeba ponownie ustalić w jego sesji.

Model [17](17_TRUSTED_QUALIFICATION_EVIDENCE.md) i procedura 18 pozostają normatywne.
Same SID, token HTTP lub podpisane pole PASS nie dowodzą prawdziwości Engineering.
Provider dopuszcza rodzaje dowodu z modelu Core (także syntetyczne OFFLINE_TEST);
operator **nie może** nadać fixture znaczenia realnego Engineering. Nie wdrażać fixtures.
Nie deklarować izolacji pomiędzy procesami wspólnego SID ani przed administratorem.

## Otwarte wejście: bootstrap kwalifikacji — raport rozbieżności

**Stan wykryty w E2-A: E2 live start BLOCKED do odrębnego rozstrzygnięcia.** Nie jest to błąd
deny-by-default. [18, Warunki rozpoczęcia E2](18_LOCAL_TRUSTED_ENROLLMENT.md#warunki-rozpoczęcia-e2)
wymaga źródłowego dowodu i rozstrzygniętego bootstrapu; profil Host wymaga już aktywnego
PASS dla exact build/ProjectKey/profile/getters przed pierwszym odczytem. B-03A nie ma
takiego rzeczywistego dowodu. X-01 ma inny zakres i nie może go zastąpić.

| Wariant do decyzji użytkownika | Skutek |
|---|---|
| Dostarczyć istniejący rzeczywisty dowód exact zakresu, niezależnie zweryfikować i jawnie enroll | Obecny Host/CLI działa bez zmiany architektury; obecnie brak takiego źródła |
| Osobno zatwierdzić projekt i implementację ograniczonego eksperymentu bootstrap | Rekomendacja, gdy brak dowodu: przed live określić dopuszczalną ścieżkę qualification-only, ten sam Worker/owner/IPC/Host, pojedynczy binding, trwałą autoryzację i evidence; wymaga przeglądu dokumentu 17/18 i testów offline przed użyciem |
| Podpisać nowy PASS bez przebiegu, użyć synthetic albo wyłączyć provider | Niedopuszczalne; fałszuje kwalifikację lub obchodzi deny |

E2-A nie wybrało ani nie implementowało wyjątku. Późniejsze jawne zlecenie E2-B0
zatwierdziło drugi wariant: implementacja qualification-only działa offline na tej
samej ścieżce Host/SQLite/scheduler/Worker, z lokalną autoryzacją i trwałym budżetem.
Nie wymaga wcześniejszego PASS, nie tworzy go i nie wyłącza E1 w trybie normalnym.
Źródłem aktualnych warunków jest dokument 19; rzeczywisty grant i source run nadal
NOT_RUN. Podpis i enrollment są etapem po prawdziwym przebiegu i review.

## Pakiet autoryzacyjny do późniejszego wypełnienia

Użytkownik zatwierdza B1 osobno. B2 wymaga osobnej decyzji i własnego preflightu po B1 PASS.
Zapisz przed wykonaniem `authorization.md` poza Git z następującymi polami:

| Pole | Wymagana wartość / stan w E2-A |
|---|---|
| Decision ID, operator, zatwierdzający, UTC i okno sesji | PENDING_USER_APPROVAL; bez automatycznego przedłużenia |
| Plan i provenance | B1 albo B2, SHA-256 planu i niniejszego pakietu, Git SHA, wynik offline |
| Odbiorca | Exact AP19 absolutny path/root, ProjectKey Bridge UUID, PID/start UTC ticks, executable path/hash, SID; wszystkie NOT_BOUND |
| Build | Host i zależności, Worker EXE i nievendorowe zależności SHA-256, V19 DLL identity/FileVersion/hash, apartment STA |
| Zakres | DiscoveryV1, poniższe 13 pozycji read scope, pomocnicze kontrole tożsamości i Dispose wyłącznie własnego uchwytu Attach; 20 sekwencyjnych cykli |
| Prawo i dowód | Oddzielne prawo operatora do read-only test copy; zatwierdzone rozwiązanie bootstrapu albo exact source evidence/revision/verify; brak implikacji z tokenu HTTP |
| STOP i zasoby | Kryteria z planu, brak auto retry, monitoring procesu, obowiązkowe release i trend review |
| Wyłączenia | Save, Import, Export, Compile, Download, Archive, zmiany struktury, RUN/STOP, HMI/OPC write, symulacja, zamykanie cudzej TIA, odłączanie innych użytkowników, naprawa projektu |

Wartości NOT_BOUND nie są argumentami do uruchomienia. Nie wpisuj PID/path z historii.
Grant obejmuje standardowe kroki wyłącznie w wypełnionym, zatwierdzonym zakresie.
Konflikt, drift, zmiana buildu, rozszerzenie lub niepewne zakończenie blokują kolejne call.
Interakcja CODEX przez Computer Use wymaga dodatkowo wyboru A — CODEX / B — użytkownik
z konkretną czynnością, powodem i instrukcją ręczną (AGENTS); cisza nie daje zgody.

## Procedura PID/start/path/ProjectKey — do wykonania dopiero w E2

1. Operator wskazuje **własną kopię testową** na lokalnym NTFS, exact AP19 i dozwolony
   root. Nie wyszukuj odbiorcy rekurencyjnie w katalogach klientów. Nie adoptuj projektu
   po samej nazwie `Testowy`. Zapewnia brak edycji i innych aktywnych czynności w oknie
   pomiaru; nie odłączaj innych klientów. Gdy nie może tego zapewnić: BLOCKED.
2. Odczytaj Windows PID, `StartTime.ToUniversalTime().Ticks`, `MainModule.FileName`,
   token SID i hashe EXE. Brak dostępu nie jest zgodą na podniesienie uprawnień.
   Wymagany executable to `Program Files/Siemens/Automation/Portal V19/Bin/Siemens.Automation.Portal.exe`.
3. Dopiero po autoryzacji Engineering wykonaj istniejące `Worker --enumerate`
   ([instrukcja](../src/TiaBridge.Worker.V19/README.md)). To **wywołuje Openness**, choć
   nie robi Attach. Zapisz dokładny stdout jako prywatny artefakt. Musi istnieć jeden
   publiczny wpis zgodny z PID i exact AP19; null, wiele dopasowań, niezgodny start/path → STOP.
4. Ustal ProjectKey z jawnego rejestru bindingu Bridge. Nowa kopia dostaje nowy UUID;
   istniejący UUID wolno zachować tylko po review exact bindingu. UUID nie pochodzi
   z nazwy ani kolejności. `registered_projects` i `v19.targets` zawierają dokładnie
   jeden ten sam ProjectKey. Zapisz binding i jego hash jako artefakt tego run.
5. Zweryfikuj przed każdym cyklem PID/start/executable/SID/path i autoryzację. Worker
   sprawdza proces przed Attach, po Attach i po Dispose; po Attach wymaga jednego
   projektu i zgodnych `Project.Path` oraz `GetCurrentProcess().ProjectPath`.
6. Wymagaj `IsModifiedBefore=false` **i** `IsModifiedAfter=false` w raw observation
   i DiscoverySnapshot każdego cyklu. Operator przed startem potwierdza czysty stan
   z UI; publiczny odczyt IsModified wymaga Attach i nie istnieje jako pre-Attach
   getter w `--enumerate`. Nie dodawaj osobnego Attach do preflightu.
7. Po każdym cyklu i zakończeniu ponownie potwierdź tę samą instancję i stan UI,
   metadane plików oraz audit/release. Jeśli stan niepewny: zachowaj dowody i STOP.

Aktualizacja B0: [VendorReader](../src/TiaBridge.Worker.V19/VendorReader.cs) przy true
na wejściu pomija inventory i zwraca raw dirty observation po Dispose. Host wymaga
false/false; true/true jest odrzucane. Sam SUCCEEDED nadal nie zalicza całego E2.
True na wejściu → REVIEW_REQUIRED/BLOCKED wejścia lub FAIL wykonanego cyklu; false→true
lub inny niezamierzony drift → FAIL i STOP. Nigdy automatyczny Save/naprawa.
Równość bitów i metadanych nie dowodzi niezmienności całego projektu w pamięci.

## Exact profil DiscoveryV1

`profile=v19.discovery.v1`, `inventory_scope=all_supported`, `fresh=true`,
`timeout_s=120`, `queue_timeout_s=300`; STA, brak auto retry native read.
20 pełnych cykli Attach/read/Dispose na jednym target, na jednym Workerze w zdrowej
sesji; cykl 1 cold, 2–20 warm. Nie odrzucaj pierwszego cyklu z dowodów. To profil E2,
nie zmiana RT/NF, NF-08/NF-11 ani soak. Brak pełnych 20 wyników wyklucza PASS.

Exact registry/enrollment `read_scope` (pisownia jak w kodzie, bez `()`):

```text
TiaPortal.GetProcesses
TiaPortalProcess.Attach
TiaPortal.Projects
Project.Path
Project.Name
Project.IsModified
Project.Devices
Device.Name
Device.DeviceItems
DeviceItem.Name
DeviceItem.DeviceItems
DeviceItem.GetService<SoftwareContainer>
SoftwareContainer.Software
```

Limity stałe: 64 Device, 256 węzłów **łącznie z root Device**,
`V19DiscoveryProfile.MaxDepth=8`. W kodzie pierwszy DeviceItem ma depth=0,
dziecko depth+1; Device jest osobnym root, zatem maks. 9 krawędzi root→najgłębszy
DeviceItem. Nie interpretuj tego jako 8 krawędzi od Device. Limit → LIMIT_EXCEEDED,
FAIL profilu i STOP; nie zwiększaj limitów w trakcie kwalifikacji.

Zakres PLC: nazwy/hierarchia Device/DeviceItem i exact software type
`Siemens.Engineering.SW.PlcSoftware`; bez bloków, źródeł, tag tables i treści DB.
HMI: tylko klasyfikacja `Siemens.Engineering.HmiUnified.HmiSoftware` (UNIFIED)
lub `Siemens.Engineering.Hmi.HmiSoftware` (CLASSIC), bez ekranów, skryptów, tagów,
zdarzeń i native artefaktów. Unknown/null/mixed pozostają UNKNOWN; nie są brakiem obiektu.
Obie rodziny oceniać osobno według faktycznej obserwacji, bez wymuszania obecności Classic.
Przed startem operator zapisuje oczekiwane urządzenia/rodziny z UI dla danego projektu.
Brak potwierdzenia PLC/HMI oczekiwanego w tym projekcie blokuje kwalifikację tego zakresu.

Pomocnicze wywołania istniejącego Workera obejmują `GetType().FullName` (dokładny runtime
typ), `GetCurrentProcess().ProjectPath`, odczyty kolekcji, procesów Windows i wersji DLL,
callback `Disposed` oraz Dispose własnego uchwytu. Nie dopisuj ich jako nowych pozycji
read_scope bez zmiany kontraktu. Audit ma znaczniki faz, **nie ślad każdego gettera**.
Kod/typ publiczny/kompilacja nie dowodzą zachowania getterów na rzeczywistym V19;
każdą z powyższych pozycji należy potwierdzić w source run. Innych getterów nie próbować.

Coverage oczekiwane: STRUCTURE COMPLETE tylko w tym bounded profilu; CONTENT i
DEPENDENCIES UNSUPPORTED; CONFIG/API/INVENTORY PARTIAL z reasons. Identity device/node
UNAVAILABLE, public IDs null, brak trwałych ObjectId. Porównaj nazwy, typy i graf
każdego cyklu z pierwszym: różnice wyjaśnij na podstawie pełnych obserwacji; sam ordinal
nie jest stabilną tożsamością. Nie usuwaj nieznanych wartości, aby uzyskać zgodność.
Nie twórz CANONICAL/NATIVE HMI i nie ogłaszaj SYNCED (ADR 0001–0003 bez zmian).

## Przygotowanie konfiguracji bez aktywacji

Nie utworzono deployment config ani store. Poniższy szablon zapisuje się dopiero po
uzupełnieniu i zatwierdzeniu poza Git, pod per-user LOCALAPPDATA na fixed NTFS.
Dla qualification-only wymagane dodatkowo pola konfiguracji i lokalny dokument B0
z dokumentu 19. Sam poniższy szablon pozostaje deny-by-default.
Jest celowo niewykonalny: liczby 0 i placeholdery mają powodować odmowę.

```json
{
  "schema_version": "1.0",
  "state_path": "<LOCALAPPDATA>/TIA_Bridge2/dev/e2/<project-run-uuid>/state",
  "repository_root": "X:\\Projects\\TIA_Bridge",
  "registered_projects": ["<reviewed-project-key-uuid>"],
  "max_in_flight": 1,
  "enable_synthetic_mutations": false,
  "v19_worker_exe": "<LOCALAPPDATA>/TIA_Bridge2/dev/e2/<project-run-uuid>/worker/TiaBridge.Worker.V19.exe",
  "v19": {
    "targets": [{"project_key": "<reviewed-project-key-uuid>", "pid": 0, "start_ticks": 0, "project_path": "<exact-ap19>"}],
    "project_root": "<authorized-test-root>",
    "apartment": "STA"
  },
  "qualification_trust": {"trusted_signer_keys": []}
}
```

Po zatwierdzeniu deploymentu: rzeczywiste SID-y, jeden publiczny anchor oraz ACL według
18; bez sekretów w JSON. Host i operator na tym samym koncie desktopowym, bez usługi.
Stan/SQLite/artifacts i config poza repo, osobny root B1 i B2, żadnego kopiowania store
B1 do B2. Przed dodaniem store do nowego state należy zainicjować **pusty** state
istniejącym StorageLocation/Host bez zleceń i bez aktywnych anchorów, a następnie
kontrolowanie zatrzymać własny Host; w tym trybie brak dispatch nie uruchamia Workera.
Nie twórz markerów StorageLocation ręcznie i nie dodawaj inbox do nieoznaczonego
niepustego state: pierwszy start Host odmówi jego adopcji. Zachowaj istniejącą historię.
Późniejszy start wymaga weryfikacji, że nie ma oczekujących obcych/starych zleceń.
Nie kopiuj Siemens DLL obok Workera. `--loader-check` też ładuje vendor DLL,
więc pozostaje NOT_RUN w E2-A. Przed live kontrola wszystkich czterech DLL z
[VendorResolver](../src/TiaBridge.Worker.V19/VendorResolver.cs): exact paths/assembly
19.0.0.0/token/FileVersion/SHA-256, bez reparse/shadow; SHA nie jest na stałe przypięty
przez resolver — należy zapisać i porównać rzeczywiste bajty. Host używa API FileVersion
i Worker `1.0+sha256(EXE)`; adapter id `tia-bridge.worker.v19`, adapter build równy
Worker build; hash EXE nie zastępuje manifestu pozostałych zależności.

Registry: wzór kształtu [syntetycznego v2](../config/examples/capabilities.project.synthetic.json)
jest wyłącznie specyfikacją pól. Użyj rzeczywistego FileVersion, exact Worker/adapter
build, read scope powyżej, pustego write scope, właściwego evidence ID i odrębnej
registry_version per projekt; zachowaj raw SHA registry oraz CapabilitySetHash z Host.
Nie przenoś syntetycznych wartości do deploymentu. Eligibility wymaga exact ProjectKey,
`project_class=null`, PASS/ProjectInspect/PROJECT/STRUCTURE i powyższego profilu.
Wpis registry nie nadaje prawa. Enrollment real evidence wyłącznie po source-run review
i osobnej decyzji, według 18; w E2-A REAL ENGINEERING EVIDENCE: NOT_REGISTERED.

## Pomiary, trend i zwolnienie zasobów

Każda próbka zawiera UTC ISO 8601, monotonic elapsed ms, project/run/cycle/operation ID,
PID/start/path Workera, Host i TIA. Mierz Handles, Private Bytes, Working Set, CPU ms,
liczbę własnych procesów. Bez dostępu do licznika zapisuj błąd/brak, nie zero.
Punkty: przed uruchomieniem Host, Worker po handshake/przed pierwszym Attach,
przed i po każdym cyklu, po ostatnim Dispose, idle przed shutdown i po zakończeniu
własnego Workera. Zewnętrzny sampler Windows co 1 s podczas sesji; nie wywołuje TIA.
Po ostatnim odczycie obserwuj 30 s idle, potem kontrolowany stop własnego Host przez
stdin `stop`; potwierdź zakończenie **exact** Workera i brak własnych orphanów.
Brak końca w 10 s obserwacji shutdown → STOP/BLOCKED, nie kill. To budżety obserwacji,
nie dowód zatrzymania vendor call i nie nowy próg NF.

Obecny Host zapisuje WorkerResourcesBefore przed IPC i After po odebraniu receipt;
nie są to próbki wewnątrz Attach. Raw `V19Observation` zawiera EnumerationMs (wewnątrz
cyklu), AttachMs, ReadMs, DisposeMs, apartment/owner/callbacks/PortalAliveAfterDispose.
Oddzielnie rejestruj zewnętrzną enumerację i submit/end-to-end oraz status/health/result/events
latency. Nie nazywaj punktów Host dokładnym czasem wejścia w getter.
Post-Dispose wymagane audit `TiaPortal.DisposeAttachedSession.Returned` i zgodny żywy
TIA PID/start/path; samo PortalAliveAfterDispose=true nie dowodzi release wszystkich
vendor resources. Nie wywołuj `TiaPortalProcess.Dispose` ani Dispose cudzych sesji.

Trend: tabela surowych before/after/delta każdego cyklu i wykres Handles/Private Bytes/
Working Set względem czasu oraz numeru cyklu; osobno cold 1 i warm 2–20, bez ukrywania
warmup. Wylicz min/median/max, netto od pierwszej próbki, nachylenie warm (jednostka/cykl),
porównaj mediany warm 2–7, 8–13, 14–20 oraz idle. Zapisz algorytm i N. Dla czasów
p50/p95/max metodą nearest rank; p99 dopiero N≥100. Restart/podmiana PID dzieli serię;
nie wolno łączyć jej w pozornie płaski trend. Nie restartuj Workera, aby usunąć wzrost.

Nie ma dopuszczalnego progu „+N handles” ani dowodu leak na podstawie samego wzrostu.
Reviewer klasyfikuje STABLE_WITHIN_OBSERVATION, GROWING, INCONCLUSIVE, opisuje cold cache,
rozrzut i idle, korelację z fazami, brakujące próbki oraz nierozstrzygnięte przyczyny.
Niewyjaśniony utrzymujący się wzrost lub brak wystarczających pomiarów → RESOURCE_REVIEW_REQUIRED,
wynik kwalifikacji BLOCKED i brak B2; wyniki funkcjonalne zachowaj osobno. Po uzasadnionym
review można przyjąć ograniczony PASS bez twierdzenia o długiej stabilności; historyczny
Worker Handle Risk UNKNOWN pozostaje, NF-11 NOT_RUN. Operator może przerwać przy presji
zasobów; zapisz objaw i czas, nie dorabiaj nowego SLO.

## Dowody, raport i integralność każdego projektu

Nowy run UUID w `%LOCALAPPDATA%/TIA_Bridge2/dev/e2/<run-id>/`, B1 i B2 nigdy wspólne
run/evidence/operation IDs. Nie nadpisuj nieudanych prób, nie kopiuj bindingu lub PASS.
Minimalny zestaw:

```text
authorization.md, preflight.json, binding.json, operator-before-after.md
build-manifest.json, host-config.redacted.json, registry.json
project-files-before.json, project-files-after.json, project-files-review.md
enumeration.json, qualification-decisions.jsonl, worker-audit.jsonl
operations/<operation-id>/{acceptance,status,events,result}.json
artifacts/<artifact-id>                 raw observation i DiscoverySnapshot
telemetry.jsonl, cycles.csv, resource-review.md
release.json, report.json, report.md, evidence-manifest.json
```

Archiwizuj wszystkie strony events i oba artefakty po ID z result wraz z SHA.
Obecny Host/CLI **nie ma endpointu pobierania bajtów artefaktu**. Operator odczytuje
wyłącznie wskazane w result `artifact-[32 lowercase hex]` z zatwierdzonego lokalnego
`state_path/artifacts/<artifact-id>.blob`; sprawdza containment/reparse i SHA względem
ArtifactRefs, nigdy „ostatni plik” lub skan orphanów. Można użyć istniejącego
`store.Artifacts.OpenVerified(id)` w zatwierdzonym harnessie albo odczytu plikowego
operatora z niezależnym SHA-256. Brak dostępu → BLOCKED, bez poszerzania ACL.
Nie publikuj tokenu discovery, prywatnych kluczy, surowych projektów/logów w Git.
Zachowaj chronione oryginały config i ewentualnych DB/WAL; nie kopiuj na żywo samego DB
jako spójnego snapshotu. Redacted config jest osobnym artefaktem z opisem redakcji.
Metadane projektu zbieraj na wejściu przed Attach i na wyjściu po release, bez parsowania
baz Siemens: względne nazwy, rozmiar, mtime UTC, zgłoszone błędy odczytu. Hash AP19
i plików dających się bezpiecznie odczytać bez współbieżnego zapisu; nie wymuszaj locków.
Nieobserwowalny plik nie uzyskuje fikcyjnego hasha. Zmiany plików lock/cache opisuj
oddzielnie, bez domyślnej allowlisty; niewyjaśniony drift → BLOCKED (potwierdzona
niezamierzona modyfikacja → FAIL). Brak możliwości niezależnego potwierdzenia czystego
stanu przed/po blokuje PASS; metadane/hash AP19 nie są dowodem pełnej niezmienności P.

Manifest: uporządkowana lista relative path/bytes/SHA-256 dokładnych bajtów **wszystkich**
zamkniętych dowodów, bez samego manifestu, końcowego integrity receipt i bez sekretów.
Pliki immutable/create-new. Raport powstaje przed manifestem i zawiera jego nazwę,
nie jego przyszły hash; eliminuje to cykliczne hashowanie. Zewnętrzny SHA-256 manifestu
zapisz w końcowym integrity receipt poza manifestem; policz ponownie
każdy hash niezależnym odczytem (np. `Get-FileHash -Algorithm SHA256 -LiteralPath ...`).
Nie normalizuj EOL/Unicode. Błąd, brak, zmiana lub niezgodna liczba pozycji → BLOCKED;
nie zastępuj braków historycznym dowodem. Podpis Local Trusted nie zastępuje tej kontroli.

Raport `report.json`/`report.md` ma wspólny zestaw pól (format raportowy, nie nowy kontrakt API):

```text
task, project_name, project_key, run_id, authorization_id, source_git_sha, plan_sha256
utc_start/end, exact_bindings, builds_and_hashes, registry_version/capability_set_hash
preflight_status/reasons, bootstrap_decision/source_evidence_ref, cycles_requested/completed
cycle_results[{cycle, operation_id, state/outcome, times, counts, coverage, is_modified_before/after, artifact_refs}]
functional_result, project_state_review, telemetry_review, release_status
qualification_result(PASS/FAIL/BLOCKED), failed_checks, stop_reason, unresolved_risks
manifest_relative_path, integrity_review_ref, reviewer/decision_utc, next_project_gate
engineering_scope, runtime=NOT_RUN, production=NOT_RUN, nf11=NOT_RUN, worker_handle_risk=UNKNOWN
```

PASS kwalifikacji jest koniunkcją: wejście zatwierdzone + 20 poprawnych, zgodnych
obserwacji + exact binding/build/coverage + false/false + czysty stan po teście + pełne
hashowane dowody + potwierdzony release + akceptowalny ograniczony trend review + brak
nierozstrzygniętego błędu bezpieczeństwa. FAILED test → FAIL; brak dowodu/zgody lub
nierozstrzygnięty stan → BLOCKED. Report PASS nie aktywuje automatycznie Local Trusted.
Późniejszy pakiet enrollment musi mieścić się w limitach E1: maks. 64 artefakty,
64 MiB na plik i 128 MiB łącznie. Pełny katalog z 20 cykli może mieć więcej plików:
zachowaj oryginały, a do review pakietu użyj zamkniętego lokalnego bundle ZIP oraz
manifestu/raportu/receipt, z hashami zewnętrznymi i kontrolą wszystkich plików wewnątrz.
To pakowanie dowodów offline, bez Siemens Archive/Export. Przekroczenie limitu →
BLOCKED do review, bez pomijania nieudanych cykli lub zmiany limitów E1.

## Bramka B1 → B2

Review receipt B1 musi wskazywać run ID, kwalifikację PASS (nie tylko SUCCEEDED),
potwierdzenie braku niezamierzonej modyfikacji, manifest i niezależną kontrolę hashy,
release exact Workera, analizę trendu oraz brak nierozstrzygniętego błędu bezpieczeństwa.
Dopiero potem osobna decyzja B2, własne NTFS/state/ProjectKey/evidence/build review,
świeży PID/start/path/SID/IsModified i preflight B2. B2 nie uruchamia się automatycznie.
Istniejąca obca instancja B2 może pozostać otwarta; nie jest włączana do B1 ani zamykana.

## Walidacja E2-A i granice

Wyniki bieżącej regresji i ocena obu instrukcji są w [Zmiany.md](../Zmiany.md).
Surowe dowody przygotowania: ignorowany run `2d9c1300a6eb42869a5f1ad2d1fa4a15`
pod `.tia-bridge/e2-preparation/`. To dowody OFFLINE, nie source Engineering.
Nie ponawiano D-11/NF-11 ani historycznych pomiarów. Ostatni raport E1-L opisuje
ograniczoną D-11 PASS i pełną kwalifikację BLOCKED; przed live trzeba zweryfikować
wiążące dowody dla rzeczywistego buildu/profilu, nie zakładać świeżości z daty.

Offline odczytano także zainstalowany plik `PublicAPI/V19/Siemens.Engineering.xml`
(SHA-256 `873d699ff53c080f26f9fcfc839d4da95bf632ee15b1600ce3075d40185ee8ac`):
member `ProjectBase.IsModified` opisuje niezapisane zmiany, a `TiaPortalProcess.Dispose`
zamknięcie procesu. To dokumentacja/API_PRESENT, bez ładowania biblioteki i bez
READ_CONFIRMED dla B-03A. Pozostałych getterów nie kwalifikuje sama ich sygnatura.

Ryzyka V19: publiczny getter może rzucić/zawisnąć; null GetService jest niejednoznaczne;
brak trwałych IDs; snapshot procesu może się zestarzeć; Dispose i callback nie dowodzą
pełnego release; profile STA/MTA nie są wymienne bez kwalifikacji. Oficjalny
[opis diagnostyki V20](https://docs.tia.siemens.cloud/r/en-us/v20/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/general-functions/diagnostic-interfaces-on-tia-portal)
odróżnia snapshot TiaPortalProcess od aktywnej sesji i wskazuje, że jego Dispose zamyka
portal. To wyłącznie kontekst producenta, **nie potwierdzenie getterów V19**. Tutaj źródłem
exact zakresu jest kod V19; zachowanie live pozostaje NOT_RUN. Timeout nie kończy call;
zachowaj UNKNOWN/quarantine, bez retry, rollbacku, Save lub automatycznej naprawy.

D-11-P dodaje kandydacki profil kwalifikacji wydajnościowej `SHARED_WORKSTATION`; jego
progi i PASS_WITH_LOAD nie zmieniają D-11 PASS związanego z tym historycznym buildem ani
nie stanowią wejścia do E2-B1. Propozycja wymaga osobnej decyzji: obowiązkowe testy
bezpieczeństwa PASS, świeży shared profile PASS, zatwierdzony bootstrap qualification-only,
exact project/process binding, jawna zgoda live Engineering oraz niezależne
`IsModified=false` przed i po read-only cyklu. Kolejność projektów pozostaje
`T05_RecipeSim_V19_Testowy`, potem `T04_T05-000_Testowy`; nic nie jest uruchamiane przez
wybór profilu.
