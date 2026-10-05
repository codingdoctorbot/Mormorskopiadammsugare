using System.IO;
using System.Text.Json;

namespace PhotoSorter.App.Services;

/// <summary>Everything the user picked last time.</summary>
public sealed class AppSettings
{
    public List<string> Sources { get; set; } = [];
    public string Destination { get; set; } = "";
    public bool IncludePhotos { get; set; } = true;
    public bool IncludeVideos { get; set; } = true;
    public bool SkipSmallFiles { get; set; } = true;
    public int MinSizeKb { get; set; } = 10;
    public bool SkipAppCaches { get; set; } = true;
    public bool VerifyCopies { get; set; }
    public bool CopySuspect { get; set; }
    public int Parallelism { get; set; } = 4;
    public bool OrganizeAfterExtract { get; set; } = true;
    public bool UseFileDatesAsLastResort { get; set; }
    public bool CountryFolders { get; set; }
    public bool SwedishCountyFolders { get; set; }
}

/// <summary>
/// Portable settings: <c>settings.json</c> next to Mormorskopiadammsugare.exe (ARCHITECTURE §7). Falls back to
/// %LOCALAPPDATA%\Mormorskopiadammsugare when the exe's folder is read-only (e.g. Program Files).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public SettingsService()
    {
        var exeDir = AppContext.BaseDirectory;
        Path = System.IO.Path.Combine(
            IsWritable(exeDir) ? exeDir : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mormorskopiadammsugare"),
            "settings.json");
    }

    public string Path { get; }

    public AppSettings Load()
    {
        // Builds from before the rename kept the fallback settings in %LOCALAPPDATA%\PhotoSorter.
        var legacy = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoSorter", "settings.json");
        var file = File.Exists(Path) ? Path : File.Exists(legacy) ? legacy : null;
        try
        {
            return file is null ? new() : JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(); // damaged or locked settings must never stop the app
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not worth bothering the user about
        }
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = System.IO.Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
