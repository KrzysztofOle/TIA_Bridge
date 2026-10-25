# TiaBridge.Client — klient lokalnego API B-02C

Cienka biblioteka .NET 10 nad istniejącym [kontraktem Core](../TiaBridge.Core/README.md). Nie zależy od Host, SQLite lub Siemens. [Opis API, security i discovery](../../docs/15_HTTP_API_CLI.md).

DiscoveryFile sprawdza lokalny NTFS, containment/reparse/ADS, user-only ACL/owner, bounded JSON, wersję i PID/start. BridgeClient używa wyłącznie numerycznego IPv4 loopback, bez proxy/redirect/cookies. Token trafia do Authorization wyłącznie w pamięci; brak tokenu w URL/argv lub logach. Request ≤1 MiB, bounded response ≤10 MiB, osobny deadline HTTP 5 s i walidacja odpowiedzi Core.

Biblioteka nie ponawia submit, nie czeka sama na wynik i nie anuluje operacji podczas Dispose. Domyślny discovery: LOCALAPPDATA/TIA_Bridge2/state/api/host.json; inne jawne dev state wymagają --discovery w CLI. Chroniony token nie nadaje grantów TIA.
