# 04 — Synchronizacja, źródła prawdy i recovery

Status: kontrakt projektowy v1, 2026-10-24. Rozwija wejściowy [sync-model](sync-model.md). Priorytet workspace oznacza zachowanie intencji CODEX i pierwszeństwo przy tworzeniu planu, nie uprawnienie do nadpisania nieznanego stanu TIA.

Implementacja offline B-01B konkretyzuje wyłącznie modele, format manifestu, codec i registry: [12 — Workspace i Capabilities](12_WORKSPACE_CAPABILITIES.md). Nie realizuje poniższego silnika synchronizacji, IO, baseline ani kwalifikacji native HMI.

## Niezależne stany

| Symbol | Stan | Właściciel / rewizja |
|---|---|---|
| P | bieżący projekt TIA w pamięci | vendor; `session_epoch` + świeże observation ID i scope fingerprint |
| D | zapisany projekt na dysku | vendor; Save receipt i kontrola `IsModified=False`, nie odczyt samych plików AP19 |
| W | pliki robocze CODEX | CODEX/Git, content digest; niezmieniane przez Apply |
| S | zamrożony zestaw wejść planu | Bridge, immutable artifact hashes + source revision; import czyta wyłącznie S |
| B | ostatni potwierdzony stan synchronizacji | Bridge immutable baseline revision z hashami obu reprezentacji, coverage i evidence |
| C | ostatnia kompilacja | scope, adapter/build, snapshot/model fingerprint, warnings policy, wynik |
| E | środowisko symulacyjne | lease, instancja, loaded program fingerprint, stan RUN/READY, izolacja |
| T | ostatnie testy | scenario/version, C/E/input revision, czasy, jakości wartości, PASS/FAIL/ERROR/SKIP |

P nie jest D; W nie jest B. C/E/T nigdy nie są aktualne tylko dlatego, że istnieje plik raportu. Zmiana zależności unieważnia ich freshness. Rewizje projektu, source tree i baseline mają oddzielne identyfikatory; Git HEAD jest provenance, nie potwierdzeniem TIA. Snapshot exportu może istnieć przy `IsModified=True`, ale nie tworzy wtedy potwierdzonej saved baseline.

## Identity, hierarchy i ścieżki

`project_key` jest nadanym przez Bridge UUID z registry. Zawiera binding do kanonicznej ścieżki projektu, identyfikatorów projektu dostępnych publicznie, produktu/build i katalogu urządzeń. Kopia AP19 nie dziedziczy automatycznie bindingu. `session_epoch` wiąże PID + start UTC + handshake instance; PID sam może zostać użyty ponownie.

`object_id` to UUID Bridge, niezależny od ścieżki. Manifest zawiera `vendor_locator` (device, groups, kind, name), ewentualny publiczny stabilny ID z jego zakresem ważności oraz `identity_evidence`. Nie zakładamy, że Export XML ID, CLR hash, Equals lub ekranowy numer są trwałym GUID. Pierwsze discovery mapuje jednoznaczne locatory; rename/move wymaga jawnego mapping action i walidacji referencji. Ambiguity → CONFLICT/UNKNOWN, bez heurystycznego delete/create.

Przykładowa hierarchia: `workspace/PLC/<device>/<groups>/<object-id-prefix>--<escaped-name>/code.scl`, `object.json`, ewentualny `native.xml`; `workspace/HMI/<device>/Screens/.../Events/.../code.js` i `object.json`; moduł ma `GlobalDefinitions.js`, `Functions/<id>--<name>.js`, `module.json` i pełny native export w artifacts. To odpowiada strukturze, lecz native jednostka wymiany może być większa niż jeden logiczny obiekt.

Escaping: NFC dla prezentacji, oryginalna nazwa zachowana w metadanych; percent encoding UTF-8 dla znaków niedozwolonych, `%` i separatorów; prefiks dla nazw CON/PRN/AUX/NUL/COM 1/LPT 1 oraz końcowych spacji/kropek. Krótki sufiks UUID rozróżnia case/Unicode collisions. Maks. segment 100 znaków, root+relative ≤240 w MVP; dłuższe nazwy skracane deterministycznie z pełną nazwą w metadanych. Odrzucamy traversal, rooted paths, alternate data streams i reparse points wychodzące poza workspace. Testy obejmują normalizacje Unicode, case-only rename i kolizję po skróceniu.

## Manifest, hashe i normalizacja

Manifest ma `schema_version`, `workspace_id`, `project_key`, `adapter_id/build`, `capability_set_hash`, inventory i coverage. Każdy object zawiera kind/language, locator, ścieżki wszystkich członków jednostki wymiany, zależności i `comparison_profile`. Native pliki nie są pomijane dlatego, że edytowalny SCL/JS jest obecny.

Trzy hashe: `raw_sha256` nad dokładnymi bajtami każdego pliku; `model_sha256` nad deterministycznym modelem z wersją normalizatora; `unit_sha256` nad posortowaną listą `(relative_path, raw_sha256)` i istotnymi metadanymi. Canonical JSON: UTF-8, sortowanie kluczy porządkowo, bez NaN i z odrzuconymi duplicate keys, jednoznaczne liczby, brak mtime/operation UUID w hash planu. Timestamp jest poza częścią canonical.

Dopuszczona normalizacja jest profilem per rodzina i wersja: np. BOM/EOL w JS przy zachowaniu każdego znaku merytorycznej treści. Nie usuwamy deklaracji, komentarzy Siemens, `auto-generated` boundaries, eksportów funkcji, kolejności semantycznej ani unknown YAML/XML. XML canonicalization może znormalizować reprezentację namespace/atrybutów, ale nie usuwa pól vendor bez golden fixture i jawnej reguły. Wartości typowane (`Int32`, `UInt32`, `MultilingualText`, enum) pozostają typowane. Zgodny raw hash potwierdza bytes, nie pełną semantykę. `model_sha256` oznacza zgodność pokrytego modelu, nie dowód równoważności każdego programu SCL/LAD.

## Trzystronny plan

Porównanie dotyczy **pary reprezentacji w B**: `B.workspace_model` i `B.tia_model`. Nie wymagamy równości raw SCL z XML. Każdy comparison profile definiuje projekcję i obowiązkowe wymiary.

| Sytuacja względem B | Klasyfikacja / polityka |
|---|---|
| W bez zmian, P bez zmian | SYNCED tylko przy ważnych dowodach i pełnym required coverage |
| W zmienione, P bez zmian | MODIFIED/WORKSPACE_ONLY, plan push zgodnie z W |
| W bez zmian, P zmienione | MODIFIED/TIA_ONLY; raport i jawny adopt/pull albo push przy osobnej decyzji; nie nadpisuj W eksportem |
| W i P zmienione do tego samego modelu | CONVERGED candidate; re-verify i nowa baseline bez importu, jeżeli wszystkie required dimensions zgodne |
| W i P zmienione różnie | CONFLICT; zachowaj W/P/B, pokaż diff i resolution policy |
| B brak, tylko W | NEW_WORKSPACE candidate; identyfikacja braku P i zależności; create po zatwierdzeniu |
| B brak, tylko P | NEW_TIA inventory; eksport do staging, jawne przyjęcie do W/B |
| B brak, W i P obecne | UNKNOWN_INITIAL; verify/adopt, brak domyślnego overwrite |
| W brak, P obecne | MISSING_WORKSPACE; bez tombstone i zgody nie ma delete |
| P brak, W obecne | MISSING_TIA; rozróżnij usunięcie TIA, niedostępny reader i filtr inventory; create tylko po wyjaśnieniu |
| obie strony brak po zatwierdzonym delete | VERIFIED_ABSENT tombstone; usunięty obiekt nie ma statusu SYNCED żywego obiektu |
| odczyt niepełny/nieobsługiwany | UNKNOWN lub UNSUPPORTED; nigdy MISSING_TIA na podstawie błędu |

Domyślna polityka konfliktów `stop_and_report`. Dopuszczone decyzje: `merge_workspace`, `adopt_tia`, `keep_workspace_with_reviewed_overwrite`. Resolution wiąże hashe obu stron, object IDs, osobę/uprawnienie i termin ważności; każda zmiana tych hashy wymaga nowego planu. Nie ma ustawienia globalnego `force all`. Usunięcia wymagają osobnego `delete_objects` grantu i tombstone; brak pliku/rename nie wystarcza.

## Export i praca CODEX

Eksport zapisuje P w osobnym staging `exports/<observation_id>`, nigdy bezwarunkowo w W. Pierwsza materializacja do pustej ścieżki stosuje exclusive create; odświeżenie istniejącego W jest osobną operacją z kontrolą oczekiwanego hasha i ochroną edycji. W MVP nie automatyzujemy zastępowania istniejących plików na exFAT: bez kwalifikacji atomowego conditional replace eksport pozostaje staging + raport. Skaner używa eventów FS wyłącznie jako podpowiedzi; rozstrzyga SHA treści, nie mtime.

Przy capture: odczytaj członków grupy → zapisz kopie S → policz hashe → sprawdź ponownie cały zestaw W i inventory → utrwal S. Zmiana w trakcie skanu daje `WORKSPACE_CHANGED`, bounded retry capture max 1. Nie deklarujemy atomowego snapshotu dowolnego katalogu aktywnie edytowanego. Apply domyślnie sprawdza expected W przed pierwszą mutacją; edycje po tej granicy są legalne i dotyczą następnej rewizji.

## Sync.plan i Sync.apply jako saga

1. Capture W do S, resolve identity, wyeksportuj świeże P, porównaj B/W/P i capability. Zbuduj zależności (UDT→DB/FB/FC→OB/IDB; HMI moduły/tagi/zdarzenia). Brak pełnej analizy dynamicznych referencji oznacza coverage gap, nie pustą listę zależności.
2. Plan wiąże expected B revision, P fingerprint/inventory, S hashes, adapter/capability versions, object scope, dependency group, wymagane compile/Save/readback, backup policy, uprawnienie i czas ważności. Planowanie jest read-only; hash planu nie nadaje prawa Apply.
3. Dispatcher bierze własność projektu; wykonawca pod ExclusiveAccess ponownie odczytuje P i sprawdza preconditions. Drift → CONFLICT/STALE_PLAN przed mutacją. `IsModified=True` przed pierwszą sesją → REVIEW_REQUIRED, bez automatycznego Save.
4. Przed pierwszą mutacją sesji wykonaj jeden zgodny z API backup i sprawdź integralność, właściwy AP19 i binding do sesji. Kontroluj destination oraz rzeczywiste potrzeby temp. Backup failure blokuje zapis; nie próbuj zapisać dirty projektu, żeby umożliwić Archive.
5. Utrwal `step.intent` przed wywołaniem importu; po powrocie receipt i fresh resolve. Importuj frozen S w kolejności zależności. Każdy krok ma expected before/after. Nie zakładamy transakcyjności grupy; compile może być wymagane przed eksportem zależnych niespójnych bloków.
6. Compile odpowiedni scope, odczytaj diagnostics i zachowaj warnings. Domyślny MVP: 0 errors i 0 niezaakceptowanych warnings. Warnings allowlist musi być wersjonowana i wiązać fingerprint, nie tylko tekstowy fragment.
7. Zweryfikuj scope i chronione zależności, zapisz Save intent, wywołaj Save, potwierdź return i `IsModified=False`. Wykonaj niezależny readback z nowym uchwytem/eksportem, a na kwalifikowanym workflow również potwierdzenie zapisanej rewizji po ponownym attach. Żadne cache pochodzące z inputu nie jest readbackiem.
8. Utrwal verification receipt i B(n+1) w DB CAS względem B(n). Crash przed krokiem 8 pozostawia stare B; reconciliation może rozpoznać wykonany stan i dopisać nowy receipt po odczycie, bez powtarzania importu.
9. Sprawdź obecne W. Jeżeli W≠S, operacja może zakończyć się SUCCEEDED dla S, ale bieżący obiekt ma MODIFIED i raport `workspace_advanced=true`. Nigdy nie cofaj W do S ani nie pokazuj starego SYNCED jako stanu bieżącego.

Dla dużego scope plan składa się z grup spójności. B promuje się tylko dla całej zweryfikowanej grupy z dependency boundary. Awaria jednej grupy nie zmienia faktu ukończenia wcześniejszej, ale cała operacja zgłasza partial work. Grupę, która wymaga wspólnej kompilacji, traktujemy jako jedną jednostkę akceptacji. NonComment XML i nieznane właściwości poza zakresem muszą przetrwać; brak dowodu → UNKNOWN.

## Formalne statusy obiektu

Nie są lifecycle operacji ani wynikiem Runtime. Pole `reason_code` precyzuje pochodzenie.

- **SYNCED**: istnieje B z dodatnim niezależnym verification receipt dla tego `project_key`, epoch/zaobserwowanej rewizji i exact scope; współczesny W ma ten sam unit digest co potwierdzone wejście; świeże P odpowiada potwierdzonemu modelowi w **każdym** wymaganym wymiarze content/config/API/structure/dependencies/inventory; capability i normalizator są kwalifikowane w tym zakresie; nie ma unknown fields w wymaganym porównaniu, unresolved conflict ani quarantine. B oznaczone `saved` wymaga Save receipt albo jawnie zweryfikowanej czystej obserwacji initial baseline. Wymagane przez plan compile/Save/checks muszą być spełnione. Status jest twierdzeniem **as_of observation**, a nie nieograniczoną gwarancją na przyszłość.
- **MODIFIED**: znany B i kompletne obserwacje wskazują zmianę co najmniej jednej strony bez nierozstrzygniętego konfliktu; `side=WORKSPACE|TIA|CONVERGED_PENDING_VERIFY`. Nowy jednoznaczny obiekt W również MODIFIED/NEW_WORKSPACE.
- **CONFLICT**: różne zmiany obu stron, niejednoznaczna tożsamość albo niezgodna resolution decyzja. Nie jest skutkiem samego EOL, jeśli profil dowodowo uznaje EOL za kosmetyczny.
- **UNSUPPORTED**: dana operacja/typ/format nie ma kwalifikowanej capability, niezależnie od istnienia reflection signature.
- **ERROR**: potwierdzony błąd odczytu/walidacji/wykonania z jednoznaczną przyczyną. Wynik mutacji może osobno mieć `outcome=UNKNOWN` i wymagać recovery.
- **UNKNOWN**: brak świeżej obserwacji, required coverage, B, potwierdzonej tożsamości albo nieznany efekt operacji. Restart TIA unieważnia świeżość; cached status raportuje UNKNOWN z last_known_status i wiekiem.

Kolejność agregacji scope: quarantine/nieznany efekt → UNKNOWN; niejednoznaczność/conflict → CONFLICT; błąd → ERROR; brak capability → UNSUPPORTED; znany drift → MODIFIED; SYNCED tylko jeśli wszystkie wybrane żywe obiekty spełniają definicję. Obiekty usunięte raportowane osobno jako tombstones, nie maskowane agregacją. Można raportować wektor per obiekt, aby agregat nie ukrywał różnic.

## Recovery, retry i rollback

Po timeout zapisu, crash hosta/workera lub połowicznym imporcie: zamroź dispatch mutacji, zachowaj intent, S, B, ostatni P i backup, identyfikuj żywe procesy, ustal czy call nadal trwa. Nie zwalniaj kwarantanny tylko z powodu wygaśnięcia lease. Jeśli TIA nie odpowiada, API hosta nadal działa; stan pozostaje UNKNOWN, bez automatycznego restartu/Save/Archive.

Po powrocie API wykonaj read-only reconciling z nowym epoch i inventory. Każdy krok zaklasyfikuj `NOT_APPLIED`, `APPLIED_VERIFIED`, `DIVERGED`, `UNKNOWN`. Zgodny stan po utracie odpowiedzi pozwala **uznać wykonanie po nowej weryfikacji**, nie ponownie je wykonać. Niepewność/DIVERGED wymaga odrębnego planu naprawy i uprawnienia. Idempotency ledger zwraca istniejącą operację; exactly-once przy braku transakcji Siemens+DB jest niemożliwe do obiecania.

Rollback: preferowana odwracalność plikowego stagingu i plan przed importem. Kompensujący reimport wcześniejszej treści jest nową mutacją i może zawieść. Przywrócenie archiwum tylko do **nowej ścieżki testowej**, z jawną decyzją, a nie nadpisanie aktywnego projektu. Publiczne Transaction/CommitOnDispose istnieją w V19, ale ich zakres względem importu/compile/Save/Archive wymaga X-02; MVP nie opiera na nich obietnicy rollbacku.

## Ograniczenia dowodowe odczytu HMI

Zgodnie z [ADR 0003](decisions/0003-hmi-canonical-model-and-native-artifacts.md), niepełny publiczny odczyt Unified nie potwierdza zgodności właściwości, których API nie ujawnia. Brak odczytu nie jest wartością pustą; podgląd, hash lub częściowy readback nie upoważnia do regeneracji ani utraty nieznanych pól. Nie zmienia to definicji `SYNCED`, sagi Apply i zakresów kwalifikacji zapisanych wyżej.

Wersjonowanie: schema major zmienia semantykę i wymaga jawnej migracji z backupem metadanych; minor może dodawać opcjonalne dane. Unknown required feature blokuje starego klienta. Normalizer/capability update nie przepisuje dawnych baseline; wymaga re-verify i nowej rewizji. Stare sesje Bridge importujemy wyłącznie jako historical evidence, nie jako świeży stan ani autoryzację.
