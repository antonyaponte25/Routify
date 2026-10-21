using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;
using Routify.Web.Services.Seguridad;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

/// <summary>Administración de usuarios y roles (RF-03). Solo Administrador.</summary>
[Authorize(Roles = Roles.Administrador)]
public class UsuariosController : BaseController
{
    private readonly RoutifyDbContext _db;
    private readonly IPasswordService _pw;

    public UsuariosController(RoutifyDbContext db, IPasswordService pw)
    {
        _db = db;
        _pw = pw;
    }

    public async Task<IActionResult> Index(string? texto, int? rolId)
    {
        var baseQ = _db.Usuarios.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            baseQ = baseQ.Where(u => u.Nombre.Contains(t) || u.Apellido.Contains(t) || u.Correo.Contains(t));
        }
        var conteo = await baseQ.GroupBy(u => u.RolId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

        var q = baseQ.Include(u => u.Rol).Include(u => u.Repartidor).AsQueryable();
        if (rolId.HasValue) q = q.Where(u => u.RolId == rolId);

        return View(new UsuarioListaVm
        {
            Texto = texto,
            RolId = rolId,
            ConteoPorRol = conteo,
            Total = conteo.Values.Sum(),
            Usuarios = await q.OrderBy(u => u.RolId).ThenBy(u => u.Nombre).ToListAsync()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        var vm = new UsuarioFormVm { RolId = Roles.IdOperador };
        await CargarListasAsync(vm);
        return View("Formulario", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioFormVm vm)
    {
        var politica = PasswordService.Validar(vm.Clave);
        if (politica != null) ModelState.AddModelError(nameof(vm.Clave), politica);
        string correo = vm.Correo.Trim().ToLowerInvariant();
        if (await _db.Usuarios.AnyAsync(u => u.Correo == correo))
            ModelState.AddModelError(nameof(vm.Correo), "Ya existe un usuario con ese correo.");
        if (!await _db.Roles.AnyAsync(r => r.Id == vm.RolId)) ModelState.AddModelError(nameof(vm.RolId), "Rol no válido.");

        if (!ModelState.IsValid) { await CargarListasAsync(vm); return View("Formulario", vm); }

        var u = new Usuario
        {
            RolId = vm.RolId,
            Nombre = vm.Nombre.Trim(),
            Apellido = vm.Apellido.Trim(),
            Correo = correo,
            Telefono = Entrada.Limpiar(vm.Telefono),
            PasswordHash = _pw.Hash(vm.Clave!),
            Estado = "Activo"
        };
        if (vm.RolId == Roles.IdRepartidor)
        {
            u.Repartidor = new Repartidor
            {
                ZonaId = vm.ZonaId,
                Capacidad = vm.Capacidad,
                Vehiculo = Entrada.Limpiar(vm.Vehiculo),
                Placa = Entrada.Limpiar(vm.Placa)?.ToUpperInvariant(),
                Disponibilidad = true
            };
        }
        _db.Usuarios.Add(u);
        await _db.SaveChangesAsync();
        Exito("Usuario creado.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var u = await _db.Usuarios.AsNoTracking().Include(x => x.Repartidor).FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();
        var vm = new UsuarioFormVm
        {
            Id = u.Id, Nombre = u.Nombre, Apellido = u.Apellido, Correo = u.Correo, Telefono = u.Telefono, RolId = u.RolId,
            ZonaId = u.Repartidor?.ZonaId, Capacidad = u.Repartidor?.Capacidad ?? 15,
            Vehiculo = u.Repartidor?.Vehiculo, Placa = u.Repartidor?.Placa
        };
        await CargarListasAsync(vm);
        return View("Formulario", vm);
    }

    [HttpPost]
    public async Task<IActionResult> Editar(int id, UsuarioFormVm vm)
    {
        vm.Id = id;
        string correo = vm.Correo.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(vm.Clave))
        {
            var politica = PasswordService.Validar(vm.Clave);
            if (politica != null) ModelState.AddModelError(nameof(vm.Clave), politica);
        }
        if (await _db.Usuarios.AnyAsync(u => u.Correo == correo && u.Id != id))
            ModelState.AddModelError(nameof(vm.Correo), "Ya existe un usuario con ese correo.");
        if (!await _db.Roles.AnyAsync(r => r.Id == vm.RolId)) ModelState.AddModelError(nameof(vm.RolId), "Rol no válido.");

        var u = await _db.Usuarios.Include(x => x.Repartidor).FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();

        // Un administrador no puede quitarse a sí mismo el rol de administrador.
        if (u.Id == UsuarioId && vm.RolId != Roles.IdAdministrador)
            ModelState.AddModelError(nameof(vm.RolId), "No puede cambiar su propio rol de administrador.");

        if (!ModelState.IsValid) { await CargarListasAsync(vm); return View("Formulario", vm); }

        u.Nombre = vm.Nombre.Trim();
        u.Apellido = vm.Apellido.Trim();
        u.Correo = correo;
        u.Telefono = Entrada.Limpiar(vm.Telefono);
        u.RolId = vm.RolId;
        if (!string.IsNullOrWhiteSpace(vm.Clave))
        {
            u.PasswordHash = _pw.Hash(vm.Clave);
            u.IntentosFallidos = 0;
            u.BloqueadoHasta = null;
        }

        if (vm.RolId == Roles.IdRepartidor)
        {
            u.Repartidor ??= new Repartidor { Disponibilidad = true };
            u.Repartidor.ZonaId = vm.ZonaId;
            u.Repartidor.Capacidad = vm.Capacidad;
            u.Repartidor.Vehiculo = Entrada.Limpiar(vm.Vehiculo);
            u.Repartidor.Placa = Entrada.Limpiar(vm.Placa)?.ToUpperInvariant();
        }
        else if (u.Repartidor != null)
        {
            u.Repartidor.Disponibilidad = false; // se conserva el historial de rutas
        }

        await _db.SaveChangesAsync();
        Exito("Usuario actualizado.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> CambiarEstado(int id)
    {
        if (id == UsuarioId)
        {
            Fallo("No puede desactivar su propia cuenta.");
            return RedirectToAction(nameof(Index));
        }
        var u = await _db.Usuarios.Include(x => x.Repartidor).FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();

        u.Estado = u.EstaActivo ? "Inactivo" : "Activo";
        if (!u.EstaActivo && u.Repartidor != null) u.Repartidor.Disponibilidad = false;
        await _db.SaveChangesAsync();
        Exito(u.EstaActivo ? "Usuario activado." : "Usuario desactivado.");
        return RedirectToAction(nameof(Index));
    }

    private async Task CargarListasAsync(UsuarioFormVm vm)
    {
        vm.Roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Id).Select(r => new SelectListItem(r.Nombre, r.Id.ToString())).ToListAsync();
        vm.Zonas = await _db.Zonas.AsNoTracking().Where(z => z.Activa).OrderBy(z => z.Nombre).Select(z => new SelectListItem(z.Nombre, z.Id.ToString())).ToListAsync();
    }
}
