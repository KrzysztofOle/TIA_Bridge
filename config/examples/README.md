# Konfiguracje syntetyczne B-01B

[workspace.synthetic.json](workspace.synthetic.json) wiąże workspace z dwoma syntetycznymi project_key i przykładowym root C:\SyntheticWorkspace. Biblioteka nie tworzy tego katalogu ani nie sprawdza jego dysku. [capabilities.synthetic.json](capabilities.synthetic.json) opisuje wyłącznie hipotetyczny profil fixture synthetic.offline: implemented FC read, designed-only FB/Classic screen oraz częściowy Unified script read. Nie jest konfiguracją rzeczywistego adaptera Siemens.

Brak wpisu dla Safety lub operacji sync.apply oznacza deny. Write scopes są puste. Bez zewnętrznie zweryfikowanych kwalifikacji nawet wpis implemented odmawia. synthetic-only-receipt jest identyfikatorem testowym; jego obecność w deklaracji nie stanowi dowodu. Registry nie ocenia uprawnień konkretnej sesji.

Schematy i format: [B-01B](../../docs/12_WORKSPACE_CAPABILITIES.md). [Manifesty](../../examples/README.md) używają tych samych workspace/project/adapter bindings i capability hash. Walidacja wszystkich przykładów odbywa się przez [testy .NET i niezależne JSON Schema](../../tests/README.md). Brak sekretów, certyfikatów, rzeczywistych odbiorców i danych klienta.
