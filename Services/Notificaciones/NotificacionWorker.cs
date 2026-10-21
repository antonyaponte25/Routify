using Microsoft.EntityFrameworkCore;
using Routify.Web.Data;
using Routify.Web.Helpers;

namespace Routify.Web.Services.Notificaciones;

/// <summary>
/// Servicio en segundo plano que envía las notificaciones sin bloquear la operación.
/// Cada notificación se intenta hasta 3 veces: la primera de inmediato y los reintentos
/// en el barrido periódico (cada 60 s) de las pendientes o fallidas.
/// </summary>
public class NotificacionWorker : BackgroundService
{
    private const int MaximoIntentos = 3;
    private static readonly TimeSpan IntervaloBarrido = TimeSpan.FromSeconds(60);

    private readonly NotificacionCola _cola;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NotificacionWorker> _log;

    public NotificacionWorker(NotificacionCola cola, IServiceScopeFactory scopes, ILogger<NotificacionWorker> log)
    {
        _cola = cola;
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Espera breve para que la aplicación termine de arrancar.
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Procesa todo lo que haya en la cola.
                while (_cola.Lector.TryRead(out int id))
                    await ProcesarAsync(id, ct);

                // Espera una nueva notificación o el siguiente barrido.
                using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
                espera.CancelAfter(IntervaloBarrido);
                try
                {
                    if (await _cola.Lector.WaitToReadAsync(espera.Token)) continue;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* toca barrido */ }

                await BarridoAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error en el servicio de notificaciones");
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task BarridoAsync(CancellationToken ct)
    {
        List<int> ids;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RoutifyDbContext>();
            var desde = DateTime.UtcNow.AddHours(-6);
            ids = await db.Notificaciones.AsNoTracking()
                .Where(n => (n.Estado == EstadoNotificacion.Pendiente || n.Estado == EstadoNotificacion.Fallida)
                            && n.Intentos < MaximoIntentos && n.FechaCreacion > desde)
                .OrderBy(n => n.Id).Take(50).Select(n => n.Id).ToListAsync(ct);
        }
        foreach (var id in ids) await ProcesarAsync(id, ct);
    }

    private async Task ProcesarAsync(int id, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoutifyDbContext>();
        var n = await db.Notificaciones.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n == null) return;
        if (n.Estado != EstadoNotificacion.Pendiente && n.Estado != EstadoNotificacion.Fallida) return;
        if (n.Intentos >= MaximoIntentos) return;

        ResultadoEnvio r;
        if (n.Canal == CanalNotificacion.Correo)
        {
            var correo = scope.ServiceProvider.GetRequiredService<ICorreoSender>();
            r = await correo.EnviarAsync(n.Destino, n.Asunto ?? "Routify", n.Mensaje, ct);
        }
        else
        {
            var wa = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();
            r = await wa.EnviarAsync(n.Destino, n.Mensaje, ct);
        }

        n.Intentos++;
        if (r.Ok)
        {
            n.Estado = r.Simulado ? EstadoNotificacion.Simulada : EstadoNotificacion.Enviada;
            n.FechaEnvio = DateTime.UtcNow;
            n.Error = null;
        }
        else
        {
            n.Estado = EstadoNotificacion.Fallida;
            n.Error = r.Error;
        }
        await db.SaveChangesAsync(ct);
    }
}
