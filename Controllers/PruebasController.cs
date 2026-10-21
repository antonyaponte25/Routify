using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Archivos;

namespace Routify.Web.Controllers;

/// <summary>Sirve las evidencias de entrega guardadas fuera de wwwroot; solo con sesión de Administrador u Operador.</summary>
[Authorize(Roles = Roles.AdminOperador)]
public class PruebasController : Controller
{
    private readonly RoutifyDbContext _db;
    private readonly IArchivoService _archivos;

    public PruebasController(RoutifyDbContext db, IArchivoService archivos)
    {
        _db = db;
        _archivos = archivos;
    }

    [HttpGet("/pruebas/{id:int}")]
    public async Task<IActionResult> Ver(int id)
    {
        var prueba = await _db.Pruebas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (prueba == null) return NotFound();

        var ruta = _archivos.RutaFisica(prueba.Archivo);
        if (ruta == null) return NotFound();

        string tipo = Path.GetExtension(ruta).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        return PhysicalFile(ruta, tipo);
    }
}
