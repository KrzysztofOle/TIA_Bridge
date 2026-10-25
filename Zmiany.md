# Zmiany — TIA_Bridge 2

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
