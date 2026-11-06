using HamContest.Udc;

namespace HamContest.Tests;

public class ExtendedUdcTests
{
    [Fact]
    public void WrtStyleLogCountsUniqueCallsignsAcrossBands()
    {
        // Original compact test definition, following the organizer's published WRT rules.
        var rules = Fixtures.Rules(("Name", "WRT"), ("PointsPerContact", "1"), ("DefaultContestExchange", "Name State"),
            ("NumMults", "1"), ("IsMultPer", "4"), ("MultSqlString", "CallSign"), ("QsoErrorString", "Any_not_Empty"),
            ("CabrilloFormat", "99"), ("CabrilloString", "SentExchPart1,10,SentExchPart2,10,CallSign,13,Name,10,Exchange1,4"));
        var session = new ContestSession(rules, Fixtures.Own);
        foreach (var qso in new[] { Fixtures.Contact("K1ABC"), Fixtures.Contact("K1ABC", 7_080_000), Fixtures.Contact("K2ABC"), Fixtures.Contact("K2ABC") })
            session.Log(qso with { Sent = new() { Value = "EUGEN YO" }, Received = new() { Name = "TIM", Value = "MA" } });
        var score = session.Score();
        Assert.Equal(3, score.QsoPoints); Assert.Equal(2, score.Multipliers); Assert.Equal(6, score.Total);
        Assert.True(score.Contacts[^1].IsDupe);
        string output = CabrilloExporter.Export(session);
        Assert.Contains("EUGEN      YO         K1ABC", output);
        Assert.DoesNotContain("599", output);
    }

    [Fact]
    public void FirstQsoMultiplierCreditsOnlyFirstContactPerBand()
    {
        var rules = Fixtures.Rules(("NumMults", "1"), ("IsMultPer", "1"), ("MultSqlString", "FirstQSO"));
        var score = rules.Score([Fixtures.Contact("DL1A"), Fixtures.Contact("DL2A"), Fixtures.Contact("DL3A", 7_080_000)], Fixtures.Own);
        Assert.Equal(2, score.Multipliers); Assert.Equal(12, score.Total);
    }

    [Fact]
    public void CountryFiltersMatchExactCtyIdentityAndNotCallsignPrefix()
    {
        var rules = Fixtures.Rules(("NumMults", "1"), ("IsMultPer", "4"), ("MultSqlString", "CallSign"), ("CountMultOnlyFor", "G"));
        var a = Fixtures.Contact("G1AAA") with { Station = new("G1AAA") { CountryPrefix = "G", Continent = "EU" } };
        var b = Fixtures.Contact("GJ1AAA") with { Station = new("GJ1AAA") { CountryPrefix = "GJ", Continent = "EU" } };
        var score = rules.Score([a, b], Fixtures.Own);
        Assert.Equal(4, score.QsoPoints); Assert.Equal(1, score.Multipliers); Assert.Equal(4, score.Total);
        Assert.Empty(score.Contacts[1].NewMultipliers);
        Assert.NotEmpty(rules.Validate(a with { Station = a.Station with { CountryPrefix = "" } }, Fixtures.Own));
    }

    [Fact]
    public void ExclusionsAndIndependentGroupsDoNotSuppressQsoPoints()
    {
        var rules = Fixtures.Rules(("NumMults", "2"), ("IsMultPer", "4"), ("MultSqlString", "CountryPrefix"),
            ("DoNotCountMultOnlyFor", "EU"), ("MultSqlString2", "CallSign"), ("CountMultOnlyFor2", "DL"));
        var score = rules.Score([Fixtures.Contact()], Fixtures.Own);
        Assert.Equal(2, score.QsoPoints); Assert.Equal(1, score.Multipliers);
        Assert.Equal(2, score.Contacts[0].NewMultipliers[0].Group);
    }

    [Fact]
    public void SectionFiltersRequireSecAndDistinguishValidExcludedFromUnknownSections()
    {
        string text = Fixtures.Definition(("NumMults", "1"), ("IsMultPer", "4"), ("MultSqlString", "CallSign"),
            ("MultWindowType", "DEMO"), ("CountMultOnlyForSec", "BU"));
        Assert.False(UdcImporter.Import(text).CanScore);
        var rules = UdcImporter.Import(text, [SectionList.Parse("Type=DEMO SubType=\nBU\nCJ")]).RequireRules();
        var qso = Fixtures.Contact(); // CJ is valid but not an eligible multiplier.
        Assert.Empty(rules.Validate(qso, Fixtures.Own));
        Assert.Equal(0, rules.Score([qso], Fixtures.Own).Multipliers);
        Assert.NotEmpty(rules.Validate(qso with { Received = qso.Received with { Section = "BAD" } }, Fixtures.Own));
        Assert.Equal(1, rules.Score([qso with { Received = qso.Received with { Section = "BU" } }], Fixtures.Own).Multipliers);
    }

    [Theory]
    [InlineData("MyCountryOnly", "YO", "EU", true)]
    [InlineData("MyCountryOnly", "DL", "EU", false)]
    [InlineData("ExceptMyCountry", "YO", "EU", false)]
    [InlineData("MyContinentOnly", "K", "NA", false)]
    [InlineData("MyContinentOnly", "DL", "EU", true)]
    [InlineData("EUonly", "DL", "EU", true)]
    [InlineData("ASIAonly", "JA", "AS", true)]
    [InlineData("NAonly", "K", "NA", true)]
    [InlineData("G,GJ", "GJ", "EU", true)]
    [InlineData("G,GJ", "GM", "EU", false)]
    public void WorkableRestrictionsAreExplicitValidationErrors(string restriction, string country, string continent, bool eligible)
    {
        var rules = Fixtures.Rules(("IsWorkable", restriction));
        var qso = Fixtures.Contact() with { Station = new("DL1ABC") { CountryPrefix = country, Continent = continent } };
        Assert.Equal(eligible, rules.Validate(qso, Fixtures.Own).IsEmpty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void AdditionalDupeFieldAllowsDifferentSectionExchangeOrGrid(int clause)
    {
        var rules = Fixtures.Rules(("DupeSqlString", clause.ToString()));
        var a = Fixtures.Contact();
        var b = a with { Id = Guid.NewGuid(), Received = a.Received with { Section = "BU", Value = "15", Grid = "KN34AA" } };
        var c = a with { Id = Guid.NewGuid() };
        var score = rules.Score([a, b, c], Fixtures.Own);
        Assert.False(score.Contacts[1].IsDupe); Assert.True(score.Contacts[2].IsDupe);
        Assert.Equal(4, score.Total);
    }

    [Fact]
    public void AdditionalModeClauseAndMissingKeyAreHandled()
    {
        var rules = Fixtures.Rules(("Mode", "BOTH"), ("DupeSqlString", "3"));
        Assert.False(rules.Score([Fixtures.Contact(), Fixtures.Contact() with { Mode = ContactMode.CW }], Fixtures.Own).Contacts[1].IsDupe);
        var sectionRules = Fixtures.Rules(("DupeSqlString", "1"));
        Assert.NotEmpty(sectionRules.Validate(Fixtures.Contact() with { Received = new() }, Fixtures.Own));
    }

    [Fact]
    public void DisplayOnlyMultipliersAreVisibleWithoutInflatingScore()
    {
        var rules = Fixtures.Rules(("NumMults", "2"), ("IsMultPer", "4"), ("MultSqlString", "CountryPrefix"),
            ("MultMult", "0"), ("MultSqlString2", "CQZONE"));
        var score = rules.Score([Fixtures.Contact()], Fixtures.Own);
        Assert.Equal(2, score.Multipliers); Assert.Equal(1, score.ScoringMultipliers); Assert.Equal(2, score.Total);
        Assert.False(score.Contacts[0].NewMultipliers.Single(m => m.Group == 1).CountsForScore);
        var displayOnly = Fixtures.Rules(("NumMults", "1"), ("IsMultPer", "4"), ("MultSqlString", "CountryPrefix"), ("MultMult", "0"));
        Assert.Equal(2, displayOnly.Score([Fixtures.Contact()], Fixtures.Own).Total);
    }

    [Fact]
    public void BandFactorsAreAppliedAfterBaseGeographicPointsAndNeverToDupes()
    {
        var rules = Fixtures.Rules(("PointsPerContact", "SM,5,MyCountry,1,SameContinent,2,OtherContinent,3"),
            ("PointsMultByBand", "1.8,2,3.5,2,7,2"));
        var a = Fixtures.Contact("SM7ABC", 7_080_000) with { Station = new("SM7ABC") { CountryPrefix = "SM", Continent = "EU" } };
        var score = rules.Score([a, a with { Id = Guid.NewGuid() }, a with { Id = Guid.NewGuid(), FrequencyHz = 14_080_000 }], Fixtures.Own);
        Assert.Equal(new[] { 10, 0, 5 }, score.Contacts.Select(q => q.Points));
        Assert.Equal(15, score.Total);
    }

    [Fact]
    public void WaeDefinitionsDemandWaeIdentitiesInsteadOfSilentlyUsingDxcc()
    {
        var rules = Fixtures.Rules(("UsesWAECountries", "True"), ("NumMults", "1"), ("IsMultPer", "4"), ("MultSqlString", "CountryPrefix"));
        Assert.Equal(CountryIdentityKind.Wae, rules.RequiredCountryIdentityKind);
        Assert.Contains(rules.Validate(Fixtures.Contact(), Fixtures.Own), issue => issue.Field == "CountryIdentityKind");
        var own = Fixtures.Own with { CountryIdentityKind = CountryIdentityKind.Wae };
        var qso = Fixtures.Contact() with { Station = Fixtures.Contact().Station with { CountryIdentityKind = CountryIdentityKind.Wae } };
        Assert.Empty(rules.Validate(qso, own));
    }

    [Theory]
    [InlineData("PointsMultByBand", "7,2,7.0,3")]
    [InlineData("PointsMultByBand", "50,2")]
    [InlineData("PointsMultByBand", "7,-2")]
    [InlineData("CountMultOnlyFor", "G,,GM")]
    [InlineData("MultMult", "1*")]
    [InlineData("ResetMultsEverySession", "1")]
    [InlineData("IsWorkable", "UnknownRestriction")]
    public void AmbiguousAndUnsupportedExtensionsRemainBlocked(string key, string value)
    {
        Assert.False(UdcImporter.Import(Fixtures.Definition((key, value))).CanScore);
    }
}
