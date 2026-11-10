using HamContest.Udc;

namespace HamContest.Tests;

/// <summary>UdcContestRules.Preview: a heard station before its exchange is known.</summary>
public class PreviewTests
{
    private static StationInfo Station(string call, string country = "DL", int zone = 14) =>
        new(call) { CountryPrefix = country, Continent = "EU", CqZone = zone, ItuZone = 28, WpxPrefix = call[..3] };

    [Fact]
    public void WorkedFollowsTheDupeScope()
    {
        var rules = Fixtures.Rules(("DupeType", "2"));   // per band
        Qso[] log = [Fixtures.Contact("DL1ABC")];
        Assert.True(rules.Preview(log, Fixtures.Own, Station("dl1abc"), 14_090_000, ContactMode.RTTY).IsWorked);
        Assert.False(rules.Preview(log, Fixtures.Own, Station("DL1ABC"), 7_040_000, ContactMode.RTTY).IsWorked);
        Assert.False(rules.Preview(log, Fixtures.Own, Station("DL2XYZ"), 14_090_000, ContactMode.RTTY).IsWorked);
        Assert.True(Fixtures.Rules(("DupeType", "1")).Preview(log, Fixtures.Own, Station("DL1ABC"), 7_040_000, ContactMode.RTTY).IsWorked);
        Assert.False(Fixtures.Rules(("DupeType", "4")).Preview(log, Fixtures.Own, Station("DL1ABC"), 14_090_000, ContactMode.RTTY).IsWorked);
    }

    [Fact]
    public void StationDerivedMultipliersAreReportedBeforeTheExchange()
    {
        var rules = Fixtures.Rules(("NumMults", "2"), ("IsMultPer", "1"), ("MultSqlString", "CountryPrefix"), ("MultSqlString2", "CQZONE"));
        Qso[] log = [Fixtures.Contact("DL1ABC")];   // DL, zona 14 (din schimb), pe 20 m
        // aceeasi tara si zona, alt indicativ: nimic nou
        Assert.Empty(rules.Preview(log, Fixtures.Own, Station("DL2XYZ"), 14_090_000, ContactMode.RTTY).NewMultipliers);
        // tara noua (zona tot 14)
        var f = rules.Preview(log, Fixtures.Own, Station("F5ABC", "F", 14), 14_090_000, ContactMode.RTTY);
        Assert.Equal(new[] { "F" }, f.NewMultipliers.Select(m => m.Key));
        // multiplicatori pe banda: pe 40 m si DL e nou
        Assert.Equal(2, rules.Preview(log, Fixtures.Own, Station("DL2XYZ"), 7_040_000, ContactMode.RTTY).NewMultipliers.Length);
        // lucrat deja: fara multiplicatori
        Assert.Empty(rules.Preview(log, Fixtures.Own, Station("DL1ABC"), 14_090_000, ContactMode.RTTY).NewMultipliers);
    }

    [Fact]
    public void ExchangeBasedMultipliersAndDupesAreNotGuessed()
    {
        var rules = Fixtures.Rules(("NumMults", "1"), ("IsMultPer", "4"), ("MultSqlString", "Exchange"));
        Assert.Empty(rules.Preview([], Fixtures.Own, Station("DL1ABC"), 14_080_000, ContactMode.RTTY).NewMultipliers);
        Assert.False(rules.Preview([], Fixtures.Own, Station("DL1ABC"), 14_080_000, ContactMode.RTTY).DupeNeedsExchange);
    }
}
