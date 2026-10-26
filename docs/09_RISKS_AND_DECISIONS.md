# 09 — Decyzje, ryzyka i pytania do weryfikacji

Stan projektu: 2026-10-24/25. Decyzja projektowa nie jest dowodem wdrożenia ani zgodą na live write. Materiały wejściowe są zachowane; [ADR 0001](decisions/0001-file-workspace-and-adapter-boundary.md) obowiązuje dla file workspace/versioned adapter, a [ADR 0002](decisions/0002-async-host-and-versioned-worker.md) doprecyzowuje runtime i autoryzację.

## Decyzje

| ID | Decyzja i status | Powód / alternatywa / konsekwencja |
|---|---|---|
| D-01 | przyjęta projektowo: host .NET10 + worker net48/x64 V19 | kompatybilność Siemens oddzielona od nowoczesnego API; Python host możliwy, lecz zwiększa packaging; X-01 przed implementation support claim |
| D-02 | przyjęta: trwałe operation IDs i SQLite na lokalnym NTFS | niewystarczający synchroniczny subprocess; samo MCP/CLI nie daje durability; M: exFAT jest workspace repo, nie stan operacji/sekrety |
| D-03 | przyjęta: HTTP loopback + CLI, MCP OPTIONAL | jeden silnik i diagnostyka; MCP Tasks nie wymagane; klient nie blokuje domyślnie tool call |
| D-04 | przyjęta: W priorytet planu, S wejściem importu, B wyłącznie po verify | swoboda CODEX, brak utraty późniejszych edycji; odrzucony import-on-save i automatyczny force overwrite |
| D-05 | przyjęta: quarantine po niepewnej mutacji, brak automatycznego replay/rollback | fence nie dociera do Siemens; exactly-once niemożliwe do obiecania; automatyzujemy diagnozę, nie nieznany efekt |
| D-06 | przyjęta: capability per operation/kind/version/coverage | reflection, compile i hash nie są round-trip proof; Unified/classic osobno; unsupported jawnie w inventory |
| D-07 | przyjęta: ograniczony grant sesji testowej zamiast ręcznej zgody na każdy krok | autonomia w zatwierdzonym zakresie, fresh recipient i backup nadal wymagane; destructive/out-of-scope nowe rozstrzygnięcie |
| D-08 | przyjęta: OPC UA i Playwright niezależne od Engineering | Runtime ma własne E/T; simulator start≠load≠RUN≠ready; GUI adapter osobno i tylko po zgodzie |
| D-09 | przyjęta: Snap7 jako OPTIONAL transport testowy, analiza REQUIRED | przydatny offsetowy test DB, lecz brak optimized/symbolic/security/subscriptions classic S7; nie zastępuje OPC ani Openness; [11](11_SNAP7_PLC_TESTING.md) |
| D-10 | odroczona: użycie Transaction jako gwarancji rollbacku | API istnieje, zakres import/compile/Save niepotwierdzony; X-02; MVP saga i reconciliation |
| D-11 | zatwierdzona przez użytkownika 2026-10-26: oddzielna ograniczona bramka X-01 read-only na lokalnym NTFS | [warunki 07](07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only) obejmują wymagane RT/NF Fake, recovery, idle ≥5 min i ≥1000 operacji; NF-11 ≥8 h pozostaje obowiązkowy w B-10B/R-14, NOT_RUN przed X-01 nie staje się PASS; NF-03 exFAT FAIL zachowany, bez dopuszczenia exFAT lub gotowości produkcyjnej |

## Rejestr ryzyk

| ID / ważność | Ryzyko i dowód/niepewność | Ograniczenie / właściciel przyszłego pakietu / kryterium zamknięcia |
|---|---|---|
| R-01 Critical | TIA call może trwać mimo timeout/worker death, OB-02 | B-02B/D: quarantine zachowana po lease expiry i host restart; RT-01/06 |
| R-02 Critical | częściowy import i brak ogólnej transakcji | B-04C/05A: journal + readback + backup, RT-07; D-10 pozostaje do kwalifikacji |
| R-03 Critical | nazwa/localhost nie dowodzą izolacji, możliwy realny PLC partner | B-07B/08B: exact instance/route/grant;0 write przy braku dowodu, bez fizycznego downloadu |
| R-04 High | API i tekst HMI nie dowodzą native public function nodes | X-04/B-05C: pełny publiczny reader albo capability ograniczona/UNSUPPORTED |
| R-05 High | CLR proxies po import/restart mają inną tożsamość, OB-12 | B-03A/05A: epoch/re-resolve/owner/inventory; bez opierania identity na Equals |
| R-06 High | unknown XML/YAML lub języki utracone przy source import | X-03/B-05B: golden round-trip i protected fields; brak coverage blokuje SYNCED |
| R-07 High | aktywny dirty T05 ma FAILED/UNKNOWN; jego recovery jest poza zadaniem | B-00A/11B: tylko dowody; nowa app nie adoptuje sesji ani nie naprawia projektu automatycznie |
| R-08 High | target repo na exFAT: inne ACL/rename/durability | B-02A/03B: NTFS state/spool/secrets, M: tylko W; brak blind replace edytowanych plików |
| R-09 High | Archive temp na C:, destination space niewystarczający | B-04B: actual storage failure preserved; jeden backup sesji, bez redirect TEMP/auto cleanup |
| R-10 High | licencje Advanced/WinCC/clients/provisioning niezweryfikowane | X-05/X-06: legal availability/licensing state jawnie sprawdzone bez kluczy w logu; inaczej BLOCKED |
| R-11 High | TLS/UMC blokują lokalny web runtime (świeży błąd SSL) | X-06/B-09A: poprawny trust i testowa tożsamość; żadnego ignoreHTTPSErrors jako domyślnego obejścia |
| R-12 High | browser canvas/CWC bez stabilnych selektorów | B-09B: adapter per family, coverage i alternatywne API; screenshots nie dowodzą logiki PLC |
| R-13 High | Windows session/Openness rights nowego EXE i net48 targeting pack | X-01/B-11A: build i attach qualified; stare doctor PASS nie przenosi permissions |
| R-14 Medium | aktywne: CPU/RAM/handles creep i koszt pełnych snapshotów | B-10B: minimum 8 h, bounded scope i streaming, NF-08/11; krótki przebieg 1001 operacji i D-11 nie zamykają ryzyka ani pełnej kwalifikacji stabilności |
| R-15 High | schema/normalizer upgrade zmienia interpretację B | B-01C/05B/11C: versioned golden tests i re-verify, bez przepisywania historycznych dowodów |
| R-16 High | sekrety w trace/log/IPC/command line; lokalny endpoint dostępny obcej stronie | B-02C/09A/10C: ACL/token/Origin checks/redaction; audit fixture bez sekretów |
| R-17 High | Snap7 PUT/GET obniża ochronę i offsets przestają odpowiadać DB | X-08/B-08D: tylko test DB/instance, layout fingerprint i grant; w przeciwnym razie disabled |
| R-18 Medium | library/version drift python-snap72.x vs3.x, OPC backend dependency mismatch | lockfiles i explicit package identity; X-07/X-08; brak globalnego install/fallback do innego backendu |
| R-19 High | nieznane współbieżne klienty TIA omijają lock Bridge | B-04C: świeży P pod ExclusiveAccess i inwarianty przed efektem; host mutex nie jest pełnym zabezpieczeniem |
| R-20 Medium | logi lokalne nieprzenośne, kod audytowany dirty | source-index z hashami, sanitized summary i reproducible fixtures; bez twierdzenia, że sam HEAD odtwarza analizę |

## Eksperymenty kwalifikacyjne

To backlog osobnych prac, nie polecenie wykonania teraz. Zapis/compile/start/stop tylko na osobno zatwierdzonym środowisku; nie na dirty T05. Każdy kończy się raportem także przy negatywnym wyniku.

| ID | Hipoteza i ograniczony eksperyment | Kryterium i dowód | Blokowane pakiety |
|---|---|---|---|
| X-01 | net48/x64 worker może stabilnie attach/inspect/dispose bez blokowania hosta; porównaj wymagane apartment/event handling i utratę rodzica | clean build,20 read-only attach/dispose, status NF-01 przy fake hang, żadnego zamknięcia TIA/orphan owned child; exact DLL+rights i trace faz; parent-loss mutacji tylko na fake przed osobnym live testem | B-03A |
| X-02 | ustalić zakres Transaction i skutki import half-fail/Save lost reply | na kopii syntetycznej wymusić błąd drugiego kroku; eksport przed/po, Save/disk/in-memory rozdzielone; brak deklaracji rollbacku ponad dowody | B-04C; ewentualna zmiana D-10 |
| X-03 | FC SCL round-trip zachowuje wszystkie required fields | native export + generated source przed, po zmianie i po ponownym Attach; wszystkie unknown fields sklasyfikowane, protected block bez zmian; then UDT/DB/FB/IDB osobno | B-03B/04C/05A |
| X-04 | publiczne API V19 pozwala potwierdzić wymagany scope HMI function/structure | na synthetic module CRLF compare native identities/functions/props/relations; złe boundary musi dać UNKNOWN/mismatch mimo compile 0; jeżeli reader niedostępny, ograniczyć capability zamiast czytać private DB | B-03D/05C |
| X-05 | lokalny SDK Advanced V6 działa z licencją i izolowanym TCP instance | enumerate owner/type/firmware, create własny test instance/start/state/stop przy S grant, potwierdzić routing i niezmienione cudze sesje; osobny load qualification | B-07A/B |
| X-06 | Runtime Manager CLI i web UI mogą działać bez GUI dla testowego projektu | najpierw read-only status i TLS/UMC diagnosis, potem jawnie own test runtime start/stop; exact syntax/exit codes/project+browser identity, bez transferu do produkcji | B-07C/09A |
| X-07 | OPC endpoint CPU i subscriptions działają dla testowej konfiguracji | namespace URI/tag resolution, Good/fresh values i sequence gaps, bounded reconnect; osobno write/ack lease test, profile TEST/SECURE jawny | B-08A/B |
| X-08 | python-snap7 pinned może testować izolowany DB Advanced bez zmiany produkcji | offline codec/mock, then read-only DB handshake, then allowlist write;100 powtórzeń, layout drift i unknown write; exact CPU/firmware/DB/network evidence | B-08D OPTIONAL |

## Otwarte informacje

1. Jakie konkretne CPU/firmware i project libraries zostaną objęte pierwszym produktem? Discovery może wskazać kandydatów, ale support matrix powstaje po kwalifikacji.
2. Czy docelowy test ma CPU OPC UA server, czy dostęp przez WinCC? Obecny endpoint potwierdza WinCC, nie CPU.
3. Czy Advanced i WinCC mają aktywne uprawnienia/licencje dla planowanej liczby instancji/klientów? Instalacja tego nie rozstrzyga.
4. Czy wymagany HMI native API reader jest w pełni dostępny publicznie? Jeśli nie, zakres importu musi być ograniczony, a pełne SYNCED HMI może pozostać niedostępne.
5. Jakie środowisko CI Windows z TIA i bezpiecznym profilem zostanie udostępnione? Nie wysyłamy projektów/licencji do publicznych runnerów.
6. Czy użytkownik będzie wymagał GUI w wersji 1? Obecny projekt priorytetyzuje API/CLI i nie potrzebuje UI dla działania CODEX.
7. Jakie rzeczywiste SLO vendor calls pozwoli osiągnąć komputer? NF wartości są do pomiaru, nie zobowiązanie Siemens.
8. Czy Snap7 jest potrzebne przy dostępnej ścieżce OPC UA i czy osobny non-optimized test DB jest dopuszczalny? Implementacja nie jest warunkiem MVP; analiza i test plan są gotowe.

Informacje te nie blokowały obecnego projektu dokumentacyjnego. Przyszłe implementacje, które od nich zależą, muszą rozstrzygnąć je eksperymentem lub jawnie ograniczyć capability.

## Dowód B-02D i zatwierdzone rozstrzygnięcie D-11

[Raport B-02D](16_RESILIENCE_RECOVERY.md) dokumentuje granice UNKNOWN/recovery i procesów Fake. W B-02D wykryto rozbieżność: ogólny profil 8 h z 07 odczytywany razem z bramką 08 i instrukcjami mógł blokować pierwszy read-only X-01, choć R-14 przypisywał soak do B-10B. D-11 rozstrzyga wyłącznie etap zastosowania wymagań. D-01–D-10, lifecycle, UNKNOWN/quarantine, worker isolation i autoryzacja pozostają bez zmian. [Ocena dowodów D-11](../tests/resilience/X01_LIMITED_GATE.md) jest odrębna od niezmienionego historycznego snapshot.

R-14 pozostaje aktywne: pełny 8 h soak NF-11 jest NOT_RUN i obowiązkowy w B-10B. R-08 również pozostaje aktywne; historyczny NF-03 exFAT: FAIL i wymóg osobnej kwalifikacji safe replace nie są usuwane przez pozytywne NTFS. Nie stosuje się delete+copy, automatycznej relokacji ani zmiany ACL.

## Przygotowane środowisko przyszłego X-01

Według deklaracji użytkownika folder `X:\TESTY_TIA_Bridge` zawiera dwa projekty TIA Portal V19 dedykowane przyszłym testom. Użytkownik dopuścił ich modyfikację, uszkodzenie lub utratę w odrębnie zlecanych eksperymentach. Nie jest to obecne polecenie otwarcia, kopiowania, modyfikacji lub usunięcia tych projektów. W tym zadaniu nie uzyskiwano do nich dostępu; typ filesystemu X: pozostaje NIEZWERYFIKOWANY i musi zostać sprawdzony przed operacjami objętymi kwalifikacją NTFS. Nie zakłada się ani nie wykonuje automatycznego przenoszenia danych.

Folder testowy nie dowodzi izolacji PLC/HMI. Dostęp do projektów i zgoda na ich przyszłe zmiany nie uprawniają do fizycznego downloadu lub sterowania sprzętem. Pierwszy X-01 pozostaje read-only, wymaga osobnego zadania i autoryzacji oraz świeżej tożsamości i weryfikacji środowiska. Późniejsze mutacje wymagają osobnego zakresu, capabilities i wszystkich właściwych bramek. Fizyczny download pozostaje domyślnie wyłączony.

## Obserwacje X-01 read-only

[Raport nowego workera V19](X01_TIA_V19_QUALIFICATION.md) zawiera rzeczywiste dowody dwóch instancji, apartment, exact binding i utraty własnych procesów. Nie zastępuje historycznej D-11 ani nie zamyka R-14: krótki przebieg wykazał wzrost liczby uchwytów w długowiecznym procesie workera, mimo prawidłowych Dispose i końca wszystkich własnych procesów po przebiegu. Nie rozstrzygnięto, jaka część to odroczone zwalnianie GC/vendor, a jaka trwała retencja; pełny soak/qualification pozostaje B-10B. Nie wprowadzono wymuszonego GC, zabijania TIA lub rozszerzenia uprawnień w celu poprawienia pomiaru.
