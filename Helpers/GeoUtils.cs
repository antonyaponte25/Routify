using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Routify.Web.Helpers;

/// <summary>
/// Utilidades geográficas. SQL Server (tipo geography) y NetTopologySuite usan X = longitud, Y = latitud,
/// SRID 4326 (WGS84).
/// </summary>
public static class GeoUtils
{
    public const int Srid = 4326;
    private const double RadioTierraKm = 6371.0088;

    public static readonly GeometryFactory Fabrica =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: Srid);

    public static Point CrearPunto(double latitud, double longitud) =>
        Fabrica.CreatePoint(new Coordinate(longitud, latitud));

    public static double Lat(this Point p) => p.Y;

    public static double Lng(this Point p) => p.X;

    public static bool CoordenadasValidas(double lat, double lng) =>
        lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);

    /// <summary>Distancia en línea recta sobre la esfera (fórmula de Haversine), en kilómetros.</summary>
    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        double dLat = Rad(lat2 - lat1);
        double dLng = Rad(lng2 - lng1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * RadioTierraKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static double HaversineKm(Point a, Point b) => HaversineKm(a.Y, a.X, b.Y, b.X);

    private static double Rad(double grados) => grados * Math.PI / 180.0;

    /// <summary>
    /// Crea un polígono a partir de vértices (lat, lng). SQL Server geography exige que el anillo
    /// exterior sea antihorario (la región queda a la izquierda); aquí se normaliza el sentido.
    /// </summary>
    public static Polygon? CrearPoligono(IList<(double Lat, double Lng)> vertices)
    {
        var lista = vertices.ToList();
        if (lista.Count > 1 && lista[0] == lista[^1]) lista.RemoveAt(lista.Count - 1);
        if (lista.Count < 3) return null;

        // Área con signo en el plano (x = lng, y = lat): positiva = antihorario.
        double area = 0;
        for (int i = 0; i < lista.Count; i++)
        {
            var (lat1, lng1) = lista[i];
            var (lat2, lng2) = lista[(i + 1) % lista.Count];
            area += lng1 * lat2 - lng2 * lat1;
        }
        if (Math.Abs(area) < 1e-12) return null; // vértices colineales
        if (area < 0) lista.Reverse();

        var coords = lista.Select(v => new Coordinate(v.Lng, v.Lat)).ToList();
        coords.Add(coords[0]);
        var poligono = Fabrica.CreatePolygon(coords.ToArray());
        return poligono.IsValid ? poligono : null;
    }

    public static LineString? CrearLinea(IEnumerable<(double Lat, double Lng)> puntos)
    {
        var coords = puntos.Select(p => new Coordinate(p.Lng, p.Lat)).ToList();
        // Quita duplicados consecutivos (SQL Server rechaza segmentos de longitud cero).
        var limpio = new List<Coordinate>();
        foreach (var c in coords)
            if (limpio.Count == 0 || !limpio[^1].Equals2D(c)) limpio.Add(c);
        return limpio.Count >= 2 ? Fabrica.CreateLineString(limpio.ToArray()) : null;
    }

    /// <summary>Vértices (lat, lng) del anillo exterior, sin repetir el último.</summary>
    public static List<double[]> Vertices(Polygon poligono)
    {
        var coords = poligono.ExteriorRing.Coordinates;
        return coords.Take(Math.Max(0, coords.Length - 1)).Select(c => new[] { c.Y, c.X }).ToList();
    }
}
