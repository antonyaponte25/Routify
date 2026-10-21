using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Routify.Web.Helpers;
using Routify.Web.Services.Notificaciones;
using Routify.Web.Services.Seguridad;
using Routify.Web.ViewModels;

namespace Routify.Web.Controllers;

[Authorize]
public class AccountController : BaseController
{
    private readonly AuthService _auth;
    private readonly ICorreoSender _correo;
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;

    public AccountController(AuthService auth, ICorreoSender correo, IConfiguration cfg, IWebHostEnvironment env)
    {
        _auth = auth;
        _correo = correo;
        _cfg = cfg;
        _env = env;
    }

    // ------------------------------------------------------------ Inicio de sesión
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirigirSegunRol();
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var r = await _auth.ValidarAsync(vm.Correo, vm.Clave);
        switch (r.Resultado)
        {
            case ResultadoLogin.Ok:
                await _auth.IniciarSesionAsync(HttpContext, r.Usuario!);
                if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return LocalRedirect(vm.ReturnUrl);
                return r.Usuario!.Rol.Nombre == Roles.Repartidor
                    ? RedirectToAction("Index", "Repartidor")
                    : RedirectToAction("Index", "Home");

            case ResultadoLogin.Bloqueado:
                ModelState.AddModelError("", $"La cuenta está bloqueada por intentos fallidos. Intente de nuevo en {r.MinutosRestantes} minuto(s).");
                break;
            case ResultadoLogin.Inactivo:
                ModelState.AddModelError("", "La cuenta está inactiva. Contacte al administrador.");
                break;
            default:
                ModelState.AddModelError("", "Correo o contraseña incorrectos.");
                break;
        }
        vm.Clave = "";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccesoDenegado() => View();

    private IActionResult RedirigirSegunRol() =>
        User.IsInRole(Roles.Repartidor) ? RedirectToAction("Index", "Repartidor") : RedirectToAction("Index", "Home");

    // ------------------------------------------------------------ Recuperación (RF-01)
    [AllowAnonymous, HttpGet]
    public IActionResult Recuperar() => View(new RecuperarVm());

    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Recuperar(RecuperarVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var (token, usuario) = await _auth.GenerarTokenRecuperacionAsync(vm.Correo);
        if (token != null && usuario != null)
        {
            string baseUrl = (_cfg["App:UrlBase"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
            string enlace = $"{baseUrl}/Account/Restablecer?correo={Uri.EscapeDataString(usuario.Correo)}&token={Uri.EscapeDataString(token)}";
            int minutos = _cfg.GetValue("Seguridad:RecuperacionMinutos", 30);
            string cuerpo = $"Hola {usuario.Nombre},\n\nRecibimos una solicitud para restablecer tu contraseña de Routify.\n" +
                            $"Usa este enlace (vale {minutos} minutos):\n{enlace}\n\nSi no fuiste tú, ignora este mensaje.";
            await _correo.EnviarAsync(usuario.Correo, "Routify: restablecer contraseña", cuerpo);

            // En desarrollo (correo simulado) se muestra el enlace para poder probar el flujo.
            if (_env.IsDevelopment()) ViewBag.EnlaceDesarrollo = enlace;
        }

        // Respuesta idéntica exista o no el correo (no revela cuentas registradas).
        ViewBag.Enviado = true;
        return View(vm);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Restablecer(string? correo, string? token) =>
        View(new RestablecerVm { Correo = correo ?? "", Token = token ?? "" });

    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Restablecer(RestablecerVm vm)
    {
        var politica = PasswordService.Validar(vm.NuevaClave);
        if (politica != null) ModelState.AddModelError(nameof(vm.NuevaClave), politica);
        if (!ModelState.IsValid) return View(vm);

        if (!await _auth.RestablecerAsync(vm.Correo, vm.Token, vm.NuevaClave))
        {
            ModelState.AddModelError("", "El enlace no es válido o ya venció. Solicite uno nuevo.");
            return View(vm);
        }
        Exito("Contraseña actualizada. Ya puede iniciar sesión.");
        return RedirectToAction(nameof(Login));
    }

    // ------------------------------------------------------------ Cambiar contraseña
    [HttpGet]
    public IActionResult CambiarClave() => View(new CambiarClaveVm());

    [HttpPost]
    public async Task<IActionResult> CambiarClave(CambiarClaveVm vm)
    {
        var politica = PasswordService.Validar(vm.NuevaClave);
        if (politica != null) ModelState.AddModelError(nameof(vm.NuevaClave), politica);
        if (!ModelState.IsValid) return View(vm);

        if (!await _auth.CambiarClaveAsync(UsuarioId, vm.ClaveActual, vm.NuevaClave))
        {
            ModelState.AddModelError(nameof(vm.ClaveActual), "La contraseña actual no es correcta.");
            return View(vm);
        }
        Exito("Contraseña actualizada correctamente.");
        return RedirectToAction("Index", User.IsInRole(Roles.Repartidor) ? "Repartidor" : "Home");
    }
}
