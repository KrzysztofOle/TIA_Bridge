# 11 — Analiza Snap7 do testowania PLC

Uzupełnienie zakresu na prośbę użytkownika, analiza 2026-10-24/25. **Rekomendacja: dodatkowy, opcjonalny adapter testów danych PLC; OPC UA pozostaje podstawowym interfejsem testów z subskrypcjami i kontrolą jakości.** Snap7 nie zastępuje Openness, kompilatora, zarządzania symulatorem ani dowodu zgodności projektu. Nie wykonywano połączenia S7, read/write pamięci ani zmian PUT/GET.

## Ustalenia o bibliotece

Rozróżniamy natywne **Snap7** (biblioteka współdzielona, LGPLv3 według [licencji projektu](https://snap7.sourceforge.net/licensing.html)) i **python-snap7**. Sprawdzony tag `3.2.0` python-snap7 opisuje implementację classic S7 w czystym Pythonie, Python≥3.10, bez native dependencies; rekomendowany import to `s7`, a `snap7` jest nazwą zgodności. Zatem nie planujemy automatycznie instalowania `snap7.dll` na podstawie dawnych wrapperów 2.x. [README tagu 3.2.0](https://raw.githubusercontent.com/gijzelaerr/python-snap7/3.2.0/README.rst).

Release notes 3.2.0 opisują zmiany odporności i rate limiting, a S7CommPlus przeniesiono do osobnego projektu. To kandydat **do przypięcia i eksperymentu**, nie zatwierdzona zależność aplikacji. Dokumentacja `latest` przedstawiała wersję 3.3.0, dlatego przed implementacją należy sprawdzić konkretny tag i API, nie mieszać latest z przypiętym buildem. [Wydania autora](https://github.com/gijzelaerr/python-snap7/releases). Licencję python-snap7 dla 3.2.0 identyfikuje jego [plik LICENSE](https://raw.githubusercontent.com/gijzelaerr/python-snap7/3.2.0/LICENSE) jako MIT; przed dystrybucją wykonać inventory zależności/notice, bez przenoszenia warunków natywnego Snap7 na inny pakiet.

## Ograniczenia S7-1200/1500

Classic S7/PUT-GET wymaga odpowiedniej konfiguracji CPU; zoptymalizowane DB nie są osiągalne klasycznym offsetowym dostępem. Nie jest to interfejs symboliczny ani bezpieczny zamiennik OPC UA z certyfikatami. Dokumentacja autora wskazuje brak uwierzytelniania PUT/GET i celowe domyślne wyłączenie tej funkcji; S7-1500R/H nie jest wspierane tą ścieżką. **Nie wolno automatycznie osłabiać ochrony CPU lub wyłączać optimized access tylko po to, aby uruchomić test.** [Macierz PLC autora](https://python-snap7.readthedocs.io/en/latest/plc-support.html).

Natywny Snap7 opisuje dla S7-1500 podstawowy transfer danych w trybie zgodności, globalne DB i ograniczone PG operations. Jego przykładowe ustawienia ochrony pochodzą ze starszego TIA; nie są instrukcją bezrefleksyjnego obniżania ochrony w V19. [Snap7 Client /1200–1500](https://snap7.sourceforge.net/snap7_client.html). Siemens opisuje, że standardowe połączenia buforowe S7-1500 wymagają PUT/GET i niezoptymalizowanych DB, podczas gdy dostęp symboliczny do optimized DB jest inną ścieżką protokołu. [Siemens, S7 communication §4.5](https://cache.industry.siemens.com/dl/files/801/67295801/att_1332517/v1/67295801_SIMATIC_NET_OPC_UA_S7bufferOrientedService_DOC_V2_1_en.pdf).

## Możliwości i zastosowanie

| Obszar | Snap7 / classic S7 | Decyzja dla TIA_Bridge 2 |
|---|---|---|
| odczyt DB | numer DB + offset + długość; dekodowanie typów w adapterze | testowe globalne DB z jawnym kontraktem layoutu; brak odgadywania offsetów |
| zapis danych | możliwy transfer bajtów do dozwolonego obszaru | tylko symulacja, lease i allowlist pól; brak dowolnego write_area przez API CODEX |
| typy | bytes wymagają jawnego endian, bitów, signedness, string header i rozmiaru | codec ma golden vectors dla BOOL/INT/DINT/REAL/STRING, zakresy i reject unsupported |
| sekwencje/timeouty | aplikacyjny polling z deadline; nie zakładamy OPC UA-style subscriptions | adapter `wait_until`, licznik próbek, monotonic clock, bounded polling i rate limit |
| spójność multi-read | odczyty wielu fragmentów nie są snapshotem cyklu CPU | `sample_sequence`/request-ack w testowym DB; brak spójnego numeru → inconclusive |
| jakość danych | brak równoważnego per-value DataValue/StatusCode/source timestamp OPC UA | raport transport result, czas klienta i freshness z testowego PLC handshake; nie wymyślać Good/source timestamp |
| optimized DB/symbolika | niedostępne w klasycznej ścieżce tego adaptera | OPC UA lub osobno kwalifikowany, przyszły protokół; brak automatycznego S7CommPlus fallback |
| compile/export/import projektu | nie jest zadaniem tego interfejsu | wyłącznie Bridge worker Openness |
| CPU control/force/block transfer | biblioteka może eksponować szersze operacje, zależne od CPU | nie udostępniać RUN/STOP, force, download/upload/delete, zegara ani security methods przez runner testów |

Szczegółowe API `db_read/db_write` i AsyncClient należy sprawdzić dla przypiętej wersji. Obecny katalog autora eksponuje również funkcje administracyjne; wrapper adaptera powinien mieć małą, pozytywną listę, bez zwracania surowego klienta. [Dokumentacja klienta](https://python-snap7.readthedocs.io/en/latest/API/client.html).

## PLCSIM i stan lokalny

Standardowy PLCSIM/Softbus nie jest automatycznie serwerem ISO-on-TCP dla zewnętrznego klienta Snap7. Preferowany kandydat: **PLCSIM Advanced V6 z TCP/IP przez zweryfikowany virtual adapter**, dokładna wspierana CPU/firmware i testowy DB. Zainstalowanie Advanced ani Up adaptera nie potwierdza, że działa instancja, która akceptuje classic S7 z wymaganym DB.

Na analizowanej maszynie nie znaleziono pakietu python-snap7 ani w bazowym Python 3.12, ani w istniejącym OPC venv. TCP 102 nasłuchuje lokalnie jako `s7oiehsx64`; to punkt dostępu Siemens, **nie dowód tożsamości symulowanej CPU**. Nie skanowano innych hostów, nie próbowano TSAP/rack/slot i nie zmieniano konfiguracji urządzenia. Tożsamość konkretnego S7 endpointu i możliwość użycia Snap7 mają status NOT_RUN/REQUIRES_EXPERIMENT.

Nie wprowadzamy automatycznie pośredników typu NetToPLCSim ani przechwytywania portu 102. Byłby to dodatkowy kanał/proces o osobnych ryzykach i osobnym zadaniu, wymagający potwierdzenia kompatybilności i izolacji. Wersja podstawowa może działać bez Snap7.

## Projekt adaptera i testowego DB

`tests/e2e/snap7/` + `src/TiaBridge.Simulation`/runner transportu w odrębnym Python venv. Interfejs `IPlcTestTransport`: `connect`, `read_values`, `write_test_values`, `wait_until`, `disconnect`; opcjonalność subscriptions zaznaczona jako false. Wspólny `test.plc` przyjmuje `transport=snap7`, profil, scenariusz i `allow_writes`; nie przyjmuje IP/rack/slot/DB offset z dowolnego żądania agenta.

Profil wskazuje zatwierdzoną instancję, expected CPU identity/firmware, IP/interface, rack/slot albo TSAP ustalone dokumentacyjnie/eksperymentalnie, endpoint allowlist oraz layout DB. Połączenie nie próbuje innych IP/portów/slotów po błędzie. Transport S7 nie jest odpowiednikiem profilu SECURE; brak TLS nie może uruchomić silent fallback z OPC UA SECURE na Snap7.

Proponowany osobny `DB_BridgeTest` na **kopii syntetycznej**: `ContractVersion`, `BuildId`, `SampleSequence`, `RequestId`, `AckId`, `TestMode`, `Command`, `Input`, `Output`, `ErrorCode`. Dopiero osobne zadanie ustala numery/offsety/typy i czy DB może być non-optimized. Źródło layoutu: kwalifikowany eksport/kompilacja; hash layoutu i loaded build musi odpowiadać scenariuszowi. Nie używamy produkcyjnego DB receptur jako domyślnej skrzynki testowej.

Write allowlist obejmuje konkretne pola, zakresy i maksymalny rate; domyślnie deny wszystkiego, zwłaszcza I/Q/M i adresów procesu. Bit write przez read-modify-write bajtu może nadpisać sąsiednie bity — zakaz takiego zapisu przy wielu writerach; zamiast tego jednoznaczny command byte/mailbox lub kwalifikowany bit primitive. Write acknowledgement transportu nie oznacza wykonania komendy PLC: potwierdza je `AckId` i stan. Po timeout zapisu `UNKNOWN`; nie powtarzać Command bez reconciliation.

Proponowane limity do pomiaru: connect 2 s, read 2 s, scena 120 s, polling 100 ms (10 Hz) z limitem 20 request/s, jeden request in flight na klienta, max 3 próby **odczytu** z backoff 1/2 s po ustaniu poprzedniej; auto reconnect/automatyczny retry write wyłączone. Po utracie połączenia runner przerywa sekwencję, zamyka klienta i raportuje ERROR/inconclusive. Domyślne zachowania konkretnej biblioteki muszą zostać jawnie nadpisane i przetestowane.

## Kwalifikacja i decyzja wdrożeniowa

Eksperyment X-08 ma etap offline codec + mock server (nie uruchamia logiki PLC), następnie read-only handshake na odizolowanej instancji Advanced, dopiero później ograniczony write/ack z osobnym uprawnieniem. Dowody: exact package/tag i dependency hash, CPU/firmware, simulation owner/route, DB layout/compiled build hashes, capture tylko z izolowanej sieci testowej bez sekretów, timeline próbek, timeout/reconnect i sąsiednie pola zachowane.

Kryterium włączenia: 100 powtarzalnych sekwencji i 100 przypadków utraty odpowiedzi bez nieautoryzowanego write; zero błędów dekodowania; odmowa po zmianie DB layoutu/build/endpointu; osobny test write ambiguity. Pomiary p50/p95 round-trip i CPU porównać z OPC UA na tym samym modelu i częstotliwości; **nie obiecujemy z góry przewagi wydajnościowej**. Jeśli wymagana konfiguracja osłabia zabezpieczenia docelowego środowiska albo nie ma izolowanej instancji, adapter pozostaje UNSUPPORTED/DISABLED.

Decyzja D-09: implementacja Snap7 OPTIONAL po MVP; analiza, kontrakt uprawnień i plan X-08 są REQUIRED i zawarte w tym zadaniu. W [strategii testów](07_TEST_STRATEGY.md) transporty pozostają niezależne, aby awaria lub brak Snap7 nie destabilizowały Bridge.
