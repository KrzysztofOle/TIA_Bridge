# LAB-03P — wymuszenie importu i cykl import/compile/export

2026-10-31. Zakres: samodzielne laboratoryjne `X:\TESTY_TIA_Bridge\src\LabExporter.cs`, nowy `LabForceSync.cs` i zbudowany `bin\LabExporter.exe`. Implementacja oraz testy offline. **Engineering/Runtime: NOT_RUN** dla nowej ścieżki. Brak operacji na projektach TIA, PLC lub folderze T05 podczas implementacji. Nie jest to produkcyjny adapter Apply ani potwierdzenie `SYNCED` z [04](04_SYNCHRONIZATION.md).

## Przyczyna odmowy

`TIA_BASELINE_DRIFT_BEFORE_IMPORT:RecipeFileApiTest` oznacza różnicę pomiędzy świeżym eksportem TIA a historycznymi hashami `.native` w folderze wejścia. [LAB-03J](LAB_03J_NATIVE_EXPORT_DIAGNOSTICS.md) dodał tę kontrolę. Sam `force-import` zapisuje i weryfikuje projekt, ale nie aktualizuje wejściowego folderu. Dlatego po pierwszym udanym imporcie kolejny import z tego folderu może trafić na starszy baseline. Ręczna kompilacja nie aktualizuje manifestów w folderze. Nie ustalono z podanego logu, który konkretny plik lub bajt modułu się różnił. Log użytkownika potwierdza `import_started=false`, `backup_directory=null`, `outcome=NOT_STARTED` dla tej próby.

## Polecenia

Jawne zastąpienie obecnych modułów TIA zawartością edytowalnych plików folderu:

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-import `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\T05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts" `
  --overwrite-tia
```

Cykl import → kompilacja HMI → Save → niezależny eksport/readback → publikacja odświeżonego folderu wejścia:

```powershell
& "X:\TESTY_TIA_Bridge\bin\LabExporter.exe" force-sync `
  --project "C:\RMA\TIA_TEST\T05_RecipeSim_V19" `
  --input "C:\RMA\T05_plc\siemens\T05_RecipeSim_V19\HMI_JSON_TEST\HMI_RT_1\Scripts"
```

Oba polecenia obsługują `--verbose`. `force-sync` sam oznacza zgodę wywołującego na zastąpienie zastanego kodu TIA wejściem; nie potrzebuje `--overwrite-tia`. Zwykły `force-import` bez tej flagi zachowuje odmowę historycznego driftu. Flaga nie jest obsługiwana w starej składni pozycyjnej ani przy `force-export`. Brakujące, powtórzone i nieznane opcje są odrzucane. Uruchomienie poprawnego polecenia jest rzeczywistą operacją Engineering; przygotowanie kodu lub dokumentacji nie uruchamia jej automatycznie.

## Granice i kolejność

- Nie ma tworzenia/usuwania modułów: zestaw nazw musi być identyczny w TIA i paczce. Pozostają walidacja pełnej paczki, natywnych hashy i YAML, markerów, układu `.native`, limitów i ścieżek. Edytowalne `.js` dostarczają kod importu; native JS pozostaje historycznym artefaktem.
- Projekt musi być zapisany przed startem (`IsModified=False`), dokładny AP19/PID/start i HMI `HMI_JSON_TEST/HMI_RT_1` muszą pasować. Jeden Attach, ręczna autoryzacja Siemens, jeden `ExclusiveAccess`, bez automatyzacji GUI. Nie ma kolejnego Attach pomiędzy importem a kompilacją/readbackiem.
- Jawne wymuszenie dotyczy **historycznego** baseline. Wszystkie moduły są świeżo eksportowane pod ExclusiveAccess, różnice z historią mają raportowane hashe, a stan sprzed zastąpienia pozostaje w `evidence\LAB-03J-pre-import-*`. Natywny backup `.zap19` nadal musi przejść weryfikację integralności i właściwego AP19. Receipt i intent zawierają tryb wymuszenia oraz różnice. Błąd backupu blokuje import.
- Po backupie następuje ponowny eksport wszystkich modułów i porównanie ze świeżym stanem z tej operacji. Zmiana JS lub YAML daje `TIA_CHANGED_DURING_IMPORT_PREFLIGHT`, również przy wymuszeniu. Frozen input i czysty projekt są ponownie sprawdzane. Wymuszenie nie wyłącza ochrony zmian zachodzących w trakcie operacji.
- `force-sync` przed importem tworzy i porównuje kopię folderu W; assembler korzysta z tej kopii. Sprawdza zależności splittera i dostępność usługi kompilacji przed pierwszą mutacją. Kompilator rozwiązywany jest ponownie po imporcie. Lookup jest ograniczony do dokładnego HMI, jego przodków i urządzenia zawierającego wyłącznie to jedno software; brak usługi albo inne software blokują operację. Nie kompiluje całego projektu ani innego urządzenia.
- Kompilacja wykorzystuje publiczne `ICompilable.Compile`, rekurencyjnie zapisuje komunikaty w `backup-directory\compile-result.json`, z limitami głębokości/liczby. Polityka akceptuje `Success`/`Information` bez ostrzeżeń oraz dokładnie jedno znane ostrzeżenie `The user "Anonymous" is not supported by WinCC Unified Runtime devices and is not available in runtime.` przy stanie `Warning`, zerze błędów i jednym ostrzeżeniu. To ostrzeżenie jest ignorowane przez `force-sync`; opisuje ograniczenie użytkownika Anonymous w Runtime i pozostaje widoczne w raporcie kompilacji. Każde inne lub dodatkowe ostrzeżenie, błąd, niekompletny raport diagnostyczny albo inny stan nadal zatrzymuje operację przed Save i publikacją. Odczyt readbacku stosuje tę samą politykę. API sprawdzono względem lokalnych assembly/XML V19; ogólny mechanizm i strukturę wyników opisuje [manual Siemens V19](https://support.industry.siemens.com/cs/attachments/109826886/TIAPortalOpenness_enUS_en-US.pdf). Dostępność usługi na rzeczywistym odbiorcy nie była kwalifikowana w tej zmianie.
- Po kompilacji wymagany jest Save z `IsModified=False`, zgodny zestaw modułów i niezależny byte-exact readback każdej pary JS/YAML. Readback jest natywnym eksportem wykonanym już po kompilacji; z niego powstaje wynik splittera. Ponowny osobny `force-export`/Attach nie jest potrzebny.
- Publikacja korzysta z dotychczasowego kopiowania do staging na docelowym woluminie, walidacji SHA-256 i rollbacku **folderu lokalnego**. Dwukrotnie sprawdza zgodność W z kopią: przed przeniesieniem i po przeniesieniu do backupu. Nowa lokalna edycja blokuje zastąpienie i zostaje zachowana. Poprzedni folder pozostaje jako sąsiedni `.Scripts.lab03p-backup-<UUID>`. Nie nadpisuje się ręcznie `.native` lub manifestów.

## Awaria i dowody

Import/compile/save/readback nie są transakcją TIA. Awaria po rozpoczęciu importu daje `RECONCILIATION_REQUIRED`, bez automatycznego rollbacku TIA, ponowienia lub ratunkowego Save. Projekt po nieudanej kompilacji może pozostać zmieniony w pamięci. Najpierw należy uzgodnić stan i diagnostykę. Timeout nie dowodzi zatrzymania Siemens call.

Jeżeli import/compile/save/readback przeszły, ale publikacja folderu się nie powiedzie, raport rozdziela `import_compile_readback=PASS`, `folder_published` i końcowe `FAIL`. Zachowane są readback, frozen input, kopia W i staging; nie należy automatycznie powtarzać importu. Pozostawiony backup folderu pozwala odczytać poprzednie pliki. Awaria publikacji nie cofa TIA.

Raport końcowy: `evidence\LAB-03P-force-sync-<UUID>.json`; odczyt: `exports\LAB-03D-readback-*\force-import-readback.json`; błędy importu: `evidence\LAB-03P-import-failure-*.json`. `force-sync` daje `PASS_IMPORT_COMPILE_EXPORT` dopiero po publikacji. Wyniki pozytywne import/readback są osobnym etapem. Exit 0 = sukces, 2 = FAIL/parametry, 3 = niezgodny zestaw modułów. Żadne polecenie nie wykonuje downloadu, Runtime, symulacji ani PLC write.

## Weryfikacja offline

[Testy LAB-03P](../scripts/test_lab_force_sync.ps1) używają syntetycznych par, paczek i delegatów awarii; nie wywołują Siemens, również kompilacji. Sprawdzają historyczny drift JS/YAML, jawne wymuszenie, świeży drift podczas operacji, akceptację dokładnego ostrzeżenia Anonymous i odmowę innych/dodatkowych ostrzeżeń lub błędów, zatrzymanie sekwencji po awarii import/compile/save, ochronę lokalnych edycji i rollback folderu, dwa kolejne cykle odświeżania baseline, zmieniony readback i wadliwy receipt.

Zależności: Windows PowerShell 5.1 x64, PowerShell 7, systemowy .NET Framework csc, lokalny zbudowany EXE i istniejący `Esprima.dll`; bez nowych pakietów. [Build](../scripts/build_lab_events.ps1) uwzględnia nowy laboratoryjny `LabForceSync.cs` i zachowuje poprzedni EXE. Kod laboratoryjny pozostaje poza repo produkcyjnym; repo zawiera runner i testy, bez vendor DLL, projektów i backupów. Dowody w nowych katalogach UUID pod laboratoryjnymi `evidence`/`exports`; historyczne nieudane próby testów pozostają zachowane.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_force_sync.ps1
```

Wyniki końcowego builda i regresji są opisane w [Zmiany.md](../Zmiany.md). PASS offline nie kwalifikuje rzeczywistego importu/kompilatora V19 ani Runtime; pierwszy live cykl wymaga osobnego wykonania w zatwierdzonym zakresie.
