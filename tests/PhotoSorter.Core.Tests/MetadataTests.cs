using PhotoSorter.Core.Metadata;

namespace PhotoSorter.Core.Tests;

public class MetadataReaderTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Reads_exif_date_and_gps_from_a_jpeg()
    {
        var path = TestFiles.Write(_tmp["a.jpg"], TestFiles.Jpeg(taken: new DateTime(2015, 6, 12, 14, 30, 5), gps: (59.3293, 18.0686)));

        var m = MetadataReader.Read(path);

        Assert.Equal(new DateTime(2015, 6, 12, 14, 30, 5), m.DateTaken);
        Assert.Equal("Exif", m.DateSource);
        Assert.Equal(59.3293, m.Latitude!.Value, 3);
        Assert.Equal(18.0686, m.Longitude!.Value, 3);
    }

    [Fact]
    public void Southern_and_western_hemispheres_are_negative()
    {
        var path = TestFiles.Write(_tmp["rio.jpg"], TestFiles.Jpeg(gps: (-22.9068, -43.1729)));

        var m = MetadataReader.Read(path);

        Assert.Equal(-22.9068, m.Latitude!.Value, 3);
        Assert.Equal(-43.1729, m.Longitude!.Value, 3);
    }

    [Fact]
    public void Camera_default_date_is_ignored()
    {
        var path = TestFiles.Write(_tmp["reset.jpg"], TestFiles.Jpeg(taken: new DateTime(2000, 1, 1, 0, 0, 0)));
        Assert.Null(MetadataReader.Read(path).DateTaken);
    }

    [Fact]
    public void Files_without_metadata_or_unreadable_files_give_empty_metadata()
    {
        Assert.Equal(MediaMetadata.Empty, MetadataReader.Read(TestFiles.Write(_tmp["plain.jpg"], TestFiles.Jpeg())));
        Assert.Equal(MediaMetadata.Empty, MetadataReader.Read(TestFiles.Write(_tmp["junk.mts"], TestFiles.TransportStream(192))));
    }

    [Theory]
    [InlineData("+59.3293+018.0686+012.000/", 59.3293, 18.0686)]   // iPhone
    [InlineData("-33.8688+151.2093/", -33.8688, 151.2093)]
    [InlineData("+4043.5-07400.0/", 40.725, -74.0)]                 // ±DDMM.m ±DDDMM.m
    public void Parses_iso6709_video_locations(string text, double lat, double lon)
    {
        var p = MetadataReader.ParseIso6709(text);
        Assert.NotNull(p);
        Assert.Equal(lat, p.Value.Lat, 4);
        Assert.Equal(lon, p.Value.Lon, 4);
    }

    [Theory]
    [InlineData("2019-07-04T12:34:56+0200", 2019, 7, 4, 12)]
    [InlineData("2019-07-04T23:10:00-05:00", 2019, 7, 4, 23)]  // keeps the local clock time, not UTC
    [InlineData("2015:06:12 14:30:05", 2015, 6, 12, 14)]
    [InlineData("Mon Jul 04 12:00:00 2016", 2016, 7, 4, 12)]
    public void Parses_video_date_formats(string text, int y, int m, int d, int h) =>
        Assert.Equal(new DateTime(y, m, d, h, MetadataReader.ParseDate(text)!.Value.Minute, MetadataReader.ParseDate(text)!.Value.Second), MetadataReader.ParseDate(text));
}

public class DateResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 5);

    private static YearResolution Resolve(DateTime? meta = null, string[]? paths = null, DateTime[]? mtimes = null, bool useFileTimes = false) =>
        DateResolver.Resolve(meta, meta is null ? null : "Exif", paths ?? [@"E:\x\file.jpg"], mtimes ?? [], useFileTimes, Now);

    [Fact]
    public void Metadata_wins() =>
        Assert.Equal(new YearResolution(2012, "Exif"), Resolve(new DateTime(2012, 5, 1), [@"E:\Rome 2009\IMG_20150101_101010.jpg"]));

    [Fact]
    public void File_name_beats_folder_name() =>
        Assert.Equal(new YearResolution(2015, "FileName"), Resolve(paths: [@"E:\Rome 2009\IMG_20150101_101010.jpg"]));

    [Fact]
    public void Oldest_copy_wins_when_names_disagree() =>
        Assert.Equal(2014, Resolve(paths: [@"E:\a\IMG-20190301-WA0001.jpg", @"E:\b\IMG_20140705_101010.jpg"]).Year);

    [Fact]
    public void Nearest_folder_year_is_used() =>
        Assert.Equal(new YearResolution(2009, "FolderName"), Resolve(paths: [@"E:\Pictures 2020\Rome 2009\DSC_0042.jpg"]));

    [Fact]
    public void Software_edit_date_ranks_below_names()
    {
        string[] paths = [@"E:\Rome 2009\DSC_0042.jpg"];
        Assert.Equal(new YearResolution(2009, "FolderName"),
            DateResolver.Resolve(new DateTime(2019, 5, 1), "ExifModified", paths, [], false, Now));
        Assert.Equal(new YearResolution(2019, "ExifModified"),
            DateResolver.Resolve(new DateTime(2019, 5, 1), "ExifModified", [@"E:\x\DSC_0042.jpg"], [], false, Now));
    }

    [Fact]
    public void Backup_folder_years_are_ignored() =>
        Assert.Equal(YearResolution.Unknown, Resolve(paths: [@"E:\Backup 2019\Pictures\DSC_0042.jpg", @"F:\Säkerhetskopia 2021\DSC_0042.jpg"]));

    [Fact]
    public void File_time_only_when_enabled()
    {
        DateTime[] mtimes = [new(2011, 3, 3, 0, 0, 0, DateTimeKind.Utc)];
        Assert.Equal(YearResolution.Unknown, Resolve(mtimes: mtimes));
        Assert.Equal(new YearResolution(2011, "FileTime"), Resolve(mtimes: mtimes, useFileTimes: true));
    }

    [Theory]
    [InlineData("IMG_20150612_143005", 2015, 6, 12)]
    [InlineData("VID_20160301_101010", 2016, 3, 1)]
    [InlineData("IMG-20160301-WA0001", 2016, 3, 1)]
    [InlineData("PXL_20210101_123456789", 2021, 1, 1)]
    [InlineData("Screenshot_2019-07-04-10-20-30", 2019, 7, 4)]
    [InlineData("2014-08-02 13.45.10", 2014, 8, 2)]
    [InlineData("WhatsApp Image 2016-03-01 at 12.00.00", 2016, 3, 1)]
    public void File_name_patterns(string name, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), FileNameDatePatterns.TryGetDate(name, Now));

    [Theory]
    [InlineData("IMG_0042")]
    [InlineData("10153567891234567_n")]   // Facebook id – long digit runs aren't dates
    [InlineData("IMG_20151345_101010")]   // month 13
    [InlineData("IMG_20300101_000000")]   // future
    [InlineData("1920x1080 wallpaper")]
    public void Non_dates_are_ignored(string name) => Assert.Null(FileNameDatePatterns.TryGetDate(name, Now));
}
