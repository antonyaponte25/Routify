using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Routify.Web.Helpers;

namespace Routify.Web.Controllers.Api;

/// <summary>
/// Geocodificación de direcciones con Nominatim (OpenStreetMap) (RF-12). Se hace desde el servidor para
/// cumplir su política de uso: User-Agent identificable, máximo 1 consulta por segundo y caché de resultados.
/// El operador siempre puede ajustar el punto arrastrando el marcador en el mapa.
/// </summary>
[ApiController, Authorize(Roles = Roles.AdminOperador)]
[Route("api/geocode")]
public class GeocodeController : ControllerBase
{
    private static readonly SemaphoreSlim Compuerta = new(1, 1);
    private static DateTime _ultimaConsulta = DateTime.MinValue;

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _cfg;
    private readonly ILogger<GeocodeController> _log;

    public GeocodeController(IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, ILogger<GeocodeController> log)
    {
        _http = http;
        _cache = cache;
        _cfg = cfg;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Buscar([FromQuery] string q, CancellationToken ct)
    {
        q = (q ?? "").Trim();
        if (q.Length < 4 || q.Length > 200) return Ok(Array.Empty<object>());

        string consulta = q.Contains("lima", StringComparison.OrdinalIgnoreCase) ? q : q + ", Lima";
        string clave = "geo:" + consulta.ToLowerInvariant();
        if (_cache.TryGetValue(clave, out List<ResultadoGeo>? enCache) && enCache != null) return Ok(enCache);

        await Compuerta.WaitAsync(ct);
        try
        {
            // Máximo 1 consulta por segundo (política de uso de Nominatim)
            var espera = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - _ultimaConsulta);
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);

            string url = "search?format=jsonv2&limit=5&addressdetails=0" +
                         $"&countrycodes={Uri.EscapeDataString(_cfg["Geocodificacion:PaisCodigo"] ?? "pe")}" +
                         $"&viewbox={Uri.EscapeDataString(_cfg["Geocodificacion:ViewBox"] ?? "-77.20,-11.90,-76.80,-12.30")}" +
                         $"&q={Uri.EscapeDataString(consulta)}";

            var cliente = _http.CreateClient("nominatim");
            using var resp = await cliente.GetAsync(url, ct);
            _ultimaConsulta = DateTime.UtcNow;

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Nominatim respondió {Codigo}", (int)resp.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "El servicio de direcciones no respondió. Ajuste el punto manualmente en el mapa." });
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var lista = new List<ResultadoGeo>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (double.TryParse(item.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                    double.TryParse(item.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                {
                    lista.Add(new ResultadoGeo(item.GetProperty("display_name").GetString() ?? "", lat, lon));
                }
            }
            _cache.Set(clave, lista, TimeSpan.FromHours(24));
            return Ok(lista);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _log.LogWarning(ex, "Fallo al geocodificar");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "No se pudo consultar el servicio de direcciones. Ajuste el punto manualmente en el mapa." });
        }
        finally
        {
            Compuerta.Release();
        }
    }

    public record ResultadoGeo(string Descripcion, double Lat, double Lng);
}
