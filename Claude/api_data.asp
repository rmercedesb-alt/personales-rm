<%@ Language="VBScript" CodePage="65001" %>
<%
'===========================================================================
' api_data.asp  —  API de datos para Dashboard Visitas (Classic ASP / IIS)
' Devuelve JSON compatible con dashboard.html
'===========================================================================
Response.ContentType = "application/json"
Response.CharSet     = "utf-8"
Response.Buffer      = True

'── Funciones auxiliares ───────────────────────────────────────────────────
Function JsEscape(s)
    s = Replace(s, "\",  "\\")
    s = Replace(s, """", "\""")
    s = Replace(s, Chr(13), "\r")
    s = Replace(s, Chr(10), "\n")
    s = Replace(s, Chr(9),  "\t")
    JsEscape = s
End Function

Function Pad2(n)
    Pad2 = Right("0" & CStr(n), 2)
End Function

Function FormatISO(dt)
    FormatISO = CStr(Year(dt)) & "-" & Pad2(Month(dt)) & "-" & Pad2(Day(dt)) & _
                "T" & Pad2(Hour(dt)) & ":" & Pad2(Minute(dt)) & ":" & Pad2(Second(dt))
End Function

Sub JsonError(msg)
    Response.Write "{""ok"":false,""error"":""" & JsEscape(msg) & """}"
    Response.End
End Sub

'── Leer .env ─────────────────────────────────────────────────────────────
Dim dbServer, dbPort, dbDatabase, dbUser, dbPassword, dbDriver
dbPort   = "1433"
dbDriver = "ODBC Driver 17 for SQL Server"

Dim fso : Set fso = Server.CreateObject("Scripting.FileSystemObject")
Dim envPath : envPath = Server.MapPath(".env")

If fso.FileExists(envPath) Then
    Dim tf : Set tf = fso.OpenTextFile(envPath, 1, False, 0)
    Dim ln, eq, ek, ev
    Do While Not tf.AtEndOfStream
        ln = Trim(tf.ReadLine())
        If Len(ln) > 0 And Left(ln,1) <> "#" Then
            eq = InStr(ln, "=")
            If eq > 0 Then
                ek = Trim(Left(ln, eq-1))
                ev = Trim(Mid(ln, eq+1))
                Select Case UCase(ek)
                    Case "DB_SERVER"   : dbServer   = ev
                    Case "DB_PORT"     : dbPort     = ev
                    Case "DB_DATABASE" : dbDatabase = ev
                    Case "DB_USER"     : dbUser     = ev
                    Case "DB_PASSWORD" : dbPassword = ev
                    Case "DB_DRIVER"   : dbDriver   = ev
                End Select
            End If
        End If
    Loop
    tf.Close : Set tf = Nothing
End If
Set fso = Nothing

If dbServer = "" Or dbUser = "" Then
    Call JsonError("Archivo .env no encontrado o incompleto")
End If

'── Cadena de conexion ─────────────────────────────────────────────────────
Dim connStr
connStr = "DRIVER={" & dbDriver & "};" & _
          "SERVER=" & dbServer & "," & dbPort & ";" & _
          "DATABASE=" & dbDatabase & ";" & _
          "UID=" & dbUser & ";" & _
          "PWD=" & dbPassword & ";" & _
          "TrustServerCertificate=yes;"

'── Validar y acotar fecha ─────────────────────────────────────────────────
Dim fechaParam, fechaDate, fechaStr
fechaParam = Trim(Request.QueryString("fecha"))

fechaDate = Date()
If Len(fechaParam) = 10 And IsDate(fechaParam) Then
    fechaDate = CDate(fechaParam)
End If

Dim dMax : dMax = Date()
Dim dMin : dMin = DateAdd("d", -30, dMax)
If fechaDate > dMax Then fechaDate = dMax
If fechaDate < dMin Then fechaDate = dMin

fechaStr = CStr(Year(fechaDate)) & "-" & Pad2(Month(fechaDate)) & "-" & Pad2(Day(fechaDate))

'── SQL (fecha embebida — segura porque viene de CDate validado) ───────────
Dim SQL : SQL = _
  "WITH Reps AS (" & _
  "  SELECT DISTINCT v.RepCodigo, r.RepNombre, r.RepSupervisor," & _
  "         ISNULL(sup.RepNombre, r.RepSupervisor) AS SupervisorNombre" & _
  "  FROM   Visitas v" & _
  "  INNER  JOIN Representantes r   ON r.RepCodigo  = v.RepCodigo" & _
  "  LEFT   JOIN Representantes sup ON sup.RepCodigo = r.RepSupervisor" & _
  "  WHERE  v.VisFechaEntrada >= DATEADD(DAY,-30,GETDATE()) AND r.RepEstatus=1" & _
  ")," & _
  "DiaFirst AS (" & _
  "  SELECT RepCodigo, MIN(VisFechaEntrada) AS PrimeraEntrada" & _
  "  FROM   Visitas WHERE CAST(VisFechaEntrada AS DATE)='" & fechaStr & "'" & _
  "  GROUP  BY RepCodigo" & _
  ")," & _
  "DiaRanked AS (" & _
  "  SELECT RepCodigo, VisFechaEntrada AS UltimaEntrada, VisFechaSalida AS UltimaSalida," & _
  "         DATEDIFF(SECOND,VisFechaEntrada,ISNULL(VisFechaSalida,GETDATE())) AS DuracionSeg," & _
  "         ROW_NUMBER() OVER(PARTITION BY RepCodigo ORDER BY VisFechaEntrada DESC) AS rn" & _
  "  FROM   Visitas WHERE CAST(VisFechaEntrada AS DATE)='" & fechaStr & "'" & _
  ")," & _
  "PrimerPedido AS (" & _
  "  SELECT RepCodigo, MIN(VisFechaEntrada) AS PrimerPedidoHora" & _
  "  FROM   Visitas" & _
  "  WHERE  CAST(VisFechaEntrada AS DATE)='" & fechaStr & "' AND VisEstatus=2" & _
  "  GROUP  BY RepCodigo" & _
  ")" & _
  "SELECT r.RepCodigo, r.RepNombre, r.RepSupervisor, r.SupervisorNombre," & _
  "       df.PrimeraEntrada, dr.UltimaEntrada, dr.UltimaSalida," & _
  "       dr.DuracionSeg, pp.PrimerPedidoHora" & _
  " FROM  Reps r" & _
  " LEFT  JOIN DiaFirst     df ON df.RepCodigo=r.RepCodigo" & _
  " LEFT  JOIN DiaRanked    dr ON dr.RepCodigo=r.RepCodigo AND dr.rn=1" & _
  " LEFT  JOIN PrimerPedido pp ON pp.RepCodigo=r.RepCodigo" & _
  " ORDER BY r.RepNombre;"

'── Ejecutar consulta ──────────────────────────────────────────────────────
Dim conn : Set conn = Server.CreateObject("ADODB.Connection")
Dim rs

On Error Resume Next
conn.ConnectionTimeout = 15
conn.Open connStr
If Err.Number <> 0 Then Call JsonError("Conexion BD: " & Err.Description)

Set rs = conn.Execute(SQL)
If Err.Number <> 0 Then Call JsonError("Consulta: " & Err.Description)
On Error GoTo 0

'── Serializar filas a JSON ────────────────────────────────────────────────
' Tipos ADODB que son fecha/datetime
'   7  = adDate
' 133  = adDBDate
' 135  = adDBTimeStamp
Dim json : json = "["
Dim firstRow : firstRow = True
Dim i, fv, ft, fn

Do While Not rs.EOF
    If Not firstRow Then json = json & ","
    firstRow = False
    json = json & "{"

    For i = 0 To rs.Fields.Count - 1
        fn = rs.Fields(i).Name
        fv = rs.Fields(i).Value
        ft = rs.Fields(i).Type

        json = json & """" & JsEscape(fn) & """:"

        If IsNull(fv) Then
            json = json & "null"
        ElseIf ft = 7 Or ft = 133 Or ft = 135 Then
            json = json & """" & FormatISO(CDate(fv)) & """"
        ElseIf VarType(fv) = 11 Then        ' vbBoolean
            If CBool(fv) Then json = json & "true" Else json = json & "false"
        ElseIf VarType(fv) >= 2 And VarType(fv) <= 6 Then  ' numeric
            json = json & CStr(fv)
        Else
            json = json & """" & JsEscape(CStr(fv)) & """"
        End If

        If i < rs.Fields.Count - 1 Then json = json & ","
    Next

    json = json & "}"
    rs.MoveNext
Loop

json = json & "]"

rs.Close   : Set rs   = Nothing
conn.Close : Set conn = Nothing

Response.Write "{""ok"":true,""reps"":" & json & ",""fecha"":""" & fechaStr & """}"
%>
