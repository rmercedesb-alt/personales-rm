using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PorfiAcademy.Services;

public record PlaybackTokens(string Video, string Thumbnail, string Storyboard);

/// <summary>Firma (RS256) los tokens JWT de corta duración que Mux exige para reproducir videos con política «signed».</summary>
public class MuxTokenSigner
{
    private readonly ISecretStore _secrets;

    public MuxTokenSigner(ISecretStore secrets) => _secrets = secrets;

    public bool IsConfigured => _secrets.GetMux()?.HasSigningKey == true;

    /// <summary>Valida que la clave privada se pueda leer (acepta PEM o PEM en base64, como la entrega Mux).</summary>
    public static bool TryLoadKey(string privateKey, out RSA? rsa, out string error)
    {
        rsa = null;
        error = "";
        try
        {
            var text = privateKey.Trim();
            if (!text.Contains("BEGIN"))
                text = Encoding.UTF8.GetString(Convert.FromBase64String(text));
            var key = RSA.Create();
            key.ImportFromPem(text);
            rsa = key;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
        {
            error = "La clave privada no tiene un formato válido (debe ser la clave privada de Mux, en base64 o PEM).";
            return false;
        }
    }

    public PlaybackTokens Sign(string playbackId, TimeSpan lifetime)
    {
        var creds = _secrets.GetMux();
        if (creds is null || !creds.HasSigningKey)
            throw new InvalidOperationException("La clave de firma de Mux no está configurada.");
        if (!TryLoadKey(creds.SigningPrivateKey, out var rsa, out var error))
            throw new InvalidOperationException(error);

        using (rsa)
        {
            var exp = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
            return new PlaybackTokens(
                Make(rsa!, creds.SigningKeyId, playbackId, "v", exp),
                Make(rsa!, creds.SigningKeyId, playbackId, "t", exp),
                Make(rsa!, creds.SigningKeyId, playbackId, "s", exp));
        }
    }

    private static string Make(RSA rsa, string keyId, string playbackId, string audience, long exp)
    {
        var header = JsonSerializer.Serialize(new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = keyId });
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["sub"] = playbackId, ["aud"] = audience, ["exp"] = exp, ["kid"] = keyId });
        var input = B64(Encoding.UTF8.GetBytes(header)) + "." + B64(Encoding.UTF8.GetBytes(payload));
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return input + "." + B64(signature);
    }

    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
