# Compatibilitate UDC / SEC — versiunea initiala

Acest document defineste comportamentul acceptat de HamContest 0.1.0-preview.2.
Sursa formatului: [manualul UDC N1MM Logger+](https://n1mmwp.hamdocs.com/appendices/udc-editor/).
Nu este o promisiune de compatibilitate cu fiecare functie sau versiune N1MM.

## Diagnostice

- Sectiunile/cheile nu tin cont de majuscule. Valorile text sunt pastrate.
- Comentariile `;`/`#` ocupa o linie intreaga. `=` si `;` din valori nu sunt eliminate.
- Cheile duplicate si liniile incorecte sunt erori, fara alegerea arbitrara a ultimei valori.
- Un parametru necunoscut **nevid** este eroare: `CanScore=false`, `Rules=null`.
- Un parametru necunoscut gol este avertisment si ramane in documentul brut.
- Metadatele `[Author]`/`[File]` sunt pastrate, fara efect asupra punctajului.
- Erorile de export sunt separate: un UDC poate avea punctaj suportat, dar Cabrillo indisponibil.
- `RequireRules()` nu poate ocoli erorile. O regula noua necesita implementare si teste.

## Parametri

| Parametru | Suport |
| --- | --- |
| Name, DisplayName, CabrilloName | Identificare; Name 1–10 caractere ASCII litere/cifre/_/- |
| Mode | CW, SSB, RTTY, BOTH; BOTH permite toate modurile modelului |
| DupeType | 1 concurs, 2 banda, 3 banda/mod, 4 fara verificare |
| DefaultContestExchange | `001` ca primul cuvant activeaza serialele; alt schimb este fix; expresiile Last… sunt refuzate |
| PointsPerContact | Intreg constant sau perechi selector/puncte, descrise mai jos |
| PointsMultByBand | Perechi banda in MHz/factor intreg nenegativ, pentru cele sase benzi HF de concurs |
| CountMultOnlyFor/2/3 | Include multiplicatorul numai pentru tari CTY exacte |
| DoNotCountMultOnlyFor/2/3 | Exclude multiplicatorul pentru tari CTY sau continente exacte |
| CountMultOnlyForSec/2/3 | Include numai sectiunile indicate; necesita lista SEC si coduri valide |
| MultMult/2/3 | 1 contribuie la scor, 0 pastreaza multiplicatorul numai pentru afisare |
| NumMults | 0–3; implicit 1 |
| IsMultPer, IsMult1Per/2Per/3Per | 0 fara, 1 banda, 2 mod, 3 banda/mod, 4 concurs |
| Multiplier1Name/2Name/3Name | Etichete; selectorul nu este dedus din eticheta |
| MultSqlString/2/3 | CountryPrefix, WPXPrefix, Section, CQZONE, IARUZONE, Exchange, Misc, Grid, SGrid, FIELD, CallSign, FirstQSO, N/A |
| DoNotCountMeAsMult/2/3 | Excludere a valorii proprii, in afara Exchange/Misc |
| MultWindowType | Alege lista SEC dupa Type; lista trebuie furnizata pentru multiplicator Section |
| QsoErrorString | Any/Numeric cu _not_Empty, Grid, nume lista SEC cu optional _or_Empty; poate avea prefixe de camp si mai multe clauze |
| EntryWindowInfo | Perechi camp/latime expuse gazdei; biblioteca nu construieste ferestre |
| CabrilloVersion | 3.0 |
| CabrilloFormat | Export doar 99 cu CabrilloString valid; celelalte formate sunt raportate ca indisponibile |
| CabrilloString | Campuri SNT, SentNr, CallSign, RCV, RcvNr, Comment, GridSquare, Exchange1, Name, SentExch, SentExchPart1/2/3, Space |
| QsoNumbersByBand | Numai valoarea neutra 0 |
| DupeSqlString | 0 fara criteriu suplimentar, 1 sectiune, 2 schimb, 3 mod, 4 locator |
| IsWorkable | Any, MyCountryOnly, ExceptMyCountry, MyContinentOnly, NAonly/SAonly/EUonly/ASIAonly/AFonly/OConly sau lista exacta de tari CTY |
| MultiplierBands | Numai 1: cele sase benzi HF de concurs |
| UsesWAECountries | True cere identitati WAE declarate de gazda; False cere DXCC |
| CQZoneMultContest, DigitalModeSqlString, SetSentTimeForContact, UsesLASTEXCHmacro, SelfSpotAllowed | Numai False/necompletat |
| ShowMyCountryStations, ShowWarcBands, ZoneType | Citite pentru gazda; nu extind benzile acceptate |
| FrameText, LogInfo, ScoreSummaryMultNames, SpecialInstructions, WebAddress, GenericPrintString/Header, Period, StartOfContest, EndOfContest, MinimumOffTime, OverlayCategory | Metadate pastrate; nu exista motor de timpi OFF sau randare N1MM |

Valorile implicite implementate: Mode=CW, DupeType=2, PointsPerContact=1, NumMults=1,
IsMultPer=0, DefaultContestExchange=001, CabrilloFormat=1 (export indisponibil).
MultSqlString absent nu este ghicit; daca o eticheta activa sugereaza un multiplicator, importul cere selector explicit.

## Punctaj

Sunt acceptate separat:

- constanta intreaga, de exemplu `2`;
- tabel pe benzi, de exemplu `20m,5,40m,3`;
- tabel pe moduri, de exemplu `CW,3,SSB,2,RTTY,4,DIGI,4`;
- tabel geografic: prefixe principale CTY, MyCountry, SameContinent, OtherContinent sau AF/AN/AS/EU/NA/OC/SA.

Exemplul documentat `OH,5,SM,5,MyCountry,1,SameContinent,2,OtherContinent,3` acorda
mai intai punctele tarii explicite; apoi foloseste prima clauza geografica potrivita.
Identitatile CTY si continentele trebuie furnizate de gazda, inclusiv pentru statia proprie.
Lipsa unei potriviri foloseste implicit 1 punct. DIGI corespunde RTTY sau PSK.
Combinarea benzilor, modurilor si geografiei in acelasi tabel este refuzata pana la verificarea
precedentei cu N1MM. Nu sunt implementate expresiile numerice Exchange/Misc, bonusurile,
factorii pe ora/putere, distantele, sesiunile multiple sau regulile de schimbare a benzilor.

Rezultatul este suma punctelor QSO inmultita cu numarul total al multiplicatorilor din grupele
active. Fara grupe active, rezultatul este suma punctelor. QSO-urile duble raman in jurnal,
dar nu primesc puncte sau multiplicatori. Calculul este in ordine UTC, cu ordine de inserare
pentru timpi egali; identitatea QSO trebuie sa fie unica.

CountryPrefix foloseste identitatea CTY furnizata, nu un prefix extras din indicativ.
CQZONE foloseste Exchange.Value daca este completat, altfel Station.CqZone; IARUZONE foloseste
Exchange.Misc sau Station.ItuZone. Intervalele sunt 1–40 si 1–90. WPXPrefix trebuie furnizat
explicit. Sectiunea este Received.Section sau Received.Value si trebuie sa existe in SEC.
Grid/SGrid/FIELD folosesc Received.Grid, cu validare Maidenhead; nu estimeaza distanta.

## SEC

Este suportat layout-ul simplu observat in fisiere de organizator, cu un cod pe linie:

```text
Type=DEMO SubType=
BU
CJ
DX
```

Type are cel mult 9 caractere. Codurile sunt unice fara diferentierea majusculelor.
SubType nevid, tabelele cu mai multe coloane si alte variante nu sunt interpretate aproximativ:
`SectionList.Parse` raporteaza `FormatException` cu linia.
Referinta de layout: [pagina UDC/SEC ORARI](https://www.oraricontest.id/udc/).
Fisierele originale ale organizatorului nu sunt redistribuite in repository.

## Export Cabrillo

Antet Cabrillo 3.0, frecventa RF rotunjita la kHz, UTC si propriul indicativ, urmate de
campurile explicite din CabrilloString. Modurile exportate: CW→CW, SSB→PH, RTTY→RY.
Serialele sunt formatate cu minimum trei cifre. Latimile sunt minime; un camp nevid care
depaseste o latime nenula produce eroare, fara trunchiere. `Space,N` adauga N spatii.
Latimea 0 permite campul integral. Datele trebuie sa fie ASCII imprimabil.

Antetele suplimentare (categorie, operator, putere, adresa etc.) sunt furnizate de gazda.
Pachetul nu inventeaza o categorie si nu garanteaza un fisier complet pentru depunere fara
configurarea ceruta de organizator. Formatul 99 necesita verificare pe exemplul concursului;
exportul nu inseamna ca toate regulile acelui concurs sunt deja implementate.

## Validare

Testele reproduc comportamentele documentate si invarianti ai jurnalului, pe date originale:
parser/BOM/duplicate, SEC, reguli refuzate, duble, multiplicatori, geografie, rezervari,
restart, editare/stergere, UTC, inlocuirea fisierului si Cabrillo fara pierderi de campuri.
Nu a fost executata compararea automata cu N1MM Logger+. Pentru extinderea compatibilitatii,
pastram UDC-ul de referinta si un jurnal mic cu rezultate asteptate verificate independent.

## Extensii preview.2

`PointsMultByBand=1.8,2,3.5,2,7,2` dubleaza punctele de baza pe 160/80/40m; benzile
neenumerate folosesc factorul 1. Factorii se aplica dupa alegerea punctelor de baza si nu
acorda puncte dublelor. Tabelele cu benzi repetate, benzi nesuportate sau factori negativi
sunt refuzate; depasirea numerica produce eroare, fara invelirea valorii.

Filtrele de multiplicatori sunt independente pe grupe. Include tara si include sectiunea
se combina prin AND; excluderea tarii/continentului are prioritate. Tari ca G si GJ nu se
confunda prin potrivire de prefix. Un QSO fara multiplicator eligibil isi pastreaza punctele.
Lipsa datelor necesare filtrului produce eroare de validare.

CallSign foloseste indicativul complet normalizat al corespondentului din model, independent
de Misc. FirstQSO crediteaza prima legatura din fiecare domeniu IsMultPer. Grupele MultMult=0
apar in `Multipliers` si creditele individuale, dar nu in `ScoringMultipliers`; cand nu exista
grupe care contribuie la scor, totalul este suma punctelor QSO.

DupeSqlString adauga cheia specificata la combinatia DupeType. Sectiunea foloseste Received.Section
sau Received.Value; locatorul complet este normalizat si trebuie sa fie valid. Cu DupeType=4
verificarea ramane dezactivata. IsWorkable produce eroare de validare pentru o statie neeligibila;
jurnalul nu accepta automat un QSO invalid doar pentru a-i acorda zero puncte.

Fisierele cu markeri literali ca `1*`, selectori necunoscuti (de exemplu YBCallsign), bonusuri
active sau sesiuni multiple raman refuzate. Nu interpretam aproximativ o functie doar pentru
a putea importa mai multe fisiere. Vezi [verificarea definitiilor reale](reference-validation.md).
