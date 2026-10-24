# Decision 0002: trwały host asynchroniczny i wykonawca V19

- Status: przyjęta decyzja projektowa; implementacja NOT_RUN.
- Data: 2026-10-24/25.
- Zastępuje tylko niedoprecyzowane elementy initial scaffold; zachowuje [Decision 0001](0001-file-workspace-and-adapter-boundary.md).

## Kontekst

Materiały wejściowe definiują workspace i adapter boundary. Audyt rzeczywistego starego Bridge wykazał blokujące wywołania subprocess, brak trwałego asynchronicznego interfejsu klienta i niepewne wyniki mutacji pomimo istniejącego journalingu, backupów i readbacku. Użytkownik wymaga autonomii CODEX w zatwierdzonym zakresie oraz dalszej pracy podczas vendor call.

## Decyzja

1. Host net10 i worker Openness V19 net48/x64 są oddzielnymi procesami; Siemens DLL/proxies nie przechodzą do hosta.
2. Host utrwala operację i zwraca operation_id. HTTP loopback i CLI są podstawowe; MCP opcjonalny.
3. W i S są oddzielne. Export trafia do staging, import czyta frozen S, a B promuje się dopiero po niezależnej weryfikacji.
4. Timeout/śmierć workera nie dowodzi zatrzymania Siemens. Niepewna mutacja pozostawia trwałą quarantine i reconciliation, bez automatic replay/rollback/Save.
5. Projekt write wymaga zatwierdzonego konkretnego planu **lub** istniejącego, ograniczonego grantu sesji testowej z jasną polityką i świeżą weryfikacją odbiorcy. Raz zatwierdzone standardowe etapy sesji nie wymagają ponownej ręcznej zgody. Delete, fizyczny download i rozszerzenie zakresu nie wynikają z tego automatycznie.
6. Nie ma import-on-save, import-on-Git-change ani bezwarunkowego nadpisania zmian TIA. Błędy i nieznany coverage pozostają jawne.
7. SQLite i chronione materiały runtime są na lokalnym NTFS poza exFAT repo. Unknown capabilities są disabled.

## Konsekwencje

Punkt 5 doprecyzowuje sformułowania initial README/architecture/safety/roadmap/AGENTS o ręcznym potwierdzaniu importu: wymagają autoryzacji, ale nie koniecznie osobnego kliknięcia dla każdego kroku zatwierdzonego workflow. Same dokumenty i obecny task analityczny **nie nadają nowej aplikacji takich grantów**. Physical PLC download pozostaje default deny. Szczegóły normatywne: [architektura](../03_ARCHITECTURE.md), [synchronizacja](../04_SYNCHRONIZATION.md) i [interfejs](../05_CODEX_INTERFACE.md).

Dodatkowy proces i durable ledger zwiększają koszt implementacji, lecz oddzielają awarię API od dostępności narzędzia dla CODEX. Nie eliminują zawieszeń TIA; ograniczają ich zasięg i zachowują stan do recovery. Wykonalność exact worker lifecycle, native HMI i symulatorów wymaga X-01–X-08.
