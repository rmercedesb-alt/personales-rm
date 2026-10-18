using System.Net.Http.Json;
using System.Text.Json;

namespace OptimizadorRutas.Services;

/// <summary>
/// Cliente OpenRouteService (ORS) para distancias reales por carretera.
/// Free tier: 2000 req/día Matrix · 500 req/día Directions
/// Registro gratuito: https://openrouteservice.org/dev/#/login
/// </summary>
public class OrsService
{
    private readonly HttpClient _http;
    private readonly string    _baseUrl;
    private readonly int       _timeoutSec;

    public OrsService(IConfiguration config, HttpClient http)
    {
        _http       = http;
        _baseUrl    = config["Ors:BaseUrl"] ?? "https://api.openrouteservice.org";
        _timeoutSec = config.GetValue<int>("Ors:TimeoutSeconds", 8);
    }

    // ── Matriz de distancias reales (km) y tiempos (min) ─────────────────────
    /// <summary>
    /// Retorna { DistancesKm, DurationsMin } o null si falla (→ fallback Haversine).
    /// coords: lista de (lat, lon)
    /// </summary>
    public async Task<OrsMatrix?> GetMatrixAsync(
        IList<(double Lat, double Lon)> coords,
        string apiKey,
        string profile = "driving-car")
    {
        if (string.IsNullOrWhiteSpace(apiKey) || coords.Count < 2)
            return null;

        // ORS espera [lon, lat]
        var locations = coords.Select(c => new[] { c.Lon, c.Lat }).ToArray();

        var url  = $"{_baseUrl}/v2/matrix/{profile}";
        var body = new { locations, metrics = new[] { "distance", "duration" } };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSec));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body)
            };
            req.Headers.Add("Authorization", apiKey);

            using var resp = await _http.SendAsync(req, cts.Token);

            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                 or System.Net.HttpStatusCode.Forbidden)
                return null;  // clave inválida → fallback silencioso

            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var doc  = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            var root = doc.RootElement;

            var rawDist = root.GetProperty("distances").EnumerateArray()
                .Select(row => row.EnumerateArray()
                    .Select(v => v.GetDouble() / 1000.0)  // metros → km
                    .ToArray())
                .ToArray();

            var rawDur = root.GetProperty("durations").EnumerateArray()
                .Select(row => row.EnumerateArray()
                    .Select(v => v.GetDouble() / 60.0)    // segundos → minutos
                    .ToArray())
                .ToArray();

            return new OrsMatrix { DistancesKm = rawDist, DurationsMin = rawDur };
        }
        catch (OperationCanceledException)  { return null; }   // timeout
        catch (HttpRequestException)        { return null; }   // sin conectividad
        catch                               { return null; }   // cualquier otro error
    }

    // ── Polilínea real de la ruta (GeoJSON) ──────────────────────────────────
    /// <summary>
    /// Retorna lista de [lat, lon] siguiendo calles reales, o null si falla.
    /// ORS limita 50 waypoints por request; se divide en chunks solapados.
    /// </summary>
    public async Task<List<double[]>?> GetPolylineAsync(
        IList<(double Lat, double Lon)> orderedCoords,
        string apiKey,
        string profile = "driving-car")
    {
        if (string.IsNullOrWhiteSpace(apiKey) || orderedCoords.Count < 2)
            return null;

        const int chunkSize = 50;
        var locs = orderedCoords.Select(c => new[] { c.Lon, c.Lat }).ToList();

        // Dividir en chunks solapados
        var chunks = new List<List<double[]>>();
        for (int i = 0; i < locs.Count; i += chunkSize - 1)
        {
            var chunk = locs.Skip(i).Take(chunkSize).ToList();
            if (chunk.Count < 2) break;
            chunks.Add(chunk);
        }

        var url    = $"{_baseUrl}/v2/directions/{profile}/geojson";
        var result = new List<double[]>();

        foreach (var chunk in chunks)
        {
            var body = new
            {
                coordinates      = chunk,
                instructions     = false,
                geometry_simplify= false
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSec));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(body)
                };
                req.Headers.Add("Authorization", apiKey);

                using var resp = await _http.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode) return null;

                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                var doc   = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                var geom  = doc.RootElement
                    .GetProperty("features")[0]
                    .GetProperty("geometry")
                    .GetProperty("coordinates");

                bool first = result.Count == 0;
                foreach (var pt in geom.EnumerateArray())
                {
                    if (!first) { first = true; continue; }  // saltar punto de union
                    var arr = pt.EnumerateArray().ToArray();
                    result.Add([arr[1].GetDouble(), arr[0].GetDouble()]); // [lat, lon]
                }

                await Task.Delay(100, cts.Token);  // respetar rate-limit free tier
            }
            catch { return null; }
        }

        return result.Count > 0 ? result : null;
    }
}

public record OrsMatrix
{
    public double[][] DistancesKm  { get; init; } = [];
    public double[][] DurationsMin { get; init; } = [];
}
