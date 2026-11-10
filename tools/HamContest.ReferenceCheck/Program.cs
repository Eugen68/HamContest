using System.Security.Cryptography;
using System.Text;
using HamContest;
using HamContest.Udc;

// The third-party UDC is read for verification and is not bundled or redistributed in the package.
const string source = "https://rttyops.com/rttyopsww/RTTY%20ops%20LOG%20SETUP/N1MM%20LOGT/RTTYops-WW-DX.udc";
const string expectedSha256 = "616e3046a80e94a25ac51f446bfa9541281a64b86cf374340674948c81acf9da";
byte[] bytes;
if (args.Length == 1) bytes = File.ReadAllBytes(args[0]);
else if (args.Length == 0)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("HamContest-ReferenceCheck/0.1");
    bytes = await http.GetByteArrayAsync(source);
}
else throw new ArgumentException("Supply zero arguments to download the pinned reference, or one local UDC path.");
string actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
Console.WriteLine($"Source: {source}\nSHA-256: {actualSha256}");
if (actualSha256 != expectedSha256) throw new InvalidDataException("Reference UDC changed. Review its revision/rules and update the pin explicitly; it must not silently change test semantics.");
var imported = UdcImporter.Import(new UTF8Encoding(false, true).GetString(bytes));
foreach (var diagnostic in imported.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
    Console.WriteLine($"{diagnostic.Key} line {diagnostic.Line}: {diagnostic.Message}");
var rules = imported.RequireRules();
if (rules.Name != "RY_OPSWWDX" || rules.UsesSerialNumbers || rules.RequiredCountryIdentityKind != CountryIdentityKind.Wae)
    throw new InvalidOperationException("Unexpected definition metadata.");
var own = new StationInfo("YO9VA") { CountryPrefix = "YO", Continent = "EU", CountryIdentityKind = CountryIdentityKind.Wae };
var session = new ContestSession(rules, own);
var cases = new (string Call, string Country, string Continent, long Frequency, string Year)[]
{
    ("SM7ABC", "SM", "EU", 7_080_000, "1984"),
    ("SM8ABC", "SM", "EU", 7_080_000, "1984"),
    ("SM7ABC", "SM", "EU", 7_080_000, "1984"), // dupe
    ("SM7ABC", "SM", "EU", 14_080_000, "1984"), // new band and year multiplier
    ("K1ABC", "K", "NA", 7_080_000, "1990"),
    ("YO3ABC", "YO", "EU", 7_080_000, "1990")
};
for (int i = 0; i < cases.Length; i++)
{
    var entry = cases[i];
    session.Log(new Qso
    {
        Time = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero).AddMinutes(i),
        FrequencyHz = entry.Frequency, Station = new(entry.Call)
        {
            CountryPrefix = entry.Country, Continent = entry.Continent, CountryIdentityKind = CountryIdentityKind.Wae
        },
        Sent = new() { Value = "1999" }, Received = new() { Value = entry.Year }
    });
}
var score = session.Score();
var expectedPoints = new[] { 10, 10, 0, 5, 6, 2 };
if (!score.Contacts.Select(c => c.Points).SequenceEqual(expectedPoints) || score.QsoPoints != 33 || score.ScoringMultipliers != 3 || score.Total != 99)
    throw new InvalidOperationException($"Reference score mismatch: {score.QsoPoints} points, {score.ScoringMultipliers} multipliers, {score.Total} total.");
string cabrillo = CabrilloExporter.Export(session);
if (!cabrillo.Contains("CONTEST: RTTY-OPS-WW-DX\r\n", StringComparison.Ordinal) || !cabrillo.Contains("CLAIMED-SCORE: 99\r\n", StringComparison.Ordinal)
    || cabrillo.Split("\r\n").Count(line => line.StartsWith("QSO:", StringComparison.Ordinal)) != 6)
    throw new InvalidOperationException("Reference Cabrillo mismatch.");
Console.WriteLine($"PASS original UDC: {rules.Name}, points=33, multipliers=3, total=99, duplicate=1, Cabrillo QSO lines=6.");
Console.WriteLine("This is an independent reference calculation, not an execution/comparison with N1MM Logger+.");
