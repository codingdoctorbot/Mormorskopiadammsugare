using PhotoSorter.Core.Catalog;
using PhotoSorter.Core.Extraction;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Organizing;

namespace PhotoSorter.Core.Tests;

/// <summary>Move mode (ARCHITECTURE §4.9): originals leave the sources only when that is provably safe.</summary>
public class MoveModeTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_tmp.Path, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal); // read-only test files
        _tmp.Dispose();
    }

    private string Src(string relative) => _tmp[Path.Combine("src", relative)];
    private string Dest => _tmp["dest"];
    private string Extracted => Path.Combine(Dest, "Extracted");

    private ScanOptions Options(bool move = true, bool dryRun = false) =>
        new() { Sources = [_tmp["src"]], Destination = Dest, MoveOriginals = move, DryRun = dryRun };

    private static Task<ExtractionResult> Run(ScanOptions options, bool otherDrive = false) =>
        new ExtractionPipeline { NeverRename = otherDrive }.RunAsync(options, null, Ct);

    private string[] Files(string root) =>
        Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains("_Mormorskopiadammsugare"))
                .Select(f => Path.GetRelativePath(root, f)).Order(StringComparer.Ordinal)]
            : [];

    private static readonly DateTime Date = new(2011, 8, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Two copies of one photo, a recovered PNG, a video – plus things that must never be touched.</summary>
    private Dictionary<string, byte[]> CreatePile()
    {
        var pile = new Dictionary<string, byte[]>
        {
            [@"Laptop\Pictures\photo1.jpg"] = TestFiles.Jpeg(seed: 1),
            [@"Backup 2019\Pictures\photo1.jpg"] = TestFiles.Jpeg(seed: 1),
            [@"Recovered\FILE0001"] = TestFiles.Png(seed: 2),
            [@"Phone\VID_20160301.mp4"] = TestFiles.Ftyp("isom", seed: 5),
        };
        foreach (var (path, bytes) in pile) TestFiles.Write(Src(path), bytes, Date);
        TestFiles.Write(Src(@"Docs\notes.txt"), TestFiles.Text("hello"));
        TestFiles.Write(Src(@"Web\fake.jpg"), TestFiles.Text("<html>404</html>"));   // suspect
        TestFiles.Write(Src(@"Web\tiny.jpg"), TestFiles.Jpeg(size: 2_000, seed: 4)); // too small
        return pile;
    }

    private static readonly string[] Untouched = [@"Docs\notes.txt", @"Web\fake.jpg", @"Web\tiny.jpg"];

    [Theory]
    [InlineData(false)] // same drive: rename
    [InlineData(true)]  // other drive: copy, check, delete
    public async Task Originals_leave_the_sources_and_everything_else_stays(bool otherDrive)
    {
        var pile = CreatePile();

        var result = await Run(Options(), otherDrive);

        Assert.Equal(RunOutcome.Completed, result.Outcome);
        Assert.Equal(["FILE0001.png", "VID_20160301.mp4", "photo1.jpg"], Files(Extracted));
        Assert.Equal(Untouched, Files(_tmp["src"]));
        Assert.Equal(4, result.Progress.RemovedFromSources);
        Assert.Equal(0, result.Progress.KeptInSources);
        Assert.Equal(pile[@"Recovered\FILE0001"], File.ReadAllBytes(Path.Combine(Extracted, "FILE0001.png")));
        Assert.Equal(Date, File.GetLastWriteTimeUtc(Path.Combine(Extracted, "photo1.jpg")));
    }

    [Fact]
    public async Task Restore_puts_every_original_back_even_after_organizing()
    {
        var pile = CreatePile();
        await Run(Options());
        var organizer = new Organizer();
        await organizer.ApplyAsync(await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct), null, Ct);
        var sorted = Files(Dest);
        Assert.Equal(4, Restorer.CountRemoved(Dest));

        var result = await Restorer.RestoreAsync(Dest, null, Ct);

        Assert.Equal((RunOutcome.Completed, 4, 0, 0), (result.Outcome, result.Restored, result.AlreadyThere, result.Failed));
        foreach (var (path, bytes) in pile)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Src(path)));
            Assert.Equal(Date, File.GetLastWriteTimeUtc(Src(path)));
        }
        Assert.Equal(sorted, Files(Dest));                       // the destination is not changed
        Assert.Equal(0, Restorer.CountRemoved(Dest));
    }

    [Fact]
    public async Task Program_folders_read_only_files_and_suspects_stay_but_are_copied()
    {
        TestFiles.Write(Src(@"Games\Some Game\res\splash.png"), TestFiles.Png(seed: 1));
        TestFiles.Write(Src(@"Tool\engine.dll"), TestFiles.Text("MZ"));
        TestFiles.Write(Src(@"Tool\Pictures\logo.jpg"), TestFiles.Jpeg(seed: 2));       // .dll one level up
        var readOnly = TestFiles.Write(Src(@"Photos\locked.jpg"), TestFiles.Jpeg(seed: 3));
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        TestFiles.Write(Src(@"Photos\fine.jpg"), TestFiles.Jpeg(seed: 4));

        var result = await Run(Options() with { CopySuspect = true });

        Assert.Equal(["fine.jpg", "locked.jpg", "logo.jpg", "splash.png"], Files(Extracted));
        Assert.Equal([@"Games\Some Game\res\splash.png", @"Photos\locked.jpg", @"Tool\Pictures\logo.jpg", @"Tool\engine.dll"],
            Files(_tmp["src"]));
        Assert.Equal((1, 3), (result.Progress.RemovedFromSources, result.Progress.KeptInSources));
    }

    [Fact]
    public async Task After_an_earlier_copy_run_the_originals_are_removed_without_copying_again()
    {
        CreatePile();
        await Run(Options(move: false));

        var result = await Run(Options());

        Assert.Equal((0, 4L), (result.Progress.Unique, result.Progress.RemovedFromSources));
        Assert.Equal(Untouched, Files(_tmp["src"]));
        Assert.Equal(["FILE0001.png", "VID_20160301.mp4", "photo1.jpg"], Files(Extracted));
    }

    [Fact]
    public async Task Duplicates_stay_when_the_kept_copy_was_changed_or_deleted()
    {
        CreatePile();
        await Run(Options(move: false));
        File.WriteAllBytes(Path.Combine(Extracted, "photo1.jpg"), TestFiles.Jpeg(seed: 99)); // edited in the destination
        File.Delete(Path.Combine(Extracted, "FILE0001.png"));                                  // deleted from the destination

        var result = await Run(Options());

        Assert.Equal((1L, 3L), (result.Progress.RemovedFromSources, result.Progress.KeptInSources)); // only the video went
        Assert.True(File.Exists(Src(@"Laptop\Pictures\photo1.jpg")));
        Assert.True(File.Exists(Src(@"Backup 2019\Pictures\photo1.jpg")));
        Assert.True(File.Exists(Src(@"Recovered\FILE0001")));
    }

    [Fact]
    public async Task Dry_run_in_move_mode_removes_nothing()
    {
        CreatePile();

        var result = await Run(Options(dryRun: true));

        Assert.Equal(4, result.Progress.RemovedFromSources);   // "would be removed"
        Assert.Equal(7, Files(_tmp["src"]).Length);
        Assert.Empty(Files(Extracted));
        Assert.Equal(0, Restorer.CountRemoved(Dest));
    }

    [Fact]
    public async Task Copy_mode_never_removes_anything()
    {
        CreatePile();

        var result = await Run(Options(move: false));

        Assert.Equal(0, result.Progress.RemovedFromSources);
        Assert.Equal(7, Files(_tmp["src"]).Length);
    }

    [Fact]
    public void Cloud_folders_are_never_emptied()
    {
        var cloud = _tmp["OneDrive"];
        var photo = TestFiles.Write(Path.Combine(cloud, @"Bilder\a.jpg"), TestFiles.Jpeg(seed: 1));
        var other = TestFiles.Write(_tmp[@"Backup\OneDrive\Bilder\a.jpg"], TestFiles.Jpeg(seed: 1));
        var guard = new SourceGuard([_tmp.Path], cloudRoots: [cloud]);

        Assert.Contains("cloud", guard.KeepReason(photo));
        Assert.Null(guard.KeepReason(other));                  // an old backup copy named OneDrive is not synced
    }

    [Fact]
    public async Task A_missing_original_is_only_restored_from_a_matching_copy()
    {
        CreatePile();
        await Run(Options());
        File.WriteAllBytes(Path.Combine(Extracted, "photo1.jpg"), TestFiles.Jpeg(seed: 99)); // kept copy edited since

        var result = await Restorer.RestoreAsync(Dest, null, Ct);

        Assert.Equal((2, 2), (result.Restored, result.Failed)); // both photo1 copies fail the check
        Assert.False(File.Exists(Src(@"Laptop\Pictures\photo1.jpg")));
        Assert.Equal(2, Restorer.CountRemoved(Dest));           // still listed – can be retried
    }
}
