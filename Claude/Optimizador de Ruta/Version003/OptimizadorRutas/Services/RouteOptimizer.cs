using OptimizadorRutas.Models;

namespace OptimizadorRutas.Services;

/// <summary>
/// Optimización de secuencia — Vecino Más Cercano.
/// Soporta Haversine, matriz ORS, y primer/último cliente fijos.
/// La optimización se ejecuta SOLO cuando el usuario presiona el botón.
/// </summary>
public static class RouteOptimizer
{
    // ── Haversine ─────────────────────────────────────────────────────────────
    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        double dLat = Deg2Rad(lat2 - lat1);
        double dLon = Deg2Rad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2))
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
    static double Deg2Rad(double d) => d * Math.PI / 180;

    // ── Optimizar ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Reordena la lista in-place y asigna NuevaPosicion.
    /// startCliId / endCliId: CliID del cliente fijo (primero/último). -1 = automático.
    /// distMatrix: matriz ORS [i,j] sobre clientes con coordenadas. null = Haversine.
    /// Retorna el método usado: "ors" | "haversine".
    /// </summary>
    public static string OptimizarSecuencia(
        List<ClienteRuta> clientes,
        int startCliId    = -1,
        int endCliId      = -1,
        double[][]? distMatrix = null)
    {
        if (clientes.Count < 2) { AsignarPosiciones(clientes); return "haversine"; }

        var conCoord = clientes.Where(c => c.TieneCoordenadas).ToList();
        var sinCoord = clientes.Where(c => !c.TieneCoordenadas).ToList();

        if (conCoord.Count < 2) { AsignarPosiciones(clientes); return "haversine"; }

        // Resolver índices dentro de conCoord
        int startIdx = 0;
        int? endIdx  = null;

        if (startCliId > 0)
        {
            int i = conCoord.FindIndex(c => c.CliID == startCliId);
            if (i >= 0) startIdx = i;
        }
        if (endCliId > 0)
        {
            int i = conCoord.FindIndex(c => c.CliID == endCliId);
            if (i >= 0 && i != startIdx) endIdx = i;
        }

        // Función de distancia
        Func<int, int, double> dist = distMatrix is not null
            ? (i, j) => distMatrix[i][j]
            : (i, j) => Haversine(conCoord[i].Latitud, conCoord[i].Longitud,
                                  conCoord[j].Latitud, conCoord[j].Longitud);

        // Vecino más cercano
        int n       = conCoord.Count;
        var visited = new bool[n];
        var order   = new List<int> { startIdx };
        visited[startIdx] = true;

        while (order.Count < n)
        {
            int    last  = order[^1];
            double bestD = double.MaxValue;
            int    bestJ = -1;

            for (int j = 0; j < n; j++)
            {
                if (visited[j]) continue;
                if (j == endIdx && order.Count < n - 1) continue;  // reservar para el final
                double d = dist(last, j);
                if (d < bestD) { bestD = d; bestJ = j; }
            }

            if (bestJ < 0)
            {
                if (endIdx.HasValue && !visited[endIdx.Value])
                { order.Add(endIdx.Value); visited[endIdx.Value] = true; }
                break;
            }
            order.Add(bestJ);
            visited[bestJ] = true;
        }

        if (endIdx.HasValue && !order.Contains(endIdx.Value))
            order.Add(endIdx.Value);

        var ordenada = order.Select(i => conCoord[i]).Concat(sinCoord).ToList();
        clientes.Clear();
        clientes.AddRange(ordenada);
        AsignarPosiciones(clientes);

        return distMatrix is not null ? "ors" : "haversine";
    }

    // ── Distancia total ───────────────────────────────────────────────────────
    public static double DistanciaTotalKm(IEnumerable<ClienteRuta> clientes)
    {
        double total = 0;
        ClienteRuta? prev = null;
        foreach (var c in clientes.Where(x => x.TieneCoordenadas))
        {
            if (prev is not null)
                total += Haversine(prev.Latitud, prev.Longitud, c.Latitud, c.Longitud);
            prev = c;
        }
        return Math.Round(total, 1);
    }

    static void AsignarPosiciones(List<ClienteRuta> lista)
    {
        for (int i = 0; i < lista.Count; i++)
            lista[i].NuevaPosicion = i + 1;
    }
}
