using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Routify.Web.Helpers;

namespace Routify.Web.Controllers;

/// <summary>
/// Web App (PWA) del repartidor. Devuelve una única página "shell" que se guarda en caché mediante el
/// Service Worker; los datos se cargan por API y se guardan en IndexedDB para operar sin conexión.
/// </summary>
[Authorize(Roles = Roles.Repartidor)]
public class RepartidorController : Controller
{
    public IActionResult Index()
    {
        ViewBag.RepartidorId = User.RepartidorId();
        ViewBag.Nombre = User.Identity?.Name;
        return View();
    }
}
