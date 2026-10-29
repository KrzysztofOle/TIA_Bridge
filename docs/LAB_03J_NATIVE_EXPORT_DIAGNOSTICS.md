# LAB-03J — diagnostyka natywnego eksportu Siemens

Wykonanie: 2026-10-28–29; raport uzupełniony 2026-10-29. Repo `067223c` z niezatwierdzonymi zmianami [LAB-03I](LAB_03I_GLOBAL_DEFINITION_NAMING.md), zachowanymi bez commita/push. Narzędzie: `X:\TESTY_TIA_Bridge\bin\LabExporter.exe`. Odbiorca testów: wyłącznie `X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy\T05_RecipeSim_V19_Testowy.ap19`, PID 39136 / start UTC `2026-10-28T08:31:38.0759006Z`, HMI `HMI_JSON_TEST / HMI_RT_1`.

## Przyczyna historycznego błędu: nieustalona

Historyczny warunek `NATIVE_FILE_COUNT_OR_LAYOUT_INVALID:JSON_Test_screen` łączył trzy sytuacje: `returned.Length == 0`, `returned.Length > 32` i obecność podkatalogu po `HmiScriptModule.Export(DirectoryInfo)`. Raport LAB-03I nie zawierał tych wartości, a ówczesna obsługa błędu usuwała surowy katalog. Nie ma podstaw do wybrania jednej z tych przyczyn na podstawie historycznych danych.

W LAB-03J pełny eksport udał się dwukrotnie przed testem importu, również z bezpośrednią kontrolą podkatalogów natychmiast po powrocie API. `JSON_Test_screen` zwrócił dokładnie dwa pliki: `JSON_Test_screen.hmi.js` (3818 bajtów) i `JSON_Test_screen.hmi.yml` (95 bajtów), bez podkatalogów. Katalog wejściowy API był pusty. Pozostałe moduły zachowywały się tak samo. Nie znaleziono dodatkowych plików technicznych, zależności wyniku od nazwy modułu ani pozostałości poprzedniego eksportu w tych próbach.

Wynik bieżący nie odtwarza historycznego błędu. Nie dowodzi jego naprawy ani trwałej stabilności Siemens. Nie przypisujemy awarii modułowi `JSON_Test_screen`, parserowi, nowej nazwie globalnego pliku, timeoutowi autoryzacji ani hipotetycznej strukturze podkatalogów. Porównanie kodu LAB-03I z zachowaną wersją sprzed tego etapu potwierdza, że wywołanie natywnego eksportu i historyczny warunek nie były zmieniane przez LAB-03I.

## Zmiany o potwierdzonym zakresie

Wspólna funkcja obserwacji otacza natywne wywołania eksportu w FORCE EXPORT, odczycie przed importem i readbacku. Zapisuje osobny JSON `evidence\LAB-03J-native-<UUID>.json` także przy odmowie. Raport zawiera moduł, dokładną ścieżkę przekazaną do API, stan przed i po wywołaniu, liczbę/nazwy/rozszerzenia/rozmiary plików, strukturę podkatalogów bez podążania za reparse points, ścieżki zwrócone przez API, oczekiwaną parę, konkretną przyczynę odmowy, typ wyjątku, HRESULT i dostępny Win32 error. Nie zawiera kodu JS ani zawartości YAML. Liczba wpisów diagnostycznych jest ograniczona.

Przed wywołaniem wymagany jest pusty katalog; stare dane blokują wywołanie, bez automatycznego czyszczenia. Po nim wymagane są dokładnie właściwe dwa pliki w katalogu głównym, zgodność zbioru zwróconego przez API z rzeczywistą zawartością, brak duplikatów, dodatkowych plików, podkatalogów i reparse points oraz limit rozmiaru pliku. To usuwa wykazane offline luki starej kontroli, która sprawdzała tylko ogólną liczbę/położenie i obecność oczekiwanej pary. Nie jest deklarowane jako naprawa nieodtworzonego błędu LAB-03I. Surowy katalog nie jest już kasowany po awarii etapu natywnego.

Nie wprowadzono retry eksportu, opóźnienia mającego zamaskować błąd, fallbacku do historycznych plików ani wyszukiwania dowolnej pasującej pary w podkatalogach. Niejednoznaczność pozostaje odmową.

Nazewnictwo i manifesty LAB-03I, `GLOBAL_PREFIX`, marker `***End of Global definition area***`, bajtowe segmenty, SHA-256, `.native` oraz mechanizm kopiowania/publikacji/rollbacku LAB-03H pozostają zachowane. Splitter nie wymagał zmian.

## Zabezpieczenie laboratoryjnego testu importu

Aktualny assembler dołącza do wewnętrznego manifestu przygotowanego wejścia hashe eksportowanego baseline JS/YAML. Po Attach i kontroli nazw importer bierze `ExclusiveAccess`, ponownie sprawdza tożsamość/czysty projekt i zestaw modułów oraz eksportuje wszystkie moduły do nowego katalogu. Drift któregokolwiek hasha blokuje Import.

Przed pierwszą mutacją wywoływany jest natywny `Project.Archive` z jawną nazwą `.zap19`. Archiwum musi być odczytywalnym ZIP, zawierać dokładnie właściwy AP19 i przejść limity ścieżek/liczby wpisów/rozmiaru; wszystkie pliki są odczytywane i hashowane, a wynik wiązany z PID/start/projektem i hashem manifestu wejścia. Błąd backupu blokuje Import. Następnie importer ponownie sprawdza hashe frozen input, zapisuje intent i korzysta z dotychczasowych Import/Save/readback. Błąd po rozpoczęciu importu raportuje `RECONCILIATION_REQUIRED`; brak automatycznego reimportu lub rollbacku TIA. Historyczne przygotowane wejścia bez baseline wymagają ponownego przygotowania z aktualnego FORCE EXPORT.

To ograniczone zabezpieczenie samodzielnego narzędzia laboratoryjnego. Nie implementuje produkcyjnego schedulera, capability, kwarantanny w Host ani pełnej sagi `SYNCED`. Compile, download, Runtime i PLC write są poza zadaniem i nie były uruchamiane. Backup jest nowym zabezpieczeniem laboratoryjnym, bez zmiany architektury produkcyjnej.

## Testy offline

| Zestaw | Wynik i dowód |
|---|---|
| LAB-03J | 15/15 PASS; `evidence\LAB-03J-offline-b013e60c85424fee833f13dd1bb4dda3\results.json` |
| LAB-03I | 18/18 PASS; `evidence\LAB-03I-offline-becbcf19f0994479b4a55b9a687c9832\results.json` |
| LAB-03H | 10/10 PASS; `evidence\LAB-03H-offline-tests-30519a5ec876423ba9770803b1d224b8.json` |

Ścieżki dowodów są względem `X:\TESTY_TIA_Bridge`. [Test LAB-03J](../scripts/test_lab03j.ps1) używa tego samego wzorca refleksji i raportowania co istniejące testy, bez połączenia z Siemens. Obejmuje poprawną parę, brak JS/YAML, dodatkowy plik, podkatalog, pozostałości przed wywołaniem producenta, błędne nazwy modułu, pusty wynik, niejednoznaczne pary, niezgodne/zdublowane zwrócone ścieżki, kompletność diagnostyki bez treści kodu i Win32 32. Dodatkowe trzy przypadki sprawdzają poprawny backup, błędny AP19 i uszkodzone archiwum. Oczekiwania istniejących 18 i 10 testów nie zostały zmienione.

## Przebieg rzeczywisty i ograniczenia dowodów

Wszystkie poniższe pliki są w `evidence\LAB-03J-live-11ef10b4f9ec4cb2a567bd0bf75aabfc`.

- `diagnostic-export-console.txt`: pierwsza próba zakończona FAIL na Attach po około 234 s, `Security error / The operation has timed out`, przed eksportem. Proces eksportera zakończył się; nie zabijano TIA. Nie jest to błąd parsera.
- `diagnostic-2-export-console.txt`: świeża próba po obsłudze natywnej autoryzacji; PASS 6 modułów / 22 funkcje / 47 plików, do nowego `diagnostic-2-Scripts`.
- `final-export-console.txt`: PASS 6/22/47, publikacja do laboratoryjnego `Scripts`. Poprzedni katalog zachowano w `Scripts-before`. Sześć nowych plików globalnych, zero starej nazwy; manifesty, rekonstrukcja i SHA-256 zweryfikowane przez istniejący pipeline.
- `testB-plan.json`: plan jednego komentarza wewnątrz `T05MoveTest.RunTest`, osobna paczka `import-Scripts`; hashami potwierdzono zmianę dokładnie jednego pliku. Pozostałe SOURCE i NATIVE pozostają bez zmian.
- `force-import-console.txt`: pierwsza próba testu B zakończona FAIL przed Import/Save. Siemens utworzył poprawne archiwum pod literalnym `targetName`, bez automatycznego rozszerzenia; wyszukiwanie `*.zap19` odmówiło użycia backupu. Archiwum zachowano; sprawdzono obecność właściwego AP19. Następnie jawnie dodano `.zap19` do nazwy przekazywanej do API. To błąd nowo dodanego zabezpieczenia backupu, niezależny od historycznego eksportu.

`force-import-2-console.txt` zakończył się później FAIL na Attach: `EngineeringSecurityException / The operation has timed out`, po około 23101 s oczekiwania. Potwierdzono `import_started=false`, `outcome=NOT_STARTED` i zakończenie procesu przed świeżą próbą z 2026-10-29. Nie powtarzano mutacji o nieznanym wyniku.

Po potwierdzeniu operatora wznowiono pracę w nowym katalogu `evidence\LAB-03J-resume-f91c1346e2b04ef582e8d4fc70521b56`. `force-import-console.txt` / `force-import-exit.txt` dokumentują **PASS**, kod 0: świeży Attach, zgodny baseline wszystkich sześciu modułów pod ExclusiveAccess, poprawny backup, Import=true, Save return, `IsModified=False` i niezależny eksport/readback wszystkich par. Pełny raport Siemens: `exports\LAB-03D-readback-85d2dc67f66d41c1bdb951cdc8659b8b\force-import-readback.json`.

Backup przed mutacją: `evidence\LAB-03J-backup-6b5a207d32a5428ebaf3673f030e87a6\T05_RecipeSim_V19_Testowy.zap19`, 287055778 bajtów, SHA-256 `a33d2d2f2607721216d52fca4bd08a876c929d98f8aa8ad9a259968eabae77f5`. `backup-receipt.json` wiąże projekt, PID/start i wejście; 121 plików archiwum odczytano i zahashowano. Nie wykonywano restore ani kwalifikacji odtwarzania całego projektu.

Następny FORCE EXPORT finalnym buildem ponownie zakończył się **PASS 6/22/47**, publikując aktualny stan do laboratoryjnego `Scripts`. Dowód: `post-import-export-console.txt` i `post-import-export-exit.txt` w katalogu wznowienia oraz `evidence\LAB-03F-force-export-6e6f11e63fd540afadf82fe9f55dec87.json`. Dotychczasowe `Scripts` zachowano w `Scripts-before-final-export`.

`final-byte-verification.json` z katalogu wznowienia potwierdza bezpośrednie porównania bajtów i SHA-256: dokładnie jeden zmieniony plik SOURCE (`T05MoveTest/RunTest.js`), zgodny z hashem zaplanowanego komentarza; 21 pozostałych funkcji, 6 globalnych segmentów i 6 YAML bez zmian. Wszystkie 12 opublikowanych artefaktów natywnych jest równych readbackowi. Rekonstrukcja każdego modułu jest identyczna bajtowo z native JS, manifesty obu poziomów zgodne, sześć nowych nazw i zero starej nazwy.

Końcowy stan: projekt laboratoryjny zapisany, `IsModified=False` w readbacku, jeden pozostawiony odwracalny komentarz testowy wewnątrz `T05MoveTest.RunTest`; laboratoryjne `Scripts` odzwierciedlają ten stan. Brak działającego procesu `LabExporter.exe` po zakończeniu. T05_PLC bez zmian.

```text
TASK: LAB-03J NATIVE EXPORT DIAGNOSTICS
ROOT CAUSE: NOT_ESTABLISHED; historical failure not reproduced, raw evidence missing
FIX: precise diagnostics, retained failed native output, strict exact-pair validation;
     verified backup and preconditions for the controlled lab import;
     no confirmed fix of the historical LAB-03I failure
NATIVE EXPORT VALIDATION: PASS
OFFLINE TESTS: PASS (15/15)
LAB-03I REGRESSION: PASS (18/18)
LAB-03H REGRESSION: PASS (10/10)
LIVE FORCE EXPORT: PASS
LIVE FORCE IMPORT: PASS
READBACK: PASS
MODULES: 6
FUNCTIONS: 22
FILES: 47 (published Scripts; readback evidence has 19 files)
READY FOR T05_PLC: NO
```

Zbiorczy raport z hashami plików i dowodów: `X:\TESTY_TIA_Bridge\evidence\LAB-03J-final-report.json`. Bieżące sukcesy nie zmieniają historycznych FAIL. Gotowość NO wynika z nieustalonej przyczyny historycznej, nie z braku wykonania testu B. Testy funkcjonalne są zakończone, natomiast kryterium ustalenia/naprawy tej przyczyny pozostaje otwarte.

Autoryzację Siemens/UAC obsługiwał operator. Umiejętność `computer-use` służyła wyłącznie wykrywaniu/wyświetlaniu właściwego okna. Agent nie wpisywał danych administratora, nie zatwierdzał uprawnień i nie zmieniał konfiguracji bezpieczeństwa.

## Pliki i gotowość

Zmiany LAB-03J: laboratoryjne `src\LabExporter.cs`, `bin\LabExporter.exe`, `README.md`; repozytoryjne `scripts\test_lab03j.ps1`, `scripts\README.md`, ten raport, README i `Zmiany.md`. Dowody, archiwa i fixtures są poza Git. `C:\RMA\t05_plc`, kod biznesowy poza jednym zaplanowanym komentarzem testowym, PLC i konfiguracja urządzeń pozostają poza zmianą. Brak commita/push.

Gotowość do T05_PLC: **NO** do czasu rozstrzygnięcia historycznej przyczyny. Bieżący sukces laboratoryjny nie oznacza potwierdzonej naprawy nieodtworzonej awarii. Wymaganie LAB-03J dotyczące ustalenia i usunięcia konkretnej przyczyny pozostaje niezamknięte; nie raportujemy całego zadania jako bezwarunkowego PASS.
