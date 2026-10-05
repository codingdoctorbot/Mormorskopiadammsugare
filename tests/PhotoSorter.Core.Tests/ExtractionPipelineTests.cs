using PhotoSorter.Core;
using PhotoSorter.Core.Extraction;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Tests;

public class ExtractionPipelineTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Src(string relative) => _tmp[Path.Combine("src", relative)];
    private string Extracted => _tmp[Path.Combine("dest", "Extracted")];

    private ScanOptions Options(Func<ScanOptions, ScanOptions>? tweak = null)
    {
        var options = new ScanOptions { Sources = [_tmp["src"]], Destination = _tmp["dest"] };
        return tweak is null ? options : tweak(options);
    }

    private static Task<ExtractionResult> Run(ScanOptions options) =>
        new ExtractionPipeline().RunAsync(options, null, TestContext.Current.CancellationToken);

    private string[] ExtractedFiles() =>
        Directory.Exists(Extracted)
            ? [.. Directory.EnumerateFiles(Extracted, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Extracted, f)).Order(StringComparer.Ordinal)]
            : [];

    private void CreateMessyPile()
    {
        var photo = TestFiles.Jpeg(seed: 1);
        TestFiles.Write(Src(@"Laptop\Pictures\photo1.jpg"), photo);
        TestFiles.Write(Src(@"Backup 2019\Backup of laptop\Pictures\photo1.jpg"), photo);      // duplicate
        TestFiles.Write(Src(@"Recovered\FILE0001"), TestFiles.Png(seed: 2));                    // no extension
        TestFiles.Write(Src(@"Mac\._photo1.jpg"), TestFiles.Jpeg(seed: 3));                     // macOS resource fork
        TestFiles.Write(Src(@"Web\tiny.jpg"), TestFiles.Jpeg(size: 2_000, seed: 4));            // below 10 KB
        TestFiles.Write(Src(@"Phone\VID_20160301.mp4"), TestFiles.Ftyp("isom", seed: 5));      // video
        TestFiles.Write(Src(@"Music\song.m4a"), TestFiles.Ftyp("M4A ", seed: 6));               // audio – not media
        TestFiles.Write(Src(@"Web\fake.jpg"), TestFiles.Text("<html>404</html>"));               // suspect
        TestFiles.Write(Src(@"Docs\notes.txt"), TestFiles.Text("hello"));
        TestFiles.Write(Src(@"Laptop\AppData\Local\cache\cached.jpg"), TestFiles.Jpeg(seed: 7)); // app cache
        TestFiles.Write(Src(@"Laptop\Thumbs.db"), TestFiles.Text("x"));
    }

    [Fact]
    public async Task Extracts_each_unique_photo_and_video_once()
    {
        CreateMessyPile();

        var result = await Run(Options());

        Assert.Equal(RunOutcome.Completed, result.Outcome);
        Assert.Equal(["FILE0001.png", "VID_20160301.mp4", "photo1.jpg"], ExtractedFiles());
        var p = result.Progress;
        Assert.Equal(3, p.Unique);
        Assert.Equal(1, p.Duplicates);
        Assert.Equal(1, p.Suspects);
        Assert.Equal(1, p.Skipped); // tiny.jpg
        Assert.Equal(0, p.Errors);
        Assert.Equal(1, p.VideosFound);
        Assert.True(File.Exists(result.LogPath));
    }

    [Fact]
    public async Task Copies_are_identical_and_keep_the_source_date()
    {
        var date = new DateTime(2009, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var source = TestFiles.Write(Src("a.jpg"), TestFiles.Jpeg(seed: 1), date);

        await Run(Options(o => o with { VerifyCopies = true }));

        var copy = Path.Combine(Extracted, "a.jpg");
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
        Assert.Equal(date, File.GetLastWriteTimeUtc(copy));
    }

    [Fact]
    public async Task Second_run_resumes_without_copying_anything()
    {
        CreateMessyPile();
        await Run(Options());

        var second = await Run(Options());

        Assert.Equal(0, second.Progress.Unique);
        Assert.Equal(0, second.Progress.Duplicates);
        Assert.Equal(4, second.Progress.AlreadyCataloged); // 2× photo1 + FILE0001 + video
        Assert.Equal(3, ExtractedFiles().Length);
    }

    [Fact]
    public async Task New_files_are_picked_up_on_the_next_run()
    {
        TestFiles.Write(Src("a.jpg"), TestFiles.Jpeg(seed: 1));
        await Run(Options());
        TestFiles.Write(Src("b.jpg"), TestFiles.Jpeg(seed: 2));

        var second = await Run(Options());

        Assert.Equal(1, second.Progress.Unique);
        Assert.Equal(["a.jpg", "b.jpg"], ExtractedFiles());
    }

    [Fact]
    public async Task Different_files_with_the_same_name_both_survive()
    {
        TestFiles.Write(Src(@"Camera1\IMG_0001.JPG"), TestFiles.Jpeg(seed: 1));
        TestFiles.Write(Src(@"Camera2\IMG_0001.JPG"), TestFiles.Jpeg(seed: 2));

        await Run(Options());

        Assert.Equal(["IMG_0001 (2).jpg", "IMG_0001.jpg"], ExtractedFiles());
    }

    [Fact]
    public async Task Destination_inside_a_source_is_not_scanned()
    {
        TestFiles.Write(_tmp[@"pile\a.jpg"], TestFiles.Jpeg(seed: 1));
        var options = new ScanOptions { Sources = [_tmp["pile"]], Destination = _tmp[@"pile\Sorted"] };

        await Run(options);
        var second = await new ExtractionPipeline().RunAsync(options, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, second.Progress.AlreadyCataloged);
        Assert.Equal(0, second.Progress.Unique + second.Progress.Duplicates);
    }

    [Fact]
    public async Task Dry_run_copies_nothing_and_leaves_no_catalog_changes()
    {
        CreateMessyPile();

        var dry = await Run(Options(o => o with { DryRun = true }));
        var real = await Run(Options());

        Assert.Equal(3, dry.Progress.Unique);
        Assert.Equal(3, real.Progress.Unique); // the dry run didn't record anything
    }

    [Fact]
    public async Task Videos_can_be_switched_off()
    {
        CreateMessyPile();

        var result = await Run(Options(o => o with { IncludeVideos = false }));

        Assert.DoesNotContain("VID_20160301.mp4", ExtractedFiles());
        Assert.Equal(2, result.Progress.Unique);
    }

    [Fact]
    public async Task Suspect_files_can_be_copied_to_their_own_folder()
    {
        CreateMessyPile();

        await Run(Options(o => o with { CopySuspect = true }));

        Assert.Contains(Path.Combine("_suspect", "fake.jpg"), ExtractedFiles());
    }

    [Fact]
    public async Task Reuses_a_copy_left_by_a_crashed_run()
    {
        var photo = TestFiles.Jpeg(seed: 1);
        TestFiles.Write(Src("a.jpg"), photo);
        TestFiles.Write(Path.Combine(Extracted, "a.jpg"), photo);           // copied, but never recorded
        TestFiles.Write(Path.Combine(Extracted, "b.jpg.partial"), [1, 2]);  // half-written leftover

        var result = await Run(Options());

        Assert.Equal(1, result.Progress.Unique);
        Assert.Equal(["a.jpg"], ExtractedFiles());
    }

    [Fact]
    public async Task Finds_the_catalog_of_a_destination_used_before_the_rename()
    {
        TestFiles.Write(Src("a.jpg"), TestFiles.Jpeg(seed: 1));
        await Run(Options());
        Directory.Move(_tmp[@"dest\" + DestinationLayout.AppFolderName], _tmp[@"dest\" + DestinationLayout.LegacyAppFolderName]);

        var second = await Run(Options());

        Assert.Equal(0, second.Progress.Unique);
        Assert.Equal(1, second.Progress.AlreadyCataloged);
        Assert.True(Directory.Exists(_tmp[@"dest\" + DestinationLayout.AppFolderName]));
        Assert.False(Directory.Exists(_tmp[@"dest\" + DestinationLayout.LegacyAppFolderName]));
    }

    [Fact]
    public async Task Rejects_a_source_inside_the_destination()
    {
        Directory.CreateDirectory(_tmp[@"dest\inside"]);
        var options = new ScanOptions { Sources = [_tmp[@"dest\inside"]], Destination = _tmp["dest"] };

        await Assert.ThrowsAsync<ArgumentException>(() => Run(options));
    }

    [Fact]
    public async Task Cancelling_stops_the_run()
    {
        CreateMessyPile();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await new ExtractionPipeline().RunAsync(Options(), null, cts.Token);

        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
    }
}
