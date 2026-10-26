# Zmiany — TIA_Bridge 2

## 2026-10-26 — końcowa kwalifikacja X-01 na X:/NTFS

**Wejście:** aktywny checkout `X:\Projects\TIA_Bridge`; przed zmianą czysty `main`, `HEAD=origin/main=e6b659708c6141ad89be4ea109a64a8e76109a3d`; implementacja X-01 `9e8b7bfadb1f4e7b0fc3a793d01cbb82f54e5c07`. `Get-Volume` potwierdził X: jako lokalny Fixed/NTFS. State oraz Workspace w pomiarach również wskazały NTFS. Nie otwierano TIA ani projektów.

**Pełny runner:** odizolowany QA Python uruchomił `scripts/validate_offline.py` z aktualnego checkoutu. Run `f116dd23c7aa480995b5a69077f4db35`, fingerprint LF źródeł `f4bd013d908d7f77a777f5741c77f402046cd9c8b459be07d13d795d9cb55ea7`. Locked restore/build PASS; Build Release 0 warning/error. .NET 772 PASS / 1 FAIL / 0 SKIP z 773; Python 31/31 PASS; generatory, audyt jakości, format i diff PASS. Jedyny FAIL: pięciominutowy idle NF-08 zmierzył CPU 1,0287988877% przy istniejącym progu 1,0%. Próg i test nie zmieniano. NF-03 workspace/repo X:/NTFS: 100 kontrolnych próbek bez workera p95 4,5592 ms; 100 paced 1 MiB samples podczas Fake Hang p95 6,5635 ms, p99 87,0439 ms; refused=0. Historyczny exFAT FAIL zachowano.

**Oddzielne oceny:** aktualna pełna kwalifikacja offline NTFS = FAIL; limited D-11 = PASS według jawnego zakresu 07/08: RT-03/05/06/15/16 i NF-01/02/03/04/05/06/12/14/15 PASS, idle 306,410 s oraz 1001 operacji PASS. NF-08 CPU failure pozostaje widoczny jako pełny profil, ale NF-08 nie należy do zestawu NF ograniczonej D-11; nie wykryto krytycznego błędu wykonania/izolacji. Istniejący ogólny reporter zachował `x01_readiness=NOT_READY`, bo konserwatywnie propaguje dowolny FAIL testu oraz nadal ma historyczną regułę agregacji NF-11. Reporter/thresholds nie modyfikowano; osobna ocena uzasadnia D-11 z jego zatwierdzonym zakresem. Engineering 93/93 PASS jest wcześniejszym dowodem, nie był powtarzany. Runtime NOT_RUN, NF-11 NOT_RUN, produkcyjna kwalifikacja nieukończona.

**Zasoby i rekomendacja:** historyczny Worker V19 miał STA handles 462→702 i MTA 456→602. Dispose i zakończenie własnych procesów przeszły, lecz przyczyna wzrostu pozostaje nierozstrzygnięta. Status ryzyka `UNKNOWN`; nie deklarujemy naprawy ani zamknięcia R-14. Rekomendacja B-03A `READY` wyłącznie dla ograniczonego, obserwowanego read-only; surowy/ciągły zakres i produkcja nie są gotowe. Dodano propozycję osobnego pomiaru cykli i kryteriów przed rozszerzeniem reuse Workera. Nie rozpoczęto B-03A.

**Dowody:** nowy, oddzielny raport [NTFS requalification](tests/resilience/X01_NTFS_REQUALIFICATION_2026-10-26.md) i JSON (źródłowy SHA, profil, statusy oraz hashe lokalnych surowych artefaktów); historyczne qualification snapshots pozostają niezmienione. Bieżące `.tia-bridge/offline` oraz `.tia-bridge/resilience` przed uruchomieniem skopiowano do ignorowanego archiwum `pre-run-5b99356133a24fe5a84ccf893fcb9bfe`; runner pracował z osobnym UUID. Brak surowych logów, danych projektu, DLL lub credentiali w Git.

**Ocena instrukcji i decyzji:** porównano sekcja po sekcji AGENTS: zakres/stan, hierarchia, niezmienne granice, autoryzacja/testy, dyscyplina zmian, Conventional Commits i dane nieufne; oraz Instrukcja_ChatGPT: stan/role, hierarchia, workflow, standard zadania, bezpieczeństwo, bramki, dowody i zamknięcie. Pozostawiono oba dokumenty bez zmian: to raport wyniku istniejących kryteriów, nie zmiana workflow, autoryzacji, architektury, kontraktu ani progu. ADR 0001/0002, 07/08/09/16 pozostają semantycznie bez zmian; dopisano wyłącznie nowy odrębny wynik i linki, zachowując historyczne raporty. Nierozstrzygnięte kwestie handle growth pozostają jawnie UNKNOWN; nie są sprzecznością architektury/bezpieczeństwa wymagającą decyzji przed ograniczonym B-03A.

**Walidacja po dokumentacji:** pełny runner powyżej poprzedzał dodanie raportu i edycje dokumentów. Po zmianach wykonano lokalny JSON parse, test dokumentacyjnej zgodności D-11, `py -3 scripts/audit_quality.py`, diff/link review; wyniki zapisano po walidacji. Brak nowych testów Engineering, TIA/Runtime, zmian produkcyjnego Workera, testów progów i NF-11.

```text
REPOSITORY: X:\Projects\TIA_Bridge
FILESYSTEM: NTFS
BUILD RELEASE: PASS
OFFLINE CURRENT NTFS: FAIL (1 NF-08 idle CPU threshold)
HISTORICAL exFAT: FAIL
X-01 LIMITED D-11: PASS
X-01 ENGINEERING: PASS (93 historical; not rerun)
NF-11: NOT_RUN
WORKER HANDLE RISK: UNKNOWN
B-03A READINESS: READY (bounded read-only only)
```

## 2026-10-26 — D-12 / ADR 0003: kanoniczny model HMI i granica artefaktów Siemens

Zatwierdzono dokumentacyjnie sześć reguł HMI: niezależny kanoniczny model, nienaruszalne NATIVE w ExchangeUnit, zakaz zastępowania nieznanych pól z częściowego odczytu, precyzyjne capabilities, kwalifikacja ryzykownego importu i rozwój na podstawie dowodów. Zmieniono wyłącznie ADR 0003, 03/04/06/08/09, README i ten rejestr. D-11, PLC MVP, X-01, istniejące kontrakty, kod, testy i historyczne dowody bez zmian.

Ocena instrukcji sekcja po sekcji: AGENTS.md (zakres/role/granice/autoryzacja/testy/dyscyplina/kontrakty) i Instrukcja_ChatGPT.md (stan/role/hierarchia/workflow/zadania/bezpieczeństwo/ocena wyników) nie wymagają modyfikacji: nowy ADR konkretyzuje już wymagane deny-by-default, coverage, izolację vendor i zakaz nieuprawnionej rekonstrukcji, bez nowych uprawnień i bez zmiany ich semantyki. ADR 0001/0002, dokument 12 i contracts/v1 zachowane. Ryzyka częściowego HMI i częściowego importu są już ujęte w R-04/R-02, bez dublowania. Nie wykonywano testów TIA, Engineering/Runtime; audyt skryptem lokalnym pozostaje NOT_RUN (brak repo/terminala lokalnego).

## 2026-10-26 — weryfikacja klonu X: i audyt odwołań

**Stan repozytorium:** checkout `X:\Projects\TIA_Bridge`; przed fetch i po nim HEAD `9e8b7bfadb1f4e7b0fc3a793d01cbb82f54e5c07`, `origin/main` ten sam SHA; `main` śledzi `origin/main`, różnica 0/0. Remote fetch/push: `git@github.com:KrzysztofOle/TIA_Bridge.git`, ten sam projekt co zadany URL HTTPS. `git fetch origin` zakończył się bez aktualizacji; brak lokalnych commitów do wysłania, zmian wejściowych, dodatkowych gałęzi/worktree, konfliktów i stanów operacji Git. Submoduły nie są używane; Git LFS jest zainstalowany, ale repo nie ma plików LFS.

**Wolumin X:** `X:` = NTFS, Fixed, partycja Basic nr 3 fizycznego dysku Micron 1100 SATA 256 GB; nie jest mapowaniem SMB ani urządzeniem USB/wymiennym. Wolumin online/healthy, ok. 163 GB wolne w chwili kontroli. ACL katalogu repo ma właściciela bieżącego użytkownika; tymczasowy zapis, `os.replace` w tym samym woluminie i odczyt kontrolny PASS, pliki próbne usunięto. `fsutil fsinfo volumeinfo X:` zwrócił odmowę dostępu; typ NTFS potwierdzono niezależnie przez `Get-Volume`.

**Audyt repozytorium:** aktywne opisy starej lokalizacji w `README.md`, `Instrukcja_ChatGPT.md`, `src/TiaBridge.Host/README.md`, `docs/03_ARCHITECTURE.md`, `docs/09_RISKS_AND_DECISIONS.md`, `docs/16_RESILIENCE_RECOVERY.md`, `docs/02_REQUIREMENTS.md` i `tests/README.md` uogólniono, usuwając założenie, że repo/workspace nadal leży na M:/exFAT. Ścieżki do trwałego state na NTFS pozostają poza repo.

| Pozostawione odwołanie | Kategoria | Powód |
|---|---|---|
| `docs/10_EVIDENCE_AND_QUALITY.md`, wcześniejsze wpisy `Zmiany.md`, `docs/X01_TIA_V19_QUALIFICATION.md`, historyczny `tests/engineering/x01-qualification.json` | C — historyczne dane | Opisują checkout, pomiary, konfigurację M:/exFAT i dowody z czasu wykonania; nie wolno ich przepisywać jako bieżącego środowiska. |
| `tests/integration/TiaBridge.Host.Tests/MigrationAndPolicyTests.cs` — syntetyczny `M:\\_Moje_Programy\\TIA_Bridge\\state` | C — fixture testowy | Ujemny przypadek testuje odmowę niezatwierdzonej lokalizacji; nie wskazuje aktywnego workspace ani magazynu. |
| VS Code `globalStorage/storage.json` i `workspaceStorage` zawierają X: jako bieżący workspace oraz M: jako wcześniejszy wpis | E — lokalna historia IDE | Nie jest to konfiguracja uruchomieniowa projektu. Nie zmieniano ustawień użytkownika ani historii VS Code. `settings.json`, tasks/launch, dostępne zmienne środowiskowe, usługi i zadania planowane nie ujawniły aktywnej konfiguracji TIA_Bridge ze starą ścieżką. |
| `bin/`, `obj/`, `.vs/`, `.tia-bridge/` | D — generowane/lokalne | Pominięte w ręcznym audycie edycyjnym; nowe buildy i dowody pozostały ignorowane. Nie są wymagane czyszczenie ani odtwarzanie konfiguracji. |

W aktywnych instrukcjach nie pozostało pełne odwołanie `M:\\_Moje_Programy\\TIA_Bridge`. Żadnych danych historycznych, testowych TIA, SQLite, instalacji Siemens ani DLL nie przenoszono. Nie znaleziono innych aktywnych bezwzględnych ścieżek do starego repo. Stare wpisy M: w historii workspace VS Code można usunąć ręcznie z listy ostatnich folderów, jeśli użytkownik nie chce ich zachować.

**Kontrola architektury i instrukcji:** `docs/03_ARCHITECTURE.md`, `docs/04_SYNCHRONIZATION.md`, `docs/07_TEST_STRATEGY.md`, `docs/09_RISKS_AND_DECISIONS.md` i raport X-01 sprawdzono pod kątem separacji repo/state/workspace, profili NTFS/exFAT oraz granic read-only. D-11, progi, architektura, worker i historyczny X-01 nie uległy zmianie.

| Dokument | Ocena spójności |
|---|---|
| AGENTS: Zakres i stan; role i hierarchia | Nowy checkout jest celem, stary checkout pozostał wyłącznie materiałem historycznym; brak kopiowania danych. Zasady spójne, bez zmian. |
| AGENTS: Niezmienne granice; Autoryzacja i testy | Uruchomiono wyłącznie OFFLINE na Fake i syntetycznych danych; TIA/Engineering/Runtime bez zmian. Granice zachowane. |
| AGENTS: Dyscyplina zmian; standard commitów | Zmiany ograniczone do dokumentacji i wpisu audytowego; zakres Git sprawdzony, obowiązuje Conventional Commit po angielsku. |
| AGENTS: Dane nieufne i stabilność kontraktów | Kontrakty i parsery niezmienione. |
| Instrukcja_ChatGPT §1 — cel i stan | Nagłówek lokalizacji zmieniono na katalog bieżącego checkoutu; nie przypina instrukcji do litery dysku. |
| Instrukcja_ChatGPT §2–5 — odpowiedzialność, źródła, workflow i zadania | Bez zmian semantycznych; hierarchia oraz workflow pozostają zgodne z AGENTS. |
| Instrukcja_ChatGPT §6–8 — architektura, pakiety, autoryzacja | Granice Core/Worker i uprawnienia Siemens bez zmian; trwałe dane opisano niezależnie od M:. |
| Instrukcja_ChatGPT §9–12 — raporty, sekwencja, styl i kontrola spójności | Kryteria statusów zachowano; ten wpis dokumentuje ocenę wpływu i wyniki. |

**Weryfikacja:** .NET SDK 10.0.402 i Python 3.12.2 dostępne. Locked restore oraz Build Release PASS (0 warnings/errors). OFFLINE: 773/773 testów .NET PASS (235 Host, 538 Core), 31/31 testów Python PASS; generatory PASS; `audit_quality.py` PASS; NF-03 `repository-replace-control` na X:/NTFS PASS: 100 próbek 1 MiB bez worker (p95 6,1634 ms) i 100 podczas Fake Hang (p95 4,4434 ms), bez odmów. Idle-300s test PASS; nie zmienia to statusu historycznych testów X-01/D-11 ani nie jest kwalifikacją Engineering/Runtime. Początkowy `dotnet format` wykazał wyłącznie CRLF wygenerowane przez globalne `core.autocrlf=true`, sprzeczne z `.editorconfig` LF. Dla tego klonu ustawiono lokalnie `core.autocrlf=false`, odtworzono z indexu niezmienione pliki C# i ujednolicono LF w zmienionych dokumentach; ponowny `dotnet format whitespace --verify-no-changes` oraz `git diff --check` PASS. Pełny runner zgłosił FAIL na kroku formatowania przed korektą lokalnego checkoutu; nie zmieniono progów ani źródeł C#.

**Ocena wpływu:** zaktualizowano tylko opisy lokalizacji i statusu profilu exFAT. Architektura, synchronizacja, autoryzacja, test strategy, ADR, D-11 i historyczne evidence bez zmian. PASS dotyczy wyłącznie opisanych testów OFFLINE na X:/NTFS; Engineering i Runtime pozostają NOT_RUN.

## 2026-10-26 — X-01: izolowany Siemens V19 read-only

Stan wejściowy: czysty main/origin main `eca8a079b04001c792c0669b991edfbeb6faaca6`. Osobne jawne zadanie użytkownika autoryzuje odczyt dwóch otwartych projektów testowych V19 pod X:\TESTY_TIA_Bridge. D-11 ogranicza profil do lokalnego NTFS; nie zmieniono historycznych QUALIFICATION/qualification.json lub ograniczonej bramki. NF-03 exFAT FAIL, NF-11 NOT_RUN/B-10B i RUNTIME NOT_RUN pozostają odrębne.

- Dodano Worker.V19 net48/x64, resolver czterech dokładnych assembly z chronionej instalacji Siemens, exact PID/start/SID/AP19 binding, jednego właściciela STA/MTA, audit przed wywołaniami i bounded metadata/inventory. Nie dodano żadnego mutatora ani wywołania TiaPortalProcess.Dispose. Zwolnienie własnego TiaPortal po Attach nie zamyka TIA.
- Ten sam Host/Core/API/CLI/SQLite/outbox/scheduler obsługuje realny odczyt. Wspólne IPC ma wariant net48 i jawne 1.1 z immutable input artifact, bez Fake success template. Brak downgrade, retry native call i automatycznego sukcesu po timeout/late/old epoch. Realne metadane i częściowe Coverage są zweryfikowanymi artefaktami; brak eksportera lub pełnego modelu PLC/HMI jest jawny.
- Naprawiono również klasyfikację mieszanego native Device: zamiast nadpisywać PLC przez ostatnio odwiedzony HMI, rodzic ma UNKNOWN, a oba typy software pozostają w bounded inventory. Syntetyczna regresja obejmuje kolejność oraz nieznane typy. Naprawiono wykryte błędy guardu AP19 (separator końcowy po ValidateRoot) i coverage_refs (referencje muszą wskazywać trwałe artefakty, nie opisy). Dodano 25 regresji offline oraz niezależną walidację dziewięciu komunikatów IPC 1.1. Core i kontrakty publicznych 19 metod nie zmieniły się. Framework reference assemblies pobrano standardowym NuGet, bez instalacji systemowej; nie zmieniano grup lub zabezpieczeń Siemens.
- Osobny harness Engineering używa rzeczywistych procesów Host/CLI/V19 oraz istniejących barier StorageProbe dla utraty IPC/rodzica, starego epoch, late response i cancel. Naprawiono jego oczekiwanie 202 przy idempotentnym HTTP 200 oraz współdzielenie audit file podczas kontrolowanego odczytu. Cleanup zwalnia wyłącznie własne bariery; nie zabija TIA ani nie zakłada przerwania vendor call.
- Użytkownik wybrał B — samodzielną obsługę dialogu Siemens. Nie stosowano Computer Use lub globalnej whitelisty. Pierwszy Attach z timeoutem pozostaje FAILED w historii; późniejsze powodzenie nie zmienia pierwotnego wyniku. [Raport X-01](docs/X01_TIA_V19_QUALIFICATION.md) zawiera osobne dowody obu instancji, próby nieudane, pomiary i końcowy zakres kwalifikacji. Surowe logi/projekty/vendor DLL/discovery/tokeny i DB pozostają poza Git.

Wyniki końcowe X-01: oba projekty mają rzeczywiste Attach/read/Dispose PASS; 80 cykli STA/MTA + 7 scenariuszy awarii + 4 testy startup/loader + 2 odczyty po końcowym guardzie = 93 przypadki Engineering PASS. Metadane plików i IsModified przed/po bez zmian. Własne procesy kończą się, portale pozostają otwarte. Zarejestrowano wzrost uchwytów workera (STA 462→702, MTA 456→602); R-14/8 h soak nie został zamknięty. Runtime NOT_RUN.

Build Release offline/Engineering: PASS, 0 warning/error. Pełny validate_offline.py run a901583fdab74745bebadad476794a39: 763/764 .NET PASS, 1 FAIL (NF-03 M:/exFAT edit p95 178,5496 ms), 31/31 Python PASS; wymagane RT PASS. Po końcowych zmianach 25/25 V19OfflineTests PASS, w tym 9 przypadków nieobecnych we wcześniejszym pełnym buildzie. Unikalnie 772 PASS / 1 FAIL / 0 SKIP z 773 .NET; nie udajemy pojedynczego pełnego uruchomienia 773. Ponowne Python/audit/format/diff PASS. Historyczne snapshoty B-02D/D-11 niezmienione. Pełny OFFLINE i X-01 OVERALL: FAIL z powodu kryterium pełnej regresji; ENGINEERING w ograniczonym NTFS: PASS, B-03A READINESS: NOT_READY. Jawny zakres częściowy jest dozwolony przez zadanie użytkownika; nie zadeklarowano pełnego X-01 PASS ani nie rozpoczęto B-03A.

Ocena wpływu instrukcji i semantyki — obie instrukcje pozostawiono bez zmian:

| Sekcje | Ocena i uzasadnienie |
|---|---|
| AGENTS: Zakres i stan; Instrukcja §1–2 | Historyczny stan odniesienia nie jest deklaracją bieżącej implementacji; aktualny stan i granice X-01 zapisano w README/raporcie. Nie zmieniono podziału ról. |
| AGENTS: Role/hierarchia; Instrukcja §3–5 | Wykonano jedno jawnie zlecone X-01 po zatwierdzonej D-11; nie rozszerzano architektury ani autoryzacji. |
| AGENTS: Niezmienne granice; Instrukcja §6–7 | Vendor tylko net48/x64, jeden owner, wersjonowane DTO, trwały intent i nieblokujące API zachowano; cały B-03A nie jest ogłoszony jako wykonany. |
| AGENTS: Autoryzacja i testy; Instrukcja §8–10 | Read-only i osobne Engineering/Runtime, UNKNOWN/quarantine, default deny, brak replay, brak PLC/download pozostały obowiązkowe. GUI obsługiwał użytkownik zgodnie z B. |
| AGENTS: Dyscyplina/Conventional Commits; Instrukcja §11–12 | Nowe skrypty i live lane mają instrukcje; audyt/linki/format/diff i named-path staging są wymagane; raport zawiera ograniczenia zamiast pozornych PASS. |
| AGENTS: Dane nieufne; ADR 0002 i specyfikacje 03/04/05 | Zachowano rozmiary/głębokość, HMAC/epoch/fence i tożsamość Core; 1.1 jest jawnym rozszerzeniem wewnętrznego IPC. Brak zmian lifecycle, synchronizacji, grantów lub definicji SYNCED. |

## 2026-10-26 — D-11: ograniczona bramka X-01 read-only i pełna kwalifikacja

Stan wejściowy: czysty main/origin main `51d859f5b361b614672ccf9abb59a05606c461c5`. Użytkownik zatwierdził propozycję z dokumentu 16: ograniczony X-01 read-only i pełna kwalifikacja stabilności mają oddzielne kryteria. Zadanie wyłącznie dokumentacyjne; bez X-01, uruchamiania TIA/Openness, procesów Host/Fake, zmian kodu produkcyjnego, dysków, przenoszenia baz, cleanup B-02C lub dostępu do projektów na X:.

- Przeczytano i porównano obie instrukcje, README, dokumenty 02/03/07/08/09/16, oba historyczne snapshot, instrukcję harnessu i historię. Niejednoznaczność dotyczyła ogólnego minimum 8 h w 07, pełnej bramki w 08, odesłań obu instrukcji do 07/08 i oczekującej decyzji w 16/09/README/test instructions. R-14 już przypisywał soak do B-10B. [Dokument 16](docs/16_RESILIENCE_RECOVERY.md#historia-rozbieżności-i-odnośnik-archiwalny) zawiera wykaz miejsc i historię rozbieżności.
- D-11 rozdziela B-02 OFFLINE TEST EXECUTION, B-02 OFFLINE QUALIFICATION, X-01 LIMITED READ-ONLY GATE i PRODUCTION QUALIFICATION. Wymagane RT-03/05/06/15/16, recovery/UNKNOWN/quarantine/epoch, brak replay niepewnej mutacji i właściwe NF-01/02/03/04/05/06/12/14/15 obowiązują nadal. Idle ≥5 min i ≥1000 operacji są osobnymi dowodami, bez pozornego zaliczenia pełnego NF-08/NF-11. NF-11 ≥8 h pozostaje obowiązkowy w B-10B; R-14 aktywne.
- Historyczne QUALIFICATION.md i qualification.json zachowano bajtowo. [Nowa ocena](tests/resilience/X01_LIMITED_GATE.md) i JSON decyzji przypinają ich SHA-256 i źródłowy run. Faktyczne stare wyniki: 748 .NET/24 Python PASS, OFFLINE/GATE FAIL, X-01 NOT_READY, NF-03 exFAT FAIL, NF-11/ENGINEERING/RUNTIME NOT_RUN nie są zmieniane. Aktualna ocena ograniczonej bramki: PASS tylko dla udokumentowanego profilu lokalnego NTFS; pełna kwalifikacja offline badanego profilu: FAIL; produkcja: NOT_RUN. Reporter/matrix pozostają historyczne; nie są aktualnym interpretatorem D-11 ani przepustką dla nowego kodu/środowiska.
- Zweryfikowano istniejące dowody: wymagane RT PASS, 306,2474483 s idle i 1001 operacji/513,2943582 s. NTFS save/replace 1 MiB/N100/co 100 ms podczas Hang p95 3,2648 ms; repo exFAT kontrolne p95 205,5911 ms pozostaje FAIL. Bez delete+copy, relokacji lub osłabienia ACL. Dodatkowo sprawdzono wskazane regresje watchdog, queue deadlines i default deny, nie tylko ogólną deklarację reportera.
- Odnotowano deklarację użytkownika o dwóch projektach V19 w X:\TESTY_TIA_Bridge i zgodzie na ich przyszłe zmiany/utratę w osobnych eksperymentach. Filesystem X: NIEZWERYFIKOWANY; brak dostępu do projektów w tym zadaniu. Nie jest to izolacja PLC/HMI lub zgoda na download. Pierwszy X-01 read-only wymaga odrębnego zadania/autoryzacji i weryfikacji środowiska; późniejsze mutacje mają własne bramki.
- Dodano sześć testów wyłącznie zgodności dokumentacyjnej decyzji z niezmiennymi dowodami, bez uruchamiania silnika. Nie zmieniono narzędzi agregujących kwalifikację ani implementacji Host/Scheduler/Worker. Dotychczasowe wyniki pomiarów nie zostały ponownie wykonane w tym zadaniu.

Ocena instrukcji sekcja po sekcji i dokumentów normatywnych:

| AGENTS.md / Instrukcja_ChatGPT.md | Decyzja i uzasadnienie |
|---|---|
| Zakres i stan / §1–2 | Bez zmian: zadanie ograniczone do zatwierdzonej decyzji dokumentacyjnej; stan implementacji/dowodów opisuje README. |
| Role, hierarchia / §3 | Bez zmian: jawna decyzja użytkownika rozstrzyga zgłoszony konflikt; nie rozstrzygano samodzielnie nowej semantyki. |
| Niezmienne granice / §6 | Bez zmian: Core/vendor boundary, trwałość, UNKNOWN, quarantine, brak replay i SYNCED pozostają obowiązkowe. |
| Autoryzacja i testy / §4–5, §7–8 | AGENTS otrzymuje jeden punkt doprecyzowujący ograniczoną bramkę i lokalny NTFS. Dotychczasowe obowiązkowe scenariusze zachowano. Workflow, granice zadań, read-only, uprawnienia, backup, download/PLC/HMI i Computer Use bez zmian. |
| Bramka / §10 | Instrukcja §10 otrzymuje jedno doprecyzowanie D-11 zgodne z AGENTS i 07/08; NF-11 w B-10B, exFAT nadal niezakwalifikowany. PASS Fake nie dowodzi Siemens; osobne zadanie i autoryzacja X-01. |
| Dyscyplina, dane nieufne / §9, §11–12 | Bez zmian: ocena wpływu zapisana tutaj, źródła historyczne przypięte hashami, niewykonane testy pozostają NOT_RUN. |
| Git / §5, §11 | Bez zmian: jeden angielski Conventional Commit, jawne ścieżki/staged diff i normalny fast-forward push. |
| Architektura 03, ADR 0001/0002, kontrakty | Bez zmian: decyzja określa etap kwalifikacji, nie zmienia process isolation, DTO/lifecycle, UNKNOWN/quarantine lub autoryzacji; te dokumenty nie nakładają dodatkowej bramki 8 h. |

Zmienione pliki: AGENTS.md, Instrukcja_ChatGPT.md, README.md, Zmiany.md; docs/02_REQUIREMENTS.md, docs/07_TEST_STRATEGY.md, docs/08_IMPLEMENTATION_PLAN.md, docs/09_RISKS_AND_DECISIONS.md, docs/16_RESILIENCE_RECOVERY.md; tests/README.md, tests/resilience/README.md, tests/resilience/X01_LIMITED_GATE.md, tests/resilience/x01-limited-gate.json, tests/contracts/test_limited_gate_documentation.py. Historyczne wpisy poniżej pozostają bez zmian.

Walidacja: sześć testów zgodności dokumentacyjnej PASS; pełne discovery Python w istniejącym QA venv: 30 PASS/0 FAIL/0 SKIP. Próba pełnego discovery systemowym Python zgłosiła brak openapi_spec_validator (24 testy PASS i 1 błąd importu); nie instalowano pakietów, powtórzono we właściwym udokumentowanym środowisku. Kontrola hashy obu historycznych raportów PASS. Pierwszy audyt wykrył jeden błędny nowy anchor instrukcji testów, poprawiony; końcowy audit_quality.py PASS: 43 Markdown, 355 lokalnych odnośników, 0 błędów (23 zastane linki zewnętrzne rejestrowane, bez ponownego sprawdzania sieciowego). Pełny build/testy odporności, Engineering i Runtime nie są ponownie uruchamiane w zadaniu dokumentacyjnym. Diff i sekrety podlegają końcowej kontroli przed commitem. Praca kończy się po commicie/push bez X-01.

## 2026-10-25 — B-02D: resilience, crash recovery i izolacja procesów

Stan wejściowy: czysty main/origin main 11eda18ae43d90c09affeb3bb2cebdd1624ee423. Przeczytano aktualne instrukcje, dokumentację 02–09/13–15, kontrakty, Core/Host i testy B-01A–B-02C. Implementacja wykorzystuje ten sam SQLite/outbox/journal, lifecycle, scheduler i IPC v1. Brak Siemens, X-01, PLC/HMI, symulatorów, OPC/Snap7, Playwright lub migracji rzeczywistych projektów.

- Dodano harness w istniejącym Host.Tests/Resilience i rozszerzono StorageProbe o ten sam HostApi w osobnym procesie z wewnętrznym composition seam. Fault flags/barriers nie są dostępne w produkcyjnym HTTP, config lub argv. 48 nowych .NET i 4 Python reportera; plan pełnego przebiegu: 748 .NET/24 Python. Końcowy pełny run e46332b7aee247c28cc9ca3910e69c48: 748/748 .NET +24/24 Python PASS, FAIL 0/SKIP 0; wszystkie kroki Release/generatorów/audytu/formatu/diffu exit 0. Kwalifikacja NF-03 repo FAIL daje OFFLINE FAIL/exit 1 i GATE FAIL mimo test_execution_status PASS. X-01 formalnie NOT_READY; wariant techniczny read-only/NTFS oddzielony od zgody. [Snapshot](tests/resilience/QUALIFICATION.md).
- Bariery i kontrolowane Environment.Exit obejmują admission przed/po commit, dispatch intent/sent, receipt odebrany/utrwalony oraz result przed/po commit. IStorageFaults obejmuje journal/outbox i symulowane SQLITE_FULL bez zapełniania dysku. WorkerReceiptCommitted jest obserwacją po zapisaniu receipt, bez zmiany dotychczasowej atomowości.
- RT-03/05/06/15/16 używają rzeczywistych Host/CLI/Fake/Named Pipes i plikowej bazy NTFS. Syntetyczny P/B guard działa na jawnym stanie testowego adaptera po potwierdzonym pierwszym efekcie; nie jest planerem, readerem TIA ani kwalifikacją rzeczywistego Apply. Fake audit effect counter jest flushowany przed odpowiedzią; nie jest drugim ledgerem i nie steruje recovery.
- Testy obejmują dwa porządki cancel/dispatch, 100 identycznych submitów/8 klientów i 409, no heartbeat/HB bez postępu, stale epoch, lease expiry, UNKNOWN/quarantine po restarcie, own child/lock anomaly, unrelated same-name sentinel, osiem procesowych ataków IPC, corrupt/missing/truncated proof oraz publish orphan i uszkodzoną końcówkę result WAL. Hardware power loss i foreign SID pozostają NOT_RUN bez zatwierdzonego środowiska.
- Regresja 1001 operacji najpierw zakończyła się FAIL: limit per-worker history 1000 zatrzymywał kolejny dispatch. Naprawiono idle recycling wyłącznie potwierdzonego, własnego, bezczynnego workera. Nowy epoch i SQLite history pozostają; UNKNOWN/blocked nie jest recyklingowane ani ponawiane. Ponowny 1001 run PASS, każdy ID z jednym efektem i trwałym wynikiem.
- Health/8 klientów początkowo p95 116,6084 ms FAIL. GET health używa teraz raz zwalidowanego stałego envelope i typowanych wewnętrznych wartości zamiast ponownego pełnego ReadReport na każdym odczycie. Nie zmieniono wejściowej walidacji, formatu DTO ani autoryzacji; klient i durable reports nadal używają Core. Ponowny profil health PASS.
- Pierwszy profil startup/history N=6 miał p95/max 5660,20 ms FAIL (pozostałe odczyty health/result/events były responsywne). Profil NF-05 rozszerzono do stałych 20 startów, aby p95 miał rzeczywistą próbkę ogona; każdy pomiar, w tym cold-process outliers, jest zapisywany. Próg 5000 ms pozostaje bez zmian; starego FAIL nie usunięto. Nie kwalifikuje to zimnego restartu całego systemu operacyjnego.
- Pierwszy rzeczywisty 306 s idle: CPU 3,6966% FAIL przy RAM 89,82 MiB host/37,38 MiB worker. Przyczyną jest polling pustej kolejki co 20 ms; tylko IDLE zmieniono na 200 ms, aktywne wywołania pozostają 20 ms. Obserwacja aktywnego wywołania korzysta z wewnętrznej projekcji report/hash/event z tego samego metadata readera, bez ponownej walidacji zamrożonego requestu w każdej iteracji. Admission, dispatch i zapis stanu nadal wykonują pełną walidację Core; hash, operation ID, schema/state/outcome/method i powiązanie journal są sprawdzane także w projekcji. Nie zmieniono deadline ani progu NF-08; pełny pomiar jest powtarzany.
- Stop testowej ApiFixture zwalnia Process przez finally także po oczekiwanym nieudanym startup/assertion. Regresja potwierdza usunięcie własnego UUID bez opuszczenia procesu. Historyczny a71bad688cf24e0eb6e04f85d6474ab9 potwierdzono przez marker/config/syntetyczny UUID/own invalid Fake DLL; 0 aktywnych procesów/reparse i 7 poprawnych exclusive read opens. Poprzednia odmowa była polityką narzędzia, nie aktualnym lockiem NTFS. Katalog pozostawiono, bez obejścia kontroli.
- NF-03: NTFS save/replace i repo volume mają odrębne próby. W finalnym 100-próbkowym repo control p95 bez workera 205,5911 ms FAIL, podczas Hang 28,5801 ms PASS; zmienność występuje niezależnie od obecności Host, nie kwalifikuje repo globalnie. Nie było odmów w tym finalnym profilu, ale wcześniejsze AccessDenied pozostają w historii. Bezpieczny test kontrolny repo zachowuje oryginał przy odmowie; brak delete+copy/retry lub rozluźnienia ochrony. Failure repo exFAT nie staje się PASS dzięki zielonemu testowi izolacji lub pozytywnemu NTFS. Formalna kwalifikacja i OFFLINE uwzględniają rzeczywisty wynik.

Jawne doprecyzowanie użytkownika: nie wykonywać teraz 8 h soak; NF-11 NOT_RUN, pełna kwalifikacja długotrwałej stabilności w B-10B/R-14. Dokument 07 mówi minimum 8 h, a AGENTS odsyła do jego obowiązkowych testów przed X-01; R-14 przypisuje długi profil B-10B. Nie przyjęto samodzielnie wyjątku. [Raport](docs/16_RESILIENCE_RECOVERY.md) zawiera dokładne odwołania, skutki, warianty i minimalną propozycję do zatwierdzenia: ograniczone X-01 read-only wyłącznie na kwalifikowanym lokalnym NTFS po obowiązkowych RT/NF, bez uznawania tego za produkcyjną stabilność. Formalne readiness pozostaje NOT_READY, dopóki bramka nie zostanie rozstrzygnięta; X-01 nie jest uruchamiane.

Ostateczne pomiary: idle 306.25 s, CPU 0.9075%, RAM Host 89.33/Fake 37.34 MiB; status/8 p95 14.5978 ms, health/8 25.6193, submit/8 236.3253, startup/history N20 1087.9415; NTFS save/replace 3.2648 ms. Wszystkie percentyle i RT/NF w snapshot JSON/MD. Audyt końcowy: 42 Markdown/323 lokalne odnośniki/0 błędów; format, diff i kontrola 38 ścieżek pod kątem sekretów/danych/binariów PASS. 1001 operacji, 1 efekt na ID, nowy epoch po idle recycle, zero zgubionych wyników.

Lista zmienionych plików (38):

```text
README.md
Zmiany.md
docs/07_TEST_STRATEGY.md
docs/08_IMPLEMENTATION_PLAN.md
docs/09_RISKS_AND_DECISIONS.md
docs/16_RESILIENCE_RECOVERY.md
scripts/README.md
scripts/report_resilience.py
scripts/validate_offline.py
src/TiaBridge.FakeWorker/Program.cs
src/TiaBridge.FakeWorker/README.md
src/TiaBridge.Host/Api/ApiResponses.cs
src/TiaBridge.Host/Api/HostApi.cs
src/TiaBridge.Host/Api/HostRuntimeHooks.cs
src/TiaBridge.Host/Operations/ApiStorage.cs
src/TiaBridge.Host/Operations/ExecutionStorage.cs
src/TiaBridge.Host/Scheduling/OperationScheduler.cs
src/TiaBridge.Host/Storage/StorageContracts.cs
src/TiaBridge.Host/TiaBridge.Host.csproj
tests/COVERAGE.md
tests/README.md
tests/contracts/test_offline_runner.py
tests/contracts/test_resilience_report.py
tests/integration/TiaBridge.Host.Tests/ApiFixture.cs
tests/integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceArtifactTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceCrashTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceEvidence.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResiliencePerformanceTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceSafetyTests.cs
tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceSecurityTests.cs
tests/integration/TiaBridge.StorageProbe/HttpResilienceProbe.cs
tests/integration/TiaBridge.StorageProbe/IpcAdversary.cs
tests/integration/TiaBridge.StorageProbe/Program.cs
tests/resilience/QUALIFICATION.md
tests/resilience/README.md
tests/resilience/matrix.json
tests/resilience/qualification.json
```

Ocena obu instrukcji sekcja po sekcji:

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres, stan / 1–2 | Nowy ograniczony B-02D jest jawnie zlecony. Historyczny scaffold nie nadpisuje rzeczywistego stanu README; brak Siemens. |
| Hierarchia / 3 | Zachowano kontrakty/ADR. Rozbieżność NF-11/bramki opisana do decyzji, bez samowolnej zmiany normy. |
| Niezmienne granice / 6 | Core bez zmian/dependency Host; durable intent, brak replay UNKNOWN, niezmienne terminal history i odrębne heartbeat/progress. |
| Autoryzacja / 4–5, 8 | Tylko własne syntetyczne procesy/pliki. Nie zmieniono ACL, tokenów, grants, receiver policy ani TIA; foreign SID NOT_RUN. |
| Testy i bramki / 7, 10 | Rzeczywiste crash/IPC/WAL/artifact tests i pomiary; NF failure/NOT_RUN nie ukryte. Technical readiness NTFS oddzielne od formalnej decyzji i produkcji. |
| Dyscyplina, niezaufane dane / 9, 11–12 | Dowody w ignorowanym root, sanitized snapshots i hashe; stare/failed/duplicate dowody odrzucane. Brak surowych tokenów/logów/DB w Git. |
| Git / 5, 11 | Jeden krótki angielski Conventional Commit test(host), explicit staging/staged review, normalny fast-forward push bez przepisywania historii. |

AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002 i normatywne kontrakty v1 pozostają bez zmian; dokumenty 07/08/09 otrzymały wyłącznie odwołania do dowodów/otwartej decyzji, nie wyjątek bramki. R-08/R-14 nie są zamykane bez właściwej kwalifikacji. Po raporcie, commicie i push zadanie kończy się; bez automatycznego X-01 lub B-10B.

## 2026-10-25 — B-02C: lokalne HTTP API, CLI i komunikacja nieblokująca

**Stan wejściowy:** czysty main/origin main `9bcf216eee753ba2844eea346be1adb80cf4fcea`. Przeczytano aktualne instrukcje, README, dokumenty 02–09/13/14, Core/Host, contracts/v1, testy i historię. Wykorzystano B-01A–B-02B, bez drugiego magazynu, lifecycle, schedulera lub IPC. Zrealizowano wyłącznie Host/CLI i syntetyczne wykonanie; bez B-02D, Siemens, PLC/HMI, symulatorów, OPC, Playwright lub MCP.

- Host jest procesem ASP.NET Core Minimal API .NET 10. Udostępnia POST /v1/operations, GET status/result/events, POST cancel i GET diagnostics/health. Dokładne DTO i OpenAPI pozostają źródłem semantyki. Nowe długie zlecenie zwraca 202 dopiero po istniejącym commit intent/journal/outbox; handler nie czeka na workera i nie wykonuje snapshot/hash/scan. Duplicate daje 200 z istniejącym ID, inny canonical body 409. Wyłączenie profilu po restarcie nie ukrywa istniejącego idempotentnego wyniku.
- Pięć szybkich metod metadata POST korzysta z tego samego trwałego ledgeru, dziennika i outbox; akcja/cancel oraz terminalny raport są atomowe. Nie uruchamiają Fake. Wszystkie 19 nazw ma jawny HTTP outcome: wykonawca syntetyczny wyłącznie dla skonfigurowanych project.inspect/sync.apply, pięć metod metadata i UNSUPPORTED dla pozostałych. Nie ma fałszywego SUCCEEDED brakującego wykonawcy.
- Startup config jest wersjonowany, bounded i zamknięty; operator rejestruje ProjectKey przed API. Fake scenario/worker path/synthetic template nie pochodzą z HTTP. Domyślnie brak profilu oznacza deny; fake mutation wymaga osobnego jawnego enable w lokalnym config, bez grantów Siemens. host.fake.json jest syntetyczny, używa dev NTFS state i nie otwiera AP19. Względne ścieżki wymagają uruchomienia z root repo.
- Bind wyłącznie 127.0.0.1 i przydzielony port, niezależnie od ASPNETCORE_URLS. Wymuszone Production nie pozwala włączyć developer error pages przez env; testowy proces startuje z env Development i niezaufanym URL. Token bieżącej instancji ma 32 losowe bajty, SID/PID/start/instance binding i jest publikowany do user-only ACL discovery na lokalnym NTFS, poza repo. CLI sprawdza owner/DACL, containment/reparse/ADS, wersję, bounded/duplicate/unknown JSON i PID/start; sekret nie trafia do argv/URL/logów. Restart rotuje token. Każdy zasób operacji sprawdza principal w SQLite; request nie nadaje uprawnień innemu SID/projektowi.
- API sprawdza Host/loopback/Origin/Sec-Fetch/forwarded host, odmawia żądań przeglądarkowych, nie dodaje CORS wildcard. Brak endpointu dowolnej komendy, konfiguracji lub kill. Limity: 1 MiB request, bounded header/body time, 2048 requests/s, 256 POST/s i 64 concurrency/connections, z rezerwą dla metadata. Zamknięte query/JSON i stałe błędy bez payloadu. Kolejka ma atomowy limit 1000 aktywnych zleceń po lookup idempotencji; przy 999 wyścig 4 requestów daje 1 acceptance i 3 odmowy. Pełna kolejka pozostawia duplicate/status/health dostępne.
- Status jest odczytem aktualnego report/hash/journal bez request-schema revalidation, artifact IO lub blokady wykonawczej. Last-Modified i journal rozróżniają czas zmiany; worker heartbeat age jest związany z epoch. Health oddziela host/store/queue/scheduler/worker/progress/tia_probe/quarantine, bez live probe. Result 202 zwraca status; terminalny result weryfikuje zarządzane artefakty. Events zachowuje sequence, cursor i references. Cancel queued jest atomowy bez dispatch; running zapisuje żądanie i nie deklaruje przerwania. UNKNOWN/quarantine pozostaje po cancel/restart, bez clear/retry.
- CLI i TiaBridge.Client są klientami tego samego HTTP, bez własnej kolejki/SQLite/workera. Obsługują submit METHOD --request FILE, operation status/result/events/cancel/wait i health, JSON, stabilne kody 0/2/3/4/5/6, protected discovery i bounded HTTP. --wait wymaga --max-wait 1..3600; zakończenie CLI/Ctrl+C nie anuluje zaakceptowanej operacji. Brak token/url/force flags i automatycznego retry. Skróty składania requestu z --workspace/--scope pozostają niezaimplementowanym zakresem docelowym, nie brakującym wykonawcą CLI.
- Utrzymano SQLite WAL/FULL/FK, jeden writer i niezmienione migracje v1–v3. Host lifetime handle usuwa koszt checkpointu po zamknięciu ostatniego połączenia; osiem wyłącznych reader connections obsługuje krótkie metadata queries bez współdzielenia aktywnych command/transaction. Nie jest to drugi ledger/cache. Pełna walidacja kolejki następuje przed zmianą wygasłego zlecenia; zwykły skan deadline nie waliduje stale tych samych 1000 requestów. Default storage API bez Host lifetime zachowuje wcześniejsze reguły i testy awarii.

**Naprawa istniejącego błędu Core:** rzeczywisty cykl HTTP/CLI wykrył, że typed serialization pomija wymagane nullable ComponentHealth.age_ms=null. Najpierw dodano i uruchomiono regresję FAIL; potem generator oznaczył wymagane nullable property JsonIgnoreCondition.Never. Regresja i health z UNKNOWN/null przechodzą. Nie zmieniono schema 1.0, stanów, błędów lub wersji kontraktu. OpenAPI otrzymało aktualny opis serwisu i typowany Operation report dla GET result/202, bez nowych endpointów lub semantyki.

**Regresja startu workera:** końcowy przegląd wykrył, że błąd uruchomienia procesu przed dispatch mógł zatrzymać scheduler bez trwałej klasyfikacji. Test rzeczywistego Host z własnym niepoprawnym plikiem Fake DLL potwierdza teraz FAILED/WORKER_UNAVAILABLE/NOT_STARTED i dalszą dostępność API. Supervisor rozpoznaje exit przed handshake, ograniczonymi buforami odprowadza stdout/stderr własnego procesu i nie ujawnia nieufnego tekstu w konsoli Host. Nie było dispatch ani efektu, więc brak kwarantanny; błędy po dispatch nadal podlegają dotychczasowym regułom UNKNOWN.

**Pomiary i wcześniejsze FAIL:** początkowy NF-02 submit/8 klientów miał p95 402,3 ms i nie był deklarowany jako PASS. Lifetime SQLite/readers oraz ograniczenie ponownej walidacji poprawiły ten wynik. Pełny przebieg wykrył presję wielu równoległych startów Host na deadline workera; kolekcje procesowe HTTP są serializowane względem innych kolekcji, a projekty Core/Host runner wykonuje kolejno (-m:1), bez zwiększania timeoutów produkcyjnych i bez pomijania przypadków. Jeden z pełnych przebiegów miał 699 PASS/1 FAIL: startup Fake w istniejącym teście Completed_quarantined_operation konkurował z masowymi testami Core. Ten sam przypadek wykonany oddzielnie przeszedł w 1 s; runner izoluje teraz wykonanie projektów, zachowując wszystkie scenariusze i pomiary. Niepulsowany burst zapisu 1 MiB na wymiennym exFAT przekroczył NF-03 (p95 188,4 ms); taki profil i conditional replace exFAT nie są kwalifikowane. Próba rename napotkała także odmowę filesystemu; NF-03 sprawdza zapis pliku w miejscu, nie kwalifikację replace. Ostateczny profil edytora jawnie stosuje odstęp 100 ms, zachowuje wszystkie 100 próbek i te same progi. Jest to udokumentowane ograniczenie pomiaru, nie ogólna gwarancja opóźnienia dysku wymiennego.

**Ostateczny profil NF:** Windows 10.0.22000, .NET 10.0.13 x64, 8 procesorów logicznych; state NTFS, syntetyczny workspace exFAT; rzeczywisty Fake Hang. Pomiar 2026-10-25T17:06:58.8244419+00:00. Status 1 klient/1000 próbek: p50/p95/p99 4,9455 / 6,4832 / 8,1994 ms. Status 8 klientów/1000: 8,9800 / 14,9753 / 24,0917 ms. Submit 8 klientów/64: 92,3584 / 104,7887 / 115,3513 ms. Edycja 1 MiB/100 próbek/co ≥100 ms i niezależne SHA: 16,8563 / 32,8080 / 103,8299 ms. Progi NF-01/02/03 w tym profilu: PASS. Raporty metryk/TRX/logi są ignorowane w .tia-bridge/b02c i .tia-bridge/offline, nie w Git.

**Testy i wynik:** dodano 39 przypadków .NET — 38 Host (13 HTTP, 15 security, 8 metadata/CLI, 2 performance) i 1 Core regression. Łącznie 700/700 .NET (538 Core +48 Storage +76 Scheduler/IPC +38 HTTP/CLI), 20/20 Python, FAIL 0, SKIP 0. Wszystkie wcześniejsze regresje wykonano rzeczywiście. validate_offline.py sprawdza obowiązkowe nowe procesowe cases, pełny Release build, kontrakty/JSON Schema/OpenAPI/przykłady, generatory, Core dependency boundary, audyt, format i diff. Build Release 0 błędów/0 ostrzeżeń. Końcowy pełny runner: PASS, TRX 63f5fa663e68414faa4c6ea17cd8eaee. Audyt: 39 dokumentów Markdown/281 lokalnych odnośników, 0 błędów. Formatowanie, diff i kontrola 52 ścieżek pod kątem sekretów, danych projektowych i niedozwolonych binariów: PASS. Automatyczna kontrola odrzuciła cleanup jednego własnego UUID po wcześniejszym nieudanym teście (blocked by policy); syntetyczny katalog a71bad688cf24e0eb6e04f85d6474ab9 pozostaje poza repo w dev/storage-tests. Nie jest częścią dostawy ani działającego Host. Pliki testowe/sekrety/DB/binaries nie trafiają do indeksu.

**Lista zmienionych plików (52):**

```text
README.md
TiaBridge.slnx
Zmiany.md
config/examples/README.md
config/examples/host.fake.json
contracts/README.md
contracts/v1/openapi.json
docs/05_CODEX_INTERFACE.md
docs/08_IMPLEMENTATION_PLAN.md
docs/13_DURABLE_STORAGE.md
docs/14_SCHEDULER_IPC.md
docs/15_HTTP_API_CLI.md
examples/README.md
scripts/README.md
scripts/generate_contracts.py
scripts/validate_offline.py
src/TiaBridge.Cli/Program.cs
src/TiaBridge.Cli/README.md
src/TiaBridge.Cli/TiaBridge.Cli.csproj
src/TiaBridge.Cli/packages.lock.json
src/TiaBridge.Client/BridgeClient.cs
src/TiaBridge.Client/HostDiscovery.cs
src/TiaBridge.Client/README.md
src/TiaBridge.Client/TiaBridge.Client.csproj
src/TiaBridge.Client/packages.lock.json
src/TiaBridge.Core/Transport/V1/MethodModels.g.cs
src/TiaBridge.Host/Api/ApiResponses.cs
src/TiaBridge.Host/Api/HostApi.cs
src/TiaBridge.Host/Api/HostConfiguration.cs
src/TiaBridge.Host/Api/RequestGate.cs
src/TiaBridge.Host/Operations/ApiStorage.cs
src/TiaBridge.Host/Operations/ExecutionStorage.cs
src/TiaBridge.Host/Operations/SqliteOperationStore.cs
src/TiaBridge.Host/Program.cs
src/TiaBridge.Host/README.md
src/TiaBridge.Host/Scheduling/OperationScheduler.cs
src/TiaBridge.Host/Storage/SQLite/SqliteDatabase.cs
src/TiaBridge.Host/Supervision/WorkerSupervisor.cs
src/TiaBridge.Host/TiaBridge.Host.csproj
src/TiaBridge.Host/packages.lock.json
tests/COVERAGE.md
tests/README.md
tests/contracts/test_offline_runner.py
tests/integration/TiaBridge.Host.Tests/ApiFixture.cs
tests/integration/TiaBridge.Host.Tests/ApiMetadataAndCliTests.cs
tests/integration/TiaBridge.Host.Tests/ApiPerformanceTests.cs
tests/integration/TiaBridge.Host.Tests/ApiSecurityTests.cs
tests/integration/TiaBridge.Host.Tests/HttpApiTests.cs
tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj
tests/integration/TiaBridge.Host.Tests/packages.lock.json
tests/integration/TiaBridge.StorageProbe/packages.lock.json
tests/unit/TiaBridge.Core.Tests/RequiredNullableTests.cs
```

**Ocena wpływu instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres/stan i role / 1–2 | Jawne zadanie B-02C implementuje API/CLI. Pierwotny opis scaffold w instrukcjach pozostaje historyczny; stan rzeczywisty jest w README/15, bez modyfikacji instrukcji. |
| Hierarchia / 3 | Użyto istniejącej architektury, v1 DTO, ledgeru i schedulera. Brak konfliktu wymagającego nowej decyzji; required-nullable to naprawa zgodności z istniejącym schematem. |
| Niezmienne granice / 6 | Core bez Host/vendor, durable acceptance przed 202, brak inline workera/TIA, UNKNOWN bez replay, niezależne heartbeat/progress i status. |
| Autoryzacja / 4–5, 8 | Token/ACL daje wyłącznie lokalny transport i SID ownership. Brak sesji/grantów Siemens, GUI lub obcych procesów; własne testowe Host/CLI/Fake i pliki objęte zadaniem. |
| Testy/bramki / 7, 10 | Rzeczywisty pełny przepływ i pomiary z jawnym profilem. B-02D, soak, rzeczywisty drugi SID, power-loss i X-01 nadal odrębne. |
| Dyscyplina / 9, 11–12 | Dokumentacja, config/example, generator, testy i runner aktualne. Wyniki robocze i protected discovery poza Git; bez uruchamiania znalezionych skryptów. |
| Git / 5, 11 | Jeden angielski Conventional Commit feat(host), named-path staging, staged review i normalny push z HEAD check, bez force. |
| Dane nieufne / 6, 9 | Zamknięte bounded JSON/query/config/discovery, user-only ACL i principal checks, brak arbitralnego endpointu/komendy; result zachowuje integrity verification. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002, wymagania 02, architektura 03, synchronizacja 04, testy 07 i ryzyka 09 bez zmiany zatwierdzonej semantyki. Dokument 05/08 ma aktualny stan i odsyła do ograniczonej implementacji 15; dokumenty 13/14 rozróżniają starszy zakres i rozszerzenie B-02C. Core nie otrzymał zależności infrastrukturalnych. Brak nierozwiązanych błędów krytycznych w zakresie; burst exFAT/conditional replace, drugi SID, B-02D/soak/power-loss i Siemens pozostają poza dowodem. Gotowość do osobnego B-02D nie otwiera bramki X-01. Po potwierdzonym commit/push praca zostaje zatrzymana.

## 2026-10-25 — B-02B: scheduler, nadzorowany Fake Worker i IPC

**Wejście i zakres:** czysty main/origin main `216c550d87c03507a641903a6e3d546308848ccf`, B-01A–C/B-02A. Przeczytano aktualne instrukcje, README, dokumenty 02–09/13, ADR 0002, Core/Host, kontrakty i testy. Zrealizowano wyłącznie wykonanie syntetyczne offline, bez HTTP/CLI B-02C, pełnego harnessu B-02D, workera V19 lub uruchamiania Siemens/PLC/HMI/symulatorów. Nie kopiowano starego Bridge lub danych klienta.

- Dodano OperationScheduler z pętlą w tle i kontrolowanym PumpOnceAsync, IExecutionProfile domyślnie deny, istniejącym outbox i tym samym ledgerem. Transakcja dispatch obejmuje QUEUED/RUNNING, journal, trwały claim, attempt/step intent i lease metadata przed wysłaniem IPC. Refaktoryzacja RecordCore współdzieli istniejące reguły lifecycle i zapis atomowy zamiast drugiego kontraktu. Queue deadline działa także przy wiszącym call; cancelled queued nie jest wysyłane. Running cancel jest intentem do najbliższej potwierdzonej granicy, bez twierdzenia o przerwaniu.
- Wyłączny plik scheduler.lock, host PID/start/SID/epoch, rosnący fence i lease 15 s; jeden aktywny call na zatwierdzony state root. Worker ma osobny PID/start/epoch i portal/capability key. Stary owner jest odrzucany po zmianie fence lub wygaśnięciu lease. Utrata lease nie daje prawa nowej mutacji. Profile syntetyczne nie nadają capability/grantów Siemens.
- FakeWorkerSupervisor uruchamia wyłącznie własny wskazany Fake Worker, bez shell/okna/sekretów argv. Sprawdza PID peer pipe, start, nonce, ID/build; handshake 10 s, send 1 s. Odczyt IPC i heartbeat są niezależne od wykonania. Dispose nie killuje trwającego wykonawcy ani obcych procesów; Fake sam kończy swoje oczekiwanie po utracie pipe. Startup failure może zakończyć wyłącznie własne dziecko przed dispatch. Job Object/parent-loss/Dispose rzeczywistego V19 pozostają X-01, bez przenoszenia gwarancji Fake na Siemens.
- Nowa biblioteka TiaBridge.Ipc nie zależy od Core/SQLite/Siemens. IPC 1.0 ma dziewięć rodzajów DTO, oddzielny schema i wspólne przykłady .NET/Python. Named Pipe: CurrentUserOnly, FirstPipeInstance, losowa nazwa, nonce i sekret przez stdin. Length prefix, limit 1 MiB przed alokacją, HMAC-SHA256 constant-time, epoch/fence/sequence, shape/unknown/duplicate/depth/UTF-8 validation. To uwierzytelnienie/integralność, bez szyfrowania. Inline receipt/report ≤64 KiB; duże wejścia przez managed artifact ID/SHA z kontrolą containment/reparse/hash. Nie ma proxy Siemens.
- Oddzielny TiaBridge.FakeWorker realizuje sukces/opóźnienie, błędy przed/po rozpoczęciu, hang, brak heartbeat, heartbeat bez postępu, timeout, utratę IPC, exit, late success, niepewną mutację, safe cancel, duplicate dispatch, bounded retry i negatywne fence/planhash. Jeden serializowany call, bounded dedupe cache i synthetic effect counter. Nie wykonuje znalezionych skryptów. LateSuccess jest zwalniany jawnie przez identyczny duplikat dla powtarzalnego testu już oczekującej odpowiedzi po deadline, bez drugiej próby/efektu.
- Watchdog rozdziela host/worker heartbeat, last_progress_at i tia_probe_at=null. Heartbeat 2 s, suspect po 6 s, lease 15 s i ExecutionBudget z Core. Timeout nie dowodzi końca call. Read timeout pozostaje FAILED bez równoległego ponowienia do potwierdzonej quiescence; możliwa mutacja daje terminalne UNKNOWN/RECONCILIATION_REQUIRED i trwałą quarantine powiązaną z worker epoch/fence. Late/fenced receipts pozostają append-only dowodami i nie promują UNKNOWN do SUCCEEDED.
- Retry tylko dla jawnie kwalifikowanych bezefektowych odczytów po Rejected/0 effect/quiescent, maksymalnie 3 próby, backoff 1/2 s +0–25% jitter i wspólny deadline. Brak retry identity/schema/auth/conflict lub możliwej mutacji. Circuit breaker utrwalany per portal/capability: 3 awarie/60 s, cooldown 60 s, jeden read half-open. Quarantine ma pierwszeństwo i nie ma automatycznego clear/replay.
- Migracja 003 dodaje 5 tabel: scheduler_ownership, worker_processes, dispatch_attempts, worker_receipts, circuit_breakers i rozszerza quarantine o epoch/fence; łącznie 23 tabele, user_version=3. SQL 001/002 oraz ich hashe bez zmian. Regresja sprawdza v1→v3 i rzeczywisty baseline v2→v3, zachowanie requestów i idempotencji. Core i contracts/v1 nie zmieniły semantyki ani zależności; aktualizowano jedynie indeks README Core/kontraktów.
- Recovery nowego hosta obserwuje stare PID/start, bez adopcji/kill. RUNNING możliwej mutacji od razu UNKNOWN/quarantine; alive/unverifiable stary proces blokuje nowe calls. Nowy read może ruszyć po potwierdzonym końcu starego read procesu i braku UNKNOWN/quarantine. Stary wynik pozostaje w historii. Nie implementowano uzgadniania projektu TIA ani automatycznego readback/Save/rollback.

**Testy:** 76 nowych przypadków .NET (SchedulerTests 29, SchedulerSafetyTests 13, IpcAndPolicyTests 34), 3 nowe Python IPC. Pełny zestaw ma 661 .NET (537 Core +48 Storage +76 nowych) oraz 20 Python. Testy używają własnych plikowych SQLite i artefaktów na lokalnym NTFS, rzeczywistych Fake Worker/Named Pipes oraz deterministycznego crash procesu hosta Environment.Exit(76) po Started. Obejmują między innymi 1000 metadata status reads/p95≤100 ms przy fake hang, cancel race, duplicate effect counter=1, brak overlap, retry/backoff/breaker, heartbeat bez postępu, restart read/mutation, late/fenced reply i lease expiry bez zwolnienia quarantine. Wspólny validate_offline.py wymaga nowych przypadków procesowych i dziewięciu fixtures IPC, bez pomijania starych bramek.

**Wykryte i poprawione w trakcie B-02B:** zbyt szeroki kod diagnostyki Fake nie mieścił się w error subset konkretnej metody — zachowano surowy kod w receipt i mapowano niedopuszczalny publiczny kod na istniejący WORKER_UNAVAILABLE, bez zmiany kontraktu. Pełny przebieg wykrył też wyścig receipt/deadline: już oczekujący sukces mógł wyprzedzić watchdog. Host sprawdza deadline przed użyciem wyniku; deterministyczna regresja wymusza obecność ramki przed krokiem hosta i potwierdza UNKNOWN z zachowanym late receipt. Pierwszy pełny przebieg z tym błędem był FAIL; ostateczne dowody są podane niżej po ponownej walidacji. Lokalny błąd kodowania skryptu dokumentacji poprawiono przez odtworzenie wyłącznie własnych edycji i jawny UTF-8; skrypt roboczy pozostaje ignorowany.

**Ocena wpływu instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / 1–2 | Jawnie zlecony B-02B to syntetyczny scheduler, nie serwer/adapter TIA. Stan w README/14; obie instrukcje bez zmian. |
| Hierarchia / 3 | Zastosowano istniejące ADR 0002, lifecycle i SQLite; brak nowej semantyki lub konfliktu architektonicznego. |
| Niezmienne granice / 6 | Core bez infrastruktury/vendor, intent przed dispatch, jeden call, UNKNOWN bez replay, epoch/fence/lease bez domniemania zatrzymania TIA. Brak nowego SYNCED/baseline. |
| Autoryzacja / 4–5, 8 | Własne testowe procesy/DB/artifacts i Git objęte zadaniem. Profile Fake nie nadają sesji/grantów. Bez Computer Use i obcych procesów. |
| Testy i bramki / 7, 10 | Realne procesy Fake i IPC, nadal wymagane B-02C/D przed X-01. Brak deklaracji pełnego resilience gate, Engineering/Runtime lub power-loss. |
| Dyscyplina / 9, 11–12 | Nowy schemat/przykłady/testy/docs, wspólny runner i jawne wyniki. Robocze logi/TRX/bazy/binaries poza Git. |
| Git / 5, 11 | Jeden krótki angielski Conventional Commit feat(host), named-path staging, staged review, normalny push i HEAD check. |
| Dane nieufne / 6, 9 | Bounded IPC, HMAC/identity/sequence, shape i artifact/hash/reparse checks. Brak wykonywania treści lub dowolnych nazw metod. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002 i normatywne dokumenty 02–07/09 bez zmiany semantyki; B-02B implementuje ich ograniczony zakres. Backlog 08 ma wyłącznie nowy stan; dokumenty 13/14 i indeksy rozróżniają historię Storage od obecnego wykonania Fake. Obcy SID/ACL deployment, wrogi proces tego samego użytkownika, pełny harness, sprzętowy power-loss i worker V19 pozostają poza dowodem. Brak publicznego HTTP/CLI, uzgadniania TIA i automatycznego clear quarantine. Gotowość do osobno zlecanego B-02C nie otwiera bramki Siemens. Po zweryfikowanym commit/push praca zostaje zatrzymana.

**Pliki (52):** `README.md`, `TiaBridge.slnx`, `Zmiany.md`, `contracts/README.md`, `contracts/ipc/v1/message.schema.json`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/13_DURABLE_STORAGE.md`, `docs/14_SCHEDULER_IPC.md`, `examples/ipc/v1/cancel.json`, `examples/ipc/v1/dispatch.json`, `examples/ipc/v1/heartbeat.json`, `examples/ipc/v1/hello.json`, `examples/ipc/v1/progress.json`, `examples/ipc/v1/ready.json`, `examples/ipc/v1/receipt.json`, `examples/ipc/v1/started.json`, `examples/ipc/v1/stop.json`, `scripts/README.md`, `scripts/validate_offline.py`, `src/TiaBridge.Core/README.md`, `src/TiaBridge.FakeWorker/Program.cs`, `src/TiaBridge.FakeWorker/README.md`, `src/TiaBridge.FakeWorker/TiaBridge.FakeWorker.csproj`, `src/TiaBridge.FakeWorker/packages.lock.json`, `src/TiaBridge.Host/Operations/ExecutionStorage.cs`, `src/TiaBridge.Host/Operations/SqliteOperationStore.cs`, `src/TiaBridge.Host/README.md`, `src/TiaBridge.Host/Scheduling/CircuitBreaker.cs`, `src/TiaBridge.Host/Scheduling/ExecutionModels.cs`, `src/TiaBridge.Host/Scheduling/OperationScheduler.cs`, `src/TiaBridge.Host/Storage/Migrations/003_execution.sql`, `src/TiaBridge.Host/Storage/SQLite/SqliteDatabase.cs`, `src/TiaBridge.Host/Supervision/WorkerSupervisor.cs`, `src/TiaBridge.Host/TiaBridge.Host.csproj`, `src/TiaBridge.Host/packages.lock.json`, `src/TiaBridge.Ipc/PipePeer.cs`, `src/TiaBridge.Ipc/Protocol.cs`, `src/TiaBridge.Ipc/README.md`, `src/TiaBridge.Ipc/TiaBridge.Ipc.csproj`, `src/TiaBridge.Ipc/packages.lock.json`, `tests/COVERAGE.md`, `tests/README.md`, `tests/contracts/test_ipc_contracts.py`, `tests/contracts/test_offline_runner.py`, `tests/integration/TiaBridge.Host.Tests/IpcAndPolicyTests.cs`, `tests/integration/TiaBridge.Host.Tests/MigrationAndPolicyTests.cs`, `tests/integration/TiaBridge.Host.Tests/SchedulerSafetyTests.cs`, `tests/integration/TiaBridge.Host.Tests/SchedulerTests.cs`, `tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj`, `tests/integration/TiaBridge.Host.Tests/packages.lock.json`, `tests/integration/TiaBridge.StorageProbe/Program.cs`, `tests/integration/TiaBridge.StorageProbe/packages.lock.json`.

**Walidacja końcowa:** pełny validate_offline.py: PASS; Build Release 0 błędów/0 ostrzeżeń; 661/661 .NET (FAIL 0, SKIP 0), 20/20 Python. Wszystkie 19 operacji/API schema/OpenAPI, 100 deterministycznych modeli, syntetyczne przykłady Workspace/Capabilities i dziewięć IPC nadal przechodzą. Testy procesowe/watchdog/quarantine/restart i regresje B-01A–C/B-02A wykonano rzeczywiście; brak mock-only deklaracji. audit_quality.py: PASS, 37 Markdown i 249 lokalnych odnośników. Format/diff i kontrola sekretów/binariów: PASS. Raporty/logi/TRX w ignorowanym .tia-bridge/offline oraz .tia-bridge/b02b; bazy i artefakty poza repo na NTFS. OFFLINE PASS, ENGINEERING NOT_RUN, RUNTIME NOT_RUN.

## 2026-10-25 — B-02A: trwały magazyn operacji SQLite

**Zakres:** biblioteka Host Storage/Operations w .NET 10, migracje SQLite, durable admission/idempotency/journal/outbox/artifacts i syntetyczne testy na lokalnym NTFS. Wejściowy czysty main/origin main: `cfe2e20d1504d8fd25a54753149ebbb74e1c7dfd`. Zweryfikowano rozwiązanie .NET, aktualne instrukcje, README, dokumenty 02–09, Core, kontrakty i testy B-01A–C oraz rejestr zmian. Nie kopiowano starego Bridge. Potwierdzono C: jako Fixed/NTFS, M: jako Removable/exFAT. Nowe testy używają wyłącznie własnych UUID katalogów `%LOCALAPPDATA%\TIA_Bridge2\dev\storage-tests`, bez otwierania produkcyjnego state i bez SQLite w repo.

- Dodano TiaBridge.Host jako bibliotekę, bez entrypointu serwera, HTTP/CLI, schedulera, supervisora, workera lub wykonywania zleceń. Core i wire v1 pozostają bez zmian. Host używa istniejących ProjectKey/OperationId, ContractJson, RequestIdentity, OperationLifecycle i DTO; nie tworzy alternatywnego modelu domenowego.
- SQLite: Microsoft.Data.Sqlite 10.0.13, lockfile, osobne private-cache połączenia per wywołanie, pooling=false, WAL, synchronous=FULL, FK, lokalny writer gate i BEGIN IMMEDIATE między procesami, bounded timeout 5 s. Brak retry efektu operacji. Domyślny root state pod LOCALAPPDATA/TIA_Bridge2; jawne inne root tylko w lokalnym katalogu aplikacji, na stałym NTFS, poza repo. Obce katalogi/bazy, UNC/SMB/device paths, exFAT i reparse points są odrzucane. Marker inicjalizacji DB zapobiega cichej rekreacji pustego ledgeru po usunięciu znanego pliku.
- Dwie migracje: 001 tworzy 18 tabel (14 docelowych encji oraz schema_migrations, operation_results, outbox, operation_artifacts), 002 dodaje indeksy i triggery ochronne. Ledger hashy SQL po normalizacji CRLF→LF, application_id/user_version, integrity_check i foreign_key_check blokują przyszły/obcy/uszkodzony schemat. Upgrade jest transakcyjny, bez down migration/resetu; v1→v2 zachowuje dane. Sessions/steps/leases/snapshots/plans/baselines/capabilities/authorizations mają tylko struktury potrzebne dalszym etapom, bez ich logiki biznesowej.
- Admission: walidacja i snapshot wejścia Core, krótka transakcja obejmująca trwały klucz, UUID operacji, wire/canonical body/SHA, ACCEPTED/NOT_STARTED, pierwszy event i PENDING outbox; acceptance dopiero po COMMIT. UNIQUE(principal,project_scope,key) działa także dla globalnego NULL project dzięki wygenerowanemu scope. Identyczne żądanie zwraca to samo ID, inne daje IDEMPOTENCY_MISMATCH. Nie oparto poprawności wyłącznie na pamięci procesu.
- Journal/result: CAS rewizji i istniejący lifecycle Core, atomowe state+event+terminal proof, immutable history i zakaz nadpisywania wyniku. Deferred composite FK wiąże bieżący report/hash z eventem. Terminalny UNKNOWN pozostaje w historii, zamyka outbox i minimalnie kwarantannuje projekt. ReadStatus czyta wyłącznie journal metadata; Read/ReadResult i duplicate acceptance sprawdzają integralność wymaganych artefaktów. Historyczny status nie jest świeżym dowodem zgodności TIA/Runtime.
- Outbox: trwały rekord per admission, PENDING→CLAIMED→DISPATCH_RECORDED→CLOSED; CAS nadaje jeden claim UUID, również przy wyścigu procesów. Nie ma expiry/release/requeue, automatycznego wznowienia po restarcie ani konsumenta wykonującego pracę. Quarantine blokuje Pending/claim/dispatch record także dla wcześniej przejętego wpisu. Claim nie jest grantem, lease ani gwarancją exactly-once Siemens.
- Artifacts: managed UUID IDs/paths, exclusive temp, stream limit 1 GiB, Flush(true), SHA-256 dokładnych bajtów, move bez overwrite na tym samym NTFS, readonly i dopiero potem referencja SQLite. Weryfikacja hash/size/path i uchwyt odmawiający write/delete chronią referencję przez commit wyniku. Uszkodzony/brakujący/niekompletny artifact blokuje poprawny wynik. Temp i niepowiązane opublikowane pliki są raportowane i zachowywane bez adopcji, GC i agresywnego usuwania; registered artifacts są pinned. Nie ma transakcji atomowej obejmującej zarazem filesystem i SQLite.
- Obsługa błędów nie ujawnia SQL, principal, request ani ścieżek. Po próbie commit StorageException.CommitMayHaveSucceeded sygnalizuje możliwość trwałego zapisu mimo braku potwierdzenia; jedynym bezpiecznym ponowieniem admission jest ten sam key/request, bez ponawiania wykonania. Generic IO, SQLITE_FULL i Windows disk-full mają kontrolowane kody. Fault seam domyślnie jest NoStorageFaults; nie pochodzi z JSON ani CLI aplikacji.

**Testy i dowody:** dodano 48 testów Host — 9 OperationStorageTests, 14 FailureAndRestartTests, 13 MigrationAndPolicyTests, 12 ArtifactStorageTests. Obejmują plikowy SQLite, 100 równoczesnych nowych zgłoszeń (1 nowe +99 duplicate), cztery niezależne procesy ścigające admission i outbox claim, deterministyczny Environment.Exit(73) przed/po COMMIT i po artifact flush/publish, odczyt wyników SUCCEEDED/UNKNOWN w nowym procesie, awarie SQL event/outbox, rollback terminal proof/quarantine, migracje i ich błędy, WAL/FULL/FK, korupcję/brak bazy i artefaktu, przerwany input stream, NTFS/repository/foreign/junction policy. Kontrolowane SQLITE_FULL i Windows disk-full IO injection nie zapełniają realnego dysku; nie kwalifikują sprzętowego power-loss. StorageProbe jest krótkim procesem testowym, bez fake workera/IPC/API. Sprzątanie sprawdza własny UUID root i nie przechodzi przez reparse points; po przebiegu brak pozostawionych katalogów testowych.

Rozszerzono istniejący validate_offline.py: build całego solution, oba projekty testowe, osobne TRX w katalogu UUID bieżącego przebiegu, agregacja i obowiązkowe przypadki Storage. Nie można nadpisać raportu Core przez Host lub przyjąć starego TRX jako wyniku nowego przebiegu. Wszystkie 537 regresji .NET i 17 Python B-01A–C nadal są wykonywane. Dodano ignorowanie lokalnych plików SQLite/WAL/SHM; nie jest to zgoda na umieszczanie bazy w repo. Pipeline offline nadal nie instaluje pakietów i używa pustego feedu/cache; nowe zależności pobrano raz zwykłym restore. Sprawdzenie podatności Host z transitive packages przez aktualnie dostępne źródła NuGet nie zgłosiło podatności w chwili kontroli; nie jest to trwała gwarancja bezpieczeństwa.

**Ocena wpływu instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / 1–2 | Zrealizowano jawnie zlecony B-02A jako bibliotekę Storage. Serwer i adapter nie istnieją; README/Host/13 opisują stan. Instrukcje pozostawiono bez zmian. |
| Hierarchia / 3 | Zastosowano zatwierdzone SQLite/outbox i granicę Core/Host. Brak nowej semantyki operacji, architektury lub konfliktu wymagającego rozstrzygnięcia. |
| Niezmienne granice / 6 | Trwały ID/intent przed acceptance, brak inline TIA, UNKNOWN bez replay, state poza exFAT/repo, osobne lifecycle/sync/Compile/Runtime. Brak nowych baseline lub SYNCED. |
| Autoryzacja i Computer Use / 4–5, 8 | Zapis własnych testowych SQLite/artifacts i Git jest objęty zadaniem. Nie nadano grantów, nie uruchomiono TIA/GUI ani obcych sesji. RegisterProject nie potwierdza bindingu Siemens. |
| Testy i bramki / 7, 10 | B-02A sprawdza trwałość, nie cały async engine. One-owner/epoch/fence, lost worker/lease, late result, scheduler i fake gate pozostają B-02B–D przed X-01. |
| Dyscyplina i raporty / 9, 11–12 | Dokumentacja, schemat SQL, macierz, runner i testy uzupełnione; wyniki robocze ignorowane. Kod danych wejściowych/artefaktów nie jest wykonywany. |
| Standard Git / 5, 11 | Jeden angielski Conventional Commit feat(host), named-path staging, staged review, normalny push i porównanie HEAD. Bez force i bez zmiany historii. |
| Dane nieufne / 6, 9 | Core waliduje wire, SQL ma parametry/FK/constraints, pliki mają limity/managed paths/hash, obcy schema/root i reparse points blokują. Brak parserów/wykonywania vendor formatów. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md bez zmian. ADR 0001/0002, wymagania 02, architektura 03, synchronizacja 04, interfejs 05, strategia 07 i decyzje 09 pozostają normatywnie bez zmian: B-02A je implementuje w ograniczonym zakresie, nie ustanawia uprawnień ani nowej sagi. Backlog 08 otrzymał tylko stan realizacji; dokument 13, Host README, tests README/COVERAGE i scripts README zawierają konkretną implementację i granice. Core oraz contracts/v1 bez zmian. Kwalifikacja ACL deployment, odporność na wrogi proces tego samego użytkownika i realny power-loss pozostają poza dowodem B-02A.

**Pliki (31):** `.gitignore`, `README.md`, `TiaBridge.slnx`, `Zmiany.md`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/13_DURABLE_STORAGE.md`, `scripts/README.md`, `scripts/validate_offline.py`, `tests/README.md`, `tests/COVERAGE.md`, `tests/contracts/test_offline_runner.py`; `src/TiaBridge.Host/TiaBridge.Host.csproj`, `packages.lock.json`, `README.md`, `Operations/SqliteOperationStore.cs`, `Storage/StorageContracts.cs`, `Storage/StorageLocation.cs`, `Storage/SQLite/SqliteDatabase.cs`, `Storage/Artifacts/FileArtifactStore.cs`, `Storage/Migrations/001_initial.sql`, `002_guards.sql`; `tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj`, `packages.lock.json`, `StoreFixture.cs`, `OperationStorageTests.cs`, `FailureAndRestartTests.cs`, `MigrationAndPolicyTests.cs`, `ArtifactStorageTests.cs`; `tests/integration/TiaBridge.StorageProbe/TiaBridge.StorageProbe.csproj`, `packages.lock.json`, `Program.cs`. DLL, bin/obj, bazy, raw logs i artifacts nie trafiają do Git.

**Walidacja OFFLINE PASS:** locked restore offline, Build Release 0 błędów/0 ostrzeżeń, **585/585 .NET** (537 regresyjnych +48 Storage), **17/17 Python**, FAIL 0, SKIP 0. Testy restartu/idempotencji/concurrency/migracji/transakcji/artefaktów rzeczywiście wykonano na lokalnym NTFS, nie :memory:. Pełny validate_offline.py, oba generatory, format, diff, kontrola sekretów/ścieżek i audit_quality.py: PASS; audyt 34 Markdown, 214 lokalnych odnośników, 19 metod. TRX/run/quality/logi w `.tia-bridge/offline/`, dodatkowe robocze wyniki w `.tia-bridge/b02a/`, poza indeksem.

**Granice i zakończenie:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. To warstwa trwałości, nie gotowy async engine. Nie uruchamiano zadań, Siemens, PLC/HMI, kompilacji lub symulacji. Brak nierozwiązanych problemów blokujących zadany zakres; B-02B jest gotowy do osobnego zlecenia na tym fundamencie, ale nie został rozpoczęty. Po commicie i zweryfikowanym push praca nad B-02A zostaje zatrzymana.

## 2026-10-25 — B-01C: walidacja kontraktów i Core

**Zakres i wejście:** czysty main/origin main `5fe5bda6eb288e7aa8b13c58fd859ea84a8e01d9`. Ponownie wykonano 160 testów .NET i 10 Python B-01A/B-01B: PASS. Przeczytano aktualne instrukcje, README, dokumenty 02–09 i 12, contracts/v1, Core, testy i dotychczasowy rejestr. Audyt testów wykazał brak pełnej macierzy wymaganych params/results/errors per metoda w .NET, tylko wybrane lifecycle edges, ograniczone przypadki kodowania/rozmiarów oraz brak 100 różnych deterministycznych scenariuszy. Macierz zachowań z konkretnymi testami, lukami i uzupełnieniami znajduje się w `tests/COVERAGE.md`; nie deklaruje procentowego branch/line coverage.

- Dodano wspólny korpus 39 mutacji × 19 metod = 741 ewaluacji request/report/error, odtwarzany w .NET oraz niezależnie przez JSON Schema i schematy danych OpenAPI. Wymagane params/results i obecne zagnieżdżone struktury są mutowane pojedynczo; wszystkie error_codes per metoda mają test poprawnego raportu/standalone error i round-trip, brakujących pól, obcego kodu i UNKNOWN+retry. Nowe required features, wersje major/minor, błędne typy/UUID i granice Unicode odrzucane zgodnie z v1.
- Dodano 100 osobnych, odtwarzalnych xUnit cases: 10 rodzin nazw × 5 wariantów capability/coverage × 2 normalizatory. Każdy ma inny ObjectId, model i golden raw/model SHA-256 wyznaczony przez Python hashlib. Testuje mapowanie/decode, NFC/case/truncation/suffix collisions, manifest/unit hashes, znane zbiory w innej kolejności, wersje, pl-PL/tr-TR, round-trip i niezmienność wejść. Nie powstał silnik planowania.
- Dodano wszystkie 147 kombinacji lifecycle current/next/outcome, testy idempotency i budget dla każdej metody, odrębność phase/state/cancel/sync status, dokładne granice 1 MiB request/acceptance, 10 MiB raportu i raw event 64 KiB oraz +1, cztery klasy błędnego UTF-8 na wszystkich wire granicach. Dodano adversarial graphs/metadata/coverage i poprawny rename/move po pełnej walidacji oraz kontrolę tranzytywnej closure assembly Core. Testy ścieżek/reparse points są modelowe; nie wykonują IO Workspace ani nie dowodzą TOCTOU safety.

**Dwa wykryte i poprawione błędy (najpierw regresja FAIL, potem PASS):**

1. B-01A `IdempotencyKey` akceptował niepoprawny UTF-16, a typowany zapis wire mógł zastępować uszkodzony surrogate znakiem U+FFFD. Dodano strict UTF-8 validation w konstruktorze i string converterze ContractJson; niepoprawny input jest odrzucany bez echo payloadu. Poprawny Unicode i dotychczasowe canonical hashes pozostają zgodne.
2. `OperationStatusResult` / `OperationResultResult` miały enumy state/outcome, lecz brakowało wspólnej walidacji lifecycle. Odczyt statusu mógł więc przenosić docelowe SUCCEEDED/UNKNOWN. Generator stosuje istniejące reguły lifecycle także do tych projekcji; zaktualizowano operation-result.schema.json, operation.schema.json i OpenAPI. Nie dodano nowych stanów/operacji, nie zmieniono lifecycle table ani zatwierdzonej semantyki i wersji 1.0. Reguła envelope SUCCEEDED dotyczy sukcesu odczytu, a reguła projekcji — osobno operacji docelowej.

**Powtarzalny przebieg:** `scripts/validate_offline.py`, uruchamiany Python z izolowanego venv QA, wykonuje preflight Core boundaries, locked restore z pustego lokalnego feedu i cache, Build Release, .NET/TRX, unittest JSON Schema/OpenAPI/przykłady, oba generatory w trybie check, audyt dokumentacji, format i diff. Brak dependency/testu, fail, skip lub niepełna wymagana macierz daje FAIL; nie instaluje pakietów ani nie przechodzi do publicznego feedu. NuGetAudit=false dotyczy wyłącznie offline restore, nie jest raportem aktualnych podatności. Pakiety i lockfiles bez zmian. Raporty/logi wyłącznie w ignorowanym `.tia-bridge/offline/`; testy helpera runnera używają izolowanych synthetic temp fixtures, bez projektów klienta.

**Rozdzielenie backlogu zgodnie z bieżącym zleceniem:** B-01C sprawdza 100 istniejących modeli/wejść. Kryterium co najmniej 100 rzeczywistych deterministycznych planów zachowano i przeniesiono do B-04A, gdzie powstanie planer W/P/B/DAG. Zaktualizowano wyłącznie odpowiednie wiersze/stany backlogu i odnośnik w strategii testów. Nie przedstawiono modeli jako wykonanego sync.plan. B-02, X-01 i Siemens pozostają poza zakresem.

**Ocena instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / 1–2 | Implementacja nadal ograniczona do Core i narzędzi offline; brak serwera/adapterów. Stan B-01C uaktualniono w README, instrukcje pozostawiono zgodnie ze zleceniem. |
| Hierarchia / 3 | Naprawy egzekwują istniejące reguły Unicode i lifecycle; brak nowej semantyki. Rozdzielenie 100 modeli/planów wynika z jawnej decyzji w tym zadaniu, nie samodzielnego osłabienia backlogu. |
| Niezmienne granice / 6 | Core bez Siemens; brak ledgeru, schedulera, baseline/receipt/SYNCED i operacji TIA. Stan, phase, outcome oraz sync/Runtime pozostają osobne. |
| Autoryzacja i Computer Use / 4–5, 8 | Zadanie offline i Git, bez grantów ani GUI; wpis capability nadal nie nadaje SessionPermission. Nie używano Computer Use. |
| Testy i bramki / 7, 10 | Nowe testy i pipeline nie zastępują B-02A–D na fake ani X-01. Bramka przed każdym rzeczywistym Siemens, także read-only, pozostaje bez zmian. |
| Dyscyplina zmian i raporty / 9, 11–12 | Schematy/OpenAPI odtworzono generatorem, dodano macierz i jedno polecenie testów, wyniki podano jako OFFLINE. Dane wyłącznie syntetyczne, raporty poza Git. |
| Standard Git / 5, 11 | Jeden angielski Conventional Commit typu test(core), named-path staging, staged review, normalny push i porównanie HEAD; historia bez zmian. |
| Dane nieufne / 6, 9 | Więcej testów limitów, strict Unicode, malformed JSON, wersji i grafu. Brak parsera native XML/YAML; surowy schema check nie jest dowodem IO ani zgodności TIA. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md pozostają bez zmian. ADR 0001/0002, wymagania 02, architektura 03, synchronizacja 04, interfejs 05, integracja 06 i decyzje 09 nie wymagają zmiany: architektura, saga, prawa i lifecycle są zachowane. Strategia 07 wskazuje aktualne dowody offline; backlog 08 uwzględnia zlecone rozdzielenie modeli i przyszłego planowania. Nie ma nierozstrzygniętego konfliktu architektury/kontraktu blokującego ten pakiet. Brak znanych nierozwiązanych krytycznych błędów w zbadanym Core nie oznacza dowodu pełnego pokrycia wszystkich możliwych wejść.

**Zmienione pliki (27):** `README.md`, `Zmiany.md`, `contracts/README.md`; `contracts/v1/openapi.json`, `operation-result.schema.json`, `operation.schema.json`; `docs/07_TEST_STRATEGY.md`, `docs/08_IMPLEMENTATION_PLAN.md`; `scripts/README.md`, `scripts/generate_contracts.py`, `scripts/generate_test_vectors.py`, `scripts/validate_offline.py`; `src/TiaBridge.Core/README.md`, `src/TiaBridge.Core/Operations/OperationModels.cs`, `src/TiaBridge.Core/Transport/V1/ContractJson.cs`; `tests/README.md`, `tests/COVERAGE.md`, `tests/contracts/test_core_matrix.py`, `tests/contracts/test_offline_runner.py`, `tests/fixtures/contract-mutations.v1.json`, `tests/fixtures/core-determinism.v1.json`; `tests/unit/TiaBridge.Core.Tests/TiaBridge.Core.Tests.csproj`, `BoundaryTests.cs`, `ContractMatrixTests.cs`, `DeterminismTests.cs`, `LifecycleMatrixTests.cs`, `WorkspaceAdversarialTests.cs`. Starsze testy/fixtures B-01A/B-01B zachowano.

**Walidacja OFFLINE PASS:** 537/537 .NET (377 nowych + 160 regresyjnych), 17/17 Python (7 nowych + 10 regresyjnych), FAIL 0, SKIP 0. Potwierdzono 100 osobnych deterministycznych model cases, 19 metod request/result/error, niezależne golden SHA-256, wszystkie syntetyczne przykłady, zgodność generatorów i Core bez vendor refs. Pełny pipeline uruchomiono ponownie po poprawkach; locked restore offline i Build Release: 0 błędów/0 ostrzeżeń. Audyt: 32 Markdown, 190 lokalnych odnośników, 19 metod, bez błędów; formatowanie i diff PASS. Kontrola nazw/rozszerzeń i sygnatur sekretów oraz ręczny diff objęły tylko wskazane pliki; raporty, binaria, projekty Siemens i dane klienta pozostają poza indeksem. Wyniki TRX/QA/run.json w `.tia-bridge/offline/` są lokalnym dowodem, nie artefaktem produkcyjnym.

**Zakończenie i granice:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. Testy rzeczywistego IO/native round-trip i 100 planów pozostają odpowiednio w B-03/B-05B i B-04A, trwałość/kolejka/awarie procesu w B-02A–D. Fundament i walidacja są gotowe do osobno zleconego B-02A; B-02A ani integracja Siemens nie zostały rozpoczęte. Po commicie i zweryfikowanym push praca nad B-01C zostaje zatrzymana.

## 2026-10-25 — B-01B: Workspace i Capabilities

**Zakres:** implementacja offline w Core .NET 10, syntetyczne schematy/konfiguracje/manifesty, testy i dokumentacja. Wejściowy main/origin main: `c1590b7afd02c8edb423ec96c6a50197af619e95`, czysty worktree. Potwierdzono rzeczywistą implementację i wejściowe 91/91 testów B-01A; przeczytano obie instrukcje, README, dokumenty 02–09, ADR 0001/0002, contracts/v1, Core i rejestr zmian. Nie kopiowano kodu starego Bridge ani danych klienta.

- Workspace: konfiguracja wielu projektów, manifest pojedynczego eksportu, PLC/Unified/Classic, hierarchia grup, niezależne ObjectId z B-01A, locatory/identity evidence, relacje i rewizje source/native. Jednostka wymiany może obejmować wiele obiektów i plików; nie powstał importer pojedynczego logicznego obiektu.
- Path codec konkretyzuje dokument 04: NFC, percent UTF-8, prefiks `_` dla reserved/trailing names, sufiks 12 cyfr UUID, deterministyczne skracanie segmentu do 100 UTF-16 bez cięcia runy/escape, oryginalna nazwa w metadanych. Decode weryfikuje te metadane. Kolizje case/Unicode/skrócenia/sufiksu są rozróżnione lub odrzucane. Limit root+relative 240 UTF-16, bez spłaszczania hierarchii. Traversal/rooted/UNC/ADS i unsafe Windows paths są odrzucane.
- Obserwacje filesystemu są tylko modelem: wymagany pełny łańcuch od woluminu przez przodków workspace do celu, każdy reparse point blokuje. Nie wykonano IO Workspace ani sprawdzania rzeczywistych symlinków; przyszły adapter nadal musi obsłużyć freshness/TOCTOU i bezpieczne uchwyty.
- Trzy nowe schematy 1.0, typowana walidacja grafu/referencji/membership, coverage z jawnymi brakami, zamknięte pola i vendor_fields zachowujące unknown JSON. Native XML/YAML pozostają nieprzetwarzanymi referencjami do plików. Raw/model/unit hashes są osobne; unit hash wiąże wszystkie deklarowane pliki i metadane członków, profile, coverage, rewizje i zależności. Nie potwierdza faktycznych bajtów dysku ani projektu TIA. Nie powstaje baseline, receipt ani status SYNCED.
- Canonical JSON ma sortowanie ordinal, dokładne liczby dziesiętne bez zaokrągleń, zachowaną semantyczną kolejność tablic i limity 10 MiB wejścia/wyniku, depth 32, token number 128 i exponent ±1000. Odrzucane są duplicate keys, invalid UTF-8/surrogates, comments, NaN, trailing commas i przyszłe wersje. Bez resolvera sieciowego i parserów vendor.
- Capabilities: wersjonowany snapshot registry, hash, dokładny TIA/adapter/build/family/kind/language/OperationMethod, zakresy read/write, coverage i normalizer/profile. Rozdzielone designed/implemented/test-qualified, domyślna odmowa, brak fallbacków/wildcard. Kwalifikacje są niezależnym wejściem zweryfikowanym przez przyszłego zaufanego callera; ich deklaracja w JSON nie wystarcza. SessionPermission zawsze NOT_EVALUATED. Brak domyślnych wpisów Siemens i wykonywania grantów.
- Przykłady syntetyczne: dwa projekty, PLC, obie rodziny HMI, zagnieżdżone grupy, CON, Pump/pump, postacie Unicode, jawne zależności, nieobsługiwane typy, częściowy coverage i współdzielona jednostka hmi.js/hmi.yml. Zerowe raw/model hashes to placeholdery bez artefaktów; unit i capability hashes policzone nad zadeklarowaną treścią. synthetic.offline nie jest istniejącym adapterem.

**Zmienione pliki (27):**

- Core: `src/TiaBridge.Core/Workspace/WorkspaceModels.cs`, `WorkspacePaths.cs`, `WorkspaceValidation.cs`, `ArtifactJson.cs` oraz `src/TiaBridge.Core/Capabilities/CapabilityRegistry.cs`.
- Schematy: `contracts/v1/workspace-manifest.schema.json`, `workspace-config.schema.json`, `capability-registry.schema.json`; opis `contracts/README.md`. Istniejące DTO, schematy operacji, generator B-01A, identity i katalog 19 metod pozostają bez zmian.
- Konfiguracje: `config/examples/workspace.synthetic.json`, `capabilities.synthetic.json`, `README.md`; manifesty `examples/workspace/plc.manifest.json`, `hmi.manifest.json`; indeks `examples/README.md`.
- Testy: `tests/unit/TiaBridge.Core.Tests/WorkspaceTests.cs`, `CapabilityTests.cs`, `TiaBridge.Core.Tests.csproj`, `tests/contracts/test_workspace_contracts.py`, instrukcja `tests/README.md`.
- Dokumentacja: `README.md`, `src/TiaBridge.Core/README.md`, `docs/03_ARCHITECTURE.md`, `docs/04_SYNCHRONIZATION.md`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/12_WORKSPACE_CAPABILITIES.md`, `Zmiany.md`.

**Ocena wpływu i zgodność instrukcji sekcja po sekcji:**

| AGENTS.md / Instrukcja_ChatGPT.md | Ocena i decyzja |
|---|---|
| Zakres i stan / sekcje 1–2 | Core B-01B jest implementacją offline; adapter i serwer nadal nie istnieją. Stan przyrostu opisany w README, bez zmiany instrukcji zgodnie z jawnym zadaniem. |
| Hierarchia / sekcja 3 | Zachowano zatwierdzone reguły 04 i D-06. Rozmieszczenie czystego registry w Core wynika z B-01B i zlecenia; 03 doprecyzowano organizacyjnie, Adapters będzie konsumentem/dostawcą deklaracji. Nie ustanowiono konkurencyjnego registry ani zmiany semantyki. |
| Niezmienne granice / sekcja 6 | Brak Siemens/host/dispatch. Manifest/hash nie nadaje SYNCED ani dowodu native HMI. W/S/B i outcome nie są utożsamiane. |
| Autoryzacja / sekcje 4–5, 8 | Kod nie tworzy grantów i nie wykonuje sesji. Zgodnie z zadaniem uprawnienia są NOT_EVALUATED. Nie użyto Computer Use ani operacji API TIA. |
| Testy i bramki / sekcje 7, 10 | Wyłącznie syntetyczne B-01B i regresje B-01A. Nie rozpoczęto B-01C, B-02, X-01 ani live testów. Pełna bramka fake przed Siemens pozostaje. |
| Dyscyplina zmian / sekcje 9, 11–12 | Zaktualizowano właściwe schematy/przykłady, opis formatu, build/test instrukcję i ten raport; dowody robocze w ignorowanym .tia-bridge. |
| Standard Git / sekcje 5, 11 | Jeden angielski Conventional Commit, wyłącznie nazwane ścieżki, staged review, normalny push i porównanie HEAD. |
| Dane nieufne / sekcje 6, 9 | Bounded JSON, unknown preservation albo odmowa, deterministyczne ścieżki/hashe, capability per exact version/scope. Nie wykonuje się znalezionych skryptów. |

Decyzja: AGENTS.md i Instrukcja_ChatGPT.md pozostają bez zmian; pakiet realizuje ich reguły, nie ustanawia nowego workflow/uprawnień. ADR 0001/0002 oraz wymagania 02, interfejs 05, integracja 06, test strategy 07 i decyzje 09 pozostają bez zmian: architektura procesów, saga, semantyka 19 operacji i kwalifikacja Siemens nie zostały zmienione. Dokument 03 precyzuje miejsce czystych modeli wobec przyszłych adapterów, 04 odsyła do zrealizowanego formatu, 08 rejestruje stan pakietu. Brak nierozstrzygniętych konfliktów architektury lub kontraktu blokujących B-01B.

**Walidacja OFFLINE:** 160/160 testów .NET (91 B-01A + 69 B-01B, 0 pominiętych), 10/10 testów niezależnych JSON Schema/OpenAPI (6 B-01A + 4 B-01B). Oba manifesty oraz obie konfiguracje przeszły schema i walidację Core, wraz z bindingiem Workspace/CapabilitySetHash. Locked restore i build Release: 0 błędów, 0 ostrzeżeń. Formatowanie dotnet, reprodukowalność generatora B-01A, audit_quality.py (31 Markdown, 169 lokalnych odnośników, 19 metod, 8 pierwotnych negatywnych przypadków), diff --check i kontrola ścieżek/sekretów/danych projektowych: PASS. Brak nowych zależności i zmian lockfile. Testy Python uruchomiono w istniejącym osobnym venv qa-b01a-20261025 na lokalnym NTFS; systemowy Python nie posiada openapi-spec-validator, więc jego niepełny przebieg nie jest traktowany jako PASS. Wymagane testy wykonano ponownie kompletnie w venv. Raporty TRX i QA pozostają w ignorowanym .tia-bridge/.

**Granice:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. Nie uruchamiano TIA/Openness/workera, symulatorów, PLC/HMI ani pełnej synchronizacji. Nie weryfikowano prawdziwych plików Workspace; surowe hashe i coverage są deklaracjami modelu, a SessionPermission nie jest grantem. Gotowość do osobno zleconego B-01C: modele/kontrakty i syntetyczne fixtures są dostępne; B-01C nie rozpoczęty. Po commicie i push B-01B praca zostaje zatrzymana.

## 2026-10-25 — B-01A: kontrakty i rdzeń operacji

**Zakres wykonany:** biblioteka Core .NET 10 i walidacja offline; bez hosta HTTP, SQLite, trwałego ledger, workera, fake workera i wykonywania operacji. Stan wejściowy: czysty main, HEAD/origin main `5a249d0`; obie instrukcje zawierają aktualne Conventional Commits. Przeczytano wymagane dokumenty, schematy, przykłady, registry i integrację 06. Nie kopiowano starego Bridge ani danych klienta.

- Zaimplementowano niemutowalne, rozróżnialne tożsamości ProjectKey/ObjectId/OperationId/SessionEpoch, binding projektu i sesji oraz ContractVersion. Tożsamość obiektu nie zawiera nazwy/ścieżki; UUID nie jest dowodem tożsamości Siemens ani uprawnień.
- Zaimplementowano OperationRequest, IdempotencyKey/RequestIdentity, ExecutionBudget/metadane, typy błędów, stany i fazę. Reguły lifecycle nie przywracają terminalnego UNKNOWN do kolejki i nie uznają timeoutu za przerwanie wywołania. Status synchronizacji, kompilacji, symulacji i testów Runtime pozostaje oddzielny.
- Uzupełniono 19 kontraktów o zamknięte DTO wyników, kody/kategorie błędów, acceptance, binding sesji i OpenAPI 3.1.1. Wyniki są podsumowaniami i odwołaniami do dowodów; szczegółowe schematy manifestów/planów/baseline należą do następnych pakietów. Zachowano wersję wire 1.0, nazwy, required params, timeouty i uprawnienia. Doprecyzowano górny zakres after_sequence do Int64 i walidację stan/outcome/wynik; nie dodano operacji ani grantów.
- Walidacja odrzuca nieznane pola, duplicate keys, błędny UTF-8/JSON, wersje/enumy, przekroczenia rozmiaru/głębokości i błędne liczby. Kanonikalizacja sortuje klucze ordinal, normalizuje zadeklarowane UUID i dokładne liczby całkowite, zachowując treść i kolejność tablic. Idempotencja porównuje uwierzytelniony principal/projekt/key i SHA-256 body bez samego key; nie realizuje trwałej deduplikacji efektów.
- Diagnostyka błędnego wejścia nie ujawnia payloadu, nazw nieznanych pól ani wyjątków parsera. Referencja grantu jest wyłącznie formatem; faktyczna polityka i odbiorca pozostają do niezależnej weryfikacji. Brak vendor DLL, shell executor, klienta sieciowego i dostępu do projektów.
- Dodano deterministyczny generator schematów/OpenAPI/DTO C# i 38 poprawnych przykładów oraz niezależny zestaw 21 niepoprawnych żądań. Testy sprawdzają także zgodność artefaktów z generatorem.
- Skorygowano wyłącznie skrót kolejności backlogu: pełne B-02A–B-02D na fake przed Openness i X-01. Pozostałe zależności bez zmian; historyczny zapis rozbieżności zachowano.

**Pliki:** utworzono `TiaBridge.slnx`, `global.json`, projekt Core z packages.lock.json, `Identity/Identities.cs`, `Operations/OperationModels.cs`, `OperationLifecycle.cs`, `OperationCatalog.g.cs`, `Transport/V1/ContractJson.cs`, `WireModels.cs`, `MethodModels.g.cs`; nowe schematy acceptance/error/operation-result/session-identity i openapi.json w contracts/v1; 39 plików przykładów w examples/contracts/v1; scripts/generate_contracts.py i requirements-qa.txt; projekt testowy z lockfile i dwiema klasami w tests/unit/TiaBridge.Core.Tests oraz tests/contracts/test_contracts.py. Zaktualizowano operation-request.schema.json, operation.schema.json, README główne/Core/contracts/examples/tests/scripts, dokument 05, wskazany akapit backlogu i ten rejestr. Registry i pierwotne przykłady zachowano.

**Ocena wpływu instrukcji:** porównano zakres z AGENTS.md i Instrukcja_ChatGPT.md; pozostawiono je bez zmian zgodnie z zadaniem, ponieważ reguły architektury, autoryzacji i workflow nie uległy zmianie. README i dokumentacja kontraktów opisują aktualny fragment implementacji. Brak nierozstrzygniętych konfliktów architektury lub semantyki blokujących B-01A. Canonical profile, limity i granice walidacji opisano w contracts/README.md.

Kontrola Git: reguła ignorowania `*.ap*` obejmuje także nazwy request.sync.apply.json i response.sync.apply.json. Po kontroli syntetycznej treści te dwa dokładne pliki dodano przez named-path `git add -f --`, bez zmiany .gitignore ani dodawania projektów Siemens. Pozostałe ścieżki dodano zwykłym selektywnym add; raporty i binaria pozostały poza indeksem.

**Walidacja OFFLINE PASS:** locked restore, build Release (0 błędów, 0 ostrzeżeń), 91/91 testów .NET (0 pominiętych), 6/6 niezależnych testów JSON Schema/OpenAPI, wszystkie 19 kontraktów i 21 przykładów negatywnych, domyślne sprawdzenie generatora, formatowanie dotnet, audit_quality.py (29 Markdown, 136 lokalnych odnośników, 19 metod i 8 pierwotnych negatywnych kontroli), diff --check. Test regresyjny obejmuje równoległą walidację i chroni przed współdzieleniem mutowalnych constraintów ewaluatora. NuGet vulnerability scan dla obu projektów i zależności przechodnich: brak zgłoszonych podatności w użytych źródłach w chwili kontroli. Zależności .NET są przypięte lockfile; narzędzia QA Python zainstalowano w osobnym venv na lokalnym NTFS. Raporty robocze pozostają w ignorowanym .tia-bridge/.

**Granice:** ENGINEERING NOT_RUN, RUNTIME NOT_RUN. Nie uruchamiano TIA/Openness/symulatorów ani operacji PLC/HMI. Gotowość do osobno zleconego B-01B: fundamenty dostępne; B-01B, B-01C i B-02A–D nie rozpoczęte. Testy B-01A są pomocniczym warunkiem jego odbioru, nie realizacją przyszłego pipeline B-01C ani kwalifikacją fake/Siemens. Commit techniczny i normalny push zamykają wyłącznie ten pakiet.

## 2026-10-25 — standard komunikatów commitów Git

- W `AGENTS.md` wprowadzono Conventional Commits: angielskie, zwięzłe tematy `type(scope): short description`, opcjonalny scope, typ zgodny ze zmianą i opis rozszerzony tylko przy potrzebie technicznej; niekompatybilność oznaczana przez `!` i `BREAKING CHANGE:`.
- Ocena wpływu workflow: zaktualizowano również `Instrukcja_ChatGPT.md`, aby zadania i ocena wyników stosowały ten sam standard, z jawnym wyjątkiem od polskiego języka odpowiedzi. Komunikaty Git nie służą sprawozdawczości pracy ani celom podatkowym/księgowym; szczegóły pozostają w dokumentacji i raportach.
- Przejrzano istniejącą dokumentację pod kątem sprzecznych zaleceń: nie znaleziono konfliktów poza tymi plikami. Pozostałe dokumenty, kod, kontrakty i historia Git bez zmian; nie rozpoczęto B-01A ani operacji Siemens.

## 2026-10-25 — standaryzacja instrukcji ChatGPT i CODEX (P1–P6)

Zakres: wyłącznie `AGENTS.md`, `Instrukcja_ChatGPT.md` i ten rejestr; kontrola dokumentacji, commit i push. Czysty lokalny `main` zaktualizowano fast-forward z `759fbad` do `70c287d`, uwzględniając trzy istniejące commity instrukcji ChatGPT i jej rejestracji. Nie rozpoczęto B-01A, implementacji aplikacji ani operacji Siemens/PLC/HMI.

Przeczytano obie instrukcje, README, wymagania 02, architekturę 03, synchronizację 04, testy 07, backlog 08, ryzyka i decyzje 09 oraz dotychczasowy rejestr zmian. Dodatkowo sprawdzono ADR 0002 i zakres walidatora QA. Zachowano zatwierdzone decyzje, kontrakty JSON/API oraz podział Core/Host/Worker; dokumentacja nadal opisuje projekt, nie działającą aplikację.

| Problem | Status | Zmieniony dokument | Sposób rozwiązania |
|---|---|---|---|
| P1 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Oddzielono świeże warunki wejścia i zweryfikowany backup od importu, Compile, Save, niezależnego readbacku i promocji B przez CAS po weryfikacji całej grupy. Zachowano sagę 04, grant ADR 0002 i MODIFIED przy W różnym od S. |
| P2 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Ujednolicono hierarchię; jej kolejność nie uprawnia do zmiany architektury, bezpieczeństwa ani semantyki kontraktu. Konflikt wymaga raportu, rekomendacji i decyzji użytkownika; zależna praca czeka. |
| P3 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Wymagane nieblokujące przyjęcie, trwały ID i historia po restarcie, dostępny status przy zawieszeniu, idempotencja i zakaz ponowienia UNKNOWN. Progi i szczegółowe scenariusze pozostają w 02/07/08. |
| P4 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Jawna bramka B-02A–B-02D na fake obejmuje wszystkie osiem wymaganych scenariuszy przed rzeczywistym Openness, także read-only X-01; X-01 kwalifikuje worker przed B-03A. |
| P5 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Dodano wzajemne odnośniki i komplementarny podział: analiza/planowanie/zadania/ocena ChatGPT oraz wykonanie CODEX. |
| P6 | PASS | `AGENTS.md`, `Instrukcja_ChatGPT.md` | Obowiązkowa ocena wpływu i kontrola obu instrukcji oraz właściwych specyfikacji po zmianie zasad; decyzja z uzasadnieniem w Zmiany.md, bez automatycznego wymogu edycji obu instrukcji. |

**Ocena wpływu i porównanie sekcja po sekcji:**

- Sekcje 1–2 ChatGPT i „Zakres i stan” AGENTS: zachowano stan projektowy, role i granicę starego repo; doprecyzowano relację instrukcji (P5).
- Sekcja 3 ChatGPT i nowa hierarchia AGENTS: zgodna procedura konfliktów (P2); sekcje 4–5 ChatGPT i „Dyscyplina zmian” AGENTS zachowują ograniczone zadania, autoryzację i dowody.
- Sekcja 6 ChatGPT i „Niezmienne granice” / „Autoryzacja i testy” AGENTS: uzgodniono P1/P3 z 02/03/04 i ADR 0002. Pamięć P, zapis D, W/S/B i rewizje C/E/T pozostają oddzielne.
- Sekcje 7 i 10 ChatGPT oraz bramka testów AGENTS: zgodne zależności B-02A–D → X-01 → B-03A, bez przedwczesnej integracji (P4).
- Sekcje 8–9 ChatGPT i „Autoryzacja i testy” / „Dane nieufne i stabilność kontraktów” AGENTS: zachowano bezpieczeństwo PLC/HMI, osobne uprawnienia, wymagany coverage i rozdział OFFLINE/ENGINEERING/RUNTIME.
- Sekcje 11–12 ChatGPT i „Dyscyplina zmian” AGENTS: zgodne raportowanie, kontrola wpływu i obowiązek zapisania decyzji (P6).

Decyzja: zmienić obie instrukcje, ponieważ P1–P6 dotyczą ich wspólnego workflow i jego oceny. README pozostawić bez zmian: ma już odnośniki do obu instrukcji i aktualny stan projektu. ADR, wymagania, architektura, synchronizacja, strategia testów, backlog, kontrakty i przykłady pozostawić bez zmian: instrukcje precyzują ich stosowanie, nie ustanawiają nowej architektury, API ani progów NF.

**Dodatkowa rozbieżność do przeglądu:** skrót „Najkrótsza droga” w [backlogu](docs/08_IMPLEMENTATION_PLAN.md) wymienia tylko 2A/2B przed 3A/3B, podczas gdy zależności etapu 3 wskazują 2A–2D, a bramka etapu 2 wymaga testu przed rzeczywistym Siemens. Skrót pozostawiono bez edycji jako poza zakresem P1–P6; rekomendacja: przy osobnej aktualizacji backlogu uzgodnić go z pełną bramką. Nie jest to zgoda na pominięcie B-02C/D; obie instrukcje odsyłają do pełnych zależności.

Walidacja dokumentacyjna: **OFFLINE PASS** — porównanie sekcja po sekcji, zgodność P1–P6 z architekturą i synchronizacją, `py -3 scripts/audit_quality.py` (29 plików Markdown, 108 lokalnych odnośników, 19 metod, 3 przykłady i 8 negatywnych przypadków kontraktowych, bez błędów), `git diff --check` oraz kontrola struktury Markdown, UTF-8 i zachowania CRLF. Potwierdzono dokładnie trzy zatwierdzone ścieżki, bez zmian kodu, schematów i innych dokumentów; raport roboczy QA w ignorowanym `.tia-bridge/`. Linków zewnętrznych nie odpytywano; nie dodano nowych adresów zewnętrznych.

Gotowość: dokumentacyjne przygotowanie do osobno zleconego B-01A; testy silnika/fake, X-01, Engineering i Runtime pozostają NOT_RUN. Ta aktualizacja nie otwiera sesji TIA ani nie rozpoczyna implementacji.

## 2026-10-25 — instrukcja współpracy ChatGPT z CODEX

- Utworzono `Instrukcja_ChatGPT.md` zgodną z `AGENTS.md`, architekturą, planem implementacji i kontraktem synchronizacji.
- Opisano podział odpowiedzialności, źródła prawdy, workflow, standard zadań CODEX, bezpieczeństwo TIA/PLC/HMI, kryteria akceptacji oraz raportowanie.
- Zaktualizowano indeks dokumentacji w README. Zmiana wyłącznie dokumentacyjna; nie testowano TIA Engineering ani Runtime.


## 2026-10-24–2026-10-25 — analiza i projekt następcy Bridge

Zakres: ANALIZA + ARCHITEKTURA + PLAN IMPLEMENTACJI + DOKUMENTACJA + COMMIT + PUSH. Nie implementowano aplikacji ani nie zmieniano kodu starego Bridge. Polecenie uzupełniono o analizę Snap7 do testowania PLC.

- Przeczytano materiały wejściowe nowego repo i rzeczywistą implementację Python→PowerShell→Openness, sesje, backup, preservation, komparatory i OPC engine w `C:\RMA\t05_plc`. Utrwalono SHA-25632 wybranych źródeł/dowodów i oddzielono dirty pliki od HEAD.
- Zidentyfikowano 24 problemy/ograniczenia z priorytetami, lokalizacjami, mechanizmami, objawami, wpływem, rozwiązaniem i dowodami. Rozdzielono historyczne awarie/naprawy CRLF/identity od bieżącego synchronicznego wykonania, kosztów snapshotów i ostatniego readback mismatch.
- Potwierdzono read-only lokalne API V19 i dokładny projekt/urządzenia; projekt był dirty. Zinwentaryzowano PLCSIM/AdvancedV 6/WinCC oraz lokalne kontrakty producenta; nie wykonywano nowego importu, Archive, Compile, Save, load/RUN/STOP ani zmiany maszyny.
- Zaprojektowano host net10, oddzielny worker net48/x64, durable queue/outbox, epoch/ownership/quarantine, watchdog i bounded retry. Rozdzielono queued cancel, running cancel, termination i reconciliation. Długie wywołanie Siemens nie blokuje statusu hosta ani edycji CODEX.
- Zdefiniowano W/S/P/D/B/C/E/T, trzystronny plan, immutable snapshoty SHA-256, dependency groups, konflikt/delete/rename policy i formalne SYNCED dopiero po niezależnej weryfikacji.
- Zaprojektowano 19 operacji HTTP/CLI z opcjonalnym MCP, params/results/errors/deadlines/permissions; dodano projektowe schematy JSON i syntetyczne przykłady, bez serwera aplikacji.
- Rozdzielono compile PLC/HMI, simulator install/start/load/RUN/ready, Runtime Manager i browser tests. Ograniczenia licencji/publicznych API i TLS są jawne; wsparcie future adaptera nie wynika z instalacji.
- Odczyt OPC UA status w istniejącym venv: PASS dla WinCC Unified, writes 0 i zamknięta sesja; podstawowy Python miał brak zależności. Nie potwierdzono CPU endpointu ani sekwencji PLC. HTTPSlocalhost miał błąd TLS; HMI E2E NOT_RUN.
- Przeanalizowano natywny Snap7 i python-snap7, classic S7/PUT-GET, optimized DB/offsety, PLCSIM Advanced TCP, allowlist, codec/polling i unknown write. Adapter pozostaje OPTIONAL po osobnej kwalifikacji; niczego nie instalowano i nie łączono z pamięcią PLC przez S7.
- Przygotowano test strategy (unit/integration/E2E/8h resilience),16 obowiązkowych awarii, mierzalne NF cele, backlog etapów 0–11 z małymi commitami, zależnościami, ryzykami i bramkami; ryzyka R-01–20, decyzje D-01–10 i eksperymenty X-01–08.
- Zachowano i powiązano initial concept docs, zaktualizowano README/AGENTS i ADR 0002. Dodano skrypt kontroli spójności dokumentacji i kontraktów.

Wyniki wykonane: **94 ukierunkowane testy offline starego Bridge PASS**. Kontrola dokumentacji/kontraktów: **PASS**, 28 plików Markdown, 89 lokalnych odnośników, 19 metod, 3 przykłady i 8 negatywnych przypadków. `git diff --check`: PASS; wszystkie 32 źródła bez driftu względem indeksu w chwili sprawdzenia. Przejrzano staged diff 33 jawnie wskazanych plików; brak wykrytych sekretów, projektów i raw logów; `git diff --cached --check`: PASS. Normalny push jest weryfikowany po commicie. Szczegóły i ograniczenia: [dowody](docs/10_EVIDENCE_AND_QUALITY.md). Ten zapis nie stwierdza nowej integracji Engineering ani Runtime PASS.

Docelowy pierwszy etap implementacji: kontrakty + durable async engine na fake workerze, następnie read-only worker V19 i mały kwalifikowany FC SCL round-trip. Obecny commit ma zamknąć wyłącznie fazę projektową; nie rozpoczyna tego wdrożenia.
