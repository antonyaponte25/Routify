using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Hubs;
using Routify.Web.Services.Parametros;
using Routify.Web.Services.Ruteo;

namespace Routify.Web.Services.Telemetria;

public class PosicionDto
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public float? Precision { get; set; }
    public float? Velocidad { get; set; }
    public DateTime? FechaUtc { get; set; }
}

/// <summary>
/// Recibe la posición GPS del repartidor (RF-17):
///  - actualiza su ubicación actual;
///  - guarda historial sólo si se movió ≥ 20 m o pasaron ≥ 30 s (evita llenar la base de datos);
///  - difunde la posición por SignalR al panel de despacho y a los clientes con enlace vigente;
///  - recalcula el ETA de las paradas pendientes (RF-11) como máximo cada 30 s por ruta.
/// </summary>
public class TelemetriaService
{
    private record UltimoPunto(double Lat, double Lng, DateTime Fecha);

    private static readonly ConcurrentDictionary<int, UltimoPunto> UltimoGuardado = new();
    private static readonly ConcurrentDictionary<int, DateTime> UltimoCalculoEta = new();

    private readonly RoutifyDbContext _db;
    private readonly IHubContext<TrackingHub> _hub;
    private readonly IConfiguracionService _cfg;

    public TelemetriaService(RoutifyDbContext db, IHubContext<TrackingHub> hub, IConfiguracionService cfg)
    {
        _db = db;
        _hub = hub;
        _cfg = cfg;
    }

    public async Task RegistrarAsync(int repartidorId, PosicionDto pos)
    {
        if (!GeoUtils.CoordenadasValidas(pos.Lat, pos.Lng)) return;

        var ahora = DateTime.UtcNow;
        var fecha = pos.FechaUtc?.ToUniversalTime() ?? ahora;
        if (fecha > ahora.AddMinutes(2)) fecha = ahora;

        var rep = await _db.Repartidores.Include(r => r.Usuario).FirstOrDefaultAsync(r => r.Id == repartidorId);
        if (rep == null) return;

        var ruta = await _db.Rutas
            .Where(r => r.RepartidorId == repartidorId && r.Estado == EstadoRuta.Activa)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        bool esReciente = rep.UltimaActualizacion == null || fecha >= rep.UltimaActualizacion;
        if (esReciente)
        {
            rep.UbicacionActual = GeoUtils.CrearPunto(pos.Lat, pos.Lng);
            rep.UltimaActualizacion = fecha;
        }

        // Historial (telemetría): sólo mientras la ruta está activa.
        if (ruta != null && await DebeGuardarAsync(repartidorId, pos.Lat, pos.Lng, fecha))
        {
            _db.Ubicaciones.Add(new Models.UbicacionRepartidor
            {
                RepartidorId = repartidorId,
                RutaId = ruta.Id,
                Ubicacion = GeoUtils.CrearPunto(pos.Lat, pos.Lng),
                PrecisionM = pos.Precision,
                VelocidadKmh = pos.Velocidad,
                FechaHora = fecha
            });
            UltimoGuardado[repartidorId] = new UltimoPunto(pos.Lat, pos.Lng, fecha);
        }

        await _db.SaveChangesAsync();

        // Sólo se difunden datos "en vivo" (los puntos acumulados sin conexión no se retransmiten).
        if (esReciente && (ahora - fecha).TotalSeconds < 120)
            await DifundirAsync(rep, ruta, pos, fecha);
    }

    private async Task<bool> DebeGuardarAsync(int repartidorId, double lat, double lng, DateTime fecha)
    {
        if (!UltimoGuardado.TryGetValue(repartidorId, out var previo)) return true;

        double minM = await _cfg.GetDoubleAsync(ClavesConfiguracion.GpsMinDistanciaM, 20);
        double minS = await _cfg.GetDoubleAsync(ClavesConfiguracion.GpsMinSegundos, 30);

        double metros = GeoUtils.HaversineKm(previo.Lat, previo.Lng, lat, lng) * 1000.0;
        double segundos = Math.Abs((fecha - previo.Fecha).TotalSeconds);
        return metros >= minM || segundos >= minS;
    }

    private async Task DifundirAsync(Models.Repartidor rep, Models.Ruta? ruta, PosicionDto pos, DateTime fecha)
    {
        await _hub.Clients.Group(Grupos.Despacho).SendAsync("PosicionRepartidor", new
        {
            repartidorId = rep.Id,
            nombre = rep.Usuario.NombreCompleto,
            lat = pos.Lat,
            lng = pos.Lng,
            velocidad = pos.Velocidad,
            fechaUtc = fecha,
            rutaId = ruta?.Id
        });

        if (ruta == null) return;

        // Paradas pendientes de la ruta activa
        var paradas = await _db.Paradas.Include(p => p.Pedido)
            .Where(p => p.RutaId == ruta.Id && p.Estado == EstadoParada.Pendiente)
            .OrderBy(p => p.Orden)
            .ToListAsync();
        if (paradas.Count == 0) return;

        var pedidoIds = paradas.Select(p => p.PedidoId).ToList();
        var ahora = DateTime.UtcNow;
        var tokens = await _db.Enlaces.AsNoTracking()
            .Where(t => pedidoIds.Contains(t.PedidoId) && t.Activo && t.FechaExpiracion > ahora)
            .Select(t => new { t.PedidoId, t.Token })
            .ToListAsync();

        // Posición hacia cada cliente (sin datos del resto de la flota)
        foreach (var t in tokens)
        {
            await _hub.Clients.Group(Grupos.Seguimiento(t.Token)).SendAsync("PosicionRepartidor", new
            {
                lat = pos.Lat,
                lng = pos.Lng,
                fechaUtc = fecha
            });
        }

        // Recalcular ETA (RF-11) como máximo cada 30 s por ruta
        if (UltimoCalculoEta.TryGetValue(ruta.Id, out var ult) && (ahora - ult).TotalSeconds < 30) return;
        UltimoCalculoEta[ruta.Id] = ahora;

        var prm = await _cfg.RuteoAsync();
        var secuencia = paradas.Select(p => new PuntoEntrega(
            p.PedidoId, p.Ubicacion.Y, p.Ubicacion.X, p.Pedido.VentanaInicio, p.Pedido.VentanaFin)).ToList();
        var plan = OptimizadorRutas.Simular(pos.Lat, pos.Lng, secuencia, prm, ahora);

        foreach (var pp in plan.Paradas)
        {
            var parada = paradas[pp.Orden - 1];
            parada.Eta = pp.EtaUtc;
        }
        await _db.SaveChangesAsync();

        await _hub.Clients.Group(Grupos.Despacho).SendAsync("EtaActualizado", new
        {
            rutaId = ruta.Id,
            paradas = plan.Paradas.Select(pp => new { pedidoId = pp.PedidoId, etaUtc = pp.EtaUtc })
        });

        foreach (var t in tokens)
        {
            var pp = plan.Paradas.FirstOrDefault(x => x.PedidoId == t.PedidoId);
            if (pp == null) continue;
            await _hub.Clients.Group(Grupos.Seguimiento(t.Token)).SendAsync("EtaPedido", new
            {
                etaUtc = pp.EtaUtc,
                paradasAntes = pp.Orden - 1
            });
        }
    }
}
