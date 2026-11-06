using HamContest.Udc;

namespace HamContest.Tests;

public class ScoringTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void DupeScopeHonorsBand(int scope, bool duplicate)
    {
        var rules = Fixtures.Rules(("DupeType", scope.ToString()));
        var score = rules.Score([Fixtures.Contact("dl1abc"), Fixtures.Contact("DL1ABC", 7_080_000)], Fixtures.Own);
        Assert.Equal(duplicate, score.Contacts[1].IsDupe);
        Assert.Equal(duplicate ? 2 : 4, score.Total);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void DupeScopeHonorsMode(int scope, bool duplicate)
    {
        var rules = Fixtures.Rules(("DupeType", scope.ToString()), ("Mode", "BOTH"));
        Assert.Equal(duplicate, rules.Score([Fixtures.Contact(), Fixtures.Contact() with { Mode = ContactMode.CW }], Fixtures.Own).Contacts[1].IsDupe);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    public void MultipliersHaveIndependentScopeAndDupesGiveNoCredit(int scope, int expected)
    {
        var rules = Fixtures.Rules(("NumMults", "1"), ("IsMultPer", scope.ToString()), ("MultSqlString", "CountryPrefix"));
        var score = rules.Score([Fixtures.Contact("DL1A"), Fixtures.Contact("DL2A", 7_080_000), Fixtures.Contact("DL2A", 7_080_000)], Fixtures.Own);
        Assert.Equal(expected, score.Multipliers);
        Assert.Equal(4 * expected, score.Total);
        Assert.True(score.Contacts[^1].IsDupe);
        Assert.Empty(score.Contacts[^1].NewMultipliers);
    }

    [Fact]
    public void IndependentGroupsUseEnteredZoneAndExplicitCountryIdentity()
    {
        var rules = Fixtures.Rules(("NumMults", "2"), ("IsMultPer", "4"), ("MultSqlString", "CountryPrefix"), ("MultSqlString2", "CQZONE"));
        var qso = Fixtures.Contact() with { Received = new() { Value = "15" } };
        var score = rules.Score([qso], Fixtures.Own);
        Assert.Equal(2, score.Multipliers);
        Assert.Contains(score.Contacts[0].NewMultipliers, m => m.Group == 2 && m.Key == "15");
        Assert.NotEmpty(rules.Validate(qso with { Station = qso.Station with { CountryPrefix = "" } }, Fixtures.Own));
        Assert.NotEmpty(rules.Validate(qso with { Received = new() { Value = "99" } }, Fixtures.Own));
    }

    [Fact]
    public void GeographicPointsFollowDocumentedCountryExample()
    {
        var rules = Fixtures.Rules(("PointsPerContact", "OH,5,SM,5,MyCountry,1,SameContinent,2,OtherContinent,3"));
        var qso = Fixtures.Contact();
        int Points(string country, string continent) => rules.Score([qso with { Station = qso.Station with { CountryPrefix = country, Continent = continent } }], Fixtures.Own).Contacts[0].Points;
        Assert.Equal(5, Points("OH", "EU"));
        Assert.Equal(1, Points("YO", "EU"));
        Assert.Equal(2, Points("DL", "EU"));
        Assert.Equal(3, Points("K", "NA"));
    }

    [Theory]
    [InlineData("RTTY,4,CW,3", 4)]
    [InlineData("20m,5,40m,3", 5)]
    public void ModeOrBandPointTablesAreInterpreted(string points, int expected) =>
        Assert.Equal(expected, Fixtures.Rules(("PointsPerContact", points)).Score([Fixtures.Contact()], Fixtures.Own).QsoPoints);

    [Fact]
    public void MissingDataIsNotSilentlyScoredAndOwnMultiplierCanBeExcluded()
    {
        var rules = Fixtures.Rules(("NumMults", "1"), ("IsMultPer", "4"), ("MultSqlString", "CountryPrefix"), ("DoNotCountMeAsMult", "True"));
        var qso = Fixtures.Contact() with { Station = Fixtures.Own with { Callsign = "YO3ABC" } };
        Assert.Equal(0, rules.Score([qso], Fixtures.Own).Total);
        Assert.Throws<ArgumentException>(() => rules.Score([qso with { Station = qso.Station with { CountryPrefix = "" } }], Fixtures.Own));
    }
}
