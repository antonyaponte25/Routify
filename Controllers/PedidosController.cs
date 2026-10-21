using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;
using Routify.Web.Services.Notificaciones;
using Routify.Web.Services.Parametros;
using Routify.Web.Services.Pedidos;
using Routify.Web.Services.Ruteo;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

[Authorize(Roles = Roles.AdminOperador)]
public class PedidosController : BaseController
{
    private const int TamanoPagina = 25;

    private readonly RoutifyDbContext _db;
    private readonly PedidoService _svc;
    private readonly RutaService _rutas;
    private readonly IConfiguracionService _cfg;
    private readonly INotificacionService _notif;
    private readonly NotificacionCola _cola;
    private readonly IConfiguration _conf;

    public PedidosController(RoutifyDbContext db, PedidoService svc, RutaService rutas, IConfiguracionService cfg,
                             INotificacionService notif, NotificacionCola cola, IConfiguration conf)
    {
        _db = db;
        _svc = svc;
        _rutas = rutas;
        _cfg = cfg;
        _notif = notif;
        _cola = cola;
        _conf = conf;
    }

    // ============================================================ Listado (RF-08)
    public async Task<IActionResult> Index([FromQuery] PedidoFiltroVm f)
    {
        var q = _db.Pedidos.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            var t = f.Texto.Trim();
            q = q.Where(p => p.Codigo.Contains(t) || p.Cliente.Nombre.Contains(t) || p.DireccionEntrega.Contains(t));
        }
        if (f.ZonaId.HasValue) q = q.Where(p => p.ZonaId == f.ZonaId);
        if (f.Desde.HasValue)
        {
            var d = TiempoLima.InicioDiaUtc(f.Desde.Value);
            q = q.Where(p => p.FechaRegistro >= d);
        }
        if (f.Hasta.HasValue)
        {
            var h = TiempoLima.InicioDiaUtc(f.Hasta.Value).AddDays(1);
            q = q.Where(p => p.FechaRegistro < h);
        }

        var conteos = await q.GroupBy(p => p.EstadoId).Select(g => new { Estado = g.Key, N = g.Count() }).ToListAsync();

        if (f.EstadoId.HasValue) q = q.Where(p => p.EstadoId == f.EstadoId);

        int total = await q.CountAsync();
        int paginas = Math.Max(1, (int)Math.Ceiling(total / (double)TamanoPagina));
        int pagina = Math.Min(Math.Max(1, f.Pagina), paginas);

        var items = await q.OrderByDescending(p => p.FechaRegistro).Skip((pagina - 1) * TamanoPagina).Take(TamanoPagina)
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
            }).ToListAsync();

        f.Pagina = pagina;
        var vm = new PedidoListaVm
        {
            Filtro = f,
            Items = items,
            Total = total,
            Pagina = pagina,
            TotalPaginas = paginas,
            Conteos = conteos.ToDictionary(c => c.Estado, c => c.N),
            Zonas = await _db.Zonas.AsNoTracking().OrderBy(z => z.Nombre)
                .Select(z => new SelectListItem(z.Nombre, z.Id.ToString())).ToListAsync()
        };
        return View(vm);
    }

    // ============================================================ Registro (RF-05)
    [HttpGet]
    public async Task<IActionResult> Crear(int? clienteId)
    {
        var vm = new PedidoFormVm();
        if (clienteId.HasValue)
        {
            var c = await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clienteId);
            if (c != null) { vm.ClienteId = c.Id; RellenarDesdeCliente(vm, c); }
        }
        await CargarListasAsync(vm);
        await CargarAlmacenAsync();
        return View("Formulario", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Crear(PedidoFormVm vm)
    {
        var (pedido, detalles, ok) = await ConstruirPedidoAsync(vm);
        if (!ok)
        {
            await CargarListasAsync(vm);
            await CargarAlmacenAsync();
            return View("Formulario", vm);
        }

        var creado = await _svc.CrearAsync(pedido!, detalles, UsuarioId);
        Exito($"Pedido {creado.Codigo} registrado.");
        if (creado.Ubicacion == null) Fallo("El pedido no tiene ubicación en el mapa: fije el punto antes de asignarlo.");
        return RedirectToAction(nameof(Detalle), new { id = creado.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var p = await _db.Pedidos.AsNoTracking().Include(x => x.Detalles).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (p.EstadoId is EstadoPedidoIds.EnRuta or EstadoPedidoIds.Entregado)
        {
            Fallo("No se puede editar un pedido en ruta o entregado.");
            return RedirectToAction(nameof(Detalle), new { id });
        }

        var vm = new PedidoFormVm
        {
            Id = p.Id,
            Codigo = p.Codigo,
            ClienteId = p.ClienteId,
            DireccionEntrega = p.DireccionEntrega,
            Referencia = p.Referencia,
            Lat = p.Ubicacion != null ? Entrada.Num(p.Ubicacion.Y) : null,
            Lng = p.Ubicacion != null ? Entrada.Num(p.Ubicacion.X) : null,
            VentanaInicio = p.VentanaInicio?.ToString("HH:mm"),
            VentanaFin = p.VentanaFin?.ToString("HH:mm"),
            Observaciones = p.Observaciones,
            Detalles = p.Detalles.Select(d => new DetalleVm { Producto = d.Producto, Cantidad = d.Cantidad, Observacion = d.Observacion }).ToList()
        };
        if (vm.Detalles.Count == 0) vm.Detalles.Add(new DetalleVm());
        await CargarListasAsync(vm);
        await CargarAlmacenAsync();
        return View("Formulario", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Editar(int id, PedidoFormVm vm)
    {
        vm.Id = id;
        vm.ModoCliente = "existente";
        var (pedido, detalles, ok) = await ConstruirPedidoAsync(vm);
        if (!ok)
        {
            await CargarListasAsync(vm);
            await CargarAlmacenAsync();
            return View("Formulario", vm);
        }

        var res = await _svc.ActualizarAsync(id, p =>
        {
            p.ClienteId = pedido!.ClienteId;
            p.DireccionEntrega = pedido.DireccionEntrega;
            p.Referencia = pedido.Referencia;
            p.Ubicacion = pedido.Ubicacion;
            p.VentanaInicio = pedido.VentanaInicio;
            p.VentanaFin = pedido.VentanaFin;
            p.Observaciones = pedido.Observaciones;
        }, detalles);

        if (!res.Ok)
        {
            Fallo(res.Error ?? "No se pudo actualizar el pedido.");
            return RedirectToAction(nameof(Detalle), new { id });
        }
        Exito("Pedido actualizado.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    // ============================================================ Detalle
    public async Task<IActionResult> Detalle(int id)
    {
        var p = await _db.Pedidos.AsNoTracking()
            .Include(x => x.Cliente).Include(x => x.Estado).Include(x => x.Zona)
            .Include(x => x.Detalles)
            .Include(x => x.Repartidor!).ThenInclude(r => r.Usuario)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();

        var enlace = p.EstadoId == EstadoPedidoIds.Entregado || p.EstadoId == EstadoPedidoIds.Fallido
            ? await _db.Enlaces.AsNoTracking().Where(t => t.PedidoId == id).OrderByDescending(t => t.Id).FirstOrDefaultAsync()
            : await _svc.AsegurarEnlaceAsync(id);

        string? url = enlace != null ? _notif.UrlSeguimiento(enlace.Token) : null;

        var vm = new PedidoDetalleVm
        {
            Pedido = p,
            Historial = await _db.Historial.AsNoTracking().Include(h => h.EstadoNuevo).Include(h => h.Usuario)
                .Where(h => h.PedidoId == id).OrderBy(h => h.Fecha).ThenBy(h => h.Id).ToListAsync(),
            Notificaciones = await _db.Notificaciones.AsNoTracking().Where(n => n.PedidoId == id).OrderByDescending(n => n.Id).ToListAsync(),
            Pruebas = await _db.Pruebas.AsNoTracking().Where(x => x.PedidoId == id).OrderBy(x => x.Id).ToListAsync(),
            Parada = await _db.Paradas.AsNoTracking().Where(x => x.PedidoId == id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(),
            UrlSeguimiento = url
        };

        // Envío manual por WhatsApp (wa.me) con el mensaje ya redactado.
        if (!string.IsNullOrWhiteSpace(p.Cliente.Telefono))
        {
            string evento = p.EstadoId switch
            {
                EstadoPedidoIds.EnRuta => EventoNotificacion.EnCamino,
                EstadoPedidoIds.Entregado => EventoNotificacion.Entregado,
                EstadoPedidoIds.Fallido => EventoNotificacion.Fallido,
                EstadoPedidoIds.EnPreparacion => EventoNotificacion.Asignado,
                _ => EventoNotificacion.Confirmado
            };
            var (_, texto) = Plantillas.Construir(evento, p, evento is EventoNotificacion.Entregado or EventoNotificacion.Fallido ? null : url,
                                                  vm.Parada?.Eta, _conf["App:NombreComercio"] ?? "Routify");
            vm.EnlaceWhatsApp = FormatoTelefono.EnlaceWaMe(p.Cliente.Telefono, texto, _conf["WhatsApp:CodigoPais"] ?? "51");
        }
        return View(vm);
    }

    // ============================================================ Asignación (RF-06, RF-07)
    [HttpGet]
    public async Task<IActionResult> Asignar(int id)
    {
        var p = await _db.Pedidos.AsNoTracking().Include(x => x.Cliente).Include(x => x.Estado).Include(x => x.Zona)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (p.EstadoId != EstadoPedidoIds.Pendiente)
        {
            Fallo("Solo se pueden asignar pedidos pendientes.");
            return RedirectToAction(nameof(Detalle), new { id });
        }
        if (p.Ubicacion == null)
        {
            Fallo("El pedido no tiene ubicación en el mapa. Edítelo y fije el punto de entrega.");
            return RedirectToAction(nameof(Detalle), new { id });
        }

        var prm = await _cfg.RuteoAsync();
        var reps = await _db.Repartidores.AsNoTracking().Include(r => r.Usuario).Include(r => r.Zona)
            .Where(r => r.Usuario.Estado == "Activo").ToListAsync();

        var candidatos = new List<CandidatoAsignacionVm>();
        foreach (var r in reps)
        {
            int carga = await _rutas.CargaActualAsync(r.Id);
            double? kmRecogida = r.UbicacionActual == null ? null
                : Math.Round(GeoUtils.HaversineKm(r.UbicacionActual.Y, r.UbicacionActual.X, prm.AlmacenLat, prm.AlmacenLng) * prm.FactorCalle, 1);
            double? kmEntrega = r.UbicacionActual == null ? null
                : Math.Round(GeoUtils.HaversineKm(r.UbicacionActual, p.Ubicacion) * prm.FactorCalle, 1);

            candidatos.Add(new CandidatoAsignacionVm
            {
                RepartidorId = r.Id,
                Nombre = r.Usuario.NombreCompleto,
                Zona = r.Zona?.Nombre,
                Vehiculo = r.Vehiculo,
                ParadasAsignadas = carga,
                Capacidad = r.Capacidad,
                DistanciaRecogidaKm = kmRecogida,
                DistanciaEntregaKm = kmEntrega,
                Disponible = r.Disponibilidad && carga < r.Capacidad,
                MismaZona = p.ZonaId.HasValue && r.ZonaId == p.ZonaId
            });
        }

        // Ordenados por proximidad al punto de recogida (los no disponibles al final)
        candidatos = candidatos.OrderByDescending(c => c.Disponible)
                               .ThenBy(c => c.DistanciaRecogidaKm ?? double.MaxValue).ToList();

        // Impacto estimado en la ruta (distancia total y ETA) para los 5 primeros disponibles
        foreach (var c in candidatos.Where(c => c.Disponible).Take(5))
        {
            var rep = reps.First(r => r.Id == c.RepartidorId);
            var sim = await _rutas.SimularAsync(p, rep);
            if (sim == null) continue;
            c.RutaKm = sim.DistanciaTotalKm;
            c.RutaMin = sim.TiempoTotalMin;
            c.EtaUtc = sim.EtaPedidoUtc;
        }

        return View(new AsignacionVm { Pedido = p, AlmacenNombre = prm.AlmacenNombre, Candidatos = candidatos });
    }

    [HttpPost]
    public async Task<IActionResult> Asignar(int id, int repartidorId)
    {
        var res = await _svc.AsignarAsync(id, repartidorId, UsuarioId);
        if (res.Ok) Exito("Pedido asignado y ruta optimizada."); else Fallo(res.Error ?? "No se pudo asignar.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> AutoAsignar(int id)
    {
        var (res, rep) = await _svc.AutoAsignarAsync(id, UsuarioId);
        if (res.Ok) Exito($"Pedido asignado automáticamente a {rep?.Usuario.NombreCompleto}.");
        else Fallo(res.Error ?? "No se pudo asignar.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> AutoDistribuir()
    {
        var r = await _svc.AutoDistribuirAsync(UsuarioId);
        if (r.Asignados == 0 && r.SinAsignar == 0) Fallo("No hay pedidos pendientes con ubicación para distribuir.");
        else
        {
            Exito($"Se asignaron {r.Asignados} pedido(s) entre {r.RepartidoresUsados} repartidor(es).");
            if (r.SinAsignar > 0) Fallo($"{r.SinAsignar} pedido(s) quedaron pendientes (sin ubicación o sin capacidad disponible).");
        }
        return RedirectToAction(nameof(Index), new { EstadoId = EstadoPedidoIds.EnPreparacion });
    }

    [HttpPost]
    public async Task<IActionResult> Desasignar(int id)
    {
        var res = await _svc.DesasignarAsync(id, UsuarioId);
        if (res.Ok) Exito("Repartidor desasignado."); else Fallo(res.Error ?? "No se pudo desasignar.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Reprogramar(int id)
    {
        var res = await _svc.ReprogramarAsync(id, UsuarioId);
        if (res.Ok) Exito("Pedido reprogramado: vuelve a estar pendiente de asignación."); else Fallo(res.Error ?? "No se pudo reprogramar.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ReenviarNotificacion(int id, int notificacionId)
    {
        var n = await _db.Notificaciones.FirstOrDefaultAsync(x => x.Id == notificacionId && x.PedidoId == id);
        if (n == null) return NotFound();
        n.Estado = EstadoNotificacion.Pendiente;
        n.Intentos = 0;
        n.Error = null;
        await _db.SaveChangesAsync();
        _cola.Encolar(n.Id);
        Exito("Notificación puesta en cola para reenvío.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    // ============================================================ Auxiliares
    private async Task CargarListasAsync(PedidoFormVm vm)
    {
        vm.Clientes = await _db.Clientes.AsNoTracking().OrderBy(c => c.Nombre)
            .Select(c => new SelectListItem(c.Nombre + (c.Telefono != null ? " · " + c.Telefono : ""), c.Id.ToString())).ToListAsync();
    }

    private async Task CargarAlmacenAsync()
    {
        var prm = await _cfg.RuteoAsync();
        ViewBag.AlmacenLat = Entrada.Num(prm.AlmacenLat);
        ViewBag.AlmacenLng = Entrada.Num(prm.AlmacenLng);
    }

    private static void RellenarDesdeCliente(PedidoFormVm vm, Cliente c)
    {
        vm.DireccionEntrega = c.Direccion;
        vm.Referencia = c.Referencia;
        vm.Lat = c.Ubicacion != null ? Entrada.Num(c.Ubicacion.Y) : null;
        vm.Lng = c.Ubicacion != null ? Entrada.Num(c.Ubicacion.X) : null;
    }

    /// <summary>Valida el formulario y construye el pedido, el cliente nuevo (si aplica) y el detalle.</summary>
    private async Task<(Pedido? Pedido, List<DetallePedido> Detalles, bool Ok)> ConstruirPedidoAsync(PedidoFormVm vm)
    {
        var detalles = (vm.Detalles ?? new()).Where(d => !string.IsNullOrWhiteSpace(d.Producto))
            .Select(d => new DetallePedido { Producto = d.Producto!.Trim(), Cantidad = Math.Max(1, d.Cantidad), Observacion = Entrada.Limpiar(d.Observacion) })
            .ToList();
        if (detalles.Count == 0) ModelState.AddModelError("", "Agregue al menos un producto al detalle del pedido.");

        if (!Entrada.TryPunto(vm.Lat, vm.Lng, out var punto, out var errorPunto))
            ModelState.AddModelError("", errorPunto!);

        var vi = Entrada.Hora(vm.VentanaInicio);
        var vf = Entrada.Hora(vm.VentanaFin);
        if (!string.IsNullOrWhiteSpace(vm.VentanaInicio) && vi == null) ModelState.AddModelError("", "La hora inicial de la franja no es válida.");
        if (!string.IsNullOrWhiteSpace(vm.VentanaFin) && vf == null) ModelState.AddModelError("", "La hora final de la franja no es válida.");
        if (vi.HasValue && vf.HasValue && vf <= vi) ModelState.AddModelError("", "La hora final de la franja debe ser posterior a la inicial.");

        Cliente? cliente = null;
        if (vm.ModoCliente == "nuevo")
        {
            if (string.IsNullOrWhiteSpace(vm.NuevoNombre)) ModelState.AddModelError(nameof(vm.NuevoNombre), "Ingrese el nombre del cliente.");
            if (vm.NuevoNotificarWhatsapp && string.IsNullOrWhiteSpace(vm.NuevoTelefono))
                ModelState.AddModelError(nameof(vm.NuevoTelefono), "Ingrese el teléfono para notificar por WhatsApp.");
            if (vm.NuevoNotificarCorreo && string.IsNullOrWhiteSpace(vm.NuevoCorreo))
                ModelState.AddModelError(nameof(vm.NuevoCorreo), "Ingrese el correo para notificar por correo.");
        }
        else
        {
            cliente = vm.ClienteId.HasValue ? await _db.Clientes.FirstOrDefaultAsync(c => c.Id == vm.ClienteId) : null;
            if (cliente == null) ModelState.AddModelError(nameof(vm.ClienteId), "Seleccione un cliente.");
        }

        if (!ModelState.IsValid) return (null, detalles, false);

        if (vm.ModoCliente == "nuevo")
        {
            cliente = new Cliente
            {
                Nombre = vm.NuevoNombre!.Trim(),
                Telefono = Entrada.Limpiar(vm.NuevoTelefono),
                Correo = Entrada.Limpiar(vm.NuevoCorreo)?.ToLowerInvariant(),
                Direccion = vm.DireccionEntrega.Trim(),
                Referencia = Entrada.Limpiar(vm.Referencia),
                Ubicacion = punto,
                NotificarCorreo = vm.NuevoNotificarCorreo,
                NotificarWhatsapp = vm.NuevoNotificarWhatsapp,
                ConsentimientoDatos = vm.NuevoConsentimiento,
                FechaConsentimiento = vm.NuevoConsentimiento ? DateTime.UtcNow : null
            };
            _db.Clientes.Add(cliente);
        }

        var pedido = new Pedido
        {
            Cliente = cliente!,
            ClienteId = cliente!.Id,
            DireccionEntrega = vm.DireccionEntrega.Trim(),
            Referencia = Entrada.Limpiar(vm.Referencia),
            Ubicacion = punto,
            VentanaInicio = vi,
            VentanaFin = vf,
            Observaciones = Entrada.Limpiar(vm.Observaciones)
        };
        return (pedido, detalles, true);
    }
}
