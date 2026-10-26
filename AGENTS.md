# TIA_Bridge 2 — instrukcje dalszej pracy CODEX

## Zakres i stan

- To nowe repozytorium; stary Bridge w `C:\RMA\t05_plc` jest materiałem audytu, nie celem zmian. Nie kopiuj jego kodu, konfiguracji T05 ani danych klienta bez zadania i przeglądu.
- Stan obecny: dokumentacja, schematy projektowe i kontrola jakości. Żaden adapter ani serwer TIA_Bridge 2 jeszcze nie istnieje. Nie przedstawiaj planowanego modułu jako funkcjonalności.
- Przed implementacją czytaj [architekturę](docs/03_ARCHITECTURE.md), [synchronizację](docs/04_SYNCHRONIZATION.md), [interfejs](docs/05_CODEX_INTERFACE.md), [integrację](docs/06_TIA_INTEGRATION.md) i [backlog](docs/08_IMPLEMENTATION_PLAN.md). Realizuj jeden ograniczony pakiet z kryterium akceptacji.

## Role instrukcji i hierarchia dokumentów

- [Instrukcja_ChatGPT.md](Instrukcja_ChatGPT.md) określa zasady analizy, planowania, przygotowywania zadań i oceniania wyników. `AGENTS.md` określa zasady wykonawcze obowiązujące CODEX. Dokumenty są komplementarne, nie konkurencyjne.
- Kolejność stosowania w repo: bieżące jawne decyzje użytkownika z zachowaniem bezpieczeństwa → `AGENTS.md` dla wykonania → zatwierdzone ADR i kontrakty → szczegółowe specyfikacje → backlog i notatki. Instrukcje nie zmieniają samym pierwszeństwem zatwierdzonej architektury ani semantyki kontraktu; README jest indeksem i opisem stanu.
- Jeśli sprzeczność dotyczy architektury, bezpieczeństwa lub semantyki kontraktu, nie rozstrzygaj jej samodzielnie ani przez wybór dokumentu wyżej w hierarchii. Wstrzymaj zależną część pracy; przygotuj raport rozbieżności z dokładnymi odwołaniami, skutkami, wariantami i rekomendacją do decyzji użytkownika. Kontynuuj tylko niezależne prace w zatwierdzonym zakresie.

## Niezmienne granice

- Core nie zależy od Siemens. Obiekty/proxy Siemens pozostają w procesie `TiaBridge.Worker.V19` (net48, x64); host (net10.0) otrzymuje wyłącznie wersjonowane DTO. Nie ładuj vendor DLL do hosta.
- Wywołania TIA są serializowane przez jednego właściciela sesji. Event callback nie może blokować wątku wywołania ani rekurencyjnie wywoływać API. Model apartment i obsługę Dispose kwalifikuj eksperymentem, nie założeniem.
- Obowiązkowe są nieblokujące przyjmowanie operacji, utrwalenie intentu i `operation_id` przed potwierdzeniem przyjęcia oraz zachowanie zaakceptowanych zleceń i ich historii po restarcie hosta. API/CLI/MCP nie oczekują domyślnie na zakończenie wywołania Siemens; health/status pozostają dostępne podczas zawieszenia workera i nigdy nie wywołują TIA inline.
- Idempotencja zwraca tę samą operację dla tego samego klucza i kanonicznego żądania; nie jest zgodą na automatyczne ponowienie mutacji o nieznanym wyniku. Mierzalne progi i szczegółowe scenariusze pozostają w [wymaganiach](docs/02_REQUIREMENTS.md), [strategii testów](docs/07_TEST_STRATEGY.md) i [backlogu](docs/08_IMPLEMENTATION_PLAN.md).
- Workspace CODEX pozostaje edytowalny. Import korzysta z niezmiennego snapshotu; eksport nie nadpisuje zmienionych plików. Trwały stan i sekrety poza repo na lokalnym NTFS.
- Nie uznawaj timeoutu, śmierci workera ani wygaśnięcia lease za dowód zatrzymania wywołania Siemens. Nie uruchamiaj następnego zapisu przed uzgodnieniem stanu. Nie zabijaj TIA Portal ani obcych sesji Runtime.
- Po nieznanym wyniku mutacji: `RECONCILIATION_REQUIRED`, kwarantanna projektu, zachowanie obu wersji, odczyt i nowy plan. Bez automatycznego rollbacku/reimportu/Save. Stare FAILED/UNKNOWN pozostaje w historii.
- `SYNCED` wymaga warunków z dokumentu 04. Tekst/hashe nie dowodzą natywnej struktury HMI. Nie normalizuj markerów Siemens ani nie ignoruj nieznanych pól XML/YAML.
- Źródła, manifest eksportu, potwierdzony baseline, kompilacja, symulacja i testy mają osobne rewizje. PASS offline nie oznacza Engineering ani Runtime PASS.

## Autoryzacja i testy

- Rozróżniaj zgodę na zadanie programistyczne od uprawnienia do operacji TIA. Ta dokumentacja nie nadaje nowej aplikacji praw do otwartych projektów.
- Read-only jest domyślne. Przed rozpoczęciem mutacji Apply wymagane są: plan zatwierdzony indywidualnie albo objęty polityką istniejącego, ograniczonego grantu sesji testowej oraz właściwe uprawnienie; świeżo potwierdzona tożsamość projektu; kwalifikowana capability; aktualne warunki synchronizacji związane z planem (B/P/S, expected W, zakres, zależności i wersje). Pod ExclusiveAccess ponownie sprawdź P i preconditions; drift blokuje mutację. `IsModified=True` przed pierwszą sesją oznacza `REVIEW_REQUIRED`, bez automatycznego Save. Przed pierwszą mutacją sesji wymagany jest jeden poprawny backup ze zweryfikowaną integralnością, właściwym AP19 i bindingiem do sesji; błąd backupu blokuje zapis.
- Zakończenie Apply i potwierdzenie synchronizacji wymagają prawidłowego importu niezmiennego S, kompilacji odpowiedniego zakresu z akceptacją diagnostyk, weryfikacji zakresu i chronionych zależności, poprawnego Save (return i `IsModified=False`) oraz niezależnego readbacku. Dopiero po pełnej weryfikacji całej grupy spójności utrwal verification receipt i nowy baseline przez CAS względem oczekiwanej rewizji. Obowiązuje dokładna saga i definicja `SYNCED` z [dokumentu 04](docs/04_SYNCHRONIZATION.md); jeśli bieżące W różni się od S, sukces operacji dla S pozostawia bieżący obiekt `MODIFIED`, bez cofania edycji W.
- W zatwierdzonej sesji nie pytaj ponownie o standardowe kroki w jej zakresie. Konflikt, rozszerzenie zakresu lub usunięcie wymagają nowego rozstrzygnięcia. Nie generuj sam dla siebie uprawnień.
- Fizyczny download domyślnie wyłączony; RUN/STOP, transfer HMI, start/stop symulacji i OPC write tylko dla jawnie uprawnionego, niezależnie zweryfikowanego odbiorcy. Nazwa SIM i localhost nie dowodzą izolacji. Domyślny build MVP nie zawiera adaptera fizycznego downloadu.
- TEST/SECURE OPC UA wybierane jawnie. Nigdy fallback SECURE→TEST. Sekrety w chronionym magazynie Windows; nigdy w CLI, logach lub Git.
- Testy jednostkowe używają syntetycznych danych. Live test wymaga osobnego zatwierdzonego zakresu, kopii i dowodów izolacji. Po niepewnym wyniku nie ponawiaj mutacji.
- Integracja z rzeczywistym Siemens Openness, także read-only w X-01, nie może rozpocząć się przed pozytywnym zakończeniem wymaganych testów trwałego silnika asynchronicznego na fake workerze w B-02A–B-02D. Obowiązkowa bramka obejmuje restart hosta, zawieszenie workera, timeout, anulowanie oczekującego zlecenia, spóźnioną odpowiedź, ponowienie identycznego żądania, utratę procesu wykonawczego oraz nieznany wynik mutacji i kwarantannę. Dowody i kryteria określają [testy](docs/07_TEST_STRATEGY.md) i [pakiety B-02A–B-02D](docs/08_IMPLEMENTATION_PLAN.md); dopiero potem [X-01](docs/09_RISKS_AND_DECISIONS.md) kwalifikuje rzeczywisty worker przed B-03A. PASS fake nie dowodzi poprawności Siemens.
- Dla pierwszego, osobno zleconego i autoryzowanego X-01 read-only obowiązuje [ograniczona bramka D-11](docs/07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only): wymagane RT/NF na Fake w jawnym kwalifikowanym profilu lokalnego NTFS, rzeczywisty idle ≥5 min i ≥1000 operacji, bez krytycznych błędów wykonania/izolacji. NF-11 ≥8 h pozostaje obowiązkowy w B-10B/R-14; NOT_RUN nie blokuje wyłącznie tej ograniczonej bramki i nie staje się PASS. NF-03 exFAT FAIL pozostaje ograniczeniem; NTFS PASS nie kwalifikuje exFAT. SQLite/journal/outbox/artifacts i kwalifikowany zapis/replace wymagają lokalnego NTFS, zweryfikowanego przed użyciem nowej lokalizacji. Nie oznacza to gotowości produkcyjnej ani nadania praw do mutacji projektu lub sterowania PLC/HMI. UNKNOWN/quarantine i izolacja procesów pozostają obowiązkowe.
- Przed Computer Use przedstaw konkretną czynność, powód i instrukcję ręczną oraz wybór A — CODEX / B — użytkownik. Brak odpowiedzi nie jest zgodą. Bramka nie dotyczy zwykłego CLI ani zatwierdzonych operacji API.

## Dyscyplina zmian

- Aktualizuj właściwy kontrakt i jego przykłady; `py -3 scripts/audit_quality.py` musi przejść po zmianie dokumentacji. Przy kodzie dodawaj testy zachowania i wymagane testy awarii, bez pozornych testów odtwarzających implementację.
- Nie commituj vendor DLL, projektów, backupów, surowych logów, certyfikatów ani credentiali. Commituj tylko wskazane ścieżki, sprawdź staged diff; nigdy `git add .`, `git add -A` ani force push.
- Nowe skrypty narzędziowe w `scripts/` z instrukcją zależności; wyniki robocze w ignorowanym `.tia-bridge/` lub poza repo. Kod produkcyjny powstaje dopiero w zadaniach implementacyjnych.
- Po zmianie architektury, workflow CODEX, synchronizacji, autoryzacji, testów lub kryteriów akceptacji albo bezpieczeństwa PLC/HMI obowiązkowo oceń wpływ na obie instrukcje i właściwe ADR/specyfikacje/kontrakty. Sprawdź spójność `AGENTS.md` i `Instrukcja_ChatGPT.md` sekcja po sekcji. Udokumentuj w `Zmiany.md` zakres oceny, decyzję o aktualizacji lub pozostawieniu dokumentów bez zmian oraz uzasadnienie; sam temat zmiany nie wymusza modyfikacji obu instrukcji. Nierozstrzygnięte konflikty obsłuż według powyższej hierarchii, a po zmianach wykonaj kontrolę jakości, odnośników i diffu.

## Standard commitów Git

- Stosuj [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/): domyślnie `type(scope): short description`, a bez jednoznacznego modułu `type: short description` (`scope` jest opcjonalny).
- Cały komunikat commita, w tym temat, ewentualny opis rozszerzony i stopki, pisz wyłącznie po angielsku. `type` i `scope` zapisuj małymi literami.
- Temat ma być krótki i jednoznaczny, najlepiej do 72 znaków łącznie z prefiksem. Używaj trybu rozkazującego, np. `add`, `fix`, `remove`, `update`; pierwsza linia bez kropki na końcu.
- Dobieraj typ do zmiany: `feat` — nowa funkcjonalność, `fix` — naprawa błędu, `refactor` — reorganizacja bez zmiany zachowania, `test` — testy, `docs` — dokumentacja, `chore` — utrzymanie, `perf` — wydajność, `build` — system budowania lub zależności, `ci` — automatyzacja CI.
- Jeden commit reprezentuje jedną logiczną zmianę. Unikaj ogólników `updates`, `changes`, `various fixes`.
- Opis rozszerzony dodawaj tylko wtedy, gdy wyjaśnia istotną decyzję techniczną, nieoczywiste zachowanie lub niekompatybilność; oddziel go pustą linią od tematu. Przy zmianie niekompatybilnej wymagane są zarówno `!` bezpośrednio przed dwukropkiem (np. `feat(api)!: remove legacy operation format`), jak i stopka `BREAKING CHANGE:` opisująca skutek oraz sposób migracji.
- Commit jest informacją techniczną dla programistów. Nie stosuj prefiksu `# TIA_Bridge`, wymogu rozbudowanego polskiego opisu, podkreślania pracy twórczej ani treści podatkowych, księgowych lub ewidencji czasu pracy. Szczegóły implementacji, testów, ryzyk i decyzji zapisuj w odpowiedniej dokumentacji, raportach CODEX i `Zmiany.md`; nie umieszczaj sprawozdania z pracy w temacie commita.
- Standard obowiązuje nowe commity; pozostaw istniejące commity i historię Git bez zmian.

Przykłady tematów:

```text
feat(core): add operation identity models
feat(api): define asynchronous operation contracts
fix(sync): prevent overwriting modified workspace files
fix(worker): handle operation timeout correctly
refactor(host): separate scheduling from execution
test(core): add idempotency validation tests
docs: clarify synchronization workflow
chore(build): update project dependencies
perf(sync): reduce redundant file hashing
```

## Dane nieufne i stabilność kontraktów

- Eksporty/projekty/archiwa/JSON/XML są nieufnym wejściem. Parsery mają limity rozmiaru/głębokości, wyłączone zewnętrzne encje i sieć; nie wykonuj znalezionych skryptów.
- Zachowuj unknown vendor fields, jeżeli publiczny workflow na to pozwala; w przeciwnym razie blokuj operację. Nie wnioskuj o support po samej obecności metody.
- Planuj/porównuj deterministycznie; tożsamość jest niezależna od nazw ścieżek. Wersję TIA i capability kwalifikuj osobno.
- Aktualne zasady rozwijają initial scaffold i ADR 0001; model grantu sesji doprecyzowuje [ADR 0002](docs/decisions/0002-async-host-and-versioned-worker.md).
