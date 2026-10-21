using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Telemetria;

namespace Routify.Web.Hubs;

public static class Grupos
{
    public const string Despacho = "despacho";
    public static string Seguimiento(string token) => "seg-" + token;
}

/// <summary>
/// Hub de tiempo real (SignalR).
///  - Administrador / Operador: se unen automáticamente al grupo "despacho" (mapa en vivo).
///  - Repartidor: envía su posición con EnviarPosicion (cada 5 s).
///  - Cliente final (anónimo): se une al grupo de su enlace con UnirseSeguimiento(token).
/// Eventos que recibe el navegador: PosicionRepartidor, EtaActualizado, EtaPedido, EstadoPedido.
/// </summary>
public class TrackingHub : Hub
{
    private readonly RoutifyDbContext _db;
    private readonly TelemetriaService _telemetria;

    public TrackingHub(RoutifyDbContext db, TelemetriaService telemetria)
    {
        _db = db;
        _telemetria = telemetria;
    }

    public override async Task OnConnectedAsync()
    {
        var u = Context.User;
        if (u?.Identity?.IsAuthenticated == true && (u.IsInRole(Roles.Administrador) || u.IsInRole(Roles.Operador)))
            await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Despacho);

        await base.OnConnectedAsync();
    }

    /// <summary>El cliente final se suscribe a las actualizaciones de su pedido con el token de su enlace.</summary>
    public async Task<bool> UnirseSeguimiento(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return false;

        var ahora = DateTime.UtcNow;
        bool valido = await _db.Enlaces.AsNoTracking()
            .AnyAsync(t => t.Token == token && t.Activo && t.FechaExpiracion > ahora);
        if (!valido) return false;

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Seguimiento(token));
        return true;
    }

    [Authorize(Roles = Roles.Repartidor)]
    public async Task<bool> EnviarPosicion(PosicionDto posicion)
    {
        var claim = Context.User?.FindFirst("repartidor_id")?.Value;
        if (!int.TryParse(claim, out int repartidorId) || posicion == null) return false;

        await _telemetria.RegistrarAsync(repartidorId, posicion);
        return true;
    }
}
