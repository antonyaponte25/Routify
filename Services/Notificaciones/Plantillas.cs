using System.Text;
using Routify.Web.Helpers;
using Routify.Web.Models;

namespace Routify.Web.Services.Notificaciones;

/// <summary>Textos de las notificaciones al cliente (español, sin datos sensibles).</summary>
public static class Plantillas
{
    public static (string Asunto, string Texto) Construir(string evento, Pedido pedido, string? urlSeguimiento,
                                                           DateTime? etaUtc, string comercio, string? motivo = null)
    {
        string nombre = (pedido.Cliente?.Nombre ?? "cliente").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "cliente";
        string codigo = pedido.Codigo;
        string eta = etaUtc.HasValue ? etaUtc.Value.Hora() : "";
        var sb = new StringBuilder();

        switch (evento)
        {
            case EventoNotificacion.Confirmado:
                sb.Append($"Hola {nombre}, {comercio} registró tu pedido {codigo}.");
                if (!string.IsNullOrEmpty(urlSeguimiento)) sb.Append($" Sigue su estado aquí: {urlSeguimiento}");
                return ($"{comercio}: pedido {codigo} confirmado", sb.ToString());

            case EventoNotificacion.Asignado:
                sb.Append($"Hola {nombre}, tu pedido {codigo} ya tiene repartidor asignado.");
                if (eta != "") sb.Append($" Llegada estimada: {eta} h.");
                if (!string.IsNullOrEmpty(urlSeguimiento)) sb.Append($" Seguimiento: {urlSeguimiento}");
                return ($"{comercio}: pedido {codigo} asignado", sb.ToString());

            case EventoNotificacion.EnCamino:
                sb.Append($"¡Hola {nombre}! Tu pedido {codigo} está en camino.");
                if (eta != "") sb.Append($" Llegada estimada: {eta} h.");
                if (!string.IsNullOrEmpty(urlSeguimiento)) sb.Append($" Sigue al repartidor en vivo: {urlSeguimiento}");
                return ($"{comercio}: tu pedido {codigo} está en camino", sb.ToString());

            case EventoNotificacion.Entregado:
                sb.Append($"Hola {nombre}, tu pedido {codigo} fue entregado. ¡Gracias por tu compra!");
                return ($"{comercio}: pedido {codigo} entregado", sb.ToString());

            case EventoNotificacion.Fallido:
                sb.Append($"Hola {nombre}, no pudimos entregar tu pedido {codigo}.");
                if (!string.IsNullOrWhiteSpace(motivo)) sb.Append($" Motivo: {motivo}.");
                sb.Append(" Nos comunicaremos contigo para reprogramar la entrega.");
                return ($"{comercio}: no pudimos entregar tu pedido {codigo}", sb.ToString());

            default:
                return ($"{comercio}: pedido {codigo}", $"Hola {nombre}, hay una novedad en tu pedido {codigo}.");
        }
    }
}

public static class FormatoTelefono
{
    /// <summary>Normaliza un teléfono peruano a formato internacional sin "+" (ej. 51999111001).</summary>
    public static string Normalizar(string? telefono, string codigoPais = "51")
    {
        var digitos = new string((telefono ?? "").Where(char.IsDigit).ToArray());
        if (digitos.StartsWith("00")) digitos = digitos[2..];
        if (digitos.Length == 9) digitos = codigoPais + digitos;
        return digitos;
    }

    /// <summary>Enlace wa.me con el mensaje ya escrito (envío manual por el operador).</summary>
    public static string EnlaceWaMe(string? telefono, string mensaje, string codigoPais = "51") =>
        $"https://wa.me/{Normalizar(telefono, codigoPais)}?text={Uri.EscapeDataString(mensaje)}";
}
