# TiaBridge.Cli — nieblokujący klient B-02C

Klient .NET 10 istniejącego lokalnego HTTP API, bez własnej kolejki, SQLite lub wykonawcy. [Dokumentacja, pełny cykl i przykład](../../docs/15_HTTP_API_CLI.md), [testy procesowe](../../tests/README.md).

- `submit METHOD --request FILE --json`: istniejący wersjonowany request; domyślnie wraca po durable acceptance.
- `operation status ID`, `operation result ID`, `operation events ID [--after-sequence N --limit N]`.
- `operation cancel ID [--reason TEXT]`: trwały cancel intent, bez kill procesu i bez auto-clear UNKNOWN.
- `operation wait ID --max-wait N` lub `submit ... --wait --max-wait N`: tylko jawne oczekiwanie 1..3600 s.
- `health`, `--discovery PATH`, `--json`.

Domyślny discovery jest na lokalnym NTFS w LOCALAPPDATA/TIA_Bridge2/state/api/host.json. Token i SID/PID/start są odczytywane wewnętrznie po kontroli owner/ACL; brak flag token/URL i sekretów w argv/logach. Dla syntetycznego dev przykładu podaj jego discovery przez --discovery. Nieobsługiwany Host lub zła odpowiedź daje kontrolowany JSON.

Kody procesu: 0 acceptance/udany odczyt, 2 argumenty/kontrakt, 3 odmowa auth/policy/conflict/unsupported, 4 transport/Host, 5 bounded wait bez terminalnego wyniku, 6 terminal FAILED/RECONCILIATION_REQUIRED dla result/wait. Zamknięcie/Ctrl+C/Dispose klienta nie anuluje zaakceptowanej operacji. Nie ma automatycznego retry, force lub grantów Siemens; kod 0 submit nie oznacza sukcesu projektu. Skróty --workspace/--scope nie są jeszcze implementowane — użyj pliku request JSON.
