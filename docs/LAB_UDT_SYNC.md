# LabExporter `udt-sync` — workflow PLC UDT

**Aktualizacja 2026-11-03: repo-to-tia ze zmianami = BLOCKED.**
Przed Archive/CreateFromFile/GenerateBlocksFromSource walidowane są jawne
adnotacje `S7_SetPoint` (XML `SetPoint`), `ExternalAccessible`,
`ExternalVisible`, `ExternalWritable` względem świeżego natywnego XML.
WithDefaults nie dowodzi jawnego ustawienia wartości, a brak adnotacji nie
oznacza zachowania metadanych. Także kompletne adnotacje nie otwierają zapisu:
gwarancja generatora V19 pozostaje niezakwalifikowana. Zmiany blokowane są
również w dry-run (raport BLOCKED, exit 2, mutation_started=false); plan jest
zachowany. Zatwierdzenie planu nie omija bramki. NO_OP, Compare i eksport
zachowują dotychczasową semantykę. Poniższa sekwencja importu pozostaje
wymaganiem na przyszłość. Szczegóły: `X:\TESTY_TIA_Bridge\UDT_METADATA_REPORT.md`.
Brak operacji TIA w tej poprawce.

Status: laboratoryjne narzędzie odczytu/eksportu i warunkowego importu UDT dla
TIA Portal V19. Implementacja znajduje się poza repozytorium TIA_Bridge, w
`X:\TESTY_TIA_Bridge\src\LabUdtSync.cs`, i jest uruchamiana przez
`X:\TESTY_TIA_Bridge\bin\LabExporter.exe`. Nie jest to capability Host/Worker
TIA_Bridge ani produkcyjnego adaptera Apply. TIA_Bridge dokumentuje ten
workflow jako odrębne narzędzie laboratoryjne.

Szczegółowy, normatywny kontrakt CLI, ograniczenia parsera i zakres kwalifikacji
znajdują się w `X:\TESTY_TIA_Bridge\PLC_DATA_TYPES.md` oraz w
`X:\TESTY_TIA_Bridge\UDT_SYNC_REPORT.md`. Źródło
kontraktu jest przy LabExporter; ten dokument opisuje kolejność pracy, kontrolę
Git i sposób interpretacji. Nie uruchamia operacji TIA.

## Zakres i wymagania wspólne

Polecenie ma postać:

```powershell
$exe = 'X:\TESTY_TIA_Bridge\bin\LabExporter.exe'
$project = 'C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19'
$plc = 'T05_PLC_SIM'
$source = 'C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\PLC data types'
$types = '*'
```

Każdy kierunek wymaga `--direction`, `--project`, `--plc`, `--source` i
`--types`. `--project` wskazuje dokładny plik `.ap19`; `--plc` musi rozwiązać
się do jednoznacznego PlcSoftware (nazwa albo dokładna ścieżka z raportu);
`--source` jest katalogiem pakietu UDT; `--types` wybiera jedną nazwę, listę
rozdzieloną przecinkami albo `*`. TIA Portal V19 musi mieć otwarty dokładnie
ten projekt w jednym procesie bieżącej sesji użytkownika. Siemens może wymagać
zatwierdzenia dostępu. Narzędzie ponownie sprawdza tożsamość projektu, PLC,
czystość projektu, snapshot i blokadę przed działaniem.

Wyniki wraz z `report.json` i `plan.json` są zapisywane jako odrębne dowody pod
`X:\TESTY_TIA_Bridge\evidence\UDT-<UTC>-<UUID>\`. Zachowuj te dowody; nie
wnioskuj o powodzeniu wyłącznie z komunikatu konsoli lub kodu wyjścia.

## Git i wybór źródła

Przed eksportem do katalogu śledzonego przez Git sprawdź `git status`, diff,
pliki nieśledzone oraz bieżącą zawartość dokładnego katalogu docelowego. Ustal,
czy każdy plik objęty planem jest nowy, zgodny z baseline czy lokalnie zmieniony.
Nie eksportuj nad lokalne różnice bez przeglądu planu. `--overwrite-local` jest
wyłącznie dla `tia-to-repo`, wymaga najpierw `--dry-run`, jawnej decyzji o
zastąpieniu, sprawdzenia `plan.json`/`report.json` i ponownego użycia świeżego
hasha z `--approve-plan`; zmieniony native sidecar nadal blokuje publikację.
Po publikacji sprawdź diff i hashe wszystkich śledzonych oraz nieśledzonych
plików. Nie zatwierdzaj ani nie publikuj Git automatycznie.

Przed `repo-to-tia` sprawdź branch/HEAD, status i pełny diff źródeł, a także
tożsamość projektu/PLC i plan zależności. Nie używaj Git HEAD jako dowodu stanu
TIA ani eksportu jako dowodu zapisu do TIA. Plan zatwierdza konkretny operator
po przeglądzie; jego hash nie jest samodzielnym uprawnieniem do importu.

## Standardowy cykl

### EXPORT — TIA → repo

```powershell
& $exe udt-sync --direction tia-to-repo --project $project --plc $plc --source $source --types $types
```

Odczytuje UDT z Engineering i publikuje `.udt`, pełny natywny XML w
`.udt-native\` oraz manifest `.udt-sync.json`. Eksport nie wykonuje Compile,
Save ani importu. Sprawdź `status`, wybrane typy, identyfikację, plan publikacji,
hash bajtów, odczyt natywny oraz `is_modified_before/after`. `PASS_EXPORT` i
niezależny readback dowodzą wyniku eksportu/odczytu w wskazanym zakresie; nie
ustanawiają automatycznie Git baseline ani synchronizacji repo→TIA.

Jeśli plan wskazuje lokalne różnice lub kolizje, zatrzymaj się. Najpierw użyj
trybu odczytowego `tia-to-repo --dry-run`, rozstrzygnij konkretny zakres i
dopiero wtedy zastosuj wymagane `--approve-plan HASH --overwrite-local`.

### COMPARE — TIA ↔ repo

```powershell
& $exe udt-sync --direction compare --project $project --plc $plc --source $source --types $types
```

Porównuje bieżące źródła repo z odczytem TIA i tworzy plan/raport bez
publikacji plików ani mutacji projektu. `compare` może zakończyć się kodem 0,
choć wykryto różnice: odczytaj per-typ status/operację i plan. Rozróżniaj
`NO_OP`, `DIFFERENT`, brak lokalnego źródła i nieobsługiwany/niejednoznaczny
zakres. Porównanie parsera nie jest pełnym porównaniem XML, wszystkich
właściwości Siemens ani fizycznego layoutu DB.

### DRY RUN — repo → TIA

```powershell
& $exe udt-sync --direction repo-to-tia --project $project --plc $plc --source $source --types $types --dry-run
```

Wymaga świeżego odczytu TIA i istniejących plików `.udt`. Nie importuje,
nie kompiluje i nie zapisuje projektu. Sprawdź plan, zakres, kolejność
zależności, statusy `NO_OP`/`CREATE_TIA`/`DIFFERENT`, zależne bloki,
`structural_impact`, coverage, nierozwiązane typy i `report.json`. `UNKNOWN`,
niepełne pokrycie, zmiana projektu lub niepewny wynik blokują dalsze działanie.
Zapisz hash planu dopiero po zatwierdzeniu przez uprawnioną osobę dokładnie
pokazanego zakresu i skutków strukturalnych.

### IMPORT — repo → TIA

Import jest osobną, mutującą operacją. Wolno go rozważać dopiero po kontroli
Git, przeglądzie świeżego dry-run oraz jawnej zgodzie na wyświetlony zakres,
backup i możliwe skutki strukturalne. Polecenie używa dokładnego hasha z
zaakceptowanego planu:

```powershell
& $exe udt-sync --direction repo-to-tia --project $project --plc $plc --source $source --types $types --approve-plan <REVIEWED_SHA256> --accept-structural-impact
```

`--approve-plan` wiąże import z projektem, PLC, źródłami, baseline, inventory
i snapshotem; każdy drift unieważnia plan. `--accept-structural-impact`
potwierdza wyłącznie zapoznanie się z potencjalną zmianą struktury — nie
autoryzuje operacji. Przed pierwszą zmianą wymagany jest jeden zweryfikowany
backup projektu. Implementacja wykonuje zależności w kolejności, Compile
dokładnego PLC (zero błędów i niezaakceptowanych ostrzeżeń), kontrolę
zachowania danych/metadata w objętym zakresem, Save z `IsModified=False` oraz
niezależny readback typów i bloków. Brak backupu, dirty project, stale plan,
konflikt lub nieznane wymagane coverage blokują zapis.

Rozmiary DB pozostają `UNKNOWN`; import nie kwalifikuje zachowania danych
online ani bezpieczeństwa późniejszego downloadu. Narzędzie nie wykonuje
downloadu, PLC write ani testu Runtime. Zmiana UDT może wymagać osobnej oceny
reinitializacji przed jakimkolwiek przyszłym downloadem.

## Błąd lub wynik niepewny

Kod 0 oznacza zakończenie operacji raportowane przez narzędzie, nie zawsze brak
różnic. Kod 2 oznacza odmowę przed mutacją lub błąd argumentów. Kod 3 oznacza
błąd po rozpoczęciu mutacji. Po kodzie 3, timeout, utracie odpowiedzi lub
raporcie z niepewnym stanem: zatrzymaj dalsze zapisy, zachowaj backup, evidence,
plan i logi, a następnie przeprowadź osobne uzgodnienie odczytowe. Nie ponawiaj
importu, Compile, Save ani rollbacku i nie usuwaj kwarantanny na podstawie
samego zakończenia procesu. `RECONCILIATION_REQUIRED` wymaga wyjaśnienia
bieżącego TIA i odrębnej decyzji/planu przed kolejnym zapisem. Compare i
dozwolone odczyty mogą służyć do zebrania dowodów, zgodnie z raportem.

Eksport i import UDT współdzielą blokadę laboratoryjną z `export-events`;
nie uruchamiaj ich równolegle. Starsze FORCE nie używają tej blokady.

## Bieżący przykład RecipeSim

Repozytoryjny pakiet UDT projektu testowego znajduje się w
`C:\RMA\t05_plc\siemens\T05_RecipeSim_V19\T05_PLC_SIM\PLC data types`.
Mapę historycznych lokalizacji, stan źródeł Git i dowody eksportu opisuje
`C:\RMA\t05_plc\siemens\TIA_Source_Map.md`.
