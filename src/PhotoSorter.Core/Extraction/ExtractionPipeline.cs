using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Channels;
using PhotoSorter.Core.Catalog;
using PhotoSorter.Core.Detection;
using PhotoSorter.Core.Hashing;
using PhotoSorter.Core.Logging;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Scanning;

namespace PhotoSorter.Core.Extraction;

/// <summary>
/// Phase 1 – Extract (ARCHITECTURE §3–4). Producer/consumer pipeline:
/// enumerator (1 thread) → classify + hash workers (N) → writer (1 thread: de-dup, copy, catalog).
/// Sources are only read – except in move mode (§4.9), where originals are removed once safely kept.
/// </summary>
public sealed class ExtractionPipeline
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Tests: treat every source as being on another drive (copy + check + delete instead of rename).</summary>
    internal bool NeverRename { get; init; }

    public async Task<ExtractionResult> RunAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        Validate(options);
        var layout = new DestinationLayout(options.Destination);
        var state = new ProgressState();

        Directory.CreateDirectory(layout.Root);
        layout.MigrateLegacyAppFolder();
        using var log = CsvRunLog.Create(layout.NewLogPath(options.DryRun ? "extract-dryrun" : "extract"));
        using var db = options.DryRun ? CatalogDb.OpenInMemoryCopy(layout.CatalogPath) : CatalogDb.Open(layout.CatalogPath);
        if (!options.DryRun) CopyService.DeleteLeftoverPartials(layout.Extracted);

        var ctx = new Context(options, layout, db, log, state, new FileEnumerator(options))
        {
            SourceIndex = db.LoadSourceIndex(),
            ExtractedNames = NameResolver.ForDirectory(layout.Extracted),
            SuspectNames = NameResolver.ForDirectory(layout.Suspect),
            Guard = options.MoveOriginals ? new SourceGuard(FileEnumerator.NormalizeSources(options.Sources)) : null,
            NeverRename = NeverRename,
        };
        var runKind = (options.DryRun ? "extract-dryrun" : "extract") + (options.MoveOriginals ? "-move" : "");
        var runId = db.StartRun(runKind, JsonSerializer.Serialize(options));

        using var stopReporting = new CancellationTokenSource();
        var reporter = ReportProgressAsync(state, progress, stopReporting.Token);

        var outcome = RunOutcome.Completed;
        string? error = null;
        try
        {
            state.Phase = ScanPhase.Counting;
            var (files, mediaBytes) = await Task.Run(() => ctx.Enumerator.Count(ct), ct);
            state.TotalFiles = files;
            state.Warning = CheckFreeSpace(layout, mediaBytes);

            state.Phase = ScanPhase.Extracting;
            await RunPipelineAsync(ctx, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = RunOutcome.Cancelled;
            log.Write("Cancelled", message: "Run cancelled by the user.");
        }
        catch (Exception ex)
        {
            outcome = RunOutcome.Failed;
            error = ex.Message;
            log.Write("Error", message: "Run stopped: " + ex.Message);
        }
        finally
        {
            state.Phase = ScanPhase.Finished;
            state.CurrentPath = null;
            await stopReporting.CancelAsync();
            await reporter;
        }

        var final = state.Snapshot();
        db.FinishRun(runId, outcome, JsonSerializer.Serialize(final));
        progress?.Report(final);
        return new ExtractionResult(outcome, final, log.Path, error);
    }

    private static async Task RunPipelineAsync(Context ctx, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linked.Token;
        var candidates = Channel.CreateBounded<FileCandidate>(new BoundedChannelOptions(1024) { SingleWriter = true });
        var hashed = Channel.CreateBounded<HashedFile>(new BoundedChannelOptions(64) { SingleReader = true });

        // Any stage failing stops the others.
        async Task Guarded(Func<Task> body)
        {
            try { await body(); }
            catch { await linked.CancelAsync(); throw; }
        }

        var producer = Task.Run(() => Guarded(async () =>
        {
            try
            {
                foreach (var c in ctx.Enumerator.Enumerate(token))
                    await candidates.Writer.WriteAsync(c, token);
            }
            finally { candidates.Writer.TryComplete(); }
        }));

        var workers = Enumerable.Range(0, Math.Clamp(ctx.Options.Parallelism, 1, 16))
            .Select(_ => Task.Run(() => Guarded(async () =>
            {
                await foreach (var c in candidates.Reader.ReadAllAsync(token))
                {
                    if (ProcessCandidate(ctx, c, token) is { } item)
                        await hashed.Writer.WriteAsync(item, token);
                }
            })))
            .ToArray();
        var workersDone = Task.WhenAll(workers).ContinueWith(_ => hashed.Writer.TryComplete(), TaskScheduler.Default);

        var writer = Task.Run(() => Guarded(async () =>
        {
            await foreach (var item in hashed.Reader.ReadAllAsync(token))
                WriteItem(ctx, item, token);
        }));

        Task[] all = [producer, .. workers, writer];
        try
        {
            await Task.WhenAll(all);
        }
        catch
        {
            // Surface the real failure, not the cancellations it caused in the other stages.
            var cause = all.Where(t => t.IsFaulted)
                .SelectMany(t => t.Exception!.InnerExceptions)
                .FirstOrDefault(e => e is not OperationCanceledException);
            if (cause is not null) ExceptionDispatchInfo.Throw(cause);
            throw;
        }
        finally
        {
            await workersDone;
        }
    }

    /// <summary>Worker: filter, classify by content, hash. Returns null when the file is not wanted.</summary>
    private static HashedFile? ProcessCandidate(Context ctx, FileCandidate c, CancellationToken ct)
    {
        var s = ctx.State;
        Interlocked.Increment(ref s.FilesSeen);
        s.CurrentPath = c.Path;

        var ext = Path.GetExtension(c.Path);
        if (ExtensionRegistry.IsKnownOther(ext)) return null;

        if (c.Size == 0 || c.Size < ctx.Options.MinSizeBytes)
        {
            if (ExtensionRegistry.ClaimedFormat(ext) is not null)
            {
                Interlocked.Increment(ref s.Skipped);
                ctx.Log.Write("Skipped", c.Path, size: c.Size, message: "Smaller than the minimum size");
            }
            return null;
        }

        var knownSource = ctx.SourceIndex.TryGetValue(c.Path, out var known) && known.Size == c.Size && known.MtimeUtc == c.LastWriteUtc;
        if (knownSource && !ctx.Options.MoveOriginals)
        {
            Interlocked.Increment(ref s.AlreadyCataloged);
            return null;
        }
        // Move mode re-reads files copied by earlier runs: their content must be checked before they're removed.

        try
        {
            using var stream = FileHasher.OpenRead(c.Path);
            Span<byte> header = stackalloc byte[SignatureSniffer.HeaderLength];
            var headerLength = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            var cls = FileClassifier.Classify(Path.GetFileName(c.Path), header[..headerLength]);

            if (cls.Result == ClassificationResult.Other) return null;

            if (cls.Result == ClassificationResult.Media)
                Interlocked.Increment(ref cls.Kind == MediaKind.Image ? ref s.PhotosFound : ref s.VideosFound);

            if (!IsIncluded(ctx.Options, cls.Kind))
            {
                Interlocked.Increment(ref s.Skipped);
                return null;
            }

            if (cls.Result == ClassificationResult.Suspect)
            {
                Interlocked.Increment(ref s.Suspects);
                if (!ctx.Options.CopySuspect)
                {
                    ctx.Log.Write("Suspect", c.Path, size: c.Size, format: cls.Format.ToString(),
                        message: "Extension says photo/video but the content doesn't match");
                    return null;
                }
            }

            stream.Position = 0;
            var sha = FileHasher.ComputeSha256(stream, n => Interlocked.Add(ref s.BytesHashed, n), ct);
            return new HashedFile(c, cls, sha, knownSource);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref s.Errors);
            ctx.Log.Write("Error", c.Path, size: c.Size, message: ex.Message);
            return null;
        }
    }

    /// <summary>Writer (single thread): duplicate check, copy (or move), catalog.</summary>
    private static void WriteItem(Context ctx, HashedFile item, CancellationToken ct)
    {
        var (c, cls, sha, knownSource) = item;
        var s = ctx.State;
        var suspect = cls.Result == ClassificationResult.Suspect;
        try
        {
            // Move mode: decided before anything is written, so a "keep" never depends on what happens next.
            var keepReason = ctx.Guard is null ? null : suspect ? "Not a real photo/video (suspect)" : ctx.Guard.KeepReason(c.Path);

            if (ctx.Db.FindByHash(sha) is { } existing)
            {
                ctx.Db.UpsertSource(existing.Id, c);
                if (knownSource)
                {
                    Interlocked.Increment(ref s.AlreadyCataloged);
                }
                else
                {
                    Interlocked.Increment(ref s.Duplicates);
                    ctx.Log.Write("Duplicate", c.Path, existing.DestPath, sha, c.Size, cls.Format.ToString());
                }
                if (!ctx.Options.DryRun) KeepOldestTimestamp(ctx.Layout, existing.DestPath, c);

                if (ctx.Guard is null) return;
                if (keepReason is null && !ctx.Options.DryRun && !KeptCopyIsIntact(ctx, existing, ct))
                    keepReason = "The kept copy in the destination is missing or was changed";
                if (keepReason is not null) KeepInSource(ctx, c, keepReason);
                else RemoveSource(ctx, c, SourceRemoval.Deleted, existing.DestPath, "Duplicate – the kept copy was checked");
                return;
            }

            var dir = suspect ? ctx.Layout.Suspect : ctx.Layout.Extracted;
            var (name, alreadyThere) = ChooseName(ctx, suspect ? ctx.SuspectNames : ctx.ExtractedNames, dir, c, cls, sha, ct);
            var fullPath = Path.Combine(dir, name);
            var relative = ctx.Layout.ToRelative(fullPath);
            var status = suspect ? CatalogStatus.Suspect : CatalogStatus.Copied;
            var move = ctx.Guard is not null && keepReason is null;

            if (move && !ctx.Options.DryRun && !alreadyThere && ctx.SameDrive(c.Path))
            {
                // Same drive: a rename – instant, and the content can't change on the way.
                RenameIntoDestination(ctx, c, cls, sha, fullPath, relative, status);
                Interlocked.Increment(ref s.Unique);
                Interlocked.Increment(ref s.RemovedFromSources);
                ctx.Log.Write("Moved", c.Path, relative, sha, c.Size, cls.Format.ToString(), "Moved (same drive)");
                return;
            }

            if (ctx.Options.DryRun)
            {
                Interlocked.Add(ref s.BytesCopied, c.Size);
            }
            else if (!alreadyThere)
            {
                Directory.CreateDirectory(dir);
                s.CurrentPath = c.Path;
                // Move mode always checks the copy: the original is about to be deleted.
                CopyService.Copy(c.Path, fullPath, c.LastWriteUtc, c.CreationUtc,
                    ctx.Options.VerifyCopies || move ? sha : null, n => Interlocked.Add(ref s.BytesCopied, n), ct);
            }

            long id;
            using (var tx = ctx.Db.BeginTransaction())
            {
                id = ctx.Db.AddMedia(sha, c.Size, cls.Kind, cls.Format, relative, status);
                ctx.Db.UpsertSource(id, c);
                tx.Commit();
            }
            Interlocked.Increment(ref s.Unique);
            ctx.Log.Write(suspect ? "CopiedSuspect" : "Copied", c.Path, relative, sha, c.Size, cls.Format.ToString(),
                alreadyThere ? "Already in place from an interrupted run" : null);

            if (ctx.Guard is null) return;
            if (keepReason is not null) KeepInSource(ctx, c, keepReason);
            else
            {
                ctx.VerifiedKept.Add(id); // just checked byte for byte (or matched by hash when already there)
                RemoveSource(ctx, c, SourceRemoval.Moved, relative, "Copied and checked, original removed");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex) when (CopyService.IsDiskFull(ex))
        {
            throw new IOException("The destination disk is full. Free up space and run again – finished files are kept.", ex);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref s.Errors);
            ctx.Log.Write("Error", c.Path, sha256: sha, size: c.Size, message: ex.Message);
        }
    }

    // ---------------- Move mode (§4.9) ----------------

    /// <summary>
    /// Same-drive move: the catalog rows (incl. "removed") and the rename happen in one transaction. If the rename
    /// fails nothing is recorded; if recording fails after the rename, the file is renamed back.
    /// </summary>
    private static void RenameIntoDestination(
        Context ctx, FileCandidate c, Classification cls, string sha, string fullPath, string relative, CatalogStatus status)
    {
        if (!IsUnchanged(c)) throw new IOException("The file changed while it was being read – it was left where it is.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        long id;
        using (var tx = ctx.Db.BeginTransaction())
        {
            id = ctx.Db.AddMedia(sha, c.Size, cls.Kind, cls.Format, relative, status);
            ctx.Db.UpsertSource(id, c);
            ctx.Db.MarkSourceRemoved(c.Path, SourceRemoval.Moved);
            File.Move(c.Path, fullPath);
            try
            {
                tx.Commit();
            }
            catch
            {
                File.Move(fullPath, c.Path);
                throw;
            }
        }
        ctx.VerifiedKept.Add(id);
    }

    /// <summary>Records the removal first, then deletes. If the delete fails the record is taken back.</summary>
    private static void RemoveSource(Context ctx, FileCandidate c, SourceRemoval how, string? keptRelative, string note)
    {
        if (!ctx.Options.DryRun)
        {
            if (!IsUnchanged(c))
            {
                KeepInSource(ctx, c, "The file changed while it was being read");
                return;
            }
            ctx.Db.MarkSourceRemoved(c.Path, how);
            try
            {
                File.Delete(c.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ctx.Db.ClearSourceRemoved(c.Path);
                KeepInSource(ctx, c, "Could not remove it: " + ex.Message);
                return;
            }
        }
        Interlocked.Increment(ref ctx.State.RemovedFromSources);
        ctx.Log.Write(how == SourceRemoval.Moved ? "Moved" : "Removed", c.Path, keptRelative, size: c.Size,
            message: ctx.Options.DryRun ? "Would be removed from the source (dry run)" : note);
    }

    private static void KeepInSource(Context ctx, FileCandidate c, string reason)
    {
        Interlocked.Increment(ref ctx.State.KeptInSources);
        ctx.Log.Write("KeptInSource", c.Path, size: c.Size, message: reason);
    }

    /// <summary>Re-hashes the kept copy once per run before any duplicate of it is deleted.</summary>
    private static bool KeptCopyIsIntact(Context ctx, MediaRecord kept, CancellationToken ct)
    {
        if (ctx.VerifiedKept.Contains(kept.Id)) return true;
        if (kept.DestPath is null) return false;
        var full = ctx.Layout.ToFull(kept.DestPath);
        if (!File.Exists(full) || FileHasher.ComputeSha256(full, ct) != kept.Sha256) return false;
        ctx.VerifiedKept.Add(kept.Id);
        return true;
    }

    /// <summary>Same size and date as when it was read – nobody edited it in between.</summary>
    private static bool IsUnchanged(FileCandidate c)
    {
        var now = new FileInfo(c.Path);
        return now.Exists && now.Length == c.Size && now.LastWriteTimeUtc == c.LastWriteUtc;
    }

    /// <summary>
    /// Free name in the target folder. If a taken name already holds exactly this content (copied by a run
    /// that crashed before recording it), reuse that file instead of making a second copy.
    /// </summary>
    private static (string Name, bool AlreadyThere) ChooseName(
        Context ctx, NameResolver names, string dir, FileCandidate c, Classification cls, string sha, CancellationToken ct)
    {
        foreach (var name in NameResolver.Candidates(Path.GetFileNameWithoutExtension(c.Path), cls.Extension))
        {
            if (!names.IsTaken(name))
            {
                names.Add(name);
                return (name, false);
            }
            if (ctx.Options.DryRun) continue;

            var existing = new FileInfo(Path.Combine(dir, name));
            if (existing.Exists && existing.Length == c.Size && FileHasher.ComputeSha256(existing.FullName, ct) == sha)
                return (name, true);
        }
        throw new UnreachableException();
    }

    /// <summary>Duplicates may disagree on dates; the oldest is most likely the original (§4.5).</summary>
    private static void KeepOldestTimestamp(DestinationLayout layout, string? destPath, FileCandidate c)
    {
        if (destPath is null) return;
        try
        {
            var full = layout.ToFull(destPath);
            if (File.Exists(full) && c.LastWriteUtc < File.GetLastWriteTimeUtc(full))
            {
                File.SetLastWriteTimeUtc(full, c.LastWriteUtc);
                if (c.CreationUtc < File.GetCreationTimeUtc(full)) File.SetCreationTimeUtc(full, c.CreationUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cosmetic only – never fail a run over a timestamp.
        }
    }

    private static bool IsIncluded(ScanOptions options, MediaKind kind) =>
        kind == MediaKind.Image ? options.IncludePhotos : options.IncludeVideos;

    private static string? CheckFreeSpace(DestinationLayout layout, long mediaBytes)
    {
        try
        {
            var free = new DriveInfo(Path.GetPathRoot(layout.Root)!).AvailableFreeSpace;
            if (free < mediaBytes)
                return $"The destination has {ByteSize.Format(free)} free, and up to {ByteSize.Format(mediaBytes)} " +
                       "of photos/videos were found (before removing duplicates). It may run out of space.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Network paths etc. – skip the check.
        }
        return null;
    }

    private static void Validate(ScanOptions options)
    {
        if (options.Sources.Count == 0) throw new ArgumentException("Add at least one source folder.");
        if (string.IsNullOrWhiteSpace(options.Destination)) throw new ArgumentException("Choose a destination folder.");
        if (!Path.IsPathFullyQualified(options.Destination))
            throw new ArgumentException("The destination must be a full path, like D:\\Photos.");

        var missing = options.Sources.Where(s => !Directory.Exists(s)).ToList();
        if (missing.Count > 0) throw new ArgumentException("Source folder not found: " + string.Join(", ", missing));

        var dest = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Destination)) + Path.DirectorySeparatorChar;
        foreach (var source in FileEnumerator.NormalizeSources(options.Sources))
            if (source.StartsWith(dest, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The source \"{source}\" is inside the destination. Pick folders outside it.");
    }

    private static async Task ReportProgressAsync(ProgressState state, IProgress<ScanProgress>? progress, CancellationToken stop)
    {
        if (progress is null) return;
        using var timer = new PeriodicTimer(ProgressInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop)) progress.Report(state.Snapshot());
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
    }

    /// <param name="KnownSource">Recorded by an earlier run with the same size and date (move mode only).</param>
    private sealed record HashedFile(FileCandidate Candidate, Classification Classification, string Sha256, bool KnownSource);

    private sealed class Context(
        ScanOptions options, DestinationLayout layout, CatalogDb db, CsvRunLog log, ProgressState state, FileEnumerator enumerator)
    {
        public ScanOptions Options { get; } = options;
        public DestinationLayout Layout { get; } = layout;
        public CatalogDb Db { get; } = db;
        public CsvRunLog Log { get; } = log;
        public ProgressState State { get; } = state;
        public FileEnumerator Enumerator { get; } = enumerator;
        public required Dictionary<string, (long Size, DateTime MtimeUtc)> SourceIndex { get; init; }
        public required NameResolver ExtractedNames { get; init; }
        public required NameResolver SuspectNames { get; init; }

        /// <summary>Move mode only; null when copying.</summary>
        public SourceGuard? Guard { get; init; }

        public bool NeverRename { get; init; }

        /// <summary>Kept copies already checked in this run (move mode).</summary>
        public HashSet<long> VerifiedKept { get; } = [];

        private readonly string _destinationDrive = Path.GetPathRoot(layout.Root) ?? "";

        public bool SameDrive(string path) =>
            !NeverRename && _destinationDrive.Length > 0 &&
            string.Equals(Path.GetPathRoot(path), _destinationDrive, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Counters shared by all threads; read as a <see cref="ScanProgress"/> snapshot.</summary>
    private sealed class ProgressState
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public volatile ScanPhase Phase;
        public volatile string? CurrentPath;
        public volatile string? Warning;
        public long TotalFiles;
        public long FilesSeen, PhotosFound, VideosFound, Unique, Duplicates, AlreadyCataloged;
        public long Skipped, Suspects, Errors, BytesCopied, BytesHashed, RemovedFromSources, KeptInSources;

        public ScanProgress Snapshot() => new()
        {
            Phase = Phase,
            TotalFiles = Interlocked.Read(ref TotalFiles),
            FilesSeen = Interlocked.Read(ref FilesSeen),
            PhotosFound = Interlocked.Read(ref PhotosFound),
            VideosFound = Interlocked.Read(ref VideosFound),
            Unique = Interlocked.Read(ref Unique),
            Duplicates = Interlocked.Read(ref Duplicates),
            AlreadyCataloged = Interlocked.Read(ref AlreadyCataloged),
            Skipped = Interlocked.Read(ref Skipped),
            Suspects = Interlocked.Read(ref Suspects),
            Errors = Interlocked.Read(ref Errors),
            RemovedFromSources = Interlocked.Read(ref RemovedFromSources),
            KeptInSources = Interlocked.Read(ref KeptInSources),
            BytesCopied = Interlocked.Read(ref BytesCopied),
            BytesHashed = Interlocked.Read(ref BytesHashed),
            CurrentPath = CurrentPath,
            Elapsed = _clock.Elapsed,
            Warning = Warning,
        };
    }
}
