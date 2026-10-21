using Microsoft.AspNetCore.Mvc;
using Routify.Web.Helpers;

namespace Routify.Web.Controllers;

public abstract class BaseController : Controller
{
    protected int UsuarioId => User.UsuarioId();

    protected void Exito(string mensaje) => TempData["ok"] = mensaje;

    protected void Fallo(string mensaje) => TempData["error"] = mensaje;
}
