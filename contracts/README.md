# Projekt kontraktów v1

To artefakty projektowe, nie implementacja aplikacji. [Interfejs](../docs/05_CODEX_INTERFACE.md) opisuje semantykę, uprawnienia i błędy. Schemat nie może potwierdzić rzeczywistego odbiorcy TIA, licencji ani izolacji symulatora.

- [operations.json](v1/operations.json):19 nazw metod, wymagane pola, permissions, default/max deadline i przykładowe kody szczególne.
- [operation-request.schema.json](v1/operation-request.schema.json): transport-neutral request, params per method; wymaganie authorization_ref przy Apply i test write.
- [operation.schema.json](v1/operation.schema.json): rdzeń status/result envelope; SUCCEEDED nie może mieć UNKNOWN outcome, RECONCILIATION_REQUIRED ma UNKNOWN.

Pełne OpenAPI i wszystkie DTO wynikowe mają powstać w B-01A; obecne schematy nie udają kompletnego API produkcyjnego. Szczegółowe snapshots/plan/baseline opisuje dokument04, a ich schematy są częścią następnych pakietów. [Przykłady](../examples/contracts/request.sync-apply.json) są syntetyczne, z zerowymi hashami; nie są wykonywalnymi planami ani grantami.

Kontrola: `py -3 scripts/audit_quality.py` z root repo, Python3.12 + jsonschema. Walidator nie pobiera zdalnych `$ref` i nie uruchamia TIA. Zmiana metody wymaga spójnej aktualizacji schema/registry/dokumentu05 i testów klientów w przyszłej implementacji.
