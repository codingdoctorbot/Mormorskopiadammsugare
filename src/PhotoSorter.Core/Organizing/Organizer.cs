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

    /// <summary>
    /// Best-effort sorting inside the unknown folders: a probable place from GPS photos taken at the same time
    /// (<c>~Sweden</c>), the original album folder, and screenshots/graphics/downloads set apart. See <see cref="BestGuess"/>.
    /// </summary>
    public bool BestGuessUnknowns { get; init; }

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
/// <param name="Note">Why it goes there, when that's a best guess (written to the log).</param>
public sealed record PlannedMove(long MediaId, string FromRelative, string ToDirectoryRelative, string BaseName, string? Note = null);

/// <summary>One destination folder in the preview, e.g. Europe\2015 – 1 204 files.</summary>
/// <param name="Directory">Relative folder, e.g. <c>Europe\Sweden\2015</c> or <c>_Unknown location\2018\~Sweden</c>.</param>
public sealed record BucketCount(string Directory, int Count, long Bytes)
{
    /// <summary>The folder's levels, top first.</summary>
    public string[] Levels => Directory.Split(Path.DirectorySeparatorChar);
}

/// <summary>How much best-guess sorting did (all 0 when it's off).</summary>
public sealed record GuessSummary(int PlaceGuessed, int InAlbums, int Screenshots, int Graphics, int Downloads)
{
    public static readonly GuessSummary None = new(0, 0, 0, 0, 0);
}

public sealed record OrganizePlan(
    string Destination,
    IReadOnlyList<PlannedMove> Moves,
    IReadOnlyList<BucketCount> Buckets,
    int TotalFiles,
    int AlreadyInPlace,
    int Missing,
    GuessSummary? Guesses = null);

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

        // 2. Year and GPS place for every file.
        progress?.Report(new(OrganizePhase.Sorting, 0, rows.Count, 0, null));
        var sources = db.LoadSources();
        var sourceRoots = db.LoadSourceRoots();
        var now = DateTime.Now;
        var resolved = new List<Resolved>(rows.Count);
        var missing = 0;
        foreach (var row0 in rows)
        {
            ct.ThrowIfCancellationRequested();
            var row = fresh.TryGetValue(row0.Id, out var m)
                ? row0 with { DateTaken = m.DateTaken, DateSource = m.DateSource, Latitude = m.Latitude, Longitude = m.Longitude, CameraMake = m.CameraMake }
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
            resolved.Add(new Resolved(row, paths, year, geo));
        }

        // 3. Target folder per file – with best guesses inside the unknown folders when enabled.
        var guesser = options.BestGuessUnknowns
            ? new BestGuess.PlaceGuesser(resolved
                .Where(r => r.Geo is not null && r.Row.DateTaken is not null && BestGuess.IsCaptureTime(r.Row.DateSource))
                .Select(r => new BestGuess.Anchor(r.Row.DateTaken!.Value, PlaceLabel(r.Geo!, options))))
            : null;
        var targets = resolved.Select(r => TargetFor(r, options, guesser, sourceRoots)).ToList();

        // An album folder only pays off when several files share it.
        var albumSizes = targets
            .Where(t => t.Album is not null)
            .GroupBy(t => (Dir: t.Dir.ToUpperInvariant(), Album: t.Album!.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.Count());

        // 4. Save, plan moves, count buckets.
        var moves = new List<PlannedMove>();
        var buckets = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        int inPlace = 0, placeGuessed = 0, inAlbums = 0, screenshots = 0, graphics = 0, downloads = 0;
        using (var tx = db.BeginTransaction())
        {
            foreach (var t0 in targets)
            {
                ct.ThrowIfCancellationRequested();
                var useAlbum = t0.Album is not null &&
                    albumSizes[(t0.Dir.ToUpperInvariant(), t0.Album.ToUpperInvariant())] >= BestGuess.MinAlbumSize;
                var t = useAlbum ? t0 with { Dir = Path.Combine(t0.Dir, NameResolver.Sanitize(t0.Album!)) } : t0 with { Album = null };
                var (row, geo, year) = (t.R.Row, t.R.Geo, t.R.Year);

                db.SaveResolution(row.Id, year.Year, year.Source, geo?.CountryCode, geo?.Continent);
                db.SaveGuess(row.Id, t.Place, t.Reason, t.Album, t.Category);
                if (t.Place is not null) placeGuessed++;
                if (t.Album is not null) inAlbums++;
                switch (t.Category)
                {
                    case BestGuess.Screenshots: screenshots++; break;
                    case BestGuess.Graphics: graphics++; break;
                    case BestGuess.Downloads: downloads++; break;
                }

                buckets[t.Dir] = buckets.TryGetValue(t.Dir, out var b) ? (b.Count + 1, b.Bytes + row.Size) : (1, row.Size);

                var current = Path.GetDirectoryName(row.DestPath) ?? "";
                if (string.Equals(current, t.Dir, StringComparison.OrdinalIgnoreCase))
                {
                    inPlace++;
                }
                else
                {
                    // Moving anyway, so take the best name among all copies ("Nokia 6.1" beats "FILE0043").
                    var baseName = NameResolver.PreferredBaseName(t.R.Paths.Select(Path.GetFileNameWithoutExtension).OfType<string>());
                    moves.Add(new PlannedMove(row.Id, row.DestPath, t.Dir, baseName, t.Reason));
                }
            }
            tx.Commit();
        }

        var bucketList = buckets
            .Select(kv => new BucketCount(kv.Key, kv.Value.Count, kv.Value.Bytes))
            .OrderBy(x => x.Directory, StringComparer.Ordinal)
            .ToList();
        progress?.Report(new(OrganizePhase.Finished, rows.Count, rows.Count, 0, null));
        return new OrganizePlan(layout.Root, moves, bucketList, rows.Count, inPlace, missing,
            new GuessSummary(placeGuessed, inAlbums, screenshots, graphics, downloads));
    }

    /// <summary>The label a place guess uses: the country with country folders on, else the continent.</summary>
    private static string PlaceLabel(GeoMatch geo, OrganizeOptions options) =>
        options.CountryFolders ? geo.CountryName : geo.Continent;

    /// <summary>Where one file goes, before the "enough files for an album" check.</summary>
    private static Target TargetFor(Resolved r, OrganizeOptions options, BestGuess.PlaceGuesser? guesser, IReadOnlyCollection<string> sourceRoots)
    {
        var region = options.SwedishCountyFolders && r.Geo?.CountryCode == "SE" ? r.Geo.Region : null;
        var dir = FolderLayout.RelativeDirectory(r.Geo?.Continent, r.Geo?.CountryName, region, r.Year.Year, options.CountryFolders);
        if (guesser is null) return new Target(r, dir, null, null, null, null);

        string? place = null, reason = null, category = null;
        if (r.Geo is null)
        {
            category = BestGuess.Category(r.Row.Kind, r.Row.Format, r.Row.CameraMake, r.Paths);
            if (category is not null)
            {
                dir = Path.Combine(FolderLayout.UnknownLocation, category, FolderLayout.YearFolder(r.Year.Year));
                reason = category.TrimStart('_');
            }
            else if (r.Row.DateTaken is { } taken && BestGuess.IsCaptureTime(r.Row.DateSource) && guesser.Guess(taken) is { } guess)
            {
                place = guess.Place;
                reason = $"Probably {guess.Place}: taken within {BestGuess.PlaceWindow.TotalHours:0} h of " +
                         $"{guess.Witnesses} GPS photo{(guess.Witnesses == 1 ? "" : "s")} from there";
                dir = Path.Combine(dir, BestGuess.GuessPrefix + NameResolver.Sanitize(guess.Place));
            }
        }
        // Album folders help wherever something is unknown (but not inside the non-photo buckets).
        var album = category is null && (r.Geo is null || r.Year.Year is null) ? BestGuess.AlbumFolder(r.Paths, sourceRoots) : null;
        return new Target(r, dir, place, reason, album, category);
    }

    private sealed record Resolved(MediaRow Row, string[] Paths, YearResolution Year, GeoMatch? Geo);

    private sealed record Target(Resolved R, string Dir, string? Place, string? Reason, string? Album, string? Category);

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
                        log.Write("Moved", move.FromRelative, relative, message: move.Note);
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
