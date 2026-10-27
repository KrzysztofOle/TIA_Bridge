# E2-B2 — T04_T05-000_Testowy

Status planu: PREPARED_OFFLINE. ENGINEERING: NOT_RUN. Zatwierdzenie: PENDING.
Baza E2-A: `97e9f803a583d727e53fb366d13a316baaaaaab7`, 2026-10-27.
To odrębny plan **drugiego** projektu. [Pakiet wspólny](B03A_E2_A_PREPARATION.md)
jest jego integralną częścią; nie wolno użyć bindingu, store ani wyniku B1 jako B2.
Implementacja [bootstrapu B0](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md) dopuszcza
wyłącznie B1; B2 nie otrzymuje grantu ani ścieżki wykonania przez ten dodatek.

## Zakres i profil

Własna wskazana kopia `T04_T05-000_Testowy`, exact AP19 na lokalnym fixed NTFS.
Path/ProjectKey/PID/start/SID: NOT_BOUND. Jeden target B2 w osobnym state Host,
max_in_flight=1, jeden Worker i owner STA. 20 sekwencyjnych cykli DiscoveryV1,
`v19.discovery.v1`, `all_supported`, `fresh=true`, timeout 120 s, queue timeout 300 s.
64 Device, 256 nodes włącznie z Device roots, MaxDepth 8 od pierwszego DeviceItem
depth=0. Bez zmiany limitów/builda/apartment, bez automatycznych ponowień.

PLC/HMI: publiczne Device/DeviceItem/nazwy/hierarchia i exact software types opisane
w pakiecie wspólnym, identyczna allowlista 13 getterów, niezależne potwierdzenie ich
działania w B2. Nie odczytuj źródeł PLC, ekranów/skryptów/zdarzeń HMI. Expected
devices/families operator ustala z UI **B2**, nie z wyniku B1 lub historii X-01.
Unknown/null/mixed pozostają jawne. Zakaz Save/Import/Export/Compile/Download/Archive,
zmian struktury, zamykania cudzej TIA, odłączania użytkowników i naprawy; Runtime,
RUN/STOP, symulacja, transfer HMI i OPC write wyłączone.

## Warunki wejścia — nowa ocena po B1

1. Dostępny receipt **qualification PASS E2-B1** z run ID, manifest SHA i niezależnie
   sprawdzonymi bajtami; potwierdzony brak niezamierzonej modyfikacji B1.
2. Dispose.Returned B1, zakończenie exact Workera B1 i brak własnych orphanów,
   zaakceptowane review trendu pamięci/uchwytów i brak unresolved safety. GROWING lub
   INCONCLUSIVE wymagające review blokuje przejście, nawet przy functional PASS B1.
3. Osobna decyzja użytkownika na B2 z exact plan hash/odbiorcą/oknem/operatorowym prawem
   odczytu; zgoda B1 nie dziedziczy się. Właściwe aktualne dowody ograniczonej D-11
   dla buildu/profilu i brak krytycznych błędów silnika, bez nowego NF-11.
4. Nowy run UUID/root NTFS i nowy ProjectKey B2 albo niezależnie zweryfikowany istniejący
   binding B2. Oddzielne operation/evidence IDs, źródło kwalifikacji B2 i registry.
   Nie kopiuj qualification-evidence/store B1; exact ProjectKey B1 musi być odrzucony.
5. Rozstrzygnięcie bootstrapu **dla B2**; PASS B1 nie kwalifikuje getterów dla innego
   ProjectKey. Bez exact rzeczywistego source evidence albo osobno zatwierdzonej
   ścieżki eksperymentu: BLOCKED. Nie wpisuj nowego PASS, aby odblokować pierwszy call.
6. Świeży preflight Windows + autoryzowana Openness enumeration: unikalny PID/start/
   executable/SID/AP19, dokładnie jeden target B2. Ponowna kontrola V19 DLL/Worker/
   Host SHA i config/ACL dla właściwego SID, bez reparse i vendor shadow.
7. Operator potwierdził test copy, czysty stan UI B2, brak kolidujących edycji,
   expected devices/families, gotowy sampler 1 s, telemetry i miejsce na komplet dowodów.

## Kroki testowe — wyłącznie B2

1. Zweryfikuj receipt B1 i zapisz go jako **referencję bramki**, z oryginalnym ID i hash.
   Utwórz odrębny authorization/preflight/binding/build manifest B2. Sprawdź brak
   aktywnego call B1; zachowaj jego historyczne dowody bez przenoszenia tożsamości.
2. Zapisz metadane plików B2 przed Attach; operator potwierdza czysty stan UI.
   Po zatwierdzeniu użyj istniejącego Worker `--enumerate`, zweryfikuj binding zgodnie
   z pakietem wspólnym. Nie zamykaj pozostałych instancji TIA ani projektu B1.
3. Uruchom własny Host z chronionym config B2 (`dotnet <Host.dll> --config <config> --stdio`),
   tylko z target B2. Health przez CLI. Starego harnessu dwuinstancyjnego nie uruchamiaj.
4. Cykl 1 cold: request `project.inspect` z ProjectKey B2 i nowym B2/run/cycle key,
   submit bez domyślnego wait, zapisz acceptance/operation_id. Poll status/health/
   result/events według poll_after_ms; bounded wait lub exit CLI nie kończy call.
5. Sprawdź terminal SUCCEEDED/CONFIRMED i oba artefakty po ID/SHA. Exact target/build/
   epoch, graf, expected rodziny i coverage; raw observation i DiscoverySnapshot
   muszą mieć false/false IsModified. Sprawdź Dispose.Returned i ten sam żywy TIA
   PID/start/path; telemetry przed/po. Równe true/true nie zalicza cyklu.
6. Dopiero po wszystkich kontrolach powtórz kroki dla cykli 2–20, jeden naraz,
   nowe klucze, ten sam zdrowy Worker. Nie dodawaj fault matrix, MTA ani soak.
7. Po ostatnim Dispose zbierz 30 s idle, kontrolowany stop własnego Host przez stdin
   `stop`, potwierdzenie końca exact Workera. Brak końca w 10 s obserwacji → BLOCKED,
   bez kill/Save/naprawy. Operator potwierdza B2 UI i stan plików po release.
8. Zamknij i zahashuj artefakty B2, niezależna weryfikacja integralności, osobny trend
   review i raport. Wynik B2 nie przepisuje B1; podsumowanie E2 wymaga obu PASS.
   Real enrollment jest osobną decyzją po niezależnym source review, bez auto aktywacji.

Istniejące komendy CLI jak w [planie B1](E2-B1_TEST_PLAN.md), lecz discovery/config/
request/operation ID muszą pochodzić z **B2**. Nie kopiuj wartości zmiennych z B1.
Dokładny instrukcyjny request B2 (placeholdery blokują wysłanie):

```json
{
  "schema_version": "1.0", "method": "project.inspect",
  "project_key": "<B2-reviewed-UUID>", "idempotency_key": "<B2-run-UUID>-cycle-01",
  "params": {"inventory_scope": "all_supported", "fresh": true, "timeout_s": 120, "queue_timeout_s": 300}
}
```

## Wymagane dane i artefakty

UTC/monotonic timestamps, exact Host/Worker/TIA identities, operation/session/worker
epoch, submit/end-to-end/status/health/result/events i enumeration/Attach/read/Dispose
ms. Counts i pełny graf/coverage, IsModified przed/po. Handles/PrivateBytes/WorkingSet/
CPU przed/po każdym cyklu oraz sampler 1 s przez całą sesję, idle i release.
Oddziel cold 1, warm 2–20 i trzy warm okna z pakietu wspólnego. Zapisz pełne próbki,
delty, mediany/nachylenie i ograniczenia, bez arbitralnego limitu wzrostu uchwytów.

Własny root B2 zawiera authorization, preflight, binding, build i registry manifest,
source evidence refs, metadane/operator before/after, enumeration, decisions/audit,
20 kompletów acceptance/status/events/result i obu immutable artefaktów, telemetry,
cycles.csv, resource-review, release.json, report.json/md, evidence-manifest.json.
Receipt B1 jest tylko dodatkowym dowodem bramki. Nie zastępuje żadnego pliku B2.

## Kryteria akceptacji, STOP i kwalifikacja PASS

Wejście B1→B2 musi mieć PASS. Następnie pełne 20 poprawnych operacji B2, exact
binding/build/registry/profile/getters, false/false, STRUCTURE COMPLETE w bounded
zakresie, pozostałe coverage zgodne z rzeczywistymi ograniczeniami, stabilne obserwacje
grafu z wyjaśnionymi różnicami; potwierdzony brak niezamierzonej modyfikacji B2,
release, komplet artefaktów/hashów i zaakceptowany ograniczony trend review.
Historyczny Worker Handle Risk UNKNOWN nie staje się PASS.

STOP: każdy drift/niejednoznaczna tożsamość, dirty lub brak IsModified, getter error,
LIMIT_EXCEEDED, timeout/hang/IPC loss, brak Dispose/release, brak lub uszkodzenie dowodu,
nieautoryzowany dialog/nowe działanie użytkownika, presja zasobów lub nierozstrzygnięty
błąd bezpieczeństwa. Nie uruchamiaj kolejnego call, nie retry/rollback/reimport/Save,
nie zamykaj TIA/obcych sesji i nie czyść quarantine. Timeout/death/lease nie dowodzą
ustania Siemens call; zachowaj receipt/audit/UNKNOWN i wymagaj osobnej decyzji.

Negatywny test lub potwierdzona niezamierzona modyfikacja → FAIL; brak zgody/dowodu,
niepewność stanu albo RESOURCE_REVIEW_REQUIRED → BLOCKED. Sam SUCCEEDED to za mało.
Raport: format z pakietu wspólnego, `task=E2-B2`, exact project name i własne IDs;
functional_result i qualification_result osobno, B1 receipt ref oraz failed checks.
Integralność: wszystkie zamknięte pliki B2 → relative path/bytes/SHA-256 manifest,
niezależny drugi odczyt każdego hasha i zewnętrzny SHA manifestu w review receipt.
Zmiana/brak dowolnego pliku blokuje PASS. Runtime/production/NF-11 NOT_RUN.
