"""
COMPLETAR MIGRACION v2.0
========================
Los datos v1.1 ya estan en la nueva spreadsheet.
Este script:
  1. Crea las hojas nuevas (CAJA, CUADRE_CAJA, EGRESOS, INGRESOS_CAPITAL)
  2. Escribe sus encabezados y el cuadre de migracion
  3. Aplica formato profesional a TODAS las hojas
  4. Renombra la hoja "Facturas" a "FACTURAS" si aplica

Uso:
  python completar_migracion.py
"""

import sys, os, datetime
from google.oauth2.service_account import Credentials
from googleapiclient.discovery import build

# ── Config ─────────────────────────────────────────────────────────────────────
CRED_FILE = r"C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\AD ENTERPRISE\Versiones\v1.1\Local-Run\ADEnterprise\credentials.json"
NEW_ID    = "1CWJL4E1OWY0NTF-G9qjax1FZoBvUOH9DVuxHLSHgN6o"
HOY       = datetime.date.today().strftime("%Y%m%d")
HOY_ISO   = datetime.date.today().isoformat()
SEQ_MIG   = f"CUADRE-{HOY}-MIG1"

SCOPES = [
    "https://www.googleapis.com/auth/spreadsheets",
    "https://www.googleapis.com/auth/drive",
]

print("Autenticando con Google...")
creds  = Credentials.from_service_account_file(CRED_FILE, scopes=SCOPES)
sheets = build("sheets", "v4", credentials=creds).spreadsheets()
print("OK - Autenticado")

# ── Colores ────────────────────────────────────────────────────────────────────
AZUL_MARINO = {"red": 0.102, "green": 0.227, "blue": 0.365}
VERDE       = {"red": 0.098, "green": 0.529, "blue": 0.329}
BLANCO      = {"red": 1.0, "green": 1.0, "blue": 1.0}

# ── Encabezados ────────────────────────────────────────────────────────────────
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

# Encabezados v2.0 para hojas existentes (para validar/agregar cols faltantes)
HDR_FACTURAS = [
    "NumTransaccion","NumTicket","EmpresaNombre","ClienteNombre",
    "Monto","FechaFactura","Estado","PagadoALaFecha","Notas",
    "ValorAnterior","Origen","FechaMigracion","UsuarioMigracion",
    "FechaModificacion","UsuarioModificacion","IdCompania","IdCliente",
    "MontoPagado","SeqCuadre","FechaCreacion",
]
HDR_PAGOS = [
    "NumTransaccionPago","NumTransaccionFactura","NumTicket",
    "FechaPago","MontoPagado","FormaPago","NumChequeReferencia",
    "Notas","UsuarioRegistro","FechaRegistro","FechaModificacion",
    "UsuarioModificacion","IdCompania","IdCliente","NumRecibo",
    "MontoInstrumento","EmpresaNombre","ClienteNombre",
    "SeqCuadre","FechaCreacion",
]

# ── 1. Leer estado actual de la spreadsheet ────────────────────────────────────
print("\nLeyendo estructura actual...")
meta      = sheets.get(spreadsheetId=NEW_ID).execute()
sheet_map = {s["properties"]["title"]: s["properties"]["sheetId"]
             for s in meta["sheets"]}
print(f"  Hojas actuales: {list(sheet_map.keys())}")

# ── 2. Renombrar hojas si tienen nombre en minusculas (copia de Drive) ─────────
renames = []
for old_name, correct_name in [("Facturas","FACTURAS"), ("Pagos","PAGOS"),
                                 ("Companias","COMPANIAS"), ("Clientes","CLIENTES"),
                                 ("Usuarios","USUARIOS")]:
    if old_name in sheet_map and correct_name not in sheet_map:
        renames.append({
            "updateSheetProperties": {
                "properties": {"sheetId": sheet_map[old_name], "title": correct_name},
                "fields": "title"
            }
        })

if renames:
    sheets.batchUpdate(spreadsheetId=NEW_ID, body={"requests": renames}).execute()
    print(f"  Renombradas {len(renames)} hojas a MAYUSCULAS")
    # Refrescar mapa
    meta      = sheets.get(spreadsheetId=NEW_ID).execute()
    sheet_map = {s["properties"]["title"]: s["properties"]["sheetId"]
                 for s in meta["sheets"]}

# ── 3. Crear hojas nuevas que no existan ───────────────────────────────────────
hojas_nuevas = ["CAJA", "CUADRE_CAJA", "EGRESOS", "INGRESOS_CAPITAL"]
solicitudes_crear = []
for h in hojas_nuevas:
    if h not in sheet_map:
        solicitudes_crear.append({"addSheet": {"properties": {"title": h}}})

if solicitudes_crear:
    resp = sheets.batchUpdate(
        spreadsheetId=NEW_ID,
        body={"requests": solicitudes_crear}
    ).execute()
    # Actualizar mapa con los IDs nuevos
    for r in resp.get("replies", []):
        if "addSheet" in r:
            props = r["addSheet"]["properties"]
            sheet_map[props["title"]] = props["sheetId"]
    print(f"  Creadas {len(solicitudes_crear)} hojas nuevas: {hojas_nuevas}")
else:
    print("  Las hojas nuevas ya existen")

# ── 4. Asegurar encabezados v2.0 en FACTURAS y PAGOS ──────────────────────────
def asegurar_encabezado(hoja, hdr_nuevo):
    """Actualiza la fila 1 con los encabezados v2.0 completos."""
    rango = f"{hoja}!A1"
    sheets.values().update(
        spreadsheetId=NEW_ID,
        range=rango,
        valueInputOption="RAW",
        body={"values": [hdr_nuevo]}
    ).execute()

print("\nActualizando encabezados v2.0...")
asegurar_encabezado("FACTURAS", HDR_FACTURAS)
print("  OK - FACTURAS (columnas MontoPagado, SeqCuadre, FechaCreacion)")
asegurar_encabezado("PAGOS", HDR_PAGOS)
print("  OK - PAGOS (columnas SeqCuadre, FechaCreacion)")

# ── 5. Escribir encabezados + datos en hojas nuevas ───────────────────────────
def escribir(hoja, filas):
    if not filas:
        return
    sheets.values().update(
        spreadsheetId=NEW_ID,
        range=f"{hoja}!A1",
        valueInputOption="RAW",
        body={"values": filas}
    ).execute()
    n = len(filas) - 1
    print(f"  OK - {hoja}: {n} fila(s) de datos")

print("\nEscribiendo hojas nuevas...")

# Contar facturas y pagos (con reintento por latencia API)
import time

def contar_filas(hoja):
    for intento in range(4):
        try:
            resp = sheets.values().get(spreadsheetId=NEW_ID, range=f"{hoja}!A:A").execute()
            return max(0, len(resp.get("values", [])) - 1)
        except Exception as e:
            if intento < 3:
                print(f"  Reintentando lectura {hoja} ({intento+1}/3)...")
                time.sleep(4)
            else:
                print(f"  WARN: No se pudo contar {hoja}: {e}")
                return 0

# Esperar propagación de las hojas recién creadas
time.sleep(3)

cant_fact = contar_filas("FACTURAS")
cant_pago = contar_filas("PAGOS")

fila_cuadre_mig = [
    SEQ_MIG,          # SeqCuadre
    "CERRADA",        # Estado
    HOY_ISO,          # FechaApertura
    HOY_ISO,          # FechaCierre
    "SISTEMA",        # UsuarioApertura
    "SISTEMA",        # UsuarioCierre
    "0",              # BalanceInicial
    "0",              # BalanceFinal
    str(cant_fact),   # CantFacturas
    "",               # MontoFacturado
    "",               # MontoPagadoFacturas
    str(cant_pago),   # CantCobros
    "",               # MontoCobros
    f"Migracion automatica desde v1.1 - {cant_fact} facturas, {cant_pago} pagos",
    HOY_ISO,          # FechaCreacion
]

escribir("CAJA",             [HDR_CAJA])
escribir("CUADRE_CAJA",      [HDR_CUADRE, fila_cuadre_mig])
escribir("EGRESOS",          [HDR_EGRESOS])
escribir("INGRESOS_CAPITAL", [HDR_INGRESOS])

# ── 6. Aplicar formato profesional ────────────────────────────────────────────
print("\nAplicando formato profesional...")

def aplicar_formato(sheet_id, num_cols, color_fondo):
    """Formatea la fila 1 de la hoja como encabezado profesional."""
    solicitudes = [
        # Color de fondo + texto blanco + negrita
        {
            "repeatCell": {
                "range": {
                    "sheetId": sheet_id,
                    "startRowIndex": 0,
                    "endRowIndex": 1,
                    "startColumnIndex": 0,
                    "endColumnIndex": num_cols
                },
                "cell": {
                    "userEnteredFormat": {
                        "backgroundColor": color_fondo,
                        "textFormat": {
                            "foregroundColor": BLANCO,
                            "bold": True,
                            "fontSize": 10
                        },
                        "horizontalAlignment": "CENTER",
                        "verticalAlignment": "MIDDLE"
                    }
                },
                "fields": "userEnteredFormat(backgroundColor,textFormat,horizontalAlignment,verticalAlignment)"
            }
        },
        # Congelar primera fila
        {
            "updateSheetProperties": {
                "properties": {
                    "sheetId": sheet_id,
                    "gridProperties": {"frozenRowCount": 1}
                },
                "fields": "gridProperties.frozenRowCount"
            }
        },
        # Auto-resize columnas
        {
            "autoResizeDimensions": {
                "dimensions": {
                    "sheetId": sheet_id,
                    "dimension": "COLUMNS",
                    "startIndex": 0,
                    "endIndex": num_cols
                }
            }
        }
    ]
    sheets.batchUpdate(spreadsheetId=NEW_ID, body={"requests": solicitudes}).execute()

formatos = [
    ("FACTURAS",         len(HDR_FACTURAS),  AZUL_MARINO),
    ("PAGOS",            len(HDR_PAGOS),     AZUL_MARINO),
    ("COMPANIAS",        9,                  AZUL_MARINO),
    ("CLIENTES",         7,                  AZUL_MARINO),
    ("USUARIOS",         8,                  AZUL_MARINO),
    ("CAJA",             len(HDR_CAJA),      VERDE),
    ("CUADRE_CAJA",      len(HDR_CUADRE),    VERDE),
    ("EGRESOS",          len(HDR_EGRESOS),   VERDE),
    ("INGRESOS_CAPITAL", len(HDR_INGRESOS),  VERDE),
]

for hoja, ncols, color in formatos:
    sid = sheet_map.get(hoja)
    if sid is not None:
        try:
            aplicar_formato(sid, ncols, color)
            print(f"  OK - Formato: {hoja}")
        except Exception as e:
            print(f"  WARN - Formato {hoja}: {e}")
    else:
        print(f"  SKIP - {hoja} no encontrada")

# ── 7. Resumen ─────────────────────────────────────────────────────────────────
print(f"""
============================================================
MIGRACION COMPLETADA
------------------------------------------------------------
SpreadsheetId v2.0 : {NEW_ID}
URL                : https://docs.google.com/spreadsheets/d/{NEW_ID}
Cuadre migracion   : {SEQ_MIG}  (Estado: CERRADA)
Facturas migradas  : {cant_fact}
Pagos migrados     : {cant_pago}
Hojas v2.0 nuevas  : CAJA, CUADRE_CAJA, EGRESOS, INGRESOS_CAPITAL
============================================================
El sistema v2.0 esta listo para iniciar.
Ejecute INICIAR_v2.0.bat para arrancar en el puerto 5001.
""")
