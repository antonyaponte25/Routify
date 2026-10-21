using System.Globalization;
using NetTopologySuite.Geometries;

namespace Routify.Web.Helpers;

/// <summary>Conversión segura de datos de formulario (coordenadas, horas) usando cultura invariante.</summary>
public static class Entrada
{
    public static bool TryPunto(string? lat, string? lng, out Point? punto, out string? error)
    {
        punto = null;
        error = null;
        if (string.IsNullOrWhiteSpace(lat) && string.IsNullOrWhiteSpace(lng)) return true; // sin ubicación

        if (!double.TryParse(lat?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double la) ||
            !double.TryParse(lng?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lo) ||
            !GeoUtils.CoordenadasValidas(la, lo))
        {
            error = "Las coordenadas del mapa no son válidas.";
            return false;
        }
        punto = GeoUtils.CrearPunto(la, lo);
        return true;
    }

    public static TimeOnly? Hora(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        return TimeOnly.TryParseExact(texto.Trim(), new[] { "HH:mm", "H:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out var h) ? h : null;
    }

    public static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Número con punto decimal (para incrustar coordenadas en JavaScript/HTML).</summary>
    public static string Num(double valor) => valor.ToString("0.000000", CultureInfo.InvariantCulture);
}
