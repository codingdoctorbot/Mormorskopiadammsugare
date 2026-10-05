namespace PhotoSorter.Core.Models;

/// <summary>Options for one extraction run (ARCHITECTURE §4).</summary>
public sealed record ScanOptions
{
    public required IReadOnlyList<string> Sources { get; init; }
    public required string Destination { get; init; }

    public bool IncludePhotos { get; init; } = true;
    public bool IncludeVideos { get; init; } = true;

    /// <summary>Files smaller than this are skipped (icons, web-cache crumbs, thumbnails). 0 = no limit.</summary>
    public long MinSizeBytes { get; init; } = 10 * 1024;

    /// <summary>Skip AppData, browser caches and thumbnail folders.</summary>
    public bool SkipAppCaches { get; init; } = true;

    /// <summary>Re-hash every copied file and compare.</summary>
    public bool VerifyCopies { get; init; }

    /// <summary>Scan, classify and hash, but copy nothing and leave the catalog unchanged.</summary>
    public bool DryRun { get; init; }

    /// <summary>Copy files whose extension says photo/video but whose content doesn't match to Extracted\_suspect.</summary>
    public bool CopySuspect { get; init; }

    /// <summary>Parallel file readers. 4 suits SSDs; use 1 for HDD/USB sources.</summary>
    public int Parallelism { get; init; } = 4;

    public IReadOnlyList<string> ExcludedDirectoryNames { get; init; } = DefaultExcludedDirectoryNames;
    public IReadOnlyList<string> AppCacheDirectoryNames { get; init; } = DefaultAppCacheDirectoryNames;

    public static readonly IReadOnlyList<string> DefaultExcludedDirectoryNames =
    [
        "$RECYCLE.BIN", "System Volume Information", "Windows", "Program Files", "Program Files (x86)",
        "node_modules", ".git",
    ];

    public static readonly IReadOnlyList<string> DefaultAppCacheDirectoryNames =
    [
        "AppData", "Temporary Internet Files", "INetCache", "Cache", "Caches", "thumbnails", ".thumbnails",
    ];
}
