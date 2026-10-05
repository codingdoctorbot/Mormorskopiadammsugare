using PhotoSorter.Core.Detection;
using PhotoSorter.Core.Extraction;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Tests;

public class SignatureSnifferTests
{
    [Fact] public void Jpeg() => AssertFormat(TestFiles.Jpeg(), MediaFormat.Jpeg);
    [Fact] public void Png() => AssertFormat(TestFiles.Png(), MediaFormat.Png);
    [Fact] public void Gif() => AssertFormat(TestFiles.Padded("GIF89a"u8.ToArray(), 100), MediaFormat.Gif);
    [Fact] public void WebP() => AssertFormat(TestFiles.Riff("WEBP"), MediaFormat.WebP);
    [Fact] public void Avi_is_video() => AssertFormat(TestFiles.Riff("AVI "), MediaFormat.Avi, MediaKind.Video);
    [Fact] public void Wav_is_not_media() => Assert.Null(SignatureSniffer.Sniff(TestFiles.Riff("WAVE")));

    [Theory]
    [InlineData("heic", MediaFormat.Heic, MediaKind.Image)]
    [InlineData("avif", MediaFormat.Avif, MediaKind.Image)]
    [InlineData("crx ", MediaFormat.Raw, MediaKind.Image)]
    [InlineData("isom", MediaFormat.Mp4, MediaKind.Video)]
    [InlineData("mp42", MediaFormat.Mp4, MediaKind.Video)]
    [InlineData("qt  ", MediaFormat.Mov, MediaKind.Video)]
    [InlineData("3gp4", MediaFormat.ThreeGp, MediaKind.Video)]
    public void Ftyp_brand_decides_photo_or_video(string brand, MediaFormat format, MediaKind kind) =>
        AssertFormat(TestFiles.Ftyp(brand), format, kind);

    [Fact]
    public void Ftyp_mif1_with_avif_compatible_brand_is_avif() =>
        AssertFormat(TestFiles.Ftyp("mif1", compatible: ["mif1", "avif"]), MediaFormat.Avif);

    [Theory]
    [InlineData("M4A ")]
    [InlineData("M4B ")]
    public void Ftyp_audio_is_not_media(string brand) => Assert.Null(SignatureSniffer.Sniff(TestFiles.Ftyp(brand)));

    [Fact] public void Tiff() => AssertFormat(TestFiles.Padded([0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0], 100), MediaFormat.Tiff);
    [Fact] public void Cr2() => AssertFormat(TestFiles.Padded([0x49, 0x49, 0x2A, 0x00, 16, 0, 0, 0, (byte)'C', (byte)'R'], 100), MediaFormat.Raw);
    [Fact] public void Mkv() => AssertFormat(TestFiles.Padded([0x1A, 0x45, 0xDF, 0xA3], 100), MediaFormat.Mkv, MediaKind.Video);
    [Fact] public void MpegTs() => AssertFormat(TestFiles.TransportStream(188), MediaFormat.MpegTs, MediaKind.Video);
    [Fact] public void M2ts() => AssertFormat(TestFiles.TransportStream(192), MediaFormat.MpegTs, MediaKind.Video);
    [Fact] public void Text_is_nothing() => Assert.Null(SignatureSniffer.Sniff(TestFiles.Text("hello")));
    [Fact] public void Empty_is_nothing() => Assert.Null(SignatureSniffer.Sniff([]));

    private static void AssertFormat(byte[] bytes, MediaFormat format, MediaKind kind = MediaKind.Image)
    {
        var sniffed = SignatureSniffer.Sniff(bytes.AsSpan(0, Math.Min(bytes.Length, SignatureSniffer.HeaderLength)));
        Assert.NotNull(sniffed);
        Assert.Equal(format, sniffed.Value.Format);
        Assert.Equal(kind, sniffed.Value.Kind);
    }
}

public class FileClassifierTests
{
    [Fact]
    public void Matching_extension_is_kept()
    {
        var c = FileClassifier.Classify("IMG_0001.JPG", TestFiles.Jpeg());
        Assert.Equal((ClassificationResult.Media, MediaFormat.Jpeg, ".jpg"), (c.Result, c.Format, c.Extension));
    }

    [Fact]
    public void Missing_extension_is_fixed()
    {
        var c = FileClassifier.Classify("photo", TestFiles.Png());
        Assert.Equal((ClassificationResult.Media, MediaFormat.Png, ".png"), (c.Result, c.Format, c.Extension));
    }

    [Fact]
    public void Wrong_extension_is_fixed()
    {
        var c = FileClassifier.Classify("really_a_png.jpg", TestFiles.Png());
        Assert.Equal((MediaFormat.Png, ".png"), (c.Format, c.Extension));
    }

    [Fact]
    public void Tiff_based_raw_keeps_raw_extension()
    {
        var c = FileClassifier.Classify("DSC_1.NEF", TestFiles.Padded([0x4D, 0x4D, 0x00, 0x2A], 100));
        Assert.Equal((ClassificationResult.Media, MediaFormat.Raw, ".nef"), (c.Result, c.Format, c.Extension));
    }

    [Fact]
    public void Mp4_with_quicktime_brand_keeps_mp4_extension()
    {
        var c = FileClassifier.Classify("clip.mp4", TestFiles.Ftyp("qt  "));
        Assert.Equal((ClassificationResult.Media, MediaKind.Video, ".mp4"), (c.Result, c.Kind, c.Extension));
    }

    [Fact]
    public void Image_extension_with_text_content_is_suspect() =>
        Assert.Equal(ClassificationResult.Suspect, FileClassifier.Classify("fake.jpg", TestFiles.Text("<html>")).Result);

    [Fact]
    public void TypeScript_ts_file_is_not_suspect() =>
        Assert.Equal(ClassificationResult.Other, FileClassifier.Classify("app.ts", TestFiles.Text("export const x = 1;")).Result);

    [Fact]
    public void Weak_ico_signature_needs_ico_extension()
    {
        byte[] ico = TestFiles.Padded([0, 0, 1, 0, 1, 0], 100);
        Assert.Equal(ClassificationResult.Media, FileClassifier.Classify("favicon.ico", ico).Result);
        Assert.Equal(ClassificationResult.Other, FileClassifier.Classify("data.bin", ico).Result);
    }

    [Fact]
    public void Dv_is_trusted_by_extension() =>
        Assert.Equal(ClassificationResult.Media, FileClassifier.Classify("tape.dv", TestFiles.Text("x")).Result);

    [Fact]
    public void Unknown_extension_with_unknown_content_is_other() =>
        Assert.Equal(ClassificationResult.Other, FileClassifier.Classify("data.bin", TestFiles.Text("x")).Result);
}

public class NameResolverTests
{
    [Fact]
    public void Collisions_get_numbered()
    {
        var names = new NameResolver(["IMG_0001.jpg"]);
        Assert.Equal("IMG_0001 (2).jpg", names.Reserve("IMG_0001", ".jpg"));
        Assert.Equal("IMG_0001 (3).jpg", names.Reserve("IMG_0001", ".jpg"));
        Assert.Equal("IMG_0002.jpg", names.Reserve("IMG_0002", ".jpg"));
    }

    [Fact]
    public void Collisions_ignore_case() =>
        Assert.Equal("photo (2).jpg", new NameResolver(["PHOTO.JPG"]).Reserve("photo", ".jpg"));

    [Theory]
    [InlineData("a:b*c?", "a_b_c_")]
    [InlineData("  trailing dots... ", "trailing dots")]
    [InlineData("", "unnamed")]
    [InlineData("CON", "_CON")]
    [InlineData("Semester i Göteborg", "Semester i Göteborg")]
    public void Sanitize(string input, string expected) => Assert.Equal(expected, NameResolver.Sanitize(input));

    [Fact]
    public void Long_names_are_truncated() => Assert.Equal(120, NameResolver.Sanitize(new string('x', 300)).Length);

    [Theory]
    [InlineData("Nokia 6.1", "FILE0043", "Nokia 6.1")]
    [InlineData("Rome", "f12345678", "Rome")]
    [InlineData("VID_20180715_150000", "VID_20180715_150000 (copy)", "VID_20180715_150000")]
    [InlineData("Apple iPhone 6", "Apple iPhone 6 (1)", "Apple iPhone 6")]
    [InlineData("IMG_0042", "IMG_0042 - Copy", "IMG_0042")]
    [InlineData("Semester", "Semester - kopia", "Semester")]
    [InlineData("DSC_0042", "photo copy", "DSC_0042")]
    public void Preferred_name_among_copies(string good, string worse, string expected)
    {
        Assert.Equal(expected, NameResolver.PreferredBaseName([good, worse]));
        Assert.Equal(expected, NameResolver.PreferredBaseName([worse, good])); // order doesn't matter
    }

    [Fact]
    public void Equally_good_names_keep_the_first_copy_seen() =>
        Assert.Equal("IMG_0001", NameResolver.PreferredBaseName(["IMG_0001", "IMG_0002"]));
}
