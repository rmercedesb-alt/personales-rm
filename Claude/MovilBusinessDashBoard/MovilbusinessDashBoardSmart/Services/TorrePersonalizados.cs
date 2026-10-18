using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace DashVisitas.Services;

// ── Monitores PERSONALIZADOS de Torre de Control ─────────────────────────────
// Un monitor personalizado mide, POR VENDEDOR y POR DÍA, una métrica de ventas
// (clientes distintos / unidades / monto / impactos) sobre un Producto, Línea
// (categoría) o Suplidor, para un GRUPO de vendedores (por RepTipe/División,
// supervisor o lista de reps). Se configura con una vigencia (rango de fechas) y
// la estadística puede verse por día o acumulada en el rango.
//
// Se guarda como una fila en TorreControlMonitores_RM con Tipo='personalizado' y
// ParametrosJSON, por ejemplo:
//   { "dimension":"linea", "ids":[136], "metrica":"clientes", "objetivo":5,
//     "comparador":"mayorIgual", "minUnid":1,
//     "grupo":{"tipo":"reptipe","valores":["10"]} }
public static class TorrePersonalizados
{
    public class CustomSpec
    {
        public int MonID; public string Codigo = "", Nombre = "";
        public string Dimension = "linea";          // producto | linea | suplidor
        public List<int> Ids = new();
        public string Metrica = "clientes";          // clientes | unidades | monto | impactos
        public double Objetivo = 1;
        public string Comparador = "mayorIgual";     // mayorIgual | menor
        public string GrupoTipo = "reptipe";         // reptipe | supervisor | reps
        public List<string> GrupoVals = new();
        public double MinUnid = 1;                    // para 'clientes': unidades mínimas por cliente
        public string? HoraHasta;                     // opcional "HH:mm": solo cuenta ventas antes de esta hora
        public int Severidad = 2;
        // Comparador 'ritmo': el Objetivo se exige de forma LINEAL entre RitmoInicio y RitmoLimite
        // (regla de 3), en vez de exigirse completo desde el inicio del día. P.ej. Objetivo=20,
        // RitmoInicio=08:20, RitmoLimite=15:00 => a las 11:40 (mitad del tramo) se espera ~10.
        public TimeSpan? RitmoInicio;
        public TimeSpan? RitmoLimite;

        public bool AplicaA(TorreCore.RepMetric m)
        {
            if (GrupoVals.Count == 0) return true;    // sin grupo => todos
            return GrupoTipo switch
            {
                "supervisor" => GrupoVals.Contains(m.SupCod),
                "reps" => GrupoVals.Contains(m.RepCodigo),
                _ => GrupoVals.Contains(m.Division)   // reptipe (División)
            };
        }

        public int MetricaIdx => Metrica switch { "unidades" => 1, "monto" => 2, "impactos" => 3, _ => 0 };
        public string Unidad => Metrica switch { "unidades" => "und", "monto" => "RD$", "impactos" => "imp", _ => "cli" };
    }

    // Extrae los specs de la lista de monitores ya cargados (Tipo='personalizado').
    public static List<CustomSpec> Parse(IEnumerable<TorreCore.Monitor> monitores)
    {
        var lst = new List<CustomSpec>();
        foreach (var mon in monitores)
        {
            if (mon.Tipo != "personalizado" || string.IsNullOrWhiteSpace(mon.ParametrosJSON)) continue;
            try
            {
                var p = JsonDocument.Parse(mon.ParametrosJSON).RootElement;
                var s = new CustomSpec { MonID = mon.MonID, Codigo = mon.Codigo, Nombre = mon.Nombre, Severidad = mon.Severidad };
                if (p.TryGetProperty("dimension", out var d)) s.Dimension = d.GetString() ?? "linea";
                if (p.TryGetProperty("metrica", out var me)) s.Metrica = me.GetString() ?? "clientes";
                if (p.TryGetProperty("comparador", out var co)) s.Comparador = co.GetString() ?? "mayorIgual";
                if (p.TryGetProperty("objetivo", out var ob) && ob.TryGetDouble(out var obv)) s.Objetivo = obv;
                if (p.TryGetProperty("minUnid", out var mu) && mu.TryGetDouble(out var muv)) s.MinUnid = muv;
                if (p.TryGetProperty("horaHasta", out var hh)) { var hv = hh.GetString(); if (!string.IsNullOrWhiteSpace(hv)) s.HoraHasta = hv; }
                if (p.TryGetProperty("horaInicioRitmo", out var hi)) { var hv = hi.GetString(); if (!string.IsNullOrWhiteSpace(hv) && TimeSpan.TryParse(hv, out var hiv)) s.RitmoInicio = hiv; }
                if (p.TryGetProperty("horaLimiteRitmo", out var hl)) { var hv = hl.GetString(); if (!string.IsNullOrWhiteSpace(hv) && TimeSpan.TryParse(hv, out var hlv)) s.RitmoLimite = hlv; }
                if (p.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    foreach (var e in ids.EnumerateArray()) if (e.TryGetInt32(out var iv)) s.Ids.Add(iv);
                if (p.TryGetProperty("grupo", out var g) && g.ValueKind == JsonValueKind.Object)
                {
                    if (g.TryGetProperty("tipo", out var gt)) s.GrupoTipo = gt.GetString() ?? "reptipe";
                    if (g.TryGetProperty("valores", out var gv) && gv.ValueKind == JsonValueKind.Array)
                        foreach (var e in gv.EnumerateArray()) { var v = e.GetString(); if (!string.IsNullOrEmpty(v)) s.GrupoVals.Add(v); }
                }
                if (s.Ids.Count > 0) lst.Add(s);
            }
            catch { }
        }
        return lst;
    }

    private static string DimCol(string dim) => dim switch
    {
        "producto" => "pr.ProID",
        "suplidor" => "pr.Cat3ID",
        _ => "pr.LinID"   // linea / categoría
    };

    // Calcula, para un rango [d0, d1) agrupando por vendedor y (opcionalmente) por día,
    // las 4 métricas: [clientes con >=MinUnid, unidades, monto, impactos].
    // Devuelve: rep -> (dia 'yyyy-MM-dd' -> double[4]).  Si porDia=false, la llave de día es "".
    public static Dictionary<string, Dictionary<string, double[]>> Calcular(
        SqlConnection cn, CustomSpec s, DateTime d0, DateTime d1, bool porDia)
    {
        var res = new Dictionary<string, Dictionary<string, double[]>>(StringComparer.OrdinalIgnoreCase);
        if (s.Ids.Count == 0) return res;

        var inIds = string.Join(",", s.Ids.Select((_, i) => "@id" + i));
        var horaFiltro = !string.IsNullOrWhiteSpace(s.HoraHasta) ? " AND CAST(p.PedFecha AS time) < @hh" : "";
        // OJO: NO se puede agrupar por un literal constante (CAST('1900-01-01' AS date)) — SQL
        // Server lo rechaza con "Each GROUP BY expression must contain at least one column that
        // is not an outer reference" en cuanto ese literal se propaga por el CTE y se vuelve a
        // agrupar en la consulta externa (bug real, encontrado en vivo con porDia=false — el caso
        // de ValoresHoy/monitor en vivo). Cuando porDia=false hay un solo "día" (todo es "hoy"),
        // así que no hace falta agruparlo: se agrega como literal en el SELECT, fuera del GROUP
        // BY (válido en T-SQL). El literal no se lee del lado C# cuando porDia=false (ver abajo).
        var diaSelectCte  = porDia ? "CAST(p.PedFecha AS date)" : "CAST('1900-01-01' AS date)";
        var diaGroupCte   = porDia ? ", CAST(p.PedFecha AS date)" : "";
        var diaSelectFin  = porDia ? "CONVERT(varchar(10), Dia, 23)" : "'1900-01-01'";
        var diaGroupFin   = porDia ? ", Dia" : "";
        var sql = $@"
;WITH B AS (
  SELECT d.RepCodigo, {diaSelectCte} Dia, d.CliID,
         SUM(ISNULL(d.PedCantidad,0)) U,
         SUM(ISNULL(d.PedCantidad,0)*ISNULL(d.PedPrecio,0)) M,
         COUNT(DISTINCT d.PedSecuencia) Imp
  FROM VWPedidosDetalle d WITH (NOLOCK)
  JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia=d.PedSecuencia AND p.RepCodigo=d.RepCodigo
  JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
  WHERE p.PedFecha>=@d0 AND p.PedFecha<@d1 AND {DimCol(s.Dimension)} IN ({inIds}){horaFiltro}
  GROUP BY d.RepCodigo, d.CliID{diaGroupCte}
)
SELECT RepCodigo, {diaSelectFin} Dia,
  SUM(CASE WHEN U>=@minU THEN 1 ELSE 0 END) Clientes,
  SUM(U) Unidades, SUM(M) Monto, SUM(Imp) Impactos
FROM B GROUP BY RepCodigo{diaGroupFin}";

        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 90 };
        cmd.Parameters.AddWithValue("@d0", d0);
        cmd.Parameters.AddWithValue("@d1", d1);
        cmd.Parameters.AddWithValue("@minU", s.MinUnid);
        if (!string.IsNullOrWhiteSpace(s.HoraHasta)) cmd.Parameters.AddWithValue("@hh", TimeSpan.TryParse(s.HoraHasta, out var ts) ? ts : new TimeSpan(23, 59, 0));
        for (int i = 0; i < s.Ids.Count; i++) cmd.Parameters.AddWithValue("@id" + i, s.Ids[i]);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var rep = rd.GetString(0);
            var dia = porDia ? (rd.IsDBNull(1) ? "" : rd.GetString(1)) : "";
            var arr = new double[4]
            {
                Convert.ToDouble(rd.GetValue(2)), Convert.ToDouble(rd.GetValue(3)),
                Convert.ToDouble(rd.GetValue(4)), Convert.ToDouble(rd.GetValue(5))
            };
            if (!res.TryGetValue(rep, out var byd)) { byd = new(); res[rep] = byd; }
            byd[dia] = arr;
        }
        return res;
    }

    // Valores de HOY por vendedor (una llave de día ""), para el monitor en vivo/snapshots.
    public static Dictionary<string, double[]> ValoresHoy(SqlConnection cn, CustomSpec s)
    {
        var hoy = DateTime.Today; var man = hoy.AddDays(1);
        var m = Calcular(cn, s, hoy, man, porDia: false);
        var o = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rep, byd) in m) o[rep] = byd.TryGetValue("", out var a) ? a : new double[4];
        return o;
    }

    // Cuánto debería llevar acumulado a esta hora si Comparador='ritmo' (regla de 3 entre
    // RitmoInicio y RitmoLimite). Sin 'ahora' (null) => se exige el Objetivo COMPLETO — es el caso
    // de un día ya cerrado (estadística histórica), donde ya no aplica proyectar, se evalúa el total.
    public static double EsperadoRitmo(CustomSpec s, DateTime? ahora)
    {
        if (ahora is null) return s.Objetivo;
        var ini = s.RitmoInicio ?? new TimeSpan(8, 20, 0);
        var lim = s.RitmoLimite ?? new TimeSpan(15, 0, 0);
        var t = ahora.Value.TimeOfDay;
        if (t <= ini) return 0;
        if (t >= lim) return s.Objetivo;
        var totalMin = (lim - ini).TotalMinutes;
        return totalMin <= 0 ? s.Objetivo : s.Objetivo * (t - ini).TotalMinutes / totalMin;
    }

    // ¿Cumple el objetivo? (true = OK, false = en alarma), dado el valor de la métrica.
    // 'ahora' solo se usa para comparador='ritmo' (null = evaluar como día cerrado, exige el Objetivo completo).
    public static bool Cumple(CustomSpec s, double valor, DateTime? ahora = null)
    {
        if (s.Comparador == "menor") return valor < s.Objetivo;
        if (s.Comparador == "ritmo") return valor >= EsperadoRitmo(s, ahora);
        return valor >= s.Objetivo;
    }

    // Construye la celda del monitor en vivo para un vendedor.
    public static TorreCore.Celda Celda(CustomSpec s, TorreCore.RepMetric m, Dictionary<string, double[]> valores)
    {
        var c = new TorreCore.Celda { Codigo = s.Codigo, Severidad = s.Severidad };
        if (!s.AplicaA(m)) { c.Texto = "n/a"; c.Alarma = false; c.Detalle = "no aplica al grupo"; return c; }
        var arr = valores.TryGetValue(m.RepCodigo, out var a) ? a : new double[4];
        double v = arr[s.MetricaIdx];
        c.Valor = Math.Round(v, s.Metrica == "monto" ? 0 : 0);
        c.Texto = s.Metrica == "monto" ? "RD$" + v.ToString("#,0") : v.ToString("0") + " " + s.Unidad;
        var ahora = DateTime.Now;
        bool ok = Cumple(s, v, ahora);
        c.Alarma = !ok;
        if (s.Comparador == "ritmo")
        {
            var esperado = EsperadoRitmo(s, ahora);
            var lim = s.RitmoLimite ?? new TimeSpan(15, 0, 0);
            c.Detalle = $"{c.Texto} · esperado {esperado:0.#} a esta hora (objetivo {s.Objetivo:0} antes de {lim:hh\\:mm}) — {(ok ? "va bien" : "atrasado")}";
        }
        else
        {
            c.Detalle = $"{c.Texto} de {s.Objetivo:0} objetivo ({(ok ? "cumple" : "por debajo")})";
        }
        return c;
    }
}
