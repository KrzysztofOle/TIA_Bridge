# TIA_Bridge 2

[LAB-03M — diagnostyka konsoli EXPORT EVENTS](docs/LAB_03M_EVENT_CONSOLE_DIAGNOSTICS.md): podsumowanie projektu/HMI, statusu i liczników, wszystkie obserwacje jako WARN/ERROR z czytelnymi nazwami i fallbackiem do locatorów. Ograniczenia kontenerów to WARN; PARTIAL nie zwiększa liczby błędów. Artefakty i kody wyjścia bez zmian, poza hashem nowego EXE w manifeście. Testy offline 145/145 PASS; Attach/Engineering/F5 NOT_RUN.

[LAB-03L — czytelne zdarzenia HMI](docs/LAB_03L_READABLE_EVENT_EXPORT.md): export-events automatycznie dodaje screens/ z osobnymi plikami JS/GDA, README i lekki events-index.json. Źródłem pozostaje events.json LAB-03K; nowe pliki obejmuje wspólny manifest. Testy offline 131/131 PASS, dodatkowo kontrole CLI/launch i identyczność 9 oryginalnych artefaktów. Historyczne eksporty bez zmian; render-events pozostaje rekomendacją osobnego pakietu. Attach/Engineering/F5 w LAB-03L NOT_RUN.

[Porównywanie eksportów zdarzeń HMI](docs/LAB_EVENT_COMPARISON.md): offline `py -3 -B scripts/compare_events.py compare-events --before <events.json> --after <events.json> --output <parent>`. Nowe raporty `EventComparisons/<run-id>/` zawierają JSON, Markdown, dokładne diffy JS/GDA i manifest SHA-256. Dopasowanie hierarchiczne niezależne od indeksów, lokalne coverage i rozłączne statusy; brak Siemens i wykonania JS. VS Code: **TIA Bridge: COMPARE EVENTS — Export ↔ Export**, trzy pytania o ścieżki. Testy 30/30 i regresje laboratorium 85/85 PASS; F5/Engineering/Runtime NOT_RUN.

Projekt nowej aplikacji Windows do integracji CODEX z Siemens TIA Portal. Stan 2026-10-26: zaimplementowano offline B-01A–B-01C, B-02A–C oraz bounded Discovery/Inventory B-03A. Działają Core, Host/API/CLI, trwały silnik Fake, registry default-deny i eksperymentalny worker V19 read-only. Discovery nie tworzy trwałych ObjectId, nie eksportuje treści i nie nadaje uprawnień Engineering; szczegóły [raportu B-03A](docs/B03A_DISCOVERY_QUALIFICATION.md). Przyjęcie operacji do SQLite nie oznacza jej wykonania ani autoryzacji TIA.

B-02D dostarczył [harness odporności i raport bramki](docs/16_RESILIENCE_RECOVERY.md), z pełnym przepływem na Fake, kontrolowanymi crash/restart i pomiarami. D-11 rozdziela pełną kwalifikację offline od ograniczonej bramki X-01 read-only na lokalnym NTFS: bramka D-11 ma PASS, historyczny NF-03 exFAT pozostaje FAIL, a pełna kwalifikacja aktualnego profilu NTFS ma FAIL z powodu NF-08 (idle CPU 1,0288% przy progu 1,0%). NF-11 ≥8 h: NOT_RUN/B-10B; produkcja i Runtime pozostają niezakwalifikowane. [Raport kwalifikacji](docs/X01_TIA_V19_QUALIFICATION.md) opisuje Engineering, a [raport ponownej kwalifikacji NTFS](tests/resilience/X01_NTFS_REQUALIFICATION_2026-10-26.md) zachowuje odrębne dowody. X-01 nie wykonuje mutacji Siemens.

Docelowy przepływ: **TIA → eksport do workspace → edycja CODEX → plan → zatwierdzone Apply → niezależna weryfikacja → kompilacja/symulacja → testy**. Pliki CODEX określają zamierzony stan. Rozbieżności z nieznanymi zmianami TIA wymagają rozstrzygnięcia; import nigdy nie stanowi samodzielnego dowodu zgodności.

Rekomendacja: host C#/.NET 10, trwałe operacje i SQLite na lokalnym NTFS, wykonawca Openness V19 w oddzielnym procesie .NET Framework 4.8 x64. CODEX otrzymuje `operation_id` po utrwaleniu zlecenia i kontynuuje niezależną pracę. CLI i lokalne HTTP API korzystają z jednego silnika; MCP jest opcjonalną fasadą. Zawieszenie Siemens nie blokuje API, ale może uniemożliwić dalsze operacje na danym projekcie do czasu uzgodnienia stanu.

## Wyniki analizy

- Stary Bridge: `C:\RMA\t05_plc\tools\T05_TIA_Bridge.py` i `tools/t05_tia_bridge/`. Zbadano kod bieżących plików, historię Git, testy, logi oraz lokalną dokumentację Siemens. [Audyt](docs/01_AUDIT_OLD_BRIDGE.md) rozdziela usterki historyczne, naprawione i nadal obecne.
- Potwierdzono lokalne API V19, read-only attach i inventory oraz obecność PLCSIM V19, PLCSIM Advanced V6.0 i Unified PC V19. Nie wykonywano importu, Compile, Save, Archive, downloadu ani sterowania symulatorem w tym zadaniu.
- OPC UA `localhost:4890`: read-only status PASS dla **WinCC Unified**. To nie jest dowód dostępu do CPU ani testu logiki PLC. HTTPS `localhost`: błąd TLS; HMI E2E NOT_RUN.
- Wykonano 94 ukierunkowane testy offline starego Bridge; wszystkie przeszły. Weryfikują istniejące zabezpieczenia i komparatory, nie nową aplikację ani Runtime. Szczegóły: [dowody i jakość](docs/10_EVIDENCE_AND_QUALITY.md).

## Dokumentacja projektu

[LAB-03K — eksport zdarzeń Unified](docs/LAB_03K_HMI_EVENT_EXPORT.md): ręczne `LabExporter.exe export-events` w `X:\TESTY_TIA_Bridge`. [LAB-03O](docs/LAB_03O_EVENT_OVERWRITE.md) domyślnie synchronizuje JSON/raport/JS/manifest bezpośrednio w dowolnym `--output` z [LAB-03N](docs/LAB_03N_EVENT_OUTPUT_LOCATION.md): nadpisuje wcześniej zarządzane pliki świeżym odczytem, zachowuje pliki użytkownika i prowadzi dziennik recovery. Usuwanie historycznych handlerów wymaga kompletności per obiekt, wiarygodnego mapowania v2 i zgodnego SHA-256, także przy globalnym PARTIAL; niepewność jest raportowana jako retencja UNVERIFIED. Migracja v1 i opcjonalny historyczny eksport są opisane w kontrakcie. Jawne `--mode snapshot` zachowuje historyczny nowy run-id bez nadpisywania. F5 **TIA Bridge: EXPORT EVENTS — TIA → Folder** korzysta z domyślnego overwrite, osobny **EXPORT EVENTS — SNAPSHOT** przekazuje tryb snapshot; jawne ścieżki projektu/wyniku i HMI pozostają bez zmian. Implementacja i testy offline; rzeczywisty eksport/Attach oraz Engineering qualification **NOT_RUN**, `RowSelected` **UNKNOWN**. Wnętrza kontenerów bez publicznego odczytu pozostają `UNKNOWN/PARTIAL`; nie jest to produkcyjna synchronizacja. Dotychczasowe FORCE EXPORT/IMPORT zachowane.

Bieżące przygotowanie B-03A/E2: [pakiet E2-A](docs/B03A_E2_A_PREPARATION.md),
[plan B1 — T05_RecipeSim_V19_Testowy](docs/E2-B1_TEST_PLAN.md),
[plan B2 — T04_T05-000_Testowy](docs/E2-B2_TEST_PLAN.md),
[instrukcja operatora](docs/E2_OPERATOR.md). Testy jednego projektu naraz, B1 przed B2.
[Wykonany B1, 2026-10-27](tests/engineering/E2_B1_2026-10-27_06cbfec6.md):
20/20 odczytów funkcjonalnych PASS, qualification BLOCKED przez niewyjaśniony
wzrost handles Workera. E2-G READY i jawny B0 poprzedziły test; grant FINISHED,
20 cykli zużytych, release potwierdzony. B2 i real E1 enrollment NOT_RUN;
przygotowanie dokumentacji samo nie aktywuje deploymentu ani praw Engineering.

[Dochodzenie E2-B1-R](docs/B03A_E2_B1_RESOURCE_INVESTIGATION.md) potwierdza przyrosty
także podczas Attach, lecz pozostawia ROOT_CAUSE_UNEXPLAINED. Dostarcza domyślnie
wyłączoną instrumentację faz Windows/CLR, regresje bez Siemens i propozycję osobno
autoryzowanej diagnostyki. Historyczny B1 BLOCKED i Worker Handle Risk UNKNOWN
pozostają bez zmian; w B1-R nie wykonano nowych Attach ani grantu.

[E2-B1-D](docs/B03A_E2_B1_D_HANDLE_DIAGNOSTICS.md) rozdziela osobny profil
HANDLE_DIAGNOSTICS/3 od B1/20. Wykonanie 2026-10-28: 1/3, kontroler STOP przez
format ProjectKey (naprawiony offline), grant odwołany i cleanup potwierdzony.
Cold Attach +37, Dispose −10, idle +28; ROOT_CAUSE_UNEXPLAINED. Diagnostyka
nie przyznaje kwalifikacji B1; historyczny BLOCKED i Worker risk UNKNOWN zachowane.

[E2-B1-I](docs/B03A_E2_B1_IPC_HANDLE_INVESTIGATION.md) izoluje infrastrukturę offline:
wzrost odtworzono bez Siemens w senderze net48, a osobny GC zwolnił uchwyty
zakończonych wątków i Eventów. Przyczyna PROBABLE, bez poprawki produkcyjnej;
historyczny B1 pozostaje BLOCKED, Worker risk UNKNOWN. Nowe Engineering NOT_RUN.

| Dokument | Zawartość |
|---|---|
| [01 — Audyt](docs/01_AUDIT_OLD_BRIDGE.md) | kod, historia, rejestr problemów i środki zaradcze |
| [02 — Wymagania](docs/02_REQUIREMENTS.md) | sześć etapów workflow i mierzalne cele |
| [03 — Architektura](docs/03_ARCHITECTURE.md) | procesy, kolejka, watchdog, struktura repo |
| [04 — Synchronizacja](docs/04_SYNCHRONIZATION.md) | stan, hashe, konflikty, SYNCED, recovery |
| [05 — Interfejs CODEX](docs/05_CODEX_INTERFACE.md) | operacje, wejścia, wyniki, błędy, uprawnienia |
| [06 — Siemens](docs/06_TIA_INTEGRATION.md) | V19, formaty, kompilacja, symulatory, licencje |
| [07 — Testy](docs/07_TEST_STRATEGY.md) | OPC UA, Playwright i 16 awarii obowiązkowych |
| [08 — Plan](docs/08_IMPLEMENTATION_PLAN.md) | etapy 0–11, małe zadania i bramki akceptacji |
| [09 — Ryzyka i decyzje](docs/09_RISKS_AND_DECISIONS.md) | ADR, ryzyka i eksperymenty do wykonania |
| [10 — Dowody i jakość](docs/10_EVIDENCE_AND_QUALITY.md) | środowisko, źródła producentów i zakres kontroli |
| [11 — Snap7](docs/11_SNAP7_PLC_TESTING.md) | dodatkowy transport testów PLC, ograniczenia i kwalifikacja |
| [12 — Workspace i Capabilities](docs/12_WORKSPACE_CAPABILITIES.md) | zaimplementowany format B-01B, ścieżki, hashe, registry i przykłady |
| [13 — Trwały magazyn](docs/13_DURABLE_STORAGE.md) | B-02A: SQLite, migracje, idempotencja, journal, outbox i artefakty |
| [14 — Scheduler i IPC](docs/14_SCHEDULER_IPC.md) | B-02B: scheduler, supervisor, Fake Worker, watchdog i quarantine |
| [15 — HTTP API i CLI](docs/15_HTTP_API_CLI.md) | B-02C: loopback, discovery/ACL, async submit/status/events/result/cancel i pomiary |
| [16 — Odporność i recovery](docs/16_RESILIENCE_RECOVERY.md) | B-02D: procesy, crash/WAL/artifacts, macierz RT/NF i odrębna formalna bramka |
| [Instrukcja ChatGPT](Instrukcja_ChatGPT.md) | podział ról ChatGPT/CODEX, workflow zadań i reguły oceny wyników zgodne z AGENTS.md |
| [Zmiany](Zmiany.md) | zapis prac projektowych |

Wykonany zakres: [Core](src/TiaBridge.Core/README.md), [Host Storage B-02A](src/TiaBridge.Host/README.md), [kontrakty i OpenAPI](contracts/README.md), [pełna walidacja offline jednym poleceniem](tests/README.md) i [macierz pokrycia](tests/COVERAGE.md). B-01C obejmuje 100 deterministycznych modeli wejściowych; rzeczywiste plany są kryterium B-04A. B-02A zapisuje operacje; [B-02B](docs/14_SCHEDULER_IPC.md) wykonuje wyłącznie jawne scenariusze syntetyczne w osobnym Fake Worker. Nie ma IO Workspace, synchronizacji ani uprawnień sesji. [B-02C](docs/15_HTTP_API_CLI.md) udostępnia ten sam silnik przez zabezpieczone API i CLI. B-02D wykonał testy syntetyczne; kolejne pakiety wymagają osobnego zlecenia. Przed integracją z Openness i X-01 obowiązuje pozytywna [ograniczona bramka B-02A–D](docs/07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only) na fake workerze; pełny NF-11 jest osobnym warunkiem B-10B. [Plan](docs/08_IMPLEMENTATION_PLAN.md) wskazuje zależności i dowody wymagane przed Apply.

Zatwierdzona polityka modelu HMI i oryginalnych artefaktów Siemens: [ADR 0003 — HMI canonical model and native artifact boundaries](docs/decisions/0003-hmi-canonical-model-and-native-artifacts.md) (D-12; decyzja, nie implementacja).

## Granice i praca z repozytorium

Nie ma automatycznego importu po zapisie pliku. Domyślny profil jest read-only; zatwierdzone, ograniczone uprawnienie sesji testowej może obejmować wiele kroków. Usuwanie, fizyczny download i rzeczywista maszyna wymagają osobnej autoryzacji. Kopia Git nie zastępuje archiwum Siemens.

Repozytorium kodu jest odrębne od trwałej bazy operacji, sekretów i spool wykonawcy, które muszą znajdować się poza repo na lokalnym NTFS. Nie opieramy gwarancji ACL ani odporności na utratę zasilania na exFAT. Nazwy `SYNCED`, `Compile PASS` i `Runtime PASS` opisują niezależne fakty.

Kontrola dokumentacji: `py -3 scripts/audit_quality.py`. Wymaga Python 3.12 i `jsonschema`; nie łączy się z Siemens. [Instrukcja](scripts/README.md), [zasady agentów](AGENTS.md).

### Laboratoryjne uruchamianie FORCE EXPORT / FORCE IMPORT

Repozytorium zawiera konfiguracje VS Code dla istniejącego laboratoryjnego narzędzia `X:\TESTY_TIA_Bridge\bin\LabExporter.exe`. Otwórz `X:\Projects\TIA_Bridge` w VS Code, przejdź do **Run and Debug**, wybierz **TIA Bridge: FORCE EXPORT — TIA → Folder** albo **TIA Bridge: FORCE IMPORT — Folder → TIA** i naciśnij **F5**. Polecenia uruchamiają się w widocznym terminalu VS Code; ewentualne okno Siemens Openness zatwierdź ręcznie.

Ścieżki laboratorium są jawnie ustawione w `.vscode\launch.json`: projekt `X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy` oraz katalog `X:\TESTY_TIA_Bridge\Scripts` jako cel eksportu i źródło importu. Zmień odpowiednie argumenty `--project`, `--output` lub `--input`, aby użyć innych lokalizacji. Eksport zastępuje zawartość katalogu docelowego; import aktualizuje moduły przy zgodnym zestawie nazw i ich nie usuwa. Te konfiguracje nie wykonują Download ani operacji Runtime.

[LAB-03I](docs/LAB_03I_GLOBAL_DEFINITION_NAMING.md) wprowadza zarezerwowany plik `_Global_definition_area.js` oraz manifesty `lab-03i-*` w narzędziu laboratoryjnym. Eksport zawsze tworzy nową nazwę; import odrzuca starsze paczki i wskazuje ponowny FORCE EXPORT. Kod i znaczniki Siemens pozostają bajtowo zachowane. Testy offline 18/18 oraz staging/rollback 10/10 PASS; rzeczywisty eksport FAIL na kontroli natywnych plików `JSON_Test_screen`, import/readback NOT_RUN. To nie jest implementacja eksportu/importu w produkcyjnym Host/Worker.

[LAB-03J](docs/LAB_03J_NATIVE_EXPORT_DIAGNOSTICS.md) dodał szczegółową diagnostykę, ścisłą kontrolę natywnej pary i zachowanie surowych danych po awarii eksportu. Regresje LAB-03I/LAB-03H pozostają PASS, nowe testy offline 15/15 PASS. Bieżące pełne eksporty 6 modułów / 22 funkcje / 47 plików przeszły; historyczna przyczyna LAB-03I nie została odtworzona ani potwierdzona jako naprawiona. Raport rozdziela aktualne wyniki od tej nierozstrzygniętej diagnozy i opisuje zabezpieczenia laboratoryjnego importu.

Wznowienie LAB-03J z 2026-10-29 potwierdziło również rzeczywisty FORCE IMPORT, Save i readback wszystkich sześciu modułów: PASS po zweryfikowanym natywnym backupie i świeżym porównaniu baseline pod ExclusiveAccess. Końcowe `Scripts` opublikowano ponownie; zmieniono wyłącznie zaplanowany komentarz w laboratoryjnym `T05MoveTest.RunTest`, pozostałe 21 funkcji, 6 globalnych segmentów i 6 YAML pozostały bajtowo zgodne. Gotowość T05_PLC nadal NO ze względu na nieustaloną historyczną przyczynę; nie deklarujemy zamknięcia całej diagnozy.

## Materiały wejściowe

Pierwotne opracowania [architecture](docs/architecture.md), [sync-model](docs/sync-model.md), [formats](docs/formats.md), [safety](docs/safety.md), [roadmap](docs/roadmap.md) i ADR 0001 zachowano i powiązano z aktualnymi specyfikacjami. [ADR 0002](docs/decisions/0002-async-host-and-versioned-worker.md) doprecyzowuje autonomię w zatwierdzonej sesji.

## X-01 — ograniczona integracja rzeczywista

Osobne [rozwiązanie Engineering](TiaBridge.Engineering.slnx) buduje worker net48/x64 z PublicAPI V19 z Program Files, bez kopiowania vendor DLL. Standardowy build/test offline nie wymaga Siemens. [Jawny harness live](tests/engineering/README.md) wykorzystuje istniejący Host, API, CLI, SQLite, scheduler i IPC 1.1; dopuszcza wyłącznie exact binding i `project.inspect`, bounded inventory oraz Detach. Pozostałe operacje TIA pozostają UNSUPPORTED. Nie ma eksportera, importu, Save, Compile, Download lub Runtime. PASS tego eksperymentu nie oznacza kwalifikacji produkcyjnej ani całego B-03A.

Końcowy X-01: oba projekty przeszły Attach/read/Dispose; Engineering 93/93 PASS w historycznym zakresie. D-11 LIMITED GATE ma PASS. Historyczna pełna kwalifikacja offline NTFS STRICT ma FAIL (NF-08: 1,0288% > 1,0%); historyczny exFAT FAIL pozostaje. Późniejszy SHARED_WORKSTATION ma ograniczoną D-11 PASS i pełną kwalifikację BLOCKED przez NF-11 NOT_RUN. Nowy [Engineering Discovery B1](tests/engineering/E2_B1_2026-10-27_06cbfec6.md) wykonał 20 odczytów, lecz qualification pozostaje BLOCKED przez zasoby; Worker Handle Risk UNKNOWN. Nie jest to pełna kwalifikacja ani gotowość produkcyjna.
