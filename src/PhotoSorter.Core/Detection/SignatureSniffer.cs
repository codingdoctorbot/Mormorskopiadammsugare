using System.Buffers.Binary;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Detection;

/// <param name="Extension">Extension to use when the file has none or a wrong one.</param>
/// <param name="Weak">Signature too short to trust on its own – needs a matching extension.</param>
public readonly record struct Sniffed(MediaFormat Format, string Extension, bool Weak = false)
{
    public MediaKind Kind => MediaFormats.KindOf(Format);
}

/// <summary>Identifies photo/video content from the first bytes of a file (ARCHITECTURE §4.2 tables).</summary>
public static class SignatureSniffer
{
    /// <summary>Bytes to read. MPEG-TS needs sync bytes at 188 and 376 (M2TS: 196 and 388).</summary>
    public const int HeaderLength = 512;

    private static ReadOnlySpan<byte> JpegSig => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngSig => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> TiffLe => [0x49, 0x49, 0x2A, 0x00];
    private static ReadOnlySpan<byte> TiffBe => [0x4D, 0x4D, 0x00, 0x2A];
    private static ReadOnlySpan<byte> Rw2Sig => [0x49, 0x49, 0x55, 0x00];
    private static ReadOnlySpan<byte> EbmlSig => [0x1A, 0x45, 0xDF, 0xA3];
    private static ReadOnlySpan<byte> AsfSig => [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11];
    private static ReadOnlySpan<byte> MpegPsSig => [0x00, 0x00, 0x01, 0xBA];
    private static ReadOnlySpan<byte> FlvSig => [0x46, 0x4C, 0x56, 0x01];
    private static ReadOnlySpan<byte> IcoSig => [0x00, 0x00, 0x01, 0x00];

    public static Sniffed? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.StartsWith(JpegSig)) return new(MediaFormat.Jpeg, ".jpg");
        if (h.StartsWith(PngSig)) return new(MediaFormat.Png, ".png");
        if (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)) return new(MediaFormat.Gif, ".gif");
        if (IsBmp(h)) return new(MediaFormat.Bmp, ".bmp");

        if (h.StartsWith(TiffLe) || h.StartsWith(TiffBe))
        {
            return h.Length >= 10 && h[8] == 'C' && h[9] == 'R'
                ? new(MediaFormat.Raw, ".cr2")
                : new(MediaFormat.Tiff, ".tif");
        }
        if (h.StartsWith("IIRO"u8) || h.StartsWith("IIRS"u8) || h.StartsWith("MMOR"u8)) return new(MediaFormat.Raw, ".orf");
        if (h.StartsWith(Rw2Sig)) return new(MediaFormat.Raw, ".rw2");
        if (h.StartsWith("FUJIFILMCCD-RAW"u8)) return new(MediaFormat.Raw, ".raf");
        if (h.Length >= 14 && h[6..14].SequenceEqual("HEAPCCDR"u8)) return new(MediaFormat.Raw, ".crw");
        if (h.StartsWith("FOVb"u8)) return new(MediaFormat.Raw, ".x3f");
        if (h.StartsWith("8BPS"u8)) return new(MediaFormat.Psd, ".psd");

        if (h.Length >= 12 && h.StartsWith("RIFF"u8))
        {
            var form = h[8..12];
            if (form.SequenceEqual("WEBP"u8)) return new(MediaFormat.WebP, ".webp");
            if (form.SequenceEqual("AVI "u8)) return new(MediaFormat.Avi, ".avi");
            return null; // WAVE (audio) and other RIFF forms
        }

        if (h.Length >= 12 && h[4..8].SequenceEqual("ftyp"u8)) return SniffFtyp(h);

        if (h.StartsWith(EbmlSig)) return new(MediaFormat.Mkv, ".mkv");
        if (h.StartsWith(AsfSig)) return new(MediaFormat.Wmv, ".wmv", Weak: true); // .wma audio shares it
        if (h.StartsWith(MpegPsSig)) return new(MediaFormat.MpegPs, ".mpg");
        if (h.Length > 376 && h[0] == 0x47 && h[188] == 0x47 && h[376] == 0x47) return new(MediaFormat.MpegTs, ".ts");
        if (h.Length > 388 && h[4] == 0x47 && h[196] == 0x47 && h[388] == 0x47) return new(MediaFormat.MpegTs, ".mts");
        if (h.StartsWith(FlvSig)) return new(MediaFormat.Flv, ".flv");

        if (h.Length >= 8 && IsClassicQuickTimeAtom(h[4..8])) return new(MediaFormat.Mov, ".mov", Weak: true);
        if (h.StartsWith(IcoSig)) return new(MediaFormat.Ico, ".ico", Weak: true);

        return null;
    }

    private static readonly HashSet<uint> HeicBrands = FourCCs("heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs");
    private static readonly HashSet<uint> AvifBrands = FourCCs("avif", "avis");
    private static readonly HashSet<uint> MultiImageBrands = FourCCs("mif1", "msf1");
    private static readonly HashSet<uint> AudioBrands = FourCCs("M4A ", "M4B ", "M4P ", "F4A ", "F4B ");
    private static readonly HashSet<uint> ClassicQuickTimeAtoms = FourCCs("moov", "mdat", "wide", "free", "skip", "pnot");
    private static readonly uint Cr3Brand = FourCC("crx ");
    private static readonly uint QuickTimeBrand = FourCC("qt  ");
    private static readonly uint AvifBrand = FourCC("avif");

    /// <summary>ISO-BMFF: the brand decides photo (HEIC/AVIF/CR3) vs video vs audio.</summary>
    private static Sniffed? SniffFtyp(ReadOnlySpan<byte> h)
    {
        var major = ReadFourCC(h[8..12]);
        if (HeicBrands.Contains(major)) return new(MediaFormat.Heic, ".heic");
        if (AvifBrands.Contains(major)) return new(MediaFormat.Avif, ".avif");
        if (major == Cr3Brand) return new(MediaFormat.Raw, ".cr3");
        if (MultiImageBrands.Contains(major))
            return HasCompatibleBrand(h, AvifBrand) ? new(MediaFormat.Avif, ".avif") : new(MediaFormat.Heic, ".heic");
        if (AudioBrands.Contains(major)) return null;
        if (major == QuickTimeBrand) return new(MediaFormat.Mov, ".mov");
        if (h[8..11].SequenceEqual("3gp"u8) || h[8..11].SequenceEqual("3g2"u8)) return new(MediaFormat.ThreeGp, ".3gp");
        return new(MediaFormat.Mp4, ".mp4"); // isom, iso2, mp41, mp42, avc1, M4V, MSNV, XAVC, …
    }

    private static bool HasCompatibleBrand(ReadOnlySpan<byte> h, uint brand)
    {
        var boxSize = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(h), (uint)h.Length);
        for (var i = 16; i + 4 <= boxSize; i += 4)
            if (ReadFourCC(h.Slice(i, 4)) == brand) return true;
        return false;
    }

    private static bool IsBmp(ReadOnlySpan<byte> h)
    {
        if (h.Length < 18 || !h.StartsWith("BM"u8)) return false;
        var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(h[14..18]);
        return dibHeaderSize is 12 or 40 or 52 or 56 or 64 or 108 or 124;
    }

    private static bool IsClassicQuickTimeAtom(ReadOnlySpan<byte> type) =>
        ClassicQuickTimeAtoms.Contains(ReadFourCC(type));

    private static uint ReadFourCC(ReadOnlySpan<byte> fourBytes) => BinaryPrimitives.ReadUInt32BigEndian(fourBytes);

    private static uint FourCC(string code) =>
        (uint)code[0] << 24 | (uint)code[1] << 16 | (uint)code[2] << 8 | code[3];

    private static HashSet<uint> FourCCs(params string[] codes) => [.. codes.Select(FourCC)];
}
