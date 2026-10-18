using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MfSoftLicense;

// Guarda la llave PRIVADA de firma CIFRADA con la clave del usuario (AES-256-GCM, clave
// derivada con PBKDF2-SHA256). Así la carpeta completa se puede comprimir y enviar por
// correo: sin la clave, el archivo llave.enc no sirve para nada. La llave en claro solo
// existe en memoria mientras el programa está abierto y desbloqueado.
public class Vault(string ruta)
{
    private record Archivo(int V, string Salt, int Iter, string Nonce, string Tag, string Cipher, string Publica);

    private readonly object _lk = new();
    private RSA? _key;
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public bool Configurado => File.Exists(ruta);
    public bool Desbloqueado { get { lock (_lk) return _key != null; } }

    public string? PublicaXml => Configurado ? Leer()?.Publica : null;
    public string? Huella
    {
        get
        {
            var p = PublicaXml; if (p is null) return null;
            using var r = RSA.Create(); r.FromXmlString(p);
            var h = Convert.ToHexString(SHA256.HashData(r.ExportParameters(false).Modulus!));
            return string.Join("-", Enumerable.Range(0, 4).Select(i => h.Substring(i * 4, 4)));
        }
    }

    private Archivo? Leer() { try { return JsonSerializer.Deserialize<Archivo>(File.ReadAllText(ruta), new JsonSerializerOptions(JsonSerializerDefaults.Web)); } catch { return null; } }

    private static byte[] Derivar(string clave, byte[] salt, int iter) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave), salt, iter, HashAlgorithmName.SHA256, 32);

    private void Guardar(string clave, string privXml)
    {
        using var rsa = RSA.Create(); rsa.FromXmlString(privXml);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        const int iter = 300_000;
        var plano = Encoding.UTF8.GetBytes(privXml);
        var cifrado = new byte[plano.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Derivar(clave, salt, iter), 16)) aes.Encrypt(nonce, plano, cifrado, tag);
        var f = new Archivo(1, Convert.ToBase64String(salt), iter, Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag), Convert.ToBase64String(cifrado), rsa.ToXmlString(false));
        var tmp = ruta + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(f, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        File.Move(tmp, ruta, true);
    }

    public void Crear(string clave, string privXml)
    {
        Guardar(clave, privXml);
        if (!Desbloquear(clave)) throw new InvalidOperationException("No se pudo verificar la llave guardada.");
    }

    public bool Desbloquear(string clave)
    {
        var f = Leer(); if (f is null) return false;
        try
        {
            var cifrado = Convert.FromBase64String(f.Cipher);
            var plano = new byte[cifrado.Length];
            using (var aes = new AesGcm(Derivar(clave, Convert.FromBase64String(f.Salt), f.Iter), 16))
                aes.Decrypt(Convert.FromBase64String(f.Nonce), cifrado, Convert.FromBase64String(f.Tag), plano);
            var rsa = RSA.Create(); rsa.FromXmlString(Encoding.UTF8.GetString(plano));
            lock (_lk) { _key?.Dispose(); _key = rsa; }
            return true;
        }
        catch (CryptographicException) { return false; }
    }

    public bool CambiarClave(string actual, string nueva)
    {
        if (!Desbloquear(actual)) return false;
        string xml; lock (_lk) xml = _key!.ToXmlString(true);
        Guardar(nueva, xml);
        return true;
    }

    public byte[] Firmar(byte[] datos)
    {
        lock (_lk)
        {
            if (_key is null) throw new InvalidOperationException("Llave bloqueada.");
            return _key.SignData(datos, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    public bool VerificarConPublica(byte[] datos, byte[] firma)
    {
        using var r = RSA.Create(); r.FromXmlString(PublicaXml!);
        return r.VerifyData(datos, firma, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
