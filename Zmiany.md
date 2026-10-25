# Zmiany — TIA_Bridge 2

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
