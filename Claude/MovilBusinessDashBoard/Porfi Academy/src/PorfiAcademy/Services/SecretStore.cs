using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace PorfiAcademy.Services;

public class MuxCredentials
{
    public string TokenId { get; set; } = "";
    public string TokenSecret { get; set; } = "";

    // Clave de firma para reproducción protegida (se crea en Mux → Configuración → Claves de firma).
    public string SigningKeyId { get; set; } = "";
    public string SigningPrivateKey { get; set; } = "";

    public bool HasSigningKey => SigningKeyId != "" && SigningPrivateKey != "";
}

/// <summary>
/// Guarda las credenciales del dueño del sitio (Mux) cifradas en disco con ASP.NET Data Protection.
/// Nunca van en el código, en appsettings ni en el repositorio.
/// </summary>
public interface ISecretStore
{
    bool IsSetupComplete { get; }
    MuxCredentials? GetMux();
    void SaveMux(MuxCredentials credentials);
}

public class FileSecretStore : ISecretStore
{
    private readonly string _path;
    private readonly IDataProtector _protector;

    public FileSecretStore(IDataProtectionProvider provider, DataPaths paths)
    {
        _path = Path.Combine(paths.DataDir, "secrets.dat");
        _protector = provider.CreateProtector("PorfiAcademy.Secrets.v1");
    }

    public bool IsSetupComplete => File.Exists(_path);

    public MuxCredentials? GetMux()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var json = _protector.Unprotect(File.ReadAllText(_path));
            return JsonSerializer.Deserialize<MuxCredentials>(json);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public void SaveMux(MuxCredentials credentials)
    {
        var json = JsonSerializer.Serialize(credentials);
        File.WriteAllText(_path, _protector.Protect(json));
    }
}

public class DataPaths
{
    public string DataDir { get; }

    public DataPaths(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(DataDir);
    }
}
