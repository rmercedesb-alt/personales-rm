using OptimizadorRutas.Models;

namespace OptimizadorRutas.Services;

/// <summary>
/// Optimización de secuencia de visita por el algoritmo
/// del Vecino Más Cercano usando distancia Haversine.
/// Se ejecuta SOLO cuando el usuario presiona el botón — nunca de forma automática.
/// </summary>
public static class RouteOptimizer
{
    // ── Haversine (km) ────────────────────────────────────────────────────────
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

    private static double Deg2Rad(double deg) => deg * Math.PI / 180;

    // ── Vecino Más Cercano ────────────────────────────────────────────────────
    /// <summary>
    /// Reordena la lista <paramref name="clientes"/> in-place y asigna NuevaPosicion.
    /// Solo trabaja con clientes que tengan coordenadas válidas.
    /// Los clientes sin coordenadas se agregan al final manteniendo su orden original.
    /// </summary>
    public static void OptimizarSecuencia(List<ClienteRuta> clientes, int startIdx = 0)
    {
        if (clientes.Count < 2) return;

        var conCoordenadas    = clientes.Where(c => c.TieneCoordenadas).ToList();
        var sinCoordenadas    = clientes.Where(c => !c.TieneCoordenadas).ToList();

        if (conCoordenadas.Count < 2)
        {
            // No hay suficientes puntos — asignar posicion secuencial y salir
            AsignarPosiciones(clientes);
            return;
        }

        int n       = conCoordenadas.Count;
        var visited = new bool[n];
        var order   = new List<int> { startIdx };
        visited[startIdx] = true;

        while (order.Count < n)
        {
            int    last   = order[^1];
            double bestD  = double.MaxValue;
            int    bestJ  = -1;

            for (int j = 0; j < n; j++)
            {
                if (visited[j]) continue;
                double d = Haversine(
                    conCoordenadas[last].Latitud,  conCoordenadas[last].Longitud,
                    conCoordenadas[j].Latitud,     conCoordenadas[j].Longitud);
                if (d < bestD) { bestD = d; bestJ = j; }
            }

            if (bestJ < 0) break;
            order.Add(bestJ);
            visited[bestJ] = true;
        }

        // Reconstruir lista ordenada
        var ordenada = order.Select(i => conCoordenadas[i]).ToList();
        ordenada.AddRange(sinCoordenadas);

        // Copiar resultado de vuelta a la lista original
        clientes.Clear();
        clientes.AddRange(ordenada);
        AsignarPosiciones(clientes);
    }

    private static void AsignarPosiciones(List<ClienteRuta> lista)
    {
        for (int i = 0; i < lista.Count; i++)
            lista[i].NuevaPosicion = i + 1;
    }

    // ── Calcular distancia total de la ruta ───────────────────────────────────
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
}
