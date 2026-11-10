using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HamContest.Udc;

/// <summary>N1MM SEC subset: Type=... SubType= header and one section code per line.</summary>
public sealed class SectionList
{
    public string Type { get; }
    public ImmutableArray<string> Sections { get; }
    public string Fingerprint { get; }
    private readonly ImmutableHashSet<string> _values;
    private SectionList(string type, ImmutableArray<string> sections)
    {
        Type = type; Sections = sections; _values = sections.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Values.Key(type) + "\n" + string.Join("\n", sections.Order(StringComparer.Ordinal)))));
    }
    public bool Contains(string value) => _values.Contains(value.Trim());
    public string? Canonical(string value) => Contains(value) ? Values.Key(value) : null;

    public static SectionList Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string? type = null;
        var sections = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        int number = 0;
        while (reader.ReadLine() is { } source)
        {
            number++;
            string line = source.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (type == null)
            {
                var match = Regex.Match(line, @"^Type\s*=\s*([A-Za-z0-9_-]{1,9})\s+SubType\s*=\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) throw new FormatException($"SEC line {number}: expected Type=NAME SubType= (nonempty SubType is not yet supported).");
                type = Values.Key(match.Groups[1].Value); continue;
            }
            if (!Regex.IsMatch(line, "^[A-Za-z0-9_-]{1,15}$")) throw new FormatException($"SEC line {number}: expected one section code; this SEC layout is not supported.");
            string code = Values.Key(line);
            if (!seen.Add(code)) throw new FormatException($"SEC line {number}: duplicate section {code}.");
            sections.Add(code);
        }
        if (type == null || sections.Count == 0) throw new FormatException("SEC list must contain a Type header and at least one section.");
        return new(type, sections.ToImmutable());
    }
}
