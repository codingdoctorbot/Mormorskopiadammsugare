using System.Globalization;

namespace PhotoSorter.Core;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>"8.2 GB" (using the current culture's decimal separator).</summary>
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = unit == 0 || value >= 100 ? "0" : "0.0";
        return value.ToString(format, culture ?? CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
