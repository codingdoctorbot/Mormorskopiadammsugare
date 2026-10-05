using PhotoSorter.Core.Extraction;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Organizing;

namespace PhotoSorter.Core.Tests;

public class OrganizerTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Src(string relative) => _tmp[Path.Combine("src", relative)];
    private string Dest => _tmp["dest"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ExtractAsync()
    {
        var result = await new ExtractionPipeline().RunAsync(new ScanOptions { Sources = [_tmp["src"]], Destination = Dest }, null, Ct);
        Assert.Equal(RunOutcome.Completed, result.Outcome);
    }

    private string[] SortedFiles() =>
        [.. Directory.EnumerateFiles(Dest, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Dest, f))
            .Where(f => !f.StartsWith("_Mormorskopiadammsugare", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    private void CreatePile()
    {
        TestFiles.Write(Src(@"Phone\stockholm.jpg"), TestFiles.Jpeg(seed: 1, taken: new(2015, 6, 12, 14, 0, 0), gps: (59.3293, 18.0686)));
        TestFiles.Write(Src(@"Phone\tokyo.jpg"), TestFiles.Jpeg(seed: 2, gps: (35.6762, 139.6503)));          // no date
        TestFiles.Write(Src(@"Camera\IMG_20120304_101010.jpg"), TestFiles.Jpeg(seed: 3));                       // name date
        TestFiles.Write(Src(@"Rome 2009\DSC_0042.jpg"), TestFiles.Jpeg(seed: 4));                               // folder year
        TestFiles.Write(Src(@"Backup 2019\scan.jpg"), TestFiles.Jpeg(seed: 5));                                 // nothing usable
        TestFiles.Write(Src(@"Phone\VID_20160301_101010.mp4"), TestFiles.Ftyp("isom", seed: 6));               // video, name date
    }

    [Fact]
    public async Task Sorts_into_continent_and_year_folders()
    {
        CreatePile();
        await ExtractAsync();
        var organizer = new Organizer();

        var plan = await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct);
        var result = await organizer.ApplyAsync(plan, null, Ct);

        Assert.Equal(RunOutcome.Completed, result.Outcome);
        Assert.Equal(6, result.Moved);
        Assert.Equal(
        [
            @"Asia\_Unknown year\tokyo.jpg",
            @"Europe\2015\stockholm.jpg",
            @"_Unknown location\2009\DSC_0042.jpg",
            @"_Unknown location\2012\IMG_20120304_101010.jpg",
            @"_Unknown location\2016\VID_20160301_101010.mp4",
            @"_Unknown location\_Unknown year\scan.jpg",
        ], SortedFiles());
    }

    [Fact]
    public async Task Country_folders_add_a_level_only_where_the_location_is_known()
    {
        CreatePile();
        await ExtractAsync();
        var organizer = new Organizer();
        var options = new OrganizeOptions { Destination = Dest, CountryFolders = true };

        var plan = await organizer.AnalyzeAsync(options, null, Ct);
        await organizer.ApplyAsync(plan, null, Ct);

        Assert.Equal(
        [
            @"Asia\Japan\_Unknown year\tokyo.jpg",
            @"Europe\Sweden\2015\stockholm.jpg",
            @"_Unknown location\2009\DSC_0042.jpg",
            @"_Unknown location\2012\IMG_20120304_101010.jpg",
            @"_Unknown location\2016\VID_20160301_101010.mp4",
            @"_Unknown location\_Unknown year\scan.jpg",
        ], SortedFiles());
        Assert.Contains(plan.Buckets, b => b is { Directory: @"Europe\Sweden\2015", Count: 1 });
        Assert.Contains(plan.Buckets, b => b.Directory == @"_Unknown location\2009");
    }

    [Fact]
    public async Task Swedish_photos_can_get_a_county_level()
    {
        CreatePile();
        TestFiles.Write(Src(@"Phone\malmo.jpg"), TestFiles.Jpeg(seed: 7, taken: new(2016, 5, 1, 12, 0, 0), gps: (55.6050, 13.0038)));
        await ExtractAsync();
        var organizer = new Organizer();
        var options = new OrganizeOptions { Destination = Dest, CountryFolders = true, SwedishCountyFolders = true };

        var plan = await organizer.AnalyzeAsync(options, null, Ct);
        await organizer.ApplyAsync(plan, null, Ct);

        Assert.Contains(@"Europe\Sweden\Stockholms län\2015\stockholm.jpg", SortedFiles());
        Assert.Contains(@"Europe\Sweden\Skåne län\2016\malmo.jpg", SortedFiles());
        Assert.Contains(@"Asia\Japan\_Unknown year\tokyo.jpg", SortedFiles());        // other countries: no county level
        Assert.Contains(@"_Unknown location\2009\DSC_0042.jpg", SortedFiles());
        Assert.Contains(plan.Buckets, b => b is { Directory: @"Europe\Sweden\Skåne län\2016", Count: 1 });
    }

    [Fact]
    public async Task County_option_alone_does_nothing_without_country_folders()
    {
        CreatePile();
        await ExtractAsync();
        var organizer = new Organizer();

        await organizer.ApplyAsync(await organizer.AnalyzeAsync(
            new OrganizeOptions { Destination = Dest, SwedishCountyFolders = true }, null, Ct), null, Ct);

        Assert.Contains(@"Europe\2015\stockholm.jpg", SortedFiles());
    }

    [Fact]
    public async Task Switching_layout_later_just_moves_the_sorted_files()
    {
        CreatePile();
        await ExtractAsync();
        var organizer = new Organizer();
        await organizer.ApplyAsync(await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct), null, Ct);

        var withCountry = await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest, CountryFolders = true }, null, Ct);
        await organizer.ApplyAsync(withCountry, null, Ct);
        var back = await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct);
        await organizer.ApplyAsync(back, null, Ct);

        Assert.Equal(2, withCountry.Moves.Count); // only the two files with GPS change folder
        Assert.Equal(2, back.Moves.Count);
        Assert.Contains(@"Europe\2015\stockholm.jpg", SortedFiles());
        Assert.Equal(6, SortedFiles().Length);
        Assert.DoesNotContain(SortedFiles(), f => f.Contains("(2)"));
    }

    [Fact]
    public async Task Preview_counts_files_per_folder_and_moves_nothing()
    {
        CreatePile();
        await ExtractAsync();

        var plan = await new Organizer().AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct);

        Assert.Equal(6, plan.Moves.Count);
        Assert.Contains(plan.Buckets, b => b is { Directory: @"Europe\2015", Count: 1 });
        Assert.Contains(plan.Buckets, b => b is { Directory: @"_Unknown location\_Unknown year", Count: 1 });
        Assert.All(SortedFiles(), f => Assert.StartsWith("Extracted", f));
    }

    [Fact]
    public async Task Running_again_moves_nothing()
    {
        CreatePile();
        await ExtractAsync();
        var organizer = new Organizer();
        await organizer.ApplyAsync(await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct), null, Ct);

        var again = await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct);

        Assert.Empty(again.Moves);
        Assert.Equal(6, again.AlreadyInPlace);
    }

    [Fact]
    public async Task Later_extract_then_organize_keeps_everything_consistent()
    {
        CreatePile();
        await ExtractAsync();
        var organizer = new Organizer();
        await organizer.ApplyAsync(await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct), null, Ct);

        // A new backup turns up with a copy of an organized photo plus a new one with the same name
        TestFiles.Write(Src(@"Old laptop\stockholm.jpg"), TestFiles.Jpeg(seed: 1, taken: new(2015, 6, 12, 14, 0, 0), gps: (59.3293, 18.0686)));
        TestFiles.Write(Src(@"Old laptop\2015\stockholm.jpg"), TestFiles.Jpeg(seed: 99, taken: new(2015, 8, 1, 9, 0, 0), gps: (59.3293, 18.0686)));
        await ExtractAsync();
        var plan = await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct);
        await organizer.ApplyAsync(plan, null, Ct);

        Assert.Single(plan.Moves);
        Assert.Contains(@"Europe\2015\stockholm (2).jpg", SortedFiles());
        Assert.Equal(7, SortedFiles().Length);
    }

    [Fact]
    public async Task Takes_the_best_name_among_copies()
    {
        var photo = TestFiles.Jpeg(seed: 1, taken: new(2015, 6, 12, 14, 0, 0));
        TestFiles.Write(Src(@"A\Recovered\FILE0043.jpg"), photo);  // seen first
        TestFiles.Write(Src(@"B\Pictures\Midsommar.jpg"), photo);
        await ExtractAsync();
        var organizer = new Organizer();

        await organizer.ApplyAsync(await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct), null, Ct);

        Assert.Equal([@"_Unknown location\2015\Midsommar.jpg"], SortedFiles());
    }

    [Fact]
    public async Task A_photo_edited_after_extraction_keeps_its_name_in_both_versions()
    {
        var path = TestFiles.Write(Src(@"Rome 2009\DSC_0042.jpg"), TestFiles.Jpeg(seed: 1));
        await ExtractAsync();
        // Edited in place: same path, new content → the source path now belongs to the new version only
        TestFiles.Write(path, TestFiles.Jpeg(seed: 2), DateTime.UtcNow);
        await ExtractAsync();
        var organizer = new Organizer();

        await organizer.ApplyAsync(await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct), null, Ct);

        Assert.Equal(
        [
            @"_Unknown location\2009\DSC_0042.jpg",           // the edited version, still linked to its folder
            @"_Unknown location\_Unknown year\DSC_0042.jpg",  // the original: no source left, keeps its own name
        ], SortedFiles());
    }

    [Fact]
    public async Task Without_a_catalog_it_asks_to_extract_first()
    {
        Directory.CreateDirectory(Dest);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Organizer().AnalyzeAsync(new OrganizeOptions { Destination = Dest }, null, Ct));
    }
}
