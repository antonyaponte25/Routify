namespace Routify.Web.Helpers;

/// <summary>Nombres de roles (coinciden con la tabla roles).</summary>
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Operador = "Operador";
    public const string Repartidor = "Repartidor";

    public const string AdminOperador = Administrador + "," + Operador;

    public const int IdAdministrador = 1;
    public const int IdOperador = 2;
    public const int IdRepartidor = 3;
}

/// <summary>Identificadores fijos de la tabla estados_pedido.</summary>
public static class EstadoPedidoIds
{
    public const int Pendiente = 1;
    public const int EnPreparacion = 2;
    public const int EnRuta = 3;
    public const int Entregado = 4;
    public const int Fallido = 5;
}

public static class EstadoRuta
{
    public const string Planificada = "Planificada";
    public const string Activa = "Activa";
    public const string Completada = "Completada";
    public const string Cancelada = "Cancelada";
}

public static class EstadoParada
{
    public const string Pendiente = "Pendiente";
    public const string Entregada = "Entregada";
    public const string Fallida = "Fallida";
    public const string Omitida = "Omitida";
}

public static class TipoPrueba
{
    public const string Fotografia = "Fotografia";
    public const string Firma = "Firma";
}

public static class CanalNotificacion
{
    public const string Correo = "Correo";
    public const string WhatsApp = "WhatsApp";
}

public static class EventoNotificacion
{
    public const string Confirmado = "Confirmado";
    public const string Asignado = "Asignado";
    public const string EnCamino = "EnCamino";
    public const string Entregado = "Entregado";
    public const string Fallido = "Fallido";
}

public static class EstadoNotificacion
{
    public const string Pendiente = "Pendiente";
    public const string Enviada = "Enviada";
    public const string Simulada = "Simulada";
    public const string Fallida = "Fallida";
}

public static class ClavesConfiguracion
{
    public const string AlmacenNombre = "almacen_nombre";
    public const string AlmacenLatitud = "almacen_latitud";
    public const string AlmacenLongitud = "almacen_longitud";
    public const string VelocidadPromedioKmh = "velocidad_promedio_kmh";
    public const string FactorCalle = "factor_calle";
    public const string TiempoServicioMin = "tiempo_servicio_min";
    public const string GpsMinDistanciaM = "gps_min_distancia_m";
    public const string GpsMinSegundos = "gps_min_segundos";
    public const string RetencionUbicacionesDias = "retencion_ubicaciones_dias";
    public const string TrackingHorasExpiracion = "tracking_horas_expiracion";
    public const string ToleranciaEtaMin = "tolerancia_eta_min";
}
