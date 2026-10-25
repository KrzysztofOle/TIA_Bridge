# Scripts

Place repository maintenance and development scripts here. Scripts must not connect to or modify a TIA Portal project unless the command and its safeguards are explicit, documented, and separately reviewed.

## Kontrola pakietu projektowego

`audit_quality.py` jest lokalnym walidatorem dokumentacji i projektowych kontraktów, nie implementacją Bridge. Nie ładuje vendor DLL, nie łączy się z TIA/OPC/HTTP i nie wykonuje kodu znalezionego w dokumentach.

Wymagania: Python 3.12, pakiet `jsonschema` (w analizowanym środowisku dostępny). Jeśli go brakuje, użyj odrębnego środowiska narzędziowego i zainstaluj `jsonschema` zgodnie z lokalną polityką; nie zmieniaj Python używanego przez Siemens lub stare OPC. Pakiet nie jest dependency aplikacji.

Z root repo:

```powershell
py -3 scripts/audit_quality.py
py -3 scripts/audit_quality.py --report .tia-bridge/quality.json
```

Exit 0 = PASS, exit 1 = wykryte niespójności, exit 2 = niepoprawny argument. Raport zapisuje się tylko do ignorowanego `.tia-bridge/`; stdout pokazuje liczby i błędy. Walidator sprawdza lokalne linki/fences i poprawność kolumn tabel, 12 wymaganych dokumentów, 19 metod w registry/schema/docs, deadline/required fields, Draft2020-12, pierwotne syntetyczne przykłady i 8 negatywnych kontraktów, 24 audit rows, 16 resilience cases i 12 etapów. Zewnętrzne odnośniki są liczone, lecz nieodpytywane sieciowo. Nie sprawdza producenta, renderingu Mermaid, semantyki PLC ani pełnego OpenAPI; niezależne testy schematów/OpenAPI B-01A opisuje [instrukcja testów](../tests/README.md).

## Generator kontraktów B-01A

`generate_contracts.py` wymaga wyłącznie standardowej biblioteki Python 3.12. Czyta operation-request.schema.json i operations.json oraz jawne definicje wyników w skrypcie; generuje lokalne schematy/DTO/OpenAPI/przykłady, bez wywołań sieci lub Siemens. Domyślnie tylko sprawdza zgodność wygenerowanych plików, a jawne --write zapisuje zmiany. Exit 0 oznacza zgodność, nie gotowość API. Nie generuje handlerów, hosta ani adapterów.

```powershell
py -3 scripts/generate_contracts.py
py -3 scripts/generate_contracts.py --write
```

Zależności niezależnej walidacji QA określa [requirements-qa.txt](requirements-qa.txt). Instaluj je w osobnym venv na NTFS zgodnie z [instrukcją](../tests/README.md), bez globalnej zmiany Python. Wygenerowane DTO C# kompilują się w Core .NET 10; nie wymagają Siemens. Pliki robocze pozostają w ignorowanym `.tia-bridge/` lub lokalnym środowisku QA poza repo.
