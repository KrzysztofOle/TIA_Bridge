# LabExporter — PLC Program blocks

Status 2026-11-04: rozszerzenie wyłącznie niezależnego laboratorium `X:\TESTY_TIA_Bridge`; brak zmian Core/Host/Worker, capabilities, produkcyjnego Apply lub definicji SYNCED. Kontrakt CLI, algorytm, limity i przykłady: `X:\TESTY_TIA_Bridge\PLC_BLOCKS.md`. Raport: `X:\TESTY_TIA_Bridge\PLC_BLOCKS_REPORT.md`.

## Zakres dostępny

`plc-blocks-sync` ma kierunki `tia-to-repo`, `compare`, `repo-to-tia`, wybór nazw lub `*` i `--dry-run`. Publiczne `PlcBlock.Export` zapisuje niezmieniony SimaticML dla OB/FC/FB/GlobalDB/InstanceDB, z rekursją folderów. Manifest zawiera tożsamość, lokalizację, numer, język i SHA-256; porównanie chroni lokalne edycje, wykazuje zmiany, przeniesienia i zniknięcia względem baseline. Brak automatycznego usuwania bloków. Nazwy komponentów plików to odwracalne hex UTF-8; oryginalne nazwy pozostają w XML/manifeście.

Profil `v19-native-blocks/1-created-only` zachowuje whitespace i unknown fields; poza regułami składni XML ignoruje jedynie tekst bezatrybutowego, bez dzieci `/Document/DocumentInfo/Created`. ID, interfejsy, kod, namespace i pozostałe daty pozostają znaczące. Żaden przekształcony XML nie zastępuje oryginalnych bajtów. Diff jest pełnym tekstowym zastąpieniem XML, nie minimalnym diffem programu/AST.

Świeża tożsamość projektu/procesu/PLC, czysty projekt, mutex sesji, ExclusiveAccess, snapshot katalogu i niezależny powtórny eksport poprzedzają publikację. Staging i zachowany poprzedni katalog chronią pliki repo; nie stanowią backupu TIA. Przy tym samym modelu zachowuje się stare bajty/czasy eksportu; `NO_OP` nie publikuje plików. Manifest nie jest podpisanym produkcyjnym baseline/verification receipt. Po awarii publikacji wymagany jest przegląd staging/backup i publication.json; nie ma kwalifikacji power-loss ani automatycznego recovery.

## Import jest zablokowany

`repo-to-tia` przy zgodności zwraca NO_OP. Każda zmiana, również dry-run i zatwierdzony SHA-256, kończy się `PLC_IMPORT_V19_NOT_QUALIFIED`, exit 2, mutation_started=false. Nowe polecenie nie ma aktywnej sagi import/backup/Compile/Save ani pełnej walidacji zależności. Otworzenie bramki wymaga osobnego zadania i zgody na mutację Engineering oraz kwalifikacji zachowania numerów, metadanych, interfejsów i zależności per rodzaj. Instancyjny DB zależy od FB i nie jest traktowany jak globalny DB. Nie wolno wywnioskować kwalifikacji importu z eksportu, obecności API ani testów offline. Zasady [synchronizacji](04_SYNCHRONIZATION.md) i [ADR 0002](decisions/0002-async-host-and-versioned-worker.md) pozostają bez zmian.

## Dowody

Na dokładnym `C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19`, PLC `T05_PLC_SIM`, odczytano 11/11 bloków wymienionych w zadaniu: 1 OB LAD, 7 FC SCL, 3 globalne DB. Zapisano 11 oryginalnych XML i `.plc-blocks-sync.json` w zadanym `Program blocks`. Powtórny eksport, dwa porównania i repo-to-tia NO_OP potwierdziły zgodność; niezależny readback PASS, IsModified=False. Cztery odczyty finalnego builda zachowały SHA-256 i czasy plików repo; dwa compare mają identyczny hash planu. Dowód zbiorczy: `X:\TESTY_TIA_Bridge\evidence\PLC-initial-20261104\final-engineering.json`.

Engineering PASS dotyczy wyłącznie eksportu/porównania tego inwentarza. FB, IDB i zagnieżdżone foldery: OFFLINE TESTED, Engineering NOT_RUN na tym projekcie, bo takich obiektów nie zawierał zakres. Import/backup projektu/Compile/Save/Runtime/Download NOT_RUN. Nie użyto Computer Use ani nie zmieniono projektu TIA.

Offline: PLC 48/48 + CLI 5/5, UDT 120/120 + CLI 6/6, HMI LAB-03P 44/44 + CLI 6/6, LAB-03Q 51/51 + CLI/workspace/reparse 8/8, LAB-03I 18/18, LAB-03K 42/42 PASS. Testy frameworkowe uruchomiono w Windows PowerShell 5.1; nieudany wcześniejszy LAB-03I w PowerShell 7 zachowano jako błąd środowiska runnera, a następnie powtórzono w wymaganym środowisku. Szczegółowe ścieżki raportów i hash finalnego EXE zawiera raport przy laboratorium.

Build: `X:\TESTY_TIA_Bridge\scripts\build_udt.ps1` (opcjonalne `-CandidateOnly`); test PLC: `scripts\test_plc_blocks.ps1` pod tym samym katalogiem. [Dotychczasowy builder laboratoryjny](../scripts/build_lab_events.ps1) otrzymał oba moduły PLC i referencję System.Xml.Linq, aby nie pomijał udt-sync/plc-blocks-sync. Nie wykonano commit/push ani instalacji narzędzia poza żądanym katalogiem bin.
