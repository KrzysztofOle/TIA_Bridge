# TIA_Bridge 2 — instrukcje wykonawcze CODEX

## 1. Cel i zakres
- Most synchronizuje wybrane obiekty TIA Portal z edytowalnym Workspace. Git nie jest pełną serializacją ani backupem projektu. Stary Bridge służy wyłącznie jako materiał audytowy; nie kopiuj jego danych ani kodu bez zadania.
- Realizuj zatwierdzony zakres i mierzalne kryteria akceptacji. Oddziel projektowane, wdrożone, OFFLINE PASS, ENGINEERING PASS i RUNTIME PASS; nie przedstawiaj Fake jako Siemens.

## 2. Hierarchia i decyzje
- `Instrukcja_ChatGPT.md` kieruje analizą i zlecaniem, `AGENTS.md` — wykonaniem. Kolejność: aktualna decyzja użytkownika z zachowaniem bezpieczeństwa → AGENTS → przyjęte ADR/kontrakty → specyfikacje → backlog. Instrukcja nie uchyla architektury ani semantyki kontraktu.
- Konflikt normatywny? Wstrzymaj zależne prace; przedstaw źródła, ryzyka, warianty i rekomendację użytkownikowi. Zachowuj ADR 0001/0002 i zatwierdzone decyzje D-xx. Nowy ADR tylko dla nowej decyzji.

## 3. Architektura
- Core/Host bez Siemens DLL i proxy. Host `net10.0` i worker V19 `net48/x64` to odrębne procesy z wersjonowanym IPC/DTO. Nowsze TIA wymaga odrębnego workera/kwalifikacji; nie zmieniaj runtime'ów podczas porządkowania.
- Sprawdzaj instalację producenta, exact DLL, wersję, uprawnienia, licencję i capability; nie zakładaj ścieżek lub kont. Host izoluje błędy Siemens od klientów. HTTP loopback i CLI używają wspólnego silnika, MCP opcjonalny.
- Przed potwierdzeniem przyjęcia utrwal intent, `operation_id`, kanoniczny request, idempotencję i historię. Zachowaj zlecenia po restarcie. Ten sam key/body → to samo ID, inny body → konflikt, nie nowy efekt. Klient nie oczekuje domyślnie na Siemens call.
- Serializuj Siemens calls przez jednego ownera sesji; callback bez blokowania/rekurencji. Apartment, Dispose, eventy i ownership wymagają dowodów. Host health/status bez inline Siemens, także podczas hang workera.

## 4. Workspace i synchronizacja
- Oddziel edytowalne W, frozen import snapshot S, potwierdzony baseline B i projekt P. Export do staging nie nadpisuje edycji W. Zakaz import-on-save, import-on-Git-change i force overwrite.
- Zachowuj natywne artefakty, markery Siemens, unknown vendor fields, dependencies i coverage. Nieznane istotne pola HMI lub brak native readback blokują właściwe Apply/`SYNCED`; tekst/hash/compile nie dowodzą natywnej struktury.
- Plan Apply wiąże B/P/S, expected W, scope, dependencies, schema/normalizer i capability. Fresh project identity i P/preconditions wymagane także pod ExclusiveAccess tuż przed zapisem; drift blokuje. Początkowe `IsModified=True` → `REVIEW_REQUIRED`, bez auto Save.
- Apply czyta S; wymaga importu, akceptowanej diagnostyki compile, kontroli chronionych dependencies, Save (return i `IsModified=False`) oraz niezależnego readbacku. Po weryfikacji całej grupy zapisuj receipt i promuj B przez CAS. W zmienione od S pozostaje `MODIFIED`. Pełna saga/`SYNCED`: dokument 04.
- Identity obiektów niezależna od ścieżek, plan deterministyczny. Projekty, archiwa, JSON/XML/YAML to nieufne wejście: limity wielkości/głębokości, brak XXE/sieci/skryptów, walidacja containment/traversal/reparse w chwili IO.

## 5. Autoryzacja i ochrona PLC/HMI
- `deny-by-default`: unknown lub niekwalifikowana capability = disabled; osobne dowody dla operacji, object kind, wersji i coverage. Reflection/metoda API nie daje kwalifikacji.
- Zgoda na prace CODEX ≠ uprawnienie TIA. Project write wymaga zatwierdzonego planu **albo** istniejącego ograniczonego grantu sesji testowej, świeżej tożsamości odbiorcy, aktualnych preconditions i capability. Standardowe kroki zatwierdzonej sesji bez kolejnej zgody; delete, destrukcja i rozszerzenie zakresu wymagają nowej decyzji.
- Przed pierwszą mutacją sesji: jeden prawidłowy backup z kontrolą integralności i bindingiem projektu/sesji. Brak backupu blokuje zapis.
- Fizyczny PLC/HMI download domyślnie zabroniony; MVP bez domyślnego adaptera download. RUN/STOP, HMI transfer, symulacja i OPC write tylko z osobnym grantem i dowodem tożsamości/izolacji/trasowania. SIM/localhost nie dowodzą izolacji. Nie zatrzymuj cudzych TIA/Runtime.
- OPC `TEST`/`SECURE` jawne; zero fallback SECURE→TEST. Sekrety poza Git w chronionym magazynie systemu, nigdy w CLI/logach/trace.
- Przed Computer Use opisz czynność, powód i metodę ręczną oraz wybór A — CODEX / B — użytkownik. Cisza ≠ zgoda. Nie dotyczy zwykłego CLI i uprawnionego API.

## 6. Trwałość i recovery
- SQLite/WAL/journal/outbox/artifacts i dane wymagające atomic safe replace muszą być poza repo na sprawdzonym lokalnym NTFS w dozwolonym katalogu. Nie przenoś kwalifikacji na exFAT/UNC; exFAT FAIL pozostaje. Zakaz delete+copy, obejść ACL i relokacji stanu bez kwalifikacji. Chroń migracje, integralność i historię.
- Timeout/cancel/worker death/lease expiry nie dowodzą zatrzymania Siemens call. Kolejny write dopiero po quiescence/reconciliation. Restart i cleanup dotyczą tylko własnych procesów/uchwytów.
- Niepewny efekt → trwałe `UNKNOWN`/`RECONCILIATION_REQUIRED`, quarantine, obie wersje, odczyt stanu i nowy plan. Bez automatycznego replay, rollback, reimportu lub Save. FAILED/UNKNOWN nie wymazuj. Idempotencja ≠ exactly-once Siemens.
- Late reply/stary epoch ani restart nie promują wyniku. Uszkodzony WAL/artefakt lub utrata storage blokują potwierdzenie, nie uruchamiają automatycznego resetu.

## 7. Testy i kwalifikacja
- Oddziel OFFLINE/Fake, ENGINEERING/Openness i RUNTIME/PLC-HMI-OPC. Fake PASS nie dowodzi Siemens. Live test wymaga osobnej autoryzacji, kopii i dowodów izolacji.
- D-11: przed osobno zleconym X-01 read-only wymagane RT-03/05/06/15/16 PASS na Fake, dowody restart/hang/timeout/cancel/late/duplicate/loss/UNKNOWN/quarantine, NF-01/02/03/04/05/06/12/14/15 we właściwym profilu NTFS, realne idle ≥5 min, ≥1000 operacji i brak krytycznych błędów izolacji/wykonania. NF-11 ≥8 h pozostaje obowiązkowy później w B-10B/R-14; NOT_RUN nie jest PASS. exFAT FAIL zostaje. Bramka nie uruchamia X-01, nie upoważnia do write ani produkcji.
- Regresja: kontrakty, trwałość intent/outbox, restart, idempotencja, races, timeouty, late epoch, quarantine, WAL/artifacts, synchronizacja i chronione obiekty. Zachowuj historyczne dowody/testy, bez przepisywania wyników.
- Raportuj PASS/FAIL/BLOCKED/NOT_RUN osobno dla testów i poziomów kwalifikacji, z dowodami, zakresem i ograniczeniami. Brak dowodu → BLOCKED, wynik negatywny → FAIL.

## 8. Workflow CODEX
- Zweryfikuj aktualny HEAD, dokumenty, ADR, granice i kryteria. Zmieniaj tylko zatwierdzone pliki. X-01 i capabilities nie rozszerzają się automatycznie. Zmiana kodu wymaga testów zachowania i awarii.
- Przy zmianie architektury/workflow/sync/uprawnień/testów/bezpieczeństwa sprawdź obie instrukcje, ADR i kontrakty. W `Zmiany.md` uzasadnij aktualizacje lub ich brak.
- Sprawdź linki, diff i sekrety; uruchom `scripts/audit_quality.py` we właściwym środowisku lub oznacz NOT_RUN. Bez `git add .`, `git add -A`, force push. Nie commituj vendor DLL, projektów, backupów, credentiali ani surowych logów. Narzędzia w `scripts/`, robocze pliki poza Git.
- Raportuj zmienione pliki, testy, dowody, ryzyka, ograniczenia, commit, branch/push i nierozstrzygnięte kwestie.

## Standard commitów Git
- Conventional Commits: `type(scope): imperative description` lub `type: imperative description`; komunikat po angielsku, type/scope lowercase, temat najlepiej ≤72 znaki bez kropki. Jedna logiczna zmiana na commit; typy: `feat/fix/refactor/test/docs/chore/perf/build/ci`.
- Breaking change wymaga `!` i stopki `BREAKING CHANGE:`. Nie przepisuj historii; dłuższe raporty do dokumentacji.

## 9. Dokumentacja referencyjna
- `docs/03_ARCHITECTURE.md`, `docs/04_SYNCHRONIZATION.md`, `docs/06_TIA_INTEGRATION.md`, `docs/07_TEST_STRATEGY.md`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/09_RISKS_AND_DECISIONS.md`, `docs/12_WORKSPACE_CAPABILITIES.md`, `docs/13_DURABLE_STORAGE.md`, `docs/16_RESILIENCE_RECOVERY.md`, `docs/decisions/`, `contracts/`, `Zmiany.md`.
