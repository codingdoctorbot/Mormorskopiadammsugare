using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Scanning;

namespace PhotoSorter.Core.Catalog;

public sealed record MediaRecord(long Id, string Sha256, string? DestPath);

/// <summary>Everything the organizer needs about one catalogued file.</summary>
public sealed record MediaRow(
    long Id,
    long Size,
    MediaKind Kind,
    string DestPath,
    int MetaVersion,
    DateTime? DateTaken,
    string? DateSource,
    double? Latitude,
    double? Longitude,
    MediaFormat Format,
    string? CameraMake);

public sealed record SourceRow(long MediaId, string Path, DateTime MtimeUtc);

/// <summary>How move mode removed a source file.</summary>
public enum SourceRemoval
{
    /// <summary>The file itself became the kept copy (moved or copied, then removed).</summary>
    Moved,

    /// <summary>A duplicate, deleted after the kept copy was checked.</summary>
    Deleted,
}

public sealed record RemovedSource(
    string Path, long Size, DateTime MtimeUtc, DateTime CreationUtc, SourceRemoval How, string Sha256, string? DestPath);

public enum CatalogStatus
{
    Copied,
    Suspect,
}

/// <summary>
/// <c>&lt;Destination&gt;\_Mormorskopiadammsugare\catalog.db</c> (ARCHITECTURE §4.6). One connection, used by one thread
/// at a time (the extraction writer or the organizer).
/// </summary>
public sealed class CatalogDb : IDisposable
{
    private const int SchemaVersion = 3;

    private readonly SqliteConnection _conn;

    private CatalogDb(SqliteConnection conn)
    {
        _conn = conn;
        Execute("PRAGMA foreign_keys = ON;");
    }

    public static CatalogDb Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, // release the file as soon as we're done
        }.ToString());
        conn.Open();
        var db = new CatalogDb(conn);
        db.Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");
        db.EnsureSchema();
        return db;
    }

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// For dry runs: an in-memory copy of the real catalog (if any), so nothing on disk changes but
    /// already-catalogued files are still recognised.
    /// </summary>
    public static CatalogDb OpenInMemoryCopy(string? path)
    {
        var mem = new SqliteConnection("Data Source=:memory:");
        mem.Open();
        if (path is not null && File.Exists(path))
        {
            using var disk = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            disk.Open();
            disk.BackupDatabase(mem);
        }
        var db = new CatalogDb(mem);
        db.EnsureSchema();
        return db;
    }

    private void EnsureSchema()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
            throw new InvalidOperationException(
                $"catalog.db was created by a newer PhotoSorter (schema {version}). Please update the app.");
        if (version == SchemaVersion) return;
        if (version > 0)
        {
            // Upgrade step by step from whatever version the catalog has.
            if (version < 2)
            {
                // v2: best-guess sorting inside the unknown folders records what it guessed and why.
                Execute("""
                    ALTER TABLE media ADD COLUMN guess_place  TEXT;
                    ALTER TABLE media ADD COLUMN guess_reason TEXT;
                    ALTER TABLE media ADD COLUMN album        TEXT;
                    ALTER TABLE media ADD COLUMN category     TEXT;
                    """);
            }
            if (version < 3)
            {
                // v3: move mode records which source files it removed, so "Restore originals" can put them back.
                Execute("""
                    ALTER TABLE sources ADD COLUMN removed_how TEXT;
                    ALTER TABLE sources ADD COLUMN removed_utc TEXT;
                    """);
            }
            Execute($"PRAGMA user_version = {SchemaVersion};");
            return;
        }

        Execute("""
            CREATE TABLE IF NOT EXISTS media (
              id             INTEGER PRIMARY KEY,
              sha256         TEXT NOT NULL UNIQUE,
              size           INTEGER NOT NULL,
              kind           TEXT NOT NULL,
              format         TEXT NOT NULL,
              dest_path      TEXT,
              status         TEXT NOT NULL,
              first_seen_utc TEXT NOT NULL,
              meta_version   INTEGER NOT NULL DEFAULT 0,
              date_taken     TEXT,
              date_source    TEXT,
              latitude       REAL,
              longitude      REAL,
              camera_make    TEXT,
              camera_model   TEXT,
              year           INTEGER,
              year_source    TEXT,
              country        TEXT,
              continent      TEXT,
              guess_place    TEXT,
              guess_reason   TEXT,
              album          TEXT,
              category       TEXT
            );
            CREATE TABLE IF NOT EXISTS sources (
              id          INTEGER PRIMARY KEY,
              media_id    INTEGER NOT NULL REFERENCES media(id),
              source_path TEXT NOT NULL UNIQUE COLLATE NOCASE,
              size        INTEGER NOT NULL,
              mtime_utc   TEXT NOT NULL,
              ctime_utc   TEXT NOT NULL,
              removed_how TEXT,
              removed_utc TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_sources_media ON sources(media_id);
            CREATE TABLE IF NOT EXISTS runs (
              id           INTEGER PRIMARY KEY,
              kind         TEXT NOT NULL,
              started_utc  TEXT NOT NULL,
              finished_utc TEXT,
              outcome      TEXT,
              options_json TEXT,
              stats_json   TEXT
            );
            """);
        Execute($"PRAGMA user_version = {SchemaVersion};");
    }

    // ---------- Extraction ----------

    /// <summary>All recorded source paths → (size, mtime), for the resume check (§4.6).</summary>
    public Dictionary<string, (long Size, DateTime MtimeUtc)> LoadSourceIndex()
    {
        var index = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = Command("SELECT source_path, size, mtime_utc FROM sources;");
        using var r = cmd.ExecuteReader();
        while (r.Read()) index[r.GetString(0)] = (r.GetInt64(1), ParseUtc(r.GetString(2)));
        return index;
    }

    public MediaRecord? FindByHash(string sha256)
    {
        using var cmd = Command("SELECT id, sha256, dest_path FROM media WHERE sha256 = $sha;", ("$sha", sha256));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new MediaRecord(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)) : null;
    }

    public long AddMedia(string sha256, long size, MediaKind kind, MediaFormat format, string destPath, CatalogStatus status)
    {
        using var cmd = Command("""
            INSERT INTO media (sha256, size, kind, format, dest_path, status, first_seen_utc)
            VALUES ($sha, $size, $kind, $format, $dest, $status, $now)
            RETURNING id;
            """,
            ("$sha", sha256), ("$size", size), ("$kind", kind.ToString()), ("$format", format.ToString()),
            ("$dest", destPath), ("$status", status.ToString()), ("$now", FormatUtc(DateTime.UtcNow)));
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Records (or re-points) a source path. A changed source file simply gets the new content's id.</summary>
    public void UpsertSource(long mediaId, FileCandidate source)
    {
        using var cmd = Command("""
            INSERT INTO sources (media_id, source_path, size, mtime_utc, ctime_utc)
            VALUES ($media, $path, $size, $mtime, $ctime)
            ON CONFLICT(source_path) DO UPDATE SET
              media_id = excluded.media_id, size = excluded.size,
              mtime_utc = excluded.mtime_utc, ctime_utc = excluded.ctime_utc,
              removed_how = NULL, removed_utc = NULL;
            """,
            ("$media", mediaId), ("$path", source.Path), ("$size", source.Size),
            ("$mtime", FormatUtc(source.LastWriteUtc)), ("$ctime", FormatUtc(source.CreationUtc)));
        cmd.ExecuteNonQuery();
    }

    // ---------- Move mode ----------

    /// <summary>Records that move mode removed this source file – written <b>before</b> the file is touched.</summary>
    public void MarkSourceRemoved(string sourcePath, SourceRemoval how)
    {
        using var cmd = Command(
            "UPDATE sources SET removed_how = $how, removed_utc = $now WHERE source_path = $path;",
            ("$how", how.ToString()), ("$now", FormatUtc(DateTime.UtcNow)), ("$path", sourcePath));
        cmd.ExecuteNonQuery();
    }

    /// <summary>The source is (back) in place: removing it failed, or it was restored.</summary>
    public void ClearSourceRemoved(string sourcePath)
    {
        using var cmd = Command(
            "UPDATE sources SET removed_how = NULL, removed_utc = NULL WHERE source_path = $path;", ("$path", sourcePath));
        cmd.ExecuteNonQuery();
    }

    public long CountRemovedSources() => Convert.ToInt64(
        Scalar("SELECT COUNT(*) FROM sources WHERE removed_how IS NOT NULL;"), CultureInfo.InvariantCulture);

    /// <summary>Every source file move mode removed, with the kept copy that holds its content.</summary>
    public List<RemovedSource> LoadRemovedSources()
    {
        var list = new List<RemovedSource>();
        using var cmd = Command("""
            SELECT s.source_path, s.size, s.mtime_utc, s.ctime_utc, s.removed_how, m.sha256, m.dest_path
            FROM sources s JOIN media m ON m.id = s.media_id
            WHERE s.removed_how IS NOT NULL
            ORDER BY s.source_path;
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new RemovedSource(r.GetString(0), r.GetInt64(1), ParseUtc(r.GetString(2)), ParseUtc(r.GetString(3)),
                Enum.TryParse<SourceRemoval>(r.GetString(4), out var how) ? how : SourceRemoval.Deleted,
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public long StartRun(string kind, string optionsJson)
    {
        using var cmd = Command(
            "INSERT INTO runs (kind, started_utc, options_json) VALUES ($kind, $now, $opts) RETURNING id;",
            ("$kind", kind), ("$now", FormatUtc(DateTime.UtcNow)), ("$opts", optionsJson));
        return (long)cmd.ExecuteScalar()!;
    }

    public void FinishRun(long runId, RunOutcome outcome, string statsJson)
    {
        using var cmd = Command(
            "UPDATE runs SET finished_utc = $now, outcome = $outcome, stats_json = $stats WHERE id = $id;",
            ("$now", FormatUtc(DateTime.UtcNow)), ("$outcome", outcome.ToString()), ("$stats", statsJson), ("$id", runId));
        cmd.ExecuteNonQuery();
    }

    // ---------- Organize ----------

    /// <summary>Every source folder any extract run used (from the runs' saved options).</summary>
    public List<string> LoadSourceRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = Command("SELECT options_json FROM runs WHERE kind LIKE 'extract%' AND options_json IS NOT NULL;");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(r.GetString(0));
                if (json.RootElement.TryGetProperty(nameof(ScanOptions.Sources), out var sources))
                    foreach (var s in sources.EnumerateArray())
                        if (s.GetString() is { Length: > 0 } path)
                            roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
            }
            catch (System.Text.Json.JsonException)
            {
                // an unreadable old entry only means fewer known roots
            }
        }
        return [.. roots];
    }

    public List<MediaRow> LoadCopiedMedia()
    {
        var rows = new List<MediaRow>();
        using var cmd = Command($"""
            SELECT id, size, kind, dest_path, meta_version, date_taken, date_source, latitude, longitude,
                   format, camera_make
            FROM media WHERE status = '{CatalogStatus.Copied}' AND dest_path IS NOT NULL ORDER BY id;
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new MediaRow(
                r.GetInt64(0),
                r.GetInt64(1),
                Enum.Parse<MediaKind>(r.GetString(2)),
                r.GetString(3),
                r.GetInt32(4),
                r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5), CultureInfo.InvariantCulture),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetDouble(7),
                r.IsDBNull(8) ? null : r.GetDouble(8),
                Enum.TryParse<MediaFormat>(r.GetString(9), out var format) ? format : MediaFormat.Jpeg,
                r.IsDBNull(10) ? null : r.GetString(10)));
        }
        return rows;
    }

    public ILookup<long, SourceRow> LoadSources()
    {
        var list = new List<SourceRow>();
        using var cmd = Command("SELECT media_id, source_path, mtime_utc FROM sources;");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new SourceRow(r.GetInt64(0), r.GetString(1), ParseUtc(r.GetString(2))));
        return list.ToLookup(s => s.MediaId);
    }

    public void SaveMetadata(long id, int metaVersion, DateTime? dateTaken, string? dateSource,
        double? latitude, double? longitude, string? make, string? model)
    {
        using var cmd = Command("""
            UPDATE media SET meta_version = $v, date_taken = $date, date_source = $src,
              latitude = $lat, longitude = $lon, camera_make = $make, camera_model = $model
            WHERE id = $id;
            """,
            ("$v", metaVersion),
            ("$date", dateTaken?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            ("$src", dateSource), ("$lat", latitude), ("$lon", longitude), ("$make", make), ("$model", model),
            ("$id", id));
        cmd.ExecuteNonQuery();
    }

    public void SaveResolution(long id, int? year, string yearSource, string? country, string? continent)
    {
        using var cmd = Command("""
            UPDATE media SET year = $year, year_source = $ysrc, country = $country, continent = $continent
            WHERE id = $id;
            """,
            ("$year", year), ("$ysrc", yearSource), ("$country", country), ("$continent", continent), ("$id", id));
        cmd.ExecuteNonQuery();
    }

    /// <summary>What best-guess sorting decided for a file (all null when it guessed nothing).</summary>
    public void SaveGuess(long id, string? place, string? reason, string? album, string? category)
    {
        using var cmd = Command("""
            UPDATE media SET guess_place = $place, guess_reason = $reason, album = $album, category = $category
            WHERE id = $id;
            """,
            ("$place", place), ("$reason", reason), ("$album", album), ("$category", category), ("$id", id));
        cmd.ExecuteNonQuery();
    }

    public void UpdateDestPath(long id, string destPath)
    {
        using var cmd = Command("UPDATE media SET dest_path = $dest WHERE id = $id;", ("$dest", destPath), ("$id", id));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Groups writes; commit with <see cref="Transaction.Commit"/>, otherwise rolled back on dispose.</summary>
    public Transaction BeginTransaction()
    {
        if (_tx is not null) throw new InvalidOperationException("A transaction is already active.");
        _tx = _conn.BeginTransaction();
        return new Transaction(this);
    }

    public void Dispose()
    {
        _tx?.Dispose();
        _conn.Dispose();
    }

    public sealed class Transaction(CatalogDb db) : IDisposable
    {
        public void Commit()
        {
            db._tx?.Commit();
            Dispose();
        }

        public void Dispose()
        {
            db._tx?.Dispose();
            db._tx = null;
        }
    }

    // ---------- helpers ----------

    private SqliteTransaction? _tx;

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _tx;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private void Execute(string sql)
    {
        using var cmd = Command(sql);
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var cmd = Command(sql);
        return cmd.ExecuteScalar();
    }

    private static string FormatUtc(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
