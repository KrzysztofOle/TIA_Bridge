# Siemens V19 — eksperymentalny worker read-only X-01

Worker jest osobnym procesem .NET Framework 4.8/x64. Host i Core nie referencjonują Siemens. Stan kwalifikacji, wyniki obu instancji i ograniczenia zawiera [raport X-01](../../docs/X01_TIA_V19_QUALIFICATION.md). Zbudowany plik EXE i działająca enumeracja nie potwierdzają jeszcze Attach, Dispose lub gotowości B-03A.

## Budowanie i wymagania

```powershell
dotnet restore TiaBridge.Engineering.slnx --locked-mode
dotnet build TiaBridge.Engineering.slnx -c Release --no-restore
```

Wymagane Windows x64, runtime .NET Framework 4.8, SDK .NET 10 oraz legalna lokalna instalacja PublicAPI V19. Brak systemowego targeting packa uzupełnia przypięty Microsoft.NETFramework.ReferenceAssemblies 1.0.3 w cache NuGet, bez instalacji systemowej lub praw administratora. System.Text.Json 10.0.14 jest zależnością wyłącznie wariantu net48 biblioteki IPC. Pierwsze przywrócenie pakietów wymaga zatwierdzonego źródła NuGet; kolejne są locked/offline z cache.

Osobne rozwiązanie Engineering zapobiega wymaganiu instalacji Siemens przy standardowym buildzie/testach offline. `SiemensV19Root` służy wyłącznie wskazaniu instalacji na etapie kompilacji; runtime akceptuje tylko chronioną lokalizację Portal V19 w Program Files. Nie kopiuje się Siemens DLL do bin, repo lub pakietu workera. Reference ma Private=false. Resolver sprawdza reparse, dokładną nazwę, wersję 19.0.0.0 i odrębne tokeny PublicAPI/Contract/ClientAdapter. Obecność Siemens DLL obok EXE powoduje odmowę. PublicAPI DLL może nie mieć podpisu Authenticode; strong-name identity i zatwierdzona lokalizacja nie są twierdzeniem o takim podpisie.

## Uruchomienie i granica zaufania

Zwykłe uruchomienie bez argumentów wymaga bootstrapu na odziedziczonym stdin od nadzorcy. Losowy sekret/nonce/epoch nie trafiają do argv ani logów. Używany jest [IPC 1.1](../../contracts/ipc/v1.1/message.schema.json): to samo length-prefix/HMAC/sequence/limity co 1.0, z jawnym profilem read-only. Brak downgrade do Fake 1.0. Hello wiąże peer PID/start i worker ID `tia-bridge.v19.read-only`. Serwer ma CurrentUserOnly; net48 nie udostępnia tej opcji klienta, dlatego klient sprawdza dokładny PID serwera i SID jego tokenu. Nie poszerza się ACL pipe.

Zaufana konfiguracja operatora zawiera `v19_worker_exe` na lokalnym NTFS poza repo oraz `v19`: dokładne dwa lub jeden binding ProjectKey/PID/start/AP19, project_root i apartment STA/MTA. Nie pochodzi ona z HTTP. Nie łączy się profili Fake z V19 ani nie dopuszcza synthetic mutations. Host rejestruje istniejące Core ProjectKey; request wybiera tylko zarejestrowaną tożsamość. Brak dokładnego bindingu oznacza odmowę. Udostępniona jest wyłącznie project.inspect i dotychczasowe metody metadata Host; pozostałe metody pozostają UNSUPPORTED.

Do diagnostyki istnieją wyłącznie stałe argumenty:

```powershell
src/TiaBridge.Worker.V19/bin/Release/net48/TiaBridge.Worker.V19.exe --self-test
src/TiaBridge.Worker.V19/bin/Release/net48/TiaBridge.Worker.V19.exe --loader-check
src/TiaBridge.Worker.V19/bin/Release/net48/TiaBridge.Worker.V19.exe --enumerate
```

Self-test nie ładuje Siemens. Loader-check ładuje tylko zatwierdzoną bibliotekę i zwraca identity/location/SHA. Enumerate używa publicznego TiaPortal.GetProcesses, bez Attach i bez zapisu projektu. Nie wybieraj odbiorcy według samego PID lub tytułu okna.

## Odczyt, ownership i zwalnianie

Jedyny dedykowany wątek STA/MTA posiada wszystkie proxy Siemens. IPC i heartbeat co 2 s działają oddzielnie. Mutex bieżącej sesji/SID dodatkowo serializuje wywołania V19 między własnymi procesami; porzucony mutex daje odmowę, nie zgodę na retry. Dokładne PID/start/executable/SID i publiczny ProjectPath sprawdzane są przed Attach, ponownie po Attach i po Dispose. Nie otwiera się projektu, nie tworzy nowej instancji TIA i nie adoptuje innego PID po błędzie.

Odczyt obejmuje Project.Name/Path/IsModified i ograniczone Devices/DeviceItems/SoftwareContainer (maks. 64 urządzenia, 256 węzłów, głębokość 8). To nie pełny inventory bloków, eksport, native HMI reader lub kwalifikowana synchronizacja. Nazwy i locatory diagnostyczne nie są nowym ObjectId. DeviceSummary ma identyfikator ważny tylko w obserwacji; inventory obiektów pozostaje jawnie niezakwalifikowane. Host sprawdza DTO, binding i limity, publikuje dowód jako istniejący immutable artifact SHA-256 i dopiero wtedy atomowo utrwala wynik/journal. Nie przyjmuje wzorca sukcesu od konfiguracji.

Dispose dotyczy wyłącznie uzyskanego TiaPortal uchwytu Attach. Nigdy nie wywołuje się TiaPortalProcess.Dispose: publiczna dokumentacja opisuje je jako zamknięcie portalu. Callback Disposed tylko odkłada ograniczoną informację o wątku, bez wywołań vendor lub IO. Błąd audit IO nie może pominąć Dispose. Brak callbacku nie dowodzi zachowania innych typów zdarzeń.

Cancel jest sprawdzany przed wejściem do odczytu; podczas vendor call pozostaje żądaniem bez obietnicy przerwania. Nie ma Thread.Abort, Kill TIA lub automatycznego retry. Powtórzony dispatch w tym samym epoch zwraca istniejącą odpowiedź; cache jest ograniczony do 1000. Nie jest drugim ledgerem lub gwarancją exactly-once Siemens.

Po utracie rodzica/IPC worker nie abortuje vendor call: kończy na właścicielu dopiero po powrocie i próbie Dispose. Host zachowuje aktywny slot do dowodu quiescence/końca dokładnego procesu. Stop własnego workera ma bounded wait i może zwrócić niepotwierdzone zakończenie. Timeout lub GUI dostępu nie uzasadnia kolejnego konfliktującego wywołania. Niepewna mutacja nie jest obsługiwana przez ten worker, a dotychczasowe UNKNOWN/quarantine w Host pozostają bez zmian.

Testy i odrębna zgoda Engineering: [instrukcja X-01](../../tests/engineering/README.md). Dostęp do nowego EXE może wymagać dialogu Siemens. Nie stosuj automatycznej whitelisty, edycji rejestru lub grantów innych sesji; GUI wyłącznie według AGENTS.md.

## Capability matrix X-01

| Zakres | Kontrakt | Implementacja | Kwalifikacja / dopuszczenie |
|---|---|---|---|
| Public enumeration, exact Attach/metadata/Dispose | project.inspect + IPC 1.1 | bounded read-only | tylko V19 Update 3, dwie instancje, jawny binding i zakres z raportu X-01 |
| Hardware/software-container inventory | project.inspect | ograniczone metadata | PARTIAL; brak pełnego modelu PLC/HMI lub niezależnych jednostek eksportu |
| Eksport, import, Save, Compile, Download, Runtime | przyszłe operacje | brak | UNSUPPORTED/deny; token HTTP i X-01 nie nadają praw |

Registry Core i jego default deny pozostają niezależne. Zaufany profil X-01 nie jest grantem mutacji lub potwierdzeniem kwalifikacji każdej metody Siemens. [Raport](../../docs/X01_TIA_V19_QUALIFICATION.md) ogranicza zakres uzyskanych dowodów; pełne adapter capabilities należą do następnych osobno zleconych etapów.

X-01 kwalifikuje wyłącznie `inventory_scope=all_supported` i `fresh=true` (również domyślne). Filtrowanie plc/hmi i jawny cache fresh=false pozostają UNSUPPORTED bez dispatch; nie wolno po cichu zamieniać ich na inny zakres odczytu. Mieszany rodzic Device ma rodzinę UNKNOWN, a rzeczywiste typy PLC/HMI zachowuje bounded inventory.
