<%@ Language="VBScript" CodePage=65001 %>
<!--#include file="../inc/config.asp"-->
<%
' Pagina temporal de diagnostico — eliminar despues de verificar
Response.Buffer      = True
Response.ContentType = "text/plain"
Response.CharSet     = "utf-8"

On Error Resume Next
Dim cn, rs
Set cn = GetConn()
If Err.Number <> 0 Then
    Response.Write "ERROR conexion: " & Err.Description
    Response.End
End If

Set rs = cn.Execute("SELECT TOP 1 * FROM Empresa")
If Err.Number <> 0 Then
    Response.Write "ERROR consulta: " & Err.Description
    cn.Close : Response.End
End If

Dim i, f
Response.Write "Columnas de la tabla Empresa:" & Chr(13) & Chr(10)
For i = 0 To rs.Fields.Count - 1
    Set f = rs.Fields(i)
    Response.Write "  [" & f.Name & "]  tipo=" & f.Type & "  valor=" & Left("" & f.Value, 60) & Chr(13) & Chr(10)
Next

rs.Close
cn.Close
%>
