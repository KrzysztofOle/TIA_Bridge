# LAB-03P — force-sync: NO_OP lub cykl import/compile/export

Utworzono 2026-10-31; aktualizacja dowodów 2026-11-03. Zakres: samodzielne laboratoryjne `X:\TESTY_TIA_Bridge\src\LabExporter.cs`, `LabForceSync.cs` i `bin\LabExporter.exe`. **OFFLINE TESTED** — testy automatyczne; **ENGINEERING VERIFIED** — pełny NO_OP wykonany przez użytkownika 2026-11-03 na laboratoryjnym TIA Portal V19; **RUNTIME NOT_RUN**. Szczegółowy zakres dowodów poniżej. Podczas implementacji nie wykonywano operacji TIA; późniejszy test użytkownika jest osobnym dowodem. Funkcje te nie należą do Host/Worker ani produkcyjnego adaptera Apply i nie potwierdzają `SYNCED` z [04](04_SYNCHRONIZATION.md).

## Historia — przyczyna odmowy

`TIA_BASELINE_DRIFT_BEFORE_IMPORT:RecipeFileApiTest` oznacza różnicę pomiędzy świeżym eksportem TIA a historycznymi hashami `.native` w folderze wejścia. [LAB-03J](LAB_03J_NATIVE_EXPORT_DIAGNOSTICS.md) dodał tę kontrolę. Sam `force-import` zapisuje i weryfikuje projekt, ale nie aktualizuje wejściowego folderu. Dlatego po pierwszym udanym imporcie kolejny import z tego folderu może trafić na starszy baseline. Ręczna kompilacja nie aktualizuje manifestów w folderze. Nie ustalono z podanego logu, który konkretny plik lub bajt modułu się różnił. Log użytkownika potwierdza `import_started=false`, `backup_directory=null`, `outcome=NOT_STARTED` dla tej próby.

## Polecenia

Jawne zastąpienie obecnych modułów TIA zawartością edytowalnych plików folderu:

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-import `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\T05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts" `
  --overwrite-tia
```

Synchronizacja z decyzją po świeżym porównaniu: NO_OP albo import → kompilacja HMI → Save → niezależny eksport/readback; następnie porównanie pakietu i ewentualna publikacja odświeżonego folderu wejścia:

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-sync `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\T05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts"
```

Oba polecenia obsługują `--verbose`. `force-sync` sam oznacza zgodę wywołującego na zastąpienie zastanego kodu TIA wejściem; nie potrzebuje `--overwrite-tia`. Zwykły `force-import` bez tej flagi zachowuje odmowę historycznego driftu. Flaga nie jest obsługiwana w starej składni pozycyjnej ani przy `force-export`. Brakujące, powtórzone i nieznane opcje są odrzucane. Uruchomienie poprawnego polecenia jest rzeczywistą operacją Engineering; przygotowanie kodu lub dokumentacji nie uruchamia jej automatycznie.

## Granice i kolejność

- Domyślnie zestaw nazw musi być identyczny w TIA i paczce. [LAB-03Q](LAB_03Q_CREATE_MODULES.md) dodaje jawne `force-sync --create-modules` dla nowych katalogów źródeł JS i `--validate-only` bez operacji Siemens. Nie ma usuwania modułów. Walidacja pełnej paczki, natywnych hashy i YAML, markerów, układu `.native`, limitów i ścieżek pozostaje obowiązkowa dla UPDATE. Edytowalne `.js` dostarczają kod importu; native JS pozostaje historycznym artefaktem. Generowane wejście CREATE nie jest native baseline.
- `force-sync` sprawdza, czy części `.native/<module>.hmi.js` są dokładnie opisane przez `parts` w obu poziomach manifestu: offsety muszą pokrywać cały artefakt, a hashe historycznych wycinków muszą pasować do bajtów `.native`. Niezależnie weryfikuje hashe/rozmiary YAML i wszystkich artefaktów native oraz zgodność powielonych wpisów root/module. Zmiana edytowalnego `.js` względem poprawnego historycznego native baseline jest dozwolona i trafia do `source_manifest_status=SOURCE_EDITED` wraz z hashami i długościami. To sygnał zmiany źródła, nie automatyczna regeneracja manifestu; uszkodzony baseline lub niespójne manifesty blokują wejście.
- Projekt musi być zapisany przed startem (`IsModified=False`), dokładny AP19/PID/start i HMI `HMI_JSON_TEST/HMI_RT_1` muszą pasować. Jeden Attach, ręczna autoryzacja Siemens, jeden `ExclusiveAccess`, bez automatyzacji GUI. Nie ma kolejnego Attach pomiędzy importem a kompilacją/readbackiem.
- Wstępne komunikaty `Plan TIA: UPDATE <moduł>` i `module_plan[].action=UPDATE` oznaczają istniejące moduły zakwalifikowane do porównania, nie wykonany import. Powstają przed decyzją NO_OP/UPDATED. Rzeczywisty przebieg ustala się z `tia_action`, receipt (`mode`, `import_returned`, `saved`, `compile`) i etapów wykonania; sam plan UPDATE nie dowodzi mutacji.
- Jawne wymuszenie dotyczy **historycznego** baseline. Wszystkie istniejące moduły są świeżo eksportowane pod ExclusiveAccess, różnice z historią mają raportowane hashe, a stan sprzed zastąpienia pozostaje w `evidence\LAB-03J-pre-import-*`. Przy zgodnym zestawie wszystkich przygotowanych par JS/YAML ze świeżym eksportem wykonywany jest pełny `NO_OP`: ponowny eksport potwierdza niezmienność stanu, Import/Compile/Save i backup TIA są pominięte. Zestaw nazw i czystość projektu są ponownie sprawdzane. Przy choć jednej różnicy obecny kontrakt nadal importuje całą kolekcję; nie ma selektywnego importu modułów.
- Jeżeli TIA już odpowiada przygotowanym parom, ale `.native` lub manifesty w repo nie odpowiadają eksportowi, LabExporter pomija mutację TIA, a może opublikować aktualizację repozytorium jako `UPDATED` z `tia_action=NO_OP`, `compile_status=NOT_RUN` i przyczynami różnic. Gdy wszystkie pliki i katalogi są identyczne bajtowo, pomija również publikację.
- Tylko dla rzeczywistej różnicy źródła względem TIA natywny backup `.zap19` nadal musi przejść weryfikację integralności i właściwego AP19. Receipt i intent zawierają tryb wymuszenia oraz różnice. Błąd backupu blokuje import.
- Po backupie następuje ponowny eksport wszystkich modułów i porównanie ze świeżym stanem z tej operacji. Zmiana JS lub YAML daje `TIA_CHANGED_DURING_IMPORT_PREFLIGHT`, również przy wymuszeniu. Frozen input i czysty projekt są ponownie sprawdzane. Wymuszenie nie wyłącza ochrony zmian zachodzących w trakcie operacji.
- `force-sync` przed importem tworzy i porównuje kopię folderu W; assembler korzysta z tej kopii. Sprawdza zależności splittera i dostępność usługi kompilacji przed pierwszą mutacją. Kompilator rozwiązywany jest ponownie po imporcie. Lookup jest ograniczony do dokładnego HMI, jego przodków i urządzenia zawierającego wyłącznie to jedno software; brak usługi albo inne software blokują operację. Nie kompiluje całego projektu ani innego urządzenia.
- Kompilacja wykorzystuje publiczne `ICompilable.Compile`, rekurencyjnie zapisuje komunikaty w `backup-directory\compile-result.json`, z limitami głębokości/liczby. Polityka akceptuje `Success`/`Information` bez ostrzeżeń oraz dokładnie jedno znane ostrzeżenie `The user "Anonymous" is not supported by WinCC Unified Runtime devices and is not available in runtime.` przy stanie `Warning`, zerze błędów i jednym ostrzeżeniu. To ostrzeżenie jest ignorowane przez `force-sync`; opisuje ograniczenie użytkownika Anonymous w Runtime i pozostaje widoczne w raporcie kompilacji. Każde inne lub dodatkowe ostrzeżenie, błąd, niekompletny raport diagnostyczny albo inny stan nadal zatrzymuje operację przed Save i publikacją. Odczyt readbacku stosuje tę samą politykę. API sprawdzono względem lokalnych assembly/XML V19; ogólny mechanizm i strukturę wyników opisuje [manual Siemens V19](https://support.industry.siemens.com/cs/attachments/109826886/TIAPortalOpenness_enUS_en-US.pdf). Dostępność usługi na rzeczywistym odbiorcy nie była kwalifikowana w tej zmianie.
- Po kompilacji wymagany jest Save z `IsModified=False`, zgodny zestaw modułów i niezależny byte-exact readback każdej pary JS/YAML. Readback jest natywnym eksportem wykonanym już po kompilacji; z niego powstaje wynik splittera. Ponowny osobny `force-export`/Attach nie jest potrzebny.
- Publikacja porównuje odtworzony z readbacku pakiet z bieżącym folderem wejścia po zestawie plików/katalogów, długościach i SHA-256; osobno sprawdza zgodność wejścia z początkowym snapshotem. Jeśli nie ma różnicy, nie tworzy stage/backup publikacji i nie zapisuje plików wejścia. Nadal powstają snapshot i nowe artefakty dowodowe poza wejściem. Przy różnicy cały pakiet trafia do walidowanego stagingu, a plikom o identycznych bajtach przywracane są czasy utworzenia i modyfikacji przed zamianą folderu z lokalnym rollbackiem. Publikacja różnicowa nie oznacza zapisu pojedynczych plików w miejscu. Weryfikacja snapshotu obejmuje bajty, zestawy katalogów oraz czasy utworzenia i modyfikacji plików; drift blokuje publikację. Poprzedni folder pozostaje jako sąsiedni `.Scripts.lab03p-backup-<UUID>`. Nie nadpisuje się ręcznie `.native` lub manifestów.

## Awaria i dowody

Import/compile/save/readback nie są transakcją TIA. Awaria po rozpoczęciu importu daje `RECONCILIATION_REQUIRED`, bez automatycznego rollbacku TIA, ponowienia lub ratunkowego Save. Projekt po nieudanej kompilacji może pozostać zmieniony w pamięci. Najpierw należy uzgodnić stan i diagnostykę. Timeout nie dowodzi zatrzymania Siemens call.

Jeżeli import/compile/save/readback przeszły, ale publikacja folderu się nie powiedzie, raport rozdziela `import_compile_readback=PASS`, `folder_published` i końcowe `FAIL`. Zachowane są readback, frozen input, kopia W i staging; nie należy automatycznie powtarzać importu. Pozostawiony backup folderu pozwala odczytać poprzednie pliki. Awaria publikacji nie cofa TIA.

Raport końcowy: `evidence\LAB-03P-force-sync-<UUID>.json`; odczyt: `exports\LAB-03D-readback-*\force-import-readback.json`; błędy importu: `evidence\LAB-03P-import-failure-*.json`. Historyczne `status=PASS_IMPORT_COMPILE_EXPORT` i status konsoli `PASS` pozostają dla kompatybilności. Nowe pola rozdzielają `sync_status=SYNC PASS`, `readback_status=READBACK MATCH`, `roundtrip_status=ROUNDTRIP CLEAN|ROUNDTRIP UPDATED`, `mode=NO_OP|UPDATED`, `tia_action`, `repository_action`, `compile_status=PASS|NOT_RUN`, `reasons` i `updated_files`. Pełny no-op daje trzy statusy PASS/CLEAN przy `compile_status=NOT_RUN`. Exit 0 = sukces, 2 = FAIL/parametry, 3 = niezgodny zestaw modułów. Żadne polecenie nie wykonuje downloadu, Runtime, symulacji ani PLC write.

Statusy należy odczytywać niezależnie:

| Pole | Znaczenie |
|---|---|
| `sync_status` | Końcowy sukces workflow LabExporter; nie jest stanem `SYNCED` Bridge. |
| `readback_status` | Zgodność natywnych par JS/YAML z przygotowanym wejściem. W NO_OP świeży eksport jest ponownie sprawdzany, a receipt powstaje z potwierdzonego eksportu preflight. |
| `roundtrip_status` | `ROUNDTRIP CLEAN` — pakiet repo nie wymaga publikacji; `ROUNDTRIP UPDATED` — opublikowano odświeżony pakiet. Nie określa wykonania importu TIA. |
| `tia_action` / `repository_action` | Osobne efekty po stronie TIA i folderu wejścia: `NO_OP` lub `UPDATED`. Możliwe jest TIA NO_OP przy repo UPDATED. |
| `mode` | Całościowy `NO_OP` tylko przy braku mutacji TIA i publikacji folderu; w pozostałych udanych przypadkach `UPDATED`. |
| `compile_status` | `NOT_RUN` dla TIA NO_OP, `PASS` po zaakceptowanej kompilacji ścieżki mutującej. Historyczna nazwa `PASS_IMPORT_COMPILE_EXPORT` nie dowodzi wykonania Compile. |

`backup_created` w raporcie końcowym dotyczy backupu folderu przy publikacji. Backup projektu TIA wskazuje osobno `import_compile_readback.backup_directory`. Pełny NO_OP nie tworzy żadnego z tych backupów.

## Test laboratoryjny 2026-11-03 — ENGINEERING VERIFIED

Test wykonał użytkownik na TIA Portal V19, projekcie `C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19`, HMI `HMI_JSON_TEST/HMI_RT_1`. Identyfikator przebiegu: `75f9e1dc47854cd29c6c6f65452f0abc`.

- Raport: `X:\TESTY_TIA_Bridge\evidence\LAB-03P-force-sync-75f9e1dc47854cd29c6c6f65452f0abc.json`.
- Readback: `X:\TESTY_TIA_Bridge\exports\LAB-03D-readback-75f9e1dc47854cd29c6c6f65452f0abc\force-import-readback.json`.
- Świeży preflight: `evidence\LAB-03J-pre-import-d03e50c9ee9044e594c20d06242c48b2` pod korzeniem laboratorium.

Potwierdzono 8 modułów, 32 funkcje i 65 plików pakietu. Raport zawiera `mode=NO_OP`, `tia_action=NO_OP`, `repository_action=NO_OP`, `sync_status=SYNC PASS`, `readback_status=READBACK MATCH`, `roundtrip_status=ROUNDTRIP CLEAN`, `compile_status=NOT_RUN`, puste `reasons` i `updated_files`, `backup_created=false`, `folder_published=false`.

Receipt readbacku potwierdza `input_tia_comparison.matches=true`, brak różnic, `matches_source=true` dla wszystkich 8 modułów, `import_returned=false`, `saved=false` oraz `is_modified_before=false` i `is_modified_after=false`. Etap `force-sync-no-op-readback` wskazuje Import/Compile/Save `NOT_RUN`. Wstępny plan zawiera 8 wpisów UPDATE, mimo że import został pominięty.

Przy aktualizacji dokumentacji sprawdzono zgodność receipt osadzonego w raporcie z osobnym readbackiem oraz długości i SHA-256 wszystkich 16 natywnych plików JS/YAML. Zachowany snapshot `evidence\LAB-03P-workspace-75f9e1dc47854cd29c6c6f65452f0abc` i pakiet wynikowy `exports\LAB-03P-package-75f9e1dc47854cd29c6c6f65452f0abc` mają po 65 plików, identyczny zestaw ścieżek i hashe. Potwierdza to wraz z pominiętą publikacją brak zmian plików wejściowego repo po synchronizacji; nie jest oceną bieżącego stanu Git całego repo T05.

**Zakres kwalifikacji:** ENGINEERING VERIFIED dotyczy wyłącznie tego pełnego NO_OP na laboratoryjnym V19. Nie kwalifikuje w tym teście importu przy zmianach, Compile/Save, tworzenia modułów ani publikacji repo-only UPDATED. Te ścieżki zachowują osobne dowody offline. **RUNTIME NOT_RUN** (`runtime_verification=NOT_RUN`); nie wykonano testu Runtime, PLC ani downloadu. Obecna aktualizacja dokumentacji nie uruchamia ponownie synchronizacji.

## Weryfikacja offline — OFFLINE TESTED

[Testy LAB-03P](../scripts/test_lab_force_sync.ps1) używają syntetycznych par, paczek i delegatów awarii; nie wywołują Siemens, również kompilacji. Obejmują poprawną edycję JS przy zachowaniu historycznego baseline, uszkodzone hashe native/manifestów, rozbieżności JS/YAML/zestawu, routing NO_OP bez Import/Compile/Save, pełny no-op bez zmian bajtów i timestampów, repo-only refresh z `compile=NOT_RUN`, publikację różnicową, drift/awarie, dotychczasowe bramki Compile oraz kontrolę CLI.

Zależności: Windows PowerShell 5.1 x64, PowerShell 7, systemowy .NET Framework csc, lokalny zbudowany EXE i istniejący `Esprima.dll`; bez nowych pakietów. [Build](../scripts/build_lab_events.ps1) uwzględnia nowy laboratoryjny `LabForceSync.cs` i zachowuje poprzedni EXE. Kod laboratoryjny pozostaje poza repo produkcyjnym; repo zawiera runner i testy, bez vendor DLL, projektów i backupów. Dowody w nowych katalogach UUID pod laboratoryjnymi `evidence`/`exports`; historyczne nieudane próby testów pozostają zachowane.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_force_sync.ps1
```

Zachowany wynik automatyczny: `evidence\LAB-03P-offline-b032c1f7c5954002a3a24cd699d009cb\results.json` — PASS, 44/44, 0 błędów; `cli-checks.json` w tym samym katalogu — 6/6 PASS. Wyniki builda i pozostałych regresji są opisane w [Zmiany.md](../Zmiany.md). Przy tej aktualizacji odczytano istniejące wyniki, bez ponownego builda lub uruchomienia testów LabExporter. OFFLINE TESTED nie kwalifikuje rzeczywistego importu/kompilatora V19 ani Runtime; osobny dowód Engineering dla NO_OP opisano powyżej.
