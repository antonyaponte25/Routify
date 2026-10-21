using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Hubs;
using Routify.Web.Models;
using Routify.Web.Services.Notificaciones;
using Routify.Web.Services.Parametros;
using Routify.Web.Services.Ruteo;
using Routify.Web.Services.Seguridad;

namespace Routify.Web.Services.Pedidos;

public record ResultadoOperacion(bool Ok, string? Error = null)
{
    public static ResultadoOperacion Exito() => new(true);
    public static ResultadoOperacion Falla(string error) => new(false, error);
}

public record ResultadoDistribucion(int Asignados, int SinAsignar, int RepartidoresUsados);

/// <summary>
/// Reglas de negocio del ciclo de vida del pedido: registro, asignación manual/automática (RF-05..RF-07),
/// cambios de estado con historial (RF-09), enlaces de seguimiento (RF-20) y notificaciones.
/// </summary>
public class PedidoService
{
    private readonly RoutifyDbContext _db;
    private readonly RutaService _rutas;
    private readonly INotificacionService _notif;
    private readonly IConfiguracionService _cfg;
    private readonly IHubContext<TrackingHub> _hub;

    public PedidoService(RoutifyDbContext db, RutaService rutas, INotificacionService notif,
                         IConfiguracionService cfg, IHubContext<TrackingHub> hub)
    {
        _db = db;
        _rutas = rutas;
        _notif = notif;
        _cfg = cfg;
        _hub = hub;
    }

    // ---------------------------------------------------------------- Registro

    /// <summary>Código con el formato RT-aammdd-NNN según la fecha de Lima.</summary>
    public async Task<string> GenerarCodigoAsync()
    {
        var hoy = TiempoLima.HoyLima;
        string prefijo = $"RT-{hoy:yyMMdd}-";
        int ultimo = 0;
        var codigos = await _db.Pedidos.AsNoTracking()
            .Where(p => p.Codigo.StartsWith(prefijo)).Select(p => p.Codigo).ToListAsync();
        foreach (var c in codigos)
            if (int.TryParse(c.AsSpan(prefijo.Length), out int n) && n > ultimo) ultimo = n;
        return $"{prefijo}{ultimo + 1:000}";
    }

    public async Task<ZonaReparto?> ZonaDePuntoAsync(Point? punto)
    {
        if (punto == null) return null;
        return await _db.Zonas.AsNoTracking()
            .Where(z => z.Activa && z.Poligono.Intersects(punto))
            .OrderBy(z => z.Poligono.Area)
            .FirstOrDefaultAsync();
    }

    public async Task<Pedido> CrearAsync(Pedido pedido, IEnumerable<DetallePedido> detalles, int? usuarioId)
    {
        pedido.Codigo = await GenerarCodigoAsync();
        pedido.EstadoId = EstadoPedidoIds.Pendiente;
        pedido.CreadoPor = usuarioId;
        pedido.FechaRegistro = DateTime.UtcNow;
        pedido.FechaActualizacion = pedido.FechaRegistro;
        pedido.ZonaId = (await ZonaDePuntoAsync(pedido.Ubicacion))?.Id;

        foreach (var d in detalles) pedido.Detalles.Add(d);
        pedido.Historial.Add(new HistorialEstado
        {
            EstadoAnteriorId = null,
            EstadoNuevoId = EstadoPedidoIds.Pendiente,
            UsuarioId = usuarioId,
            Fecha = pedido.FechaRegistro,
            Comentario = "Pedido registrado"
        });
        pedido.Enlaces.Add(NuevoEnlace());

        _db.Pedidos.Add(pedido);
        await _db.SaveChangesAsync();

        await _notif.EncolarAsync(pedido.Id, EventoNotificacion.Confirmado);
        await DifundirEstadoAsync(pedido);
        return pedido;
    }

    /// <summary>Actualiza datos del pedido (solo mientras no esté en ruta o cerrado) y recalcula la zona y la ruta.</summary>
    public async Task<ResultadoOperacion> ActualizarAsync(int pedidoId, Action<Pedido> aplicar, IList<DetallePedido> nuevosDetalles)
    {
        var p = await _db.Pedidos.Include(x => x.Detalles).FirstOrDefaultAsync(x => x.Id == pedidoId);
        if (p == null) return ResultadoOperacion.Falla("El pedido no existe.");
        if (p.EstadoId is EstadoPedidoIds.EnRuta or EstadoPedidoIds.Entregado)
            return ResultadoOperacion.Falla("No se puede editar un pedido en ruta o entregado.");

        aplicar(p);
        p.ZonaId = (await ZonaDePuntoAsync(p.Ubicacion))?.Id;
        p.FechaActualizacion = DateTime.UtcNow;

        _db.Detalles.RemoveRange(p.Detalles);
        foreach (var d in nuevosDetalles) { d.PedidoId = p.Id; _db.Detalles.Add(d); }

        await _db.SaveChangesAsync();
        if (p.RepartidorId.HasValue) await _rutas.ReoptimizarAsync(p.RepartidorId.Value);
        return ResultadoOperacion.Exito();
    }

    // -------------------------------------------------------------- Estados

    private static readonly Dictionary<int, int[]> Transiciones = new()
    {
        [EstadoPedidoIds.Pendiente] = new[] { EstadoPedidoIds.EnPreparacion },
        [EstadoPedidoIds.EnPreparacion] = new[] { EstadoPedidoIds.Pendiente, EstadoPedidoIds.EnRuta },
        [EstadoPedidoIds.EnRuta] = new[] { EstadoPedidoIds.Entregado, EstadoPedidoIds.Fallido },
        [EstadoPedidoIds.Fallido] = new[] { EstadoPedidoIds.Pendiente },
        [EstadoPedidoIds.Entregado] = Array.Empty<int>()
    };

    /// <summary>
    /// Cambia el estado, registra el historial (RF-09), actualiza la vigencia del enlace de seguimiento,
    /// dispara la notificación correspondiente y avisa por SignalR. No hace SaveChanges del llamador: guarda aquí.
    /// </summary>
    public async Task<ResultadoOperacion> CambiarEstadoAsync(Pedido p, int nuevoEstadoId, int? usuarioId, string? comentario)
    {
        if (!Transiciones.TryGetValue(p.EstadoId, out var permitidos) || !permitidos.Contains(nuevoEstadoId))
            return ResultadoOperacion.Falla("Cambio de estado no permitido para el estado actual del pedido.");

        int anterior = p.EstadoId;
        p.EstadoId = nuevoEstadoId;
        p.FechaActualizacion = DateTime.UtcNow;

        _db.Historial.Add(new HistorialEstado
        {
            PedidoId = p.Id,
            EstadoAnteriorId = anterior,
            EstadoNuevoId = nuevoEstadoId,
            UsuarioId = usuarioId,
            Fecha = p.FechaActualizacion,
            Comentario = Recortar(comentario, 300)
        });

        // Vigencia del enlace de seguimiento (RNF-08)
        var enlaces = await _db.Enlaces.Where(t => t.PedidoId == p.Id && t.Activo).ToListAsync();
        if (nuevoEstadoId is EstadoPedidoIds.Entregado or EstadoPedidoIds.Fallido)
        {
            int horas = await _cfg.GetIntAsync(ClavesConfiguracion.TrackingHorasExpiracion, 24);
            foreach (var t in enlaces) t.FechaExpiracion = DateTime.UtcNow.AddHours(Math.Max(1, horas));
        }
        else if (nuevoEstadoId == EstadoPedidoIds.Pendiente)
        {
            foreach (var t in enlaces) t.FechaExpiracion = DateTime.UtcNow.AddDays(7);
        }

        await _db.SaveChangesAsync();

        string? evento = nuevoEstadoId switch
        {
            EstadoPedidoIds.EnRuta => EventoNotificacion.EnCamino,
            EstadoPedidoIds.Entregado => EventoNotificacion.Entregado,
            EstadoPedidoIds.Fallido => EventoNotificacion.Fallido,
            _ => null
        };
        if (evento != null) await _notif.EncolarAsync(p.Id, evento, nuevoEstadoId == EstadoPedidoIds.Fallido ? comentario : null);

        await DifundirEstadoAsync(p);
        return ResultadoOperacion.Exito();
    }

    // ------------------------------------------------------------ Asignación

    /// <summary>Asignación manual a un repartidor disponible (RF-06): estado "En preparación" y ruta optimizada.</summary>
    public async Task<ResultadoOperacion> AsignarAsync(int pedidoId, int repartidorId, int? usuarioId)
    {
        var p = await _db.Pedidos.FirstOrDefaultAsync(x => x.Id == pedidoId);
        if (p == null) return ResultadoOperacion.Falla("El pedido no existe.");
        if (p.EstadoId != EstadoPedidoIds.Pendiente) return ResultadoOperacion.Falla("Solo se pueden asignar pedidos pendientes.");
        if (p.Ubicacion == null) return ResultadoOperacion.Falla("El pedido no tiene ubicación en el mapa. Edítelo y fije el punto de entrega.");

        var r = await _db.Repartidores.Include(x => x.Usuario).FirstOrDefaultAsync(x => x.Id == repartidorId);
        if (r == null || !r.Usuario.EstaActivo) return ResultadoOperacion.Falla("El repartidor no existe o está inactivo.");
        if (!r.Disponibilidad) return ResultadoOperacion.Falla("El repartidor no está disponible.");
        if (await _rutas.CargaActualAsync(r.Id) >= r.Capacidad) return ResultadoOperacion.Falla("El repartidor alcanzó su capacidad máxima.");

        p.RepartidorId = r.Id;
        var res = await CambiarEstadoAsync(p, EstadoPedidoIds.EnPreparacion, usuarioId, $"Asignado a {r.Usuario.NombreCompleto}");
        if (!res.Ok) return res;

        await _rutas.ReoptimizarAsync(r.Id);
        await _notif.EncolarAsync(p.Id, EventoNotificacion.Asignado);
        return ResultadoOperacion.Exito();
    }

    /// <summary>Auto-asignación al repartidor disponible más cercano al punto de recogida (almacén) con capacidad libre (RF-07).</summary>
    public async Task<(ResultadoOperacion Resultado, Repartidor? Repartidor)> AutoAsignarAsync(int pedidoId, int? usuarioId)
    {
        var prm = await _cfg.RuteoAsync();
        var recogida = GeoUtils.CrearPunto(prm.AlmacenLat, prm.AlmacenLng);

        var candidatos = await _db.Repartidores.Include(r => r.Usuario)
            .Where(r => r.Disponibilidad && r.Usuario.Estado == "Activo")
            .OrderBy(r => r.UbicacionActual == null ? 1 : 0)
            .ThenBy(r => r.UbicacionActual!.Distance(recogida))
            .ToListAsync();

        foreach (var r in candidatos)
        {
            if (await _rutas.CargaActualAsync(r.Id) >= r.Capacidad) continue;
            var res = await AsignarAsync(pedidoId, r.Id, usuarioId);
            return res.Ok ? (res, r) : (res, null);
        }
        return (ResultadoOperacion.Falla("No hay repartidores disponibles con capacidad libre."), null);
    }

    /// <summary>Reparte todos los pedidos pendientes con ubicación entre los repartidores disponibles (jornadas de alto volumen).</summary>
    public async Task<ResultadoDistribucion> AutoDistribuirAsync(int? usuarioId)
    {
        var prm = await _cfg.RuteoAsync();
        var pendientes = await _db.Pedidos.AsNoTracking()
            .Where(p => p.EstadoId == EstadoPedidoIds.Pendiente && p.Ubicacion != null)
            .OrderBy(p => p.FechaRegistro).ToListAsync();
        if (pendientes.Count == 0) return new ResultadoDistribucion(0, 0, 0);

        var reps = await _db.Repartidores.Include(r => r.Usuario)
            .Where(r => r.Disponibilidad && r.Usuario.Estado == "Activo").ToListAsync();

        var candidatos = new List<CandidatoReparto>();
        foreach (var r in reps)
        {
            int libre = r.Capacidad - await _rutas.CargaActualAsync(r.Id);
            if (libre <= 0) continue;
            double lat = r.UbicacionActual?.Y ?? prm.AlmacenLat;
            double lng = r.UbicacionActual?.X ?? prm.AlmacenLng;
            candidatos.Add(new CandidatoReparto(r.Id, lat, lng, libre));
        }

        var puntos = pendientes.Select(p => new PuntoEntrega(p.Id, p.Ubicacion!.Y, p.Ubicacion.X, p.VentanaInicio, p.VentanaFin)).ToList();
        var reparto = OptimizadorRutas.Repartir(prm.AlmacenLat, prm.AlmacenLng, puntos, candidatos, out var sinAsignar);

        int asignados = 0;
        foreach (var (repartidorId, pedidoIds) in reparto)
        {
            foreach (var pid in pedidoIds)
            {
                var res = await AsignarAsync(pid, repartidorId, usuarioId);
                if (res.Ok) asignados++;
            }
        }
        int sin = Math.Max(0, pendientes.Count - asignados);
        return new ResultadoDistribucion(asignados, sin, reparto.Count);
    }

    /// <summary>Quita el repartidor de un pedido "En preparación" y lo devuelve a Pendiente.</summary>
    public async Task<ResultadoOperacion> DesasignarAsync(int pedidoId, int? usuarioId)
    {
        var p = await _db.Pedidos.FirstOrDefaultAsync(x => x.Id == pedidoId);
        if (p == null) return ResultadoOperacion.Falla("El pedido no existe.");
        if (p.EstadoId != EstadoPedidoIds.EnPreparacion) return ResultadoOperacion.Falla("Solo se puede desasignar un pedido en preparación.");

        int? anterior = p.RepartidorId;
        p.RepartidorId = null;
        var res = await CambiarEstadoAsync(p, EstadoPedidoIds.Pendiente, usuarioId, "Repartidor desasignado");
        if (res.Ok && anterior.HasValue) await _rutas.ReoptimizarAsync(anterior.Value);
        return res;
    }

    /// <summary>Devuelve un pedido con entrega fallida a Pendiente para reprogramarlo.</summary>
    public async Task<ResultadoOperacion> ReprogramarAsync(int pedidoId, int? usuarioId)
    {
        var p = await _db.Pedidos.FirstOrDefaultAsync(x => x.Id == pedidoId);
        if (p == null) return ResultadoOperacion.Falla("El pedido no existe.");
        if (p.EstadoId != EstadoPedidoIds.Fallido) return ResultadoOperacion.Falla("Solo se reprograman pedidos con entrega fallida.");
        p.RepartidorId = null;
        return await CambiarEstadoAsync(p, EstadoPedidoIds.Pendiente, usuarioId, "Reprogramado para un nuevo intento");
    }

    // ------------------------------------------------------ Seguimiento

    private Tracking NuevoEnlace() => new()
    {
        Token = Tokens.Nuevo(),
        FechaCreacion = DateTime.UtcNow,
        FechaExpiracion = DateTime.UtcNow.AddDays(7),
        Activo = true
    };

    /// <summary>Devuelve el enlace vigente del pedido o crea uno nuevo.</summary>
    public async Task<Tracking> AsegurarEnlaceAsync(int pedidoId)
    {
        var ahora = DateTime.UtcNow;
        var t = await _db.Enlaces.Where(x => x.PedidoId == pedidoId && x.Activo && x.FechaExpiracion > ahora)
                                 .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (t != null) return t;

        t = NuevoEnlace();
        t.PedidoId = pedidoId;
        _db.Enlaces.Add(t);
        await _db.SaveChangesAsync();
        return t;
    }

    private async Task DifundirEstadoAsync(Pedido p)
    {
        string nombreEstado = await _db.Estados.AsNoTracking().Where(e => e.Id == p.EstadoId).Select(e => e.Nombre).FirstAsync();
        var carga = new { pedidoId = p.Id, codigo = p.Codigo, estadoId = p.EstadoId, estado = nombreEstado, repartidorId = p.RepartidorId };
        await _hub.Clients.Group(Grupos.Despacho).SendAsync("EstadoPedido", carga);

        var ahora = DateTime.UtcNow;
        var tokens = await _db.Enlaces.AsNoTracking()
            .Where(t => t.PedidoId == p.Id && t.Activo && t.FechaExpiracion > ahora).Select(t => t.Token).ToListAsync();
        foreach (var token in tokens)
            await _hub.Clients.Group(Grupos.Seguimiento(token)).SendAsync("EstadoPedido", new { estadoId = p.EstadoId, estado = nombreEstado });
    }

    private static string? Recortar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s.Trim() : s.Trim()[..max]);
}
