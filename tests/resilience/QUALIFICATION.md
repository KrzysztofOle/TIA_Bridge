# B-02D — kwalifikacja 2026-10-25T21:18:26.251285+00:00

Run ID: `e46332b7aee247c28cc9ca3910e69c48`. Snapshot pochodzi z pełnego validate_offline.py; dowody szczegółowe/TRX pozostają ignorowane w .tia-bridge. Hash źródeł LF: `72a2501c733d0286ffa1fe5fe8d74458bcef78a8d0e0a45c5790a0bd88d92ce7`.

Wykonanie testów: **748 .NET PASS +24 Python PASS**, FAIL 0, SKIP 0. Nowe: 48 .NET i 4 Python. Release PASS, 0 warning/0 error; generatory, kontrakty/OpenAPI, Core boundary, audyt, format i diff PASS. Audyt po publikacji snapshot: 42 Markdown/323 lokalne odnośniki/0 błędów. Same zielone testy nie potwierdzają wszystkich NF.

**OFFLINE: FAIL**
**ENGINEERING: NOT_RUN**
**RUNTIME: NOT_RUN**

**B-02A–B-02D GATE: FAIL**
**X-01 READINESS: NOT_READY**

Wariant ograniczonego X-01 read-only na lokalnym NTFS jest technicznie przygotowany po pozytywnych wymaganych RT i zmierzonym profilu NTFS, ale formalna bramka wymaga decyzji. Nie jest to zgoda na uruchomienie X-01 ani kwalifikacja produkcyjnej stabilności. NF-11 minimum 8 h: NOT_RUN, jawna decyzja użytkownika/B-10B/R-14. [Dokładna rozbieżność i minimalna propozycja do zatwierdzenia](../../docs/16_RESILIENCE_RECOVERY.md#nf-11-i-formalna-bramka--rozbieżność-do-decyzji).

## RT — oryginalne kryteria i wynik

| RT | Wynik | Liczba dowodów / zakres |
|---|---|---|
| RT-01 | PASS | 6; wyłącznie Fake/Host, bez TIA |
| RT-02 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-03 | PASS | 18; wyłącznie Fake/Host, bez TIA |
| RT-04 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-05 | PASS | 4; wyłącznie Fake/Host, bez TIA |
| RT-06 | PASS | 6; wyłącznie Fake/Host, bez TIA |
| RT-07 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-08 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-09 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-10 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-11 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-12 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-13 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-14 | NOT_RUN | 0; późniejszy Engineering/sync/simulation |
| RT-15 | PASS | 12; wyłącznie Fake/Host, bez TIA |
| RT-16 | PASS | 12; wyłącznie Fake/Host, bez TIA |

RT-03/05/06/15/16: PASS na Fake; to nie readback, compile, Save lub Apply TIA. Syntetyczny P/B guard, efekt counter i brak overlap są jawnie testowym adapterem. 49 dowodów obejmuje 48 nowych testów oraz istniejący queue-capacity. Wszystkie mandatory RT mają dodatni wynik; pozostałe pełne kryteria TIA/sync/symulacji nie są pozornie zaliczone.

## NF — kwalifikacja

| NF | Wynik | Zakres / ograniczenie |
|---|---|---|
| NF-01 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |
| NF-02 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |
| NF-03 | FAIL | NTFS save/replace PASS; repo exFAT SLO niezakwalifikowane. Zielony test safe refusal/izolacji nie oznacza pozytywnego SLO. |
| NF-04 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |
| NF-05 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |
| NF-06 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |
| NF-07 | NOT_RUN | Full target profile depends on later implementation or laboratory qualification. |
| NF-08 | NOT_RUN | Idle 306 s PASS; pełna skala 1000 obiektów/100 MiB i alert workera niezakwalifikowane. |
| NF-09 | NOT_RUN | Full target profile depends on later implementation or laboratory qualification. |
| NF-10 | NOT_RUN | Full target profile depends on later implementation or laboratory qualification. |
| NF-11 | NOT_RUN | 1001 operacji / 513.29 s; minimum 8 h NOT_RUN, B-10B/R-14. |
| NF-12 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |
| NF-13 | NOT_RUN | 100 deterministic Core cases PASS; actual planner remains B-04A. |
| NF-14 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |
| NF-15 | PASS | Implemented synthetic offline boundary only; see measurements and qualification limits. |

## Pomiary — wszystkie próbki w percentylach

| Profil | N | p50 ms | p95 ms | p99 ms |
|---|---:|---:|---:|---:|
| Status/1 | 1000 | 4.8155 | 6.1413 | 6.8501 |
| Status/8 | 1000 | 8.9787 | 14.5978 | 19.3525 |
| Submit/8 | 64 | 140.0376 | 236.3253 | 318.9709 |
| Health/8 | 1000 | 15.9288 | 25.6193 | 34.2449 |
| Result/8 | 1000 | 9.0015 | 14.0561 | 17.9073 |
| Events/8 | 1000 | 56.8113 | 86.8999 | 97.8438 |
| Start i historia | 20 | 980.4183 | 1087.9415 | 2189.3305 |
| NTFS 1 MiB save/replace | 100 | 2.4019 | 3.2648 | 4.0410 |
| Repo exFAT replace bez workera | 100 | 19.2293 | 205.5911 | 311.1510 |
| Repo exFAT replace przy Hang | 100 | 18.9724 | 28.5801 | 68.5112 |
| Repo exFAT zapis w miejscu | 100 | 17.1572 | 31.1062 | 71.7243 |

Idle: 306.25 s, CPU 0.9075% znormalizowane do 8 logicznych procesorów; maks. RAM Host 89.33 MiB/Fake 37.34 MiB. Uchwyty: 463→469, max 472; dwa własne procesy. Kolejka 1000: odmowa nowego, duplicate/status/health dostępne. Read po recovery 847.71 ms.

Środowisko: Intel Xeon E3-1240 v5 3.50 GHz, 4 rdzenie/8 logicznych, 31.93 GiB fizycznego RAM; Windows 10.0.22000/.NET 10.0.13 x64, state/NTFS, repo/exFAT. Stan AV odczytano przez SecurityCenter2 (ESET Security/Windows Defender); wpisy nie stanowią dowodu aktywności każdego produktu. Nie zmieniano ochrony. Pełne wartości i wszystkie 20 próbek startu w JSON.

## Recovery, bezpieczeństwo i ograniczenia

Zaakceptowane ID/history przetrwały deterministyczne crash boundaries; wynik UNKNOWN i quarantine przetrwały restart, cancel, late success oraz lease expiry. Stary epoch nie zatwierdza wyniku. Po uszkodzeniu końcówki result WAL zachowano valid intent prefix i UNKNOWN zamiast fałszywego sukcesu. Hardware power-loss i odzyskiwanie całkowicie zniszczonego WAL nie są zaliczone.

HMAC/epoch/sequence/version/frame size/JSON/truncated/peer PID: 8 procesowych prób odrzuconych. ACL nie osłabiano. Own child/lock anomaly wykryto, worker/child kontrolowano po PID/start, sentinel niebędący własnością Host przetrwał. Po testach brak nowych własnych orphan processes. Foreign SID: NOT_RUN (brak zatwierdzonego środowiska).

Pozostawiony katalog B-02C a71bad688cf24e0eb6e04f85d6474ab9: potwierdzona własność syntetyczna, 0 aktywnych procesów/reparse, 7 exclusive read opens. Nadal pozostawiony bez obejścia wcześniejszej odmowy narzędzia blocked by policy. Cleanup po nieudanym startup ma regresję PASS.

Naprawiono limit historii workera po 1000, nadmierny polling idle i koszt pełnej walidacji health/obserwacji. Historyczne FAIL i zmiana liczby próbek startup N=6→20 są jawne w Zmiany.md; nie zmieniono progów. M:/exFAT ma niestabilne opóźnienia i wcześniejsze odmowy replace także w próbach niezależnych od Host. Nie naprawiono tego przez niebezpieczne delete+copy, retry lub zmianę ACL.

Brak unresolved critical w modelu/lifecycle Core; brak pozytywnej pełnej kwalifikacji całego profilu dysku repo i produkcyjnej stabilności. [Dokumentacja i propozycja bramki](../../docs/16_RESILIENCE_RECOVERY.md), [lista zmienionych plików](../../Zmiany.md), [maszynowy snapshot](qualification.json). Po commicie/push zadanie kończy się bez X-01.
