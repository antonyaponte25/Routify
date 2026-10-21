namespace Routify.Web.Helpers;

/// <summary>Utilidades de presentación para las vistas Razor.</summary>
public static class Vista
{
    public static string ClaseEstado(int estadoId) => estadoId switch
    {
        EstadoPedidoIds.Pendiente => "e-pendiente",
        EstadoPedidoIds.EnPreparacion => "e-prep",
        EstadoPedidoIds.EnRuta => "e-ruta",
        EstadoPedidoIds.Entregado => "e-ok",
        EstadoPedidoIds.Fallido => "e-fallo",
        _ => ""
    };

    public static string ClaseRuta(string estado) => estado switch
    {
        EstadoRuta.Activa => "e-ruta",
        EstadoRuta.Planificada => "e-prep",
        EstadoRuta.Completada => "e-ok",
        EstadoRuta.Cancelada => "e-fallo",
        _ => ""
    };

    public static string ClaseParada(string estado) => estado switch
    {
        EstadoParada.Entregada => "e-ok",
        EstadoParada.Fallida => "e-fallo",
        EstadoParada.Omitida => "e-fallo",
        _ => "e-pendiente"
    };

    public static string ClaseNotificacion(string estado) => estado switch
    {
        EstadoNotificacion.Enviada => "e-ok",
        EstadoNotificacion.Simulada => "e-prep",
        EstadoNotificacion.Fallida => "e-fallo",
        _ => "e-pendiente"
    };

    /// <summary>Variación porcentual respecto al día anterior, para las tarjetas de indicadores.</summary>
    public static string Variacion(int hoy, int ayer)
    {
        if (ayer == 0) return hoy == 0 ? "sin cambio" : "nuevo";
        double v = (hoy - ayer) * 100.0 / ayer;
        return (v >= 0 ? "+" : "") + v.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " % vs. ayer";
    }
}
