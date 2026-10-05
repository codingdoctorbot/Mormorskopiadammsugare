namespace PhotoSorter.Core.Models;

public enum ScanPhase
{
    Counting,
    Extracting,
    Finished,
}

public enum RunOutcome
{
    Completed,
    Cancelled,
    Failed,
}

/// <summary>Immutable snapshot of an extraction run, reported ~10 times per second.</summary>
public sealed record ScanProgress
{
    public ScanPhase Phase { get; init; }

    /// <summary>Files looked at so far (all types).</summary>
    public long FilesSeen { get; init; }

    /// <summary>Total files from the counting pre-pass (0 while counting).</summary>
    public long TotalFiles { get; init; }

    public long PhotosFound { get; init; }
    public long VideosFound { get; init; }

    /// <summary>New unique files copied (or that would be copied, in a dry run).</summary>
    public long Unique { get; init; }

    /// <summary>Content already copied earlier (in this or a previous run) – only the source path was recorded.</summary>
    public long Duplicates { get; init; }

    /// <summary>Source files recorded by an earlier run with the same size and date – not even re-read.</summary>
    public long AlreadyCataloged { get; init; }

    /// <summary>Photo/video files skipped by a filter (too small, type switched off).</summary>
    public long Skipped { get; init; }

    /// <summary>Photo/video extension but the content doesn't match.</summary>
    public long Suspects { get; init; }

    public long Errors { get; init; }
    public long BytesCopied { get; init; }
    public long BytesHashed { get; init; }
    public string? CurrentPath { get; init; }
    public TimeSpan Elapsed { get; init; }

    /// <summary>Non-fatal warning to show the user (e.g. low free space).</summary>
    public string? Warning { get; init; }
}

public sealed record ExtractionResult(
    RunOutcome Outcome,
    ScanProgress Progress,
    string? LogPath,
    string? ErrorMessage);
