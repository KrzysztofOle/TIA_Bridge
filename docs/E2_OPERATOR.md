# E2 — krótka instrukcja operatora przed B1

**Teraz niczego nie otwieraj ani nie zatwierdzaj w TIA.** E2-A przygotowało dokumenty;
Engineering i enrollment nie są autoryzowane. [Plan B1](E2-B1_TEST_PLAN.md),
[pakiet decyzji i ograniczenia](B03A_E2_A_PREPARATION.md).

Po osobnym zleceniu E2-B1 operator wykonuje ręcznie:

1. Wskazuje własną kopię `T05_RecipeSim_V19_Testowy`, jej dokładny AP19 i root NTFS
   oraz potwierdza prawo read-only. Jeśli kopia nie jest przygotowana, jej utworzenie/
   otwarcie jest odrębną zatwierdzoną czynnością, bez Save/Archive z tego pakietu.
2. Wybiera właściwą już otwartą instancję V19 i potwierdza w UI exact ścieżkę oraz
   brak niezapisanych zmian. Nie zapisuje/naprawia dirty projektu w celu zaliczenia
   preflightu. Uzgadnia brak edycji i konkurencyjnych działań na czas testu; nie
   odłącza innych użytkowników i nie zamyka ich instancji.
3. Przekazuje exact path i potwierdzenie stanu operator-before; CODEX w późniejszym
   zatwierdzonym zadaniu koreluje je z Windows PID/start/path/SID i publiczną enumeracją.
   Sam tytuł okna lub nazwa Testowy nie wystarcza. IsModified z API jest odczytywane
   dopiero wewnątrz autoryzowanego cyklu Attach, nie w Windows preflight.
4. Przegląda i zatwierdza wypełniony authorization.md: B1, hash planu, buildy,
   binding/ProjectKey, 13 getterów DiscoveryV1 + kontrole/release, 20 cykli STA,
   monitoring i STOP, okno i operator. To zgoda na B1, bez automatycznej zgody na B2.
5. Dopiero po osobnej decyzji przygotowuje konto desktopowe Host/operator, chronione
   config/store/ACL według [18](18_LOCAL_TRUSTED_ENROLLMENT.md). Bootstrap nie potrzebuje
   klucza CNG. Nic nie ustawia się automatycznie; odrębne konta/usługa nie są kwalifikowane.
6. Po osobnej zgodzie na live przygotowuje grant
   [qualification-only B0](19_CONTROLLED_QUALIFICATION_BOOTSTRAP.md): świeże exact
   wartości, nowe Authorization ID, plan hash, Host/Worker/API build i okno sesji.
   Przegląda bootstrap-inspect i jawnie bootstrap-authorize z exact document hash.
   Bez aktywnego lokalnego grantu albo normalnego rzeczywistego E1 → BLOCKED.
   Nie podpisuje synthetic/nieuruchomionego PASS. Historyczne X-01 nie kwalifikuje B1.
   Klucz i real enrollment do E1 są późniejszymi oddzielnymi decyzjami.
   Podpis/enrollment wymagają późniejszego prawdziwego source-run review.
7. Jeśli Siemens pokaże dialog dostępu dla exact zatwierdzonego Workera, operator
   sam sprawdza tożsamość aplikacji i obsługuje dialog w zakresie grantu. CODEX nie
   klika bez przedstawienia czynności/powodu/instrukcji i wyboru A — CODEX / B — użytkownik.
   Nieoczekiwany dialog albo brak możliwości weryfikacji → STOP.

W trakcie testu nie edytuj projektu. Po B1 potwierdź ponownie ścieżkę/czysty stan UI
i zapisz operator-after. Dirty/drift/niepewność → STOP bez Save. Zachowaj otwartą TIA;
zwolnienie zasobów dotyczy wyłącznie własnej sesji Bridge i Workera.
Przejście do [B2](E2-B2_TEST_PLAN.md) wymaga udokumentowanego B1 qualification PASS,
hashów, release, trend review, osobnej decyzji i całkiem nowego preflightu B2.

## E2-G — przed zgodą na rzeczywisty test

Komputer jest współdzielonym stanowiskiem: normalna praca poza projektem testowym
jest dozwolona. Tylko kolizja Siemens, niespełnione safety lub rzeczywista presja
uniemożliwiająca wiarygodną pracę wymagają STOP. Zwykły CPU/dysk i PASS_WITH_LOAD
nie blokują [SHARED_WORKSTATION 1.0.0](20_PERFORMANCE_QUALIFICATION_PROFILES.md).

Najpierw sprawdź [raport E2-G](21_E2_B1_READINESS_GATE.md) dla jednego bieżącego runu
i uzupełnij dossier exact bindingu z niezależnie przejrzanych danych Windows/UI.
Bez dossier wynik pozostaje BLOCKED. READY_FOR_OPERATOR_AUTHORIZATION oznacza,
że można świadomie rozpatrzyć autoryzację; nie wykonuj na jego podstawie Attach.
Następnie wykonaj kroki 1–7 powyżej w osobno zatwierdzonym zadaniu B1, w tym wybór
okna/operatora, sprawdzenie ACL, bootstrap-inspect i jawne bootstrap-authorize.
Przed inventory pierwszy autoryzowany odczyt potwierdza IsModified=false, każdy
cykl wymaga false/false. Nie pozyskuj tej wartości przez nieautoryzowany Attach.
Pełna kwalifikacja offline BLOCKED/NF-11 NOT_RUN i Worker Handle Risk UNKNOWN
pozostają odrębne od gotowości do ograniczonego eksperymentu. B2 nie ma autoryzacji.
