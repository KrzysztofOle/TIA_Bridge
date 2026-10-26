# 06 — Integracja Siemens V19, kompilacja i symulacja

## Źródła i poziomy dowodów

Pełny rejestr źródeł oraz bieżących komend jest w [10](10_EVIDENCE_AND_QUALITY.md). Rozróżniamy `INSTALLED`, `API_PRESENT`, `READ_CONFIRMED`, `HISTORICAL_WRITE_EVIDENCE`, `ROUNDTRIP_QUALIFIED` i `NOT_RUN`. TIA_Bridge 2 ma dziś **zero wdrożonych capabilities**, nawet jeśli stary Bridge wykonywał podobną operację.

Świeży `doctor` starego Bridge potwierdził Windows PowerShell 5.1 x64, CLR 4.0.30319.42000, Framework Release 533325, Siemens.Engineering AssemblyVersion 19.0.0.0 i oddzielne kontrakty w `Bin/PublicAPI`. `identify` podał hostVersion 1900.3.501.1; rejestr Windows wskazuje V19 UPD 3. AssemblyVersion nie oznacza dokładnego patch level produktu. SDK nowoczesnego .NET10.0.402 jest zainstalowany, ale dostępności **net48 targeting pack/MSBuild** nie potwierdzono; runtime Framework nie dowodzi możliwości zbudowania workera.

## Worker Openness V19

PublicAPI: `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll` i `.Hmi.dll`; kontrakty `Siemens.Engineering.Contract` oraz `ClientAdapter.Interfaces` są w `Bin\PublicAPI`. Resolver ma allowlist nazw/tokenów i wskazuje wyłącznie katalog Siemens, nie user-writable search path. Worker net48/x64 jest decyzją technologiczną opartą na działającym starym adapterze i przykładzie Siemens, a nie potwierdzonym buildem nowej aplikacji.

Do Attach potrzebna jest konfiguracja uprawnień Openness, akceptacja dostępu aplikacji/firewall w TIA i odpowiednie produkty. Udany Attach starego procesu potwierdza jego dostęp; nowy EXE ma inną tożsamość i może wymagać provisioning/zgody. Nie zmieniać grup, whitelisting ani global search podczas tego audytu. Zarządzanie dialogiem przez GUI wymaga osobnego zakresu Computer Use. Dokument producenta ostrzega, że zdarzenia mogą przychodzić z innego wątku; to nie jest pozwolenie na współbieżne używanie tych samych proxy. [Readme Openness](https://support.industry.siemens.com/cs/attachments/109826886/TIAPortalOpenness_enUS_en-US.pdf).

Każdy Attach ma exact PID/start/project/device checks. Ponowne utworzenie proxy po imporcie i nowy epoch po restarcie są obowiązkowe. Nie używać bezpośredniego odczytu baz wewnętrznych TIA/Lucene jako produkcyjnego adaptera; historyczny eksperyment może wskazywać defekt, lecz nie definiuje wspieranego interfejsu.

## Macierz obiektów i formatów

Lokalny, zainstalowany plik `Siemens.Engineering.xml` zawiera member IDs niżej. Hash pliku jest w indeksie dowodów. Sygnatura to `API_PRESENT`, a nie gwarancja działania dla dowolnego obiektu. Ochrona know-how, Safety, HSP, software units, zależności, języki i inconsistent state mogą ograniczyć operację. Nie omijamy zabezpieczeń.

| Rodzina | Format / mechanizm i lokalny member | Stan dowodu oraz wymaganie przed włączeniem |
|---|---|---|
| PLC FC/FB/OB/DB | `PlcBlock.Export(FileInfo,ExportOptions)` około 119843; `PlcBlockComposition.Import` około 120698, XML SimaticML | API_PRESENT; stary adapter stosuje eksport. Import kwalifikować per kind/language/CPU; OB/LAD Main obecnie tracked-xml, nie target source import |
| PLC UDT | `PlcType.Export` około 129373; `PlcTypeComposition.Import` około 129772, XML albo generated TYPE source | API_PRESENT; `HeaderVersion`/`Version` nie były dostępne w świeżych GetAttribute dla czterech UDT. Nie dopisywać fikcyjnego pola wersji |
| SCL FC/FB i GlobalDB/UDT | `ExternalSourceGroup.GenerateSource`, `ExternalSources.CreateFromFile`, `GenerateBlocksFromSource` | doctor potwierdził overloads; historyczne wykonania i staging w starym Bridge. Nie każdy block/kind ma generated source. Nazwa deklaracji musi pasować |
| Instance DB | XML odczyt / powiązanie FB; API CreateInstanceDB dla zakwalifikowanego tworzenia | istniejący Bridge odrzuca IDB XML import. Nie przenosić GlobalDB source workflow na IDB; kwalifikować oddzielnie |
| PLC tag tables | `PlcTagTable.Export` około 125729, composition Import | API_PRESENT z lokalnych docs; osobny round-trip z constants/languages/order. XLSX extension nie definiuje importu |
| LAD/FBD | native XML + metadane i zależności; repo `.s7dcl`/`.s7res` to sparowane źródła LAD/FBD | nie traktować jako SCL; zachować MLC IDs/resources; brak ogólnego semantic comparatora. Początkowo read-only/tracked |
| WinCC Unified script modules | `HmiScriptModule.Export(DirectoryInfo[,String])` około 84443; `HmiScriptModuleComposition.Import` około 84537 | API_PRESENT i historyczne eksporty/importy. Jednostka native to `.hmi.js` + `.hmi.yml`; serializer CRLF z fixture, zachować YAML i boundary; native API/structure osobno |
| Unified tagi | `HmiTagComposition.Export/Import(DirectoryInfo[,String])` około 81405/81420 | API_PRESENT; nie oznacza screen importu. Typy i referencje kwalifikować osobno |
| Unified ekrany/elementy/zdarzenia | typowane publiczne Create, Delete i attributes; JSON model Bridge jako projekcja | historyczny audyt V19 nie znalazł bezpośredniego Screen/Composition Export/Import dla Unified. Write wyłącznie per potwierdzona właściwość/kompozycja; pełny odtworzalny ekran niepotwierdzony |
| Klasyczne WinCC HMI | `Siemens.Engineering.Hmi.Screen.Screen.Export` i composition Import, XML | odrębna rodzina od `HmiUnified`; API_PRESENT z wcześniejszej lokalnej reflection, nie świeży integration test i nie deklaracja wszystkich wariantów |
| Biblioteki/faceplates/alarms/hardware/Safety | inventory/public metadata i niezmienione native artifacts tam, gdzie API wspiera | początkowo UNSUPPORTED do importu; odczyt rodzica nie dowodzi pokrycia children. Oddzielne capability i test |

XML: preferowany native format wymiany, z wersją namespace/XSD zgodną z rzeczywistym eksportem. Nie wymyślać schematów; parser ma disabled XXE/network resolution i limity głębokości/rozmiaru. SCL: czytelny format edycji dla wspieranego języka; zachować atrybuty, komentarze i export XML jako protected evidence. JSON: typowany model Bridge i kontrakty, **nie natywny pełny projekt Siemens**. YAML: nieprzetwarzane nieznane metadata native Unified; ewentualny manifest Bridge jest jawnie innym schematem. ZAP 19: backup, nie format codziennej edycji. `.ap19` i vendor databases nigdy nie są edytowane przez CODEX.

Siemens opisuje możliwą utratę części ustawień/start values/języków przy workflow export/import; to wystarczający powód, aby kwalifikować format i ochronę pól per kind. Nie traktujemy przykładu add-in jako dowodu lossless round-trip. [Przykład Siemens Export/Import V19](https://cache.industry.siemens.com/dl/files/999/109773999/att_1356247/v1/109773999_TIAV19_Add-In_Export_Import_DOC_v101_en.pdf).

## Kompilacja

`Compiler.ICompilable.Compile` jest udokumentowane lokalnie około 6272. Stary adapter PLC pobiera usługę z PlcSoftware; HMI update pobiera usługę przez `IEngineeringServiceProvider.GetService<ICompilable>` dla dokładnego urządzenia HMI. Nie zakładać, że samo HmiSoftware implementuje ICompilable. Naprawiony lookup reflection ma test syntetyczny, lecz nie nową live kompilację w tym audycie.

Compile PLC/HMI są oddzielnymi operacjami. Raport zbiera rekurencyjne messages, counts, path/state/description, product/adapter versions, elapsed i fingerprint wejść/scope. Errors→FAIL; warnings→policy (MVP zero niezaakceptowanych warnings). Compile może zmienić stan Engineering, więc wymaga E i pełnej historii. Nie utożsamiać go z Save ani z load programu. Laboratoryjny defekt LF/CRLF kompilował się również w błędnym wariancie — compile nie potwierdza natywnych funkcji HMI.

## PLCSIM V19 i PLCSIM Advanced V6.0

Lokalnie INSTALLED: PLCSIM V19, Advanced V6.0 i Virtual Ethernet Adapter (Up), procesy simulation runtime manager i VplcHost. SDK Advanced znajduje się w `C:\Program Files (x86)\Common Files\Siemens\PLCSIMADV\API\6.0`: x64/x86 API DLL i `SimulationRuntimeApi.h`. Nagłówek zawiera `GetRegisteredInstancesCount`, `GetRegisteredInstanceInfoAt`, `RegisterInstance`, `GetOperatingState`, `PowerOn(timeout)`. Lokalne HTML5_API opisuje .NET/C++ i cleanup interfejsów. Nie wywoływano tych metod; aktywna instancja, jej owner, firmware i licencja pozostają do weryfikacji.

| Krok | Docelowy mechanizm | Warunek sukcesu i granica |
|---|---|---|
| wykrycie instalacji | registry, pliki/API, version | INSTALLED nie oznacza licencji ani gotowej instancji |
| uruchomienie managera/instancji | adapter Advanced API; zwykły PLCSIM osobno | zarejestrowane instance ID, owner lease i product version |
| konfiguracja komunikacji | Advanced TCP/IP virtual adapter / izolowany profil | potwierdzone IP/interface/routing, brak dostępu do maszyny; nie automatyczna globalna rekonfiguracja |
| kompilacja do symulacji | Openness + jawne project setting tam, gdzie wymagane | compiled fingerprint, wymagania CPU/software/firmware; nie zmieniamy setting w audycie |
| load do symulowanej CPU | osobno kwalifikowany Openness DownloadProvider / dokumentowany workflow do exact simulation recipient | odrębny S grant, independent target verification; brak fallback do fizycznego urządzenia |
| RUN i readiness | tylko własna autoryzowana instancja przez właściwy simulator adapter | RUN + zgodny loaded program fingerprint + testowe handshake/quality |
| stop/close | tylko instance własnego lease, bounded cleanup | stan potwierdzony; nie stop wszystkich symulatorów ani cudzej sesji |

Openness ma `DownloadProvider` API w lokalnym XML około 10066; to nie jest automatycznie metoda startowania symulatora. Nie znaleziono `StartSimulation`/`SimulationProvider` w badanym publicznym XML V19; brak tych nazw nie dowodzi braku wszystkich możliwych mechanizmów. Advanced API służy zarządzaniu symulacją; load i TIA engineering mają osobne kontrakty. Standardowy PLCSIM/Softbus nie daje automatycznie zewnętrznego serwera OPC UA ani S7 TCP endpointu.

Advanced V6.0 UPD 1 manual, sekcja 7.1, dokumentuje OPC UA przez TCP/IP, niedostępne przez Softbus, oraz user authorization OPC UA w licencji Advanced dla dwóch symulowanych instancji. To nie potwierdza lokalnej aktywnej licencji, liczby dostępnych instances ani całego zestawu ograniczeń CPU/firmware. Sprawdzić ALM/provisioning bez ujawniania kluczy i dokumentację konkretnego build. [Siemens Advanced V6 manual](https://cache.industry.siemens.com/dl/files/578/109955578/att_1267815/v1/s7-plcsim_advanced_function_manual_en-US.pdf).

## WinCC Runtime i HMI simulation

INSTALLED: Unified PC V19, Runtime Advanced V17, Unified Runtime Manager executable ProductVersion 500.30.8901.3. WCCIL procesy są aktywne. To nie ustala projektu aktualnie obsługiwanego przez web klienta ani typu/ważności licencji. Obsługa Runtime Advanced V17 nie wynika z samego posiadania V19 Engineering.

V19 Unified manual §17.4 opisuje `SIMATICRuntimeManager.exe` z silent mode `-s`, `-sim` dla odpowiednich poleceń i końcowym `-c` (m.in. projectstate/start/stop). To kandydat adaptera bez GUI; dokładną składnię argumentów/projekt IDs, kody wyjścia i konieczne uprawnienia należy kwalifikować na kopii. Nie wykonywano start/stop/status przez to narzędzie w audycie. Manager operuje konfiguracją już przygotowaną dla Runtime; nie zastępuje kompilacji, transferu ani logowania do HMI. [WinCC Unified V19 manual](https://support.industry.siemens.com/cs/attachments/109828368/WinCC_VisualizingProcessesUnified_enUS_en-US.pdf).

Wymagana osobna macierz: Unified PC, Unified panel simulation, klasyczne Runtime Advanced, Professional i zdalne panele. Każda obejmuje wersję browsera, runtime build, licencję engineering/simulation/runtime/client, user management UMC/local, certyfikaty i dostępne automation API. Demo/simulation działa tylko w warunkach producenta; nie obiecujemy nieograniczonego bezlicencyjnego działania. Brak API skutkuje UNSUPPORTED lub oddzielnym adapterem z zaakceptowaną obsługą GUI, nie ukrytym klikaniem.

## OPC UA i Snap7 w tym środowisku

Read-only `opc status` na zarejestrowanym `t05.connection.json` z jawnym TEST/Anonymous/None w istniejącym venv: PASS, writes 0, closedtrue, ServerArray wskazuje WinCC Unified. Podstawowy Python wcześniej dał dependency ImportError; niczego nie doinstalowywano. Słuchający 4840 to `opcualds`, nie CPU;4890 to `OpcUaServerRTIL`. Endpoint PLC, namespaces i dozwolone testowe tagi nie zostały potwierdzone. Nie skanowano sieci ani nie wykonywano zapisów.

Port TCP 102 lokalnie należy do `s7oiehsx64`, co nie dowodzi dostępnej CPU Snap7. Brak python-snap7 w badanym Python i OPC venv. Snap7 nie zastępuje Openness ani nie importuje projektu/źródeł. Jego wykonalność dla testów wymaga zewnętrznego TCP/IP do symulatora, kwalifikowanego DB i osobnego grantu; pełna analiza i decyzja w [11](11_SNAP7_PLC_TESTING.md).

## Eksperymenty potrzebne przed reklamowaniem wsparcia

## Zatwierdzona polityka HMI Unified (D-12)

[ADR 0003](decisions/0003-hmi-canonical-model-and-native-artifacts.md) oddziela semantyczny model Bridge od artefaktów vendorowych i wymaga kwalifikowania wersji/builda, rodziny Unified/Classic, rodzaju obiektu, operacji, reprezentacji i scope/coverage osobno. READ/EXPORT nie kwalifikują WRITE/IMPORT ani lossless round-trip. Reader HMI po X-01 i X-04 bazuje na jawnych, sprawdzonych profilach; pełny eksport ekranów Unified V19/V21 pozostaje UNKNOWN, nie jest przyjmowany z istnienia API. Potencjalnie zastępujące importy wymagają niezależnych eksperymentów i readbacku. Brak rozszerzenia X-01.

X-01 worker attach/dispose/apartment i parent loss; X-02 transaction scope/partial import; X-03 PLC round-trip wszystkich pól pierwszej FC; X-04 publiczny natywny reader HMI API/structure; X-05 Advanced instance/API/license + TCP isolation; X-06 Runtime Manager status/start/stop i TLS/UMC; X-07 OPC subscriptions/quality/namespace mapping; X-08 Snap7 test DB i layout. Każdy ma zakres, dowody i kryteria w [09](09_RISKS_AND_DECISIONS.md). Żaden nie jest ukrytą zgodą na bieżące zmiany dirty projektu T05.

## X-01 — rzeczywisty worker read-only, 2026-10-26

Powyższe obserwacje audytu starego Bridge pozostają historyczne. Nowy [Worker.V19](../src/TiaBridge.Worker.V19/README.md) został zbudowany jako net48/x64 z przypiętych reference assemblies NuGet i lokalnego PublicAPI V19; runtime nie dowodził targeting packa. [Osobne rozwiązanie Engineering](../TiaBridge.Engineering.slnx) nie wprowadza vendor dependencies do Core/Host lub standardowego offline build. [Raport X-01](X01_TIA_V19_QUALIFICATION.md) podaje rzeczywiste Attach/read/Dispose dla dwóch jednoznacznych PID/start/AP19, model apartment, callbacki, pomiary i próby awarii własnych procesów.

Uwaga wynikająca z lokalnego XML V19: `TiaPortalProcess.Dispose` zamyka proces TIA i nie jest używane. Worker zwalnia wyłącznie własny `TiaPortal` uzyskany przez Attach. Callback Disposed zapisuje ograniczoną kolejkę numerów wątków, bez IO i ponownego wywołania API. Każde vendor call jest serializowane przez jednego właściciela; transport/heartbeat pozostają niezależne. Wynik udanego odczytu powstaje dopiero po Dispose. Timeout/utrata IPC nie przerywają Siemens call; wymagany jest dowód quiescence albo końca dokładnego procesu przed dalszą pracą.

X: zweryfikowano jako lokalny NTFS; projekty nie były kopiowane, otwierane od nowa ani zapisywane przez Bridge. Zgoda read-only nie daje praw PLC/HMI, Save, Compile lub Download. [Jawna instrukcja testów](../tests/engineering/README.md) nie jest automatyczną zgodą na kolejne live uruchomienie.
