using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;
using Routify.Web.Services.Archivos;
using Routify.Web.Services.Parametros;
using Routify.Web.Services.Pedidos;
using Routify.Web.Services.Ruteo;
using Routify.Web.Services.Telemetria;

namespace Routify.Web.Controllers.Api;

public class AccionDto
{
    public Guid Guid { get; set; }
    /// <summary>iniciar | entrega</summary>
    public string Tipo { get; set; } = "";
    public int? RutaId { get; set; }
    public int? ParadaId { get; set; }
    /// <summary>Entregada | Fallida</summary>
    public string? Resultado { get; set; }
    public string? Observacion { get; set; }
    public string? Lat { get; set; }
    public string? Lng { get; set; }
    public DateTime? FechaUtc { get; set; }
    public IFormFile? Foto { get; set; }
    public IFormFile? Firma { get; set; }
}

/// <summary>
/// API de la Web App del repartidor: hoja de ruta, acciones sincronizables (RF-19) y posiciones GPS (RF-17).
/// Las acciones son idempotentes: cada una lleva un GUID generado en el dispositivo, de modo que un reintento
/// tras una desconexión no duplica entregas.
/// Códigos: 200 OK · 422 la acción fue rechazada por una regla de negocio (el cliente no debe reintentar).
/// </summary>
[ApiController, Authorize(Roles = Roles.Repartidor)]
[Route("api/repartidor")]
public class RepartidorApiController : ControllerBase
{
    private readonly RoutifyDbContext _db;
    private readonly PedidoService _pedidos;
    private readonly RutaService _rutas;
    private readonly TelemetriaService _telemetria;
    private readonly IArchivoService _archivos;
    private readonly IConfiguracionService _cfg;

    public RepartidorApiController(RoutifyDbContext db, PedidoService pedidos, RutaService rutas,
                                   TelemetriaService telemetria, IArchivoService archivos, IConfiguracionService cfg)
    {
        _db = db;
        _pedidos = pedidos;
        _rutas = rutas;
        _telemetria = telemetria;
        _archivos = archivos;
        _cfg = cfg;
    }

    private int RepartidorId => User.RepartidorId() ?? 0;

    // ----------------------------------------------------------------- Hoja de ruta (RF-15)
    [HttpGet("ruta")]
    public async Task<IActionResult> Ruta()
    {
        int repId = RepartidorId;
        if (repId == 0) return Forbid();

        var prm = await _cfg.RuteoAsync();
        var hoy = TiempoLima.HoyLima;

        var ruta = await _db.Rutas.AsNoTracking()
            .Include(r => r.Paradas).ThenInclude(p => p.Pedido).ThenInclude(p => p.Cliente)
            .Where(r => r.RepartidorId == repId && r.Fecha == hoy && r.Estado != EstadoRuta.Cancelada)
            .OrderBy(r => r.Estado == EstadoRuta.Activa ? 0 : r.Estado == EstadoRuta.Planificada ? 1 : 2)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync();

        var rep = await _db.Repartidores.AsNoTracking().Include(r => r.Usuario).FirstAsync(r => r.Id == repId);

        if (ruta == null)
        {
            return Ok(new
            {
                repartidorId = repId,
                nombre = rep.Usuario.NombreCompleto,
                generadaUtc = DateTime.UtcNow,
                ruta = (object?)null
            });
        }

        var pedidoIds = ruta.Paradas.Select(p => p.PedidoId).ToList();
        var detalles = await _db.Detalles.AsNoTracking().Where(d => pedidoIds.Contains(d.PedidoId)).ToListAsync();

        var paradas = ruta.Paradas.OrderBy(p => p.Orden).Select(p => new
        {
            id = p.Id,
            orden = p.Orden,
            pedidoId = p.PedidoId,
            codigo = p.Pedido.Codigo,
            cliente = p.Pedido.Cliente.Nombre,
            telefono = p.Pedido.Cliente.Telefono,
            direccion = p.Pedido.DireccionEntrega,
            referencia = p.Pedido.Referencia,
            observaciones = p.Pedido.Observaciones,
            lat = p.Ubicacion.Y,
            lng = p.Ubicacion.X,
            etaUtc = p.Eta,
            estado = p.Estado,
            ventana = p.Pedido.VentanaInicio.HasValue || p.Pedido.VentanaFin.HasValue
                ? $"{p.Pedido.VentanaInicio?.ToString("HH:mm") ?? "—"} - {p.Pedido.VentanaFin?.ToString("HH:mm") ?? "—"}" : null,
            items = detalles.Where(d => d.PedidoId == p.PedidoId).Select(d => new { d.Producto, d.Cantidad, d.Observacion })
        }).ToList();

        return Ok(new
        {
            repartidorId = repId,
            nombre = rep.Usuario.NombreCompleto,
            generadaUtc = DateTime.UtcNow,
            almacen = new { nombre = prm.AlmacenNombre, lat = prm.AlmacenLat, lng = prm.AlmacenLng },
            ruta = new
            {
                id = ruta.Id,
                estado = ruta.Estado,
                distanciaKm = ruta.DistanciaTotal,
                tiempoMin = ruta.TiempoEstimado,
                total = paradas.Count,
                hechas = paradas.Count(p => p.estado != EstadoParada.Pendiente),
                paradas
            }
        });
    }

    // ----------------------------------------------------------------- Acciones sincronizables (RF-18, RF-19)
    [HttpPost("acciones")]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> Acciones([FromForm] AccionDto dto, CancellationToken ct)
    {
        int repId = RepartidorId;
        if (repId == 0) return Forbid();
        if (dto.Guid == Guid.Empty) return Rechazo("Acción sin identificador.");

        // Idempotencia: si ya se procesó, se responde OK sin repetir el efecto.
        if (await _db.Sincronizaciones.AnyAsync(s => s.Guid == dto.Guid, ct))
            return Ok(new { ok = true, duplicado = true });

        return dto.Tipo switch
        {
            "iniciar" => await IniciarRutaAsync(dto, repId, ct),
            "entrega" => await RegistrarEntregaAsync(dto, repId, ct),
            _ => Rechazo("Tipo de acción desconocido.")
        };
    }

    private async Task<IActionResult> IniciarRutaAsync(AccionDto dto, int repId, CancellationToken ct)
    {
        var ruta = await _db.Rutas.Include(r => r.Paradas)
            .FirstOrDefaultAsync(r => r.Id == dto.RutaId && r.RepartidorId == repId, ct);
        if (ruta == null) return Rechazo("La ruta no existe.");

        if (ruta.Estado == EstadoRuta.Activa)
        {
            await RegistrarSincronizacionAsync(dto, repId, "iniciar", ct);
            return Ok(new { ok = true, duplicado = true });
        }
        if (ruta.Estado != EstadoRuta.Planificada) return Rechazo("La ruta ya no se puede iniciar.");
        if (await _db.Rutas.AnyAsync(r => r.RepartidorId == repId && r.Estado == EstadoRuta.Activa && r.Id != ruta.Id, ct))
            return Rechazo("Ya tiene otra ruta activa.");

        ruta.Estado = EstadoRuta.Activa;
        ruta.FechaInicio = DateTime.UtcNow;
        await RegistrarSincronizacionAsync(dto, repId, "iniciar", ct, guardar: false);
        await _db.SaveChangesAsync(ct);

        // Los pedidos pasan a "En ruta" (y se notifica al cliente).
        var ids = ruta.Paradas.Where(p => p.Estado == EstadoParada.Pendiente).Select(p => p.PedidoId).ToList();
        var pedidos = await _db.Pedidos.Where(p => ids.Contains(p.Id) && p.EstadoId == EstadoPedidoIds.EnPreparacion).ToListAsync(ct);
        foreach (var p in pedidos)
            await _pedidos.CambiarEstadoAsync(p, EstadoPedidoIds.EnRuta, User.UsuarioId(), "Ruta iniciada por el repartidor");

        await _rutas.ReoptimizarAsync(repId, ct); // recalcula ETA desde la posición actual
        return Ok(new { ok = true });
    }

    private async Task<IActionResult> RegistrarEntregaAsync(AccionDto dto, int repId, CancellationToken ct)
    {
        var parada = await _db.Paradas.Include(p => p.Ruta).Include(p => p.Pedido)
            .FirstOrDefaultAsync(p => p.Id == dto.ParadaId && p.Ruta.RepartidorId == repId, ct);
        if (parada == null) return Rechazo("La parada no existe o no le pertenece.");

        if (parada.Estado != EstadoParada.Pendiente)
        {
            await RegistrarSincronizacionAsync(dto, repId, "entrega", ct);
            return Ok(new { ok = true, duplicado = true });
        }
        if (parada.Ruta.Estado != EstadoRuta.Activa) return Rechazo("Inicie la ruta antes de registrar entregas.");

        bool entregada = dto.Resultado == EstadoParada.Entregada;
        bool fallida = dto.Resultado == EstadoParada.Fallida;
        if (!entregada && !fallida) return Rechazo("Resultado de entrega no válido.");
        if (entregada && dto.Foto == null && dto.Firma == null) return Rechazo("La entrega requiere una fotografía o una firma (RF-18).");
        if (fallida && string.IsNullOrWhiteSpace(dto.Observacion)) return Rechazo("Indique el motivo de la entrega fallida.");

        Entrada.TryPunto(dto.Lat, dto.Lng, out var punto, out _);
        var ahora = DateTime.UtcNow;
        var fecha = dto.FechaUtc?.ToUniversalTime() ?? ahora;
        if (fecha > ahora) fecha = ahora;

        // Evidencias en disco (fuera de wwwroot)
        var pruebas = new List<PruebaEntrega>();
        try
        {
            if (entregada && dto.Foto != null)
            {
                await using var s = dto.Foto.OpenReadStream();
                string ruta = await _archivos.GuardarPruebaAsync(parada.PedidoId, s, TipoPrueba.Fotografia, ct);
                pruebas.Add(new PruebaEntrega
                {
                    PedidoId = parada.PedidoId, ParadaId = parada.Id, Tipo = TipoPrueba.Fotografia, Archivo = ruta,
                    Observacion = Recortar(dto.Observacion), Ubicacion = punto, GuidCliente = dto.Guid, FechaRegistro = fecha
                });
            }
            if (entregada && dto.Firma != null)
            {
                await using var s = dto.Firma.OpenReadStream();
                string ruta = await _archivos.GuardarPruebaAsync(parada.PedidoId, s, TipoPrueba.Firma, ct);
                pruebas.Add(new PruebaEntrega
                {
                    PedidoId = parada.PedidoId, ParadaId = parada.Id, Tipo = TipoPrueba.Firma, Archivo = ruta,
                    Observacion = Recortar(dto.Observacion), Ubicacion = punto,
                    GuidCliente = pruebas.Count == 0 ? dto.Guid : null, FechaRegistro = fecha
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            return Rechazo(ex.Message);
        }

        parada.Estado = entregada ? EstadoParada.Entregada : EstadoParada.Fallida;
        parada.FechaLlegada = fecha;
        _db.Pruebas.AddRange(pruebas);
        await RegistrarSincronizacionAsync(dto, repId, "entrega", ct, guardar: false);
        await _db.SaveChangesAsync(ct);

        // Estado del pedido (RF-09) y notificación al cliente
        var pedido = parada.Pedido;
        if (pedido.EstadoId == EstadoPedidoIds.EnPreparacion)
            await _pedidos.CambiarEstadoAsync(pedido, EstadoPedidoIds.EnRuta, User.UsuarioId(), "Ruta iniciada");
        await _pedidos.CambiarEstadoAsync(pedido, entregada ? EstadoPedidoIds.Entregado : EstadoPedidoIds.Fallido,
                                          User.UsuarioId(), Recortar(dto.Observacion) ?? (entregada ? "Entrega realizada" : "Entrega fallida"));

        // ¿Ruta completada?
        bool quedan = await _db.Paradas.AnyAsync(p => p.RutaId == parada.RutaId && p.Estado == EstadoParada.Pendiente, ct);
        if (!quedan)
        {
            var ruta = await _db.Rutas.FirstAsync(r => r.Id == parada.RutaId, ct);
            ruta.Estado = EstadoRuta.Completada;
            ruta.FechaFin = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { ok = true, parada = parada.Estado, rutaCompletada = !quedan });
    }

    // ----------------------------------------------------------------- GPS (RF-17)
    [HttpPost("posicion")]
    public async Task<IActionResult> Posicion([FromBody] PosicionDto pos)
    {
        if (RepartidorId == 0) return Forbid();
        await _telemetria.RegistrarAsync(RepartidorId, pos);
        return Ok(new { ok = true });
    }

    /// <summary>Puntos GPS acumulados mientras no había conexión.</summary>
    [HttpPost("gps-lote")]
    public async Task<IActionResult> GpsLote([FromBody] List<PosicionDto> puntos)
    {
        if (RepartidorId == 0) return Forbid();
        if (puntos == null || puntos.Count == 0) return Ok(new { ok = true, procesados = 0 });

        var ordenados = puntos.Take(200).OrderBy(p => p.FechaUtc ?? DateTime.UtcNow).ToList();
        foreach (var p in ordenados) await _telemetria.RegistrarAsync(RepartidorId, p);
        return Ok(new { ok = true, procesados = ordenados.Count });
    }

    // ----------------------------------------------------------------- Auxiliares
    private IActionResult Rechazo(string mensaje) => UnprocessableEntity(new { ok = false, error = mensaje });

    private async Task RegistrarSincronizacionAsync(AccionDto dto, int repId, string tipo, CancellationToken ct, bool guardar = true)
    {
        _db.Sincronizaciones.Add(new SincronizacionOffline { Guid = dto.Guid, RepartidorId = repId, Tipo = tipo });
        if (guardar) await _db.SaveChangesAsync(ct);
    }

    private static string? Recortar(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= 500 ? s.Trim() : s.Trim()[..500]);
}
