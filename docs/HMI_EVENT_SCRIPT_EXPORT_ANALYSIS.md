# Analiza eksportu skryptów zdarzeń HMI przez Openness V19

Data analizy: 2026-10-29
Zakres: analiza repozytoriów, dokumentacji i lokalnych metadanych API; bez Attach, operacji Engineering, zmian projektu TIA ani testów live.

## Streszczenie

Odczyt treści `ScriptCode` jest technicznie możliwy przez publiczny model Unified w Openness V19: lokalna dokumentacja API V19 deklaruje `HmiScreen`, `HmiScreenItemBase`, `IHmiScript` i `ScriptDynamization`, w tym `ScriptCode` oraz `GlobalDefinitionAreaScriptCode`. Odczyt konkretnego drzewa zdarzeń i kompletność coverage dla wszystkich typów ekranu/elementów wymagają jednak kwalifikowanego eksperymentu. Historyczny kod starego Bridge zawiera odczyt `ScriptCode` przypisanego handlera; nie jest to kwalifikacja bieżącego Workera TIA_Bridge 2.

Istniejący Worker V19 można rozszerzyć o bounded read-only reader, zachowując obecny Host, IPC, trwały ledger, artefakty, binding i default-deny. Osobny proces/adapter dostępu do Siemens nie jest potrzebny ani wskazany. Ograniczony eksperyment laboratoryjny też może użyć osobnego trybu w istniejącym LabExporter, ale obecne narzędzie obsługuje moduły globalne, nie eventy ekranów.

## 1. Poziom pewności i stan dowodów

| Poziom | Ustalenie |
|---|---|
| `SIEMENS_DOCUMENTED_V19` | Lokalny `PublicAPI\V19\Siemens.Engineering.xml` opisuje typy ekranu, elementu, skryptu, właściwości kodu i kompozycje. Oficjalny podręcznik Openness pokazuje dostęp do `HmiSoftware.Screens` i `ScreenItems`. |
| `API_METADATA_VERIFIED_LOCAL` | Metadane lokalnej instalacji V19 zawierają `HmiUnified.HmiSoftware.Screens/Scripts`, `UIBase.Dynamizations/PropertyEventHandlers` oraz `IHmiScript`/`ScriptDynamization.ScriptCode` i `GlobalDefinitionAreaScriptCode`. Jest to deklaracja API, nie wykonanie getterów na projekcie. |
| `CODE_CONFIRMED_HISTORICAL` | Stary Bridge implementuje odczyt drzewa HMI i skryptów event handlers, a kilka ścieżek weryfikuje `EventType` oraz `Script.ScriptCode`. To dowodzi istnienia wcześniejszej ścieżki kodowej, nie jej obecnej niezawodności ani kompletności. |
| `ENGINEERING_QUALIFICATION_UNKNOWN` | W tym zadaniu nie wykonano Attach ani odczytu wskazanego projektu. Nie potwierdzono wyniku dla sześciu zdarzeń T05, pustego handlera, stabilności odczytu, coverage wszystkich widgetów ani braku zmian `IsModified`. |

## 2. Openness V19: możliwości i granice

Lokalnym źródłem metadanych jest `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.xml` i odpowiadające mu `Siemens.Engineering.dll`. Istotne wpisy w XML:

- `HmiUnified.HmiSoftware.Screens` i `.Scripts` (około linii 74644–74650).
- `HmiUnified.UI.UIBase.Dynamizations` i `.PropertyEventHandlers` (około linii 84739–84751).
- `HmiUnified.UI.Dynamization.Script.IHmiScript.ScriptCode` oraz `GlobalDefinitionAreaScriptCode` (około linii 86930–86936).
- Konkretne `ScriptDynamization.ScriptCode`, `GlobalDefinitionAreaScriptCode`, `Async` i `Trigger` (około linii 86990–87002).
- `HmiScreenItemBase.Name` oraz kompozycje/metadane obiektu bazowego; szczegółowy model zależy od typu ekranu i elementu.

Oficjalny Openness manual (V19, rozdział „Functions for accessing the data of an HMI device (Unified)”) pokazuje pobranie `hmiSoftware.Screens[0].ScreenItems[0]`, iterowanie `ScreenItems` i użycie `Find`/`Contains`. To potwierdza publiczny model dostępu do ekranu i elementów, ale samo w sobie nie opisuje serializacji event scriptów. [Dokumentacja Siemens Openness](https://cache.industry.siemens.com/dl/files/802/109773802/att_1007204/v1/TIAPortalOpenness_en-US.pdf)

Oficjalny V19 Programming Reference opisuje lokalne skrypty zdarzeń w edytorze Unified, lecz dotyczy głównie modelu Runtime. Nie należy używać Runtime JavaScript API jako dowodu Openness API. [WinCC Unified V19 Programming Reference](https://support.industry.siemens.com/cs/attachments/109896132/WCC_Unified_ProgRef_enUS_en-US.pdf)

| Pytanie | Wniosek z dokumentacji/API | Co pozostaje do potwierdzenia |
|---|---|---|
| Zdarzenia ekranu | `HmiSoftware.Screens` jest publiczne. Event/dynamization obiekty udostępniają kompozycje i skrypty. | Czy wszystkie zdarzenia ekranu są dostępne przez tę samą kompozycję i czy wymagają osobnego API dla określonych ekranów. |
| Zdarzenia elementu graficznego | Publiczne `ScreenItems` oraz `HmiScreenItemBase`; bazowy UI ma `PropertyEventHandlers` i dynamizations. | Pokrycie klas elementów, kontenerów, faceplate’ów, właściwości i zdarzeń tabeli. |
| Pełny `ScriptCode` | Typy `IHmiScript` i `ScriptDynamization` deklarują właściwość `ScriptCode`. Stary Bridge odczytuje ją z obiektu `Script` handlera. | Czy każda wersja V19/build i każdy rodzaj handlera zwraca kompletny tekst bez transformacji; kod powinien zachować bajty/znaki po serializacji. |
| Deklaracje globalne/importy | `GlobalDefinitionAreaScriptCode` jest deklarowane dla skryptu zdarzenia. | Czy zawsze zawiera wszystkie importy, jak odróżnić pustą deklarację od braku/nieobsługiwanej wartości i czy inne deklaracje są ukryte w innym obiekcie. |
| Nazwa zdarzenia | Historyczne typowane API zwraca `EventType`; enumy zdarzeń i `GetValidEnumValues()` występują w kodzie starego Bridge. | Znormalizowane mapowanie enumów per typ obiektu oraz stabilność nazw między buildami. |
| Parametry zdarzenia | API ujawnia skrypt i typ zdarzenia; treść skryptu w praktyce może być samym body. | Nie potwierdzono osobnego publicznego metadanych listy parametrów ani ich kolejności/typów dla każdego eventu. Nie wyprowadzać parametrów z sygnatury dopisanej przez generator. |
| Brak handlera vs pusty kod | Można enumerować event handler objects i odczytać `ScriptCode`. | Niepotwierdzone, czy skonfigurowane zdarzenie bez kodu istnieje jako obiekt z pustym stringiem, czy jest nieobecne w kompozycji. Również enum obsługiwanych typów nie dowodzi konfiguracji. |
| Metadane porównawcze | Dostępne są nazwy obiektów, rodzaje obiektów, typ zdarzenia, kod, global definition area i `Async`. | Stabilne identyfikatory vendor, kompletność hierarchii/nazw i kontrola zmian w czasie wymagają eksperymentu. |

**Odpowiedź techniczna:** publiczne API ma kształt wystarczający do zbudowania readera dla `ScriptCode`; odczyt wszystkich eventów i pełna semantyka braku/pustości są obecnie `UNKNOWN` do czasu kontrolowanej kwalifikacji. Nie wolno wywnioskować ich z eksportu globalnych modułów.

## 3. Rozwiązania istniejące

### TIA_Bridge 2 (`X:\Projects\TIA_Bridge`)

- `src/TiaBridge.Worker.V19/VendorReader.cs` wykonuje Attach i bounded discovery urządzeń, `DeviceItems` i `SoftwareContainer`. Klasyfikuje rodzinę Unified, ale nie schodzi do `HmiSoftware.Screens`, `ScreenItems` ani eventów.
- `src/TiaBridge.Worker.V19/README.md` jawnie wskazuje brak native HMI reader, eksportu, importu i pełnego inventory. `project.inspect` jest jedyną obsługiwaną metodą; pozostałe pozostają `UNSUPPORTED`.
- Host posiada już async admission, trwałe operacje/artefakty SHA-256, wersjonowane DTO/IPC, binding procesu/projektu, scheduler i default-deny. To jest właściwa ścieżka przyszłego odczytu.
- Raporty X-01/D-11/B-03A są ograniczonymi/historycznymi dowodami odczytu discovery/Attach/Dispose; nie kwalifikują odczytu skryptów zdarzeń ani gotowości produkcyjnej Workera.

### Laboratoryjny LabExporter (`X:\TESTY_TIA_Bridge`)

- `src/LabExporter.cs` enumeruje `HmiSoftware.Scripts` i wykonuje `HmiScriptModule.Export(...)`, czyli eksportuje globalne moduły do natywnych `.hmi.js` i `.hmi.yml`. Ma kontrolę staging, tożsamości, czystego `IsModified`, hashe i raporty.
- Testy/rzeczywiste wyniki LAB-03J z 2026-10-29 potwierdzają eksport/import/readback sześciu modułów globalnych w laboratorium. Nie testowały eventów ekranowych. Wynik nie jest kwalifikacją TIA_Bridge 2.
- LAB-03I miał nieudany rzeczywisty eksport przed publikacją z powodu układu natywnych plików `JSON_Test_screen`; późniejszy LAB-03J rozwiązał ścieżkę modułów. Dowodzi to potrzeby zachowania natywnego rezultatu i braku założeń o layout.
- LabExporter można wykorzystać jako odrębny, ręcznie uruchamiany eksperyment wyłącznie po osobnym zatwierdzeniu zakresu Engineering. Samo dopisanie eksportu eventów nie jest uzasadnione bez osobnego readera; istniejący mechanizm modułów nie wystarczy.

### Historyczny T05_TIA_Bridge (`C:\RMA\t05_plc\tools\T05_TIA_Bridge.py`)

- `tools/t05_tia_bridge/hmi.py` zawiera `hmi-script-export`, które eksportuje natywny moduł globalny; jego opis mówi, że V19 screen transfer nie jest zweryfikowany.
- `hmi.ps1` zawiera bounded `Hmi-Tree`: enumeruje `GetCompositionInfos()`, `GetComposition()`, czyta jawnie allowlistowane właściwości `ScriptDynamization` w tym `Async`, `ScriptCode`, `GlobalDefinitionAreaScriptCode`, a także wartości atrybutów i błędy odczytu.
- `hmi_script_export.ps1` eksportuje wyłącznie `HmiScriptModule` i wymaga dokładnych plików modułu `.hmi.js/.hmi.yml`.
- `navigation.ps1`, `move_test.ps1` oraz `recipe_table_rowselected.ps1` odczytują `EventHandlers`, `EventType` i `Script.ScriptCode`; przykładowo istnieje kontrolowany readback handlera `RowSelected`. Część pozostałych ścieżek w starym Bridge wykonuje writes/Compile/Save i nie jest do przenoszenia ani uruchamiania w tej analizie.
- Wniosek: stary Bridge to materiał referencyjny dla ścieżek API i struktur, nie komponent do kopiowania. Szczególnie nie przenosić T05-owych nazw, uprawnień, adaptera PowerShell ani logiki mutacji.

## 4. Przykład `T05_Recipes_Main`

`tools/build_recipe_catalog.mjs` generuje `main-table-events.json` dla `T05_Recipes_Main` (`Loaded`, `Unloaded`), `ACTION_0/1` (`Tapped`), `RecipeTable_Main` (`RowSelected`) i `NAV_1` (`Up`). Jest to oczekiwany kod źródłowy/generated target, nie obserwacja z bieżącego projektu TIA. Jego rekordy zawierają `name`, `action`, `event`, `global` i `code`; `action` jest metadanymi generatora, nie danymi, które Openness powinno odczytać.

Porównywacz powinien mapować po hierarchicznym locatorze HMI + nazwie obiektu + typie obiektu + zdarzeniu, nie po generatorowym `action` ani samych nazwach bez kontekstu. Zdarzenie nieobecne w TIA względem oczekiwanego pliku może być wykryte dopiero po potwierdzeniu kompletnego inventory eventów w danym zakresie. Wynik przy niepełnym coverage musi być `UNKNOWN`/`PARTIAL`, a nie `MISSING`.

## 5. Proponowana architektura TIA_Bridge 2

1. **Operacja Host** — dodać przyszłą, osobno zatwierdzoną read-only metodę do kontraktu Host i registry z wąskim zakresem: dokładny project binding, urządzenie Unified i bounded screen/event scope. Host przyjmuje ją asynchronicznie, utrwala intent/idempotency/operation ID, nie wykonuje vendor calls w HTTP.
2. **Worker V19** — rozszerzyć obecnego `TiaBridge.Worker.V19` o typed, allowlisted traverser: dokładny `HmiSoftware` → screens → screen items / wymagane UI objects → EventHandlers/PropertyEventHandlers → skrypt. Jeden właściciel sesji wykonuje wszystkie proxy calls; żadnego dodatkowego Siemens process/adaptera.
3. **DTO/IPC** — zwrócić tylko wersjonowany, bounded DTO bez proxy Siemens. Każdy rekord ma `read_status`, `coverage`, typy/locator i jawne błędy/unsupported. Błąd pojedynczego obiektu nie może stać się pustą listą ani PASS całego ekranu.
4. **Artefakty** — Host zapisuje niezmienny JSON/manifest jako artifact przez istniejący store, SHA-256, rozmiary i provenance. Nie zapisuje automatycznie do edytowalnego Workspace i nie nadpisuje plików.
5. **Tożsamość** — wiązać ProjectKey wraz z potwierdzonym exact AP19/PID/start/SID/buildem Workera, device identity, rodzajem Unified i zaobserwowanymi ścieżkami ekranu/elementu. Same nazwy `HMI_JSON_TEST`, `HMI_RT_1` lub `T05_Recipes_Main` nie są trwałym ID.
6. **Autoryzacja/capability** — default-deny; osobna capability `READ`/`EXPORT` dla V19 + Unified + object/event scope + coverage CONTENT/API/STRUCTURE. Dotychczasowe X-01 discovery evidence ani D-11 nie nadają jej. Live read wymaga bramek B-02A–D, aktualnego E1 dla dokładnego operation/profile/build/scope, świeżego bindingu i osobnego zatwierdzenia zakresu. Operacja nie może wykonywać Save/Compile/Import/Download.
7. **Porównanie** — osobna, offline'owa funkcja porównuje artifact z repo i zwraca rozłączne statusy (`MATCH`, `DIFFERENT`, `MISSING`, `UNKNOWN`, `UNSUPPORTED`, `PARTIAL`). Tylko `MISSING` po kompletnym coverage oznacza potwierdzoną brakującą konfigurację.

`IsModified=True` przed rozpoczęciem odczytu powinno w pierwszej wersji zatrzymać traversowanie z `REVIEW_REQUIRED`; nigdy nie Save. Po odczycie ponownie sprawdzić project identity i `IsModified`, a zmianę stanu uznać za review/failure, nie za czysty read. Getter exceptions, limity, cancellation/timeout i utrata Workera muszą pozostać widoczne w ledgerze; timeout nie dowodzi zatrzymania wywołania.

## 6. JSON do porównania — propozycja, nie zatwierdzony kontrakt

ADR 0003 wymaga oddzielenia **CANONICAL**, **SOURCE** i **NATIVE**, nie zatwierdza nowego schematu eksportu eventów ani nowego v1 DTO. Poniższy szkic to propozycja do osobnej decyzji/kontraktu. JSON jest kanonicznym *candidate projection* dla semantycznego porównania; natywne artefakty Siemens pozostają osobne, surowe i niezmienne. Nie nazywać go obowiązującym formatem kanonicznym przed zatwierdzeniem.

```json
{
  "schema_version": "tia-bridge.hmi-event-snapshot/0.1",
  "capture": {
    "observation_id": "uuid",
    "operation_id": "uuid",
    "captured_at_utc": "2026-10-29T12:00:00Z",
    "status": "PARTIAL",
    "tia": {"version": "19", "build": "observed"},
    "adapter": {"id": "v19.hmi.event.reader", "build_sha256": "..."},
    "project": {"project_key": "...", "name": "...", "ap19_path_hash": "..."},
    "device": {"observed_locator": ["HMI_JSON_TEST", "HMI_RT_1"], "family": "UNIFIED"},
    "coverage": {"screens": "COMPLETE", "screen_items": "PARTIAL", "event_content": "PARTIAL"}
  },
  "screens": [{
    "locator": ["observed screen group", "T05_Recipes_Main"],
    "name": "T05_Recipes_Main",
    "object_type": "observed Siemens type",
    "read_status": "READ",
    "events": [{
      "object": {"name": "T05_Recipes_Main", "object_type": "observed Siemens type", "locator": ["..."]},
      "event": {"name": "Loaded", "vendor_value": "...", "parameters": {"status": "UNKNOWN"}},
      "read_status": "READ_SCRIPT",
      "script_code": "...",
      "global_definition_area_script_code": "import * as RecipeCatalog from \"T05RecipeCatalog\";",
      "imports": [{"specifier": "T05RecipeCatalog", "status": "PARSED_FROM_GDA"}],
      "async": false,
      "sha256": {"script_code_utf8": "...", "global_definition_area_utf8": "..."}
    }]
  }],
  "issues": [{"locator": ["..."], "status": "UNSUPPORTED", "reason": "..."}]
}
```

Rules for the proposal:

- Preserve code exactly as the API string; hash UTF-8 serialization with a declared encoding/newline policy. For full fidelity also retain raw native export files only if a documented native event export becomes available; do not invent one.
- Keep `global_definition_area_script_code` as the source value. `imports` is a derived convenience list, parsed without executing JavaScript and never a replacement for the raw declaration string.
- Parameter metadata defaults to `UNKNOWN`; do not infer it from JS body text or the source generator's `action`.
- Distinguish `HANDLER_ABSENT`, `HANDLER_PRESENT_EMPTY`, `READ_SCRIPT`, `READ_FAILED`, `UNSUPPORTED`, and `UNKNOWN` only after each state is empirically defined. Until then use `UNKNOWN` for ambiguous absent/empty cases.
- Keep API snapshot/report as an immutable artifact; promotion to ADR 0003 CANONICAL/SOURCE/NATIVE roles and Workspace paths requires a later approved contract. This proposal must not introduce a competing `WorkspaceObject` schema or claim `SYNCED`.

## 7. Laboratory exporter vs target implementation

**Laboratory:** a small manually invoked diagnostic command is feasible in LabExporter as a controlled engineering experiment, reusing its target project checks, staging-only output, before/after `IsModified`, unique artifacts and SHA-256 manifest. It should first capture a narrow known test screen and one graphic item, compare API getters with TIA Inspector values, and exercise present script, empty handler, absent handler, event enum and unsupported object. Do not fold it into the current global-module command implicitly. Live run requires its own approval and isolated copy; this analysis did not authorize it.

**TIA_Bridge 2:** the destination is the existing V19 Worker and durable Host path, not LabExporter code or a parallel adapter. Implement only after mandatory fake-worker B-02A–D gate, an approved package, contract/ADR review, exact current E1 evidence for the operation and scope, and a separately authorized Engineering qualification. D-11 and old T05 experiments are not substitutes.

## 8. Test/qualification scenarios for a future task

The following are proposed future acceptance cases, not executed in this task:

1. Synthetic DTO/contract: screen and item locators, event name, exact code/GDA strings, empty strings, Unicode, CRLF/LF, code/GDA independent hashes, bounded sizes/depth, malformed response and unknown fields.
2. Fake Worker/Host: admission durability, duplicate idempotent submit, restart, worker hang/timeout, late result, worker loss, health/status availability, cancellation before dispatch, artifact hash/ACL and quarantine on uncertain outcomes.
3. Offline comparator: expected six handlers; exact matches, changed body/import, extra handler, absent handler only under complete coverage, incomplete page/object coverage remains `UNKNOWN`, duplicate names are disambiguated by locator/type, no execution of scripts.
4. Isolated V19 experiment: screen events and item events across target types (`Loaded`, `Unloaded`, `Tapped`, `RowSelected`, `Up`), known present/empty/absent states, event valid-value enums, nested items and special containers; compare direct values against TIA UI and record exact V19 build.
5. Read-only integrity: clean project before, same exact process/project/device after, `IsModified=False` before/after, no Save/import/compile calls, no output overwrite, fresh artifact hashes and per-object errors retained.
6. Qualification matrix: exact API/adapter build × Unified version/build × object family × event family × coverage dimension; unsupported cases remain deny-by-default. A successful read is not round-trip, mutating capability, production qualification or `SYNCED`.

## 9. Risks and open questions

- **API class ≠ supported complete model.** The object model is extensible and per-object compositions vary; typed allowlists and actual `GetCompositionInfos`/`GetInvocationInfos` observations are necessary.
- **Events vs property handlers.** `EventHandlers`, `PropertyEventHandlers` and `Dynamizations` can represent different semantics. A screen-event exporter must define which are in scope and avoid collapsing them.
- **No-handler ambiguity.** Empty/missing values may be represented differently or omitted. False `MISSING` is a material comparison risk.
- **Code boundaries.** Openness returns script body plus global definition area; original editor formatting, parameter list, line endings, comments, imports and module relationship may not be fully represented.
- **Names/identity.** HMI object names can collide at different depths and can change. Names/order are locators/evidence, not durable ObjectId.
- **Artifact/provenance.** JSON alone does not preserve native HMI structure. No documented native event export format was found in the inspected V19 public API metadata.
- **Engineering safety.** Current Worker has only bounded discovery qualification; API support does not extend its approved capabilities. Timeouts, changed dirty state, process loss and incomplete coverage must fail closed.
- **Scope creep.** Reusing old Bridge write flows or LabExporter global-module Import/Save would violate this read-only analysis and the intended first reader.

## 10. Minimal first implementation and next single task

Minimal first product increment should be a **read-only diagnostic snapshot for one explicitly bound Unified HMI and a bounded screen/item scope**, returning raw `ScriptCode`, raw global definition area, vendor event kind, object type/name/locator, per-record status/error, coverage, provenance and SHA-256 as an immutable artifact. It should not materialize/overwrite Workspace files, parse JavaScript for authority, compare through `SYNCED`, or include import/Save/Compile/Download.

**Recommended next task:** after verifying the B-02A–D entrance gate status and current HEAD, authorize one isolated engineering qualification package for a prototype read-only reader on a disposable/isolated V19 test project. First acceptance objective: prove screen and graphic-item handler traversal plus the absent-vs-empty behavior for one controlled screen. Record API/build, exact locator, values, `IsModified` before/after, coverage and artifact hashes. If the distinction cannot be established, preserve `UNKNOWN` and keep the comparator from reporting `MISSING`. This task must be separately scoped/authorized; this analysis grants no Attach or live operation.

## Sources inspected

- Local Siemens V19 public API XML: `C:\Program Files\Siemens Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.xml` (notably HmiUnified screens/scripts, UIBase, IHmiScript and ScriptDynamization entries cited above).
- Siemens, *Openness: API for automation of engineering workflows*, V19 HMI Unified data access, screen and `ScreenItems` sections: [official PDF](https://cache.industry.siemens.com/dl/files/802/109773802/att_1007204/v1/TIAPortalOpenness_en-US.pdf).
- Siemens, *WinCC Unified V19 Programming Reference*: [official PDF](https://support.industry.siemens.com/cs/attachments/109896132/WCC_Unified_ProgRef_enUS_en-US.pdf); used only for the distinction between Runtime scripting and Openness.
- TIA_Bridge 2: `src/TiaBridge.Worker.V19/VendorReader.cs`, `src/TiaBridge.Worker.V19/README.md`, `docs/06_TIA_INTEGRATION.md`, `docs/08_IMPLEMENTATION_PLAN.md`, `docs/decisions/0003-hmi-canonical-model-and-native-artifacts.md`.
- LabExporter: `X:\TESTY_TIA_Bridge\src\LabExporter.cs`, `X:\TESTY_TIA_Bridge\README.md`, `X:\TESTY_TIA_Bridge\evidence\LAB-03J-final-report.json` (global script modules only).
- Historical Bridge: `C:\RMA\t05_plc\tools\t05_tia_bridge\hmi.ps1`, `hmi_script_export.ps1`, `navigation.ps1`, `move_test.ps1`, `recipe_table_rowselected.ps1`, and `T05_TIA_Bridge.py`.
- Expected generated representation: `C:\RMA\t05_plc\tools\build_recipe_catalog.mjs`; it generates `main-table-events.json`, not a TIA readback.
