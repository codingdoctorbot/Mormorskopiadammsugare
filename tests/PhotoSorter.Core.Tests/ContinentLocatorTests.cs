using PhotoSorter.Core.Geo;

namespace PhotoSorter.Core.Tests;

public class ContinentLocatorTests
{
    private static readonly ContinentLocator Locator = ContinentLocator.Default;

    [Theory]
    [InlineData("Stockholm", 59.3293, 18.0686, "Europe")]
    [InlineData("Paris", 48.8566, 2.3522, "Europe")]
    [InlineData("Moscow", 55.7558, 37.6173, "Europe")]
    [InlineData("Novosibirsk", 55.0084, 82.9357, "Asia")]
    [InlineData("Anadyr, Chukotka (west of 180°)", 64.7337, 177.5089, "Asia")]
    [InlineData("Istanbul old town (European side)", 41.0086, 28.9802, "Europe")]
    [InlineData("Kadıköy, Istanbul (Asian side)", 40.9903, 29.0290, "Asia")]
    [InlineData("Ankara", 39.9334, 32.8597, "Asia")]
    [InlineData("Tokyo", 35.6762, 139.6503, "Asia")]
    [InlineData("Sydney", -33.8688, 151.2093, "Oceania")]
    [InlineData("Auckland", -36.8485, 174.7633, "Oceania")]
    [InlineData("New York", 40.7128, -74.0060, "North America")]
    [InlineData("Honolulu", 21.3069, -157.8583, "North America")]
    [InlineData("Rio de Janeiro", -22.9068, -43.1729, "South America")]
    [InlineData("Cairo", 30.0444, 31.2357, "Africa")]
    [InlineData("Cape Town", -33.9249, 18.4241, "Africa")]
    [InlineData("Réunion (France)", -21.1151, 55.5364, "Africa")]
    [InlineData("Martinique (France)", 14.6415, -61.0242, "North America")]
    [InlineData("Cayenne, French Guiana (France)", 4.9224, -52.3135, "South America")]
    [InlineData("Maldives", 4.1755, 73.5093, "Asia")]
    [InlineData("Longyearbyen, Svalbard", 78.2232, 15.6267, "Europe")]
    [InlineData("McMurdo Station", -77.8419, 166.6863, "Antarctica")]
    public void Finds_the_continent(string place, double lat, double lon, string continent)
    {
        var match = Locator.Locate(lat, lon);
        Assert.True(match is not null, $"{place}: no match");
        Assert.Equal(continent, match.Continent);
    }

    [Fact]
    public void A_boat_just_off_the_coast_matches_the_nearest_land()
    {
        var match = Locator.Locate(57.70, 11.70); // Kattegat, ~10 km off Gothenburg
        Assert.NotNull(match);
        Assert.Equal("Europe", match.Continent);
        Assert.True(match.DistanceKm > 0);
    }

    [Theory]
    [InlineData(0, 0)]            // "no fix" written as zeros
    [InlineData(0, -150)]         // mid-Pacific
    [InlineData(-45, -30)]        // South Atlantic
    [InlineData(91, 0)]           // invalid
    public void Open_ocean_and_bad_values_are_unknown(double lat, double lon) => Assert.Null(Locator.Locate(lat, lon));

    [Fact]
    public void Reports_the_country()
    {
        var match = Locator.Locate(59.3293, 18.0686);
        Assert.Equal(("SE", "Sweden"), (match?.CountryCode, match?.CountryName));
    }
}
