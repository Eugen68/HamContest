using HamContest.Udc;

namespace HamContest.Tests;

public class ExportTests
{
    private static ContestSession Create(string template = "SNT,3,SentNr,3,CallSign,13,RCV,3,RcvNr,3,Exchange1,4")
    {
        var rules = Fixtures.Rules(("DefaultContestExchange", "001"), ("CabrilloFormat", "99"), ("CabrilloString", template));
        var session = new ContestSession(rules, Fixtures.Own);
        session.Log(Fixtures.Contact() with { Received = new() { Serial = 12, Value = "BU" } }, session.ReserveSerial());
        return session;
    }

    [Fact]
    public void CabrilloHasExplicitFixedWidthQsoAndUtc()
    {
        string output = CabrilloExporter.Export(Create(), new Dictionary<string, string> { ["CATEGORY-MODE"] = "RTTY" });
        Assert.StartsWith("START-OF-LOG: 3.0\r\n", output);
        Assert.Contains("CONTEST: TESTRTTY\r\n", output);
        Assert.Contains("CLAIMED-SCORE: 2\r\n", output);
        Assert.Contains("QSO: 14080 RY 2026-11-06 1200 YO9VA         599 001 DL1ABC        599 012 BU\r\n", output);
        Assert.EndsWith("END-OF-LOG:\r\n", output);
    }

    [Fact]
    public void ExportRejectsTruncationUnknownLayoutsAndHeaderInjection()
    {
        Assert.Throws<InvalidOperationException>(() => CabrilloExporter.Export(Create("CallSign,2")));
        Assert.Throws<ArgumentException>(() => CabrilloExporter.Export(Create(), new Dictionary<string, string> { ["NAME"] = "A\r\nQSO: injected" }));
        Assert.Throws<ArgumentException>(() => CabrilloExporter.Export(Create(), new Dictionary<string, string> { ["CONTEST"] = "Other" }));
        var imported = UdcImporter.Import(Fixtures.Definition(("CabrilloFormat", "99"), ("CabrilloString", "UnknownField, 4")));
        Assert.True(imported.CanScore);
        Assert.False(imported.RequireRules().CanExportCabrillo);
        Assert.Throws<NotSupportedException>(() => CabrilloExporter.Export(new(imported.RequireRules(), Fixtures.Own)));
    }

    [Fact]
    public void ExportIncludesDupesWithoutCountingTheirPoints()
    {
        var session = Create();
        session.Log(Fixtures.Contact(), session.ReserveSerial());
        string output = CabrilloExporter.Export(session);
        Assert.Equal(2, output.Split("\r\n").Count(line => line.StartsWith("QSO: ")));
        Assert.Contains("CLAIMED-SCORE: 2\r\n", output);
    }
}
