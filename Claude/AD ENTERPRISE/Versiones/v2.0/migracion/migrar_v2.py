"""
MIGRACIÓN v1.1 → v2.0
======================
1. Lee TODOS los datos de la spreadsheet v1.1
2. Crea una nueva spreadsheet  "AD Enterprise.Fanny Mercedes & Asoc. V2.0"
3. Copia los datos a las hojas existentes (FACTURAS, PAGOS, COMPANIAS, CLIENTES, USUARIOS)
4. Agrega las columnas nuevas a FACTURAS y PAGOS
5. Crea las hojas nuevas: CAJA, CUADRE_CAJA, EGRESOS, INGRESOS_CAPITAL
6. Inserta un CUADRE_CAJA de migración (SeqCuadre CUADRE-MIG-0001, estado CERRADA)
7. Aplica formato profesional a todas las hojas
8. Imprime el nuevo SpreadsheetId para ponerlo en appsettings.json de v2.0
"""

import json, datetime, os, sys
from google.oauth2.service_account import Credentials
from googleapiclient.discovery import build

# ── Config ────────────────────────────────────────────────────────────────────
CRED_FILE       = r"C:\Personales RM\Claude\AD ENTERPRISE\Versiones\v1.1\Local-Run\ADEnterprise\credentials.json"
OLD_SHEET_ID    = "16A-Xgwjd8-yYgWCtFz8dcAUJLdwkvs_yYlwJlzbTphM"
NEW_SHEET_NAME  = "AD Enterprise.Fanny Mercedes & Asoc. V2.0"
HOY             = datetime.date.today().strftime("%Y%m%d")
SEQ_MIG         = f"CUADRE-{HOY}-MIG1"

SCOPES = [
    "https://www.googleapis.com/auth/spreadsheets",
    "https://www.googleapis.com/auth/drive",
]

# ── Autenticacion ─────────────────────────────────────────────────────────────
creds   = Credentials.from_service_account_file(CRED_FILE, scopes=SCOPES)
sheets  = build("sheets", "v4", credentials=creds).spreadsheets()
drive   = build("drive",  "v3", credentials=creds)

print("✅ Autenticado con Google")

# ── Helpers ───────────────────────────────────────────────────────────────────
AZUL_MARINO = {"red": 0.102, "green": 0.227, "blue": 0.365}
VERDE       = {"red": 0.098, "green": 0.529, "blue": 0.329}
GRIS_CLARO  = {"red": 0.93,  "green": 0.95,  "blue": 1.0}

def letra_col(n):  # 0-based → A, B, ... Z, AA...
    s = ""
    n += 1
    while n:
        n, r = divmod(n - 1, 26)
        s = chr(65 + r) + s
    return s

def rango_a1(hoja, fila, col_ini, col_fin):
    return f"'{hoja}'!{letra_col(col_ini)}{fila}:{letra_col(col_fin)}{fila}"

def fmt_encabezado(sheet_id, n_cols, color):
    return {
        "repeatCell": {
            "range": {
                "sheetId": sheet_id,
                "startRowIndex": 0, "endRowIndex": 1,
                "startColumnIndex": 0, "endColumnIndex": n_cols
            },
            "cell": {
                "userEnteredFormat": {
                    "backgroundColor": color,
                    "textFormat": {"bold": True, "foregroundColor": {"red":1,"green":1,"blue":1}},
                    "horizontalAlignment": "CENTER",
                }
            },
            "fields": "userEnteredFormat(backgroundColor,textFormat,horizontalAlignment)"
        }
    }

def fmt_freeze(sheet_id):
    return {
        "updateSheetProperties": {
            "properties": {"sheetId": sheet_id, "gridProperties": {"frozenRowCount": 1}},
            "fields": "gridProperties.frozenRowCount"
        }
    }

def fmt_banding(sheet_id, n_cols):
    return {
        "addBanding": {
            "bandedRange": {
                "range": {
                    "sheetId": sheet_id,
                    "startRowIndex": 1, "endRowIndex": 2000,
                    "startColumnIndex": 0, "endColumnIndex": n_cols
                },
                "rowProperties": {
                    "headerColor": AZUL_MARINO,
                    "firstBandColor": {"red":1,"green":1,"blue":1},
                    "secondBandColor": GRIS_CLARO,
                }
            }
        }
    }

def fmt_filtro(sheet_id, n_cols):
    return {
        "setBasicFilter": {
            "filter": {
                "range": {
                    "sheetId": sheet_id,
                    "startRowIndex": 0, "endRowIndex": 1,
                    "startColumnIndex": 0, "endColumnIndex": n_cols
                }
            }
        }
    }

def aplicar_formato(new_id, sheet_id, n_cols, color=None):
    c = color or AZUL_MARINO
    reqs = [
        fmt_encabezado(sheet_id, n_cols, c),
        fmt_freeze(sheet_id),
        fmt_banding(sheet_id, n_cols),
        fmt_filtro(sheet_id, n_cols),
    ]
    sheets.batchUpdate(spreadsheetId=new_id, body={"requests": reqs}).execute()

# ── 1. Leer datos v1.1 ────────────────────────────────────────────────────────
print("\n📖 Leyendo datos de v1.1...")

def leer(hoja, rango="A:Z"):
    r = sheets.values().get(spreadsheetId=OLD_SHEET_ID, range=f"{hoja}!{rango}").execute()
    return r.get("values", [])

data_facturas  = leer("FACTURAS")
data_pagos     = leer("PAGOS")
data_companias = leer("COMPANIAS")
data_clientes  = leer("CLIENTES")
data_usuarios  = leer("USUARIOS")

print(f"  FACTURAS:  {len(data_facturas)-1  if data_facturas  else 0} filas")
print(f"  PAGOS:     {len(data_pagos)-1     if data_pagos     else 0} filas")
print(f"  COMPANIAS: {len(data_companias)-1 if data_companias else 0} filas")
print(f"  CLIENTES:  {len(data_clientes)-1  if data_clientes  else 0} filas")
print(f"  USUARIOS:  {len(data_usuarios)-1  if data_usuarios  else 0} filas")

# ── 2. Crear nueva spreadsheet (copia de la original via Drive) ───────────────
print(f"\nCreando nueva spreadsheet: '{NEW_SHEET_NAME}'...")

try:
    # Intentar copiar el archivo existente (mas rapido, preserva todo)
    copy_resp = drive.files().copy(
        fileId=OLD_SHEET_ID,
        body={"name": NEW_SHEET_NAME}
    ).execute()
    NEW_ID = copy_resp["id"]
    print(f"  Copia creada via Drive. SpreadsheetId: {NEW_ID}")
    MODO = "COPIA"
except Exception as e:
    print(f"  Drive.copy no disponible ({e}). Creando via Sheets API...")
    try:
        body = {
            "properties": {"title": NEW_SHEET_NAME},
            "sheets": [
                {"properties": {"title": h}} for h in
                ["FACTURAS","PAGOS","COMPANIAS","CLIENTES","USUARIOS",
                 "CAJA","CUADRE_CAJA","EGRESOS","INGRESOS_CAPITAL"]
            ]
        }
        new_ss = sheets.create(body=body).execute()
        NEW_ID = new_ss["spreadsheetId"]
        print(f"  Spreadsheet nueva creada. SpreadsheetId: {NEW_ID}")
        MODO = "NUEVA"
    except Exception as e2:
        print(f"\n  ERROR: No se pudo crear la spreadsheet automaticamente.")
        print(f"  {e2}")
        print("""
  SOLUCION MANUAL:
  1. Ve a https://drive.google.com
  2. Busca el archivo 'AD Enterprise.Fanny Mercedes & Asoc.'
  3. Clic derecho → Hacer una copia
  4. Renombra la copia como: 'AD Enterprise.Fanny Mercedes & Asoc. V2.0'
  5. Abre la copia y copia el ID de la URL (entre /d/ y /edit)
  6. Ejecuta este script con: python migrar_v2.py <NUEVO_ID>
        """)
        if len(sys.argv) > 1:
            NEW_ID = sys.argv[1]
            print(f"  Usando SpreadsheetId manual: {NEW_ID}")
            MODO = "MANUAL"
        else:
            sys.exit(1)

# Obtener sheet_ids de la spreadsheet nueva/copiada
meta = sheets.get(spreadsheetId=NEW_ID).execute()
sheet_ids = {s["properties"]["title"]: s["properties"]["sheetId"]
             for s in meta["sheets"]}
print(f"  Hojas encontradas: {list(sheet_ids.keys())}")

# ── 3. Definir encabezados v2.0 ───────────────────────────────────────────────

HDR_FACTURAS = [
    "NumTransaccion","NumTicket","EmpresaNombre","ClienteNombre",
    "Monto","FechaFactura","Estado","PagadoALaFecha","Notas",
    "ValorAnterior","Origen","FechaMigracion","UsuarioMigracion",
    "FechaModificacion","UsuarioModificacion","IdCompania","IdCliente",
    "MontoPagado",   # NUEVO v2.0 — neto al cliente (factoring)
    "SeqCuadre",     # NUEVO v2.0 — FK → CUADRE_CAJA
    "FechaCreacion", # NUEVO v2.0 — timestamp de registro
]

HDR_PAGOS = [
    "NumTransaccionPago","NumTransaccionFactura","NumTicket",
    "FechaPago","MontoPagado","FormaPago","NumChequeReferencia",
    "Notas","UsuarioRegistro","FechaRegistro","FechaModificacion",
    "UsuarioModificacion","IdCompania","IdCliente","NumRecibo",
    "MontoInstrumento","EmpresaNombre","ClienteNombre",
    "SeqCuadre",     # NUEVO v2.0
    "FechaCreacion", # NUEVO v2.0
]

HDR_COMPANIAS = [
    "ID","Nombre","RNC","Telefono","Direccion","Correo",
    "CondicionPagoDias","Activo","FechaCreacion",
]

HDR_CLIENTES = [
    "ID","NombreCompleto","Cedula","Telefono","Correo","Activo","FechaCreacion",
]

HDR_USUARIOS = [
    "ID","NombreCompleto","NombreUsuario","PasswordHash","Correo",
    "Perfil","Estado","FechaCreacion",
]

HDR_CAJA = [
    "SeqMovCaja","SeqCuadre","TipoMov","Categoria","RefDocumento",
    "Concepto","Monto","FechaMovimiento","EmpresaNombre","ClienteNombre",
    "NumTicket","UsuarioRegistro","FechaCreacion",
]

HDR_CUADRE = [
    "SeqCuadre","Estado","FechaApertura","FechaCierre",
    "UsuarioApertura","UsuarioCierre","BalanceInicial","BalanceFinal",
    "CantFacturas","MontoFacturado","MontoPagadoFacturas",
    "CantCobros","MontoCobros","Notas","FechaCreacion",
]

HDR_EGRESOS = [
    "SeqEgreso","SeqCuadre","TipoEgreso","ANombreDe","Fecha",
    "Monto","Nota","UsuarioRegistro","FechaCreacion","SeqMovCaja",
]

HDR_INGRESOS = [
    "Secuencia","SeqCuadre","Nombre","Fecha","Monto",
    "Observaciones","UsuarioRegistro","FechaCreacion","SeqMovCaja",
]

# ── 4. Escribir encabezados + datos ───────────────────────────────────────────
print("\n📝 Escribiendo datos en nueva spreadsheet...")

def normalizar(rows_old, hdr_old, hdr_new, extra_cols_default=None):
    """
    Ajusta las filas de datos de hdr_old a hdr_new.
    Columnas nuevas se rellenan con extra_cols_default o "".
    """
    result = [hdr_new]  # fila de encabezado
    old_set = set(hdr_old)
    for row in rows_old[1:]:  # saltar encabezado original
        new_row = []
        for col in hdr_new:
            if col in old_set:
                idx = hdr_old.index(col)
                new_row.append(row[idx] if idx < len(row) else "")
            else:
                # columna nueva
                default = (extra_cols_default or {}).get(col, "")
                new_row.append(default)
        result.append(new_row)
    return result

# Encabezados originales (primera fila de cada hoja)
hdr_orig_facturas  = list(data_facturas[0])  if data_facturas  else HDR_FACTURAS
hdr_orig_pagos     = list(data_pagos[0])     if data_pagos     else HDR_PAGOS
hdr_orig_companias = list(data_companias[0]) if data_companias else HDR_COMPANIAS
hdr_orig_clientes  = list(data_clientes[0])  if data_clientes  else HDR_CLIENTES
hdr_orig_usuarios  = list(data_usuarios[0])  if data_usuarios  else HDR_USUARIOS

hoy_iso = datetime.datetime.now().isoformat()

filas_facturas  = normalizar(data_facturas,  hdr_orig_facturas,  HDR_FACTURAS,
                              {"MontoPagado":"", "SeqCuadre": SEQ_MIG, "FechaCreacion": hoy_iso})
filas_pagos     = normalizar(data_pagos,     hdr_orig_pagos,     HDR_PAGOS,
                              {"SeqCuadre": SEQ_MIG, "FechaCreacion": hoy_iso})
filas_companias = normalizar(data_companias, hdr_orig_companias, HDR_COMPANIAS,
                              {"FechaCreacion": hoy_iso})
filas_clientes  = normalizar(data_clientes,  hdr_orig_clientes,  HDR_CLIENTES,
                              {"FechaCreacion": hoy_iso})
filas_usuarios  = normalizar(data_usuarios,  hdr_orig_usuarios,  HDR_USUARIOS,
                              {"FechaCreacion": hoy_iso})

# Fila del cuadre de migración
fila_cuadre_mig = [
    SEQ_MIG,       # SeqCuadre
    "CERRADA",     # Estado
    hoy_iso,       # FechaApertura
    hoy_iso,       # FechaCierre
    "SISTEMA",     # UsuarioApertura
    "SISTEMA",     # UsuarioCierre
    "0",           # BalanceInicial
    "0",           # BalanceFinal
    str(max(0, len(filas_facturas)-1)),  # CantFacturas
    "",            # MontoFacturado (calculado después)
    "",            # MontoPagadoFacturas
    str(max(0, len(filas_pagos)-1)),     # CantCobros
    "",            # MontoCobros
    "Migración automática desde v1.1",  # Notas
    hoy_iso,       # FechaCreacion
]

filas_cuadre = [HDR_CUADRE, fila_cuadre_mig]

def escribir(hoja, filas):
    if not filas:
        return
    body = {"values": filas}
    sheets.values().update(
        spreadsheetId=NEW_ID,
        range=f"{hoja}!A1",
        valueInputOption="RAW",
        body=body
    ).execute()
    print(f"  ✅ {hoja}: {len(filas)-1} filas de datos")

escribir("FACTURAS",         filas_facturas)
escribir("PAGOS",            filas_pagos)
escribir("COMPANIAS",        filas_companias)
escribir("CLIENTES",         filas_clientes)
escribir("USUARIOS",         filas_usuarios)
escribir("CAJA",             [HDR_CAJA])       # vacía — se llenará en operaciones
escribir("CUADRE_CAJA",      filas_cuadre)
escribir("EGRESOS",          [HDR_EGRESOS])    # vacía
escribir("INGRESOS_CAPITAL", [HDR_INGRESOS])   # vacía

# ── 5. Aplicar formato a todas las hojas ──────────────────────────────────────
print("\n🎨 Aplicando formato profesional...")

formatos = [
    ("FACTURAS",         len(HDR_FACTURAS),  AZUL_MARINO),
    ("PAGOS",            len(HDR_PAGOS),     AZUL_MARINO),
    ("COMPANIAS",        len(HDR_COMPANIAS), AZUL_MARINO),
    ("CLIENTES",         len(HDR_CLIENTES),  AZUL_MARINO),
    ("USUARIOS",         len(HDR_USUARIOS),  AZUL_MARINO),
    ("CAJA",             len(HDR_CAJA),      VERDE),
    ("CUADRE_CAJA",      len(HDR_CUADRE),    VERDE),
    ("EGRESOS",          len(HDR_EGRESOS),   VERDE),
    ("INGRESOS_CAPITAL", len(HDR_INGRESOS),  VERDE),
]

for hoja, ncols, color in formatos:
    sid = sheet_ids.get(hoja)
    if sid is not None:
        aplicar_formato(NEW_ID, sid, ncols, color)
        print(f"  ✅ Formato aplicado: {hoja}")

# ── 6. Guardar resultado ───────────────────────────────────────────────────────
resultado = {
    "new_spreadsheet_id": NEW_ID,
    "new_spreadsheet_name": NEW_SHEET_NAME,
    "url": f"https://docs.google.com/spreadsheets/d/{NEW_ID}",
    "seq_cuadre_migracion": SEQ_MIG,
    "fecha_migracion": hoy_iso,
    "filas_migradas": {
        "FACTURAS":  len(filas_facturas)-1,
        "PAGOS":     len(filas_pagos)-1,
        "COMPANIAS": len(filas_companias)-1,
        "CLIENTES":  len(filas_clientes)-1,
        "USUARIOS":  len(filas_usuarios)-1,
    }
}

out_file = r"C:\Personales RM\Claude\AD ENTERPRISE\Versiones\v2.0\migracion\resultado_migracion.json"
os.makedirs(os.path.dirname(out_file), exist_ok=True)
with open(out_file, "w") as f:
    json.dump(resultado, f, indent=2)

print(f"""
{'='*60}
✅ MIGRACIÓN COMPLETADA
{'='*60}
Nueva SpreadsheetId : {NEW_ID}
URL                 : https://docs.google.com/spreadsheets/d/{NEW_ID}
Cuadre migración    : {SEQ_MIG}
Resultado guardado  : {out_file}
{'='*60}
Pega este SpreadsheetId en appsettings.json de v2.0
""")
