namespace PhotoSorter.Core;

/// <summary>Well-known paths inside the destination folder.</summary>
public sealed class DestinationLayout(string destination)
{
    public const string ExtractedFolderName = "Extracted";
    public const string SuspectFolderName = "_suspect";
    public const string AppFolderName = "_Mormorskopiadammsugare";

    /// <summary>App folder name used by builds from before the rename (working title "PhotoSorter").</summary>
    public const string LegacyAppFolderName = "_PhotoSorter";

    public string Root { get; } = Path.GetFullPath(destination);
    public string Extracted => Path.Combine(Root, ExtractedFolderName);
    public string Suspect => Path.Combine(Extracted, SuspectFolderName);
    public string AppFolder => Path.Combine(Root, AppFolderName);
    public string CatalogPath => Path.Combine(AppFolder, "catalog.db");
    public string LogFolder => Path.Combine(AppFolder, "logs");

    /// <summary>
    /// A destination used by an older build keeps its catalog in <c>_PhotoSorter\</c>. Renames it, so the
    /// catalog is found again instead of everything being copied a second time.
    /// </summary>
    public void MigrateLegacyAppFolder()
    {
        var legacy = Path.Combine(Root, LegacyAppFolderName);
        if (Directory.Exists(legacy) && !Directory.Exists(AppFolder)) Directory.Move(legacy, AppFolder);
    }

    public string NewLogPath(string kind) =>
        Path.Combine(LogFolder, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

    public string ToRelative(string fullPath) => Path.GetRelativePath(Root, fullPath);
    public string ToFull(string relativePath) => Path.Combine(Root, relativePath);
}
