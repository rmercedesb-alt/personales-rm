using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace ADEnterprise.Services;

/// <summary>
/// Servicio base para todas las operaciones con Google Sheets.
/// Usar Service Account (credentials.json) para acceso sin login interactivo.
/// </summary>
public class GoogleSheetsService
{
    private SheetsService? _sheets;
    private readonly string _spreadsheetId;

    // Nombres de hojas maestras
    public const string HOJA_USUARIOS   = "USUARIOS";
    public const string HOJA_COMPANIAS  = "COMPANIAS";
    public const string HOJA_CLIENTES   = "CLIENTES";
    public const string HOJA_PAGOS      = "PAGOS";

    public bool EstaListo => _spreadsheetId != "PENDIENTE" && !string.IsNullOrWhiteSpace(_spreadsheetId);

    public GoogleSheetsService(IConfiguration config)
    {
        var credFile  = config["Google:CredentialsFile"] ?? "credentials.json";
        _spreadsheetId = config["Google:SpreadsheetId"] ?? "PENDIENTE";

        // Si aún no está configurado, no inicializar el cliente
        if (_spreadsheetId == "PENDIENTE" || !System.IO.File.Exists(credFile))
            return;

        GoogleCredential credential;
        using var stream = new FileStream(credFile, FileMode.Open, FileAccess.Read);
        credential = GoogleCredential.FromStream(stream)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        _sheets = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = config["Google:ApplicationName"] ?? "ADEnterprise"
        });
    }

    // ─── LECTURA ─────────────────────────────────────────────────────────────

    /// <summary>Lee todas las filas de un rango. Devuelve lista vacía si no hay datos o sin internet.</summary>
    public async Task<IList<IList<object>>> LeerAsync(string hoja, string rango = "A:Z")
    {
        if (!EstaListo || _sheets == null)
            return new List<IList<object>>();
        try
        {
            var respuesta = await _sheets.Spreadsheets.Values
                .Get(_spreadsheetId, $"{hoja}!{rango}")
                .ExecuteAsync();
            UltimoError = null;
            return respuesta.Values ?? new List<IList<object>>();
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            UltimoError = $"Sin conexión a internet: {ex.Message}";
            return new List<IList<object>>();
        }
        catch (Google.GoogleApiException ex)
        {
            UltimoError = $"Error Google Sheets: {ex.Error?.Message ?? ex.Message}";
            return new List<IList<object>>();
        }
        catch (Exception ex)
        {
            UltimoError = $"Error inesperado: {ex.Message}";
            return new List<IList<object>>();
        }
    }

    /// <summary>Último error de red o API, null si todo está bien.</summary>
    public string? UltimoError { get; private set; }

    // ─── ESCRITURA ────────────────────────────────────────────────────────────

    /// <summary>Agrega una fila al final de la hoja.</summary>
    public async Task AgregarFilaAsync(string hoja, IList<object> valores)
    {
        if (!EstaListo || _sheets == null) return;
        var body = new ValueRange { Values = new List<IList<object>> { valores } };
        var req  = _sheets.Spreadsheets.Values
            .Append(body, _spreadsheetId, $"{hoja}!A:A");
        req.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await EjecutarConManejoAsync(() => req.ExecuteAsync());
    }

    /// <summary>Agrega múltiples filas de una sola vez (mucho más rápido para importaciones).</summary>
    public async Task AgregarMultiplesFilasAsync(string hoja, IList<IList<object>> filas)
    {
        if (!EstaListo || _sheets == null || filas.Count == 0) return;
        var body = new ValueRange { Values = filas };
        var req  = _sheets.Spreadsheets.Values
            .Append(body, _spreadsheetId, $"{hoja}!A:A");
        req.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await EjecutarConManejoAsync(() => req.ExecuteAsync());
    }

    /// <summary>Actualiza un rango específico (ej: "COMPANIAS!A5:H5").</summary>
    public async Task ActualizarRangoAsync(string rango, IList<IList<object>> valores)
    {
        if (!EstaListo || _sheets == null) return;
        var body = new ValueRange { Values = valores };
        var req  = _sheets.Spreadsheets.Values
            .Update(body, _spreadsheetId, rango);
        req.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await EjecutarConManejoAsync(() => req.ExecuteAsync());
    }

    /// <summary>
    /// Actualiza múltiples rangos en UNA SOLA llamada a la API (evita cuota por escritura).
    /// rangosYValores: lista de (rango tipo "HOJA!A5:H5", valores de la fila).
    /// </summary>
    public async Task ActualizarMultiplesRangosAsync(IList<(string Rango, IList<object> Valores)> rangosYValores)
    {
        if (!EstaListo || _sheets == null || rangosYValores.Count == 0) return;

        var body = new BatchUpdateValuesRequest
        {
            ValueInputOption = "USER_ENTERED",
            Data = rangosYValores.Select(rv => new ValueRange
            {
                Range  = rv.Rango,
                Values = new List<IList<object>> { rv.Valores }
            }).ToList()
        };

        await EjecutarConManejoAsync(async () =>
        {
            await _sheets.Spreadsheets.Values
                .BatchUpdate(body, _spreadsheetId)
                .ExecuteAsync();
        });
    }

    private async Task EjecutarConManejoAsync(Func<Task> accion)
    {
        try { await accion(); UltimoError = null; }
        catch (System.Net.Http.HttpRequestException ex)
        {
            UltimoError = $"Sin conexión a internet: {ex.Message}";
            throw; // Re-lanzar para que el llamador sepa que falló
        }
        catch (Google.GoogleApiException ex)
        {
            UltimoError = $"Error Google: {ex.Error?.Message ?? ex.Message}";
            throw;
        }
    }

    /// <summary>
    /// Elimina filas específicas de una hoja (números de fila 1-based).
    /// Procesa de mayor a menor para que el desplazamiento no afecte índices previos.
    /// </summary>
    public async Task EliminarFilasAsync(string hoja, IList<int> numFilas)
    {
        if (!EstaListo || _sheets == null || numFilas.Count == 0) return;

        // Obtener sheetId numérico de la hoja
        var spreadsheet = await _sheets.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        var sheetId = (int)(spreadsheet.Sheets
            .FirstOrDefault(s => s.Properties.Title.Equals(hoja, StringComparison.OrdinalIgnoreCase))
            ?.Properties.SheetId ?? 0);

        // Ordenar de abajo hacia arriba para no desplazar índices
        var ordenadas = numFilas.Distinct().OrderByDescending(r => r).ToList();

        var requests = ordenadas.Select(fila => new Request
        {
            DeleteDimension = new DeleteDimensionRequest
            {
                Range = new DimensionRange
                {
                    SheetId    = sheetId,
                    Dimension  = "ROWS",
                    StartIndex = fila - 1,   // API usa 0-based
                    EndIndex   = fila        // exclusive
                }
            }
        }).ToList();

        await EjecutarConManejoAsync(async () =>
        {
            await _sheets.Spreadsheets
                .BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, _spreadsheetId)
                .ExecuteAsync();
        });
    }

    /// <summary>Actualiza una sola fila dado su número (1-based).</summary>
    public async Task ActualizarFilaAsync(string hoja, int numFila, IList<object> valores)
    {
        var rango = $"{hoja}!A{numFila}:Z{numFila}";
        await ActualizarRangoAsync(rango, new List<IList<object>> { valores });
    }

    // ─── HOJAS ────────────────────────────────────────────────────────────────

    /// <summary>Obtiene los nombres de todas las hojas del spreadsheet.</summary>
    public async Task<List<string>> ObtenerNombresHojasAsync()
    {
        if (!EstaListo || _sheets == null) return new List<string>();
        var spreadsheet = await _sheets.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        return spreadsheet.Sheets.Select(s => s.Properties.Title).ToList();
    }

    /// <summary>Borra todo el contenido de una hoja (para restauración de backup).</summary>
    public async Task LimpiarHojaAsync(string hoja)
    {
        if (!EstaListo || _sheets == null) return;
        var body = new ClearValuesRequest();
        await _sheets.Spreadsheets.Values
            .Clear(body, _spreadsheetId, $"{hoja}!A:Z")
            .ExecuteAsync();
    }

    /// <summary>
    /// Actualiza el encabezado de una hoja y aplica formato profesional de tabla:
    /// - Fila 1 en negrita con color de fondo
    /// - Fila 1 fija (freeze)
    /// - Filtros automáticos en toda la tabla
    /// - Filas alternas con color de fondo suave
    /// </summary>
    public async Task FormatearHojaAsync(string hoja, IList<string> encabezados)
    {
        if (!EstaListo || _sheets == null) return;

        // 1. Actualizar los encabezados en la fila 1
        var headerRange = new ValueRange
        {
            Values = new List<IList<object>> { encabezados.Cast<object>().ToList() }
        };
        var updateReq = _sheets.Spreadsheets.Values
            .Update(headerRange, _spreadsheetId, $"{hoja}!A1");
        updateReq.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await EjecutarConManejoAsync(() => updateReq.ExecuteAsync());

        // 2. Obtener sheetId
        var spreadsheet = await _sheets.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        var sheetId = (int)(spreadsheet.Sheets
            .FirstOrDefault(s => s.Properties.Title.Equals(hoja, StringComparison.OrdinalIgnoreCase))
            ?.Properties.SheetId ?? 0);

        int numCols = encabezados.Count;

        // Color azul marino para el header
        var colorHeader = new Color { Red = 0.102f, Green = 0.227f, Blue = 0.365f, Alpha = 1f };
        // Color blanco para texto del header
        var colorBlanco = new Color { Red = 1f, Green = 1f, Blue = 1f, Alpha = 1f };
        // Color azul muy claro para filas alternas
        var colorAlterna = new Color { Red = 0.898f, Green = 0.937f, Blue = 0.980f, Alpha = 1f };

        var requests = new List<Request>
        {
            // ── Negrita + color de fondo en header (fila 1) ──
            new Request
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = new GridRange { SheetId = sheetId, StartRowIndex = 0, EndRowIndex = 1,
                                           StartColumnIndex = 0, EndColumnIndex = numCols },
                    Cell  = new CellData
                    {
                        UserEnteredFormat = new CellFormat
                        {
                            BackgroundColor  = colorHeader,
                            TextFormat       = new TextFormat { Bold = true, FontSize = 10, ForegroundColor = colorBlanco },
                            HorizontalAlignment = "CENTER",
                            VerticalAlignment   = "MIDDLE"
                        }
                    },
                    Fields = "userEnteredFormat(backgroundColor,textFormat,horizontalAlignment,verticalAlignment)"
                }
            },

            // ── Freeze fila 1 ──
            new Request
            {
                UpdateSheetProperties = new UpdateSheetPropertiesRequest
                {
                    Properties = new SheetProperties
                    {
                        SheetId      = sheetId,
                        GridProperties = new GridProperties { FrozenRowCount = 1 }
                    },
                    Fields = "gridProperties.frozenRowCount"
                }
            },

            // ── Filtros automáticos ──
            new Request
            {
                SetBasicFilter = new SetBasicFilterRequest
                {
                    Filter = new BasicFilter
                    {
                        Range = new GridRange
                        {
                            SheetId          = sheetId,
                            StartRowIndex    = 0,
                            StartColumnIndex = 0,
                            EndColumnIndex   = numCols
                        }
                    }
                }
            },

            // ── Filas alternas (banding) ──
            new Request
            {
                AddBanding = new AddBandingRequest
                {
                    BandedRange = new BandedRange
                    {
                        Range = new GridRange
                        {
                            SheetId          = sheetId,
                            StartRowIndex    = 0,
                            StartColumnIndex = 0,
                            EndColumnIndex   = numCols
                        },
                        RowProperties = new BandingProperties
                        {
                            HeaderColor      = colorHeader,
                            FirstBandColor   = new Color { Red = 1f, Green = 1f, Blue = 1f, Alpha = 1f },
                            SecondBandColor  = colorAlterna
                        }
                    }
                }
            },

            // ── Auto-resize columnas ──
            new Request
            {
                AutoResizeDimensions = new AutoResizeDimensionsRequest
                {
                    Dimensions = new DimensionRange
                    {
                        SheetId    = sheetId,
                        Dimension  = "COLUMNS",
                        StartIndex = 0,
                        EndIndex   = numCols
                    }
                }
            }
        };

        await EjecutarConManejoAsync(async () =>
        {
            await _sheets.Spreadsheets
                .BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, _spreadsheetId)
                .ExecuteAsync();
        });
    }

    /// <summary>Crea una hoja nueva si no existe. Ignora el error si ya existe.</summary>
    public async Task CrearHojaSiNoExisteAsync(string nombreHoja)
    {
        if (!EstaListo || _sheets == null) return;

        // Verificar primero — doble protección con try-catch por si hay race condition
        var hojas = await ObtenerNombresHojasAsync();
        if (hojas.Contains(nombreHoja, StringComparer.OrdinalIgnoreCase)) return;

        try
        {
            var batch = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>
                {
                    new Request
                    {
                        AddSheet = new AddSheetRequest
                        {
                            Properties = new SheetProperties { Title = nombreHoja }
                        }
                    }
                }
            };
            await _sheets.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
        }
        catch (Google.GoogleApiException ex)
            when (ex.Error?.Code == 400 &&
                  ex.Error.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // La hoja ya existe — no es un error, ignorar
        }
    }

    /// <summary>Inicializa las hojas maestras con encabezados si están vacías.</summary>
    public async Task InicializarHojasMaestrasAsync()
    {
        if (!EstaListo || _sheets == null) return;

        await InicializarHojaConEncabezadoAsync(HOJA_USUARIOS, new[]
        {
            "ID", "NombreCompleto", "NombreUsuario", "PasswordHash",
            "Correo", "Perfil", "Estado", "FechaCreacion"
        });

        await InicializarHojaConEncabezadoAsync(HOJA_COMPANIAS, new[]
        {
            "ID", "Nombre", "RNC", "Telefono", "Direccion",
            "Correo", "CondicionPagoDias", "Activo"
        });

        await InicializarHojaConEncabezadoAsync(HOJA_CLIENTES, new[]
        {
            "ID", "NombreCompleto", "Cedula", "Telefono", "Correo", "Activo"
        });

        await InicializarHojaConEncabezadoAsync(HOJA_PAGOS, new[]
        {
            "NumTransaccionPago", "NumTransaccionFactura", "NumTicket",
            "FechaPago", "MontoPagado", "FormaPago",
            "NumChequeReferencia", "Notas", "UsuarioRegistro", "FechaRegistro"
        });
    }

    private async Task InicializarHojaConEncabezadoAsync(string hoja, string[] encabezados)
    {
        await CrearHojaSiNoExisteAsync(hoja);
        var datos = await LeerAsync(hoja, "A1:Z1");
        if (datos.Count == 0)
        {
            await AgregarFilaAsync(hoja, encabezados.Cast<object>().ToList());
        }
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    public static string Str(IList<object> fila, int col)
        => fila.Count > col ? fila[col]?.ToString()?.Trim() ?? "" : "";

    public static decimal Dec(IList<object> fila, int col)
        => decimal.TryParse(Str(fila, col), out var v) ? v : 0m;

    public static int Int(IList<object> fila, int col)
        => int.TryParse(Str(fila, col), out var v) ? v : 0;

    public static DateTime? Fecha(IList<object> fila, int col)
        => DateTime.TryParse(Str(fila, col), out var v) ? v : null;

    public static bool Bool(IList<object> fila, int col)
    {
        var s = Str(fila, col).ToUpper();
        return s == "TRUE" || s == "1" || s == "SI" || s == "SÍ" || s == "ACTIVO";
    }
}
