# 10 — Dowody analizy i kontrola jakości

Analiza rozpoczęta 2026-10-24, dokumentacja i końcowa kontrola 2026-10-25 (Europe/Warsaw). Odczyty środowiska są obserwacjami punktowymi z24 października, nie ciągłym monitoringiem. Nie implementowano nowej aplikacji i nie wykonano operacji zmieniającej projekt TIA.

## Materiał wejściowy

Nowe repo `M:\_Moje_Programy\TIA_Bridge`: branch `main`, clean worktree przed zadaniem, HEAD/origin main `a7c734de57798ccb879fa9ab2764c21505a2cabd`, wcześniejszy commit `01b2181`. Origin: `https://github.com/KrzysztofOle/TIA_Bridge.git`. Przeczytano AGENTS, README, architecture/sync-model/formats/safety/roadmap, ADR 0001 i README planowanych modułów. Wartościowe koncepcje zachowano; ADR 0002 wyjaśnia rozszerzenie o autonomię i async runtime.

Stary Bridge: `C:\RMA\t05_plc`, HEAD `76dea4c1f5102de391b44b0b80aa037a03a0adcf`, bieżąca gałąź `codex/t05-opc-test-engine-v1`; worktree dirty. [source-index.json](evidence/source-index.json) zawiera 32 pliki kodu/założeń/vendor docs/wybranych logów z SHA-256. Pole `git_status` odnosi się do chwili indeksowania, nie finalnego stanu innej sesji. Nie kopiowano kodu klienta, AP19, DLL lub raw logów do nowego repo.

Indeks jest kontrolą tożsamości materiału, nie gwarancją dostępu po klonowaniu. Ignorowane `UserFiles`/vendor docs pozostają lokalne. Inny komputer potrzebuje tej samej legalnej instalacji i udostępnionych chronionych artifacts albo odtworzenia testów na syntetycznym projekcie. Hash bez zawartości nie dowodzi poprawności logiki. Pełna rekonstrukcja dirty kodu wymaga zachowanego checkoutu; sam stary HEAD nie wystarcza.

## Odczyty i wynik

| Dowód | Fakty potwierdzone teraz | Ograniczenie |
|---|---|---|
| CLI `--help` | aktualne komendy i profile; brak działającego CLI nowej aplikacji | help nie dowodzi możliwości wykonania |
| `doctor --timeout 15` | PS5.1.22000.2538 x64/CLR 4.0.30319.42000, Framework release 533325, załadowane V19 DLL+kontrakty, sygnatury GenerateSource/CreateFromFile/GenerateBlocksFromSource/Import | reflection bez importu/compile |
| `processes --timeout 15` | dwa dostępne portal handles: PID 1916 RecipeSim i 26064 `Project1` | inne procesy Portal z task managera nie muszą być niezależnym Openness recipient |
| `--process-id 1916 --timeout 15 identify` | exact AP19 RecipeSim, PLC i HMI match, API 19.0.0.0, host 1900.3.501.1, `IsModified=True`, `projectWrites=false`, `onlineOperationsPerformed=false` | brak fizycznej izolacji i brak Save; pamięć≠dysk |
| ten sam `inspect` |15 PLC obiektów, UDT Version/HeaderVersion unsupported, udane API read | nie pełny software-unit/safety inventory ani source equivalence |
| inventory HMI z identify |7 ekranów,4 tag tables,16 user tags,1 connection | nie native public function readback ani Runtime proof |
| registry/pliki/procesy | PLCSIM V19, AdvancedV 6.0/API 6.0 header+DLL, Virtual Ethernet Up, WinCC Unified PCV 19, Runtime AdvancedV 17, WCCIL procesy | license NOT_VERIFIED; brak simulator start/load/RUN |
| `dotnet --list-sdks`, `py -3` | SDK 10.0.402, Python 3.12.2 x64 | net48 targeting pack/build workera NOT_VERIFIED |
| listen ports |4840 opcualds,4890 OpcUaServerRTIL,102 s7oiehsx 64, HTTP 80/443 | porty nie identyfikują CPU/projektu/izolacji |
| OPC status: bazowy Python | ERROR/ImportError, writes 0, closedtrue; brak backend dependency | nie błąd serwera ani powód globalnego install |
| OPC status: istniejący OPC venv | TEST/Anonymous/None jawnie z config, PASS, writes 0, closedtrue; około 37 ms connect i1.58 s całej komendy; ServerArray WinCC Unified | odczyt standardowego statusu, nie zmiennych/logiki CPU ani SECURE |
| `Invoke-WebRequest https://localhost/ -TimeoutSec5` | błąd ustanowienia SSL | przyczyna TLS (np. trust/hostname/protocol) nierozstrzygnięta; nie obniżano weryfikacji; HMI browser E2E NOT_RUN |
| Snap7 availability | brak python-snap7 w bazowym Python i OPC venv | brak połączenia S7, test DB/PUT-GET/API compatibility NOT_RUN |
| storage | M:exFAT około 14 GB free; C:NTFS około 32.8 GB; X:NTFS około 164.6 GB w chwili odczytu | snapshot pojemności, nie gwarancja przyszłej Archive; state DB poza M: |

Polecenia Bridge wykonano poprzez istniejące `py -3 tools/T05_TIA_Bridge.py --format json [--process-id 1916] --timeout 15 <doctor|processes|identify|inspect>`, bez alternatywnego kanału TIA. OPC: `tools/T05_TIA_Bridge.py --format json opc status --connection tools/t05_tia_bridge/config/opc/t05.connection.json`, najpierw bazowy Python, potem istniejący `UserFiles/T05_TIA_Bridge/OPC/venv/Scripts/python.exe`. Sam `status` wykonuje wyłącznie connect/read standard probes/disconnect; nie Browse pełnego namespace ani write.

Logi historyczne wybrane w [01](01_AUDIT_OLD_BRIDGE.md) poświadczają NonRecoverableException, Archive space failure, timeout i readback mismatch. Najnowsza zaobserwowana sesja `982a336fd22c4cf3a779cf81553fffec` rev 13 REVIEW_REQUIRED / FAILED/UNKNOWN jest nowsza od wcześniejszej sesji 5cf..., której latest w odczycie był już ABORTED. Audyt nie podjął recovery żadnej z nich.

## Wykonane testy offline starego Bridge

Python z `PYTHONDONTWRITEBYTECODE=1`, testy na mocks/syntetycznych plikach. Nie wywołują kompilatora Siemens. Test PowerShell reliability używa produkcyjnego modułu z fake project.Save i fake compiler/guards.

| Polecenie | Wynik |
|---|---|
| z root starego repo: `py -3 -m unittest discover -s tools/t05_tia_bridge/tests -p test_modification_session.py -v` |25 testów,4.552 s, OK |
| z `tools/t05_tia_bridge/tests`: `py -3 -m unittest test_operation_reliability test_v2 test_hmi_script_export test_sync_native_import_serialization test_native_module_layers -q` |69 testów,13.877 s, OK |

Łącznie **94 PASS**, bez skip w tych dwóch wynikach. To wybrana regresja zabezpieczeń sesji, timeoutów, stagingu, baseline i HMI content/API/structure oraz CRLF serializer. Nie jest pełną regresją starego Bridge ani dowodem działania nowego hosta, workera, live importu, Compile, Save, symulacji lub testów HMI.

## Źródła pierwotne

Zweryfikowano w24/25 października 2026, bez traktowania nowszego produktu jako dowodu wsparcia V19.

| ID | Źródło | Wykorzystanie |
|---|---|---|
| S-01 | lokalny `PublicAPI/V19/Siemens.Engineering.xml`, hash w indeksie | dokładne member IDs PLC/Unified/Compiler/Transaction/DownloadProvider; lokalna wersja zamiast zgadywania |
| S-02 | lokalny `PLCSIMADV/API/6.0/SimulationRuntimeApi.h`, hash w indeksie; HTML5_API en-US | API instance/manager/power/state i cleanup; bez wywołania |
| S-03 | [Siemens V19 Export/Import Add-In](https://cache.industry.siemens.com/dl/files/999/109773999/att_1356247/v1/109773999_TIAV19_Add-In_Export_Import_DOC_v101_en.pdf) | komponenty .NET Framework 4.8 i ostrzeżenia o stratach przy przykładowym workflow; nie pełna gwarancja API |
| S-04 | [Siemens Openness manual/readme](https://support.industry.siemens.com/cs/attachments/109826886/TIAPortalOpenness_enUS_en-US.pdf) | event thread safety i ograniczenia; nie zmieniano global search |
| S-05 | [PLCSIM Advanced V6.0 UPD 1 manual](https://cache.industry.siemens.com/dl/files/578/109955578/att_1267815/v1/s7-plcsim_advanced_function_manual_en-US.pdf) | §7.1 OPC UA TCP/IP vs Softbus i uprawnienie licencyjne; lokalna aktywacja niepotwierdzona |
| S-06 | [WinCC Unified V19 manual](https://support.industry.siemens.com/cs/attachments/109828368/WinCC_VisualizingProcessesUnified_enUS_en-US.pdf) | §17.4 Runtime Manager CLI; kandydat automatyzacji, nie wykonany test |
| S-07 | [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) | .NET10 LTS jako rekomendowany host |
| S-08 | [SQLite WAL](https://www.sqlite.org/wal.html) | ograniczenie współlokalnych procesów/shared memory; NTFS jest dodatkową lokalną polityką |
| S-09 | [MCP Tasks](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/tasks) | eksperymentalne Tasks i negotiation; brak uzależnienia hosta od klienta |
| S-10 | [Playwright locators](https://playwright.dev/docs/locators), [frames](https://playwright.dev/docs/frames) | mechanizmy web automation, brak gwarancji DOM konkretnych HMI |
| S-11 | [Snap7 Client](https://snap7.sourceforge.net/snap7_client.html), [license](https://snap7.sourceforge.net/licensing.html) | classic S7/native library i ograniczenia CPU |
| S-12 | [python-snap7 tag 3.2.0 README](https://raw.githubusercontent.com/gijzelaerr/python-snap7/3.2.0/README.rst), [LICENSE](https://raw.githubusercontent.com/gijzelaerr/python-snap7/3.2.0/LICENSE), [releases](https://github.com/gijzelaerr/python-snap7/releases) | rozdzielenie 2.x wrapper/3.x pure Python, przypięcie wersji do kwalifikacji |
| S-13 | [python-snap7 PLC support](https://python-snap7.readthedocs.io/en/latest/plc-support.html), [client](https://python-snap7.readthedocs.io/en/latest/API/client.html) | wymagania/ograniczenia, odróżnienie latest od tagu |
| S-14 | [Siemens S7 buffer services](https://cache.industry.siemens.com/dl/files/801/67295801/att_1332517/v1/67295801_SIMATIC_NET_OPC_UA_S7bufferOrientedService_DOC_V2_1_en.pdf) | §4.5 PUT/GET i non-optimized DB vs symboliczne protokoły |

## Kontrola jakości pakietu

`scripts/audit_quality.py` sprawdza istnienie wymaganych 12 dokumentów, lokalne linki/anchors, spójność 19 metod registry/schema/tabeli, default≤max timeout, poprawność schematów Draft 2020-12, syntetyczne request/result examples oraz negatywne przypadki (brak grantu, hash mismatch format, unknown param, false success). Dodatkowo wymaga pokrycia OB-01–24 i RT-01–16 oraz etapów 0–11. Nie sprawdza semantyki Siemens ani nie renderuje/wykonuje aplikacji. Wynik maszynowy można zapisać wyłącznie do ignorowanego `.tia-bridge/quality.json`.

Końcowa kontrola offline: **PASS** — 28 plików Markdown, 89 lokalnych odnośników, 19 spójnych metod, 3 poprawne przykłady i 8 odrzuconych negatywnych kontraktów; wszystkie 24 wpisy audytu, 16 scenariuszy awarii i 12 etapów obecne. `git diff --check` po korekcie pustych końców plików: PASS. Weryfikacja hashy wszystkich 32 zaindeksowanych źródeł: bez driftu w chwili kontroli. Przegląd staged diff: 33 jawnie wskazane pliki, zgodność indeksu z worktree (z uwzględnieniem CRLF), poprawny JSON, brak wykrytych wzorców credentiali/kluczy oraz wykluczonych ścieżek; `git diff --cached --check`: PASS. Push weryfikowany osobno po commicie. Przegląd merytoryczny obejmuje wszystkie 6 etapów workflow, separation W/S/B/P/D/C/E/T, formalne SYNCED, grant/delete policy, dispatcher/quarantine, native/public HMI coverage, deadline/heartbeat/cancel, per-operation contracts, MVP dependencies i dodatkowe Snap7. Mermaid ma3 proste diagramy w architekturze/audycie; nie deklarujemy wykonania renderera Mermaid ani sprawdzenia przez TIA.

Git: każda dodana ścieżka jest jawnie wskazana; staged diff i `git diff --cached --check` przed jednym commitem. Nie dodajemy `.vscode/mcp.json`, starych repo zmian, raw logs, projektów, backupów, vendor binaries, credentials ani `.tia-bridge` artifacts. Dla filesystem ownership M: używamy wyłącznie per-command `git -c safe.directory=M:/_Moje_Programy/TIA_Bridge`, bez zmiany global config. Push normalny, bez force; przy ruchu origin zachowujemy obce zmiany i nie nadpisujemy ich.
