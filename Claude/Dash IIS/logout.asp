<%@ Language="VBScript" CodePage=65001 %>
<%
Response.Buffer = True
Session.Abandon
Response.Redirect "login.asp"
Response.End
%>
