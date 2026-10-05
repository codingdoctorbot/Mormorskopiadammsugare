using System.Buffers.Binary;
using System.Text;

namespace PhotoSorter.Core.Tests;

/// <summary>Builds small but structurally real media files for tests.</summary>
public static class TestFiles
{
    /// <summary>
    /// A JPEG (SOI, optional APP1/Exif with DateTimeOriginal and GPS, COM padding, EOI).
    /// <paramref name="seed"/> changes the padding so files get different hashes.
    /// </summary>
    public static byte[] Jpeg(int size = 12_000, int seed = 0, DateTime? taken = null, (double Lat, double Lon)? gps = null)
    {
        var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        if (taken is not null || gps is not null)
        {
            var tiff = ExifTiff(taken, gps);
            var app1Length = 2 + 6 + tiff.Length;
            ms.Write([0xFF, 0xE1, (byte)(app1Length >> 8), (byte)app1Length]);
            ms.Write("Exif\0\0"u8);
            ms.Write(tiff);
        }
        // COM segment(s) as padding, up to 65533 bytes of payload each
        var remaining = Math.Max(0, size - (int)ms.Length - 2);
        var rng = new Random(seed);
        while (remaining > 4)
        {
            var payload = Math.Min(remaining - 4, 65533);
            ms.Write([0xFF, 0xFE, (byte)((payload + 2) >> 8), (byte)(payload + 2)]);
            var bytes = new byte[payload];
            rng.NextBytes(bytes);
            for (var i = 0; i < bytes.Length; i++) if (bytes[i] == 0xFF) bytes[i] = 0xFE; // keep markers out
            ms.Write(bytes);
            remaining -= payload + 4;
        }
        ms.Write([0xFF, 0xD9]);
        return ms.ToArray();
    }

    public static byte[] Png(int size = 12_000, int seed = 0) =>
        Padded([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8], size, seed);

    /// <summary>ISO-BMFF file starting with an ftyp box with the given major brand.</summary>
    public static byte[] Ftyp(string brand, int size = 20_000, int seed = 0, params string[] compatible)
    {
        var box = new List<byte>();
        var boxSize = 16 + 4 * compatible.Length;
        box.AddRange([(byte)(boxSize >> 24), (byte)(boxSize >> 16), (byte)(boxSize >> 8), (byte)boxSize]);
        box.AddRange("ftyp"u8.ToArray());
        box.AddRange(Encoding.ASCII.GetBytes(brand));
        box.AddRange([0, 0, 0, 0]);
        foreach (var c in compatible) box.AddRange(Encoding.ASCII.GetBytes(c));
        return Padded([.. box], size, seed);
    }

    public static byte[] Riff(string form, int size = 20_000, int seed = 0) =>
        Padded([.. "RIFF"u8, 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(form)], size, seed);

    /// <summary>MPEG transport stream: 0x47 sync byte every <paramref name="packet"/> bytes (188 TS, 192 M2TS).</summary>
    public static byte[] TransportStream(int packet, int size = 20_000)
    {
        var data = new byte[size];
        var offset = packet == 192 ? 4 : 0;
        for (var i = offset; i < size; i += packet) data[i] = 0x47;
        return data;
    }

    public static byte[] Text(string text, int size = 12_000)
    {
        var sb = new StringBuilder();
        while (sb.Length < size) sb.Append(text).Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString(0, size));
    }

    public static byte[] Padded(byte[] header, int size, int seed = 0)
    {
        var data = new byte[Math.Max(size, header.Length)];
        new Random(seed + 1).NextBytes(data);
        header.CopyTo(data, 0);
        return data;
    }

    public static string Write(string path, byte[] content, DateTime? lastWriteUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        if (lastWriteUtc is not null) File.SetLastWriteTimeUtc(path, lastWriteUtc.Value);
        return path;
    }

    // ---------- EXIF (little-endian TIFF) ----------

    private const ushort Ascii = 2, Long = 4, Rational = 5;

    private static byte[] ExifTiff(DateTime? taken, (double Lat, double Lon)? gps)
    {
        // Layout: header(8) | IFD0 | ExifIFD | GPS IFD | data area
        var ifd0Entries = (taken is not null ? 1 : 0) + (gps is not null ? 1 : 0);
        var ifd0Size = 2 + 12 * ifd0Entries + 4;
        var exifIfdSize = taken is not null ? 2 + 12 + 4 : 0;
        var gpsIfdSize = gps is not null ? 2 + 12 * 4 + 4 : 0;

        var ifd0Offset = 8;
        var exifOffset = ifd0Offset + ifd0Size;
        var gpsOffset = exifOffset + exifIfdSize;
        var dataOffset = gpsOffset + gpsIfdSize;

        var data = new List<byte>();
        int AddData(byte[] bytes)
        {
            var at = dataOffset + data.Count;
            data.AddRange(bytes);
            if (data.Count % 2 == 1) data.Add(0); // word-align
            return at;
        }

        var w = new List<byte>();
        w.AddRange("II"u8.ToArray());
        w.AddRange(U16(42));
        w.AddRange(U32((uint)ifd0Offset));

        // IFD0: pointers to the Exif and GPS sub-IFDs (tags must be sorted)
        w.AddRange(U16((ushort)ifd0Entries));
        if (taken is not null) w.AddRange(Entry(0x8769, Long, 1, (uint)exifOffset));
        if (gps is not null) w.AddRange(Entry(0x8825, Long, 1, (uint)gpsOffset));
        w.AddRange(U32(0));

        if (taken is not null)
        {
            var text = Encoding.ASCII.GetBytes(taken.Value.ToString("yyyy:MM:dd HH:mm:ss") + "\0");
            var at = AddData(text);
            w.AddRange(U16(1));
            w.AddRange(Entry(0x9003, Ascii, (uint)text.Length, (uint)at)); // DateTimeOriginal
            w.AddRange(U32(0));
        }

        if (gps is not null)
        {
            var (lat, lon) = gps.Value;
            var latAt = AddData(Dms(Math.Abs(lat)));
            var lonAt = AddData(Dms(Math.Abs(lon)));
            w.AddRange(U16(4));
            w.AddRange(Entry(0x0001, Ascii, 2, InlineAscii(lat >= 0 ? 'N' : 'S')));
            w.AddRange(Entry(0x0002, Rational, 3, (uint)latAt));
            w.AddRange(Entry(0x0003, Ascii, 2, InlineAscii(lon >= 0 ? 'E' : 'W')));
            w.AddRange(Entry(0x0004, Rational, 3, (uint)lonAt));
            w.AddRange(U32(0));
        }

        w.AddRange(data);
        return [.. w];
    }

    private static byte[] Dms(double value)
    {
        var deg = Math.Floor(value);
        var min = Math.Floor((value - deg) * 60);
        var sec = ((value - deg) * 60 - min) * 60;
        return [.. U32((uint)deg), .. U32(1), .. U32((uint)min), .. U32(1), .. U32((uint)Math.Round(sec * 10000)), .. U32(10000)];
    }

    private static uint InlineAscii(char c) => c; // "X\0" stored left-justified in the 4-byte value field (LE)

    private static byte[] Entry(ushort tag, ushort type, uint count, uint valueOrOffset) =>
        [.. U16(tag), .. U16(type), .. U32(count), .. U32(valueOrOffset)];

    private static byte[] U16(ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        return b;
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }
}

/// <summary>A temp folder deleted after the test.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PhotoSorterTests", Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string this[string relative] => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
