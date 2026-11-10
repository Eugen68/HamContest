using System.Text;
using HamContest.Udc;

namespace HamContest.Tests;

public class ImportTests
{
    [Fact]
    public void CaseInsensitiveBomCommentsAndEqualsInValueArePreserved()
    {
        string text = "\uFEFF;comment\r\n[author]\r\nAuthorName=Example\r\n[contest]\r\nname=TESTRTTY\r\nmode=rtty\r\nNumMults=0\r\nDefaultContestExchange=DX\r\nSpecialInstructions=Keep a=b; c\r\n";
        var imported = UdcImporter.Import(text);
        Assert.True(imported.CanScore);
        Assert.Equal("Keep a=b; c", imported.Document.Entries.Single(e => e.Key == "SpecialInstructions").Value);
    }

    [Theory]
    [InlineData("BonusPoints", "DL, +5")]
    [InlineData("UnknownRule", "True")]
    [InlineData("MultipleSessions", "0000/30")]
    [InlineData("DupeSqlString", "99")]
    [InlineData("QsoNumbersByBand", "1")]
    [InlineData("PointsPerContact", "Exchange, *")]
    [InlineData("PointsPerContact", "20m, 3, RTTY, 5")]
    [InlineData("SetSentTimeForContact", "True")]
    [InlineData("NumMults", "banana")]
    [InlineData("Name", "VHFTEST")]
    public void UnsupportedOrInvalidRulesBlockScoring(string key, string value)
    {
        var imported = UdcImporter.Import(Fixtures.Definition((key, value)));
        Assert.False(imported.CanScore);
        Assert.Contains(imported.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Key == key && d.Line > 0);
        Assert.Throws<InvalidOperationException>(() => imported.RequireRules());
    }

    [Fact]
    public void EmptyUnknownParametersAreReportedButDoNotBlock()
    {
        var result = UdcImporter.Import(Fixtures.Definition(("FutureFeature", "")));
        Assert.True(result.CanScore);
        Assert.Contains(result.Diagnostics, d => d.Key == "FutureFeature" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("[Contest]\nName=A\nname=B")]
    [InlineData("Name=A")]
    [InlineData("[Contest]\nName=A\nbroken line")]
    public void MalformedOrDuplicateIniCannotCreateRules(string text) => Assert.False(UdcImporter.Import(text).CanScore);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamReaderDetectsUtfBomAndLeavesStreamOpen(bool utf16)
    {
        var encoding = utf16 ? (Encoding)Encoding.Unicode : new UTF8Encoding(true);
        using var stream = new MemoryStream(encoding.GetPreamble().Concat(encoding.GetBytes(Fixtures.Definition())).ToArray());
        var document = UdcDocument.Read(stream);
        Assert.Empty(document.Diagnostics);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void SecHeaderAndRequiredSectionsAreValidated()
    {
        var sections = SectionList.Parse("; example\r\nType=DEMO  SubType=\r\nBU\r\nCJ\r\nDX\r\n");
        Assert.True(sections.Contains("cj"));
        string udc = Fixtures.Definition(("NumMults", "1"), ("IsMultPer", "1"), ("MultSqlString", "Section"), ("MultWindowType", "DEMO"), ("QsoErrorString", "DEMO"));
        Assert.False(UdcImporter.Import(udc).CanScore);
        var rules = UdcImporter.Import(udc, [sections]).RequireRules();
        Assert.Empty(rules.Validate(Fixtures.Contact() with { Received = new() { Value = "CJ" } }, Fixtures.Own));
        Assert.NotEmpty(rules.Validate(Fixtures.Contact() with { Received = new() { Value = "BAD" } }, Fixtures.Own));
        Assert.Throws<FormatException>(() => SectionList.Parse("Type=DEMO SubType=\nBU\nbu"));
        Assert.Throws<FormatException>(() => SectionList.Parse("Type=DEMO SubType=Other\nBU"));
        Assert.Throws<FormatException>(() => SectionList.Parse("Type=DEMO SubType=\nBU,NotSupported"));
    }

    [Fact]
    public void FingerprintChangesWithRuleOrSecChangesButNotIniLineEndings()
    {
        string text = Fixtures.Definition();
        Assert.Equal(UdcImporter.Import(text).RequireRules().DefinitionId, UdcImporter.Import(text.Replace("\n", "\r\n")).RequireRules().DefinitionId);
        Assert.NotEqual(Fixtures.Rules().DefinitionId, Fixtures.Rules(("PointsPerContact", "3")).DefinitionId);
        Assert.NotEqual(SectionList.Parse("Type=X SubType=\nAA").Fingerprint, SectionList.Parse("Type=X SubType=\nBB").Fingerprint);
    }

    [Fact]
    public void MultipleFieldChecksRespectNumericRequiredAndGrid()
    {
        var rules = Fixtures.Rules(("QsoErrorString", "Exchange1Text_Numeric_not_Empty, MiscText_Any_not_Empty, GridSquareText_Grid"));
        var qso = Fixtures.Contact() with { Received = new() { Value = "12", Misc = "YO", Grid = "KN34" } };
        Assert.Empty(rules.Validate(qso, Fixtures.Own));
        var invalid = rules.Validate(qso with { Received = qso.Received with { Value = "", Misc = "", Grid = "XX00" } }, Fixtures.Own);
        Assert.Equal(3, invalid.Length);
    }
}
