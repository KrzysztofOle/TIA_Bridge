# X-01 / B-03A — jawnie uruchamiane testy Engineering V19

Ten katalog nie należy do standardowego discovery offline. Testy Engineering wymagają osobnego zakresu i autoryzacji dla dokładnych getterów, świeżej identyfikacji procesów/projektów oraz instalacji V19. Zgoda X-01 nie obejmuje automatycznie nowych odczytów B-03A. Testy X-01 nie uruchamiają TIA, nie otwierają innego projektu ani nie wykonują Save/Import/Compile/Download lub Runtime.

## Build i kontrolowany przebieg

**Dla B-03A/E2 nie uruchamiaj poniższego historycznego harnessu X-01.** Wymaga dwóch
projektów, nadaje nowe klucze i przeplata A/B, a jego config nie provisionuje Local
Trusted ani exact evidence. Bieżąca kolejność i procedura jednego targetu przez istniejący
Host/API/CLI są w [E2-A](../../docs/B03A_E2_A_PREPARATION.md), [B1](../../docs/E2-B1_TEST_PLAN.md)
i [B2](../../docs/E2-B2_TEST_PLAN.md). Bootstrap i autoryzacja są warunkiem wejścia,
nie flagą obejścia providera. E2-A nie uruchamia żadnego z poniższych poleceń vendor.

```powershell
dotnet restore TiaBridge.Engineering.slnx --locked-mode
dotnet build TiaBridge.Engineering.slnx -c Release --no-restore
dotnet tests/engineering/TiaBridge.V19.Qualification/bin/Release/net10.0/TiaBridge.V19.Qualification.dll --project-root X:\TESTY_TIA_Bridge --cycles 20
```

Uruchamiaj z root repo. `--project-root` jest zatwierdzonym katalogiem testowym, nie argumentem publicznego API. Harness wymaga dokładnie dwóch plików AP19 i dokładnie dwóch publicznych instancji o zgodnych ścieżkach; brak/niejednoznaczność blokuje Attach. `--cycles` 1..100 oznacza osobne cykle dla każdej instancji, przeplatane A/B. Defaultu automatycznego live test nie ma.

Harness tworzy własny UUID root w LOCALAPPDATA/TIA_Bridge2/dev/x01 na lokalnym stałym NTFS, marker StorageLocation, plikową SQLite i prywatne discovery Host. Worker i nievendorowe zależności są kopiowane do własnego katalogu wykonawczego poza repo/projektami. Zwykły Host/CLI i ich istniejący scheduler/outbox/IPC realizują pełny przepływ; harness nie jest drugim silnikiem. Proces CLI health jest uruchamiany osobno. Sekretów bootstrap/discovery nie drukuje się w stdout, raportach lub Git.

Każdy cykl wykonuje submit → operation_id → status/health podczas oczekiwania → events → result z kontrolą immutable artifact SHA i porównaniem exact PID/start. Trace workera zawiera tylko jawne dozwolone wywołania i dane korelacji. Błąd lub timeout zostaje zapisany; dalsze konfliktujące wywołania nie są uruchamiane bez dowodu zakończenia poprzedniego. Sam brak odpowiedzi nie potwierdza dialogu autoryzacji — diagnoza GUI wymaga właściwej zgody.

## Dowody i ograniczenia

Własny root zawiera bindings, enumeration, metadata plików przed/po, raporty operacji, audit wywołań, run-progress i qualification.json. Raportowanie zachowuje nieudane próby, rzeczywistą liczbę próbek i p50/p95/max; p99 wyłącznie przy N≥100. Snapshot metadanych plików nie jest dowodem pełnej niezmienności pamięci TIA lub braku wewnętrznych zapisów producenta. Nie czyta się wewnętrznych baz Siemens i nie zapisuje do katalogów projektów.

Nie usuwaj dowodów po błędzie. Sprzątaj wyłącznie własne potwierdzone procesy. Pozostały worker po nieukończonym Attach/Dispose wymaga jawnego dowodu lub autoryzowanej czynności administracyjnej; zamknięcie Host nie jest potwierdzeniem zamknięcia sesji Siemens. TiaPortalProcess.Dispose i zamknięcie cudzych portali są zabronione. Katalog B-02C nie jest sprzątany przez ten harness.

[Raport rzeczywistej kwalifikacji](../../docs/X01_TIA_V19_QUALIFICATION.md) podaje, które scenariusze faktycznie wykonano. Samo istnienie tego harnessu nie zalicza 20 cykli, MTA/STA, resource release, parent-loss, restartu lub obu projektów. [Regresje offline](../README.md) używają Fake oraz osobnego net48 IPC probe bez vendor DLL; nie zastępują Engineering.

## Rozszerzone scenariusze

Przyszły, osobno autoryzowany przebieg B-03A musi potwierdzić dokładny zestaw getterów z [raportu B-03A](../../docs/B03A_DISCOVERY_QUALIFICATION.md), exact project binding, `IsModified` przed/po, powtarzalność hierarchii i brak zmian metadanych plików projektu. Nowa obserwacja zawiera liczbę handles, Private Bytes i Working Set workera przed Attach i po Dispose oraz osobne czasy enumeracji, Attach, read i Dispose. Te próbki są punktami pomiaru pojedynczej operacji; wymagane powtórzenia nadal służą ocenie trendu. Brak świeżego pomiaru pozostaje NOT_RUN, a trend handles UNKNOWN.

Opcjonalne `--apartment MTA` porównuje model wątkowy (domyślnie STA). `--worker-exe <ścieżka zatwierdzonego EXE na lokalnym NTFS>` pozwala ponownie użyć tej samej tożsamości aplikacji Siemens; harness weryfikuje hash wszystkich nie-PDB zależności względem bieżącego build i odmawia nadpisania niezgodnego zestawu. Nie edytuje whitelisty. Obsługa dialogu dostępu wymaga procedury A/B z AGENTS; w X-01 użytkownik wybrał B i sam obsługiwał jednorazową zgodę.

`--faults all` dodaje siedem scenariuszy z istniejącym procesem StorageProbe/HostApi i rzeczywistym V19: queued cancel, running cancel, late result po deadline, odrzucenie starego epoch, utrata pipe, parent loss oraz zakończenie dokładnego własnego workera przed dispatch. Bariery są jawne: before-dispatch, dispatch-sent i receipt-received, z lokalnymi reached/release. Nie zabija się TIA ani workera wykonującego niepotwierdzony vendor call. Parent loss wymaga rzeczywistego Dispose.Returned i końca dziecka przed recovery. Każdy przypadek powtarza oryginalny submit po restarcie i kontroluje brak nowego dispatch. Utrata pipe to testowy internal composition seam, bez endpointu HTTP. Osiem równoczesnych klientów status/health, odczyt result/events i syntetyczna edycja własnego pliku na NTFS są mierzone niezależnie od vendor owner. Przypadki zostają zaliczone dopiero przez faktyczne wykonanie, nie obecność kodu.

## Opt-in startup i loader bez Attach

Po zatwierdzonym wdrożeniu workera w prywatnym katalogu NTFS:

```powershell
$env:TIA_BRIDGE_V19_WORKER = '<dokładna ścieżka własnego wdrożonego TiaBridge.Worker.V19.exe>'
py -3 -B -m unittest discover -s tests/engineering -p test_worker_preflight.py -v
```

[test_worker_preflight.py](test_worker_preflight.py) używa wyłącznie standardowej biblioteki Python i zbudowanego workera z lokalną instalacją V19. Wymaga exact path pod własnym root X-01 z markerem, lokalnego fixed NTFS i braku reparse. Cztery testy sprawdzają x64/self-test bez vendor load, zatwierdzoną DLL/wersję/hash, odmowę syntetycznego vendor shadow i nieznanego argumentu. Nie wykonują Attach lub dostępu do projektu. Syntetyczna nieprawidłowa DLL powstaje wyłącznie w nowym własnym UUID pod root dowodowym poza Git; jest odrzucana, nie wykonywana. Dowody pozostają zachowane, bez automatycznego cleanup obcych katalogów.

Końcowy raport rozdziela PASS ograniczonego Engineering od FAIL pełnej regresji na exFAT. Nie rozpoczynaj kolejnego zadania na podstawie samego PASS Attach; [kwalifikacja](../../docs/X01_TIA_V19_QUALIFICATION.md) wskazuje pełne kryteria i ograniczenia.
