using Microsoft.Data.SqlClient;
using OptimizadorRutas.Models;

namespace OptimizadorRutas.Services;

/// <summary>
/// Acceso a datos SQL Server para el Optimizador de Rutas.
/// Todas las operaciones son async para no bloquear el hilo UI de Blazor.
/// </summary>
public class RutaService
{
    private readonly string _conn;

    public RutaService(IConfiguration config)
    {
        _conn = config.GetConnectionString("MovilBusiness")
                ?? throw new InvalidOperationException("Connection string 'MovilBusiness' not found.");
    }

    // ── Lista de supervisores (solo los que tienen reps en RutaVisitas) ────────
    public async Task<List<SupervisorInfo>> GetSupervisoresAsync()
    {
        const string sql = """
            SELECT DISTINCT
                ISNULL(s.RepCodigo, r.RepSupervisor) AS SupCodigo,
                ISNULL(s.RepNombre, r.RepSupervisor)  AS SupNombre
            FROM   RutaVisitas rv
            INNER JOIN Representantes r ON r.RepCodigo  = rv.RepCodigo
            LEFT  JOIN Representantes s ON s.RepCodigo  = r.RepSupervisor
            WHERE  r.RepSupervisor IS NOT NULL AND r.RepSupervisor <> ''
            ORDER  BY SupNombre
            """;

        var list = new List<SupervisorInfo>();
        await using var cn = new SqlConnection(_conn);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd  = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            list.Add(new SupervisorInfo
            {
                RepCodigo = rd.GetString(0),
                RepNombre = rd.GetString(1),
            });
        }
        return list;
    }

    // ── Lista de representantes (solo los que tienen rutas, con filtro de sup) ─
    /// <summary>
    /// supervisores: lista de RepCodigo de supervisores a filtrar.
    /// Si está vacía se devuelven todos los representantes con rutas.
    /// </summary>
    public async Task<List<RepresentanteInfo>> GetRepresentantesAsync(
        IList<string>? supervisores = null)
    {
        string supFilter = (supervisores is { Count: > 0 })
            ? $"AND ISNULL(r.RepSupervisor,'') IN ({string.Join(",", supervisores.Select((_, i) => $"@s{i}"))})"
            : "";

        string sql = $"""
            SELECT rv.RepCodigo,
                   ISNULL(r.RepNombre,     rv.RepCodigo) AS RepNombre,
                   ISNULL(r.RepSupervisor, '')           AS RepSupervisor,
                   COUNT(*) AS NroClientes
            FROM   RutaVisitas rv
            LEFT JOIN Representantes r ON r.RepCodigo = rv.RepCodigo
            WHERE  1=1 {supFilter}
            GROUP  BY rv.RepCodigo, r.RepNombre, r.RepSupervisor
            ORDER  BY r.RepNombre
            """;

        var list = new List<RepresentanteInfo>();
        await using var cn = new SqlConnection(_conn);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        if (supervisores is { Count: > 0 })
            for (int i = 0; i < supervisores.Count; i++)
                cmd.Parameters.AddWithValue($"@s{i}", supervisores[i]);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            list.Add(new RepresentanteInfo
            {
                RepCodigo    = rd.GetString(0),
                RepNombre    = rd.GetString(1),
                RepSupervisor= rd.GetString(2),
                NroClientes  = Convert.ToInt32(rd.GetValue(3)),
            });
        }
        return list;
    }

    // ── Ruta completa de uno o varios representantes ──────────────────────────
    public async Task<List<ClienteRuta>> GetRutaAsync(IEnumerable<string> repCodigos)
    {
        var codes = repCodigos.ToList();
        if (codes.Count == 0) return [];

        var parametros = string.Join(",", codes.Select((_, i) => $"@p{i}"));
        var sql = $"""
            SELECT
                rv.RepCodigo,
                ISNULL(rep.RepNombre, rv.RepCodigo)      AS RepNombre,
                rv.CliID,
                c.CliNombre,
                c.CliCodigo,
                ISNULL(c.ClaID, 9999)                   AS ClaID,
                ISNULL(cl.ClaDescripcion, 'Sin clas')   AS ClaDescripcion,
                ISNULL(rv.RutSemana1, '0000000')        AS RutSemana1,
                ISNULL(rv.RutSemana2, '0000000')        AS RutSemana2,
                ISNULL(rv.RutSemana3, '0000000')        AS RutSemana3,
                ISNULL(rv.RutSemana4, '0000000')        AS RutSemana4,
                ISNULL(rv.RutPosicion, 0)               AS RutPosicion,
                ISNULL(c.CliLatitud,  rv.RutLatitud)    AS Latitud,
                ISNULL(c.CliLongitud, rv.RutLongitud)   AS Longitud,
                ISNULL(c.CliPromedioCompra, 0)          AS CliPromedioCompra,
                ISNULL(c.CliVentasAnioActual, 0)        AS CliVentasAnioActual,
                ISNULL(c.CliEstatus, 0)                 AS CliEstatus
            FROM RutaVisitas rv
            INNER JOIN Clientes       c   ON c.CliID      = rv.CliID
            INNER JOIN Representantes rep ON rep.RepCodigo = rv.RepCodigo
            LEFT  JOIN Clasifica      cl  ON cl.ClaID     = c.ClaID
            WHERE rv.RepCodigo IN ({parametros})
            ORDER BY rv.RepCodigo, rv.RutPosicion, rv.CliID
            """;

        var list = new List<ClienteRuta>();
        await using var cn = new SqlConnection(_conn);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        for (int i = 0; i < codes.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", codes[i]);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            list.Add(new ClienteRuta
            {
                RepCodigo          = rd.GetString(0),
                RepNombre          = rd.GetString(1),
                CliID              = Convert.ToInt32(rd.GetValue(2)),
                CliNombre          = rd.GetString(3),
                CliCodigo          = rd.GetString(4),
                ClaID              = Convert.ToInt32(rd.GetValue(5)),
                ClaDescripcion     = rd.GetString(6),
                RutSemana1         = rd.GetString(7),
                RutSemana2         = rd.GetString(8),
                RutSemana3         = rd.GetString(9),
                RutSemana4         = rd.GetString(10),
                RutPosicion        = Convert.ToInt32(rd.GetValue(11)),
                Latitud            = rd.IsDBNull(12) ? 0 : Convert.ToDouble(rd.GetValue(12)),
                Longitud           = rd.IsDBNull(13) ? 0 : Convert.ToDouble(rd.GetValue(13)),
                CliPromedioCompra  = rd.IsDBNull(14) ? 0 : Convert.ToDecimal(rd.GetValue(14)),
                CliVentasAnioActual= rd.IsDBNull(15) ? 0 : Convert.ToDecimal(rd.GetValue(15)),
                CliEstatus         = Convert.ToInt32(rd.GetValue(16)),
            });
        }
        return list;
    }

    // ── Guardar nuevas posiciones en BD ───────────────────────────────────────
    /// <summary>
    /// Actualiza RutPosicion en RutaVisitas.
    /// Guarda la secuencia calculada/editada para cada cliente del representante.
    /// </summary>
    public async Task<int> GuardarPosicionesAsync(
        IEnumerable<(int CliID, string RepCodigo, int NuevaPosicion)> cambios)
    {
        int total = 0;
        await using var cn = new SqlConnection(_conn);
        await cn.OpenAsync();
        foreach (var (cliId, repCod, pos) in cambios)
        {
            const string upd = """
                UPDATE RutaVisitas
                SET    RutPosicion = @pos
                WHERE  CliID = @cli AND RepCodigo = @rep
                """;
            await using var cmd = new SqlCommand(upd, cn);
            cmd.Parameters.AddWithValue("@pos", pos);
            cmd.Parameters.AddWithValue("@cli", cliId);
            cmd.Parameters.AddWithValue("@rep", repCod);
            total += await cmd.ExecuteNonQueryAsync();
        }
        return total;
    }
}
