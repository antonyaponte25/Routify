using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Indicadores;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

[Authorize]
public class HomeController : BaseController
{
    private readonly RoutifyDbContext _db;
    private readonly IndicadoresService _kpis;

    public HomeController(RoutifyDbContext db, IndicadoresService kpis)
    {
        _db = db;
        _kpis = kpis;
    }

    /// <summary>Panel de despacho (RF-13, RF-14): indicadores, pedidos activos y mapa en vivo.</summary>
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole(Roles.Repartidor)) return RedirectToAction("Index", "Repartidor");

        var vm = new DashboardVm
        {
            Kpis = await _kpis.ObtenerAsync(),
            Zonas = await _db.Zonas.AsNoTracking().Where(z => z.Activa).OrderBy(z => z.Nombre).ToListAsync(),
            Activos = await _db.Pedidos.AsNoTracking()
                .Where(p => p.EstadoId == EstadoPedidoIds.Pendiente || p.EstadoId == EstadoPedidoIds.EnPreparacion || p.EstadoId == EstadoPedidoIds.EnRuta)
                .OrderBy(p => p.EstadoId).ThenBy(p => p.FechaRegistro)
                .Take(60)
                .Select(p => new PedidoFilaVm
                {
                    Id = p.Id,
                    Codigo = p.Codigo,
                    Cliente = p.Cliente.Nombre,
                    Direccion = p.DireccionEntrega,
                    Zona = p.Zona != null ? p.Zona.Nombre : null,
                    EstadoId = p.EstadoId,
                    Estado = p.Estado.Nombre,
                    Repartidor = p.Repartidor != null ? p.Repartidor.Usuario.Nombre + " " + p.Repartidor.Usuario.Apellido : null,
                    FechaRegistro = p.FechaRegistro
                }).ToListAsync()
        };
        return View(vm);
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
