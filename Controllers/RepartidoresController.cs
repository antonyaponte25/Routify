using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Ruteo;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

[Authorize(Roles = Roles.AdminOperador)]
public class RepartidoresController : BaseController
{
    private readonly RoutifyDbContext _db;
    private readonly RutaService _rutas;

    public RepartidoresController(RoutifyDbContext db, RutaService rutas)
    {
        _db = db;
        _rutas = rutas;
    }

    public async Task<IActionResult> Index()
    {
        var reps = await _db.Repartidores.AsNoTracking().Include(r => r.Usuario).Include(r => r.Zona)
            .OrderBy(r => r.Usuario.Nombre).ToListAsync();

        var filas = new List<RepartidorFilaVm>();
        foreach (var r in reps)
        {
            filas.Add(new RepartidorFilaVm
            {
                Id = r.Id,
                Nombre = r.Usuario.NombreCompleto,
                Zona = r.Zona?.Nombre,
                Vehiculo = r.Vehiculo,
                Placa = r.Placa,
                Disponible = r.Disponibilidad,
                Activo = r.Usuario.EstaActivo,
                Capacidad = r.Capacidad,
                Carga = await _rutas.CargaActualAsync(r.Id),
                UltimaActualizacion = r.UltimaActualizacion,
                Telefono = r.Usuario.Telefono
            });
        }
        return View(filas);
    }

    [HttpPost]
    public async Task<IActionResult> CambiarDisponibilidad(int id)
    {
        var r = await _db.Repartidores.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        r.Disponibilidad = !r.Disponibilidad;
        await _db.SaveChangesAsync();
        Exito(r.Disponibilidad ? "Repartidor marcado como disponible." : "Repartidor marcado como no disponible.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Editar(int id)
    {
        var r = await _db.Repartidores.AsNoTracking().Include(x => x.Usuario).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        var vm = new RepartidorFormVm
        {
            Id = r.Id, Nombre = r.Usuario.NombreCompleto, ZonaId = r.ZonaId, Capacidad = r.Capacidad,
            Vehiculo = r.Vehiculo, Placa = r.Placa, Disponibilidad = r.Disponibilidad
        };
        await CargarZonasAsync(vm);
        return View(vm);
    }

    [HttpPost, Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Editar(int id, RepartidorFormVm vm)
    {
        var r = await _db.Repartidores.Include(x => x.Usuario).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        vm.Id = id;
        vm.Nombre = r.Usuario.NombreCompleto;
        if (!ModelState.IsValid) { await CargarZonasAsync(vm); return View(vm); }

        r.ZonaId = vm.ZonaId;
        r.Capacidad = vm.Capacidad;
        r.Vehiculo = Entrada.Limpiar(vm.Vehiculo);
        r.Placa = Entrada.Limpiar(vm.Placa)?.ToUpperInvariant();
        r.Disponibilidad = vm.Disponibilidad;
        await _db.SaveChangesAsync();
        Exito("Repartidor actualizado.");
        return RedirectToAction(nameof(Index));
    }

    private async Task CargarZonasAsync(RepartidorFormVm vm) =>
        vm.Zonas = await _db.Zonas.AsNoTracking().Where(z => z.Activa).OrderBy(z => z.Nombre)
            .Select(z => new SelectListItem(z.Nombre, z.Id.ToString())).ToListAsync();
}
