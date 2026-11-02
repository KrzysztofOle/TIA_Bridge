# Offline Source Package Preflight Validator — MVP 1.0

Status: IMPLEMENTED / OFFLINE_TESTED. Kryterium: `MVP_PASS — OFFLINE_SOURCE_PREFLIGHT_VALIDATOR`.
Walidator sprawdza integralność wybranego katalogu przed ewentualnym, osobno
autoryzowanym użyciem TIA. PASS dotyczy wyłącznie plików i zadeklarowanej polityki.
Nie dowodzi importowalności, natywnej struktury HMI, kompilacji, `SYNCED` ani Runtime.

## Architektura i zależności

[source_package_preflight.py](../scripts/source_package_preflight.py) to niewielki
samodzielny moduł/CLI: Python 3.12, stdlib i istniejąca zależność QA `jsonschema`
z [requirements-qa.txt](../scripts/requirements-qa.txt). Nie instaluje zależności.
Używa lokalnego JSON Schema Draft 2020-12, bez zewnętrznych `$ref`, i standardowego
`hashlib.sha256`. Nie uruchamia procesów, skryptów pakietu ani operacji sieciowych.

`WorkspaceValidation` i `ArtifactJson` obsługują typowany WorkspaceManifest,
graf tożsamości oraz ExchangeUnit; nie pasują do dowolnego pakietu źródłowego.
Pozostają bez zmian. Helpery C# nie wymagają uruchamiania .NET z Pythona tylko
do SHA-256. `validate_offline.py` jest runnerem kwalifikacji Fake/Host, nie
walidatorem payloadu; nowe testy wchodzą do jego istniejącego discovery
`tests/contracts`. Host/API/CLI/Worker i schematy Workspace/API nie zmieniają się.
LabExporter nadal jest właścicielem natywnych kontraktów import/export/readback.

## Manifest

Oddzielny addytywny [schemat manifestu](../contracts/source-package/v1/manifest.schema.json):

```json
{
  "schema_version": "tia-bridge.source-package/1.0",
  "required_checks": ["manifest.schema", "manifest.files", "files.sha256", "files.encoding", "files.eol"],
  "files": [
    {
      "path": "module.js",
      "role": "native",
      "type": "text",
      "sha256": "<64 lowercase hexadecimal digits>",
      "text": {
        "encoding": "utf-8",
        "bom": "forbidden",
        "eol": "crlf",
        "final_newline": "required"
      }
    }
  ]
}
```

Powyższy hash jest miejscem na wartość; kompletny poprawny przykład znajduje się
w [pakiecie PASS](../examples/source-package/pass/manifest.json).
Wszystkie pola pokazane powyżej są wymagane dla tekstu. `role` to `source`,
`native` lub `auxiliary`. Rola opisuje przeznaczenie, nie dowodzi obsługi Siemens.
`type` to `text` lub `binary`; binarny plik nie może mieć pola `text`.
Nie rozpoznajemy typu po rozszerzeniu. Każda polityka tekstu jest jawna,
także dla natywnego JS. Nie ma globalnego CRLF ani automatycznej konwersji.

Nieznane pola, brakujące pola, duplicate JSON keys, NaN/Infinity, nieprawidłowy
UTF-8 manifestu i niesparowane Unicode surrogates są odrzucane. Manifest może
mieć UTF-8 BOM. Nieobsługiwana wersja i nieznana wymagana kontrola dają exit 3.
Wszystkie pięć kontroli podstawowych jest zawsze wykonywane, także gdy
`required_checks` jest puste. Pole pozwala zażądać dodatkowego zakresu w przyszłości;
nie wyłącza kontroli MVP. Nieznana kontrola nigdy nie daje PASS.

## Zakres, ścieżki i integralność

`--root` określa cały katalog pakietu, rekurencyjnie. Skanowanie nie wychodzi
do repozytorium nadrzędnego. Manifest wewnątrz root jest jedynym automatycznie
wyłączonym plikiem; nie może deklarować siebie. Manifest zewnętrzny jest
dopuszczalny. Każdy plik pomocniczy/README musi mieć wpis, rolę i hash.
Nie ma wildcardów, ukrytych wyjątków ani listy ignorowanych plików. Puste katalogi
są dozwolone, ale ich nazwy i limity nadal obowiązują.

Ścieżki wpisów używają `/`, są względne, NFC, mają do 240 znaków i 16 segmentów.
Odrzucane są `..`, `.`, puste segmenty, backslash, absolute/drive-relative paths,
ADS, kontrolne/niedozwolone znaki Windows, końcowe kropki/spacje, nazwy urządzeń,
duplikaty również case-insensitive oraz kolizje plik/katalog.
Symlinki, junctions i wszystkie reparse points są zabronione, także wewnątrz root
oraz w przodkach root/manifestu/raportu. Nie są śledzone. Obsługiwane są tylko
zwykłe pliki i katalogi. UNC/device paths oraz mapowane dyski sieciowe Windows
są odrzucane przed odczytem; mechanizm wymaga lokalnego filesystemu.

SHA-256 obejmuje oryginalne bajty, łącznie z BOM i EOL, przed dekodowaniem.
Nie ma normalizacji ani zapisu źródeł. Raport podaje oczekiwany/rzeczywisty hash,
nie zawartość pliku. Hash niezgodny z manifestem daje `SHA256_MISMATCH`.
Pozostałe kontrole tekstu mogą nadal dostarczyć diagnostykę tego samego pliku.

## Encoding i EOL

Tekst jest dekodowany ściśle jako UTF-8. BOM: `required`, `forbidden`, `optional`.
EOL: `crlf`, `lf`, `any`. `any` dopuszcza także mieszankę CRLF/LF; samotny CR
jest zawsze błędem. Plik bez nowej linii spełnia oba style EOL; osobna polityka
`final_newline` (`required`, `forbidden`, `optional`) decyduje o końcowym LF/CRLF.
Pusty plik spełnia `optional`/`forbidden`, nie spełnia `required`.
Niepoprawne kodowanie uniemożliwia kontrolę EOL i cały wynik jest FAIL.
Pliki binarne są hashowane; nie mają wymagań BOM/EOL. Kontrola tekstu może mieć
licznik 0 przy pakiecie wyłącznie binarnym — oznacza poprawny zadeklarowany zakres.

## Uruchomienie i raport

Z katalogu repozytorium, Pythonem z istniejącą zależnością QA:

```powershell
py -3 -B scripts/source_package_preflight.py --manifest examples/source-package/pass/manifest.json --root examples/source-package/pass --json
```

Opcjonalnie `--output <report.json>` tworzy nowy plik poza root pakietu.
Rodzic musi już istnieć. Nie nadpisujemy istniejących plików, symlinków ani hardlinków;
manifest zewnętrzny również jest chroniony. Nie tworzymy katalogów.
Zapis stosuje exclusive create (`xb`). Błąd zapisu daje exit 2 i diagnostykę JSON
na stdout; nie wolno wtedy uznawać wcześniejszego PASS za wynik całej komendy.
Nieudany zapis może pozostawić częściowy plik raportu, który nie jest dowodem PASS.

JSON jest domyślnym stdout; `--json` jest jawnym aliasem tej polityki.
Błędna składnia argumentów używa standardowego argparse (stderr, exit 2).
Raport odpowiada [schematowi raportu](../contracts/source-package/v1/report.schema.json):
`schema_version`, `status`, `exit_code`, `scope` (root, manifest, SHA manifestu),
pięć uporządkowanych `checks` z `id/status/count` i lista `issues`.
Issue zawiera stabilny `code`, względny `path` (lub null), `expected`, `actual`
(lub null) i krótkie `remedy`. Nie ujawniamy treści źródeł ani wyjątków systemowych.
`NOT_RUN` po wcześniejszej odmowie nie oznacza PASS. Nie ma timestampu/run ID;
ten sam nieruchomy pakiet i te same argumenty dają identyczny JSON.

| Exit | Znaczenie |
|---|---|
| 0 | PASS wszystkich wymaganych kontroli we właściwym zakresie |
| 1 | Błąd pakietu: lista, ścieżki, reparse, hash, tekst, limit pliku/inventory |
| 2 | Argumenty, brak/nieczytelny root lub manifest, niepoprawny/za duży manifest, schema, raport |
| 3 | Nieobsługiwana wersja manifestu lub wymagana kontrola |
| 4 | Nieoczekiwany błąd wewnętrzny lub brak zależności/lokalnego schematu |

Najpierw walidowane są wejścia/schema, następnie obsługiwany zakres, lista plików
i zawartość. Wiele problemów zawartości daje exit 1; wcześniejsza odmowa zatrzymuje
zależne kontrole. Błąd publikacji raportu ma pierwszeństwo (2, wewnętrzny 4).
Główne kody: `FILE_MISSING`, `FILE_EXTRA`, `PATH_DUPLICATE`, `PATH_COLLISION`,
`PATH_INVALID`, `REPARSE_POINT_FORBIDDEN`, `SHA256_MISMATCH`, `ENCODING_INVALID`,
`BOM_MISMATCH`, `EOL_MISMATCH`, `FINAL_NEWLINE_MISMATCH`, `MANIFEST_INVALID`,
`MANIFEST_SCHEMA_INVALID`, `MANIFEST_VERSION_UNSUPPORTED`, `REQUIRED_CHECK_UNSUPPORTED`,
`INPUT_LIMIT`, `INVENTORY_LIMIT`, `MANIFEST_LIMIT`, `INPUT_CHANGED`,
`REPORT_PATH_UNSAFE`, `REPORT_WRITE_FAILED`, `VALIDATOR_INTERNAL_ERROR`.

## Trzy demonstracje

Każda używa własnego małego syntetycznego pakietu (JS + plik binarny), bez TIA:

```powershell
py -3 -B scripts/source_package_preflight.py --manifest examples/source-package/pass/manifest.json --root examples/source-package/pass --json
py -3 -B scripts/source_package_preflight.py --manifest examples/source-package/bad-hash/manifest.json --root examples/source-package/bad-hash --json
py -3 -B scripts/source_package_preflight.py --manifest examples/source-package/bad-eol/manifest.json --root examples/source-package/bad-eol --json
```

Oczekiwane wyniki: `PASS/exit 0/issues []`, następnie `FAIL/exit 1/SHA256_MISMATCH`
dla `module.js`, następnie `FAIL/exit 1/EOL_MISMATCH`, expected `crlf`, actual `lf`.
Ostatni pakiet ma poprawny hash LF, więc demonstruje niezależną kontrolę EOL.
[.gitattributes](../examples/source-package/.gitattributes) zachowuje bajty fixtures
przy checkout niezależnie od `core.autocrlf`.

## Testy, limity i dalszy zakres

```powershell
py -3 -B -m unittest discover -s tests/contracts -p test_source_package_preflight.py -v
py -3 -B -m unittest discover -s tests/contracts -p 'test_*.py' -v
py -3 -B scripts/audit_quality.py
```

[Testy](../tests/contracts/test_source_package_preflight.py) obejmują pozytywne/negatywne
pakiety, wszystkie exit codes, realny CLI, niezależne schematy, demonstracje,
niezmienność źródeł, ochronę zapisu/hardlinków, rzeczywiste junctions Windows,
zakaz procesów/sieci i statyczną listę zależności. Pętle/subtests nie są liczone
jako osobne testy. Nie uruchamiają żadnej operacji Siemens.

Stałe limity: manifest 1 MiB, JSON depth 16, 1024 deklarowane pliki, 32 wymagane
kontrole, 4096 wpisów filesystemu (pliki + katalogi + manifest), 16 MiB/plik,
256 MiB odczytanych źródeł. Przekroczenie limitu blokuje PASS.
Pakiet powinien być nieruchomy podczas kontroli. Sprawdzamy reparse przed/po
odczycie oraz identity/size/mtime otwartego pliku i ścieżki, lecz nie tworzymy
snapshotu ani izolacji wobec wrogich współbieżnych zmian drzewa. PASS jest dowodem
odczytanych bajtów; przyszły konsument musi ponownie związać je z niezmiennym S.
Raport nie jest podpisem manifestu ani dowodem autentyczności dostawcy.

MVP nie parsuje składni JS/SCL/XML/YAML, indeksu funkcji ani Events. Format binarny
jest nieprzezroczysty. Nie implementujemy kontraktów LabExporter/T05, receptur,
Task Checkpoint, E2E Reporter, synchronizacji ani integracji Apply.
Następne ograniczone rozszerzenie: jawny indeks funkcji JS, potem osobno
Events → module → handler na zatwierdzonym kontrakcie. Kolejne kontrole można
dodać obok `validate_text` i do listy `CHECKS`, z własnym kontraktem/testami;
nie wprowadzamy systemu pluginów.
