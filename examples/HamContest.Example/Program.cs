using HamContest;
using HamContest.Udc;

string folder = AppContext.BaseDirectory;
if (args.Length != 0)
{
    // Inspect a user's UDC without silently claiming support for unknown rules.
    var external = UdcImporter.Import(File.ReadAllText(args[0]), args.Skip(1).Select(path => SectionList.Parse(File.ReadAllText(path))));
    foreach (var diagnostic in external.Diagnostics)
        Console.WriteLine($"{diagnostic.Severity} line {diagnostic.Line} {diagnostic.Key}: {diagnostic.Message}");
    Console.WriteLine($"Can score: {external.CanScore}; Cabrillo: {external.Rules?.CanExportCabrillo == true}");
    return external.CanScore ? 0 : 2;
}
var imported = UdcImporter.Import(File.ReadAllText(Path.Combine(folder, "DEMO-RTTY.udc")),
    [SectionList.Parse(File.ReadAllText(Path.Combine(folder, "DEMO.sec")))]);
var rules = imported.RequireRules();
var session = new ContestSession(rules, new StationInfo("YO9VA"));
var serial = session.ReserveSerial();
// Persist BEFORE TX. A repeat must reuse this reservation, including after restart.
string path = Path.Combine(Path.GetTempPath(), "hamcontest-demo-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    SessionStore.Save(path, session);
    session = SessionStore.Load(path, rules);
    session.Log(new Qso
    {
        Time = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero), FrequencyHz = 14_080_000,
        Station = new("YO3ABC"), Received = new() { Serial = 12, Value = "BU" }
    }, session.PendingSerial);
    SessionStore.Save(path, session);
    Console.WriteLine($"Points={session.Score().QsoPoints}; Multipliers={session.Score().Multipliers}; Total={session.Score().Total}");
    Console.Write(CabrilloExporter.Export(session, new Dictionary<string, string> { ["CATEGORY-MODE"] = "RTTY" }));
    return 0;
}
finally { File.Delete(path); }
