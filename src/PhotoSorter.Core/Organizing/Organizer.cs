using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using PhotoSorter.Core.Catalog;
using PhotoSorter.Core.Extraction;
using PhotoSorter.Core.Geo;
using PhotoSorter.Core.Logging;
using PhotoSorter.Core.Metadata;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Organizing;

public sealed record OrganizeOptions
{
    public required string Destination { get; init; }

    /// <summary>Use the file's modified date when nothing else is known. Off by default: backups reset it.</summary>
    public bool UseFileDatesAsLastResort { get; init; }

    /// <summary><c>Continent\Country\Year</c> instead of <c>Continent\Year</c>. Switching later just moves files.</summary>
    public bool CountryFolders { get; init; }

    /// <summary>With country folders: Swedish photos get a county level, <c>Europe\Sweden\Skåne län\Year</c>.</summary>
    public bool SwedishCountyFolders { get; init; }

    public int Parallelism { get; init; } = 4;
}

public enum OrganizePhase
{
    ReadingMetadata,
    Sorting,
    Moving,
    Finished,
}

public sealed record OrganizeProgress(OrganizePhase Phase, int Done, int Total, int Errors, string? CurrentPath);

/// <param name="BaseName">File name (without extension) to use – the best name among all copies of the file.</param>
public sealed record PlannedMove(long MediaId, string FromRelative, string ToDirectoryRelative, string BaseName);

/// <summary>One destination folder in the preview, e.g. Europe\2015 – 1 204 files.</summary>
/// <param name="Country">Country folder, when country folders are on and the location is known.</param>
/// <param name="Region">Region folder (Swedish län), when that option is on and the photo is from Sweden.</param>
public sealed record BucketCount(string Location, string? Country, string? Region, string Year, int Count, long Bytes);

public sealed record OrganizePlan(
    string Destination,
    IReadOnlyList<PlannedMove> Moves,
    IReadOnlyList<BucketCount> Buckets,
    int TotalFiles,
    int AlreadyInPlace,
    int Missing);

public sealed record OrganizeResult(RunOutcome Outcome, int Moved, int Errors, string? LogPath, string? ErrorMessage);

/// <summary>
/// The destination folders (ARCHITECTURE §5.1): <c>Continent\Year</c>, or <c>Continent\Country\Year</c>
/// with country folders on (<c>Europe\Sweden\Skåne län\Year</c> with Swedish counties on), plus
/// <c>_Unknown location\Year</c> and the <c>_Unknown year</c> fallbacks.
/// </summary>
public static class FolderLayout
{
    public const string UnknownLocation = "_Unknown location";
    public const string UnknownYear = "_Unknown year";

    public static string LocationFolder(string? continent) => continent ?? UnknownLocation;
    public static string YearFolder(int? year) => year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? UnknownYear;

    /// <summary>The country folder, or null when there is no country level (option off, or location unknown).</summary>
    public static string? CountryFolder(string? continent, string? country, bool countryFolders) =>
        countryFolders && continent is not null && !string.IsNullOrWhiteSpace(country) ? NameResolver.Sanitize(country) : null;

    /// <summary>The region folder, only below a country folder.</summary>
    public static string? RegionFolder(string? countryFolder, string? region) =>
        countryFolder is not null && !string.IsNullOrWhiteSpace(region) ? NameResolver.Sanitize(region) : null;

    public static string RelativeDirectory(string? continent, int? year) => RelativeDirectory(continent, null, null, year, false);

    public static string RelativeDirectory(string? continent, string? country, string? region, int? year, bool countryFolders)
    {
        var countryFolder = CountryFolder(continent, country, countryFolders);
        string?[] levels = [LocationFolder(continent), countryFolder, RegionFolder(countryFolder, region), YearFolder(year)];
        return Path.Combine([.. levels.OfType<string>()]);
    }
}

/// <summary>
/// Phase 2 – Organize (ARCHITECTURE §5): read metadata, resolve continent + year, preview, then move the
/// extracted copies into <c>Continent\Year\</c> inside the destination. Sources are never touched.
/// </summary>
public sealed class Organizer(ContinentLocator? locator = null)
{
    private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(100);
    private readonly ContinentLocator _locator = locator ?? ContinentLocator.Default;

    /// <summary>Works out where every file should go. Caches metadata in the catalog; moves nothing.</summary>
    public async Task<OrganizePlan> AnalyzeAsync(OrganizeOptions options, IProgress<OrganizeProgress>? progress = null, CancellationToken ct = default)
    {
        var layout = new DestinationLayout(options.Destination);
        if (Directory.Exists(layout.Root)) layout.MigrateLegacyAppFolder();
        if (!CatalogDb.Exists(layout.CatalogPath))
            throw new InvalidOperationException("No catalog in this destination yet – run Extract first.");

        using var db = CatalogDb.Open(layout.CatalogPath);
        var rows = db.LoadCopiedMedia();

        // 1. Read metadata for files not read yet (or read by an older version) – in parallel.
        var stale = rows.Where(r => r.MetaVersion < MetadataReader.Version).ToList();
        var fresh = new ConcurrentDictionary<long, MediaMetadata>();
        var done = 0;
        var clock = Stopwatch.StartNew();
        await Parallel.ForEachAsync(stale,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(options.Parallelism, 1, 16), CancellationToken = ct },
            (row, _) =>
            {
                var full = layout.ToFull(row.DestPath);
                if (File.Exists(full)) fresh[row.Id] = MetadataReader.Read(full);
                var n = Interlocked.Increment(ref done);
                if (n % 25 == 0) progress?.Report(new(OrganizePhase.ReadingMetadata, n, stale.Count, 0, full));
                return ValueTask.CompletedTask;
            });

        using (var tx = db.BeginTransaction())
        {
            foreach (var (id, m) in fresh)
                db.SaveMetadata(id, MetadataReader.Version, m.DateTaken, m.DateSource, m.Latitude, m.Longitude, m.CameraMake, m.CameraModel);
            tx.Commit();
        }

        // 2. Decide continent + year for every file.
        progress?.Report(new(OrganizePhase.Sorting, 0, rows.Count, 0, null));
        var sources = db.LoadSources();
        var now = DateTime.Now;
        var moves = new List<PlannedMove>();
        var buckets = new Dictionary<(string Location, string? Country, string? Region, string Year), (int Count, long Bytes)>();
        int inPlace = 0, missing = 0;

        using (var tx = db.BeginTransaction())
        {
            foreach (var row0 in rows)
            {
                ct.ThrowIfCancellationRequested();
                var row = fresh.TryGetValue(row0.Id, out var m)
                    ? row0 with { DateTaken = m.DateTaken, DateSource = m.DateSource, Latitude = m.Latitude, Longitude = m.Longitude }
                    : row0;

                if (!File.Exists(layout.ToFull(row.DestPath)))
                {
                    missing++;
                    continue;
                }

                var src = sources[row.Id].ToList();
                // A file can lose all its source paths (its source was edited and re-extracted as new content);
                // then its own current path is the best name/folder evidence left.
                string[] paths = src.Count > 0 ? [.. src.Select(s => s.Path)] : [row.DestPath];
                var year = DateResolver.Resolve(row.DateTaken, row.DateSource, paths,
                    src.Select(s => s.MtimeUtc), options.UseFileDatesAsLastResort, now);
                var geo = row is { Latitude: { } lat, Longitude: { } lon } ? _locator.Locate(lat, lon) : null;
                db.SaveResolution(row.Id, year.Year, year.Source, geo?.CountryCode, geo?.Continent);

                var region = options.SwedishCountyFolders && geo?.CountryCode == "SE" ? geo.Region : null;
                var country = FolderLayout.CountryFolder(geo?.Continent, geo?.CountryName, options.CountryFolders);
                var key = (FolderLayout.LocationFolder(geo?.Continent), country, FolderLayout.RegionFolder(country, region),
                    FolderLayout.YearFolder(year.Year));
                buckets[key] = buckets.TryGetValue(key, out var b) ? (b.Count + 1, b.Bytes + row.Size) : (1, row.Size);

                var target = FolderLayout.RelativeDirectory(geo?.Continent, geo?.CountryName, region, year.Year, options.CountryFolders);
                var current = Path.GetDirectoryName(row.DestPath) ?? "";
                if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
                {
                    inPlace++;
                }
                else
                {
                    // Moving anyway, so take the best name among all copies ("Nokia 6.1" beats "FILE0043").
                    var baseName = NameResolver.PreferredBaseName(paths.Select(Path.GetFileNameWithoutExtension).OfType<string>());
                    moves.Add(new PlannedMove(row.Id, row.DestPath, target, baseName));
                }

                if (clock.Elapsed > ReportEvery)
                {
                    clock.Restart();
                    progress?.Report(new(OrganizePhase.Sorting, inPlace + moves.Count + missing, rows.Count, 0, row.DestPath));
                }
            }
            tx.Commit();
        }

        var bucketList = buckets
            .Select(kv => new BucketCount(kv.Key.Location, kv.Key.Country, kv.Key.Region, kv.Key.Year, kv.Value.Count, kv.Value.Bytes))
            .OrderBy(x => x.Location, StringComparer.Ordinal)
            .ThenBy(x => x.Country, StringComparer.Ordinal)
            .ThenBy(x => x.Region, StringComparer.Ordinal)
            .ThenBy(x => x.Year, StringComparer.Ordinal)
            .ToList();
        progress?.Report(new(OrganizePhase.Finished, rows.Count, rows.Count, 0, null));
        return new OrganizePlan(layout.Root, moves, bucketList, rows.Count, inPlace, missing);
    }

    /// <summary>Moves files according to the plan (same drive → instant renames, no extra space).</summary>
    public async Task<OrganizeResult> ApplyAsync(OrganizePlan plan, IProgress<OrganizeProgress>? progress = null, CancellationToken ct = default)
    {
        var layout = new DestinationLayout(plan.Destination);
        using var db = CatalogDb.Open(layout.CatalogPath);
        using var log = CsvRunLog.Create(layout.NewLogPath("organize"));
        var runId = db.StartRun("organize", JsonSerializer.Serialize(new { plan.Moves.Count }));

        var names = new Dictionary<string, NameResolver>(StringComparer.OrdinalIgnoreCase);
        int moved = 0, errors = 0;
        var outcome = RunOutcome.Completed;
        string? errorMessage = null;
        var clock = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < plan.Moves.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var move = plan.Moves[i];
                    var from = layout.ToFull(move.FromRelative);
                    try
                    {
                        if (!File.Exists(from))
                        {
                            errors++;
                            log.Write("Error", destPath: move.FromRelative, message: "File no longer exists");
                            continue;
                        }

                        var targetDir = layout.ToFull(move.ToDirectoryRelative);
                        System.IO.Directory.CreateDirectory(targetDir);
                        if (!names.TryGetValue(targetDir, out var resolver))
                            names[targetDir] = resolver = NameResolver.ForDirectory(targetDir);
                        var name = resolver.Reserve(move.BaseName, Path.GetExtension(from));
                        var to = Path.Combine(targetDir, name);

                        File.Move(from, to);
                        var relative = layout.ToRelative(to);
                        try
                        {
                            db.UpdateDestPath(move.MediaId, relative);
                        }
                        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
                        {
                            // The catalog must keep pointing at the file: put it back where it was.
                            File.Move(to, from);
                            throw new IOException("Could not update the catalog, file left in place: " + ex.Message, ex);
                        }
                        moved++;
                        log.Write("Moved", move.FromRelative, relative);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors++;
                        log.Write("Error", destPath: move.FromRelative, message: ex.Message);
                    }

                    if (clock.Elapsed > ReportEvery)
                    {
                        clock.Restart();
                        progress?.Report(new(OrganizePhase.Moving, i + 1, plan.Moves.Count, errors, move.FromRelative));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                outcome = RunOutcome.Cancelled;
            }
            catch (Exception ex)
            {
                outcome = RunOutcome.Failed;
                errorMessage = ex.Message;
                log.Write("Error", message: "Organize stopped: " + ex.Message);
            }
        }, CancellationToken.None);

        db.FinishRun(runId, outcome, JsonSerializer.Serialize(new { moved, errors }));
        progress?.Report(new(OrganizePhase.Finished, moved, plan.Moves.Count, errors, null));
        return new OrganizeResult(outcome, moved, errors, log.Path, errorMessage);
    }
}
