using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Models;

namespace Routify.Web.Services.Seguridad;

public enum ResultadoLogin { Ok, Invalido, Bloqueado, Inactivo }

public record RespuestaLogin(ResultadoLogin Resultado, Usuario? Usuario, int MinutosRestantes = 0);

/// <summary>Autenticación propia: tabla usuarios + hash PBKDF2 + cookie de sesión + bloqueo por intentos fallidos.</summary>
public class AuthService
{
    private readonly RoutifyDbContext _db;
    private readonly IPasswordService _pw;
    private readonly IConfiguration _cfg;

    public AuthService(RoutifyDbContext db, IPasswordService pw, IConfiguration cfg)
    {
        _db = db;
        _pw = pw;
        _cfg = cfg;
    }

    private int MaximoIntentos => _cfg.GetValue("Seguridad:MaximoIntentosLogin", 5);
    private int BloqueoMinutos => _cfg.GetValue("Seguridad:BloqueoMinutos", 15);
    private int RecuperacionMinutos => _cfg.GetValue("Seguridad:RecuperacionMinutos", 30);

    public async Task<RespuestaLogin> ValidarAsync(string correo, string clave)
    {
        correo = (correo ?? "").Trim().ToLowerInvariant();
        var u = await _db.Usuarios.Include(x => x.Rol).Include(x => x.Repartidor)
                         .FirstOrDefaultAsync(x => x.Correo == correo);

        if (u == null)
        {
            // Hash ficticio para igualar tiempos de respuesta y no revelar si el correo existe.
            _pw.Verificar(clave ?? "", "v1.100000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
            return new RespuestaLogin(ResultadoLogin.Invalido, null);
        }

        if (u.BloqueadoHasta.HasValue && u.BloqueadoHasta > DateTime.UtcNow)
        {
            int min = (int)Math.Ceiling((u.BloqueadoHasta.Value - DateTime.UtcNow).TotalMinutes);
            return new RespuestaLogin(ResultadoLogin.Bloqueado, u, Math.Max(1, min));
        }

        if (!_pw.Verificar(clave ?? "", u.PasswordHash))
        {
            u.IntentosFallidos++;
            if (u.IntentosFallidos >= MaximoIntentos)
            {
                u.BloqueadoHasta = DateTime.UtcNow.AddMinutes(BloqueoMinutos);
                u.IntentosFallidos = 0;
                await _db.SaveChangesAsync();
                return new RespuestaLogin(ResultadoLogin.Bloqueado, u, BloqueoMinutos);
            }
            await _db.SaveChangesAsync();
            return new RespuestaLogin(ResultadoLogin.Invalido, null);
        }

        if (!u.EstaActivo) return new RespuestaLogin(ResultadoLogin.Inactivo, u);

        u.IntentosFallidos = 0;
        u.BloqueadoHasta = null;
        u.UltimoAcceso = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new RespuestaLogin(ResultadoLogin.Ok, u);
    }

    public async Task IniciarSesionAsync(HttpContext ctx, Usuario u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.NombreCompleto),
            new(ClaimTypes.Email, u.Correo),
            new(ClaimTypes.Role, u.Rol.Nombre)
        };
        if (u.Repartidor != null) claims.Add(new Claim("repartidor_id", u.Repartidor.Id.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        // RF-02: cierre por inactividad configurable. Los repartidores tienen una ventana mayor
        // para poder trabajar sin conexión durante la jornada y sincronizar al volver la señal.
        var ventana = u.Rol.Nombre == Roles.Repartidor
            ? TimeSpan.FromHours(_cfg.GetValue("Seguridad:InactividadRepartidorHoras", 12))
            : TimeSpan.FromMinutes(_cfg.GetValue("Seguridad:InactividadMinutos", 20));

        var propiedades = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(ventana)
        };
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, propiedades);
    }

    /// <summary>Genera un token de recuperación (válido unos minutos). Devuelve el token en claro o null si el correo no existe.</summary>
    public async Task<(string? Token, Usuario? Usuario)> GenerarTokenRecuperacionAsync(string correo)
    {
        correo = (correo ?? "").Trim().ToLowerInvariant();
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.Correo == correo && x.Estado == "Activo");
        if (u == null) return (null, null);

        var token = Tokens.Nuevo();
        u.TokenRecuperacionHash = Tokens.Hash(token);
        u.TokenRecuperacionExpira = DateTime.UtcNow.AddMinutes(RecuperacionMinutos);
        await _db.SaveChangesAsync();
        return (token, u);
    }

    public async Task<bool> RestablecerAsync(string correo, string token, string nuevaClave)
    {
        correo = (correo ?? "").Trim().ToLowerInvariant();
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.Correo == correo);
        if (u == null || u.TokenRecuperacionHash == null || u.TokenRecuperacionExpira == null) return false;
        if (u.TokenRecuperacionExpira < DateTime.UtcNow) return false;
        if (u.TokenRecuperacionHash != Tokens.Hash(token ?? "")) return false;

        u.PasswordHash = _pw.Hash(nuevaClave);
        u.TokenRecuperacionHash = null;
        u.TokenRecuperacionExpira = null;
        u.IntentosFallidos = 0;
        u.BloqueadoHasta = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarClaveAsync(int usuarioId, string claveActual, string nuevaClave)
    {
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId);
        if (u == null || !_pw.Verificar(claveActual ?? "", u.PasswordHash)) return false;
        u.PasswordHash = _pw.Hash(nuevaClave);
        await _db.SaveChangesAsync();
        return true;
    }
}
