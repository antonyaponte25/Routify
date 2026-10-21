using System.Net;
using System.Net.Mail;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Routify.Web.Services.Notificaciones;

public record ResultadoEnvio(bool Ok, bool Simulado, string? Error = null);

public class CorreoOpciones
{
    public bool Habilitado { get; set; }
    public string Servidor { get; set; } = "smtp.gmail.com";
    public int Puerto { get; set; } = 587;
    public bool UsarSsl { get; set; } = true;
    public string Usuario { get; set; } = "";
    public string Clave { get; set; } = "";
    public string RemitenteNombre { get; set; } = "Routify";
    public string RemitenteCorreo { get; set; } = "";
}

public class WhatsAppOpciones
{
    public bool Habilitado { get; set; }
    public string PhoneNumberId { get; set; } = "";
    public string TokenAcceso { get; set; } = "";
    public string VersionApi { get; set; } = "v21.0";
    public string NombrePlantilla { get; set; } = "routify_notificacion";
    public string Idioma { get; set; } = "es";
    public string CodigoPais { get; set; } = "51";
}

public interface ICorreoSender
{
    Task<ResultadoEnvio> EnviarAsync(string destino, string asunto, string cuerpo, CancellationToken ct = default);
}

public interface IWhatsAppSender
{
    Task<ResultadoEnvio> EnviarAsync(string telefono, string mensaje, CancellationToken ct = default);
}

/// <summary>
/// Envío de correo por SMTP. Si Correo:Habilitado = false trabaja en MODO SIMULADO:
/// no envía nada y deja constancia en la tabla notificaciones (estado "Simulada").
/// </summary>
public class SmtpCorreoSender : ICorreoSender
{
    private readonly CorreoOpciones _op;
    private readonly ILogger<SmtpCorreoSender> _log;

    public SmtpCorreoSender(IOptions<CorreoOpciones> op, ILogger<SmtpCorreoSender> log)
    {
        _op = op.Value;
        _log = log;
    }

    public async Task<ResultadoEnvio> EnviarAsync(string destino, string asunto, string cuerpo, CancellationToken ct = default)
    {
        if (!_op.Habilitado || string.IsNullOrWhiteSpace(_op.Servidor) || string.IsNullOrWhiteSpace(_op.RemitenteCorreo))
        {
            _log.LogInformation("[CORREO SIMULADO] Para: {Destino} | Asunto: {Asunto} | {Cuerpo}", destino, asunto, cuerpo);
            return new ResultadoEnvio(true, true);
        }

        try
        {
            using var cliente = new SmtpClient(_op.Servidor, _op.Puerto)
            {
                EnableSsl = _op.UsarSsl,
                Credentials = new NetworkCredential(_op.Usuario, _op.Clave),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            using var mensaje = new MailMessage
            {
                From = new MailAddress(_op.RemitenteCorreo, _op.RemitenteNombre, Encoding.UTF8),
                Subject = asunto,
                Body = cuerpo,
                IsBodyHtml = false,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mensaje.To.Add(destino);
            await cliente.SendMailAsync(mensaje, ct);
            return new ResultadoEnvio(true, false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error al enviar correo a {Destino}", destino);
            return new ResultadoEnvio(false, false, Recortar(ex.Message));
        }
    }

    private static string Recortar(string s) => s.Length <= 480 ? s : s[..480];
}

/// <summary>
/// WhatsApp Business Cloud API (Meta). Con WhatsApp:Habilitado = false trabaja en modo simulado.
/// Los mensajes iniciados por la empresa requieren una PLANTILLA aprobada en Meta con un parámetro {{1}}
/// en el cuerpo (el texto completo de la notificación).
/// </summary>
public class MetaWhatsAppSender : IWhatsAppSender
{
    private readonly WhatsAppOpciones _op;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<MetaWhatsAppSender> _log;

    public MetaWhatsAppSender(IOptions<WhatsAppOpciones> op, IHttpClientFactory http, ILogger<MetaWhatsAppSender> log)
    {
        _op = op.Value;
        _http = http;
        _log = log;
    }

    public async Task<ResultadoEnvio> EnviarAsync(string telefono, string mensaje, CancellationToken ct = default)
    {
        if (!_op.Habilitado || string.IsNullOrWhiteSpace(_op.PhoneNumberId) || string.IsNullOrWhiteSpace(_op.TokenAcceso))
        {
            _log.LogInformation("[WHATSAPP SIMULADO] Para: {Telefono} | {Mensaje}", telefono, mensaje);
            return new ResultadoEnvio(true, true);
        }

        try
        {
            var destino = FormatoTelefono.Normalizar(telefono, _op.CodigoPais);
            var cuerpo = new
            {
                messaging_product = "whatsapp",
                to = destino,
                type = "template",
                template = new
                {
                    name = _op.NombrePlantilla,
                    language = new { code = _op.Idioma },
                    components = new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new[] { new { type = "text", text = LimpiarParametro(mensaje) } }
                        }
                    }
                }
            };

            var cliente = _http.CreateClient("whatsapp");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_op.VersionApi}/{_op.PhoneNumberId}/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _op.TokenAcceso);

            using var resp = await cliente.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return new ResultadoEnvio(true, false);

            var detalle = await resp.Content.ReadAsStringAsync(ct);
            _log.LogWarning("WhatsApp respondió {Codigo}: {Detalle}", (int)resp.StatusCode, detalle);
            return new ResultadoEnvio(false, false, $"HTTP {(int)resp.StatusCode}: {(detalle.Length > 400 ? detalle[..400] : detalle)}");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error al enviar WhatsApp a {Telefono}", telefono);
            return new ResultadoEnvio(false, false, ex.Message.Length <= 480 ? ex.Message : ex.Message[..480]);
        }
    }

    /// <summary>Meta no admite saltos de línea, tabulaciones ni más de 4 espacios seguidos en un parámetro.</summary>
    private static string LimpiarParametro(string texto) =>
        System.Text.RegularExpressions.Regex.Replace(texto.Replace('\n', ' ').Replace('\t', ' '), " {2,}", " ").Trim();
}
