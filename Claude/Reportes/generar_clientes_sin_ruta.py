# encoding: utf-8
"""
Clientes que estaban en RutaVisitasFechaHistoricos (junio 2026)
pero ya NO estan en RutaVisitas actualmente.
Supervisores S8 y S9 unicamente.
"""
import pyodbc, sys, os
from datetime import datetime
sys.path.insert(0, '.')
from config import CONN_STR

try:
    import openpyxl
    from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
    from openpyxl.utils import get_column_letter
except ImportError:
    import subprocess
    subprocess.check_call([sys.executable, '-m', 'pip', 'install', 'openpyxl'])
    import openpyxl
    from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
    from openpyxl.utils import get_column_letter

conn = pyodbc.connect(CONN_STR, timeout=60)
cur  = conn.cursor()

# ─── Paso 1: Tamaño por cliente (desde VW, columna con ñ en posicion 3)
print("Cargando tamanos desde VW...")
tam_col = 'Tama' + chr(241) + 'oCte'   # TamañoCte
cur.execute(f"""
    SELECT ClienteCodigo, MAX([{tam_col}]) AS Tamano
    FROM VWpedidosResumenMesactual
    WHERE YEAR(PedFecha) = 2026
    GROUP BY ClienteCodigo
""")
tamanos = {row[0]: row[1] for row in cur.fetchall()}
print(f"  {len(tamanos)} clientes con tamano")

# ─── Paso 2: Dia mas frecuente de visita en junio 2026 (por RepCodigo+CliID)
print("Calculando dias de visita desde historico junio 2026...")
cur.execute("""
    SELECT h.RepCodigo, h.CliID,
           DATEPART(WEEKDAY, h.RutFecha) AS DiaNro,
           COUNT(*) AS veces
    FROM RutaVisitasFechaHistoricos h
    JOIN representantes r ON r.RepCodigo = h.RepCodigo
    WHERE h.RutFecha BETWEEN '2026-06-01' AND '2026-06-30'
      AND r.RepSupervisor IN ('S8','S9')
    GROUP BY h.RepCodigo, h.CliID, DATEPART(WEEKDAY, h.RutFecha)
""")
# DATEPART WEEKDAY en SQL Server: 1=Dom, 2=Lun, 3=Mar, 4=Mie, 5=Jue, 6=Vie, 7=Sab
DIA_MAP = {1:'Domingo', 2:'Lunes', 3:'Martes', 4:'Miercoles',
           5:'Jueves',  6:'Viernes', 7:'Sabado'}

dia_raw = {}
for rep, cli, dia_nro, veces in cur.fetchall():
    k = (rep, cli)
    if k not in dia_raw or veces > dia_raw[k][1]:
        dia_raw[k] = (dia_nro, veces)
dias_visita = {k: DIA_MAP.get(v[0], 'N/A') for k, v in dia_raw.items()}
print(f"  {len(dias_visita)} pares rep+cliente con dia de visita")

# ─── Paso 3: Query principal
print("Ejecutando query principal...")
cur.execute("""
    SELECT DISTINCT h.RepCodigo, h.CliID
    FROM RutaVisitasFechaHistoricos h
    JOIN representantes r   ON r.RepCodigo = h.RepCodigo
    JOIN clientes c         ON c.CliID     = h.CliID
                            AND c.RepCodigo = h.RepCodigo
    WHERE h.RutFecha BETWEEN '2026-06-01' AND '2026-06-30'
      AND r.RepSupervisor IN ('S8','S9')
      AND NOT EXISTS (
          SELECT 1 FROM RutaVisitas rv
          WHERE rv.RepCodigo = h.RepCodigo
            AND rv.CliID     = h.CliID
      )
""")
pares = cur.fetchall()
print(f"  {len(pares)} clientes encontrados sin ruta actual")

# ─── Paso 4: Enriquecer con datos del cliente y representante
print("Enriqueciendo con datos de clientes...")
results = []
for rep_cod, cli_id in pares:
    cur.execute("""
        SELECT c.CliNombre, c.TiNID,
               r.RepNombre, r.RepSupervisor,
               tn.TinDescripcion
        FROM clientes c
        JOIN representantes r ON r.RepCodigo = c.RepCodigo
        LEFT JOIN TiposNegocio tn ON tn.TinID = c.TiNID
        WHERE c.CliID = ? AND c.RepCodigo = ?
    """, (cli_id, rep_cod))
    row = cur.fetchone()
    if not row:
        continue
    cli_nombre, ti_nid, rep_nombre, rep_sup, tipo_negocio = row
    tamano  = tamanos.get(cli_id, 'Sin Clasificar')
    dia_vis = dias_visita.get((rep_cod, cli_id), 'N/A')
    results.append({
        'RepCodigo':          rep_cod,
        'RepNombre':          rep_nombre or '',
        'Supervisor':         rep_sup or '',
        'CliID':              cli_id,
        'CliNombre':          cli_nombre or '',
        'TipoNegocio':        tipo_negocio or '',
        'Tamano':             tamano or 'Sin Clasificar',
        'DiaVisitaAsignar':   dia_vis,
    })

print(f"  {len(results)} registros en resultado final")
conn.close()

# ─── Paso 5: Generar Excel
print("Generando Excel...")
wb = openpyxl.Workbook()
ws = wb.active
ws.title = 'Clientes Sin Ruta'

# Colores
HDR_FILL  = PatternFill('solid', fgColor='1A3A5C')   # azul marino
SUP_S8    = PatternFill('solid', fgColor='E8F4FD')   # azul claro
SUP_S9    = PatternFill('solid', fgColor='FEF9E7')   # amarillo claro
ALT_FILL  = PatternFill('solid', fgColor='F8FBFE')
HDR_FONT  = Font(name='Calibri', bold=True, color='FFFFFF', size=10)
BOLD_FONT = Font(name='Calibri', bold=True, size=10)
NORM_FONT = Font(name='Calibri', size=10)
thin = Side(style='thin', color='CBD5E1')
bord = Border(left=thin, right=thin, top=thin, bottom=thin)
center = Alignment(horizontal='center', vertical='center', wrap_text=False)
left   = Alignment(horizontal='left',   vertical='center', wrap_text=False)

# Titulo
ws.merge_cells('A1:H1')
title_cell = ws['A1']
title_cell.value = 'CLIENTES EN RUTA JUNIO 2026 SIN ASIGNACION ACTUAL — Supervisores S8 / S9'
title_cell.font  = Font(name='Calibri', bold=True, size=12, color='1A3A5C')
title_cell.alignment = Alignment(horizontal='center', vertical='center')
ws.row_dimensions[1].height = 22

ws.merge_cells('A2:H2')
sub = ws['A2']
sub.value = f'Generado: {datetime.now().strftime("%d/%m/%Y %H:%M")}  |  Fuente: RutaVisitasFechaHistoricos (01/06/2026 - 30/06/2026)  |  Total: {len(results)} clientes'
sub.font  = Font(name='Calibri', size=9, color='5A7A9A', italic=True)
sub.alignment = Alignment(horizontal='center', vertical='center')
ws.row_dimensions[2].height = 16

# Encabezados
COLS = ['Rep\nCodigo', 'Representante', 'Supervisor', 'Cod.\nCliente',
        'Nombre Cliente', 'Tipo Negocio', 'Tamaño', 'Día Visita\nAsignar']
WIDTHS = [10, 28, 10, 10, 35, 16, 16, 14]
FIELDS = ['RepCodigo','RepNombre','Supervisor','CliID','CliNombre','TipoNegocio','Tamano','DiaVisitaAsignar']
ALIGNS = [center, left, center, center, left, center, center, center]

for col, (hdr, width) in enumerate(zip(COLS, WIDTHS), start=1):
    cell = ws.cell(row=3, column=col, value=hdr)
    cell.font      = HDR_FONT
    cell.fill      = HDR_FILL
    cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
    cell.border    = bord
    ws.column_dimensions[get_column_letter(col)].width = width
ws.row_dimensions[3].height = 28

# Datos
prev_sup = None
row_idx  = 4
for i, rec in enumerate(sorted(results, key=lambda x: (x['Supervisor'], x['RepCodigo'], x['CliNombre']))):
    fill = SUP_S8 if rec['Supervisor'] == 'S8' else SUP_S9
    if i % 2 == 0:
        base_fill = fill
    else:
        # Slightly alternate within same supervisor
        alt_color = 'EBF4FB' if rec['Supervisor'] == 'S8' else 'FFFDE7'
        base_fill = PatternFill('solid', fgColor=alt_color)

    for col, (field, aln) in enumerate(zip(FIELDS, ALIGNS), start=1):
        val = rec[field]
        cell = ws.cell(row=row_idx, column=col, value=val)
        cell.font      = NORM_FONT
        cell.fill      = base_fill
        cell.alignment = aln
        cell.border    = bord
    ws.row_dimensions[row_idx].height = 15
    row_idx += 1

# Congelar paneles
ws.freeze_panes = 'A4'

# Filtro
ws.auto_filter.ref = f'A3:H{row_idx - 1}'

# Hoja resumen
ws2 = wb.create_sheet('Resumen')
ws2['A1'].value = 'RESUMEN POR REPRESENTANTE'
ws2['A1'].font  = Font(bold=True, size=12, color='1A3A5C')

from collections import Counter, defaultdict
rep_count = defaultdict(lambda: {'nombre':'', 'sup':'', 'clientes':0, 'dias': Counter()})
for rec in results:
    k = rec['RepCodigo']
    rep_count[k]['nombre']  = rec['RepNombre']
    rep_count[k]['sup']     = rec['Supervisor']
    rep_count[k]['clientes'] += 1
    rep_count[k]['dias'][rec['DiaVisitaAsignar']] += 1

hdr2 = ['RepCodigo','Representante','Supervisor','Clientes Sin Ruta','Dia mas frecuente']
for col, h in enumerate(hdr2, 1):
    c = ws2.cell(row=2, column=col, value=h)
    c.font = Font(bold=True, color='FFFFFF'); c.fill = HDR_FILL
    c.alignment = Alignment(horizontal='center', vertical='center')
    c.border = bord

ri = 3
for rep, info in sorted(rep_count.items(), key=lambda x: (x[1]['sup'], x[0])):
    dia_frec = info['dias'].most_common(1)[0][0] if info['dias'] else 'N/A'
    vals = [rep, info['nombre'], info['sup'], info['clientes'], dia_frec]
    fill_r = SUP_S8 if info['sup']=='S8' else SUP_S9
    for col, v in enumerate(vals, 1):
        c = ws2.cell(row=ri, column=col, value=v)
        c.font = NORM_FONT; c.fill = fill_r
        c.alignment = Alignment(horizontal='center' if col != 2 else 'left', vertical='center')
        c.border = bord
    ri += 1

# Anchos hoja resumen
for col, w in enumerate([12, 32, 12, 20, 18], 1):
    ws2.column_dimensions[get_column_letter(col)].width = w

# Guardar
desktop = os.path.join(os.path.expanduser('~'), 'Desktop')
fname = os.path.join(desktop, f'Clientes_Sin_Ruta_S8_S9_{datetime.now().strftime("%Y%m%d_%H%M")}.xlsx')
wb.save(fname)
print(f'\nArchivo guardado: {fname}')
print(f'Total clientes: {len(results)}')

# Stats finales
from collections import Counter
print("\nPor supervisor:")
for sup in ['S8','S9']:
    n = sum(1 for r in results if r['Supervisor']==sup)
    print(f"  {sup}: {n} clientes")
print("\nPor dia de visita:")
dias = Counter(r['DiaVisitaAsignar'] for r in results)
for dia, n in sorted(dias.items()): print(f"  {dia}: {n}")
