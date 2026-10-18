using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MfSoftLicense;

// MfSoft License — generador de licencias del Dashboard Smart (solo para el administrador).
// Corre en LOCAL (127.0.0.1), protegido con clave. Guarda la llave de firma cifrada y una
// pequeña base de clientes en la carpeta "datos" (junto a la carpeta de la app).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory, WebRootPath = "wwwroot" });
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

var datosDir = Path.GetFullPath(builder.Configuration["datos"] ?? Path.Combine(AppContext.BaseDirectory, "..", "datos"));
Directory.CreateDirectory(datosDir);
var vault = new Vault(Path.Combine(datosDir, "llave.enc"));
var store = new Store(datosDir);
builder.Services.AddSingleton(vault);
builder.Services.AddSingleton(store);

var app = builder.Build();
var sesiones = new ConcurrentDictionary<string, DateTime>();

bool Autenticado(HttpContext c)
{
    if (c.Request.Cookies.TryGetValue("mfsl", out var t) && sesiones.TryGetValue(t, out var exp) && exp > DateTime.UtcNow)
    { sesiones[t] = DateTime.UtcNow.AddHours(2); return true; }
    return false;
}
void IniciarSesion(HttpContext c)
{
    var tok = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    sesiones[tok] = DateTime.UtcNow.AddHours(2);
    c.Response.Cookies.Append("mfsl", tok, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
}
IResult Mal(string msg, int code = 400) => Results.Json(new { ok = false, error = msg }, statusCode: code);
static string Slug(string s) { var bad = Path.GetInvalidFileNameChars(); var r = new string(s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.'); return r.Length == 0 ? "cliente" : r; }

// Solo accesible desde esta misma máquina (evita DNS-rebinding) + cabecera obligatoria en POST/DELETE (evita CSRF).
app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host;
    if (host != "127.0.0.1" && host != "localhost" && host != "[::1]") { ctx.Response.StatusCode = 403; return; }
    var path = ctx.Request.Path.Value ?? "";
    if (path.StartsWith("/api/"))
    {
        if (ctx.Request.Method != "GET" && ctx.Request.Headers["X-MfSoft"] != "1") { ctx.Response.StatusCode = 403; return; }
        var publico = path is "/api/estado" or "/api/login" or "/api/configurar";
        if (!publico && !Autenticado(ctx))
        {
            ctx.Response.StatusCode = 401; ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":false,\"error\":\"Sesión no iniciada\"}"); return;
        }
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/estado", (HttpContext c) => Results.Ok(new
{
    configurado = vault.Configurado,
    autenticado = Autenticado(c) && vault.Desbloqueado,
    modulos = Mod.Todos.Select(m => new { clave = m.Clave, nombre = m.Nombre }),
    huella = vault.Huella
}));

app.MapPost("/api/configurar", (ConfigReq r, HttpContext c) =>
{
    if (vault.Configurado) return Mal("Ya está configurado.");
    if (string.IsNullOrEmpty(r.Clave) || r.Clave.Length < 8) return Mal("La clave debe tener al menos 8 caracteres.");
    if (r.Clave != r.Confirmar) return Mal("La confirmación no coincide con la clave.");
    string priv;
    if (r.Modo == "importar")
    {
        var ruta = (r.Ruta ?? "").Trim().Trim('"');
        if (!File.Exists(ruta)) return Mal("No se encontró el archivo de la llave privada en esa ruta.");
        priv = File.ReadAllText(ruta);
        try { using var t = RSA.Create(); t.FromXmlString(priv); t.ExportParameters(true); }
        catch { return Mal("Ese archivo no es una llave privada RSA válida (formato XML de LicenseTool)."); }
    }
    else { using var n = RSA.Create(2048); priv = n.ToXmlString(true); }
    vault.Crear(r.Clave, priv);
    IniciarSesion(c);
    return Results.Ok(new { ok = true, modo = r.Modo, publica = vault.PublicaXml, huella = vault.Huella });
});

app.MapPost("/api/login", async (LoginReq r, HttpContext c) =>
{
    if (!vault.Configurado) return Mal("Aún no está configurado.");
    var s = Guardia.SegundosBloqueo();
    if (s > 0) return Mal($"Demasiados intentos. Espere {s} segundos.", 429);
    if (!vault.Desbloquear(r.Clave ?? ""))
    {
        Guardia.Fallo(); await Task.Delay(800);
        return Mal("Clave incorrecta.", 401);
    }
    Guardia.Exito(); IniciarSesion(c);
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/logout", (HttpContext c) =>
{
    if (c.Request.Cookies.TryGetValue("mfsl", out var t)) sesiones.TryRemove(t, out _);
    c.Response.Cookies.Delete("mfsl");
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/cambiar-clave", (CambiarClaveReq r) =>
{
    if (string.IsNullOrEmpty(r.Nueva) || r.Nueva.Length < 8) return Mal("La clave nueva debe tener al menos 8 caracteres.");
    if (r.Nueva != r.Confirmar) return Mal("La confirmación no coincide.");
    return vault.CambiarClave(r.Actual ?? "", r.Nueva) ? Results.Ok(new { ok = true }) : Mal("La clave actual es incorrecta.", 401);
});

app.MapGet("/api/clave-publica", () => Results.Ok(new { ok = true, publica = vault.PublicaXml, huella = vault.Huella }));

app.MapGet("/api/clientes", () => Results.Ok(new { ok = true, clientes = store.Todos() }));

app.MapPost("/api/emitir", (EmitirReq r) =>
{
    var nombre = (r.Nombre ?? "").Trim();
    if (nombre.Length == 0) return Mal("Falta el nombre del cliente.");
    if (!DateOnly.TryParseExact(r.Expira ?? "", "yyyy-MM-dd", out var expira)) return Mal("La fecha de vencimiento no es válida.");
    var activos = (r.Modulos ?? new()).Where(Mod.Claves.Contains).Distinct().ToList();
    if (activos.Count == 0) return Mal("Selecciona al menos un módulo.");
    if (r.Id is not null && store.Get(r.Id) is null) return Mal("El cliente ya no existe.", 404);

    // Huella del servidor (opcional). Con huella la licencia queda ATADA a ese servidor; sin ella
    // funciona en cualquier servidor. Se normaliza a XXXX-XXXX-XXXX-XXXX-XXXX (20 hex).
    string? huella = null;
    if (!string.IsNullOrWhiteSpace(r.Huella))
    {
        var h = new string(r.Huella.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (h.Length != 20 || !h.All(Uri.IsHexDigit))
            return Mal("La huella del servidor no es válida: deben ser 20 caracteres (formato XXXX-XXXX-XXXX-XXXX-XXXX). Cópiala tal cual la muestra el sistema.");
        huella = string.Join("-", Enumerable.Range(0, 5).Select(i => h.Substring(i * 4, 4)));
    }

    var modulos = Mod.Todos.ToDictionary(m => m.Clave, m => activos.Contains(m.Clave));
    var hoy = DateOnly.FromDateTime(DateTime.Today);
    var payload = JsonSerializer.SerializeToUtf8Bytes(
        new { cliente = nombre, emitido = hoy.ToString("yyyy-MM-dd"), expira = expira.ToString("yyyy-MM-dd"), modulos, huella },
        new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    var firma = vault.Firmar(payload);
    if (!vault.VerificarConPublica(payload, firma)) return Mal("La firma generada no verifica con la llave pública (llave dañada).", 500);
    var lic = JsonSerializer.Serialize(new { payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(firma) });

    var carpeta = Path.Combine(datosDir, "licencias", Slug(nombre));
    Directory.CreateDirectory(carpeta);
    var archivo = $"license_{DateTime.Now:yyyyMMdd_HHmmss_fff}.lic";
    for (int k = 2; File.Exists(Path.Combine(carpeta, archivo)); k++) archivo = $"license_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{k}.lic";
    File.WriteAllText(Path.Combine(carpeta, archivo), lic);
    File.WriteAllText(Path.Combine(carpeta, "license.lic"), lic);   // la vigente, lista para copiar al servidor

    var cli = store.Guardar(r.Id, nombre, (r.Contacto ?? "").Trim(), (r.Notas ?? "").Trim(), huella,
        new Emision(DateTime.Now.ToString("yyyy-MM-dd HH:mm"), expira.ToString("yyyy-MM-dd"), activos,
                    Path.Combine("licencias", Slug(nombre), archivo), huella));
    return Results.Ok(new { ok = true, cliente = store.Dto(cli), carpeta, huella = vault.Huella });
});

app.MapGet("/api/clientes/{id}/licencia", (string id, int? n) =>
{
    var c = store.Get(id);
    if (c is null || c.Emisiones.Count == 0) return Results.NotFound();
    var em = n is >= 0 && n < c.Emisiones.Count ? c.Emisiones[n.Value] : c.Emisiones[^1];
    var ruta = Path.Combine(datosDir, em.Archivo);
    if (!File.Exists(ruta)) return Results.NotFound();
    return Results.File(File.ReadAllBytes(ruta), "application/octet-stream", "license.lic");
});

// ── Instalador COMPLETO: el ZIP del sistema (DashSmart_*.zip, carpeta "instalables" junto a app\) con la
// licencia del cliente ya metida junto a INSTALAR_IIS/ACTUALIZAR_IIS (los scripts la toman solos).
var instaladoresDir = Path.GetFullPath(Path.Combine(datosDir, "..", "instalables"));

app.MapGet("/api/instaladores", () =>
{
    var lista = Directory.Exists(instaladoresDir)
        ? new DirectoryInfo(instaladoresDir).GetFiles("*.zip").OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new { nombre = f.Name, mb = Math.Round(f.Length / 1048576.0, 1), fecha = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") }).ToList()
        : new();
    return Results.Ok(new { ok = true, carpeta = instaladoresDir, instaladores = lista });
});

app.MapGet("/api/clientes/{id}/instalador", (string id, string? zip, int? n) =>
{
    var c = store.Get(id);
    if (c is null || c.Emisiones.Count == 0) return Results.NotFound();
    var em = n is >= 0 && n < c.Emisiones.Count ? c.Emisiones[n.Value] : c.Emisiones[^1];
    var licRuta = Path.Combine(datosDir, em.Archivo);
    if (!File.Exists(licRuta)) return Mal("No se encontró el archivo de esa licencia en la carpeta datos.", 404);
    var nombreZip = Path.GetFileName(zip ?? "");   // solo el nombre: evita rutas hacia otras carpetas
    var origen = Path.Combine(instaladoresDir, nombreZip);
    if (nombreZip.Length == 0 || !nombreZip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !File.Exists(origen))
        return Mal("No se encontró ese instalador en la carpeta instalables.", 404);

    var tmp = Path.Combine(Path.GetTempPath(), "adl_" + Guid.NewGuid().ToString("N") + ".zip");
    try
    {
        using (var src = ZipFile.OpenRead(origen))
        using (var dst = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            // La licencia va en la carpeta donde esta INSTALAR_IIS.ps1 dentro del ZIP del sistema.
            var marcador = src.Entries.FirstOrDefault(e => e.Name.Equals("INSTALAR_IIS.ps1", StringComparison.OrdinalIgnoreCase));
            var prefijo = marcador is null ? "" : marcador.FullName[..^marcador.Name.Length];
            foreach (var e in src.Entries)
            {
                if (e.FullName.Equals(prefijo + "license.lic", StringComparison.OrdinalIgnoreCase)) continue;   // nunca arrastrar otra licencia
                var ne = dst.CreateEntry(e.FullName, CompressionLevel.Fastest);
                ne.LastWriteTime = e.LastWriteTime;
                if (e.FullName.EndsWith('/')) continue;   // entrada de carpeta
                using var a = e.Open(); using var b = ne.Open(); a.CopyTo(b);
            }
            var le = dst.CreateEntry(prefijo + "license.lic", CompressionLevel.Optimal);
            using var s = le.Open(); var bytes = File.ReadAllBytes(licRuta); s.Write(bytes, 0, bytes.Length);
        }
    }
    catch (Exception ex) { try { File.Delete(tmp); } catch { } return Mal("No se pudo armar el instalador: " + ex.Message, 500); }
    var stream = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
    return Results.File(stream, "application/zip", Path.GetFileNameWithoutExtension(nombreZip) + "_" + Slug(c.Nombre) + ".zip");
});

// "Paquete de licencia": un ZIP que el cliente descomprime en su servidor; con doble clic en
// INSTALAR_LICENCIA.bat la licencia se instala sola en App_Data (verifica la huella si la trae,
// respalda la anterior y aplica el cambio). Se arma con la licencia YA emitida + las plantillas.
app.MapGet("/api/clientes/{id}/paquete", (string id, int? n) =>
{
    var c = store.Get(id);
    if (c is null || c.Emisiones.Count == 0) return Results.NotFound();
    var em = n is >= 0 && n < c.Emisiones.Count ? c.Emisiones[n.Value] : c.Emisiones[^1];
    var ruta = Path.Combine(datosDir, em.Archivo);
    var plantillas = Path.Combine(AppContext.BaseDirectory, "plantillas");
    var ps1 = Path.Combine(plantillas, "INSTALAR_LICENCIA.ps1"); var bat = Path.Combine(plantillas, "INSTALAR_LICENCIA.bat");
    if (!File.Exists(ruta)) return Mal("No se encontró el archivo de esa licencia en la carpeta datos.", 404);
    if (!File.Exists(ps1) || !File.Exists(bat)) return Mal("Faltan las plantillas del instalador (carpeta app\\plantillas). Vuelva a publicar el programa.", 500);

    var expira = DateOnly.TryParseExact(em.Expira, "yyyy-MM-dd", out var ex) ? ex.ToString("dd/MM/yyyy") : em.Expira;
    var leeme =
$@"INSTALAR LA LICENCIA  -  Dashboard Smart
==========================================

Cliente : {c.Nombre}
Vence   : {expira}
Modulos : {em.Modulos.Count} de {Mod.Todos.Length}
Servidor: {(string.IsNullOrEmpty(em.Huella) ? "no atada a ningun servidor (funciona en cualquiera)" : "atada al servidor " + em.Huella)}

COMO INSTALARLA (en el servidor donde esta instalado el sistema)
  1. Descomprima TODO este ZIP en una carpeta del servidor (no ejecute desde dentro del ZIP).
  2. Haga doble clic en  INSTALAR_LICENCIA.bat  (pide permisos de Administrador).
  3. Siga lo que dice la pantalla. Al final debe decir ""Licencia VALIDA"".

  El programa busca solo donde esta instalado el sistema, respalda la licencia anterior
  (si habia) y la reemplaza. Para renovar mas adelante se hace exactamente igual con el
  paquete nuevo.

SI DICE QUE LA LICENCIA ES DE OTRO SERVIDOR
  Esta licencia esta atada a un servidor especifico y no se instala en otro. Entregue al
  administrador del sistema la ""huella"" de ESTE servidor (la muestra el sistema en pantalla
  y esta en la carpeta App_Data\huella_servidor.txt) para que le emita la licencia correcta.

SI NO SE REFLEJA DE INMEDIATO
  El sistema relee la licencia cada ~10 minutos (o reinicie la aplicacion en IIS).
  Si despues sigue diciendo ""Sistema no disponible"", avise al administrador del sistema.
";
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
    {
        void Add(string nombre, byte[] bytes) { var e = zip.CreateEntry(nombre, CompressionLevel.Optimal); using var s = e.Open(); s.Write(bytes, 0, bytes.Length); }
        Add("license.lic", File.ReadAllBytes(ruta));
        Add("INSTALAR_LICENCIA.ps1", File.ReadAllBytes(ps1));
        Add("INSTALAR_LICENCIA.bat", File.ReadAllBytes(bat));
        Add("LEEME.txt", new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(leeme.Replace("\r\n", "\n").Replace("\n", "\r\n"))).ToArray());
    }
    return Results.File(ms.ToArray(), "application/zip", $"Licencia_{Slug(c.Nombre)}_{em.Expira}.zip");
});

app.MapDelete("/api/clientes/{id}", (string id) => store.Eliminar(id) ? Results.Ok(new { ok = true }) : Mal("No existe.", 404));

app.MapGet("/api/exportar-excel", () =>
    Results.File(store.ExportarExcel(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"MfSoft_Clientes_{DateTime.Now:yyyyMMdd}.xlsx"));

app.Run();
