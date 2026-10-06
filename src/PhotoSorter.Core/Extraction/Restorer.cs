using System.Diagnostics;
using System.Text.Json;
using PhotoSorter.Core.Catalog;
using PhotoSorter.Core.Logging;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Extraction;

public sealed record RestoreProgress(int Done, int Total, string? CurrentPath);

public sealed record RestoreResult(
    RunOutcome Outcome, int Restored, int AlreadyThere, int Failed, TimeSpan Elapsed, string? LogPath, string? ErrorMessage);

/// <summary>
/// Undo for move mode (ARCHITECTURE §4.9): copies every removed original back to its old path from the kept
/// copy in the destination, checked against the original's SHA-256, with its old dates. The destination is
/// not changed – afterwards it is as if the run had been a plain copy.
/// </summary>
public static class Restorer
{
    /// <summary>How many originals move mode removed (0 when there is no catalog).</summary>
    public static long CountRemoved(string destination)
    {
        var layout = new DestinationLayout(destination);
        if (!CatalogDb.Exists(layout.CatalogPath)) return 0;
        using var db = CatalogDb.Open(layout.CatalogPath);
        return db.CountRemovedSources();
    }

    public static Task<RestoreResult> RestoreAsync(
        string destination, IProgress<RestoreProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Restore(destination, progress, ct), ct);

    private static RestoreResult Restore(string destination, IProgress<RestoreProgress>? progress, CancellationToken ct)
    {
        var layout = new DestinationLayout(destination);
        if (!CatalogDb.Exists(layout.CatalogPath))
            return new RestoreResult(RunOutcome.Completed, 0, 0, 0, TimeSpan.Zero, null, null);

        var clock = Stopwatch.StartNew();
        using var db = CatalogDb.Open(layout.CatalogPath);
        using var log = CsvRunLog.Create(layout.NewLogPath("restore"));
        var runId = db.StartRun("restore", JsonSerializer.Serialize(new { Destination = layout.Root }));
        var removed = db.LoadRemovedSources();
        int restored = 0, alreadyThere = 0, failed = 0;
        var outcome = RunOutcome.Completed;
        string? error = null;

        try
        {
            for (var i = 0; i < removed.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var r = removed[i];
                progress?.Report(new RestoreProgress(i, removed.Count, r.Path));
                try
                {
                    if (File.Exists(r.Path))
                    {
                        db.ClearSourceRemoved(r.Path);
                        alreadyThere++;
                        log.Write("AlreadyThere", r.Path, r.DestPath, r.Sha256, r.Size, message: "A file is already at the original place – left as it is");
                        continue;
                    }
                    var kept = r.DestPath is null ? null : layout.ToFull(r.DestPath);
                    if (kept is null || !File.Exists(kept))
                        throw new FileNotFoundException("The kept copy is missing from the destination: " + r.DestPath);

                    Directory.CreateDirectory(Path.GetDirectoryName(r.Path)!);
                    CopyService.Copy(kept, r.Path, r.MtimeUtc, r.CreationUtc, verifySha256: r.Sha256, null, ct);
                    db.ClearSourceRemoved(r.Path);
                    restored++;
                    log.Write("Restored", r.Path, r.DestPath, r.Sha256, r.Size);
                }
                catch (IOException ex) when (CopyService.IsDiskFull(ex))
                {
                    throw new IOException("The disk is full. Free up space and press Restore again – restored files are kept.", ex);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    failed++;
                    log.Write("Error", r.Path, r.DestPath, r.Sha256, r.Size, message: ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = RunOutcome.Cancelled;
            log.Write("Cancelled", message: "Restore cancelled by the user.");
        }
        catch (Exception ex)
        {
            outcome = RunOutcome.Failed;
            error = ex.Message;
            log.Write("Error", message: "Restore stopped: " + ex.Message);
        }

        progress?.Report(new RestoreProgress(restored + alreadyThere + failed, removed.Count, null));
        db.FinishRun(runId, outcome, JsonSerializer.Serialize(new { restored, alreadyThere, failed }));
        return new RestoreResult(outcome, restored, alreadyThere, failed, clock.Elapsed, log.Path, error);
    }
}
