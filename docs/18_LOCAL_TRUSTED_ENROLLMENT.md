# E1 Local Trusted v1 — lokalna procedura operatora

Zakres: przygotowanie deploymentu i lokalna obsługa zaufanych dowodów. [Architektura i granice](17_TRUSTED_QUALIFICATION_EVIDENCE.md). Instrukcja nie jest zgodą na utworzenie klucza produkcyjnego, ustawienie ACL, rejestrację rzeczywistego Engineering lub uruchomienie Siemens. CODEX nie wykonał tych czynności. W bieżącej implementacji wszystkie dowody testowe są syntetyczne.

## Tożsamość i ACL

Ustal rzeczywisty SID procesu Host oraz SID operatora. Nie zakładaj LocalSystem ani konta zalogowanego na podstawie nazwy folderu. Obecny state root pozostaje pod `%LOCALAPPDATA%/TIA_Bridge2` właściwego użytkownika na lokalnym stałym NTFS, poza repo. Przetestowany wariant Local Trusted v1 używa tego samego konta Windows dla Host i operatora. Wariant usługowy z różnymi kontami wymaga osobnego przeglądu deploymentu i istniejącego ograniczenia per-user StorageLocation; nie jest kwalifikowany w tym zadaniu.

W zatwierdzonym deploymentcie operator ręcznie przygotowuje **nowy, pusty** `<state_path>/qualification-evidence`, chroniony przed dziedziczeniem obcych reguł, oraz pusty `lifecycle.jsonl`. Nigdy nie twórz pustego journalu w miejscu utraconej historii istniejącego magazynu. Nie usuwaj lub nie skracaj wcześniejszego journalu ani katalogów records/staging.

Wymagana polityka:

| Obiekt | Wymaganie |
|---|---|
| Root qualification-evidence | Chroniona DACL; owner operator, SYSTEM albo Administrators; operator FullControl z dziedziczeniem do plików/katalogów; Host bezpośrednie ReadAndExecute |
| Konfiguracja startowa Host | Chroniona DACL; zatwierdzony owner; operator zapis, Host bezpośredni odczyt |
| Potomkowie magazynu | Dziedziczą bezpieczne reguły root; brak obcych allow i brak deny; jawne wyjątki nie mogą poszerzać dostępu |
| Katalogi nadrzędne | Brak obcego ownera i uprawnień usunięcia/podmiany/zmiany właściciela; bezpośredni rodzic bez obcego tworzenia plików/katalogów |
| Systemowe konta administracyjne | SYSTEM i Administrators mogą być zaufanymi administratorami; TrustedInstaller jest dopuszczony dla systemowych przodków |
| Różne SID-y | Host bez zapisu do magazynu, także przez aktywne grupy tokenu; sam wpis Read obok grupowego FullControl nie wystarcza |

Jeżeli Host i operator mają ten sam SID, jedna bezpośrednia reguła FullControl operatora zapewnia również odczyt Host. Windows ACL nie odróżnia procesów tego samego konta. Nie deklaruj izolacji od wrogiego procesu tego SID lub administratora. Zwykły klient HTTP/CLI nie otrzymuje przez token transportowy prawa lokalnego enrollment; nowe narzędzie sprawdza SID operatora.

Po osobnym zatwierdzeniu administrator może zastosować powyższą DACL standardowymi narzędziami Windows lub FileSystemAccessRule. Sprawdź `Get-Acl -LiteralPath <path>` dla root, konfiguracji, lifecycle i rzeczywistych plików; sprawdź również przodków. Narzędzie wyłącznie weryfikuje i odmawia pracy przy rozbieżności. Nie ma komendy auto-fix ACL, wyłączania UAC/AV ani obchodzenia odmowy.

## Jeden lokalny klucz

Klucz przygotowuje operator dopiero po osobnej decyzji użytkownika. Narzędzie enrollment **nie generuje** kluczy. Korzysta z istniejącego user-scoped klucza ECDSA P-256 w Microsoft Software Key Storage Provider, z `ExportPolicy=None`, wyłącznie do podpisu. Nie używa certyfikatów CA, plików prywatnego PEM/PFX, HTTP ani sekretu w argv. `--key-name` jest niesekretnym identyfikatorem istniejącego klucza.

Poniższy przykład jest instrukcją do późniejszego, jawnie zatwierdzonego ręcznego wykonania w sesji operatora; nie uruchamiano go w tym zadaniu:

```powershell
$qualificationKeyName = 'TIA_Bridge2.LocalQualification.v1'
$qualificationKeyParameters = [Security.Cryptography.CngKeyCreationParameters]::new()
$qualificationKeyParameters.Provider = [Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider
$qualificationKeyParameters.KeyCreationOptions = [Security.Cryptography.CngKeyCreationOptions]::None
$qualificationKeyParameters.ExportPolicy = [Security.Cryptography.CngExportPolicies]::None
$qualificationKeyParameters.KeyUsage = [Security.Cryptography.CngKeyUsages]::Signing
$qualificationKey = [Security.Cryptography.CngKey]::Create(
    [Security.Cryptography.CngAlgorithm]::ECDsaP256, $qualificationKeyName, $qualificationKeyParameters)
try {
    $qualificationSigner = [Security.Cryptography.ECDsaCng]::new($qualificationKey)
    try { [Convert]::ToBase64String($qualificationSigner.ExportSubjectPublicKeyInfo()) }
    finally { $qualificationSigner.Dispose() }
}
finally { $qualificationKey.Dispose() }
```

Użyj PowerShell na współczesnym .NET (`pwsh`), aby `ExportSubjectPublicKeyInfo` było dostępne; nie używaj do tej instrukcji starego Windows PowerShell/.NET Framework. Sprawdź nazwę, user scope, P-256 i nieeksportowalność klucza, następnie niezależnie porównaj publiczne SPKI z przypinanym wpisem. Nie stosuj OverwriteExistingKey; istniejącego klucza nie zastępuj automatycznie. Prywatny materiał pozostaje w magazynie CNG Windows, a uchwyt jest otwierany tylko w lokalnym procesie operatora podczas enroll. API i Host nie otrzymują prywatnego materiału.

Standardowe API: [CngKey.Create](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkey.create), [parametry CNG](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkeycreationparameters). W testach używano kluczy efemerycznych w pamięci, nie produkcyjnego kontenera CNG.

W chronionej konfiguracji Host umieść fragment z rzeczywistymi, zatwierdzonymi wartościami:

```json
"qualification_trust": {
  "trusted_signer_keys": ["local-operator=<base64-SPKI>"],
  "operator_sid": "<actual-operator-SID>",
  "host_sid": "<actual-Host-SID>"
}
```

To fragment instrukcyjny z placeholderami, nie gotowa konfiguracja ani grant. Host zna tylko klucz publiczny. Brak anchorów oznacza odmowę kwalifikacji. Domyślnych konfiguracji repo nie aktywowano.

## Pakiet i review

Operator przygotowuje pakiet wyłącznie pod `qualification-evidence/inbox/<evidence_id>/$qualificationRevision/`. `evidence.json` jest istniejącym modelem Core QualificationEvidence w JSON snake_case z nazwami enumów Core, nie requestem HTTP. Artefakty są nazwami liści, z hashami SHA-256 dokładnych bajtów. Wymagane pola:

| Grupa | Pola do przeglądu |
|---|---|
| Tożsamość/provenance | evidence_id, revision, kind, source, provenance, result, run_id, created_at_utc |
| Buildy | tia_build, adapter_id, adapter_build, worker_build |
| Odbiorca i operacja | project_key, project_class, operation, scope, device_family, object_kind |
| Ograniczony odczyt | profile, read_scope, coverage |
| Bajty dowodów | artifacts: name i sha256 każdego pliku |

Dla v1 dopuszczony jest wyłącznie PASS/ProjectInspect/PROJECT dla dokładnego zarejestrowanego ProjectKey, obecnego DiscoveryV1 i jego zamkniętego read scope. READ nie daje EXPORT/IMPORT/WRITE. Testowe OFFLINE_TEST i efemeryczny signer nie są dowodem rzeczywistego Engineering. Przed rzeczywistym enrollment niezależnie sprawdź źródłowy run, buildy, project binding, izolację i akceptację zakresu; samo pole PASS nie wystarcza.

## Polecenia operatora

Zależności: Windows, zbudowane Release .NET/ASP.NET Core 10 i przygotowane cache zależności repo. Bez Siemens i nowych pakietów. Poniżej `$qualificationTool` wskazuje zbudowane `src/TiaBridge.Qualification/bin/Release/net10.0/TiaBridge.Qualification.dll`, `$qualificationConfig` — chronioną konfigurację Host poza repo. ID/revision i hash należy wziąć z przeglądanego pakietu, a nie kopiować jako zgodę z przykładu.

```powershell
dotnet $qualificationTool --config $qualificationConfig inspect $qualificationEvidenceId $qualificationRevision
dotnet $qualificationTool --config $qualificationConfig approve $qualificationEvidenceId $qualificationRevision
```

`inspect` pokazuje pełny payload, Eligible i PackageSha256. `approve` bez flag wypisuje ten sam materiał i kończy się odmową, bez zatwierdzenia. Po ręcznym przeglądzie i świadomej decyzji operator wykonuje:

```powershell
dotnet $qualificationTool --config $qualificationConfig approve $qualificationEvidenceId $qualificationRevision --package-sha256 $qualificationReviewedHash --confirm-operator-approval
dotnet $qualificationTool --config $qualificationConfig enroll $qualificationEvidenceId $qualificationRevision --key-name $qualificationKeyName
dotnet $qualificationTool --config $qualificationConfig verify $qualificationEvidenceId $qualificationRevision
dotnet $qualificationTool --config $qualificationConfig list
```

Zatwierdzenie jest create-new i wiąże exact package hash oraz SID. Zmiana dowolnych zatwierdzonych danych wymaga nowej rewizji i nowego review. Enrollment weryfikuje przypięty klucz publiczny, podpisuje snapshot payloadu, kopiuje/weryfikuje i flushuje artefakty, publikuje rewizję bez overwrite, dopiero potem flushuje ENROLLED do lifecycle. `verify` wymaga aktualnej aktywacji, podpisu, hashów i dozwolonego zakresu. `list` pokazuje zweryfikowane aktywne kwalifikacje; nie pomija po cichu uszkodzonego aktywnego rekordu.

CLI zwraca 0 po powodzeniu, 3 i stały `LOCAL_QUALIFICATION_DENIED` po odmowie; nie wypisuje stack trace, prywatnego klucza ani szczegółów systemowych. Nie ponawia automatycznie komendy po błędzie. Odczytaj list/verify i zachowaną historię; częściowe staging/published pliki nie są zgodą na ich adopcję. Nowa rewizja po niepewnej publikacji wymaga review operatora.

## Odwołanie, restart i rotacja

```powershell
dotnet $qualificationTool --config $qualificationConfig revoke $qualificationEvidenceId $qualificationRevision --reason '<operator reason>'
dotnet $qualificationTool --config $qualificationConfig list
dotnet $qualificationTool --config $qualificationConfig verify $qualificationEvidenceId $qualificationRevision
```

Revoke nie wymaga klucza prywatnego. Dopisuje trwałe REVOKED dla dokładnej rewizji; verify ma następnie odmówić. Nie usuwa podpisów, artefaktów, approval, wcześniejszych zdarzeń ani wyników operacji. Odwołanie najnowszej rewizji nie aktywuje starszej. Ponowna kwalifikacja wymaga wyższej rewizji i jawnego enrollment.

Host sprawdza bieżący journal na admission-resolution, przed trwałym dispatch i bezpośrednio przed IPC. Po restarcie odwołanie pozostaje skuteczne bez ponownego enrollment. Nie wykonuj restartu w celu „cofnięcia” wysłanego call; zachowaj UNKNOWN/quarantine i obowiązującą procedurę recovery. Odwołanie nie jest atomową transakcją z IPC i nie zatrzymuje call, który przekroczył końcową granicę dispatch.

Aby wyłączyć mechanizm, operator usuwa publiczne anchory z chronionej konfiguracji (`trusted_signer_keys: []` albo brak qualification_trust) i kontrolowanie restartuje Host według istniejącej procedury. Nie usuwa magazynu ani historii. Zmiana konfiguracji nie przeładowuje automatycznie klucza w już działającym Host. Brak automatycznego wygasania jest świadomą decyzją v1; wymagany jest ręczny przegląd po zmianie builda, projektu lub zakresu.

Rotacja jest ręczna: odwołaj aktywne kwalifikacje starego signera, zachowaj historię, po osobnej decyzji przygotuj nowy lokalny klucz, przypnij tylko jego publiczny klucz, kontrolowanie zrestartuj Host i zatwierdź/enroll wyższe rewizje. Nie zmieniaj SID operatora w istniejącym journalu jako obejścia migracji; zmiana konta jest osobnym przeglądem deploymentu. Nie ma automatycznej odnowy, CRL ani PKI.

## Warunki rozpoczęcia E2

Aktualizacja E2-B0: [qualification-only](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md)
rozwiązuje bootstrap pierwszego B1 bez wcześniejszego PASS: lokalny dokument operatora,
exact hash/binding/build/okno, trwałe zużycie maks. 20 cykli. Bez klucza CNG i bez
aktywacji capability E1. W normalnym trybie nadal obowiązują real source evidence i
enrollment. Rejestracja kandydackiego PASS po eksperymencie pozostaje osobną decyzją.

Bieżący pakiet przygotowania i raport bootstrapu: [E2-A](B03A_E2_A_PREPARATION.md),
[operator przed B1](E2_OPERATOR.md). Kolejność: [E2-B1](E2-B1_TEST_PLAN.md)
`T05_RecipeSim_V19_Testowy`, dopiero po jego qualification PASS i osobnym preflight
[E2-B2](E2-B2_TEST_PLAN.md) `T04_T05-000_Testowy`. Brak exact źródłowego dowodu
blokuje zwykły dispatch; projekt eksperymentu B0 został zatwierdzony do implementacji
offline, lecz rzeczywisty grant wymaga osobnej decyzji użytkownika.
Pakiet E2-A nie zmienia polityki provider/enrollment i nie nadaje nowego wyjątku.

W obu trybach wymagane są: oddzielne zadanie i jawna autoryzacja bounded Engineering,
aktualna ograniczona D-11 dla właściwego buildu/profilu, świeży preflight
PID/start/executable/project path/ProjectKey/IsModified, exact DLL i Worker SHA,
sprawdzone ACL realnego konta Host, ograniczony odczyt, monitoring i UNKNOWN/quarantine.
Normalny dispatch dodatkowo wymaga niezależnego review rzeczywistego source evidence
i operatorowego enrollment. Qualification-only B0 wymaga zamiast wcześniejszego
PASS jawnego lokalnego grantu z dokumentu 19; nie fabrykuje dowodu E1.

E1-L nie jest takim zadaniem, nie tworzy grantu Siemens i nie rozstrzyga Worker Handle Risk UNKNOWN. Real Engineering evidence NOT_REGISTERED, B-03A Engineering BLOCKED/NOT_RUN, B-03B NOT_READY. NF-11 minimum 8 h pozostaje NOT_RUN.
