using Routify.Web.Helpers;

namespace Routify.Web.Services.Ruteo;

public record PuntoEntrega(int PedidoId, double Lat, double Lng, TimeOnly? VentanaInicio, TimeOnly? VentanaFin);

public record ParadaPlan(int PedidoId, int Orden, double Lat, double Lng, double DistanciaKm, DateTime EtaUtc);

public record PlanRuta(IReadOnlyList<ParadaPlan> Paradas, double DistanciaTotalKm, int TiempoTotalMin, DateTime FinUtc);

public record CandidatoReparto(int RepartidorId, double Lat, double Lng, int CapacidadRestante);

/// <summary>
/// Motor de optimización de rutas propio (sin servicios externos):
///  1) Distancia = Haversine × factor de calle (aproxima el recorrido urbano).
///  2) Secuencia inicial por vecino más cercano.
///  3) Mejora con 2-opt (ruta abierta, origen fijo) minimizando el tiempo total de la ruta
///     (viaje + servicio + espera/penalización por ventanas horarias).
///  4) ETA acumulado con velocidad promedio y tiempo de servicio por parada.
/// Para 40 paradas se resuelve en milisegundos (RNF-04: ≤ 5 s).
/// </summary>
public static class OptimizadorRutas
{
    private const double PenalizacionRetrasoPorMin = 3.0;

    public static double DistanciaCalleKm(double lat1, double lng1, double lat2, double lng2, ParametrosRuteo p) =>
        GeoUtils.HaversineKm(lat1, lng1, lat2, lng2) * p.FactorCalle;

    /// <summary>Calcula la mejor secuencia de visita a partir del origen indicado.</summary>
    public static PlanRuta Optimizar(double origenLat, double origenLng, IReadOnlyList<PuntoEntrega> puntos,
                                     ParametrosRuteo p, DateTime inicioUtc)
    {
        int n = puntos.Count;
        if (n == 0) return new PlanRuta(Array.Empty<ParadaPlan>(), 0, 0, inicioUtc);

        // Matriz de distancias: índice 0 = origen, 1..n = puntos.
        var d = new double[n + 1, n + 1];
        var lat = new double[n + 1];
        var lng = new double[n + 1];
        lat[0] = origenLat; lng[0] = origenLng;
        for (int i = 0; i < n; i++) { lat[i + 1] = puntos[i].Lat; lng[i + 1] = puntos[i].Lng; }
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
                d[i, j] = i == j ? 0 : DistanciaCalleKm(lat[i], lng[i], lat[j], lng[j], p);

        // 1) Vecino más cercano
        var orden = new List<int>(n);
        var visitado = new bool[n + 1];
        int actual = 0;
        for (int k = 0; k < n; k++)
        {
            int mejor = -1;
            double menor = double.MaxValue;
            for (int j = 1; j <= n; j++)
            {
                if (visitado[j]) continue;
                if (d[actual, j] < menor) { menor = d[actual, j]; mejor = j; }
            }
            visitado[mejor] = true;
            orden.Add(mejor);
            actual = mejor;
        }

        // 2) Mejora 2-opt
        double costo = Costo(orden, d, puntos, p, inicioUtc);
        bool mejora = true;
        int iteraciones = 0;
        while (mejora && iteraciones++ < 200)
        {
            mejora = false;
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var candidato = new List<int>(orden);
                    candidato.Reverse(i, j - i + 1);
                    double c = Costo(candidato, d, puntos, p, inicioUtc);
                    if (c < costo - 1e-6)
                    {
                        orden = candidato;
                        costo = c;
                        mejora = true;
                    }
                }
            }
        }

        var secuencia = orden.Select(i => puntos[i - 1]).ToList();
        return Simular(origenLat, origenLng, secuencia, p, inicioUtc);
    }

    /// <summary>Calcula distancias y ETA de una secuencia ya definida (sin reordenar). Sirve para recalcular ETA (RF-11).</summary>
    public static PlanRuta Simular(double origenLat, double origenLng, IReadOnlyList<PuntoEntrega> secuencia,
                                   ParametrosRuteo p, DateTime inicioUtc)
    {
        var paradas = new List<ParadaPlan>(secuencia.Count);
        double lat = origenLat, lng = origenLng, total = 0;
        DateTime t = inicioUtc;
        int orden = 1;

        foreach (var pt in secuencia)
        {
            double km = DistanciaCalleKm(lat, lng, pt.Lat, pt.Lng, p);
            t = t.AddMinutes(km / p.VelocidadKmh * 60.0);
            t = EsperarVentana(t, pt, out _);
            paradas.Add(new ParadaPlan(pt.PedidoId, orden++, pt.Lat, pt.Lng, Math.Round(km, 2), t));
            total += km;
            t = t.AddMinutes(p.TiempoServicioMin);
            lat = pt.Lat; lng = pt.Lng;
        }

        int minutos = (int)Math.Round((t - inicioUtc).TotalMinutes);
        return new PlanRuta(paradas, Math.Round(total, 2), minutos, t);
    }

    private static double Costo(List<int> orden, double[,] d, IReadOnlyList<PuntoEntrega> puntos,
                                ParametrosRuteo p, DateTime inicioUtc)
    {
        DateTime t = inicioUtc;
        int pos = 0;
        double retrasoMin = 0;
        foreach (int idx in orden)
        {
            t = t.AddMinutes(d[pos, idx] / p.VelocidadKmh * 60.0);
            t = EsperarVentana(t, puntos[idx - 1], out double tarde);
            retrasoMin += tarde;
            t = t.AddMinutes(p.TiempoServicioMin);
            pos = idx;
        }
        return (t - inicioUtc).TotalMinutes + PenalizacionRetrasoPorMin * retrasoMin;
    }

    /// <summary>Si se llega antes de la ventana se espera; si se llega después se contabiliza el retraso.</summary>
    private static DateTime EsperarVentana(DateTime llegadaUtc, PuntoEntrega pt, out double retrasoMin)
    {
        retrasoMin = 0;
        if (pt.VentanaInicio is null && pt.VentanaFin is null) return llegadaUtc;

        var local = llegadaUtc.ALima();
        double minutoDia = local.Hour * 60 + local.Minute + local.Second / 60.0;

        if (pt.VentanaInicio is { } vi)
        {
            double ini = vi.Hour * 60 + vi.Minute;
            if (minutoDia < ini)
            {
                llegadaUtc = llegadaUtc.AddMinutes(ini - minutoDia);
                minutoDia = ini;
            }
        }
        if (pt.VentanaFin is { } vf)
        {
            double fin = vf.Hour * 60 + vf.Minute;
            if (minutoDia > fin) retrasoMin = minutoDia - fin;
        }
        return llegadaUtc;
    }

    /// <summary>
    /// Reparto automático de varios pedidos entre varios repartidores (heurística de barrido):
    /// los pedidos se ordenan por ángulo alrededor del almacén y se cortan en tramos proporcionales
    /// a la capacidad restante de cada repartidor; los repartidores se ordenan por su propio ángulo.
    /// Devuelve repartidorId -> pedidoIds; los que no caben quedan en <paramref name="sinAsignar"/>.
    /// </summary>
    public static Dictionary<int, List<int>> Repartir(double almacenLat, double almacenLng,
        IReadOnlyList<PuntoEntrega> pedidos, IReadOnlyList<CandidatoReparto> repartidores, out List<int> sinAsignar)
    {
        var resultado = new Dictionary<int, List<int>>();
        sinAsignar = new List<int>();

        var disponibles = repartidores.Where(r => r.CapacidadRestante > 0).ToList();
        if (disponibles.Count == 0 || pedidos.Count == 0)
        {
            sinAsignar.AddRange(pedidos.Select(x => x.PedidoId));
            return resultado;
        }

        double Angulo(double la, double lo) => Math.Atan2(la - almacenLat, lo - almacenLng);

        var ordenados = pedidos.OrderBy(x => Angulo(x.Lat, x.Lng)).ThenBy(x => x.PedidoId).ToList();
        var repOrdenados = disponibles.OrderBy(r => Angulo(r.Lat, r.Lng)).ThenBy(r => r.RepartidorId).ToList();

        int capacidadTotal = repOrdenados.Sum(r => r.CapacidadRestante);
        int aAsignar = Math.Min(ordenados.Count, capacidadTotal);

        // Cuotas proporcionales a la capacidad restante, respetando el tope de cada repartidor.
        var cuota = new int[repOrdenados.Count];
        int asignado = 0;
        for (int i = 0; i < repOrdenados.Count; i++)
        {
            cuota[i] = Math.Min(repOrdenados[i].CapacidadRestante,
                                (int)Math.Floor((double)aAsignar * repOrdenados[i].CapacidadRestante / capacidadTotal));
            asignado += cuota[i];
        }
        for (int i = 0; asignado < aAsignar; i = (i + 1) % repOrdenados.Count)
        {
            if (cuota[i] < repOrdenados[i].CapacidadRestante) { cuota[i]++; asignado++; }
        }

        int cursor = 0;
        for (int i = 0; i < repOrdenados.Count; i++)
        {
            if (cuota[i] == 0) continue;
            resultado[repOrdenados[i].RepartidorId] = ordenados.Skip(cursor).Take(cuota[i]).Select(x => x.PedidoId).ToList();
            cursor += cuota[i];
        }
        sinAsignar.AddRange(ordenados.Skip(cursor).Select(x => x.PedidoId));
        return resultado;
    }
}
