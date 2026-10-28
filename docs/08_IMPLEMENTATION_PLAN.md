# 08 — Etapowy backlog implementacyjny

Plan przygotowany 2026-10-24/25. Obecne zadanie dostarcza projekt etapu 0 i kontrakty **projektowe**, nie gotowe etapy 1–11. Każdy pakiet poniżej jest kandydatem osobnego commita. Nie kopiujemy starego monolitu ani jego stanów sesji. `MVP` to minimalny zweryfikowany przepływ PLC; `REQUIRED` obejmuje pełny docelowy zakres; `OPTIONAL` to rozszerzenie po MVP.

## Kolejność dostarczenia

Najkrótsza droga: **1A→2A–2D na fake→X-01→3A/3B read-only→4A/4B dla jednej FC→5A→6A→ograniczone 4C Apply**. Przed integracją z rzeczywistym Siemens Openness, także przed X-01, wymagana jest pozytywna ograniczona bramka B-02A–D opisana w [07](07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only), zgodnie z D-11. Pełna kwalifikacja stabilności produkcyjnej pozostaje osobnym etapem B-10B, z NF-11 ≥8 h. Pozostałe zależności pakietów poniżej nadal obowiązują. Bez qualification importer pozostaje disabled. Równoległe moduły logiczne mogą być rozwijane osobno, ale nie uruchamiają jednocześnie vendor calls do tego samego portalu. HMI, symulatory i E2E nie blokują pierwszego PLC MVP. Odporność jest budowana od2B; etap 10 rozszerza ją, nie odkłada bezpieczeństwa na koniec.

Pierwszy task CODEX: zbudować kontrakty + host na fake, `submit/status/result/cancel`, durable idempotency i test wiszącego wykonawcy. Bez Siemens DLL, uprawnień TIA ani importów. Dopiero kolejny task X-01 daje read-only worker V19.

## Etap 0 — Audyt i środowisko [MVP]

**Cel:** odtwarzalna podstawa decyzji, z oddzieleniem kodu/historii/live. **Zależności:** materiały wejściowe nowego repo, dostęp read-only do starego Bridge. **Pakiety:**

| ID | Zadanie / pliki | Kryterium oddzielnego commita |
|---|---|---|
| B-00A | audyt `docs/01`, source-index, pomiary zastanego środowiska `docs/10` | każdy OB ma lokalizację/mechanizm/skutek/priorytet/rozwiązanie/dowód; zero zmian starego kodu |
| B-00B | architektura `docs/02–09`, ADR, kontrakty i przykłady | wszystkie 6 etapów i19 metod mają specyfikację; ograniczenia są jawne |
| B-00C | dokumentacja Snap7 `docs/11`, analiza bibliotek i środowiska | rozdzielone native/python, TCP endpoint nieuznany za CPU; plan X-08 |

**Ryzyka:** dirty/drifting worktree starego repo i nieportable raw logs. **Testy:** ukierunkowane offline 94 PASS, read-only doctor/identify/inspect i OPC status, kontrola dokumentacji. **Akceptacja:** linki i schematy poprawne, source hashes, bez danych projektu/sekretów w Git, commit i próba push. **Rezultat:** obecny pakiet projektowy. **Bramka następnego etapu:** użytkownik zleca implementację ograniczonego pakietu; nie inferować jej z zaakceptowania tej dokumentacji.

## Etap 1 — Kontrakty i rdzeń [MVP]

Stan 2026-10-25: B-01A/B-01B zaimplementowane offline w [Core](../src/TiaBridge.Core/README.md); zakres i ograniczenia B-01B opisuje [12](12_WORKSPACE_CAPABILITIES.md). B-01C dostarcza [powtarzalną walidację i macierz pokrycia](../tests/COVERAGE.md), bez planera i B-02. Poniższe bramki następnych pakietów pozostają obowiązujące.

**Cel:** stabilny model i spójna semantyka klientów. **Zależności:** etap 0, D-01–D-06. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-01A | `contracts/v1`, `Core/Identity`, `Core/Operations`; doprecyzować schema dla wszystkich results/errors i OpenAPI | request/result per 19 metod, schema version, unknown fields, canonicalization i idempotency rules; generator klientów bez Siemens |
| B-01B | `Core/Workspace`, `Core/Capabilities`, config examples | UUID/locator/path codec, coverage i normalizer version; registry domyślnie deny |
| B-01C | `tests/contracts`, unit fixtures i pipeline offline | pozytywne i negatywne kontrakty, co najmniej 100 deterministycznych modeli/wejść w istniejącym Core, brak importów vendor; 100 rzeczywistych planów jest kryterium B-04A |

**Ryzyka:** zbyt szeroki schemat niechroniący semantyki, mieszanie operation lifecycle ze sync status. **Testy:** parsery/identity/collisions, permissions nie wynikają z samego JSON, contract matrix. **Akceptacja:** wszystkie nazwy/timeouts/errors spójne; brak arbitralnej ścieżki/komendy shell i sekretów w wire. **Rezultat:** typy i kontrakty konsumowane przez host/worker. **Bramka:** request schema i lifecycle zamrożone na pierwszą integrację, breaking changes przez version bump.

## Etap 2 — Silnik operacji, procesy i recovery [MVP]

Pakiet D-11-P wprowadza wersjonowane profile KPI offline opisane w [20](20_PERFORMANCE_QUALIFICATION_PROFILES.md): `STRICT` zachowuje dotychczasowe progi i domyślny wybór; `SHARED_WORKSTATION 1.0.0` jest zatwierdzonym profilem dla współdzielonego stanowiska. E2-G formalizuje [ograniczoną bramkę E2-B1](21_E2_B1_READINESS_GATE.md): wymagane safety i D-11 PASS, exact evidence/build/profile/binding, readiness B0 oraz osobna autoryzacja operatora. READY nie uruchamia Siemens i nie aktywuje B0. Pełna kwalifikacja pozostaje BLOCKED przez NF-11 NOT_RUN; pełny NF-08 nie jest fikcyjnym PASS. API/IPC, scheduler i timeouty ochronne pozostają bez zmian. Historyczne raporty i decyzje pozostają bez zmian.

Stan 2026-10-26: zrealizowano [B-02A — warstwę trwałości](13_DURABLE_STORAGE.md) oraz [B-02B — scheduler, supervisor i Fake Worker/IPC](14_SCHEDULER_IPC.md). Działa wyłącznie wykonanie syntetyczne. [B-02C — HTTP API i CLI](15_HTTP_API_CLI.md) udostępnia ten sam silnik. [B-02D](16_RESILIENCE_RECOVERY.md) dostarczył harness i historyczny raport RT/NF z OFFLINE/GATE FAIL. Zatwierdzona D-11 rozdziela jego wykonanie, ograniczoną bramkę NTFS i produkcję; [aktualna interpretacja dowodów](../tests/resilience/X01_LIMITED_GATE.md) nie zmienia snapshot. NF-11 minimum 8 h pozostaje NOT_RUN/B-10B, a NF-03 exFAT FAIL nie jest uchylony.

**Cel:** nieblokujący, trwały interfejs jeszcze przed TIA. **Zależności:**1A, częściowo 1B. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-02A | `Host/Storage`, `Host/Operations`, migrations, SQLite/outbox/artifacts | intent commit przed 202; unique key, terminal result survive restart; baza odmawia network/exFAT state location wg polityki |
| B-02B | `Host/Scheduling`, `Host/Supervision`, fake worker, IPC | one owner, epoch/fence/quarantine, heartbeat 2 s, deadline/backoff/breaker; unknown write nie zwalnia kolejki mutacji |
| B-02C | `Host/Api`, `Cli`, token discovery/ACL, status/result/events/cancel | klient kończy submit szybko; status NF-01 i edit W w NF-03 przy wiszącym fake; cancel queued nie dispatchuje |
| B-02D | crash/restart/late-result/parent-loss harness `tests/resilience` | RT-03/05/06/15/16 na fake; zero fałszywego sukcesu, zero podwójnego efektu |

**Ryzyka:** lease mylony z fencing w TIA, automatyczny kill workera po śmierci hosta, WAL/artifact durability. **Testy:** każdy journal boundary i host restart, bounded queue 1000, rate/status 8 klientów. **Akceptacja:** 100 powtórzonych submitów 1 ID, każdy zaakceptowany ID po crash dostępny, status p95≤100 ms, brak ponowienia unknown mutacji. **Rezultat:** działające async API+CLI na fake.

Zamknięcie prac B-02D wymaga wykonania i jawnego raportu RT/NF, bez ukrywania FAIL/NOT_RUN. Przejście do osobnego X-01 wymaga wszystkich warunków [ograniczonej bramki 07](07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only): RT-03/05/06/15/16, recovery/UNKNOWN/quarantine/epoch, NF-01/02/03/04/05/06/12/14/15 w kwalifikowanym lokalnym NTFS, rzeczywistego idle ≥5 min, ≥1000 operacji i braku krytycznych błędów. Pełny NF-11 nie jest warunkiem tego ograniczonego przejścia; nie wolno na tej podstawie ogłosić całego B-02 lub produkcji bezwarunkowo PASS.

| Raport | Zakres statusu |
|---|---|
| B-02 OFFLINE TEST EXECUTION | PASS/FAIL faktycznie wykonanych testów |
| B-02 OFFLINE QUALIFICATION | PASS/FAIL całego badanego profilu, z zachowaniem NF-03 exFAT FAIL |
| X-01 LIMITED READ-ONLY GATE | PASS/FAIL/BLOCKED tylko według powyższych warunków; osobne zadanie i autoryzacja X-01 |
| PRODUCTION QUALIFICATION | PASS/FAIL/NOT_RUN; pełna kwalifikacja późniejszych etapów, w tym B-10B/NF-11 i aktywne R-14 |

### Docelowy HMI canonical model (zatwierdzone D-12; dokumentacyjnie, NOT_IMPLEMENTED)

[ADR 0003](decisions/0003-hmi-canonical-model-and-native-artifacts.md) wymaga koncepcji kanonicznego modelu HMI przed pełnym Workspace HMI, logicznego podziału CANONICAL/SOURCE/NATIVE, zachowania niezmienionych eksportów Siemens w ExchangeUnit z SHA-256, niezależnych read-only kwalifikacji HMI **po X-01** oraz X-04 dla rzeczywistej struktury Unified. Oddzielne eksperymenty mają kwalifikować importy o zakresie replace/delete i nieznanych skutkach; V21 otrzyma osobny adapter i dowody per build. Brak implementacji i brak PASS. Nie rozszerza to X-01 ani nie zmienia głównej kolejności MVP PLC.

## Etap 3 — Discovery, eksport i mapowanie [MVP PLC / REQUIRED HMI]

**Cel:** kwalifikowana read-only projekcja projektu. **Zależności:**1B,2A–2D, X-01. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-03A / MVP | `Worker.V19` bounded Discovery/Inventory, Core DTO, Host projection, exact DLL loader, handshake | profile Device/DeviceItem ograniczone limitami; PID/start/project binding, IsModified, jawne rodziny/relacje/coverage; PROJECT-scoped `project.inspect` z osobną kwalifikacją DeviceFamily; brak stabilnego ID pozostaje jawny; E1 dodaje offline trusted-evidence provider. Real enrollment wymaga operatorowej polityki kluczy, ACL i revocation opisanej w [dokumencie 17](17_TRUSTED_QUALIFICATION_EVIDENCE.md); Engineering qualification nowych artefaktów pozostaje osobna. |
| B-03B / MVP | `Core/Workspace`, `Worker.V19/Plc/Export`, manifest+staging | FC SCL native XML/generated source z hierarchy, hashami, Unsupported rows; brak overwrite W |
| B-03C / REQUIRED | matrix DB/UDT/FB/OB/IDB/tag tables/groups | golden export fixtures i coverage per kind; brak reklamy importu na podstawie export |
| B-03D / REQUIRED | `Worker.V19/HmiUnified,HmiClassic`, typed model | scripts/tags i read-only screen/event mapping z error coverage, native YAML zachowany; rodziny rozdzielone |

HMI-JS-02 (osobno zlecony przyrost B-03D): typowany eksport jednego modułu Unified
V19, zachowanie natywnych plików i konserwatywna projekcja funkcji są zaimplementowane
i testowane offline. [Specyfikacja/procedura](HMI_JS_SYNC_MVP.md) wymaga nowego
bindingu i kwalifikacji E1 ENGINEERING_TEST; brak nowego bootstrapu, live export NOT_RUN.
Nie zamyka pełnego B-03D, X-04, rozpoznania ekranów/tagów, importu ani globalnej
kwalifikacji; rzeczywisty format i granice obszarów edytora pozostają UNKNOWN.

**Ryzyka:** know-how/Safety/inconsistent blocks, brak API Unified screen round-trip, nieznane ID. **Testy:** fresh identity, export twice, rename/group/Unicode collisions, unsupported inventories. **Akceptacja:** każdy odkryty obiekt ma artifact lub jawny powód; pliki CODEX zachowane; brak setter/Compile/Save/Archive w R. **Rezultat:** Git-friendly pliki + obiektowe locatory. **Bramka:** pierwsza FC ma native i editable reprezentację oraz kwalifikowany comparison profile; HMI może nadal pozostać RO.

Uszczegółowienie przygotowania B-03A, 2026-10-27: [E2-A](B03A_E2_A_PREPARATION.md)
przygotowuje offline [B1](E2-B1_TEST_PLAN.md) `T05_RecipeSim_V19_Testowy`, następnie
[B2](E2-B2_TEST_PLAN.md) `T04_T05-000_Testowy`. B2 wymaga B1 qualification PASS,
hashowanych dowodów, release, trend review i osobnej decyzji/świeżego preflightu.
Historyczne przygotowanie nie uruchamiało Engineering. Późniejszy osobno autoryzowany
[B1](../tests/engineering/E2_B1_2026-10-27_06cbfec6.md) wykonał 20/20 odczytów PASS,
lecz qualification BLOCKED przez rosnące handles; B2 i real E1 enrollment NOT_RUN.
To nie otwiera B-03B, Etapu 4 ani praw do mutacji.

[E2-B0](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md) implementuje offline zatwierdzony
Controlled Qualification Bootstrap dla B1: jawny lokalny grant qualification-only,
exact binding/build/read scope i trwale zużywany budżet 20; restart/STOP zamyka grant.
Normalny tryb nadal wymaga E1. Implementacja offline nie aktywowała grantu;
późniejszy grant B1 został osobno autoryzowany i zakończony po zużyciu 20 cykli.

## Etap 4 — Pliki i CODEX→TIA [MVP]

**Cel:** mały, kontrolowany import bez utraty intencji CODEX. **Zależności:**3A/3B,2,5A/6A przed włączeniem Apply, X-02/X-03. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-04A | `Core/Sync/ChangeDetection,Planning,Dependencies`, files.changes, sync.plan | tabela W/P/B, snapshot S, immutable plan/DAG; syntax/unknown/conflict blokują execution; co najmniej 100 deterministycznych rzeczywistych planów po implementacji planera |
| B-04B | `Core/Policy`, `Host/Authorizations`, backup provider adapter | E grant/scope/epoch, one verified backup/session, temp space diagnostics i no fallback; zero auto Save dirty |
| B-04C | `Worker.V19/Plc/Import`, saga steps, wyłącznie kwalifikowana FC SCL | intent/receipt, staged strict encoding, recheck P pod ExclusiveAccess, Compile/Save/Verify hooks; import disabled bez 5A/6A |
| B-04D / REQUIRED | dependency groups UDT/DB/FB/IDB, explicit tombstones i rename | osobna capability dla create/update/delete; deletion grant nie wynika z missing file; zero write outside scope |

**Ryzyka:** partial import, concurrent TIA edits, późniejsze edycje W, nieatomowość vendor API. **Testy:** RT-04/05/07/08/12/16; backup fail i source capture race. **Akceptacja:** Wnew przetrwa import S; konflikt nie importuje; partial state ma UNKNOWN i backup, nie rollback. **Rezultat:** Apply sagi jednej jednostki, feature-gated. **Bramka:** pierwszy live Apply dopiero po syntetycznym fault harness, zatwierdzonym odbiorcy i zaimplementowanych 5A/6A.

## Etap 5 — Weryfikacja zgodności [MVP / rozszerzenia REQUIRED]

**Cel:** status SYNCED wyłącznie z dowodami. **Zależności:**3B,1B, może powstawać przed 4C. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-05A | `Core/Sync/Verification,Baselines`, `Worker.V19/FreshReadback` | content/config/inventory/dependencies zgodne w zakresie FC, saved receipt i fresh proxy; B CAS, unknown→nie SYNCED |
| B-05B | normalizers i golden fixture transformations | raw/model hashes osobno; zachowany EOL/marker semantics i unknown XML fields; wersja normalizatora wiązana z B |
| B-05C / REQUIRED | HMI API/structure reader + per-dimension reports | X-04 udowadnia publiczny natywny scope; same exported function text nie daje API PASS |

**Ryzyka:** fałszywa równoważność po normalizacji i stale receipts. **Testy:** model/content equal przy innej strukturze, artifact tamper, restart epoch, B race. **Akceptacja:** zero fałszywych SYNCED we wszystkich fault points; verified S i bieżące Wnew rozróżnione. **Rezultat:** wiarygodny status, zapis B po pełnej grupie. **Bramka:** można włączyć pierwszy end-to-end 4C po6A; HMI write nadal czeka na własną kwalifikację.

## Etap 6 — Kompilacja i zapis [MVP PLC / REQUIRED HMI]

**Cel:** diagnostyki powiązane z konkretnym snapshotem, bez utożsamiania z Runtime. **Zależności:**3A,2,4B;5 do końcowego przepływu. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-06A / MVP | `Worker.V19/Plc/Compiler`, `Core/BuildEvidence`, Save receipt | ICompilable lookup, rekurencyjne messages, warnings policy, Save return+clean i independent verify; RT-13/14 |
| B-06B / REQUIRED | `Worker.V19/Hmi*/Compiler` | usługa z właściwego urządzenia/rodziny, compile nie maskuje native API gap |
| B-06C | report freshness invalidation, API scope bindings | zmiana zależności unieważnia C; pomyślny compile bez Save/verify nie daje Engineering completion |

**Ryzyka:** compile długo blokuje portal, niespójne dependent blocks nieeksportowalne przed compile. **Testy:** errors/warnings/timeout/lost reply i post-compile export. **Akceptacja:** jeden pierwszy PLC przepływ eksport→edycja→plan→backup→Apply→Compile→Save→readback; drugi identyczny submit nie mutuje. **Rezultat:** **MVP Engineering**, Runtime NOT_RUN jest poprawnym wynikiem. **Bramka:** dopiero teraz rozszerzać kind matrix oraz symulatory, zachowując qual gates.

## Etap 7 — Symulatory i HMI Runtime [REQUIRED]

**Cel:** własna, kontrolowana sesja testowa. **Zależności:**MVP, X-05/X-06, licencje i izolacja. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-07A | `Simulation/AdvancedV6`, instance registry/lease | enumerate/start/get-state/stop własnej instancji przez SDK; license result jawny |
| B-07B | `Simulation/Preparation`, osobno kwalifikowany load | exact simulation recipient, routing isolation, loaded fingerprint; odmowa każdego physical fallback |
| B-07C | `Simulation/UnifiedRuntimeManager`, profile Runtime | status/start/stop z udokumentowanego CLI, TLS/UMC readiness, oddzielny transfer grant; nie rusza istniejącego Runtime |
| B-07D / OPTIONAL | standard PLCSIM/klasyczny Runtime inne wersje | capability per version potwierdzone testami; brak wspólnego domyślnego obejścia |

**Ryzyka:** działa cudza sesja, brak licencji, Session 0/desktop, sieć osiąga maszynę. **Testy:** RT-09/11, NOT_OWNER, manager crash i runtime active≠ready. **Akceptacja:** STARTING/RUNNING/LOADED/READY rozróżnione, brak zamykania cudzych procesów i brak fizycznego downloadu. **Rezultat:** odtwarzalny profil E. **Bramka:** E ma potwierdzony build i izolację przed dowolnym test write.

## Etap 8 — Testy PLC [REQUIRED OPC / OPTIONAL Snap7]

**Cel:** automatyczne sprawdzanie sekwencji bez wpływu na maszynę. **Zależności:**7, X-07; X-08 tylko Snap7. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-08A / REQUIRED | `tests/e2e/opcua`, runner+profiles+namespace mapper | jawny TEST/SECURE, bounded read, jakości, timestamps, subscription gaps i reconnect; zero fallback |
| B-08B / REQUIRED | write policy/mailbox/sequence assertions | allowlist pól+wartości+rate, Request/Ack i loaded fingerprint; write timeout UNKNOWN, nie retry |
| B-08C / REQUIRED | scenarios/reports, transport-neutral assertions | stany, timeouty, błędy, cleanup i JSON/Markdown/JUnit dowody; HMI cache nie udaje CPU |
| B-08D / OPTIONAL | `tests/e2e/snap7`, pinned python-snap7 i codec | X-08: kwalifikowany non-optimized test DB, layout hash, endian/bit tests, polling/rate, brak expose force/control/download |

**Ryzyka:** błędny NodeId/offset, zły recipient, stale quality, transport ACK mylony z PLC ACK. **Testy:** RT-10, unknown write; Snap7 layout drift/neighbor byte i porównanie z OPC na tym samym scenariuszu. **Akceptacja:**100 sekwencji powtarzalnych; każde niedozwolone pole odrzucone; brak auto osłabienia CPU dla Snap7. **Rezultat:** `test.plc`, z niezależnymi capability transportów. **Bramka:** dany transport i scenariusz dostają PASS tylko dla własnego build/profile; OPC nie zależy od instalacji Snap7.

## Etap 9 — Testy HMI Playwright [REQUIRED]

**Cel:** rzeczywiste dowody web UI i powiązanie z testowym PLC. **Zależności:**7C i8 tam, gdzie scenariusz wymaga PLC write; X-06, publiczny runtime/DOM. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-09A | `tests/e2e/hmi` TypeScript, browser/profile/credentials provider | TLS i testowy URL/build/role verified, bez sekretów w trace i Git |
| B-09B | page objects per family/screen/CWC, read-only navigation | brak ambiguous selectors; iframes/canvas raportują realne pokrycie |
| B-09C | test input/ack/alarm i artifacts | screenshots/trace/network errors, test alarm source, wartości wyłącznie allowlist symulacji |
| B-09D / OPTIONAL | klasyczny desktop Runtime adapter | udokumentowane API albo osobno zatwierdzony UIA; nie reklamuje Playwright bez web UI |

**Ryzyka:** UMC/certyfikat, canvas bez DOM, wersja browsera, kliknięcie uruchamia PLC. **Testy:** RT-11, login/role denied, zły screen/build i alarm timeout. **Akceptacja:** minimum start/navigation/read/input/alarm pass na izolowanym profilu; brak UI coverage nie jest pełnym PASS. **Rezultat:** `test.hmi` i ograniczenia per rodzina. **Bramka:** testy mogą wejść do opt-in E2E lane dopiero po zweryfikowanej izolacji i redakcji artifacts.

## Etap 10 — Odporność i wydajność [REQUIRED]

**Cel:** kwalifikacja 8 h i limity NF-01–15, pełna macierz RT-01–16. **Zależności:**od2 rozwijany harness, MVP oraz adaptery objęte release. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-10A | `tests/resilience`, deterministic crash matrix i SQLite artifacts recovery | każdy journal boundary, unknown fence, queued cancel i duplicate submit pokryte |
| B-10B | benchmark/soak harness i performance baselines | hardware/software versions, p50/p95/p99,1000 operacji fake i100 read-only live,8 h |
| B-10C | storage/retention/limits/security regressions | disk full, pinned evidence, log rotation, API abuse i bad files; brak usuwania aktywnego backupu |

**Ryzyka:** testy awarii same uszkadzają projekt, memory creep, zbyt ambitne vendor SLO. **Testy:** fake przed live; power-loss tylko osobne laboratorium/storage kopia. **Akceptacja:**0 fałszywych SYNCED,0 utraconych IDs,0 nieautoryzowanych write, cele API spełnione; vendor timeouty izolowane i udokumentowane. **Rezultat:** kwalifikowany release candidate z jawną matrix. **Bramka:** nierozwiązane Critical blokują release; vendor limitation może pozostać jawnie disabled.

## Etap 11 — Wydanie i migracja [REQUIRED; MCP OPTIONAL]

**Cel:** instalowalny, audytowalny produkt bez przenoszenia nieznanych stanów starego narzędzia. **Zależności:**10 i zatwierdzony capability set. **Pakiety:**

| ID | Zadanie / moduły | Kryterium commita |
|---|---|---|
| B-11A | packaging host/worker/CLI, doctor, provisioning i protected stores | net10/net48 dependencies, rights i DLL identity checked; vendor binaries nie w Git; bez globalnych zmian przy uninstall |
| B-11B | migrator read-only starych manifests/evidence, fresh baseline workflow | stare FAILED/UNKNOWN zachowane jako historia; żaden stary grant/checkpoint nie daje automatycznie prawa nowemu workerowi |
| B-11C | runbooks start/recovery/restore/upgrade, guide CODEX | konkretne polecenia, status/result, risk i offline/Engineering/Runtime; test clean install i schema upgrade z backupem metadata |
| B-11D / OPTIONAL | `TiaBridge.Mcp` i client contract test | submit/status/result wspólne z HTTP, restart MCP nie traci pracy, Tasks tylko po negotiated support |

**Ryzyka:** redistribucja/licencje, snapshot z nieaktualnego T05, klient myli acceptance z success. **Testy:** install/upgrade/restart/migration bez Siemens i opt-in read-only integration. **Akceptacja:** jednoznaczny release matrix, powtarzalny install, działający recovery, użytkownik może przejrzeć pełny plan przed prawami E/S. **Rezultat:** wersja 1 aplikacji i ścieżka migracji. **Bramka dalszego rozwoju:** nowa wersja TIA/rodzina/typ dostaje osobną kwalifikację i kompatybilność, nie tylko podmianę DLL.

## Pokrycie problemów audytu

| Problemy | Pakiety implementujące rozwiązania |
|---|---|
| OB-01–OB-06 | B-02A–D, B-01A, B-10A |
| OB-07–OB-09 | B-03A/B, B-01B, B-04A/D, X-01 |
| OB-10–OB-12 | B-05A–C, B-03D, X-03/X-04 |
| OB-13–OB-14 | B-01B, B-04B, B-10A/C; denylist i storage qualification |
| OB-15–OB-16 | B-03B/D, B-04A/C, B-10B; bounded coverage bez ignorowania unknown |
| OB-17–OB-20 | B-02A/B/D, B-04C, B-05A, B-10A |
| OB-21–OB-24 | B-01A/B, B-03A/D, B-08A–C, B-11A/B |

Każdy pakiet kończy się named-path diff, właściwymi testami i osobnym commitem; nie łączyć rozwoju adaptera z niezamówionym wdrożeniem do aktywnego projektu. Obecne zadanie tworzy jeden wspólny commit dokumentacyjno-projektowy zgodnie z poleceniem użytkownika.

## Wykonanie X-01 read-only

Eksperymentalny [worker V19](../src/TiaBridge.Worker.V19/README.md) korzysta z istniejącego trwałego silnika i osobnego net48/x64. [Raport X-01](X01_TIA_V19_QUALIFICATION.md) pozostaje źródłem wyłącznie historycznych wyników dwóch instancji. [Raport B-03A](B03A_DISCOVERY_QUALIFICATION.md) opisuje implementację offline oraz brak nowej Engineering kwalifikacji; [harness Engineering](../tests/engineering/README.md) jest opt-in i nie uruchamia się automatycznie. B-03A nie rozszerza D-11 na produkcję/exFAT, nie realizuje NF-11, eksportu lub mutacji.
