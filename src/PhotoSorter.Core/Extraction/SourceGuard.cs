namespace PhotoSorter.Core.Extraction;

/// <summary>
/// Move mode (ARCHITECTURE §4.9): decides whether an original may be removed from its source folder. Anything
/// doubtful is <b>kept</b> – it is still copied, so the only cost of a "no" is a file left behind.
/// Used by the single writer thread only (not thread-safe).
/// </summary>
public sealed class SourceGuard
{
    /// <summary>Folders of installed programs and games: their pictures are program parts, not photos.</summary>
    private static readonly HashSet<string> SoftwareFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Files", "Program Files (x86)", "ProgramData", "WindowsApps", "AppData", "Application Data",
        "steamapps", "SteamLibrary", "Steam", "Epic Games", "GOG Galaxy", "GOG Games", "Origin Games", "EA Games",
        "Ubisoft Game Launcher", "Riot Games", "Battle.net", "XboxGames", "node_modules", "site-packages",
        "lib", "libs", "res", "resources", "assets", "textures", "sprites", "icons", "skins", "plugins", "mods",
    };

    /// <summary>How many folders up (within the source folder) to look for program files.</summary>
    private const int ProgramFileLevels = 3;

    private readonly IReadOnlyList<string> _roots;
    private readonly IReadOnlyList<string> _cloudRoots;
    private readonly Dictionary<string, bool> _hasProgramFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _cloudDrive = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="roots">The normalized source folders.</param>
    /// <param name="cloudRoots">Synced cloud folders; default: the OneDrive folders Windows knows about.</param>
    public SourceGuard(IReadOnlyList<string> roots, IReadOnlyList<string>? cloudRoots = null)
    {
        _roots = roots;
        _cloudRoots = cloudRoots ?? OneDriveRoots();
    }

    /// <summary>Null when the file may be removed; otherwise why it stays.</summary>
    public string? KeepReason(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Can't read its attributes"; }

        if ((attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0)
            return "Read-only or system file";
        // Cloud placeholders (OneDrive, iCloud, Dropbox) are reparse points; deleting one deletes it in the cloud.
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 || IsInCloudFolder(path))
            return "Synced cloud folder – deleting here would delete it in the cloud too";

        var dir = Path.GetDirectoryName(path);
        if (dir is null) return "No folder";
        var root = _roots.FirstOrDefault(r => IsUnder(dir, r));
        var below = root is null ? dir : Path.GetRelativePath(root, dir);
        if (below.Split(Path.DirectorySeparatorChar).Any(SoftwareFolderNames.Contains))
            return "Looks like a program or game folder";

        var level = dir;
        for (var i = 0; i <= ProgramFileLevels && level is not null; i++)
        {
            if (HasProgramFiles(level)) return "Next to program files (.dll)";
            if (root is null || string.Equals(level, root, StringComparison.OrdinalIgnoreCase)) break;
            level = Path.GetDirectoryName(level);
        }
        return null;
    }

    private bool IsInCloudFolder(string path)
    {
        if (_cloudRoots.Any(r => IsUnder(path, r))) return true;

        // Google Drive for desktop shows up as its own drive.
        var drive = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(drive) || drive.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (!_cloudDrive.TryGetValue(drive, out var cloud))
        {
            try
            {
                var info = new DriveInfo(drive);
                cloud = info.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase) ||
                        info.DriveFormat.Contains("GoogleDrive", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                cloud = false;
            }
            _cloudDrive[drive] = cloud;
        }
        return cloud;
    }

    private bool HasProgramFiles(string dir)
    {
        if (_hasProgramFiles.TryGetValue(dir, out var has)) return has;
        try { has = Directory.EnumerateFiles(dir, "*.dll").Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { has = false; }
        return _hasProgramFiles[dir] = has;
    }

    private static bool IsUnder(string path, string root)
    {
        var r = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(r, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> OneDriveRoots() =>
        new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .OfType<string>()
            .Where(p => p.Length > 3 && Path.IsPathFullyQualified(p))
            .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
