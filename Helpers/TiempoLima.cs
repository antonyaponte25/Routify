namespace Routify.Web.Helpers;

/// <summary>
/// Perú no usa horario de verano: Lima es siempre UTC-5. Todas las fechas se guardan en UTC
/// y se muestran en hora de Lima. Se evita depender de los identificadores de zona horaria
/// (distintos en Windows y Linux).
/// </summary>
public static class TiempoLima
{
    public const int OffsetHoras = -5;

    public static DateTime ALima(this DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(OffsetHoras);

    public static DateTime? ALima(this DateTime? utc) => utc.HasValue ? utc.Value.ALima() : null;

    public static DateTime AUtcDesdeLima(this DateTime lima) => DateTime.SpecifyKind(lima.AddHours(-OffsetHoras), DateTimeKind.Utc);

    public static DateTime HoyLima => DateTime.UtcNow.ALima().Date;

    /// <summary>Inicio (UTC) del día de Lima indicado.</summary>
    public static DateTime InicioDiaUtc(DateTime fechaLima) => fechaLima.Date.AUtcDesdeLima();

    public static string Fmt(this DateTime? utc, string formato = "dd/MM/yyyy HH:mm") =>
        utc.HasValue ? utc.Value.ALima().ToString(formato) : "—";

    public static string Fmt(this DateTime utc, string formato = "dd/MM/yyyy HH:mm") =>
        utc.ALima().ToString(formato);

    public static string Hora(this DateTime? utc) => utc.Fmt("HH:mm");

    public static string Hora(this DateTime utc) => utc.Fmt("HH:mm");
}
