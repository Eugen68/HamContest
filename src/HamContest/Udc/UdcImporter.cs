using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HamContest.Udc;

public sealed record UdcImportResult(UdcDocument Document, ImmutableArray<ImportDiagnostic> Diagnostics, UdcContestRules? Rules)
{
    public bool CanScore => Rules != null;
    public UdcContestRules RequireRules() => Rules ?? throw new InvalidOperationException(
        "UDC cannot be used: " + string.Join("; ", Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
}

internal sealed record PointRule(string Selector, int Points, int Priority);
internal sealed record MultiplierRule(int Group, string Selector, MultiplierScope Scope, bool ExcludeOwn,
    ImmutableArray<string> IncludeCountries, ImmutableArray<string> ExcludeCountriesOrContinents,
    ImmutableArray<string> IncludeSections, bool CountsForScore);
internal sealed record ExchangeCheck(string Field, string Kind, bool Required, string? SectionType = null);
public sealed record CabrilloField(string Name, int Width);

/// <summary>Imports the documented subset. Nonempty unknown rule parameters block creation of a scoring engine.</summary>
public static class UdcImporter
{
    public static UdcImportResult Import(string text, IEnumerable<SectionList>? sectionLists = null)
    {
        var document = UdcDocument.Parse(text);
        var diagnostics = document.Diagnostics.ToBuilder();
        var lists = (sectionLists ?? []).ToImmutableArray();
        if (lists.Select(s => Values.Key(s.Type)).Distinct().Count() != lists.Length)
            diagnostics.Add(new(DiagnosticSeverity.Error, "DuplicateSections", "More than one SEC list has the same Type."));
        var contest = document.Entries.Where(e => e.Section.Equals("Contest", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Get(string key, string fallback = "")
        {
            consumed.Add(key);
            return contest.TryGetValue(key, out var entry) && entry.Value.Length != 0 ? entry.Value : fallback;
        }
        void Error(string key, string message)
        {
            diagnostics.Add(new(DiagnosticSeverity.Error, "UnsupportedOrInvalid", message, contest.GetValueOrDefault(key)?.Line ?? 0, key));
        }
        int Number(string key, int fallback, int min, int max)
        {
            string value = Get(key, fallback.ToString(CultureInfo.InvariantCulture));
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max) return number;
            Error(key, $"{key}: expected integer {min}..{max}; found '{value}'."); return fallback;
        }
        bool Boolean(string key, bool fallback = false)
        {
            string value = Get(key, fallback.ToString());
            if (bool.TryParse(value, out bool result)) return result;
            Error(key, $"{key}: expected True or False."); return fallback;
        }
        ImmutableArray<string> Filter(string key)
        {
            string value = Get(key);
            if (value.Length == 0) return [];
            var values = Split(value).Select(Values.Key).ToImmutableArray();
            if (values.Any(v => !Regex.IsMatch(v, "^[A-Z0-9_-]{1,15}$"))) Error(key, $"{key}: expected a comma-separated list of exact identifiers.");
            return values;
        }
        string name = Get("Name");
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,10}$")) Error("Name", "Name must contain 1..10 ASCII letters/digits/underscores/hyphens.");
        if (name.StartsWith("VHF", StringComparison.OrdinalIgnoreCase)) Error("Name", "VHF distance scoring is not supported in this version.");
        string displayName = Get("DisplayName", name), cabrilloName = Get("CabrilloName", name);
        string mode = Values.Key(Get("Mode", "CW"));
        if (mode is not ("CW" or "SSB" or "BOTH" or "RTTY")) Error("Mode", $"Unsupported Mode '{mode}'.");
        var dupe = (DupeScope)Number("DupeType", 2, 1, 4);
        string exchange = Get("DefaultContestExchange", "001");
        if (exchange.Contains("Last", StringComparison.OrdinalIgnoreCase)) Error("DefaultContestExchange", "Dynamic last-callsign exchange is not supported.");
        bool serials = exchange.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == "001";
        string points = Get("PointsPerContact", "1");
        var pointRules = ImmutableArray.CreateBuilder<PointRule>();
        int defaultPoints = 1;
        if (int.TryParse(points, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int constant)) defaultPoints = constant;
        else
        {
            var tokens = Split(points);
            if (tokens.Length % 2 != 0) Error("PointsPerContact", "PointsPerContact requires selector, points pairs.");
            for (int i = 0; i + 1 < tokens.Length; i += 2)
            {
                string selector = Values.Key(tokens[i]);
                int priority = Bands.IsHfContestBand(tokens[i].ToLowerInvariant()) ? 4
                    : selector is "CW" or "SSB" or "RTTY" or "PSK" or "DIGI" or "FM" or "AM" ? 3
                    : selector is "MYCOUNTRY" or "SAMECONTINENT" or "OTHERCONTINENT" or "EU" or "AS" or "AF" or "NA" or "SA" or "OC" or "AN" ? 2
                    : Regex.IsMatch(selector, "^[A-Z]{1,3}[0-9]?$") ? 1 : 0;
                if (priority == 0 || !int.TryParse(tokens[i + 1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                    Error("PointsPerContact", $"Unsupported points clause '{tokens[i]}, {tokens[i + 1]}'.");
                else pointRules.Add(new(selector, value, priority));
            }
            // Mixed band/mode/geography precedence needs independent N1MM comparison before enabling it.
            if (pointRules.Select(p => p.Priority >= 3 ? p.Priority : 2).Distinct().Count() > 1)
                Error("PointsPerContact", "Mixed band/mode/geography points tables are not supported yet.");
        }
        var bandFactors = ImmutableDictionary.CreateBuilder<string, int>();
        string bandFactorText = Get("PointsMultByBand");
        if (bandFactorText.Length != 0)
        {
            string[] tokens = Split(bandFactorText);
            if (tokens.Length % 2 != 0) Error("PointsMultByBand", "Expected band MHz, integer factor pairs.");
            for (int i = 0; i + 1 < tokens.Length; i += 2)
            {
                string? band = decimal.TryParse(tokens[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal mhz)
                    ? mhz switch { 1.8m => "160m", 3.5m => "80m", 7m => "40m", 14m => "20m", 21m => "15m", 28m => "10m", _ => null } : null;
                if (band == null || !int.TryParse(tokens[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int factor) || factor < 0)
                    Error("PointsMultByBand", $"Unsupported band/factor '{tokens[i]}, {tokens[i + 1]}'.");
                else if (!bandFactors.TryAdd(band, factor)) Error("PointsMultByBand", $"Repeated factor for {band}.");
            }
        }
        int numMults = Number("NumMults", 1, 0, 3);
        var globalScope = (MultiplierScope)Number("IsMultPer", 0, 0, 4);
        var multipliers = ImmutableArray.CreateBuilder<MultiplierRule>();
        string sectionType = Get("MultWindowType", "Section");
        for (int group = 1; group <= 3; group++)
        {
            string suffix = group == 1 ? "" : group.ToString(CultureInfo.InvariantCulture);
            string label = Get($"Multiplier{group}Name", "N/A");
            string selector = Values.Key(Get("MultSqlString" + suffix, "N/A"));
            var scope = (MultiplierScope)Number($"IsMult{group}Per", (int)globalScope, 0, 4);
            bool exclude = Boolean("DoNotCountMeAsMult" + suffix);
            var includeCountries = Filter("CountMultOnlyFor" + suffix);
            var excludeCountries = Filter("DoNotCountMultOnlyFor" + suffix);
            var includeSections = Filter("CountMultOnlyForSec" + suffix);
            bool countsForScore = Number("MultMult" + suffix, 1, 0, 1) == 1;
            if (group > numMults || scope == MultiplierScope.None) continue;
            if (selector == "N/A")
            {
                if (!label.Equals("N/A", StringComparison.OrdinalIgnoreCase)) Error("MultSqlString" + suffix, "Multiplier selector is required; it cannot be inferred from a display label.");
                continue;
            }
            if (selector is not ("COUNTRYPREFIX" or "WPXPREFIX" or "SECTION" or "CQZONE" or "IARUZONE" or "EXCHANGE" or "MISC" or "GRID" or "SGRID" or "FIELD" or "CALLSIGN" or "FIRSTQSO"))
                Error("MultSqlString" + suffix, $"Unsupported multiplier selector '{selector}'.");
            else multipliers.Add(new(group, selector, scope, exclude, includeCountries, excludeCountries, includeSections, countsForScore));
            if (exclude && selector is "EXCHANGE" or "MISC" or "FIRSTQSO") Error("DoNotCountMeAsMult" + suffix, "Own-exchange/FirstQSO exclusion is not supported.");
        }
        bool hasSections = multipliers.Any(m => m.Selector == "SECTION" || !m.IncludeSections.IsEmpty);
        var checks = ImmutableArray.CreateBuilder<ExchangeCheck>();
        string validation = Get("QsoErrorString");
        foreach (string clause in Split(validation).Where(s => s.Length != 0))
        {
            string field = "Exchange1Text", kind = clause;
            foreach (string prefix in new[] { "Exchange1Text", "MiscText", "CommentText", "NameText", "RcvNrText", "GridSquareText", "SectText" })
                if (clause.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)) { field = prefix; kind = clause[(prefix.Length + 1)..]; break; }
            bool required = kind.EndsWith("_not_Empty", StringComparison.OrdinalIgnoreCase);
            if (required) kind = kind[..^10];
            bool optionalSection = kind.EndsWith("_or_Empty", StringComparison.OrdinalIgnoreCase);
            if (optionalSection) kind = kind[..^9];
            if (kind.Equals("Numeric", StringComparison.OrdinalIgnoreCase) || kind.Equals("Any", StringComparison.OrdinalIgnoreCase))
                checks.Add(new(field, Values.Key(kind), required));
            else if (kind.Equals("Grid", StringComparison.OrdinalIgnoreCase)) checks.Add(new(field, "GRID", true));
            else
            {
                checks.Add(new(field, "SECTION", !optionalSection, kind));
                if (!lists.Any(s => s.Type.Equals(kind, StringComparison.OrdinalIgnoreCase))) Error("QsoErrorString", $"Missing SEC list with Type={kind}.");
            }
        }
        if (hasSections && !lists.Any(s => s.Type.Equals(sectionType, StringComparison.OrdinalIgnoreCase))) Error("MultWindowType", $"Missing SEC list with Type={sectionType}.");
        if (lists.FirstOrDefault(s => s.Type.Equals(sectionType, StringComparison.OrdinalIgnoreCase)) is { } filterSections)
            foreach (var multiplier in multipliers)
                if (multiplier.IncludeSections.Any(s => !filterSections.Contains(s)))
                    Error("CountMultOnlyForSec" + (multiplier.Group == 1 ? "" : multiplier.Group.ToString(CultureInfo.InvariantCulture)), "Section filter contains a code absent from the supplied SEC list.");
        string entryWindow = Get("EntryWindowInfo");
        var entryFields = ParseFields(entryWindow, ["SNTText", "RCVText", "NameText", "CommentText", "SntNrText", "RcvNrText", "Exchange1Text", "GridSquareText", "MiscText", "SectText"],
            message => Error("EntryWindowInfo", message), 10000);
        int format = Number("CabrilloFormat", 1, 0, 99);
        string formatText = Get("CabrilloString");
        if (Get("CabrilloVersion", "3.0") != "3.0") diagnostics.Add(new(DiagnosticSeverity.Warning, "CabrilloUnsupported", "Only Cabrillo 3.0 is supported."));
        var cabrillo = ParseFields(formatText, ["SNT", "SentNr", "CallSign", "RCV", "RcvNr", "Comment", "GridSquare", "Exchange1", "Name", "SentExch", "SentExchPart1", "SentExchPart2", "SentExchPart3", "Space"],
            message => diagnostics.Add(new(DiagnosticSeverity.Warning, "CabrilloUnsupported", message, contest.GetValueOrDefault("CabrilloString")?.Line ?? 0, "CabrilloString")), 200);
        bool canExport = format == 99 && cabrillo.Length != 0 && !diagnostics.Any(d => d.Code == "CabrilloUnsupported");
        if (!canExport) diagnostics.Add(new(DiagnosticSeverity.Warning, "CabrilloUnsupported", "This version exports only explicit CabrilloFormat=99 templates; scoring is independent of export support."));

        // These fields are descriptive and exposed through Document, without affecting scoring.
        foreach (string key in new[] { "FrameText", "LogInfo", "ScoreSummaryMultNames", "SpecialInstructions", "WebAddress", "GenericPrintString", "GenericPrintStringHeader", "Period", "StartOfContest", "EndOfContest", "MinimumOffTime", "OverlayCategory" }) Get(key);
        // Recognized parameters whose neutral setting is supported. Active versions must not be ignored.
        bool waeCountries = Boolean("UsesWAECountries");
        foreach (string key in new[] { "CQZoneMultContest", "DigitalModeSqlString", "SetSentTimeForContact", "UsesLASTEXCHmacro", "SelfSpotAllowed" })
            if (Boolean(key)) Error(key, $"{key}=True is not supported yet.");
        if (Number("QsoNumbersByBand", 0, 0, 2) != 0) Error("QsoNumbersByBand", "Only a single serial sequence is supported.");
        int dupeClause = Number("DupeSqlString", 0, 0, 4);
        string workable = Values.Key(Get("IsWorkable", "Any"));
        if (workable is not ("ANY" or "MYCOUNTRYONLY" or "EXCEPTMYCOUNTRY" or "MYCONTINENTONLY" or "NAONLY" or "SAONLY" or "EUONLY" or "ASIAONLY" or "AFONLY" or "OCONLY"))
        {
            if (Split(workable).Any(s => !Regex.IsMatch(s, "^[A-Z]{1,3}[0-9]?$"))) Error("IsWorkable", "Unknown eligibility rule or invalid CTY country list.");
        }
        foreach (string key in new[] { "ResetMultsEverySession", "ResetSNEverySession" })
            if (Number(key, 0, 0, 1) != 0) Error(key, "Session resets are not supported yet.");
        if (Number("MultiplierBands", 1, 1, 4) != 1) Error("MultiplierBands", "Only HF contest bands are supported.");
        Boolean("ShowMyCountryStations", true); Boolean("ShowWarcBands");
        string zoneType = Get("ZoneType", "CQ");
        if (Values.Key(zoneType) is not ("CQ" or "IARU")) Error("ZoneType", "ZoneType must be CQ or IARU.");
        foreach (var entry in document.Entries)
        {
            bool metadata = entry.Section.Equals("Author", StringComparison.OrdinalIgnoreCase) || entry.Section.Equals("File", StringComparison.OrdinalIgnoreCase);
            if (metadata) continue;
            if (entry.Section.Equals("Contest", StringComparison.OrdinalIgnoreCase) && consumed.Contains(entry.Key)) continue;
            diagnostics.Add(new(entry.Value.Length == 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                "UnknownParameter", $"Unsupported [{entry.Section}] {entry.Key}='{entry.Value}'.", entry.Line, entry.Key));
        }
        UdcContestRules? rules = null;
        if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            string canonical = string.Join("\n", document.Entries.OrderBy(e => e.Section, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .Select(e => Values.Key(e.Section) + "/" + Values.Key(e.Key) + "=" + e.Value));
            canonical += "\n" + string.Join("\n", lists.OrderBy(s => s.Type, StringComparer.OrdinalIgnoreCase).Select(s => s.Fingerprint));
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("HamContest-UDC-v2\n" + canonical)));
            rules = new(name, displayName, cabrilloName, fingerprint, mode, dupe, serials, exchange, defaultPoints,
                pointRules.ToImmutable(), multipliers.ToImmutable(), checks.ToImmutable(), lists, sectionType, entryFields, canExport ? cabrillo : [], dupeClause, workable,
                bandFactors.ToImmutable(), waeCountries ? CountryIdentityKind.Wae : CountryIdentityKind.Dxcc);
        }
        return new(document, diagnostics.ToImmutable(), rules);
    }

    internal static string[] Split(string value) => value.Split(',').Select(s => s.Trim()).ToArray();
    private static ImmutableArray<CabrilloField> ParseFields(string value, string[] supported, Action<string> error, int maxWidth)
    {
        if (value.Length == 0) return [];
        string[] tokens = Split(value);
        var fields = ImmutableArray.CreateBuilder<CabrilloField>();
        if (tokens.Length % 2 != 0) error("Expected field, width pairs.");
        for (int i = 0; i + 1 < tokens.Length; i += 2)
        {
            string? name = supported.FirstOrDefault(s => s.Equals(tokens[i], StringComparison.OrdinalIgnoreCase));
            if (name == null || !int.TryParse(tokens[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int width) || width < 0 || width > maxWidth)
                error($"Unsupported field/width '{tokens[i]}, {tokens[i + 1]}'.");
            else fields.Add(new(name, width));
        }
        return fields.ToImmutable();
    }
}
