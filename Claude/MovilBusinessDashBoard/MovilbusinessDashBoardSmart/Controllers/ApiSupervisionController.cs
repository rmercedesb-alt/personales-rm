using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── API de Supervisión Dinámica ─────────────────────────────────────────────
// Acompañamiento y Auditoría en calle + Supervisión Virtual (IEGT + alertas de
// Torre) + expediente. Identidad y alcance vienen de SupervisionUsuarioMapa_RM
// (Rol 'Gerencia' = ve todo; 'Supervisor' = solo sus SupCod).
//
// Licencia (2026-09-30): gateado bajo el módulo "supervision", EXCEPTO Branding() (ver
// [SinGateDeLicencia] en esa acción — el Menú la llama para TODAS las tarjetas). Ojo:
// la sub-vista "Virtual" de este módulo llama a /apiseg/semana (módulo "seguimientos") y
// algunos filtros llaman a /apipresupuestos/filtros (módulo "presupuestos") — si alguna
// vez licencias "Supervisión Dinámica" sin "Seguimientos Semanales" y/o "Presupuestos",
// esas 2 partes puntuales de Supervisión no van a cargar (el resto del módulo sigue
// funcionando). No se resolvió ese acoplamiento en esta entrega — ver PROGRESS.md.
[ApiController]
[Route("apisuperv")]
[RequiereModulo("supervision")]
public partial class ApiSupervisionController(IConfiguration config) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    // ── Identidad del usuario logueado (rol + códigos de supervisor) ─────────
    private sealed class Ident { public string User = "", Rol = "Ninguno", Nombre = ""; public List<string> SupCods = new(); public bool VeTodo => Rol == "Gerencia"; }

    private Ident Identidad(SqlConnection cn)
    {
        var id = new Ident
        {
            User = HttpContext.Session.GetString("user_login") ?? "",
            Nombre = HttpContext.Session.GetString("user_nombre") ?? ""
        };
        using var cmd = new SqlCommand(
            "SELECT SupCod, Rol, Nombre FROM dbo.SupervisionUsuarioMapa_RM WITH (NOLOCK) WHERE UsuInicioSesion=@u AND Activo=1", cn);
        cmd.Parameters.AddWithValue("@u", id.User);
        using var rd = cmd.ExecuteReader();
        bool any = false;
        while (rd.Read())
        {
            any = true;
            if (!rd.IsDBNull(1)) id.Rol = rd.GetString(1).Trim();
            if (!rd.IsDBNull(2) && !string.IsNullOrWhiteSpace(rd.GetString(2))) id.Nombre = rd.GetString(2).Trim();
            if (!rd.IsDBNull(0)) { var s = rd.GetString(0).Trim(); if (s.Length > 0) id.SupCods.Add(s); }
        }
        if (!any) id.Rol = "Ninguno";
        else if (id.Rol != "Gerencia") id.Rol = "Supervisor";
        return id;
    }

    // Fragmento AND para restringir Representantes al alcance del usuario.
    // Devuelve false si el usuario no tiene acceso (sin mapeo).
    private bool RepFilter(Ident id, Dictionary<string, object> parms, out string filtro)
    {
        filtro = "";
        if (id.Rol == "Ninguno") return false;
        if (id.VeTodo) return true;                         // Gerencia: sin filtro
        if (id.SupCods.Count == 0) { filtro = "AND 1=0"; return true; } // Supervisor sin equipo
        var names = id.SupCods.Select((s, i) => { var n = $"@sc{i}"; parms[n] = s; return n; });
        filtro = "AND r.RepSupervisor IN (" + string.Join(",", names) + ")";
        return true;
    }

    // ── GET /apisuperv/identidad ────────────────────────────────────────────
    [HttpGet("identidad")]
    public IActionResult Identidad()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            return Ok(new { ok = true, usuario = id.User, nombre = id.Nombre, rol = id.Rol, veTodo = id.VeTodo, supervisores = id.SupCods });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/reps ─────────────────────────────────────────────────
    // Representantes (vendedores) dentro del alcance del usuario.
    [HttpGet("reps")]
    public IActionResult Reps()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var parms = new Dictionary<string, object>();
            if (!RepFilter(id, parms, out var f))
                return Ok(new { ok = true, rol = id.Rol, reps = new List<object>(), error = "Usuario sin acceso configurado (SupervisionUsuarioMapa_RM)." });

            using var cmd = new SqlCommand($@"
SELECT r.RepCodigo, LTRIM(RTRIM(r.RepNombre)) RepNombre, ISNULL(cd.CedDescripcion, r.CedCodigo) Centro,
       ISNULL(r.RepSupervisor,'') SupCod, LTRIM(RTRIM(ISNULL(sup.RepNombre, r.RepSupervisor))) Supervisor,
       ISNULL(ra.RepTipe,'') Division
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo = r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo = r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'') <> 'Supervisor' {f}
ORDER BY Supervisor, RepNombre", cn) { CommandTimeout = 60 };
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            var reps = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                reps.Add(new
                {
                    RepCodigo = rd.GetString(0),
                    RepNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    Centro = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    SupCod = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    Supervisor = rd.IsDBNull(4) ? "" : rd.GetString(4),
                    Division = rd.IsDBNull(5) ? "" : rd.GetString(5)
                });
            return Ok(new { ok = true, rol = id.Rol, veTodo = id.VeTodo, reps });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/clientes?rep=XXX ─────────────────────────────────────
    // Clientes asignados al vendedor (RutaVisitas) para el buscador de calle.
    [HttpGet("clientes")]
    public IActionResult Clientes([FromQuery] string rep)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(rep)) return Ok(new { ok = true, clientes = new List<object>() });
        try
        {
            using var cn = NewConn(); cn.Open();
            // Universo de clientes del vendedor = asignaciones distintas de los últimos 60 días
            // (RutaVisitasFechaHistoricos cubre TODOS los reps; RutaVisitas fija solo 82).
            using var cmd = new SqlCommand(@"
SELECT DISTINCT rv.CliID, ISNULL(c.CliCodigo, CAST(rv.CliID AS varchar(20))) Cod, LTRIM(RTRIM(ISNULL(c.CliNombre,''))) Nombre
FROM RutaVisitasFechaHistoricos rv WITH (NOLOCK)
LEFT JOIN Clientes c WITH (NOLOCK) ON c.CliID = rv.CliID
WHERE rv.RepCodigo=@rep AND rv.RutFecha >= DATEADD(DAY,-60,GETDATE())
ORDER BY Nombre", cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@rep", rep.Trim());
            var cli = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                cli.Add(new { CliID = Convert.ToInt32(rd.GetValue(0)), Cod = rd.IsDBNull(1) ? "" : rd.GetValue(1).ToString()!.Trim(), Nombre = rd.IsDBNull(2) ? "" : rd.GetString(2) });
            return Ok(new { ok = true, rep, clientes = cli });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/suplidores ────────────────────────────────────────────
    // Catálogo real de suplidores (Categoria3) para el multiselect de Calle/Virtual.
    [HttpGet("suplidores")]
    public IActionResult Suplidores()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            using var cmd = new SqlCommand(@"
SELECT Cat3ID, Cat3Descripcion FROM Categoria3 WITH (NOLOCK)
WHERE Cat3Descripcion IS NOT NULL AND LTRIM(RTRIM(Cat3Descripcion))<>'' AND Cat3Descripcion<>'no usar'
ORDER BY Cat3Descripcion", cn) { CommandTimeout = 30 };
            var items = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                items.Add(new { id = Convert.ToInt32(rd.GetValue(0)), nombre = rd.GetString(1).Trim() });
            return Ok(new { ok = true, items });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/config ───────────────────────────────────────────────
    // Pesos: esquemas (50/35/15) + factores por esquema (acomp/audit).
    [HttpGet("config")]
    public IActionResult Config()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var esquemas = new List<object>();
            using (var cmd = new SqlCommand("SELECT Codigo, Nombre, Peso FROM dbo.SupervisionEsquemas_RM WITH (NOLOCK) ORDER BY Orden", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) esquemas.Add(new { codigo = rd.GetString(0), nombre = rd.GetString(1), peso = Convert.ToDouble(rd.GetValue(2)) });

            var factores = new List<object>();
            using (var cmd = new SqlCommand(@"SELECT Esquema, Codigo, Nombre, ISNULL(Grupo,''), Escala, Peso, Orden, Activo
                                              FROM dbo.SupervisionFactores_RM WITH (NOLOCK) ORDER BY Esquema, Orden", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    factores.Add(new
                    {
                        esquema = rd.GetString(0), codigo = rd.GetString(1), nombre = rd.GetString(2),
                        grupo = rd.GetString(3), escala = rd.GetString(4), peso = Convert.ToDouble(rd.GetValue(5)),
                        orden = rd.GetInt32(6), activo = Convert.ToBoolean(rd.GetValue(7))
                    });
            return Ok(new { ok = true, esquemas, factores });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record PesoEsq(string codigo, double peso);
    public sealed record PesoFac(string esquema, string codigo, double peso);
    public sealed record ConfigReq(List<PesoEsq>? esquemas, List<PesoFac>? factores);

    // ── POST /apisuperv/config ──────────────────────────────────────────────
    // Guarda los pesos (mantenimiento). Solo Gerencia.
    [HttpPost("config")]
    public IActionResult GuardarConfig([FromBody] ConfigReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede editar los pesos." });

            int n = 0;
            foreach (var e in req?.esquemas ?? new())
            {
                using var cmd = new SqlCommand("UPDATE dbo.SupervisionEsquemas_RM SET Peso=@p, FechaActualizacion=GETDATE() WHERE Codigo=@c", cn);
                cmd.Parameters.AddWithValue("@p", e.peso); cmd.Parameters.AddWithValue("@c", e.codigo);
                n += cmd.ExecuteNonQuery();
            }
            foreach (var f in req?.factores ?? new())
            {
                using var cmd = new SqlCommand("UPDATE dbo.SupervisionFactores_RM SET Peso=@p, FechaActualizacion=GETDATE() WHERE Esquema=@e AND Codigo=@c", cn);
                cmd.Parameters.AddWithValue("@p", f.peso); cmd.Parameters.AddWithValue("@e", f.esquema); cmd.Parameters.AddWithValue("@c", f.codigo);
                n += cmd.ExecuteNonQuery();
            }
            return Ok(new { ok = true, actualizados = n });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── Branding: apodo del IEGT (ConfiguracionApp_RM, clave IegtApodo) ──────
    private static string LeerIegtApodo(SqlConnection cn)
    {
        try
        {
            using var cmd = new SqlCommand("SELECT TOP 1 Valor FROM dbo.ConfiguracionApp_RM WITH (NOLOCK) WHERE Clave='IegtApodo'", cn);
            var o = cmd.ExecuteScalar();
            return o is string s ? s.Trim() : "";
        }
        catch { return ""; }   // tabla aún no creada → apodo vacío
    }

    // ── GET /apisuperv/branding ─────────────────────────────────────────────
    // [SinGateDeLicencia]: el Menú la llama para TODAS las tarjetas (reemplaza "IEGT"
    // por el apodo configurado), no solo cuando Supervisión Dinámica está activa.
    [HttpGet("branding")]
    [SinGateDeLicencia]
    public IActionResult Branding()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try { using var cn = NewConn(); cn.Open(); return Ok(new { ok = true, iegtApodo = LeerIegtApodo(cn) }); }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record BrandingReq(string? iegtApodo);

    // ── POST /apisuperv/branding ── (solo Gerencia) ─────────────────────────
    [HttpPost("branding")]
    public IActionResult GuardarBranding([FromBody] BrandingReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede editar el apodo." });
            var apodo = (req?.iegtApodo ?? "").Trim();
            if (apodo.Length > 60) apodo = apodo.Substring(0, 60);
            try
            {
                using var cmd = new SqlCommand(@"
UPDATE dbo.ConfiguracionApp_RM SET Valor=@v, FechaActualizacion=GETDATE() WHERE Clave='IegtApodo';
IF @@ROWCOUNT=0 INSERT INTO dbo.ConfiguracionApp_RM (Clave,Valor) VALUES ('IegtApodo',@v);", cn);
                cmd.Parameters.AddWithValue("@v", apodo);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { return Ok(new { ok = false, error = "Falta crear ConfiguracionApp_RM (corre SQL/ConfiguracionApp_RM.sql): " + ex.Message }); }
            return Ok(new { ok = true, iegtApodo = apodo });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/alertas?rep=XXX ──────────────────────────────────────
    // Alertas del momento (Torre de Control) de un vendedor, con hora de generación.
    [HttpGet("alertas")]
    public IActionResult Alertas([FromQuery] string rep)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(rep)) return Ok(new { ok = true, alertas = new List<object>() });
        try
        {
            using var cn = NewConn(); cn.Open();

            // Mute: al guardar un contacto (comentario) el vendedor queda en pausa muteMin minutos
            // (mismo mecanismo/duración que Torre de Control en vivo) — reactivado con el estado
            // real de sus alertas (las que sigan vigentes reaparecen; las de una-vez-al-día no).
            var (enMute, ultimoContacto, muteMin) = TorreCore.EstadoMute(cn, rep.Trim());
            if (enMute)
            {
                var pausadoHasta = ultimoContacto!.Value.AddMinutes(muteMin).ToString("HH:mm");
                return Ok(new { ok = true, rep, corte = DateTime.Now.ToString("HH:mm"), enMute = true, pausadoHasta, alertas = new List<object>() });
            }

            var parms = new Dictionary<string, object> { ["@rep"] = rep.Trim() };
            var metricas = TorreCore.CargarMetricas(cn, "AND r.RepCodigo=@rep", parms);
            var monitores = TorreCore.CargarMonitores(cn, soloActivos: true).Where(m => m.Tipo == "nucleo").ToList();
            var (vDesde, vHasta) = TorreCore.LeerVentanaHoraria(cn);

            // Hora de generación = primer snapshot EnAlarma de hoy por código.
            var desdeMap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(@"SELECT Codigo, MIN(FechaCorte) FROM dbo.TorreControlSnapshots_RM WITH (NOLOCK)
                                              WHERE RepCodigo=@rep AND EnAlarma=1 AND FechaCorte>=CAST(CAST(GETDATE() AS date) AS datetime)
                                              GROUP BY Codigo", cn))
            {
                cmd.Parameters.AddWithValue("@rep", rep.Trim());
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) if (!rd.IsDBNull(1)) desdeMap[rd.GetString(0)] = rd.GetDateTime(1);
            }

            var alertas = new List<object>();
            if (metricas.TryGetValue(rep.Trim(), out var m))
            {
                TorreCore.CalcularCumplimiento(m, vDesde, vHasta, DateTime.Now);
                foreach (var mon in monitores)
                {
                    var c = TorreCore.Evaluar(mon, m);
                    if (!c.Alarma) continue;
                    // Una vez contactado el rep, las alertas "de una vez al día" (evento único de la
                    // jornada) no vuelven a mostrarse, aunque ya haya pasado el mute.
                    if (ultimoContacto.HasValue && TorreCore.UnaVezAlDia.Contains(mon.Codigo)) continue;
                    DateTime? desde = desdeMap.TryGetValue(mon.Codigo, out var dt) ? dt : (DateTime?)null;
                    alertas.Add(new
                    {
                        codigo = mon.Codigo, nombre = mon.Nombre, detalle = c.Detalle, texto = c.Texto,
                        severidad = c.Severidad,
                        desde = desde?.ToString("HH:mm"),
                        minutos = desde.HasValue ? (int)(DateTime.Now - desde.Value).TotalMinutes : (int?)null
                    });
                }
            }
            return Ok(new { ok = true, rep, corte = DateTime.Now.ToString("HH:mm"), alertas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FASE 2 — guardar/leer evaluaciones, contactos, reportes, expediente
    // ════════════════════════════════════════════════════════════════════════
    private static string BandaDe(double v) => v >= 85 ? "Solida" : v >= 70 ? "Aceptable" : v >= 55 ? "Irregular" : "Deficiente";

    private Dictionary<string, double> PesosFactores(SqlConnection cn, string esquema)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand("SELECT Codigo, Peso FROM dbo.SupervisionFactores_RM WITH (NOLOCK) WHERE Esquema=@e AND Activo=1", cn);
        cmd.Parameters.AddWithValue("@e", esquema);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) d[rd.GetString(0)] = Convert.ToDouble(rd.GetValue(1));
        return d;
    }
    private (double wi, double wa, double wu) PesosEsquemas(SqlConnection cn)
    {
        double wi = 50, wa = 35, wu = 15;
        using var cmd = new SqlCommand("SELECT Codigo, Peso FROM dbo.SupervisionEsquemas_RM WITH (NOLOCK)", cn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var v = Convert.ToDouble(rd.GetValue(1));
            switch (rd.GetString(0)) { case "iegt": wi = v; break; case "acomp": wa = v; break; case "audit": wu = v; break; }
        }
        return (wi, wa, wu);
    }
    private static double? GlobalIndex(double? iegt, double? acomp, double? audit, (double wi, double wa, double wu) w)
    {
        double num = 0, den = 0;
        if (iegt.HasValue) { num += iegt.Value * w.wi; den += w.wi; }
        if (acomp.HasValue) { num += acomp.Value * w.wa; den += w.wa; }
        if (audit.HasValue) { num += audit.Value * w.wu; den += w.wu; }
        return den > 0 ? Math.Round(num / den, 1) : (double?)null;
    }
    // IEGT por rep: pendiente de integrar con Seguimientos (por ahora null).
    private static double? IegtDeRep(SqlConnection cn, string rep) => null;

    private static DateTime? CombinarHora(DateOnly fecha, string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        if (DateTime.TryParse(hhmm, out var full) && full.Year > 2000) return full;
        if (TimeSpan.TryParse(hhmm, out var ts)) return fecha.ToDateTime(TimeOnly.FromTimeSpan(ts));
        return null;
    }

    public sealed record PuntajeReq(string factor, double puntaje);
    public sealed record ClienteEvalReq(int cliId, string? cliCod, string? cliNom, string? foto, string? comentario, List<PuntajeReq>? puntajes);
    public sealed record EvaluacionReq(string tipo, string repCodigo, string? repNombre, string? centro, string? supCod,
        string? supervisor, string? division, string? fecha, string? suplidores, string? fechaAcceso, string? horaInicio,
        string? horaFin, string? comentario, string? compromiso, string? firmaVendedor, string? firmaSupervisor, List<ClienteEvalReq>? clientes);

    // ── POST /apisuperv/evaluacion ──────────────────────────────────────────
    [HttpPost("evaluacion")]
    public IActionResult GuardarEvaluacion([FromBody] EvaluacionReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (req is null || string.IsNullOrWhiteSpace(req.repCodigo)) return BadRequest(new { ok = false, error = "Datos incompletos." });
        var tipo = req.tipo == "audit" ? "audit" : "acomp";
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "";
            var fecha = DateOnly.TryParse(req.fecha, out var fd) ? fd : DateOnly.FromDateTime(DateTime.Today);

            // Índice consolidado = promedio por factor entre clientes, ponderado por los pesos del esquema
            var pesos = PesosFactores(cn, tipo);
            var suma = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var cont = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cli in req.clientes ?? new())
                foreach (var p in cli.puntajes ?? new())
                {
                    suma[p.factor] = suma.GetValueOrDefault(p.factor) + p.puntaje;
                    cont[p.factor] = cont.GetValueOrDefault(p.factor) + 1;
                }
            double num = 0, den = 0;
            foreach (var kv in pesos)
                if (cont.TryGetValue(kv.Key, out var n) && n > 0) { num += (suma[kv.Key] / n) * kv.Value; den += kv.Value; }
            double? indice = den > 0 ? Math.Round(num / den, 1) : (double?)null;
            string? banda = indice.HasValue ? BandaDe(indice.Value) : null;

            // Cabecera
            using var ins = new SqlCommand(@"
INSERT INTO dbo.SupervisionEvaluaciones_RM
 (Tipo,UsuInicioSesion,SupCod,SupervisorNombre,RepCodigo,RepNombre,Centro,Division,Fecha,Suplidores,
  FechaAcceso,HoraInicio,HoraFin,ClientesEval,Indice,Banda,Comentario,Compromiso,FirmaVendedor,FirmaSupervisor)
 OUTPUT INSERTED.EvaID
 VALUES (@t,@u,@sc,@sup,@rep,@rn,@cen,@div,@f,@supl,@fa,@hi,@hf,@ncli,@ind,@band,@com,@comp,@fv,@fs)", cn) { CommandTimeout = 60 };
            ins.Parameters.AddWithValue("@t", tipo);
            ins.Parameters.AddWithValue("@u", user);
            ins.Parameters.AddWithValue("@sc", (object?)req.supCod ?? DBNull.Value);
            ins.Parameters.AddWithValue("@sup", (object?)req.supervisor ?? DBNull.Value);
            ins.Parameters.AddWithValue("@rep", req.repCodigo);
            ins.Parameters.AddWithValue("@rn", (object?)req.repNombre ?? DBNull.Value);
            ins.Parameters.AddWithValue("@cen", (object?)req.centro ?? DBNull.Value);
            ins.Parameters.AddWithValue("@div", (object?)req.division ?? DBNull.Value);
            ins.Parameters.AddWithValue("@f", fecha.ToString("yyyy-MM-dd"));
            ins.Parameters.AddWithValue("@supl", (object?)req.suplidores ?? "Todos");
            ins.Parameters.AddWithValue("@fa", (object?)CombinarHora(fecha, req.fechaAcceso) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@hi", (object?)CombinarHora(fecha, req.horaInicio) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@hf", (object?)CombinarHora(fecha, req.horaFin) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@ncli", (req.clientes?.Count ?? 0));
            ins.Parameters.AddWithValue("@ind", (object?)indice ?? DBNull.Value);
            ins.Parameters.AddWithValue("@band", (object?)banda ?? DBNull.Value);
            ins.Parameters.AddWithValue("@com", (object?)req.comentario ?? DBNull.Value);
            ins.Parameters.AddWithValue("@comp", (object?)req.compromiso ?? DBNull.Value);
            ins.Parameters.AddWithValue("@fv", (object?)req.firmaVendedor ?? DBNull.Value);
            ins.Parameters.AddWithValue("@fs", (object?)req.firmaSupervisor ?? DBNull.Value);
            int evaID = Convert.ToInt32(ins.ExecuteScalar());

            // Detalle (cliente × factor) + fotos
            foreach (var cli in req.clientes ?? new())
            {
                foreach (var p in cli.puntajes ?? new())
                {
                    using var d = new SqlCommand(@"INSERT INTO dbo.SupervisionDetalle_RM (EvaID,CliID,CliCodigo,CliNombre,FactorCod,Puntaje)
                                                   VALUES (@e,@ci,@cc,@cn,@fc,@p)", cn);
                    d.Parameters.AddWithValue("@e", evaID);
                    d.Parameters.AddWithValue("@ci", cli.cliId);
                    d.Parameters.AddWithValue("@cc", (object?)cli.cliCod ?? DBNull.Value);
                    d.Parameters.AddWithValue("@cn", (object?)cli.cliNom ?? DBNull.Value);
                    d.Parameters.AddWithValue("@fc", p.factor);
                    d.Parameters.AddWithValue("@p", p.puntaje);
                    d.ExecuteNonQuery();
                }
                if (!string.IsNullOrWhiteSpace(cli.foto))
                {
                    using var fcmd = new SqlCommand(@"INSERT INTO dbo.SupervisionFotos_RM (EvaID,CliID,CliCodigo,Imagen) VALUES (@e,@ci,@cc,@img)", cn);
                    fcmd.Parameters.AddWithValue("@e", evaID);
                    fcmd.Parameters.AddWithValue("@ci", cli.cliId);
                    fcmd.Parameters.AddWithValue("@cc", (object?)cli.cliCod ?? DBNull.Value);
                    fcmd.Parameters.AddWithValue("@img", cli.foto);
                    fcmd.ExecuteNonQuery();
                }
                // Comentario por cliente (tabla opcional; si TI no la creó, se ignora)
                if (!string.IsNullOrWhiteSpace(cli.comentario))
                {
                    try
                    {
                        using var ccmd = new SqlCommand(@"INSERT INTO dbo.SupervisionClienteComent_RM (EvaID,CliID,Comentario) VALUES (@e,@ci,@cm)", cn);
                        ccmd.Parameters.AddWithValue("@e", evaID);
                        ccmd.Parameters.AddWithValue("@ci", cli.cliId);
                        ccmd.Parameters.AddWithValue("@cm", cli.comentario);
                        ccmd.ExecuteNonQuery();
                    }
                    catch { /* SupervisionClienteComent_RM aún no existe */ }
                }
            }
            return Ok(new { ok = true, evaID, indice, banda });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record ContactoReq(string repCodigo, string? repNombre, string? supCod, string? supervisor,
        string? centro, string? canal, string? contactoCon, string? suplidores, string? comentario, string? compromiso, List<string>? alertas);

    // ── POST /apisuperv/contacto (Supervisión Virtual) ──────────────────────
    [HttpPost("contacto")]
    public IActionResult GuardarContacto([FromBody] ContactoReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (req is null || string.IsNullOrWhiteSpace(req.repCodigo)) return BadRequest(new { ok = false, error = "Datos incompletos." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "";
            var alertasTxt = req.alertas is { Count: > 0 } ? string.Join(" · ", req.alertas) : null;
            // Indicadores: marca 'origen=supervision' (para distinguir de los contactos de Torre en la
            // misma tabla), + suplidores + alertas atendidas (Torre las respeta para el mute).
            var ind = System.Text.Json.JsonSerializer.Serialize(new { origen = "supervision", suplidores = req.suplidores ?? "Todos", atendidas = req.alertas ?? new List<string>() });
            using var cmd = new SqlCommand(@"
INSERT INTO dbo.TorreControlContactos_RM
 (UsuarioTorre,RepCodigo,RepNombre,SupCod,SupervisorNombre,Centro,Alertas,Canal,ContactoCon,Resumen,Compromiso,Indicadores)
 OUTPUT INSERTED.ConID, INSERTED.FechaContacto
 VALUES (@u,@rep,@rn,@sc,@sup,@cen,@ale,@can,@con,@res,@comp,@ind)", cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@u", user);
            cmd.Parameters.AddWithValue("@rep", req.repCodigo);
            cmd.Parameters.AddWithValue("@rn", (object?)req.repNombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sc", (object?)req.supCod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sup", (object?)req.supervisor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cen", (object?)req.centro ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ale", (object?)alertasTxt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@can", (object?)req.canal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@con", (object?)req.contactoCon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@res", (object?)req.comentario ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@comp", (object?)req.compromiso ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ind", ind);
            int conID = 0; string hora = DateTime.Now.ToString("HH:mm");
            using (var rd = cmd.ExecuteReader()) { if (rd.Read()) { conID = rd.GetInt32(0); hora = rd.GetDateTime(1).ToString("HH:mm"); } }
            int muteMin = TorreCore.LeerMuteMin(cn);
            return Ok(new { ok = true, conID, hora, muteMin });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // Códigos de supervisor en alcance para consultas de reportes (usa SupCod almacenado).
    private (bool ok, string filtroEva, string filtroCon) ScopeReportes(Ident id, Dictionary<string, object> parms)
    {
        if (id.Rol == "Ninguno") return (false, "", "");
        if (id.VeTodo) return (true, "", "");
        if (id.SupCods.Count == 0) return (true, "AND 1=0", "AND 1=0");
        var names = id.SupCods.Select((s, i) => { var n = $"@rc{i}"; parms[n] = s; return n; }).ToList();
        var inClause = "IN (" + string.Join(",", names) + ")";
        return (true, $"AND e.SupCod {inClause}", $"AND c.SupCod {inClause}");
    }

    // ── GET /apisuperv/reportes ─────────────────────────────────────────────
    [HttpGet("reportes")]
    public IActionResult Reportes([FromQuery] string? desde, [FromQuery] string? hasta, [FromQuery] string? tipo)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var parms = new Dictionary<string, object>();
            var (okScope, fEva, fCon) = ScopeReportes(id, parms);
            if (!okScope) return Ok(new { ok = true, filas = new List<object>(), error = "Usuario sin acceso." });
            var d = DateOnly.TryParse(desde, out var dd) ? dd : DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            var h = DateOnly.TryParse(hasta, out var hh) ? hh : DateOnly.FromDateTime(DateTime.Today);
            var w = PesosEsquemas(cn);

            // Evaluaciones de calle agregadas por rep
            var agg = new Dictionary<string, dynamic>();
            using (var cmd = new SqlCommand($@"
SELECT e.RepCodigo, MAX(e.RepNombre) RepNombre, MAX(e.SupCod) SupCod,
       SUM(CASE WHEN e.Tipo='acomp' THEN 1 ELSE 0 END) NAcomp,
       SUM(CASE WHEN e.Tipo='audit' THEN 1 ELSE 0 END) NAudit,
       SUM(ISNULL(e.ClientesEval,0)) Clientes,
       AVG(CASE WHEN e.Tipo='acomp' THEN e.Indice END) PromAcomp,
       AVG(CASE WHEN e.Tipo='audit' THEN e.Indice END) PromAudit
FROM dbo.SupervisionEvaluaciones_RM e WITH (NOLOCK)
WHERE e.Fecha>=@d AND e.Fecha<=@h {fEva}
GROUP BY e.RepCodigo", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@d", d.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@h", h.ToString("yyyy-MM-dd"));
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var rep = rd.GetString(0);
                    double? pa = rd.IsDBNull(6) ? null : Convert.ToDouble(rd.GetValue(6));
                    double? pu = rd.IsDBNull(7) ? null : Convert.ToDouble(rd.GetValue(7));
                    agg[rep] = new
                    {
                        RepCodigo = rep,
                        RepNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                        SupCod = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        NAcomp = rd.GetInt32(3),
                        NAudit = rd.GetInt32(4),
                        Clientes = rd.GetInt32(5),
                        PromAcomp = pa,
                        PromAudit = pu,
                        NVirt = 0
                    };
                }
            }
            // Contactos virtuales por rep
            using (var cmd = new SqlCommand($@"
SELECT c.RepCodigo, COUNT(*) N
FROM dbo.TorreControlContactos_RM c WITH (NOLOCK)
WHERE c.FechaContacto>=@d AND c.FechaContacto<@h2 AND c.Indicadores LIKE '%""origen"":""supervision""%' {fCon}
GROUP BY c.RepCodigo", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@d", d.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@h2", h.AddDays(1).ToDateTime(TimeOnly.MinValue));
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var rep = rd.GetString(0); int n = rd.GetInt32(1);
                    if (agg.TryGetValue(rep, out var a))
                        agg[rep] = new { a.RepCodigo, a.RepNombre, a.SupCod, a.NAcomp, a.NAudit, a.Clientes, a.PromAcomp, a.PromAudit, NVirt = n };
                    else
                        agg[rep] = new { RepCodigo = rep, RepNombre = "", SupCod = "", NAcomp = 0, NAudit = 0, Clientes = 0, PromAcomp = (double?)null, PromAudit = (double?)null, NVirt = n };
                }
            }
            var filas = agg.Values.Select(a =>
            {
                double? iegt = IegtDeRep(cn, (string)a.RepCodigo);
                double? global = GlobalIndex(iegt, (double?)a.PromAcomp, (double?)a.PromAudit, w);
                return new
                {
                    a.RepCodigo, a.RepNombre, a.SupCod, a.NAcomp, a.NAudit, a.NVirt, a.Clientes,
                    promAcomp = a.PromAcomp, promAudit = a.PromAudit, iegt, global,
                    banda = global.HasValue ? BandaDe(global.Value) : null
                };
            }).OrderByDescending(x => x.global ?? -1).ToList();

            return Ok(new { ok = true, rol = id.Rol, desde = d.ToString("yyyy-MM-dd"), hasta = h.ToString("yyyy-MM-dd"),
                            pesos = new { iegt = w.wi, acomp = w.wa, audit = w.wu }, iegtPendiente = true, filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/expediente?rep=XXX ───────────────────────────────────
    [HttpGet("expediente")]
    public IActionResult Expediente([FromQuery] string rep, [FromQuery] string? desde, [FromQuery] string? hasta)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(rep)) return BadRequest(new { ok = false, error = "Falta rep." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var d = DateOnly.TryParse(desde, out var dd) ? dd : DateOnly.FromDateTime(DateTime.Today.AddDays(-90));
            var h = DateOnly.TryParse(hasta, out var hh) ? hh : DateOnly.FromDateTime(DateTime.Today);
            var w = PesosEsquemas(cn);
            var evals = new List<object>();
            double sumA = 0, sumU = 0; int nA = 0, nU = 0, cli = 0;
            using (var cmd = new SqlCommand(@"
SELECT EvaID, Tipo, Fecha, HoraInicio, Indice, Banda, ClientesEval
FROM dbo.SupervisionEvaluaciones_RM WITH (NOLOCK)
WHERE RepCodigo=@rep AND Fecha>=@d AND Fecha<=@h ORDER BY Fecha DESC, EvaID DESC", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@rep", rep.Trim());
                cmd.Parameters.AddWithValue("@d", d.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@h", h.ToString("yyyy-MM-dd"));
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var tp = rd.GetString(1); double? ind = rd.IsDBNull(4) ? null : Convert.ToDouble(rd.GetValue(4));
                    if (ind.HasValue) { if (tp == "acomp") { sumA += ind.Value; nA++; } else { sumU += ind.Value; nU++; } }
                    cli += rd.IsDBNull(6) ? 0 : rd.GetInt32(6);
                    evals.Add(new
                    {
                        evaID = rd.GetInt32(0), tipo = tp, fecha = rd.GetDateTime(2).ToString("yyyy-MM-dd"),
                        hora = rd.IsDBNull(3) ? "" : rd.GetDateTime(3).ToString("HH:mm"),
                        indice = ind, banda = rd.IsDBNull(5) ? null : rd.GetString(5), clientes = rd.IsDBNull(6) ? 0 : rd.GetInt32(6)
                    });
                }
            }
            // Contactos virtuales del rep
            var contactos = new List<object>();
            using (var cmd = new SqlCommand(@"
SELECT TOP 100 ConID, FechaContacto, Canal, Resumen FROM dbo.TorreControlContactos_RM WITH (NOLOCK)
WHERE RepCodigo=@rep AND FechaContacto>=@d AND Indicadores LIKE '%""origen"":""supervision""%' ORDER BY FechaContacto DESC", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@rep", rep.Trim());
                cmd.Parameters.AddWithValue("@d", d.ToDateTime(TimeOnly.MinValue));
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    contactos.Add(new { conID = rd.GetInt32(0), fecha = rd.GetDateTime(1).ToString("yyyy-MM-dd"), hora = rd.GetDateTime(1).ToString("HH:mm"), canal = rd.IsDBNull(2) ? "" : rd.GetString(2), resumen = rd.IsDBNull(3) ? "" : rd.GetString(3) });
            }
            double? promAcomp = nA > 0 ? Math.Round(sumA / nA, 1) : (double?)null;
            double? promAudit = nU > 0 ? Math.Round(sumU / nU, 1) : (double?)null;
            double? iegt = IegtDeRep(cn, rep.Trim());
            double? global = GlobalIndex(iegt, promAcomp, promAudit, w);
            return Ok(new
            {
                ok = true, rep = rep.Trim(), promAcomp, promAudit, iegt, iegtPendiente = true,
                global, banda = global.HasValue ? BandaDe(global.Value) : null,
                nAcomp = nA, nAudit = nU, nVirt = contactos.Count, clientes = cli, evaluaciones = evals, contactos
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ANÁLISIS DE SUPERVISIÓN — reportes por rango, agrupables + detalle
    // ════════════════════════════════════════════════════════════════════════
    private static List<string> ParseCsv(string? s) =>
        string.IsNullOrWhiteSpace(s) ? new() :
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    private static string InClause(string col, List<string> vals, string pfx, Dictionary<string, object> parms)
    {
        if (vals.Count == 0) return "";
        var ns = vals.Select((v, i) => { var n = $"@{pfx}{i}"; parms[n] = v; return n; });
        return $"AND {col} IN ({string.Join(",", ns)})";
    }

    // ── GET /apisuperv/analisis ─────────────────────────────────────────────
    // Agrega evaluaciones (acomp/audit) + contactos virtuales por la dimensión:
    // compania | supervisor | vendedor | tipo | dia | factor.
    [HttpGet("analisis")]
    public IActionResult Analisis([FromQuery] string? desde, [FromQuery] string? hasta,
        [FromQuery] string? group, [FromQuery] string? tipos,
        [FromQuery] string? supervisores, [FromQuery] string? reps)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var parms = new Dictionary<string, object>();
            var (okScope, fEva, fCon) = ScopeReportes(id, parms);
            if (!okScope) return Ok(new { ok = true, group, filas = new List<object>(), error = "Usuario sin acceso." });

            var d = DateOnly.TryParse(desde, out var dd) ? dd : DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            var h = DateOnly.TryParse(hasta, out var hh) ? hh : DateOnly.FromDateTime(DateTime.Today);
            var g = (group ?? "vendedor").Trim().ToLowerInvariant();

            var lTipos = ParseCsv(tipos).Select(t => t.ToLowerInvariant()).ToList();
            var lSups = ParseCsv(supervisores);
            var lReps = ParseCsv(reps);
            var tiposCalle = lTipos.Where(t => t == "acomp" || t == "audit").ToList();
            bool incVirtual = lTipos.Count == 0 || lTipos.Contains("virtual");
            bool incCalle = lTipos.Count == 0 || tiposCalle.Count > 0;

            var pD = new Dictionary<string, object>(parms);
            var fSupE = InClause("e.SupCod", lSups, "se", pD);
            var fRepE = InClause("e.RepCodigo", lReps, "re", pD);
            var fTipoE = tiposCalle.Count > 0 ? InClause("e.Tipo", tiposCalle, "te", pD) : "";
            pD["@ed"] = d.ToString("yyyy-MM-dd"); pD["@eh"] = h.ToString("yyyy-MM-dd");

            // group = factor (tipo de pregunta): promedio por factor sobre el detalle
            if (g == "factor")
            {
                var filasF = new List<object>();
                if (incCalle)
                {
                    using var cmd = new SqlCommand($@"
SELECT d.FactorCod, AVG(CAST(d.Puntaje AS float)) Prom, COUNT(*) N, MAX(e.Tipo) Tipo
FROM dbo.SupervisionDetalle_RM d WITH (NOLOCK)
JOIN dbo.SupervisionEvaluaciones_RM e WITH (NOLOCK) ON e.EvaID=d.EvaID
WHERE e.Fecha>=@ed AND e.Fecha<=@eh {fEva} {fSupE} {fRepE} {fTipoE}
GROUP BY d.FactorCod ORDER BY Prom DESC", cn) { CommandTimeout = 60 };
                    foreach (var (k, v) in pD) cmd.Parameters.AddWithValue(k, v);
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        double prom = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1));
                        filasF.Add(new { key = rd.GetString(0), etiqueta = rd.GetString(0), tipo = rd.IsDBNull(3) ? "" : rd.GetString(3),
                                         indiceProm = (double?)Math.Round(prom, 1), estrellas = (int)Math.Round(prom / 20.0), nTotal = rd.GetInt32(2) });
                    }
                }
                return Ok(new { ok = true, group = g, desde = d.ToString("yyyy-MM-dd"), hasta = h.ToString("yyyy-MM-dd"), filas = filasF });
            }

            (string keyE, string labE) = g switch
            {
                "supervisor" => ("ISNULL(e.SupCod,'')", "MAX(ISNULL(e.SupervisorNombre, e.SupCod))"),
                "vendedor" => ("e.RepCodigo", "MAX(e.RepNombre)"),
                "tipo" => ("e.Tipo", "MAX(e.Tipo)"),
                "dia" => ("CONVERT(varchar(10), e.Fecha, 23)", "CONVERT(varchar(10), e.Fecha, 23)"),
                _ => ("'__all__'", "'Compañía'")
            };

            var acc = new Dictionary<string, double[]>(); // [nAcomp,nAudit,nVirt,clientes,durTotal,indSum,indCnt]
            var lab = new Dictionary<string, string>();

            if (incCalle)
            {
                using var cmd = new SqlCommand($@"
SELECT {keyE} K, {labE} Etiqueta,
   SUM(CASE WHEN e.Tipo='acomp' THEN 1 ELSE 0 END) NAcomp,
   SUM(CASE WHEN e.Tipo='audit' THEN 1 ELSE 0 END) NAudit,
   SUM(ISNULL(e.ClientesEval,0)) Clientes,
   SUM(CASE WHEN e.HoraInicio IS NOT NULL AND e.HoraFin IS NOT NULL THEN DATEDIFF(MINUTE,e.HoraInicio,e.HoraFin) ELSE 0 END) DurTotal,
   SUM(CASE WHEN e.Indice IS NOT NULL THEN e.Indice ELSE 0 END) IndSum,
   SUM(CASE WHEN e.Indice IS NOT NULL THEN 1 ELSE 0 END) IndCnt
FROM dbo.SupervisionEvaluaciones_RM e WITH (NOLOCK)
WHERE e.Fecha>=@ed AND e.Fecha<=@eh {fEva} {fSupE} {fRepE} {fTipoE}
GROUP BY {keyE}", cn) { CommandTimeout = 60 };
                foreach (var (k, v) in pD) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var key = rd.IsDBNull(0) ? "" : rd.GetValue(0).ToString()!;
                    lab[key] = rd.IsDBNull(1) ? key : rd.GetValue(1).ToString()!;
                    acc[key] = new double[] { rd.GetInt32(2), rd.GetInt32(3), 0, rd.GetInt32(4),
                        Convert.ToDouble(rd.GetValue(5)), Convert.ToDouble(rd.GetValue(6)), rd.GetInt32(7) };
                }
            }

            if (incVirtual)
            {
                var pV = new Dictionary<string, object>(parms);
                var fSupC = InClause("c.SupCod", lSups, "sc", pV);
                var fRepC = InClause("c.RepCodigo", lReps, "rc", pV);
                pV["@vd"] = d.ToDateTime(TimeOnly.MinValue); pV["@vh"] = h.AddDays(1).ToDateTime(TimeOnly.MinValue);
                (string keyC, string labC) = g switch
                {
                    "supervisor" => ("ISNULL(c.SupCod,'')", "MAX(ISNULL(c.SupervisorNombre, c.SupCod))"),
                    "vendedor" => ("c.RepCodigo", "MAX(c.RepNombre)"),
                    "tipo" => ("'virtual'", "'virtual'"),
                    "dia" => ("CONVERT(varchar(10), c.FechaContacto, 23)", "CONVERT(varchar(10), c.FechaContacto, 23)"),
                    _ => ("'__all__'", "'Compañía'")
                };
                using var cmd = new SqlCommand($@"
SELECT {keyC} K, {labC} Etiqueta, COUNT(*) N
FROM dbo.TorreControlContactos_RM c WITH (NOLOCK)
WHERE c.FechaContacto>=@vd AND c.FechaContacto<@vh AND c.Indicadores LIKE '%""origen"":""supervision""%' {fCon} {fSupC} {fRepC}
GROUP BY {keyC}", cn) { CommandTimeout = 60 };
                foreach (var (k, v) in pV) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var key = rd.IsDBNull(0) ? "" : rd.GetValue(0).ToString()!;
                    int n = rd.GetInt32(2);
                    if (!acc.TryGetValue(key, out var a)) { a = new double[7]; acc[key] = a; lab[key] = rd.IsDBNull(1) ? key : rd.GetValue(1).ToString()!; }
                    a[2] += n;
                }
            }

            var filas = acc.Select(kv =>
            {
                var a = kv.Value;
                int nAcomp = (int)a[0], nAudit = (int)a[1], nVirt = (int)a[2], clientes = (int)a[3];
                double durTot = a[4]; int indCnt = (int)a[6]; int nCalle = nAcomp + nAudit;
                double? indProm = indCnt > 0 ? Math.Round(a[5] / indCnt, 1) : (double?)null;
                return new
                {
                    key = kv.Key, etiqueta = lab.TryGetValue(kv.Key, out var l) ? l : kv.Key,
                    nAcomp, nAudit, nVirtual = nVirt, nTotal = nCalle + nVirt, clientes,
                    durTotal = (int)durTot, durProm = nCalle > 0 ? (int)Math.Round(durTot / nCalle, 0) : 0,
                    indiceProm = indProm, banda = indProm.HasValue ? BandaDe(indProm.Value) : null
                };
            }).OrderByDescending(x => x.nTotal).ToList();

            return Ok(new { ok = true, group = g, desde = d.ToString("yyyy-MM-dd"), hasta = h.ToString("yyyy-MM-dd"), filas });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/evaluaciones ─────────────────────────────────────────
    // Lista plana de evaluaciones (acomp/audit) + contactos virtuales del rango.
    [HttpGet("evaluaciones")]
    public IActionResult Evaluaciones([FromQuery] string? desde, [FromQuery] string? hasta,
        [FromQuery] string? tipos, [FromQuery] string? supervisores, [FromQuery] string? reps, [FromQuery] string? dia)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var parms = new Dictionary<string, object>();
            var (okScope, fEva, fCon) = ScopeReportes(id, parms);
            if (!okScope) return Ok(new { ok = true, evaluaciones = new List<object>() });
            var d = DateOnly.TryParse(desde, out var dd) ? dd : DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            var h = DateOnly.TryParse(hasta, out var hh) ? hh : DateOnly.FromDateTime(DateTime.Today);
            if (DateOnly.TryParse(dia, out var dsel)) { d = dsel; h = dsel; }
            var lTipos = ParseCsv(tipos).Select(t => t.ToLowerInvariant()).ToList();
            var tiposCalle = lTipos.Where(t => t == "acomp" || t == "audit").ToList();
            bool incVirtual = lTipos.Count == 0 || lTipos.Contains("virtual");
            bool incCalle = lTipos.Count == 0 || tiposCalle.Count > 0;
            var lSups = ParseCsv(supervisores); var lReps = ParseCsv(reps);
            var list = new List<object>();

            if (incCalle)
            {
                var pD = new Dictionary<string, object>(parms);
                var fSupE = InClause("e.SupCod", lSups, "se", pD); var fRepE = InClause("e.RepCodigo", lReps, "re", pD);
                var fTipoE = tiposCalle.Count > 0 ? InClause("e.Tipo", tiposCalle, "te", pD) : "";
                pD["@ed"] = d.ToString("yyyy-MM-dd"); pD["@eh"] = h.ToString("yyyy-MM-dd");
                using var cmd = new SqlCommand($@"
SELECT e.EvaID, e.Tipo, CONVERT(varchar(10),e.Fecha,23) Fecha, e.HoraInicio, e.HoraFin,
  e.RepCodigo, e.RepNombre, e.SupCod, e.SupervisorNombre, e.Centro, ISNULL(e.ClientesEval,0) Clientes, e.Indice, e.Banda,
  CASE WHEN e.HoraInicio IS NOT NULL AND e.HoraFin IS NOT NULL THEN DATEDIFF(MINUTE,e.HoraInicio,e.HoraFin) END DurMin
FROM dbo.SupervisionEvaluaciones_RM e WITH (NOLOCK)
WHERE e.Fecha>=@ed AND e.Fecha<=@eh {fEva} {fSupE} {fRepE} {fTipoE}
ORDER BY e.Fecha DESC, e.HoraInicio DESC", cn) { CommandTimeout = 60 };
                foreach (var (k, v) in pD) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    list.Add(new
                    {
                        id = rd.GetInt32(0), clase = "calle", tipo = rd.GetString(1), fecha = rd.GetString(2),
                        horaInicio = rd.IsDBNull(3) ? null : rd.GetDateTime(3).ToString("HH:mm"),
                        horaFin = rd.IsDBNull(4) ? null : rd.GetDateTime(4).ToString("HH:mm"),
                        rep = rd.GetString(5), repNombre = rd.IsDBNull(6) ? "" : rd.GetString(6),
                        supCod = rd.IsDBNull(7) ? "" : rd.GetString(7), supervisor = rd.IsDBNull(8) ? "" : rd.GetString(8),
                        centro = rd.IsDBNull(9) ? "" : rd.GetString(9), clientes = rd.GetInt32(10),
                        indice = rd.IsDBNull(11) ? (double?)null : Convert.ToDouble(rd.GetValue(11)),
                        banda = rd.IsDBNull(12) ? null : rd.GetString(12),
                        durMin = rd.IsDBNull(13) ? (int?)null : Convert.ToInt32(rd.GetValue(13)), resumen = (string?)null
                    });
            }
            if (incVirtual)
            {
                var pV = new Dictionary<string, object>(parms);
                var fSupC = InClause("c.SupCod", lSups, "sc", pV); var fRepC = InClause("c.RepCodigo", lReps, "rc", pV);
                pV["@vd"] = d.ToDateTime(TimeOnly.MinValue); pV["@vh"] = h.AddDays(1).ToDateTime(TimeOnly.MinValue);
                using var cmd = new SqlCommand($@"
SELECT c.ConID, CONVERT(varchar(10),c.FechaContacto,23) Fecha, c.FechaContacto, c.RepCodigo, c.RepNombre,
  c.SupCod, c.SupervisorNombre, c.Centro, c.Canal, c.Resumen
FROM dbo.TorreControlContactos_RM c WITH (NOLOCK)
WHERE c.FechaContacto>=@vd AND c.FechaContacto<@vh AND c.Indicadores LIKE '%""origen"":""supervision""%' {fCon} {fSupC} {fRepC}
ORDER BY c.FechaContacto DESC", cn) { CommandTimeout = 60 };
                foreach (var (k, v) in pV) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    list.Add(new
                    {
                        id = rd.GetInt32(0), clase = "virtual", tipo = "virtual", fecha = rd.GetString(1),
                        horaInicio = rd.GetDateTime(2).ToString("HH:mm"), horaFin = (string?)null,
                        rep = rd.GetString(3), repNombre = rd.IsDBNull(4) ? "" : rd.GetString(4),
                        supCod = rd.IsDBNull(5) ? "" : rd.GetString(5), supervisor = rd.IsDBNull(6) ? "" : rd.GetString(6),
                        centro = rd.IsDBNull(7) ? "" : rd.GetString(7), clientes = 0,
                        indice = (double?)null, banda = (string?)null, durMin = (int?)null,
                        resumen = rd.IsDBNull(9) ? "" : rd.GetString(9)
                    });
            }
            return Ok(new { ok = true, evaluaciones = list });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/evaluacion?id=NNN ────────────────────────────────────
    // Detalle completo de UNA evaluación de calle: cabecera, factores, clientes, fotos.
    [HttpGet("evaluacion")]
    public IActionResult EvaluacionDetalle([FromQuery] int id)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var ident = Identidad(cn);
            object? cab = null;
            using (var cmd = new SqlCommand(@"
SELECT Tipo,RepCodigo,RepNombre,ISNULL(SupCod,'') SupCod,SupervisorNombre,Centro,Division,CONVERT(varchar(10),Fecha,23) Fecha,
  Suplidores,FechaAcceso,HoraInicio,HoraFin,ClientesEval,Indice,Banda,Comentario,Compromiso,
  FirmaVendedor, FirmaSupervisor
FROM dbo.SupervisionEvaluaciones_RM WITH (NOLOCK) WHERE EvaID=@id", cn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using var rd = cmd.ExecuteReader();
                if (!rd.Read()) return NotFound(new { ok = false, error = "Evaluación no encontrada." });
                var supCod = rd.GetString(3);
                if (!ident.VeTodo && (ident.SupCods.Count == 0 || !ident.SupCods.Contains(supCod)))
                    return StatusCode(403, new { ok = false, error = "Sin acceso a esta evaluación." });
                DateTime? hi = rd.IsDBNull(10) ? (DateTime?)null : rd.GetDateTime(10);
                DateTime? hf = rd.IsDBNull(11) ? (DateTime?)null : rd.GetDateTime(11);
                int? durMin = (hi.HasValue && hf.HasValue) ? (int)(hf.Value - hi.Value).TotalMinutes : (int?)null;
                string? fvImg = rd.IsDBNull(17) ? null : rd.GetString(17);
                string? fsImg = rd.IsDBNull(18) ? null : rd.GetString(18);
                cab = new
                {
                    id, tipo = rd.GetString(0), rep = rd.GetString(1), repNombre = rd.IsDBNull(2) ? "" : rd.GetString(2),
                    supCod, supervisor = rd.IsDBNull(4) ? "" : rd.GetString(4), centro = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    division = rd.IsDBNull(6) ? "" : rd.GetString(6), fecha = rd.GetString(7), suplidores = rd.IsDBNull(8) ? "" : rd.GetString(8),
                    fechaAcceso = rd.IsDBNull(9) ? null : rd.GetDateTime(9).ToString("HH:mm"),
                    horaInicio = hi?.ToString("HH:mm"), horaFin = hf?.ToString("HH:mm"), durMin,
                    clientes = rd.IsDBNull(12) ? 0 : rd.GetInt32(12),
                    indice = rd.IsDBNull(13) ? (double?)null : Convert.ToDouble(rd.GetValue(13)),
                    banda = rd.IsDBNull(14) ? null : rd.GetString(14),
                    comentario = rd.IsDBNull(15) ? "" : rd.GetString(15), compromiso = rd.IsDBNull(16) ? "" : rd.GetString(16),
                    firmaVendedor = fvImg != null, firmaSupervisor = fsImg != null,
                    firmaVendedorImg = fvImg, firmaSupervisorImg = fsImg
                };
            }
            var factores = new List<object>();
            using (var cmd = new SqlCommand("SELECT FactorCod, AVG(CAST(Puntaje AS float)) Prom, COUNT(*) N FROM dbo.SupervisionDetalle_RM WITH (NOLOCK) WHERE EvaID=@id GROUP BY FactorCod", cn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) { double p = rd.IsDBNull(1) ? 0 : Convert.ToDouble(rd.GetValue(1)); factores.Add(new { factor = rd.GetString(0), prom = Math.Round(p, 1), estrellas = (int)Math.Round(p / 20.0), n = rd.GetInt32(2) }); }
            }
            // Matriz por cliente (en orden de visita = orden de inserción DetID) con su detalle por factor
            var order = new List<int>();
            var cod = new Dictionary<int, string>(); var nom = new Dictionary<int, string>();
            var punt = new Dictionary<int, List<object>>(); var sum = new Dictionary<int, double>(); var cnt = new Dictionary<int, int>();
            using (var cmd = new SqlCommand("SELECT CliID, ISNULL(CliCodigo,'') Cod, ISNULL(CliNombre,'') Nom, FactorCod, Puntaje FROM dbo.SupervisionDetalle_RM WITH (NOLOCK) WHERE EvaID=@id ORDER BY DetID", cn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    int cli = rd.GetInt32(0);
                    if (!punt.ContainsKey(cli)) { order.Add(cli); cod[cli] = rd.GetString(1); nom[cli] = rd.GetString(2); punt[cli] = new(); sum[cli] = 0; cnt[cli] = 0; }
                    var fc = rd.GetString(3); double pj = Convert.ToDouble(rd.GetValue(4));
                    punt[cli].Add(new { factor = fc, puntaje = pj });
                    sum[cli] += pj; cnt[cli]++;
                }
            }
            // Comentario por cliente (tabla opcional)
            var comMap = new Dictionary<int, string>();
            try
            {
                using var cmd = new SqlCommand("SELECT CliID, ISNULL(Comentario,'') FROM dbo.SupervisionClienteComent_RM WITH (NOLOCK) WHERE EvaID=@id", cn);
                cmd.Parameters.AddWithValue("@id", id);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) comMap[rd.GetInt32(0)] = rd.GetString(1);
            }
            catch { }
            // Foto por cliente (primera)
            var fotoMap = new Dictionary<int, string>();
            using (var cmd = new SqlCommand("SELECT CliID, Imagen FROM dbo.SupervisionFotos_RM WITH (NOLOCK) WHERE EvaID=@id ORDER BY FotoID", cn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) { int cli = rd.GetInt32(0); if (!fotoMap.ContainsKey(cli) && !rd.IsDBNull(1)) fotoMap[cli] = rd.GetString(1); }
            }
            var clientes = order.Select(cli => new
            {
                cliId = cli, cod = cod[cli], nombre = nom[cli],
                prom = cnt[cli] > 0 ? Math.Round(sum[cli] / cnt[cli], 1) : 0,
                estrellas = cnt[cli] > 0 ? (int)Math.Round(sum[cli] / cnt[cli] / 20.0) : 0,
                comentario = comMap.TryGetValue(cli, out var cm) ? cm : "",
                foto = fotoMap.TryGetValue(cli, out var ft) ? ft : null,
                puntajes = punt[cli]
            }).ToList();
            return Ok(new { ok = true, cabecera = cab, factores, clientes, fotos = fotoMap.Count });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/virtual ──────────────────────────────────────────────
    // Contactos de Supervisión Virtual (TorreControlContactos_RM, origen=supervision)
    // para el reporte por vendedor/rango y para el detalle de un contacto (id=ConID).
    [HttpGet("virtual")]
    public IActionResult Virtual([FromQuery] string? rep, [FromQuery] string? desde, [FromQuery] string? hasta,
        [FromQuery] int? id, [FromQuery] string? supervisores, [FromQuery] string? reps)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var ident = Identidad(cn);
            var parms = new Dictionary<string, object>();
            var (okScope, _, fCon) = ScopeReportes(ident, parms);
            if (!okScope) return Ok(new { ok = true, contactos = new List<object>() });
            var d = DateOnly.TryParse(desde, out var dd) ? dd : DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
            var h = DateOnly.TryParse(hasta, out var hh) ? hh : DateOnly.FromDateTime(DateTime.Today);
            var lSups = ParseCsv(supervisores); var lReps = ParseCsv(reps);
            var fSupC = InClause("c.SupCod", lSups, "sc", parms);
            var fRepC = InClause("c.RepCodigo", lReps, "rc", parms);
            var fRep = string.IsNullOrWhiteSpace(rep) ? "" : "AND c.RepCodigo=@rep";
            var fId = id.HasValue ? "AND c.ConID=@cid" : "";
            parms["@vd"] = d.ToDateTime(TimeOnly.MinValue); parms["@vh"] = h.AddDays(1).ToDateTime(TimeOnly.MinValue);
            if (!string.IsNullOrWhiteSpace(rep)) parms["@rep"] = rep.Trim();
            if (id.HasValue) parms["@cid"] = id.Value;

            var contactos = new List<object>();
            using var cmd = new SqlCommand($@"
SELECT c.ConID, c.FechaContacto, c.RepCodigo, c.RepNombre, ISNULL(c.SupCod,'') SupCod, c.SupervisorNombre, c.Centro,
       c.Canal, c.ContactoCon, c.Alertas, c.Resumen, c.Compromiso, c.Indicadores
FROM dbo.TorreControlContactos_RM c WITH (NOLOCK)
WHERE c.FechaContacto>=@vd AND c.FechaContacto<@vh AND c.Indicadores LIKE '%""origen"":""supervision""%' {fCon} {fSupC} {fRepC} {fRep} {fId}
ORDER BY c.FechaContacto", cn) { CommandTimeout = 60 };
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string suplidores = ""; var atendidas = new List<string>();
                if (!rd.IsDBNull(12))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(rd.GetString(12));
                        if (doc.RootElement.TryGetProperty("suplidores", out var sp) && sp.ValueKind == System.Text.Json.JsonValueKind.String) suplidores = sp.GetString() ?? "";
                        if (doc.RootElement.TryGetProperty("atendidas", out var at) && at.ValueKind == System.Text.Json.JsonValueKind.Array)
                            foreach (var a in at.EnumerateArray()) if (a.ValueKind == System.Text.Json.JsonValueKind.String) atendidas.Add(a.GetString() ?? "");
                    }
                    catch { }
                }
                var f = rd.GetDateTime(1);
                contactos.Add(new
                {
                    conID = rd.GetInt32(0), fecha = f.ToString("yyyy-MM-dd"), hora = f.ToString("HH:mm"),
                    rep = rd.GetString(2), repNombre = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    supCod = rd.GetString(4), supervisor = rd.IsDBNull(5) ? "" : rd.GetString(5), centro = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    canal = rd.IsDBNull(7) ? "" : rd.GetString(7), contactoCon = rd.IsDBNull(8) ? "" : rd.GetString(8),
                    alertas = rd.IsDBNull(9) ? "" : rd.GetString(9), resumen = rd.IsDBNull(10) ? "" : rd.GetString(10),
                    compromiso = rd.IsDBNull(11) ? "" : rd.GetString(11), suplidores, atendidas
                });
            }
            return Ok(new { ok = true, contactos });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CRONOGRAMA semanal (tipo de supervisión por rep × día) + prioridad
    // ════════════════════════════════════════════════════════════════════════
    // Default por día (1=Lun..6=Sáb): Lun=V, Mar=P, Mié=P, Jue=Auditoría, Vie=V, Sáb=V (2P·1A·3V).
    private static readonly string[] CronoDefault = { "virtual", "presencial", "presencial", "auditoria", "virtual", "virtual" };
    private static readonly string[] DiasSem = { "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb" };

    // ── GET /apisuperv/cronograma ───────────────────────────────────────────
    [HttpGet("cronograma")]
    public IActionResult Cronograma()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var parms = new Dictionary<string, object>();
            if (!RepFilter(id, parms, out var f))
                return Ok(new { ok = true, dias = DiasSem, filas = new List<object>(), error = "Usuario sin acceso." });

            // Reps del alcance
            var reps = new List<(string cod, string nom, string sup, string supcod, string cen, string div)>();
            using (var cmd = new SqlCommand($@"
SELECT r.RepCodigo, LTRIM(RTRIM(r.RepNombre)) AS RepNombre, ISNULL(cd.CedDescripcion,r.CedCodigo) AS Centro, ISNULL(r.RepSupervisor,'') AS SupCod,
       LTRIM(RTRIM(ISNULL(sup.RepNombre,r.RepSupervisor))) AS Supervisor, ISNULL(ra.RepTipe,'') AS Division
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor' {f}
ORDER BY Supervisor, RepNombre", cn) { CommandTimeout = 60 })
            {
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    reps.Add((rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), rd.IsDBNull(4) ? "" : rd.GetString(4),
                              rd.IsDBNull(3) ? "" : rd.GetString(3), rd.IsDBNull(2) ? "" : rd.GetString(2), rd.IsDBNull(5) ? "" : rd.GetString(5)));
            }
            // Overrides de cronograma
            var cro = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand("SELECT RepCodigo, DiaSemana, Tipo FROM dbo.SupervisionCronograma_RM WITH (NOLOCK)", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                {
                    var rep = rd.GetString(0); int dia = Convert.ToInt32(rd.GetValue(1)); var tipo = rd.GetString(2);
                    if (dia < 1 || dia > 6) continue;
                    if (!cro.TryGetValue(rep, out var arr)) { arr = (string[])CronoDefault.Clone(); cro[rep] = arr; }
                    arr[dia - 1] = tipo;
                }
            // Prioridades
            var prio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand("SELECT RepCodigo FROM dbo.SupervisionRepConfig_RM WITH (NOLOCK) WHERE Prioritario=1", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read()) prio.Add(rd.GetString(0));

            // Días por supervisor (patrón del equipo). La tabla puede no existir aún → default.
            var supDias = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var cmd = new SqlCommand("SELECT SupCod, DiaSemana, Tipo FROM dbo.SupervisionSupDias_RM WITH (NOLOCK)", cn);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var sc = rd.GetString(0); int dia = Convert.ToInt32(rd.GetValue(1)); var tipo = rd.GetString(2);
                    if (dia < 1 || dia > 6) continue;
                    if (!supDias.TryGetValue(sc, out var a2)) { a2 = (string[])CronoDefault.Clone(); supDias[sc] = a2; }
                    a2[dia - 1] = tipo;
                }
            }
            catch { }
            // Último acompañamiento presencial por rep (para la rotación mensual).
            var ultPres = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var cmd = new SqlCommand("SELECT RepCodigo, MAX(Fecha) FROM dbo.SupervisionEvaluaciones_RM WITH (NOLOCK) WHERE Tipo='acomp' GROUP BY RepCodigo", cn);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) if (!rd.IsDBNull(1)) ultPres[rd.GetString(0)] = rd.GetDateTime(1);
            }
            catch { }

            string[] TiposDe(string rep, string supcod) =>
                cro.TryGetValue(rep, out var ro) ? ro : (supDias.TryGetValue(supcod, out var sp) ? sp : CronoDefault);

            var filas = reps.Select(r => new
            {
                rep = r.cod, nombre = r.nom, supervisor = r.sup, supcod = r.supcod, centro = r.cen, division = r.div,
                prioritario = prio.Contains(r.cod),
                tipos = TiposDe(r.cod, r.supcod),
                ultPresencial = ultPres.TryGetValue(r.cod, out var up) ? up.ToString("yyyy-MM-dd") : null
            }).ToList();
            // Patrón semanal por supervisor (para la configuración por supervisor)
            var supervisores = reps.Where(r => !string.IsNullOrEmpty(r.supcod))
                .GroupBy(r => r.supcod)
                .Select(g => new { supcod = g.Key, nombre = g.First().sup, dias = supDias.TryGetValue(g.Key, out var sp) ? sp : CronoDefault })
                .OrderBy(x => x.nombre).ToList();
            return Ok(new { ok = true, dias = DiasSem, filas, supervisores });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record CronoItem(string rep, int dia, string tipo);
    public sealed record PrioItem(string rep, bool prioritario);
    public sealed record SupDiaItem(string supcod, int dia, string tipo);
    public sealed record CronoReq(List<CronoItem>? items, List<PrioItem>? prioridades, List<SupDiaItem>? supdias);

    // ── POST /apisuperv/cronograma ──────────────────────────────────────────
    [HttpPost("cronograma")]
    public IActionResult GuardarCronograma([FromBody] CronoReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            int n = 0;
            foreach (var it in req?.items ?? new())
            {
                if (it.dia < 1 || it.dia > 6) continue;
                var tipo = it.tipo == "presencial" || it.tipo == "auditoria" || it.tipo == "virtual" ? it.tipo : "virtual";
                using var cmd = new SqlCommand(@"
UPDATE dbo.SupervisionCronograma_RM SET Tipo=@t, FechaActualizacion=GETDATE() WHERE RepCodigo=@r AND DiaSemana=@d;
IF @@ROWCOUNT=0 INSERT INTO dbo.SupervisionCronograma_RM (RepCodigo,DiaSemana,Tipo) VALUES (@r,@d,@t);", cn);
                cmd.Parameters.AddWithValue("@r", it.rep); cmd.Parameters.AddWithValue("@d", it.dia); cmd.Parameters.AddWithValue("@t", tipo);
                n += cmd.ExecuteNonQuery();
            }
            foreach (var pr in req?.prioridades ?? new())
            {
                using var cmd = new SqlCommand(@"
UPDATE dbo.SupervisionRepConfig_RM SET Prioritario=@p, FechaActualizacion=GETDATE() WHERE RepCodigo=@r;
IF @@ROWCOUNT=0 INSERT INTO dbo.SupervisionRepConfig_RM (RepCodigo,Prioritario) VALUES (@r,@p);", cn);
                cmd.Parameters.AddWithValue("@r", pr.rep); cmd.Parameters.AddWithValue("@p", pr.prioritario);
                cmd.ExecuteNonQuery();
            }
            // Días por supervisor (tabla puede no existir aún)
            try
            {
                foreach (var sd in req?.supdias ?? new())
                {
                    if (sd.dia < 1 || sd.dia > 6) continue;
                    var tipo = sd.tipo == "presencial" || sd.tipo == "auditoria" || sd.tipo == "virtual" ? sd.tipo : "virtual";
                    using var cmd = new SqlCommand(@"
UPDATE dbo.SupervisionSupDias_RM SET Tipo=@t, FechaActualizacion=GETDATE() WHERE SupCod=@s AND DiaSemana=@d;
IF @@ROWCOUNT=0 INSERT INTO dbo.SupervisionSupDias_RM (SupCod,DiaSemana,Tipo) VALUES (@s,@d,@t);", cn);
                    cmd.Parameters.AddWithValue("@s", sd.supcod); cmd.Parameters.AddWithValue("@d", sd.dia); cmd.Parameters.AddWithValue("@t", tipo);
                    cmd.ExecuteNonQuery(); n++;
                }
            }
            catch (Exception ex) { return Ok(new { ok = true, guardados = n, aviso = "Días por supervisor no se guardaron (¿falta crear SupervisionSupDias_RM?): " + ex.Message }); }
            return Ok(new { ok = true, guardados = n });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MANTENIMIENTO DE ACCESOS — SupervisionUsuarioMapa_RM (SOLO Gerencia)
    //  Permite a Gerencia otorgar/editar/quitar qué representantes ve cada
    //  usuario del sistema: Gerencia (ve todo) o acceso limitado a N supervisores.
    // ════════════════════════════════════════════════════════════════════════
    private sealed class UsuAcc { public string login = "", nombre = "", rol = ""; public bool activo = true; public List<string> supcods = new(); }

    // ── GET /apisuperv/mapa ─────────────────────────────────────────────────
    [HttpGet("mapa")]
    public IActionResult Mapa()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede administrar accesos." });

            // Catálogo de supervisores (SupCod con reps activos) → nombre + conteo
            var supMap = new Dictionary<string, (string nombre, int reps)>(StringComparer.OrdinalIgnoreCase);
            var supervisores = new List<object>();
            using (var cmd = new SqlCommand(@"
SELECT r.RepSupervisor AS SupCod, MAX(LTRIM(RTRIM(ISNULL(sup.RepNombre, r.RepSupervisor)))) AS Nombre, COUNT(*) AS Reps
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo = r.RepSupervisor
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor'
  AND r.RepSupervisor IS NOT NULL AND r.RepSupervisor<>''
GROUP BY r.RepSupervisor
ORDER BY Nombre", cn) { CommandTimeout = 60 })
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                {
                    var sc = rd.GetString(0).Trim();
                    var nm = rd.IsDBNull(1) ? sc : rd.GetString(1);
                    var rp = rd.GetInt32(2);
                    supMap[sc] = (nm, rp);
                    supervisores.Add(new { supcod = sc, nombre = nm, reps = rp });
                }

            // Accesos actuales, agrupados por login
            var mapa = new Dictionary<string, UsuAcc>(StringComparer.OrdinalIgnoreCase);
            var orden = new List<string>();
            using (var cmd = new SqlCommand(@"
SELECT UsuInicioSesion, ISNULL(SupCod,'') SupCod, ISNULL(Nombre,'') Nombre, Rol, Activo
FROM dbo.SupervisionUsuarioMapa_RM WITH (NOLOCK)
ORDER BY UsuInicioSesion, SupCod", cn))
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                {
                    var login = rd.GetString(0);
                    if (!mapa.TryGetValue(login, out var u))
                    {
                        u = new UsuAcc { login = login, nombre = rd.GetString(2), rol = rd.GetString(3).Trim(), activo = Convert.ToBoolean(rd.GetValue(4)) };
                        mapa[login] = u; orden.Add(login);
                    }
                    var sc = rd.GetString(1).Trim();
                    if (sc.Length > 0) u.supcods.Add(sc);
                }

            var usuarios = orden.Select(l =>
            {
                var u = mapa[l];
                var equipos = u.supcods.Select(sc => new { supcod = sc, nombre = supMap.TryGetValue(sc, out var t) ? t.nombre : sc, reps = supMap.TryGetValue(sc, out var t2) ? t2.reps : 0 }).ToList();
                return new { login = u.login, nombre = u.nombre, rol = u.rol, activo = u.activo, veTodo = u.rol == "Gerencia", equipos, esYo = string.Equals(u.login, id.User, StringComparison.OrdinalIgnoreCase) };
            }).ToList();

            // Catálogo de logins del sistema (activos) para el desplegable
            var logins = new List<object>();
            using (var cmd = new SqlCommand(@"
SELECT UsuInicioSesion, LTRIM(RTRIM(ISNULL(UsuNombres,'') + ' ' + ISNULL(UsuApellidos,''))) Nombre
FROM UsuarioSistema WITH (NOLOCK)
WHERE ISNULL(UsuEstatus,0)=1
ORDER BY UsuInicioSesion", cn) { CommandTimeout = 60 })
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    logins.Add(new { login = rd.GetString(0).Trim(), nombre = rd.IsDBNull(1) ? "" : rd.GetString(1).Trim() });

            return Ok(new { ok = true, usuarios, logins, supervisores });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record MapaReq(string? login, string? rol, List<string>? supcods);

    // ── POST /apisuperv/mapa ────────────────────────────────────────────────
    // Guarda (reemplaza) el acceso de UN usuario. Rol 'Gerencia' → ve todo;
    // 'Supervisor' → una fila por SupCod seleccionado. El nombre se toma de
    // UsuarioSistema (bytes correctos, sin problemas de acentos/ñ).
    [HttpPost("mapa")]
    public IActionResult GuardarMapa([FromBody] MapaReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede administrar accesos." });

            var login = req?.login?.Trim();
            if (string.IsNullOrWhiteSpace(login)) return BadRequest(new { ok = false, error = "Falta el usuario." });
            var rol = req?.rol == "Gerencia" ? "Gerencia" : "Supervisor";

            // Anti-bloqueo: Gerencia no puede quitarse a sí misma el rol Gerencia.
            if (string.Equals(login, id.User, StringComparison.OrdinalIgnoreCase) && rol != "Gerencia")
                return BadRequest(new { ok = false, error = "No puedes quitarte a ti mismo el rol Gerencia (evita bloqueo)." });

            // Nombre desde UsuarioSistema (evita el problema de la ñ). Valida que exista.
            string nombre = login;
            using (var q = new SqlCommand(@"SELECT TOP 1 LTRIM(RTRIM(ISNULL(UsuNombres,'') + ' ' + ISNULL(UsuApellidos,''))) FROM UsuarioSistema WITH (NOLOCK) WHERE UsuInicioSesion=@l", cn))
            {
                q.Parameters.AddWithValue("@l", login);
                var o = q.ExecuteScalar();
                if (o is null) return BadRequest(new { ok = false, error = $"El usuario '{login}' no existe en UsuarioSistema." });
                var nm = o?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(nm)) nombre = nm!;
            }

            var supcods = (req?.supcods ?? new()).Select(s => s.Trim()).Where(s => s.Length > 0)
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (rol == "Supervisor" && supcods.Count == 0)
                return BadRequest(new { ok = false, error = "Selecciona al menos un supervisor para acceso limitado (o elige Gerencia)." });

            using var tx = cn.BeginTransaction();
            try
            {
                using (var del = new SqlCommand("DELETE FROM dbo.SupervisionUsuarioMapa_RM WHERE UsuInicioSesion=@l", cn, tx))
                { del.Parameters.AddWithValue("@l", login); del.ExecuteNonQuery(); }

                if (rol == "Gerencia")
                {
                    using var i = new SqlCommand("INSERT INTO dbo.SupervisionUsuarioMapa_RM (UsuInicioSesion,SupCod,Nombre,Rol) VALUES (@l,NULL,@n,'Gerencia')", cn, tx);
                    i.Parameters.AddWithValue("@l", login); i.Parameters.AddWithValue("@n", nombre); i.ExecuteNonQuery();
                }
                else
                {
                    foreach (var sc in supcods)
                    {
                        using var i = new SqlCommand("INSERT INTO dbo.SupervisionUsuarioMapa_RM (UsuInicioSesion,SupCod,Nombre,Rol) VALUES (@l,@s,@n,'Supervisor')", cn, tx);
                        i.Parameters.AddWithValue("@l", login); i.Parameters.AddWithValue("@s", sc); i.Parameters.AddWithValue("@n", nombre); i.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }

            return Ok(new { ok = true, login, nombre, rol, supcods });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record MapaDelReq(string? login);

    // ── POST /apisuperv/mapa-eliminar ───────────────────────────────────────
    [HttpPost("mapa-eliminar")]
    public IActionResult EliminarMapa([FromBody] MapaDelReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede administrar accesos." });

            var login = req?.login?.Trim();
            if (string.IsNullOrWhiteSpace(login)) return BadRequest(new { ok = false, error = "Falta el usuario." });
            if (string.Equals(login, id.User, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { ok = false, error = "No puedes quitar tu propio acceso (evita bloqueo)." });

            using var del = new SqlCommand("DELETE FROM dbo.SupervisionUsuarioMapa_RM WHERE UsuInicioSesion=@l", cn);
            del.Parameters.AddWithValue("@l", login);
            int n = del.ExecuteNonQuery();
            return Ok(new { ok = true, eliminadas = n });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  COBROS DEL DÍA — metas diarias configurables (Verde/Amarillo/Rojo por
    //  vendedor), entorno igual a Virtual: contacto por RECURSO (vendedor o
    //  supervisor) + mute 60 min + bitácora 3x/día (9:50/11:50/15:30) + reporte
    //  de contactos + tarjetas resumen. 3 KPIs calculados: presupuesto
    //  (PresupuestosHistoricos), pedidos (distintos, VWPedidos) y producto
    //  (VWPedidosDetalle por ProID). Los demás Tipo (unidades, activaciones,
    //  impactos/facturas, montos, fotos) quedan preparados en Configuración.
    // ════════════════════════════════════════════════════════════════════════
    private sealed class CobrosFila
    {
        public string rep = "", nombre = "", supcod = "", supervisor = "";
        public double meta, logro, logroPct; public double? proyPct; public string semaforo = "";
    }

    private sealed class CobrosCfgRow
    {
        public int cfgId; public string nombre = "", tipo = ""; public string? preTipo;
        public double? metaFija; public int? proId; public string? division; public string? excluirSupCods;
        public string? medicion; public int? linId; public int? cat3Id;   // Tipo='activaciones': medicion 'producto'(proId) | 'categoria'(linId) | 'suplidor'(cat3Id)
        public DateTime? vigenciaDesde, vigenciaHasta;
        public double umbralVerde = 90, umbralAmarillo = 80;
        public bool Vigente(DateTime hoy) =>
            (!vigenciaDesde.HasValue || hoy.Date >= vigenciaDesde.Value.Date) &&
            (!vigenciaHasta.HasValue || hoy.Date <= vigenciaHasta.Value.Date);
    }

    // Catálogo de alertas de Cobros del día. Si la tabla aún no existe, cae a
    // un único KPI por defecto (presupuesto diario) para que el módulo funcione
    // igual mientras TI corre SQL/Supervision_Cobros_RM.sql.
    private List<CobrosCfgRow> LeerCobrosCfgs(SqlConnection cn, bool soloActivos)
    {
        var list = new List<CobrosCfgRow>();
        try
        {
            using var cmd = new SqlCommand($@"SELECT CfgID,Nombre,Tipo,PreTipo,MetaFija,ProID,Division,ExcluirSupCods,VigenciaDesde,VigenciaHasta,UmbralVerde,UmbralAmarillo,Medicion,LinID,Cat3ID
FROM dbo.SupervisionCobrosConfig_RM WITH (NOLOCK) {(soloActivos ? "WHERE Activo=1" : "")} ORDER BY Orden, CfgID", cn);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                list.Add(new CobrosCfgRow
                {
                    cfgId = rd.GetInt32(0), nombre = rd.GetString(1), tipo = rd.GetString(2),
                    preTipo = rd.IsDBNull(3) ? null : rd.GetString(3),
                    metaFija = rd.IsDBNull(4) ? (double?)null : Convert.ToDouble(rd.GetValue(4)),
                    proId = rd.IsDBNull(5) ? (int?)null : rd.GetInt32(5),
                    division = rd.IsDBNull(6) ? null : rd.GetString(6),
                    excluirSupCods = rd.IsDBNull(7) ? null : rd.GetString(7),
                    vigenciaDesde = rd.IsDBNull(8) ? (DateTime?)null : rd.GetDateTime(8),
                    vigenciaHasta = rd.IsDBNull(9) ? (DateTime?)null : rd.GetDateTime(9),
                    umbralVerde = Convert.ToDouble(rd.GetValue(10)), umbralAmarillo = Convert.ToDouble(rd.GetValue(11)),
                    medicion = rd.IsDBNull(12) ? null : rd.GetString(12),
                    linId = rd.IsDBNull(13) ? (int?)null : rd.GetInt32(13),
                    cat3Id = rd.IsDBNull(14) ? (int?)null : rd.GetInt32(14)
                });
        }
        catch { /* SupervisionCobrosConfig_RM aún no existe */ }
        if (list.Count == 0)
            list.Add(new CobrosCfgRow { cfgId = 0, nombre = "Presupuesto del día", tipo = "presupuesto", preTipo = "MNTDCAT1DIA", umbralVerde = 90, umbralAmarillo = 80 });
        return list;
    }

    private (double logroPct, double? proyPct, string semaforo) EvaluarSemaforo(CobrosCfgRow cfg, double meta, double logro, DateTime now)
    {
        // Ventana del día: 8:00 (arranque) → 15:30 (cierre/corte final, no se proyecta).
        var inicio = now.Date.AddHours(8); var cierre = now.Date.AddHours(15).AddMinutes(30);
        double totalHoras = (cierre - inicio).TotalHours;
        bool esFinal = now >= cierre;
        double transcurridas = Math.Max(0.05, Math.Min(totalHoras, (now - inicio).TotalHours));
        double logroPct = meta > 0 ? logro * 100.0 / meta : 0;
        double? proyPct = null; double pctSem;
        if (esFinal) pctSem = logroPct;
        else
        {
            double proyLogro = logro / (transcurridas / totalHoras);   // regla de 3 por tiempo transcurrido
            proyPct = meta > 0 ? proyLogro * 100.0 / meta : 0;
            pctSem = proyPct.Value;
        }
        string sem = pctSem >= cfg.umbralVerde ? "verde" : (pctSem >= cfg.umbralAmarillo ? "amarillo" : "rojo");
        return (logroPct, proyPct, sem);
    }

    private (Dictionary<string, double> meta, Dictionary<string, double> logro) ValoresPresupuesto(SqlConnection cn, string preTipo, DateTime now)
    {
        var meta = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var logro = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(@"
SELECT RepCodigo, SUM(PrePresupuesto), SUM(PreEjecutado)
FROM PresupuestosHistoricos WITH (NOLOCK)
WHERE PreTipo=@pt AND PreAnio=@an AND PreMes=@me AND LEFT(PreReferencia,2)=@di
GROUP BY RepCodigo", cn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@pt", preTipo);
        cmd.Parameters.AddWithValue("@an", now.Year);
        cmd.Parameters.AddWithValue("@me", now.Month);
        cmd.Parameters.AddWithValue("@di", now.Day.ToString("00"));
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) { var rep = rd.GetString(0); meta[rep] = Convert.ToDouble(rd.GetValue(1)); logro[rep] = Convert.ToDouble(rd.GetValue(2)); }
        return (meta, logro);
    }

    // "Pedidos distintos" del día = COUNT(DISTINCT PedSecuencia), mismo patrón que
    // TorrePersonalizados.cs (métrica "impactos").
    private Dictionary<string, double> ValoresPedidosHoy(SqlConnection cn, DateTime now)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(@"
SELECT RepCodigo, COUNT(DISTINCT PedSecuencia) FROM VWPedidos WITH (NOLOCK)
WHERE PedFecha>=@d0 AND PedFecha<@d1 GROUP BY RepCodigo", cn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@d0", now.Date);
        cmd.Parameters.AddWithValue("@d1", now.Date.AddDays(1));
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) d[rd.GetString(0)] = Convert.ToDouble(rd.GetValue(1));
        return d;
    }

    private Dictionary<string, double> ValoresProductoHoy(SqlConnection cn, int proId, DateTime now)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(@"
SELECT d.RepCodigo, COUNT(DISTINCT d.PedSecuencia)
FROM VWPedidosDetalle d WITH (NOLOCK)
JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia=d.PedSecuencia AND p.RepCodigo=d.RepCodigo
WHERE p.PedFecha>=@d0 AND p.PedFecha<@d1 AND d.ProID=@pid
GROUP BY d.RepCodigo", cn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@d0", now.Date);
        cmd.Parameters.AddWithValue("@d1", now.Date.AddDays(1));
        cmd.Parameters.AddWithValue("@pid", proId);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) d[rd.GetString(0)] = Convert.ToDouble(rd.GetValue(1));
        return d;
    }

    // "Activaciones" del día = COUNT(DISTINCT CliID) por vendedor — clientes distintos
    // que compraron el producto (ProID), la categoría/línea (LinID) o el suplidor
    // (Cat3ID) hoy. Mismo patrón que TorrePersonalizados.cs (métrica "clientes"),
    // pero acotado a un solo día.
    private Dictionary<string, double> ValoresActivacionesHoy(SqlConnection cn, string medicion, int? proId, int? linId, int? cat3Id, DateTime now)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        string sql; string parNombre; int valor;
        if (medicion == "producto") { if (!proId.HasValue) return d; sql = @"
SELECT d.RepCodigo, COUNT(DISTINCT d.CliID)
FROM VWPedidosDetalle d WITH (NOLOCK)
JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia=d.PedSecuencia AND p.RepCodigo=d.RepCodigo
WHERE p.PedFecha>=@d0 AND p.PedFecha<@d1 AND d.ProID=@val
GROUP BY d.RepCodigo"; parNombre = "@val"; valor = proId.Value; }
        else if (medicion == "categoria") { if (!linId.HasValue) return d; sql = @"
SELECT d.RepCodigo, COUNT(DISTINCT d.CliID)
FROM VWPedidosDetalle d WITH (NOLOCK)
JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia=d.PedSecuencia AND p.RepCodigo=d.RepCodigo
JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE p.PedFecha>=@d0 AND p.PedFecha<@d1 AND pr.LinID=@val
GROUP BY d.RepCodigo"; parNombre = "@val"; valor = linId.Value; }
        else if (medicion == "suplidor") { if (!cat3Id.HasValue) return d; sql = @"
SELECT d.RepCodigo, COUNT(DISTINCT d.CliID)
FROM VWPedidosDetalle d WITH (NOLOCK)
JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia=d.PedSecuencia AND p.RepCodigo=d.RepCodigo
JOIN Productos pr WITH (NOLOCK) ON pr.ProID=d.ProID
WHERE p.PedFecha>=@d0 AND p.PedFecha<@d1 AND pr.Cat3ID=@val
GROUP BY d.RepCodigo"; parNombre = "@val"; valor = cat3Id.Value; }
        else return d;
        using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        cmd.Parameters.AddWithValue("@d0", now.Date);
        cmd.Parameters.AddWithValue("@d1", now.Date.AddDays(1));
        cmd.Parameters.AddWithValue(parNombre, valor);
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) d[rd.GetString(0)] = Convert.ToDouble(rd.GetValue(1));
        return d;
    }

    // Calcula meta/logro/%/semáforo por vendedor para UN KPI, ya filtrado al alcance
    // del usuario (+ división si el KPI la exige + exclusión de supervisores).
    // Peor primero (el que va peor, en primer plano).
    private List<CobrosFila> CalcularCobros(SqlConnection cn, Ident id, CobrosCfgRow cfg)
    {
        var now = DateTime.Now;
        if (!cfg.Vigente(now)) return new List<CobrosFila>();
        var parms = new Dictionary<string, object>();
        if (!RepFilter(id, parms, out var f)) return new List<CobrosFila>();

        // Asignacion fina (cotejar/descotejar). Si la alerta YA paso por el
        // mantenimiento => set no-nulo (autoridad, universo div 10/11 completo);
        // si nunca paso => null => se respeta la logica Division/ExcluirSupCods.
        var asignados = LeerAsignacionCobros(cn, cfg.cfgId);

        // Con mantenimiento, la asignacion manda sobre TODO el universo: no se
        // aplica el filtro grueso de Division en el query (evita dejar fuera a un
        // vendedor cotejado de otra division).
        var divFiltro = (asignados == null && !string.IsNullOrWhiteSpace(cfg.division)) ? " AND ra.RepTipe=@div" : "";
        var reps = new List<(string cod, string nom, string sc, string sv)>();
        using (var cmd = new SqlCommand($@"
SELECT r.RepCodigo, LTRIM(RTRIM(r.RepNombre)) RepNombre, ISNULL(r.RepSupervisor,'') SupCod,
       LTRIM(RTRIM(ISNULL(sup.RepNombre,r.RepSupervisor))) Supervisor
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor' {f}{divFiltro}", cn) { CommandTimeout = 60 })
        {
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            if (divFiltro.Length > 0) cmd.Parameters.AddWithValue("@div", cfg.division);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                reps.Add((rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2), rd.IsDBNull(3) ? "" : rd.GetString(3)));
        }

        HashSet<string>? excluir = null;
        if (!string.IsNullOrWhiteSpace(cfg.excluirSupCods))
            excluir = cfg.excluirSupCods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, double> metaPorRep, logroPorRep;
        if (cfg.tipo == "presupuesto")
        {
            (metaPorRep, logroPorRep) = ValoresPresupuesto(cn, cfg.preTipo ?? "MNTDCAT1DIA", now);
        }
        else if (cfg.tipo == "pedidos")
        {
            logroPorRep = ValoresPedidosHoy(cn, now);
            metaPorRep = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in reps) metaPorRep[r.cod] = cfg.metaFija ?? 0;
        }
        else if (cfg.tipo == "producto" && cfg.proId.HasValue)
        {
            logroPorRep = ValoresProductoHoy(cn, cfg.proId.Value, now);
            metaPorRep = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in reps) metaPorRep[r.cod] = cfg.metaFija ?? 0;
        }
        else if (cfg.tipo == "activaciones" && !string.IsNullOrWhiteSpace(cfg.medicion))
        {
            logroPorRep = ValoresActivacionesHoy(cn, cfg.medicion, cfg.proId, cfg.linId, cfg.cat3Id, now);
            metaPorRep = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in reps) metaPorRep[r.cod] = cfg.metaFija ?? 0;
        }
        else
        {
            return new List<CobrosFila>();   // tipo aún sin fuente real (unidades/impactos/montos/fotos/personalizado, o activaciones sin medición)
        }

        var list = new List<CobrosFila>();
        foreach (var r in reps)
        {
            if (asignados != null) { if (!asignados.Contains(r.cod)) continue; }   // mantenimiento manda
            else if (excluir != null && excluir.Contains(r.sc)) continue;          // sin mantenimiento: filtro grueso
            if (cfg.tipo == "presupuesto" && !metaPorRep.ContainsKey(r.cod)) continue;   // sin presupuesto asignado hoy
            metaPorRep.TryGetValue(r.cod, out var meta);
            logroPorRep.TryGetValue(r.cod, out var logro);
            list.Add(new CobrosFila { rep = r.cod, nombre = r.nom, supcod = r.sc, supervisor = r.sv, meta = meta, logro = logro });
        }
        foreach (var x in list)
        {
            var (lp, pp, sem) = EvaluarSemaforo(cfg, x.meta, x.logro, now);
            x.logroPct = lp; x.proyPct = pp; x.semaforo = sem;
        }
        bool esFinal = now >= now.Date.AddHours(15).AddMinutes(30);
        return list.OrderBy(x => esFinal ? x.logroPct : (x.proyPct ?? 0)).ToList();
    }

    // ── GET /apisuperv/cobros/config ────────────────────────────────────────
    [HttpGet("cobros/config")]
    public IActionResult CobrosConfig()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var list = new List<object>();
            try
            {
                using var cmd = new SqlCommand("SELECT CfgID,Nombre,Tipo,PreTipo,MetaFija,ProID,Division,ExcluirSupCods,VigenciaDesde,VigenciaHasta,Activo,Orden,UmbralVerde,UmbralAmarillo,Medicion,LinID,Cat3ID FROM dbo.SupervisionCobrosConfig_RM WITH (NOLOCK) ORDER BY Orden, Nombre", cn);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    list.Add(new
                    {
                        cfgId = rd.GetInt32(0), nombre = rd.GetString(1), tipo = rd.GetString(2),
                        preTipo = rd.IsDBNull(3) ? null : rd.GetString(3),
                        metaFija = rd.IsDBNull(4) ? (double?)null : Convert.ToDouble(rd.GetValue(4)),
                        proId = rd.IsDBNull(5) ? (int?)null : rd.GetInt32(5),
                        division = rd.IsDBNull(6) ? null : rd.GetString(6),
                        excluirSupCods = rd.IsDBNull(7) ? null : rd.GetString(7),
                        vigenciaDesde = rd.IsDBNull(8) ? null : rd.GetDateTime(8).ToString("yyyy-MM-dd"),
                        vigenciaHasta = rd.IsDBNull(9) ? null : rd.GetDateTime(9).ToString("yyyy-MM-dd"),
                        activo = rd.GetBoolean(10), orden = rd.GetInt32(11),
                        umbralVerde = Convert.ToDouble(rd.GetValue(12)), umbralAmarillo = Convert.ToDouble(rd.GetValue(13)),
                        medicion = rd.IsDBNull(14) ? null : rd.GetString(14),
                        linId = rd.IsDBNull(15) ? (int?)null : rd.GetInt32(15),
                        cat3Id = rd.IsDBNull(16) ? (int?)null : rd.GetInt32(16)
                    });
            }
            catch (Exception ex)
            {
                return Ok(new { ok = true, items = new List<object>(), aviso = "Falta crear SupervisionCobrosConfig_RM (SQL/Supervision_Cobros_RM.sql): " + ex.Message });
            }
            return Ok(new { ok = true, items = list });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record CobrosCfgReq(int cfgId, string nombre, string tipo, string? preTipo, double? metaFija, int? proId,
        string? division, string? excluirSupCods, string? vigenciaDesde, string? vigenciaHasta, bool activo, int orden,
        double umbralVerde, double umbralAmarillo, string? medicion, int? linId, int? cat3Id);

    // ── POST /apisuperv/cobros/config (solo Gerencia) ───────────────────────
    [HttpPost("cobros/config")]
    public IActionResult GuardarCobrosConfig([FromBody] CobrosCfgReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede configurar Cobros del día." });
            using var cmd = new SqlCommand(req.cfgId > 0 ? @"
UPDATE dbo.SupervisionCobrosConfig_RM SET Nombre=@n,Tipo=@t,PreTipo=@p,MetaFija=@mf,ProID=@pid,Division=@dv,ExcluirSupCods=@ex,VigenciaDesde=@vd,VigenciaHasta=@vh,Activo=@a,Orden=@o,UmbralVerde=@uv,UmbralAmarillo=@ua,Medicion=@md,LinID=@lid,Cat3ID=@c3,UsuInicioSesion=@u,FechaActualizacion=GETDATE()
WHERE CfgID=@id" : @"
INSERT INTO dbo.SupervisionCobrosConfig_RM (Nombre,Tipo,PreTipo,MetaFija,ProID,Division,ExcluirSupCods,VigenciaDesde,VigenciaHasta,Activo,Orden,UmbralVerde,UmbralAmarillo,Medicion,LinID,Cat3ID,UsuInicioSesion)
VALUES (@n,@t,@p,@mf,@pid,@dv,@ex,@vd,@vh,@a,@o,@uv,@ua,@md,@lid,@c3,@u)", cn);
            cmd.Parameters.AddWithValue("@n", req.nombre);
            cmd.Parameters.AddWithValue("@t", req.tipo);
            cmd.Parameters.AddWithValue("@p", (object?)req.preTipo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mf", (object?)req.metaFija ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pid", (object?)req.proId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dv", (object?)req.division ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ex", (object?)req.excluirSupCods ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vd", string.IsNullOrWhiteSpace(req.vigenciaDesde) ? (object)DBNull.Value : DateTime.Parse(req.vigenciaDesde));
            cmd.Parameters.AddWithValue("@vh", string.IsNullOrWhiteSpace(req.vigenciaHasta) ? (object)DBNull.Value : DateTime.Parse(req.vigenciaHasta));
            cmd.Parameters.AddWithValue("@a", req.activo);
            cmd.Parameters.AddWithValue("@o", req.orden);
            cmd.Parameters.AddWithValue("@uv", req.umbralVerde);
            cmd.Parameters.AddWithValue("@ua", req.umbralAmarillo);
            cmd.Parameters.AddWithValue("@md", (object?)req.medicion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lid", (object?)req.linId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c3", (object?)req.cat3Id ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@u", HttpContext.Session.GetString("user_login") ?? "");
            if (req.cfgId > 0) cmd.Parameters.AddWithValue("@id", req.cfgId);
            cmd.ExecuteNonQuery();
            return Ok(new { ok = true });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "Falta crear SupervisionCobrosConfig_RM (SQL/Supervision_Cobros_RM.sql): " + ex.Message }); }
    }

    // ── Asignacion cotejar/descotejar (SupervisionCobrosAsignacion_RM) ───────
    // Devuelve el set de RepCodigo con Asignado=1 SOLO si la alerta ya tiene
    // mantenimiento guardado; null si nunca lo tuvo (=> respeta Division/Excluir).
    private HashSet<string>? LeerAsignacionCobros(SqlConnection cn, int cfgId)
    {
        try
        {
            HashSet<string>? set = null;
            using var cmd = new SqlCommand("SELECT RepCodigo, Asignado FROM dbo.SupervisionCobrosAsignacion_RM WITH (NOLOCK) WHERE CfgID=@c", cn);
            cmd.Parameters.AddWithValue("@c", cfgId);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                set ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // hubo mantenimiento => autoridad
                if (rd.GetBoolean(1)) set.Add(rd.GetString(0));
            }
            return set;
        }
        catch { return null; }   // tabla ausente => back-compat
    }

    // Universo de vendedores de Cobros del dia = TODOS los activos, no supervisores (cualquier
    // division). Antes estaba limitado a IN ('10','11'), lo que dejaba fuera de la pantalla de
    // Asignacion (cotejar/descotejar) a grupos como los supervisados por Cristiano Perez
    // (S12/S13), cuyos reps caen en otras divisiones (20, 15, o sin division) — ago-2026.
    private List<(string cod, string nom, string sc, string sv, string div)> UniversoCobros(SqlConnection cn)
    {
        var reps = new List<(string, string, string, string, string)>();
        using var cmd = new SqlCommand(@"
SELECT r.RepCodigo, LTRIM(RTRIM(r.RepNombre)) RepNombre, ISNULL(r.RepSupervisor,'') SupCod,
       LTRIM(RTRIM(ISNULL(sup.RepNombre,r.RepSupervisor))) Supervisor, ISNULL(ra.RepTipe,'') Div
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor'
ORDER BY Supervisor, RepNombre", cn) { CommandTimeout = 60 };
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            reps.Add((rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2),
                     rd.IsDBNull(3) ? "" : rd.GetString(3), rd.IsDBNull(4) ? "" : rd.GetString(4)));
        return reps;
    }

    // ── GET /apisuperv/cobros/asignacion?cfgId= ─────────────────────────────
    // Lista TODO el universo (div 10/11) con su check actual para esa alerta.
    [HttpGet("cobros/asignacion")]
    public IActionResult CobrosAsignacion([FromQuery] int cfgId)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede asignar alertas." });
            var cfg = LeerCobrosCfgs(cn, soloActivos: false).FirstOrDefault(c => c.cfgId == cfgId);
            if (cfg == null) return Ok(new { ok = false, error = "Alerta no encontrada." });

            var overrides = LeerAsignacionCobros(cn, cfgId);
            HashSet<string>? excluir = string.IsNullOrWhiteSpace(cfg.excluirSupCods) ? null
                : cfg.excluirSupCods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var items = new List<object>();
            foreach (var r in UniversoCobros(cn))
            {
                bool asignado;
                if (overrides != null) asignado = overrides.Contains(r.cod);                 // mantenimiento manda
                else asignado = (string.IsNullOrWhiteSpace(cfg.division) || r.div == cfg.division)   // default = filtro grueso
                                && !(excluir != null && excluir.Contains(r.sc));
                items.Add(new { rep = r.cod, nombre = r.nom, supcod = r.sc, supervisor = r.sv, division = r.div, asignado });
            }
            return Ok(new { ok = true, cfgId, nombre = cfg.nombre, tieneMantenimiento = overrides != null, items });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "Falta crear SupervisionCobrosAsignacion_RM (SQL/SupervisionCobrosAsignacion_RM.sql): " + ex.Message }); }
    }

    public sealed record CobrosAsigItem(string rep, bool asignado);
    public sealed record CobrosAsigReq(int cfgId, CobrosAsigItem[] reps);

    // ── POST /apisuperv/cobros/asignacion (solo Gerencia) ───────────────────
    // Reescribe el set completo para la alerta (DELETE + INSERT en transacción).
    [HttpPost("cobros/asignacion")]
    public IActionResult GuardarCobrosAsignacion([FromBody] CobrosAsigReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (req == null || req.cfgId <= 0 || req.reps == null) return BadRequest(new { ok = false, error = "Datos incompletos." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            if (!id.VeTodo) return StatusCode(403, new { ok = false, error = "Solo Gerencia puede asignar alertas." });
            var usu = HttpContext.Session.GetString("user_login") ?? "";
            using var tx = cn.BeginTransaction();
            using (var del = new SqlCommand("DELETE FROM dbo.SupervisionCobrosAsignacion_RM WHERE CfgID=@c", cn, tx))
            { del.Parameters.AddWithValue("@c", req.cfgId); del.ExecuteNonQuery(); }
            foreach (var it in req.reps)
            {
                if (string.IsNullOrWhiteSpace(it.rep)) continue;
                using var ins = new SqlCommand("INSERT INTO dbo.SupervisionCobrosAsignacion_RM (CfgID,RepCodigo,Asignado,UsuInicioSesion) VALUES (@c,@r,@a,@u)", cn, tx);
                ins.Parameters.AddWithValue("@c", req.cfgId);
                ins.Parameters.AddWithValue("@r", it.rep);
                ins.Parameters.AddWithValue("@a", it.asignado);
                ins.Parameters.AddWithValue("@u", usu);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            return Ok(new { ok = true, guardados = req.reps.Length });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "Falta crear SupervisionCobrosAsignacion_RM (SQL/SupervisionCobrosAsignacion_RM.sql): " + ex.Message }); }
    }

    // ── GET /apisuperv/cobros?cfgId= ────────────────────────────────────────
    // Estado EN VIVO de hoy (recalcula en cada llamada): meta/logro/% y semáforo por vendedor.
    [HttpGet("cobros")]
    public IActionResult Cobros([FromQuery] int cfgId = 0)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var cfgs = LeerCobrosCfgs(cn, soloActivos: true);
            var cfg = (cfgId > 0 ? cfgs.FirstOrDefault(c => c.cfgId == cfgId) : null) ?? cfgs.FirstOrDefault();
            if (cfg == null) return Ok(new { ok = true, corte = DateTime.Now.ToString("HH:mm"), nombre = "", filas = new List<object>(), aviso = "No hay alertas configuradas." });
            var now = DateTime.Now;
            bool vigente = cfg.Vigente(now);
            var filas = vigente ? CalcularCobros(cn, id, cfg) : new List<CobrosFila>();
            bool esFinal = now >= now.Date.AddHours(15).AddMinutes(30);
            return Ok(new
            {
                ok = true, cfgId = cfg.cfgId, corte = now.ToString("HH:mm"), esFinal, nombre = cfg.nombre, tipo = cfg.tipo, vigente,
                aviso = vigente ? null : $"Esta alerta solo aplica del {cfg.vigenciaDesde:yyyy-MM-dd} al {cfg.vigenciaHasta:yyyy-MM-dd}.",
                filas = filas.Select(x => new
                {
                    x.rep, x.nombre, x.supcod, x.supervisor,
                    meta = Math.Round(x.meta, 2), logro = Math.Round(x.logro, 2),
                    logroPct = Math.Round(x.logroPct, 1),
                    proyPct = x.proyPct.HasValue ? Math.Round(x.proyPct.Value, 1) : (double?)null,
                    x.semaforo
                })
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/cobros/matriz ────────────────────────────────────────
    // Drill Supervisor→Representante con TODAS las alertas visibles a la vez:
    // por cada vendedor, la lista de alertas que le aplican con su color y alcance%.
    [HttpGet("cobros/matriz")]
    public IActionResult CobrosMatriz()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var now = DateTime.Now;
            var cfgs = LeerCobrosCfgs(cn, soloActivos: true).Where(c => c.Vigente(now)).ToList();
            bool esFinal = now >= now.Date.AddHours(15).AddMinutes(30);

            // rep -> (info + celdas por cfgId)
            var repInfo = new Dictionary<string, (string nom, string sc, string sv)>(StringComparer.OrdinalIgnoreCase);
            var celdas = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var cfg in cfgs)
            {
                foreach (var f in CalcularCobros(cn, id, cfg))
                {
                    if (!repInfo.ContainsKey(f.rep)) { repInfo[f.rep] = (f.nombre, f.supcod, f.supervisor); celdas[f.rep] = new List<object>(); }
                    celdas[f.rep].Add(new
                    {
                        cfgId = cfg.cfgId, nombre = cfg.nombre,
                        pct = Math.Round(f.proyPct ?? f.logroPct, 1),
                        logroPct = Math.Round(f.logroPct, 1),
                        semaforo = f.semaforo,
                        meta = Math.Round(f.meta, 2), logro = Math.Round(f.logro, 2)
                    });
                }
            }

            var filas = repInfo.Keys.Select(rep => new
            {
                rep, nombre = repInfo[rep].nom, supcod = repInfo[rep].sc, supervisor = repInfo[rep].sv,
                alertas = celdas[rep]
            })
            .OrderBy(x => x.supervisor).ThenBy(x => x.nombre).ToList();

            return Ok(new
            {
                ok = true, corte = now.ToString("HH:mm"), esFinal,
                alertas = cfgs.Select(c => new { cfgId = c.cfgId, nombre = c.nombre }).ToList(),
                filas
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    private sealed class CobrosAlertaDto { public int cfgId; public string nombre = ""; public double meta, logro, pct; public string semaforo = ""; }

    // Evalúa TODAS las alarmas configuradas (vigentes) para UN recurso (vendedor o
    // el equipo completo de un supervisor) y devuelve solo las que están en alarma
    // (semáforo distinto de verde) — igual que Virtual solo muestra monitores en alarma.
    private List<CobrosAlertaDto> EvaluarAlarmasRecurso(SqlConnection cn, Ident id, string? rep, string? sup)
    {
        var res = new List<CobrosAlertaDto>();
        var now = DateTime.Now;
        var cfgs = LeerCobrosCfgs(cn, soloActivos: true).Where(c => c.Vigente(now)).ToList();
        foreach (var cfg in cfgs)
        {
            var filas = CalcularCobros(cn, id, cfg);
            if (!string.IsNullOrWhiteSpace(rep))
            {
                var f = filas.FirstOrDefault(x => string.Equals(x.rep, rep, StringComparison.OrdinalIgnoreCase));
                if (f == null || f.semaforo == "verde") continue;
                res.Add(new CobrosAlertaDto { cfgId = cfg.cfgId, nombre = cfg.nombre, meta = f.meta, logro = f.logro, pct = f.proyPct ?? f.logroPct, semaforo = f.semaforo });
            }
            else if (!string.IsNullOrWhiteSpace(sup))
            {
                var equipo = filas.Where(x => string.Equals(x.supcod, sup, StringComparison.OrdinalIgnoreCase)).ToList();
                if (equipo.Count == 0) continue;
                double meta = equipo.Sum(x => x.meta), logro = equipo.Sum(x => x.logro);
                var (lp, pp, sem) = EvaluarSemaforo(cfg, meta, logro, now);
                if (sem == "verde") continue;
                res.Add(new CobrosAlertaDto { cfgId = cfg.cfgId, nombre = cfg.nombre, meta = meta, logro = logro, pct = pp ?? lp, semaforo = sem });
            }
        }
        return res;
    }

    private (bool enMute, DateTime? ultimo, int muteMin) EstadoMuteCobros(SqlConnection cn, string tipoRecurso, string codigo)
    {
        int muteMin = TorreCore.LeerMuteMin(cn);
        try
        {
            var col = tipoRecurso == "supervisor" ? "SupCod" : "RepCodigo";
            using var cmd = new SqlCommand($@"SELECT MAX(FechaContacto) FROM dbo.SupervisionCobrosContactos_RM WITH (NOLOCK)
WHERE TipoRecurso=@tr AND {col}=@cod AND FechaContacto>=CAST(CAST(GETDATE() AS date) AS datetime)", cn);
            cmd.Parameters.AddWithValue("@tr", tipoRecurso);
            cmd.Parameters.AddWithValue("@cod", codigo);
            var v = cmd.ExecuteScalar();
            if (v == null || v is DBNull) return (false, null, muteMin);
            var lc = (DateTime)v;
            return ((DateTime.Now - lc).TotalMinutes < muteMin, lc, muteMin);
        }
        catch { return (false, null, muteMin); }
    }

    // ── GET /apisuperv/cobros/alertas?rep=|sup= ─────────────────────────────
    // Para "Cargar vendedor"/"Cargar supervisor" en el flujo de contacto.
    [HttpGet("cobros/alertas")]
    public IActionResult CobrosAlertas([FromQuery] string? rep, [FromQuery] string? sup)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (string.IsNullOrWhiteSpace(rep) && string.IsNullOrWhiteSpace(sup)) return BadRequest(new { ok = false, error = "Falta rep o sup." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var tipoRecurso = !string.IsNullOrWhiteSpace(rep) ? "vendedor" : "supervisor";
            var codigo = (tipoRecurso == "vendedor" ? rep! : sup!).Trim();
            var (enMute, ultimo, muteMin) = EstadoMuteCobros(cn, tipoRecurso, codigo);
            if (enMute)
            {
                var pausadoHasta = ultimo!.Value.AddMinutes(muteMin).ToString("HH:mm");
                return Ok(new { ok = true, enMute = true, pausadoHasta, alertas = new List<object>() });
            }
            var alertas = EvaluarAlarmasRecurso(cn, id, rep, sup);
            return Ok(new
            {
                ok = true, enMute = false,
                alertas = alertas.Select(a => new { a.cfgId, a.nombre, meta = Math.Round(a.meta, 2), logro = Math.Round(a.logro, 2), pct = Math.Round(a.pct, 1), a.semaforo })
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record CobrosContactoReq(string tipoRecurso, string? repCodigo, string? repNombre, string? supCod, string? supervisor,
        string? canal, string? contactoCon, string? comentario, string? compromiso, List<int>? cfgIds, List<string>? nombresAlertas);

    // ── POST /apisuperv/cobros/contacto ─────────────────────────────────────
    [HttpPost("cobros/contacto")]
    public IActionResult GuardarCobrosContacto([FromBody] CobrosContactoReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (req is null || string.IsNullOrWhiteSpace(req.tipoRecurso) || string.IsNullOrWhiteSpace(req.supCod))
            return BadRequest(new { ok = false, error = "Datos incompletos." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var user = HttpContext.Session.GetString("user_login") ?? "";
            var alertasTxt = req.nombresAlertas is { Count: > 0 } ? string.Join(" · ", req.nombresAlertas) : null;
            var cfgIdsTxt = req.cfgIds is { Count: > 0 } ? string.Join(",", req.cfgIds) : null;
            using var cmd = new SqlCommand(@"
INSERT INTO dbo.SupervisionCobrosContactos_RM
 (UsuarioTorre,TipoRecurso,RepCodigo,RepNombre,SupCod,Supervisor,Alertas,CfgIds,Canal,ContactoCon,Comentario,Compromiso)
 OUTPUT INSERTED.ConID, INSERTED.FechaContacto
 VALUES (@u,@tr,@rep,@rn,@sc,@sv,@ale,@cfg,@can,@con,@com,@cmp)", cn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@u", user);
            cmd.Parameters.AddWithValue("@tr", req.tipoRecurso);
            cmd.Parameters.AddWithValue("@rep", (object?)req.repCodigo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rn", (object?)req.repNombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sc", req.supCod);
            cmd.Parameters.AddWithValue("@sv", (object?)req.supervisor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ale", (object?)alertasTxt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cfg", (object?)cfgIdsTxt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@can", (object?)req.canal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@con", (object?)req.contactoCon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@com", (object?)req.comentario ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cmp", (object?)req.compromiso ?? DBNull.Value);
            int conID = 0; string hora = DateTime.Now.ToString("HH:mm");
            using (var rd = cmd.ExecuteReader()) { if (rd.Read()) { conID = rd.GetInt32(0); hora = rd.GetDateTime(1).ToString("HH:mm"); } }
            int muteMin = TorreCore.LeerMuteMin(cn);
            return Ok(new { ok = true, conID, hora, muteMin });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = "Falta crear SupervisionCobrosContactos_RM (SQL/Supervision_Cobros_RM.sql): " + ex.Message }); }
    }

    // ── GET /apisuperv/cobros/contactos?desde=&hasta= ───────────────────────
    // Detalle de contactos del rango (reporte: por supervisor/vendedor/tipo de alerta).
    [HttpGet("cobros/contactos")]
    public IActionResult CobrosContactos([FromQuery] string? desde, [FromQuery] string? hasta)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var d0 = string.IsNullOrWhiteSpace(desde) ? DateTime.Now.Date : DateTime.Parse(desde);
            var d1 = (string.IsNullOrWhiteSpace(hasta) ? DateTime.Now.Date : DateTime.Parse(hasta)).AddDays(1);
            var parms = new Dictionary<string, object>();
            string scopeFiltro = "";
            if (!id.VeTodo)
            {
                if (id.SupCods.Count == 0) return Ok(new { ok = true, items = new List<object>() });
                var names = id.SupCods.Select((s, i) => { var n = $"@sc{i}"; parms[n] = s; return n; });
                scopeFiltro = " AND SupCod IN (" + string.Join(",", names) + ")";
            }
            var list = new List<object>();
            try
            {
                using var cmd = new SqlCommand($@"
SELECT FechaContacto,TipoRecurso,RepCodigo,RepNombre,SupCod,Supervisor,Alertas,CfgIds,Canal,ContactoCon,Comentario,Compromiso
FROM dbo.SupervisionCobrosContactos_RM WITH (NOLOCK)
WHERE FechaContacto>=@d0 AND FechaContacto<@d1 {scopeFiltro}
ORDER BY FechaContacto DESC", cn) { CommandTimeout = 60 };
                cmd.Parameters.AddWithValue("@d0", d0);
                cmd.Parameters.AddWithValue("@d1", d1);
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    list.Add(new
                    {
                        fecha = rd.GetDateTime(0).ToString("yyyy-MM-dd HH:mm"),
                        tipoRecurso = rd.GetString(1),
                        rep = rd.IsDBNull(2) ? null : rd.GetString(2),
                        repNombre = rd.IsDBNull(3) ? null : rd.GetString(3),
                        supCod = rd.IsDBNull(4) ? null : rd.GetString(4),
                        supervisor = rd.IsDBNull(5) ? null : rd.GetString(5),
                        alertas = rd.IsDBNull(6) ? null : rd.GetString(6),
                        cfgIds = rd.IsDBNull(7) ? null : rd.GetString(7),
                        canal = rd.IsDBNull(8) ? null : rd.GetString(8),
                        contactoCon = rd.IsDBNull(9) ? null : rd.GetString(9),
                        comentario = rd.IsDBNull(10) ? null : rd.GetString(10),
                        compromiso = rd.IsDBNull(11) ? null : rd.GetString(11)
                    });
            }
            catch (Exception ex) { return Ok(new { ok = true, items = new List<object>(), aviso = "Falta crear SupervisionCobrosContactos_RM: " + ex.Message }); }
            return Ok(new { ok = true, desde = d0.ToString("yyyy-MM-dd"), hasta = d1.AddDays(-1).ToString("yyyy-MM-dd"), items = list });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/cobros/resumen ───────────────────────────────────────
    // Tarjetas: mejor/peor alerta (promedio % hoy) + supervisores con más/menos contactos hoy.
    [HttpGet("cobros/resumen")]
    public IActionResult CobrosResumen()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var now = DateTime.Now;
            var cfgs = LeerCobrosCfgs(cn, soloActivos: true).Where(c => c.Vigente(now)).ToList();
            var porAlerta = new List<object>();
            CobrosCfgRow? mejor = null, peor = null; double mejorAvg = double.MinValue, peorAvg = double.MaxValue;
            foreach (var cfg in cfgs)
            {
                var filas = CalcularCobros(cn, id, cfg);
                if (filas.Count == 0) continue;
                double avg = filas.Average(x => x.proyPct ?? x.logroPct);
                porAlerta.Add(new
                {
                    cfg.cfgId, cfg.nombre, promedioPct = Math.Round(avg, 1), vendedores = filas.Count,
                    rojos = filas.Count(x => x.semaforo == "rojo"), amarillos = filas.Count(x => x.semaforo == "amarillo"), verdes = filas.Count(x => x.semaforo == "verde")
                });
                if (avg > mejorAvg) { mejorAvg = avg; mejor = cfg; }
                if (avg < peorAvg) { peorAvg = avg; peor = cfg; }
            }

            // Supervisores del alcance (para incluir también los de 0 contactos) + conteo de hoy.
            var parms = new Dictionary<string, object>();
            var supervisoresScope = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (RepFilter(id, parms, out var f))
            {
                using var cmd = new SqlCommand($@"
SELECT DISTINCT r.RepSupervisor, LTRIM(RTRIM(ISNULL(sup.RepNombre,r.RepSupervisor)))
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor' AND ISNULL(r.RepSupervisor,'')<>'' {f}", cn);
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) if (!rd.IsDBNull(0)) supervisoresScope[rd.GetString(0)] = rd.IsDBNull(1) ? rd.GetString(0) : rd.GetString(1);
            }
            var contactosHoy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var cmd = new SqlCommand("SELECT SupCod, COUNT(*) FROM dbo.SupervisionCobrosContactos_RM WITH (NOLOCK) WHERE FechaContacto>=CAST(CAST(GETDATE() AS date) AS datetime) AND SupCod IS NOT NULL GROUP BY SupCod", cn);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) contactosHoy[rd.GetString(0)] = rd.GetInt32(1);
            }
            catch { }
            var ranking = supervisoresScope.Select(kv => new { supcod = kv.Key, nombre = kv.Value, contactos = contactosHoy.TryGetValue(kv.Key, out var n) ? n : 0 }).ToList();

            return Ok(new
            {
                ok = true, porAlerta,
                mejorAlerta = mejor == null ? null : new { mejor.cfgId, mejor.nombre, promedioPct = Math.Round(mejorAvg, 1) },
                peorAlerta = peor == null ? null : new { peor.cfgId, peor.nombre, promedioPct = Math.Round(peorAvg, 1) },
                masContactos = ranking.OrderByDescending(x => x.contactos).Take(3),
                menosContactos = ranking.OrderBy(x => x.contactos).Take(3)
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    public sealed record CobrosBitReq(int cfgId, string corte);

    // ── POST /apisuperv/cobros/bitacora ─────────────────────────────────────
    // Guarda el corte (9:50 / 11:50 / 15:30): recalcula en el servidor (no confía en el cliente).
    [HttpPost("cobros/bitacora")]
    public IActionResult GuardarCobrosBitacora([FromBody] CobrosBitReq req)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (req is null || string.IsNullOrWhiteSpace(req.corte)) return BadRequest(new { ok = false, error = "Falta el corte." });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var cfgs = LeerCobrosCfgs(cn, soloActivos: true);
            var cfg = (req.cfgId > 0 ? cfgs.FirstOrDefault(c => c.cfgId == req.cfgId) : null) ?? cfgs.FirstOrDefault();
            if (cfg == null) return Ok(new { ok = true, guardados = 0, aviso = "No hay alertas configuradas." });
            var filas = CalcularCobros(cn, id, cfg);
            var user = HttpContext.Session.GetString("user_login") ?? "";
            var fecha = DateTime.Now.Date;
            try
            {
                foreach (var x in filas)
                {
                    using var cmd = new SqlCommand(@"
UPDATE dbo.SupervisionCobrosBitacora_RM SET RepNombre=@rn,SupCod=@sc,Supervisor=@sv,Meta=@me,Logro=@lo,LogroPct=@lp,ProyeccionPct=@pp,Semaforo=@sem,UsuInicioSesion=@u,FechaRegistro=GETDATE()
WHERE Fecha=@f AND Corte=@c AND CfgID=@cf AND RepCodigo=@r;
IF @@ROWCOUNT=0 INSERT INTO dbo.SupervisionCobrosBitacora_RM (Fecha,Corte,CfgID,RepCodigo,RepNombre,SupCod,Supervisor,Meta,Logro,LogroPct,ProyeccionPct,Semaforo,UsuInicioSesion)
VALUES (@f,@c,@cf,@r,@rn,@sc,@sv,@me,@lo,@lp,@pp,@sem,@u);", cn);
                    cmd.Parameters.AddWithValue("@f", fecha);
                    cmd.Parameters.AddWithValue("@c", req.corte);
                    cmd.Parameters.AddWithValue("@cf", cfg.cfgId);
                    cmd.Parameters.AddWithValue("@r", x.rep);
                    cmd.Parameters.AddWithValue("@rn", x.nombre);
                    cmd.Parameters.AddWithValue("@sc", x.supcod);
                    cmd.Parameters.AddWithValue("@sv", x.supervisor);
                    cmd.Parameters.AddWithValue("@me", x.meta);
                    cmd.Parameters.AddWithValue("@lo", x.logro);
                    cmd.Parameters.AddWithValue("@lp", x.logroPct);
                    cmd.Parameters.AddWithValue("@pp", (object?)x.proyPct ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@sem", x.semaforo);
                    cmd.Parameters.AddWithValue("@u", user);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                return Ok(new { ok = true, guardados = 0, aviso = "Falta crear SupervisionCobrosBitacora_RM (SQL/Supervision_Cobros_RM.sql): " + ex.Message });
            }
            return Ok(new { ok = true, guardados = filas.Count, corte = req.corte });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/cobros/bitacora?fecha=&cfgId= ────────────────────────
    [HttpGet("cobros/bitacora")]
    public IActionResult LeerCobrosBitacora([FromQuery] string? fecha, [FromQuery] int cfgId = 0)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var f = string.IsNullOrWhiteSpace(fecha) ? DateTime.Now.Date : DateTime.Parse(fecha);
            var list = new List<object>();
            try
            {
                using var cmd = new SqlCommand("SELECT Corte,RepCodigo,RepNombre,SupCod,Supervisor,Meta,Logro,LogroPct,ProyeccionPct,Semaforo FROM dbo.SupervisionCobrosBitacora_RM WITH (NOLOCK) WHERE Fecha=@f AND CfgID=@c ORDER BY Corte, LogroPct", cn);
                cmd.Parameters.AddWithValue("@f", f);
                cmd.Parameters.AddWithValue("@c", cfgId);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    list.Add(new
                    {
                        corte = rd.GetString(0), rep = rd.GetString(1), nombre = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        supcod = rd.IsDBNull(3) ? "" : rd.GetString(3), supervisor = rd.IsDBNull(4) ? "" : rd.GetString(4),
                        meta = Convert.ToDouble(rd.GetValue(5)), logro = Convert.ToDouble(rd.GetValue(6)),
                        logroPct = Convert.ToDouble(rd.GetValue(7)),
                        proyPct = rd.IsDBNull(8) ? (double?)null : Convert.ToDouble(rd.GetValue(8)),
                        semaforo = rd.GetString(9)
                    });
            }
            catch (Exception ex) { return Ok(new { ok = true, items = new List<object>(), aviso = ex.Message }); }
            return Ok(new { ok = true, fecha = f.ToString("yyyy-MM-dd"), items = list });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apisuperv/cobros/reporte ───────────────────────────────────────
    // Consulta de ALERTAS GENERADAS (bitácora) en un rango, con filtros
    // Supervisor/Vendedor/Alerta/Semáforo. Una "alerta generada" = un
    // (día × alerta × vendedor) que terminó en Amarillo o Rojo (peor corte del día).
    // Devuelve agregados por vendedor y por alerta + detalle, para el reporte imprimible.
    [HttpGet("cobros/reporte")]
    public IActionResult CobrosReporte([FromQuery] string? desde, [FromQuery] string? hasta,
        [FromQuery] string? sup, [FromQuery] string? rep, [FromQuery] int cfgId = 0, [FromQuery] string? sem = null)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn(); cn.Open();
            var id = Identidad(cn);
            var d0 = string.IsNullOrWhiteSpace(desde) ? DateTime.Now.Date : DateTime.Parse(desde);
            var d1 = string.IsNullOrWhiteSpace(hasta) ? DateTime.Now.Date : DateTime.Parse(hasta);
            var nombres = LeerCobrosCfgs(cn, soloActivos: false).ToDictionary(c => c.cfgId, c => c.nombre);

            var parms = new Dictionary<string, object> { ["@d0"] = d0, ["@d1"] = d1 };
            var where = " WHERE Fecha>=@d0 AND Fecha<=@d1";
            if (!id.VeTodo)
            {
                if (id.SupCods.Count == 0) return Ok(new { ok = true, generadas = 0, porRep = new List<object>(), porAlerta = new List<object>(), detalle = new List<object>() });
                var ns = id.SupCods.Select((s, i) => { var n = $"@sc{i}"; parms[n] = s; return n; });
                where += " AND SupCod IN (" + string.Join(",", ns) + ")";
            }
            if (!string.IsNullOrWhiteSpace(sup)) { where += " AND SupCod=@sup"; parms["@sup"] = sup.Trim(); }
            if (!string.IsNullOrWhiteSpace(rep)) { where += " AND RepCodigo=@rep"; parms["@rep"] = rep.Trim(); }
            if (cfgId > 0) { where += " AND CfgID=@cfg"; parms["@cfg"] = cfgId; }

            // Peor semáforo del día por (Fecha,CfgID,RepCodigo).
            var dias = new Dictionary<string, (DateTime fecha, int cfg, string rep, string nom, string sc, string sv, int sev)>();
            int Sev(string s) => s == "rojo" ? 3 : (s == "amarillo" ? 2 : 1);
            try
            {
                using var cmd = new SqlCommand("SELECT Fecha,CfgID,RepCodigo,RepNombre,SupCod,Supervisor,Semaforo FROM dbo.SupervisionCobrosBitacora_RM WITH (NOLOCK)" + where, cn) { CommandTimeout = 60 };
                foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var fe = rd.GetDateTime(0); var cf = rd.GetInt32(1); var rp = rd.GetString(2);
                    var key = fe.ToString("yyyyMMdd") + "|" + cf + "|" + rp;
                    var sv = Sev(rd.GetString(6));
                    if (!dias.TryGetValue(key, out var cur) || sv > cur.sev)
                        dias[key] = (fe, cf, rp, rd.IsDBNull(3) ? "" : rd.GetString(3), rd.IsDBNull(4) ? "" : rd.GetString(4), rd.IsDBNull(5) ? "" : rd.GetString(5), sv);
                }
            }
            catch (Exception ex) { return Ok(new { ok = true, generadas = 0, porRep = new List<object>(), porAlerta = new List<object>(), detalle = new List<object>(), aviso = "Falta bitácora / SQL: " + ex.Message }); }

            // Filtrar a "generadas" (>=amarillo) y por semáforo pedido.
            var gen = dias.Values.Where(x => x.sev >= 2)
                .Where(x => string.IsNullOrWhiteSpace(sem) || (sem == "rojo" ? x.sev == 3 : (sem == "amarillo" ? x.sev == 2 : true)))
                .ToList();

            string SemTxt(int s) => s == 3 ? "rojo" : (s == 2 ? "amarillo" : "verde");
            var porRep = gen.GroupBy(x => x.rep).Select(g => new
            {
                rep = g.Key, nombre = g.First().nom, supervisor = g.First().sv,
                generadas = g.Count(), rojas = g.Count(x => x.sev == 3), amarillas = g.Count(x => x.sev == 2)
            }).OrderByDescending(x => x.rojas).ThenByDescending(x => x.generadas).ToList();
            var porAlerta = gen.GroupBy(x => x.cfg).Select(g => new
            {
                cfgId = g.Key, nombre = nombres.TryGetValue(g.Key, out var nm) ? nm : ("Alerta " + g.Key),
                generadas = g.Count(), rojas = g.Count(x => x.sev == 3), amarillas = g.Count(x => x.sev == 2)
            }).OrderByDescending(x => x.generadas).ToList();
            var detalle = gen.OrderBy(x => x.fecha).ThenBy(x => x.sv).Select(x => new
            {
                fecha = x.fecha.ToString("yyyy-MM-dd"), rep = x.rep, nombre = x.nom, supervisor = x.sv,
                alerta = nombres.TryGetValue(x.cfg, out var nm) ? nm : ("Alerta " + x.cfg), semaforo = SemTxt(x.sev)
            }).ToList();

            return Ok(new
            {
                ok = true, desde = d0.ToString("yyyy-MM-dd"), hasta = d1.ToString("yyyy-MM-dd"),
                generadas = gen.Count, rojas = gen.Count(x => x.sev == 3), amarillas = gen.Count(x => x.sev == 2),
                porRep, porAlerta, detalle
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
