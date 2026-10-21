using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Parametros;

namespace Routify.Web.Controllers.Api;

/// <summary>Estado completo para el mapa en vivo del panel de despacho (RF-13).</summary>
[ApiController, Authorize(Roles = Roles.AdminOperador)]
[Route("api/despacho")]
public class DespachoApiController : ControllerBase
{
    private static readonly string[] Paleta =
        { "#4F46E5", "#0EA5E9", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#EC4899", "#14B8A6" };

    private readonly RoutifyDbContext _db;
    private readonly IConfiguracionService _cfg;

    public DespachoApiController(RoutifyDbContext db, IConfiguracionService cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    [HttpGet("estado")]
    public async Task<IActionResult> Estado()
    {
        var prm = await _cfg.RuteoAsync();
        var hoy = TiempoLima.HoyLima;

        var reps = await _db.Repartidores.AsNoTracking().Include(r => r.Usuario)
            .Where(r => r.Usuario.Estado == "Activo").ToListAsync();

        var rutas = await _db.Rutas.AsNoTracking()
            .Include(r => r.Paradas).ThenInclude(p => p.Pedido).ThenInclude(p => p.Cliente)
            .Where(r => r.Fecha == hoy && (r.Estado == EstadoRuta.Activa || r.Estado == EstadoRuta.Planificada))
            .ToListAsync();

        var pendientes = await _db.Pedidos.AsNoTracking().Include(p => p.Cliente).Include(p => p.Zona)
            .Where(p => p.EstadoId == EstadoPedidoIds.Pendiente && p.Ubicacion != null)
            .OrderBy(p => p.FechaRegistro).Take(200).ToListAsync();

        var zonas = await _db.Zonas.AsNoTracking().Where(z => z.Activa).ToListAsync();

        string Color(int repartidorId) => Paleta[repartidorId % Paleta.Length];

        return Ok(new
        {
            almacen = new { nombre = prm.AlmacenNombre, lat = prm.AlmacenLat, lng = prm.AlmacenLng },
            repartidores = reps.Select(r => new
            {
                id = r.Id,
                nombre = r.Usuario.NombreCompleto,
                disponible = r.Disponibilidad,
                color = Color(r.Id),
                lat = r.UbicacionActual?.Y,
                lng = r.UbicacionActual?.X,
                fechaUtc = r.UltimaActualizacion,
                carga = rutas.Where(x => x.RepartidorId == r.Id).Sum(x => x.Paradas.Count(p => p.Estado == EstadoParada.Pendiente))
            }),
            rutas = rutas.Select(r => new
            {
                id = r.Id,
                repartidorId = r.RepartidorId,
                estado = r.Estado,
                color = Color(r.RepartidorId),
                linea = r.Geometria?.Coordinates.Select(c => new[] { c.Y, c.X }),
                paradas = r.Paradas.OrderBy(p => p.Orden).Select(p => new
                {
                    pedidoId = p.PedidoId,
                    codigo = p.Pedido.Codigo,
                    cliente = p.Pedido.Cliente.Nombre,
                    orden = p.Orden,
                    estado = p.Estado,
                    lat = p.Ubicacion.Y,
                    lng = p.Ubicacion.X,
                    etaUtc = p.Eta
                })
            }),
            pendientes = pendientes.Select(p => new
            {
                pedidoId = p.Id,
                codigo = p.Codigo,
                cliente = p.Cliente.Nombre,
                direccion = p.DireccionEntrega,
                zona = p.Zona?.Nombre,
                lat = p.Ubicacion!.Y,
                lng = p.Ubicacion.X
            }),
            zonas = zonas.Select(z => new { id = z.Id, nombre = z.Nombre, color = z.Color, vertices = GeoUtils.Vertices(z.Poligono) })
        });
    }
}

/// <summary>Entrega un token antiforgery fresco a páginas ya cargadas (p. ej. la PWA al sincronizar tras estar sin conexión).</summary>
[ApiController, Authorize]
public class CsrfController : ControllerBase
{
    private readonly IAntiforgery _antiforgery;

    public CsrfController(IAntiforgery antiforgery) => _antiforgery = antiforgery;

    [HttpGet("/api/csrf")]
    public IActionResult Obtener()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(new { token = tokens.RequestToken });
    }
}
