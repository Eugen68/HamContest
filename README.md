# HamContest

Biblioteca .NET pentru jurnal si reguli de concurs radioamator, cu import de definitii
N1MM Logger+ UDC/SEC. Independenta de WPF, audio, decodor si controlul statiei.

**Prima versiune: 0.1.0-preview.1**, pentru .NET 8 si .NET 10, Windows/Linux.
Suportul UDC este un subset explicit, nu compatibilitate completa cu N1MM.
Vezi [matricea de compatibilitate](docs/udc-support.md) inainte de folosirea unui fisier.

## Functionalitati

- Import INI UDC cu diagnostice, numarul liniei si pastrarea tuturor parametrilor.
- SEC simplu: antet `Type=... SubType=` si cate un cod de sectiune pe linie.
- QSO-uri cu identitate stabila, UTC, frecventa RF in **Hz**, mod, statie si schimburi separate.
- Duble o data pe concurs, pe banda, pe banda/mod sau fara verificare.
- Punctaj constant sau tabele simple pe banda, mod ori tara/continent.
- Pana la trei grupe de multiplicatori, cu domeniu independent: banda, mod, banda/mod sau concurs.
- Serial rezervat explicit, reutilizat la repetare; numerele abandonate sau sterse nu sunt reciclate.
- Editare/stergere si recalcularea rezultatelor, inclusiv promovarea urmatorului QSO dupa stergerea unui dupe.
- Snapshot JSON versionat, verificat fata de definitia concursului; salvare prin fisier temporar si inlocuire.
- Cabrillo 3.0 pentru sabloane explicite `CabrilloFormat=99`; exportul refuza trunchierea schimburilor.
- Exemplu executabil si teste automate pe ambele sisteme si versiuni .NET.

## Exemplu

```csharp
using HamContest;
using HamContest.Udc;

var imported = UdcImporter.Import(
    File.ReadAllText("samples/DEMO-RTTY.udc"),
    [SectionList.Parse(File.ReadAllText("samples/DEMO.sec"))]);

// Arata toate diagnosticele in aplicatia gazda, inclusiv avertismentele.
foreach (var item in imported.Diagnostics)
    Console.WriteLine($"{item.Severity}, linia {item.Line}: {item.Message}");

var rules = imported.RequireRules(); // refuza definitiile cu reguli nesuportate
var session = new ContestSession(rules, new StationInfo("YO9VA"));
var serial = session.ReserveSerial();
SessionStore.Save("session.json", session); // obligatoriu INAINTE de transmiterea serialului

// Repetarea raportului: refoloseste serial.Number, nu rezerva alt numar.
session.Log(new Qso
{
    Time = DateTimeOffset.UtcNow,
    FrequencyHz = 14_080_000,
    Mode = ContactMode.RTTY,
    Station = new("YO3ABC"),
    Received = new() { Serial = 12, Value = "BU" }
}, serial);
SessionStore.Save("session.json", session);

Console.WriteLine(session.Score().Total);
File.WriteAllText("contest.log", CabrilloExporter.Export(session));
```

`DEMO-RTTY` este un exemplu original, **nu regulamentul unui concurs real**.
Exemplul complet, cu salvare/redeschidere, se ruleaza astfel:

```sh
dotnet run --project examples/HamContest.Example -c Release
```

Inspectarea unui UDC existent si a fisierelor lui SEC:

```sh
dotnet run --project examples/HamContest.Example -c Release -- contest.udc sections.sec
```

Codul de iesire este 2 daca definitia nu poate fi folosita pentru punctaj. Importul nu descarca
fisiere si nu depinde de o instalare N1MM. UDC/SEC pot fi decodate de gazda din alte codificari;
`UdcDocument.Read` accepta UTF-8 strict si UTF-16/32 cu BOM.

## Integrare

`IContestRules` permite si reguli native C#, independent de importator. `StationInfo` primeste
din gazda identitatea tarii (prefixul principal CTY), continentul, zonele CQ/ITU si prefixul WPX.
HamCallsign poate furniza o parte din acestea; HamContest nu ghiceste tara dupa inceputul
indicativului si nu are dependenta de HamRig sau HamCallsign.

Gazda mapeaza explicit campurile de receptie: `Exchange.Value` pentru Exchange1Text,
`Misc`, `Comment`, `Section`, `Grid`, `Name`, `Serial` pentru celelalte campuri.
`Sent.Value` contine partea fixa a schimbului transmis, fara serial; serialul este separat.
Frecventa este frecventa RF a QSO-ului, nu frecventa audio Mark/Space.

Aplicatia gazda gestioneaza interfata, macro-urile, CAT/PTT si momentul transmiterii.
HamContest nu comanda statia. Integrarea in RttyDecoder va fi un pas separat.

## Salvare si seriale

O sesiune foloseste o singura secventa de seriale. `ReserveSerial()` intoarce aceeasi rezervare
pana la logarea QSO-ului sau `AbandonSerial()`. Rezervarea avanseaza contorul imediat.
Salveaza dupa rezervare **inainte de TX**, apoi dupa fiecare logare, editare, stergere sau abandon.
Daca salvarea esueaza, gazda nu trebuie sa transmita un serial nepersistat.

`SessionStore.Load(path, rules)` reface inclusiv rezervarea activa. Pastreaza UDC si SEC folosite:
snapshot-ul contine amprenta SHA-256 a definitiei si sectiunilor, nu fisierele propriu-zise.
Schimbarea definitiei cere migrare explicita; incarcare refuzata inseamna ca regulile nu mai coincid.
Amprenta include versiunea interpretului. Un nou calcul nu rescrie serialele deja transmise.

Sesiunea sincronizeaza accesul din firele aceleiasi aplicatii. Gazda trebuie sa serializeze
salvarile si sa asigure un singur scriitor pentru un fisier. Nu exista sincronizare multioperator,
autosave in fundal sau tranzactie comuna cu hardware-ul de emisie.

## Compilare si verificare

Cu SDK .NET 10 si runtime-urile .NET 8/10:

```sh
dotnet test tests/HamContest.Tests -c Release
dotnet run --project examples/HamContest.Example -c Release
dotnet pack src/HamContest -c Release -o artifacts
```

GitHub Actions ruleaza testele pe Windows/Linux, executa exemplul si produce pachetul NuGet
ca artefact. Pachetul nu este publicat automat pe NuGet.org.

## Compatibilitate

Regulile sunt implementate independent dupa [documentatia UDC N1MM](https://n1mmwp.hamdocs.com/appendices/udc-editor/).
Testele folosesc exemple originale si cazurile de comportament documentate. Compatibilitatea
nu a fost inca verificata prin rularea aceluiasi jurnal intr-o instalare N1MM Logger+.
Inainte de folosirea unui concurs real, trebuie comparate schimburile, dublele, punctele,
multiplicatorii si exportul cu regulamentul organizatorului si un jurnal de referinta.

Nu sunt incluse definitii de concurs descarcate de la terti. Nu este implementat importul
bazelor de date N1MM, al macro-urilor `.mc`, exportul ADIF sau punctajul tuturor concursurilor.
