using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using InmueblesAPI.Models;

namespace InmueblesAPI.Services;

public class GoogleSheetsService
{
    private readonly SheetsService _service;
    private readonly string _spreadsheetId;

    // Tab names matching the GAS system
    public const string TAB_PROPIETARIOS  = "👤 PROPIETARIOS";
    public const string TAB_INMUEBLES     = "🏢 INMUEBLES";
    public const string TAB_INQUILINOS    = "👥 INQUILINOS";
    public const string TAB_CONTRATOS     = "📋 CONTRATOS";
    public const string TAB_CXC_CARGOS   = "💰 CXC_CARGOS";
    public const string TAB_CXC_PAGOS    = "💳 CXC_PAGOS";
    public const string TAB_SESIONES     = "🏧 SESIONES_CAJA";
    public const string TAB_LIQUIDACIONES = "🔄 LIQUIDACIONES";
    public const string TAB_LOG_CUSTODIA  = "📊 LOG_CUSTODIA";
    public const string TAB_ADELANTOS        = "💸 ADELANTOS";
    public const string TAB_CO_PROPIETARIOS  = "👨‍👩‍👧 CO_PROPIETARIOS";
    public const string TAB_ABONOS_CUENTA    = "💵 ABONOS_CUENTA";
    public const string TAB_NOTIFICACIONES = "📢 NOTIFICACIONES";
    public const string TAB_CONFIG       = "⚙ CONFIG";
    public const string TAB_ROLES        = "🔒 ROLES";

    public GoogleSheetsService(IConfiguration config)
    {
        _spreadsheetId = config["GoogleSheets:SpreadsheetId"]!;
        var credPath = config["GoogleSheets:CredentialsPath"]!;

        GoogleCredential credential;
        using var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read);
        credential = GoogleCredential.FromStream(stream)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "InmueblesRD"
        });
    }

    // ─── Generic read ───────────────────────────────────────────────────────
    private HashSet<string>? _tabNamesCache;

    private async Task EnsureTabExistsAsync(string tab, IList<object>? headerRow = null)
    {
        if (_tabNamesCache == null)
        {
            var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
            _tabNamesCache = spreadsheet.Sheets.Select(s => s.Properties.Title).ToHashSet();
        }
        if (_tabNamesCache.Contains(tab)) return;

        var addSheetReq = new Request
        {
            AddSheet = new AddSheetRequest { Properties = new SheetProperties { Title = tab } }
        };
        await _service.Spreadsheets.BatchUpdate(
            new BatchUpdateSpreadsheetRequest { Requests = new List<Request> { addSheetReq } },
            _spreadsheetId).ExecuteAsync();
        _tabNamesCache.Add(tab);

        if (headerRow != null)
            await AppendRowAsync(tab, headerRow, skipEnsure: true);
    }

    public async Task<IList<IList<object>>> ReadRangeAsync(string range)
    {
        var tab = range.Split('!')[0];
        await EnsureTabExistsAsync(tab);
        var req = _service.Spreadsheets.Values.Get(_spreadsheetId, range);
        // UNFORMATTED_VALUE devuelve el número real aunque la celda esté formateada como Fecha
        req.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        var resp = await req.ExecuteAsync();
        return resp.Values ?? new List<IList<object>>();
    }

    public async Task AppendRowAsync(string tab, IList<object> values, bool skipEnsure = false)
    {
        if (!skipEnsure) await EnsureTabExistsAsync(tab);
        var body = new ValueRange { Values = new List<IList<object>> { values } };
        var req = _service.Spreadsheets.Values.Append(body, _spreadsheetId, $"{tab}!A:A");
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    public async Task UpdateCellAsync(string tab, int row, int col, object value)
    {
        var cellRef = CellAddress(col, row);
        var body = new ValueRange { Values = new List<IList<object>> { new List<object> { value } } };
        var req = _service.Spreadsheets.Values.Update(body, _spreadsheetId, $"{tab}!{cellRef}");
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    public async Task UpdateRowAsync(string tab, int row, IList<object> values)
    {
        var range = $"{tab}!A{row}";
        var body = new ValueRange { Values = new List<IList<object>> { values } };
        var req = _service.Spreadsheets.Values.Update(body, _spreadsheetId, range);
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await req.ExecuteAsync();
    }

    public async Task BatchUpdateAsync(IList<ValueRange> updates)
    {
        var body = new BatchUpdateValuesRequest
        {
            Data = updates,
            ValueInputOption = "USER_ENTERED"
        };
        await _service.Spreadsheets.Values.BatchUpdate(body, _spreadsheetId).ExecuteAsync();
    }

    // ─── Find row by column A value ─────────────────────────────────────────
    public async Task<(int rowIndex, IList<object> row)?> FindRowByIdAsync(string tab, string id)
    {
        var data = await ReadRangeAsync($"{tab}!A:AZ");
        for (int i = 1; i < data.Count; i++)
        {
            if (data[i].Count > 0 && data[i][0]?.ToString() == id)
                return (i + 1, data[i]); // 1-based row
        }
        return null;
    }

    // ─── CONFIG helpers ──────────────────────────────────────────────────────
    public async Task<string?> GetConfigAsync(string clave)
    {
        var data = await ReadRangeAsync($"{TAB_CONFIG}!A:B");
        foreach (var row in data.Skip(1))
        {
            if (row.Count > 1 && row[0]?.ToString() == clave)
                return row[1]?.ToString();
        }
        return null;
    }

    public async Task SetConfigAsync(string clave, string valor)
    {
        var data = await ReadRangeAsync($"{TAB_CONFIG}!A:B");
        for (int i = 1; i < data.Count; i++)
        {
            if (data[i].Count > 0 && data[i][0]?.ToString() == clave)
            {
                await UpdateCellAsync(TAB_CONFIG, i + 1, 2, valor);
                return;
            }
        }
        await AppendRowAsync(TAB_CONFIG, new List<object> { clave, valor });
    }

    // ─── ID generation ──────────────────────────────────────────────────────
    public async Task<string> GenerarIdUnicoAsync(string prefijo, string tab)
    {
        var data = await ReadRangeAsync($"{tab}!A:A");
        int max = 0;
        foreach (var row in data.Skip(1))
        {
            var cell = row.Count > 0 ? row[0]?.ToString() : null;
            if (cell != null && cell.StartsWith(prefijo + "-"))
            {
                var parts = cell.Split('-');
                if (int.TryParse(parts.Last(), out int n) && n > max) max = n;
            }
        }
        int next = max + 1;
        return $"{prefijo}-{next.ToString().PadLeft(next > 999 ? 4 : 3, '0')}";
    }

    // ─── Users (ROLES tab) ──────────────────────────────────────────────────
    public async Task<UsuarioSistema?> GetUsuarioAsync(string email)
    {
        var data = await ReadRangeAsync($"{TAB_ROLES}!A:F");
        foreach (var row in data.Skip(1))
        {
            if (row.Count >= 4 && row[0]?.ToString()?.Equals(email, StringComparison.OrdinalIgnoreCase) == true)
            {
                return new UsuarioSistema
                {
                    Email        = row[0]?.ToString() ?? "",
                    Nombre       = row[1]?.ToString() ?? "",
                    Rol          = row[2]?.ToString() ?? "",
                    Estado       = row[3]?.ToString() ?? "SI",
                    PasswordHash = row.Count > 4 ? row[4]?.ToString() ?? "" : ""
                };
            }
        }
        return null;
    }

    public async Task SetPasswordHashAsync(string email, string hash)
    {
        var data = await ReadRangeAsync($"{TAB_ROLES}!A:F");
        for (int i = 1; i < data.Count; i++)
        {
            if (data[i].Count > 0 && data[i][0]?.ToString()?.Equals(email, StringComparison.OrdinalIgnoreCase) == true)
            {
                await UpdateCellAsync(TAB_ROLES, i + 1, 5, hash);
                return;
            }
        }
    }

    public async Task<List<UsuarioSistema>> GetUsuariosAsync()
    {
        var data = await ReadRangeAsync($"{TAB_ROLES}!A:F");
        return data.Skip(1)
            .Where(r => r.Count >= 1 && r[0]?.ToString() != "")
            .Select(r => new UsuarioSistema
            {
                Email        = r[0]?.ToString() ?? "",
                Nombre       = r.Count > 1 ? r[1]?.ToString() ?? "" : "",
                Rol          = r.Count > 2 ? r[2]?.ToString() ?? "" : "",
                Estado       = r.Count > 3 ? r[3]?.ToString() ?? "SI" : "SI",
                PasswordHash = r.Count > 4 ? r[4]?.ToString() ?? "" : ""
            }).ToList();
    }

    public async Task CrearUsuarioAsync(string email, string nombre, string rol, string activo, string passwordHash)
    {
        await AppendRowAsync(TAB_ROLES, new List<object>
        {
            email, nombre, rol, activo, passwordHash, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    public async Task ActualizarUsuarioAsync(string email, string nombre, string rol, string activo, string? newHash)
    {
        var data = await ReadRangeAsync($"{TAB_ROLES}!A:F");
        for (int i = 1; i < data.Count; i++)
        {
            if (data[i].Count > 0 && data[i][0]?.ToString()?.Equals(email, StringComparison.OrdinalIgnoreCase) == true)
            {
                var existingHash = data[i].Count > 4 ? data[i][4]?.ToString() ?? "" : "";
                var row = i + 1;
                var values = new List<object>
                {
                    email, nombre, rol, activo,
                    !string.IsNullOrEmpty(newHash) ? newHash : existingHash,
                    data[i].Count > 5 ? data[i][5]?.ToString() ?? "" : ""
                };
                await UpdateRowAsync(TAB_ROLES, row, values);
                return;
            }
        }
    }

    // ─── PROPIETARIOS ────────────────────────────────────────────────────────
    public async Task<List<Propietario>> GetPropietariosAsync()
    {
        var data = await ReadRangeAsync($"{TAB_PROPIETARIOS}!A:P");
        return data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapPropietario).ToList();
    }

    public async Task<Propietario?> GetPropietarioAsync(string id)
    {
        var result = await FindRowByIdAsync(TAB_PROPIETARIOS, id);
        return result.HasValue ? MapPropietario(result.Value.row) : null;
    }

    public async Task<Propietario> CrearPropietarioAsync(CrearPropietarioRequest req)
    {
        var id = await GenerarIdUnicoAsync("PROP", TAB_PROPIETARIOS);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_PROPIETARIOS, new List<object>
        {
            id, req.Nombre, req.Cedula, req.Telefono, req.Email,
            req.Banco, req.NumeroCuenta, req.TipoCuenta,
            req.TipoComision, req.ComisionPorcentaje, req.ComisionFija,
            "ACTIVO", req.Notas, now, req.TipoDueno
        });
        return (await GetPropietarioAsync(id))!;
    }

    private static Propietario MapPropietario(IList<object> r) => new()
    {
        Id = r.Get(0), Nombre = r.Get(1), Cedula = r.Get(2),
        Telefono = r.Get(3), Email = r.Get(4), Banco = r.Get(5),
        NumeroCuenta = r.Get(6), TipoCuenta = r.Get(7),
        TipoComision = r.Get(8), ComisionPorcentaje = r.GetDecimal(9),
        ComisionFija = r.GetDecimal(10), Estado = r.Get(11),
        Notas = r.Get(12), FechaRegistro = r.GetDate(13),
        TipoDueno = string.IsNullOrEmpty(r.Get(14)) ? "ÚNICO" : r.Get(14)
    };

    public async Task ActualizarPropietarioAsync(string id, ActualizarPropietarioRequest req)
    {
        var result = await FindRowByIdAsync(TAB_PROPIETARIOS, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        await BatchUpdateAsync(new List<ValueRange> {
            MakeUpdate($"{TAB_PROPIETARIOS}!B{row}", req.Nombre),
            MakeUpdate($"{TAB_PROPIETARIOS}!C{row}", req.Cedula),
            MakeUpdate($"{TAB_PROPIETARIOS}!D{row}", req.Telefono),
            MakeUpdate($"{TAB_PROPIETARIOS}!E{row}", req.Email),
            MakeUpdate($"{TAB_PROPIETARIOS}!F{row}", req.Banco),
            MakeUpdate($"{TAB_PROPIETARIOS}!G{row}", req.NumeroCuenta),
            MakeUpdate($"{TAB_PROPIETARIOS}!H{row}", req.TipoCuenta),
            MakeUpdate($"{TAB_PROPIETARIOS}!I{row}", req.TipoComision),
            MakeUpdate($"{TAB_PROPIETARIOS}!J{row}", req.ComisionPorcentaje),
            MakeUpdate($"{TAB_PROPIETARIOS}!K{row}", req.ComisionFija),
            MakeUpdate($"{TAB_PROPIETARIOS}!L{row}", req.Estado),
            MakeUpdate($"{TAB_PROPIETARIOS}!M{row}", req.Notas),
            MakeUpdate($"{TAB_PROPIETARIOS}!O{row}", req.TipoDueno),
        });
    }

    // ─── CO-PROPIETARIOS ─────────────────────────────────────────────────────
    private static readonly List<object> HeaderCoPropietarios = new()
    {
        "Id", "IdPropietario", "Nombre", "Cedula", "Telefono", "Email",
        "Banco", "NumeroCuenta", "TipoCuenta", "PesoPorc", "Estado", "Notas", "FechaRegistro"
    };

    public async Task<List<CoPropietario>> GetCoPropietariosAsync(string? idPropietario = null)
    {
        await EnsureTabExistsAsync(TAB_CO_PROPIETARIOS, HeaderCoPropietarios);
        var data = await ReadRangeAsync($"{TAB_CO_PROPIETARIOS}!A:N");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapCoPropietario).ToList();
        return idPropietario != null ? list.Where(c => c.IdPropietario == idPropietario).ToList() : list;
    }

    public async Task<CoPropietario> CrearCoPropietarioAsync(CrearCoPropietarioRequest req)
    {
        var id = await GenerarIdUnicoAsync("COPROP", TAB_CO_PROPIETARIOS);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_CO_PROPIETARIOS, new List<object>
        {
            id, req.IdPropietario, req.Nombre, req.Cedula, req.Telefono, req.Email,
            req.Banco, req.NumeroCuenta, req.TipoCuenta,
            req.PesoPorc, "ACTIVO", req.Notas, now
        });
        return (await GetCoPropietariosAsync()).First(c => c.Id == id);
    }

    public async Task ActualizarCoPropietarioAsync(string id, ActualizarCoPropietarioRequest req)
    {
        var result = await FindRowByIdAsync(TAB_CO_PROPIETARIOS, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!C{row}", req.Nombre),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!D{row}", req.Cedula),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!E{row}", req.Telefono),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!F{row}", req.Email),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!G{row}", req.Banco),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!H{row}", req.NumeroCuenta),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!I{row}", req.TipoCuenta),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!J{row}", req.PesoPorc),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!K{row}", req.Estado),
            MakeUpdate($"{TAB_CO_PROPIETARIOS}!L{row}", req.Notas),
        });
    }

    public async Task EliminarCoPropietarioAsync(string id)
    {
        var result = await FindRowByIdAsync(TAB_CO_PROPIETARIOS, id);
        if (!result.HasValue) return;
        await UpdateCellAsync(TAB_CO_PROPIETARIOS, result.Value.rowIndex, 11, "INACTIVO");
    }

    private static CoPropietario MapCoPropietario(IList<object> r) => new()
    {
        Id = r.Get(0), IdPropietario = r.Get(1), Nombre = r.Get(2),
        Cedula = r.Get(3), Telefono = r.Get(4), Email = r.Get(5),
        Banco = r.Get(6), NumeroCuenta = r.Get(7), TipoCuenta = r.Get(8),
        PesoPorc = r.GetDecimal(9), Estado = r.Get(10),
        Notas = r.Get(11), FechaRegistro = r.GetDate(12)
    };

    // ─── INMUEBLES ───────────────────────────────────────────────────────────
    public async Task<List<Inmueble>> GetInmueblesAsync(bool soloDisponibles = false)
    {
        var data = await ReadRangeAsync($"{TAB_INMUEBLES}!A:T");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapInmueble).ToList();
        return soloDisponibles ? list.Where(i => i.Estado == "DISPONIBLE").ToList() : list;
    }

    public async Task<Inmueble?> GetInmuebleAsync(string id)
    {
        var result = await FindRowByIdAsync(TAB_INMUEBLES, id);
        return result.HasValue ? MapInmueble(result.Value.row) : null;
    }

    public async Task<Inmueble> CrearInmuebleAsync(CrearInmuebleRequest req)
    {
        var id = await GenerarIdUnicoAsync("INM", TAB_INMUEBLES);
        var propietario = await GetPropietarioAsync(req.IdPropietario);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_INMUEBLES, new List<object>
        {
            id, req.IdPropietario, propietario?.Nombre ?? "",
            req.Tipo, req.Descripcion, req.Direccion, req.Sector, req.Ciudad,
            req.Latitud, req.Longitud, req.MontoAlquiler,
            "DISPONIBLE", "VERDE", req.Notas, now
        });
        return (await GetInmuebleAsync(id))!;
    }

    public async Task ActualizarEstadoInmuebleAsync(string id, string estado)
    {
        var result = await FindRowByIdAsync(TAB_INMUEBLES, id);
        if (result.HasValue) await UpdateCellAsync(TAB_INMUEBLES, result.Value.rowIndex, 12, estado);
    }

    public async Task ActualizarInmuebleAsync(string id, ActualizarInmuebleRequest req)
    {
        var result = await FindRowByIdAsync(TAB_INMUEBLES, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var propietario = await GetPropietarioAsync(req.IdPropietario);
        await BatchUpdateAsync(new List<ValueRange> {
            MakeUpdate($"{TAB_INMUEBLES}!B{row}", req.IdPropietario),
            MakeUpdate($"{TAB_INMUEBLES}!C{row}", propietario?.Nombre ?? ""),
            MakeUpdate($"{TAB_INMUEBLES}!D{row}", req.Tipo),
            MakeUpdate($"{TAB_INMUEBLES}!E{row}", req.Descripcion),
            MakeUpdate($"{TAB_INMUEBLES}!F{row}", req.Direccion),
            MakeUpdate($"{TAB_INMUEBLES}!G{row}", req.Sector),
            MakeUpdate($"{TAB_INMUEBLES}!H{row}", req.Ciudad),
            MakeUpdate($"{TAB_INMUEBLES}!I{row}", req.Latitud),
            MakeUpdate($"{TAB_INMUEBLES}!J{row}", req.Longitud),
            MakeUpdate($"{TAB_INMUEBLES}!K{row}", req.MontoAlquiler),
            MakeUpdate($"{TAB_INMUEBLES}!N{row}", req.Notas),
        });
    }

    public async Task ActualizarMorosidadInmuebleAsync(string id, string nivel)
    {
        var result = await FindRowByIdAsync(TAB_INMUEBLES, id);
        if (result.HasValue) await UpdateCellAsync(TAB_INMUEBLES, result.Value.rowIndex, 13, nivel);
    }

    private static Inmueble MapInmueble(IList<object> r) => new()
    {
        Id = r.Get(0), IdPropietario = r.Get(1), NombrePropietario = r.Get(2),
        Tipo = r.Get(3), Descripcion = r.Get(4), Direccion = r.Get(5),
        Sector = r.Get(6), Ciudad = r.Get(7),
        Latitud = r.GetDecimal(8), Longitud = r.GetDecimal(9),
        MontoAlquiler = r.GetDecimal(10), Estado = r.Get(11),
        NivelMorosidad = r.Get(12), Notas = r.Get(13), FechaRegistro = r.GetDate(14)
    };

    // ─── INQUILINOS ──────────────────────────────────────────────────────────
    public async Task<List<Inquilino>> GetInquilinosAsync()
    {
        var data = await ReadRangeAsync($"{TAB_INQUILINOS}!A:L");
        return data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapInquilino).ToList();
    }

    public async Task<Inquilino?> GetInquilinoAsync(string id)
    {
        var result = await FindRowByIdAsync(TAB_INQUILINOS, id);
        return result.HasValue ? MapInquilino(result.Value.row) : null;
    }

    public async Task<Inquilino> CrearInquilinoAsync(CrearInquilinoRequest req)
    {
        var id = await GenerarIdUnicoAsync("INQ", TAB_INQUILINOS);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_INQUILINOS, new List<object>
        {
            id, req.Nombre, req.Cedula, req.Telefono, req.Email,
            req.Ocupacion, req.ReferenciaPersonal, req.TelefonoReferencia,
            "ACTIVO", req.Notas, now
        });
        return (await GetInquilinoAsync(id))!;
    }

    private static Inquilino MapInquilino(IList<object> r) => new()
    {
        Id = r.Get(0), Nombre = r.Get(1), Cedula = r.Get(2),
        Telefono = r.Get(3), Email = r.Get(4), Ocupacion = r.Get(5),
        ReferenciaPersonal = r.Get(6), TelefonoReferencia = r.Get(7),
        Estado = r.Get(8), Notas = r.Get(9), FechaRegistro = r.GetDate(10)
    };

    public async Task ActualizarInquilinoAsync(string id, ActualizarInquilinoRequest req)
    {
        var result = await FindRowByIdAsync(TAB_INQUILINOS, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        await BatchUpdateAsync(new List<ValueRange> {
            MakeUpdate($"{TAB_INQUILINOS}!B{row}", req.Nombre),
            MakeUpdate($"{TAB_INQUILINOS}!C{row}", req.Cedula),
            MakeUpdate($"{TAB_INQUILINOS}!D{row}", req.Telefono),
            MakeUpdate($"{TAB_INQUILINOS}!E{row}", req.Email),
            MakeUpdate($"{TAB_INQUILINOS}!F{row}", req.Ocupacion),
            MakeUpdate($"{TAB_INQUILINOS}!G{row}", req.ReferenciaPersonal),
            MakeUpdate($"{TAB_INQUILINOS}!H{row}", req.TelefonoReferencia),
            MakeUpdate($"{TAB_INQUILINOS}!I{row}", req.Estado),
            MakeUpdate($"{TAB_INQUILINOS}!J{row}", req.Notas),
        });
    }

    // ─── CONTRATOS ───────────────────────────────────────────────────────────
    public async Task<List<Contrato>> GetContratosAsync(bool soloVigentes = false)
    {
        var data = await ReadRangeAsync($"{TAB_CONTRATOS}!A:X");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapContrato).ToList();
        return soloVigentes ? list.Where(c => c.Estado == "VIGENTE").ToList() : list;
    }

    public async Task<Contrato?> GetContratoAsync(string id)
    {
        var result = await FindRowByIdAsync(TAB_CONTRATOS, id);
        return result.HasValue ? MapContrato(result.Value.row) : null;
    }

    public async Task<Contrato> CrearContratoAsync(CrearContratoRequest req)
    {
        var id = await GenerarIdUnicoAsync("CTR", TAB_CONTRATOS);
        var inmueble = await GetInmuebleAsync(req.IdInmueble);
        var inquilino = await GetInquilinoAsync(req.IdInquilino);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_CONTRATOS, new List<object>
        {
            id, req.IdInmueble, inmueble?.Descripcion ?? "",
            req.IdInquilino, inquilino?.Nombre ?? "", inmueble?.IdPropietario ?? "",
            req.FechaInicio.ToString("yyyy-MM-dd"), req.FechaFin.ToString("yyyy-MM-dd"),
            req.MontoAlquiler, req.Periodicidad, req.DiaCobro, req.DiasGracia,
            req.TipoMora, req.MoraPorcentaje, req.MontoMoraFija,
            req.Deposito, "VIGENTE", req.Notas, now
        });
        await ActualizarEstadoInmuebleAsync(req.IdInmueble, "OCUPADO");
        return (await GetContratoAsync(id))!;
    }

    public async Task ActualizarContratoAsync(string id, ActualizarContratoRequest req)
    {
        var result = await FindRowByIdAsync(TAB_CONTRATOS, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var estadoAnterior = MapContrato(result.Value.row).Estado;
        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_CONTRATOS}!H{row}", req.FechaFin.ToString("yyyy-MM-dd")),
            MakeUpdate($"{TAB_CONTRATOS}!I{row}", req.MontoAlquiler),
            MakeUpdate($"{TAB_CONTRATOS}!J{row}", req.Periodicidad),
            MakeUpdate($"{TAB_CONTRATOS}!K{row}", req.DiaCobro),
            MakeUpdate($"{TAB_CONTRATOS}!L{row}", req.DiasGracia),
            MakeUpdate($"{TAB_CONTRATOS}!M{row}", req.TipoMora),
            MakeUpdate($"{TAB_CONTRATOS}!N{row}", req.MoraPorcentaje),
            MakeUpdate($"{TAB_CONTRATOS}!O{row}", req.MontoMoraFija),
            MakeUpdate($"{TAB_CONTRATOS}!P{row}", req.Deposito),
            MakeUpdate($"{TAB_CONTRATOS}!Q{row}", req.Estado),
            MakeUpdate($"{TAB_CONTRATOS}!R{row}", req.Notas),
        });
        if (estadoAnterior == "VIGENTE" && req.Estado != "VIGENTE")
        {
            var contrato = await GetContratoAsync(id);
            if (contrato != null) await ActualizarEstadoInmuebleAsync(contrato.IdInmueble, "DISPONIBLE");
        }
    }

    private static Contrato MapContrato(IList<object> r) => new()
    {
        Id = r.Get(0), IdInmueble = r.Get(1), NombreInmueble = r.Get(2),
        IdInquilino = r.Get(3), NombreInquilino = r.Get(4), IdPropietario = r.Get(5),
        FechaInicio = r.GetDate(6), FechaFin = r.GetDate(7),
        MontoAlquiler = r.GetDecimal(8), Periodicidad = r.Get(9),
        DiaCobro = r.GetInt(10), DiasGracia = r.GetInt(11),
        TipoMora = r.Get(12), MoraPorcentaje = r.GetDecimal(13),
        MontoMoraFija = r.GetDecimal(14), Deposito = r.GetDecimal(15),
        Estado = r.Get(16), Notas = r.Get(17), FechaRegistro = r.GetDate(18)
    };

    // ─── CXC_CARGOS ──────────────────────────────────────────────────────────
    public async Task<List<CxCCargo>> GetCxCCargosAsync(string? idInquilino = null, string? estado = null)
    {
        var data = await ReadRangeAsync($"{TAB_CXC_CARGOS}!A:T");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapCxC).ToList();
        await EnriquecerMoraAsync(list);
        if (idInquilino != null) list = list.Where(c => c.IdInquilino == idInquilino).ToList();
        if (estado != null) list = list.Where(c => c.Estado == estado).ToList();
        return list;
    }

    private async Task EnriquecerMoraAsync(List<CxCCargo> cargos)
    {
        var pendientes = cargos.Where(c => c.Estado != "PAGADO").ToList();
        if (pendientes.Count == 0) return;
        var contratosData = await ReadRangeAsync($"{TAB_CONTRATOS}!A:X");
        var graciaPorContrato = contratosData.Skip(1)
            .Where(r => r.Count > 0 && r[0]?.ToString() != "")
            .ToDictionary(r => r.Get(0), r => r.GetInt(11) > 0 ? r.GetInt(11) : 5);
        var hoy = DateTime.Today;
        foreach (var c in pendientes)
        {
            var diasGracia = graciaPorContrato.TryGetValue(c.IdContrato, out var g) ? g : 5;
            var diasTranscurridos = (hoy - c.FechaVencimiento.Date).Days;
            if (diasTranscurridos <= 0) continue;
            if (diasTranscurridos <= diasGracia) { c.EnGracia = true; c.DiasMora = 0; }
            else { c.EnGracia = false; c.DiasMora = diasTranscurridos - diasGracia; }
        }
    }

    public async Task<CxCCargo?> GetCxCCargoAsync(string id)
    {
        var result = await FindRowByIdAsync(TAB_CXC_CARGOS, id);
        return result.HasValue ? MapCxC(result.Value.row) : null;
    }

    // ─── RECÁLCULO DE MORA (nocturno / on-demand) ─────────────────────────────
    public async Task<int> RecalcularMorasMasivoAsync()
    {
        var data = await ReadRangeAsync($"{TAB_CXC_CARGOS}!A:T");
        var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        var contratosData = await ReadRangeAsync($"{TAB_CONTRATOS}!A:X");
        var contratos = contratosData.Skip(1)
            .Where(r => r.Count > 0 && r[0]?.ToString() != "")
            .ToDictionary(r => r.Get(0), r => new
            {
                DiasGracia = r.GetInt(11) > 0 ? r.GetInt(11) : 5,
                TipoMora = string.IsNullOrEmpty(r.Get(12)) ? "PORCENTAJE" : r.Get(12),
                MoraPorcentaje = r.GetDecimal(13),
                MontoMoraFija = r.GetDecimal(14),
                Periodicidad = r.Get(9),
            });

        var hoy = DateTime.Today;
        var updates = new List<ValueRange>();
        int actualizados = 0;

        // Recorre el rango ya leído; la fila en la hoja = índice (0-based) + 1 (encabezado se salta con i empezando en 1)
        for (int i = 1; i < data.Count; i++)
        {
            var r = data[i];
            if (r.Count == 0 || string.IsNullOrEmpty(r.Get(0))) continue;
            var cargo = MapCxC(r);
            if (cargo.Estado == "PAGADO") continue;

            var row = i + 1; // fila 1-based en la hoja (A1 notation)

            if (!contratos.TryGetValue(cargo.IdContrato, out var ctr))
                ctr = new { DiasGracia = 5, TipoMora = "PORCENTAJE", MoraPorcentaje = 5m, MontoMoraFija = 0m, Periodicidad = "MENSUAL" };

            var diasTranscurridos = (hoy - cargo.FechaVencimiento.Date).Days;
            decimal moraCalculada = 0;
            if (diasTranscurridos > ctr.DiasGracia)
            {
                var tamanoPeriodo = ctr.Periodicidad == "QUINCENAL" ? 15 : 30;
                var periodosAtrasados = (diasTranscurridos - ctr.DiasGracia) / tamanoPeriodo + 1;
                moraCalculada = ctr.TipoMora == "MONTO_FIJO"
                    ? ctr.MontoMoraFija * periodosAtrasados
                    : cargo.MontoOriginal * (ctr.MoraPorcentaje / 100) * periodosAtrasados;
            }

            if (moraCalculada == cargo.MontoMora) continue;

            var nuevoSaldo = Math.Max(0, cargo.MontoOriginal + moraCalculada - cargo.MontoPagado);
            var nuevoEstado = nuevoSaldo <= 0 ? "PAGADO" : diasTranscurridos > ctr.DiasGracia ? "EN_MORA" : (cargo.MontoPagado > 0 ? "PAGADO_PARCIAL" : "PENDIENTE");

            updates.Add(MakeUpdate($"{TAB_CXC_CARGOS}!L{row}", moraCalculada));
            updates.Add(MakeUpdate($"{TAB_CXC_CARGOS}!N{row}", nuevoSaldo));
            updates.Add(MakeUpdate($"{TAB_CXC_CARGOS}!O{row}", nuevoEstado));
            updates.Add(MakeUpdate($"{TAB_CXC_CARGOS}!T{row}", nowStr));
            actualizados++;
        }

        // Google Sheets permite hasta 100 rangos por batchUpdate cómodamente; se trocea por seguridad
        foreach (var chunk in updates.Chunk(400))
            await BatchUpdateAsync(chunk.ToList());

        await SetConfigAsync("ULTIMA_ACTUALIZACION_MORA", nowStr);
        return actualizados;
    }

    public async Task AjustarMoraCxCAsync(string id, AjustarMoraCxCRequest req, string usuario)
    {
        var result = await FindRowByIdAsync(TAB_CXC_CARGOS, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var cargo = MapCxC(result.Value.row);
        var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        var nuevoSaldo = Math.Max(0, cargo.MontoOriginal + req.MontoMora - cargo.MontoPagado);
        var nuevoEstado = nuevoSaldo <= 0 ? "PAGADO" : cargo.MontoPagado > 0 ? "PAGADO_PARCIAL" : cargo.Estado;

        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_CXC_CARGOS}!L{row}", req.MontoMora),
            MakeUpdate($"{TAB_CXC_CARGOS}!N{row}", nuevoSaldo),
            MakeUpdate($"{TAB_CXC_CARGOS}!O{row}", nuevoEstado),
            MakeUpdate($"{TAB_CXC_CARGOS}!T{row}", nowStr),
        });

        var logId = await GenerarIdUnicoAsync("LOG", TAB_LOG_CUSTODIA);
        await AppendRowAsync(TAB_LOG_CUSTODIA, new List<object>
        {
            logId, id, "", cargo.IdInquilino, cargo.NombreInquilino,
            cargo.IdInmueble, req.MontoMora, "AJUSTE_MORA", "AJUSTE_MORA",
            $"Mora: {cargo.MontoMora}→{req.MontoMora} | Motivo: {req.MotivoAjuste}",
            usuario, nowStr
        });
    }

    // ─── GENERACIÓN DE CARGOS RECURRENTES (nocturno / on-demand) ──────────────
    public async Task<int> GenerarCargosRecurrentesAsync()
    {
        var contratos = (await GetContratosAsync(soloVigentes: true)).ToList();
        if (contratos.Count == 0) { await SetConfigAsync("ULTIMA_GENERACION_CARGOS", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); return 0; }

        var cargosExistentes = await GetCxCCargosAsync();
        var hoy = DateTime.Today;
        int generados = 0;

        foreach (var ctr in contratos)
        {
            var tamanoPeriodo = ctr.Periodicidad == "QUINCENAL" ? 15 : 30;
            var ultimoCargo = cargosExistentes
                .Where(c => c.IdContrato == ctr.Id && c.TipoCargo == "ALQUILER")
                .OrderByDescending(c => c.FechaVencimiento)
                .FirstOrDefault();

            DateTime proximoVencimiento;
            if (ultimoCargo == null)
            {
                proximoVencimiento = ctr.Periodicidad == "QUINCENAL"
                    ? ctr.FechaInicio.AddDays(15)
                    : new DateTime(ctr.FechaInicio.Year, ctr.FechaInicio.Month, Math.Min(ctr.DiaCobro, DateTime.DaysInMonth(ctr.FechaInicio.Year, ctr.FechaInicio.Month)));
            }
            else
            {
                proximoVencimiento = ctr.Periodicidad == "QUINCENAL"
                    ? ultimoCargo.FechaVencimiento.AddDays(15)
                    : ultimoCargo.FechaVencimiento.AddMonths(1);
            }

            // Genera todos los períodos vencidos que falten hasta hoy (cubre el caso de que el proceso no haya corrido varios días)
            while (proximoVencimiento <= hoy && proximoVencimiento <= ctr.FechaFin)
            {
                var periodoInicio = ctr.Periodicidad == "QUINCENAL" ? proximoVencimiento.AddDays(-15) : proximoVencimiento.AddMonths(-1);
                var id = await GenerarIdUnicoAsync("CXC", TAB_CXC_CARGOS);
                var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                await AppendRowAsync(TAB_CXC_CARGOS, new List<object>
                {
                    id, ctr.Id, ctr.IdInquilino, ctr.NombreInquilino, ctr.IdInmueble,
                    "ALQUILER", $"Alquiler {proximoVencimiento:MMMM yyyy}",
                    periodoInicio.ToString("yyyy-MM-dd"), proximoVencimiento.ToString("yyyy-MM-dd"),
                    proximoVencimiento.ToString("yyyy-MM-dd"),
                    ctr.MontoAlquiler, 0, 0, ctr.MontoAlquiler,
                    "PENDIENTE", 0, "", "Generado automáticamente", now
                });
                generados++;
                proximoVencimiento = ctr.Periodicidad == "QUINCENAL" ? proximoVencimiento.AddDays(tamanoPeriodo) : proximoVencimiento.AddMonths(1);
            }
        }

        await SetConfigAsync("ULTIMA_GENERACION_CARGOS", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return generados;
    }

    public async Task<ProcesosEstado> GetProcesosEstadoAsync()
    {
        var mora = await GetConfigAsync("ULTIMA_ACTUALIZACION_MORA");
        var cargos = await GetConfigAsync("ULTIMA_GENERACION_CARGOS");
        return new ProcesosEstado(
            string.IsNullOrEmpty(mora) ? null : DateTime.Parse(mora),
            string.IsNullOrEmpty(cargos) ? null : DateTime.Parse(cargos)
        );
    }

    public async Task CrearCxCAsync(CxCCargo cxc)
    {
        await AppendRowAsync(TAB_CXC_CARGOS, new List<object>
        {
            cxc.Id, cxc.IdContrato, cxc.IdInquilino, cxc.NombreInquilino, cxc.IdInmueble,
            cxc.TipoCargo, cxc.Descripcion,
            cxc.PeriodoInicio.ToString("yyyy-MM-dd"), cxc.PeriodoFin.ToString("yyyy-MM-dd"),
            cxc.FechaVencimiento.ToString("yyyy-MM-dd"),
            cxc.MontoOriginal, 0, 0, cxc.MontoOriginal,
            "PENDIENTE", 0, "", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    public async Task<CxCCargo> CrearCxCManualAsync(CrearCxCManualRequest req)
    {
        var id = await GenerarIdUnicoAsync("CXC", TAB_CXC_CARGOS);
        var inquilino = await GetInquilinoAsync(req.IdInquilino);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_CXC_CARGOS, new List<object>
        {
            id, req.IdContrato, req.IdInquilino, inquilino?.Nombre ?? "",
            req.IdInmueble, req.TipoCargo, req.Descripcion,
            req.PeriodoInicio.ToString("yyyy-MM-dd"), req.PeriodoFin.ToString("yyyy-MM-dd"),
            req.FechaVencimiento.ToString("yyyy-MM-dd"),
            req.MontoOriginal, 0, 0, req.MontoOriginal,
            "PENDIENTE", 0, "", req.Notas, now
        });
        return (await GetCxCCargoAsync(id))!;
    }

    public async Task AjustarCxCConLogAsync(string id, AjustarCxCRequest req, string usuario)
    {
        var result = await FindRowByIdAsync(TAB_CXC_CARGOS, id);
        if (!result.HasValue) return;
        var row   = result.Value.rowIndex;
        var cargo = MapCxC(result.Value.row);

        var notaAnterior = cargo.Notas;
        var nuevaNota    = string.IsNullOrWhiteSpace(notaAnterior) ? "" : notaAnterior + "\n";
        var lineaLog     = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] Ajuste por {usuario}: {req.MotivoAjuste}";
        var notaFinal    = nuevaNota + lineaLog;

        var saldoNuevo = req.MontoOriginal - cargo.MontoPagado;

        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_CXC_CARGOS}!G{row}",  req.Descripcion),
            MakeUpdate($"{TAB_CXC_CARGOS}!J{row}",  req.FechaVencimiento.ToString("yyyy-MM-dd")),
            MakeUpdate($"{TAB_CXC_CARGOS}!K{row}",  req.MontoOriginal),
            MakeUpdate($"{TAB_CXC_CARGOS}!N{row}",  Math.Max(0, saldoNuevo)),
            MakeUpdate($"{TAB_CXC_CARGOS}!R{row}",  notaFinal),
        });

        // Escribir log de modificación
        var logId = await GenerarIdUnicoAsync("LOG", TAB_LOG_CUSTODIA);
        await AppendRowAsync(TAB_LOG_CUSTODIA, new List<object>
        {
            logId, id, "", cargo.IdInquilino, cargo.NombreInquilino,
            cargo.IdInmueble, cargo.MontoOriginal, "AJUSTE_CXC", "AJUSTE_CXC",
            $"Monto: {cargo.MontoOriginal}→{req.MontoOriginal} | Venc: {cargo.FechaVencimiento:yyyy-MM-dd}→{req.FechaVencimiento:yyyy-MM-dd} | Motivo: {req.MotivoAjuste}",
            usuario, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    public async Task ActualizarCxCAsync(string id, decimal montoPagado, decimal mora, string estado)
    {
        var result = await FindRowByIdAsync(TAB_CXC_CARGOS, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var original = result.Value.row.GetDecimal(10);
        var saldo = original + mora - montoPagado;
        var updates = new List<ValueRange>
        {
            MakeUpdate($"{TAB_CXC_CARGOS}!L{row}", mora),
            MakeUpdate($"{TAB_CXC_CARGOS}!M{row}", montoPagado),
            MakeUpdate($"{TAB_CXC_CARGOS}!N{row}", Math.Max(0, saldo)),
            MakeUpdate($"{TAB_CXC_CARGOS}!O{row}", estado),
            MakeUpdate($"{TAB_CXC_CARGOS}!Q{row}", DateTime.Now.ToString("yyyy-MM-dd"))
        };
        await BatchUpdateAsync(updates);
    }

    private static CxCCargo MapCxC(IList<object> r) => new()
    {
        Id = r.Get(0), IdContrato = r.Get(1), IdInquilino = r.Get(2),
        NombreInquilino = r.Get(3), IdInmueble = r.Get(4),
        TipoCargo = r.Get(5), Descripcion = r.Get(6),
        PeriodoInicio = r.GetDate(7), PeriodoFin = r.GetDate(8),
        FechaVencimiento = r.GetDate(9), MontoOriginal = r.GetDecimal(10),
        MontoMora = r.GetDecimal(11), MontoPagado = r.GetDecimal(12),
        SaldoPendiente = r.GetDecimal(13), Estado = r.Get(14),
        DiasMora = r.GetInt(15), FechaUltimoPago = r.GetDate(16) == default ? null : r.GetDate(16),
        Notas = r.Get(17), FechaRegistro = r.GetDate(18),
        FechaUltimaActualizacionMora = r.GetDate(19) == default ? null : r.GetDate(19)
    };

    // ─── CXC_PAGOS ───────────────────────────────────────────────────────────
    public async Task<List<CxCPago>> GetPagosAsync(string? idInquilino = null, string? estadoCustodia = null, string? idSesionCaja = null)
    {
        var data = await ReadRangeAsync($"{TAB_CXC_PAGOS}!A:W");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapPago).ToList();
        if (idInquilino != null) list = list.Where(p => p.IdInquilino == idInquilino).ToList();
        if (estadoCustodia != null) list = list.Where(p => p.EstadoCustodia == estadoCustodia).ToList();
        if (idSesionCaja != null) list = list.Where(p => p.IdSesionCaja == idSesionCaja).ToList();
        return list;
    }

    public async Task<CxCPago?> GetPagoAsync(string id)
    {
        var result = await FindRowByIdAsync(TAB_CXC_PAGOS, id);
        return result.HasValue ? MapPago(result.Value.row) : null;
    }

    public async Task<CxCPago> InsertarPagoAsync(CxCPago pago)
    {
        await AppendRowAsync(TAB_CXC_PAGOS, new List<object>
        {
            pago.Id, pago.IdCxC, pago.IdContrato, pago.IdInquilino, pago.NombreInquilino,
            pago.IdInmueble, pago.IdSesionCaja, pago.FechaPago.ToString("yyyy-MM-dd HH:mm:ss"),
            pago.MontoAlquiler, pago.MontoMora, pago.TotalPagado,
            pago.FormaPago, pago.Referencia, pago.EstadoCustodia,
            pago.IdLiquidacion ?? "", pago.UrlReciboPDF, pago.Notas,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            pago.IdRecibo, pago.Anulado ? "ANULADO" : "ACTIVO", pago.MotivoAnulacion,
            pago.MontoAbonoAplicado
        });
        return pago;
    }

    public async Task AplicarPagoCxCAsync(string idCxC, decimal montoAlquilerAplicado, decimal montoMoraAplicada)
    {
        var result = await FindRowByIdAsync(TAB_CXC_CARGOS, idCxC);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var cargo = MapCxC(result.Value.row);
        var nuevoMontoPagado = cargo.MontoPagado + montoAlquilerAplicado + montoMoraAplicada;
        var nuevoSaldo = Math.Max(0, cargo.MontoOriginal - nuevoMontoPagado);
        var nuevoEstado = nuevoSaldo <= 0.01m ? "PAGADO" : "PAGADO_PARCIAL";
        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_CXC_CARGOS}!M{row}", nuevoMontoPagado),
            MakeUpdate($"{TAB_CXC_CARGOS}!N{row}", nuevoSaldo),
            MakeUpdate($"{TAB_CXC_CARGOS}!O{row}", nuevoEstado),
            MakeUpdate($"{TAB_CXC_CARGOS}!Q{row}", DateTime.Now.ToString("yyyy-MM-dd")),
        });
    }

    // ─── ABONOS A CUENTA ─────────────────────────────────────────────────────
    private static readonly List<object> HeaderAbonos = new()
    {
        "Id", "IdInquilino", "NombreInquilino", "Fecha", "MontoOriginal",
        "SaldoDisponible", "Estado", "IdReciboOrigen", "Usuario", "Notas", "FechaRegistro"
    };

    public async Task<List<AbonoCuenta>> GetAbonosCuentaAsync(string? idInquilino = null)
    {
        await EnsureTabExistsAsync(TAB_ABONOS_CUENTA, HeaderAbonos);
        var data = await ReadRangeAsync($"{TAB_ABONOS_CUENTA}!A:K");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapAbono).ToList();
        return idInquilino != null ? list.Where(a => a.IdInquilino == idInquilino).ToList() : list;
    }

    public async Task<AbonoCuenta> CrearAbonoCuentaAsync(string idInquilino, string nombreInquilino, decimal monto, string idReciboOrigen, string usuario, string notas)
    {
        var id = await GenerarIdUnicoAsync("ABN", TAB_ABONOS_CUENTA);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_ABONOS_CUENTA, new List<object>
        {
            id, idInquilino, nombreInquilino, DateTime.Now.ToString("yyyy-MM-dd"),
            monto, monto, "DISPONIBLE", idReciboOrigen, usuario, notas, now
        });
        return (await GetAbonosCuentaAsync()).First(a => a.Id == id);
    }

    public async Task AplicarAbonoAsync(string idAbono, decimal montoAplicado)
    {
        var result = await FindRowByIdAsync(TAB_ABONOS_CUENTA, idAbono);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var abono = MapAbono(result.Value.row);
        var nuevoSaldo = Math.Max(0, abono.SaldoDisponible - montoAplicado);
        var nuevoEstado = nuevoSaldo <= 0.01m ? "AGOTADO" : "PARCIAL";
        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_ABONOS_CUENTA}!F{row}", nuevoSaldo),
            MakeUpdate($"{TAB_ABONOS_CUENTA}!G{row}", nuevoEstado),
        });
    }

    private static AbonoCuenta MapAbono(IList<object> r) => new()
    {
        Id = r.Get(0), IdInquilino = r.Get(1), NombreInquilino = r.Get(2),
        Fecha = r.GetDate(3), MontoOriginal = r.GetDecimal(4), SaldoDisponible = r.GetDecimal(5),
        Estado = r.Get(6), IdReciboOrigen = r.Get(7), Usuario = r.Get(8),
        Notas = r.Get(9), FechaRegistro = r.GetDate(10)
    };

    public async Task ActualizarEstadoCustodiaAsync(string idPago, string nuevoEstado, string? idLiquidacion = null)
    {
        var result = await FindRowByIdAsync(TAB_CXC_PAGOS, idPago);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var updates = new List<ValueRange>
        {
            MakeUpdate($"{TAB_CXC_PAGOS}!N{row}", nuevoEstado)
        };
        if (idLiquidacion != null)
            updates.Add(MakeUpdate($"{TAB_CXC_PAGOS}!O{row}", idLiquidacion));
        await BatchUpdateAsync(updates);
    }

    public async Task AnularPagoAsync(string idPago, string motivo)
    {
        var result = await FindRowByIdAsync(TAB_CXC_PAGOS, idPago);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_CXC_PAGOS}!T{row}", "ANULADO"),
            MakeUpdate($"{TAB_CXC_PAGOS}!U{row}", motivo),
        });
    }

    public async Task RestaurarSaldoCxCAsync(string idCxC, decimal montoAlquilerDevuelto, decimal montoMoraDevuelto)
    {
        var result = await FindRowByIdAsync(TAB_CXC_CARGOS, idCxC);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var cargo = MapCxC(result.Value.row);
        var nuevoMontoPagado = Math.Max(0, cargo.MontoPagado - (montoAlquilerDevuelto + montoMoraDevuelto));
        var nuevoSaldo = cargo.MontoOriginal - nuevoMontoPagado;
        var nuevoEstado = nuevoMontoPagado <= 0 ? "PENDIENTE" : "PAGADO_PARCIAL";
        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_CXC_CARGOS}!M{row}", nuevoMontoPagado),
            MakeUpdate($"{TAB_CXC_CARGOS}!N{row}", Math.Max(0, nuevoSaldo)),
            MakeUpdate($"{TAB_CXC_CARGOS}!O{row}", nuevoEstado),
        });
    }

    private static CxCPago MapPago(IList<object> r) => new()
    {
        Id = r.Get(0), IdCxC = r.Get(1), IdContrato = r.Get(2),
        IdInquilino = r.Get(3), NombreInquilino = r.Get(4), IdInmueble = r.Get(5),
        IdSesionCaja = r.Get(6), FechaPago = r.GetDate(7),
        MontoAlquiler = r.GetDecimal(8), MontoMora = r.GetDecimal(9),
        TotalPagado = r.GetDecimal(10), FormaPago = r.Get(11),
        Referencia = r.Get(12), EstadoCustodia = r.Get(13),
        IdLiquidacion = r.Get(14), UrlReciboPDF = r.Get(15),
        Notas = r.Get(16), FechaRegistro = r.GetDate(17),
        IdRecibo = r.Get(18), Anulado = r.Get(19) == "ANULADO", MotivoAnulacion = r.Get(20),
        MontoAbonoAplicado = r.GetDecimal(21)
    };

    // ─── SESIONES CAJA ───────────────────────────────────────────────────────
    public async Task<SesionCaja?> GetSesionHoyAsync()
    {
        var hoy = DateTime.Today.ToString("yyyy-MM-dd");
        var data = await ReadRangeAsync($"{TAB_SESIONES}!A:P");
        var row = data.Skip(1).FirstOrDefault(r => r.Get(1).StartsWith(hoy));
        return row != null ? MapSesion(row) : null;
    }

    public async Task<SesionCaja> ObtenerOCrearSesionAsync(string usuario)
    {
        var sesion = await GetSesionHoyAsync();
        if (sesion != null && sesion.Estado == "ABIERTA") return sesion;
        var id = await GenerarIdUnicoAsync("SES", TAB_SESIONES);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_SESIONES, new List<object>
        {
            id, now, "ABIERTA", 0, 0, 0, 0, 0, 0, usuario, "", "", "", now
        });
        return (await GetSesionHoyAsync())!;
    }

    public async Task ActualizarTotalesSesionAsync(string idSesion, string formaPago, decimal monto)
    {
        var result = await FindRowByIdAsync(TAB_SESIONES, idSesion);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var r = result.Value.row;
        decimal ef = r.GetDecimal(3), tr = r.GetDecimal(4), ta = r.GetDecimal(5), ch = r.GetDecimal(6);
        if (formaPago == "EFECTIVO") ef += monto;
        else if (formaPago == "TRANSFERENCIA") tr += monto;
        else if (formaPago == "TARJETA") ta += monto;
        else if (formaPago == "CHEQUE") ch += monto;
        decimal total = ef + tr + ta + ch;
        int cant = r.GetInt(8) + 1;
        var updates = new List<ValueRange>
        {
            MakeUpdate($"{TAB_SESIONES}!D{row}", ef),
            MakeUpdate($"{TAB_SESIONES}!E{row}", tr),
            MakeUpdate($"{TAB_SESIONES}!F{row}", ta),
            MakeUpdate($"{TAB_SESIONES}!G{row}", ch),
            MakeUpdate($"{TAB_SESIONES}!H{row}", total),
            MakeUpdate($"{TAB_SESIONES}!I{row}", cant),
        };
        await BatchUpdateAsync(updates);
    }

    public async Task AprobarCuadreAsync(string idSesion, string usuario)
    {
        var result = await FindRowByIdAsync(TAB_SESIONES, idSesion);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        var updates = new List<ValueRange>
        {
            MakeUpdate($"{TAB_SESIONES}!C{row}", "CERRADA"),
            MakeUpdate($"{TAB_SESIONES}!K{row}", usuario),
            MakeUpdate($"{TAB_SESIONES}!L{row}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };
        await BatchUpdateAsync(updates);
        // Move all COBRADO payments to EN_ADMINISTRACION
        var pagos = await GetPagosAsync(estadoCustodia: "COBRADO");
        foreach (var pago in pagos.Where(p => p.IdSesionCaja == idSesion))
            await ActualizarEstadoCustodiaAsync(pago.Id, "EN_ADMINISTRACION");
    }

    private static SesionCaja MapSesion(IList<object> r) => new()
    {
        Id = r.Get(0), Fecha = r.GetDate(1), Estado = r.Get(2),
        TotalEfectivo = r.GetDecimal(3), TotalTransferencia = r.GetDecimal(4),
        TotalTarjeta = r.GetDecimal(5), TotalCheque = r.GetDecimal(6),
        TotalGeneral = r.GetDecimal(7), CantidadTransacciones = r.GetInt(8),
        UsuarioApertura = r.Get(9), UsuarioCierre = r.Get(10),
        FechaCierre = r.GetDate(11) == default ? null : r.GetDate(11),
        UrlCierrePDF = r.Get(12), Notas = r.Get(13)
    };

    // ─── ADELANTOS ───────────────────────────────────────────────────────────
    public async Task<List<Adelanto>> GetAdelantosAsync(string? idPropietario = null)
    {
        var data = await ReadRangeAsync($"{TAB_ADELANTOS}!A:K");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapAdelanto).ToList();
        return idPropietario != null ? list.Where(a => a.IdPropietario == idPropietario).ToList() : list;
    }

    public async Task<Adelanto> CrearAdelantoAsync(CrearAdelantoRequest req, string usuario)
    {
        var id = await GenerarIdUnicoAsync("ADL", TAB_ADELANTOS);
        var prop = await GetPropietarioAsync(req.IdPropietario);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AppendRowAsync(TAB_ADELANTOS, new List<object>
        {
            id, req.IdPropietario, prop?.Nombre ?? "", req.Fecha.ToString("yyyy-MM-dd"),
            req.Concepto, req.Monto, "PENDIENTE", "", usuario, req.Notas, now
        });
        return (await GetAdelantosAsync()).First(a => a.Id == id);
    }

    public async Task AplicarAdelantoAsync(string id, string idLiquidacion)
    {
        var result = await FindRowByIdAsync(TAB_ADELANTOS, id);
        if (!result.HasValue) return;
        var row = result.Value.rowIndex;
        await BatchUpdateAsync(new List<ValueRange>
        {
            MakeUpdate($"{TAB_ADELANTOS}!G{row}", "APLICADO"),
            MakeUpdate($"{TAB_ADELANTOS}!H{row}", idLiquidacion),
        });
    }

    private static Adelanto MapAdelanto(IList<object> r) => new()
    {
        Id = r.Get(0), IdPropietario = r.Get(1), NombrePropietario = r.Get(2),
        Fecha = r.GetDate(3), Concepto = r.Get(4), Monto = r.GetDecimal(5),
        Estado = r.Get(6), IdLiquidacion = r.Get(7), Usuario = r.Get(8),
        Notas = r.Get(9), FechaRegistro = r.GetDate(10)
    };

    // ─── LIQUIDACIONES ───────────────────────────────────────────────────────
    public async Task<List<Liquidacion>> GetLiquidacionesAsync(string? idPropietario = null)
    {
        var data = await ReadRangeAsync($"{TAB_LIQUIDACIONES}!A:P");
        var list = data.Skip(1).Where(r => r.Count > 0 && r[0]?.ToString() != "").Select(MapLiquidacion).ToList();
        return idPropietario != null ? list.Where(l => l.IdPropietario == idPropietario).ToList() : list;
    }

    public async Task<Liquidacion> CrearLiquidacionAsync(Liquidacion liq)
    {
        await AppendRowAsync(TAB_LIQUIDACIONES, new List<object>
        {
            liq.Id, liq.IdPropietario, liq.NombrePropietario,
            liq.FechaDesde.ToString("yyyy-MM-dd"), liq.FechaHasta.ToString("yyyy-MM-dd"),
            liq.TotalCobrado, liq.Comision, liq.NetoPropietario,
            liq.Estado, "", "", liq.UrlPDF, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
        return liq;
    }

    private static Liquidacion MapLiquidacion(IList<object> r) => new()
    {
        Id = r.Get(0), IdPropietario = r.Get(1), NombrePropietario = r.Get(2),
        FechaDesde = r.GetDate(3), FechaHasta = r.GetDate(4),
        TotalCobrado = r.GetDecimal(5), Comision = r.GetDecimal(6),
        NetoPropietario = r.GetDecimal(7), Estado = r.Get(8),
        FechaPago = r.GetDate(9) == default ? null : r.GetDate(9),
        ReferenciaPago = r.Get(10), UrlPDF = r.Get(11),
        FechaRegistro = r.GetDate(12)
    };

    // ─── LOG CUSTODIA ────────────────────────────────────────────────────────
    public async Task InsertarLogCustodiaAsync(LogCustodia log)
    {
        await AppendRowAsync(TAB_LOG_CUSTODIA, new List<object>
        {
            log.Id, log.IdPago, log.IdContrato, log.IdInquilino, log.NombreInquilino,
            log.IdInmueble, log.Monto, log.EstadoAnterior, log.EstadoNuevo,
            log.Motivo, log.Usuario, log.FechaMovimiento.ToString("yyyy-MM-dd HH:mm:ss"),
            log.HashSHA256
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private static ValueRange MakeUpdate(string range, object value) => new()
    {
        Range = range,
        Values = new List<IList<object>> { new List<object> { value } }
    };

    private static string CellAddress(int col, int row)
    {
        string colLetter = "";
        while (col > 0)
        {
            col--;
            colLetter = (char)('A' + col % 26) + colLetter;
            col /= 26;
        }
        return $"{colLetter}{row}";
    }
}

public static class RowExtensions
{
    public static string Get(this IList<object> r, int i) => i < r.Count ? r[i]?.ToString() ?? "" : "";
    public static decimal GetDecimal(this IList<object> r, int i) => decimal.TryParse(r.Get(i), out var v) ? v : 0;
    public static int GetInt(this IList<object> r, int i) => int.TryParse(r.Get(i), out var v) ? v : 0;
    public static DateTime GetDate(this IList<object> r, int i) => DateTime.TryParse(r.Get(i), out var v) ? v : default;
}
