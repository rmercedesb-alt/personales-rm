// LicenseTool — generador/emisor de licencias para MovilBusiness Dashboard Smart.
//
// Esta herramienta NUNCA se despliega junto al producto (no es un proyecto referenciado
// por DashSmart.csproj, vive en una carpeta hermana). Solo debe correrla AM COMERCIAL,
// en su propia máquina, nunca en el servidor de un cliente.
//
// La llave PRIVADA que genera "claves" es lo único que permite emitir/renovar licencias
// válidas — el sistema (DashSmart) solo trae la llave PÚBLICA, que sirve para VERIFICAR
// una firma, nunca para crearla. Por eso, apenas generes la llave privada, MUÉVELA fuera
// de esta carpeta (que vive en OneDrive, sincronizada) a un lugar que controles tú solo
// (ej. un USB, un gestor de contraseñas, una carpeta local no sincronizada). Si esa llave
// se pierde, no podrás renovar licencias ya emitidas con ella — tendrías que generar un
// par nuevo y re-pegar la llave pública en el código (y re-emitir licencias para todos
// los clientes activos).

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Lista canónica de los 9 módulos — debe coincidir exactamente con las claves que usa
// Services/LicenseService.cs y los atributos [RequiereModulo("...")] en los controllers
// del proyecto principal (DashSmart). Si agregan un 10º módulo al sistema, agregarlo
// aquí también.
string[] TodosLosModulos =
[
    "torre", "visitas", "geo", "seguimientos", "productividad",
    "historicos", "supervision", "presupuestos", "presupuestosGestion"
];

if (args.Length == 0) { MostrarAyuda(); return; }

switch (args[0])
{
    case "claves": GenerarClaves(args); break;
    case "emitir": Emitir(args); break;
    default: MostrarAyuda(); break;
}

void MostrarAyuda()
{
    Console.WriteLine($$"""
    LicenseTool — generador de licencias MovilBusiness Dashboard Smart

    Uso:
      dotnet run -- claves --out <ruta_llave_privada.xml>
          Genera un par de llaves RSA-2048 nuevo. Guarda la llave PRIVADA en la ruta
          indicada e imprime la llave PÚBLICA en pantalla para pegarla UNA SOLA VEZ en
          MovilbusinessDashBoardSmart/Services/LicenseService.cs (constante PublicKeyXml).
          Solo se hace una vez por producto (no por cliente ni por licencia).

      dotnet run -- emitir --clave <ruta_llave_privada.xml> --cliente "Nombre del cliente"
                   --expira 2026-12-31 [--modulos torre,visitas,...] [--huella XXXX-XXXX-XXXX-XXXX-XXXX] [--out license.lic]
          Emite (o renueva) un license.lic firmado, listo para copiar a
          App_Data/license.lic en el servidor de ese cliente.
          Si omites --modulos, activa los 9. Módulos válidos:
          {{string.Join(", ", TodosLosModulos)}}

    Ejemplos:
      dotnet run -- claves --out "D:\LlavesPrivadas\dashsmart_private.xml"
      dotnet run -- emitir --clave "D:\LlavesPrivadas\dashsmart_private.xml" --cliente "Distribuidora Ejemplo" --expira 2027-03-31
      dotnet run -- emitir --clave "D:\LlavesPrivadas\dashsmart_private.xml" --cliente "Distribuidora Ejemplo" --expira 2027-03-31 --modulos torre,visitas,historicos
    """);
}

void GenerarClaves(string[] a)
{
    var outPath = Arg(a, "--out") ?? "license_private.xml";
    if (File.Exists(outPath))
    {
        Console.WriteLine($"Ya existe un archivo en {Path.GetFullPath(outPath)} — no se sobrescribe. Borra ese archivo o usa otra ruta con --out si de verdad quieres generar un par NUEVO (ojo: eso invalida las licencias ya emitidas con el par anterior).");
        return;
    }
    using var rsa = RSA.Create(2048);
    var privada = rsa.ToXmlString(true);
    var publica = rsa.ToXmlString(false);
    File.WriteAllText(outPath, privada);
    Console.WriteLine($"Llave PRIVADA guardada en: {Path.GetFullPath(outPath)}");
    Console.WriteLine();
    Console.WriteLine("*** IMPORTANTE *** Mueve ese archivo AHORA MISMO fuera de esta carpeta");
    Console.WriteLine("(esta carpeta vive en OneDrive, sincronizada) a un lugar que controles");
    Console.WriteLine("solo tú. Sin esa llave no podrás emitir ni renovar ninguna licencia.");
    Console.WriteLine();
    Console.WriteLine("Pega esto UNA SOLA VEZ en Services/LicenseService.cs, en la constante");
    Console.WriteLine("PublicKeyXml (reemplaza el valor de ejemplo que trae el código):");
    Console.WriteLine();
    Console.WriteLine(publica);
}

void Emitir(string[] a)
{
    var clavePath = Arg(a, "--clave");
    if (clavePath is null || !File.Exists(clavePath))
    {
        Console.WriteLine("Falta --clave <ruta a la llave privada> o el archivo no existe.");
        return;
    }
    var cliente = Arg(a, "--cliente") ?? "Cliente";
    var expiraStr = Arg(a, "--expira");
    if (expiraStr is null || !DateOnly.TryParse(expiraStr, out var expira))
    {
        Console.WriteLine("Falta --expira o no es una fecha válida (formato: yyyy-MM-dd, ej. 2026-12-31).");
        return;
    }
    var modulosArg = Arg(a, "--modulos");
    var activos = modulosArg is null
        ? TodosLosModulos.ToHashSet()
        : modulosArg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    var desconocidos = activos.Except(TodosLosModulos).ToList();
    if (desconocidos.Count > 0)
    {
        Console.WriteLine($"Módulo(s) desconocido(s): {string.Join(", ", desconocidos)}. Válidos: {string.Join(", ", TodosLosModulos)}");
        return;
    }

    var modulos = TodosLosModulos.ToDictionary(m => m, m => activos.Contains(m));

    // --huella XXXX-XXXX-XXXX-XXXX-XXXX (opcional): ata la licencia a UN servidor. Sin ella, funciona en cualquiera.
    string? huella = null;
    var huellaArg = Arg(a, "--huella");
    if (huellaArg is not null)
    {
        var h = new string(huellaArg.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (h.Length != 20 || !h.All(Uri.IsHexDigit))
        {
            Console.WriteLine("La huella no es válida: debe ser 20 caracteres hexadecimales (formato XXXX-XXXX-XXXX-XXXX-XXXX).");
            return;
        }
        huella = string.Join("-", Enumerable.Range(0, 5).Select(i => h.Substring(i * 4, 4)));
    }

    var payloadObj = new
    {
        cliente,
        emitido = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
        expira = expira.ToString("yyyy-MM-dd"),
        modulos,
        huella
    };
    var payloadJson = JsonSerializer.Serialize(payloadObj, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

    using var rsa = RSA.Create();
    rsa.FromXmlString(File.ReadAllText(clavePath));
    var firma = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    var licenseFile = new { payload = Convert.ToBase64String(payloadBytes), signature = Convert.ToBase64String(firma) };
    var outPath = Arg(a, "--out") ?? "license.lic";
    File.WriteAllText(outPath, JsonSerializer.Serialize(licenseFile));

    Console.WriteLine($"Licencia emitida: {Path.GetFullPath(outPath)}");
    Console.WriteLine($"Cliente: {cliente}");
    Console.WriteLine($"Expira: {expira:yyyy-MM-dd}");
    Console.WriteLine($"Módulos activos: {string.Join(", ", modulos.Where(m => m.Value).Select(m => m.Key))}");
    var inactivos = modulos.Where(m => !m.Value).Select(m => m.Key).ToList();
    if (inactivos.Count > 0) Console.WriteLine($"Módulos INACTIVOS: {string.Join(", ", inactivos)}");
    Console.WriteLine();
    Console.WriteLine("Copia este archivo a App_Data/license.lic en el servidor del cliente.");
    Console.WriteLine("El sistema lo detecta solo (revisa cada ~10 min), sin reiniciar nada.");
}

string? Arg(string[] a, string nombre)
{
    var i = Array.IndexOf(a, nombre);
    return (i >= 0 && i + 1 < a.Length) ? a[i + 1] : null;
}
