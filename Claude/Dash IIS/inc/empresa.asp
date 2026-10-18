<%
' ── Carga nombre y logo de la tabla Empresa ────────────────────────────────────
' Variables globales disponibles despues de incluir este archivo:
'   g_EmpNombre   (String)  — nombre de la empresa
'   g_EmpHasLogo  (Boolean) — True si hay logo binario en la BD
'
' El logo se sirve via:  api/logo.asp
' ─────────────────────────────────────────────────────────────────────────────

Dim g_EmpNombre, g_EmpHasLogo
g_EmpNombre  = ""
g_EmpHasLogo = False

On Error Resume Next

Dim cnEmp, rsEmp
Set cnEmp = GetConn()

If Err.Number = 0 Then
    Set rsEmp = cnEmp.Execute("SELECT TOP 1 * FROM Empresa")

    If Err.Number = 0 And Not rsEmp.EOF Then

        ' ── Nombre de empresa: intentar varios nombres de columna comunes ──
        Dim tmpN
        tmpN = rsEmp("EmpNombre")
        If Err.Number <> 0 Then Err.Clear : tmpN = rsEmp("EmpRazonSocial")
        If Err.Number <> 0 Then Err.Clear : tmpN = rsEmp("EmpDescripcion")
        If Err.Number <> 0 Then Err.Clear : tmpN = rsEmp("EmpNombreComercial")
        If Err.Number <> 0 Then Err.Clear : tmpN = rsEmp("Nombre")
        If Err.Number = 0 And Not IsNull(tmpN) And Trim(CStr(tmpN)) <> "" Then
            g_EmpNombre = Trim(CStr(tmpN))
        End If
        Err.Clear

        ' ── Logo: verificar si existe columna con datos binarios ──────────
        Dim tmpL
        tmpL = rsEmp("EmpLogo")
        If Err.Number <> 0 Then Err.Clear : tmpL = rsEmp("EmpImagen")
        If Err.Number <> 0 Then Err.Clear : tmpL = rsEmp("EmpFoto")
        If Err.Number <> 0 Then Err.Clear : tmpL = rsEmp("Logo")
        If Err.Number = 0 And Not IsNull(tmpL) And LenB(tmpL) > 4 Then
            g_EmpHasLogo = True
        End If
        Err.Clear

        rsEmp.Close
    End If
    cnEmp.Close
End If

On Error GoTo 0
%>
