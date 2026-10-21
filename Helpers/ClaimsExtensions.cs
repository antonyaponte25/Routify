using System.Security.Claims;

namespace Routify.Web.Helpers;

public static class ClaimsExtensions
{
    public static int UsuarioId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    public static int? RepartidorId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("repartidor_id")?.Value, out int id) ? id : null;
}
