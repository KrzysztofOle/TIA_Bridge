# Audyt gałęzi — 2026-11-21

Zakres: zdalne gałęzie GitHub na tle `main` **przed dodaniem tego raportu**. `Ahead/behind`: commity unikalne na gałęzi / brakujące na niej względem `main`. Opis bazuje na porównaniu plików i typów zmian; ocena funkcjonalna wymaga kwalifikacji kodu i testów. Lokalne gałęzie i worktree nie są widoczne w GitHub API.

Wiele gałęzi eksperymentalnych rozgałęzia się przed obecnym `main`; efekty mogą być już zaimplementowane w innej postaci. Granice Host/Worker/Siemens i bramki Engineering pozostają nadrzędne.

| Gałąź | Ostatni commit UTC | Ahead/behind | Zawartość techniczna / uzasadnienie | Rekomendacja |
|---|---|---:|---|---|
| `docs/adr-hmi-canonical-model` | 2026-10-26 | 0/63 | Historyczne ADR dla kanonicznego modelu HMI; osiągalne z `main`. | **[USUNĄĆ]** |
| `docs/e2-b1-qualification-06cbfec6` | 2026-10-27 | 0/36 | Wyniki kwalifikacji E2-B1; historia osiągalna z `main`. | **[USUNĄĆ]** |
| `docs/refactor-agents-portable` | 2026-10-26 | 2/77 | Dwa unikalne commity dotyczą cofnięcia instrukcji; porównanie drzew nie zwraca zmian plików. | **[DO PRZEGLĄDU]** |
| `docs/refactor-chatgpt-instructions` | 2026-10-26 | 0/54 | Instrukcje ChatGPT; historia osiągalna z `main`. | **[USUNĄĆ]** |
| `feat/b10b-a1-nf11-synthetic` | 2026-10-28 | 1/37 | Syntetyczny harness NF-11: `scripts/performance_evidence.py`, resilience reporting, testy. | **[DO PRZEGLĄDU]** |
| `feat/hmi-js-01` | 2026-10-28 | 1/32 | Eksport/offline packager JS HMI: `scripts/hmi_js_sync.py` i testy. | **[DO PRZEGLĄDU]** |
| `feat/hmi-js-02` | 2026-10-28 | 2/32 | Eksport modułów WinCC V19, schema kontraktów, skrypty i testy HMI. | **[DO PRZEGLĄDU]** |
| `feat/qa-01-runner` | 2026-10-28 | 1/32 | Runner scenariuszy QA `qa`, `qa.ps1`, przykłady i testy. | **[DO PRZEGLĄDU]** |
| `feat/qa-02a-unified-diagnostics` | 2026-10-28 | 2/32 | Korelacja diagnostyki runnera QA, przykłady i skrypty. | **[DO PRZEGLĄDU]** |
| `feat/qa-03-nf11-vscode-launcher` | 2026-10-28 | 2/37 | Launcher VS Code `scripts/run-nf11.ps1`, testy NF-11 i resilience. | **[DO PRZEGLĄDU]** |
| `test/e2-b1-ipc-sender-lifetime` | 2026-10-28 | 2/32 | Diagnostyka czasu życia IPC sendera; scenariusze QA i testy. | **[DO PRZEGLĄDU]** |

## Bramy
Integracja wymaga przeglądu aktualnego `main`, testów offline i — tam gdzie obowiązuje — osobnej weryfikacji Siemens Engineering. Brak zgody na mutacje TIA/PLC/HMI.

Nie dokonano merge, kasowania gałęzi, force-push ani uruchomienia systemów zewnętrznych. Sam fakt obecności unikalnych commitów nie dowodzi gotowości do integracji.
