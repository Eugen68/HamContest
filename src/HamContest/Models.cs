using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace HamContest;

public enum ContactMode { CW, SSB, RTTY, PSK, FM, AM }
public enum DupeScope { Contest = 1, Band = 2, BandAndMode = 3, None = 4 }
public enum MultiplierScope { None, Band, Mode, BandAndMode, Contest }

/// <summary>Country and prefix identifiers are supplied by the host, e.g. from HamCallsign.</summary>
public sealed record StationInfo(string Callsign)
{
    public string CountryPrefix { get; init; } = "";
    public string Continent { get; init; } = "";
    public int? CqZone { get; init; }
    public int? ItuZone { get; init; }
    public string WpxPrefix { get; init; } = "";
    public string Section { get; init; } = "";
    public string Grid { get; init; } = "";
}

/// <summary>Explicit exchange fields; the library does not guess their meaning from decoded text.</summary>
public sealed record Exchange
{
    public string Report { get; init; } = "599";
    public int? Serial { get; init; }
    public string Value { get; init; } = "";
    public string Misc { get; init; } = "";
    public string Comment { get; init; } = "";
    public string Section { get; init; } = "";
    public string Grid { get; init; } = "";
    public string Name { get; init; } = "";
}

public sealed record Qso
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Time { get; init; }
    public long FrequencyHz { get; init; }
    public ContactMode Mode { get; init; } = ContactMode.RTTY;
    public StationInfo Station { get; init; } = new("");
    public Exchange Sent { get; init; } = new();
    public Exchange Received { get; init; } = new();
    public string Band => Bands.FromFrequency(FrequencyHz) ?? "";
}

public static class Bands
{
    private static readonly (long Low, long High, string Name)[] Ranges =
    [
        (1_800_000, 2_000_000, "160m"), (3_500_000, 4_000_000, "80m"),
        (5_000_000, 5_500_000, "60m"), (7_000_000, 7_300_000, "40m"),
        (10_100_000, 10_150_000, "30m"), (14_000_000, 14_350_000, "20m"),
        (18_068_000, 18_168_000, "17m"), (21_000_000, 21_450_000, "15m"),
        (24_890_000, 24_990_000, "12m"), (28_000_000, 29_700_000, "10m")
    ];
    public static string? FromFrequency(long hz) => Ranges.Where(b => hz >= b.Low && hz <= b.High).Select(b => b.Name).FirstOrDefault();
    public static bool IsHfContestBand(string band) => band is "160m" or "80m" or "40m" or "20m" or "15m" or "10m";
}

public sealed record ValidationIssue(string Field, string Message);
public sealed record MultiplierCredit(int Group, string Key);
public sealed record QsoEvaluation(Qso Qso, bool IsDupe, int Points, ImmutableArray<MultiplierCredit> NewMultipliers);
public sealed record ContestScore(long QsoPoints, int Multipliers, long Total, ImmutableArray<QsoEvaluation> Contacts);

/// <summary>Rules may come from UDC or a host implementation. DefinitionId must change when rules change.</summary>
public interface IContestRules
{
    string Name { get; }
    string DefinitionId { get; }
    bool UsesSerialNumbers { get; }
    ImmutableArray<ValidationIssue> Validate(Qso qso, StationInfo ownStation);
    ContestScore Score(IEnumerable<Qso> contacts, StationInfo ownStation);
}

internal static class Values
{
    internal static string Key(string value) => value.Trim().ToUpperInvariant();
    internal static bool IsCallsign(string value) => value.Length is >= 3 and <= 20
        && Regex.IsMatch(value, "^[A-Za-z0-9]+(?:/[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)
        && value.Any(char.IsAsciiLetter) && value.Any(char.IsAsciiDigit);
    internal static bool IsGrid(string value) => Regex.IsMatch(value, "^[A-Ra-r]{2}[0-9]{2}([A-Xa-x]{2})?$", RegexOptions.CultureInvariant);
    internal static void SingleLine(string value, string field)
    {
        if (value.Any(char.IsControl)) throw new ArgumentException($"{field} must be a single line without control characters.", field);
    }
}
