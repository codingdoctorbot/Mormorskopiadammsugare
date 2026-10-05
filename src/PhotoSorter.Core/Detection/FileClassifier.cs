using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Detection;

public enum ClassificationResult
{
    /// <summary>Not a photo or video.</summary>
    Other,
    /// <summary>A photo or video (content confirmed, or extension-only formats like .dv).</summary>
    Media,
    /// <summary>Extension says photo/video, but the content doesn't match any known signature.</summary>
    Suspect,
}

/// <param name="Extension">Extension (lower case, with dot) the copy should get.</param>
public readonly record struct Classification(ClassificationResult Result, MediaFormat Format, string Extension)
{
    public MediaKind Kind => MediaFormats.KindOf(Format);

    public static readonly Classification Other = new(ClassificationResult.Other, default, "");
}

/// <summary>Combines the extension's claim with the content signature (decision table in ARCHITECTURE §4.2).</summary>
public static class FileClassifier
{
    public static Classification Classify(string fileName, ReadOnlySpan<byte> header)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var claimed = ExtensionRegistry.ClaimedFormat(ext);
        var sniffed = SignatureSniffer.Sniff(header);

        if (sniffed is { } s)
        {
            var extensionAgrees = claimed is { } c && MediaFormats.SameFamily(c, s.Format);

            // Weak signatures (ICO, classic QuickTime atoms, ASF) are only believed with a matching extension.
            if (!s.Weak || extensionAgrees)
            {
                if (extensionAgrees)
                {
                    // Keep the original extension (.nef stays .nef, .m4v stays .m4v); TIFF-based RAW stays RAW.
                    var format = claimed == MediaFormat.Raw ? MediaFormat.Raw : s.Format;
                    return new(ClassificationResult.Media, format, ext);
                }
                return new(ClassificationResult.Media, s.Format, s.Extension); // no/wrong extension → fix it
            }
        }

        if (claimed is { } claimedFormat)
        {
            // DV has no reliable signature: trust the extension.
            if (claimedFormat == MediaFormat.Dv) return new(ClassificationResult.Media, claimedFormat, ext);
            if (ExtensionRegistry.IsAmbiguous(ext)) return Classification.Other;
            return new(ClassificationResult.Suspect, claimedFormat, ext);
        }

        return Classification.Other;
    }
}
