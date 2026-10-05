using System.Buffers;
using System.Security.Cryptography;

namespace PhotoSorter.Core.Hashing;

/// <summary>Streaming SHA-256 (ARCHITECTURE §4.4). Reports bytes as it goes so multi-GB videos show progress.</summary>
public static class FileHasher
{
    public const int BufferSize = 1 << 20;

    /// <summary>Opens a source file read-only without blocking other programs (sources are never modified).</summary>
    public static FileStream OpenRead(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.SequentialScan,
        BufferSize = 0, // we read in 1 MB blocks ourselves
    });

    public static string ComputeSha256(Stream stream, Action<int>? onBytesRead = null, CancellationToken ct = default)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, BufferSize)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
                onBytesRead?.Invoke(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    public static string ComputeSha256(string path, CancellationToken ct = default)
    {
        using var stream = OpenRead(path);
        return ComputeSha256(stream, null, ct);
    }
}
