using HamContest.Udc;

namespace HamContest.Tests;

internal static class Fixtures
{
    internal static string Definition(params (string Key, string Value)[] changes)
    {
        var fields = new Dictionary<string, string>
        {
            ["Name"] = "TESTRTTY", ["Mode"] = "RTTY", ["DupeType"] = "2", ["NumMults"] = "0",
            ["PointsPerContact"] = "2", ["DefaultContestExchange"] = "DX", ["CabrilloFormat"] = "0"
        };
        foreach (var item in changes) fields[item.Key] = item.Value;
        return "[Contest]\n" + string.Join("\n", fields.Select(p => p.Key + "=" + p.Value));
    }
    internal static UdcContestRules Rules(params (string Key, string Value)[] changes) => UdcImporter.Import(Definition(changes)).RequireRules();
    internal static StationInfo Own => new("YO9VA") { CountryPrefix = "YO", Continent = "EU", CqZone = 20, ItuZone = 28, WpxPrefix = "YO9", Section = "BU", Grid = "KN34AA" };
    internal static Qso Contact(string call = "DL1ABC", long frequency = 14_080_000) => new()
    {
        Station = new(call) { CountryPrefix = "DL", Continent = "EU", CqZone = 14, ItuZone = 28, WpxPrefix = "DL1" },
        Time = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero), FrequencyHz = frequency,
        Received = new() { Value = "14", Serial = 12, Grid = "JO31AA", Section = "CJ" }
    };
    internal static UdcContestRules SerialRules() => Rules(("DefaultContestExchange", "001"));
}
