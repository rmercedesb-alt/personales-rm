"""
Genera el archivo Excel de carga inicial de datos para el Sistema de Administración de Inmuebles RD.
Salida: CARGA_DATOS_INMUEBLES_RD.xlsx
"""
from openpyxl import Workbook
from openpyxl.styles import (Font, PatternFill, Alignment, Border, Side,
                              GradientFill)
from openpyxl.styles.differential import DifferentialStyle
from openpyxl.formatting.rule import ColorScaleRule, DataBarRule, Rule
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.datavalidation import DataValidation
import datetime

wb = Workbook()

# ─── Colores corporativos ────────────────────────────────────────────────────
AZUL_DARK   = "0E2F55"
AZUL_CORP   = "1B4F8C"
AZUL_LIGHT  = "D6E4F7"
VERDE       = "1B5E20"
VERDE_LIGHT = "C8E6C9"
ROJO_LIGHT  = "FFCDD2"
AMARILLO    = "FFF9C4"
GRIS_HEADER = "F5F5F5"
BLANCO      = "FFFFFF"
NARANJA_LIGHT = "FFE0B2"

def hdr_fill(color): return PatternFill("solid", fgColor=color)
def font(bold=False, color="000000", size=10, italic=False):
    return Font(name="Arial", bold=bold, color=color, size=size, italic=italic)
def center(): return Alignment(horizontal="center", vertical="center", wrap_text=True)
def left():   return Alignment(horizontal="left",   vertical="center", wrap_text=True)
def thin_border():
    s = Side(style="thin", color="CCCCCC")
    return Border(left=s, right=s, top=s, bottom=s)

def make_header_row(ws, row, cols, bg=AZUL_CORP, fg=BLANCO, size=10):
    for col_idx, (col_letter, title) in enumerate(cols, 1):
        c = ws.cell(row=row, column=col_idx, value=title)
        c.font = font(bold=True, color=fg, size=size)
        c.fill = hdr_fill(bg)
        c.alignment = center()
        c.border = thin_border()

def make_desc_row(ws, row, descs, bg=AZUL_LIGHT):
    for col_idx, desc in enumerate(descs, 1):
        c = ws.cell(row=row, column=col_idx, value=desc)
        c.font = font(italic=True, color="1B4F8C", size=9)
        c.fill = hdr_fill(bg)
        c.alignment = left()
        c.border = thin_border()

def style_data_row(ws, row, num_cols, obligatorio_cols=None, bg=BLANCO):
    obligatorio_cols = obligatorio_cols or []
    for col in range(1, num_cols + 1):
        c = ws.cell(row=row, column=col)
        c.font = font(size=10)
        c.border = thin_border()
        c.alignment = left()
        if col in obligatorio_cols:
            c.fill = hdr_fill("FFFDE7")
        else:
            c.fill = hdr_fill(bg)

def add_example_label(ws, row, col=1, text="→ EJEMPLO"):
    c = ws.cell(row=row, column=col)
    c.value = c.value  # keep existing
    # Add a note on the side
    note = ws.cell(row=row, column=col)

def freeze_and_filter(ws, freeze_row=3):
    ws.freeze_panes = f"A{freeze_row}"
    ws.auto_filter.ref = ws.dimensions

def set_col_widths(ws, widths):
    for col_letter, w in widths.items():
        ws.column_dimensions[col_letter].width = w

def add_dv(ws, formula1, sqref, prompt_title, prompt_body, error_msg):
    dv = DataValidation(type="list", formula1=formula1, allow_blank=True,
                        showDropDown=False)
    dv.prompt = prompt_body
    dv.promptTitle = prompt_title
    dv.error = error_msg
    dv.errorTitle = "Valor inválido"
    dv.showErrorMessage = True
    dv.sqref = sqref
    ws.add_data_validation(dv)

# ════════════════════════════════════════════════════════════════════════════
# HOJA 0 — INSTRUCCIONES
# ════════════════════════════════════════════════════════════════════════════
ws0 = wb.active
ws0.title = "📋 INSTRUCCIONES"
ws0.sheet_view.showGridLines = False
ws0.column_dimensions["A"].width = 3
ws0.column_dimensions["B"].width = 28
ws0.column_dimensions["C"].width = 70
ws0.column_dimensions["D"].width = 20

# Título
ws0.row_dimensions[1].height = 10
ws0.merge_cells("B2:D2")
c = ws0["B2"]
c.value = "SISTEMA DE ADMINISTRACIÓN DE INMUEBLES RD — CARGA INICIAL DE DATOS"
c.font = Font(name="Arial", bold=True, size=14, color=BLANCO)
c.fill = hdr_fill(AZUL_DARK)
c.alignment = center()
ws0.row_dimensions[2].height = 36

ws0.merge_cells("B3:D3")
c = ws0["B3"]
c.value = "Instrucciones generales para poblar el sistema"
c.font = font(italic=True, color=AZUL_CORP, size=11)
c.fill = hdr_fill(AZUL_LIGHT)
c.alignment = center()
ws0.row_dimensions[3].height = 22

instrucciones = [
    ("", ""),
    ("PASO A PASO", ""),
    ("1️⃣  Empieza por la pestaña USUARIOS", "Registra los usuarios que tendrán acceso al sistema (mínimo un ADMINISTRADOR)."),
    ("2️⃣  Carga PROPIETARIOS", "Dueños de los inmuebles. El ID lo genera el sistema (PROP-001, PROP-002...)."),
    ("3️⃣  Carga INMUEBLES", "Propiedades administradas. Usa el ID de Propietario de la pestaña anterior."),
    ("4️⃣  Carga INQUILINOS", "Personas que alquilan. El ID lo genera el sistema (INQ-001...)."),
    ("5️⃣  Carga CONTRATOS", "Un contrato vincula un Inmueble con un Inquilino. Usa los IDs anteriores."),
    ("6️⃣  Carga CXC_PENDIENTES", "Cuentas por cobrar que aún NO han sido pagadas (estado PENDIENTE o EN_MORA)."),
    ("7️⃣  Carga CXC_PAGADAS", "Pagos históricos ya cobrados (puede ser COBRADO, EN_ADMINISTRACION o LIQUIDADO)."),
    ("", ""),
    ("CONVENCIONES DE COLOR", ""),
    ("🟡 Fondo amarillo", "Campo OBLIGATORIO — debes llenarlo siempre."),
    ("⬜ Fondo blanco", "Campo OPCIONAL — puedes dejarlo vacío si no tienes el dato."),
    ("🔵 Fila azul oscuro", "Encabezado de columnas."),
    ("🔵 Fila azul claro", "Descripción de cada columna — lee antes de llenar."),
    ("✏️ Filas 4 en adelante", "Aquí ingresas tus datos. Las filas de ejemplo son en gris."),
    ("", ""),
    ("REGLAS IMPORTANTES", ""),
    ("IDs", "El sistema genera IDs automáticos (PROP-001, INM-001, INQ-001, CTR-001, CXC-001, PAG-001). Si ya tienes IDs de tu sistema anterior puedes usarlos respetando el formato."),
    ("Fechas", "Usa el formato AAAA-MM-DD  (ej: 2024-01-15). Excel puede cambiarlo a otro formato visual, pero el valor interno debe ser una fecha válida."),
    ("Montos", "Números sin símbolos, sin comas. Usa punto decimal. Ejemplo: 25000.00"),
    ("Cédulas", "Formato dominicano: 001-0000000-0  (con guiones o sin guiones, ambos se aceptan)"),
    ("Teléfonos", "Formato: 809-000-0000 o 8090000000 — ambos aceptados"),
    ("Estado mora INMUEBLES", "Se actualiza automáticamente cada noche: VERDE (al día), AMARILLO (1 mes), NARANJA (2 meses), ROJO (3+), GRIS (vacío/sin contrato)"),
]

row = 4
for label, desc in instrucciones:
    if label == "" and desc == "":
        row += 1
        continue
    is_section = desc == ""
    ws0.merge_cells(f"C{row}:D{row}") if not is_section else None
    if is_section:
        ws0.merge_cells(f"B{row}:D{row}")
        c = ws0[f"B{row}"]
        c.value = label
        c.font = font(bold=True, color=BLANCO, size=11)
        c.fill = hdr_fill(AZUL_CORP)
        c.alignment = center()
        ws0.row_dimensions[row].height = 22
    else:
        b = ws0[f"B{row}"]
        b.value = label
        b.font = font(bold=True, color=AZUL_CORP, size=10)
        b.fill = hdr_fill(GRIS_HEADER)
        b.alignment = left()
        c2 = ws0[f"C{row}"]
        c2.value = desc
        c2.font = font(size=10)
        c2.alignment = left()
        ws0.row_dimensions[row].height = 30
    row += 1


# ════════════════════════════════════════════════════════════════════════════
# HOJA 1 — USUARIOS (ROLES)
# ════════════════════════════════════════════════════════════════════════════
ws1 = wb.create_sheet("👤 USUARIOS")
ws1.sheet_view.showGridLines = False

ws1.merge_cells("A1:E1")
c = ws1["A1"]
c.value = "USUARIOS DEL SISTEMA (pestaña ROLES en Google Sheets)"
c.font = font(bold=True, color=BLANCO, size=12)
c.fill = hdr_fill(AZUL_DARK)
c.alignment = center()
ws1.row_dimensions[1].height = 30

cols1 = [
    ("A","EMAIL (usuario@dominio.com)"),("B","NOMBRE COMPLETO"),
    ("C","ROL"),("D","ACTIVO (SI/NO)"),("E","CONTRASEÑA INICIAL"),
]
make_header_row(ws1, 2, cols1)

descs1 = [
    "Correo Google con el que entrará al sistema. OBLIGATORIO.",
    "Nombre que aparece en el sistema. OBLIGATORIO.",
    "ADMINISTRADOR = acceso total | ASISTENTE = puede registrar pagos | LECTURA = solo ver. OBLIGATORIO.",
    "SI = puede entrar | NO = bloqueado. OBLIGATORIO.",
    "Primera contraseña. En el primer login se guarda como hash seguro. Si la dejas vacía, la primera contraseña que escriba el usuario será la definitiva.",
]
make_desc_row(ws1, 3, descs1)

ejemplos1 = [
    ["admin@miempresa.com", "Juan Pérez García", "ADMINISTRADOR", "SI", "Admin2024!"],
    ["cobros@miempresa.com", "María Rodríguez", "ASISTENTE", "SI", "Cobros2024!"],
    ["lectura@miempresa.com", "Carlos López", "LECTURA", "SI", ""],
]
for i, row_data in enumerate(ejemplos1, 4):
    for j, val in enumerate(row_data, 1):
        c = ws1.cell(row=i, column=j, value=val)
        c.font = font(size=10, italic=True, color="555555")
        c.fill = hdr_fill(GRIS_HEADER)
        c.border = thin_border()
        c.alignment = left()

for r in range(7, 50):
    obligatorios = [1, 2, 3, 4]
    style_data_row(ws1, r, 5, obligatorios)

add_dv(ws1, '"ADMINISTRADOR,ASISTENTE,LECTURA"', "C7:C50",
       "Rol del usuario", "Selecciona el nivel de acceso",
       "Debe ser ADMINISTRADOR, ASISTENTE o LECTURA")
add_dv(ws1, '"SI,NO"', "D7:D50",
       "¿Activo?", "SI = puede entrar al sistema",
       "Debe ser SI o NO")

set_col_widths(ws1, {"A":35,"B":28,"C":18,"D":14,"E":20})
ws1.freeze_panes = "A4"


# ════════════════════════════════════════════════════════════════════════════
# HOJA 2 — PROPIETARIOS
# ════════════════════════════════════════════════════════════════════════════
ws2 = wb.create_sheet("🏠 PROPIETARIOS")
ws2.sheet_view.showGridLines = False

ws2.merge_cells("A1:N1")
c = ws2["A1"]
c.value = "PROPIETARIOS — Dueños de los inmuebles administrados"
c.font = font(bold=True, color=BLANCO, size=12)
c.fill = hdr_fill(AZUL_DARK)
c.alignment = center()
ws2.row_dimensions[1].height = 30

cols2 = [
    ("A","ID PROPIETARIO"),("B","NOMBRE COMPLETO *"),("C","CÉDULA"),
    ("D","TELÉFONO"),("E","EMAIL"),("F","BANCO"),("G","NÚMERO CUENTA"),
    ("H","TIPO CUENTA"),("I","TIPO COMISIÓN *"),("J","COMISIÓN %"),
    ("K","COMISIÓN FIJA RD$"),("L","ESTADO *"),("M","NOTAS"),("N","FECHA REGISTRO"),
]
make_header_row(ws2, 2, cols2)

descs2 = [
    "Auto-generado: PROP-001, PROP-002... Si dejas vacío el sistema lo asigna. Si ya tienes un ID propio úsalo.",
    "Nombre legal completo. OBLIGATORIO.",
    "Cédula dominicana. Formato: 001-0000000-0",
    "Teléfono. Formato: 809-000-0000",
    "Correo electrónico del propietario",
    "Banco donde recibe pagos: BanReservas, Popular, BHD León, etc.",
    "Número de cuenta bancaria",
    "AHORRO o CORRIENTE",
    "PORCENTAJE = cobra % del alquiler | FIJO = monto fijo mensual | MIXTO = % + fijo. OBLIGATORIO.",
    "Si tipo=PORCENTAJE o MIXTO, ingresa el % aquí. Ej: 10 (para 10%). Sin símbolo %.",
    "Si tipo=FIJO o MIXTO, ingresa el monto fijo aquí en RD$. Ej: 2000",
    "ACTIVO o INACTIVO. OBLIGATORIO.",
    "Observaciones adicionales",
    "Fecha de registro. Se puede dejar vacía, el sistema usa la fecha actual.",
]
make_desc_row(ws2, 3, descs2)

ejemplos2 = [
    ["PROP-001","Roberto Jiménez Marte","001-0123456-7","809-555-1001","rjimenez@gmail.com","BanReservas","01-012-12345678-0","AHORRO","PORCENTAJE",10,"","ACTIVO","Propietario principal","2024-01-05"],
    ["PROP-002","Carmen Altagracia Sosa","001-9876543-2","829-444-2002","casosa@hotmail.com","Popular","200-612345678-9","AHORRO","PORCENTAJE",8,"","ACTIVO","Tiene 2 apartamentos","2024-02-10"],
    ["PROP-003","Inversiones del Este SRL","","809-333-3003","info@inversiones.com","BHD León","","CORRIENTE","FIJO","",5000,"ACTIVO","Empresa - cuota fija mensual","2024-03-01"],
    ["PROP-004","Ana Mercedes Valdez","001-5551234-1","849-222-4004","anavaldez@mail.com","Scotiabank","","AHORRO","MIXTO",5,1500,"ACTIVO","5% + RD$1,500 fijo","2024-04-15"],
]
for i, row_data in enumerate(ejemplos2, 4):
    for j, val in enumerate(row_data, 1):
        c = ws2.cell(row=i, column=j, value=val)
        c.font = font(size=10, italic=True, color="555555")
        c.fill = hdr_fill(GRIS_HEADER)
        c.border = thin_border()
        c.alignment = left()

for r in range(8, 100):
    style_data_row(ws2, r, 14, [2, 9, 12])

add_dv(ws2, '"PORCENTAJE,FIJO,MIXTO"', "I8:I100", "Tipo comisión",
       "PORCENTAJE, FIJO o MIXTO", "Valor no válido")
add_dv(ws2, '"AHORRO,CORRIENTE"', "H8:H100", "Tipo cuenta", "AHORRO o CORRIENTE", "Valor no válido")
add_dv(ws2, '"ACTIVO,INACTIVO"', "L8:L100", "Estado", "ACTIVO o INACTIVO", "Valor no válido")
add_dv(ws2, '"BanReservas,Popular,BHD León,Scotiabank,Banistmo,Asociación Cibao,Otra"',
       "F8:F100", "Banco", "Selecciona el banco", "Banco no válido")

set_col_widths(ws2, {"A":14,"B":28,"C":17,"D":16,"E":28,"F":18,"G":22,"H":14,"I":16,"J":12,"K":16,"L":12,"M":30,"N":16})
ws2.freeze_panes = "A4"


# ════════════════════════════════════════════════════════════════════════════
# HOJA 3 — INMUEBLES
# ════════════════════════════════════════════════════════════════════════════
ws3 = wb.create_sheet("🏢 INMUEBLES")
ws3.sheet_view.showGridLines = False

ws3.merge_cells("A1:O1")
c = ws3["A1"]
c.value = "INMUEBLES — Propiedades bajo administración"
c.font = font(bold=True, color=BLANCO, size=12)
c.fill = hdr_fill(AZUL_DARK)
c.alignment = center()
ws3.row_dimensions[1].height = 30

cols3 = [
    ("A","ID INMUEBLE"),("B","ID PROPIETARIO *"),("C","TIPO *"),("D","DESCRIPCIÓN *"),
    ("E","DIRECCIÓN *"),("F","SECTOR"),("G","CIUDAD *"),("H","LATITUD"),
    ("I","LONGITUD"),("J","MONTO ALQUILER RD$ *"),("K","ESTADO *"),
    ("L","NIVEL MOROSIDAD"),("M","NOTAS"),("N","FECHA REGISTRO"),
]
make_header_row(ws3, 2, cols3)

descs3 = [
    "Auto-generado: INM-001, INM-002... Puedes usar tu propio ID.",
    "ID del propietario de la pestaña PROPIETARIOS. OBLIGATORIO. Ej: PROP-001",
    "APARTAMENTO, CASA, LOCAL, OFICINA, VILLA, HABITACION u OTRO. OBLIGATORIO.",
    "Nombre o descripción corta del inmueble. Ej: Apto 2BR Torre Piantini. OBLIGATORIO.",
    "Dirección completa. Ej: Av. Abraham Lincoln #345, Apto 4B. OBLIGATORIO.",
    "Sector o urbanización. Ej: Piantini, Naco, La Esperilla",
    "Ciudad. OBLIGATORIO.",
    "Latitud GPS para el mapa interactivo. Ej: 18.4861  (puedes obtenerla en Google Maps)",
    "Longitud GPS. Ej: -69.9312  (negativa en RD)",
    "Precio de alquiler mensual en RD$. Sin comas. Ej: 25000. OBLIGATORIO.",
    "DISPONIBLE = sin inquilino | OCUPADO = con contrato vigente. OBLIGATORIO.",
    "Lo calcula el sistema cada noche: VERDE/AMARILLO/NARANJA/ROJO/GRIS. Puedes dejarlo en VERDE.",
    "Observaciones: número de habitaciones, parqueos, etc.",
    "Fecha. Se puede dejar vacía.",
]
make_desc_row(ws3, 3, descs3)

ejemplos3 = [
    ["INM-001","PROP-001","APARTAMENTO","Apto 2BR Torre Piantini Piso 8","Av. Abraham Lincoln #345 Apto 8B","Piantini","Santo Domingo",18.473910,-69.944260,35000,"OCUPADO","VERDE","2 hab, 2 baños, parqueo cubierto","2024-01-10"],
    ["INM-002","PROP-001","APARTAMENTO","Apto 1BR Edificio Costa Verde","C/ Gustavo Mejía Ricart #78 Apto 3A","Naco","Santo Domingo",18.477420,-69.936150,22000,"OCUPADO","AMARILLO","1 hab, 1 baño, sin parqueo","2024-01-10"],
    ["INM-003","PROP-002","CASA","Casa 3BR Villa Mella","Calle 5 #89, Res. La Altagracia","Villa Mella","Santo Domingo Norte",18.531200,-69.978900,18000,"DISPONIBLE","GRIS","3 hab, patio, cisterna","2024-02-15"],
    ["INM-004","PROP-002","LOCAL","Local Comercial Centro","C/ El Conde #123 Local 2","Ciudad Colonial","Santo Domingo",18.474680,-69.900230,45000,"OCUPADO","VERDE","80m², baño, aire acondicionado","2024-02-20"],
    ["INM-005","PROP-003","OFICINA","Oficina Torre Empresarial","Av. 27 de Febrero #456 Of. 501","Gazcue","Santo Domingo",18.475000,-69.925000,60000,"OCUPADO","NARANJA","Suite ejecutiva 120m²","2024-03-05"],
    ["INM-006","PROP-004","APARTAMENTO","Apto 3BR Santiago Centro","C/ del Sol #34 Apto 2C","Santiago Centro","Santiago",19.450900,-70.697000,28000,"OCUPADO","VERDE","3 hab, 2 baños, balcón","2024-04-20"],
]
for i, row_data in enumerate(ejemplos3, 4):
    for j, val in enumerate(row_data, 1):
        c = ws3.cell(row=i, column=j, value=val)
        c.font = font(size=10, italic=True, color="555555")
        c.fill = hdr_fill(GRIS_HEADER)
        c.border = thin_border()
        c.alignment = left()

for r in range(10, 200):
    style_data_row(ws3, r, 14, [2, 3, 4, 5, 7, 10, 11])

add_dv(ws3, '"APARTAMENTO,CASA,LOCAL,OFICINA,VILLA,HABITACION,OTRO"', "C10:C200", "Tipo", "Tipo de inmueble", "Tipo no válido")
add_dv(ws3, '"DISPONIBLE,OCUPADO,EN_MANTENIMIENTO"', "K10:K200", "Estado", "Estado del inmueble", "Estado no válido")
add_dv(ws3, '"VERDE,AMARILLO,NARANJA,ROJO,GRIS"', "L10:L200", "Morosidad", "Nivel de morosidad", "Nivel no válido")
add_dv(ws3, '"Santo Domingo,Santiago,La Romana,San Pedro de Macorís,Puerto Plata,Higüey,La Vega,Baní,Azua,Otra"',
       "G10:G200", "Ciudad", "Ciudad del inmueble", "Ciudad no válida")

set_col_widths(ws3, {"A":14,"B":16,"C":16,"D":35,"E":38,"F":20,"G":18,"H":14,"I":14,"J":20,"K":14,"L":18,"M":35,"N":16})
ws3.freeze_panes = "A4"


# ════════════════════════════════════════════════════════════════════════════
# HOJA 4 — INQUILINOS
# ════════════════════════════════════════════════════════════════════════════
ws4 = wb.create_sheet("👥 INQUILINOS")
ws4.sheet_view.showGridLines = False

ws4.merge_cells("A1:K1")
c = ws4["A1"]
c.value = "INQUILINOS — Personas que alquilan los inmuebles"
c.font = font(bold=True, color=BLANCO, size=12)
c.fill = hdr_fill(AZUL_DARK)
c.alignment = center()
ws4.row_dimensions[1].height = 30

cols4 = [
    ("A","ID INQUILINO"),("B","NOMBRE COMPLETO *"),("C","CÉDULA"),
    ("D","TELÉFONO *"),("E","EMAIL"),("F","OCUPACIÓN"),
    ("G","REFERENCIA PERSONAL"),("H","TELÉFONO REFERENCIA"),
    ("I","ESTADO *"),("J","NOTAS"),("K","FECHA REGISTRO"),
]
make_header_row(ws4, 2, cols4)

descs4 = [
    "Auto-generado: INQ-001, INQ-002...",
    "Nombre completo del inquilino. OBLIGATORIO.",
    "Cédula dominicana o pasaporte. Formato: 001-0000000-0",
    "Teléfono principal. Formato: 809-000-0000. OBLIGATORIO.",
    "Correo electrónico (para notificaciones)",
    "Trabajo u ocupación. Ej: Médico, Empresario, Empleado público",
    "Nombre de persona de referencia/garantía",
    "Teléfono de la referencia personal",
    "ACTIVO o INACTIVO. OBLIGATORIO.",
    "Notas adicionales: número de personas, mascotas, garantías, etc.",
    "Fecha de registro. Puede dejarse vacía.",
]
make_desc_row(ws4, 3, descs4)

ejemplos4 = [
    ["INQ-001","Luis Miguel Ortega Sánchez","001-1234567-8","809-701-0001","lortega@empresa.com","Médico","Pedro Ortega","809-701-9999","ACTIVO","2 personas. Sin mascotas. Trabaja en Clínica Abreu","2024-01-15"],
    ["INQ-002","Yessenia Altagracia Mejía","001-2345678-9","829-702-0002","ymejia@correo.com","Contadora","Carmen Mejía (madre)","829-888-1234","ACTIVO","4 personas. Garantía pagada doble","2024-02-20"],
    ["INQ-003","Comercial El Éxito SRL","","809-703-0003","info@exito.com.do","Comercio","Lic. José Pérez","809-703-5555","ACTIVO","Local comercial. Representante: José Martínez","2024-02-25"],
    ["INQ-004","Ramona Feliz de la Cruz","001-3456789-0","849-704-0004","","Maestra","María de la Cruz (hermana)","849-704-8888","ACTIVO","","2024-03-01"],
    ["INQ-005","Fernando Aurelio Peña","001-4567890-1","809-705-0005","fapena@mail.com","Ingeniero","","","ACTIVO","Contrato empresa, paga la constructora","2024-04-20"],
    ["INQ-006","Inversiones Norte SA","","809-706-0006","contacto@invertnorte.com","Empresa","Juan Reyes","809-706-1111","ACTIVO","Firma renta oficina para sus ejecutivos","2024-04-22"],
]
for i, row_data in enumerate(ejemplos4, 4):
    for j, val in enumerate(row_data, 1):
        c = ws4.cell(row=i, column=j, value=val)
        c.font = font(size=10, italic=True, color="555555")
        c.fill = hdr_fill(GRIS_HEADER)
        c.border = thin_border()
        c.alignment = left()

for r in range(10, 200):
    style_data_row(ws4, r, 11, [2, 4, 9])

add_dv(ws4, '"ACTIVO,INACTIVO"', "I10:I200", "Estado", "ACTIVO o INACTIVO", "Valor no válido")
set_col_widths(ws4, {"A":14,"B":32,"C":17,"D":16,"E":30,"F":20,"G":28,"H":20,"I":12,"J":40,"K":16})
ws4.freeze_panes = "A4"


# ════════════════════════════════════════════════════════════════════════════
# HOJA 5 — CONTRATOS
# ════════════════════════════════════════════════════════════════════════════
ws5 = wb.create_sheet("📋 CONTRATOS")
ws5.sheet_view.showGridLines = False

ws5.merge_cells("A1:S1")
c = ws5["A1"]
c.value = "CONTRATOS — Vinculan un Inmueble con un Inquilino en un período determinado"
c.font = font(bold=True, color=BLANCO, size=12)
c.fill = hdr_fill(AZUL_DARK)
c.alignment = center()
ws5.row_dimensions[1].height = 30

cols5 = [
    ("A","ID CONTRATO"),("B","ID INMUEBLE *"),("C","ID INQUILINO *"),
    ("D","FECHA INICIO *"),("E","FECHA FIN *"),("F","MONTO ALQUILER RD$ *"),
    ("G","PERIODICIDAD *"),("H","DÍA DE COBRO *"),("I","DÍAS DE GRACIA"),
    ("J","TIPO MORA *"),("K","MORA % (si es %)"),("L","MORA FIJA RD$ (si es fijo)"),
    ("M","DEPÓSITO RD$"),("N","ESTADO *"),("O","NOTAS"),("P","FECHA REGISTRO"),
]
make_header_row(ws5, 2, cols5)

descs5 = [
    "Auto-generado: CTR-001, CTR-002...",
    "ID del inmueble de la pestaña INMUEBLES. OBLIGATORIO. Ej: INM-001",
    "ID del inquilino de la pestaña INQUILINOS. OBLIGATORIO. Ej: INQ-001",
    "Fecha de inicio del contrato. Formato: AAAA-MM-DD. OBLIGATORIO.",
    "Fecha de vencimiento del contrato. Formato: AAAA-MM-DD. OBLIGATORIO.",
    "Monto mensual o quincenal del alquiler en RD$. Sin comas. OBLIGATORIO.",
    "MENSUAL = una vez al mes | QUINCENAL = dos veces al mes. OBLIGATORIO.",
    "Día del mes en que se genera el cobro. Ej: 1 = día 1 de cada mes. OBLIGATORIO.",
    "Número de días después del vencimiento antes de aplicar mora. Ej: 5",
    "PORCENTAJE = mora es un % del alquiler | MONTO_FIJO = mora es un monto fijo. OBLIGATORIO.",
    "Si tipo mora = PORCENTAJE, indica el %. Ej: 5 (para 5%). Sin símbolo.",
    "Si tipo mora = MONTO_FIJO, indica el monto en RD$. Ej: 1000",
    "Monto del depósito de garantía pagado al inicio. Puede ser 0.",
    "VIGENTE = activo | VENCIDO = expiró | CANCELADO = terminado antes. OBLIGATORIO.",
    "Observaciones adicionales del contrato",
    "Fecha de registro. Puede dejarse vacía.",
]
make_desc_row(ws5, 3, descs5)

ejemplos5 = [
    ["CTR-001","INM-001","INQ-001","2024-01-15","2025-01-14",35000,"MENSUAL",1,5,"PORCENTAJE",5,"",35000,"VIGENTE","Contrato estándar 1 año. Depósito equivalente a 1 mes","2024-01-15"],
    ["CTR-002","INM-002","INQ-002","2024-03-01","2025-02-28",22000,"MENSUAL",1,3,"PORCENTAJE",3,"",44000,"VIGENTE","Depósito doble por primer contrato","2024-03-01"],
    ["CTR-003","INM-004","INQ-003","2024-02-25","2025-02-24",45000,"MENSUAL",5,5,"MONTO_FIJO","",2000,45000,"VIGENTE","Local comercial. Mora fija RD$2,000 por retraso","2024-02-25"],
    ["CTR-004","INM-005","INQ-006","2024-03-05","2025-03-04",60000,"MENSUAL",1,0,"PORCENTAJE",10,"",60000,"VIGENTE","Empresa. Sin días de gracia","2024-03-05"],
    ["CTR-005","INM-006","INQ-005","2024-04-20","2025-04-19",28000,"MENSUAL",20,5,"PORCENTAJE",5,"",28000,"VIGENTE","Cobro el día 20 de cada mes","2024-04-20"],
]
for i, row_data in enumerate(ejemplos5, 4):
    for j, val in enumerate(row_data, 1):
        c = ws5.cell(row=i, column=j, value=val)
        c.font = font(size=10, italic=True, color="555555")
        c.fill = hdr_fill(GRIS_HEADER)
        c.border = thin_border()
        c.alignment = left()

for r in range(9, 200):
    style_data_row(ws5, r, 16, [2, 3, 4, 5, 6, 7, 8, 10, 14])

add_dv(ws5, '"MENSUAL,QUINCENAL"', "G9:G200", "Periodicidad", "MENSUAL o QUINCENAL", "Valor no válido")
add_dv(ws5, '"PORCENTAJE,MONTO_FIJO"', "J9:J200", "Tipo mora", "PORCENTAJE o MONTO_FIJO", "Valor no válido")
add_dv(ws5, '"VIGENTE,VENCIDO,CANCELADO"', "N9:N200", "Estado contrato", "Estado del contrato", "Valor no válido")

set_col_widths(ws5, {"A":14,"B":14,"C":14,"D":14,"E":14,"F":20,"G":14,"H":14,"I":14,"J":16,"K":14,"L":16,"M":16,"N":12,"O":35,"P":16})
ws5.freeze_panes = "A4"


# ════════════════════════════════════════════════════════════════════════════
# HOJA 6 — CXC PENDIENTES
# ════════════════════════════════════════════════════════════════════════════
ws6 = wb.create_sheet("💰 CXC_PENDIENTES")
ws6.sheet_view.showGridLines = False

ws6.merge_cells("A1:S1")
c = ws6["A1"]
c.value = "CxC PENDIENTES — Cuentas por cobrar que aún NO han sido pagadas (estado PENDIENTE o EN_MORA)"
c.font = font(bold=True, color=BLANCO, size=12)
c.fill = hdr_fill(VERDE)
c.alignment = center()
ws6.row_dimensions[1].height = 30

ws6.merge_cells("A2:S2")
c = ws6["A2"]
c.value = "⚠️  USA ESTA PESTAÑA para cargar deuda pendiente histórica de inquilinos que ya tienen contratos activos. El sistema generará CxC automáticamente para períodos futuros."
c.font = font(bold=True, color=VERDE, size=10)
c.fill = hdr_fill(VERDE_LIGHT)
c.alignment = center()
ws6.row_dimensions[2].height = 25

cols6 = [
    ("A","ID CXC"),("B","ID CONTRATO *"),("C","ID INQUILINO *"),
    ("D","NOMBRE INQUILINO"),("E","ID INMUEBLE *"),("F","TIPO CARGO *"),
    ("G","DESCRIPCIÓN"),("H","PERÍODO INICIO *"),("I","PERÍODO FIN *"),
    ("J","FECHA VENCIMIENTO *"),("K","MONTO ORIGINAL RD$ *"),("L","MORA ACUMULADA RD$"),
    ("M","MONTO PAGADO RD$"),("N","SALDO PENDIENTE RD$"),("O","ESTADO *"),
    ("P","DÍAS EN MORA"),("Q","FECHA ÚLTIMO PAGO"),("R","NOTAS"),("S","FECHA REGISTRO"),
]
make_header_row(ws6, 3, cols6)

descs6 = [
    "Auto-generado: CXC-001...",
    "ID del contrato. OBLIGATORIO. Ej: CTR-001",
    "ID del inquilino. OBLIGATORIO. Ej: INQ-001",
    "Nombre (referencia). Lo llena el sistema automáticamente.",
    "ID del inmueble. OBLIGATORIO. Ej: INM-001",
    "ALQUILER = cuota de alquiler | CARGO_EXTRA = cargo adicional (agua, luz, etc.). OBLIGATORIO.",
    "Descripción. Ej: Alquiler Enero 2024 o Recibo agua noviembre",
    "Primer día del período que cubre este cargo. Formato: AAAA-MM-DD. OBLIGATORIO.",
    "Último día del período. Formato: AAAA-MM-DD. OBLIGATORIO.",
    "Fecha límite de pago (sin mora). Formato: AAAA-MM-DD. OBLIGATORIO.",
    "Monto original del cargo antes de mora. OBLIGATORIO.",
    "Mora acumulada hasta hoy. Si no hay mora, pon 0.",
    "Si ya se abonó algo, pon el monto. Si no, pon 0.",
    "Saldo que falta pagar = Monto Original + Mora - Pagado. El sistema lo calcula pero puedes ingresarlo.",
    "PENDIENTE = sin pagar sin mora | EN_MORA = vencido con mora. OBLIGATORIO.",
    "Número de días que lleva en mora. El sistema lo actualiza cada noche.",
    "Si hubo algún abono, la fecha del último pago. Puede dejarse vacía.",
    "Observaciones",
    "Fecha de registro.",
]
make_desc_row(ws6, 4, descs6)

hoy = datetime.date.today()
hace2m = hoy - datetime.timedelta(days=60)
hace1m = hoy - datetime.timedelta(days=30)
hace45 = hoy - datetime.timedelta(days=45)
venc30  = hoy - datetime.timedelta(days=30)
venc60  = hoy - datetime.timedelta(days=60)

ejemplos6 = [
    ["CXC-001","CTR-001","INQ-001","Luis Miguel Ortega","INM-001","ALQUILER","Alquiler Mayo 2024",
     f"{hace1m.replace(day=1)}",f"{hace1m.replace(day=28) if hace1m.month!=2 else hace1m.replace(day=28)}",
     f"{hace1m.replace(day=1)}",35000,0,0,35000,"PENDIENTE",0,"","Pendiente del mes anterior","2024-05-01"],
    ["CXC-002","CTR-002","INQ-002","Yessenia Mejía","INM-002","ALQUILER","Alquiler Abril 2024",
     f"{hace2m.replace(day=1)}",f"{hace2m.replace(day=30)}",
     f"{hace2m.replace(day=1)}",22000,660,0,22660,"EN_MORA",35,"","En mora 35 días. Mora 3%=660","2024-04-01"],
    ["CXC-003","CTR-002","INQ-002","Yessenia Mejía","INM-002","ALQUILER","Alquiler Mayo 2024",
     f"{hace1m.replace(day=1)}",f"{hace1m.replace(day=30)}",
     f"{hace1m.replace(day=1)}",22000,0,0,22000,"PENDIENTE",0,"","Pendiente del mes actual","2024-05-01"],
    ["CXC-004","CTR-003","INQ-003","Comercial El Éxito","INM-004","ALQUILER","Alquiler Abril 2024",
     f"{hace2m.replace(day=1)}",f"{hace2m.replace(day=30)}",
     f"{hace2m.replace(day=5)}",45000,2000,0,47000,"EN_MORA",40,"","En mora 40 días. Mora fija RD$2,000","2024-04-05"],
    ["CXC-005","CTR-003","INQ-003","Comercial El Éxito","INM-004","CARGO_EXTRA","Recibo agua marzo 2024",
     f"{hace2m}",f"{hace2m}",f"{hace2m}",1200,0,0,1200,"PENDIENTE",0,"","Cargo adicional agua","2024-03-31"],
    ["CXC-006","CTR-005","INQ-005","Fernando Peña","INM-006","ALQUILER","Alquiler Mayo 2024",
     f"{hace1m.replace(day=1)}",f"{hace1m.replace(day=30)}",
     f"{hace1m.replace(day=20)}",28000,0,5000,23000,"PENDIENTE",0,f"{hace45}","Abono parcial RD$5,000","2024-05-20"],
]
for i, row_data in enumerate(ejemplos6, 5):
    for j, val in enumerate(row_data, 1):
        c = ws6.cell(row=i, column=j, value=val)
        c.font = font(size=10, italic=True, color="555555")
        c.fill = hdr_fill(GRIS_HEADER)
        c.border = thin_border()
        c.alignment = left()

for r in range(11, 500):
    style_data_row(ws6, r, 19, [2, 3, 5, 6, 8, 9, 10, 11, 15])

add_dv(ws6, '"ALQUILER,CARGO_EXTRA"', "F11:F500", "Tipo cargo", "ALQUILER o CARGO_EXTRA", "Valor no válido")
add_dv(ws6, '"PENDIENTE,EN_MORA,PAGADO_PARCIAL"', "O11:O500", "Estado CxC", "Estado de la cuenta", "Valor no válido")

set_col_widths(ws6, {"A":12,"B":12,"C":12,"D":25,"E":12,"F":14,"G":30,"H":14,"I":14,"J":16,"K":20,"L":18,"M":18,"N":18,"O":14,"P":12,"Q":16,"R":30,"S":14})
ws6.freeze_panes = "A5"


# ════════════════════════════════════════════════════════════════════════════
# HOJA 7 — CXC PAGADAS / HISTORICO
# ════════════════════════════════════════════════════════════════════════════
ws7 = wb.create_sheet("✅ CXC_PAGADAS_HISTORICO")
ws7.sheet_view.showGridLines = False

ws7.merge_cells("A1:R1")
c = ws7["A1"]
c.value = "CxC PAGADAS / HISTÓRICO — Registros de cobros anteriores para cuadre contable y auditoría"
c.font = font(bold=True, color=BLANCO, size=12)
c.fill = hdr_fill("4A148C")
c.alignment = center()
ws7.row_dimensions[1].height = 30

ws7.merge_cells("A2:R2")
c = ws7["A2"]
c.value = "💡 USA ESTA PESTAÑA para el historial de pagos anteriores. Indica el ESTADO DE CUSTODIA según donde estén los fondos: COBRADO (en caja), EN_ADMINISTRACION (pendiente liquidar a propietario), LIQUIDADO (ya pagado al propietario)."
c.font = font(bold=True, color="4A148C", size=10)
c.fill = hdr_fill("EDE7F6")
c.alignment = center()
ws7.row_dimensions[2].height = 35

cols7 = [
    ("A","ID PAGO"),("B","ID CXC ORIGEN"),("C","ID CONTRATO *"),
    ("D","ID INQUILINO *"),("E","NOMBRE INQUILINO"),("F","ID INMUEBLE *"),
    ("G","ID SESIÓN CAJA"),("H","FECHA DE PAGO *"),("I","MONTO ALQUILER RD$ *"),
    ("J","MORA PAGADA RD$"),("K","TOTAL PAGADO RD$ *"),("L","FORMA DE PAGO *"),
    ("M","REFERENCIA / RECIBO"),("N","ESTADO CUSTODIA *"),("O","ID LIQUIDACIÓN"),
    ("P","NOTAS"),("Q","FECHA REGISTRO"),
]
make_header_row(ws7, 3, cols7)

descs7 = [
    "Auto-generado: PAG-001...",
    "ID de la CxC que origina este pago. Puede dejarse vacío en migraciones.",
    "ID del contrato relacionado. OBLIGATORIO. Ej: CTR-001",
    "ID del inquilino. OBLIGATORIO. Ej: INQ-001",
    "Nombre del inquilino (referencia).",
    "ID del inmueble. OBLIGATORIO. Ej: INM-001",
    "ID de la sesión de caja. Puedes usar MIGRACION como valor.",
    "Fecha en que se realizó el pago. Formato: AAAA-MM-DD. OBLIGATORIO.",
    "Monto de alquiler pagado (sin mora). OBLIGATORIO.",
    "Si pagó mora, cuánto. Si no, pon 0.",
    "Total cobrado = alquiler + mora. OBLIGATORIO.",
    "EFECTIVO, TRANSFERENCIA, TARJETA o CHEQUE. OBLIGATORIO.",
    "Número de recibo, referencia de transferencia o cheque. Opcional.",
    "COBRADO = en caja sin cuadrar | EN_ADMINISTRACION = cuadrado, pendiente liquidar | LIQUIDADO = ya pagado al propietario. OBLIGATORIO.",
    "Si ya fue liquidado, el ID de la liquidación. Sino, dejarlo vacío.",
    "Observaciones",
    "Fecha de registro en el sistema.",
]
make_desc_row(ws7, 4, descs7)

hace3m = hoy - datetime.timedelta(days=90)
hace4m = hoy - datetime.timedelta(days=120)

ejemplos7 = [
    ["PAG-0001","","CTR-001","INQ-001","Luis Miguel Ortega","INM-001","MIGRACION",f"{hace4m.replace(day=3)}",35000,0,35000,"TRANSFERENCIA","TRF-2024-03-001","LIQUIDADO","LIQ-001","Alquiler marzo 2024 - ya liquidado a propietario","2024-03-03"],
    ["PAG-0002","","CTR-001","INQ-001","Luis Miguel Ortega","INM-001","MIGRACION",f"{hace3m.replace(day=2)}",35000,0,35000,"TRANSFERENCIA","TRF-2024-04-001","LIQUIDADO","LIQ-001","Alquiler abril 2024 - ya liquidado","2024-04-02"],
    ["PAG-0003","","CTR-002","INQ-002","Yessenia Mejía","INM-002","MIGRACION",f"{hace3m.replace(day=5)}",22000,0,22000,"EFECTIVO","REC-0045","EN_ADMINISTRACION","","Alquiler abril 2024 - pendiente liquidar","2024-04-05"],
    ["PAG-0004","","CTR-003","INQ-003","Comercial El Éxito","INM-004","MIGRACION",f"{hace4m.replace(day=8)}",45000,0,45000,"CHEQUE","CHQ-1234","LIQUIDADO","LIQ-001","Alquiler marzo 2024 local comercial","2024-03-08"],
    ["PAG-0005","","CTR-004","INQ-006","Inversiones Norte","INM-005","MIGRACION",f"{hace3m.replace(day=1)}",60000,0,60000,"TRANSFERENCIA","TRF-ABR-INV","LIQUIDADO","LIQ-002","Alquiler abril - empresa","2024-04-01"],
    ["PAG-0006","","CTR-005","INQ-005","Fernando Peña","INM-006","MIGRACION",f"{hace3m.replace(day=21)}",28000,0,28000,"EFECTIVO","","EN_ADMINISTRACION","","Alquiler abril Santiago","2024-04-21"],
]
for i, row_data in enumerate(ejemplos7, 5):
    for j, val in enumerate(row_data, 1):
        c = ws7.cell(row=i, column=j, value=val)
        c.font = font(size=10, italic=True, color="555555")
        c.fill = hdr_fill(GRIS_HEADER)
        c.border = thin_border()
        c.alignment = left()

for r in range(11, 1000):
    style_data_row(ws7, r, 17, [3, 4, 6, 8, 9, 11, 12, 14])

add_dv(ws7, '"EFECTIVO,TRANSFERENCIA,TARJETA,CHEQUE"', "L11:L1000", "Forma pago", "Selecciona la forma de pago", "Valor no válido")
add_dv(ws7, '"COBRADO,EN_ADMINISTRACION,LIQUIDADO"', "N11:N1000", "Estado custodia",
       "COBRADO=en caja | EN_ADMINISTRACION=cuadrado | LIQUIDADO=pagado a propietario", "Valor no válido")

set_col_widths(ws7, {"A":12,"B":12,"C":12,"D":12,"E":25,"F":12,"G":14,"H":14,"I":18,"J":16,"K":18,"L":16,"M":22,"N":20,"O":14,"P":35,"Q":14})
ws7.freeze_panes = "A5"


# ════════════════════════════════════════════════════════════════════════════
# HOJA 8 — RESUMEN / CHECKLIST
# ════════════════════════════════════════════════════════════════════════════
ws8 = wb.create_sheet("✔ CHECKLIST")
ws8.sheet_view.showGridLines = False
ws8.column_dimensions["A"].width = 3
ws8.column_dimensions["B"].width = 40
ws8.column_dimensions["C"].width = 18
ws8.column_dimensions["D"].width = 45

ws8.merge_cells("B1:D1")
c = ws8["B1"]
c.value = "CHECKLIST DE CARGA — Estado del proceso de migración"
c.font = font(bold=True, color=BLANCO, size=13)
c.fill = hdr_fill(AZUL_DARK)
c.alignment = center()
ws8.row_dimensions[1].height = 36

headers_cl = ["PASO","COMPLETADO","NOTAS / RESULTADO"]
for j, h in enumerate(headers_cl, 2):
    c = ws8.cell(row=2, column=j, value=h)
    c.font = font(bold=True, color=BLANCO, size=10)
    c.fill = hdr_fill(AZUL_CORP)
    c.alignment = center()
    c.border = thin_border()
ws8.row_dimensions[2].height = 22

pasos = [
    ("Conexión API verificada (health check OK)","⬜ Pendiente",""),
    ("Google Sheet compartido con cuenta de servicio","⬜ Pendiente","inmuebles-api@ad-enterprise-498621.iam.gserviceaccount.com"),
    ("USUARIOS cargados en pestaña ROLES","⬜ Pendiente","Mínimo 1 ADMINISTRADOR"),
    ("PROPIETARIOS cargados en Google Sheets","⬜ Pendiente",""),
    ("INMUEBLES cargados en Google Sheets","⬜ Pendiente","Con coordenadas GPS para el mapa"),
    ("INQUILINOS cargados en Google Sheets","⬜ Pendiente",""),
    ("CONTRATOS cargados en Google Sheets","⬜ Pendiente","Estado VIGENTE para activos"),
    ("CxC PENDIENTES cargadas en Google Sheets","⬜ Pendiente","Solo los no pagados"),
    ("HISTÓRICO DE PAGOS cargado en Google Sheets","⬜ Pendiente","Para cuadre contable"),
    ("Login probado desde la web","⬜ Pendiente","http://localhost:5173/login"),
    ("Dashboard muestra los KPIs correctos","⬜ Pendiente",""),
    ("Mapa muestra los inmuebles con semáforo","⬜ Pendiente","Requiere token Mapbox"),
    ("Primera sesión de caja probada","⬜ Pendiente","Registrar un pago de prueba"),
    ("Token Mapbox configurado en .env.local","⬜ Pendiente","Gratis en mapbox.com"),
]
for i, (paso, estado, nota) in enumerate(pasos, 3):
    b = ws8.cell(row=i, column=2, value=paso)
    b.font = font(size=10)
    b.border = thin_border()
    b.alignment = left()
    b.fill = hdr_fill(BLANCO)
    ws8.row_dimensions[i].height = 22

    est_col = ["⬜ Pendiente","✅ Listo","❌ Error"]
    add_dv(ws8, '"⬜ Pendiente,✅ Listo,❌ Error"', f"C{i}", "Estado", "Marca el progreso", "Valor inválido")
    c2 = ws8.cell(row=i, column=3, value=estado)
    c2.font = font(size=10)
    c2.border = thin_border()
    c2.alignment = center()
    c2.fill = hdr_fill(AMARILLO)

    c3 = ws8.cell(row=i, column=4, value=nota)
    c3.font = font(size=10, italic=True)
    c3.border = thin_border()
    c3.alignment = left()
    c3.fill = hdr_fill(BLANCO)


# ─── Guardar ────────────────────────────────────────────────────────────────
output = r"C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Sistema de Condominios\web-app\CARGA_DATOS_INMUEBLES_RD.xlsx"
wb.save(output)
print(f"✅ Archivo creado: {output}")
