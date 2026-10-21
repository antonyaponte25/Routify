using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Parametros;
using Routify.Web.Services.Pedidos;
using Routify.Web.Services.Ruteo;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

[Authorize(Roles = Roles.AdminOperador)]
public class RutasController : BaseController
{
    private readonly RoutifyDbContext _db;
    private readonly RutaService _rutas;
    private readonly PedidoService _pedidos;
    private readonly IConfiguracionService _cfg;

    public RutasController(RoutifyDbContext db, RutaService rutas, PedidoService pedidos, IConfiguracionService cfg)
    {
        _db = db;
        _rutas = rutas;
        _pedidos = pedidos;
        _cfg = cfg;
    }

    public async Task<IActionResult> Index(DateTime? fecha)
    {
        var dia = (fecha ?? TiempoLima.HoyLima).Date;
        ViewBag.Fecha = dia;

        var rutas = await _db.Rutas.AsNoTracking()
            .Where(r => r.Fecha == dia)
            .OrderBy(r => r.Estado == EstadoRuta.Activa ? 0 : r.Estado == EstadoRuta.Planificada ? 1 : 2).ThenBy(r => r.Id)
            .Select(r => new RutaFilaVm
            {
                Id = r.Id,
                Repartidor = r.Repartidor.Usuario.Nombre + " " + r.Repartidor.Usuario.Apellido,
                Fecha = r.Fecha,
                Estado = r.Estado,
                Paradas = r.Paradas.Count,
                Entregadas = r.Paradas.Count(p => p.Estado == EstadoParada.Entregada),
                DistanciaKm = r.DistanciaTotal,
                TiempoMin = r.TiempoEstimado
            }).ToListAsync();
        return View(rutas);
    }

    public async Task<IActionResult> Detalle(int id)
    {
        var ruta = await _db.Rutas.AsNoTracking().Include(r => r.Repartidor).ThenInclude(r => r.Usuario)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (ruta == null) return NotFound();

        var prm = await _cfg.RuteoAsync();
        return View(new RutaDetalleVm
        {
            Ruta = ruta,
            RepartidorNombre = ruta.Repartidor.Usuario.NombreCompleto,
            Paradas = await _db.Paradas.AsNoTracking().Include(p => p.Pedido).ThenInclude(p => p.Cliente)
                .Where(p => p.RutaId == id).OrderBy(p => p.Orden).ToListAsync(),
            AlmacenLat = prm.AlmacenLat,
            AlmacenLng = prm.AlmacenLng,
            AlmacenNombre = prm.AlmacenNombre
        });
    }

    [HttpPost]
    public async Task<IActionResult> Reoptimizar(int id)
    {
        var ruta = await _db.Rutas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (ruta == null) return NotFound();
        if (ruta.Estado is EstadoRuta.Completada or EstadoRuta.Cancelada)
        {
            Fallo("Solo se pueden reoptimizar rutas planificadas o activas.");
            return RedirectToAction(nameof(Detalle), new { id });
        }
        await _rutas.ReoptimizarAsync(ruta.RepartidorId);
        Exito("Ruta reoptimizada.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    /// <summary>Cancela una ruta planificada: sus pedidos vuelven a Pendiente.</summary>
    [HttpPost]
    public async Task<IActionResult> Cancelar(int id)
    {
        var ruta = await _db.Rutas.AsNoTracking().Include(r => r.Paradas).FirstOrDefaultAsync(r => r.Id == id);
        if (ruta == null) return NotFound();
        if (ruta.Estado != EstadoRuta.Planificada)
        {
            Fallo("Solo se puede cancelar una ruta planificada (aún no iniciada).");
            return RedirectToAction(nameof(Detalle), new { id });
        }

        foreach (var pid in ruta.Paradas.Where(p => p.Estado == EstadoParada.Pendiente).Select(p => p.PedidoId).ToList())
            await _pedidos.DesasignarAsync(pid, UsuarioId);

        Exito("Ruta cancelada. Los pedidos volvieron a estar pendientes de asignación.");
        return RedirectToAction(nameof(Index));
    }
}
