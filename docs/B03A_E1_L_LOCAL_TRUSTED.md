# B-03A/E1-L — Local Trusted v1, implementacja offline

Implementacja i pomiary: 2026-10-26; końcowa weryfikacja i dostawa: 2026-10-27. Baza: `8aa01e01306650d18dcc834868e68aaf66ee5b48`, czyste main równe świeżo pobranemu origin/main. Zakres: zatwierdzony lokalny model E1, kod/testy/dokumentacja i Git; bez Siemens, B-03A/E2, B-03B, produkcyjnego klucza i rzeczywistych dowodów Engineering. [Model](17_TRUSTED_QUALIFICATION_EVIDENCE.md) i [procedura operatora](18_LOCAL_TRUSTED_ENROLLMENT.md).

## Implementacja i zachowane granice

- Osobny `TiaBridge.Qualification`: inspect/approve/enroll/list/revoke/verify, bez HTTP, GUI, PKI lub automatycznych lifecycle działań. Host nie zależy od tego projektu i nie otwiera prywatnego klucza; narzędzie używa istniejących helperów Host/Core i standardowego user CNG P-256/SHA-256. Nie dodano pakietu kryptograficznego ani Siemens DLL.
- Ten sam `<state_path>/qualification-evidence`: inbox, create-new approvals, niezmienne records per ID/revision, staging i append/flush lifecycle.jsonl. Podpisywany jest dokładny payload; zatwierdzenie wiąże package hash, SID operatora i rewizję. Klient nie wybiera ścieżek. Publikacja jest same-volume move bez overwrite, aktywacja następuje dopiero przez trwały ENROLLED. Nie ma adopcji orphanów, auto retry lub odtwarzania brakujących danych.
- Ręczne REVOKED per ID/revision zachowuje wszystkie dowody i historię. Aktualizacja providera E1 wymaga aktywnej najnowszej rewizji, poprawnego podpisu, envelope/package SHA, actual artifact SHA oraz dotychczasowego exact matching Core. Po revoke brak fallback do starszej rewizji. Host rewaliduje przed przygotowaniem dispatch i przed IPC oraz czyta journal ponownie po hashach; restart nie przywraca grantu z admission.
- Windows ACL: rzeczywisty current Host SID, operator SID przed lokalnym zapisem, owner/DACL root i config, ACL otwartych plików i katalogów, aktywne grupy Host, przodkowie z prawem podmiany/usunięcia. Żaden walidator nie zmienia uprawnień. Jedyne zmiany ACL w wykonaniu testów dotyczą nowo utworzonych, własnych UUID fixtures, nie istniejącego deploymentu lub konfiguracji Windows.
- Enrollment ograniczony do registered exact ProjectKey, PROJECT/ProjectInspect, PASS, obecnego DiscoveryV1/read scope i exact buildów. WRITE/IMPORT/EXPORT nie są kwalifikowane. HTTP/API v1, Core, Capability Registry v1/v2, Worker i IPC 1.1, scheduler, trwałe operacje, epoch/fencing i UNKNOWN/quarantine pozostają bez zmiany kształtu lub semantyki.

Nowe opcjonalne pola konfiguracji operator_sid/host_sid konkretyzują lokalną politykę. Stare same publiczne anchory lub luźne podpisane pliki nie aktywują kwalifikacji bez zatwierdzenia i journalu. Pozytywny test E1 przeniesiono na ten jawny synthetic enrollment, zachowując jego asercje exact matching/tamper/deny; nie usunięto ani nie osłabiono istniejących scenariuszy.

## Pokrycie 24 wymagań

| Nr | Test/dowód |
|---|---|
| 1 | Explicit_approval_enrolls_exact_signed_read_evidence_and_keeps_private_key_out_of_Host |
| 2 | Enrollment_requires_explicit_approval_of_the_exact_immutable_package: brak approval, brak flagi, błędny hash review, drift po approval |
| 3–6 | Signature_integrity_and_exact_scope_drift_fail_closed: zły podpis przy zgodnym hashu aktywacji (dochodzi do ECDSA verify), inny publiczny klucz, brak anchora, zmiana/brak/ucięcie artefaktu; osobno niewłaściwy klucz podpisujący |
| 7–12 | Ten sam test: exact evidence ID, ProjectKey, Worker/TIA build, profil i read scope; istniejące QualificationEvidenceTests Core |
| 13 | Revocation_survives_new_provider_and_preserves_all_evidence_and_revision_history: revoke, restart providera, wyższa rewizja, brak fallback, brak reuse starej rewizji |
| 14–15 | Trzy deterministyczne granice w LocalTrustedDispatchTests: po durable admission, przed PrepareDispatch i przed IPC; prawdziwy provider/profil/scheduler, zero wysłanych call do Fake; zachowany intent dla trzeciej granicy |
| 16 | Host_process_restart_rechecks_revocation_without_rewriting_historical_results_or_exposing_enrollment: rzeczywiste procesy Host/Fake, revoke na działającym Host, restart, odmowa nowej pracy bez attempt, dawny wynik pozostaje dostępny |
| 17 | Wrong_operator_and_widened_startup_configuration_cannot_enroll, brak endpointu HTTP enroll, jawna odmowa approve bez flagi w rzeczywistym CLI |
| 18 | Rzeczywiste ACL własnych fixture root/file/config/parent; obce granty i uprawnienie DeleteChild rodzica blokują; zły SID Host odmawia |
| 19–20 | Missing store/journal oraz niedostępny uchwyt journalu; torn journal; valid signed files bez aktywacji nie są adoptowane |
| 21 | Dotychczasowy pozytywny/negatywny E1 Project_discovery_dispatch_requires_exact_project_scope_profile_builds_getters_and_verified_qualification |
| 22 | Istniejące HTTP/API v1 regression oraz rzeczywisty nowy HTTP/restart case |
| 23 | Istniejące IPC/framing/net48 probe, bez zmian kontraktów |
| 24 | Local_enrollment_never_authorizes_export_import_or_write: ProjectExport, SyncApply, CompilePlc; istniejące reguły Core |

Dodatkowo: duplicate/unknown fields, null collection, traversal, case collision i oversize manifest są odrzucane przed approval/activation. JSON/path/size limity i fail-closed history nie są zastąpione atrapą. Klucze testowe są efemeryczne w pamięci; podpis i hashe są rzeczywiste. Sukces CLI enroll z produkcyjnym kontenerem CNG nie był aktywowany — CLI odmawia nieistniejącego klucza, a pozytywny signing/enrollment test używa efemerycznego ECDsa.

## Walidacja

Finalna regresja celowana: **48/48 PASS**, w tym **47 nowych przypadków LocalTrusted** oraz zachowany test integracji E1. Wcześniejsze iteracje 30/30, 34/34, 35/35 i 44/44 PASS odzwierciedlają dodawanie zakresu testów. Początkowe błędy kompilacji adnotacji Windows/local helper oraz brak using Storage poprawiono przed finalnym buildem; nie zmieniono wymagań ani progów.

Pierwszy niezmieniony kanoniczny runner `scripts/validate_offline.py`, run `80d1564aebc344a18d92d096ace17981`: Build Release 0 warnings/errors PASS, Host 307/307 i Core 564/564, razem 871/871 .NET PASS, Python 42/42 PASS. Generatory i audyt PASS. Runner zakończył się **FAIL** na dwóch odstępach/newline inicjalizatora LocalTrustStore w `08-format`; nie wykonał końcowej oceny kwalifikacji. Wynik i zamknięty manifest zachowano, nie promowano go do pełnego PASS. Format skorygowano i pełny format verify przeszedł przed finalnym przebiegiem.

Przegląd ujawnił dodatkowe przypadki brzegowe. `boundary-reproduction.trx` zachowuje **0/4 PASS** przed naprawą: (1) manifest 800 KiB po base64 przekraczał wspólny limit JSON 1 MiB, więc po publikacji nie dawał się odczytać jako koperta; (2–3) HostConfiguration.Read nie walidował kryptograficznie malformed/P-384 anchora przed startem; (4) append mógł przekroczyć 8 MiB reader limit i uczynić historię nieczytelną. Poprawki: osobne bounded 2 MiB envelope z nadal 1 MiB decoded payload, wczesna walidacja SPKI/P-256 z kontrolowanym błędem konfiguracji, sprawdzenie pojemności journalu przed zapisem. Cztery regresje po poprawce PASS, cały finalny zestaw celowany 48/48 PASS. Nie osłabiono SHA, podpisu, ACL, limitu payloadu, retencji historii lub kontraktów; nie skrócono testów NF.

Końcowy kanoniczny runner na finalnym kodzie: **`b92e24a5659f45e5b58d1eedbbeb5b8a`**, UTC `2026-10-26T22:14:28.619967+00:00`–`2026-10-26T22:36:54.457864+00:00`. Wszystkie dziewięć kroków zakończyło się exit 0. Źródeł/binariów nie zmieniano w trakcie żadnego pełnego runnera; dokumentację uzupełniano osobno.

| Kontrola | Wynik |
|---|---|
| Restore / Build Release | PASS; 0 warnings/errors |
| Core / Host | 564/564 i 311/311 PASS; razem 875/875, zero FAIL/SKIP |
| Python | 42/42 PASS |
| E1 | 66/66 PASS: 18 Core, 48 Host (47 nowych LocalTrusted i zachowany E1) |
| IPC / scheduler i IPC | 34/34 i 76/76 PASS |
| Resilience | 52/52 testy i 49/49 wymaganych records PASS |
| Generator kontraktów / vectors / audit / format / diff | PASS; audyt 58 Markdown, 489 lokalnych linków, 0 błędów |
| Niezależna kontrola dowodów | 73/73 pliki manifestu, trzy bundles, 464/464 próbki; bieżące hashe źródeł i binariów zgodne |

Dowody pozostają w ignorowanym `.tia-bridge/resilience/<run-id>/`, podsumowanie kontrolne w `.tia-bridge/e1-l/final-validation.json`. SHA-256:

```text
evidence-manifest.json d7f63059158ed2332c0f426d939f14c2a648862e76ca09790a5864928f9da8f7
validation/run.json 26fb1d4f5fd0451511bd50faf4696d433b934134203d9cb14869bf650d03157d
validation/resilience.json 0b3310a571c6f0ea8dbc64bdbf85941365ebd149aee68c63c00030999e3396b3
Core TRX bc86674068f9d348ab77fad40f7de06baf8369563d58911be12a032aa4c89b34
Host TRX 8809663e0c72aaaa7eb5ba32d3c51546d4ef84d9283c3c19d2774bc155167937
```

Preflight potwierdził C:/X: Fixed/NTFS, dostępną pamięć i miejsce oraz brak aktywnego Host/Fake/TIA Portal przed startem. Wstępny snapshot CPU 96% zachowano jako ryzyko pomiarów; finalna obserwacja CPU 58/50/65%. Pliki `preflight.json`, `preflight-final.json`, TRX i logi obu przebiegów pozostają poza Git.

**Functional test execution PASS. Nowa ograniczona D-11 PASS**: RT-03/05/06/15/16 oraz NF-01/02/03/04/05/06/12/14/15 PASS, rzeczywisty idle 306.0543894 s, CPU 0.8493946%, 1001 operacji w 551.6663539 s, bez błędów krytycznych. NF-02 p95/p99 143.1194/187.8481 ms (64 próbki); NF-03 p95: inplace 1.2616 ms, NTFS replace 3.0966 ms, kontrola repo bez/z workerem 9.1101/7.0633 ms (po 100 próbek).

**Full offline qualification BLOCKED**, mimo runner exit 0 i funkcjonalnego PASS: `resilience_gate=BLOCKED`, NF-11 NOT_RUN, pełny profil skali NF-08 NOT_RUN. Zmierzone idle i 1001 operacji nie zastępują 8 h soak ani kwalifikacji peak resources; dlatego zbiorczy operations/NF-08 status pozostaje NOT_RUN. Ostatnia historycznie potwierdzona ograniczona D-11 PASS pozostaje zachowana, historycznych FAIL nie zmieniono. Production/Engineering/Runtime NOT_RUN. E1 Local Trusted v1 COMPLETE_OFFLINE; Real Engineering evidence NOT_REGISTERED; B-03A Engineering BLOCKED/NOT_RUN; B-03B NOT_READY; Worker Handle Risk UNKNOWN.

## Ocena instrukcji, ADR i ograniczenia

Zakres dostawy (24 pliki; bez surowych logów, DB, podpisanych pakietów lub kluczy):

```text
TiaBridge.slnx
Zmiany.md
docs/17_TRUSTED_QUALIFICATION_EVIDENCE.md
docs/18_LOCAL_TRUSTED_ENROLLMENT.md
docs/B03A_E1_L_LOCAL_TRUSTED.md
src/TiaBridge.Host/Api/HostConfiguration.cs
src/TiaBridge.Host/Qualification/LocalTrustAcl.cs
src/TiaBridge.Host/Qualification/LocalTrustStore.cs
src/TiaBridge.Host/README.md
src/TiaBridge.Host/Scheduling/QualificationEvidenceProvider.cs
src/TiaBridge.Host/TiaBridge.Host.csproj
src/TiaBridge.Qualification/LocalEnrollment.cs
src/TiaBridge.Qualification/Program.cs
src/TiaBridge.Qualification/README.md
src/TiaBridge.Qualification/TiaBridge.Qualification.csproj
src/TiaBridge.Qualification/packages.lock.json
tests/integration/TiaBridge.Host.Tests/ApiFixture.cs
tests/integration/TiaBridge.Host.Tests/LocalTrustedDispatchTests.cs
tests/integration/TiaBridge.Host.Tests/LocalTrustedFixture.cs
tests/integration/TiaBridge.Host.Tests/LocalTrustedTests.cs
tests/integration/TiaBridge.Host.Tests/TiaBridge.Host.Tests.csproj
tests/integration/TiaBridge.Host.Tests/V19OfflineTests.cs
tests/integration/TiaBridge.Host.Tests/packages.lock.json
tests/integration/TiaBridge.StorageProbe/HttpResilienceProbe.cs
```

AGENTS oceniono sekcja po sekcji: zakres/stan, role/hierarchia, granice, autoryzacja/testy/D-11/Computer Use, dyscyplina, Git i nieufne dane. Instrukcja_ChatGPT: cel/odpowiedzialność, źródła/hierarchia, architektura, standard zadania, kontrola wyników, bezpieczeństwo/recovery, decyzje/spójność, Git/dokumentacja i styl. Obie pozostają bez edycji: E1-L konkretyzuje zatwierdzoną politykę lokalną i nadal nie nadaje grantów Siemens. ADR 0001–0003 nie zmieniają się; nie ma zmiany Workspace/SYNCED, boundary Core/Worker, HMI lub sagi mutacji. Specyfikacje 03–09 i kontrakty pozostają zgodne; dokument 17 zastępuje wcześniejsze otwarte propozycje dual review/expiry bieżącą jawną decyzją użytkownika: jeden operator, manual revocation/rotation, brak automatycznego wygasania. Host README i nowa procedura 18 opisują implementację. Historycznych X-01/D-11/kwalifikacyjnych raportów nie zmieniano.

Ryzyka: operator odpowiada za prawdziwość podpisywanych twierdzeń; SHA/podpis nie zastępują kwalifikacji Siemens. Nie kwalifikowano oddzielnych kont/usługi, hardware power-loss ani odporności na administratora/wrogi proces tego samego SID. Wspólny SID operatora i Host nie jest granicą izolacji OS, a SYSTEM/Administrators pozostają zaufani. Manualna rotacja i brak expiry wymagają dyscypliny operatora. Revocation nie jest atomowym zatrzymaniem wysłanego call; końcowy dispatch/check pozostaje granicą obserwacji, obowiązują UNKNOWN/quarantine. Limity lifecycle wymagają review przy wyczerpaniu, bez automatycznego kasowania historii.

E2 wymaga osobnego zatwierdzonego zadania, właściwego aktualnego D-11 i preflightu, realnego source evidence i operatorowego enrollment, świeżego exact PID/start/path/build/project binding, autoryzacji bounded odczytu i monitoringu zasobów. Nie twórz fikcyjnego Engineering PASS dla bootstrapu. E1-L nie rozpoczyna E2 lub B-03B.
