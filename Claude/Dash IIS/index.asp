<%@ Language="VBScript" CodePage=65001 %>
<%
' Pagina de entrada: redirige al dashboard si hay sesion activa,
' o al login si no.
Response.Buffer = True

If Session("user_login") <> "" Then
    Response.Redirect "dashboard.asp"
Else
    Response.Redirect "login.asp"
End If
Response.End
%>
