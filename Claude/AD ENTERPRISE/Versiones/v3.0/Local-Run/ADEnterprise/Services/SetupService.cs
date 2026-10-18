using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace ADEnterprise.Services;

/// <summary>
/// Servicio de configuración inicial.
/// Se ejecuta UNA SOLA VEZ cuando la empresa es instalada por primera vez.
/// Crea el archivo Google Sheets con el nombre "AD Enterprise.[NombreEmpresa]"
/// </summary>
public class SetupService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    // Scopes necesarios: Sheets + Drive
    private static readonly string[] Scopes =
    {
        SheetsService.Scope.Spreadsheets,
        DriveService.Scope.Drive
    };

    public SetupService(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env    = env;
    }

    /// <summary>
    /// Retorna true si el sistema ya fue configurado (tiene SpreadsheetId guardado).
    /// </summary>
    public bool EstaConfigurado()
    {
        var id = _config["Empresa:SpreadsheetId"];
        return !string.IsNullOrWhiteSpace(id) && id != "PENDIENTE";
    }

    /// <summary>
    /// Inicializa un Spreadsheet existente (creado manualmente por el usuario)
    /// y guarda la configuración.
    /// </summary>
    public async Task InicializarSpreadsheetExistenteAsync(
        string spreadsheetId, string nombreEmpresa, string credentialsPath)
    {
        GoogleCredential credential;
        using var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read);
        credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);

        var sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "AD Enterprise Setup"
        });

        await InicializarHojasAsync(sheetsService, spreadsheetId, nombreEmpresa);
    }

    /// <summary>Guarda la configuración — método público para uso desde Setup page.</summary>
    public async Task GuardarConfiguracionPublicaAsync(
        string nombreEmpresa, string spreadsheetId, string nombreArchivo)
        => await GuardarConfiguracionAsync(nombreEmpresa, spreadsheetId, nombreArchivo);

    /// <summary>
    /// (Opcional) Crea el Google Sheets automáticamente via Drive API.
    /// Puede fallar si la cuenta de servicio no tiene cuota de Drive.
    /// </summary>
    public async Task<string> ConfigurarEmpresaAsync(string nombreEmpresa, string credentialsPath)
    {
        GoogleCredential credential;
        using var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read);
        credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);

        // ── Crear archivo en Google Drive ────────────────────────────────────
        var driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "AD Enterprise Setup"
        });

        var nombreArchivo = $"AD Enterprise.{nombreEmpresa}";

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name     = nombreArchivo,
            MimeType = "application/vnd.google-apps.spreadsheet"
        };

        var createRequest = driveService.Files.Create(fileMetadata);
        createRequest.Fields = "id, name";
        var file = await createRequest.ExecuteAsync();

        string spreadsheetId = file.Id;

        // ── Inicializar hojas maestras en el nuevo Sheets ────────────────────
        var sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "AD Enterprise Setup"
        });

        await InicializarHojasAsync(sheetsService, spreadsheetId, nombreEmpresa);

        // ── Guardar configuración en appsettings.json ────────────────────────
        await GuardarConfiguracionAsync(nombreEmpresa, spreadsheetId, nombreArchivo);

        return spreadsheetId;
    }

    /// <summary>
    /// Inicializa todas las hojas del nuevo Spreadsheet.
    /// </summary>
    private async Task InicializarHojasAsync(SheetsService sheets, string spreadsheetId, string empresa)
    {
        // La hoja que crea Google por defecto se llama "Hoja 1" — la renombramos a FACTURAS
        var requests = new List<Request>
        {
            // Renombrar hoja inicial → FACTURAS (única hoja para todas las facturas)
            new Request
            {
                UpdateSheetProperties = new UpdateSheetPropertiesRequest
                {
                    Properties = new SheetProperties { SheetId = 0, Title = "FACTURAS" },
                    Fields = "title"
                }
            },
            // Crear hoja USUARIOS
            new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = "USUARIOS" } } },
            // Crear hoja COMPANIAS
            new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = "COMPANIAS" } } },
            // Crear hoja CLIENTES
            new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = "CLIENTES" } } },
            // Crear hoja PAGOS
            new Request { AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = "PAGOS" } } },
        };

        await sheets.Spreadsheets.BatchUpdate(
            new BatchUpdateSpreadsheetRequest { Requests = requests },
            spreadsheetId
        ).ExecuteAsync();

        // Agregar encabezados a cada hoja
        var encabezados = new Dictionary<string, IList<object>>
        {
            ["USUARIOS"]  = new object[] { "ID","NombreCompleto","NombreUsuario","PasswordHash","Correo","Perfil","Estado","FechaCreacion" },
            ["COMPANIAS"] = new object[] { "ID","Nombre","RNC","Telefono","Direccion","Correo","CondicionPagoDias","Activo" },
            ["CLIENTES"]  = new object[] { "ID","NombreCompleto","Cedula","Telefono","Correo","Activo" },
            ["PAGOS"]     = new object[] { "NumTransaccionPago","NumTransaccionFactura","NumTicket","FechaPago","MontoPagado","FormaPago","NumChequeReferencia","Notas","UsuarioRegistro","FechaRegistro" },
            ["FACTURAS"]  = new object[] { "NumTransaccion","NumTicket","Empresa","Cliente","Monto","FechaFactura","Estado","PagadoALaFecha","Notas","ValorAnterior","Origen","FechaMigracion","UsuarioMigracion" }
        };

        var data = encabezados.Select(kvp => new ValueRange
        {
            Range  = $"{kvp.Key}!A1",
            Values = new List<IList<object>> { kvp.Value }
        }).ToList();

        await sheets.Spreadsheets.Values.BatchUpdate(
            new BatchUpdateValuesRequest
            {
                Data             = data,
                ValueInputOption = "RAW"
            },
            spreadsheetId
        ).ExecuteAsync();
    }

    /// <summary>
    /// Guarda la configuración de la empresa en appsettings.json
    /// </summary>
    private async Task GuardarConfiguracionAsync(string nombreEmpresa, string spreadsheetId, string nombreArchivo)
    {
        var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = await System.IO.File.ReadAllTextAsync(appSettingsPath);

        // Actualizar usando reemplazo simple de JSON
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Reescribir el JSON con los nuevos valores
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        var dict    = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

        // Actualizar sección Empresa
        var empresaSection = new Dictionary<string, string>
        {
            ["Nombre"]            = nombreEmpresa,
            ["SpreadsheetId"]     = spreadsheetId,
            ["SpreadsheetNombre"] = nombreArchivo
        };

        dict!["Empresa"] = empresaSection;

        // Actualizar Google:SpreadsheetId también para compatibilidad
        if (dict.ContainsKey("Google"))
        {
            var googleJson = System.Text.Json.JsonSerializer.Serialize(dict["Google"]);
            var googleDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(googleJson);
            googleDict!["SpreadsheetId"] = spreadsheetId;
            dict["Google"] = googleDict;
        }

        var nuevoJson = System.Text.Json.JsonSerializer.Serialize(dict, options);
        await System.IO.File.WriteAllTextAsync(appSettingsPath, nuevoJson);
    }
}
