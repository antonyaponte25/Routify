using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;
using Routify.Web.Services.Parametros;

namespace Routify.Web.Services.Notificaciones;

/// <summary>Cola en memoria con los identificadores de notificaciones pendientes de envío.</summary>
public class NotificacionCola
{
    private readonly Channel<int> _canal = Channel.CreateUnbounded<int>();

    public void Encolar(int notificacionId) => _canal.Writer.TryWrite(notificacionId);

    public ChannelReader<int> Lector => _canal.Reader;
}

public interface INotificacionService
{
    /// <summary>Registra las notificaciones del evento (correo y/o WhatsApp según las preferencias del cliente) y las encola.</summary>
    Task EncolarAsync(int pedidoId, string evento, string? motivo = null);

    /// <summary>URL pública del seguimiento de un token.</summary>
    string UrlSeguimiento(string token);
}

public class NotificacionService : INotificacionService
{
    private readonly RoutifyDbContext _db;
    private readonly NotificacionCola _cola;
    private readonly IConfiguration _cfg;

    public NotificacionService(RoutifyDbContext db, NotificacionCola cola, IConfiguration cfg)
    {
        _db = db;
        _cola = cola;
        _cfg = cfg;
    }

    public string UrlSeguimiento(string token) =>
        $"{(_cfg["App:UrlBase"] ?? "https://localhost:7080").TrimEnd('/')}/seguimiento/{token}";

    public async Task EncolarAsync(int pedidoId, string evento, string? motivo = null)
    {
        var pedido = await _db.Pedidos.AsNoTracking().Include(p => p.Cliente)
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido == null) return;

        var c = pedido.Cliente;
        if (!c.ConsentimientoDatos) return;                       // RNF-09: solo con consentimiento registrado
        bool porCorreo = c.NotificarCorreo && !string.IsNullOrWhiteSpace(c.Correo);
        bool porWhatsApp = c.NotificarWhatsapp && !string.IsNullOrWhiteSpace(c.Telefono);
        if (!porCorreo && !porWhatsApp) return;

        var ahora = DateTime.UtcNow;
        var enlace = await _db.Enlaces.AsNoTracking()
            .Where(t => t.PedidoId == pedidoId && t.Activo && t.FechaExpiracion > ahora)
            .OrderByDescending(t => t.Id).FirstOrDefaultAsync();
        string? url = enlace != null && evento != EventoNotificacion.Entregado && evento != EventoNotificacion.Fallido
            ? UrlSeguimiento(enlace.Token) : null;

        DateTime? eta = await _db.Paradas.AsNoTracking()
            .Where(p => p.PedidoId == pedidoId && p.Estado == EstadoParada.Pendiente)
            .OrderByDescending(p => p.Id).Select(p => p.Eta).FirstOrDefaultAsync();

        string comercio = _cfg["App:NombreComercio"] ?? "Routify";
        var (asunto, texto) = Plantillas.Construir(evento, pedido, url, eta, comercio, motivo);

        var nuevas = new List<Notificacion>();
        if (porCorreo)
            nuevas.Add(Crear(pedido, evento, CanalNotificacion.Correo, c.Correo!, asunto, texto));
        if (porWhatsApp)
            nuevas.Add(Crear(pedido, evento, CanalNotificacion.WhatsApp, c.Telefono!, null, texto));

        _db.Notificaciones.AddRange(nuevas);
        await _db.SaveChangesAsync();
        foreach (var n in nuevas) _cola.Encolar(n.Id);
    }

    private static Notificacion Crear(Pedido p, string evento, string canal, string destino, string? asunto, string texto) => new()
    {
        PedidoId = p.Id,
        ClienteId = p.ClienteId,
        Evento = evento,
        Canal = canal,
        Destino = destino.Length > 150 ? destino[..150] : destino,
        Asunto = asunto == null ? null : (asunto.Length > 200 ? asunto[..200] : asunto),
        Mensaje = texto.Length > 1000 ? texto[..1000] : texto,
        Estado = EstadoNotificacion.Pendiente
    };
}
