using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;
using Routify.Web.Services.Parametros;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

[Authorize(Roles = Roles.AdminOperador)]
public class ClientesController : BaseController
{
    private readonly RoutifyDbContext _db;
    private readonly IConfiguracionService _cfg;

    public ClientesController(RoutifyDbContext db, IConfiguracionService cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    public async Task<IActionResult> Index(string? texto)
    {
        var q = _db.Clientes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            q = q.Where(c => c.Nombre.Contains(t) || (c.Telefono != null && c.Telefono.Contains(t)) || c.Direccion.Contains(t));
        }
        ViewBag.Texto = texto;
        var lista = await q.OrderBy(c => c.Nombre).Take(200).ToListAsync();
        return View(lista);
    }

    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        await CargarAlmacenAsync();
        return View("Formulario", new ClienteFormVm());
    }

    [HttpPost]
    public async Task<IActionResult> Crear(ClienteFormVm vm)
    {
        if (!Entrada.TryPunto(vm.Lat, vm.Lng, out var punto, out var err)) ModelState.AddModelError("", err!);
        if (!ModelState.IsValid) { await CargarAlmacenAsync(); return View("Formulario", vm); }

        var c = new Cliente();
        Aplicar(c, vm, punto);
        _db.Clientes.Add(c);
        await _db.SaveChangesAsync();
        Exito("Cliente registrado.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var c = await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        await CargarAlmacenAsync();
        return View("Formulario", new ClienteFormVm
        {
            Id = c.Id, Nombre = c.Nombre, Telefono = c.Telefono, Correo = c.Correo, Direccion = c.Direccion,
            Referencia = c.Referencia,
            Lat = c.Ubicacion != null ? Entrada.Num(c.Ubicacion.Y) : null,
            Lng = c.Ubicacion != null ? Entrada.Num(c.Ubicacion.X) : null,
            NotificarCorreo = c.NotificarCorreo, NotificarWhatsapp = c.NotificarWhatsapp, Consentimiento = c.ConsentimientoDatos
        });
    }

    [HttpPost]
    public async Task<IActionResult> Editar(int id, ClienteFormVm vm)
    {
        if (!Entrada.TryPunto(vm.Lat, vm.Lng, out var punto, out var err)) ModelState.AddModelError("", err!);
        if (!ModelState.IsValid) { vm.Id = id; await CargarAlmacenAsync(); return View("Formulario", vm); }

        var c = await _db.Clientes.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        Aplicar(c, vm, punto);
        await _db.SaveChangesAsync();
        Exito("Cliente actualizado.");
        return RedirectToAction(nameof(Index));
    }

    private static void Aplicar(Cliente c, ClienteFormVm vm, NetTopologySuite.Geometries.Point? punto)
    {
        c.Nombre = vm.Nombre.Trim();
        c.Telefono = Entrada.Limpiar(vm.Telefono);
        c.Correo = Entrada.Limpiar(vm.Correo)?.ToLowerInvariant();
        c.Direccion = vm.Direccion.Trim();
        c.Referencia = Entrada.Limpiar(vm.Referencia);
        c.Ubicacion = punto;
        c.NotificarCorreo = vm.NotificarCorreo;
        c.NotificarWhatsapp = vm.NotificarWhatsapp;
        if (vm.Consentimiento && !c.ConsentimientoDatos) c.FechaConsentimiento = DateTime.UtcNow;
        if (!vm.Consentimiento) c.FechaConsentimiento = null;
        c.ConsentimientoDatos = vm.Consentimiento;
    }

    private async Task CargarAlmacenAsync()
    {
        var prm = await _cfg.RuteoAsync();
        ViewBag.AlmacenLat = Entrada.Num(prm.AlmacenLat);
        ViewBag.AlmacenLng = Entrada.Num(prm.AlmacenLng);
    }
}
