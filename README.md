# TIA_Bridge 2

Projekt nowej aplikacji Windows do integracji CODEX z Siemens TIA Portal. Stan opracowania 2026-10-24–25: **analiza i projekt, bez implementacji aplikacji**. Istniejące katalogi `src/` są miejscami na przyszły kod. Schematy w `contracts/` i przykłady opisują projekt kontraktu, nie działający serwer.

Docelowy przepływ: **TIA → eksport do workspace → edycja CODEX → plan → zatwierdzone Apply → niezależna weryfikacja → kompilacja/symulacja → testy**. Pliki CODEX określają zamierzony stan. Rozbieżności z nieznanymi zmianami TIA wymagają rozstrzygnięcia; import nigdy nie stanowi samodzielnego dowodu zgodności.

Rekomendacja: host C#/.NET 10, trwałe operacje i SQLite na lokalnym NTFS, wykonawca Openness V19 w oddzielnym procesie .NET Framework 4.8 x64. CODEX otrzymuje `operation_id` po utrwaleniu zlecenia i kontynuuje niezależną pracę. CLI i lokalne HTTP API korzystają z jednego silnika; MCP jest opcjonalną fasadą. Zawieszenie Siemens nie blokuje API, ale może uniemożliwić dalsze operacje na danym projekcie do czasu uzgodnienia stanu.

## Wyniki analizy

- Stary Bridge: `C:\RMA\t05_plc\tools\T05_TIA_Bridge.py` i `tools/t05_tia_bridge/`. Zbadano kod bieżących plików, historię Git, testy, logi oraz lokalną dokumentację Siemens. [Audyt](docs/01_AUDIT_OLD_BRIDGE.md) rozdziela usterki historyczne, naprawione i nadal obecne.
- Potwierdzono lokalne API V19, read-only attach i inventory oraz obecność PLCSIM V19, PLCSIM Advanced V6.0 i Unified PC V19. Nie wykonywano importu, Compile, Save, Archive, downloadu ani sterowania symulatorem w tym zadaniu.
- OPC UA `localhost:4890`: read-only status PASS dla **WinCC Unified**. To nie jest dowód dostępu do CPU ani testu logiki PLC. HTTPS `localhost`: błąd TLS; HMI E2E NOT_RUN.
- Wykonano 94 ukierunkowane testy offline starego Bridge; wszystkie przeszły. Weryfikują istniejące zabezpieczenia i komparatory, nie nową aplikację ani Runtime. Szczegóły: [dowody i jakość](docs/10_EVIDENCE_AND_QUALITY.md).

## Dokumentacja projektu

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
| [Instrukcja ChatGPT](Instrukcja_ChatGPT.md) | podział ról ChatGPT/CODEX, workflow zadań i reguły oceny wyników zgodne z AGENTS.md |
| [Zmiany](Zmiany.md) | zapis prac projektowych |

Pierwsze zadanie implementacyjne: **etap 1 + pierwszy przekrój etapu 2**, kontrakty i asynchroniczny silnik na fałszywym wykonawcy. Następnie read-only V19 i jeden kwalifikowany round-trip SCL FC na czystej kopii syntetycznego projektu. [Plan](docs/08_IMPLEMENTATION_PLAN.md) wskazuje kolejność i dowody wymagane przed Apply.

## Granice i praca z repozytorium

Nie ma automatycznego importu po zapisie pliku. Domyślny profil jest read-only; zatwierdzone, ograniczone uprawnienie sesji testowej może obejmować wiele kroków. Usuwanie, fizyczny download i rzeczywista maszyna wymagają osobnej autoryzacji. Kopia Git nie zastępuje archiwum Siemens.

Repozytorium znajduje się obecnie na M: (exFAT). Trwała baza operacji, sekrety i spool wykonawcy mają docelowo znajdować się na lokalnym NTFS poza repo; na exFAT nie opieramy gwarancji ACL ani odporności na utratę zasilania. Nazwy `SYNCED`, `Compile PASS` i `Runtime PASS` opisują niezależne fakty.

Kontrola dokumentacji: `py -3 scripts/audit_quality.py`. Wymaga Python 3.12 i `jsonschema`; nie łączy się z Siemens. [Instrukcja](scripts/README.md), [zasady agentów](AGENTS.md).

## Materiały wejściowe

Pierwotne opracowania [architecture](docs/architecture.md), [sync-model](docs/sync-model.md), [formats](docs/formats.md), [safety](docs/safety.md), [roadmap](docs/roadmap.md) i ADR 0001 zachowano i powiązano z aktualnymi specyfikacjami. [ADR 0002](docs/decisions/0002-async-host-and-versioned-worker.md) doprecyzowuje autonomię w zatwierdzonej sesji.
