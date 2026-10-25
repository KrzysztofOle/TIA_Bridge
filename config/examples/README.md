# Konfiguracje syntetyczne B-01B

[workspace.synthetic.json](workspace.synthetic.json) wiąże workspace z dwoma syntetycznymi project_key i przykładowym root C:\SyntheticWorkspace. Biblioteka nie tworzy tego katalogu ani nie sprawdza jego dysku. [capabilities.synthetic.json](capabilities.synthetic.json) opisuje wyłącznie hipotetyczny profil fixture synthetic.offline: implemented FC read, designed-only FB/Classic screen oraz częściowy Unified script read. Nie jest konfiguracją rzeczywistego adaptera Siemens.

Brak wpisu dla Safety lub operacji sync.apply oznacza deny. Write scopes są puste. Bez zewnętrznie zweryfikowanych kwalifikacji nawet wpis implemented odmawia. synthetic-only-receipt jest identyfikatorem testowym; jego obecność w deklaracji nie stanowi dowodu. Registry nie ocenia uprawnień konkretnej sesji.

Schematy i format: [B-01B](../../docs/12_WORKSPACE_CAPABILITIES.md). [Manifesty](../../examples/README.md) używają tych samych workspace/project/adapter bindings i capability hash. Walidacja wszystkich przykładów odbywa się przez [testy .NET i niezależne JSON Schema](../../tests/README.md). Brak sekretów, certyfikatów, rzeczywistych odbiorców i danych klienta.

## B-02C — lokalny Host z Fake

[host.fake.json](host.fake.json) jest konfiguracją syntetyczną bez sekretów: osobny dev state na LOCALAPPDATA/NTFS, UUID 111…, Fake Worker i tylko project.inspect. Wymaga build Release i uruchomienia z root repo. Nie zawiera adresu PLC, AP19, native adaptera ani grantu; token jest tworzony dopiero przez Host poza repo. [Polecenia i cykl CODEX](../../docs/15_HTTP_API_CLI.md), [walidacja](../../tests/README.md).
