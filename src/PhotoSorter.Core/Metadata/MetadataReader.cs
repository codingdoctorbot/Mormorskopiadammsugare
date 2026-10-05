using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MDirectory = MetadataExtractor.Directory;

namespace PhotoSorter.Core.Metadata;

/// <param name="DateTaken">Local clock time when the photo/video was taken.</param>
/// <param name="DateSource">Exif | QuickTime | Avi | QuickTimeUtc | ExifModified</param>
public sealed record MediaMetadata(
    DateTime? DateTaken,
    string? DateSource,
    double? Latitude,
    double? Longitude,
    string? CameraMake,
    string? CameraModel)
{
    public static readonly MediaMetadata Empty = new(null, null, null, null, null, null);
}

/// <summary>Reads date taken + GPS from photos (EXIF) and videos (QuickTime/MP4, AVI) – ARCHITECTURE §5.2/§5.3.</summary>
public static partial class MetadataReader
{
    /// <summary>Bump when reading improves, so the organizer re-reads files catalogued with an older version.</summary>
    public const int Version = 1;

    public static MediaMetadata Read(string path, DateTime? now = null)
    {
        IReadOnlyList<MDirectory> dirs;
        try
        {
            dirs = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception)
        {
            // Unsupported format (MTS, MKV, WMV…) or a damaged file: no metadata, fall back to names.
            return MediaMetadata.Empty;
        }

        var (date, source) = FindDate(dirs, now ?? DateTime.Now);
        var (lat, lon) = FindGps(dirs);
        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        return new MediaMetadata(
            date, source, lat, lon,
            Clean(ifd0?.GetString(ExifDirectoryBase.TagMake)),
            Clean(ifd0?.GetString(ExifDirectoryBase.TagModel)));
    }

    private static (DateTime?, string?) FindDate(IReadOnlyList<MDirectory> dirs, DateTime now)
    {
        // Best evidence first.
        IEnumerable<(DateTime? Date, string Source)> candidates =
        [
            .. dirs.OfType<ExifSubIfdDirectory>().Select(d => (GetDate(d, ExifDirectoryBase.TagDateTimeOriginal), "Exif")),
            .. dirs.OfType<ExifSubIfdDirectory>().Select(d => (GetDate(d, ExifDirectoryBase.TagDateTimeDigitized), "Exif")),
            .. dirs.OfType<QuickTimeMetadataHeaderDirectory>().Select(d => (GetDate(d, QuickTimeMetadataHeaderDirectory.TagCreationDate), "QuickTime")),
            .. dirs.OfType<AviDirectory>().Select(d => (GetDate(d, AviDirectory.TagDateTimeOriginal), "Avi")),
            .. dirs.OfType<QuickTimeMovieHeaderDirectory>().Select(d => (GetDate(d, QuickTimeMovieHeaderDirectory.TagCreated), "QuickTimeUtc")),
            .. dirs.OfType<ExifIfd0Directory>().Select(d => (GetDate(d, ExifDirectoryBase.TagDateTime), "ExifModified")),
        ];
        foreach (var (date, source) in candidates)
            if (date is { } d && DateResolver.IsPlausibleMetadataDate(d, now))
                return (d, source);
        return (null, null);
    }

    private static DateTime? GetDate(MDirectory dir, int tag)
    {
        if (!dir.ContainsTag(tag)) return null;
        try
        {
            if (dir.TryGetDateTime(tag, out var dt)) return dt;
        }
        catch (Exception)
        {
            // fall through to string parsing
        }
        return dir.GetObject(tag) switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            _ => ParseDate(dir.GetString(tag)),
        };
    }

    /// <summary>Parses ISO 8601 (with "+0200" or "+02:00" offsets), EXIF "yyyy:MM:dd HH:mm:ss" and AVI "Mon Jul 04 12:00:00 2016".</summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = OffsetWithoutColon().Replace(text.Trim().TrimEnd('\0'), "$1:$2");
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto))
            return dto.DateTime; // the clock time as written, which is the local time at the place it was taken
        string[] formats = ["yyyy:MM:dd HH:mm:ss", "yyyy:MM:dd", "ddd MMM dd HH:mm:ss yyyy", "ddd MMM d HH:mm:ss yyyy"];
        var normalized = MultipleSpaces().Replace(s, " ");
        return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt)
            ? dt
            : null;
    }

    private static (double?, double?) FindGps(IReadOnlyList<MDirectory> dirs)
    {
        foreach (var gps in dirs.OfType<GpsDirectory>())
        {
            try
            {
                if (gps.TryGetGeoLocation(out var loc) && IsUsable(loc.Latitude, loc.Longitude))
                    return (loc.Latitude, loc.Longitude);
            }
            catch (Exception)
            {
                // malformed GPS block
            }
        }

        // Videos: ISO 6709 strings such as "+59.3293+018.0686+012.000/" (iPhone, Android, GoPro).
        foreach (var qt in dirs.OfType<QuickTimeMetadataHeaderDirectory>())
            if (ParseIso6709(qt.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation)) is { } p)
                return (p.Lat, p.Lon);
        foreach (var dir in dirs.Where(d => d.Name.Contains("QuickTime", StringComparison.OrdinalIgnoreCase)))
            foreach (var tag in dir.Tags)
                if (ParseIso6709(dir.GetString(tag.Type)) is { } p)
                    return (p.Lat, p.Lon);

        return (null, null);
    }

    /// <summary>ISO 6709: decimal degrees, or ±DDMM.mm / ±DDDMM.mm / ±DDMMSS / ±DDDMMSS.</summary>
    public static (double Lat, double Lon)? ParseIso6709(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Iso6709().Match(text);
        if (!m.Success) return null;
        var lat = ToDegrees(m.Groups[1].Value, degreeDigits: 2);
        var lon = ToDegrees(m.Groups[2].Value, degreeDigits: 3);
        return IsUsable(lat, lon) ? (lat, lon) : null;
    }

    private static double ToDegrees(string value, int degreeDigits)
    {
        var sign = value[0] == '-' ? -1 : 1;
        var unsigned = value[1..];
        var dot = unsigned.IndexOf('.');
        var intDigits = dot < 0 ? unsigned.Length : dot;
        var number = double.Parse(unsigned, CultureInfo.InvariantCulture);
        if (intDigits <= degreeDigits) return sign * number;
        if (intDigits <= degreeDigits + 2)
        {
            var deg = Math.Floor(number / 100);
            return sign * (deg + (number - deg * 100) / 60);
        }
        var d = Math.Floor(number / 10000);
        var rest = number - d * 10000;
        var min = Math.Floor(rest / 100);
        return sign * (d + min / 60 + (rest - min * 100) / 3600);
    }

    private static bool IsUsable(double lat, double lon) =>
        !double.IsNaN(lat) && !double.IsNaN(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180 &&
        !(Math.Abs(lat) < 1e-6 && Math.Abs(lon) < 1e-6);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().TrimEnd('\0').Trim();

    [GeneratedRegex(@"^\s*([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)(?:[+-]\d+(?:\.\d+)?)?(?:CRS[^/]*)?/?\s*$")]
    private static partial Regex Iso6709();

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();
}
