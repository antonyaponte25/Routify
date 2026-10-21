using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;
using Routify.Web.Services.Parametros;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

/// <summary>Zonas de reparto como polígonos sobre el mapa (RF-04). Edición solo para el administrador.</summary>
[Authorize(Roles = Roles.AdminOperador)]
public class ZonasController : BaseController
{
    private readonly RoutifyDbContext _db;
    private readonly IConfiguracionService _cfg;

    public ZonasController(RoutifyDbContext db, IConfiguracionService cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    public async Task<IActionResult> Index()
    {
        var zonas = await _db.Zonas.AsNoTracking().OrderBy(z => z.Nombre).ToListAsync();
        var conteos = await _db.Pedidos.AsNoTracking().Where(p => p.ZonaId != null)
            .GroupBy(p => p.ZonaId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        ViewBag.Conteos = conteos;
        return View(zonas);
    }

    [HttpGet, Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Crear()
    {
        await CargarAlmacenAsync();
        return View("Formulario", new ZonaFormVm());
    }

    [HttpPost, Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Crear(ZonaFormVm vm)
    {
        var poligono = LeerPoligono(vm);
        if (!ModelState.IsValid || poligono == null) { await CargarAlmacenAsync(); return View("Formulario", vm); }

        if (await _db.Zonas.AnyAsync(z => z.Nombre == vm.Nombre.Trim()))
        {
            ModelState.AddModelError(nameof(vm.Nombre), "Ya existe una zona con ese nombre.");
            await CargarAlmacenAsync();
            return View("Formulario", vm);
        }

        _db.Zonas.Add(new ZonaReparto
        {
            Nombre = vm.Nombre.Trim(),
            Descripcion = Entrada.Limpiar(vm.Descripcion),
            Color = vm.Color.ToUpperInvariant(),
            Poligono = poligono,
            Activa = vm.Activa
        });
        await _db.SaveChangesAsync();
        Exito("Zona de reparto creada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Editar(int id)
    {
        var z = await _db.Zonas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (z == null) return NotFound();
        await CargarAlmacenAsync();
        return View("Formulario", new ZonaFormVm
        {
            Id = z.Id, Nombre = z.Nombre, Descripcion = z.Descripcion, Color = z.Color, Activa = z.Activa,
            Vertices = JsonSerializer.Serialize(GeoUtils.Vertices(z.Poligono))
        });
    }

    [HttpPost, Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Editar(int id, ZonaFormVm vm)
    {
        vm.Id = id;
        var poligono = LeerPoligono(vm);
        if (!ModelState.IsValid || poligono == null) { await CargarAlmacenAsync(); return View("Formulario", vm); }

        var z = await _db.Zonas.FirstOrDefaultAsync(x => x.Id == id);
        if (z == null) return NotFound();

        if (await _db.Zonas.AnyAsync(x => x.Nombre == vm.Nombre.Trim() && x.Id != id))
        {
            ModelState.AddModelError(nameof(vm.Nombre), "Ya existe una zona con ese nombre.");
            await CargarAlmacenAsync();
            return View("Formulario", vm);
        }

        z.Nombre = vm.Nombre.Trim();
        z.Descripcion = Entrada.Limpiar(vm.Descripcion);
        z.Color = vm.Color.ToUpperInvariant();
        z.Poligono = poligono;
        z.Activa = vm.Activa;
        await _db.SaveChangesAsync();
        Exito("Zona actualizada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> CambiarEstado(int id)
    {
        var z = await _db.Zonas.FirstOrDefaultAsync(x => x.Id == id);
        if (z == null) return NotFound();
        z.Activa = !z.Activa;
        await _db.SaveChangesAsync();
        Exito(z.Activa ? "Zona activada." : "Zona desactivada.");
        return RedirectToAction(nameof(Index));
    }

    private NetTopologySuite.Geometries.Polygon? LeerPoligono(ZonaFormVm vm)
    {
        try
        {
            var pares = JsonSerializer.Deserialize<List<double[]>>(vm.Vertices ?? "[]") ?? new();
            var vertices = pares.Where(p => p.Length >= 2 && GeoUtils.CoordenadasValidas(p[0], p[1]))
                                .Select(p => (Lat: p[0], Lng: p[1])).ToList();
            var poligono = GeoUtils.CrearPoligono(vertices);
            if (poligono == null) ModelState.AddModelError(nameof(vm.Vertices), "El polígono debe tener al menos 3 vértices válidos y no cruzarse.");
            return poligono;
        }
        catch (JsonException)
        {
            ModelState.AddModelError(nameof(vm.Vertices), "El polígono no es válido.");
            return null;
        }
    }

    private async Task CargarAlmacenAsync()
    {
        var prm = await _cfg.RuteoAsync();
        ViewBag.AlmacenLat = Entrada.Num(prm.AlmacenLat);
        ViewBag.AlmacenLng = Entrada.Num(prm.AlmacenLng);
    }
}
