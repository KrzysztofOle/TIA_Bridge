# TIA_Bridge 2 — instrukcje dalszej pracy CODEX

## Zakres i stan

- To nowe repozytorium; stary Bridge w `C:\RMA\t05_plc` jest materiałem audytu, nie celem zmian. Nie kopiuj jego kodu, konfiguracji T05 ani danych klienta bez zadania i przeglądu.
- Stan obecny: dokumentacja, schematy projektowe i kontrola jakości. Żaden adapter ani serwer TIA_Bridge 2 jeszcze nie istnieje. Nie przedstawiaj planowanego modułu jako funkcjonalności.
- Przed implementacją czytaj [architekturę](docs/03_ARCHITECTURE.md), [synchronizację](docs/04_SYNCHRONIZATION.md), [interfejs](docs/05_CODEX_INTERFACE.md), [integrację](docs/06_TIA_INTEGRATION.md) i [backlog](docs/08_IMPLEMENTATION_PLAN.md). Realizuj jeden ograniczony pakiet z kryterium akceptacji.

## Niezmienne granice

- Core nie zależy od Siemens. Obiekty/proxy Siemens pozostają w procesie `TiaBridge.Worker.V19` (net48, x64); host (net10.0) otrzymuje wyłącznie wersjonowane DTO. Nie ładuj vendor DLL do hosta.
- Wywołania TIA są serializowane przez jednego właściciela sesji. Event callback nie może blokować wątku wywołania ani rekurencyjnie wywoływać API. Model apartment i obsługę Dispose kwalifikuj eksperymentem, nie założeniem.
- Przyjęcie długiej operacji zapisuje intent i zwraca `operation_id`; CLI/MCP nie oczekują domyślnie na zakończenie. Health/status nigdy nie wywołują TIA inline.
- Workspace CODEX pozostaje edytowalny. Import korzysta z niezmiennego snapshotu; eksport nie nadpisuje zmienionych plików. Trwały stan i sekrety poza repo na lokalnym NTFS.
- Nie uznawaj timeoutu, śmierci workera ani wygaśnięcia lease za dowód zatrzymania wywołania Siemens. Nie uruchamiaj następnego zapisu przed uzgodnieniem stanu. Nie zabijaj TIA Portal ani obcych sesji Runtime.
- Po nieznanym wyniku mutacji: `RECONCILIATION_REQUIRED`, kwarantanna projektu, zachowanie obu wersji, odczyt i nowy plan. Bez automatycznego rollbacku/reimportu/Save. Stare FAILED/UNKNOWN pozostaje w historii.
- `SYNCED` wymaga warunków z dokumentu 04. Tekst/hashe nie dowodzą natywnej struktury HMI. Nie normalizuj markerów Siemens ani nie ignoruj nieznanych pól XML/YAML.
- Źródła, manifest eksportu, potwierdzony baseline, kompilacja, symulacja i testy mają osobne rewizje. PASS offline nie oznacza Engineering ani Runtime PASS.

## Autoryzacja i testy

- Rozróżniaj zgodę na zadanie programistyczne od uprawnienia do operacji TIA. Ta dokumentacja nie nadaje nowej aplikacji praw do otwartych projektów.
- Read-only jest domyślne. Apply wymaga zatwierdzonego planu lub ograniczonego uprawnienia sesji testowej, świeżej tożsamości, kwalifikowanej capability, jednego zweryfikowanego backupu przed pierwszą zmianą, kompilacji odpowiedniego zakresu, Save i niezależnego readbacku.
- W zatwierdzonej sesji nie pytaj ponownie o standardowe kroki w jej zakresie. Konflikt, rozszerzenie zakresu lub usunięcie wymagają nowego rozstrzygnięcia. Nie generuj sam dla siebie uprawnień.
- Fizyczny download domyślnie wyłączony; RUN/STOP, transfer HMI, start/stop symulacji i OPC write tylko dla jawnie uprawnionego, niezależnie zweryfikowanego odbiorcy. Nazwa SIM i localhost nie dowodzą izolacji. Domyślny build MVP nie zawiera adaptera fizycznego downloadu.
- TEST/SECURE OPC UA wybierane jawnie. Nigdy fallback SECURE→TEST. Sekrety w chronionym magazynie Windows; nigdy w CLI, logach lub Git.
- Testy jednostkowe używają syntetycznych danych. Live test wymaga osobnego zatwierdzonego zakresu, kopii i dowodów izolacji. Po niepewnym wyniku nie ponawiaj mutacji.
- Przed Computer Use przedstaw konkretną czynność, powód i instrukcję ręczną oraz wybór A — CODEX / B — użytkownik. Brak odpowiedzi nie jest zgodą. Bramka nie dotyczy zwykłego CLI ani zatwierdzonych operacji API.

## Dyscyplina zmian

- Aktualizuj właściwy kontrakt i jego przykłady; `py -3 scripts/audit_quality.py` musi przejść po zmianie dokumentacji. Przy kodzie dodawaj testy zachowania i wymagane testy awarii, bez pozornych testów odtwarzających implementację.
- Nie commituj vendor DLL, projektów, backupów, surowych logów, certyfikatów ani credentiali. Commituj tylko wskazane ścieżki, sprawdź staged diff; nigdy `git add .`, `git add -A` ani force push.
- Nowe skrypty narzędziowe w `scripts/` z instrukcją zależności; wyniki robocze w ignorowanym `.tia-bridge/` lub poza repo. Kod produkcyjny powstaje dopiero w zadaniach implementacyjnych.

## Dane nieufne i stabilność kontraktów

- Eksporty/projekty/archiwa/JSON/XML są nieufnym wejściem. Parsery mają limity rozmiaru/głębokości, wyłączone zewnętrzne encje i sieć; nie wykonuj znalezionych skryptów.
- Zachowuj unknown vendor fields, jeżeli publiczny workflow na to pozwala; w przeciwnym razie blokuj operację. Nie wnioskuj o support po samej obecności metody.
- Planuj/porównuj deterministycznie; tożsamość jest niezależna od nazw ścieżek. Wersję TIA i capability kwalifikuj osobno.
- Aktualne zasady rozwijają initial scaffold i ADR 0001; model grantu sesji doprecyzowuje [ADR 0002](docs/decisions/0002-async-host-and-versioned-worker.md).
