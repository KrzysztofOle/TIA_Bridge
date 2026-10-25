# TiaBridge.FakeWorker — wykonawca syntetyczny B-02B

Oddzielny proces .NET 10 używany wyłącznie przez zaufany FakeWorkerSupervisor i testy. Bootstrap przez odziedziczone stdin; brak publicznego CLI, HTTP, Siemens DLL lub operacji PLC/HMI. Nie uruchamia TIA ani skryptów znalezionych w danych.

[Scenariusze i ograniczenia](../../docs/14_SCHEDULER_IPC.md) obejmują sukces, opóźnienie, błędy, hang, utratę heartbeat/IPC/procesu, late success, synthetic unknown, safe cancel i duplicate dispatch. Odbiór IPC i heartbeat są niezależne od jednej serializowanej operacji. Cache i effect counter są ograniczone do sesji; brak replay mutacji po restarcie zapewnia ledger Host.

Fake odczytuje tylko managed artifacts po ID/SHA i odrzuca reparse/traversal/limit. Syntetyczny wzorzec wyniku nie dowodzi wykonania TIA. Zakończenie fake delay po utracie pipe nie jest kwalifikacją Dispose ani przerwania Siemens. Pełne testy: [instrukcja](../../tests/README.md).
