# Definitii reale si jurnale de referinta

## RTTYops WW DX — fisier original

Sursa publica, legata din pagina Software & Hardware a organizatorului:

https://rttyops.com/rttyopsww/RTTY%20ops%20LOG%20SETUP/N1MM%20LOGT/RTTYops-WW-DX.udc

- Name: RY_OPSWWDX
- Revision: 1.0.0; LastUpdate: 2021-08-18
- SHA-256: `616e3046a80e94a25ac51f446bfa9541281a64b86cf374340674948c81acf9da`
- Verificare: `dotnet run --project tools/HamContest.ReferenceCheck -c Release`

Unealta citeste fisierul nemodificat de pe site, in memorie, sau dintr-o cale locala data ca
argument. Refuza o alta amprenta. Fisierul tert nu este copiat in repository sau pachetul NuGet.
Daca site-ul modifica fisierul ori nu poate fi accesat, pasul extern de CI esueaza explicit;
unit tests si biblioteca nu au dependente de retea.

Jurnalul sintetic foloseste statia proprie YO9VA, tara YO, continent EU si identitati WAE
declarate de gazda. Schimbul transmis este anul 1999. Se verifica:

| QSO | Corespondent | Tara | Banda | An primit | Puncte | Multiplicator nou |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | SM7ABC | SM | 40m | 1984 | 10 | 1984 / 40m |
| 2 | SM8ABC | SM | 40m | 1984 | 10 | — |
| 3 | SM7ABC | SM | 40m | 1984 | 0, dupe | — |
| 4 | SM7ABC | SM | 20m | 1984 | 5 | 1984 / 20m |
| 5 | K1ABC | K | 40m | 1990 | 6 | 1990 / 40m |
| 6 | YO3ABC | YO | 40m | 1990 | 2 | — |

Total: 33 puncte QSO, 3 multiplicatori, scor 99. Sunt verificate si antetul Cabrillo,
scorul declarat si prezenta tuturor celor sase QSO-uri, inclusiv cel dublu.
Rezultatul asteptat este calculat independent dupa parametrii fisierului, nu dupa iesirea
bibliotecii. Fisierul este o revizie istorica: verificarea lui nu certifica regulile actuale
ale concursului sau toate posibilitatile formatului UDC.

## Weekly RTTY Test — scenariu de reguli

Sursa regulilor si a definitiei WRT: https://radiosport.world/wrt.html

`ExtendedUdcTests.WrtStyleLogCountsUniqueCallsignsAcrossBands` foloseste o definitie compacta
originala a testului, cu punctaj de 1/QSO, dupe pe banda si indicativ unic pe concurs ca
multiplicator. Doua indicative lucrate pe trei combinatii valide de banda/indicativ dau
3 × 2 = 6; repetarea pe aceeasi banda nu adauga puncte. Exportul foloseste nume/QTH,
fara a inventa un raport RST care nu este in sablon.

Acest test verifica scenariul si campurile documentate. Nu incarca o copie nemodificata a
intregului WRT.udc si nu redistribuie acel fisier.

## Limite si migrare

Nu a fost executata o comparare cu o instalare N1MM Logger+. Pentru aceasta sunt necesare
aceleasi definitii, date de tara, statie proprie, QSO-uri si setari in ambele programe.
Datele WAE trebuie rezolvate corect de aplicatia gazda; setarea unui enum nu transforma date DXCC.

Interpretul este acum `HamContest-UDC-v2`. Amprenta definitiei difera de preview.1, chiar daca
textul UDC este identic: regulile suportate si semantica rezultatelor s-au extins. Snapshot-urile
preview.1 nu sunt redeschise automat cu v2. Pastrati versiunea veche pentru citire si migrati
explicit jurnalul verificat; nu eliminati verificarea amprentei din JSON.
