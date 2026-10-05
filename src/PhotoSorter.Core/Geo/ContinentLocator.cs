using System.IO.Compression;
using System.Text;

namespace PhotoSorter.Core.Geo;

/// <param name="DistanceKm">0 when the point is inside the country; otherwise the distance to its coast.</param>
public sealed record GeoMatch(string Continent, string CountryCode, string CountryName, double DistanceKm);

/// <summary>
/// Offline GPS → country → continent lookup over Natural Earth polygons (ARCHITECTURE §5.3).
/// Points just off the coast (beach, boat, simplified coastline) match the nearest land within 200 km.
/// Thread-safe after construction.
/// </summary>
public sealed class ContinentLocator
{
    public const double CoastalToleranceKm = 200;
    private const string ResourceName = "PhotoSorter.Core.Geo.countries.gz";

    private static readonly Lazy<ContinentLocator> LazyDefault = new(LoadEmbedded);

    /// <summary>
    /// European Turkey (East Thrace): the land north/west of the Dardanelles – Sea of Marmara – Bosporus line.
    /// (lon, lat) pairs; only used for points already inside Turkey.
    /// </summary>
    private static readonly float[] EastThrace =
    [
        25.00f, 39.80f, 26.19f, 40.02f, 26.40f, 40.15f, 26.68f, 40.38f, 27.00f, 40.55f, 28.00f, 40.80f,
        28.995f, 41.005f, 29.005f, 41.03f, 29.037f, 41.045f, 29.055f, 41.08f, 29.075f, 41.15f, 29.13f, 41.23f,
        29.20f, 42.50f, 25.00f, 42.50f,
    ];

    private readonly Part[] _parts;

    private ContinentLocator(Part[] parts) =>
        _parts = [.. parts.OrderBy(p => (p.MaxLon - p.MinLon) * (p.MaxLat - p.MinLat))]; // small first: fast exits

    public static ContinentLocator Default => LazyDefault.Value;

    public GeoMatch? Locate(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;
        if (Math.Abs(lat) < 1e-6 && Math.Abs(lon) < 1e-6) return null; // (0,0) = "no fix" written as zeros

        foreach (var part in _parts)
            if (part.BoxContains(lon, lat) && part.Contains(lon, lat))
                return Match(part, lat, lon, 0);

        var marginLat = CoastalToleranceKm / 110.574;
        var marginLon = Math.Min(180, marginLat / Math.Max(Math.Cos(lat * Math.PI / 180), 0.01));
        Part? nearest = null;
        var best = double.MaxValue;
        foreach (var part in _parts)
        {
            if (!part.BoxNear(lon, lat, marginLon, marginLat)) continue;
            var d = part.DistanceKm(lon, lat);
            if (d < best)
            {
                best = d;
                nearest = part;
            }
        }
        return nearest is not null && best <= CoastalToleranceKm ? Match(nearest, lat, lon, best) : null;
    }

    private static GeoMatch Match(Part part, double lat, double lon, double distanceKm)
    {
        var continent = part.Country.Code switch
        {
            // Natural Earth puts all of Russia in Europe: east of the Urals (and Chukotka across 180°) is Asia.
            "RU" => lon >= 60 || lon <= -160 ? "Asia" : "Europe",
            "TR" => PointInRing(EastThrace, lon, lat) ? "Europe" : "Asia",
            _ => part.Continent,
        };
        return new GeoMatch(continent, part.Country.Code, part.Country.Name, distanceKm);
    }

    private static ContinentLocator LoadEmbedded()
    {
        using var resource = typeof(ContinentLocator).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}. Run tools/GeoPrep.");
        return Load(resource);
    }

    public static ContinentLocator Load(Stream gzipped)
    {
        using var gzip = new GZipStream(gzipped, CompressionMode.Decompress);
        using var r = new BinaryReader(gzip, Encoding.UTF8);
        if (!r.ReadBytes(6).AsSpan().SequenceEqual("PSGEO1"u8)) throw new InvalidDataException("Not a PhotoSorter geo file.");

        var continents = new string[r.ReadInt32()];
        for (var i = 0; i < continents.Length; i++) continents[i] = r.ReadString();

        var parts = new List<Part>();
        var countryCount = r.ReadInt32();
        for (var c = 0; c < countryCount; c++)
        {
            var country = new Country(r.ReadString(), r.ReadString());
            var partCount = r.ReadInt32();
            for (var p = 0; p < partCount; p++)
            {
                var continent = continents[r.ReadByte()];
                var rings = new float[r.ReadInt32()][];
                for (var i = 0; i < rings.Length; i++)
                {
                    var ring = new float[r.ReadInt32() * 2];
                    for (var k = 0; k < ring.Length; k++) ring[k] = r.ReadSingle();
                    rings[i] = ring;
                }
                parts.Add(new Part(country, continent, rings));
            }
        }
        return new ContinentLocator([.. parts]);
    }

    /// <summary>Even-odd ray casting over interleaved (lon, lat) pairs.</summary>
    private static bool PointInRing(float[] ring, double x, double y)
    {
        var inside = false;
        var n = ring.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = ring[2 * i], yi = ring[2 * i + 1], xj = ring[2 * j], yj = ring[2 * j + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    private sealed record Country(string Code, string Name);

    /// <summary>One polygon (outer ring + holes) of a country.</summary>
    private sealed class Part
    {
        public Part(Country country, string continent, float[][] rings)
        {
            Country = country;
            Continent = continent;
            Rings = rings;
            MinLon = MinLat = float.MaxValue;
            MaxLon = MaxLat = float.MinValue;
            foreach (var ring in rings)
            {
                for (var i = 0; i < ring.Length; i += 2)
                {
                    MinLon = Math.Min(MinLon, ring[i]);
                    MaxLon = Math.Max(MaxLon, ring[i]);
                    MinLat = Math.Min(MinLat, ring[i + 1]);
                    MaxLat = Math.Max(MaxLat, ring[i + 1]);
                }
            }
        }

        public Country Country { get; }
        public string Continent { get; }
        public float[][] Rings { get; }
        public float MinLon { get; }
        public float MaxLon { get; }
        public float MinLat { get; }
        public float MaxLat { get; }

        public bool BoxContains(double lon, double lat) => lon >= MinLon && lon <= MaxLon && lat >= MinLat && lat <= MaxLat;

        /// <summary>Box test with a margin, also across the ±180° meridian.</summary>
        public bool BoxNear(double lon, double lat, double marginLon, double marginLat)
        {
            if (lat < MinLat - marginLat || lat > MaxLat + marginLat) return false;
            foreach (var x in (ReadOnlySpan<double>)[lon, lon - 360, lon + 360])
                if (x >= MinLon - marginLon && x <= MaxLon + marginLon) return true;
            return false;
        }

        /// <summary>Holes are rings too, so even-odd over all rings handles them (e.g. Lesotho inside South Africa).</summary>
        public bool Contains(double lon, double lat)
        {
            var inside = false;
            foreach (var ring in Rings)
                if (PointInRing(ring, lon, lat)) inside = !inside;
            return inside;
        }

        /// <summary>Distance to the nearest edge, in a local flat projection around the point (fine within ~200 km).</summary>
        public double DistanceKm(double lon, double lat)
        {
            var kmPerLon = 111.320 * Math.Cos(lat * Math.PI / 180);
            const double kmPerLat = 110.574;
            var best = double.MaxValue;
            foreach (var ring in Rings)
            {
                var n = ring.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double ax = WrapLon(ring[2 * j] - lon) * kmPerLon, ay = (ring[2 * j + 1] - lat) * kmPerLat;
                    double bx = WrapLon(ring[2 * i] - lon) * kmPerLon, by = (ring[2 * i + 1] - lat) * kmPerLat;
                    best = Math.Min(best, DistanceToSegment(ax, ay, bx, by));
                }
            }
            return best;
        }

        private static double WrapLon(double d) => d > 180 ? d - 360 : d < -180 ? d + 360 : d;

        /// <summary>Distance from the origin to segment AB.</summary>
        private static double DistanceToSegment(double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            var lengthSq = dx * dx + dy * dy;
            var t = lengthSq == 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / lengthSq, 0, 1);
            double px = ax + t * dx, py = ay + t * dy;
            return Math.Sqrt(px * px + py * py);
        }
    }
}
