using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Parametros;

namespace Routify.Web.Services.Telemetria;

/// <summary>Elimina periódicamente la telemetría GPS más antigua que el período de retención configurado.</summary>
public class DepuracionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DepuracionWorker> _log;

    public DepuracionWorker(IServiceScopeFactory scopes, ILogger<DepuracionWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RoutifyDbContext>();
                var cfg = scope.ServiceProvider.GetRequiredService<IConfiguracionService>();
                int dias = Math.Max(1, await cfg.GetIntAsync(ClavesConfiguracion.RetencionUbicacionesDias, 30));
                var limite = DateTime.UtcNow.AddDays(-dias);
                int borrados = await db.Ubicaciones.Where(u => u.FechaHora < limite).ExecuteDeleteAsync(ct);
                if (borrados > 0) _log.LogInformation("Telemetría depurada: {N} registros", borrados);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo depurar la telemetría");
            }

            try { await Task.Delay(TimeSpan.FromHours(6), ct); } catch (OperationCanceledException) { break; }
        }
    }
}
