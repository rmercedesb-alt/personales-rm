using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DashVisitas.Services;

namespace DashVisitas.Controllers;

// ── API de Torre de Control (motor de monitores) ────────────────────────────
[ApiController]
[Route("apitorre")]
[RequiereModulo("torre")]
public class ApiTorreController(IConfiguration config) : ControllerBase
{
    private SqlConnection NewConn() => new(config.GetConnectionString("Default"));
    private bool IsAuth => HttpContext.Session.GetString("user_login") is not null;

    private static List<string> ParseList(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? new() :
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string BuildIn(string column, List<string> vals, string prefix, Dictionary<string, object> parms)
    {
        if (vals.Count == 0) return "";
        var names = vals.Select((v, i) => { var n = $"@{prefix}{i}"; parms[n] = v; return n; }).ToList();
        return $"AND {column} IN ({string.Join(",", names)})";
    }

    // ── GET /apitorre/monitor ───────────────────────────────────────────────
    // Devuelve por vendedor (activos con ruta/visita hoy) las celdas evaluadas de
    // cada monitor activo + el catálogo de monitores. Filtros opcionales.
    [HttpGet("monitor")]
    public IActionResult MonitorVivo(
        [FromQuery] string? centros, [FromQuery] string? supervisores,
        [FromQuery] string? reps, [FromQuery] string? divisiones)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            var parms = new Dictionary<string, object>();
            var f = BuildIn("r.CedCodigo", ParseList(centros), "c", parms)
                  + " " + BuildIn("r.RepSupervisor", ParseList(supervisores), "s", parms)
                  + " " + BuildIn("r.RepCodigo", ParseList(reps), "rp", parms)
                  + " " + BuildIn("ra.RepTipe", ParseList(divisiones), "dv", parms);

            using var cn = NewConn();
            cn.Open();
            var monitores = TorreCore.CargarMonitores(cn, soloActivos: true);
            var metricas = TorreCore.CargarMetricas(cn, f, parms);

            // Contacto de HOY por vendedor (el más reciente). Dentro del mute (config, default 60 min)
            // => todo callado. Pasado el mute => se REINICIAN los contadores desde (contacto+mute) y las
            // alertas que se repiten vuelven a evaluarse; la de inicio tardío (UnaVezAlDia) NO reaparece.
            var muteMin = TorreCore.LeerMuteMin(cn);
            var ultimoContacto = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            using (var cc = new SqlCommand(@"SELECT RepCodigo, MAX(FechaContacto) FROM dbo.TorreControlContactos_RM
                                             WHERE FechaContacto >= CAST(CAST(GETDATE() AS date) AS datetime) GROUP BY RepCodigo", cn))
            using (var rd = cc.ExecuteReader())
                while (rd.Read()) ultimoContacto[rd.GetString(0)] = rd.GetDateTime(1);

            // Alertas puntuales que el supervisor marcó "atendida" en algún contacto de hoy
            // (checklist del modal). Esa alerta específica queda apagada el resto del día,
            // sin afectar las demás alertas del mismo vendedor.
            var atendidasHoy = TorreCore.LeerAtendidasHoy(cn);

            var (vDesde, vHasta) = TorreCore.LeerVentanaHoraria(cn);
            // Las alertas solo se activan dentro de la ventana horaria (desde hora inicio, hasta hora fin).
            var ahoraTod = DateTime.Now.TimeOfDay;
            bool enVentana = ahoraTod >= vDesde && ahoraTod <= vHasta;
            var repsOff = TorreCore.LeerRepsOff(cn);
            var nucleo = monitores.Where(m => m.Tipo == "nucleo").ToList();
            // Monitores personalizados (métricas de venta por grupo): valores de HOY una vez por monitor.
            var custom = TorrePersonalizados.Parse(monitores);
            var customVals = custom.ToDictionary(s => s.MonID, s => { try { return TorrePersonalizados.ValoresHoy(cn, s); } catch { return new Dictionary<string, double[]>(); } });
            var vendedores = new List<object>();
            foreach (var m in metricas.Values)
            {
                TorreCore.CalcularCumplimiento(m, vDesde, vHasta, DateTime.Now);
                bool off = repsOff.Contains(m.RepCodigo);
                bool contactadoHoy = ultimoContacto.TryGetValue(m.RepCodigo, out var lc);
                bool enMute = false; string? contHora = null;
                if (contactadoHoy)
                {
                    contHora = lc.ToString("HH:mm");
                    if ((DateTime.Now - lc).TotalMinutes < muteMin) enMute = true;   // dentro del mute: todo callado
                    else { try { TorreCore.RecomputarDesde(cn, m, lc.AddMinutes(muteMin)); } catch { } }  // reactivado: reloj desde el contacto
                }
                // Base de silencio para TODAS: no medir, fuera de ventana, o dentro del mute.
                bool baseSil = off || !enVentana || enMute;
                atendidasHoy.TryGetValue(m.RepCodigo, out var atendidasRep);
                var celdas = new Dictionary<string, object>();
                int sev = 0, alarmas = 0;
                foreach (var mon in nucleo)
                {
                    var c = TorreCore.Evaluar(mon, m);
                    // Marcada "atendida" en algún contacto de hoy → apagada el resto del día (esa alerta puntual).
                    bool atendida = atendidasRep != null && atendidasRep.Contains(mon.Codigo);
                    // El inicio-tardío (UnaVezAlDia), una vez contactado, no reaparece el resto del día (regla genérica).
                    bool sil = baseSil || atendida || (contactadoHoy && TorreCore.UnaVezAlDia.Contains(mon.Codigo));
                    bool al = c.Alarma && !sil;
                    celdas[mon.Codigo] = new { c.Valor, c.Texto, Alarma = al, c.Detalle, c.Severidad, Atendida = atendida };
                    if (al) { alarmas++; sev += c.Severidad; }
                }
                foreach (var s in custom)
                {
                    var c = TorrePersonalizados.Celda(s, m, customVals[s.MonID]);
                    bool atendida = atendidasRep != null && atendidasRep.Contains(s.Codigo);
                    bool al = c.Alarma && !baseSil && !atendida;   // los personalizados se repiten (no son "una vez al día"), salvo si se marcó atendida
                    celdas[s.Codigo] = new { c.Valor, c.Texto, Alarma = al, c.Detalle, c.Severidad, Atendida = atendida };
                    if (al) { alarmas++; sev += c.Severidad; }
                }
                vendedores.Add(new
                {
                    m.RepCodigo, m.RepNombre, m.Centro, m.SupCod, m.Supervisor, m.Division,
                    m.Lat, m.Lng, celdas, alarmas, severidad = sev,
                    contactado = enMute, contactadoHora = contHora, reiniciado = m.Reseteado, sinAlerta = off,
                    terminado = m.Terminado
                });
            }

            var mons = monitores.Select(m => new { m.MonID, m.Codigo, m.Tipo, m.Nombre, m.Ambito, m.Umbral, m.Direccion, m.Severidad, m.Orden, m.Color }).ToList();
            return Ok(new { ok = true, ahora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), monitores = mons, vendedores });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apitorre/monitores ─────────────────────────────────────────────
    [HttpGet("monitores")]
    public IActionResult Monitores()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            var lst = TorreCore.CargarMonitores(cn, soloActivos: false);
            using var cmd = new SqlCommand("SELECT MonID, Activo, VigenciaDesde, VigenciaHasta, ParametrosJSON FROM dbo.TorreControlMonitores_RM", cn);
            var extra = new Dictionary<int, (bool act, string? vd, string? vh, string? pj)>();
            using (var rd = cmd.ExecuteReader())
                while (rd.Read())
                    extra[rd.GetInt32(0)] = (
                        !rd.IsDBNull(1) && rd.GetBoolean(1),
                        rd.IsDBNull(2) ? null : rd.GetDateTime(2).ToString("yyyy-MM-dd"),
                        rd.IsDBNull(3) ? null : rd.GetDateTime(3).ToString("yyyy-MM-dd"),
                        rd.IsDBNull(4) ? null : rd.GetString(4));
            var res = lst.Select(m => new
            {
                m.MonID, m.Codigo, m.Tipo, m.Nombre, m.Ambito, m.Umbral, m.Direccion, m.Severidad, m.Orden, m.Color,
                Activo = extra.TryGetValue(m.MonID, out var e) && e.act,
                VigenciaDesde = extra.TryGetValue(m.MonID, out var e2) ? e2.vd : null,
                VigenciaHasta = extra.TryGetValue(m.MonID, out var e3) ? e3.vh : null,
                Parametros = extra.TryGetValue(m.MonID, out var e4) ? e4.pj : null
            });
            return Ok(new { ok = true, monitores = res });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apitorre/monitores  (crear / actualizar / activar) ─────────────
    public record MonitorEdit(int? MonID, string? Codigo, string? Tipo, string? Nombre, string? Ambito,
                              double? Umbral, string? Direccion, string? ParametrosJSON, int? Severidad, int? Orden,
                              string? VigenciaDesde, string? VigenciaHasta, bool? Activo);

    [HttpPost("monitores")]
    public IActionResult GuardarMonitor([FromBody] MonitorEdit e)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (e is null) return BadRequest(new { ok = false, error = "Datos inválidos" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            string sql;
            if (e.MonID is > 0)
                sql = @"UPDATE dbo.TorreControlMonitores_RM SET
                          Nombre=ISNULL(@nom,Nombre), Umbral=COALESCE(@umb,Umbral), Direccion=COALESCE(@dir,Direccion),
                          ParametrosJSON=COALESCE(@par,ParametrosJSON),
                          Severidad=ISNULL(@sev,Severidad), Orden=ISNULL(@ord,Orden), Ambito=ISNULL(@amb,Ambito),
                          VigenciaDesde=COALESCE(@vd,VigenciaDesde), VigenciaHasta=COALESCE(@vh,VigenciaHasta),
                          Activo=ISNULL(@act,Activo), UsuInicioSesion=@usu, FechaActualizacion=GETDATE()
                        WHERE MonID=@id";
            else
                sql = @"INSERT INTO dbo.TorreControlMonitores_RM
                          (Codigo,Tipo,Nombre,Ambito,Umbral,Direccion,ParametrosJSON,Severidad,Orden,VigenciaDesde,VigenciaHasta,Activo,UsuInicioSesion)
                        VALUES (@cod,@tipo,@nom,ISNULL(@amb,'vendedor'),@umb,@dir,@par,ISNULL(@sev,1),ISNULL(@ord,0),@vd,@vh,ISNULL(@act,1),@usu)";
            using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@id", e.MonID ?? 0);
            cmd.Parameters.AddWithValue("@cod", (object?)e.Codigo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tipo", (object?)e.Tipo ?? "producto");
            cmd.Parameters.AddWithValue("@nom", (object?)e.Nombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@amb", (object?)e.Ambito ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@umb", (object?)e.Umbral ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dir", (object?)e.Direccion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@par", (object?)e.ParametrosJSON ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sev", (object?)e.Severidad ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ord", (object?)e.Orden ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vd", string.IsNullOrWhiteSpace(e.VigenciaDesde) ? DBNull.Value : e.VigenciaDesde);
            cmd.Parameters.AddWithValue("@vh", string.IsNullOrWhiteSpace(e.VigenciaHasta) ? DBNull.Value : e.VigenciaHasta);
            cmd.Parameters.AddWithValue("@act", (object?)e.Activo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@usu", HttpContext.Session.GetString("user_login") ?? "");
            var n = cmd.ExecuteNonQuery();
            return Ok(new { ok = true, filas = n });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apitorre/contactos ─────────────────────────────────────────────
    [HttpGet("contactos")]
    public IActionResult Contactos([FromQuery] string? rep, [FromQuery] string? desde, [FromQuery] string? hasta)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            var w = "1=1"; var parms = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(rep)) { w += " AND RepCodigo=@rep"; parms["@rep"] = rep; }
            if (!string.IsNullOrWhiteSpace(desde)) { w += " AND FechaContacto>=@d"; parms["@d"] = desde; }
            if (!string.IsNullOrWhiteSpace(hasta)) { w += " AND FechaContacto<DATEADD(DAY,1,CAST(@h AS datetime))"; parms["@h"] = hasta; }
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand($@"
SELECT TOP 500 ConID, FechaContacto, UsuarioTorre, RepCodigo, RepNombre, SupervisorNombre, Centro,
       Alertas, Canal, ContactoCon, Resumen, Compromiso, FechaSeguimiento, Estado
FROM dbo.TorreControlContactos_RM WHERE {w} ORDER BY FechaContacto DESC", cn) { CommandTimeout = 30 };
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            var lst = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lst.Add(new
                {
                    ConID = rd.GetInt32(0), Fecha = rd.GetDateTime(1).ToString("yyyy-MM-dd HH:mm"),
                    Usuario = rd.IsDBNull(2) ? "" : rd.GetString(2), RepCodigo = rd.GetString(3),
                    RepNombre = rd.IsDBNull(4) ? "" : rd.GetString(4), Supervisor = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Centro = rd.IsDBNull(6) ? "" : rd.GetString(6), Alertas = rd.IsDBNull(7) ? "" : rd.GetString(7),
                    Canal = rd.IsDBNull(8) ? "" : rd.GetString(8), ContactoCon = rd.IsDBNull(9) ? "" : rd.GetString(9),
                    Resumen = rd.IsDBNull(10) ? "" : rd.GetString(10), Compromiso = rd.IsDBNull(11) ? "" : rd.GetString(11),
                    FechaSeguimiento = rd.IsDBNull(12) ? "" : rd.GetDateTime(12).ToString("yyyy-MM-dd"),
                    Estado = rd.IsDBNull(13) ? "" : rd.GetString(13)
                });
            return Ok(new { ok = true, contactos = lst });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── POST /apitorre/contactos  (registrar un contacto) ────────────────────
    public record ContactoEdit(string RepCodigo, string? RepNombre, string? SupCod, string? SupervisorNombre,
                               string? Centro, string? Alertas, string? Canal, string? ContactoCon,
                               string? Resumen, string? Compromiso, string? FechaSeguimiento, string? Estado, string? Indicadores);

    [HttpPost("contactos")]
    public IActionResult GuardarContacto([FromBody] ContactoEdit e)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        if (e is null || string.IsNullOrWhiteSpace(e.RepCodigo)) return BadRequest(new { ok = false, error = "Falta el vendedor" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand(@"
INSERT INTO dbo.TorreControlContactos_RM
  (UsuarioTorre, RepCodigo, RepNombre, SupCod, SupervisorNombre, Centro, Alertas, Canal, ContactoCon,
   Resumen, Compromiso, FechaSeguimiento, Estado, Indicadores)
VALUES (@usu,@rep,@nom,@sc,@sup,@cen,@ale,@can,@con,@res,@com,@seg,ISNULL(@est,'Abierto'),@ind);
SELECT SCOPE_IDENTITY();", cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@usu", HttpContext.Session.GetString("user_login") ?? "");
            cmd.Parameters.AddWithValue("@rep", e.RepCodigo);
            cmd.Parameters.AddWithValue("@nom", (object?)e.RepNombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sc", (object?)e.SupCod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sup", (object?)e.SupervisorNombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cen", (object?)e.Centro ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ale", (object?)e.Alertas ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@can", (object?)e.Canal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@con", (object?)e.ContactoCon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@res", (object?)e.Resumen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@com", (object?)e.Compromiso ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@seg", string.IsNullOrWhiteSpace(e.FechaSeguimiento) ? DBNull.Value : e.FechaSeguimiento);
            cmd.Parameters.AddWithValue("@est", (object?)e.Estado ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ind", (object?)e.Indicadores ?? DBNull.Value);
            var id = cmd.ExecuteScalar();
            return Ok(new { ok = true, conId = Convert.ToInt32(id) });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apitorre/snapshots ─────────────────────────────────────────────
    [HttpGet("snapshots")]
    public IActionResult Snapshots([FromQuery] string? rep, [FromQuery] string? codigo, [FromQuery] string? fecha)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            var w = "EnAlarma=1"; var parms = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(rep)) { w += " AND RepCodigo=@rep"; parms["@rep"] = rep; }
            if (!string.IsNullOrWhiteSpace(codigo)) { w += " AND Codigo=@cod"; parms["@cod"] = codigo; }
            var dia = string.IsNullOrWhiteSpace(fecha) ? DateTime.Today.ToString("yyyy-MM-dd") : fecha;
            w += " AND FechaCorte>=@d AND FechaCorte<DATEADD(DAY,1,CAST(@d AS datetime))"; parms["@d"] = dia;
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand($@"
SELECT TOP 2000 FechaCorte, Codigo, RepCodigo, RepNombre, Supervisor=SupervisorNombre, Centro, ValorTexto, Detalle, Severidad
FROM dbo.TorreControlSnapshots_RM WHERE {w} ORDER BY FechaCorte DESC, Severidad DESC", cn) { CommandTimeout = 30 };
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            var lst = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                lst.Add(new
                {
                    Corte = rd.GetDateTime(0).ToString("yyyy-MM-dd HH:mm"), Codigo = rd.GetString(1),
                    RepCodigo = rd.GetString(2), RepNombre = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    Supervisor = rd.IsDBNull(4) ? "" : rd.GetString(4), Centro = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Valor = rd.IsDBNull(6) ? "" : rd.GetString(6), Detalle = rd.IsDBNull(7) ? "" : rd.GetString(7),
                    Severidad = rd.GetInt32(8)
                });
            return Ok(new { ok = true, snapshots = lst });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apitorre/reporte  (resumen ejecutivo de bitácora + snapshots) ───
    // Rango desde-hasta: alertas generadas por tipo/día/supervisor/vendedor y cuántas
    // fueron atendidas (contactos). Para pantalla, impresión y Excel.
    [HttpGet("reporte")]
    public IActionResult Reporte([FromQuery] string? desde, [FromQuery] string? hasta,
        [FromQuery] string? supervisores, [FromQuery] string? reps, [FromQuery] string? codigos)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            var d0 = DateOnly.TryParse(desde, out var a) ? a : DateOnly.FromDateTime(DateTime.Today);
            var d1 = DateOnly.TryParse(hasta, out var b) ? b : DateOnly.FromDateTime(DateTime.Today);
            if (d1 < d0) d1 = d0;
            var ini = d0.ToString("yyyy-MM-dd");
            var finNext = d1.AddDays(1).ToString("yyyy-MM-dd");
            var fparms = new Dictionary<string, object>();
            var wSup = BuildIn("SupCod", ParseList(supervisores), "sp", fparms);
            var wRep = BuildIn("RepCodigo", ParseList(reps), "rp", fparms);
            var wCod = BuildIn("Codigo", ParseList(codigos), "cd", fparms);
            var filtro = wSup + " " + wRep;            // contactos (por sup/rep)
            var filtroSnap = filtro + " " + wCod;       // snapshots (además por tipo de alerta)

            using var cn = NewConn();
            cn.Open();
            var nombres = new Dictionary<string, string>();
            foreach (var m in TorreCore.CargarMonitores(cn, false)) nombres[m.Codigo] = m.Nombre;
            // Representantes marcados "no medir": se excluyen del reporte por completo.
            var repsOff = TorreCore.LeerRepsOff(cn);

            // 1) Alertas distintas (rep, codigo, día)
            var porTipo = new Dictionary<string, int>();
            var porDiaAlertas = new Dictionary<string, int>();
            var repInfo = new Dictionary<string, (string nom, string sup)>(StringComparer.OrdinalIgnoreCase);
            var vendAlertas = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var vendTotal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var vendGenSet = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);   // (codigo|dia) generadas por rep
            var supAlertas = new Dictionary<string, int>();
            var supRepsAlarma = new Dictionary<string, HashSet<string>>();
            // El 1er pedido tardío NO cuenta cuando aún no había pedido (ValorTexto null/—):
            // eso es "aún no ha pedido" a esa hora, no un pedido tardío real. Solo cuenta la hora real.
            using (var cmd = new SqlCommand($@"
SELECT DISTINCT RepCodigo, ISNULL(RepNombre,'') RepNombre, ISNULL(SupervisorNombre,'') Sup, Codigo, CAST(FechaCorte AS date) Dia
FROM dbo.TorreControlSnapshots_RM WITH (NOLOCK)
WHERE EnAlarma=1 AND FechaCorte>=@ini AND FechaCorte<@fin {filtroSnap}
  AND NOT (Codigo='PRIMERA_VISITA_PEDIDO' AND (ValorTexto IS NULL OR ValorTexto IN (N'—', N'-', N'')))", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@ini", ini); cmd.Parameters.AddWithValue("@fin", finNext);
                foreach (var (k, v) in fparms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var rep = rd.GetString(0); var nom = rd.GetString(1); var sup = rd.GetString(2); var cod = rd.GetString(3);
                    if (repsOff.Contains(rep)) continue;   // no medir
                    var dia = rd.GetDateTime(4).ToString("yyyy-MM-dd");
                    porTipo[cod] = porTipo.GetValueOrDefault(cod) + 1;
                    porDiaAlertas[dia] = porDiaAlertas.GetValueOrDefault(dia) + 1;
                    repInfo[rep] = (nom, sup);
                    if (!vendAlertas.TryGetValue(rep, out var vt)) { vt = new(); vendAlertas[rep] = vt; }
                    vt[cod] = vt.GetValueOrDefault(cod) + 1;
                    vendTotal[rep] = vendTotal.GetValueOrDefault(rep) + 1;
                    if (!vendGenSet.TryGetValue(rep, out var gs)) { gs = new(); vendGenSet[rep] = gs; }
                    gs.Add(cod + "|" + dia);
                    supAlertas[sup] = supAlertas.GetValueOrDefault(sup) + 1;
                    if (!supRepsAlarma.TryGetValue(sup, out var s)) { s = new(); supRepsAlarma[sup] = s; }
                    s.Add(rep);
                }
            }

            // 2) Contactos (atendidos). Homologación: un contacto ATIENDE todas las alertas de ese
            //    vendedor ese día (aunque queden sin resolver). Se registran los días contactados por rep.
            var contactoRep = new Dictionary<string, (int n, string comp)>(StringComparer.OrdinalIgnoreCase);
            var porDiaContactos = new Dictionary<string, int>();
            var supAtendidos = new Dictionary<string, HashSet<string>>();
            var contactoDias = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);   // rep -> días contactados
            int contactosTotal = 0;
            using (var cmd = new SqlCommand($@"
SELECT RepCodigo, ISNULL(SupervisorNombre,'') Sup, FechaContacto, ISNULL(Compromiso,'') Comp
FROM dbo.TorreControlContactos_RM WITH (NOLOCK)
WHERE FechaContacto>=@ini AND FechaContacto<@fin {filtro}", cn) { CommandTimeout = 60 })
            {
                cmd.Parameters.AddWithValue("@ini", ini); cmd.Parameters.AddWithValue("@fin", finNext);
                foreach (var (k, v) in fparms) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var rep = rd.GetString(0); var sup = rd.GetString(1); var f = rd.GetDateTime(2); var comp = rd.GetString(3);
                    if (repsOff.Contains(rep)) continue;
                    contactosTotal++;
                    var cur = contactoRep.GetValueOrDefault(rep);
                    contactoRep[rep] = (cur.n + 1, string.IsNullOrEmpty(comp) ? cur.comp : comp);
                    var dia = f.ToString("yyyy-MM-dd");
                    porDiaContactos[dia] = porDiaContactos.GetValueOrDefault(dia) + 1;
                    if (!supAtendidos.TryGetValue(sup, out var s)) { s = new(); supAtendidos[sup] = s; }
                    s.Add(rep);
                    if (!contactoDias.TryGetValue(rep, out var cd)) { cd = new(); contactoDias[rep] = cd; }
                    cd.Add(dia);
                }
            }
            // Alertas atendidas por rep = las generadas (codigo|día) cuyo DÍA fue contactado para ese rep.
            int AtnDeRep(string rep, HashSet<string> gen)
            {
                if (!contactoDias.TryGetValue(rep, out var cd)) return 0;
                return gen.Count(z => { var i = z.IndexOf('|'); return i >= 0 && cd.Contains(z.Substring(i + 1)); });
            }

            var porTipoOut = porTipo.Select(kv => new { codigo = kv.Key, nombre = nombres.GetValueOrDefault(kv.Key, kv.Key), alertas = kv.Value }).OrderByDescending(x => x.alertas).ToList();
            var dias = porDiaAlertas.Keys.Union(porDiaContactos.Keys).Distinct().OrderBy(x => x).ToList();
            var porDiaOut = dias.Select(d => new { dia = d, alertas = porDiaAlertas.GetValueOrDefault(d), contactos = porDiaContactos.GetValueOrDefault(d) }).ToList();
            var vendedores = vendTotal.Select(kv =>
            {
                var ri = repInfo.GetValueOrDefault(kv.Key); var c = contactoRep.GetValueOrDefault(kv.Key);
                var gen = vendGenSet.TryGetValue(kv.Key, out var gs) ? gs : new HashSet<string>();
                var atn = AtnDeRep(kv.Key, gen);
                return new { rep = kv.Key, nombre = ri.nom, supervisor = ri.sup, alertas = kv.Value,
                    alertasGeneradas = gen.Count, alertasAtendidas = atn,
                    tipos = vendAlertas[kv.Key], atendido = c.n > 0, contactos = c.n, compromiso = c.comp };
            }).OrderByDescending(x => x.alertas).ToList();
            var supOut = supAlertas.Select(kv => new { supervisor = kv.Key, alertas = kv.Value, repsAlarma = supRepsAlarma.TryGetValue(kv.Key, out var ra) ? ra.Count : 0, repsAtendidos = supAtendidos.TryGetValue(kv.Key, out var at) ? at.Count : 0 }).OrderByDescending(x => x.alertas).ToList();

            int repsEnAlarma = repInfo.Count, repsAtendidos = contactoRep.Count, alertasTotal = porTipo.Values.Sum();
            int alertasGenTotal = vendGenSet.Values.Sum(s => s.Count);
            int alertasAtnTotal = vendGenSet.Sum(kv => AtnDeRep(kv.Key, kv.Value));
            return Ok(new
            {
                ok = true, desde = ini, hasta = d1.ToString("yyyy-MM-dd"),
                resumen = new { alertasTotal, repsEnAlarma, contactosTotal, repsAtendidos,
                    pctAtendido = repsEnAlarma > 0 ? Math.Round(repsAtendidos * 100.0 / repsEnAlarma) : 0,
                    alertasGeneradas = alertasGenTotal, alertasAtendidas = alertasAtnTotal,
                    pctAlertasAtendidas = alertasGenTotal > 0 ? Math.Round(alertasAtnTotal * 100.0 / alertasGenTotal) : 0 },
                porTipo = porTipoOut, porDia = porDiaOut, vendedores, supervisores = supOut
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apitorre/catalogo  (opciones para armar monitores personalizados) ─
    // tipo = linea | producto | suplidor | reptipe | supervisor.  q = búsqueda opcional.
    [HttpGet("catalogo")]
    public IActionResult Catalogo([FromQuery] string tipo, [FromQuery] string? q)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            string sql; bool usaQ = !string.IsNullOrWhiteSpace(q);
            var qp = usaQ ? "%" + q!.Trim() + "%" : "%";
            switch ((tipo ?? "").ToLowerInvariant())
            {
                case "producto":
                    sql = @"SELECT TOP 300 ProID id, ProDescripcion nombre FROM Productos WITH (NOLOCK)
                            WHERE (@q='%' OR ProDescripcion LIKE @q OR ProCodigo LIKE @q) ORDER BY ProDescripcion"; break;
                case "suplidor":
                    sql = @"SELECT Cat3ID id, Cat3Descripcion nombre FROM Categoria3 WITH (NOLOCK)
                            WHERE (@q='%' OR Cat3Descripcion LIKE @q) ORDER BY Cat3Descripcion"; break;
                case "reptipe":
                    sql = @"SELECT DISTINCT ISNULL(RepTipe,'') id, ISNULL(RepTipe,'') nombre FROM RepresentantesAdicionales WITH (NOLOCK)
                            WHERE ISNULL(RepTipe,'')<>'' ORDER BY id"; break;
                case "supervisor":
                    sql = @"SELECT DISTINCT r.RepSupervisor id, ISNULL(sup.RepNombre,r.RepSupervisor) nombre
                            FROM Representantes r WITH (NOLOCK) LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
                            WHERE r.RepEstatus=1 AND ISNULL(r.RepSupervisor,'')<>'' ORDER BY nombre"; break;
                default: // linea / categoría
                    sql = @"SELECT LinID id, LinDescripcion nombre FROM Lineas WITH (NOLOCK)
                            WHERE (@q='%' OR LinDescripcion LIKE @q) ORDER BY LinDescripcion"; break;
            }
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
            cmd.Parameters.AddWithValue("@q", qp);
            var lst = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) lst.Add(new { id = rd.IsDBNull(0) ? "" : Convert.ToString(rd.GetValue(0)), nombre = rd.IsDBNull(1) ? "" : Convert.ToString(rd.GetValue(1)) });
            return Ok(new { ok = true, items = lst });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apitorre/personalizado  (estadística de un monitor personalizado) ─
    // monId + desde/hasta (rango); devuelve por vendedor el valor de la métrica por
    // día y acumulado, y en cuántos días cumplió el objetivo.
    [HttpGet("personalizado")]
    public IActionResult PersonalizadoStats([FromQuery] int monId, [FromQuery] string? desde, [FromQuery] string? hasta)
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            var mon = TorreCore.CargarMonitores(cn, soloActivos: false).FirstOrDefault(x => x.MonID == monId && x.Tipo == "personalizado");
            if (mon == null) return NotFound(new { ok = false, error = "Monitor no encontrado" });
            var spec = TorrePersonalizados.Parse(new[] { mon }).FirstOrDefault();
            if (spec == null) return BadRequest(new { ok = false, error = "Configuración inválida" });

            var d0 = DateOnly.TryParse(desde, out var a) ? a : DateOnly.FromDateTime(DateTime.Today);
            var d1 = DateOnly.TryParse(hasta, out var b) ? b : DateOnly.FromDateTime(DateTime.Today);
            if (d1 < d0) d1 = d0;
            var dt0 = d0.ToDateTime(TimeOnly.MinValue);
            var dt1 = d1.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var datos = TorrePersonalizados.Calcular(cn, spec, dt0, dt1, porDia: true);   // rep -> dia -> double[4]
            int idx = spec.MetricaIdx;

            // Reps del grupo (para mostrar también los que vendieron 0)
            var repNom = new Dictionary<string, (string nom, string sup)>(StringComparer.OrdinalIgnoreCase);
            var gp = new Dictionary<string, object>();
            string gWhere = spec.GrupoTipo switch
            {
                "supervisor" => BuildIn("r.RepSupervisor", spec.GrupoVals, "g", gp),
                "reps" => BuildIn("r.RepCodigo", spec.GrupoVals, "g", gp),
                _ => BuildIn("ra.RepTipe", spec.GrupoVals, "g", gp)
            };
            using (var cmd = new SqlCommand($@"
SELECT r.RepCodigo, ISNULL(r.RepNombre,'') Nom, ISNULL(sup.RepNombre,r.RepSupervisor) Sup
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor' {gWhere}", cn) { CommandTimeout = 30 })
            {
                foreach (var (k, v) in gp) cmd.Parameters.AddWithValue(k, v);
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) repNom[rd.GetString(0)] = (rd.IsDBNull(1) ? "" : rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2));
            }

            // Días con actividad (columnas) = unión de días presentes en los datos
            var dias = datos.SelectMany(kv => kv.Value.Keys).Distinct().OrderBy(x => x).ToList();
            // Reps a mostrar = grupo ∪ los que aparecen en datos
            var reps = repNom.Keys.Union(datos.Keys, StringComparer.OrdinalIgnoreCase).ToList();

            var filas = reps.Select(rep =>
            {
                var byd = datos.TryGetValue(rep, out var dd) ? dd : new Dictionary<string, double[]>();
                var valores = dias.Select(d => byd.TryGetValue(d, out var arr) ? Math.Round(arr[idx], idx == 2 ? 0 : 0) : 0.0).ToList();
                double total = valores.Sum();
                int cumplidos = dias.Count(d => TorrePersonalizados.Cumple(spec, byd.TryGetValue(d, out var arr) ? arr[idx] : 0));
                var ri = repNom.TryGetValue(rep, out var rn) ? rn : (nom: "", sup: "");
                return new { rep, nombre = ri.nom, supervisor = ri.sup, valores, total, diasCumplidos = cumplidos };
            }).Where(x => x.total > 0 || repNom.ContainsKey(x.rep))   // ocultar reps ajenos al grupo sin ventas
              .OrderByDescending(x => x.diasCumplidos).ThenByDescending(x => x.total).ToList();

            int diasEval = dias.Count;
            double totalGrupo = filas.Sum(f => f.total);
            return Ok(new
            {
                ok = true, monitor = new { mon.MonID, mon.Codigo, mon.Nombre, spec.Metrica, spec.Objetivo, spec.Comparador, spec.Unidad },
                desde = d0.ToString("yyyy-MM-dd"), hasta = d1.ToString("yyyy-MM-dd"),
                dias, filas, resumen = new { reps = filas.Count, diasEval, totalGrupo }
            });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }

    // ── GET /apitorre/filtros  (jerarquía de reps para filtros) ──────────────
    [HttpGet("filtros")]
    public IActionResult Filtros()
    {
        if (!IsAuth) return Unauthorized(new { ok = false, error = "Sesión expirada" });
        try
        {
            using var cn = NewConn();
            cn.Open();
            using var cmd = new SqlCommand(@"
SELECT r.RepCodigo, r.RepNombre, ISNULL(r.CedCodigo,'') CedCodigo, ISNULL(cd.CedDescripcion,r.CedCodigo) CentroNombre,
       ISNULL(r.RepSupervisor,'') SupCod, ISNULL(sup.RepNombre,r.RepSupervisor) SupNombre, ISNULL(ra.RepTipe,'') Division
FROM Representantes r WITH (NOLOCK)
LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo=r.RepSupervisor
LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo=r.CedCodigo
LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo=r.RepCodigo
WHERE r.RepEstatus=1 AND ISNULL(r.RepCargo,'')<>'Supervisor'
  AND r.RepCodigo IN (SELECT RepCodigo FROM RutaVisitasFechaHistoricos WITH (NOLOCK) WHERE RutFecha >= DATEADD(DAY,-60,GETDATE()))
ORDER BY CentroNombre, SupNombre, r.RepNombre", cn) { CommandTimeout = 30 };
            var reps = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                reps.Add(new
                {
                    RepCodigo = rd.IsDBNull(0) ? "" : rd.GetString(0), RepNombre = rd.IsDBNull(1) ? "" : rd.GetString(1),
                    CedCodigo = rd.IsDBNull(2) ? "" : rd.GetString(2), CentroNombre = rd.IsDBNull(3) ? "" : rd.GetString(3),
                    SupervisorCod = rd.IsDBNull(4) ? "" : rd.GetString(4), SupervisorNombre = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    Division = rd.IsDBNull(6) ? "" : rd.GetString(6)
                });
            return Ok(new { ok = true, representantes = reps });
        }
        catch (Exception ex) { return StatusCode(500, new { ok = false, error = ex.Message }); }
    }
}
