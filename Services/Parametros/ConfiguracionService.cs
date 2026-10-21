using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Routify.Web.Data;
using Routify.Web.Helpers;
using Routify.Web.Services.Ruteo;

namespace Routify.Web.Services.Parametros;

public interface IConfiguracionService
{
    Task<string> GetAsync(string clave, string defecto);
    Task<double> GetDoubleAsync(string clave, double defecto);
    Task<int> GetIntAsync(string clave, int defecto);
    Task<ParametrosRuteo> RuteoAsync();
}

/// <summary>Lee la tabla configuracion con caché de 60 segundos.</summary>
public class ConfiguracionService : IConfiguracionService
{
    private const string CacheKey = "routify:configuracion";
    private readonly RoutifyDbContext _db;
    private readonly IMemoryCache _cache;

    public ConfiguracionService(RoutifyDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private async Task<Dictionary<string, string>> TodoAsync()
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, string>? valores) && valores != null)
            return valores;

        valores = await _db.Parametros.AsNoTracking().ToDictionaryAsync(x => x.Clave, x => x.Valor);
        _cache.Set(CacheKey, valores, TimeSpan.FromSeconds(60));
        return valores;
    }

    public async Task<string> GetAsync(string clave, string defecto)
    {
        var todo = await TodoAsync();
        return todo.TryGetValue(clave, out var v) ? v : defecto;
    }

    public async Task<double> GetDoubleAsync(string clave, double defecto)
    {
        var s = await GetAsync(clave, "");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defecto;
    }

    public async Task<int> GetIntAsync(string clave, int defecto)
    {
        var s = await GetAsync(clave, "");
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defecto;
    }

    public async Task<ParametrosRuteo> RuteoAsync()
    {
        var def = new ParametrosRuteo();
        return new ParametrosRuteo
        {
            VelocidadKmh = Math.Max(5, await GetDoubleAsync(ClavesConfiguracion.VelocidadPromedioKmh, def.VelocidadKmh)),
            FactorCalle = Math.Max(1, await GetDoubleAsync(ClavesConfiguracion.FactorCalle, def.FactorCalle)),
            TiempoServicioMin = Math.Max(0, await GetIntAsync(ClavesConfiguracion.TiempoServicioMin, def.TiempoServicioMin)),
            AlmacenLat = await GetDoubleAsync(ClavesConfiguracion.AlmacenLatitud, def.AlmacenLat),
            AlmacenLng = await GetDoubleAsync(ClavesConfiguracion.AlmacenLongitud, def.AlmacenLng),
            AlmacenNombre = await GetAsync(ClavesConfiguracion.AlmacenNombre, def.AlmacenNombre),
            ToleranciaEtaMin = await GetIntAsync(ClavesConfiguracion.ToleranciaEtaMin, def.ToleranciaEtaMin)
        };
    }
}
