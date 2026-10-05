using System.Text;
using System.Text.RegularExpressions;

namespace PhotoSorter.Core.Extraction;

/// <summary>
/// Picks file names inside one folder: <c>name.ext</c>, then <c>name (2).ext</c>, <c>name (3).ext</c> …
/// Not thread-safe – used by the single writer only.
/// </summary>
public sealed partial class NameResolver
{
    private const int MaxBaseNameLength = 120;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly HashSet<string> _taken;

    public NameResolver(IEnumerable<string> existingFileNames)
    {
        _taken = new HashSet<string>(existingFileNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Starts from the files already in <paramref name="directory"/> (if it exists).</summary>
    public static NameResolver ForDirectory(string directory) =>
        new(Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>()
            : []);

    public bool IsTaken(string fileName) => _taken.Contains(fileName);

    public void Add(string fileName) => _taken.Add(fileName);

    /// <summary>Candidate names in order of preference; the caller decides which one to use.</summary>
    public static IEnumerable<string> Candidates(string baseName, string extension)
    {
        var safe = Sanitize(baseName);
        yield return safe + extension;
        for (var i = 2; ; i++) yield return $"{safe} ({i}){extension}";
    }

    /// <summary>First free name, which is then marked as taken.</summary>
    public string Reserve(string baseName, string extension)
    {
        var name = Candidates(baseName, extension).First(n => !IsTaken(n));
        Add(name);
        return name;
    }

    /// <summary>
    /// Picks the most meaningful name among the names a file had in different backups (without extensions):
    /// recovery-tool names (FILE0043, f12345678) and copy markers ("(1)", " - Copy", "(copy)", "kopia") lose.
    /// Ties keep the first name, i.e. the first copy seen.
    /// </summary>
    public static string PreferredBaseName(IEnumerable<string> baseNames)
    {
        string? best = null;
        var bestScore = int.MaxValue;
        foreach (var name in baseNames)
        {
            var score = (RecoveredName().IsMatch(name) ? 2 : 0) + (CopyMarker().IsMatch(name) ? 1 : 0);
            if (score < bestScore)
            {
                best = name;
                bestScore = score;
            }
        }
        return best ?? "unnamed";
    }

    [GeneratedRegex(@"^(?:file|f)\d+$|^\d{6,}(?:_[a-z])?$", RegexOptions.IgnoreCase)]
    private static partial Regex RecoveredName();

    [GeneratedRegex(@"(?:\s*\(\d+\)|\s*-\s*(?:copy|kopia)(?:\s*\(\d+\))?|\s*\((?:copy|kopia)\)|[\s_]copy(?:\s*\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex CopyMarker();

    /// <summary>Makes a base name (without extension) safe for any Windows folder.</summary>
    public static string Sanitize(string baseName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(baseName.Length);
        foreach (var ch in baseName) sb.Append(invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch);

        var result = sb.ToString().Trim().TrimEnd('.');
        if (result.Length > MaxBaseNameLength) result = result[..MaxBaseNameLength].TrimEnd();
        if (result.Length == 0) result = "unnamed";
        if (ReservedNames.Contains(result)) result = "_" + result;
        return result;
    }
}
