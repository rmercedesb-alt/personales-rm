using ADEnterprise.Models;

namespace ADEnterprise.Services;

public class ClienteService
{
    private readonly GoogleSheetsService _gs;
    public ClienteService(GoogleSheetsService gs) => _gs = gs;

    public async Task<List<Cliente>> ObtenerTodosAsync(bool soloActivos = false)
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_CLIENTES);
        var lista = filas.Skip(1).Where(f => f.Count >= 3).Select(Mapear).ToList();
        return soloActivos ? lista.Where(c => c.Activo).ToList() : lista;
    }

    public async Task<Cliente?> BuscarPorIdAsync(string id)
    {
        var todos = await ObtenerTodosAsync();
        return todos.FirstOrDefault(c => c.Id == id);
    }

    // ── ID helpers ───────────────────────────────────────────────────────────
    public async Task<string> SiguienteIdClienteAsync()
    {
        var todos = await ObtenerTodosAsync();
        int max = 0;
        foreach (var x in todos)
            if (x.Id.StartsWith("ADCTE", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(x.Id[5..], out int n) && n > max)
                max = n;
        return $"ADCTE{(max + 1):D3}";
    }

    /// <summary>
    /// Asigna IDs ADCTE### a todos los registros que aún no tengan el formato correcto.
    /// Usa una sola llamada batch a la API para evitar la cuota de escrituras.
    /// </summary>
    public async Task<int> BackfillIdsAsync()
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_CLIENTES);
        int max = 0;
        // Paso 1: detectar máximo actual
        for (int i = 1; i < filas.Count; i++)
        {
            var id = GoogleSheetsService.Str(filas[i], 0);
            if (id.StartsWith("ADCTE", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(id[5..], out int n) && n > max)
                max = n;
        }
        // Paso 2: construir lista de actualizaciones necesarias
        var actualizaciones = new List<(string Rango, IList<object> Valores)>();
        for (int i = 1; i < filas.Count; i++)
        {
            var id = GoogleSheetsService.Str(filas[i], 0);
            if (string.IsNullOrWhiteSpace(id) ||
                !id.StartsWith("ADCTE", StringComparison.OrdinalIgnoreCase))
            {
                max++;
                var c = Mapear(filas[i]);
                c.Id = $"ADCTE{max:D3}";
                actualizaciones.Add(($"{GoogleSheetsService.HOJA_CLIENTES}!A{i + 1}:F{i + 1}", Serializar(c)));
            }
        }
        // Paso 3: una sola llamada batch con TODOS los cambios
        if (actualizaciones.Count > 0)
            await _gs.ActualizarMultiplesRangosAsync(actualizaciones);
        return actualizaciones.Count;
    }

    public async Task CrearAsync(Cliente c)
    {
        c.Id = await SiguienteIdClienteAsync();
        await _gs.AgregarFilaAsync(GoogleSheetsService.HOJA_CLIENTES, Serializar(c));
    }

    public async Task ActualizarAsync(Cliente c)
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_CLIENTES);
        for (int i = 1; i < filas.Count; i++)
        {
            if (GoogleSheetsService.Str(filas[i], 0) == c.Id)
            {
                await _gs.ActualizarFilaAsync(GoogleSheetsService.HOJA_CLIENTES, i + 1, Serializar(c));
                return;
            }
        }
    }

    private static IList<object> Serializar(Cliente c) => new object[]
    {
        c.Id, c.NombreCompleto, c.Cedula, c.Telefono,
        c.Correo, c.Activo ? "TRUE" : "FALSE"
    };

    private static Cliente Mapear(IList<object> f) => new()
    {
        Id             = GoogleSheetsService.Str(f, 0),
        NombreCompleto = GoogleSheetsService.Str(f, 1),
        Cedula         = GoogleSheetsService.Str(f, 2),
        Telefono       = GoogleSheetsService.Str(f, 3),
        Correo         = GoogleSheetsService.Str(f, 4),
        Activo         = GoogleSheetsService.Bool(f, 5)
    };
}
