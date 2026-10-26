# D-11 — ocena ograniczonej bramki X-01 read-only

Data decyzji i oceny: 2026-10-26. Podstawa: jawne zatwierdzenie użytkownika, [D-11](../../docs/09_RISKS_AND_DECISIONS.md#decyzje) i [kryteria 07](../../docs/07_TEST_STRATEGY.md#ograniczona-bramka-x-01-read-only). Jest to interpretacja istniejących dowodów według zatwierdzonych kryteriów, nie nowe uruchomienie testów. [Wersja maszynowa oceny](x01-limited-gate.json) jest dokumentem decyzji, a nie nowym kontraktem API lub zamiennikiem reportera B-02D.

```text
B-02 OFFLINE TEST EXECUTION: PASS
B-02 OFFLINE QUALIFICATION: FAIL
X-01 LIMITED READ-ONLY GATE: PASS
PRODUCTION QUALIFICATION: NOT_RUN
ENGINEERING: NOT_RUN
RUNTIME: NOT_RUN
```

Ograniczony PASS dotyczy udokumentowanego lokalnego NTFS i zaimplementowanego zakresu Host/Fake. Historyczny pełny profil obejmuje M:/exFAT i nadal ma FAIL. Żaden z tych statusów nie oznacza poprawności Siemens, gotowości produkcyjnej ani zgody na rozpoczęcie X-01. Zmiana kodu lub środowiska wymaga ponownej oceny dowodów; nie przenosi się PASS automatycznie.

## Niezmienione źródła

Dowody z commita `51d859f5b361b614672ccf9abb59a05606c461c5`, run `e46332b7aee247c28cc9ca3910e69c48`, fingerprint źródeł LF `72a2501c733d0286ffa1fe5fe8d74458bcef78a8d0e0a45c5790a0bd88d92ce7`:

| Plik historyczny | SHA-256 bajtów |
|---|---|
| [QUALIFICATION.md](QUALIFICATION.md) | `64c8bcece5907244d5b6ca9cdc120835a0296c758fd1fd7d88448b9957aba776` |
| [qualification.json](qualification.json) | `cbb66c2e5208f53e33f6aa66c4645b99d6b9f34304f2abc965715f5a046ebb97` |

Powyższe hashe dotyczą niezmienionych bajtów zastanego checkout Windows/CRLF. JSON oceny podaje również hashe oryginalnych blobów Git/LF z commita źródłowego; test zgodności akceptuje wyłącznie te dwie reprezentacje EOL tej samej treści. Nie zapisuje ani nie normalizuje plików historycznych.

Zachowano dawne OFFLINE/GATE FAIL, X-01 NOT_READY, NF-03 exFAT FAIL, NF-11 NOT_RUN, ENGINEERING/RUNTIME NOT_RUN i wszystkie pomiary. Reporter oraz matrix.json zachowują historyczną konserwatywną agregację; aktualną ograniczoną interpretację przedstawia niniejszy raport. Pełne kryteria produkcyjne nie zostały zmienione. 748 .NET i 24 Python PASS, 0 FAIL/0 SKIP, Build Release PASS są wynikami tamtego przebiegu, nie bieżącego zadania dokumentacyjnego.

## Warunki RT i bezpieczeństwa wykonania

| Kryterium | Wynik i dowód w historycznym JSON |
|---|---|
| RT-03/05/06/15/16 | wszystkie PASS, odpowiednio 18/4/6/12/12 dowodów; `/rt` i `/scenarios` |
| Trwałość po restarcie | PASS: admission-AfterCommit, queued-restart, running-result-after, pozostałe granice dispatch/receipt/result |
| Hang, timeout, late response, utrata procesu/IPC | PASS: worker-loss-HeartbeatWithoutProgress/NoHeartbeat/Exit/Disconnect i deadline-late-receipt; heartbeat nie dowodzi postępu |
| Idempotencja i cancel | PASS: idempotency-100 (100 identycznych żądań/8 klientów, 1 ID/efekt, konflikt 409), cancel-race-False/True; anulowanie nie dowodzi zatrzymania call |
| UNKNOWN/quarantine, restart, brak replay | PASS: lease-expiry-live-call, deadline-late-receipt, owned-child-lock; pierwotny UNKNOWN pozostaje, lease/cancel/late success nie zwalniają kwarantanny |
| Stary epoch | PASS: old-epoch-receipt i ipc-epoch nie zatwierdzają wyniku |
| Izolacja procesów i krytyczne błędy | PASS w zakresie Fake: IPC attacks odrzucone, sentinel przetrwał, own child anomaly wykryta; raport nie wskazuje unresolved critical wykonania/izolacji, regresje PASS; obecne zadanie nie zmienia kodu |

Są to dowody syntetyczne. RT-05 używa jawnego testowego P/B guard, a nie planera lub readbacku TIA. PASS Fake nie kwalifikuje COM/Dispose/Job Object ani rzeczywistych odbiorców. Foreign SID i rzeczywista utrata zasilania pozostają NOT_RUN; brak zatwierdzonego środowiska. Nie osłabiono ACL ani zasad UNKNOWN.

## Wymagania NF we właściwym profilu

Profil: Windows 10.0.22000, .NET 10.0.13 x64, Xeon E3-1240 v5, 4 rdzenie/8 logicznych, 31,93 GiB RAM. State SQLite/journal/outbox/artifacts na lokalnym C:/NTFS poza repo, bez Siemens. Operacje zależne od kwalifikowanego save/replace wyłącznie na syntetycznym W lokalnego NTFS. Host API mierzono na tym samym NTFS state przy 1/8 klientach; binaria/repo M:/exFAT nie są dowodem kwalifikacji workspace replace. Źródła: `/environment`, `/measurements`, `/nf`, `/test_results` w historycznym JSON.

| Warunek | Dowód i zakres PASS dla ograniczonej bramki |
|---|---|
| NF-01 | status N1000/1 i N1000/8: p95 6,1413/14,5978 ms, p99 6,8501/19,3525 ms; health N1000/8 p95 25,6193, p99 34,2449 ms; podczas Hang |
| NF-02 | submit N64/8: p95 236,3253 ms, p99 318,9709 ms; trwały intent przed potwierdzeniem |
| NF-03 NTFS | workspace-replace: 100 zapisów/replace 1 MiB, interwał 100 ms, SHA, podczas Hang; p50/p95/p99 2,4019/3,2648/4,0410 ms; filesystem NTFS potwierdzony w pomiarze |
| NF-04 | worker-loss-NoHeartbeat PASS z asercją klasyfikacji <10 s, rozdzielone heartbeat/progress; test procesowy i watchdog regresyjny, bez probe Siemens |
| NF-05 | startup/history N20 p95 1087,9415 ms; safe read po recovery 847,7089 ms; nie kwalifikuje zimnego reboot OS ani TIA |
| NF-06 | regresje Queue_deadline_fails_without_dispatch_or_worker_start i Queue_deadline_is_enforced_even_while_another_call_is_hung w SchedulerSafetyTests; WAIT_TIMEOUT bez dispatch, domyślne 300 s i krótszy limit, kontrolowany zegar; pełny historyczny .NET PASS |
| NF-12 | admission/dispatch/receipt/result crash boundaries, artifacts/WAL-tail: zaakceptowane ID zachowane, brak fałszywego sukcesu; brak dowodu hardware power loss lub całkowicie zniszczonego storage |
| NF-14 | default deny przed adapterem, niezaimplementowane operacje odrzucone i brak fizycznego wykonawcy; regresje CapabilityTests i HttpApiTests w historycznym PASS; nie kwalifikuje przyszłego adaptera download/recipient/allowlist |
| NF-15 | queue-capacity: 1000 aktywnych, kolejna operacja odrzucona, duplicate/status/health dostępne; regresje limitów Core/HTTP, w tym Oversize_bodies_are_rejected_for_declared_and_streamed_lengths; przyszłe planowanie/adaptery nadal osobno |
| Rzeczywisty idle ≥5 min | idle-300s: 306,2474483 s, CPU 0,9075%, RAM Host 89,33/Fake 37,34 MiB; PASS pomiaru idle, pełny NF-08 nadal NOT_RUN |
| Krótki przebieg ≥1000 operacji | operations-1001: 1001 wyników/513,2943582 s, jeden efekt na ID, dwa epoch po recyklingu idle; nie jest 8 h soak |

NF-03 exFAT pozostaje FAIL: kontrolne replace bez workera p95 205,5911 ms; pozytywne próbki przy Hang nie kwalifikują całego profilu. NTFS PASS nie dopuszcza exFAT, nie zmienia konfiguracji dysków i nie uzasadnia delete+copy lub relokacji. NF-11 nadal NOT_RUN, minimum 8 h i pełny zakres obowiązkowy w B-10B; R-14 aktywne. Skala, retencja, pełne NF-08/09/10/13 oraz późniejszy Siemens/live/Runtime pozostają oddzielną kwalifikacją.

## Warunki przed osobno zleconym X-01

- Osobne zadanie i odrębna autoryzacja ograniczonego X-01 read-only. Nie uruchamiać go w ramach tej decyzji.
- Sprawdzenie filesystemu wszystkich używanych lokalizacji objętych NTFS. `X:\TESTY_TIA_Bridge` według użytkownika zawiera dwa dedykowane projekty V19; X: pozostaje NIEZWERYFIKOWANY. W tym zadaniu nie otwierano ani nie kopiowano/modyfikowano/usuwano projektów. Nie ma automatycznej relokacji.
- Weryfikacja exact DLL V19, praw nowego net48/x64 worker, świeżych PID/start/project identity i własności procesów w autoryzowanym eksperymencie. Są to przedmioty kwalifikacji X-01, nie wyniki Fake.
- Potwierdzenie zakresu i izolacji: folder testowy ani zgoda na przyszłą utratę projektów nie dowodzą izolacji PLC/HMI. Późniejsze mutacje wymagają osobnego zadania i bramek; fizyczny download pozostaje domyślnie wyłączony.

[Kontrola dokumentacyjna](../README.md#kontrola-dokumentacji) sprawdza hashe i zgodność oceny z istniejącymi dowodami, bez nowych pomiarów i bez uruchamiania Host lub Siemens. Brak wymaganego dowodu w nowej ocenie daje BLOCKED, negatywny wynik w wybranym profilu daje FAIL.
