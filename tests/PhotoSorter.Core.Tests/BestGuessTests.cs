using Microsoft.Data.Sqlite;
using PhotoSorter.Core.Catalog;
using PhotoSorter.Core.Extraction;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Organizing;

namespace PhotoSorter.Core.Tests;

public class BestGuessUnitTests
{
    [Theory]
    [InlineData(@"E:\Old\Midsommar 2018\IMG_0001.JPG", "Midsommar 2018")]
    [InlineData(@"E:\Rome 2009\DCIM\100CANON\IMG_0001.JPG", "Rome 2009")]       // generic camera folders skipped
    [InlineData(@"E:\Bilder\Fjällen\IMG_1.jpg", "Fjällen")]
    [InlineData(@"C:\Users\Someone\IMG_1.jpg", null)]                             // a user profile is not an album
    [InlineData(@"C:\Users\Someone\Pictures\IMG_1.jpg", null)]
    [InlineData(@"E:\Backup 2019\Pictures\IMG_1.jpg", null)]                      // backup folders end the search
    [InlineData(@"E:\IMG_1.jpg", null)]                                           // drive root
    [InlineData(@"E:\Old\Downloads\x (1).jpg", "Old")]                            // Downloads is generic
    [InlineData(@"E:\Trip\Camera SD card (2)\DCIM\IMG_1.jpg", "Trip")]          // device names aren't albums
    [InlineData(@"E:\Trip\SD-kort\IMG_1.jpg", "Trip")]
    [InlineData(@"E:\Trip\Minneskort\IMG_1.jpg", "Trip")]
    [InlineData(@"E:\Trip\Gamla mobilen\IMG_1.jpg", "Trip")]
    [InlineData(@"E:\Trip\iPhone backup\IMG_1.jpg", null)]                       // ("backup" ends the search)
    [InlineData(@"E:\Trip\Mobil 2004\IMG_1.jpg", "Mobil 2004")]                  // device + year: kept, it's a period
    [InlineData(@"E:\Stuff\2018\IMG_1.jpg", "Stuff")]                              // plain years skipped
    public void Album_folder(string path, string? album) => Assert.Equal(album, BestGuess.AlbumFolder(path));

    [Theory]
    [InlineData(@"E:\Gamla bilder\Desktop\IMG_1.jpg", null)]                 // only the source folder above → no album
    [InlineData(@"E:\Gamla bilder\Rome 2009\IMG_1.jpg", "Rome 2009")]       // a real folder below the source → album
    [InlineData(@"F:\Elsewhere\Trip\IMG_1.jpg", "Trip")]                    // not under any known source → unchanged
    public void Source_folders_are_never_albums(string path, string? album) =>
        Assert.Equal(album, BestGuess.AlbumFolder(path, [@"E:\Gamla bilder"]));

    [Theory]
    [InlineData(@"Extracted\IMG_1.jpg")]
    [InlineData(@"_Unknown location\2018\~Sweden\IMG_1.jpg")]
    public void Our_own_folders_are_never_albums(string path) => Assert.Null(BestGuess.AlbumFolder(path));

    [Fact]
    public void A_download_needs_every_copy_in_downloads()
    {
        Assert.Equal(BestGuess.Downloads, BestGuess.Category(MediaKind.Image, MediaFormat.Jpeg, null, [@"E:\Downloads\a.jpg", @"F:\Hämtade filer\a.jpg"]));
        Assert.Null(BestGuess.Category(MediaKind.Image, MediaFormat.Jpeg, null, [@"E:\Downloads\a (1).jpg", @"E:\Pictures\Rome\a.jpg"]));
    }

    [Fact]
    public void Album_folder_is_the_most_common_among_copies() =>
        Assert.Equal("Lofoten", BestGuess.AlbumFolder([@"E:\a\DCIM\x.jpg", @"E:\Lofoten\x.jpg", @"F:\b\Lofoten\x.jpg", @"F:\Misc\Other\x.jpg"]));

    [Theory]
    [InlineData(MediaKind.Image, MediaFormat.Png, null, @"E:\Phone\Screenshot_2019-07-04-10-20-30.png", BestGuess.Screenshots)]
    [InlineData(MediaKind.Image, MediaFormat.Jpeg, null, @"E:\Mac\Skärmavbild 2020-01-01.jpg", BestGuess.Screenshots)]
    [InlineData(MediaKind.Image, MediaFormat.Png, null, @"E:\Web\logo.png", BestGuess.Graphics)]
    [InlineData(MediaKind.Image, MediaFormat.Gif, null, @"E:\Fun\cat.gif", BestGuess.Graphics)]
    [InlineData(MediaKind.Image, MediaFormat.Png, "Apple", @"E:\Phone\IMG_1.png", null)]  // camera data → a photo
    [InlineData(MediaKind.Image, MediaFormat.Jpeg, null, @"E:\Downloads\photo.jpg", BestGuess.Downloads)]
    [InlineData(MediaKind.Image, MediaFormat.Jpeg, null, @"E:\Hämtade filer\photo.jpg", BestGuess.Downloads)]
    [InlineData(MediaKind.Image, MediaFormat.Jpeg, null, @"E:\Camera\IMG_1.jpg", null)]
    [InlineData(MediaKind.Video, MediaFormat.Mp4, null, @"E:\Camera\VID_1.mp4", null)]
    public void Category(MediaKind kind, MediaFormat format, string? make, string path, string? expected) =>
        Assert.Equal(expected, BestGuess.Category(kind, format, make, [path]));

    [Theory]
    [InlineData("shutterstock_123456789.jpg", true)]
    [InlineData("iStock-1203456789.jpg", true)]
    [InlineData("GettyImages-1234567.jpg", true)]
    [InlineData("AdobeStock_98765.jpeg", true)]
    [InlineData("pexels-photo-1234567.jpeg", true)]
    [InlineData("john-smith-AbC12dEf-unsplash.jpg", true)]
    [InlineData("funny meme 3.png", true)]
    [InlineData("image0.png", true)]                      // Discord
    [InlineData("unknown.png", true)]
    [InlineData("a1b2c3d4e5f6g.jpg", true)]               // Reddit
    [InlineData("EaB3x9KqWzT1mPq.jpg", true)]             // Twitter
    [InlineData("tumblr_n1abc2def3_1280.jpg", true)]
    [InlineData("FB_IMG_1546273920123.jpg", true)]
    [InlineData("received_10157392018472.jpeg", true)]
    [InlineData("IMG_0042.JPG", false)]                   // normal camera names
    [InlineData("DSC_1234.jpg", false)]
    [InlineData("IMG_20180715_143005.jpg", false)]
    [InlineData("Midsommar med familjen.jpg", false)]
    [InlineData("Mistock garden.jpg", false)]             // "istock" inside a word doesn't count
    [InlineData("Memento 2009.jpg", false)]
    public void Stock_and_meme_names(string name, bool expected) =>
        Assert.Equal(expected, BestGuess.IsStockOrMeme([@"E:\x\" + name]));

    [Fact]
    public void Place_guess_needs_agreeing_gps_photos_within_three_hours()
    {
        var t = new DateTime(2018, 7, 15, 12, 0, 0);
        var guesser = new BestGuess.PlaceGuesser(
        [
            new(t, "Sweden"),
            new(t.AddHours(1), "Sweden"),
            new(t.AddDays(1), "Japan"),
            new(t.AddDays(1).AddHours(2), "Sweden"),
        ]);

        Assert.Equal(("Sweden", 2), guesser.Guess(t.AddHours(2.5)));   // both Sweden photos within 3 h
        Assert.Null(guesser.Guess(t.AddHours(-4)));                    // nothing near
        Assert.Null(guesser.Guess(t.AddDays(1).AddHours(1)));          // Japan and Sweden disagree
        Assert.Equal(("Sweden", 1), guesser.Guess(t.AddDays(1).AddHours(4.5)));
    }
}

public class BestGuessOrganizerTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Src(string relative) => _tmp[Path.Combine("src", relative)];
    private string Dest => _tmp["dest"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly (double, double) Stockholm = (59.3293, 18.0686);
    private static readonly (double, double) Tokyo = (35.6762, 139.6503);

    private async Task<OrganizePlan> ExtractAndOrganizeAsync(bool countryFolders = false)
    {
        await new ExtractionPipeline().RunAsync(new ScanOptions { Sources = [_tmp["src"]], Destination = Dest }, null, Ct);
        var organizer = new Organizer();
        var plan = await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest, BestGuessUnknowns = true, CountryFolders = countryFolders }, null, Ct);
        await organizer.ApplyAsync(plan, null, Ct);
        return plan;
    }

    private string[] SortedFiles() =>
        [.. Directory.EnumerateFiles(Dest, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Dest, f))
            .Where(f => !f.StartsWith("_Mormorskopiadammsugare", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Camera_photos_without_gps_borrow_the_place_of_phone_photos_taken_the_same_day()
    {
        var noon = new DateTime(2018, 7, 15, 12, 0, 0);
        TestFiles.Write(Src(@"Phone\phone.jpg"), TestFiles.Jpeg(seed: 1, taken: noon, gps: Stockholm));
        TestFiles.Write(Src(@"Camera\DSC_0001.jpg"), TestFiles.Jpeg(seed: 2, taken: noon.AddHours(2)));
        TestFiles.Write(Src(@"Camera\DSC_0002.jpg"), TestFiles.Jpeg(seed: 3, taken: noon.AddDays(5)));

        var plan = await ExtractAndOrganizeAsync(countryFolders: true);

        Assert.Contains(@"Europe\Sweden\2018\phone.jpg", SortedFiles());
        Assert.Contains(@"_Unknown location\2018\~Sweden\DSC_0001.jpg", SortedFiles());   // same day as the phone photo
        Assert.Contains(@"_Unknown location\2018\DSC_0002.jpg", SortedFiles());           // days later: no guess
        Assert.Equal(1, plan.Guesses!.PlaceGuessed);
    }

    [Fact]
    public async Task Place_guess_uses_the_continent_without_country_folders()
    {
        var noon = new DateTime(2018, 7, 15, 12, 0, 0);
        TestFiles.Write(Src(@"Phone\phone.jpg"), TestFiles.Jpeg(seed: 1, taken: noon, gps: Stockholm));
        TestFiles.Write(Src(@"Camera\DSC_0001.jpg"), TestFiles.Jpeg(seed: 2, taken: noon.AddHours(1)));

        await ExtractAndOrganizeAsync();

        Assert.Contains(@"_Unknown location\2018\~Europe\DSC_0001.jpg", SortedFiles());
    }

    [Fact]
    public async Task Conflicting_gps_photos_mean_no_place_guess()
    {
        var noon = new DateTime(2018, 7, 15, 12, 0, 0);
        TestFiles.Write(Src(@"Phone\a.jpg"), TestFiles.Jpeg(seed: 1, taken: noon, gps: Stockholm));
        TestFiles.Write(Src(@"Phone\b.jpg"), TestFiles.Jpeg(seed: 2, taken: noon.AddHours(2), gps: Tokyo));
        TestFiles.Write(Src(@"Camera\DSC_0001.jpg"), TestFiles.Jpeg(seed: 3, taken: noon.AddHours(1)));

        await ExtractAndOrganizeAsync(countryFolders: true);

        Assert.Contains(@"_Unknown location\2018\DSC_0001.jpg", SortedFiles());
    }

    [Fact]
    public async Task Album_folders_are_kept_when_three_or_more_files_share_them()
    {
        for (var i = 1; i <= 3; i++) TestFiles.Write(Src($@"Old stuff\Midsommar 2018\IMG_{i}.jpg"), TestFiles.Jpeg(seed: i));
        for (var i = 1; i <= 3; i++) TestFiles.Write(Src($@"Rome 2009\DCIM\100CANON\IMG_{i}.JPG"), TestFiles.Jpeg(seed: 10 + i));
        TestFiles.Write(Src(@"Old stuff\Party 2017\only.jpg"), TestFiles.Jpeg(seed: 20));      // one file: no album folder
        for (var i = 1; i <= 3; i++) TestFiles.Write(Src($@"Users\Someone\x{i}.jpg"), TestFiles.Jpeg(seed: 30 + i)); // no name as album

        var plan = await ExtractAndOrganizeAsync();

        Assert.Contains(@"_Unknown location\2018\Midsommar 2018\IMG_1.jpg", SortedFiles());
        Assert.Contains(@"_Unknown location\2009\Rome 2009\IMG_3.jpg", SortedFiles());
        Assert.Contains(@"_Unknown location\2017\only.jpg", SortedFiles());
        Assert.Contains(@"_Unknown location\_Unknown year\x1.jpg", SortedFiles());
        Assert.Equal(6, plan.Guesses!.InAlbums);
    }

    [Fact]
    public async Task Screenshots_graphics_and_downloads_get_their_own_folders()
    {
        TestFiles.Write(Src(@"Phone\Screenshot_2019-07-04-10-20-30.png"), TestFiles.Png(seed: 1));
        TestFiles.Write(Src(@"Website\logo.png"), TestFiles.Png(seed: 2));
        TestFiles.Write(Src(@"Downloads\photo.jpg"), TestFiles.Jpeg(seed: 3));
        TestFiles.Write(Src(@"Camera\IMG_20120304_101010.jpg"), TestFiles.Jpeg(seed: 4));

        var plan = await ExtractAndOrganizeAsync();

        Assert.Equal(
        [
            @"_Unknown location\2012\IMG_20120304_101010.jpg",
            @"_Unknown location\_Downloads\_Unknown year\photo.jpg",
            @"_Unknown location\_Graphics\_Unknown year\logo.png",
            @"_Unknown location\_Screenshots\2019\Screenshot_2019-07-04-10-20-30.png",
        ], SortedFiles());
        Assert.Equal((1, 1, 1), (plan.Guesses!.Screenshots, plan.Guesses.Graphics, plan.Guesses.Downloads));
    }

    [Fact]
    public async Task The_source_folder_itself_never_becomes_an_album()
    {
        // Files straight under the source (only generic folders in between) – "src" must not be an album.
        for (var i = 1; i <= 3; i++) TestFiles.Write(Src($@"Desktop\IMG_{i}.jpg"), TestFiles.Jpeg(seed: i));

        await ExtractAndOrganizeAsync();

        Assert.All(SortedFiles(), f => Assert.StartsWith(@"_Unknown location\_Unknown year\IMG_", f));
    }

    [Fact]
    public async Task Stock_photos_and_memes_go_one_level_deeper_in_their_own_folder()
    {
        TestFiles.Write(Src(@"Trip\shutterstock_123456789.jpg"), TestFiles.Jpeg(seed: 1, taken: new(2015, 6, 20, 12, 0, 0), gps: Stockholm));
        TestFiles.Write(Src(@"Trip\IMG_0001.jpg"), TestFiles.Jpeg(seed: 2, taken: new(2015, 6, 20, 13, 0, 0), gps: Stockholm));
        TestFiles.Write(Src(@"Chat\image0.png"), TestFiles.Png(seed: 3));
        TestFiles.Write(Src(@"Saved\FB_IMG_1546273920123.jpg"), TestFiles.Jpeg(seed: 4));

        await new ExtractionPipeline().RunAsync(new ScanOptions { Sources = [_tmp["src"]], Destination = Dest }, null, Ct);
        var organizer = new Organizer();
        var plan = await organizer.AnalyzeAsync(new OrganizeOptions { Destination = Dest, StockAndMemesApart = true, BestGuessUnknowns = true }, null, Ct);
        await organizer.ApplyAsync(plan, null, Ct);

        Assert.Equal(
        [
            @"Europe\2015\IMG_0001.jpg",
            @"Europe\2015\_Stock & memes\shutterstock_123456789.jpg",
            // FB_IMG's number is when it was saved from Facebook, not when it was taken – deliberately not a year.
            @"_Unknown location\_Unknown year\_Stock & memes\FB_IMG_1546273920123.jpg",
            @"_Unknown location\_Unknown year\_Stock & memes\image0.png",
        ], SortedFiles());
        Assert.Equal(3, plan.Guesses!.StockAndMemes);
    }

    [Fact]
    public async Task Stock_and_memes_sit_below_country_and_lan_too()
    {
        TestFiles.Write(Src(@"Trip\shutterstock_1.jpg"), TestFiles.Jpeg(seed: 1, taken: new(2016, 5, 1, 12, 0, 0), gps: (55.6050, 13.0038)));
        TestFiles.Write(Src(@"Trip\IMG_0001.jpg"), TestFiles.Jpeg(seed: 2, taken: new(2016, 5, 1, 13, 0, 0), gps: (55.6050, 13.0038)));

        await new ExtractionPipeline().RunAsync(new ScanOptions { Sources = [_tmp["src"]], Destination = Dest }, null, Ct);
        var organizer = new Organizer();
        await organizer.ApplyAsync(await organizer.AnalyzeAsync(new OrganizeOptions
        {
            Destination = Dest, CountryFolders = true, SwedishCountyFolders = true, StockAndMemesApart = true,
        }, null, Ct), null, Ct);

        Assert.Equal(
        [
            @"Europe\Sweden\Skåne län\2016\IMG_0001.jpg",
            @"Europe\Sweden\Skåne län\2016\_Stock & memes\shutterstock_1.jpg",
        ], SortedFiles());
    }

    [Fact]
    public async Task Known_location_and_year_is_never_touched_by_guesses()
    {
        for (var i = 1; i <= 3; i++)
            TestFiles.Write(Src($@"Midsommar\IMG_{i}.jpg"), TestFiles.Jpeg(seed: i, taken: new(2015, 6, 20, 12, 0, 0), gps: Stockholm));

        await ExtractAndOrganizeAsync();

        Assert.All(SortedFiles(), f => Assert.StartsWith(@"Europe\2015\IMG_", f));
    }
}

public class CatalogMigrationTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void A_version_1_catalog_is_upgraded_in_place_step_by_step()
    {
        var path = _tmp["catalog.db"];
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE media (id INTEGER PRIMARY KEY, sha256 TEXT NOT NULL UNIQUE, size INTEGER NOT NULL,
                  kind TEXT NOT NULL, format TEXT NOT NULL, dest_path TEXT, status TEXT NOT NULL, first_seen_utc TEXT NOT NULL,
                  meta_version INTEGER NOT NULL DEFAULT 0, date_taken TEXT, date_source TEXT, latitude REAL, longitude REAL,
                  camera_make TEXT, camera_model TEXT, year INTEGER, year_source TEXT, country TEXT, continent TEXT);
                CREATE TABLE sources (id INTEGER PRIMARY KEY, media_id INTEGER NOT NULL, source_path TEXT NOT NULL UNIQUE,
                  size INTEGER NOT NULL, mtime_utc TEXT NOT NULL, ctime_utc TEXT NOT NULL);
                CREATE TABLE runs (id INTEGER PRIMARY KEY, kind TEXT NOT NULL, started_utc TEXT NOT NULL, finished_utc TEXT,
                  outcome TEXT, options_json TEXT, stats_json TEXT);
                INSERT INTO media (sha256, size, kind, format, dest_path, status, first_seen_utc)
                  VALUES ('abc', 1, 'Image', 'Jpeg', 'Extracted\a.jpg', 'Copied', '2026-01-01T00:00:00Z');
                INSERT INTO sources (media_id, source_path, size, mtime_utc, ctime_utc)
                  VALUES (1, 'C:\old.jpg', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
        }

        using (var db = CatalogDb.Open(path))
        {
            var row = Assert.Single(db.LoadCopiedMedia());
            db.SaveGuess(row.Id, "Sweden", "test", "Album", null);   // v2 – would throw if the columns were missing
            db.MarkSourceRemoved(@"C:\old.jpg", SourceRemoval.Deleted);   // v3
            Assert.Equal(1, db.CountRemovedSources());
        }

        using var check = new SqliteConnection($"Data Source={path};Pooling=False");
        check.Open();
        using var q = check.CreateCommand();
        q.CommandText = "SELECT guess_place || '/' || album FROM media; ";
        Assert.Equal("Sweden/Album", q.ExecuteScalar());
        q.CommandText = "PRAGMA user_version;";
        Assert.Equal(3L, q.ExecuteScalar());
    }
}
