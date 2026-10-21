using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;
using Routify.Web.Services.Parametros;

namespace Routify.Web.Services.Ruteo;

public record SimulacionAsignacion(double DistanciaTotalKm, int TiempoTotalMin, DateTime? EtaPedidoUtc, int ParadasResultantes);

/// <summary>
/// Persiste las rutas del día de cada repartidor aplicando el optimizador propio.
/// Una ruta (Planificada o Activa) por repartidor y día; las paradas ya atendidas se conservan.
/// </summary>
public class RutaService
{
    private readonly RoutifyDbContext _db;
    private readonly IConfiguracionService _cfg;

    public RutaService(RoutifyDbContext db, IConfiguracionService cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    private static readonly int[] EstadosPorEntregar = { EstadoPedidoIds.EnPreparacion, EstadoPedidoIds.EnRuta };

    /// <summary>
    /// Recalcula la secuencia óptima de las paradas pendientes del repartidor (RF-06, RF-10, RF-11).
    /// Si la ruta ya está activa, parte de la posición actual del repartidor; si no, del almacén.
    /// </summary>
    public async Task<Ruta?> ReoptimizarAsync(int repartidorId, CancellationToken ct = default)
    {
        var prm = await _cfg.RuteoAsync();
        var rep = await _db.Repartidores.FirstOrDefaultAsync(r => r.Id == repartidorId, ct);
        if (rep == null) return null;

        var hoy = TiempoLima.HoyLima;
        var ahora = DateTime.UtcNow;

        var ruta = await _db.Rutas.Include(r => r.Paradas)
            .Where(r => r.RepartidorId == repartidorId && r.Fecha == hoy
                        && (r.Estado == EstadoRuta.Activa || r.Estado == EstadoRuta.Planificada))
            .OrderBy(r => r.Estado == EstadoRuta.Activa ? 0 : 1)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var pedidos = await _db.Pedidos
            .Where(p => p.RepartidorId == repartidorId && EstadosPorEntregar.Contains(p.EstadoId) && p.Ubicacion != null)
            .ToListAsync(ct);

        if (ruta == null)
        {
            if (pedidos.Count == 0) return null;
            ruta = new Ruta { RepartidorId = repartidorId, Fecha = hoy, Estado = EstadoRuta.Planificada };
            _db.Rutas.Add(ruta);
        }

        var idsPendientes = pedidos.Select(p => p.Id).ToHashSet();
        var existentes = ruta.Paradas.ToDictionary(x => x.PedidoId);

        // Paradas pendientes cuyo pedido ya no pertenece al repartidor: se eliminan.
        foreach (var obsoleta in ruta.Paradas.Where(x => x.Estado == EstadoParada.Pendiente && !idsPendientes.Contains(x.PedidoId)).ToList())
        {
            ruta.Paradas.Remove(obsoleta);
            _db.Paradas.Remove(obsoleta);
            existentes.Remove(obsoleta.PedidoId);
        }

        var hechas = ruta.Paradas.Where(x => !idsPendientes.Contains(x.PedidoId) && x.Estado != EstadoParada.Pendiente)
                                 .OrderBy(x => x.Orden).ToList();

        // Sin nada pendiente: se cierra o cancela la ruta.
        if (pedidos.Count == 0)
        {
            if (hechas.Count == 0)
            {
                ruta.Estado = EstadoRuta.Cancelada;
            }
            else if (ruta.Estado == EstadoRuta.Activa)
            {
                ruta.Estado = EstadoRuta.Completada;
                ruta.FechaFin = ahora;
            }
            await _db.SaveChangesAsync(ct);
            return ruta;
        }

        double origenLat = prm.AlmacenLat, origenLng = prm.AlmacenLng;
        if (ruta.Estado == EstadoRuta.Activa && rep.UbicacionActual != null)
        {
            origenLat = rep.UbicacionActual.Y;
            origenLng = rep.UbicacionActual.X;
        }

        var puntos = pedidos.Select(p => new PuntoEntrega(p.Id, p.Ubicacion!.Y, p.Ubicacion.X, p.VentanaInicio, p.VentanaFin)).ToList();
        var plan = OptimizadorRutas.Optimizar(origenLat, origenLng, puntos, prm, ahora);

        int baseOrden = hechas.Count == 0 ? 0 : hechas.Max(x => x.Orden);
        var porId = pedidos.ToDictionary(p => p.Id);

        foreach (var pp in plan.Paradas)
        {
            if (!existentes.TryGetValue(pp.PedidoId, out var parada))
            {
                parada = new Parada { PedidoId = pp.PedidoId, Ruta = ruta };
                ruta.Paradas.Add(parada);
                _db.Paradas.Add(parada);
                existentes[pp.PedidoId] = parada;
            }
            parada.Orden = baseOrden + pp.Orden;
            parada.Eta = pp.EtaUtc;
            parada.Distancia = (decimal)pp.DistanciaKm;
            parada.Estado = EstadoParada.Pendiente;
            parada.FechaLlegada = null;
            parada.Ubicacion = porId[pp.PedidoId].Ubicacion!;
        }

        decimal distanciaHechas = hechas.Sum(x => x.Distancia ?? 0m);
        ruta.DistanciaTotal = distanciaHechas + (decimal)plan.DistanciaTotalKm;
        int transcurridos = ruta.Estado == EstadoRuta.Activa && ruta.FechaInicio.HasValue
            ? (int)Math.Max(0, (ahora - ruta.FechaInicio.Value).TotalMinutes) : 0;
        ruta.TiempoEstimado = transcurridos + plan.TiempoTotalMin;

        // Geometría: almacén -> paradas atendidas -> paradas pendientes (en orden)
        var linea = new List<(double Lat, double Lng)> { (prm.AlmacenLat, prm.AlmacenLng) };
        linea.AddRange(hechas.Select(x => (x.Ubicacion.Y, x.Ubicacion.X)));
        linea.AddRange(plan.Paradas.Select(x => (x.Lat, x.Lng)));
        ruta.Geometria = GeoUtils.CrearLinea(linea);

        await _db.SaveChangesAsync(ct);
        return ruta;
    }

    /// <summary>Estima el efecto de asignar un pedido a un repartidor (distancia total, ETA del pedido) sin guardar nada.</summary>
    public async Task<SimulacionAsignacion?> SimularAsync(Pedido pedido, Repartidor rep, CancellationToken ct = default)
    {
        if (pedido.Ubicacion == null) return null;
        var prm = await _cfg.RuteoAsync();

        var hoy = TiempoLima.HoyLima;
        bool rutaActiva = await _db.Rutas.AnyAsync(r => r.RepartidorId == rep.Id && r.Fecha == hoy && r.Estado == EstadoRuta.Activa, ct);

        var actuales = await _db.Pedidos.AsNoTracking()
            .Where(p => p.RepartidorId == rep.Id && EstadosPorEntregar.Contains(p.EstadoId) && p.Ubicacion != null && p.Id != pedido.Id)
            .ToListAsync(ct);

        var puntos = actuales.Select(p => new PuntoEntrega(p.Id, p.Ubicacion!.Y, p.Ubicacion.X, p.VentanaInicio, p.VentanaFin)).ToList();
        puntos.Add(new PuntoEntrega(pedido.Id, pedido.Ubicacion.Y, pedido.Ubicacion.X, pedido.VentanaInicio, pedido.VentanaFin));

        double lat = prm.AlmacenLat, lng = prm.AlmacenLng;
        if (rutaActiva && rep.UbicacionActual != null) { lat = rep.UbicacionActual.Y; lng = rep.UbicacionActual.X; }

        var plan = OptimizadorRutas.Optimizar(lat, lng, puntos, prm, DateTime.UtcNow);
        var suya = plan.Paradas.FirstOrDefault(x => x.PedidoId == pedido.Id);
        return new SimulacionAsignacion(plan.DistanciaTotalKm, plan.TiempoTotalMin, suya?.EtaUtc, plan.Paradas.Count);
    }

    /// <summary>Pedidos asignados y por entregar de un repartidor (carga actual).</summary>
    public Task<int> CargaActualAsync(int repartidorId, CancellationToken ct = default) =>
        _db.Pedidos.CountAsync(p => p.RepartidorId == repartidorId && EstadosPorEntregar.Contains(p.EstadoId), ct);
}
