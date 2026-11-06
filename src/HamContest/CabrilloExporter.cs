using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HamContest.Udc;

namespace HamContest;

/// <summary>Explicit UDC format-99 QSO layouts. Never truncates an exchange to make it fit.</summary>
public static class CabrilloExporter
{
    public static string Export(ContestSession session, IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Rules is not UdcContestRules rules || !rules.CanExportCabrillo)
            throw new NotSupportedException("This contest has no supported CabrilloFormat=99 template.");
        var state = session.CaptureState();
        var score = rules.Score(state.Contacts, state.OwnStation);
        var lines = new List<string>
        {
            "START-OF-LOG: 3.0", "CREATED-BY: HamContest 0.1", "CONTEST: " + rules.CabrilloName,
            "CALLSIGN: " + state.OwnStation.Callsign, "CLAIMED-SCORE: " + score.Total.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var pair in headers ?? new Dictionary<string, string>())
        {
            string key = Values.Key(pair.Key);
            if (!Regex.IsMatch(key, "^[A-Z][A-Z0-9-]*$") || key is "START-OF-LOG" or "END-OF-LOG" or "CREATED-BY" or "CONTEST" or "CALLSIGN" or "CLAIMED-SCORE" or "QSO" or "X-QSO")
                throw new ArgumentException($"Invalid or reserved Cabrillo header '{key}'.", nameof(headers));
            lines.Add(key + ": " + pair.Value);
        }
        foreach (var contact in state.Contacts.OrderBy(q => q.Time))
        {
            string mode = contact.Mode switch
            {
                ContactMode.CW => "CW", ContactMode.SSB => "PH", ContactMode.RTTY => "RY",
                _ => throw new NotSupportedException($"Cabrillo mode mapping for {contact.Mode} is not supported yet.")
            };
            var line = new StringBuilder("QSO: ");
            line.Append(((contact.FrequencyHz + 500) / 1000).ToString(CultureInfo.InvariantCulture).PadLeft(5));
            line.Append(' ').Append(mode).Append(' ').Append(contact.Time.UtcDateTime.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture));
            line.Append(' ').Append(Fit(state.OwnStation.Callsign, 13, "own callsign"));
            string sent = string.Join(" ", new[] { contact.Sent.Serial?.ToString("D3", CultureInfo.InvariantCulture), contact.Sent.Value }.Where(s => !string.IsNullOrEmpty(s)));
            string[] sentParts = sent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var field in rules.CabrilloFields)
            {
                if (field.Name == "Space") { line.Append(' ', field.Width); continue; }
                string value = field.Name switch
                {
                    "SNT" => contact.Sent.Report, "SentNr" => contact.Sent.Serial?.ToString("D3", CultureInfo.InvariantCulture) ?? "",
                    "CallSign" => Values.Key(contact.Station.Callsign), "RCV" => contact.Received.Report,
                    "RcvNr" => contact.Received.Serial?.ToString("D3", CultureInfo.InvariantCulture) ?? "",
                    "Comment" => contact.Received.Comment, "GridSquare" => Values.Key(contact.Received.Grid),
                    "Exchange1" => contact.Received.Value, "Name" => contact.Received.Name, "SentExch" => sent,
                    "SentExchPart1" => sentParts.ElementAtOrDefault(0) ?? "", "SentExchPart2" => sentParts.ElementAtOrDefault(1) ?? "",
                    "SentExchPart3" => sentParts.ElementAtOrDefault(2) ?? "", _ => throw new NotSupportedException(field.Name)
                };
                line.Append(' ').Append(Fit(value, field.Width, field.Name));
            }
            lines.Add(line.ToString().TrimEnd());
        }
        lines.Add("END-OF-LOG:");
        foreach (string line in lines)
            if (line.Any(c => c < 32 || c > 126)) throw new ArgumentException("Cabrillo requires printable ASCII, without embedded newlines. Transliterate names/headers in the host if needed.");
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string Fit(string value, int width, string field)
    {
        if (width == 0) return value;
        if (value.Length > width) throw new InvalidOperationException($"{field} is {value.Length} characters but template allows {width}; export would lose data.");
        return value.PadRight(width);
    }
}
