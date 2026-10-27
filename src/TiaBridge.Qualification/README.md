# TiaBridge.Qualification — E1 Local Trusted v1

Lokalne narzędzie operatora .NET 10/Windows. Komendy: `inspect`, `approve`, `enroll`, `list`, `revoke`, `verify`. Nie jest klientem HTTP ani serwerem administracyjnym. Host nie zależy od tego projektu; narzędzie korzysta ze wspólnych walidatorów Host i istniejącego modelu Core. Tylko lokalny proces operatora otwiera istniejący nieeksportowalny klucz CNG P-256; nie ma generowania kluczy, PKI, automatycznej rotacji ani modyfikacji ACL.

Zależności: te same przypięte zależności Host/Core z packages.lock.json, .NET/ASP.NET Core 10, Windows i lokalny NTFS. Build z root repo po przygotowaniu cache: `dotnet build TiaBridge.slnx -c Release --no-restore`. Brak Siemens DLL i dodatkowych pakietów kryptograficznych.

[Instrukcja operatora, ACL i klucze](../../docs/18_LOCAL_TRUSTED_ENROLLMENT.md), [architektura i format](../../docs/17_TRUSTED_QUALIFICATION_EVIDENCE.md), [raport offline](../../docs/B03A_E1_L_LOCAL_TRUSTED.md). Przykład wywołania: `dotnet TiaBridge.Qualification.dll --config <protected-host-config> inspect <evidence-id> <revision>`. Narzędzie nie przyjmuje ścieżki pakietu; wybiera go z zamkniętego inbox istniejącego qualification-evidence.
