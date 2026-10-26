# X-01 — kwalifikacja Siemens TIA Portal V19

Data: 2026-10-26. Baza: `eca8a079b04001c792c0669b991edfbeb6faaca6`, czysty main/origin main przed zadaniem. Jawna autoryzacja obejmowała wyłącznie odczyt dwóch otwartych projektów pod X:\TESTY_TIA_Bridge. [D-11](../tests/resilience/X01_LIMITED_GATE.md) dopuszczała osobny eksperyment na lokalnym NTFS; nie kwalifikuje produkcji, exFAT lub mutacji.

**Odczyt obu instancji i izolacja procesów przeszły. Nie ogłaszamy pełnego X-01 PASS:** pełna regresja nadal zawiera jeden FAIL NF-03 na M:/exFAT. Kryterium 11 z bieżącego zadania nie jest więc bezwarunkowo spełnione. Użytkownik dopuścił commit poprawnej, przetestowanej części z jawnym wynikiem częściowym. Nie zmieniano progów, historycznych raportów lub semantyki kontraktów w celu uzyskania PASS.

[Raport maszynowy i pełna lista zmienionych plików](../tests/engineering/x01-qualification.json) wiążą agregaty z SHA-256 lokalnych dowodów. Surowe logi, projekty, inventory producenta, discovery/tokeny, DLL i bazy pozostają poza Git, w LOCALAPPDATA/TIA_Bridge2/dev/x01/<run-id>. To raport wyników, nie nowy kontrakt API.

## Środowisko i dowody

| Element | Rzeczywiście potwierdzony stan |
|---|---|
| Windows | 10.0.22000, x64 |
| TIA Portal | V19 Update 3, executable ProductVersion 1900.3.501.1 |
| PublicAPI | Program Files/Siemens/Automation/Portal V19/PublicAPI/V19; Engineering i Hmi 19.0.0.0, token d29ec89bac048f84 |
| Kontrakty | Bin/PublicAPI, Contract token 37a18b206f7724a6; ClientAdapter.Interfaces token 82af32ec2e7eb4c1; obie wersje 19.0.0.0 |
| Engineering DLL | FileVersion 1900.0.6801.3, SHA-256 `2d60b4bb34f810e67e78b8018e59a3b738b0a2393ef273164b4858f6c2196626`; Authenticode NotSigned, nie deklarujemy podpisu Authenticode |
| Framework | runtime Release 533325 / 4.8.09037; systemowego net48 targeting packa brak |
| Build | SDK/MSBuild .NET 10.0.402; przypięte NuGet reference assemblies 1.0.3 umożliwiły rzeczywisty build net48/x64 bez instalacji systemowej |
| Uprawnienia | użytkownik jest członkiem Siemens TIA Openness także w efektywnym tokenie; SID operatora/właścicieli w lokalnym environment.json; nie zmieniano grup lub rejestru Siemens |
| Licencje | ALM service Running, produkty wykryte przez publiczne InstalledSoftware; entitlements NOT_VERIFIED, bez odczytu kluczy i bez założenia, że działający serwis dowodzi licencji |
| X: | lokalny Fixed/NTFS, dwa pliki AP19; brak migracji lub zapisu projektów |
| Stan Bridge | osobny własny UUID root w LOCALAPPDATA/TIA_Bridge2/dev/x01 na C:/NTFS; plikowa SQLite, journal/outbox, artifacts i discovery |


Maszyna: Intel Xeon E3-1240 v5 3,50 GHz, 4 rdzenie/8 logicznych CPU, 34 288 074 752 B RAM. Pomiar końcowy częściowo pokrywał się z regresją Fake na tej samej maszynie; nie jest izolowanym benchmarkiem producenta. M: pozostaje exFAT. Stan, SQLite/journal/outbox/artifacts i syntetyczna edycja pliku podczas odczytu były na C:/NTFS. X: zweryfikowano jako Fixed/NTFS, bez zmiany konfiguracji dysków lub relokacji projektów.

## Tożsamość dwóch instancji

| Instancja | PID / start UTC | Projekt testowy | Attach / Read / Dispose |
|---|---|---|---|
| A | 29068 / 2026-10-25T15:05:18.4759749Z | X:\TESTY_TIA_Bridge\T04_T05-000_Testowy\T04_T05-000_Testowy.ap19 | PASS / PASS / PASS |
| B | 26064 / 2026-10-24T15:54:32.2859503Z | X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy\T05_RecipeSim_V19_Testowy.ap19 | PASS / PASS / PASS |

Publiczne TiaPortal.GetProcesses wskazało dokładnie te dwa autoryzowane AP19, mimo pięciu procesów Windows Portal. Innych instancji nie próbowano Attach i nie zamykano. Bieżący SID zgadzał się z właścicielem obu procesów; pełne SID są w lokalnym environment.json. Exact PID/start/executable/SID/path sprawdzano przed Attach, ponownie przez publiczny ProjectPath po Attach i po Dispose. Nie używano tytułów okien lub samej kolejności listy. ProjectKey jest istniejącym modelem Core, zapisanym w zaufanej konfiguracji operatora, a nie hashem nazwy projektu.

A: dwa native Device i 58 bounded nodes, w tym dwa PlcSoftware i dwa Unified HmiSoftware. Rodzice zawierający oba typy mają UNKNOWN, zamiast fałszywej jednej rodziny. B: dwa Device i 14 nodes, po jednym PLC i Unified HMI. Coverage jest PARTIAL/UNSUPPORTED dla nieczytanych wymiarów; nie ma pełnego inventory obiektów, eksportera, trwałych ObjectId dla nodes lub dowodu poprawności natywnego API HMI.

## Wykonany zestaw Engineering

Końcowy zestaw: **93 PASS / 0 FAIL / 0 SKIP** — 80 przeplatanych cykli odczytu (20 A + 20 B w STA oraz tyle samo w MTA), siedem scenariuszy procesowych, cztery opt-in testy startup/loader i dwa świeże odczyty po dodaniu odmowy niezakwalifikowanego scope/cache w Host. Historyczne nieudane próby podano osobno; nie zostały przepisane na PASS. Standardowe discovery offline nie uruchamia testów live.

| Dowód | Run ID | Rzeczywisty wynik |
|---|---|---|
| STA i fault matrix, poprawiony worker | 98a2ac3a1f4945759947b509acf38630 | 40 odczytów + 7 scenariuszy PASS |
| MTA, ten sam build workera | c2327b77fd6d49128aa3bd3c17e343bd | 40 odczytów PASS |
| Smoke po guardzie scope/cache Host | c8c0c733b6044aefb9b6a94c7dcfac8f | A i B PASS |
| Startup/loader, env TIA_BRIDGE_V19_WORKER | wdrożony EXE z ebccfe25f59d4bf6bc805452436a8356 | 4 unittest PASS; x64, brak vendor load w self-test, exact DLL identity/hash, odmowa syntetycznego vendor shadow oraz nieznanych argumentów |

W STA owner i callback Disposed miały managed thread 9, w MTA thread 10; jeden dedykowany owner posiadał proxy. Callback jedynie kolejkował numer wątku, bez blokującego IO lub ponownego API. Oba modele przeszły ten ograniczony scenariusz; nie dowodzi to poprawności wszystkich callbacków lub wszystkich metod Openness. Konserwatywny domyślny profil pozostaje STA, pojedynczy vendor call, bez zwiększania równoległości.

## Awarie, restart i brak fałszywego sukcesu

| Scenariusz rzeczywistego workera | Wynik i dowód |
|---|---|
| QUEUED cancel przed dispatch | PASS; CANCELLED, zero attempts dla anulowanego B; pierwszy odczyt A kończy się prawidłowo |
| RUNNING cancel | PASS; intent nie zmienia od razu RUNNING na CANCELLED; realny zakończony odczyt i Dispose mogą dać SUCCEEDED |
| Spóźniony wynik | PASS; autentyczny receipt po Dispose przytrzymany do deadline; FAILED/TIMEOUT pozostaje, receipt LATE_OR_DUPLICATE i QUIESCENT zapisane jako dowody |
| Stary epoch | PASS; po autentycznej komunikacji wrapper testowy zmienia epoch; FENCED_REJECTED, brak SUCCEEDED, brak ponowienia |
| Utrata pipe | PASS; realny pipe zamknięty po wejściu w Attach; brak wyniku sukcesu, własny worker kończy odczyt/Dispose i wychodzi |
| Utrata rodzica | PASS; zabito wyłącznie dokładny własny Host; worker potwierdził Dispose.Returned i zakończył się, zanim rozpoczęto recovery; FAILED/WORKER_UNAVAILABLE po restarcie |
| Zakończenie workera | PASS; exact PID/start/path własnego dziecka, przed dispatch/vendor call; brak sukcesu lub automatycznego retry |

Każdy scenariusz sprawdzał ten sam operation_id i końcową klasyfikację po ponownym uruchomieniu Host oraz powtórzeniu oryginalnego submitu; najwyżej jeden attempt. Nie symulowano mutacji w Siemens i nie przedstawiano śmierci procesu lub lease jako dowodu braku skutków mutacji. Trwałe UNKNOWN/quarantine pozostają zakresem wcześniej wykonanych regresji Fake.

Odtworzono też pierwotny RUNNING po błędzie coverage_refs (operation 81964ae8-f61c-4d49-98d2-ab429977ef0f): po restarcie FAILED, nie SUCCEEDED, dispatch count nadal 1, ponowiony submit ten sam ID. Host READY po 1016,32 ms, N=1; rzeczywiste procesy CLI status/events/result zwróciły odpowiednio kody 0/0/6. SUCCEEDED na odpowiedzi operation.events oznacza udane zapytanie o historię, nie sukces pierwotnego odczytu. Późniejsze odczyty miały osobne operation_id.

## Benchmark rzeczywisty

N=20 dla każdej fazy i instancji/modelu; p99 faz nie podajemy przy takiej liczbie próbek. Czasy w ms, z uwzględnieniem wszystkich próbek udanych końcowych przebiegów. Błędy/timeouty historyczne są niżej, poza tabelą udanych cykli.

| Tryb / instancja | Faza | p50 | p95 | max |
|---|---|---:|---:|---:|
| STA / A | Enumeracja | 64.23 | 194.37 | 338.73 |
| STA / A | Attach | 138.22 | 210.73 | 219.98 |
| STA / A | Odczyt | 1025.85 | 1351.60 | 2233.98 |
| STA / A | Dispose | 23.11 | 43.80 | 97.30 |
| STA / A | Cykl vendor | 1262.41 | 1774.13 | 2755.58 |
| STA / A | Submit HTTP | 17.54 | 32.42 | 931.53 |
| STA / B | Enumeracja | 56.09 | 113.74 | 125.60 |
| STA / B | Attach | 126.58 | 184.97 | 575.92 |
| STA / B | Odczyt | 322.12 | 433.02 | 853.89 |
| STA / B | Dispose | 20.75 | 32.54 | 34.62 |
| STA / B | Cykl vendor | 546.03 | 1001.03 | 1054.25 |
| STA / B | Submit HTTP | 18.36 | 46.90 | 61.99 |
| MTA / A | Enumeracja | 65.85 | 83.70 | 157.88 |
| MTA / A | Attach | 130.16 | 189.14 | 287.98 |
| MTA / A | Odczyt | 1042.28 | 1208.53 | 1220.97 |
| MTA / A | Dispose | 26.04 | 41.93 | 43.77 |
| MTA / A | Cykl vendor | 1276.01 | 1467.06 | 1500.39 |
| MTA / A | Submit HTTP | 20.04 | 32.41 | 781.21 |
| MTA / B | Enumeracja | 56.31 | 73.27 | 144.53 |
| MTA / B | Attach | 131.26 | 148.80 | 155.29 |
| MTA / B | Odczyt | 304.50 | 371.50 | 435.50 |
| MTA / B | Dispose | 24.03 | 36.31 | 37.64 |
| MTA / B | Cykl vendor | 521.23 | 632.66 | 667.78 |
| MTA / B | Submit HTTP | 17.20 | 36.75 | 43.73 |

Cykl vendor sumuje enumerację/Attach/read/Dispose, bez startu procesu i HTTP polling. Pierwszy zimny submit A wyniósł 931,53 ms (STA) i 781,21 ms (MTA); nie ukryto ich w max. Nie stwierdzono timeoutów w 80 końcowych normalnych cyklach. Dwa TIMEOUT w końcowej fault matrix są oczekiwanymi wynikami kontrolowanych scenariuszy, a nie udanymi operacjami.

| Tryb / endpoint | N | p50 ms | p95 ms | p99 ms | max ms |
|---|---:|---:|---:|---:|---:|
| STA / status | 3272 | 35.46 | 79.47 | 170.31 | 303.34 |
| STA / health | 2168 | 37.42 | 72.27 | 112.29 | 219.03 |
| STA / result | 271 | 9.45 | 49.68 | 65.71 | 73.38 |
| STA / events | 271 | 15.29 | 37.21 | 101.06 | 209.29 |
| MTA / status | 2136 | 29.50 | 69.63 | 112.53 | 184.89 |
| MTA / health | 2136 | 38.64 | 74.24 | 123.30 | 248.24 |
| MTA / result | 267 | 10.01 | 52.70 | 74.33 | 120.18 |
| MTA / events | 267 | 15.58 | 35.36 | 53.45 | 170.17 |

Osiem równoczesnych klientów statusu i health, niezależny result/events oraz syntetyczna edycja pliku na NTFS działały podczas pracy i przy przytrzymanej odpowiedzi. p95 status ≤100 ms w obu przebiegach. Host był również dostępny podczas rzeczywistego pierwszego Attach oczekującego na autoryzację: 945 statusów p50/p95/p99/max 7,18/12,01/25,91/159,26 ms i 945 health 10,23/15,29/25,60/81,55 ms. Heartbeat nie jest uznawany za dowód postępu.

Start workera do trwałej rejestracji po handshake: 1395,63 ms STA i 996,54 ms MTA (po jednej obserwacji na proces, bez deklaracji percentyli startup). Pełne scenariusze recovery/utrata rodzica trwały 6,48 s z dispatch, Dispose, startami Host i sprawdzeniem idempotencji; nie jest to czysty czas restartu. Czyste odtworzenie starego RUNNING do Host READY: 1016,32 ms, N=1.

## Zasoby i ograniczenie stabilności

STA: Host RAM 110 575 616 → 143 310 848 B (max 158 191 616), handles 533 → 544; worker RAM 49 922 048 → 55 934 976 B, handles **462 → 702**. CPU skumulowane Host 4015,63 → 63 687,50 ms; worker 1984,38 → 15 281,25 ms. MTA: worker RAM 49 311 744 → 55 099 392 B (max 57 806 848), handles **456 → 602**, CPU 1453,13 → 11 218,75 ms; Host handles 539 → 546. Nie utożsamiamy tego z CPU procentowym/idle.

Wzrost uchwytów jest jawny. Nie rozstrzygnięto, co jest odroczonym zwalnianiem GC/vendor, a co trwałą retencją. Nie wymuszano GC dla lepszego wyniku. Wszystkie własne procesy z końcowych przebiegów zakończyły się; nie pozostały niekontrolowane workery. Nie ma kwalifikacji pracy bezterminowej, zasobów w 8 h ani zamknięcia R-14. To ograniczony test Attach/read/Dispose, nie produkcyjny release.

## Historia błędów i poprawek

- d3bfd383509149b2b7526fa4895343d1: guard AP19 odrzucał końcowy separator z WorkspacePath.ValidateRoot; poprawiony i testowany, przed pierwszym vendor call.
- 05ff7c9904694682b29492933d3e592a: Attach A przekroczył 120 s przy oczekiwaniu na zgodę aplikacji. FAILED/TIMEOUT pozostaje; późniejszy powrót nie awansował go do SUCCEEDED. Własny worker skończył po obsłudze dialogu, bez zakończenia TIA.
- 1149d7208e604edabc4587b1e9b9912c: realny Attach/read/Dispose A zakończył się, ale Host nie mógł zatwierdzić wyniku z tekstami w coverage_refs. Poprawiono referencje na dwa zweryfikowane artefakty (observation i Core Coverage), dodano test trwałości. Pierwotne RUNNING odzyskano jako FAILED, bez replay.
- 479a865b1d2c42fe9fdf4abb42b89e6f: 40 MTA odczytów przeszło, później harness błędnie wymagał 202 od idempotentnego submitu zwracającego 200; poprawiono klienta testowego, nie API.
- 3c8e4b12d41a4f84bab7b80e6090a6f3: współdzielenie własnego audit file w harnessie spowodowało IO error; zastosowano FileShare.ReadWrite i zwalnianie tylko własnych barier w finally. Wcześniejszy worker kończył Dispose i wyszedł; nie zabijano TIA.
- 9a0c8e40f7b84a89a0ebc9cdf50f1ed4: 47 scenariuszy przeszło przed korektą mixed Device. Naprawiono nadpisywanie rodziny ostatnim child; UNKNOWN rodzica zachowuje obie rzeczywiste klasy w inventory. Ponowiono 80 odczytów i 7 fault cases na poprawionym buildzie.
- ebccfe25f59d4bf6bc805452436a8356: nowy EXE ponownie czekał na jednorazowy dostęp, odpowiedź po 120 s; timeout zachowano. Użytkownik wybrał i wykonał wariant B. Potwierdzony Dispose/koniec procesu poprzedzały nową próbę. Nie zmieniono globalnej whitelisty i nie użyto Computer Use.
- Guard Host odmawia niezakwalifikowanego inventory_scope plc/hmi i fresh=false, zamiast wykonywać inny zakres/cache. Domyślny all_supported/fresh pozostaje ograniczonym live odczytem. Dodano pięć testów decyzji i dwa ponowne live odczyty.

## Implementacja i bezpieczeństwo

[Worker.V19](../src/TiaBridge.Worker.V19/README.md), [osobny Engineering lane](../tests/engineering/README.md) i [IPC 1.1](../contracts/ipc/v1.1/message.schema.json) współpracują z istniejącym Host/Core/API/CLI/Storage/outbox/schedulerem; nie ma drugiego silnika. IPC 1.0 Fake i 19 kontraktów publicznych są zachowane. Dispatch 1.1 nie posiada syntetycznego wzorca sukcesu; dopiero autentyczny wynik po Dispose może być walidowany względem bindingu i utrwalony. Brak automatycznego retry native call. SIEMENS DLL nie trafiły do Host/Core, bin workera lub Git.

Self-test dowiódł net48/x64 bez załadowanego Siemens; loader-check dokładnego assembly i SHA; syntetyczny vendor shadow obok własnego EXE został odrzucony przed Attach. Rzeczywisty handshake sprawdzał PID/start/nonce/build hash/epoch i SID. Odczyt ACL Engineering DLL potwierdził właściciela Administrators, FullControl SYSTEM/Administrators oraz ReadAndExecute Users/AppPackages; nie zmieniano ACL. Regresje Fake badają HMAC/sequence/frame attacks; test obcego SID nie był nowym live testem X-01 i nie jest deklarowany jako PASS. ACL/auth nie osłabiano.

Kod Bridge i audit etapów obejmują GetProcesses, exact Attach, Projects/identity/IsModified, bounded DeviceItems/SoftwareContainer i Dispose wyłącznie własnej attached session. Audit grupuje odczyty getterów w jawne etapy; nie jest trace wszystkich wewnętrznych wywołań Siemens. Przegląd źródła potwierdza brak Save/Import/Create/Delete/Rename/Compile/Download/Runtime/project.Open, zapisu AP19 lub wewnętrznych baz. TiaPortalProcess.Dispose (zamknięcie TIA według lokalnego XML) nigdy nie jest wywoływane. To nie obchodzi własnych wewnętrznych działań producenta.

IsModified przed/po wszystkich końcowych odczytach było false, metadane plików przed/po każdego końcowego przebiegu były identyczne. To ograniczone dowody; sam hash/mtime AP19 nie dowodzi całego stanu projektu w pamięci. PID/start obu portali zachowano, nie zamykano ich. Nie uruchamiano PLC/HMI, symulatorów lub funkcji Runtime. Historyczny katalog B-02C nie był sprzątany.

## Walidacja offline i wynik końcowy

Build Release rozwiązania offline i Engineering: PASS, zero warning/error. Pełny validate_offline.py run a901583fdab74745bebadad476794a39: **763 PASS / 1 FAIL / 0 SKIP z 764 .NET**, **31/31 Python PASS**, RT-03/05/06/15/16 PASS, audit/format/diff i Core dependency boundary PASS. Jedyny FAIL: ApiPerformanceTests.One_and_eight_clients_keep_status_and_workspace_responsive_during_real_worker_hang — **NF-03 edycja M:/exFAT p95 178,5496 ms**. Bez zmiany progu lub niebezpiecznego obejścia plików.

Po końcowych zmianach uruchomiono dodatkowo **25/25 V19OfflineTests**, obejmujących dziewięć nowych przypadków klasyfikacji i odmowy scope/cache. Suma unikalnych przypadków .NET obu przebiegów: **772 PASS / 1 FAIL / 0 SKIP z 773**, nie suma zawierająca powtórzone 16 testów. Nie twierdzimy, że wszystkie 773 były w jednym pełnym wywołaniu runnera. Dodatkowy smoke live obu instancji po tym buildzie: PASS.

Reporter B-02D nadal zawiera historyczne sformułowanie o oczekującej D-11; nie interpretujemy go jako aktualnej normy. D-11 jest zatwierdzona, NF-11 ≥8 h pozostaje NOT_RUN/B-10B, NTFS profile zachowuje własne dowody, exFAT FAIL nie staje się PASS. Mimo dopuszczenia ograniczonego eksperymentu nie zamieniamy tego na deklarację pełnego spełnienia kryterium 11 obecnego zlecenia. R-14 i pełna kwalifikacja produkcyjna pozostają otwarte. Nie rozpoczyna się B-03A ani następnych eksperymentów.

```text
OFFLINE: FAIL
ENGINEERING: PASS (ograniczony odczyt V19 Update 3, C:/X: lokalny NTFS)
RUNTIME: NOT_RUN
X-01 INSTANCE A: PASS
X-01 INSTANCE B: PASS
X-01 OVERALL: FAIL (kryterium pełnej regresji offline)
B-03A READINESS: NOT_READY
```

Lista zmienionych plików: pole changed_paths w [raporcie maszynowym](../tests/engineering/x01-qualification.json). Obejmuje nowe rozwiązanie/worker net48 i jego README/lockfile, wspólny IPC/kompatybilność, profil/supervisor/scheduler Host, net48 probe i testy, IPC schema/examples 1.1, audit środowiska, harness/reporty Engineering oraz aktualizacje README/dokumentów 06/08/09/14/contracts/scripts/tests/Zmiany. AGENTS.md i Instrukcja_ChatGPT.md pozostawiono bez zmian; ocenę każdej sekcji i uzasadnienie zapisano w Zmiany.md.

## Ponowna kwalifikacja po przeniesieniu repo na X:/NTFS — 2026-10-26

Wyniki nowego pełnego przebiegu z checkoutu `e6b659708c6141ad89be4ea109a64a8e76109a3d` są oddzielone od powyższego historycznego X-01. Raport przebiegu D-11, rozdział exFAT/NTFS, statusy i hashe dowodów znajdują się w [kwalifikacji NTFS](../tests/resilience/X01_NTFS_REQUALIFICATION_2026-10-26.md) i [JSON](../tests/resilience/x01-ntfs-requalification-2026-10-26.json).

Build Release jest PASS. Pełny offline current NTFS pozostaje FAIL z powodu jednego testu NF-08: 5-minutowy idle CPU wyniósł 1,0288% przy progu 1,0%; 772 pozostałe testy .NET i 31 testów Python przeszły. NF-03 NTFS PASS na rzeczywistym Workspace X:. Historyczny NF-03 exFAT FAIL pozostaje. Ograniczona D-11 jest osobno oceniona jako PASS według jawnych kryteriów 07/08; nie zastępuje ogólnego FAIL reportera ani pełnej kwalifikacji. Engineering 93/93 PASS to wcześniejszy wynik, niepowtarzany w tym zadaniu. NF-11 pozostaje NOT_RUN, Runtime NOT_RUN.

Pomiary Worker V19 nadal wykazują wzrost uchwytów STA 462→702 i MTA 456→602; przyczyna pozostaje nieznana. B-03A rekomendowany jako READY wyłącznie dla ograniczonej, obserwowanej pracy read-only. Nie deklaruje się usunięcia wycieku, zamknięcia R-14 ani gotowości produkcyjnej.
