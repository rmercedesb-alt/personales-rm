<%@ Language="VBScript" CodePage=65001 %>
<!--#include file="../inc/config.asp"-->
<!--#include file="../inc/auth.asp"-->
<!--#include file="../inc/json.asp"-->
<%
Option Explicit
Response.Buffer      = True
Response.ContentType = "application/json"
Response.CharSet     = "utf-8"

RequireLoginAPI()

' ── Validar y normalizar fecha ────────────────────────────────────────────────
Dim fechaParam, fechaDate, hoy, minDate, maxDate, fechaStr
fechaParam = Trim(Request.QueryString("fecha"))

On Error Resume Next
fechaDate = CDate(fechaParam)
If Err.Number <> 0 Or fechaParam = "" Then
    fechaDate = Date()
End If
Err.Clear
On Error GoTo 0

hoy     = Date()
minDate = DateAdd("d", -30, hoy)
maxDate = hoy

If fechaDate < minDate Then fechaDate = minDate
If fechaDate > maxDate Then fechaDate = maxDate

fechaStr = Format(Year(fechaDate), "0000") & "-" & _
           Right("0" & Month(fechaDate), 2) & "-" & _
           Right("0" & Day(fechaDate), 2)

' ── Consulta principal ────────────────────────────────────────────────────────
Dim SQL
SQL = "WITH Reps AS (" & _
"    SELECT DISTINCT" & _
"           v.RepCodigo," & _
"           r.RepNombre," & _
"           r.RepSupervisor," & _
"           ISNULL(sup.RepNombre, r.RepSupervisor) AS SupervisorNombre" & _
"    FROM   Visitas v" & _
"    INNER  JOIN Representantes r   ON r.RepCodigo  = v.RepCodigo" & _
"    LEFT   JOIN Representantes sup ON sup.RepCodigo = r.RepSupervisor" & _
"    WHERE  v.VisFechaEntrada >= DATEADD(DAY, -30, GETDATE())" & _
"      AND  r.RepEstatus = 1" & _
")," & _
"DiaFirst AS (" & _
"    SELECT RepCodigo, MIN(VisFechaEntrada) AS PrimeraEntrada" & _
"    FROM   Visitas" & _
"    WHERE  CAST(VisFechaEntrada AS DATE) = ?" & _
"    GROUP  BY RepCodigo" & _
")," & _
"DiaRanked AS (" & _
"    SELECT RepCodigo," & _
"           VisFechaEntrada AS UltimaEntrada," & _
"           VisFechaSalida  AS UltimaSalida," & _
"           DATEDIFF(SECOND, VisFechaEntrada," & _
"               ISNULL(VisFechaSalida, GETDATE())) AS DuracionSeg," & _
"           ROW_NUMBER() OVER (" & _
"               PARTITION BY RepCodigo ORDER BY VisFechaEntrada DESC" & _
"           ) AS rn" & _
"    FROM   Visitas" & _
"    WHERE  CAST(VisFechaEntrada AS DATE) = ?" & _
")," & _
"PrimerPedido AS (" & _
"    SELECT RepCodigo, MIN(VisFechaEntrada) AS PrimerPedidoHora" & _
"    FROM   Visitas" & _
"    WHERE  CAST(VisFechaEntrada AS DATE) = ?" & _
"      AND  VisEstatus = 2" & _
"    GROUP  BY RepCodigo" & _
")" & _
"SELECT r.RepCodigo," & _
"       r.RepNombre," & _
"       r.RepSupervisor," & _
"       r.SupervisorNombre," & _
"       df.PrimeraEntrada," & _
"       dr.UltimaEntrada," & _
"       dr.UltimaSalida," & _
"       dr.DuracionSeg," & _
"       pp.PrimerPedidoHora" & _
" FROM  Reps r" & _
" LEFT  JOIN DiaFirst     df ON df.RepCodigo = r.RepCodigo" & _
" LEFT  JOIN DiaRanked    dr ON dr.RepCodigo = r.RepCodigo AND dr.rn = 1" & _
" LEFT  JOIN PrimerPedido pp ON pp.RepCodigo = r.RepCodigo" & _
" ORDER BY r.RepNombre;"

' ── Ejecutar consulta ─────────────────────────────────────────────────────────
On Error Resume Next

Dim cn, cmd, rs
Set cn = GetConn()

If Err.Number <> 0 Then
    Response.Write "{""ok"":false,""error"":" & JsonStr("Error de conexion: " & Err.Description) & "}"
    Response.End
End If

Err.Clear

Set cmd = Server.CreateObject("ADODB.Command")
Set cmd.ActiveConnection = cn
cmd.CommandType    = 1 ' adCmdText
cmd.CommandTimeout = 30
cmd.CommandText    = SQL

' Mismo valor de fecha 3 veces (para DiaFirst, DiaRanked, PrimerPedido)
' adVarChar=200, adParamInput=1
cmd.Parameters.Append cmd.CreateParameter("p_fecha1", 200, 1, 10, fechaStr)
cmd.Parameters.Append cmd.CreateParameter("p_fecha2", 200, 1, 10, fechaStr)
cmd.Parameters.Append cmd.CreateParameter("p_fecha3", 200, 1, 10, fechaStr)

Set rs = cmd.Execute()

If Err.Number <> 0 Then
    Response.Write "{""ok"":false,""error"":" & JsonStr("Error de consulta: " & Err.Description) & "}"
    cn.Close : Response.End
End If

On Error GoTo 0

' ── Construir JSON ────────────────────────────────────────────────────────────
Dim rows, sep
rows = ""
sep  = ""

Do While Not rs.EOF
    Dim repCodigo, repNombre, repSupervisor, supNombre
    Dim primeraEntrada, ultimaEntrada, ultimaSalida, duracionSeg, primerPedidoHora

    repCodigo       = rs("RepCodigo")
    repNombre       = rs("RepNombre")
    repSupervisor   = rs("RepSupervisor")
    supNombre       = rs("SupervisorNombre")
    primeraEntrada  = rs("PrimeraEntrada")
    ultimaEntrada   = rs("UltimaEntrada")
    ultimaSalida    = rs("UltimaSalida")
    duracionSeg     = rs("DuracionSeg")
    primerPedidoHora= rs("PrimerPedidoHora")

    Dim durVal
    If IsNull(duracionSeg) Then
        durVal = "null"
    Else
        durVal = CStr(CLng(duracionSeg))
    End If

    rows = rows & sep & "{" & _
        """RepCodigo"":" & JsonStr(repCodigo) & "," & _
        """RepNombre"":" & JsonStr(repNombre) & "," & _
        """RepSupervisor"":" & JsonStr(repSupervisor) & "," & _
        """SupervisorNombre"":" & JsonStr(supNombre) & "," & _
        """PrimeraEntrada"":" & FmtDateJson(primeraEntrada) & "," & _
        """UltimaEntrada"":" & FmtDateJson(ultimaEntrada) & "," & _
        """UltimaSalida"":" & FmtDateJson(ultimaSalida) & "," & _
        """DuracionSeg"":" & durVal & "," & _
        """PrimerPedidoHora"":" & FmtDateJson(primerPedidoHora) & _
        "}"
    sep = ","

    rs.MoveNext
Loop

rs.Close
cn.Close

Response.Write "{""ok"":true," & _
               """fecha"":" & JsonStr(fechaStr) & "," & _
               """usuario"":" & JsonStr(Session("user_nombre")) & "," & _
               """reps"":[" & rows & "]}"
%>
