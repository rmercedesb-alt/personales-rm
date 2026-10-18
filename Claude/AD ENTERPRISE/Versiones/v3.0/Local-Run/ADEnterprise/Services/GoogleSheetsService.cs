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

    /// <summary>
    /// Lee todas las filas de un rango.
    /// Intenta primero values.get (rápido). Si la API devuelve "Unable to parse range"
    /// (bug conocido de la API v4 con hojas que tienen guiones bajos: CUADRE_CAJA,
    /// INGRESOS_CAPITAL, CAJA, EGRESOS), hace fallback automático a spreadsheets.get
    /// con includeGridData que usa el título de hoja sin rango de celda.
    /// </summary>
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
        catch (Google.GoogleApiException ex)
            when (ex.HttpStatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // values.get falla con 400 BadRequest para hojas creadas programáticamente
            // (CAJA, CUADRE_CAJA, EGRESOS, INGRESOS_CAPITAL). Fallback: spreadsheets.get.
            UltimoError = null;
            return await LeerPorGridDataAsync(hoja);
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

    /// <summary>
    /// Fallback de lectura usando spreadsheets.get con includeGridData.
    /// Funciona con CUALQUIER nombre de hoja sin depender de rangos string.
    /// El parámetro ranges recibe solo el título de hoja (sin !A:Z).
    /// </summary>
    private async Task<IList<IList<object>>> LeerPorGridDataAsync(string hoja)
    {
        try
        {
            var req = _sheets!.Spreadsheets.Get(_spreadsheetId);
            req.Ranges       = new Google.Apis.Util.Repeatable<string>(new[] { hoja });
            req.IncludeGridData = true;

            var spreadsheet = await req.ExecuteAsync();
            var sheet = spreadsheet.Sheets?.FirstOrDefault(s =>
                string.Equals(s.Properties.Title, hoja, StringComparison.OrdinalIgnoreCase));

            if (sheet?.Data == null || sheet.Data.Count == 0)
                return new List<IList<object>>();

            var result = new List<IList<object>>();
            foreach (var gridData in sheet.Data)
            {
                if (gridData.RowData == null) continue;
                foreach (var row in gridData.RowData)
                {
                    if (row?.Values == null) continue;
                    var cells = row.Values
                        .Select(c => (object)(c.FormattedValue ?? ""))
                        .ToList();
                    result.Add(cells);
                }
            }

            // Quitar filas vacías al final (Google Sheets las incluye a veces)
            while (result.Count > 0 &&
                   result[^1].All(v => string.IsNullOrEmpty(v?.ToString())))
                result.RemoveAt(result.Count - 1);

            UltimoError = null;
            return result;
        }
        catch (Google.GoogleApiException ex)
        {
            UltimoError = $"Error Google Sheets (fallback): {ex.Error?.Message ?? ex.Message}";
            return new List<IList<object>>();
        }
        catch (Exception ex)
        {
            UltimoError = $"Error inesperado (fallback): {ex.Message}";
            return new List<IList<object>>();
        }
    }

    /// <summary>Último error de red o API, null si todo está bien.</summary>
    public string? UltimoError { get; private set; }

    // ─── ESCRITURA ────────────────────────────────────────────────────────────
    // IMPORTANTE: NO usamos values.append ni values.update con nombre de hoja en
    // el rango, porque la API v4 falla con "Unable to parse range" en hojas cuyos
    // nombres contienen guiones bajos (CUADRE_CAJA, INGRESOS_CAPITAL, etc.).
    // Usamos batchUpdate → AppendCells que opera con sheetId NUMÉRICO, sin rangos.

    // Caché de sheetId numérico para evitar múltiples llamadas al metadata
    private readonly Dictionary<string, int> _sheetIdCache = new();

    /// <summary>Obtiene el sheetId numérico de una hoja. Usa caché interno.</summary>
    private async Task<int?> ObtenerSheetIdAsync(string hoja)
    {
        if (_sheetIdCache.TryGetValue(hoja, out var cached)) return cached;
        var meta = await _sheets!.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        foreach (var s in meta.Sheets)
            _sheetIdCache[s.Properties.Title] = (int)(s.Properties.SheetId ?? 0);
        return _sheetIdCache.TryGetValue(hoja, out var id) ? id : (int?)null;
    }

    /// <summary>Convierte un valor object a CellData para AppendCells.</summary>
    private static CellData ToCellData(object? val) => new()
    {
        UserEnteredValue = new ExtendedValue { StringValue = val?.ToString() ?? "" }
    };

    /// <summary>
    /// Agrega una fila al final de la hoja usando batchUpdate → AppendCells.
    /// Opera con sheetId numérico para evitar problemas de parseo de nombres con _.
    /// Incluye SetBasicFilter en el mismo batch para que el auto-filtro siempre
    /// cubra todas las filas existentes (EndRowIndex = null = sin límite).
    /// </summary>
    public async Task AgregarFilaAsync(string hoja, IList<object> valores)
    {
        if (!EstaListo || _sheets == null) return;
        await CrearHojaSiNoExisteAsync(hoja);
        _sheetIdCache.Remove(hoja); // invalidar caché tras posible creación
        var sheetId = await ObtenerSheetIdAsync(hoja);
        if (sheetId == null) return;

        int numCols = Math.Max(valores.Count, 26); // mínimo A–Z para filtros futuros

        var req = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                // 1. Agregar la fila de datos
                new() { AppendCells = new AppendCellsRequest
                {
                    SheetId = sheetId,
                    Rows    = new List<RowData>
                    {
                        new() { Values = valores.Select(ToCellData).ToList() }
                    },
                    Fields  = "userEnteredValue"
                }},
                // 2. Refrescar el auto-filtro para cubrir todas las filas
                //    EndRowIndex = null → sin límite de filas (cubre filas nuevas automáticamente)
                new() { SetBasicFilter = new SetBasicFilterRequest
                {
                    Filter = new BasicFilter
                    {
                        Range = new GridRange
                        {
                            SheetId          = sheetId,
                            StartRowIndex    = 0,
                            StartColumnIndex = 0,
                            EndColumnIndex   = numCols
                            // EndRowIndex no se asigna (null) = rango sin límite de filas
                        }
                    }
                }}
            }
        };
        await EjecutarConManejoAsync(() =>
            _sheets.Spreadsheets.BatchUpdate(req, _spreadsheetId).ExecuteAsync());
    }

    /// <summary>
    /// Agrega múltiples filas usando batchUpdate → AppendCells (una sola llamada).
    /// Incluye SetBasicFilter en el mismo batch para mantener el auto-filtro actualizado.
    /// </summary>
    public async Task AgregarMultiplesFilasAsync(string hoja, IList<IList<object>> filas)
    {
        if (!EstaListo || _sheets == null || filas.Count == 0) return;
        await CrearHojaSiNoExisteAsync(hoja);
        _sheetIdCache.Remove(hoja);
        var sheetId = await ObtenerSheetIdAsync(hoja);
        if (sheetId == null) return;

        int numCols = Math.Max(filas.Max(f => f.Count), 26);

        var rows = filas.Select(fila =>
            new RowData { Values = fila.Select(ToCellData).ToList() }
        ).ToList();

        var req = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                // 1. Agregar todas las filas de datos
                new() { AppendCells = new AppendCellsRequest
                {
                    SheetId = sheetId,
                    Rows    = rows,
                    Fields  = "userEnteredValue"
                }},
                // 2. Refrescar el auto-filtro
                new() { SetBasicFilter = new SetBasicFilterRequest
                {
                    Filter = new BasicFilter
                    {
                        Range = new GridRange
                        {
                            SheetId          = sheetId,
                            StartRowIndex    = 0,
                            StartColumnIndex = 0,
                            EndColumnIndex   = numCols
                            // EndRowIndex no se asigna (null) = rango sin límite de filas
                        }
                    }
                }}
            }
        };
        await EjecutarConManejoAsync(() =>
            _sheets.Spreadsheets.BatchUpdate(req, _spreadsheetId).ExecuteAsync());
    }

    /// <summary>
    /// Actualiza un rango específico (ej: "COMPANIAS!A5:H5" o "CUADRE_CAJA!B3").
    /// Si values.update falla con "Unable to parse range" (hojas con guiones bajos),
    /// hace fallback a batchUpdate → updateCells usando sheetId numérico.
    /// </summary>
    public async Task ActualizarRangoAsync(string rango, IList<IList<object>> valores)
    {
        if (!EstaListo || _sheets == null) return;
        try
        {
            var body = new ValueRange { Values = valores };
            var req  = _sheets.Spreadsheets.Values
                .Update(body, _spreadsheetId, rango);
            req.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await EjecutarConManejoAsync(() => req.ExecuteAsync());
        }
        catch (Google.GoogleApiException ex)
            when (ex.HttpStatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // values.update falla con 400 para hojas creadas programáticamente. Fallback.
            await ActualizarRangoConGridAsync(rango, valores);
        }
    }

    /// <summary>
    /// Fallback de actualización: parsea "HOJA!ColFila" y usa batchUpdate → updateCells
    /// con sheetId numérico. Soporta celdas únicas ("CUADRE_CAJA!B3") y rangos de fila
    /// ("CUADRE_CAJA!A3:Z3").
    /// </summary>
    private async Task ActualizarRangoConGridAsync(string rango, IList<IList<object>> valores)
    {
        // Parsear "HOJA!ColFila" → hoja, col0, row0
        var bang = rango.IndexOf('!');
        if (bang < 0) return;
        var hoja      = rango[..bang];
        var celda     = rango[(bang + 1)..]; // "B3" ó "A3:Z3"

        // Extraer columna inicial y fila inicial
        var colon     = celda.IndexOf(':');
        var parte1    = colon >= 0 ? celda[..colon] : celda;
        int col0      = ColLetraAIndice(parte1.TrimEnd('0','1','2','3','4','5','6','7','8','9'));
        int row0      = int.TryParse(new string(parte1.Where(char.IsDigit).ToArray()), out var r) ? r - 1 : 0;

        var sheetId   = await ObtenerSheetIdAsync(hoja);
        if (sheetId == null) return;

        var rows = valores.Select((rowVals, ri) => new RowData
        {
            Values = rowVals.Select((v, ci) => new CellData
            {
                UserEnteredValue = new ExtendedValue { StringValue = v?.ToString() ?? "" }
            }).ToList()
        }).ToList();

        var req = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new() { UpdateCells = new UpdateCellsRequest
                {
                    Rows   = rows,
                    Fields = "userEnteredValue",
                    Start  = new GridCoordinate
                    {
                        SheetId     = sheetId,
                        RowIndex    = row0,
                        ColumnIndex = col0
                    }
                }}
            }
        };
        await EjecutarConManejoAsync(() =>
            _sheets!.Spreadsheets.BatchUpdate(req, _spreadsheetId).ExecuteAsync());
    }

    /// <summary>Convierte letra(s) de columna a índice 0-based. "A"→0, "B"→1, "Z"→25, "AA"→26.</summary>
    private static int ColLetraAIndice(string col)
    {
        col = col.ToUpper().Trim();
        int result = 0;
        foreach (char c in col)
            result = result * 26 + (c - 'A' + 1);
        return result - 1;
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

    /// <summary>Actualiza una sola celda dado su número de fila y columna letra (A, B, C…).</summary>
    public async Task ActualizarCeldaAsync(string hoja, int numFila, string columna, string valor)
    {
        var rango = $"{hoja}!{columna}{numFila}";
        await ActualizarRangoAsync(rango,
            new List<IList<object>> { new List<object> { valor } });
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
            // EndRowIndex no asignado (null) = sin límite de filas → cubre todos los datos futuros
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
                            // EndRowIndex = null → ilimitado
                        }
                    }
                }
            },

            // ── Filas alternas (banding) ──
            // EndRowIndex no asignado (null) = sin límite de filas
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
                            // EndRowIndex = null → ilimitado
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

    /// <summary>Renombra una hoja existente. No hace nada si no existe.</summary>
    public async Task RenombrarHojaAsync(string nombreActual, string nombreNuevo)
    {
        if (!EstaListo || _sheets == null) return;
        var spreadsheet = await _sheets.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
        var hoja = spreadsheet.Sheets
            .FirstOrDefault(s => s.Properties.Title.Equals(nombreActual, StringComparison.OrdinalIgnoreCase));
        if (hoja == null) return;

        var batch = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new Request
                {
                    UpdateSheetProperties = new UpdateSheetPropertiesRequest
                    {
                        Properties = new SheetProperties
                        {
                            SheetId = (int)hoja.Properties.SheetId!,
                            Title   = nombreNuevo
                        },
                        Fields = "title"
                    }
                }
            }
        };
        await _sheets.Spreadsheets.BatchUpdate(batch, _spreadsheetId).ExecuteAsync();
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
