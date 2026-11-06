using System.Collections.Immutable;
using System.Text;

namespace HamContest.Udc;

public enum DiagnosticSeverity { Warning, Error }
public sealed record ImportDiagnostic(DiagnosticSeverity Severity, string Code, string Message, int Line = 0, string Key = "");
public sealed record UdcEntry(string Section, string Key, string Value, int Line);

/// <summary>Case-insensitive INI syntax. Comments occupy a whole line; semicolons inside values are retained.</summary>
public sealed record UdcDocument(ImmutableArray<UdcEntry> Entries, ImmutableArray<ImportDiagnostic> Diagnostics)
{
    public static UdcDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = ImmutableArray.CreateBuilder<UdcEntry>();
        var diagnostics = ImmutableArray.CreateBuilder<ImportDiagnostic>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        string section = "";
        int number = 0;
        while (reader.ReadLine() is { } source)
        {
            number++;
            var line = source.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            int equal = line.IndexOf('=');
            if (equal <= 0 || section.Length == 0)
            {
                diagnostics.Add(new(DiagnosticSeverity.Error, "Syntax", "Expected [section] followed by key=value.", number));
                continue;
            }
            string key = line[..equal].Trim(), value = line[(equal + 1)..].Trim();
            if (!seen.Add(section + "/" + key)) diagnostics.Add(new(DiagnosticSeverity.Error, "DuplicateKey", $"Duplicate [{section}] {key}.", number, key));
            entries.Add(new(section, key, value, number));
        }
        return new(entries.ToImmutable(), diagnostics.ToImmutable());
    }

    /// <summary>UTF-8 (strict) or BOM-marked UTF-16/32. For other encodings, decode explicitly before Parse.</summary>
    public static UdcDocument Read(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 1024, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }
}
