// GeoPrep – builds the offline continent lookup data (ARCHITECTURE §5.3).
//
//   dotnet run --project tools/GeoPrep [countries.geojson] [admin1.geojson]
//
// Without arguments both GeoJSON files are downloaded from the Natural Earth repository (public domain):
// admin-0 countries 1:50m, and admin-1 regions 1:10m (only the countries in RegionCountries are kept –
// today Sweden's 21 län).
// Output: src/PhotoSorter.Core/Geo/countries.gz (embedded into PhotoSorter.Core).
//
// Format (gzip): "PSGEO3" | int32 n + n continent names | int32 countries,
//   per country: string iso2, string name, int32 parts,
//   per part: byte continent, string code, string name ("" = the country's), rings
// | int32 regions, per region: string country iso2, string code, string name, int32 parts, per part: rings
//   rings = int32 count, per ring: int32 points + points × (float32 lon, float32 lat)

using System.IO.Compression;
using System.Text.Json;

const string CountriesUrl =
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_countries.geojson";
const string RegionsUrl =
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_1_states_provinces.geojson";
string[] continents = ["Africa", "Antarctica", "Asia", "Europe", "North America", "Oceania", "South America"];
string[] regionCountries = ["SE"]; // countries that get a region level (Sweden: län)

var repoRoot = FindRepoRoot();
var output = Path.Combine(repoRoot, "src", "PhotoSorter.Core", "Geo", "countries.gz");

using var http = new HttpClient();
async Task<string> LoadJson(int arg, string url)
{
    if (args.Length > arg) return await File.ReadAllTextAsync(args[arg]);
    Console.WriteLine($"Downloading {url}");
    return await http.GetStringAsync(url);
}

using var doc = JsonDocument.Parse(await LoadJson(0, CountriesUrl));
using var regionDoc = JsonDocument.Parse(await LoadJson(1, RegionsUrl));
var countries = new List<Country>();
foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
{
    var props = feature.GetProperty("properties");
    var admin = props.GetProperty("ADMIN").GetString()!;
    var name = FriendlyNames.GetValueOrDefault(admin, admin);
    var code = props.GetProperty("ISO_A2_EH").GetString() is { } iso && iso != "-99"
        ? iso
        : props.GetProperty("ADM0_A3").GetString()!;
    var baseContinent = BaseContinent(props);
    if (baseContinent is null)
    {
        Console.WriteLine($"  skipped (no continent): {name}");
        continue;
    }

    var parts = new List<Part>();
    foreach (var rings in Polygons(feature.GetProperty("geometry")))
    {
        var (cLon, cLat) = (rings[0].Average(p => p.Lon), rings[0].Average(p => p.Lat));
        var overseas = Overseas(code, cLon, cLat);
        parts.Add(new Part(Array.IndexOf(continents, overseas?.Continent ?? baseContinent), overseas?.Code ?? "", overseas?.Name ?? "", rings));
    }
    countries.Add(new Country(code, name, parts));
}

// Regions (Sweden's län): ISO 3166-2 code and the Swedish name, e.g. SE-O "Västra Götalands län".
var regions = new List<Region>();
foreach (var feature in regionDoc.RootElement.GetProperty("features").EnumerateArray())
{
    var props = feature.GetProperty("properties");
    var country = props.GetProperty("iso_a2").GetString();
    if (country is null || !regionCountries.Contains(country)) continue;
    var name = props.TryGetProperty("name_sv", out var sv) && sv.GetString() is { Length: > 0 } s ? s : props.GetProperty("name").GetString()!;
    regions.Add(new Region(country, props.GetProperty("iso_3166_2").GetString()!, name, Polygons(feature.GetProperty("geometry"))));
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await using (var file = File.Create(output))
await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
await using (var w = new BinaryWriter(gzip))
{
    w.Write("PSGEO3"u8);
    w.Write(continents.Length);
    foreach (var c in continents) w.Write(c);
    w.Write(countries.Count);
    foreach (var country in countries)
    {
        w.Write(country.Code);
        w.Write(country.Name);
        w.Write(country.Parts.Count);
        foreach (var part in country.Parts)
        {
            w.Write((byte)part.Continent);
            w.Write(part.Code);
            w.Write(part.Name);
            WriteRings(w, part.Rings);
        }
    }
    w.Write(regions.Count);
    foreach (var region in regions.OrderBy(r => r.Code, StringComparer.Ordinal))
    {
        w.Write(region.Country);
        w.Write(region.Code);
        w.Write(region.Name);
        w.Write(region.Parts.Count);
        foreach (var rings in region.Parts) WriteRings(w, rings);
    }
}

var points = countries.Sum(c => c.Parts.Sum(p => p.Rings.Sum(r => r.Length)));
var regionPoints = regions.Sum(r => r.Parts.Sum(p => p.Sum(ring => ring.Length)));
Console.WriteLine($"Wrote {output}: {countries.Count} countries ({points:N0} points), {regions.Count} regions " +
                  $"({regionPoints:N0} points), {new FileInfo(output).Length / 1024:N0} KB");

static List<List<(float Lon, float Lat)[]>> Polygons(JsonElement geometry)
{
    var polygons = geometry.GetProperty("type").GetString() == "Polygon"
        ? [geometry.GetProperty("coordinates")]
        : geometry.GetProperty("coordinates").EnumerateArray().ToList();
    return polygons
        .Select(polygon => polygon.EnumerateArray()
            .Select(ring => ring.EnumerateArray().Select(p => (Lon: p[0].GetSingle(), Lat: p[1].GetSingle())).ToArray())
            .ToList())
        .ToList();
}

static void WriteRings(BinaryWriter w, List<(float Lon, float Lat)[]> rings)
{
    w.Write(rings.Count);
    foreach (var ring in rings)
    {
        w.Write(ring.Length);
        foreach (var (lon, lat) in ring)
        {
            w.Write(lon);
            w.Write(lat);
        }
    }
}

// Natural Earth puts a few island groups in "Seven seas (open ocean)" – use their UN region instead.
static string? BaseContinent(JsonElement props)
{
    var continent = props.GetProperty("CONTINENT").GetString()!;
    if (!continent.StartsWith("Seven seas", StringComparison.Ordinal)) return continent;
    return props.GetProperty("REGION_UN").GetString() switch
    {
        "Americas" => props.GetProperty("SUBREGION").GetString() == "Central America" ? "North America" : "South America",
        "Africa" => "Africa",
        "Asia" => "Asia",
        "Europe" => "Europe",
        "Oceania" => "Oceania",
        _ => null,
    };
}

// Overseas parts that Natural Earth keeps inside a European country's shape get their own
// continent, ISO code and name (Réunion is Africa\Réunion, not Africa\France).
static (string Continent, string Code, string Name)? Overseas(string code, double lon, double lat)
{
    if (code == "NL" && lon < -20) return ("North America", "BQ", "Caribbean Netherlands");
    if (code != "FR" || !(lon < -20 || (lon > 40 && lat < 0))) return null;
    (double Lon, double Lat, string Continent, string Code, string Name)[] french =
    [
        (55.53, -21.13, "Africa", "RE", "Réunion"),
        (45.15, -12.83, "Africa", "YT", "Mayotte"),
        (-53.10, 3.90, "South America", "GF", "French Guiana"),
        (-61.55, 16.25, "North America", "GP", "Guadeloupe"),
        (-61.02, 14.65, "North America", "MQ", "Martinique"),
    ];
    var t = french.MinBy(t => (t.Lon - lon) * (t.Lon - lon) + (t.Lat - lat) * (t.Lat - lat));
    return (t.Continent, t.Code, t.Name);
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "PhotoSorter.sln"))) return dir.FullName;
    throw new InvalidOperationException("Run from inside the PhotoSorter repository.");
}

internal sealed record Part(int Continent, string Code, string Name, List<(float Lon, float Lat)[]> Rings);

internal sealed record Region(string Country, string Code, string Name, List<List<(float Lon, float Lat)[]>> Parts);

internal partial class Program
{
    /// <summary>Folder-friendly names where Natural Earth's ADMIN name is long, official or ASCII-only.</summary>
    private static readonly Dictionary<string, string> FriendlyNames = new()
    {
        ["United States of America"] = "United States",
        ["Republic of Serbia"] = "Serbia",
        ["United Republic of Tanzania"] = "Tanzania",
        ["Democratic Republic of the Congo"] = "DR Congo",
        ["Republic of the Congo"] = "Congo",
        ["Federated States of Micronesia"] = "Micronesia",
        ["Hong Kong S.A.R."] = "Hong Kong",
        ["Macao S.A.R"] = "Macau",
        ["Aland"] = "Åland",
        ["Saint Barthelemy"] = "Saint Barthélemy",
        ["São Tomé and Principe"] = "São Tomé and Príncipe",
        ["eSwatini"] = "Eswatini",
        ["Vatican"] = "Vatican City",
        ["Indian Ocean Territories"] = "Christmas and Cocos Islands",
    };
}

internal sealed record Country(string Code, string Name, List<Part> Parts);
