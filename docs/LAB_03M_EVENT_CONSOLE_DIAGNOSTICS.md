# LAB-03M — czytelna diagnostyka konsoli EXPORT EVENTS

Data: 2026-10-29. Baza `4fa6887`, `main`, z zachowanymi niecommitowanymi zmianami LAB-03L. Implementacja i testy **OFFLINE PASS**. Attach, rzeczywisty eksport TIA, F5 i Runtime **NOT_RUN**; bez Import, Save, Compile, Download, commita i push.

## Zakres prezentacji

Istniejący laboratoryjny `LabExporter.exe export-events` po poprawnej publikacji pokazuje projekt/HMI, oryginalny status, liczniki i katalog wyniku. Lista diagnostyki zachowuje wszystkie obserwacje, ich kolejność, pierwotny kod, fazę i ewentualny detail_code oraz typ wyjątku. Nie drukuje treści JS lub wiadomości wyjątków Siemens.

Przykład z rzeczywistego **syntetycznego testu offline**, nie z projektu TIA:

```text
TIA BRIDGE — EXPORT EVENTS

Project : Synthetic_RecipeSim
HMI     : HMI_JSON_TEST/HMI_RT_1

Status       : PARTIAL
Screens      : 1
Objects      : 2
Handlers     : 1
Observations : 1
Warnings     : 1
Errors       : 0
Unclassified : 0

WARN  T05_Recipes_Main / RecipeTable_Main
      ENCAPSULATED_CHILDREN_NOT_PUBLIC
      phase=children

INFO  Publikacja zakończona. Status odczytu: PARTIAL
INFO  Publiczny zakres odczytu; ograniczenia i pełne locatory: events.json / events-report.md.
Output: X:\Synthetic\EventExports\run
```

Status pozostaje dokładnym statusem LAB-03K, również COMPLETE_FOR_PUBLIC_SCOPE, PARTIAL lub UNKNOWN. Screens liczy SCREEN, Objects liczy ITEM/UNKNOWN, z wyłączeniem ekranów, grup i software — zgodnie z [indeksem LAB-03L](LAB_03L_READABLE_EVENT_EXPORT.md). Handlers i Observations to liczby rekordów. Warnings/Errors liczą obserwacje, nie unikalne obiekty lub przyczyny. PARTIAL nie zwiększa licznika błędów. Kilka obserwacji tej samej awarii może dać kilka ERROR.

## Jawna klasyfikacja kodów

| Poziom | Kody / zastosowanie |
|---|---|
| INFO | informacja przed istniejącym Attach o ręcznej autoryzacji i braku retry; informacja po ukończeniu publikacji z osobnym statusem odczytu |
| WARN | ENCAPSULATED_CHILDREN_NOT_PUBLIC, UNSUPPORTED_OBJECT, UNSUPPORTED_COLLECTION, LIMIT_EXCEEDED, UNKNOWN_EVENT_VALUE, NULL_VALUE_UNKNOWN |
| ERROR | READ_FAILED, ENUMERATION_FAILED, DISPOSE_FAILED, EXPORT_READ_FAILED, POSTCHECK_FAILED, PROJECT_DIRTY_AFTER_READ, INVALID_UNICODE, NULL_ITERATOR, NULL_ELEMENT |
| WARN, classification=UNKNOWN | każdy inny kod; dodatkowo licznik Unclassified i zachowany typ wyjątku, jeżeli istnieje |

ENCAPSULATED_CHILDREN_NOT_PUBLIC oznacza ograniczenie pokrycia publicznego API, bez awansu do błędu wykonania. NULL_VALUE_UNKNOWN pozostaje niepełnym/nieznanym odczytem, bez wniosku o potwierdzonym braku. NULL_ITERATOR/NULL_ELEMENT oznaczają nieprawidłowy wynik enumeracji. PROJECT_DIRTY_AFTER_READ oznacza nieudaną końcową kontrolę stanu, nie błąd zapisu. EXPORT_READ_FAILED obejmuje również odmowę/niepowodzenie wykonania przez warunki bezpieczeństwa; detail_code pozostaje widoczny. Klasyfikacja nie jest diagnozą przyczyny Siemens.

Nieznany kod nie jest automatycznie błędem wykonania na podstawie PARTIAL lub obecności exception_type. Otrzymuje WARN z jawnym UNKNOWN, więc Errors=0 przy Unclassified>0 nie dowodzi braku rzeczywistych błędów. Bieżący format źródłowy nie dostarcza severity; mapa jest lokalną polityką prezentacji i może wymagać aktualizacji dla przyszłych kodów. Nie dodano pola severity do JSON ani sztucznych obserwacji INFO.

INFO o publikacji nie oznacza kompletności odczytu, Engineering PASS, pełnego modelu HMI lub SYNCED. Błędy odczytu mogą nadal zakończyć się opublikowanym PARTIAL i exit 2 — tak jak wcześniej. Błąd parametrów/publikacji ma ERROR EXPORT EVENTS FAILED i exit 1. Ukończony COMPLETE_FOR_PUBLIC_SCOPE nadal exit 0. Wszystkie trzy gałęzie powrotu pozostawiono bez zmian.

## Nazwy i bezpieczeństwo wyświetlania

Nazwy pochodzą wyłącznie z inventory tego odczytu: grupy, ekran i łańcuch elementów. Handler jest powiązany przez dokładny locator i object_locator; jego czytelny opis zawiera Event: <nazwa>. Nie ma dopasowania po podobieństwie lub samej nazwie. Duplikaty locatorów, nazw/etykiet, zdarzeń, brak nazwy, rodzica lub nieznany kind powodują wyświetlenie pełnego locatora. Etykiety sprawdzane są także po escapowaniu znaków konsoli; fallback obejmuje kolizje ich widocznej reprezentacji. Nieodczytane dziecko nie dziedziczy nazwy rodzica.

Pełne locatory pozostają w niezmienionych raportach. Unicode i długie ścieżki nie są obcinane w konsoli. Znaki sterujące, formatowania/bidi i separatory linii są widoczne jako literalne escape \uXXXX, aby metadane nie wstrzykiwały nowych wierszy lub sekwencji terminala. Kod JS, stringi API i artefakty pozostają nietknięte. UTF-8 ustawiany jest wyłącznie w ścieżce konsoli export-events; bez kolorów lub wymogu obsługi ANSI.

## Niezmienność i pliki

Nowy `X:\TESTY_TIA_Bridge\src\LabEventConsole.cs` jest niezależny od Siemens i nie modyfikuje LabEventResult. W `LabEventVendor.cs` dodano wyłącznie inicjalizację konsoli i zamieniono trzy istniejące miejsca wypisywania: monit Attach, końcowe podsumowanie i błąd zewnętrzny. Odczyty, identity checks, IsModified, lifecycle, mutex, publikacja i return branches są identyczne po pominięciu tych czterech zmian prezentacji. Test porównuje cały bieżący plik z zachowaną wersją LAB-03L po dokładnie tych podstawieniach.

Reader/publisher `LabEventExport.cs`, widok `LabEventView.cs`, główny `LabExporter.cs` i compare-events pozostają bajtowo niezmienione. Schematy events.json i events-index.json oraz wszystkie artefakty danych są niezmienione. Manifest zachowuje schemat i tool_version LAB-03L/1.0.0; aktualizuje się hash zbudowanego programu. Test dwóch syntetycznych publikacji potwierdził identyczność 9 plików danych, jednakowy zestaw plików i jednakowy manifest po wyłączeniu wyłącznie tool_sha256.

Zmiany tego zadania:

- Laboratorium: nowy src/LabEventConsole.cs, ograniczona edycja src/LabEventVendor.cs, przebudowany bin/LabExporter.exe, dopisany README.md.
- Repo: scripts/build_lab_events.ps1, nowe scripts/lab_event_console_tests.cs i scripts/test_lab_event_console.ps1, scripts/README.md, README.md, Zmiany.md, ten raport oraz odnośnik w LAB-03L.

Nie zmieniono FORCE EXPORT/IMPORT, produkcyjnego Worker/Host/API/IPC/registry, uprawnień, launch.json, T05_plc, projektów TIA i historii eksportów. Początkowe zmiany LAB-03L zachowano bez commita.

## Testy i dowody

Zależności: Windows PowerShell 5.1 x64, systemowy .NET Framework csc; build EXE i kontrola metadata wymagają istniejącego PublicAPI V19. Bez nowych pakietów. compare-events wymaga Python 3.12 stdlib; dodatkowa regresja LAB-03I korzysta z istniejących PowerShell 7 i Esprima.dll. Testy nie wykonują Siemens calls.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_console.ps1 `
  -BaselineDirectory 'X:\TESTY_TIA_Bridge\evidence\LAB-03M-before-6231897a6b124761b01b4cb6be1fe765'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_events.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test_lab_event_view.ps1 `
  -BaselineSource 'X:\TESTY_TIA_Bridge\evidence\LAB-03L-before-e0d00e4b42694ab3bca6cf8539c84720\LabEventExport.cs'
py -3 -B scripts/test_compare_events.py
py -3 scripts/audit_quality.py
git diff --check
```

Dowody pod X:\TESTY_TIA_Bridge\evidence:

| Zestaw | Wynik | Katalog/pliki |
|---|---|---|
| Finalny build net48/x64 | PASS, 0 warnings/errors | LAB-03K-build-307769c1ee924189b99b6d40a4778b5a/build-manifest.json |
| LAB-03M, konsola | 14/14 PASS | LAB-03M-offline-f08c83ef67f0454aad091c797b926336/results.json |
| Artefakty/lifecycle/exit | PASS, 9 identycznych artefaktów, manifest różni tylko tool_sha256 | ten sam katalog, compatibility.json |
| LAB-03K | 42/42, CLI/launch 2/2 PASS | LAB-03K-offline-71ffafbbd63b401aa2ac81b3b30c0264 |
| LAB-03L | 16/16 PASS i byte regression LAB-03K | LAB-03L-offline-0f13c4fca8af41d7a7ece066191c1ba6 |
| compare-events | 30/30 PASS | polecenie powyżej, wynik zapisany w raporcie końcowym |
| Dodatkowo LAB-03I / LAB-03J / LAB-03H | 18/18 + 15/15 + 10/10 PASS | szczegółowe ścieżki w raporcie końcowym |

Razem **145/145** testów zachowania/regresji PASS, w tym wymagane M/K/L/compare-events **102/102**. Dodatkowo kontrole kompatybilności i CLI/launch. Nowe testy obejmują brak obserwacji, ograniczenia kontenerów, wszystkie znane kody błędów, przypadki mieszane/nieznane, brakujące i niejednoznaczne nazwy/locatory, grupy/nesting, długie ścieżki, Unicode, kontrolki terminala i brak mutacji źródłowych danych. Exit 1 sprawdzono przez rzeczywiste uruchomienie niepełnego CLI, przed Attach. Exit 0/2 i vendor lifecycle zweryfikowano przez niezmienność całego kodu poza prezentacją; nie wykonano live wywołań dla tych ścieżek.

Pierwszy przebieg 14/14 miał FAIL dodatkowej kontroli źródła: Windows PowerShell 5.1 odczytał polski literal w skrypcie bez BOM przez lokalną stronę kodową. Kontrolę ograniczono do wzorca ASCII tego samego komunikatu. Zdarzenie i wcześniejsze testy zachowano w evidence; finalny przebieg powyżej ma wszystkie kontrole PASS.

SHA-256 finalnego EXE: `2b63124664703a1b6282eac2c51c0ba64b08aa483d9ede4ebe6344bbd0bb77b4`. Pełny raport/hash inventory: `X:\TESTY_TIA_Bridge\evidence\LAB-03M-final-report.json`. Instrukcje i ADR/specyfikacje oceniono sekcja po sekcji; uzasadnienie pozostawienia bez zmian w [Zmiany.md](../Zmiany.md). OFFLINE PASS nie kwalifikuje Siemens ani rzeczywistego UX F5.
