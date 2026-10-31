# Deep Research — moduł diagnostyki Siemens HMI/PLC dla TIA_Bridge

> **Status:** materiał badawczy i propozycje do przeglądu; nie jest zatwierdzoną architekturą, zakresem implementacji ani dowodem kwalifikacji. Nie zmienia bieżących ograniczeń TIA_Bridge opisanych w `AGENTS.md` i przyjętych ADR. Odnośniki `cite...` są identyfikatorami z sesji badawczej, nie trwałymi linkami; twierdzenia wymagają weryfikacji w aktualnych źródłach producenta przed decyzją lub implementacją.

## Executive Summary

**Tak, ale nie w jednym uniwersalnym wariancie.** Z poziomu Pythona można dziś zbudować bezpieczny, zdalny moduł diagnostyczny dla urządzeń Siemens, który bez zmian programu PLC zbiera znaczną część informacji diagnostycznych. Dla PLC sytuacja jest bardzo dobra: w aktualnych rodzinach S7‑1500/ET 200SP oraz S7‑1200 G2 Siemens udostępnia oficjalny HTTPS/JSON‑RPC Web API obejmujący m.in. `DiagnosticBuffer.Browse`, `Syslog.Browse` i `Alarms.Browse`. Dla starszych S7‑1200 dostęp jest bardziej ograniczony, ale istnieją Web Server, diagnostyczny CSV oraz oficjalny SIMATIC Automation Tool SDK. citeturn548201search0turn468062search0turn468062search1turn468062search4

W przypadku **WinCC Unified** trzeba rozdzielić dwa zupełnie różne strumienie danych. OPC UA A&C jest oficjalnym, stabilnym interfejsem do skonfigurowanych alarmów Runtime/PLC i oferuje subskrypcje zdarzeń z `EventId`, czasem źródłowym, tekstem i severity, ale **nie jest interfejsem do pełnego wewnętrznego trace/logu WinCC Unified**. Pełniejszy strumień diagnostyczny samego Runtime jest generowany przez infrastrukturę **RTIL Trace** i wyświetlany przez `RTILtraceViewer.exe`; na Unified Panel może być przekazywany zdalnie przez funkcję **Trace Forwarder** i odbierany przez `RTILtraceTool.exe`. citeturn257340search5turn257340search6turn871871search2turn398659search0

Najważniejsze ograniczenie brzmi: **Siemens dokumentuje Trace Forwarder oraz RTILtraceTool jako narzędzia diagnostyczne, ale nie znalazłem publicznego, wspieranego SDK ani specyfikacji protokołu RTIL, dzięki którym czysty klient Python mógłby bezpośrednio konsumować ten sam strumień co TraceViewer.** Dlatego napisanie własnego parsera ruchu sieciowego RTIL nie powinno być rozwiązaniem produkcyjnym. To rozgraniczenie jest kluczowe: sam strumień istnieje i jest zdalny, lecz udokumentowany odbiornik pozostaje komponentem Siemens. citeturn398659search0turn871871search0turn871871search3

**Rekomendacja dla TIA_Bridge:** zastosować architekturę wieloadapterową. Dla HMI Unified podstawowym, oficjalnie wspieranym adapterem produkcyjnym powinien być **OPC UA A&C** dla alarmów Runtime/PLC, natomiast odrębny `UnifiedTraceAdapter` powinien wykorzystywać **RTIL Trace Forwarder + narzędzie Siemens** jako źródło rozszerzonej diagnostyki dopiero po PoC potwierdzającym stabilny i legalnie wspierany sposób przekazania danych z komponentu Siemens do TIA_Bridge. Dla Unified PC można dodatkowo bezpośrednio czytać lokalne pliki trace, ponieważ Siemens dokumentuje ich położenie. Dla PLC rekomendowany jest natomiast **Web API przez HTTPS/JSON‑RPC**, bez Snap7 i bez zmian programu użytkownika. citeturn871871search2turn257340search2turn548201search2turn468062search0

W kategoriach wymaganej ingerencji oznacza to: PLC S7‑1500/ET 200SP/S7‑1200 G2 — **poziom 1** (włączenie Web Server/API i uprawnień); Unified OPC UA — **poziom 1**; Unified Trace Forwarder — **poziom 1**; Unified PC odczyt lokalnego trace logu — **poziom 0–1**, zależnie od lokalizacji TIA_Bridge; klasyczne Comfort Panels — zwykle **poziom 2 lub wyższy**, ponieważ nie dysponują współczesnym odpowiednikiem Unified Trace Forwardera udokumentowanym jako zdalny strumień diagnostyczny. citeturn402361search12turn257340search1turn548201search9

## TraceViewer i rzeczywiste źródło diagnostyki Unified

### Co oznacza „Siemens TraceViewer”

W kontekście WinCC Unified właściwym narzędziem jest **RTIL Trace Viewer**, uruchamiany jako `RTILtraceViewer.exe` z katalogu instalacyjnego `WinCCUnified\bin`. Siemens opisuje go jako narzędzie do analizy trace'y funkcji, wartości tagów, błędów skryptów i procesów Runtime; w dokumentacji pojawiają się kolumny typu Severity, Timestamp, Message, Application i Module. citeturn838591search45turn402361search11

TraceViewer nie jest ograniczony do komunikatów wygenerowanych ręcznie przez `HMIRuntime.Trace()`. Dokumentacja Siemens mówi o wszystkich dostępnych trace messages dostarczanych przez Unified Runtime oraz pokazuje przykładowe komunikaty procesów i modułów WinCC, w tym ostrzeżenia, błędy i informacje dotyczące startu komponentów. To jest właśnie źródło najbardziej zbliżone do kategorii **A — wewnętrznych logów diagnostycznych Runtime**. citeturn838591search46turn838591search45

Na Unified PC komunikaty o poziomie `Fatal`, `Error` i `Warning` są zapisywane do `.log` w `%ProgramData%\Siemens\Automation\Logfiles\WinCC_Unified_SCADA_Vxx`. Dostęp do pełnego zestawu trace'y w TraceViewer jest dodatkowo zależny od członkostwa użytkownika w grupach Windows `RTIL Tracing Users` i `SIMATIC HMI`. citeturn871871search2

Na Unified Comfort Panel Siemens udostępnia w Control Panel funkcję **Trace Forwarder**. Jest ona domyślnie wyłączona i służy właśnie do udostępnienia trace'y do celów diagnostycznych i serwisowych. Nowsze wersje mają także **Trace Logger**, który zapisuje trace na nośniku USB/SD z rotacją plików; jest to jednak archiwizacja plikowa, nie transmisja online. citeturn402361search12turn694584search3

Oficjalna procedura po stronie PC jest następująca:

```text
RTILtraceTool.exe -mode receiver -host "<IP panelu>" -tcp
```

a następnie uruchamia się `RTILtraceViewer.exe`. Siemens wyraźnie opisuje ten scenariusz jako wymianę trace'y pomiędzy Unified Panel a komputerem inżynierskim podczas działania Runtime. citeturn398659search0turn871871search3

W innych komponentach Unified Siemens dokumentuje również jawnie porty RTIL, np. port `35505` przy Online Engineering i port `4725` przy nowszej diagnostyce Data Hub. Nie należy jednak przenosić tych numerów automatycznie na Unified Comfort Panel: FAQ dla panelu pokazuje wywołanie bez jawnego parametru `-port`, więc numer właściwy dla tej konfiguracji powinien zostać ustalony z `RTILtraceTool -help` dla konkretnej wersji Runtime albo przez oficjalną dokumentację tej wersji. citeturn398659search7turn871871search1

### Czy Python może używać tego samego źródła?

**Potwierdzone:** źródło trace może być przekazywane sieciowo bez instalowania dodatkowego oprogramowania na panelu poza włączeniem istniejącego Trace Forwardera. Odbiornikiem po stronie PC jest komponent Siemens `RTILtraceTool`. citeturn398659search0turn402361search12

**Niepotwierdzone / brak publicznego kontraktu:** dokumentacja, którą znalazłem, nie publikuje formatu protokołu RTIL, modelu rekordów wire-level ani biblioteki klienckiej przeznaczonej do własnych aplikacji. Nie ma więc podstaw, aby uznać bezpośredniego klienta Python TCP za wspieraną implementację Siemens. Dokumentowane scenariusze zawsze posługują się `RTILtraceTool`, `TraceViewer` albo `Diagnostics Viewer`. citeturn871871search0turn398659search0turn398659search2

**Wniosek techniczny:** Python może wykorzystywać *to samo źródło funkcjonalne* tylko pośrednio, jeżeli Siemensowy receiver zostanie zachowany jako sidecar/bridge. Czy możliwe jest następnie stabilne pobieranie rekordów z sidecara przez plik, stdout, named pipe lub inny publiczny interfejs — tego dokumentacja Siemens nie potwierdza. To powinien być pierwszy przedmiot PoC, a nie założenie architektury.

Nie rekomenduję sniffingu ani reverse engineeringu prywatnego protokołu RTIL. Taki adapter byłby związany z wewnętrznym formatem Siemens, trudny do walidacji po aktualizacjach firmware i problematyczny z punktu widzenia wsparcia OT.

## HMI — macierz kompatybilności i źródła danych

Kluczowe rozróżnienie jest następujące: **alarmy HMI/PLC** są częścią modelu alarmowego projektu, natomiast **trace Runtime** jest wewnętrznym kanałem serwisowym WinCC. OPC UA wystawia ten pierwszy, nie drugi. Dokumentacja Unified mówi wprost, że OPC UA Server udostępnia proces values, tag/controller-based alarms i logging tags; alarm conditions są odwzorowane z *configured alarms* Runtime. citeturn257340search2turn257340search6

| Platforma | Natywne logi Runtime A | Alarmy B | Online | Historia | Najlepszy interfejs | Ingerencja |
|---|---|---|---|---|---|---|
| Unified Comfort Panel | **Tak, RTIL Trace Forwarder** | Tak | Trace: tak; alarmy: tak | Trace Logger lokalnie; alarm logging zależne od konfiguracji | RTIL + OPC UA A&C | 1 |
| Unified Basic Panel | Trace Forwarder jest dokumentowany | Tak | Tak | lokalny Trace Logger / alarm logs | RTIL + OPC UA zależnie od urządzenia | 1 |
| WinCC Unified PC | **Tak, trace files + RTIL** | Tak | Tak | pliki trace + alarm logging | lokalny file tail + OPC UA/ODK | 0–1 |
| Classic Comfort Panel | brak potwierdzonego odpowiednika RTIL Unified | Tak | alarmy: tak | alarm logs CSV / archiwa | standardowe WinCC archiving / eksport | 2 |
| WinCC Runtime Advanced | diagnostyka Windows/Runtime dostępna lokalnie, ale bez Unified RTIL | Tak | tak | CSV/TXT/SQL alarm logging | pliki/SQL/Windows | 1–2 |
| WinCC Runtime Professional / WinCC V7/V8 | bogate logi diagnostyczne, OPC A&E, ODK, SQL | Tak | tak | tak | OPC A&E/ODK/files/SQL | 0–1 |
| WinCC Unified PC Runtime | jak Unified PC powyżej | Tak | tak | tak | trace files + OPC UA + ODK | 0–1 |

Dla Unified Comfort Panel dokumentacja urządzenia potwierdza, że Runtime data, np. alarms, znajdują się wewnętrznie podczas pracy, natomiast Trace Logger i alarm/tag logs są trwale zapisywane na zewnętrznym nośniku. Siemens zaznacza zarazem, że panel sam nie składuje wygenerowanych danych na zdalnym serwerze. citeturn728407search0

Trace Logger i **Event Logger** nie są równoważne streamingowi. Event Logger potrafi nagrać cały scenariusz pracy, w tym trace, zawartość ekranów, zmiany ekranów i akcje operatora, lecz zapisuje je w zaszyfrowanych plikach na USB/SD i wymaga restartu Runtime po aktywacji. Siemens zaleca używanie Event Loggera tylko krótkotrwale z uwagi na ilość generowanych danych. citeturn728407search1turn728407search4

WinCC Unified OPC UA Server jest natomiast dostępny na Unified PC i Unified Comfort Panels jako DA oraz A&C Server. Domyślny port to `4890`, jest konfigurowalny, a komunikacja używa `opc.tcp`. Wymagany jest ważny Runtime license; certyfikaty klienta i serwera muszą być wzajemnie zaufane. citeturn257340search0turn257340search1turn257340search2turn257340search8

Dla zdarzeń OPC UA Unified udostępnia m.in. `EventId`, `SourceName`, `Time`, `ReceiveTime`, `Message` i `Severity`. Klient może subskrybować zmiany aktywnych alarmów i reagować bez pollingu. Jest to bardzo dobre dopasowanie do modelu zdarzenia TIA_Bridge, ale wyłącznie dla alarmów widocznych w modelu alarmowym Runtime. citeturn257340search5turn257340search6

`HistoryRead` w obecnej dokumentacji Unified dotyczy **logging tags**; Siemens nie dokumentuje go jako dowolnego odczytu historycznego wewnętrznych trace'y Runtime. Dlatego nie należy zakładać, że OPC UA zapewnia historię TraceViewera. citeturn257340search4turn257340search9

WinCC Unified Runtime API/ODK jest znacznie bogatsze od OPC UA i zawiera interfejsy alarmów, alarm logging, subskrypcje, connection status itd., lecz oficjalnie jest przeznaczone dla C++ i C#, a `Connect()` w aktualnej wersji łączy się z **lokalnym** Runtime. ODK nie jest więc zdalnym Python API dla panelu. citeturn402361search6turn500246search1turn500246search2

W Runtime Professional sytuacja jest korzystniejsza dla PC: Siemens dokumentuje zarówno pliki Channel Diagnosis, jak i logbook/trace komunikacji. WinCC Professional oferuje również OPC Alarm & Events, HDA, OLE DB, ODK i SQL-based Historian; OPC A&E może przekazywać komunikaty WinCC do zewnętrznego klienta. citeturn871871search7turn394433search28turn394433search29

Dla klasycznych Comfort Panels dostępne są alarm logs i zapis archiwów w standardowych formatach, lecz oficjalna dokumentacja produktu nie wskazuje odpowiednika Unified RTIL Trace Forwardera umożliwiającego zdalne streamowanie pełnego wewnętrznego logu Runtime. Dlatego dla tej rodziny należy zakładać osobny adapter i gorszą pokrywalność kategorii A. citeturn838591search9

## Technologie, Python i ranking rozwiązań

Poniższa ocena rozdziela „mogę się połączyć” od „mogę uzyskać interesującą diagnostykę”.

| Mechanizm | HMI system trace | Alarmy | PLC Diagnostic Buffer | Online | Historia | Python | Ocena |
|---|---:|---:|---:|---:|---:|---:|---|
| Unified RTIL Trace Forwarder | **bardzo wysoka** | pośrednio | nie | **tak** | Trace Logger osobno | brak publicznego SDK | ★★★★★ jako źródło, ★★ jako API |
| Unified OPC UA A&C | nie | **bardzo wysoka** | nie | **subskrypcja** | ograniczona do modelu OPC/alarm log | `asyncua` | ★★★★★ |
| PLC Web API JSON‑RPC | n/d | CPU alarms | **bardzo wysoka** | polling | bufor CPU | `httpx`/`aiohttp` | ★★★★★ |
| SIMATIC Automation Tool SDK | n/d | n/d | **wysoka** | polling | bieżący bufor | .NET; Python przez bridge | ★★★★ |
| WinCC Unified ODK | Runtime objects, nie RTIL wire stream | **bardzo wysoka** | n/d | subskrypcje | alarm logging | C#/C++, nie natywny Python | ★★★★ dla PC |
| Windows Event Log | część błędów usług/OS | nie wszystkie | n/d | tak | tak | `pywin32` | ★★★ dla PC |
| Log files / SMB | **wysoka na PC** | zależnie od pliku | n/d | tail/poll | tak | doskonała | ★★★★ |
| S7/Snap7 | nie | nie | brak gwarantowanego nowego API diag | polling | nie | tak | ★★ |
| SNMP | zdrowie urządzenia | nie | nie | traps/poll | nie | tak | ★★ |
| TIA Portal Openness | diagnostyka TIA Portal, engineering | nie jako collector | nie jako lekki daemon | słabo | nie | .NET | ★ |
| SSH/SFTP | brak uniwersalnego oficjalnego kanału logów | — | — | — | pliki | tak | zależne od platformy |
| Syslog | istotny przede wszystkim dla CPU Web API | — | security/system CPU | polling przez API | bufor | tak | ★★★★ dla PLC |

**OPC UA.** Dla Unified jest to najlepszy standardowy kanał czasu rzeczywistego dla alarmów. Server wspiera event subscriptions i A&C, a model zdarzenia mapuje pola potrzebne TIA_Bridge. Należy jednak traktować go jako adapter alarmów, nie adapter TraceViewer. citeturn257340search6turn257340search7

**`asyncua`.** Na koniec października 2026 projekt jest aktywną implementacją asyncio OPC UA; PyPI wskazuje serię 2.x, a repozytorium deklaruje LGPL‑3.0-or-later. To lepszy kandydat do nowego asynchronicznego kolektora niż starszy `python-opcua`, którego dokumentacja/repozytorium jest znacznie mniej aktywne. citeturn942603search2turn942603search5turn942603search39

**`python-snap7`.** Projekt jest obecnie aktywny — wersja 3.3.1 została opublikowana 28 października 2026 i ma licencję MIT — ale jego podstawowym zadaniem jest klasyczna komunikacja S7, nie udokumentowany odczyt nowoczesnego diagnostic buffer API S7‑1500. Nie powinien być pierwszym wyborem do omawianego modułu tylko dlatego, że potrafi połączyć się z PLC. citeturn942603search0turn942603search37

**Windows Event Log.** `pywin32` udostępnia natywne API `win32evtlog`, więc na PC Runtime warto je wykorzystać jako niezależny adapter systemu operacyjnego. Nie zastąpi jednak WinCC trace, ponieważ część diagnostyki Unified trafia do własnych plików RTIL. citeturn942603search25turn871871search2

**TIA Portal Openness** nie jest właściwym mechanizmem do daemonowego kolektora diagnostyki. Openness udostępnia interfejs diagnostyczny dotyczący procesów samego TIA Portal i ma ograniczenia bezpieczeństwa dotyczące zdalnego użycia; nie jest publicznym API do streamowania bufora diagnostycznego HMI/PLC. citeturn548201search6turn548201search8

## PLC — natywny Diagnostic Buffer bez zmian programu sterowania

Tutaj odpowiedź jest najbardziej jednoznaczna: **tak, da się odczytywać natywny Diagnostic Buffer bez dodawania bloków do programu użytkownika.** W S7‑1500 bufor zawiera m.in. błędy wewnętrzne i zewnętrzne modułów, system errors CPU, RUN/STOP, błędy programu użytkownika, wyjmowanie/wkładanie modułów i security events. System diagnostics w S7‑1500 jest wbudowane i aktywne automatycznie. citeturn548201search11turn548201search5

Dla S7‑1500 standard/T/F, ET 200SP CPU i odpowiednich Software Controllerów Web API jest dostępne od firmware 2.8, a nowsze metody diagnostyczne rozszerzono w kolejnych wersjach. Aktualna macierz V21 podaje `DiagnosticBuffer.Browse` dla S7‑1500 od V3.1, dla R/H od V4.1 oraz dla S7‑1200 G2 od V1.0. citeturn548201search9turn468062search4

`DiagnosticBuffer.Browse` zwraca natywnie:

- `timestamp` w UTC,
- `status` incoming/outgoing,
- identyfikator zdarzenia składający się z `textlist_id` i `text_id`,
- `short_text`,
- `long_text`,
- `help_text`,
- `last_modified`,
- aktualną oraz maksymalną liczbę rekordów.

Wymagane jest uprawnienie `read_diagnostics`. To praktycznie idealny materiał dla `PlcWebApiAdapter`. citeturn468062search0

CPU ma dodatkowo `Syslog.Browse`. Siemens opisuje go jako odczyt wewnętrznego syslog buffer CPU; API zwraca również `count_total` i `count_lost`, co jest szczególnie wartościowe przy wykrywaniu luk w TIA_Bridge. citeturn468062search1

`Alarms.Browse` pozwala natomiast odczytać bieżące alarmy CPU i rozróżnia producenta, np. `system_diagnostics` oraz `program_alarm`. Dzięki temu TIA_Bridge może zachować wyraźną granicę między alarmem systemowym PLC a alarmem zaprogramowanym przez użytkownika. citeturn468062search2

Dla klasycznego S7‑1200 Web Server również pokazuje Diagnostic Buffer, a bieżąca dokumentacja opisuje do 50 wpisów oraz eksport całego bufora do CSV. Aktualne S7‑1200 obsługują też Web API, lecz w starszej generacji lista metod nie obejmuje `DiagnosticBuffer.Browse`; zawiera m.in. `Syslog.Browse`. Dlatego adapter musi wykonać discovery modelu/firmware, zamiast zakładać jednolite API. citeturn184142search0turn184142search1

Alternatywą dla starszych CPU jest **SIMATIC Automation Tool SDK**. Oficjalny .NET API ma `IDiagnosticBuffer.GetDiagnosticsBuffer()` i zwraca kolekcję wpisów diagnostycznych. Jest to wspierany sposób odczytu CPU bez zmian programu użytkownika, ale wymaga komponentu .NET/Siemens zamiast czystej biblioteki Python. citeturn844183search7turn844183search10

Warto przy tym uwzględnić bezpieczeństwo: Siemens opublikował advisory dotyczące wersji SIMATIC Automation Tool SDK starszych niż 5.0 SP4, więc w nowej implementacji należałoby wymagać wersji 5.0 SP4 lub nowszej. citeturn844183search4

### PoC PLC — rekomendowany

Najczystszy PoC dla S7‑1500/ET 200SP/S7‑1200 G2 powinien używać Web API HTTPS/JSON‑RPC. Web API działa pod endpointem `/api/jsonrpc`, a API wymaga zalogowania użytkownika posiadającego odpowiednie prawa. citeturn548201search2turn468062search0

Schemat kodu w Pythonie:

```python
import asyncio
import httpx

class SiemensWebApi:
    def __init__(self, host: str):
        self.url = f"https://{host}/api/jsonrpc"
        self.client = httpx.AsyncClient(
            verify="/path/to/trusted_cpu_ca.pem",
            timeout=5.0,
        )
        self.token = None
        self.rpc_id = 0

    async def rpc(self, method, params=None):
        self.rpc_id += 1
        payload = {
            "jsonrpc": "2.0",
            "id": self.rpc_id,
            "method": method,
        }
        if params is not None:
            payload["params"] = params

        headers = {}
        if self.token:
            headers["X-Auth-Token"] = self.token

        response = await self.client.post(
            self.url, json=payload, headers=headers
        )
        response.raise_for_status()
        return response.json()

    async def diagnostics(self):
        return await self.rpc(
            "DiagnosticBuffer.Browse",
            {
                "language": "en-US",
                "count": 50,
                "filters": {
                    "mode": "include",
                    "attributes": [
                        "short_text",
                        "long_text",
                        "help_text",
                    ],
                },
            },
        )
```

Nazwy metody i parametrów powyżej wynikają bezpośrednio z aktualnego kontraktu Siemens. Logowanie należy dopracować zgodnie z `Api.Login` dla konkretnej wersji firmware, zamiast kopiować nieudokumentowany format nagłówka/tokena. citeturn468062search0turn548201search9

Bezpieczny test powinien ograniczyć się do konta z `read_diagnostics`/`read_syslog`, HTTPS, częstotliwości rzędu sekund zamiast agresywnego pollingu oraz sprawdzenia `last_modified` lub licznika przed pobieraniem całego bufora. Nie ma potrzeby przełączać RUN/STOP; zdarzenie testowe można wywołać kontrolowanie przez podłączenie/odłączenie niekrytycznego urządzenia testowego albo użyć wpisów już istniejących.

## Architektura TIA_Bridge i PoC HMI

Nie rekomenduję przebudowy TIA_Bridge przed obejrzeniem obecnych granic modułów. Pierwszym zadaniem implementacyjnym powinno być wyłącznie zidentyfikowanie istniejących mechanizmów konfiguracji urządzeń, lifecycle usług, event busa, logowania, retry i storage, a następnie wpięcie diagnostyki przez mały port/interfejs.

Docelowy przepływ proponuję w tej postaci:

```text
                        ┌─────────────────────┐
                        │ Device Discovery    │
                        │ model/fw/capability │
                        └─────────┬───────────┘
                                  │
                 ┌────────────────▼────────────────┐
                 │      Connection Manager         │
                 │ TLS / cert / credentials / QoS  │
                 └───────┬─────────┬─────────┬─────┘
                         │         │         │
        ┌────────────────▼─┐ ┌─────▼─────┐ ┌─▼────────────────┐
        │ Unified OPC UA   │ │ PLC Web   │ │ Unified Trace    │
        │ A&C Adapter      │ │ API       │ │ Adapter          │
        │ asyncua          │ │ HTTPS     │ │ RTIL sidecar     │
        └────────┬─────────┘ └─────┬─────┘ └───────┬──────────┘
                 │                 │               │
        ┌────────▼─────────────────▼───────────────▼──────────┐
        │                    Collector                        │
        │ subscriptions / polling / reconnect / cursors      │
        └──────────────────────────┬──────────────────────────┘
                                   │
        ┌──────────────────────────▼──────────────────────────┐
        │                    Normalizer                       │
        │ timestamps / severity / event-id / locale / source │
        └──────────────────────────┬──────────────────────────┘
                                   │
                    ┌──────────────▼──────────────┐
                    │ Dedup + Gap Detection       │
                    └──────────────┬──────────────┘
                                   │
             ┌─────────────────────▼────────────────────┐
             │ TIA_Bridge Event Stream / local storage │
             └──────────────────────────────────────────┘
```

Wspólny rekord powinien być rozszerzeniem zaproponowanego przez Ciebie modelu:

```python
@dataclass(frozen=True)
class DiagnosticEvent:
    timestamp_source: datetime | None
    timestamp_received: datetime
    device_id: str
    device_name: str | None
    device_type: str
    source: str
    category: str
    severity: str | None
    event_id: str | None
    message: str
    raw_payload: dict | str | bytes

    source_sequence: int | None = None
    source_status: str | None = None
    locale: str | None = None
    clock_quality: str | None = None
    dedup_key: str | None = None
    gap_before: bool = False
```

`event_id` nie może być globalnym kluczem głównym, bo część źródeł go nie gwarantuje. Deduplikację proponuję wykonywać przez `device_id + source + event_id + timestamp_source` tam, gdzie event ID istnieje, a w pozostałych źródłach przez hash znormalizowanego payloadu i okna czasowego. Dla OPC UA dostępny jest natywny `EventId`; dla PLC Web API można zbudować ID z `textlist_id`, `text_id`, timestamp i statusu. citeturn257340search5turn468062search0

Dla reconnectu OPC UA należy korzystać z mechanizmu ponownej sesji/subskrypcji i po reconnect wykonać reconciliation aktywnych alarmów. Dla PLC Web API cursor można oprzeć na `last_modified`, licznikach i ostatnim znanym timestampie. `Syslog.Browse` daje dodatkowo `count_lost`, więc lukę można oznaczyć deterministycznie. citeturn468062search0turn468062search1

### PoC HMI — co da się zweryfikować uczciwie

**PoC A1: pełniejszy native trace Unified Comfort Panel.** W Control Panel należy włączyć `Trace Forwarder`, na komputerze testowym z WinCC Unified uruchomić `RTILtraceTool -mode receiver -host <panel> -tcp`, a następnie wygenerować prawdziwy błąd skryptu lub zdarzenie Runtime. TraceViewer powinien zobaczyć komunikat bez zatrzymywania Runtime. Jest to oficjalny PoC źródła danych i wymaga jedynie konfiguracji poziomu 1. citeturn398659search0turn838980search6

**Nie należy jednak w tym momencie deklarować sukcesu TIA_Bridge**, ponieważ Python jeszcze nie otrzymuje rekordu. Druga część PoC ma ustalić, czy `RTILtraceTool` w danej wersji zapewnia udokumentowany output nadający się do maszynowego przetwarzania. Jeżeli `RTILtraceTool -help`, dokumentacja instalacyjna lub Siemens Support nie potwierdzi takiego kontraktu, `UnifiedTraceAdapter` powinien pozostać oznaczony jako „unsupported for production”, a nie być realizowany poprzez reverse engineering.

**PoC A2: oficjalny Python end-to-end.** Dla Unified V21 można uruchomić OPC UA Server i w Pythonie z `asyncua` zasubskrybować `EventNotifier`. Ten PoC pozwoli rzeczywiście pokazać zdarzenie w konsoli TIA_Bridge, z `EventId`, timestampem, message i severity. Nie będzie jednak testem pełnych logów RTIL, tylko oficjalnego alarm interface. citeturn257340search2turn257340search5turn257340search7

Schemat klienta jest następujący:

```python
from asyncua import Client

class AlarmHandler:
    def event_notification(self, event):
        print(
            {
                "timestamp_source": event.Time,
                "timestamp_received": datetime.now(timezone.utc),
                "event_id": bytes(event.EventId).hex(),
                "severity": int(event.Severity),
                "message": str(event.Message.Text),
            }
        )

async with Client("opc.tcp://hmi-host:4890") as client:
    # certyfikaty i SecurityPolicy należy skonfigurować
    # zgodnie z polityką danego HMI
    server = client.get_server_node()

    subscription = await client.create_subscription(
        period=500,
        handler=AlarmHandler(),
    )

    await subscription.subscribe_events(server)
```

Sam model subskrypcji alarmów jest zgodny z dokumentowanym OPC UA EventNotifier WinCC Unified, lecz przed użyciem produkcyjnym należy dobrać właściwy event filter i politykę bezpieczeństwa do konfiguracji urządzenia. citeturn257340search6turn257340search7turn257340search8

Dla **Unified PC Runtime** można wykonać jeszcze bardziej bezpośredni PoC kategorii A: Python tailuje `%ProgramData%\Siemens\Automation\Logfiles\WinCC_Unified_SCADA_Vxx\*.log`, zapamiętuje inode/nazwę/offset i normalizuje nowo dopisywane rekordy. To jest realny dostęp do natywnych plików trace dokumentowanych przez Siemens i nie wymaga modyfikacji projektu HMI. citeturn871871search2

## Plan wdrożenia, ryzyka i rekomendowane rozwiązanie

Najpierw należy potraktować **capability discovery** jako część funkcjonalności, nie konfigurację statyczną. Adapter powinien rozpoznać rodzinę urządzenia, firmware/Runtime i dostępne endpointy, po czym wybrać metodę w kolejności preferencji. Dla PLC Web API nadaje się szczególnie dobrze do takiej architektury, ponieważ Siemens sam opisuje je jako interfejs do aplikacji łączących się równocześnie z wieloma CPU. citeturn548201search4

Proponowany porządek realizacji jest następujący:

1. **Inwentaryzacja TIA_Bridge** — tylko analiza interfejsów konfiguracji, event bus, lifecycle i storage; bez zmian funkcjonalnych.
2. **PLC Web API PoC** — S7‑1500 lub ET 200SP z `DiagnosticBuffer.Browse`, read-only user, HTTPS i reconnect. Kryterium: odczyt wpisów oraz prawidłowe `timestamp/event_id/status`.
3. **Unified OPC UA PoC** — subskrypcja jednego rzeczywistego alarmu i jego normalizacja do `DiagnosticEvent`.
4. **RTIL source PoC** — Unified Comfort Panel + Trace Forwarder + RTILtraceTool. Kryterium pierwsze: TraceViewer widzi prawdziwy system/runtime error. Kryterium drugie: ustalenie, czy istnieje oficjalnie wspierany programistyczny output RTILtraceTool.
5. **Unified PC Trace File Adapter** — tail natywnych `.log`, obsługa rotacji i restartów.
6. **Gap/dedup/reconnect layer** — wspólny dla wszystkich źródeł.
7. **Multi-device soak test** — minimum kilkadziesiąt godzin, kontrolowane przerwy sieciowe, restarty Runtime i PLC, rotacja logów, zmiana czasu/NTP.
8. Dopiero potem adaptery legacy dla Comfort/Advanced/Professional.

Największym ryzykiem jest założenie, że „TraceViewer = API”. Dokumentacja tego nie potwierdza. RTIL jest najlepszym odkrytym źródłem diagnostyki Unified, lecz jego publiczny kontrakt integracyjny kończy się obecnie na narzędziach Siemens. citeturn398659search0turn871871search0

Drugim ryzykiem jest utożsamienie OPC UA A&C z logami Runtime. OPC UA jest rozwiązaniem bardzo dobrym do alarmów i komunikatów PLC, lecz jego address space w Unified opisuje skonfigurowane alarmy oraz tagi, a nie wszystkie wewnętrzne trace'y procesów WinCC. citeturn257340search5turn257340search6

Trzecim ryzykiem jest użycie klasycznego S7Comm/Snap7 dla nowych CPU mimo dostępnego HTTPS Web API. Web API ma jawne uprawnienia, format JSON‑RPC i natywne metody diagnostyczne oraz nie wymaga zmian logiki PLC; z punktu widzenia utrzymania jest znacznie lepszym kontraktem. citeturn548201search2turn468062search0

Czwartym jest obciążenie urządzenia. TIA_Bridge powinien subskrybować tam, gdzie protokół to umożliwia, a przy API pollingowym najpierw wykonywać lekkie zapytania o stan/licznik i dopiero po zmianie pobierać dane. Siemens sam wskazuje, że Web Server zużywa zasoby komunikacyjne CPU i zaleca ograniczanie liczby równoczesnych klientów. citeturn184142search3

Piątym są certyfikaty i credentials. OPC UA Unified korzysta z wzajemnego zaufania certyfikatów, a Web API CPU powinien działać przez HTTPS i użytkownika z minimalnymi prawami. Siemens explicite zaleca wyłączenie anonimowego dostępu i używanie bezpiecznego HTTPS/VPN w sieciach nieufnych. citeturn257340search8turn184142search2

### Końcowa rekomendacja

**Moduł TIA_Bridge należy zaprojektować jako platformę adapterów, a nie jako jeden klient „Siemens diagnostics”.**

Dla HMI Unified rekomendowany zestaw to:

**`UnifiedOpcUaAlarmAdapter` + `UnifiedTraceAdapter`.**

Pierwszy jest w pełni wspierany, działa w Pythonie, zapewnia subskrypcję czasu rzeczywistego i stabilny model zdarzeń. Drugi ma dać dostęp do najbogatszych natywnych logów Runtime przez Trace Forwarder, lecz powinien zostać dopuszczony do produkcji tylko wtedy, gdy PoC potwierdzi oficjalny sposób programistycznego wyjścia z Siemens `RTILtraceTool`. citeturn257340search6turn398659search0

Dla Unified PC `UnifiedTraceAdapter` może od razu korzystać z dokumentowanych lokalnych plików `.log`, więc tutaj pełniejsza diagnostyka kategorii A jest osiągalna bez reverse engineeringu. citeturn871871search2

Dla PLC rekomendowanym adapterem jest:

**`SiemensPlcWebApiAdapter` → HTTPS/JSON‑RPC → `DiagnosticBuffer.Browse` + `Syslog.Browse` + opcjonalnie `Alarms.Browse`.**

To rozwiązanie jest read-only, nie wymaga żadnego bloku w programie PLC, udostępnia tekst, identyfikator, timestamp i status zdarzenia oraz nadaje się bezpośrednio do asynchronicznej implementacji Python. citeturn468062search0turn468062search1turn468062search2

Minimalny techniczny test powinien więc objąć **jeden Unified Comfort Panel, jeden Unified PC Runtime i jeden S7‑1500/ET 200SP**. Na panelu należy potwierdzić pełny tor `Trace Forwarder → RTILtraceTool → TraceViewer`; na Unified PC — `native .log → Python → DiagnosticEvent`; na PLC — `Web API → Python → DiagnosticEvent`. Dopiero wynik pierwszego testu odpowie, czy pełny Unified Panel trace może zostać włączony do TIA_Bridge jako oficjalnie wspierany strumień, czy też produkcyjna wersja dla paneli będzie musiała pozostać przy OPC UA A&C plus okresowej diagnostyce RTIL.

Najważniejsza odpowiedź na pytanie badawcze brzmi zatem:

**Można zbudować TIA_Bridge odbierający w czasie rzeczywistym szeroki zakres diagnostyki Siemens bez modyfikowania programu sterowania. Dla PLC istnieje dziś bardzo dobre, oficjalne API. Dla HMI Unified najbardziej kompletne natywne źródło to RTIL Trace Forwarder, lecz Siemens nie publikuje obecnie publicznego protokołu/SDK dla bezpośredniego klienta Python. Dlatego produkcyjna architektura powinna łączyć oficjalny OPC UA A&C z adapterem RTIL opartym na komponentach Siemens, a nie na reverse engineeringu.** citeturn468062search0turn257340search6turn398659search0

## Źródła techniczne

Najbardziej istotnymi źródłami dla dalszej implementacji są aktualna dokumentacja **WinCC Unified OPC UA V21** dotycząca działania serwera, A&C, EventNotifier i certyfikatów, citeturn257340search2turn257340search6turn257340search8 dokumentacja **TraceViewer with Unified Panels** oraz Trace Options dla Unified Comfort Panel, citeturn398659search0turn694584search3 dokumentacja **Unified PC trace logs**, citeturn871871search2 oraz bieżąca dokumentacja **S7‑1500/ET 200MP Web API V21**, szczególnie `DiagnosticBuffer.Browse`, `Syslog.Browse` i `Alarms.Browse`. citeturn468062search0turn468062search1turn468062search2

Dla starszych PLC przydatny jest **SIMATIC Automation Tool SDK**, który zawiera oficjalny `GetDiagnosticsBuffer()`, a dla bibliotek Python aktualnymi kandydatami są `asyncua` dla OPC UA i standardowy klient HTTPS, taki jak `httpx`/`aiohttp`, dla PLC Web API. `python-snap7` pozostaje użyteczną biblioteką S7, lecz nie jest rekomendowanym podstawowym mechanizmem nowego modułu diagnostycznego. citeturn844183search7turn942603search2turn942603search0
