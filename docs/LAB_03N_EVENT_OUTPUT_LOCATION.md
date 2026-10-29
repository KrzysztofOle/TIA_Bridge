# LAB-03N — dowolna lokalizacja eksportu zdarzeń HMI

Aktualizacja [LAB-03O](LAB_03O_EVENT_OVERWRITE.md): swoboda lokalizacji pozostaje; domyślny tryb zapisuje bezpośrednio do --output. Opisane niżej niepowtarzalne run-id obowiązują obecnie po jawnym `--mode snapshot`. Poniższe wyniki i przyczyna historycznej blokady pozostają zachowane.

Data: 2026-10-29. Repo `main`, HEAD `62ee9f5ae0b11074e404c86f4a09b6e96e4da99a`, początkowo czyste drzewo. Zmiana obejmuje wyłącznie laboratoryjne `export-events` i jego testy/dokumentację. Bez rzeczywistego eksportu, Siemens Attach, commita lub push.

## Przyczyna i nowy kontrakt katalogu

Dotychczasowy `ValidateEventOutput` współdzielił z JS parser wymagający absolutnej ścieżki z literą dysku. Dodatkowo narzucał fixed/local NTFS, zabraniał relacji z projektem/instalacją/src/bin, segmentu Scripts, korzenia dysku i każdego przodka z plikiem lub katalogiem `.git`. Odmowa `EVENT_OUTPUT_INSIDE_REPOSITORY` była polityką lokalizacji, niezależną od praw zapisu i integralności. Nie była potrzebna do ochrony istniejących eksportów, ponieważ publikacja używa nowego katalogu run-id i zapisu CreateNew.

Nowy niezależny od Siemens `LabEventOutput.Root` korzysta z `Path.GetFullPath`. `--output` jednoznacznie wskazuje **katalog nadrzędny** eksportów. Ścieżki względne rozwiązuje względem katalogu roboczego procesu; ścieżki absolutne, Unicode, UNC i dyski mapowane obsługuje według możliwości .NET Framework/systemu plików. Nie sprawdza Git, kategorii katalogu, typu dysku ani formatu filesystemu. Nie ma whitelisty ani wyjątków dla konkretnych ścieżek. Nie zmieniono wymogu jawnego projektu/HMI ani ich walidacji.

Przykłady argumentu (nie lista dozwolonych miejsc):

```text
--output "C:\RMA\T05_plc\siemens\Events"
--output "D:\Backup\TIA\Events"
--output "\\SERVER\Shared\TIA\Events"
--output ".\Events"
```

Przyszłe uruchomienie eksportera tworzy `<output>/<UTC>-<UUID>`. Powtórzenie polecenia wybiera nowy run-id, nie modyfikuje poprzedniego wyniku. Przykłady nie były uruchamiane przeciw TIA w tym zadaniu.

## Zachowane zabezpieczenia i zakres

- Istniejące reparse points są odrzucane; plik zamiast katalogu jest jawnym błędem. Błędy dostępu nie są omijane. Walidacja rodzica jest ponawiana przed publikacją.
- `RunDirectory` wymaga pojedynczej nazwy run-id; ścieżki artefaktów muszą być względnymi potomkami stagingu, bez traversal, ścieżek absolutnych lub ADS. To chroni wybrany katalog przed przypadkowym wyjściem poza jego zakres.
- Zachowano odmowę `EXPORT_ALREADY_EXISTS`, FileMode.CreateNew, staging `.event-incomplete-UUID` w wybranym rodzicu, marker nieukończenia, limity, długości/SHA-256 i końcowy Directory.Move bez zastępowania celu. Kolizja istniejącego pliku/katalogu nie uruchamia retry ani czyszczenia. Nadal usuwany jest wyłącznie własny marker po publikacji.
- Nie zmieniono statusów i gałęzi exit 0/2/1, events.json, events-index.json, schematów/wersji manifestu, compare-events, JS FORCE EXPORT/IMPORT ani Attach/lifecycle. Błędy są raportowane przez dotychczasowe LabEventConsole.Failure: typ, HResult, bez ujawniania wiadomości Siemens. Własne błędy walidacji mają jawne identyfikatory w wyjątku; istniejący format konsoli pozostał bez zmian.
- Host/API/Worker, produkcyjny storage, projekty TIA, stare eksporty i dane użytkownika pozostają poza zmianą. Brak normalizacji JS/EOL/Unicode.

To nie jest kwalifikacja niezawodności konkretnego serwera SMB ani odporności sieci na utratę zasilania. System plików może odmówić dostępu, Flush lub rename; taki błąd zachowuje dotychczasowy mechanizm nieukończonej publikacji. Walidator nadal nie gwarantuje ochrony przed złośliwym równoległym podmienianiem filesystemu przez inny proces.

Źródła poza Git: `X:\TESTY_TIA_Bridge\src\LabEventVendor.cs` (walidator i złożenie run path) oraz `LabEventExport.cs` (helper ścieżek, ponowne sprawdzenie rodzica i containment zapisu). Poprzednie źródła, EXE i README zachowano w `X:\TESTY_TIA_Bridge\evidence\LAB-03N-before-ff764986a4d647c8af2b8c86b5ec9d6c`. Build aktualizuje wyłącznie laboratoryjny EXE, nie uruchamia go z poprawnymi argumentami live.

Test LAB-03K zmienia wyłącznie oczekiwanie dawnej polityki katalogów. Guard LAB-03M porównuje nadal cały vendor lifecycle z bazą LAB-03L po czterech zmianach prezentacji oraz dwóch dokładnych zmianach LAB-03N: walidator i złożenie run path. Wszystkie pozostałe instrukcje lifecycle i gałęzie return są porównywane bez zmian.

## Testy i dowody offline

Dowody w `X:\TESTY_TIA_Bridge\evidence\`:

| Kontrola | Wynik | Katalog dowodów |
|---|---|---|
| Build net48/x64 | PASS, bez diagnostyk kompilatora | LAB-03K-build-b9c745e09f0d4e50886e9bd15e69f3c2 |
| LAB-03N | 18/18 PASS | LAB-03N-offline-9cb22c15569249cd9775f7a98ee748c3 |
| LAB-03K | 42/42 PASS oraz CLI/launch 2/2 | LAB-03K-offline-f8cd72d315324452a7e30c626c61635b |
| LAB-03L | 16/16 PASS, 9 oryginalnych artefaktów identycznych bajtowo | LAB-03L-offline-45b84275d2cf4246b6a44aecab278759 |
| LAB-03M | 14/14 PASS, 9 plików identycznych, manifest różni tylko tool_sha256, lifecycle PASS | LAB-03M-offline-b83503be3c4b4fb7b71c753c75d71599 |
| compare-events | 44/44 PASS | podsumowanie w LAB-03N offline/verification.json |

Łącznie **134/134 testy PASS**, dodatkowo dwie kontrole CLI/launch. LAB-03N faktycznie publikuje syntetyczne artefakty wewnątrz bieżącego repo, poza repo na X:, przez `\\localhost\X$`, do ścieżki względnej i Unicode. Sprawdza dwa eksporty do jednego rodzica, kolizję run-id z katalogiem i plikiem, rzeczywisty deny ACL bieżącego SID na nowym katalogu NTFS (przywrócony w finally), brakujące katalogi nadrzędne, hashe istniejących plików, `.git` jako plik/katalog, dawne zakazane nazwy, akceptację korzeni bez zapisu, odmowę junction, traversal/ADS i plik zamiast rodzica. Lokalny UNC potwierdza rzeczywisty dostęp SMB; zewnętrzny serwer nie był testowany. Nie tworzono udziałów/mapowań.

Pierwszy przebieg 16/17 PASS zachowano w `LAB-03N-offline-fec5e258b17b4c53b84a84c01d39ed4d`: test błędnie wymagał końcowego separatora korzenia UNC, który .NET usuwa przy kanonizacji. Poprawiono wyłącznie oczekiwanie testu i dodano przypadek junction; kod produkcyjny nie wymagał korekty.

Polecenia reprodukcji z repo:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_output.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_view.ps1 `
  -BaselineSource 'X:\TESTY_TIA_Bridge\evidence\LAB-03L-before-e0d00e4b42694ab3bca6cf8539c84720\LabEventExport.cs'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_console.ps1 `
  -BaselineDirectory 'X:\TESTY_TIA_Bridge\evidence\LAB-03M-before-6231897a6b124761b01b4cb6be1fe765'
py -3 -m unittest discover -s scripts -p test_compare_events.py
py -3 scripts/audit_quality.py
git diff --check
```

Attach/Engineering/Runtime **NOT_RUN**. Ocena instrukcji, ADR i kontraktów: [Zmiany.md](../Zmiany.md).
