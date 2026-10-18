<%@ Language="VBScript" CodePage=65001 %>
<!--#include file="../inc/auth.asp"-->
<!--#include file="../inc/json.asp"-->
<%
Response.Buffer      = True
Response.ContentType = "application/json"
Response.CharSet     = "utf-8"

RequireLoginAPI()

Response.Write "{""ok"":true," & _
               """login"":" & JsonStr(Session("user_login")) & "," & _
               """nombre"":" & JsonStr(Session("user_nombre")) & "}"
%>
