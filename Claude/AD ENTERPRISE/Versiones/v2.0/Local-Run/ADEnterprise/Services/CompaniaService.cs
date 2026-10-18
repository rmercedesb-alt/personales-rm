using ADEnterprise.Models;

namespace ADEnterprise.Services;

public class CompaniaService
{
    private readonly GoogleSheetsService _gs;
    public CompaniaService(GoogleSheetsService gs) => _gs = gs;

    public async Task<List<Compania>> ObtenerTodasAsync(bool soloActivas = false)
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_COMPANIAS);
        var lista = filas.Skip(1).Where(f => f.Count >= 4).Select(Mapear).ToList();
        return soloActivas ? lista.Where(c => c.Activo).ToList() : lista;
    }

    public async Task<Compania?> BuscarPorIdAsync(string id)
    {
        var todas = await ObtenerTodasAsync();
        return todas.FirstOrDefault(c => c.Id == id);
    }

    // ── ID helpers ───────────────────────────────────────────────────────────
    public async Task<string> SiguienteIdCompaniaAsync()
    {
        var todas = await ObtenerTodasAsync();
        int max = 0;
        foreach (var x in todas)
            if (x.Id.StartsWith("ADCOM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(x.Id[5..], out int n) && n > max)
                max = n;
        return $"ADCOM{(max + 1):D3}";
    }

    /// <summary>
    /// Asigna IDs ADCOM### a todos los registros que aún no tengan el formato correcto.
    /// Usa una sola llamada batch a la API para evitar la cuota de escrituras.
    /// </summary>
    public async Task<int> BackfillIdsAsync()
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_COMPANIAS);
        int max = 0;
        // Paso 1: detectar máximo actual
        for (int i = 1; i < filas.Count; i++)
        {
            var id = GoogleSheetsService.Str(filas[i], 0);
            if (id.StartsWith("ADCOM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(id[5..], out int n) && n > max)
                max = n;
        }
        // Paso 2: construir lista de actualizaciones necesarias
        var actualizaciones = new List<(string Rango, IList<object> Valores)>();
        for (int i = 1; i < filas.Count; i++)
        {
            var id = GoogleSheetsService.Str(filas[i], 0);
            if (string.IsNullOrWhiteSpace(id) ||
                !id.StartsWith("ADCOM", StringComparison.OrdinalIgnoreCase))
            {
                max++;
                var c = Mapear(filas[i]);
                c.Id = $"ADCOM{max:D3}";
                actualizaciones.Add(($"{GoogleSheetsService.HOJA_COMPANIAS}!A{i + 1}:H{i + 1}", Serializar(c)));
            }
        }
        // Paso 3: una sola llamada batch con TODOS los cambios
        if (actualizaciones.Count > 0)
            await _gs.ActualizarMultiplesRangosAsync(actualizaciones);
        return actualizaciones.Count;
    }

    public async Task CrearAsync(Compania c)
    {
        c.Id = await SiguienteIdCompaniaAsync();
        await _gs.AgregarFilaAsync(GoogleSheetsService.HOJA_COMPANIAS, Serializar(c));
    }

    public async Task ActualizarAsync(Compania c)
    {
        var filas = await _gs.LeerAsync(GoogleSheetsService.HOJA_COMPANIAS);
        for (int i = 1; i < filas.Count; i++)
        {
            if (GoogleSheetsService.Str(filas[i], 0) == c.Id)
            {
                await _gs.ActualizarFilaAsync(GoogleSheetsService.HOJA_COMPANIAS, i + 1, Serializar(c));
                return;
            }
        }
    }

    private static IList<object> Serializar(Compania c) => new object[]
    {
        c.Id, c.Nombre, c.Rnc, c.Telefono, c.Direccion,
        c.Correo, c.CondicionPagoDias, c.Activo ? "TRUE" : "FALSE"
    };

    private static Compania Mapear(IList<object> f) => new()
    {
        Id                = GoogleSheetsService.Str(f, 0),
        Nombre            = GoogleSheetsService.Str(f, 1),
        Rnc               = GoogleSheetsService.Str(f, 2),
        Telefono          = GoogleSheetsService.Str(f, 3),
        Direccion         = GoogleSheetsService.Str(f, 4),
        Correo            = GoogleSheetsService.Str(f, 5),
        CondicionPagoDias = GoogleSheetsService.Int(f, 6),
        Activo            = GoogleSheetsService.Bool(f, 7)
    };
}
