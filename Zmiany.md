# Zmiany — TIA_Bridge 2

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
