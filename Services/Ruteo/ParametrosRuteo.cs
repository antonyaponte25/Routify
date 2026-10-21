namespace Routify.Web.Services.Ruteo;

/// <summary>Parámetros del motor de optimización (se leen de la tabla configuracion).</summary>
public class ParametrosRuteo
{
    public double VelocidadKmh { get; set; } = 22;
    public double FactorCalle { get; set; } = 1.3;
    public int TiempoServicioMin { get; set; } = 4;
    public double AlmacenLat { get; set; } = -12.1190;
    public double AlmacenLng { get; set; } = -77.0300;
    public string AlmacenNombre { get; set; } = "Almacén";
    public int ToleranciaEtaMin { get; set; } = 10;
}
