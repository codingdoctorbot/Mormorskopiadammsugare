// GeoPrep – builds the offline continent lookup data (ARCHITECTURE §5.3).
//
//   dotnet run --project tools/GeoPrep [path/to/ne_50m_admin_0_countries.geojson]
//
// Without an argument the GeoJSON is downloaded from the Natural Earth repository (public domain).
// Output: src/PhotoSorter.Core/Geo/countries.gz (embedded into PhotoSorter.Core).
//
// Format (gzip): "PSGEO1" | int32 n + n continent names | int32 countries,
//   per country: string iso2, string name, int32 parts,
//   per part: byte continent, int32 rings, per ring: int32 points + points × (float32 lon, float32 lat)

using System.IO.Compression;
using System.Text.Json;

const string SourceUrl =
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_countries.geojson";
string[] continents = ["Africa", "Antarctica", "Asia", "Europe", "North America", "Oceania", "South America"];

var repoRoot = FindRepoRoot();
var output = Path.Combine(repoRoot, "src", "PhotoSorter.Core", "Geo", "countries.gz");

string json;
if (args.Length > 0)
{
    json = await File.ReadAllTextAsync(args[0]);
}
else
{
    Console.WriteLine($"Downloading {SourceUrl}");
    using var http = new HttpClient();
    json = await http.GetStringAsync(SourceUrl);
}

using var doc = JsonDocument.Parse(json);
var countries = new List<Country>();
foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
{
    var props = feature.GetProperty("properties");
    var name = props.GetProperty("ADMIN").GetString()!;
    var code = props.GetProperty("ISO_A2_EH").GetString() is { } iso && iso != "-99"
        ? iso
        : props.GetProperty("ADM0_A3").GetString()!;
    var baseContinent = BaseContinent(props);
    if (baseContinent is null)
    {
        Console.WriteLine($"  skipped (no continent): {name}");
        continue;
    }

    var geometry = feature.GetProperty("geometry");
    var polygons = geometry.GetProperty("type").GetString() == "Polygon"
        ? [geometry.GetProperty("coordinates")]
        : geometry.GetProperty("coordinates").EnumerateArray().ToList();

    var parts = new List<Part>();
    foreach (var polygon in polygons)
    {
        var rings = polygon.EnumerateArray()
            .Select(ring => ring.EnumerateArray().Select(p => (Lon: p[0].GetSingle(), Lat: p[1].GetSingle())).ToArray())
            .ToList();
        var (cLon, cLat) = (rings[0].Average(p => p.Lon), rings[0].Average(p => p.Lat));
        var continent = OverseasContinent(code, cLon, cLat) ?? baseContinent;
        parts.Add(new Part(Array.IndexOf(continents, continent), rings));
    }
    countries.Add(new Country(code, name, parts));
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await using (var file = File.Create(output))
await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
await using (var w = new BinaryWriter(gzip))
{
    w.Write("PSGEO1"u8);
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
            w.Write(part.Rings.Count);
            foreach (var ring in part.Rings)
            {
                w.Write(ring.Length);
                foreach (var (lon, lat) in ring)
                {
                    w.Write(lon);
                    w.Write(lat);
                }
            }
        }
    }
}

var points = countries.Sum(c => c.Parts.Sum(p => p.Rings.Sum(r => r.Length)));
Console.WriteLine($"Wrote {output}: {countries.Count} countries, {points:N0} points, {new FileInfo(output).Length / 1024:N0} KB");

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

// Overseas parts that Natural Earth keeps inside a European country's shape.
static string? OverseasContinent(string code, double lon, double lat) => code switch
{
    // Guadeloupe, Martinique, St-Martin → North America; French Guiana → South America; Réunion, Mayotte → Africa
    "FR" when lon < -20 => lat > 8 ? "North America" : "South America",
    "FR" when lon > 40 && lat < 0 => "Africa",
    // Bonaire, Sint Eustatius, Saba
    "NL" when lon < -20 => "North America",
    _ => null,
};

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "PhotoSorter.sln"))) return dir.FullName;
    throw new InvalidOperationException("Run from inside the PhotoSorter repository.");
}

internal sealed record Part(int Continent, List<(float Lon, float Lat)[]> Rings);

internal sealed record Country(string Code, string Name, List<Part> Parts);
