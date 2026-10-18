<%@ Language="VBScript" CodePage=65001 %>
<!--#include file="inc/config.asp"-->
<!--#include file="inc/json.asp"-->
<!--#include file="inc/empresa.asp"-->
<%
Option Explicit
Response.Buffer  = True
Response.CharSet = "utf-8"
Session.Timeout  = 480

' ── Si ya hay sesion activa, ir al dashboard ──────────────────────────────────
If Session("user_login") <> "" Then
    Response.Redirect "dashboard.asp"
    Response.End
End If

' ── Manejar POST JSON (llamado por el fetch del navegador) ───────────────────
If Request.ServerVariables("REQUEST_METHOD") = "POST" Then
    Response.ContentType = "application/json"

    Dim body, usuario, clave
    body    = ReadRequestBody()
    usuario = Trim(JsonField(body, "usuario"))
    clave   = JsonField(body, "clave")

    If usuario = "" Or clave = "" Then
        Response.Write "{""ok"":false,""error"":""Complete usuario y contrasena""}"
        Response.End
    End If

    Const MAX_INTENTOS = 5
    Const BLOQUEO_MINS = 15

    On Error Resume Next

    Dim cn
    Set cn = GetConn()

    If Err.Number <> 0 Then
        Response.Write "{""ok"":false,""error"":""Error de conexion: " & Replace(Err.Description, """", "'") & """}"
        Response.End
    End If

    Err.Clear

    ' ── Buscar usuario en la tabla ────────────────────────────────────────────
    Dim cmd, rs
    Set cmd = Server.CreateObject("ADODB.Command")
    Set cmd.ActiveConnection = cn
    cmd.CommandType = 1 ' adCmdText
    cmd.CommandText = "SELECT UsuInicioSesion, UsuNombres, UsuApellidos, " & _
                      "UsuEstatus, UsuClave, " & _
                      "ISNULL(UsuIntentosClave, 0) AS UsuIntentosClave, " & _
                      "UsuUltimoIntento " & _
                      "FROM UsuarioSistema " & _
                      "WHERE UsuInicioSesion = ?"
    ' adVarChar=200, adParamInput=1
    cmd.Parameters.Append cmd.CreateParameter("p_login", 200, 1, 64, usuario)

    Set rs = cmd.Execute()

    If Err.Number <> 0 Then
        Response.Write "{""ok"":false,""error"":""Error de consulta: " & Replace(Err.Description, """", "'") & """}"
        cn.Close : Response.End
    End If

    If rs.EOF Then
        rs.Close : cn.Close
        Response.Write "{""ok"":false,""error"":""Usuario o contrasena incorrectos""}"
        Response.End
    End If

    Dim login, nombres, apellidos, estatus, claveDB, intentos, ultimoIntento
    login         = rs("UsuInicioSesion")
    nombres       = Trim("" & rs("UsuNombres"))
    apellidos     = Trim("" & rs("UsuApellidos"))
    estatus       = rs("UsuEstatus")
    claveDB       = UCase(Trim("" & rs("UsuClave")))
    intentos      = CInt(rs("UsuIntentosClave"))
    ultimoIntento = rs("UsuUltimoIntento")
    rs.Close

    ' ── Verificar estatus ─────────────────────────────────────────────────────
    If Not CBool(estatus) Then
        cn.Close
        Response.Write "{""ok"":false,""error"":""Usuario inactivo. Contacte al administrador""}"
        Response.End
    End If

    ' ── Verificar bloqueo por intentos ────────────────────────────────────────
    If intentos >= MAX_INTENTOS And Not IsNull(ultimoIntento) Then
        Dim minsPasados
        minsPasados = DateDiff("n", ultimoIntento, Now())
        If minsPasados < BLOQUEO_MINS Then
            Dim restMin
            restMin = BLOQUEO_MINS - minsPasados
            cn.Close
            Response.Write "{""ok"":false,""error"":""Cuenta bloqueada. Intente de nuevo en " & restMin & " min""}"
            Response.End
        Else
            ' Periodo vencido, resetear contador (ignorar si sin permiso UPDATE)
            Dim cmdRst
            Set cmdRst = Server.CreateObject("ADODB.Command")
            Set cmdRst.ActiveConnection = cn
            cmdRst.CommandType = 1
            cmdRst.CommandText = "UPDATE UsuarioSistema SET UsuIntentosClave = 0 WHERE UsuInicioSesion = ?"
            cmdRst.Parameters.Append cmdRst.CreateParameter("p1", 200, 1, 64, usuario)
            cmdRst.Execute
            Err.Clear
            intentos = 0
        End If
    End If

    ' ── Comparar contrasena usando HASHBYTES de SQL Server ───────────────────
    ' HASHBYTES('MD5', VARCHAR) = hash de bytes ANSI (equivalente a UTF-8 para ASCII)
    Dim cmdHash, rsHash, hashDB
    Set cmdHash = Server.CreateObject("ADODB.Command")
    Set cmdHash.ActiveConnection = cn
    cmdHash.CommandType = 1
    cmdHash.CommandText = "SELECT UPPER(CONVERT(VARCHAR(32), HASHBYTES('MD5', CAST(? AS VARCHAR(100))), 2)) AS H"
    cmdHash.Parameters.Append cmdHash.CreateParameter("p_pwd", 200, 1, 100, clave)
    Set rsHash = cmdHash.Execute()

    If Err.Number <> 0 Then
        cn.Close
        Response.Write "{""ok"":false,""error"":""Error interno: " & Replace(Err.Description, """", "'") & """}"
        Response.End
    End If

    hashDB = UCase(Trim("" & rsHash("H")))
    rsHash.Close

    If hashDB <> claveDB Then
        ' Contrasena incorrecta — registrar intento (ignorar si sin permiso)
        Dim cmdFail
        Set cmdFail = Server.CreateObject("ADODB.Command")
        Set cmdFail.ActiveConnection = cn
        cmdFail.CommandType = 1
        cmdFail.CommandText = "UPDATE UsuarioSistema " & _
                              "SET UsuIntentosClave = ISNULL(UsuIntentosClave, 0) + 1, " & _
                              "    UsuUltimoIntento = GETDATE() " & _
                              "WHERE UsuInicioSesion = ?"
        cmdFail.Parameters.Append cmdFail.CreateParameter("p1", 200, 1, 64, usuario)
        cmdFail.Execute
        Err.Clear

        Dim restantes, errMsg
        restantes = MAX_INTENTOS - intentos - 1
        If restantes > 0 And restantes <= 2 Then
            Dim singl
            If restantes = 1 Then singl = "intento restante" Else singl = "intentos restantes"
            errMsg = "Usuario o contrasena incorrectos (" & restantes & " " & singl & ")"
        ElseIf restantes <= 0 Then
            errMsg = "Cuenta bloqueada por " & BLOQUEO_MINS & " minutos"
        Else
            errMsg = "Usuario o contrasena incorrectos"
        End If

        cn.Close
        Response.Write "{""ok"":false,""error"":" & JsonStr(errMsg) & "}"
        Response.End
    End If

    ' ── Login exitoso — resetear intentos (ignorar si sin permiso) ───────────
    Dim cmdOK
    Set cmdOK = Server.CreateObject("ADODB.Command")
    Set cmdOK.ActiveConnection = cn
    cmdOK.CommandType = 1
    cmdOK.CommandText = "UPDATE UsuarioSistema " & _
                        "SET UsuIntentosClave = 0, UsuFechaActualizacion = GETDATE() " & _
                        "WHERE UsuInicioSesion = ?"
    cmdOK.Parameters.Append cmdOK.CreateParameter("p1", 200, 1, 64, usuario)
    cmdOK.Execute
    Err.Clear

    cn.Close

    ' Guardar en sesion
    Session("user_login")  = login
    Session("user_nombre") = Trim(nombres & " " & apellidos)

    Response.Write "{""ok"":true}"
    Response.End
End If
' ── FIN POST — continua con el HTML del formulario (GET) ─────────────────────
%><!DOCTYPE html>
<html lang="es">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Acceso &mdash; AM COMERCIAL</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{
  background:#0d1117;
  color:#e6edf3;
  font-family:'Segoe UI',Tahoma,sans-serif;
  min-height:100vh;
  display:flex;
  flex-direction:column;
  align-items:center;
  justify-content:center;
  padding:20px;
}
.brand{display:flex;align-items:center;gap:14px;margin-bottom:32px}
.brand-name{font-size:22px;font-weight:800;letter-spacing:.4px}
.brand-sub{font-size:12px;color:#58a6ff;margin-top:3px}
.card{
  background:#161b22;
  border:1px solid #30363d;
  border-radius:14px;
  padding:36px 40px;
  width:100%;
  max-width:400px;
  box-shadow:0 16px 48px rgba(0,0,0,.5);
}
.card h1{font-size:18px;font-weight:700;margin-bottom:6px}
.card p{font-size:12px;color:#8b949e;margin-bottom:28px}
.field{margin-bottom:18px}
.field label{display:block;font-size:11px;color:#8b949e;font-weight:600;text-transform:uppercase;letter-spacing:.6px;margin-bottom:6px}
.field input{
  width:100%;background:#21262d;border:1px solid #30363d;
  color:#e6edf3;border-radius:8px;padding:10px 14px;
  font-size:14px;outline:none;transition:border .15s;
}
.field input:focus{border-color:#58a6ff;background:#1c2128}
.field input::placeholder{color:#484f58}
.pw-wrap{position:relative}
.pw-wrap input{padding-right:42px}
.pw-eye{
  position:absolute;right:12px;top:50%;transform:translateY(-50%);
  background:none;border:none;color:#8b949e;cursor:pointer;
  font-size:16px;line-height:1;padding:4px;
}
.pw-eye:hover{color:#e6edf3}
.btn-login{
  width:100%;background:#1f6feb;border:none;color:#fff;
  border-radius:8px;padding:11px;font-size:14px;font-weight:700;
  cursor:pointer;transition:background .15s;margin-top:4px;
  display:flex;align-items:center;justify-content:center;gap:8px;
}
.btn-login:hover:not(:disabled){background:#388bfd}
.btn-login:disabled{opacity:.6;cursor:not-allowed}
.spinner{
  width:16px;height:16px;border:2px solid rgba(255,255,255,.3);
  border-top-color:#fff;border-radius:50%;
  animation:spin .7s linear infinite;display:none;
}
@keyframes spin{to{transform:rotate(360deg)}}
.msg{
  margin-top:18px;padding:10px 14px;border-radius:8px;
  font-size:13px;display:none;
}
.msg.err{background:#3a1515;border:1px solid #7a1010;color:#ffaaaa}
.msg.ok {background:#0f2d0f;border:1px solid #1a6a1a;color:#7ee07e}
.footer{margin-top:28px;font-size:11px;color:#484f58;text-align:center}
</style>
</head>
<body>

<div class="brand">
  <% If g_EmpHasLogo Then %>
  <img src="api/logo.asp" alt="Logo" style="height:40px;width:auto;border-radius:6px;object-fit:contain">
  <% Else %>
  <svg width="60" height="40" viewBox="0 0 70 46" xmlns="http://www.w3.org/2000/svg">
    <defs><clipPath id="lc"><rect width="70" height="46" rx="6"/></clipPath></defs>
    <rect width="70" height="46" rx="6" fill="#0f1922"/>
    <g clip-path="url(#lc)">
      <polygon points="1,0 15,0 25,46 11,46"  fill="#2196F3"/>
      <polygon points="19,0 28,0 38,46 29,46" fill="#43A047"/>
      <polygon points="32,0 41,0 51,46 42,46" fill="#43A047"/>
      <polygon points="45,0 54,0 64,46 55,46" fill="#43A047"/>
      <polygon points="58,0 67,0 77,46 68,46" fill="#43A047"/>
    </g>
  </svg>
  <% End If %>
  <div>
    <div class="brand-name"><%= Server.HtmlEncode(g_EmpNombre) %></div>
    <div class="brand-sub">Dashboard Visitas en Vivo</div>
  </div>
</div>

<div class="card">
  <h1>Iniciar sesion</h1>
  <p>Ingresa tus credenciales del sistema para continuar</p>

  <form id="form" onsubmit="doLogin(event)">
    <div class="field">
      <label>Usuario</label>
      <input type="text" id="usuario" placeholder="Tu usuario del sistema"
             autocomplete="username" autofocus required>
    </div>
    <div class="field">
      <label>Contrasena</label>
      <div class="pw-wrap">
        <input type="password" id="clave" placeholder="Contrasena"
               autocomplete="current-password" required>
        <button type="button" class="pw-eye" onclick="togglePw()" title="Mostrar/ocultar">&#128065;</button>
      </div>
    </div>

    <button class="btn-login" id="btn" type="submit">
      <span class="spinner" id="spin"></span>
      <span id="btn-txt">Entrar</span>
    </button>
  </form>

  <div class="msg" id="msg"></div>
</div>

<div class="footer"><%= Server.HtmlEncode(g_EmpNombre) %> &mdash; Acceso restringido a usuarios autorizados</div>

<script>
function togglePw() {
  const inp = document.getElementById('clave');
  inp.type  = inp.type === 'password' ? 'text' : 'password';
}

function showMsg(text, type) {
  const el  = document.getElementById('msg');
  el.textContent   = text;
  el.className     = 'msg ' + type;
  el.style.display = 'block';
}

function setLoading(on) {
  const btn  = document.getElementById('btn');
  const spin = document.getElementById('spin');
  const txt  = document.getElementById('btn-txt');
  btn.disabled       = on;
  spin.style.display = on ? 'block' : 'none';
  txt.textContent    = on ? 'Verificando...' : 'Entrar';
}

async function doLogin(e) {
  e.preventDefault();
  const usuario = document.getElementById('usuario').value.trim();
  const clave   = document.getElementById('clave').value;
  if (!usuario || !clave) return;

  setLoading(true);
  document.getElementById('msg').style.display = 'none';

  try {
    const resp = await fetch('login.asp', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ usuario, clave })
    });
    const json = await resp.json();

    if (json.ok) {
      showMsg('Acceso correcto. Redirigiendo...', 'ok');
      setTimeout(() => { location.href = 'dashboard.asp'; }, 500);
    } else {
      showMsg(json.error || 'Error al iniciar sesion', 'err');
      setLoading(false);
      document.getElementById('clave').focus();
    }
  } catch (err) {
    showMsg('No se pudo conectar con el servidor', 'err');
    setLoading(false);
  }
}
</script>
</body>
</html>
