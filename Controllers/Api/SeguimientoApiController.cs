using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;

namespace Routify.Web.Controllers.Api;

/// <summary>Datos del seguimiento público. Solo expone lo mínimo necesario del pedido (RNF-09).</summary>
[ApiController, AllowAnonymous]
[Route("api/seguimiento")]
public class SeguimientoApiController : ControllerBase
{
    private readonly RoutifyDbContext _db;
    private readonly IConfiguration _cfg;

    public SeguimientoApiController(RoutifyDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    [HttpGet("{token}")]
    [EnableRateLimiting("seguimiento")]
    public async Task<IActionResult> Obtener(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return NotFound(new { vigente = false });

        var ahora = DateTime.UtcNow;
        var enlace = await _db.Enlaces.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Token == token && t.Activo && t.FechaExpiracion > ahora);
        if (enlace == null) return NotFound(new { vigente = false });

        var p = await _db.Pedidos.AsNoTracking()
            .Include(x => x.Estado).Include(x => x.Cliente)
            .Include(x => x.Repartidor!).ThenInclude(r => r.Usuario)
            .FirstAsync(x => x.Id == enlace.PedidoId);

        var parada = await _db.Paradas.AsNoTracking()
            .Where(x => x.PedidoId == p.Id).OrderByDescending(x => x.Id).FirstOrDefaultAsync();

        int paradasAntes = 0;
        if (parada != null && parada.Estado == EstadoParada.Pendiente)
        {
            paradasAntes = await _db.Paradas.CountAsync(x => x.RutaId == parada.RutaId
                                                             && x.Estado == EstadoParada.Pendiente && x.Orden < parada.Orden);
        }

        // La posición solo se comparte mientras el pedido está en ruta.
        object? posicion = null;
        if (p.EstadoId == EstadoPedidoIds.EnRuta && p.Repartidor?.UbicacionActual != null)
        {
            posicion = new { lat = p.Repartidor.UbicacionActual.Y, lng = p.Repartidor.UbicacionActual.X, fechaUtc = p.Repartidor.UltimaActualizacion };
        }

        var historial = await _db.Historial.AsNoTracking().Include(h => h.EstadoNuevo)
            .Where(h => h.PedidoId == p.Id).OrderBy(h => h.Fecha).ThenBy(h => h.Id)
            .Select(h => new { estadoId = h.EstadoNuevoId, estado = h.EstadoNuevo.Nombre, fechaUtc = h.Fecha })
            .ToListAsync();

        string primerNombre = (p.Repartidor?.Usuario.Nombre ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        return Ok(new
        {
            vigente = true,
            comercio = _cfg["App:NombreComercio"] ?? "Routify",
            codigo = p.Codigo,
            estadoId = p.EstadoId,
            estado = p.Estado.Nombre,
            cliente = p.Cliente.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            direccion = p.DireccionEntrega,
            etaUtc = p.EstadoId is EstadoPedidoIds.Entregado or EstadoPedidoIds.Fallido ? null : parada?.Eta,
            paradasAntes,
            repartidor = p.EstadoId == EstadoPedidoIds.EnRuta || p.EstadoId == EstadoPedidoIds.Entregado ? primerNombre : null,
            vehiculo = p.EstadoId == EstadoPedidoIds.EnRuta ? p.Repartidor?.Vehiculo : null,
            destino = p.Ubicacion == null ? null : new { lat = p.Ubicacion.Y, lng = p.Ubicacion.X },
            posicion,
            historial
        });
    }
}
