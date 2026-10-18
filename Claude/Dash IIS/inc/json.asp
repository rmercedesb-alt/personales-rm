<%
' ── Helpers JSON para Classic ASP ─────────────────────────────────────────────

' Escapa un string para incluirlo dentro de JSON (con comillas externas)
Function JsonStr(s)
    If IsNull(s) Or IsEmpty(s) Then
        JsonStr = "null"
        Exit Function
    End If
    Dim v
    v = CStr(s)
    v = Replace(v, "\",  "\\")
    v = Replace(v, """", "\""")
    v = Replace(v, Chr(13), "\r")
    v = Replace(v, Chr(10), "\n")
    v = Replace(v, Chr(9),  "\t")
    JsonStr = """" & v & """"
End Function

' Convierte cualquier valor a su representacion JSON
' (null, numero, booleano o string escapado)
Function JsonVal(v)
    If IsNull(v) Or IsEmpty(v) Then
        JsonVal = "null"
    ElseIf VarType(v) = vbBoolean Then
        If v Then JsonVal = "true" Else JsonVal = "false"
    ElseIf IsNumeric(v) Then
        JsonVal = CStr(v)
    ElseIf IsDate(v) Then
        JsonVal = FmtDateJson(v)
    Else
        JsonVal = JsonStr(CStr(v))
    End If
End Function

' Formatea un datetime como ISO-8601 entre comillas ("2024-01-15T08:30:00")
' o "null" si es nulo/vacio
Function FmtDateJson(dt)
    If IsNull(dt) Or IsEmpty(dt) Or dt = "" Then
        FmtDateJson = "null"
        Exit Function
    End If
    Dim y, mo, d, h, mi, s
    y  = Year(dt)
    mo = Right("0" & Month(dt),  2)
    d  = Right("0" & Day(dt),    2)
    h  = Right("0" & Hour(dt),   2)
    mi = Right("0" & Minute(dt), 2)
    s  = Right("0" & Second(dt), 2)
    FmtDateJson = """" & y & "-" & mo & "-" & d & "T" & h & ":" & mi & ":" & s & """"
End Function

' Lee el cuerpo POST como texto UTF-8 (para peticiones JSON/fetch)
Function ReadRequestBody()
    Dim bytes, stream
    bytes = Request.BinaryRead(Request.TotalBytes)
    If Not IsEmpty(bytes) And LenB(bytes) > 0 Then
        Set stream = Server.CreateObject("ADODB.Stream")
        stream.Type    = 1 ' adTypeBinary
        stream.Open
        stream.Write bytes
        stream.Position = 0
        stream.Type    = 2 ' adTypeText
        stream.CharSet = "utf-8"
        ReadRequestBody = stream.ReadText
        stream.Close
        Set stream = Nothing
    Else
        ReadRequestBody = ""
    End If
End Function

' Extrae un campo string de un JSON simple {"campo":"valor"}
Function JsonField(json, campo)
    Dim pat, pos, endPos
    pat = """" & campo & """:"""
    pos = InStr(1, json, pat, 1)
    If pos = 0 Then
        JsonField = ""
        Exit Function
    End If
    pos    = pos + Len(pat)
    endPos = InStr(pos, json, """")
    If endPos = 0 Then
        JsonField = ""
        Exit Function
    End If
    ' Desescapar \n \r \t \\  \"
    Dim v
    v = Mid(json, pos, endPos - pos)
    v = Replace(v, "\""", """")
    v = Replace(v, "\\",  "\")
    v = Replace(v, "\n",  Chr(10))
    v = Replace(v, "\r",  Chr(13))
    v = Replace(v, "\t",  Chr(9))
    JsonField = v
End Function
%>
