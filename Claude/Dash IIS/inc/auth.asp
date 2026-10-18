<%
' ── Autenticacion por sesion ───────────────────────────────────────────────────

' Redirige a login si no hay sesion activa (para paginas HTML)
Sub RequireLogin()
    If Session("user_login") = "" Then
        Response.Redirect "login.asp"
        Response.End
    End If
End Sub

' Devuelve JSON 401 si no hay sesion activa (para endpoints API)
Sub RequireLoginAPI()
    If Session("user_login") = "" Then
        Response.Clear
        Response.ContentType = "application/json"
        Response.Status       = "401 Unauthorized"
        Response.Write "{""ok"":false,""error"":""Sesion expirada""}"
        Response.End
    End If
End Sub
%>
