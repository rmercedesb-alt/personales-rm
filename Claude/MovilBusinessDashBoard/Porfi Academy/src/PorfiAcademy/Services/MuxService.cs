using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PorfiAcademy.Services;

public record MuxAssetInfo(string Id, string Status, double? Duration, string? PlaybackId, string? PlaybackPolicy, DateTimeOffset? Created, string? Title);

public class MuxService
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;

    public MuxService(HttpClient http, ISecretStore secrets)
    {
        _http = http;
        _secrets = secrets;
    }

    /// <summary>Prueba las credenciales listando 1 asset del environment.</summary>
    public async Task<(bool Ok, string Message)> TestAsync(MuxCredentials credentials)
    {
        try
        {
            using var request = Build(HttpMethod.Get, "video/v1/assets?limit=1", credentials);
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return (true, "Conexión con Mux correcta.");
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => (false, "Mux rechazó las credenciales (Token ID o Secret incorrectos)."),
                System.Net.HttpStatusCode.Forbidden => (false, "El token no tiene permiso de Mux Video (Leer/Escribir)."),
                _ => (false, $"Mux respondió {(int)response.StatusCode}.")
            };
        }
        catch (HttpRequestException ex)
        {
            return (false, "No se pudo contactar a Mux: " + ex.Message);
        }
    }

    public async Task<(bool Ok, string Message, List<MuxAssetInfo> Assets)> ListAssetsAsync()
    {
        var credentials = _secrets.GetMux();
        if (credentials is null) return (false, "Mux no está configurado.", new());
        try
        {
            using var request = Build(HttpMethod.Get, "video/v1/assets?limit=25", credentials);
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return (false, $"Mux respondió {(int)response.StatusCode}.", new());

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var list = doc.RootElement.GetProperty("data").EnumerateArray().Select(ParseAsset).ToList();
            return (true, "OK", list);
        }
        catch (HttpRequestException ex)
        {
            return (false, "No se pudo contactar a Mux: " + ex.Message, new());
        }
    }

    public async Task<MuxAssetInfo?> GetAssetAsync(string assetId)
    {
        var credentials = _secrets.GetMux();
        if (credentials is null || !IsSafeId(assetId)) return null;
        try
        {
            using var request = Build(HttpMethod.Get, $"video/v1/assets/{assetId}", credentials);
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return ParseAsset(doc.RootElement.GetProperty("data"));
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Crea una URL de subida directa: el navegador sube el archivo a Mux sin pasar por nuestro servidor.</summary>
    public async Task<(bool Ok, string Message, string? Url)> CreateUploadAsync(string corsOrigin, string? title, bool isPublicClip = false)
    {
        var credentials = _secrets.GetMux();
        if (credentials is null) return (false, "Mux no está configurado.", null);
        try
        {
            var body = new
            {
                cors_origin = corsOrigin,
                new_asset_settings = new
                {
                    // Los cursos son «signed»; solo los cortes promocionales del carrusel son públicos.
                    playback_policy = new[] { isPublicClip ? "public" : "signed" },
                    passthrough = title ?? ""
                }
            };
            using var request = Build(HttpMethod.Post, "video/v1/uploads", credentials);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return (false, ErrorMessage(response, text), null);

            using var doc = JsonDocument.Parse(text);
            return (true, "OK", doc.RootElement.GetProperty("data").GetProperty("url").GetString());
        }
        catch (HttpRequestException ex)
        {
            return (false, "No se pudo contactar a Mux: " + ex.Message, null);
        }
    }

    public async Task<(bool Ok, string Message)> DeleteAssetAsync(string assetId)
    {
        var credentials = _secrets.GetMux();
        if (credentials is null || !IsSafeId(assetId)) return (false, "Solicitud no válida.");
        try
        {
            using var request = Build(HttpMethod.Delete, $"video/v1/assets/{assetId}", credentials);
            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode ? (true, "Video eliminado.") : (false, ErrorMessage(response, await response.Content.ReadAsStringAsync()));
        }
        catch (HttpRequestException ex)
        {
            return (false, "No se pudo contactar a Mux: " + ex.Message);
        }
    }

    private static bool IsSafeId(string id) => id.Length is > 5 and < 100 && id.All(char.IsLetterOrDigit);

    private static MuxAssetInfo ParseAsset(JsonElement item)
    {
        string? playback = null, policy = null;
        if (item.TryGetProperty("playback_ids", out var ids) && ids.GetArrayLength() > 0)
        {
            playback = ids[0].GetProperty("id").GetString();
            policy = ids[0].TryGetProperty("policy", out var p) ? p.GetString() : null;
        }
        double? duration = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
        DateTimeOffset? created = null;
        if (item.TryGetProperty("created_at", out var c) && long.TryParse(c.GetString(), out var unix))
            created = DateTimeOffset.FromUnixTimeSeconds(unix);
        var title = item.TryGetProperty("passthrough", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        return new MuxAssetInfo(item.GetProperty("id").GetString() ?? "", item.GetProperty("status").GetString() ?? "", duration, playback, policy, created, title);
    }

    private static string ErrorMessage(HttpResponseMessage response, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("messages", out var msgs))
                return $"Mux ({(int)response.StatusCode}): " + string.Join(" ", msgs.EnumerateArray().Select(m => m.GetString()));
        }
        catch (JsonException) { }
        return $"Mux respondió {(int)response.StatusCode}.";
    }

    private static HttpRequestMessage Build(HttpMethod method, string url, MuxCredentials credentials)
    {
        var request = new HttpRequestMessage(method, url);
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.TokenId}:{credentials.TokenSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        return request;
    }
}
