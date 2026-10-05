using System.IO.Enumeration;
using PhotoSorter.Core.Detection;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Scanning;

public sealed record FileCandidate(string Path, long Size, DateTime LastWriteUtc, DateTime CreationUtc);

/// <summary>
/// Walks the source folders (ARCHITECTURE §4.1): skips reparse points (junction loops), excluded and
/// cache folders, the destination itself, and junk files like Thumbs.db and macOS "._" resource forks.
/// </summary>
public sealed class FileEnumerator
{
    private static readonly HashSet<string> ExcludedFileNames =
        new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "desktop.ini", ".DS_Store", "ehthumbs.db" };

    private readonly IReadOnlyList<string> _roots;
    private readonly HashSet<string> _excludedDirNames;
    private readonly string _destinationPrefix;

    public FileEnumerator(ScanOptions options)
    {
        _roots = NormalizeSources(options.Sources);
        _excludedDirNames = new HashSet<string>(options.ExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
        if (options.SkipAppCaches) _excludedDirNames.UnionWith(options.AppCacheDirectoryNames);
        _destinationPrefix = WithTrailingSeparator(Path.GetFullPath(options.Destination));
    }

    public IReadOnlyList<string> Roots => _roots;

    public IEnumerable<FileCandidate> Enumerate(CancellationToken ct = default)
    {
        foreach (var root in _roots)
        {
            if (!Directory.Exists(root)) continue;
            if (IsInsideDestination(root)) continue;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false,
            };
            var entries = new FileSystemEnumerable<FileCandidate>(
                root,
                (ref FileSystemEntry e) => new FileCandidate(
                    e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcDateTime, e.CreationTimeUtc.UtcDateTime),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && !IsExcludedFile(e.FileName),
                ShouldRecursePredicate = (ref FileSystemEntry e) => ShouldRecurse(ref e),
            };

            foreach (var candidate in entries)
            {
                ct.ThrowIfCancellationRequested();
                yield return candidate;
            }
        }
    }

    /// <summary>Fast pre-pass for the progress bar and the free-space check.</summary>
    public (long Files, long MediaBytes) Count(CancellationToken ct = default)
    {
        long files = 0, mediaBytes = 0;
        foreach (var c in Enumerate(ct))
        {
            files++;
            if (ExtensionRegistry.ClaimedFormat(Path.GetExtension(c.Path)) is not null) mediaBytes += c.Size;
        }
        return (files, mediaBytes);
    }

    private bool ShouldRecurse(ref FileSystemEntry dir)
    {
        if (_excludedDirNames.Contains(dir.FileName.ToString())) return false;
        return !IsInsideDestination(dir.ToFullPath());
    }

    private bool IsInsideDestination(string path) =>
        WithTrailingSeparator(path).StartsWith(_destinationPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsExcludedFile(ReadOnlySpan<char> name) =>
        name.StartsWith("._") || ExcludedFileNames.Contains(name.ToString());

    /// <summary>Full paths, no duplicates, and no source nested inside another source.</summary>
    public static IReadOnlyList<string> NormalizeSources(IEnumerable<string> sources)
    {
        var full = sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => WithTrailingSeparator(Path.GetFullPath(s.Trim())))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s.Length)
            .ToList();

        var result = new List<string>();
        foreach (var s in full)
            if (!result.Any(r => s.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                result.Add(s);
        return result;
    }

    private static string WithTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
