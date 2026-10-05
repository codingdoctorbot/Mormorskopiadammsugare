using System.Diagnostics;
using System.IO;

namespace PhotoSorter.App.Services;

/// <summary>Opens folders and files the way Explorer would.</summary>
public static class Shell
{
    public static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Start("explorer.exe", $"\"{path}\"");
    }

    public static void OpenFile(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Opens Explorer with the file selected (useful for CSV logs when no CSV app is installed).</summary>
    public static void ShowInFolder(string path)
    {
        if (File.Exists(path)) Start("explorer.exe", $"/select,\"{path}\"");
    }

    private static void Start(string exe, string args) => Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
}
