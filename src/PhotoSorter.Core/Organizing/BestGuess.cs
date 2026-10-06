using System.Text.RegularExpressions;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Organizing;

/// <summary>
/// Best-effort sorting <em>inside</em> the unknown folders (ARCHITECTURE §5.5). Every guess stays under
/// <c>_Unknown location</c> / <c>_Unknown year</c> and is marked, so trusted folders stay trusted:
/// <list type="bullet">
/// <item>a probable place (<c>~Sweden</c>) borrowed from GPS photos taken at about the same time,</item>
/// <item>the original album folder name (<c>Midsommar 2018</c>),</item>
/// <item>non-photos set apart (<c>_Screenshots</c>, <c>_Graphics</c>, <c>_Downloads</c>).</item>
/// </list>
/// </summary>
public static partial class BestGuess
{
    public const string GuessPrefix = "~";
    public const string Screenshots = "_Screenshots";
    public const string Graphics = "_Graphics";
    public const string Downloads = "_Downloads";

    /// <summary>Subfolder inside every final folder for stock photos and memes/web images (by file name).</summary>
    public const string StockAndMemes = "_Stock & memes";

    /// <summary>GPS photos within this time of a photo vouch for its place.</summary>
    public static readonly TimeSpan PlaceWindow = TimeSpan.FromHours(3);

    /// <summary>An album folder is only created when at least this many files share it.</summary>
    public const int MinAlbumSize = 3;

    /// <summary>How far up from the file to look for a meaningful folder name.</summary>
    private const int MaxAlbumLevels = 4;

    /// <summary>Capture dates trustworthy enough to compare with other photos' times.</summary>
    public static bool IsCaptureTime(string? dateSource) =>
        dateSource is "Exif" or "QuickTime" or "QuickTimeUtc" or "Avi";

    // ---------------- 1. Place from photos taken at the same time ----------------

    /// <summary>A GPS-located photo with a reliable capture time.</summary>
    public readonly record struct Anchor(DateTime Taken, string Place);

    /// <summary>Looks up the place of photos without GPS from GPS photos taken around the same time.</summary>
    public sealed class PlaceGuesser
    {
        private readonly Anchor[] _anchors;

        public PlaceGuesser(IEnumerable<Anchor> anchors) => _anchors = [.. anchors.OrderBy(a => a.Taken)];

        /// <summary>
        /// The place all GPS photos within <see cref="PlaceWindow"/> agree on, with how many there were.
        /// Null when there are none or they disagree (a border crossing, a flight – better no guess).
        /// </summary>
        public (string Place, int Witnesses)? Guess(DateTime taken)
        {
            var from = taken - PlaceWindow;
            var to = taken + PlaceWindow;
            var i = LowerBound(from);
            string? place = null;
            var count = 0;
            for (; i < _anchors.Length && _anchors[i].Taken <= to; i++)
            {
                if (place is null) place = _anchors[i].Place;
                else if (!string.Equals(place, _anchors[i].Place, StringComparison.Ordinal)) return null;
                count++;
            }
            return place is null ? null : (place, count);
        }

        private int LowerBound(DateTime t)
        {
            int lo = 0, hi = _anchors.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (_anchors[mid].Taken < t) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }

    // ---------------- 2. Album folder names ----------------

    /// <summary>
    /// The album a file came from: the nearest meaningful folder above it ("Rome 2009" in
    /// <c>Rome 2009\DCIM\100CANON\IMG_0001.JPG</c>). Generic folders (DCIM, Pictures, Bilder, Desktop, camera
    /// folders like 100CANON, plain numbers) are skipped; backup folders and user-profile folders end the search,
    /// so a backup's or a person's name never becomes an album.
    /// </summary>
    /// <param name="sourceRoots">
    /// The folders the user added as sources. They and everything above them are never albums – a source folder
    /// is a container the user picked ("E:\Gamla bilder"), not an event.
    /// </param>
    public static string? AlbumFolder(string path, IReadOnlyCollection<string>? sourceRoots = null)
    {
        var parts = (Path.GetDirectoryName(path) ?? "")
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var firstAllowed = sourceRoots is null ? 0 : FirstPartBelowRoot(path, sourceRoots);
        for (int i = parts.Length - 1, level = 0; i >= firstAllowed && level < MaxAlbumLevels; i--, level++)
        {
            var name = parts[i].Trim();
            if (i == 0 && name.EndsWith(':')) return null;                       // drive root
            if (i > 0 && UserProfileParent().IsMatch(parts[i - 1])) return null;  // C:\Users\<name>
            if (BackupFolder().IsMatch(name)) return null;
            if (GenericFolder().IsMatch(name) || CameraFolder().IsMatch(name) || DeviceFolder().IsMatch(name)) continue;
            if (name.StartsWith('_') || name.StartsWith(GuessPrefix) ||          // our own folders
                name.Equals(DestinationLayout.ExtractedFolderName, StringComparison.OrdinalIgnoreCase)) continue;
            return name;
        }
        return null;
    }

    /// <summary>Index of the first folder below the source root the path lives in (0 when none matches).</summary>
    private static int FirstPartBelowRoot(string path, IReadOnlyCollection<string> sourceRoots)
    {
        var root = sourceRoots
            .Select(r => Path.TrimEndingDirectorySeparator(r) + Path.DirectorySeparatorChar)
            .Where(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            .MaxBy(r => r.Length);
        return root is null
            ? 0
            : root.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>The most common album among a file's copies (first one on a tie).</summary>
    public static string? AlbumFolder(IEnumerable<string> paths, IReadOnlyCollection<string>? sourceRoots = null) =>
        paths.Select(p => AlbumFolder(p, sourceRoots))
            .OfType<string>()
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .FirstOrDefault();

    // ---------------- 4. Stock photos and memes (any folder) ----------------

    /// <summary>
    /// True when any copy's file name looks like a stock-site download or a meme/web image: stock agencies,
    /// meme sites, Discord (<c>image0.png</c>, <c>unknown.png</c>), Reddit/Twitter-style random names, Tumblr,
    /// Facebook-saved (<c>FB_IMG_…</c>) and Messenger (<c>received_…</c>) names. Name-based only, so false
    /// positives happen – they just end up one subfolder deeper in the same folder, easy to check.
    /// </summary>
    public static bool IsStockOrMeme(IEnumerable<string> paths) =>
        paths.Select(Path.GetFileName).OfType<string>().Any(name =>
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            return StockName().IsMatch(name) || MemeName().IsMatch(name) || RedditName().IsMatch(stem) || TwitterName(stem);
        });

    /// <summary>15 random characters with upper case, lower case and digits (Twitter/X media) – not a camera name.</summary>
    private static bool TwitterName(string stem) =>
        stem.Length == 15 && TwitterChars().IsMatch(stem) && stem.Any(char.IsUpper) && stem.Any(char.IsLower) &&
        stem.Any(char.IsDigit) && !CameraPrefix().IsMatch(stem);

    [GeneratedRegex(@"(?<![a-z])(shutterstock|istock(photo)?|gettyimages|getty_images|adobestock|adobe_stock|depositphotos|123rf|dreamstime|fotolia|bigstock|canstock|freepik|pexels|unsplash|pixabay|alamy|thinkstock|vecteezy|rawpixel|stock[-_ ]photo)",
        RegexOptions.IgnoreCase)]
    private static partial Regex StockName();

    [GeneratedRegex(@"(?<![a-z])(memes?|9gag|imgflip|ifunny)(?![a-z])|^tumblr_|^fb_img_\d{13}|^received_\d{6,}|^(image\d+|unknown)\.(png|jpe?g|gif|webp)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex MemeName();

    /// <summary>Reddit media: 13 lower-case letters and digits (mixed).</summary>
    [GeneratedRegex(@"^(?=[a-z0-9]*\d)(?=[a-z0-9]*[a-z])[a-z0-9]{13}$")]
    private static partial Regex RedditName();

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex TwitterChars();

    [GeneratedRegex(@"^(img|dsc|dscn|dscf|pxl|vid|mvi|p\d|pict|sam|imag|gopr|dji|photo|bild|screenshot)", RegexOptions.IgnoreCase)]
    private static partial Regex CameraPrefix();

    // ---------------- 3. Non-photos ----------------

    /// <summary>Screenshots, graphics and downloads – images that aren't camera photos. Null for everything else.</summary>
    public static string? Category(MediaKind kind, MediaFormat format, string? cameraMake, IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Any(p => ScreenshotPath().IsMatch(p))) return Screenshots;
        if (kind == MediaKind.Image && cameraMake is null &&
            format is MediaFormat.Png or MediaFormat.Gif or MediaFormat.Bmp or MediaFormat.Ico or MediaFormat.Psd or MediaFormat.WebP)
            return Graphics;
        // Only when every copy is in a Downloads folder: one stray copy there doesn't make a camera photo a download.
        if (list.Count > 0 && list.All(p => DownloadsFolder().IsMatch(p))) return Downloads;
        return null;
    }

    [GeneratedRegex(@"^(dcim|camera|camera roll|kamera|kamerabilder|kamerarulle|pictures|my pictures|bilder|mina bilder|photos|foton|fotos|images|imgs?|media|desktop|skrivbord|documents|dokument|my documents|mina dokument|new folder( \(\d+\))?|ny mapp( \(\d+\))?|onedrive|icloud|icloud photos|dropbox|google photos|sent|received|whatsapp|whatsapp images|whatsapp video|whatsapp animated gifs|export|exports|edited|originals|misc|diverse|övrigt|other|unsorted|osorterat|import|imports|recovered|found\.\d+|files|filer|data|gallery|galleri|downloads|hämtade filer|hamtade filer|nedladdningar)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex GenericFolder();

    /// <summary>Camera-made folder names (100CANON, 101MSDCF, 100APPLE, 100_0306) and plain numbers (incl. years).</summary>
    [GeneratedRegex(@"^(\d{3}[A-Z0-9_]{5}|\d{3}_\d{4}|\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CameraFolder();

    /// <summary>
    /// Names of devices rather than events: "Camera SD card (2)", "SD-kort", "Minneskort", "iPhone", "Mobil",
    /// "Samsung phone backup". Optional copy numbers like " (2)" are ignored.
    /// </summary>
    [GeneratedRegex(@"^((camera|kamera|my|min|gamla?|old)\s+)?(sd|cf|micro\s*sd)[\s-]*(card|kort)(\s*\(\d+\))?$|^(memory card|minneskort)(\s*\(\d+\))?$|^((my|min|gamla?|old)\s+)?(phone|mobile|mobil|mobilen|telefon|telefonen|iphone|android|samsung|galaxy|nokia|sony ericsson|huawei|pixel)(\s+(phone|backup|bilder|pictures|photos|foton))?(\s*\(\d+\))?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex DeviceFolder();

    [GeneratedRegex(@"back\s*-?\s*up|s[äa]kerhetskopi|time\s*machine|\bbak\b", RegexOptions.IgnoreCase)]
    private static partial Regex BackupFolder();

    [GeneratedRegex(@"^(users|documents and settings|home)$", RegexOptions.IgnoreCase)]
    private static partial Regex UserProfileParent();

    [GeneratedRegex(@"screenshot|screen shot|skärmbild|skärmavbild|skarmbild|[\\/]screenshots?[\\/]|[\\/]skärmbilder[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenshotPath();

    [GeneratedRegex(@"[\\/](downloads|hämtade filer|hamtade filer|nedladdningar)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadsFolder();
}
