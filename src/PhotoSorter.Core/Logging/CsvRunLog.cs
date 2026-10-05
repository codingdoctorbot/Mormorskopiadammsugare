using System.Globalization;
using System.Text;

namespace PhotoSorter.Core.Logging;

/// <summary>
/// Thread-safe CSV log of one run (ARCHITECTURE §4.7). UTF-8 with BOM so Excel shows å/ä/ö correctly.
/// Columns: timestamp,action,source_path,dest_path,sha256,size,format,message
/// </summary>
public sealed class CsvRunLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly Lock _lock = new();

    private CsvRunLog(StreamWriter? writer) => _writer = writer;

    public string? Path { get; private init; }

    public static CsvRunLog Create(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("timestamp,action,source_path,dest_path,sha256,size,format,message");
        return new CsvRunLog(writer) { Path = path };
    }

    /// <summary>A log that writes nothing (tests, or when the log can't be created).</summary>
    public static CsvRunLog Null() => new(null);

    public void Write(
        string action,
        string? sourcePath = null,
        string? destPath = null,
        string? sha256 = null,
        long? size = null,
        string? format = null,
        string? message = null)
    {
        if (_writer is null) return;
        var line = string.Join(',',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            action,
            Escape(sourcePath),
            Escape(destPath),
            sha256 ?? "",
            size?.ToString(CultureInfo.InvariantCulture) ?? "",
            format ?? "",
            Escape(message));
        lock (_lock) _writer.WriteLine(line);
    }

    public void Dispose()
    {
        lock (_lock) _writer?.Dispose();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
