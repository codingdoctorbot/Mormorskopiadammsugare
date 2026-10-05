namespace PhotoSorter.Core.Models;

public enum MediaKind
{
    Image,
    Video,
}

public enum MediaFormat
{
    // Images
    Jpeg,
    Png,
    Gif,
    Bmp,
    Tiff,
    WebP,
    Heic,
    Avif,
    Psd,
    Ico,
    /// <summary>Any camera RAW (CR2/CR3/NEF/ARW/DNG/ORF/RW2/RAF/…). The original extension is kept.</summary>
    Raw,

    // Videos
    Mp4,
    Mov,
    ThreeGp,
    Avi,
    Mkv,
    WebM,
    Wmv,
    MpegPs,
    MpegTs,
    Flv,
    Dv,
}

public static class MediaFormats
{
    public static MediaKind KindOf(MediaFormat format) =>
        format >= MediaFormat.Mp4 ? MediaKind.Video : MediaKind.Image;

    /// <summary>
    /// Formats in the same family share a container, so a mismatch between them is not suspicious
    /// (e.g. a .nef is a TIFF, a .mp4 may carry the QuickTime brand, a .webm is Matroska).
    /// </summary>
    public static bool SameFamily(MediaFormat a, MediaFormat b) => Family(a) == Family(b);

    private static int Family(MediaFormat f) => f switch
    {
        MediaFormat.Tiff or MediaFormat.Raw => 1,
        MediaFormat.Mp4 or MediaFormat.Mov or MediaFormat.ThreeGp => 2,
        MediaFormat.Mkv or MediaFormat.WebM => 3,
        _ => 100 + (int)f,
    };
}
