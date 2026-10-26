# B-03A/E1-R — naprawa resilience artifact tests

Data: 2026-10-26. Baza: `956df6c69922aa73776f1afd6f2f378ff79f375f`, czyste main równe origin/main po fetch. Zakres: syntetyczny Host/Fake/Storage, bez Siemens, E2, B-03B i NF-11. Nie zmieniono kodu produkcyjnego ani kontraktów. Raport nie zastępuje historycznych kwalifikacji.

## Przyczyna i odtworzenie

Każdy z trzech przypadków `Corrupt_artifact_never_confirms_a_result_and_does_not_destroy_historical_status` uruchomiono osobno; wszystkie FAIL. Następnie dwa przebiegi grupowe: każdy 0/3 PASS. Objawy powtarzały się w uruchomieniach osobnych i grupowych: modified po completion dawał 200 zamiast 422, truncated/missing przed completion nie powodowały scheduler DEGRADED w 10 s. Każda fixture ma własny UUID state na lokalnym C:/NTFS i własne procesy Host/Fake; kolekcja HTTP serializuje te testy. Nie stwierdzono współdzielenia plików lub procesu między przypadkami.

E1 dodało ponowne rozwiązywanie profilu przed przygotowaniem dispatch i przed wysłaniem IPC. Produkcyjny `ConfiguredFakeProfile` zachowuje przygotowany spec per OperationId. Testowy wrapper `HttpResilienceProbe.Profile` tego nie robił: przy każdym `Resolve` publikował nowy proof i szablon, nadpisując `result-artifact.json`. Scheduler wysyłał nadal pierwszy spec. Marker wskazywał trzeci, niewykorzystany proof; test uszkadzał niewłaściwy plik. Integralność faktycznego wyniku pozostawała poprawna, więc 200 i brak DEGRADED były prawidłową reakcją na tę ingerencję.

Zależności trzech przypadków: ApiFixture/StoreFixture zapewniają własny state/config/proces i SQLite; ResilienceEvidence obsługuje reached/release i zapis dowodów; StorageProbe/HostRuntimeHooks składają rzeczywisty HostApi z wrapperami Profile/Supervisor/Connection; ConfiguredFakeProfile i domyślne IExecutionProfile.Revalidate przygotowują/ponownie rozwiązują spec; OperationScheduler używa FakeWorkerSupervisor, Protocol i rzeczywistego FakeWorker; SqliteOperationStore/ExecutionStorage zapisują dispatch/receipt/result i recovery; FileArtifactStore weryfikuje pliki; handler result w HostApi i ApiResponses.Health mapują wynik na HTTP oraz LastError na scheduler DEGRADED. Wire request/result fixture `sync.apply`, ContractJson/Core lifecycle, Microsoft.Data.Sqlite i lokalny NTFS są wspólnymi zależnościami. Nie ma cache HTTP ani periodycznego skanera integrity w tym przepływie.

Instrumentacja przed naprawą zapisała trzy materializacje i niezależnie odczytała `artifact_refs` z rzeczywistej odpowiedzi Fake przed barierą receipt. Asercja bindingu wykazała ID markera `artifact-69d9822f964b4490977df3a5bf367df4`, podczas gdy odpowiedź wskazywała `artifact-2a18af897c6b4102bcdd673f36e2c4b3`. Dowód: `.tia-bridge/e1-r/binding-diagnostic.trx`. Nie znaleziono dowodu cache HTTP, starego wyniku walidacji magazynu, pominięcia hashów lub wyścigu publikacji w kodzie produkcyjnym.

## Granice mechanizmu i poprawka

Przepływ pozostaje: API utrwala intent/outbox przed 202 → scheduler tworzy wejście/spec i utrwala dispatch z owner/epoch/fence → Fake czyta dokładny szablon → receipt → trwały worker receipt → `Complete` weryfikuje referencje, rozmiar i SHA poza transakcją, zachowując uchwyty odmawiające write/delete przez commit → terminalny raport/event/result w jednej transakcji → GET result ponownie weryfikuje pliki.

Naruszona była wyłącznie granica testowego markera do faktycznie wysłanego szablonu. Wrapper przechowuje teraz deskryptor syntetycznego szablonu per OperationId; każda rewalidacja nadal wywołuje bieżący profil wewnętrzny i zachowuje jego pola kwalifikacji. Nie cache'uje decyzji providera E1. Marker i trace materializacji powstają raz na operację. Odpowiedź odebrana z workera jest zapisywana przed istniejącą barierą; test sprawdza jej ID/SHA i jedną materializację przed uszkodzeniem pliku.

Przed completion test potwierdza RUNNING i brak receipt w DB na barierze; po ingerencji: receipt zachowany, zero operation_results, scheduler DEGRADED w dotychczasowym limicie, result 202. Restart utrwala RECONCILIATION_REQUIRED/UNKNOWN/quarantine, zachowuje jeden attempt i jeden efekt. Po completion uszkodzenie daje 422/VERIFY_FAILED, status/history nadal SUCCEEDED/CONFIRMED; restart tego nie przepisuje. Health nie wykonuje skanu filesystemu inline; DEGRADED schedulera oznacza wykryty błąd jego pętli, a nie dowolne późniejsze uszkodzenie historycznego artefaktu.

## Macierz regresji

| Wymaganie | Dowód zachowania |
|---|---|
| 1. Poprawny wynik | Nowe cztery procesowe przypadki odczytują 200 przed uszkodzeniem i sprawdzają ID/SHA |
| 2–4. Modified/truncated/missing | Trzy pierwotne przypadki + cztery nowe warianty po completion |
| 5. Podmiana | Nowy plik o tym samym rozmiarze i innych bajtach zastępuje zarejestrowany plik; 422 przed/po restarcie |
| 6–7. Błędny hash / hash innego pliku | Trzy nowe przypadki Storage: losowy hash, prawidłowy hash innego pliku, ID innego pliku ze starym hashem; brak commitu wyniku |
| 8. Stara walidacja | Poprawny odczyt 200 poprzedza ingerencję; osiem kolejnych klientów otrzymuje 422, bez użycia poprzedniej walidacji |
| 9. Restart | Cztery uszkodzenia po completion nadal dają 422 po restarcie; pierwotne przypadki przed completion zachowują UNKNOWN/quarantine |
| 10. Oczekiwanie na completion | Deterministyczna bariera receipt, ingerencja w dokładny proof przed zapisem receipt/wyniku |
| 11–12. Współbieżność | Osiem równoległych klientów status/result; inna operacja kończy się w tym czasie i ma poprawny wynik; odrębny ukończony artefakt nadal daje 200 |
| 13. Brak fałszywego 200 | Wszystkie odczyty uszkodzonego wyniku wymagają 422; przed completion pozostaje 202 |
| 14. DEGRADED | Pierwotne truncated/missing sprawdzają rzeczywisty błąd pętli, bez wymuszania health i bez wydłużania timeoutu |
| 15. Historia | Porównanie sequence, UTC i serializowanych raportów wszystkich eventów przed/po ingerencji i restarcie; jeden attempt/efekt |

Dodano siedem przypadków: cztery procesowe i trzy Storage. Oryginalne testy pozostają; zachowano ich trzy scenario IDs oraz macierz i reporter kwalifikacji. Nowa regresja nie emituje dodatkowych qualification scenario IDs i nie zaburza zamkniętej listy dowodów. Przy tworzeniu regresji wykryto i poprawiono wadliwe porównanie rekordów zawierających ImmutableArray: historia jest porównywana według zapisanej treści raportów. Ten przejściowy FAIL zachowano w `artifact-regression.trx`.

## Walidacja i kwalifikacja

Po naprawie pierwotne przypadki: 3/3 PASS. Regresja artefaktów/Storage wraz z procesowym testem zaufania E1: 26/26 PASS. Następnie rozszerzono nową regresję o dodatkową operację przy równoległych odczytach; tę finalną wersję zweryfikował jeden pełny, niezmieniony `scripts/validate_offline.py`, uruchomiony z zewnętrznego QA Python na lokalnym NTFS. Preflight `.tia-bridge/e1-r/preflight.json` potwierdził C:/X: Fixed/NTFS, brak reparse w sprawdzonych łańcuchach, pamięć i miejsce, CPU 9/14/14%, brak kolejki dyskowej i brak aktywnych Host/Fake/TIA Portal przed startem. `Get-Volume` nie udostępnił woluminów w tej sesji; format/typ i miejsce niezależnie potwierdziły CIM Win32_LogicalDisk oraz kontrole Storage w testach. Nie zmieniano środowiska Windows.

Run ID: `b18c8af2274240ebb714d72c3563a3e0`, UTC 20:26:12.529169–20:45:49.325556. Dowody są w `.tia-bridge/resilience/<run-id>/`. Run wiąże bazowy Git SHA i dirty tree z hashami rzeczywiście zbudowanych źródeł/binariów; commit powstaje dopiero po walidacji. Po starcie runnera nie zmieniano kodu/testów.

| Kontrola finalnego kodu | Wynik |
|---|---|
| Locked restore / Build Release | PASS; 0 warnings, 0 errors |
| Core | 564/564 PASS |
| Host | 264/264 PASS |
| Wszystkie .NET | 828/828 PASS; 0 FAIL/SKIP |
| E1 | 19/19 PASS: 18 QualificationEvidenceTests Core + 1 integracja podpisów/hashów/rewalidacji Host |
| IPC | 34/34 IpcAndPolicyTests PASS, w tym rzeczywisty net48 probe; łącznie scheduler/IPC 76/76 |
| Resilience | 52/52 PASS; 49/49 wymaganych scenario records PASS |
| Artefakty | 25/25 PASS: Storage 15, pierwotne artifact/WAL 6, nowe procesowe 4 |
| Python | 42/42 PASS; 0 FAIL/SKIP |
| Generatory kontraktów / wektorów | PASS / PASS |
| Audyt dokumentacji/linków | PASS; 55 Markdown, 474 lokalne linki, 0 błędów |
| Format whitespace verify / diff check | PASS / PASS |
| Kanoniczny runner | exit 0; test_execution_status PASS; resilience_gate BLOCKED |

Niezależnie zweryfikowano 73/73 pliki run manifestu i trzy bundle pełnych pomiarów, z 464/464 próbkami NF-02/NF-03, spójnością run/build/source i odtworzeniem agregatów. Manifest SHA-256: `8de8fcc70c8d9355e5282d286134ec94849a13dea3b704fc6c924a7ba10c34ca`; run.json: `c59be092357bac394b486806edfc15cbf3ee781d92ce010f9ef55776c36e8bd0`; resilience.json: `62b05c974b661a4734d19d378e19360b840980dc41e2f021f392e71b86780d9a`. Host TRX SHA-256: `5919062e7100606546dbde27f68a6336594b5691c1187ce8810d362ab3decb85`; Core TRX: `9c1524f820172574e4914d98352031a5a1de97d002ee7f34138d02c0af14b39f`. Odrębne zestawienia diagnostyczne i finalnej walidacji są w `.tia-bridge/e1-r/diagnostic-summary.json` oraz `final-validation.json`; nie nadpisano zamkniętego runu.

### Nowa ograniczona ocena D-11

RT-03/05/06/15/16 oraz NF-01/02/03/04/05/06/12/14/15: PASS na lokalnym NTFS. Rzeczywisty idle **306.1528275 s**, CPU **0.7821359255%** wszystkich logicznych CPU, peak Host **93.0234 MiB**, Fake **38.5508 MiB**. Seria **1001 operacji**, **442.7658765 s**, PASS; bez krytycznego błędu wykonania/izolacji. Nowa ograniczona D-11: **PASS**, wyłącznie Host/Fake i obecny profil NTFS.

| Pełne próbki | N | p95 ms | p99 ms | Wynik |
|---|---|---|---|---|
| NF-02 submit / 8 klientów | 64 | 139.2870 | 183.2894 | PASS |
| NF-03 in-place | 100 | 1.3459 | 1.5743 | PASS |
| NF-03 NTFS save/replace | 100 | 2.6664 | 3.4290 | PASS |
| NF-03 repository replace bez worker | 100 | 3.4691 | 3.7841 | PASS |
| NF-03 repository replace z worker | 100 | 3.2496 | 3.3802 | PASS |

Functional test execution: **PASS**. Full offline qualification: **BLOCKED**; runnerowy gate pozostaje BLOCKED przez NF-11 NOT_RUN, a pełny profil NF-08 skali/obiektów nie jest wykonany (podprzypadek idle PASS). Production qualification: **NOT_RUN**. Nie przenosi się PASS na exFAT, Siemens lub produkcję; historyczne NF-03 exFAT FAIL i NF-08 FAIL pozostają zachowane. NF-11: **NOT_RUN**, Worker Handle Risk: **UNKNOWN**. E1 implementation: **COMPLETE_OFFLINE**. Real Engineering evidence: **NOT_REGISTERED**; B-03A ENGINEERING: **BLOCKED/NOT_RUN**; B-03B: **NOT_READY**. B-03A/E2 nie rozpoczęto.

## Pliki i ocena wpływu

Zmiany wykonawcze: `tests/integration/TiaBridge.StorageProbe/HttpResilienceProbe.cs`, `tests/integration/TiaBridge.Host.Tests/Resilience/ResilienceArtifactTests.cs`, nowy `ResilienceArtifactRegressionTests.cs` w tym samym katalogu oraz `tests/integration/TiaBridge.Host.Tests/ArtifactStorageTests.cs`. Dokumentacja: niniejszy raport, dokumenty 13/16/17 i `Zmiany.md`. Logi/TRX/DB/klucze pozostają poza Git.

Obie instrukcje oceniono sekcja po sekcji (szczegóły w Zmiany.md); pozostają bez edycji. ADR 0001–0003, specyfikacje 02–09, HTTP/API/IPC, schemat SQLite/migracje i architektura zaufania E1 pozostają bez zmian. Jest to naprawa testowego bindingu i rozszerzenie dowodów dla istniejących gwarancji. Nie zmieniono uprawnień, norm, progów lub kryteriów bezpieczeństwa.

Otwarte ograniczenia: testy syntetyczne nie dowodzą Siemens, hardware power-loss, odporności na administratora ani wrogi proces tego samego SID zmieniający ścieżki. Nie ma nowego skanera wszystkich historycznych artefaktów ani automatycznego odtwarzania dowodów. ACL deploymentu, rotacja/revocation/expiry trust anchors E1 i ryzyko uchwytów Workera pozostają w dotychczasowym osobnym zakresie.
