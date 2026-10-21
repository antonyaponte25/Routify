using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Routify.Web.Controllers;

/// <summary>Página pública de seguimiento para el cliente final (RF-20). Sin registro ni inicio de sesión.</summary>
[AllowAnonymous]
public class SeguimientoController : Controller
{
    [HttpGet("/seguimiento/{token}")]
    [EnableRateLimiting("seguimiento")]
    public IActionResult Index(string token)
    {
        // El token solo se incrusta como atributo de datos; la validación ocurre en la API.
        ViewBag.Token = token.Length > 64 ? "" : token;
        return View();
    }
}
