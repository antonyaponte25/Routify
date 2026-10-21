using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Parametros;

namespace Routify.Web.Services.Indicadores;

public record RendimientoRepartidor(string Nombre, int Entregadas, int Fallidas, int Pendientes);

public record Kpis(
    int PedidosHoy, int PedidosAyer,
    int EnRuta,
    int EntregasHoy, int EntregasAyer,
    int PorAsignar,
    double? PrecisionEta, int ToleranciaEtaMin, int MuestrasEta,
    IReadOnlyList<RendimientoRepartidor> PorRepartidor);

/// <summary>Indicadores de la jornada para el panel de despacho (RF-14). Jornada = día calendario en hora de Lima.</summary>
public class IndicadoresService
{
    private readonly RoutifyDbContext _db;
    private readonly IConfiguracionService _cfg;

    public IndicadoresService(RoutifyDbContext db, IConfiguracionService cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    public async Task<Kpis> ObtenerAsync()
    {
        var hoy = TiempoLima.InicioDiaUtc(TiempoLima.HoyLima);
        var ayer = hoy.AddDays(-1);
        var manana = hoy.AddDays(1);

        int pedidosHoy = await _db.Pedidos.CountAsync(p => p.FechaRegistro >= hoy && p.FechaRegistro < manana);
        int pedidosAyer = await _db.Pedidos.CountAsync(p => p.FechaRegistro >= ayer && p.FechaRegistro < hoy);
        int enRuta = await _db.Pedidos.CountAsync(p => p.EstadoId == EstadoPedidoIds.EnRuta);
        int porAsignar = await _db.Pedidos.CountAsync(p => p.EstadoId == EstadoPedidoIds.Pendiente);
        int entregasHoy = await _db.Pedidos.CountAsync(p => p.EstadoId == EstadoPedidoIds.Entregado && p.FechaActualizacion >= hoy && p.FechaActualizacion < manana);
        int entregasAyer = await _db.Pedidos.CountAsync(p => p.EstadoId == EstadoPedidoIds.Entregado && p.FechaActualizacion >= ayer && p.FechaActualizacion < hoy);

        // Precisión del ETA: % de entregas cuya hora real estuvo dentro de la tolerancia (últimos 7 días).
        int tolerancia = await _cfg.GetIntAsync(ClavesConfiguracion.ToleranciaEtaMin, 10);
        var desde = hoy.AddDays(-6);
        var muestras = await _db.Paradas.AsNoTracking()
            .Where(p => p.Estado == EstadoParada.Entregada && p.Eta != null && p.FechaLlegada != null && p.FechaLlegada >= desde)
            .Select(p => new { p.Eta, p.FechaLlegada })
            .ToListAsync();
        double? precision = muestras.Count == 0
            ? null
            : Math.Round(100.0 * muestras.Count(m => Math.Abs((m.FechaLlegada!.Value - m.Eta!.Value).TotalMinutes) <= tolerancia) / muestras.Count, 1);

        var filas = await _db.Paradas.AsNoTracking()
            .Where(p => p.Ruta.Fecha == TiempoLima.HoyLima)
            .Select(p => new { Nombre = p.Ruta.Repartidor.Usuario.Nombre + " " + p.Ruta.Repartidor.Usuario.Apellido, p.Estado })
            .ToListAsync();
        var porRep = filas.GroupBy(f => f.Nombre)
            .Select(g => new RendimientoRepartidor(
                g.Key,
                g.Count(x => x.Estado == EstadoParada.Entregada),
                g.Count(x => x.Estado == EstadoParada.Fallida),
                g.Count(x => x.Estado == EstadoParada.Pendiente)))
            .OrderByDescending(r => r.Entregadas).ToList();

        return new Kpis(pedidosHoy, pedidosAyer, enRuta, entregasHoy, entregasAyer, porAsignar,
                        precision, tolerancia, muestras.Count, porRep);
    }
}
