# E2-B1 — T05_RecipeSim_V19_Testowy

Status planu: PREPARED_OFFLINE. ENGINEERING: NOT_RUN. Zatwierdzenie: PENDING.
Baza E2-A: `97e9f803a583d727e53fb366d13a316baaaaaab7`, 2026-10-27.
Plan obejmuje wyłącznie pierwszy projekt. [Pakiet wspólny](B03A_E2_A_PREPARATION.md)
jest integralną częścią tego planu: exact gettery, konfiguracja, pomiary, integralność
i kryteria kwalifikacji. [Operator przed B1](E2_OPERATOR.md).

## Zakres i profil

Własna, wskazana przez operatora kopia `T05_RecipeSim_V19_Testowy`, exact AP19 na
lokalnym fixed NTFS. Path/ProjectKey/PID/start/SID: NOT_BOUND do świeżego preflightu.
Jeden target w Host, jeden Worker, jeden owner STA; 20 kolejnych cykli
DiscoveryV1 (`v19.discovery.v1`), `all_supported`, `fresh=true`, timeout 120 s,
queue timeout 300 s; 64 Device, 256 nodes włącznie z root, MaxDepth 8 liczone od
pierwszego DeviceItem depth=0. Jeden request naraz, bez automatycznych ponowień.

PLC/HMI tylko Device/DeviceItem, nazwy, relacje i runtime software type z allowlisty
pakietu; bez źródeł PLC i bez wnętrza HMI. Operator ustala listę oczekiwanych urządzeń
i rodzin z UI przed testem; historyczne liczby X-01 nie są expected counts B1.
Nie wykonuj Save/Import/Export/Compile/Download/Archive, zmian struktury, odłączania
innych użytkowników, zamykania obcej TIA, auto napraw ani Runtime/symulacji/OPC write.

## Warunki wejścia — wszystkie wymagane

1. Osobna zgoda użytkownika na ten exact plan i odbiorcę; ograniczone okno sesji,
   operator i uprawnienie read-only. Przygotowanie E2-A nie jest taką zgodą.
2. Rozstrzygnięty bootstrap z pakietu wspólnego: rzeczywisty exact source evidence
   zatwierdzony/enrolled/verify albo osobno zaprojektowany i zatwierdzony eksperyment.
   Obecnie brak dowodu; zwykły Host musi odmawiać. Żadnego synthetic PASS.
3. Zweryfikowana właściwa ograniczona D-11 dla buildu/profilu NTFS i brak krytycznych
   błędów B-02A–D; NF-11 pozostaje NOT_RUN. Nie przepisuj dawnych FAIL lub X-01.
4. Operator potwierdził kopię, brak edycji/aktywności kolidujących i czysty stan UI.
   Brak pewności/dirty → REVIEW_REQUIRED/BLOCKED, bez Save.
5. Nowy run UUID B1, osobny per-user NTFS root, konfiguracja/ACL/SID z procedury 18,
   manifest Host/Worker i DLL, brak reparse/shadow. Żadnego klucza lub ACL z automatu.
6. Świeże Windows PID/start/executable/SID oraz autoryzowana enumeracja Openness
   dokładnie dopasowana do AP19; unikalny ProjectKey B1. Jeden target, max_in_flight=1.
7. Działający zewnętrzny sampler Windows oraz metadane projektu przed odczytem,
   gotowe miejsce na wszystkie dowody i wypełniony authorization.md z hashami planów.

## Kroki testowe po spełnieniu wejścia

1. Zapisz preflight/binding/build manifest, wersje konfiguracji/registry i source
   qualification verify. Nie uruchamiaj starego harnessu `--project-root --cycles`:
   wymaga dwóch projektów, przeplata A/B, nie provisionuje Local Trusted.
2. Uruchom istniejący Host `dotnet <Host.dll> --config <reviewed-config> --stdio`
   jako własny proces z kontrolowanym stdin. Nie uruchamiaj TIA z Bridge. Zachowaj
   chroniony discovery path; odczytaj health przez istniejący CLI.
3. Dla cyklu 1 przygotuj nowy plik request z rzeczywistym ProjectKey oraz unikalnym
   idempotency_key związanym z B1/run/cycle; wzór poniżej. Zapisz exact bytes i hash.
4. Wykonaj submit przez istniejący CLI, zapisz acceptance/operation_id. Sprawdzaj
   status/health i result/events, respektując poll_after_ms; odczyty metadanych nie
   mogą wywoływać TIA inline. Bounded wait nie anuluje operacji i nie oznacza jej końca.
5. Po terminalnym wyniku odczytaj oba immutable artefakty wskazane w result z lokalnego
   zatwierdzonego store (procedura w pakiecie), zweryfikuj SHA-256, binding, epoch/build/coverage i graf.
   Wymagaj false/false IsModified w raw i snapshot; SUCCEEDED true/true odrzucić.
   Zapisz fazy audit i Dispose.Returned, PID/start żywej TIA oraz próbki zasobów.
6. Gdy cykl spełnia wszystkie kontrole i potwierdzono quiescence, wykonaj identyczną
   procedurę dla cykli 2–20 z nowymi kluczami. Zachowaj wyniki cyklu cold. Nie mieszaj
   B2 ani innych targetów. Nie zmieniaj limitów, STA ani buildu w tej serii.
7. Po cyklu 20: 30 s obserwacji idle, kontrolowany stop własnego Host, potwierdzone
   zakończenie exact Workera i brak własnych orphanów. Brak końca w 10 s → BLOCKED,
   bez kill lub zamykania TIA. Sprawdź stan projektu/UI i metadane po teście.
8. Zamknij pliki dowodowe, utwórz manifest/hash, niezależnie sprawdź integralność,
   opracuj trend review i raport. Ręczny reviewer wyznacza qualification_result.
   Nie aktywuj enrollment i nie rozpoczynaj B2 automatycznie.

Instrukcyjne argumenty CLI (wszystkie zmienne muszą pochodzić z zatwierdzonej sesji,
to nie komendy do wykonania w E2-A):

```powershell
dotnet $e2Cli health --discovery $e2Discovery --json
dotnet $e2Cli submit project.inspect --request $e2Request --discovery $e2Discovery --json
dotnet $e2Cli operation status $e2OperationId --discovery $e2Discovery --json
dotnet $e2Cli operation result $e2OperationId --discovery $e2Discovery --json
dotnet $e2Cli operation events $e2OperationId --discovery $e2Discovery --json
```

Wzór request — **niegotowy do wysłania**:

```json
{
  "schema_version": "1.0", "method": "project.inspect",
  "project_key": "<B1-reviewed-UUID>", "idempotency_key": "<B1-run-UUID>-cycle-01",
  "params": {"inventory_scope": "all_supported", "fresh": true, "timeout_s": 120, "queue_timeout_s": 300}
}
```

## Dane pomiarowe i artefakty

Każdy cykl: UTC, monotonic duration, operation/session/worker epoch, exact PID/start,
submit/status/health/result/events i enumeration/Attach/read/Dispose ms, devices/nodes,
graf i coverage, IsModified przed/po, Handles/PrivateBytes/WorkingSet/CPU before/after.
Sampler 1 s obejmuje Host/Worker/TIA. Pełne surowe serie, cold/warm i idle, bez usuwania
outlierów; trend i ograniczenia zgodnie z pakietem wspólnym, bez progu wzrostu uchwytów.

Wymagany cały zestaw z sekcji „Dowody, raport i integralność każdego projektu” pakietu:
autoryzacja, preflight/binding/build, before/after, enumeracja, audit/decisions,
acceptance/status/events/result wszystkich 20 operacji, raw i Discovery artefakty,
telemetria, cycles.csv, release.json, resource-review, report.json/md i manifest SHA.
Root i identyfikatory należą wyłącznie do B1. Nie commituj surowych danych projektu.

## Akceptacja, STOP i wynik

PASS tylko przy kompletnym wejściu, 20 potwierdzonych operacjach bez retry, poprawnym
ograniczonym grafie/coverage z zachowaniem UNKNOWN, exact binding/build, false/false,
potwierdzonym braku niezamierzonej modyfikacji, pełnych hashach i release oraz zamkniętym
review zasobów. Nie jest wymagany pełny content/inventory lub natywny HMI export;
nie deklaruj ich jako COMPLETE. PASS dotyczy tylko zadanego read scope.

STOP: drift PID/start/path/SID/build, dirty/unknown IsModified, błąd gettera/limit,
timeout/hang/IPC loss, brak Dispose.Returned/release, dialog bez rozstrzygnięcia,
presja zasobów, nieoczekiwane działania innego użytkownika, uszkodzony/brakujący artefakt.
Nie wysyłaj następnego requestu ani retry. Zachowaj intent, audit, receipt i procesy;
timeout/śmierć procesu/lease nie dowodzą końca Siemens call. Nie kasuj quarantine.
Potwierdzona niezamierzona modyfikacja lub negatywny test → FAIL; brak dowodu,
unresolved trend/safety albo niepewny stan → BLOCKED. Nie naprawiaj projektu.

Raport używa pełnego formatu z pakietu wspólnego z `task=E2-B1`, exact project name,
własnym run/project/evidence IDs oraz osobnymi functional_result i qualification_result.
Weryfikacja integralności: wszystkie zamknięte pliki → manifest bytes/SHA-256 → drugi
odczyt każdego pliku i zewnętrzny hash manifestu w receipt reviewera. Jakikolwiek brak
blokuje PASS. Runtime/production/NF-11 pozostają NOT_RUN; Worker Handle Risk UNKNOWN.

## Wyjście do B2

Wymagany osobny receipt B1 PASS: czysty projekt, pełne artefakty/hashe, release,
trend review i brak unresolved safety. Następnie osobna zgoda i świeży preflight
[E2-B2](E2-B2_TEST_PLAN.md). Sam SUCCEEDED/functional PASS albo dawny X-01 nie otwiera bramki.
