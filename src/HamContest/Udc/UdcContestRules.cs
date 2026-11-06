using System.Collections.Immutable;
using System.Globalization;

namespace HamContest.Udc;

public sealed class UdcContestRules : IContestRules
{
    public string Name { get; }
    public string DisplayName { get; }
    public string CabrilloName { get; }
    public string DefinitionId { get; }
    public bool UsesSerialNumbers { get; }
    public string DefaultSentExchange { get; }
    public DupeScope DupeScope { get; }
    public ImmutableArray<CabrilloField> EntryFields { get; }
    public ImmutableArray<CabrilloField> CabrilloFields { get; }
    public bool CanExportCabrillo => !CabrilloFields.IsEmpty;
    private readonly string _mode, _sectionType;
    private readonly int _defaultPoints;
    private readonly ImmutableArray<PointRule> _points;
    private readonly ImmutableArray<MultiplierRule> _multipliers;
    private readonly ImmutableArray<ExchangeCheck> _checks;
    private readonly ImmutableArray<SectionList> _sections;

    internal UdcContestRules(string name, string displayName, string cabrilloName, string fingerprint, string mode,
        DupeScope dupe, bool serials, string exchange, int defaultPoints, ImmutableArray<PointRule> points,
        ImmutableArray<MultiplierRule> multipliers, ImmutableArray<ExchangeCheck> checks, ImmutableArray<SectionList> sections,
        string sectionType, ImmutableArray<CabrilloField> entryFields, ImmutableArray<CabrilloField> cabrilloFields)
    {
        Name = name; DisplayName = displayName; CabrilloName = cabrilloName; DefinitionId = fingerprint;
        _mode = mode; DupeScope = dupe; UsesSerialNumbers = serials; DefaultSentExchange = exchange;
        _defaultPoints = defaultPoints; _points = points; _multipliers = multipliers; _checks = checks;
        _sections = sections; _sectionType = sectionType; EntryFields = entryFields; CabrilloFields = cabrilloFields;
    }

    public ImmutableArray<ValidationIssue> Validate(Qso qso, StationInfo ownStation)
    {
        ArgumentNullException.ThrowIfNull(qso); ArgumentNullException.ThrowIfNull(ownStation);
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        void Need(bool valid, string field, string message) { if (!valid) issues.Add(new(field, message)); }
        Need(qso.Id != Guid.Empty, "Id", "QSO identity cannot be empty.");
        Need(qso.Time != default, "Time", "QSO needs an explicit timestamp.");
        Need(Values.IsCallsign(ownStation.Callsign), "OwnCallsign", "Invalid own callsign.");
        Need(Values.IsCallsign(qso.Station.Callsign), "Callsign", "Invalid correspondent callsign.");
        Need(Bands.IsHfContestBand(qso.Band), "FrequencyHz", "Only the six HF contest bands are supported.");
        Need(Enum.IsDefined(qso.Mode), "Mode", "Unknown contact mode.");
        Need(_mode == "BOTH" || qso.Mode.ToString() == _mode, "Mode", $"Contest mode is {_mode}.");
        Need(qso.Sent.Serial is null or > 0 && qso.Received.Serial is null or > 0, "Serial", "Serial numbers must be positive.");
        Need(!UsesSerialNumbers || qso.Sent.Serial != null, "Sent.Serial", "A reserved serial number is required.");
        foreach (var exchange in new[] { qso.Sent, qso.Received })
        {
            foreach (string value in new[] { exchange.Report, exchange.Value, exchange.Misc, exchange.Comment, exchange.Section, exchange.Grid, exchange.Name })
                Need(!value.Any(char.IsControl), "Exchange", "Control characters are not permitted in exchange fields.");
        }
        foreach (var check in _checks)
        {
            string value = Field(qso.Received, check.Field);
            Need(value.Length <= (check.Field == "CommentText" ? 60 : check.Field == "NameText" ? 20 : check.Field == "MiscText" ? 15 : 12), check.Field, "Exchange is too long.");
            if (value.Length == 0) { Need(!check.Required, check.Field, "Exchange cannot be empty."); continue; }
            Need(check.Kind switch
            {
                "NUMERIC" => value.All(char.IsAsciiDigit),
                "ANY" => true,
                "GRID" => Values.IsGrid(value),
                "SECTION" => _sections.First(s => s.Type.Equals(check.SectionType, StringComparison.OrdinalIgnoreCase)).Contains(value),
                _ => false
            }, check.Field, $"Invalid {check.Kind.ToLowerInvariant()} exchange '{value}'.");
        }
        bool country = _points.Any(p => p.Priority == 1 || p.Selector == "MYCOUNTRY") || _multipliers.Any(m => m.Selector == "COUNTRYPREFIX");
        bool continent = _points.Any(p => p.Selector is "SAMECONTINENT" or "OTHERCONTINENT" or "EU" or "AS" or "AF" or "NA" or "SA" or "OC" or "AN");
        Need(!country || (ownStation.CountryPrefix.Length != 0 && qso.Station.CountryPrefix.Length != 0), "CountryPrefix", "Country identities are required for these rules; supply them from the host country database.");
        Need(!continent || (ValidContinent(ownStation.Continent) && ValidContinent(qso.Station.Continent)), "Continent", "Valid own/correspondent continents are required for these points rules.");
        foreach (var multiplier in _multipliers)
        {
            string key = MultiplierValue(multiplier.Selector, qso, ownStation, own: false);
            Need(key.Length != 0, "Multiplier" + multiplier.Group, $"Missing/invalid {multiplier.Selector} multiplier value.");
            if (multiplier.ExcludeOwn) Need(MultiplierValue(multiplier.Selector, qso, ownStation, own: true).Length != 0,
                "OwnMultiplier" + multiplier.Group, $"Missing own {multiplier.Selector} value for exclusion.");
        }
        return issues.ToImmutable();
    }

    private static bool ValidContinent(string value) => Values.Key(value) is "EU" or "AS" or "AF" or "NA" or "SA" or "OC" or "AN";
    internal static string Field(Exchange exchange, string name) => name.ToUpperInvariant() switch
    {
        "EXCHANGE1TEXT" => exchange.Value, "MISCTEXT" => exchange.Misc, "COMMENTTEXT" => exchange.Comment,
        "NAMETEXT" => exchange.Name, "RCVNRTEXT" => exchange.Serial?.ToString(CultureInfo.InvariantCulture) ?? "",
        "GRIDSQUARETEXT" => exchange.Grid, "SECTTEXT" => exchange.Section, _ => ""
    };

    public ContestScore Score(IEnumerable<Qso> contacts, StationInfo ownStation)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        var rows = ImmutableArray.CreateBuilder<QsoEvaluation>();
        var dupes = new HashSet<string>(StringComparer.Ordinal);
        var mults = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        long points = 0;
        // Equal timestamps retain insertion order. Recomputing after edits/removals promotes the next valid QSO.
        foreach (var qso in contacts.OrderBy(q => q.Time))
        {
            if (!ids.Add(qso.Id)) throw new ArgumentException("Duplicate QSO identity.", nameof(contacts));
            var issues = Validate(qso, ownStation);
            if (issues.Length != 0) throw new ArgumentException(string.Join("; ", issues.Select(i => i.Field + ": " + i.Message)), nameof(contacts));
            string dupeKey = Values.Key(qso.Station.Callsign) + (DupeScope == DupeScope.Contest ? "" : "|" + qso.Band)
                + (DupeScope == DupeScope.BandAndMode ? "|" + qso.Mode : "");
            bool duplicate = DupeScope != DupeScope.None && !dupes.Add(dupeKey);
            var credits = ImmutableArray.CreateBuilder<MultiplierCredit>();
            int qsoPoints = duplicate ? 0 : ContactPoints(qso, ownStation);
            if (!duplicate)
            {
                foreach (var rule in _multipliers)
                {
                    string value = MultiplierValue(rule.Selector, qso, ownStation, own: false);
                    if (rule.ExcludeOwn && value == MultiplierValue(rule.Selector, qso, ownStation, own: true)) continue;
                    string scope = rule.Scope switch
                    {
                        MultiplierScope.Band => qso.Band, MultiplierScope.Mode => qso.Mode.ToString(),
                        MultiplierScope.BandAndMode => qso.Band + "/" + qso.Mode, _ => ""
                    };
                    if (mults.Add(rule.Group + "|" + scope + "|" + value)) credits.Add(new(rule.Group, value));
                }
            }
            points = checked(points + qsoPoints);
            rows.Add(new(qso, duplicate, qsoPoints, credits.ToImmutable()));
        }
        return new(points, mults.Count, checked(points * (_multipliers.Length == 0 ? 1 : mults.Count)), rows.ToImmutable());
    }

    private int ContactPoints(Qso qso, StationInfo ownStation)
    {
        // Explicit countries precede broader geographic clauses, as in the documented OH/SM example.
        foreach (var rule in _points.OrderByDescending(p => p.Priority == 1 ? 5 : p.Priority))
        {
            bool matches = rule.Selector switch
            {
                "MYCOUNTRY" => Values.Key(qso.Station.CountryPrefix) == Values.Key(ownStation.CountryPrefix),
                "SAMECONTINENT" => Values.Key(qso.Station.Continent) == Values.Key(ownStation.Continent),
                "OTHERCONTINENT" => Values.Key(qso.Station.Continent) != Values.Key(ownStation.Continent),
                "DIGI" => qso.Mode is ContactMode.RTTY or ContactMode.PSK,
                _ when rule.Priority == 4 => Values.Key(qso.Band) == rule.Selector,
                _ when rule.Priority == 3 => qso.Mode.ToString() == rule.Selector,
                _ when rule.Priority == 2 => Values.Key(qso.Station.Continent) == rule.Selector,
                _ => Values.Key(qso.Station.CountryPrefix) == rule.Selector
            };
            if (matches) return rule.Points;
        }
        return _defaultPoints;
    }

    private string MultiplierValue(string selector, Qso qso, StationInfo ownStation, bool own)
    {
        var station = own ? ownStation : qso.Station;
        string grid = Values.Key(own ? station.Grid : qso.Received.Grid);
        string section = own ? station.Section : qso.Received.Section.Length == 0 ? qso.Received.Value : qso.Received.Section;
        int? Zone(string text, int? fallback, int max)
        {
            int? number = text.Length == 0 ? fallback : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
            return number is >= 1 && number <= max ? number : null;
        }
        return selector switch
        {
            "COUNTRYPREFIX" => Values.Key(station.CountryPrefix), "WPXPREFIX" => Values.Key(station.WpxPrefix),
            "CQZONE" => Zone(own ? "" : qso.Received.Value, station.CqZone, 40)?.ToString(CultureInfo.InvariantCulture) ?? "",
            "IARUZONE" => Zone(own ? "" : qso.Received.Misc, station.ItuZone, 90)?.ToString(CultureInfo.InvariantCulture) ?? "",
            "SECTION" => _sections.First(s => s.Type.Equals(_sectionType, StringComparison.OrdinalIgnoreCase)).Canonical(section) ?? "",
            "EXCHANGE" => own ? "" : Values.Key(qso.Received.Value), "MISC" => own ? "" : Values.Key(qso.Received.Misc),
            "GRID" => Values.IsGrid(grid) ? grid[..4] : "", "SGRID" => Values.IsGrid(grid) && grid.Length == 6 ? grid : "",
            "FIELD" => Values.IsGrid(grid) ? grid[..2] : "", _ => ""
        };
    }
}
