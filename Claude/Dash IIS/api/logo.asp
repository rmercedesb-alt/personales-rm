<%@ Language="VBScript" CodePage=65001 %>
<!--#include file="../inc/config.asp"-->
<%
' Sirve el logo binario de la tabla Empresa con el Content-Type correcto.
' Si no hay logo o hay error, devuelve 404.
Response.Buffer = True

On Error Resume Next

Dim cn, rs, logoData
Set cn = GetConn()

If Err.Number <> 0 Then
    Response.Status = "503 Service Unavailable"
    Response.End
End If

' Intentar leer el logo con varios nombres de columna
Dim logoCol
For Each logoCol In Array("EmpLogo", "EmpImagen", "EmpFoto", "Logo")
    Set rs = cn.Execute("SELECT TOP 1 " & logoCol & " FROM Empresa")
    If Err.Number = 0 Then Exit For
    Err.Clear
Next

If Err.Number <> 0 Or rs Is Nothing Then
    cn.Close
    Response.Status = "404 Not Found"
    Response.End
End If

If rs.EOF Then
    rs.Close : cn.Close
    Response.Status = "404 Not Found"
    Response.End
End If

logoData = rs.Fields(0).Value
rs.Close
cn.Close

If IsNull(logoData) Or LenB(logoData) < 4 Then
    Response.Status = "404 Not Found"
    Response.End
End If

On Error GoTo 0

' ── Detectar tipo de imagen por magic bytes ───────────────────────────────────
Dim b1, b2, b3, b4, contentType
b1 = AscB(MidB(logoData, 1, 1))
b2 = AscB(MidB(logoData, 2, 1))
b3 = AscB(MidB(logoData, 3, 1))
b4 = AscB(MidB(logoData, 4, 1))

If b1 = &HFF And b2 = &HD8 Then
    contentType = "image/jpeg"
ElseIf b1 = &H89 And b2 = &H50 And b3 = &H4E And b4 = &H47 Then
    contentType = "image/png"
ElseIf b1 = &H47 And b2 = &H49 And b3 = &H46 Then
    contentType = "image/gif"
ElseIf b1 = &H42 And b2 = &H4D Then
    contentType = "image/bmp"
Else
    contentType = "image/jpeg"  ' asumir JPEG si no se reconoce
End If

' ── Servir la imagen ──────────────────────────────────────────────────────────
Response.ContentType = contentType
Response.AddHeader  "Cache-Control", "public, max-age=86400"  ' cache 1 dia
Response.BinaryWrite logoData
%>
