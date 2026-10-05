using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Detection;

/// <summary>What a file's extension claims it is (ARCHITECTURE §4.2). Extensions include the dot, any case.</summary>
public static class ExtensionRegistry
{
    private static readonly Dictionary<string, MediaFormat> Media = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = MediaFormat.Jpeg, [".jpeg"] = MediaFormat.Jpeg, [".jpe"] = MediaFormat.Jpeg, [".jfif"] = MediaFormat.Jpeg,
        [".png"] = MediaFormat.Png,
        [".gif"] = MediaFormat.Gif,
        [".bmp"] = MediaFormat.Bmp, [".dib"] = MediaFormat.Bmp,
        [".tif"] = MediaFormat.Tiff, [".tiff"] = MediaFormat.Tiff,
        [".webp"] = MediaFormat.WebP,
        [".heic"] = MediaFormat.Heic, [".heif"] = MediaFormat.Heic,
        [".avif"] = MediaFormat.Avif,
        [".psd"] = MediaFormat.Psd,
        [".ico"] = MediaFormat.Ico,

        [".cr2"] = MediaFormat.Raw, [".cr3"] = MediaFormat.Raw, [".crw"] = MediaFormat.Raw,
        [".nef"] = MediaFormat.Raw, [".nrw"] = MediaFormat.Raw,
        [".arw"] = MediaFormat.Raw, [".srf"] = MediaFormat.Raw, [".sr2"] = MediaFormat.Raw,
        [".dng"] = MediaFormat.Raw, [".orf"] = MediaFormat.Raw, [".rw2"] = MediaFormat.Raw,
        [".raf"] = MediaFormat.Raw, [".pef"] = MediaFormat.Raw, [".srw"] = MediaFormat.Raw,
        [".x3f"] = MediaFormat.Raw, [".3fr"] = MediaFormat.Raw, [".mef"] = MediaFormat.Raw,
        [".mos"] = MediaFormat.Raw, [".erf"] = MediaFormat.Raw, [".kdc"] = MediaFormat.Raw,

        [".mp4"] = MediaFormat.Mp4, [".m4v"] = MediaFormat.Mp4,
        [".mov"] = MediaFormat.Mov, [".qt"] = MediaFormat.Mov,
        [".3gp"] = MediaFormat.ThreeGp, [".3g2"] = MediaFormat.ThreeGp,
        [".avi"] = MediaFormat.Avi,
        [".mkv"] = MediaFormat.Mkv,
        [".webm"] = MediaFormat.WebM,
        [".wmv"] = MediaFormat.Wmv, [".asf"] = MediaFormat.Wmv,
        [".mpg"] = MediaFormat.MpegPs, [".mpeg"] = MediaFormat.MpegPs, [".mpe"] = MediaFormat.MpegPs, [".vob"] = MediaFormat.MpegPs,
        [".ts"] = MediaFormat.MpegTs, [".mts"] = MediaFormat.MpegTs, [".m2ts"] = MediaFormat.MpegTs,
        [".flv"] = MediaFormat.Flv,
        [".dv"] = MediaFormat.Dv,
    };

    /// <summary>Extensions that are also common for non-media files (.ts = TypeScript). A mismatch is not suspicious.</summary>
    private static readonly HashSet<string> Ambiguous = new(StringComparer.OrdinalIgnoreCase) { ".ts" };

    /// <summary>Known non-media types: skipped without reading the file. Audio is deliberately here.</summary>
    private static readonly HashSet<string> Other = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".msi", ".cab", ".lnk", ".url", ".ini", ".inf", ".cfg", ".log", ".tmp", ".bak",
        ".txt", ".csv", ".xml", ".json", ".html", ".htm", ".css", ".js", ".md", ".rtf",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".pdf", ".epub",
        ".mp3", ".m4a", ".m4b", ".m4p", ".wav", ".wma", ".flac", ".aac", ".ogg", ".opus", ".mid", ".midi", ".aiff",
        ".zip", ".7z", ".rar", ".gz", ".tar", ".bz2", ".xz", ".iso", ".img", ".vhd", ".vhdx",
        ".db", ".sqlite", ".mdb", ".accdb", ".pst", ".ost", ".eml", ".msg", ".vcf", ".ics",
        ".ttf", ".otf", ".woff", ".woff2", ".fon",
        ".cs", ".c", ".cpp", ".h", ".java", ".py", ".ps1", ".bat", ".cmd", ".sh",
        ".thm", ".lrv", ".xmp", ".aae", ".ithmb",
    };

    public static MediaFormat? ClaimedFormat(string extension) =>
        Media.TryGetValue(extension, out var f) ? f : null;

    public static bool IsKnownOther(string extension) => Other.Contains(extension);

    public static bool IsAmbiguous(string extension) => Ambiguous.Contains(extension);
}
