# LAB-03O — overwrite i snapshot eksportu zdarzeń HMI

Data: 2026-10-29. Repo `main`, HEAD `62ee9f5ae0b11074e404c86f4a09b6e96e4da99a`. Zastane, niecommitowane LAB-03N zachowano. Wyłącznie laboratoryjny filesystem/CLI i testy offline; bez rzeczywistego eksportu TIA, Attach, commita lub push.

## Tryby i F5

`export-events --project ... --output ... --hmi ...` domyślnie działa jak `--mode overwrite`: aktualizuje artefakty **bezpośrednio w --output**, bez katalogów wersji. Zachowuje dowolność lokalizacji LAB-03N: ścieżki absolutne/względne, repo Git, Unicode, dyski mapowane i UNC według możliwości systemu plików. Ścieżka względna jest rozwiązywana względem katalogu roboczego procesu.

`--mode snapshot` zachowuje LAB-03N: nowy `<UTC>-<UUID>` w --output, bez nadpisywania lub usuwania poprzednich eksportów. Format/schematy i teksty snapshot są zgodne z LAB-03K/L/M. Inne wartości --mode oraz duplikaty opcji są błędem parametrów. Tryb nie zmienia zakresu i autoryzacji odczytu TIA.

F5 **TIA Bridge: EXPORT EVENTS — TIA → Folder** zachowuje dokładnie poprzednie argumenty projektu/HMI/output i korzysta z domyślnego overwrite bez --mode. Dodano **TIA Bridge: EXPORT EVENTS — SNAPSHOT** z identycznymi argumentami i jawnym `--mode snapshot`. Brak pytań interaktywnych F5; natywny monit autoryzacji Siemens nadal obsługuje operator. FORCE EXPORT/IMPORT i konfiguracja compare-events pozostają bez zmian.

Gotowe polecenie do późniejszego, osobno autoryzowanego rzeczywistego eksportu (nie wykonano):

```powershell
& 'X:\TESTY_TIA_Bridge\bin\LabExporter.exe' export-events `
  --project 'X:\TESTY_TIA_Bridge\T05_RecipeSim_V19_Testowy' `
  --output 'X:\TESTY_TIA_Bridge\EventExports' `
  --hmi 'HMI_JSON_TEST/HMI_RT_1'
```

Aby zachować nowy historyczny snapshot, dopisz `--mode snapshot`. Polecenie wymaga właściwego otwartego projektu i dotychczasowych kontroli identity/IsModified; dokumentacja nie nadaje uprawnień TIA.

## Własność plików i usuwanie

Overwrite tworzy `.event-sync/owner`, `lock`, `state.json` i `publication.json`. Jest to rejestr sterujący, nie katalog wersji. Stan wiąże dokładne project_path/device_path z listą względnych plików, długościami i SHA-256. Nie utożsamia tożsamości TIA z samym katalogiem. Proces publikuje pod wyłącznym filesystemowym lockiem; żaden proxy Siemens nie trafia do publishera.

Przed publikacją sprawdzany jest każdy poprzedni zarządzany plik. Ręczna zmiana, brak pliku, obcy plik na nowej ścieżce lub inny projekt/HMI oznaczają konflikt. Eksporter **nie przejmuje plików po nazwie** ani na podstawie podobieństwa do JS/JSON. Nie adoptuje automatycznie starego eksportu bez rejestru LAB-03O, nawet jeśli ma manifest LAB-03K. Użyj nowego/pustego katalogu lub zachowaj stary zestaw i wybierz snapshot; nie usuwaj danych dla ominięcia konfliktu.

Nowy zestaw powstaje w tymczasowym `.event-sync/tx-XXXXXXXX/new`; identyfikator transakcji ma kontrolę kolizji. Nie jest publikowany jako osobna wersja. Kontrolowana przestrzeń nazw obejmuje dokładne główne artefakty, scripts/ o określonym formacie oraz pliki widoku screens/. Rejestr jest limitowany do 40000 wpisów, 64 MiB/pliku i 256 MiB łącznie; parser JSON do 64 MiB i głębokości 64. Sprawdzane są duplikaty bez rozróżniania wielkości liter, traversal/ADS, aliasy Windows, zawartość/hash i reparse points, także w zagnieżdżonych katalogach i plikach.

Kompletny odczyt i kompletny zestaw artefaktów umożliwiają usunięcie wyłącznie starych wpisów rejestru nieobecnych w nowym zestawie. Przy PARTIAL/UNKNOWN, błędzie handlera lub pominięciach wynikających z limitu publikacji zachowywane są **wszystkie** stare pliki nieobecne w nowym zestawie. Jest to celowo konserwatywne: także lokalnie prawdopodobne usunięcia czekają na kompletny odczyt. Lista zachowanych ścieżek jest w state.json.retained. Dotyczy to również JS z błędem ponownego odczytu. Zachowany stary plik nie staje się odczytanym kodem bieżącego handlera; bieżący events.json/index zachowuje READ_FAILED/UNKNOWN/PARTIAL i brak ścieżki dla nieodczytanej właściwości. Nowy manifest opisuje nowy odczyt, a rejestr własności dodatkowo obejmuje zachowane stare pliki.

Pliki użytkownika poza rejestrem są pozostawiane, także wewnątrz screens/ i scripts/. Nie usuwa się katalogu --output ani katalogów ekranów; po usuniętych artefaktach mogą pozostać puste katalogi. Historyczne run-id w tym samym rodzicu pozostają nietknięte. Git odpowiada za wersjonowanie opublikowanych plików; rejestr state.json należy zachować razem z ich zgodną rewizją. Cofnięcie samych artefaktów w Git przy pozostawieniu innego rejestru daje konflikt hashy, bez automatycznego nadpisania.

## Publikacja, przerwanie i odzyskiwanie

1. Publisher renderuje kopię captured result, bez kolejnych wywołań Siemens. W overwrite nazwy scripts/ wynikają z hierarchii nazw/kind i nazwy zdarzenia, bez indeksu enumeracji. Sufiksy kolizji/truncation widoku screens/ również bazują na tej hierarchii. Niejednoznaczna tożsamość blokuje publikację; brak zgadywania. Oryginalne locatory i schematy events.json/index pozostają bez zmian.
2. Weryfikuje własność wszystkich celów i przygotowuje zweryfikowane kopie poprzednich plików w tx/old. Flushuje dziennik pending.json i tworzy własny znacznik `.incomplete` przed pierwszą zmianą zarządzanych artefaktów.
3. Aktualizuje pliki i usuwa potwierdzone stale entries, ponownie sprawdzając oczekiwane hashe/reparse points. Nowy plik jest przenoszony z kompletnego, zweryfikowanego stagingu; poprzedni cel jest wcześniej usuwany. Manifest i events.json są publikowane na końcu. Po weryfikacji całego zestawu zmienia rejestr, usuwa dziennik/marker i zapisuje receipt COMPLETE. Nie ma obietnicy atomowości wieloplikowej ani bezprzerwowego dostępu czytelników.
4. Przy następnej publikacji overwrite istniejący dziennik uruchamia odzyskanie **poprzedniego zestawu plików na dysku**. Cały plan/destinations/backups są sprawdzane przed rozpoczęciem przywracania. Aktualny cel musi być nieobecny lub mieć hash starego/nowego artefaktu. Obca zmiana lub uszkodzony backup blokują odzyskanie i zachowują dowody. Recovery jest ponawialne po kolejnej przerwie; nie wywołuje TIA, Import, Save ani Attach. Następuje w publisherze po zakończeniu aktualnej sesji read-only, przed publikacją nowego zestawu. Nie jest ponowieniem odczytu TIA.
5. W oknie po usunięciu dziennika, ale przed usunięciem markera, recovery weryfikuje kompletny state.json i usuwa wyłącznie rozpoznany własny marker. Obcy marker/obszar .event-sync jest konfliktem. Zakończone transakcje są czyszczone wyłącznie po znanych ścieżkach i hashach, bez recursive delete. Obcy plik w stagingu pozostaje. Nieudane sprzątanie zgłasza cleanup REVIEW_REQUIRED; nie unieważnia już zweryfikowanego zestawu.

Odczyt i publikacja są osobnymi wynikami. Konsola wypisuje READ RESULT przed próbą publikacji i PUBLICATION COMPLETE dopiero po jej zakończeniu, z read_status i artifact_status. `.event-sync/publication.json` zapisuje IN_PROGRESS przed przygotowaniem/publikacją, a COMPLETE po sukcesie. IN_PROGRESS oznacza również nieudaną/przerwaną próbę i **nie jest sukcesem**. W takim przypadku poprzedni events.json może nadal być poprawnym eksportem poprzedniej próby; nie stanowi potwierdzenia bieżącej. state.json ma osobno read_status, artifact_status i publication_status. Exit 0 = kompletny opublikowany zakres, 2 = opublikowane częściowe/nieznane artefakty, 1 = odmowa/błąd publikacji. Kompletne dane w pamięci nie maskują pominięć artefaktów przez limit — wtedy exit 2.

Przed użyciem zestawu sprawdź receipt COMPLETE, brak `.incomplete` i pending.json oraz integralność manifestu; retained wskazuje historyczne pliki spoza nowego manifestu. Niezmieniony compare-events czyta sam events.json i **nie interpretuje** nowego rejestru/receipt. Nie uruchamiaj go na katalogu podczas publikacji lub recovery. Test integracyjny obejmuje zakończony zestaw.

Nie ma gwarancji odporności na utratę zasilania/awarię serwera SMB ani ochrony przed złośliwym równoległym podmienianiem plików przez obcy proces. Uszkodzony journal/rejestr lub konflikt odzyskiwania oznacza STOP i ręczny przegląd `.incomplete`, pending.json i tx/old/new; zachowaj je razem z kopiami obu zestawów. Nie usuwaj markerów ani nie edytuj rejestru w celu wymuszenia sukcesu. Niedokończone stagingi i osierocone pliki tymczasowe po awarii przed dziennikiem mogą wymagać ręcznego przeglądu; normalny sukces nie pozostawia katalogów wersji. Ochrona ACL i reparse points pozostaje, a możliwości Flush/Move/Replace zależą od filesystemu. UNC testowano przez lokalny SMB, nie zewnętrzny serwer.

## Pliki i wyniki offline

Poza Git, w `X:\TESTY_TIA_Bridge`: nowy src/LabEventSync.cs; zmiany src/LabEventVendor.cs (wyłącznie CLI, wybór celu, dispatch publikacji/diagnostyka i źródło statusu exit), src/LabEventExport.cs (opcjonalny rendering bez drugiego stagingu dla overwrite), src/LabEventView.cs (opcjonalne stabilne sufiksy overwrite), README i bin/LabExporter.exe. LabExporter.cs, reader/traversal, LabEventConsole.cs, FORCE JS, produkcyjny Host/API/Worker i projekty bez zmian. Przed zmianami źródła/EXE zachowano w `evidence/LAB-03O-before-2acb3edc40204fc382ffb03b50190b6c`.

W repo: .vscode/launch.json; scripts/build_lab_events.ps1; scripts/test_lab_events.ps1; scripts/test_lab_event_console.ps1; nowe scripts/lab_event_sync_tests.cs i scripts/test_lab_event_sync.ps1; README.md, scripts/README.md, Zmiany.md, aktualne odnośniki LAB-03K/N i niniejszy raport. Zastane pliki LAB-03N nie zostały cofnięte.

Testy: LAB-03O **38/38 PASS** i dodatkowy test integracyjny z compare-events (1 MODIFIED handler, 0 UNKNOWN). Wymagane regresje LAB-03K/L/M/N **42/16/14/18 PASS**, compare-events **44/44 PASS** — łącznie **172/172** oraz integracja i dwie kontrole CLI/launch. Snapshot byte regression: 9 oryginalnych artefaktów identycznych, także events.json/index; manifest różni wyłącznie tool_sha256. Guard całego vendor source dopuszcza dokładne zmiany CLI/ścieżek/publishera i zachowuje wszystkie Siemens calls/lifecycle. Build net48/x64 bez ostrzeżeń/błędów. Kontrola dokumentacji/linków i diffu po zmianach.

Końcowe dowody w `X:\TESTY_TIA_Bridge\evidence`:

| Zakres | Katalog |
|---|---|
| LAB-03O, integracja, zbiorcza weryfikacja | LAB-03O-offline-1239e649d6bb497dad9e58217f8fdc58 |
| Build | LAB-03K-build-2d2b293f85694e11bfaea05f9368266e |
| LAB-03K | LAB-03K-offline-952a107ce236479b94ec4d93cd22d4b5 |
| LAB-03M | LAB-03M-offline-1ea522c4ee384935b9f4a10fae7dc721 |
| LAB-03L | LAB-03L-offline-ca47ad7506bb439c93608a31c983d4c3 |
| LAB-03N | LAB-03N-offline-f0fe87fba5b048b58b65290c0b88fbce |

Finalne LAB-03K/M i pełny stdout compare-events wskazuje verification.json w katalogu LAB-03O. Testy obejmują pierwszy/ponowny eksport, aktualizacje JS/JSON/Markdown, dodawanie/usuwanie handlera/ekranu, pliki użytkownika i edycje zarządzanych, PARTIAL/UNKNOWN/read failure, stabilność nazw przy zmianie indeksów i sanitization collision, snapshot, względne/Unicode/repo/UNC, lock, registry traversal, obcy marker/control, junction wewnątrz zarządzanego drzewa, pięć okien awarii, przerwanie przy usuwaniu oraz rzeczywisty Exit procesu podczas publikacji i następny eksport. Osobny test limitu artefaktów potwierdza PARTIAL bez zmiany captured result i zachowanie starego JS.

Wcześniejsze nieudane przebiegi zachowano: kompilacja testu (kolizja lokalnej nazwy), nadmiernie zagnieżdżony staging i VIEW_PATH_LIMIT_EXCEEDED (naprawiono rendering bez dodatkowego wrappera stagingu), checkpoint niezmienianego README (przeniesiono na zmieniany events-index). Nie zamieniono tych wyników historycznych na PASS.

Reprodukcja: [scripts/README.md](../scripts/README.md). Ocena instrukcji/ADR/kontraktów: [Zmiany.md](../Zmiany.md). Engineering/Attach/F5/Runtime **NOT_RUN**. Nie wykonano commita/push.
