# LAB-03O — overwrite i snapshot eksportu zdarzeń HMI

Pierwotny pakiet: 2026-10-29. Aktualizacja zachowania overwrite i zatwierdzona ocena per obiekt: 2026-10-30. Wyłącznie laboratoryjny filesystem/CLI i testy offline; bez rzeczywistego eksportu TIA, Attach, commita lub push.

Aktualna reguła: globalny `PARTIAL` nie blokuje samodzielnie usuwania historycznego handlera. Decyzja wymaga jego wiarygodnego pochodzenia, kompletnego odczytu konkretnego obiektu, poprawnej tożsamości projektu/HMI i zgodności SHA-256. Nie zmienia to znaczenia `PARTIAL`, zakresu odczytu ani kwalifikacji Engineering/Runtime. Poniższy akapit o poprzednim buildzie i sekcja historycznych wyników pozostają zapisem wcześniejszych dowodów; aktualne wyniki są w sekcji „Ocena per obiekt — weryfikacja offline”.

Aktualizacja 2026-10-30: `overwrite` zastępuje zmienione lub usunięte pliki na ścieżkach już zarządzanych przez LAB-03O. Recovery journal rozróżnia rejestr poprzedniej publikacji od przechwyconego baseline bieżących bajtów, aby po przerwaniu odtworzyć dokładny stan sprzed próby. Weryfikacja: testy offline **40/40 PASS**, build net48/x64 **PASS**, SHA-256 nowego `X:\TESTY_TIA_Bridge\bin\LabExporter.exe`: `BEA48D14ED2003D7961B4811A1D89CCFAF74DC9CDA2E5BB63836D1CB241BE933`. Nie uruchomiono rzeczywistego Attach/eksportu; pierwotna publikacja i jej receipt nie zostały zmienione przez tę aktualizację.

## Tryby i F5

`export-events --project ... --output ... --hmi ...` domyślnie działa jak `--mode overwrite`: aktualizuje artefakty **bezpośrednio w --output**, bez katalogów wersji. Zachowuje dowolność lokalizacji LAB-03N: ścieżki absolutne/względne, repo Git, Unicode, dyski mapowane i UNC według możliwości systemu plików. Ścieżka względna jest rozwiązywana względem katalogu roboczego procesu.

`--mode snapshot` zachowuje LAB-03N: nowy `<UTC>-<UUID>` w --output, bez nadpisywania lub usuwania poprzednich eksportów. Format/schematy i teksty snapshot są zgodne z LAB-03K/L/M. Inne wartości --mode oraz duplikaty opcji są błędem parametrów. Opcjonalne `--history VERIFIED_EVENT_EXPORT` wskazuje pojedynczy istniejący katalog historycznego eksportu jako dowód migracji, tylko w overwrite; nie jest poleceniem importu ani zgadywania pochodzenia. Snapshot z --history jest odrzucany przed Attach. Bez tego argumentu nie przeszukuje się Git, sąsiednich katalogów ani dawnych runów. Tryb nie zmienia zakresu i autoryzacji odczytu TIA.

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

`overwrite` zastępuje pliki na wcześniej zarejestrowanych ścieżkach zarządzanych przez eksport, nawet jeśli ich zawartość została od poprzedniego przebiegu ręcznie zmieniona lub plik usunięto. Bieżące bajty tych ścieżek są najpierw przechwytywane jako baseline recovery, więc przerwana publikacja może je bezpiecznie odtworzyć. To dotychczasowe zachowanie aktualizacji różni się od usuwania nieobecnego historycznego handlera: **usuwanie wymaga zgodności z zarejestrowanym i powiązanym z pochodzeniem SHA-256**. Drift zachowuje aktualne bajty jako UNVERIFIED. Eksporter **nie przejmuje plików po nazwie** ani na podstawie podobieństwa do JS/JSON: niezarządzany plik kolidujący z celem eksportu, inny projekt/HMI, obcy marker, reparse point lub niepewny recovery nadal blokują publikację. Eksport bez rejestru LAB-03O nie adoptuje automatycznie istniejącego katalogu, nawet jeśli zawiera manifest LAB-03K. W takim przypadku użyj nowego katalogu wyniku; nie usuwaj rejestru ani danych dla ominięcia kontroli.

Nowy zestaw powstaje w tymczasowym `.event-sync/tx-XXXXXXXX/new`; identyfikator transakcji ma kontrolę kolizji. Nie jest publikowany jako osobna wersja. Kontrolowana przestrzeń nazw obejmuje dokładne główne artefakty, scripts/ o określonym formacie oraz pliki widoku screens/. Rejestr jest limitowany do 40000 wpisów, 64 MiB/pliku i 256 MiB łącznie; parser JSON do 64 MiB i głębokości 64. Sprawdzane są duplikaty bez rozróżniania wielkości liter, traversal/ADS, aliasy Windows, zawartość/hash i reparse points, także w zagnieżdżonych katalogach i plikach.

Nowy `state.json` ma schemat `lab-03o-owner/2`. Każdy rozpoznany artefakt handlera otrzymuje `origin`: ścieżkę i raw SHA-256 artefaktu, ekran i `screen_key`, pełny łańcuch właściciela `owner` (kind/name/type), `object_key`, nazwę zdarzenia, `handler_key`, właściwość ScriptCode/GDA i reprezentację source/view. Klucze logiczne wynikają z dokładnych kind/name i zdarzenia; nie zawierają indeksów enumeracji i nie są trwałym identyfikatorem Siemens. Typy właściciela są dodatkowo porównywane. Nie wnioskuje się o rename/move. Niejednoznaczna tożsamość nie uprawnia do usunięcia.

Usunięcie starego artefaktu nieobecnego w świeżym zestawie wymaga łącznie:

1. Wiarygodnego, jednoznacznego mapowania historycznego handlera i poprawnych kluczy origin, ścieżki oraz SHA-256.
2. Zgodnego bindingu project_path/device_path. Dla ENGINEERING_OBSERVATION_ONLY wymagane są identity_before/after i hmi_after VERIFIED, oba IsModified=false, Dispose RETURNED oraz brak diagnostyk sesji o locatorze `/`. Tryb SYNTHETIC_OFFLINE służy wyłącznie testom wartości, bez Siemens.
3. Jednego bieżącego właściciela o zgodnej hierarchii i typach, z handlers_status ABSENT albo ENUMERATED, bez lokalnych diagnostyk odczytu/limitów i błędów enumeracji przodków. Ograniczenie wnętrza innego obiektu nie blokuje tej decyzji.
4. Rozpoznanych, unikalnych nazw wszystkich zdarzeń tego właściciela, kompletnych rekordów handlerów i nieobecności historycznej nazwy zdarzenia. Nieznany enum, nazwa null/UNKNOWN/liczbowa, błąd ScriptCode/GDA/Async lub limit publikacji nie dowodzą usunięcia.
5. Niezmienionych bajtów względem rejestru i origin, sprawdzanych ponownie przed mutacją filesystemu.

Globalny PARTIAL może współistnieć z DELETE dla konkretnego obiektu; globalny UNKNOWN nie daje takiego uprawnienia. Jeżeli właściciela nie ma w odczycie, wymagany jest kompletny cały publiczny zakres bez obserwacji, z rozpoznanymi metadanymi i zdarzeniami. Pozwala to zachować dotychczasową obsługę usunięcia całego ekranu. Historyczne pliki handlerów bez mapowania pozostają zachowane również przy kompletnym nowym odczycie. Nieobecne metadane zakresu, np. README usuniętego ekranu, można usuwać tylko przy kompletnym całym zakresie i zgodnym SHA-256.

`state.json.retained` pozostaje listą zachowanych ścieżek. `state.json.decisions` oraz nowe zarządzane `sync-report.json` (`lab-03o-sync-report/2`) i `sync-report.md` zapisują DELETE/CONFIRMED_ABSENT albo RETAIN_UNVERIFIED z powodem, handler_key oraz oczekiwanym i rzeczywistym SHA-256. Powody obejmują LEGACY_ORIGIN_UNVERIFIED, HISTORICAL_MAPPING_INVALID, SHA256_MISMATCH, IDENTITY_OR_SESSION_UNVERIFIED, OWNER_NOT_OBSERVED, OWNER_IDENTITY_AMBIGUOUS, OWNER_TYPE_CHANGED, LOCAL_ENUMERATION_INCOMPLETE, LOCAL_READ_UNCERTAIN, EVENT_NAME_UNKNOWN, EVENT_NAME_AMBIGUOUS, LOCAL_HANDLER_INCOMPLETE, HANDLER_STILL_PRESENT i SCOPE_METADATA_UNVERIFIED. Bieżący events-report.md odsyła do raportu decyzji. Zachowany plik nie staje się kodem odczytanym w bieżącym handlerze; events.json/index zachowują rzeczywiste statusy i odczytane ścieżki. Manifest obejmuje świeży odczyt i raporty decyzji; state obejmuje dodatkowo retained. Raporty wchodzą do limitu artefaktów i kontroli SHA-256; przekroczenie limitu blokuje publikację przed mutacją.

## Migracja rejestru v1

Migracja odbywa się w kopii rejestru pod tym samym lockiem i jest zatwierdzana razem z publikacją v2. Zarejestrowane hashe events.json i events-index.json muszą odpowiadać dyskowi, indeks musi wskazywać dokładny SHA-256/długość źródła i prawidłowe source_ref. Mapowane pliki muszą mieć jawne powiązania źródło/widok, jednoznaczną hierarchię i zdarzenie oraz zgodne bajty, SHA-256 i binding. Niewiarygodne bieżące metadane nie są używane do mapowania. Rejestr przechowuje oryginalny expected hash pochodzenia także po zachowaniu lokalnie zmienionego pliku; kolejne przebiegi nie akceptują go automatycznie jako nowej podstawy usuwania.

W starszym v1 pliki już retained mogą nie występować w aktualnym events.json/index. Takie wpisy pozostają bez origin i z powodem LEGACY_ORIGIN_UNVERIFIED. Opcjonalne --history pozwala operatorowi wskazać wcześniejszy, zachowany eksport zawierający te handlery. Weryfikowane są jego manifest LAB-03K, każdy wymieniony plik/długość/hash, źródło, indeks, source_ref, tożsamość i brak markerów nieukończonej publikacji. Do rejestru przenosi się tylko mapowania o dokładnie zgodnej ścieżce i SHA-256; istniejących niepoprawnych origin nie naprawia się w ciemno. Nie wykonuje się kodu ani normalizacji EOL/Unicode. Wadliwy jawny history blokuje publikację, zachowując poprzedni zestaw i receipt IN_PROGRESS. Katalog historyczny jest dowodem wskazanym przez operatora, nie kryptograficznym trust anchorem; samodzielnie spreparowany manifest nie poświadcza autentyczności TIA. Jego kopię i ACL należy zachować. Nie ma automatycznego przygotowywania historii z Git lub modyfikacji danych T05 w tym zadaniu.

Recovery przyjmuje v1/v2 i stare dzienniki bez origin/decisions. Porównanie rejestrów używa wszystkich zdeserializowanych pól kontraktu zamiast zależeć od obecności nowych opcjonalnych pól w dawnych bajtach JSON. Kopie odzyskiwania i kontrola docelowych bajtów nadal wymagają dokładnego SHA-256. Nie usuwa się dziennika lub markerów w celu wymuszenia migracji.

Pliki użytkownika poza rejestrem są pozostawiane, także wewnątrz screens/ i scripts/. Nie usuwa się katalogu --output ani katalogów ekranów; po usuniętych artefaktach mogą pozostać puste katalogi. Historyczne run-id w tym samym rodzicu pozostają nietknięte. Git odpowiada za wersjonowanie opublikowanych plików; rejestr state.json należy zachować razem z ich zgodną rewizją. Cofnięcie samych artefaktów w Git przy pozostawieniu innego rejestru daje konflikt hashy, bez automatycznego nadpisania.

## Publikacja, przerwanie i odzyskiwanie

1. Publisher renderuje kopię captured result, bez kolejnych wywołań Siemens. W overwrite nazwy scripts/ wynikają z hierarchii nazw/kind i nazwy zdarzenia, bez indeksu enumeracji. Sufiksy kolizji/truncation widoku screens/ również bazują na tej hierarchii. Niejednoznaczna tożsamość blokuje publikację; brak zgadywania. Oryginalne locatory i schematy events.json/index pozostają bez zmian.
2. Weryfikuje rejestr i zakres zarządzanych ścieżek, przechwytuje ich aktualne pliki jako baseline oraz przygotowuje kopie w tx/old. Flushuje dziennik pending.json i tworzy własny znacznik `.incomplete` przed pierwszą zmianą zarządzanych artefaktów.
3. Aktualizuje pliki i usuwa potwierdzone stale entries, ponownie sprawdzając hashe przechwyconego baseline/reparse points. Nowy plik jest przenoszony z kompletnego, zweryfikowanego stagingu; poprzedni cel jest wcześniej usuwany. Manifest i events.json są publikowane na końcu. Po weryfikacji całego zestawu zmienia rejestr, usuwa dziennik/marker i zapisuje receipt COMPLETE. Nie ma obietnicy atomowości wieloplikowej ani bezprzerwowego dostępu czytelników.
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

## Ocena per obiekt — weryfikacja offline

Decyzja użytkownika z 2026-10-30 zatwierdziła zmianę globalnej retencji na kompletność per obiekt i bezpieczną migrację. Zmieniono poza Git src/LabEventSync.cs (origin, decyzje, migracja, raporty i zgodność recovery), src/LabEventVendor.cs (wyłącznie opcjonalny --history i dispatch), README i bin/LabExporter.exe. W repo zmieniono scripts/lab_event_sync_tests.cs, scripts/test_lab_event_console.ps1, niniejszy kontrakt, README, scripts/README i Zmiany. Reader/traversal, LabEventView/Console, globalne Scripts, PLC, projekty i konfiguracje F5 pozostają bez zmian. Kopia źródeł/EXE sprzed prac oraz kontrolne hashe Events T05: `X:\TESTY_TIA_Bridge\evidence\LAB-03O-object-before-979a4d9672264782a00f12f5db8fec9a`.

Testy: **66/66 LAB-03O PASS**, w tym wszystkie poprzednie 40; dodatkowo integracja compare-events (1 MODIFIED/0 UNKNOWN). LAB-03K/L/M/N **42/16/14/18 PASS**, compare-events **44/44 PASS** — łącznie **200/200** testów offline, plus kontrole CLI/launch, bajtów i vendor lifecycle. Build net48/x64 bez błędów/ostrzeżeń; EXE SHA-256: `CAB1C664E828FF3E054FC3D72EB8F4F1FC017D48F796CE787494B9412034F35F`. Nie uruchomiono rzeczywistego eksportu, Attach, importu, kompilacji TIA ani Runtime.

Syntetyczny JSON_Test/Button_3: Tapped→Up przy globalnym PARTIAL usuwa dokładnie cztery stare pliki i publikuje cztery artefakty Up z kodem TEST LOGGER. Inny obiekt zachowuje bajty/hash. Wariant v1 z retained bez bieżącego mapowania zachowuje stare pliki; po jawnym wskazaniu zweryfikowanego historycznego eksportu migracja usuwa je bez zmiany PARTIAL. Zmieniony hash, błędny origin, nieznane zdarzenie, lokalny błąd, niejednoznaczny właściciel i niesprawdzona tożsamość zachowują artefakty albo blokują próbę. Testy sprawdzają świeży manifest, source hash indeksu, rejestr i zgodność decyzji w raportach, przerwanie usuwania, migracji oraz recovery dziennika bez nowych pól.

| Zakres | Nowe dowody w X:\TESTY_TIA_Bridge\evidence |
|---|---|
| LAB-03O, symulacja, integracja i podsumowanie | LAB-03O-offline-1bcb8cb170f14ca49b09edb4af8561ec |
| Build | LAB-03K-build-8474c3b5538746518098bc7f1fc98a78 |
| LAB-03K | LAB-03K-offline-1d285e6b9d2c426b871ae8f562ca3686 |
| LAB-03L | LAB-03L-offline-10162645b4f9412fa13eb55949d7f9c5 |
| LAB-03M | LAB-03M-offline-40e811ac91bc4146a1dc2171200f3d89 |
| LAB-03N | LAB-03N-offline-19eb3807e7194ef0b26c4e97fad868c2 |

Wcześniejsze nieudane przebiegi tej aktualizacji zachowano: raport synchronizacji początkowo omyłkowo trafiał do retencji, co zmieniało baseline recovery; poprawiono planowanie raportów i oddzielono kopie wpisów. Guard wykrył brak nowej opcji w drugim tekście Usage; ujednolicono pomoc i ponownie potwierdzono pełną zgodność vendor lifecycle. Nie przepisano dawnych FAIL na PASS.

Rzeczywisty Events T05 i jego 26 zmodyfikowanych oraz 4 nieśledzone pliki zachowano bajtowo. Nie wykonano migracji ani symulacji na kopii danych klienta; scenariusze używają wyłącznie syntetycznych wartości. Ograniczenia: mapowanie nazw nie jest UUID Siemens, nieodczytane wnętrza pozostają UNKNOWN, historia bez wiarygodnego origin pozostaje UNVERIFIED, a jawny history nie jest podpisanym poświadczeniem TIA. Starszy program nie rozpoznaje rejestru v2 i musi odmówić jego użycia; nie wykonuj downgrade przez ręczną zmianę schematu. Staging i recovery chronią własne pliki filesystemu, nie zapewniają atomowości całego katalogu ani odporności na złośliwe równoległe zmiany. Engineering/Runtime **NOT_RUN**. Kontrola jakości dokumentacji i odnośników oraz końcowego diffu obowiązuje po aktualizacji.
