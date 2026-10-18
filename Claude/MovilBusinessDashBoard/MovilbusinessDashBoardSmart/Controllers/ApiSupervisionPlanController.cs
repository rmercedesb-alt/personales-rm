using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ClosedXML.Excel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DashVisitas.Controllers;

// ═════════════════════════════════════════════════════════════════════════════
//  PLAN DE TRABAJO SEMANAL DEL SUPERVISOR (Supervisión Dinámica → Plan) — pedido del usuario 2026-10-10
//  Rutas /apisuperv/plan/…  (partial de ApiSupervisionController: reutiliza Identidad/RepFilter/NewConn/IsAuth).
//    GET  plan/contexto     → supervisores permitidos, equipo (con cronograma por vendedor), tipo de cada día del rango
//    GET  plan/lista        → planes visibles (con % de logro vivo)          GET  plan/detalle?id=  → plan + actividades + ejecución + métricas
//    POST plan/guardar      → crea/actualiza un plan completo (cabecera + actividades)
//    POST plan/actividad / plan/actividad-eliminar → alta/edición/baja de una actividad (p.ej. actividad adicional en ejecución)
//    POST plan/ejecucion / plan/ejecucion-eliminar → registra el trabajo realizado de una actividad (fecha, vendedor/cliente atendido, resultado)
//    POST plan/estatus      → activar un borrador / reabrir (solo Gerencia)  POST plan/cerrar → cierre semanal (congela el % de logro)
//    POST plan/eliminar     → borra un plan en Borrador
//    GET  plan/plantilla / plan/excel?id= / POST plan/importar → plantilla Excel para cargar un plan ya realizado, exportación y carga
//  Tablas: SQL/SupervisionPlanTrabajo_RM.sql (las crea TI). % de logro = Σ(Peso×valor)/ΣPeso de las actividades del plan
//  (Realizada=100, Parcial=50, resto=0; las Canceladas y las «extra» (Peso 0) no cuentan).
// ═════════════════════════════════════════════════════════════════════════════
public partial class ApiSupervisionController
{
    private const string TRec = "dbo.SupervisionPlanTrabajoRec_RM", TPlan = "dbo.SupervisionPlanTrabajo_RM", TAct = "dbo.SupervisionPlanTrabajoAct_RM", TEje = "dbo.SupervisionPlanTrabajoEje_RM";
    private static readonly string[] PTipos = { "calle", "virtual", "cobros", "reunion", "gestion", "capacitacion", "otro" };
    private static readonly string[] PTiposNom = { "Calle", "Telemática", "Cobros", "Reunión", "Gestión", "Capacitación", "Otro" };
    private static readonly string[] PEstados = { "Pendiente", "Realizada", "Parcial", "No realizada", "Reprogramada", "Cancelada" };
    private static readonly string[] PNombreDia = { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };

    public sealed record ActIn(int? actId, string fecha, string? horaIni, string? horaFin, string tipo, string? subtipo, string titulo, string? detalle,
        string? repCodigo, string? repNombre, int? cliId, string? cliNombre, string? meta, string? prioridad, decimal? peso, bool? fijo, string? origen, int? orden,
        string? modalidad = null, string? alcance = null, string? alcanceCod = null, string? alcanceNombre = null);
    public sealed record RecIn(int? recId, string supCod, int dia, string? horaIni, string? horaFin, string titulo, string? modalidad, string? alcance, string? alcanceCod, string? alcanceNombre, string? meta);
    public sealed record PlanIn(int? planId, string supCod, string desde, string hasta, string? objetivo, string? opcion, string? estatus, string? origen, List<ActIn>? actividades);
    public sealed record EjeIn(int actId, string? fechaRealizada, string? horaRealizada, string? trabajo, string? repAtendido, string? repAtendidoNombre,
        int? cliId, string? cliNombre, string? resultado, string estado);
    public sealed record ActEdit(int planId, ActIn act);
    public sealed record IdReq(int id, int? actId, int? ejeId);
    public sealed record EstatusReq(int id, string estatus, string? comentario = null);
    public sealed record CierreReq(int id, string? logros, string? aprendizajes, string? compromisos);

    // ── Una PERSONA puede tener varios códigos de supervisor (grupos/divisiones: p. ej. Ramón = S1+S4+S7, Eliezer = S2+S5, Cristiano = S12+S13).
    //    El plan es UNO por persona; se agrupan los códigos por el nombre del supervisor sin el sufijo entre paréntesis. El código principal es el primero.
    private sealed record GrupoSup(string Primary, string Nombre, List<(string cod, string etiqueta)> Codes);
    private static List<GrupoSup>? _grpCache; private static DateTime _grpT; private static readonly object _grpLock = new();
    private static List<GrupoSup> Grupos(SqlConnection cn)
    {
        lock (_grpLock)
        {
            if (_grpCache != null && (DateTime.Now - _grpT).TotalMinutes < 10) return _grpCache;
            var raw = new List<(string cod, string nom)>();
            using (var cmd = new SqlCommand(@"
SELECT DISTINCT r.RepSupervisor, LTRIM(RTRIM(ISNULL(sup.RepNombre, r.RepSupervisor)))
FROM Representantes r WITH (NOLOCK) LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor' AND ISNULL(r.RepSupervisor,'')<>''", cn) { CommandTimeout = 60 })
            using (var rd = cmd.ExecuteReader()) while (rd.Read()) raw.Add((rd.GetString(0).Trim(), rd.IsDBNull(1) ? rd.GetString(0) : rd.GetString(1)));
            var porNombre = new Dictionary<string, List<(string cod, string etiqueta)>>(StringComparer.OrdinalIgnoreCase); var nombres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (cod, nom) in raw)
            {
                var m = Regex.Match(nom, @"^(.*?)\s*\((.*)$"); var baseN = Regex.Replace(m.Success ? m.Groups[1].Value : nom, @"\s+", " ").Trim(); if (baseN == "") baseN = cod;
                var et = m.Success ? m.Groups[2].Value.TrimEnd(')', ' ') : ""; if (!porNombre.TryGetValue(baseN, out var l)) { porNombre[baseN] = l = new(); nombres[baseN] = baseN; }
                l.Add((cod, et == "" ? cod : cod + " · " + et));
            }
            var res = porNombre.Select(kv => { var cs = kv.Value.OrderBy(c => c.cod.Length).ThenBy(c => c.cod, StringComparer.OrdinalIgnoreCase).ToList(); return new GrupoSup(cs[0].cod, nombres[kv.Key], cs); }).OrderBy(g => g.Nombre).ToList();
            _grpCache = res; _grpT = DateTime.Now; return res;
        }
    }
    private static GrupoSup? GrupoDe(SqlConnection cn, string? cod) => string.IsNullOrWhiteSpace(cod) ? null : Grupos(cn).FirstOrDefault(g => g.Codes.Any(c => c.cod.Equals(cod.Trim(), StringComparison.OrdinalIgnoreCase)));
    /// Acceso a un plan/supervisor: Gerencia, o un usuario mapeado a CUALQUIER código del grupo de esa persona.
    private bool AccesoSupG(SqlConnection cn, Ident id, string? sup)
    {
        if (id.Rol == "Ninguno") return false; if (id.VeTodo) return true;
        var g = GrupoDe(cn, sup); var cods = g != null ? g.Codes.Select(c => c.cod) : new[] { (sup ?? "").Trim() };
        return cods.Any(c => id.SupCods.Contains(c, StringComparer.OrdinalIgnoreCase));
    }
    private bool AccesoSup(Ident id, string? sup) =>
        id.Rol != "Ninguno" && (id.VeTodo || (sup != null && id.SupCods.Contains(sup.Trim(), StringComparer.OrdinalIgnoreCase)));
    private static string? Txt(string? s, int max) { if (string.IsNullOrWhiteSpace(s)) return null; s = s.Trim(); return s.Length > max ? s[..max] : s; }
    private static bool HoraOk(string? h) => string.IsNullOrEmpty(h) || Regex.IsMatch(h, @"^([01]\d|2[0-3]):[0-5]\d$");
    private static bool TryFecha(string? s, out DateTime d) => DateTime.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
    private static double ValorEstado(string e) => e == "Realizada" ? 100 : e == "Parcial" ? 50 : 0;
    private static string Semana(DateTime d) => $"{ISOWeek.GetYear(d)}-W{ISOWeek.GetWeekOfYear(d):00}";
    private static string TipoNom(string k) { var i = Array.IndexOf(PTipos, k); return i >= 0 ? PTiposNom[i] : k; }

    private sealed class PlanH
    {
        public int PlanID; public string Numero = "", SupCod = "", SupNombre = "", Objetivo = "", Opcion = "", Estatus = "", Origen = "", CreadoPor = "", CerradoPor = "";
        public DateTime Ini, Fin, FechaCreacion; public DateTime? FechaCierre; public string? Semana;
        public double? LogroPct; public string? CierreLogros, CierreAprendizajes, CierreCompromisos, EnviadoPor, AprobadoPor, ComentarioGerencia; public DateTime? FechaEnvio, FechaAprobacion;
    }
    private sealed class ActR
    {
        public int ActID, PlanID, Orden; public DateTime Fecha; public string? HoraIni, HoraFin, Subtipo, Detalle, RepCodigo, RepNombre, CliNombre, Meta;
        public string Tipo = "", Titulo = "", Prioridad = "M", Origen = "plan", Estado = "Pendiente"; public int? CliID; public double Peso = 1; public bool Fijo; public string? Modalidad, Alcance, AlcanceCod, AlcanceNombre;
    }

    private const string SqlPlanCols = "p.PlanID, p.PlanNumero, p.SupCod, ISNULL(p.SupNombre,''), p.FechaIni, p.FechaFin, p.Semana, ISNULL(p.Objetivo,''), ISNULL(p.Opcion,''), p.Estatus, p.Origen, p.LogroPct, p.CierreLogros, p.CierreAprendizajes, p.CierreCompromisos, p.CreadoPor, p.FechaCreacion, ISNULL(p.CerradoPor,''), p.FechaCierre, p.EnviadoPor, p.FechaEnvio, p.AprobadoPor, p.FechaAprobacion, p.ComentarioGerencia";
    private static PlanH LeerPlanH(SqlDataReader r) => new()
    {
        PlanID = r.GetInt32(0), Numero = r.GetString(1), SupCod = r.GetString(2), SupNombre = r.GetString(3), Ini = r.GetDateTime(4), Fin = r.GetDateTime(5),
        Semana = r.IsDBNull(6) ? null : r.GetString(6), Objetivo = r.GetString(7), Opcion = r.GetString(8), Estatus = r.GetString(9), Origen = r.GetString(10),
        LogroPct = r.IsDBNull(11) ? null : Convert.ToDouble(r.GetValue(11)), CierreLogros = r.IsDBNull(12) ? null : r.GetString(12),
        CierreAprendizajes = r.IsDBNull(13) ? null : r.GetString(13), CierreCompromisos = r.IsDBNull(14) ? null : r.GetString(14),
        CreadoPor = r.GetString(15), FechaCreacion = r.GetDateTime(16), CerradoPor = r.GetString(17), FechaCierre = r.IsDBNull(18) ? null : r.GetDateTime(18),
        EnviadoPor = r.IsDBNull(19) ? null : r.GetString(19), FechaEnvio = r.IsDBNull(20) ? null : r.GetDateTime(20), AprobadoPor = r.IsDBNull(21) ? null : r.GetString(21),
        FechaAprobacion = r.IsDBNull(22) ? null : r.GetDateTime(22), ComentarioGerencia = r.IsDBNull(23) ? null : r.GetString(23)
    };
    private const string SqlActCols = "ActID, PlanID, Fecha, HoraIni, HoraFin, Tipo, Subtipo, Titulo, Detalle, RepCodigo, RepNombre, CliID, CliNombre, Meta, Prioridad, Peso, Fijo, Origen, Estado, Orden, Modalidad, Alcance, AlcanceCod, AlcanceNombre";
    private static ActR LeerAct(SqlDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new ActR
        {
            ActID = r.GetInt32(0), PlanID = r.GetInt32(1), Fecha = r.GetDateTime(2), HoraIni = S(3), HoraFin = S(4), Tipo = r.GetString(5), Subtipo = S(6), Titulo = r.GetString(7),
            Detalle = S(8), RepCodigo = S(9), RepNombre = S(10), CliID = r.IsDBNull(11) ? null : r.GetInt32(11), CliNombre = S(12), Meta = S(13), Prioridad = r.GetString(14).Trim(),
            Peso = Convert.ToDouble(r.GetValue(15)), Fijo = r.GetBoolean(16), Origen = r.GetString(17), Estado = r.GetString(18), Orden = r.GetInt32(19),
            Modalidad = S(20), Alcance = S(21), AlcanceCod = S(22), AlcanceNombre = S(23)
        };
    }

    // ── Métricas de logro de un conjunto de actividades ─────────────────────────────────────────
    private static object Metricas(IEnumerable<ActR> acts, DateTime hoy)
    {
        var lista = acts.ToList();
        var plan = lista.Where(a => a.Origen == "plan" && a.Estado != "Cancelada" && a.Peso > 0).ToList();
        double sp = plan.Sum(a => a.Peso), sv = plan.Sum(a => a.Peso * ValorEstado(a.Estado));
        var hasta = plan.Where(a => a.Fecha.Date <= hoy.Date).ToList();
        double sph = hasta.Sum(a => a.Peso), svh = hasta.Sum(a => a.Peso * ValorEstado(a.Estado));
        double? logro = sp > 0 ? Math.Round(sv / sp, 1) : null, aFecha = sph > 0 ? Math.Round(svh / sph, 1) : null;
        var nivel = logro == null ? "Sin actividades" : logro >= 90 ? "Excelente" : logro >= 75 ? "En ruta" : logro >= 50 ? "Atrasado" : "Crítico";
        return new
        {
            total = plan.Count, realizadas = plan.Count(a => a.Estado == "Realizada"), parciales = plan.Count(a => a.Estado == "Parcial"),
            pendientes = plan.Count(a => a.Estado == "Pendiente"), noRealizadas = plan.Count(a => a.Estado == "No realizada"),
            reprogramadas = plan.Count(a => a.Estado == "Reprogramada"), canceladas = lista.Count(a => a.Origen == "plan" && a.Estado == "Cancelada"),
            extras = lista.Count(a => a.Origen == "extra"), vencidas = hasta.Count(a => a.Estado == "Pendiente"), logroPct = logro, logroAFechaPct = aFecha, nivel
        };
    }
    private static double? LogroDe(IEnumerable<ActR> acts)
    {
        var plan = acts.Where(a => a.Origen == "plan" && a.Estado != "Cancelada" && a.Peso > 0).ToList();
        double sp = plan.Sum(a => a.Peso); return sp > 0 ? Math.Round(plan.Sum(a => a.Peso * ValorEstado(a.Estado)) / sp, 1) : null;
    }

    private object ActJson(ActR a, Dictionary<int, List<object>>? eje = null) => new
    {
        actId = a.ActID, fecha = a.Fecha.ToString("yyyy-MM-dd"), horaIni = a.HoraIni, horaFin = a.HoraFin, tipo = a.Tipo, subtipo = a.Subtipo, titulo = a.Titulo, detalle = a.Detalle,
        repCodigo = a.RepCodigo, repNombre = a.RepNombre, cliId = a.CliID, cliNombre = a.CliNombre, meta = a.Meta, prioridad = a.Prioridad, peso = a.Peso, fijo = a.Fijo,
        origen = a.Origen, estado = a.Estado, orden = a.Orden, modalidad = a.Modalidad, alcance = a.Alcance, alcanceCod = a.AlcanceCod, alcanceNombre = a.AlcanceNombre, ejecuciones = eje != null && eje.TryGetValue(a.ActID, out var l) ? l : new List<object>()
    };
    private object PlanJson(PlanH p, IEnumerable<ActR>? acts = null) => new
    {
        planId = p.PlanID, numero = p.Numero, supCod = p.SupCod, supNombre = p.SupNombre, desde = p.Ini.ToString("yyyy-MM-dd"), hasta = p.Fin.ToString("yyyy-MM-dd"), semana = p.Semana,
        objetivo = p.Objetivo, opcion = p.Opcion, estatus = p.Estatus, origen = p.Origen, logroCongelado = p.LogroPct, creadoPor = p.CreadoPor, fechaCreacion = p.FechaCreacion.ToString("yyyy-MM-dd HH:mm"),
        cerradoPor = p.CerradoPor, fechaCierre = p.FechaCierre?.ToString("yyyy-MM-dd HH:mm"), cierreLogros = p.CierreLogros, cierreAprendizajes = p.CierreAprendizajes, cierreCompromisos = p.CierreCompromisos,
        enviadoPor = p.EnviadoPor, fechaEnvio = p.FechaEnvio?.ToString("yyyy-MM-dd HH:mm"), aprobadoPor = p.AprobadoPor, fechaAprobacion = p.FechaAprobacion?.ToString("yyyy-MM-dd HH:mm"), comentarioGerencia = p.ComentarioGerencia,
        limiteCierre = LimiteCierre(p).ToString("yyyy-MM-dd"),
        sinCerrar = p.Estatus == "Activo" && DateTime.Today > LimiteCierre(p),                                                   // Activo y ya pasó el viernes sin cerrarse
        cierreTardio = p.Estatus == "Cerrado" && p.Origen != "excel" && p.FechaCierre != null && p.FechaCierre.Value.Date > LimiteCierre(p),
        metricas = acts == null ? null : Metricas(acts, DateTime.Today)
    };
    /// Viernes de la semana del plan (día límite para que el supervisor cierre); si el plan termina antes, su último día.
    private static DateTime LimiteCierre(PlanH p)
    {
        var vie = p.Ini.Date.AddDays(-(((int)p.Ini.DayOfWeek + 6) % 7) + 4); if (vie < p.Ini.Date) vie = p.Ini.Date;
        return p.Fin.Date < vie ? p.Fin.Date : vie;
    }
    /// Reglas del flujo Borrador → Por aprobar → Activo → Cerrado. Devuelve el error o null.
    private static string? Transicion(string actual, string nuevo, bool ger)
    {
        if (actual == "Borrador" && nuevo == "Por aprobar") return null;
        if ((actual == "Borrador" || actual == "Por aprobar") && nuevo == "Activo") return ger ? null : "Solo Gerencia puede aprobar un plan.";
        if (actual == "Por aprobar" && nuevo == "Borrador") return null;     // el supervisor lo retira o Gerencia lo devuelve
        if (actual == "Cerrado" && nuevo == "Activo") return ger ? null : "Solo Gerencia puede reabrir un plan cerrado.";
        return $"Cambio de estatus no permitido ({actual} → {nuevo}).";
    }
    private static void CambiarEstatus(SqlConnection cn, SqlTransaction? tx, int planId, string actual, string nuevo, string user, string? comentario)
    {
        string sql = actual == "Cerrado"
            ? $"UPDATE {TPlan} SET Estatus='Activo', LogroPct=NULL, CerradoPor=NULL, FechaCierre=NULL, ActualizadoPor=@u, FechaActualizacion=GETDATE() WHERE PlanID=@p"
            : nuevo == "Por aprobar" ? $"UPDATE {TPlan} SET Estatus='Por aprobar', EnviadoPor=@u, FechaEnvio=GETDATE(), ActualizadoPor=@u, FechaActualizacion=GETDATE() WHERE PlanID=@p"
            : nuevo == "Activo" ? $"UPDATE {TPlan} SET Estatus='Activo', AprobadoPor=@u, FechaAprobacion=GETDATE(), ComentarioGerencia=@c, ActualizadoPor=@u, FechaActualizacion=GETDATE() WHERE PlanID=@p"
            : $"UPDATE {TPlan} SET Estatus='Borrador', AprobadoPor=NULL, FechaAprobacion=NULL, ComentarioGerencia=@c, ActualizadoPor=@u, FechaActualizacion=GETDATE() WHERE PlanID=@p";
        using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@u", user); cmd.Parameters.AddWithValue("@p", planId); cmd.Parameters.AddWithValue("@c", (object?)Txt(comentario, 500) ?? DBNull.Value); cmd.ExecuteNonQuery();
    }

    private List<ActR> LeerActs(SqlConnection cn, int planId, SqlTransaction? tx = null)
    {
        using var cmd = new SqlCommand($"SELECT {SqlActCols} FROM {TAct} WITH (NOLOCK) WHERE PlanID=@p ORDER BY Fecha, ISNULL(HoraIni,'99:99'), Orden, ActID", cn, tx);
        cmd.Parameters.AddWithValue("@p", planId);
        var l = new List<ActR>(); using var rd = cmd.ExecuteReader(); while (rd.Read()) l.Add(LeerAct(rd)); return l;
    }
    private PlanH? LeerPlan(SqlConnection cn, int planId, SqlTransaction? tx = null)
    {
        using var cmd = new SqlCommand($"SELECT {SqlPlanCols} FROM {TPlan} p WHERE p.PlanID=@p", cn, tx); cmd.Parameters.AddWithValue("@p", planId);
        using var rd = cmd.ExecuteReader(); return rd.Read() ? LeerPlanH(rd) : null;
    }
    private static string ErrTabla(Exception ex) =>
        ex is SqlException se && (se.Number == 208 || se.Number == 207) ? "Faltan las tablas/columnas del Plan de Trabajo: pide a TI correr (de nuevo) SQL/SupervisionPlanTrabajo_RM.sql. (" + se.Message + ")" : ex.Message;

    // ═══════════════ GET plan/contexto ═══════════════
    [HttpGet("plan/contexto")]
    public IActionResult PlanContexto([FromQuery] string? supcod, [FromQuery] string? desde, [FromQuery] string? hasta)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (id.Rol == "Ninguno") return Ok(new { ok = false, error = "Usuario sin acceso configurado (SupervisionUsuarioMapa_RM)." });
            // Supervisores = PERSONAS (un plan por persona aunque tenga varios grupos/divisiones)
            var todos = Grupos(cn);
            var visibles = todos.Where(g => id.VeTodo || g.Codes.Any(c => id.SupCods.Contains(c.cod, StringComparer.OrdinalIgnoreCase))).ToList();
            GrupoSup? g = null;
            if (!string.IsNullOrWhiteSpace(supcod)) { g = todos.FirstOrDefault(x => x.Codes.Any(c => c.cod.Equals(supcod.Trim(), StringComparison.OrdinalIgnoreCase))); if (g != null && !visibles.Contains(g)) return Ok(new { ok = false, error = "No tienes acceso a ese supervisor." }); }
            g ??= visibles.FirstOrDefault();
            var sc = g?.Primary ?? ""; var supNombre = g?.Nombre ?? ""; var codigos = g?.Codes.Select(c => c.cod).ToList() ?? new List<string>();
            var inCods = codigos.Select((c, k) => "@c" + k).ToList();
            void AddCods(SqlCommand cmd) { for (int k = 0; k < codigos.Count; k++) cmd.Parameters.AddWithValue("@c" + k, codigos[k]); }

            // Equipo (de TODOS los grupos de la persona) + cronograma por vendedor (override de rep > patrón del supervisor > default)
            var vend = new List<(string cod, string nom, string div, string grp)>();
            if (codigos.Count > 0)
                using (var cmd = new SqlCommand($@"
SELECT r.RepCodigo, LTRIM(RTRIM(r.RepNombre)), ISNULL(ra.RepTipe,''), r.RepSupervisor FROM Representantes r WITH (NOLOCK)
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor' AND r.RepSupervisor IN ({string.Join(",", inCods)}) ORDER BY r.RepNombre", cn) { CommandTimeout = 60 })
                {
                    AddCods(cmd);
                    using var rd = cmd.ExecuteReader(); while (rd.Read()) vend.Add((rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2), rd.GetString(3).Trim()));
                }
            var patron = (string[])CronoDefault.Clone(); var fijado = new bool[6];
            if (codigos.Count > 0)
                try
                {
                    using var cmd = new SqlCommand($"SELECT SupCod, DiaSemana, Tipo FROM dbo.SupervisionSupDias_RM WITH (NOLOCK) WHERE SupCod IN ({string.Join(",", inCods)})", cn); AddCods(cmd);
                    var filas = new List<(string cod, int d, string t)>(); using (var rd = cmd.ExecuteReader()) while (rd.Read()) filas.Add((rd.GetString(0).Trim(), Convert.ToInt32(rd.GetValue(1)), rd.GetString(2)));
                    foreach (var cod in codigos) foreach (var f in filas.Where(x => x.cod.Equals(cod, StringComparison.OrdinalIgnoreCase))) if (f.d is >= 1 and <= 6 && !fijado[f.d - 1]) { patron[f.d - 1] = f.t; fijado[f.d - 1] = true; }
                }
                catch { }
            var ov = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var prio = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var ultPres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (codigos.Count > 0)
                try
                {
                    using var cmd = new SqlCommand($"SELECT RepCodigo, DiaSemana, Tipo FROM dbo.SupervisionCronograma_RM WITH (NOLOCK) WHERE SupCod IN ({string.Join(",", inCods)})", cn); AddCods(cmd);
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) { var rep = rd.GetString(0); int d = Convert.ToInt32(rd.GetValue(1)); if (d is < 1 or > 6) continue; if (!ov.TryGetValue(rep, out var a)) ov[rep] = a = (string[])patron.Clone(); a[d - 1] = rd.GetString(2); }
                }
                catch { }
            try { using var cmd = new SqlCommand("SELECT RepCodigo FROM dbo.SupervisionRepConfig_RM WITH (NOLOCK) WHERE Prioritario=1", cn); using var rd = cmd.ExecuteReader(); while (rd.Read()) prio.Add(rd.GetString(0)); } catch { }
            try { using var cmd = new SqlCommand("SELECT RepCodigo, MAX(Fecha) FROM dbo.SupervisionEvaluaciones_RM WITH (NOLOCK) WHERE Tipo='acomp' GROUP BY RepCodigo", cn); using var rd = cmd.ExecuteReader(); while (rd.Read()) if (!rd.IsDBNull(1)) ultPres[rd.GetString(0)] = rd.GetDateTime(1).ToString("yyyy-MM-dd"); } catch { }

            // Tipo de cada día del rango (patrón del supervisor; domingo = libre)
            var dias = new List<object>();
            DateTime d0 = default, d1 = default;
            if (TryFecha(desde, out d0) && TryFecha(hasta, out d1) && d1 >= d0 && (d1 - d0).TotalDays <= 20)
                for (var d = d0; d <= d1; d = d.AddDays(1))
                {
                    int dow = (int)d.DayOfWeek; var tipo = dow == 0 ? "libre" : patron[dow - 1];
                    dias.Add(new { fecha = d.ToString("yyyy-MM-dd"), dow, nombre = PNombreDia[dow], tipo, calle = tipo is "presencial" or "auditoria" });
                }
            // Planes de la persona que se cruzan con el rango (aviso) y reuniones fijas semanales. Las tablas pueden no existir aún.
            var existentes = new List<object>(); var recurrentes = new List<object>(); string? avisoTablas = null;
            if (codigos.Count > 0)
            {
                try
                {
                    using var cmd = new SqlCommand($"SELECT PlanID, PlanNumero, Estatus, FechaIni, FechaFin FROM {TPlan} WITH (NOLOCK) WHERE SupCod IN ({string.Join(",", inCods)}) AND FechaIni<=@h AND FechaFin>=@d ORDER BY FechaIni", cn); AddCods(cmd);
                    cmd.Parameters.AddWithValue("@d", d0 == default ? DateTime.Today : d0); cmd.Parameters.AddWithValue("@h", d1 == default ? DateTime.Today : d1);
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) existentes.Add(new { planId = rd.GetInt32(0), numero = rd.GetString(1), estatus = rd.GetString(2), desde = rd.GetDateTime(3).ToString("yyyy-MM-dd"), hasta = rd.GetDateTime(4).ToString("yyyy-MM-dd") });
                }
                catch (Exception ex) { avisoTablas = ErrTabla(ex); }
                try
                {
                    using var cmd = new SqlCommand($"SELECT RecID, Dia, HoraIni, HoraFin, Titulo, Modalidad, Alcance, AlcanceCod, AlcanceNombre, Meta FROM {TRec} WITH (NOLOCK) WHERE SupCod IN ({string.Join(",", inCods)}) AND Activo=1 ORDER BY Dia, HoraIni", cn); AddCods(cmd);
                    using var rd = cmd.ExecuteReader(); string? S(int k) => rd.IsDBNull(k) ? null : rd.GetString(k);
                    while (rd.Read()) recurrentes.Add(new { recId = rd.GetInt32(0), dia = Convert.ToInt32(rd.GetValue(1)), horaIni = S(2), horaFin = S(3), titulo = rd.GetString(4), modalidad = S(5), alcance = S(6), alcanceCod = S(7), alcanceNombre = S(8), meta = S(9) });
                }
                catch (Exception ex) { avisoTablas ??= ErrTabla(ex); }
            }
            return Ok(new
            {
                ok = true, rol = id.Rol, veTodo = id.VeTodo, usuario = id.User, supcod = sc, supNombre, codigos,
                supervisores = visibles.Select(v => new { supcod = v.Primary, nombre = v.Nombre, codigos = v.Codes.Select(c => c.cod) }),
                grupos = g == null ? new List<object>() : g.Codes.Select(c => (object)new { cod = c.cod, etiqueta = c.etiqueta, vendedores = vend.Count(v => v.grp.Equals(c.cod, StringComparison.OrdinalIgnoreCase)) }).ToList(),
                divisiones = vend.Where(v => v.div != "").GroupBy(v => v.div).OrderBy(x => x.Key).Select(x => new { cod = x.Key, vendedores = x.Count() }),
                vendedores = vend.Select(v => new { rep = v.cod, nombre = v.nom, division = v.div, grupo = v.grp, prioritario = prio.Contains(v.cod), tipos = ov.TryGetValue(v.cod, out var t) ? t : patron, ultPresencial = ultPres.TryGetValue(v.cod, out var up) ? up : null }),
                patron, dias, existentes, recurrentes, avisoTablas
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ GET plan/lista ═══════════════
    [HttpGet("plan/lista")]
    public IActionResult PlanLista([FromQuery] string? supcod, [FromQuery] string? desde, [FromQuery] string? hasta, [FromQuery] string? estatus)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (id.Rol == "Ninguno") return Ok(new { ok = true, planes = new List<object>() });
            var w = new List<string>(); var prm = new List<KeyValuePair<string, object>>();
            if (!id.VeTodo)
            {   // cualquier código de la persona (Ramón = S1+S4+S7) da acceso a los planes de esa persona
                var mis = Grupos(cn).Where(g => g.Codes.Any(c => id.SupCods.Contains(c.cod, StringComparer.OrdinalIgnoreCase))).SelectMany(g => g.Codes.Select(c => c.cod)).Union(id.SupCods, StringComparer.OrdinalIgnoreCase).ToList();
                if (mis.Count == 0) return Ok(new { ok = true, planes = new List<object>() });
                var names = mis.Select((s, i) => { prm.Add(new($"@sc{i}", s)); return $"@sc{i}"; });
                w.Add($"p.SupCod IN ({string.Join(",", names)})");
            }
            if (!string.IsNullOrWhiteSpace(supcod))
            {
                var gs = GrupoDe(cn, supcod); var cs = gs != null ? gs.Codes.Select(c => c.cod).ToList() : new List<string> { supcod.Trim() };
                w.Add($"p.SupCod IN ({string.Join(",", cs.Select((c, i) => { prm.Add(new($"@sup{i}", c)); return $"@sup{i}"; }))})");
            }
            if (TryFecha(desde, out var d0)) { w.Add("p.FechaFin>=@d0"); prm.Add(new("@d0", d0)); }
            if (TryFecha(hasta, out var d1)) { w.Add("p.FechaIni<=@d1"); prm.Add(new("@d1", d1)); }
            var est = (estatus ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(e => e is "Borrador" or "Por aprobar" or "Activo" or "Cerrado").ToList();
            if (est.Count > 0) { w.Add($"p.Estatus IN ({string.Join(",", est.Select((e, i) => { prm.Add(new($"@e{i}", e)); return $"@e{i}"; }))})"); }
            var planes = new List<PlanH>();
            using (var cmd = new SqlCommand($"SELECT {SqlPlanCols} FROM {TPlan} p WITH (NOLOCK) {(w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "")} ORDER BY p.FechaIni DESC, p.PlanID DESC", cn) { CommandTimeout = 60 })
            {
                foreach (var kv in prm) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
                using var rd = cmd.ExecuteReader(); while (rd.Read()) planes.Add(LeerPlanH(rd));
            }
            var porPlan = new Dictionary<int, List<ActR>>();
            if (planes.Count > 0)
            {
                var ids = string.Join(",", planes.Select(p => p.PlanID.ToString(CultureInfo.InvariantCulture)));   // enteros propios: sin inyección
                using var cmd = new SqlCommand($"SELECT {SqlActCols} FROM {TAct} WITH (NOLOCK) WHERE PlanID IN ({ids})", cn) { CommandTimeout = 60 };
                using var rd = cmd.ExecuteReader(); while (rd.Read()) { var a = LeerAct(rd); if (!porPlan.TryGetValue(a.PlanID, out var l)) porPlan[a.PlanID] = l = new(); l.Add(a); }
            }
            return Ok(new { ok = true, rol = id.Rol, planes = planes.Select(p => PlanJson(p, porPlan.TryGetValue(p.PlanID, out var l) ? l : new List<ActR>())) });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ GET plan/detalle ═══════════════
    [HttpGet("plan/detalle")]
    public IActionResult PlanDetalle([FromQuery] int id)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var ident = Identidad(cn);
            var p = LeerPlan(cn, id);
            if (p == null) return Ok(new { ok = false, error = "El plan no existe." });
            if (!AccesoSupG(cn, ident, p.SupCod)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
            var acts = LeerActs(cn, id);
            var eje = new Dictionary<int, List<object>>();
            using (var cmd = new SqlCommand($"SELECT EjeID, ActID, FechaRealizada, HoraRealizada, TrabajoRealizado, RepAtendido, RepAtendidoNombre, CliID, CliNombre, Resultado, Estado, RegistradoPor, FechaRegistro FROM {TEje} WITH (NOLOCK) WHERE PlanID=@p ORDER BY FechaRealizada, EjeID", cn))
            {
                cmd.Parameters.AddWithValue("@p", id);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string? S(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
                    int aid = rd.GetInt32(1); if (!eje.TryGetValue(aid, out var l)) eje[aid] = l = new();
                    l.Add(new { ejeId = rd.GetInt32(0), actId = aid, fechaRealizada = rd.GetDateTime(2).ToString("yyyy-MM-dd"), horaRealizada = S(3), trabajo = S(4), repAtendido = S(5), repAtendidoNombre = S(6),
                        cliId = rd.IsDBNull(7) ? (int?)null : rd.GetInt32(7), cliNombre = S(8), resultado = S(9), estado = rd.GetString(10), registradoPor = rd.GetString(11), fechaRegistro = rd.GetDateTime(12).ToString("yyyy-MM-dd HH:mm") });
                }
            }
            // Avance por día y por tipo
            object Agr(IEnumerable<IGrouping<string, ActR>> gs) => gs.Select(g => new { clave = g.Key, metricas = Metricas(g, DateTime.Today) }).ToList();
            return Ok(new
            {
                ok = true, plan = PlanJson(p, acts), actividades = acts.Select(a => ActJson(a, eje)),
                porDia = Agr(acts.GroupBy(a => a.Fecha.ToString("yyyy-MM-dd")).OrderBy(g => g.Key)), porTipo = Agr(acts.GroupBy(a => a.Tipo).OrderBy(g => g.Key)),
                rol = ident.Rol, esViernes = DateTime.Today.DayOfWeek == DayOfWeek.Friday,
                puedeEditar = p.Estatus == "Borrador" || (ident.VeTodo && p.Estatus is "Por aprobar" or "Activo"), puedeEjecutar = p.Estatus == "Activo",
                puedeCerrar = p.Estatus == "Activo" && (ident.VeTodo || DateTime.Today >= LimiteCierre(p)), puedeAprobar = ident.VeTodo && p.Estatus == "Por aprobar",
                puedeEnviar = p.Estatus == "Borrador", puedeRetirar = p.Estatus == "Por aprobar", puedeReabrir = ident.VeTodo && p.Estatus == "Cerrado", puedeEliminar = p.Estatus == "Borrador"
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ── Validación + inserción/actualización de una actividad ──
    private string? ValidarAct(ActIn a, DateTime ini, DateTime fin)
    {
        if (!TryFecha(a.fecha, out var f)) return "Fecha de actividad inválida (usa AAAA-MM-DD).";
        if (f < ini || f > fin) return $"La actividad «{a.titulo}» ({a.fecha}) está fuera del rango del plan.";
        if (string.IsNullOrWhiteSpace(a.titulo)) return "Toda actividad necesita una descripción.";
        if (!PTipos.Contains(a.tipo ?? "")) return $"Tipo de actividad inválido: {a.tipo}.";
        if (!HoraOk(a.horaIni) || !HoraOk(a.horaFin)) return $"Hora inválida en «{a.titulo}» (usa HH:mm).";
        if (a.prioridad != null && a.prioridad is not ("A" or "M" or "B")) return "Prioridad inválida (A, M o B).";
        if (a.peso is < 0 or > 10) return "El peso debe estar entre 0 y 10.";
        if (!string.IsNullOrEmpty(a.modalidad) && a.modalidad is not ("presencial" or "virtual" or "llamada")) return "Modalidad inválida (presencial, virtual o llamada).";
        if (!string.IsNullOrEmpty(a.alcance) && a.alcance is not ("vendedor" or "todos" or "grupo" or "division")) return "Alcance inválido (vendedor, todos, grupo o división).";
        return null;
    }
    private static void ParamsAct(SqlCommand cmd, int planId, ActIn a, string origenDef)
    {
        TryFecha(a.fecha, out var f);
        cmd.Parameters.AddWithValue("@p", planId); cmd.Parameters.AddWithValue("@f", f);
        cmd.Parameters.AddWithValue("@hi", (object?)a.horaIni ?? DBNull.Value); cmd.Parameters.AddWithValue("@hf", (object?)a.horaFin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", a.tipo); cmd.Parameters.AddWithValue("@st", (object?)Txt(a.subtipo, 14) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ti", Txt(a.titulo, 200)!); cmd.Parameters.AddWithValue("@de", (object?)Txt(a.detalle, 500) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rc", (object?)Txt(a.repCodigo, 20) ?? DBNull.Value); cmd.Parameters.AddWithValue("@rn", (object?)Txt(a.repNombre, 100) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ci", (object?)a.cliId ?? DBNull.Value); cmd.Parameters.AddWithValue("@cn", (object?)Txt(a.cliNombre, 150) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@me", (object?)Txt(a.meta, 300) ?? DBNull.Value); cmd.Parameters.AddWithValue("@pr", string.IsNullOrEmpty(a.prioridad) ? "M" : a.prioridad);
        cmd.Parameters.AddWithValue("@pe", a.peso ?? 1m); cmd.Parameters.AddWithValue("@fi", a.fijo ?? false);
        cmd.Parameters.AddWithValue("@or", a.origen is "extra" ? "extra" : origenDef); cmd.Parameters.AddWithValue("@ord", a.orden ?? 0);
        cmd.Parameters.AddWithValue("@mo", (object?)Txt(a.modalidad, 10) ?? DBNull.Value); cmd.Parameters.AddWithValue("@al", (object?)Txt(a.alcance, 10) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ac", (object?)Txt(a.alcanceCod, 20) ?? DBNull.Value); cmd.Parameters.AddWithValue("@an", (object?)Txt(a.alcanceNombre, 100) ?? DBNull.Value);
    }
    private const string SqlInsAct = $"INSERT INTO {TAct}(PlanID,Fecha,HoraIni,HoraFin,Tipo,Subtipo,Titulo,Detalle,RepCodigo,RepNombre,CliID,CliNombre,Meta,Prioridad,Peso,Fijo,Origen,Orden,Modalidad,Alcance,AlcanceCod,AlcanceNombre) " +
        "VALUES(@p,@f,@hi,@hf,@t,@st,@ti,@de,@rc,@rn,@ci,@cn,@me,@pr,@pe,@fi,@or,@ord,@mo,@al,@ac,@an); SELECT CAST(SCOPE_IDENTITY() AS int);";
    private const string SqlUpdAct = $"UPDATE {TAct} SET Fecha=@f,HoraIni=@hi,HoraFin=@hf,Tipo=@t,Subtipo=@st,Titulo=@ti,Detalle=@de,RepCodigo=@rc,RepNombre=@rn,CliID=@ci,CliNombre=@cn,Meta=@me,Prioridad=@pr,Peso=@pe,Fijo=@fi,Origen=@or,Orden=@ord,Modalidad=@mo,Alcance=@al,AlcanceCod=@ac,AlcanceNombre=@an,FechaActualizacion=GETDATE() WHERE ActID=@a AND PlanID=@p";

    // ═══════════════ POST plan/guardar ═══════════════
    [HttpPost("plan/guardar")]
    public IActionResult PlanGuardar([FromBody] PlanIn req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (req == null) return BadRequest(new { ok = false, error = "Solicitud vacía." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!AccesoSupG(cn, id, req.supCod)) return Ok(new { ok = false, error = "No tienes acceso a ese supervisor." });
            if (!TryFecha(req.desde, out var ini) || !TryFecha(req.hasta, out var fin) || fin < ini) return Ok(new { ok = false, error = "Rango de fechas inválido." });
            if ((fin - ini).TotalDays > 20) return Ok(new { ok = false, error = "El plan no puede abarcar más de 21 días." });
            var acts = req.actividades ?? new List<ActIn>();
            if (acts.Count > 400) return Ok(new { ok = false, error = "Demasiadas actividades (máximo 400)." });
            foreach (var a in acts) { var e = ValidarAct(a, ini, fin); if (e != null) return Ok(new { ok = false, error = e }); }
            var ger = id.VeTodo;
            var pedido = req.estatus is "Activo" or "Por aprobar" ? req.estatus : "Borrador";
            if (!ger && pedido == "Activo") pedido = "Por aprobar";                    // el supervisor nunca se autoaprueba
            if (pedido != "Borrador" && acts.Count == 0 && req.planId is null or 0) return Ok(new { ok = false, error = "El plan no tiene actividades." });
            var supNom = "";
            var gSup = GrupoDe(cn, req.supCod); req = req with { supCod = gSup?.Primary ?? req.supCod.Trim() }; supNom = gSup?.Nombre ?? req.supCod;
            using var tx = cn.BeginTransaction();
            int planId;
            if (req.planId is null or 0)
            {
                using var cmd = new SqlCommand($@"INSERT INTO {TPlan}(SupCod,SupNombre,FechaIni,FechaFin,Semana,Objetivo,Opcion,Estatus,Origen,CreadoPor,EnviadoPor,FechaEnvio,AprobadoPor,FechaAprobacion)
VALUES(@s,@sn,@i,@f,@sem,@o,@op,@e,@or,@u,@env,CASE WHEN @env IS NULL THEN NULL ELSE GETDATE() END,@apr,CASE WHEN @apr IS NULL THEN NULL ELSE GETDATE() END); SELECT CAST(SCOPE_IDENTITY() AS int);", cn, tx);
                cmd.Parameters.AddWithValue("@env", pedido == "Por aprobar" ? id.User : DBNull.Value); cmd.Parameters.AddWithValue("@apr", pedido == "Activo" ? id.User : DBNull.Value);
                cmd.Parameters.AddWithValue("@s", req.supCod.Trim()); cmd.Parameters.AddWithValue("@sn", supNom); cmd.Parameters.AddWithValue("@i", ini); cmd.Parameters.AddWithValue("@f", fin);
                cmd.Parameters.AddWithValue("@sem", Semana(ini)); cmd.Parameters.AddWithValue("@o", (object?)Txt(req.objetivo, 500) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@op", (object?)Txt(req.opcion, 20) ?? DBNull.Value); cmd.Parameters.AddWithValue("@e", pedido);
                cmd.Parameters.AddWithValue("@or", req.origen == "excel" ? "excel" : "linea"); cmd.Parameters.AddWithValue("@u", id.User);
                planId = (int)cmd.ExecuteScalar()!;
                foreach (var a in acts) { using var ci = new SqlCommand(SqlInsAct, cn, tx); ParamsAct(ci, planId, a, "plan"); ci.ExecuteScalar(); }
            }
            else
            {
                planId = req.planId.Value;
                var cur = LeerPlan(cn, planId, tx);
                if (cur == null) return Ok(new { ok = false, error = "El plan no existe." });
                if (!AccesoSupG(cn, id, cur.SupCod)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
                if (cur.Estatus == "Cerrado") return Ok(new { ok = false, error = "El plan está cerrado: no se puede modificar (Gerencia puede reabrirlo)." });
                if (!ger && cur.Estatus == "Por aprobar") return Ok(new { ok = false, error = "El plan está en revisión de Gerencia. Si quieres cambiarlo, retíralo a borrador primero." });
                if (!ger && cur.Estatus == "Activo") return Ok(new { ok = false, error = "El plan ya fue aprobado: los cambios de estructura los hace Gerencia. Puedes agregar actividades adicionales y registrar la ejecución." });
                if (acts.Count == 0 && (pedido != "Borrador" || cur.Estatus != "Borrador")) return Ok(new { ok = false, error = "El plan no puede quedar sin actividades." });
                if (pedido != cur.Estatus)
                {   // transición pedida junto con el guardado (enviar a aprobación / aprobar). Pedir el mismo estatus = solo guardar
                    var et = Transicion(cur.Estatus, pedido, ger); if (et != null) return Ok(new { ok = false, error = et });
                }
                using (var cmd = new SqlCommand($"UPDATE {TPlan} SET FechaIni=@i,FechaFin=@f,Semana=@sem,Objetivo=@o,Opcion=@op,ActualizadoPor=@u,FechaActualizacion=GETDATE() WHERE PlanID=@p", cn, tx))
                {
                    cmd.Parameters.AddWithValue("@i", ini); cmd.Parameters.AddWithValue("@f", fin); cmd.Parameters.AddWithValue("@sem", Semana(ini));
                    cmd.Parameters.AddWithValue("@o", (object?)Txt(req.objetivo, 500) ?? DBNull.Value); cmd.Parameters.AddWithValue("@op", (object?)Txt(req.opcion, 20) ?? (object?)cur.Opcion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@u", id.User); cmd.Parameters.AddWithValue("@p", planId); cmd.ExecuteNonQuery();
                }
                var existentes = LeerActs(cn, planId, tx).ToDictionary(a => a.ActID);
                var conEje = new HashSet<int>();
                using (var cmd = new SqlCommand($"SELECT DISTINCT ActID FROM {TEje} WHERE PlanID=@p", cn, tx)) { cmd.Parameters.AddWithValue("@p", planId); using var rd = cmd.ExecuteReader(); while (rd.Read()) conEje.Add(rd.GetInt32(0)); }
                var mantener = new HashSet<int>();
                foreach (var a in acts)
                {
                    if (a.actId is > 0 && existentes.ContainsKey(a.actId.Value))
                    { mantener.Add(a.actId.Value); using var cu = new SqlCommand(SqlUpdAct, cn, tx); ParamsAct(cu, planId, a, existentes[a.actId.Value].Origen); cu.Parameters.AddWithValue("@a", a.actId.Value); cu.ExecuteNonQuery(); }
                    else { using var ci = new SqlCommand(SqlInsAct, cn, tx); ParamsAct(ci, planId, a, "plan"); ci.ExecuteScalar(); }
                }
                foreach (var ex in existentes.Values.Where(x => !mantener.Contains(x.ActID)))
                {
                    if (conEje.Contains(ex.ActID)) { using var cx = new SqlCommand($"UPDATE {TAct} SET Estado='Cancelada',FechaActualizacion=GETDATE() WHERE ActID=@a", cn, tx); cx.Parameters.AddWithValue("@a", ex.ActID); cx.ExecuteNonQuery(); }   // ya tiene trabajo registrado: se conserva como Cancelada
                    else { using var cd = new SqlCommand($"DELETE FROM {TAct} WHERE ActID=@a", cn, tx); cd.Parameters.AddWithValue("@a", ex.ActID); cd.ExecuteNonQuery(); }
                }
                if (pedido != cur.Estatus) CambiarEstatus(cn, tx, planId, cur.Estatus, pedido, id.User, null);
            }
            tx.Commit();
            var h = LeerPlan(cn, planId)!;
            return Ok(new { ok = true, planId, numero = h.Numero, estatus = h.Estatus });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ POST plan/actividad (alta/edición de una actividad, p.ej. «adicional» durante la ejecución) ═══════════════
    [HttpPost("plan/actividad")]
    public IActionResult PlanActividad([FromBody] ActEdit req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            var p = LeerPlan(cn, req.planId); if (p == null) return Ok(new { ok = false, error = "El plan no existe." });
            if (!AccesoSupG(cn, id, p.SupCod)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
            if (p.Estatus == "Cerrado") return Ok(new { ok = false, error = "El plan está cerrado: Gerencia puede reabrirlo." });
            if (!id.VeTodo)
            {   // el supervisor solo agrega/edita actividades ADICIONALES (extra) y con el plan aprobado
                if (p.Estatus != "Activo") return Ok(new { ok = false, error = "Las actividades adicionales se agregan cuando el plan está aprobado (Activo). Antes, edita el plan." });
                req = req with { act = req.act with { origen = "extra", peso = 0, fijo = false } };
                if (req.act.actId is > 0)
                {
                    using var co = new SqlCommand($"SELECT Origen FROM {TAct} WHERE ActID=@a AND PlanID=@p", cn); co.Parameters.AddWithValue("@a", req.act.actId.Value); co.Parameters.AddWithValue("@p", req.planId);
                    if ((co.ExecuteScalar() as string) != "extra") return Ok(new { ok = false, error = "Solo Gerencia puede modificar actividades del plan aprobado." });
                }
            }
            var e = ValidarAct(req.act, p.Ini, p.Fin); if (e != null) return Ok(new { ok = false, error = e });
            int actId;
            if (req.act.actId is > 0)
            {
                using var cu = new SqlCommand(SqlUpdAct, cn); ParamsAct(cu, req.planId, req.act, "plan"); cu.Parameters.AddWithValue("@a", req.act.actId.Value);
                if (cu.ExecuteNonQuery() == 0) return Ok(new { ok = false, error = "La actividad no existe." }); actId = req.act.actId.Value;
            }
            else { using var ci = new SqlCommand(SqlInsAct, cn); ParamsAct(ci, req.planId, req.act, "plan"); actId = (int)ci.ExecuteScalar()!; }
            return Ok(new { ok = true, actId });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    [HttpPost("plan/actividad-eliminar")]
    public IActionResult PlanActividadEliminar([FromBody] IdReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            var p = LeerPlan(cn, req.id); if (p == null) return Ok(new { ok = false, error = "El plan no existe." });
            if (!AccesoSupG(cn, id, p.SupCod)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
            if (p.Estatus == "Cerrado") return Ok(new { ok = false, error = "El plan está cerrado: Gerencia puede reabrirlo." });
            if (!id.VeTodo)
            {
                using var co = new SqlCommand($"SELECT Origen FROM {TAct} WHERE ActID=@a AND PlanID=@p", cn); co.Parameters.AddWithValue("@a", req.actId ?? 0); co.Parameters.AddWithValue("@p", req.id);
                if ((co.ExecuteScalar() as string) != "extra") return Ok(new { ok = false, error = "Solo Gerencia puede quitar actividades del plan aprobado (el supervisor puede quitar sus actividades adicionales)." });
            }
            using var tx = cn.BeginTransaction();
            using (var c1 = new SqlCommand($"DELETE FROM {TEje} WHERE ActID=@a AND PlanID=@p", cn, tx)) { c1.Parameters.AddWithValue("@a", req.actId ?? 0); c1.Parameters.AddWithValue("@p", req.id); c1.ExecuteNonQuery(); }
            using (var c2 = new SqlCommand($"DELETE FROM {TAct} WHERE ActID=@a AND PlanID=@p", cn, tx)) { c2.Parameters.AddWithValue("@a", req.actId ?? 0); c2.Parameters.AddWithValue("@p", req.id); c2.ExecuteNonQuery(); }
            tx.Commit(); return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ POST plan/ejecucion ═══════════════
    [HttpPost("plan/ejecucion")]
    public IActionResult PlanEjecucion([FromBody] EjeIn req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            if (!PEstados.Contains(req.estado) || req.estado == "Pendiente") return Ok(new { ok = false, error = "Elige el estado: Realizada, Parcial, No realizada, Reprogramada o Cancelada." });
            if (!HoraOk(req.horaRealizada)) return Ok(new { ok = false, error = "Hora realizada inválida (HH:mm)." });
            var fr = DateTime.Today; if (!string.IsNullOrWhiteSpace(req.fechaRealizada) && !TryFecha(req.fechaRealizada, out fr)) return Ok(new { ok = false, error = "Fecha realizada inválida." });
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            int planId; string sup;
            using (var c0 = new SqlCommand($"SELECT a.PlanID, p.SupCod, p.Estatus FROM {TAct} a JOIN {TPlan} p ON p.PlanID=a.PlanID WHERE a.ActID=@a", cn))
            {
                c0.Parameters.AddWithValue("@a", req.actId); using var rd = c0.ExecuteReader();
                if (!rd.Read()) return Ok(new { ok = false, error = "La actividad no existe." });
                planId = rd.GetInt32(0); sup = rd.GetString(1); var est = rd.GetString(2);
                if (!AccesoSupG(cn, id, sup)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
                if (est == "Borrador") return Ok(new { ok = false, error = "El plan aún es un borrador: envíalo a aprobación de Gerencia para empezar a registrar su ejecución." });
                if (est == "Por aprobar") return Ok(new { ok = false, error = "El plan está pendiente de aprobación de Gerencia: aún no se puede registrar su ejecución." });
                if (est == "Cerrado") return Ok(new { ok = false, error = "El plan está cerrado: Gerencia puede reabrirlo." });
            }
            if (req.estado is "Realizada" or "Parcial" && string.IsNullOrWhiteSpace(req.trabajo)) return Ok(new { ok = false, error = "Describe el trabajo realizado." });
            using var tx = cn.BeginTransaction();
            using (var ci = new SqlCommand($@"INSERT INTO {TEje}(ActID,PlanID,FechaRealizada,HoraRealizada,TrabajoRealizado,RepAtendido,RepAtendidoNombre,CliID,CliNombre,Resultado,Estado,RegistradoPor)
VALUES(@a,@p,@f,@h,@t,@ra,@rn,@ci,@cn,@r,@e,@u)", cn, tx))
            {
                ci.Parameters.AddWithValue("@a", req.actId); ci.Parameters.AddWithValue("@p", planId); ci.Parameters.AddWithValue("@f", fr);
                ci.Parameters.AddWithValue("@h", (object?)req.horaRealizada ?? DBNull.Value); ci.Parameters.AddWithValue("@t", (object?)Txt(req.trabajo, 1000) ?? DBNull.Value);
                ci.Parameters.AddWithValue("@ra", (object?)Txt(req.repAtendido, 20) ?? DBNull.Value); ci.Parameters.AddWithValue("@rn", (object?)Txt(req.repAtendidoNombre, 100) ?? DBNull.Value);
                ci.Parameters.AddWithValue("@ci", (object?)req.cliId ?? DBNull.Value); ci.Parameters.AddWithValue("@cn", (object?)Txt(req.cliNombre, 150) ?? DBNull.Value);
                ci.Parameters.AddWithValue("@r", (object?)Txt(req.resultado, 1000) ?? DBNull.Value); ci.Parameters.AddWithValue("@e", req.estado); ci.Parameters.AddWithValue("@u", id.User); ci.ExecuteNonQuery();
            }
            using (var cu = new SqlCommand($"UPDATE {TAct} SET Estado=@e, FechaActualizacion=GETDATE() WHERE ActID=@a", cn, tx)) { cu.Parameters.AddWithValue("@e", req.estado); cu.Parameters.AddWithValue("@a", req.actId); cu.ExecuteNonQuery(); }
            tx.Commit();
            var acts = LeerActs(cn, planId);
            return Ok(new { ok = true, planId, metricas = Metricas(acts, DateTime.Today) });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    [HttpPost("plan/ejecucion-eliminar")]
    public IActionResult PlanEjecucionEliminar([FromBody] IdReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            int actId, planId; string sup, est;
            using (var c0 = new SqlCommand($"SELECT e.ActID, e.PlanID, p.SupCod, p.Estatus FROM {TEje} e JOIN {TPlan} p ON p.PlanID=e.PlanID WHERE e.EjeID=@e", cn))
            {
                c0.Parameters.AddWithValue("@e", req.ejeId ?? 0); using var rd = c0.ExecuteReader();
                if (!rd.Read()) return Ok(new { ok = false, error = "El registro no existe." });
                actId = rd.GetInt32(0); planId = rd.GetInt32(1); sup = rd.GetString(2); est = rd.GetString(3);
            }
            if (!AccesoSupG(cn, id, sup)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
            if (est == "Cerrado") return Ok(new { ok = false, error = "El plan está cerrado: Gerencia puede reabrirlo." });
            if (est != "Activo") return Ok(new { ok = false, error = "El plan no está activo." });
            using var tx = cn.BeginTransaction();
            using (var c1 = new SqlCommand($"DELETE FROM {TEje} WHERE EjeID=@e", cn, tx)) { c1.Parameters.AddWithValue("@e", req.ejeId ?? 0); c1.ExecuteNonQuery(); }
            string nuevo;
            using (var c2 = new SqlCommand($"SELECT TOP 1 Estado FROM {TEje} WHERE ActID=@a ORDER BY FechaRealizada DESC, EjeID DESC", cn, tx)) { c2.Parameters.AddWithValue("@a", actId); nuevo = c2.ExecuteScalar() as string ?? "Pendiente"; }
            using (var c3 = new SqlCommand($"UPDATE {TAct} SET Estado=@e, FechaActualizacion=GETDATE() WHERE ActID=@a", cn, tx)) { c3.Parameters.AddWithValue("@e", nuevo); c3.Parameters.AddWithValue("@a", actId); c3.ExecuteNonQuery(); }
            tx.Commit(); return Ok(new { ok = true, planId, estado = nuevo });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ POST plan/estatus  (enviar a aprobación · aprobar · devolver/retirar · reabrir) ═══════════════
    [HttpPost("plan/estatus")]
    public IActionResult PlanEstatus([FromBody] EstatusReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            var p = LeerPlan(cn, req.id); if (p == null) return Ok(new { ok = false, error = "El plan no existe." });
            if (!AccesoSupG(cn, id, p.SupCod)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
            var et = Transicion(p.Estatus, req.estatus, id.VeTodo); if (et != null) return Ok(new { ok = false, error = et });
            if (req.estatus == "Por aprobar" && LeerActs(cn, req.id).Count == 0) return Ok(new { ok = false, error = "El plan no tiene actividades." });
            if (p.Estatus == "Por aprobar" && req.estatus == "Borrador" && id.VeTodo && string.IsNullOrWhiteSpace(req.comentario)) return Ok(new { ok = false, error = "Escribe qué debe ajustar el supervisor para devolverle el plan." });
            CambiarEstatus(cn, null, req.id, p.Estatus, req.estatus, id.User, req.comentario);
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ POST plan/cerrar (cierre semanal: congela el % de logro) ═══════════════
    [HttpPost("plan/cerrar")]
    public IActionResult PlanCerrar([FromBody] CierreReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            var p = LeerPlan(cn, req.id); if (p == null) return Ok(new { ok = false, error = "El plan no existe." });
            if (!AccesoSupG(cn, id, p.SupCod)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
            if (p.Estatus != "Activo") return Ok(new { ok = false, error = p.Estatus == "Cerrado" ? "El plan ya está cerrado." : "Solo se cierra un plan aprobado (Activo)." });
            var lim = LimiteCierre(p);
            if (!id.VeTodo && DateTime.Today < lim) return Ok(new { ok = false, error = $"El plan se cierra el viernes ({lim:dd/MM}). Mientras tanto sigue registrando la ejecución." });
            var acts = LeerActs(cn, req.id); var logro = LogroDe(acts);
            using var cmd = new SqlCommand($"UPDATE {TPlan} SET Estatus='Cerrado', LogroPct=@l, CierreLogros=@a, CierreAprendizajes=@b, CierreCompromisos=@c, CerradoPor=@u, FechaCierre=GETDATE(), ActualizadoPor=@u, FechaActualizacion=GETDATE() WHERE PlanID=@p", cn);
            cmd.Parameters.AddWithValue("@l", (object?)logro ?? DBNull.Value); cmd.Parameters.AddWithValue("@a", (object?)Txt(req.logros, 1000) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@b", (object?)Txt(req.aprendizajes, 1000) ?? DBNull.Value); cmd.Parameters.AddWithValue("@c", (object?)Txt(req.compromisos, 1000) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@u", id.User); cmd.Parameters.AddWithValue("@p", req.id); cmd.ExecuteNonQuery();
            return Ok(new { ok = true, logroPct = logro });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ POST plan/eliminar (solo Borrador) ═══════════════
    [HttpPost("plan/eliminar")]
    public IActionResult PlanEliminar([FromBody] IdReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            var p = LeerPlan(cn, req.id); if (p == null) return Ok(new { ok = false, error = "El plan no existe." });
            if (!AccesoSupG(cn, id, p.SupCod)) return Ok(new { ok = false, error = "No tienes acceso a este plan." });
            if (p.Estatus != "Borrador") return Ok(new { ok = false, error = "Solo se pueden eliminar planes en Borrador." });
            using var tx = cn.BeginTransaction();
            foreach (var t in new[] { TEje, TAct }) { using var c = new SqlCommand($"DELETE FROM {t} WHERE PlanID=@p", cn, tx); c.Parameters.AddWithValue("@p", req.id); c.ExecuteNonQuery(); }
            using (var c = new SqlCommand($"DELETE FROM {TPlan} WHERE PlanID=@p", cn, tx)) { c.Parameters.AddWithValue("@p", req.id); c.ExecuteNonQuery(); }
            tx.Commit(); return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═══════════════ Reuniones fijas semanales del supervisor (p. ej. «todos los jueves 5:00 pm por Teams con mis vendedores») ═══════════════
    [HttpPost("plan/recurrente")]
    public IActionResult PlanRecurrente([FromBody] RecIn req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            if (req.dia is < 1 or > 6) return Ok(new { ok = false, error = "Elige el día de la semana (lunes a sábado)." });
            if (string.IsNullOrWhiteSpace(req.titulo)) return Ok(new { ok = false, error = "Escribe el nombre de la reunión." });
            if (!HoraOk(req.horaIni) || !HoraOk(req.horaFin)) return Ok(new { ok = false, error = "Hora inválida (HH:mm)." });
            if (!string.IsNullOrEmpty(req.modalidad) && req.modalidad is not ("presencial" or "virtual" or "llamada")) return Ok(new { ok = false, error = "Modalidad inválida." });
            if (!string.IsNullOrEmpty(req.alcance) && req.alcance is not ("vendedor" or "todos" or "grupo" or "division")) return Ok(new { ok = false, error = "Alcance inválido." });
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            if (!AccesoSupG(cn, id, req.supCod)) return Ok(new { ok = false, error = "No tienes acceso a ese supervisor." });
            var g = GrupoDe(cn, req.supCod); var prim = g?.Primary ?? req.supCod.Trim(); var cods = g != null ? g.Codes.Select(c => c.cod).ToList() : new List<string> { prim };
            int recId;
            if (req.recId is > 0)
            {
                using var cu = new SqlCommand($"UPDATE {TRec} SET Dia=@d,HoraIni=@hi,HoraFin=@hf,Titulo=@t,Modalidad=@mo,Alcance=@al,AlcanceCod=@ac,AlcanceNombre=@an,Meta=@me WHERE RecID=@r AND SupCod IN ({string.Join(",", cods.Select((c, i) => "@c" + i))})", cn);
                for (int i = 0; i < cods.Count; i++) cu.Parameters.AddWithValue("@c" + i, cods[i]);
                RecParams(cu, req); cu.Parameters.AddWithValue("@r", req.recId.Value);
                if (cu.ExecuteNonQuery() == 0) return Ok(new { ok = false, error = "La reunión no existe." }); recId = req.recId.Value;
            }
            else
            {
                using var ci = new SqlCommand($"INSERT INTO {TRec}(SupCod,Dia,HoraIni,HoraFin,Titulo,Modalidad,Alcance,AlcanceCod,AlcanceNombre,Meta,CreadoPor) VALUES(@s,@d,@hi,@hf,@t,@mo,@al,@ac,@an,@me,@u); SELECT CAST(SCOPE_IDENTITY() AS int);", cn);
                ci.Parameters.AddWithValue("@s", prim); ci.Parameters.AddWithValue("@u", id.User); RecParams(ci, req); recId = (int)ci.ExecuteScalar()!;
            }
            return Ok(new { ok = true, recId });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }
    private static void RecParams(SqlCommand c, RecIn r)
    {
        c.Parameters.AddWithValue("@d", r.dia); c.Parameters.AddWithValue("@hi", (object?)Txt(r.horaIni, 5) ?? DBNull.Value); c.Parameters.AddWithValue("@hf", (object?)Txt(r.horaFin, 5) ?? DBNull.Value);
        c.Parameters.AddWithValue("@t", Txt(r.titulo, 200)!); c.Parameters.AddWithValue("@mo", (object?)Txt(r.modalidad, 10) ?? DBNull.Value); c.Parameters.AddWithValue("@al", (object?)Txt(r.alcance, 10) ?? DBNull.Value);
        c.Parameters.AddWithValue("@ac", (object?)Txt(r.alcanceCod, 20) ?? DBNull.Value); c.Parameters.AddWithValue("@an", (object?)Txt(r.alcanceNombre, 100) ?? DBNull.Value); c.Parameters.AddWithValue("@me", (object?)Txt(r.meta, 300) ?? DBNull.Value);
    }
    [HttpPost("plan/recurrente-eliminar")]
    public IActionResult PlanRecurrenteEliminar([FromBody] IdReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            string? sup; using (var c0 = new SqlCommand($"SELECT SupCod FROM {TRec} WHERE RecID=@r", cn)) { c0.Parameters.AddWithValue("@r", req.id); sup = c0.ExecuteScalar() as string; }
            if (sup == null) return Ok(new { ok = false, error = "La reunión no existe." });
            if (!AccesoSupG(cn, id, sup)) return Ok(new { ok = false, error = "No tienes acceso a ese supervisor." });
            using var cd = new SqlCommand($"DELETE FROM {TRec} WHERE RecID=@r", cn); cd.Parameters.AddWithValue("@r", req.id); cd.ExecuteNonQuery();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    // ═════════════════════════ EXCEL: plantilla, exportación e importación ═════════════════════════
    private static readonly string[] ExColumnas = { "Fecha", "Hora inicio", "Hora fin", "Tipo", "Actividad", "Vendedor (código)", "Cliente (código)", "Objetivo / meta", "Prioridad", "Peso", "Estado", "Fecha realizada", "Trabajo realizado", "Vendedor atendido (código)", "Cliente atendido (código)", "Resultado obtenido", "Con quién (Todos / Grupo:S4 / División:11; vacío = el vendedor)", "Modalidad (Presencial / Virtual / Llamada)" };
    private static string PrioNom(string p) => p == "A" ? "Alta" : p == "B" ? "Baja" : "Media";

    private static void HojaInstrucciones(IXLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Instrucciones"); ws.ShowGridLines = false; ws.Column(1).Width = 28; ws.Column(2).Width = 110;
        ws.Cell(1, 1).Value = "Plan de Trabajo Semanal del Supervisor — plantilla de carga"; ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 15;
        var l = new (string, string)[]
        {
            ("Para qué sirve", "Cargar al sistema un plan de trabajo YA realizado (o por realizar) sin digitarlo en línea. El archivo crea un plan nuevo con su secuencia única."),
            ("Paso 1", "Hoja «Datos del plan»: escriba el código del supervisor, las fechas Desde/Hasta (AAAA-MM-DD), el objetivo de la semana y el estatus a cargar."),
            ("Paso 2", "Hoja «Actividades»: una fila por actividad. Solo son obligatorias Fecha, Tipo y Actividad. Las columnas de Estado a Resultado se llenan solo si la actividad ya se ejecutó."),
            ("Fecha / Hora", "Fecha AAAA-MM-DD dentro del rango del plan. Hora en formato HH:mm de 24 horas (ej. 15:30). Si no hay hora, déjela vacía."),
            ("Tipo", "Calle, Telemática, Cobros, Reunión, Gestión, Capacitación u Otro (lista desplegable)."),
            ("Vendedor / Cliente", "Use el CÓDIGO del vendedor (ej. 84) y el CÓDIGO del cliente (ej. C021623). Son opcionales."),
            ("Con quién / Modalidad", "Columna «Con quién»: Todos (todo el equipo), Grupo:S4 (un grupo/código de supervisor) o División:11. Déjela vacía para una actividad con un vendedor (columna Vendedor). Modalidad: Presencial, Virtual (Teams/Meet) o Llamada colectiva."),
            ("Prioridad / Peso", "Prioridad: Alta, Media o Baja. Peso de 1 a 5 (importancia de la actividad para el % de logro; por defecto 1; 0 = no cuenta)."),
            ("Estado", "Pendiente, Realizada, Parcial, No realizada, Reprogramada o Cancelada. Vacío = Pendiente."),
            ("Ejecución", "Si el Estado es Realizada o Parcial, describa el Trabajo realizado; indique Fecha realizada, vendedor/cliente atendido y Resultado obtenido."),
            ("% de logro", "Realizada = 100 %, Parcial = 50 %, el resto 0 %, ponderado por el Peso (las Canceladas no cuentan)."),
            ("Recordatorios", "Cuide los días de calle y de supervisión telemática · desde las 3:30 p. m. cobros dinámicos · 5:30 p. m. reunión diaria (lunes a viernes) · sábado 8:30 a. m.: reunión con Gerencia · el plan se cierra todos los viernes."),
            ("Revisión", "Al subir el archivo el sistema muestra una vista previa con los errores por fila; solo se crea el plan cuando usted confirma. Si lo carga un supervisor, el plan queda «Por aprobar» (Gerencia lo aprueba); si lo carga Gerencia, queda con el estatus indicado."),
        };
        int r = 3; foreach (var (k, v) in l) { ws.Cell(r, 1).Value = k; ws.Cell(r, 1).Style.Font.Bold = true; ws.Cell(r, 2).Value = v; ws.Cell(r, 2).Style.Alignment.WrapText = true; ws.Cell(r, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top; r++; }
    }
    private static void HojasPlan(IXLWorkbook wb, PlanH? p, List<ActR>? acts, Dictionary<int, List<(string trabajo, string rep, string cli, string res, string fecha)>>? eje, List<(string cod, string nom)>? equipo, string? supCod, string? supNombre, Dictionary<int, string>? cliCod = null)
    {
        var d = wb.Worksheets.Add("Datos del plan"); d.Column(1).Width = 26; d.Column(2).Width = 44;
        d.Cell(1, 1).Value = "Datos del plan"; d.Cell(1, 1).Style.Font.Bold = true; d.Cell(1, 1).Style.Font.FontSize = 14;
        var campos = new[] { "Supervisor (código)", "Desde (AAAA-MM-DD)", "Hasta (AAAA-MM-DD)", "Objetivo de la semana", "Estatus a cargar (Activo o Cerrado; supervisor = Por aprobar)" };
        for (int i = 0; i < campos.Length; i++) { d.Cell(3 + i, 1).Value = campos[i]; d.Cell(3 + i, 1).Style.Font.Bold = true; d.Cell(3 + i, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFF99"); }
        d.Cell(3, 2).Value = p?.SupCod ?? supCod ?? ""; d.Cell(4, 2).Value = p?.Ini.ToString("yyyy-MM-dd") ?? ""; d.Cell(5, 2).Value = p?.Fin.ToString("yyyy-MM-dd") ?? "";
        d.Cell(6, 2).Value = p?.Objetivo ?? ""; d.Cell(7, 2).Value = p == null ? "Activo" : (p.Estatus == "Cerrado" ? "Cerrado" : "Activo");
        d.Cell(9, 1).Value = p != null ? $"Plan {p.Numero} — {p.SupNombre} · estatus {p.Estatus}" + (p.LogroPct != null ? $" · logro {p.LogroPct}%" : "") : $"Supervisor: {supNombre ?? ""}";
        d.Cell(9, 1).Style.Font.Italic = true;
        d.Cell(4, 2).Style.NumberFormat.Format = "@"; d.Cell(5, 2).Style.NumberFormat.Format = "@";
        d.Range("B7").CreateDataValidation().List("Activo,Cerrado");

        var ws = wb.Worksheets.Add("Actividades"); ws.SheetView.FreezeRows(1);
        int[] anchos = { 12, 11, 10, 14, 44, 16, 16, 34, 11, 7, 14, 14, 44, 18, 18, 44, 26, 18 };
        for (int c = 0; c < ExColumnas.Length; c++)
        {
            var h = ws.Cell(1, c + 1); h.Value = ExColumnas[c]; h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White;
            h.Style.Fill.BackgroundColor = (c < 10 || c > 15) ? XLColor.FromHtml("#1C2F6E") : XLColor.FromHtml("#2F7D3F"); h.Style.Alignment.WrapText = true; h.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Column(c + 1).Width = anchos[c];
        }
        ws.Row(1).Height = 32;
        int r = 2;
        if (acts != null)
            foreach (var a in acts)
            {
                var e = eje != null && eje.TryGetValue(a.ActID, out var l) ? l.LastOrDefault() : default;
                ws.Cell(r, 1).Value = a.Fecha.ToString("yyyy-MM-dd"); ws.Cell(r, 2).Value = a.HoraIni ?? ""; ws.Cell(r, 3).Value = a.HoraFin ?? ""; ws.Cell(r, 4).Value = TipoNom(a.Tipo);
                ws.Cell(r, 5).Value = a.Titulo; ws.Cell(r, 6).Value = a.RepCodigo ?? ""; ws.Cell(r, 7).Value = a.CliID != null && cliCod != null && cliCod.TryGetValue(a.CliID.Value, out var cc0) ? cc0 : ""; ws.Cell(r, 8).Value = a.Meta ?? "";
                ws.Cell(r, 9).Value = PrioNom(a.Prioridad); ws.Cell(r, 10).Value = a.Peso; ws.Cell(r, 11).Value = a.Estado;
                ws.Cell(r, 17).Value = a.Alcance == "todos" ? "Todos" : a.Alcance == "grupo" ? "Grupo:" + a.AlcanceCod : a.Alcance == "division" ? "División:" + a.AlcanceCod : "";
                ws.Cell(r, 18).Value = a.Modalidad == "presencial" ? "Presencial" : a.Modalidad == "virtual" ? "Virtual" : a.Modalidad == "llamada" ? "Llamada" : "";
                if (e.trabajo != null || e.res != null) { ws.Cell(r, 12).Value = e.fecha ?? ""; ws.Cell(r, 13).Value = e.trabajo ?? ""; ws.Cell(r, 14).Value = e.rep ?? ""; ws.Cell(r, 15).Value = e.cli ?? ""; ws.Cell(r, 16).Value = e.res ?? ""; }
                r++;
            }
        else
        {   // ejemplos (una fila planificada y otra ya ejecutada)
            ws.Cell(2, 1).Value = "2026-10-12"; ws.Cell(2, 2).Value = "08:30"; ws.Cell(2, 3).Value = "11:30"; ws.Cell(2, 4).Value = "Calle"; ws.Cell(2, 5).Value = "Acompañamiento en ruta al vendedor"; ws.Cell(2, 6).Value = "84";
            ws.Cell(2, 8).Value = "Subir efectividad a 55 %"; ws.Cell(2, 9).Value = "Alta"; ws.Cell(2, 10).Value = 2; ws.Cell(2, 11).Value = "Pendiente";
            ws.Cell(3, 1).Value = "2026-10-12"; ws.Cell(3, 2).Value = "15:30"; ws.Cell(3, 3).Value = "17:30"; ws.Cell(3, 4).Value = "Cobros"; ws.Cell(3, 5).Value = "Cobros dinámicos"; ws.Cell(3, 9).Value = "Media"; ws.Cell(3, 10).Value = 1;
            ws.Cell(3, 11).Value = "Realizada"; ws.Cell(3, 12).Value = "2026-10-12"; ws.Cell(3, 13).Value = "Se contactó a 8 vendedores con alerta roja"; ws.Cell(3, 16).Value = "5 de 8 corrigieron su ritmo";
            ws.Range("A2:P3").Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F4F7"); ws.Cell(4, 1).Value = "← Filas 2 y 3 son EJEMPLOS: bórrelas y escriba las suyas.";
            ws.Cell(4, 1).Style.Font.Italic = true; r = 4;
        }
        int hasta = Math.Max(r + 200, 400);
        ws.Range(2, 4, hasta, 4).CreateDataValidation().List(string.Join(",", PTiposNom)); ws.Range(2, 18, hasta, 18).CreateDataValidation().List("Presencial,Virtual,Llamada");
        ws.Range(2, 9, hasta, 9).CreateDataValidation().List("Alta,Media,Baja");
        ws.Range(2, 11, hasta, 11).CreateDataValidation().List(string.Join(",", PEstados));
        var dv = ws.Range(2, 10, hasta, 10).CreateDataValidation(); dv.Decimal.Between(0, 10); dv.ErrorMessage = "El peso va de 0 a 10.";
        ws.Range(2, 1, hasta, 3).Style.NumberFormat.Format = "@"; ws.Range(2, 12, hasta, 12).Style.NumberFormat.Format = "@";
        var li = wb.Worksheets.Add("Listas"); li.Cell(1, 1).Value = "Tipos"; li.Cell(1, 2).Value = "Estados"; li.Cell(1, 4).Value = "Vendedores del equipo (código)"; li.Cell(1, 5).Value = "Nombre";
        for (int i = 0; i < PTiposNom.Length; i++) li.Cell(2 + i, 1).Value = PTiposNom[i];
        for (int i = 0; i < PEstados.Length; i++) li.Cell(2 + i, 2).Value = PEstados[i];
        if (equipo != null) for (int i = 0; i < equipo.Count; i++) { li.Cell(2 + i, 4).Value = equipo[i].cod; li.Cell(2 + i, 5).Value = equipo[i].nom; }
        li.Row(1).Style.Font.Bold = true; li.Columns().AdjustToContents();
    }
    private FileContentResult Xlsx(XLWorkbook wb, string nombre)
    {
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
    }

    [HttpGet("plan/plantilla")]
    public IActionResult PlanPlantilla([FromQuery] string? supcod)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            var sc = (supcod ?? "").Trim(); if (sc == "" && id.SupCods.Count == 1) sc = id.SupCods[0];
            if (sc != "" && !AccesoSupG(cn, id, sc)) return StatusCode(403, new { ok = false, error = "Sin acceso." });
            var equipo = new List<(string, string)>(); string supNom = "";
            if (sc != "")
            {
                var gp = GrupoDe(cn, sc); var cods = gp != null ? gp.Codes.Select(c => c.cod).ToList() : new List<string> { sc }; sc = gp?.Primary ?? sc; supNom = gp?.Nombre ?? sc;
                using var cmd = new SqlCommand($"SELECT RepCodigo, LTRIM(RTRIM(RepNombre)) FROM Representantes WITH (NOLOCK) WHERE RepEstatus=1 AND ISNULL(RepCargo,'')<>'Supervisor' AND RepSupervisor IN ({string.Join(",", cods.Select((c, i) => "@c" + i))}) ORDER BY RepNombre", cn);
                for (int i = 0; i < cods.Count; i++) cmd.Parameters.AddWithValue("@c" + i, cods[i]); using var rd = cmd.ExecuteReader(); while (rd.Read()) equipo.Add((rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1)));
            }
            using var wb = new XLWorkbook(); HojaInstrucciones(wb); HojasPlan(wb, null, null, null, equipo, sc, supNom);
            wb.Worksheets.First(w => w.Name == "Instrucciones").SetTabColor(XLColor.FromHtml("#1C2F6E"));
            return Xlsx(wb, "Plantilla_Plan_de_Trabajo.xlsx");
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    [HttpGet("plan/excel")]
    public IActionResult PlanExcel([FromQuery] int id)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open(); var ident = Identidad(cn);
            var p = LeerPlan(cn, id); if (p == null) return NotFound();
            if (!AccesoSupG(cn, ident, p.SupCod)) return StatusCode(403, new { ok = false, error = "Sin acceso." });
            var acts = LeerActs(cn, id);
            var eje = new Dictionary<int, List<(string, string, string, string, string)>>();
            using (var cmd = new SqlCommand($"SELECT e.ActID, e.TrabajoRealizado, ISNULL(e.RepAtendido,''), ISNULL(c.CliCodigo,''), e.Resultado, e.FechaRealizada FROM {TEje} e WITH (NOLOCK) LEFT JOIN Clientes c WITH (NOLOCK) ON c.CliID=e.CliID WHERE e.PlanID=@p ORDER BY e.FechaRealizada, e.EjeID", cn))
            {
                cmd.Parameters.AddWithValue("@p", id); using var rd = cmd.ExecuteReader();
                while (rd.Read()) { int a = rd.GetInt32(0); if (!eje.TryGetValue(a, out var l)) eje[a] = l = new(); l.Add((rd.IsDBNull(1) ? "" : rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.IsDBNull(4) ? "" : rd.GetString(4), rd.GetDateTime(5).ToString("yyyy-MM-dd"))); }
            }
            var cliCods = new Dictionary<int, string>();
            var cids = acts.Where(a => a.CliID != null).Select(a => a.CliID!.Value).Distinct().ToList();
            if (cids.Count > 0)
            {
                using var cmdc = new SqlCommand($"SELECT CliID, CliCodigo FROM Clientes WITH (NOLOCK) WHERE CliID IN ({string.Join(",", cids)})", cn);
                using var rdc = cmdc.ExecuteReader(); while (rdc.Read()) cliCods[Convert.ToInt32(rdc.GetValue(0))] = rdc.IsDBNull(1) ? "" : rdc.GetString(1).Trim();
            }
            using var wb = new XLWorkbook(); HojaInstrucciones(wb); HojasPlan(wb, p, acts, eje, null, p.SupCod, p.SupNombre, cliCods);
            return Xlsx(wb, $"Plan_{p.Numero}_{p.Ini:yyyyMMdd}.xlsx");
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }

    private static string CelTxt(IXLCell c) => c.IsEmpty() ? "" : (c.DataType == XLDataType.DateTime ? c.GetDateTime().ToString("yyyy-MM-dd") : c.GetString().Trim());
    private static string? CelHora(IXLCell c)
    {
        if (c.IsEmpty()) return null;
        if (c.DataType == XLDataType.TimeSpan) return c.GetTimeSpan().ToString(@"hh\:mm");
        if (c.DataType == XLDataType.DateTime) return c.GetDateTime().ToString("HH:mm");
        if (c.DataType == XLDataType.Number) { var ts = TimeSpan.FromDays(c.GetDouble()); return $"{ts.Hours:00}:{ts.Minutes:00}"; }
        var s = c.GetString().Trim(); var m = Regex.Match(s, @"^(\d{1,2}):(\d{2})"); return m.Success ? $"{int.Parse(m.Groups[1].Value):00}:{m.Groups[2].Value}" : s;
    }
    private static string? TipoClave(string s)
    {
        var x = (s ?? "").Trim().ToLowerInvariant().Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");
        return x switch { "calle" or "presencial" => "calle", "telematica" or "virtual" or "telematica " => "virtual", "cobros" or "cobro" => "cobros", "reunion" => "reunion", "gestion" => "gestion", "capacitacion" => "capacitacion", "otro" or "" => x == "" ? null : "otro", _ => null };
    }

    public sealed class ImportForm { public IFormFile? archivo { get; set; } }

    // POST plan/importar?confirmar=0|1  (multipart, campo «archivo»)
    [HttpPost("plan/importar")]
    [RequestSizeLimit(8_000_000)]
    public IActionResult PlanImportar([FromForm] ImportForm form, [FromQuery] int confirmar = 0)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        var f = form?.archivo; if (f == null || f.Length == 0) return Ok(new { ok = false, error = "Elige un archivo Excel (.xlsx)." });
        try
        {
            using var cn = NewConn(); cn.Open(); var id = Identidad(cn);
            using var ms = new MemoryStream(); f.CopyTo(ms); ms.Position = 0;
            using var wb = new XLWorkbook(ms);
            if (!wb.TryGetWorksheet("Datos del plan", out var d) || !wb.TryGetWorksheet("Actividades", out var ws)) return Ok(new { ok = false, error = "El archivo no tiene las hojas «Datos del plan» y «Actividades». Descarga la plantilla y úsala tal cual." });
            var errores = new List<string>();
            var sup = CelTxt(d.Cell(3, 2)); if (sup == "" && id.SupCods.Count == 1) sup = id.SupCods[0];
            if (sup == "" || !AccesoSupG(cn, id, sup)) errores.Add(sup == "" ? "Falta el código del supervisor (Datos del plan, B3)." : $"No tienes acceso al supervisor «{sup}».");
            if (!TryFecha(CelTxt(d.Cell(4, 2)), out var ini)) errores.Add("Desde (B4) inválida: use AAAA-MM-DD.");
            if (!TryFecha(CelTxt(d.Cell(5, 2)), out var fin)) errores.Add("Hasta (B5) inválida: use AAAA-MM-DD.");
            if (errores.Count == 0 && fin < ini) errores.Add("Hasta no puede ser anterior a Desde.");
            if (errores.Count == 0 && (fin - ini).TotalDays > 20) errores.Add("El plan no puede abarcar más de 21 días.");
            var rangoOk = errores.Count == 0;   // rango y supervisor válidos: recién entonces se valida cada fecha contra el rango
            var objetivo = CelTxt(d.Cell(6, 2)); var estCarga = CelTxt(d.Cell(7, 2)).Equals("Cerrado", StringComparison.OrdinalIgnoreCase) ? "Cerrado" : "Activo";
            var estFinal = id.VeTodo ? estCarga : "Por aprobar";   // lo que carga un supervisor siempre pasa por la aprobación de Gerencia

            // Catálogos para validar códigos
            var reps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand("SELECT RepCodigo, LTRIM(RTRIM(RepNombre)) FROM Representantes WITH (NOLOCK)", cn)) using (var rd = cmd.ExecuteReader()) while (rd.Read()) reps[rd.GetString(0).Trim()] = rd.IsDBNull(1) ? "" : rd.GetString(1);
            var filas = new List<(int fila, ActIn act, string estado, string? trabajo, string? fecReal, string? repAt, string? cliAtCod, string? resultado, string? cliCod)>();
            var ultima = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= ultima; r++)
            {
                if (ws.Row(r).IsEmpty()) continue;
                var fec = CelTxt(ws.Cell(r, 1)); var titulo = CelTxt(ws.Cell(r, 5)); var tipoTxt = CelTxt(ws.Cell(r, 4));
                if (fec == "" && titulo == "" && tipoTxt == "") continue;
                if (fec.StartsWith("←")) continue;
                var errF = new List<string>();
                if (!TryFecha(fec, out var fd)) errF.Add("fecha inválida"); else if (rangoOk && (fd < ini || fd > fin)) errF.Add("fecha fuera del rango del plan");
                var tipo = TipoClave(tipoTxt); if (tipo == null) errF.Add($"tipo «{tipoTxt}» no reconocido");
                if (titulo == "") errF.Add("falta la descripción de la actividad");
                var hi = CelHora(ws.Cell(r, 2)); var hf = CelHora(ws.Cell(r, 3)); if (!HoraOk(hi) || !HoraOk(hf)) errF.Add("hora inválida (HH:mm)");
                var rep = CelTxt(ws.Cell(r, 6)); if (rep != "" && !reps.ContainsKey(rep)) errF.Add($"vendedor «{rep}» no existe");
                string? alc = null, alcCod = null, alcNom = null, mod = null;
                var alT = CelTxt(ws.Cell(r, 17)); var alN = alT.ToLowerInvariant().Replace("ó", "o");
                if (alT != "")
                {
                    if (alN is "todos" or "todo el equipo") { alc = "todos"; alcNom = "Todo el equipo"; }
                    else if (alN.StartsWith("grupo") || alN.StartsWith("division"))
                    {
                        var cod = alT.Contains(':') ? alT[(alT.IndexOf(':') + 1)..].Trim() : ""; var esG = alN.StartsWith("grupo");
                        if (cod == "") errF.Add($"«Con quién» «{alT}»: falta el código después de los dos puntos");
                        else if (esG) { var gg = GrupoDe(cn, cod); var et = gg?.Codes.FirstOrDefault(c => c.cod.Equals(cod, StringComparison.OrdinalIgnoreCase)).etiqueta; if (et == null) errF.Add($"grupo «{cod}» no existe"); else { alc = "grupo"; alcCod = cod; alcNom = "Grupo " + et; } }
                        else { alc = "division"; alcCod = cod; alcNom = "División " + cod; }
                    }
                    else errF.Add($"«Con quién» «{alT}» no reconocido (Todos, Grupo:S4 o División:11)");
                }
                var moT = CelTxt(ws.Cell(r, 18)).ToLowerInvariant();
                if (moT != "") { mod = moT.StartsWith("pres") ? "presencial" : moT.StartsWith("virt") ? "virtual" : moT.StartsWith("llam") ? "llamada" : null; if (mod == null) errF.Add($"modalidad «{moT}» no reconocida (Presencial, Virtual o Llamada)"); }
                if (alc == null && rep != "") alc = "vendedor";
                var prioT = CelTxt(ws.Cell(r, 9)).ToLowerInvariant(); var prio = prioT.StartsWith("a") ? "A" : prioT.StartsWith("b") ? "B" : "M";
                decimal peso = 1; var pesoT = CelTxt(ws.Cell(r, 10)); if (pesoT != "" && (!decimal.TryParse(pesoT.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out peso) || peso < 0 || peso > 10)) errF.Add("peso inválido (0 a 10)");
                var estado = CelTxt(ws.Cell(r, 11)); if (estado == "") estado = "Pendiente";
                var estOk = PEstados.FirstOrDefault(e => e.Equals(estado, StringComparison.OrdinalIgnoreCase)); if (estOk == null) errF.Add($"estado «{estado}» no válido"); else estado = estOk;
                var repAt = CelTxt(ws.Cell(r, 14)); if (repAt != "" && !reps.ContainsKey(repAt)) errF.Add($"vendedor atendido «{repAt}» no existe");
                var fr = CelTxt(ws.Cell(r, 12)); if (fr != "" && !TryFecha(fr, out _)) errF.Add("fecha realizada inválida");
                if (estado is "Realizada" or "Parcial" && CelTxt(ws.Cell(r, 13)) == "") errF.Add("falta el trabajo realizado");
                if (errF.Count > 0) { errores.Add($"Fila {r}: " + string.Join("; ", errF)); continue; }
                filas.Add((r, new ActIn(null, fec, hi, hf, tipo!, null, titulo, null, rep == "" ? null : rep, rep != "" ? reps[rep] : null, null, null, Txt(CelTxt(ws.Cell(r, 8)), 300), prio, peso, false, null, filas.Count + 1, mod, alc, alcCod, alcNom),
                    estado, Txt(CelTxt(ws.Cell(r, 13)), 1000), fr == "" ? null : fr, repAt == "" ? null : repAt, CelTxt(ws.Cell(r, 15)) == "" ? null : CelTxt(ws.Cell(r, 15)), Txt(CelTxt(ws.Cell(r, 16)), 1000), CelTxt(ws.Cell(r, 7)) == "" ? null : CelTxt(ws.Cell(r, 7))));
            }
            if (filas.Count == 0 && errores.Count == 0) errores.Add("No se encontraron actividades en la hoja «Actividades».");
            // Clientes por código (objetivo y atendido)
            var cods = filas.SelectMany(x => new[] { x.cliCod, x.cliAtCod }).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var cli = new Dictionary<string, (int id, string nom)>(StringComparer.OrdinalIgnoreCase);
            if (cods.Count > 0)
            {
                var ps = cods.Select((c, i) => $"@c{i}").ToList();
                using var cmd = new SqlCommand($"SELECT CliCodigo, CliID, LTRIM(RTRIM(CliNombre)) FROM Clientes WITH (NOLOCK) WHERE CliCodigo IN ({string.Join(",", ps)})", cn);
                for (int i = 0; i < cods.Count; i++) cmd.Parameters.AddWithValue(ps[i], cods[i]!);
                using var rd = cmd.ExecuteReader(); while (rd.Read()) cli[rd.GetString(0).Trim()] = (Convert.ToInt32(rd.GetValue(1)), rd.IsDBNull(2) ? "" : rd.GetString(2));
                foreach (var c in cods) if (!cli.ContainsKey(c!)) errores.Add($"Cliente «{c}» no existe (use el código, ej. C021623).");
            }
            var resumen = new { actividades = filas.Count, calle = filas.Count(x => x.act.tipo == "calle"), virtualesN = filas.Count(x => x.act.tipo == "virtual"), ejecutadas = filas.Count(x => x.estado != "Pendiente"), supervisor = sup, desde = ini == default ? "" : ini.ToString("yyyy-MM-dd"), hasta = fin == default ? "" : fin.ToString("yyyy-MM-dd"), estatus = estFinal };
            var vista = filas.Take(300).Select(x => new { fila = x.fila, fecha = x.act.fecha, horaIni = x.act.horaIni, horaFin = x.act.horaFin, tipo = TipoNom(x.act.tipo), titulo = x.act.titulo, rep = x.act.repCodigo, estado = x.estado });
            if (confirmar != 1 || errores.Count > 0) return Ok(new { ok = errores.Count == 0, soloVista = true, errores, resumen, filas = vista });

            // ── Crear el plan (un plan por persona: se guarda con su código principal) ──
            var gS = GrupoDe(cn, sup); sup = gS?.Primary ?? sup; var supNom = gS?.Nombre ?? sup;
            using var tx = cn.BeginTransaction();
            int planId;
            using (var cmd = new SqlCommand($@"INSERT INTO {TPlan}(SupCod,SupNombre,FechaIni,FechaFin,Semana,Objetivo,Opcion,Estatus,Origen,CreadoPor,EnviadoPor,FechaEnvio,AprobadoPor,FechaAprobacion)
VALUES(@s,@sn,@i,@f,@sem,@o,'excel',@est,'excel',@u,@env,CASE WHEN @env IS NULL THEN NULL ELSE GETDATE() END,@apr,CASE WHEN @apr IS NULL THEN NULL ELSE GETDATE() END); SELECT CAST(SCOPE_IDENTITY() AS int);", cn, tx))
            {
                cmd.Parameters.AddWithValue("@est", id.VeTodo ? "Activo" : "Por aprobar"); cmd.Parameters.AddWithValue("@env", id.VeTodo ? DBNull.Value : id.User); cmd.Parameters.AddWithValue("@apr", id.VeTodo ? id.User : DBNull.Value);
                cmd.Parameters.AddWithValue("@s", sup); cmd.Parameters.AddWithValue("@sn", supNom); cmd.Parameters.AddWithValue("@i", ini); cmd.Parameters.AddWithValue("@f", fin); cmd.Parameters.AddWithValue("@sem", Semana(ini));
                cmd.Parameters.AddWithValue("@o", (object?)Txt(objetivo, 500) ?? DBNull.Value); cmd.Parameters.AddWithValue("@u", id.User); planId = (int)cmd.ExecuteScalar()!;
            }
            foreach (var x in filas)
            {
                var a = x.act with { cliId = x.cliCod != null ? cli[x.cliCod].id : null, cliNombre = x.cliCod != null ? cli[x.cliCod].nom : null };
                int actId; using (var ci = new SqlCommand(SqlInsAct, cn, tx)) { ParamsAct(ci, planId, a, "plan"); actId = (int)ci.ExecuteScalar()!; }
                if (x.estado != "Pendiente")
                {
                    using (var ce = new SqlCommand($@"INSERT INTO {TEje}(ActID,PlanID,FechaRealizada,TrabajoRealizado,RepAtendido,RepAtendidoNombre,CliID,CliNombre,Resultado,Estado,RegistradoPor) VALUES(@a,@p,@f,@t,@ra,@rn,@ci,@cn,@r,@e,@u)", cn, tx))
                    {
                        TryFecha(x.fecReal, out var fr); if (fr == default) TryFecha(a.fecha, out fr);
                        ce.Parameters.AddWithValue("@a", actId); ce.Parameters.AddWithValue("@p", planId); ce.Parameters.AddWithValue("@f", fr); ce.Parameters.AddWithValue("@t", (object?)x.trabajo ?? DBNull.Value);
                        ce.Parameters.AddWithValue("@ra", (object?)x.repAt ?? DBNull.Value); ce.Parameters.AddWithValue("@rn", x.repAt != null ? (object)reps[x.repAt] : DBNull.Value);
                        ce.Parameters.AddWithValue("@ci", x.cliAtCod != null ? (object)cli[x.cliAtCod].id : DBNull.Value); ce.Parameters.AddWithValue("@cn", x.cliAtCod != null ? (object)cli[x.cliAtCod].nom : DBNull.Value);
                        ce.Parameters.AddWithValue("@r", (object?)x.resultado ?? DBNull.Value); ce.Parameters.AddWithValue("@e", x.estado); ce.Parameters.AddWithValue("@u", id.User); ce.ExecuteNonQuery();
                    }
                    using var cu = new SqlCommand($"UPDATE {TAct} SET Estado=@e WHERE ActID=@a", cn, tx); cu.Parameters.AddWithValue("@e", x.estado); cu.Parameters.AddWithValue("@a", actId); cu.ExecuteNonQuery();
                }
            }
            if (id.VeTodo && estCarga == "Cerrado")
            {
                var logro = LogroDe(LeerActs(cn, planId, tx));
                using var cc = new SqlCommand($"UPDATE {TPlan} SET Estatus='Cerrado', LogroPct=@l, CerradoPor=@u, FechaCierre=GETDATE() WHERE PlanID=@p", cn, tx);
                cc.Parameters.AddWithValue("@l", (object?)logro ?? DBNull.Value); cc.Parameters.AddWithValue("@u", id.User); cc.Parameters.AddWithValue("@p", planId); cc.ExecuteNonQuery();
            }
            tx.Commit();
            var h = LeerPlan(cn, planId)!;
            return Ok(new { ok = true, soloVista = false, planId, numero = h.Numero, resumen, estatus = estFinal });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ErrTabla(ex) }); }
    }
}
