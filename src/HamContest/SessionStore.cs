using System.Text.Json;
using System.Text.Json.Serialization;

namespace HamContest;

/// <summary>Versioned JSON snapshots. Save after each journal/serial change; a host must coordinate writers.</summary>
public static class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ContestSession session) => JsonSerializer.Serialize(session.CaptureState(), Options);
    public static ContestSession Deserialize(string json, IContestRules rules)
    {
        var state = JsonSerializer.Deserialize<SessionState>(json, Options) ?? throw new InvalidDataException("Missing session.");
        return ContestSession.Restore(rules, state);
    }

    /// <summary>Flushes a temporary file in the destination directory, then replaces the destination.</summary>
    public static void Save(string path, ContestSession session)
    {
        string target = Path.GetFullPath(path), folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(file, session.CaptureState(), Options);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ContestSession Load(string path, IContestRules rules) => Deserialize(File.ReadAllText(path), rules);
}
