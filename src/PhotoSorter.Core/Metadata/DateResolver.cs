using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoSorter.Core.Metadata;

/// <param name="Source">Where the year came from: Exif, QuickTime, …, FileName, FolderName, FileTime, or Unknown.</param>
public sealed record YearResolution(int? Year, string Source)
{
    public static readonly YearResolution Unknown = new(null, "Unknown");
}

/// <summary>
/// Picks the year a photo/video was taken, stopping at the first trustworthy hit (ARCHITECTURE §5.2):
/// metadata → file name → folder name → file date (only if enabled; backups reset it).
/// </summary>
public static partial class DateResolver
{
    /// <summary>Oldest year accepted from names (scanned prints can be old). Metadata uses the same floor.</summary>
    public const int MinYear = 1950;

    public static YearResolution Resolve(
        DateTime? metadataDate,
        string? metadataSource,
        IReadOnlyCollection<string> sourcePaths,
        IEnumerable<DateTime> sourceModifiedUtc,
        bool useFileTimes,
        DateTime now)
    {
        // "ExifModified" is when software last saved the file (e.g. an edit years later) – weaker than names.
        var metadataIsEditDate = metadataSource == "ExifModified";
        if (metadataDate is { } d && IsPlausibleMetadataDate(d, now) && !metadataIsEditDate)
            return new(d.Year, metadataSource ?? "Metadata");

        // Several copies may disagree (e.g. a WhatsApp re-send has a newer name); the oldest is most likely right.
        var fromName = sourcePaths
            .Select(p => FileNameDatePatterns.TryGetDate(Path.GetFileNameWithoutExtension(p), now))
            .Where(x => x is not null)
            .Select(x => x!.Value.Year)
            .ToList();
        if (fromName.Count > 0) return new(fromName.Min(), "FileName");

        var fromFolder = sourcePaths.Select(p => NearestFolderYear(p, now)).Where(y => y is not null).ToList();
        if (fromFolder.Count > 0) return new(fromFolder.Min(), "FolderName");

        if (metadataIsEditDate && metadataDate is { } edited && IsPlausibleMetadataDate(edited, now))
            return new(edited.Year, "ExifModified");

        if (useFileTimes)
        {
            var years = sourceModifiedUtc.Select(t => t.ToLocalTime()).Where(t => IsPlausibleYear(t.Year, now)).ToList();
            if (years.Count > 0) return new(years.Min().Year, "FileTime");
        }

        return YearResolution.Unknown;
    }

    /// <summary>
    /// Rejects dates cameras write when their clock was never set: epochs (1904, 1970, 1980) and the
    /// January 1st midnight reset many cameras fall back to (2000-01-01 00:00 etc.).
    /// </summary>
    public static bool IsPlausibleMetadataDate(DateTime d, DateTime now)
    {
        if (!IsPlausibleYear(d.Year, now) || d > now.AddDays(1)) return false;
        if (d is { Month: 1, Day: 1 } && d.Year is 1904 or 1970 or 1980) return false;
        if (d is { Month: 1, Day: 1, Hour: 0, Minute: < 10 } && d.Year <= 2010) return false;
        return true;
    }

    public static bool IsPlausibleYear(int year, DateTime now) => year >= MinYear && year <= now.Year;

    /// <summary>
    /// Year in the closest folder name above the file ("Rome 2009", "2015-06 Summer").
    /// Folders named like backups are skipped – their year is when the backup was made, not when photos were taken.
    /// </summary>
    public static int? NearestFolderYear(string path, DateTime now)
    {
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) break; // drive root
            if (BackupFolder().IsMatch(name)) continue;
            foreach (Match m in YearToken().Matches(name))
            {
                var year = int.Parse(m.Value, CultureInfo.InvariantCulture);
                if (IsPlausibleYear(year, now)) return year;
            }
        }
        return null;
    }

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d\d(?!\d)")]
    private static partial Regex YearToken();

    [GeneratedRegex(@"back\s*-?\s*up|s[äa]kerhetskopi|time\s*machine|\bbak\b", RegexOptions.IgnoreCase)]
    private static partial Regex BackupFolder();
}

/// <summary>Dates in camera/phone/app file names (ARCHITECTURE §5.2 step 3).</summary>
public static partial class FileNameDatePatterns
{
    /// <summary>IMG_20150612_143005, VID-20160301-WA0001, PXL_20210101_…, Screenshot_2019-07-04-…, 2014-08-02 13.45.10</summary>
    public static DateOnly? TryGetDate(string fileNameWithoutExtension, DateTime now)
    {
        foreach (var regex in (ReadOnlySpan<Regex>)[Separated(), Compact()])
        {
            foreach (Match m in regex.Matches(fileNameWithoutExtension))
            {
                var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
                var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
                var d = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
                if (!DateResolver.IsPlausibleYear(y, now) || d > DateTime.DaysInMonth(y, mo)) continue;
                var date = new DateOnly(y, mo, d);
                if (date.ToDateTime(TimeOnly.MinValue) <= now.AddDays(1)) return date;
            }
        }
        return null;
    }

    [GeneratedRegex(@"(?<!\d)(?<y>(?:19|20)\d\d)[-_. ](?<m>0[1-9]|1[0-2])[-_. ](?<d>0[1-9]|[12]\d|3[01])(?!\d)")]
    private static partial Regex Separated();

    [GeneratedRegex(@"(?<!\d)(?<y>(?:19|20)\d\d)(?<m>0[1-9]|1[0-2])(?<d>0[1-9]|[12]\d|3[01])(?!\d)")]
    private static partial Regex Compact();
}
