using System.Buffers;
using PhotoSorter.Core.Hashing;

namespace PhotoSorter.Core.Extraction;

/// <summary>
/// Copies via <c>name.partial</c> then renames, so a crash or cancel never leaves a half file under a real
/// name (ARCHITECTURE §4.5). Preserves the source's timestamps.
/// </summary>
public static class CopyService
{
    public const string PartialSuffix = ".partial";

    public static void Copy(
        string source,
        string destination,
        DateTime lastWriteUtc,
        DateTime creationUtc,
        string? verifySha256,
        Action<int>? onBytesCopied,
        CancellationToken ct)
    {
        var partial = destination + PartialSuffix;
        try
        {
            using (var input = FileHasher.OpenRead(source))
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 0))
            {
                output.SetLength(input.Length); // one allocation, less fragmentation
                CopyStream(input, output, onBytesCopied, ct);
                output.SetLength(output.Position); // in case the source shrank while copying
            }

            if (verifySha256 is not null && FileHasher.ComputeSha256(partial, ct) != verifySha256)
                throw new IOException("Verification failed: the copy differs from the source.");

            File.Move(partial, destination);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        // Cosmetic: some drives (NAS, FAT/exFAT) reject odd dates such as 1601/1980 from recovery tools.
        // The copy itself is complete, so a timestamp failure must never fail it.
        try
        {
            File.SetCreationTimeUtc(destination, creationUtc);
            File.SetLastWriteTimeUtc(destination, lastWriteUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    /// <summary>Removes leftovers from a crashed or killed run.</summary>
    public static void DeleteLeftoverPartials(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*" + PartialSuffix, SearchOption.AllDirectories))
            TryDelete(file);
    }

    /// <summary>ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL – no point trying the next file.</summary>
    public static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 0x70 or 0x27;

    private static void CopyStream(Stream input, Stream output, Action<int>? onBytesCopied, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(FileHasher.BufferSize);
        try
        {
            int read;
            while ((read = input.Read(buffer, 0, FileHasher.BufferSize)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                onBytesCopied?.Invoke(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
