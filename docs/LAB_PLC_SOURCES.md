# LabExporter — czytelne źródła PLC

2026-11-04: rozszerzono wyłącznie laboratorium `X:\TESTY_TIA_Bridge` o `plc-source-export`. Kontrakt: `X:\TESTY_TIA_Bridge\PLC_SOURCES.md`; wyniki i status kwalifikacji: `X:\TESTY_TIA_Bridge\PLC_SOURCES_REPORT.md`.

Źródła pochodzą z V19 `GenerateSource` dla pojedynczego obiektu, `GenerateOptions.None`: `.scl` dla FC/FB/OB SCL, `.awl` dla STL, `.db` dla DB i `.udt` dla PLC types, gdy generator obsługuje dany obiekt. LAD/FBD/GRAPH są jawnie XML-only, software units są blokowane; `.nvt` pozostaje nieobsługiwane. Nie ma rekonstrukcji pseudokodu z XML. Bajty generatora zachowuje się wraz z oryginalnym XML; nie jest to dowód pełnego odwzorowania wszystkich ustawień w źródle.

Oddzielny workspace zawiera `Program blocks/`, `PLC data types/`, techniczne `.native/` i `.plc-sources.json`. Nazwy i foldery są czytelne, z odwracalnym escape niedozwolonych znaków Windows. Dwa odczyty, manifest SHA-256, snapshot/CAS, kontrola lokalnych edycji/kolizji i zachowanie poprzedniego katalogu chronią publikację. Zniknięcia i przeniesienia wymagają przeglądu, bez automatycznego usuwania. Powtórny eksport niezmienionego modelu zachowuje bajty i mtime. Produkcyjny manifest/baseline i definicja SYNCED pozostają bez zmian.

Nowe polecenie obsługuje tylko `tia-to-repo`/`compare` z pełnym `--blocks '*'`; `repo-to-tia` i flagi importu są odrzucane przed Attach. Przyszłą synchronizację opisano jako B/W/P/S, preservation metadanych/interfejsów/komentarzy, dependency closure, indywidualny plan i grant, backup, niezależny readback i CAS. Nie zaimplementowano adaptera CreateFromFile/GenerateBlocksFromSource. Dotychczasowe ścieżki XML PLC i UDT oraz ich kwalifikacje pozostają odrębne.

Brak zmian Core/Host/Worker, capabilities, [architektury](03_ARCHITECTURE.md), [synchronizacji](04_SYNCHRONIZATION.md), [interfejsu](05_CODEX_INTERFACE.md), [integracji](06_TIA_INTEGRATION.md) i [kolejności MVP](08_IMPLEMENTATION_PLAN.md). Eksport nie daje uprawnień do Compile/Save, Runtime, Download lub fizycznego PLC/HMI.
