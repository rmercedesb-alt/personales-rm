from openpyxl import Workbook
from openpyxl.styles import (Font, PatternFill, Alignment, Border, Side,
                              GradientFill)
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.datavalidation import DataValidation

OUT = r'C:\Personales RM\Claude\Sistema de Condominios\web-app\Plantilla_Carga_Maestras_InmueblesRD.xlsx'

# ── Paleta ──────────────────────────────────────────────────────────────────
AZUL       = '1B4F8C'
AZUL_FONT  = 'FFFFFF'
AMARILLO   = 'FFFACD'
GRIS_INS   = 'F1F5F9'
GRIS_ID    = 'E2E8F0'
VERDE_TAB  = '16A34A'
BORDE_CLR  = 'CBD5E1'

def thin_border():
    s = Side(style='thin', color=BORDE_CLR)
    return Border(left=s, right=s, top=s, bottom=s)

def hdr_font():  return Font(name='Arial', bold=True, color=AZUL_FONT, size=10)
def body_font(): return Font(name='Arial', size=10)
def ins_font():  return Font(name='Arial', italic=True, size=9, color='475569')
def title_font(size=14): return Font(name='Arial', bold=True, size=size, color=AZUL)

def hdr_fill():  return PatternFill('solid', fgColor=AZUL)
def ej_fill():   return PatternFill('solid', fgColor=AMARILLO)
def ins_fill():  return PatternFill('solid', fgColor=GRIS_INS)
def id_fill():   return PatternFill('solid', fgColor=GRIS_ID)

def center(): return Alignment(horizontal='center', vertical='center', wrap_text=True)
def left():   return Alignment(horizontal='left',   vertical='center', wrap_text=True)

# ── Helpers ──────────────────────────────────────────────────────────────────
def add_sheet(wb, name, tab_color):
    ws = wb.create_sheet(name)
    ws.sheet_properties.tabColor = tab_color
    return ws

def write_instruction_row(ws, col_count, text):
    ws.row_dimensions[1].height = 36
    ws.merge_cells(start_row=1, start_column=1, end_row=1, end_column=col_count)
    c = ws.cell(1, 1, text)
    c.font  = ins_font()
    c.fill  = ins_fill()
    c.alignment = left()
    c.border = thin_border()

def write_header_row(ws, cols, row=2):
    ws.row_dimensions[row].height = 32
    for ci, (hdr, comment_txt) in enumerate(cols, 1):
        c = ws.cell(row, ci, hdr)
        c.font      = hdr_font()
        c.fill      = hdr_fill()
        c.alignment = center()
        c.border    = thin_border()
        if comment_txt:
            from openpyxl.comments import Comment
            c.comment = Comment(comment_txt, 'Sistema')

def write_example_rows(ws, rows, start_row=3, id_col=1):
    for ri, row_data in enumerate(rows):
        r = start_row + ri
        ws.row_dimensions[r].height = 20
        for ci, val in enumerate(row_data, 1):
            c = ws.cell(r, ci, val)
            fill = id_fill() if ci == id_col else ej_fill()
            c.fill      = fill
            c.font      = body_font()
            c.alignment = left()
            c.border    = thin_border()
            if ci == id_col:
                c.font = Font(name='Arial', size=10, color='94A3B8',
                              strike=True, italic=True)

def set_col_widths(ws, widths):
    for ci, w in enumerate(widths, 1):
        ws.column_dimensions[get_column_letter(ci)].width = w

def add_autofilter(ws, col_count, row=2):
    ws.auto_filter.ref = f"A{row}:{get_column_letter(col_count)}{row}"

def freeze_at(ws, row=3):
    ws.freeze_panes = f'A{row}'

def add_dv(ws, formula, col, first_row=3, last_row=1000):
    dv = DataValidation(type='list', formula1=formula, allow_blank=True,
                        showErrorMessage=True, errorTitle='Valor inválido',
                        error='Seleccione un valor de la lista')
    ws.add_data_validation(dv)
    col_l = get_column_letter(col)
    dv.sqref = f'{col_l}{first_row}:{col_l}{last_row}'


# ════════════════════════════════════════════════════════════════════════════
wb = Workbook()
wb.remove(wb.active)

# ══════════════════════════════════════════════════════════════════════════
# HOJA 0 – INSTRUCCIONES
# ══════════════════════════════════════════════════════════════════════════
ws0 = add_sheet(wb, '📋 INSTRUCCIONES', VERDE_TAB)
ws0.column_dimensions['A'].width = 4
ws0.column_dimensions['B'].width = 6
ws0.column_dimensions['C'].width = 55
ws0.column_dimensions['D'].width = 35
ws0.column_dimensions['E'].width = 35

# Título
ws0.row_dimensions[1].height = 12
ws0.row_dimensions[2].height = 52
ws0.merge_cells('B2:E2')
c = ws0['B2']
c.value     = '📊  Plantilla de Carga de Maestras\nSistema de Administración de Inmuebles RD'
c.font      = Font(name='Arial', bold=True, size=18, color=AZUL)
c.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)

ws0.row_dimensions[3].height = 22
ws0.merge_cells('B3:E3')
c = ws0['B3']
c.value     = 'Complete cada hoja en el orden indicado y respete los formatos solicitados.'
c.font      = Font(name='Arial', italic=True, size=11, color='475569')
c.alignment = Alignment(horizontal='left', vertical='center')

# Separador
ws0.row_dimensions[4].height = 8

# Título pasos
ws0.row_dimensions[5].height = 24
ws0.merge_cells('B5:E5')
c = ws0['B5']
c.value = 'INSTRUCCIONES DE USO'
c.font  = Font(name='Arial', bold=True, size=12, color=AZUL)
c.fill  = PatternFill('solid', fgColor='EFF6FF')
c.alignment = Alignment(horizontal='left', vertical='center')
c.border = thin_border()

pasos = [
    ('1', 'Orden de carga',
     'Llena las hojas en este orden: PROPIETARIOS → INQUILINOS → INMUEBLES → CONTRATOS → CxC_CARGOS. '
     'Los IDs de una hoja son referenciados por las siguientes.'),
    ('2', 'Campos obligatorios',
     'Los campos con (*) son obligatorios. Sin ellos el sistema rechazará el registro.'),
    ('3', 'Campo ID',
     'La columna "Id" se genera AUTOMÁTICAMENTE por el sistema. Déjala VACÍA. '
     'Aparece en gris/tachado como recordatorio visual.'),
    ('4', 'Formatos de fecha',
     'Las fechas deben estar en formato YYYY-MM-DD (ej: 2026-06-15). '
     'Formatea las celdas como Texto si Excel las convierte automáticamente.'),
    ('5', 'Listas desplegables',
     'Los campos con dropdown solo aceptan los valores de la lista. '
     'Haz clic en la celda para ver las opciones disponibles.'),
    ('6', 'Filas de ejemplo',
     'Las filas en AMARILLO son ejemplos de referencia. '
     'BÓRRALAS antes de cargar tus datos reales al sistema.'),
    ('7', 'Coordenadas GPS',
     'Para obtener latitud/longitud: abre Google Maps → clic derecho en la ubicación → '
     'copia el primer número (latitud) y el segundo (longitud, negativo para RD).'),
    ('8', 'Formato de cédula',
     'Ingresa las cédulas en formato 000-0000000-0 incluyendo los guiones. '
     'Formatea esas celdas como Texto para evitar que Excel elimine los ceros.'),
    ('9', 'IDs de referencia',
     'En las hojas INMUEBLES, CONTRATOS y CxC deberás colocar los IDs generados '
     'por el sistema (ej: PROP-001, INQ-001). Primero carga la maestra anterior y '
     'obtén los IDs antes de continuar con la siguiente.'),
    ('10', 'Entrega',
     'Una vez completa, comparte este archivo con el administrador del sistema '
     'para proceder con la importación masiva de datos.'),
]

for i, (num, titulo, desc) in enumerate(pasos):
    r = 6 + i
    ws0.row_dimensions[r].height = 42
    c_num = ws0.cell(r, 2, num)
    c_num.font      = Font(name='Arial', bold=True, size=11, color=AZUL_FONT)
    c_num.fill      = hdr_fill()
    c_num.alignment = center()
    c_num.border    = thin_border()

    c_tit = ws0.cell(r, 3, titulo)
    c_tit.font      = Font(name='Arial', bold=True, size=10, color=AZUL)
    c_tit.fill      = PatternFill('solid', fgColor='EFF6FF')
    c_tit.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
    c_tit.border    = thin_border()

    ws0.merge_cells(start_row=r, start_column=4, end_row=r, end_column=5)
    c_desc = ws0.cell(r, 4, desc)
    c_desc.font      = Font(name='Arial', size=9, color='334155')
    c_desc.fill      = PatternFill('solid', fgColor='F8FAFC')
    c_desc.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
    c_desc.border    = thin_border()

# Separador
ws0.row_dimensions[16].height = 12

# Tabla resumen
ws0.row_dimensions[17].height = 24
hdrs_res = ['Hoja', 'Descripción', 'Campos obligatorios']
fills_h  = [hdr_fill(), hdr_fill(), hdr_fill()]
for ci, h in enumerate(hdrs_res, 3):
    c = ws0.cell(17, ci, h)
    c.font = hdr_font(); c.fill = hdr_fill()
    c.alignment = center(); c.border = thin_border()

resumen = [
    ('PROPIETARIOS',  'Dueños de los inmuebles con datos bancarios y comisión',
     'Nombre, TipoComision'),
    ('INQUILINOS',    'Personas que arriendan los inmuebles',
     'Nombre'),
    ('INMUEBLES',     'Propiedades disponibles para alquiler',
     'IdPropietario, Tipo, Descripcion, Direccion, Ciudad, MontoAlquiler'),
    ('CONTRATOS',     'Acuerdos de arrendamiento entre propietario e inquilino',
     'IdInmueble, IdInquilino, FechaInicio, FechaFin, MontoAlquiler, Periodicidad, DiaCobro, DiasGracia, TipoMora'),
    ('CxC_CARGOS',   'Cuentas por cobrar / cargos generados por contrato',
     'IdInquilino, IdInmueble, TipoCargo, Descripcion, PeriodoInicio, PeriodoFin, FechaVencimiento, MontoOriginal'),
]

for ri, (h, d, obl) in enumerate(resumen):
    r = 18 + ri
    ws0.row_dimensions[r].height = 32
    for ci, val in enumerate([h, d, obl], 3):
        c = ws0.cell(r, ci, val)
        c.font      = Font(name='Arial', size=9, color='1E293B',
                           bold=(ci == 3))
        c.fill      = PatternFill('solid', fgColor='F8FAFC' if ri % 2 == 0 else 'FFFFFF')
        c.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
        c.border    = thin_border()

# Advertencia
ws0.row_dimensions[24].height = 12
ws0.row_dimensions[25].height = 36
ws0.merge_cells('B25:E25')
c = ws0['B25']
c.value     = ('⚠️  IMPORTANTE: No cambies los nombres de las hojas ni el orden de las columnas. '
               'El sistema importa datos por posición de columna.')
c.font      = Font(name='Arial', bold=True, size=10, color='92400E')
c.fill      = PatternFill('solid', fgColor='FEF3C7')
c.alignment = Alignment(horizontal='left', vertical='center', wrap_text=True)
c.border    = Border(
    left  =Side(style='medium', color='F59E0B'),
    right =Side(style='medium', color='F59E0B'),
    top   =Side(style='medium', color='F59E0B'),
    bottom=Side(style='medium', color='F59E0B'),
)


# ══════════════════════════════════════════════════════════════════════════
# HOJA 1 – PROPIETARIOS
# ══════════════════════════════════════════════════════════════════════════
ws1 = add_sheet(wb, '👤 PROPIETARIOS', AZUL)

cols1 = [
    ('Id',                 'Auto-generado por el sistema. Dejar VACÍO.'),
    ('Nombre *',           'Nombre completo del propietario. Requerido.'),
    ('Cedula',             'Cédula dominicana. Formato: 000-0000000-0\nFormatea la celda como Texto.'),
    ('Telefono',           'Teléfono. Formato: 809-000-0000'),
    ('Email',              'Correo electrónico del propietario.'),
    ('Banco',              'Banco donde tiene la cuenta. Seleccionar de la lista.'),
    ('NumeroCuenta',       'Número de cuenta bancaria completo.'),
    ('TipoCuenta',         'Tipo de cuenta bancaria. Lista: AHORRO / CORRIENTE / NOMINA'),
    ('TipoComision',       'Cómo se calcula la comisión. Lista: PORCENTAJE / FIJO / MIXTO'),
    ('ComisionPorcentaje', 'Porcentaje de comisión (ej: 10 = 10%). Solo si TipoComision = PORCENTAJE o MIXTO. Poner 0 si no aplica.'),
    ('ComisionFija',       'Monto fijo de comisión en RD$. Solo si TipoComision = FIJO o MIXTO. Poner 0 si no aplica.'),
    ('Notas',              'Observaciones adicionales del propietario. Campo libre.'),
]

write_instruction_row(ws1, len(cols1),
    'ℹ️  PROPIETARIOS — Registra los dueños de los inmuebles. Llena esta hoja PRIMERO. '
    'Los campos marcados con (*) son obligatorios. La columna "Id" se genera automáticamente — déjala vacía.')
write_header_row(ws1, cols1)

ej1 = [
    ['', 'José Mercedes Báez', '001-1234567-8', '809-555-1234', 'jose.mercedes@gmail.com',
     'BanReservas', '3100012345678', 'AHORRO', 'PORCENTAJE', 10, 0,
     'Propietario del edificio Torre Azul, La Romana'],
    ['', 'María Altagracia Rodríguez', '002-9876543-2', '809-777-5678', 'maria.rodriguez@hotmail.com',
     'Popular', '7200098765432', 'CORRIENTE', 'FIJO', 0, 5000,
     'Cobra comisión fija de RD$5,000 por mes'],
    ['', 'Carlos Ernesto Reyes Pérez', '003-4567890-1', '829-333-9012', 'c.reyes@empresa.com.do',
     'BHD León', '5300045678901', 'AHORRO', 'MIXTO', 5, 2000,
     'Comisión mixta: 5% + RD$2,000 por propiedad'],
]
write_example_rows(ws1, ej1)

set_col_widths(ws1, [12, 28, 18, 16, 30, 14, 18, 13, 15, 18, 15, 40])
add_autofilter(ws1, len(cols1))
freeze_at(ws1, 3)

# Dropdowns hoja 1
add_dv(ws1, '"BanReservas,Banesco,Popular,BHD León,Scotiabank,Santa Cruz,Caribe,López de Haro,Otro"', 6)
add_dv(ws1, '"AHORRO,CORRIENTE,NOMINA"', 8)
add_dv(ws1, '"PORCENTAJE,FIJO,MIXTO"', 9)


# ══════════════════════════════════════════════════════════════════════════
# HOJA 2 – INQUILINOS
# ══════════════════════════════════════════════════════════════════════════
ws2 = add_sheet(wb, '👥 INQUILINOS', AZUL)

cols2 = [
    ('Id',                  'Auto-generado por el sistema. Dejar VACÍO.'),
    ('Nombre *',            'Nombre completo del inquilino. Requerido.'),
    ('Cedula',              'Cédula dominicana. Formato: 000-0000000-0\nFormatea la celda como Texto.'),
    ('Telefono',            'Teléfono principal del inquilino.'),
    ('Email',               'Correo electrónico del inquilino.'),
    ('Ocupacion',           'Ocupación o empresa donde trabaja el inquilino.'),
    ('ReferenciaPersonal',  'Nombre completo de una persona de referencia.'),
    ('TelefonoReferencia',  'Teléfono de la persona de referencia.'),
    ('Notas',               'Observaciones adicionales. Campo libre.'),
]

write_instruction_row(ws2, len(cols2),
    'ℹ️  INQUILINOS — Registra las personas que arriendan los inmuebles. '
    'Llena esta hoja DESPUÉS de PROPIETARIOS. La columna "Id" se genera automáticamente — déjala vacía.')
write_header_row(ws2, cols2)

ej2 = [
    ['', 'Ana Yolanda Fernández Cruz', '402-1234567-8', '809-444-2345',
     'ana.fernandez@gmail.com', 'Maestra del MINERD', 'Pedro García', '829-111-2222',
     'Inquilina puntual, lleva 3 años en el apartamento'],
    ['', 'Luis Antonio Tejada Méndez', '222-9876543-2', '829-666-7890',
     'luistejada@hotmail.com', 'Empleado de Claro RD', 'Carmen Méndez', '809-333-4444',
     'Contrato renovado por segundo año consecutivo'],
    ['', 'Rosa Belén Almonte Vargas', '001-4567890-5', '849-888-0123',
     'rosa.almonte@yahoo.com', 'Dueña de colmado', 'Juan Almonte', '809-555-6666',
     'Requirió mes de garantía adicional por primer contrato'],
]
write_example_rows(ws2, ej2)

set_col_widths(ws2, [12, 28, 18, 16, 30, 30, 25, 18, 40])
add_autofilter(ws2, len(cols2))
freeze_at(ws2, 3)


# ══════════════════════════════════════════════════════════════════════════
# HOJA 3 – INMUEBLES
# ══════════════════════════════════════════════════════════════════════════
ws3 = add_sheet(wb, '🏢 INMUEBLES', AZUL)

cols3 = [
    ('Id',            'Auto-generado por el sistema. Dejar VACÍO.'),
    ('IdPropietario *', 'ID del propietario dueño del inmueble.\nEj: PROP-001\nDebe existir en la hoja PROPIETARIOS o en el sistema.'),
    ('Tipo *',        'Tipo de inmueble. Seleccionar de la lista:\nAPARTAMENTO / CASA / LOCAL / OFICINA / VILLA / HABITACION / OTRO'),
    ('Descripcion *', 'Descripción clara del inmueble.\nEj: Apto 2BR Torre Caribeña Piso 3'),
    ('Direccion *',   'Dirección física completa del inmueble.'),
    ('Sector',        'Sector, barrio o urbanización donde está ubicado.'),
    ('Ciudad *',      'Ciudad. Seleccionar de la lista.'),
    ('Latitud',       'Coordenada GPS de latitud.\nEj: 18.427000\nPara RD los valores están entre 17.5 y 19.9\nUsa Google Maps: clic derecho → copiar coordenadas'),
    ('Longitud',      'Coordenada GPS de longitud.\nEj: -68.972000\nPara RD los valores son NEGATIVOS entre -68 y -72\nUsa Google Maps: clic derecho → copiar coordenadas'),
    ('MontoAlquiler *', 'Monto mensual del alquiler en pesos dominicanos (RD$). Sin símbolos ni comas.'),
    ('Notas',         'Observaciones adicionales del inmueble. Campo libre.'),
]

write_instruction_row(ws3, len(cols3),
    'ℹ️  INMUEBLES — Registra cada propiedad disponible para alquiler. '
    'Llena esta hoja DESPUÉS de PROPIETARIOS. Para las coordenadas GPS: en Google Maps, clic derecho en la ubicación y copia los números. '
    'La columna "Id" se genera automáticamente — déjala vacía.')
write_header_row(ws3, cols3)

ej3 = [
    ['', 'PROP-001', 'APARTAMENTO', 'Apto 2BR Torre Caribeña Piso 3',
     'C/ Duarte #45 Edif. Torre Caribeña', 'Centro', 'La Romana',
     18.427000, -68.972000, 25000, 'Incluye agua y electricidad básica hasta RD$800'],
    ['', 'PROP-001', 'CASA', 'Casa 3BR con patio Urb. Las Palmas',
     'Calle Las Palmas #12', 'Las Palmas', 'La Romana',
     18.435000, -68.968000, 35000, 'Recientemente remodelada. Parqueo doble.'],
    ['', 'PROP-002', 'LOCAL', 'Local Comercial Planta Baja Av. Libertad',
     'Av. Libertad #78', 'El Seibo', 'La Altagracia',
     18.765000, -68.723000, 45000, 'Zona comercial de alta circulación. 80 m².'],
]
write_example_rows(ws3, ej3)

set_col_widths(ws3, [12, 15, 14, 32, 35, 20, 16, 12, 12, 15, 38])
add_autofilter(ws3, len(cols3))
freeze_at(ws3, 3)

add_dv(ws3, '"APARTAMENTO,CASA,LOCAL,OFICINA,VILLA,HABITACION,OTRO"', 3)
add_dv(ws3, '"La Romana,La Altagracia,San Pedro de Macorís,Santo Domingo,Santiago,Puerto Plata,Otra"', 7)


# ══════════════════════════════════════════════════════════════════════════
# HOJA 4 – CONTRATOS
# ══════════════════════════════════════════════════════════════════════════
ws4 = add_sheet(wb, '📋 CONTRATOS', AZUL)

cols4 = [
    ('Id',             'Auto-generado por el sistema. Dejar VACÍO.'),
    ('IdInmueble *',   'ID del inmueble arrendado.\nEj: INM-001\nDebe existir en la hoja INMUEBLES o en el sistema.'),
    ('IdInquilino *',  'ID del inquilino que firma el contrato.\nEj: INQ-001\nDebe existir en la hoja INQUILINOS o en el sistema.'),
    ('FechaInicio *',  'Fecha de inicio del contrato.\nFormato: YYYY-MM-DD\nEj: 2026-01-01'),
    ('FechaFin *',     'Fecha de vencimiento del contrato.\nFormato: YYYY-MM-DD\nEj: 2027-01-01'),
    ('MontoAlquiler *','Monto pactado de alquiler en RD$. Sin símbolos ni comas.'),
    ('Periodicidad *', 'Frecuencia de cobro. Lista:\nMENSUAL / BIMESTRAL / TRIMESTRAL / ANUAL'),
    ('DiaCobro *',     'Día del mes en que vence el pago.\nEj: 1 = día 1 de cada mes\nNúmero del 1 al 31.'),
    ('DiasGracia *',   'Días de gracia antes de aplicar mora.\nEj: 5 = 5 días después del vencimiento.'),
    ('TipoMora *',     'Cómo se calcula la mora. Lista:\nPORCENTAJE / FIJO / NINGUNO'),
    ('MoraPorcentaje', 'Porcentaje de mora mensual (ej: 5 = 5%).\nSolo si TipoMora = PORCENTAJE. Poner 0 si no aplica.'),
    ('MontoMoraFija',  'Monto fijo de mora en RD$.\nSolo si TipoMora = FIJO. Poner 0 si no aplica.'),
    ('Deposito',       'Monto del depósito de garantía cobrado al inquilino en RD$.\nGeneralmente equivale a 1 mes de alquiler.'),
    ('Notas',          'Observaciones adicionales del contrato. Campo libre.'),
]

write_instruction_row(ws4, len(cols4),
    'ℹ️  CONTRATOS — Registra los acuerdos de arrendamiento. '
    'Llena esta hoja DESPUÉS de INMUEBLES e INQUILINOS. '
    'Usa el formato YYYY-MM-DD para todas las fechas. La columna "Id" se genera automáticamente — déjala vacía.')
write_header_row(ws4, cols4)

ej4 = [
    ['', 'INM-001', 'INQ-001', '2026-01-01', '2027-01-01',
     25000, 'MENSUAL', 1, 5, 'PORCENTAJE', 5, 0, 25000,
     'Contrato anual renovado. Primera renovación.'],
    ['', 'INM-002', 'INQ-002', '2026-02-01', '2027-02-01',
     35000, 'MENSUAL', 5, 3, 'FIJO', 0, 1500, 35000,
     'Primer contrato. Depósito pagado en dos cuotas.'],
    ['', 'INM-003', 'INQ-003', '2026-03-01', '2026-09-01',
     45000, 'MENSUAL', 1, 7, 'PORCENTAJE', 3, 0, 45000,
     'Contrato de 6 meses con opción a renovación anual'],
]
write_example_rows(ws4, ej4)

set_col_widths(ws4, [12, 14, 14, 14, 14, 15, 13, 10, 11, 13, 15, 14, 12, 38])
add_autofilter(ws4, len(cols4))
freeze_at(ws4, 3)

add_dv(ws4, '"MENSUAL,BIMESTRAL,TRIMESTRAL,ANUAL"', 7)
add_dv(ws4, '"PORCENTAJE,FIJO,NINGUNO"', 10)


# ══════════════════════════════════════════════════════════════════════════
# HOJA 5 – CxC_CARGOS
# ══════════════════════════════════════════════════════════════════════════
ws5 = add_sheet(wb, '💰 CxC_CARGOS', AZUL)

cols5 = [
    ('Id',               'Auto-generado por el sistema. Dejar VACÍO.'),
    ('IdContrato',       'ID del contrato asociado.\nEj: CTR-001\nPuede dejarse vacío si es un cargo manual sin contrato.'),
    ('IdInquilino *',    'ID del inquilino al que se le cobra.\nEj: INQ-001'),
    ('IdInmueble *',     'ID del inmueble al que aplica el cargo.\nEj: INM-001'),
    ('TipoCargo *',      'Tipo de cargo. Lista:\nALQUILER / MORA / DEPOSITO / CARGO_EXTRA / OTRO'),
    ('Descripcion *',    'Descripción del cargo.\nEj: Alquiler enero 2026\nEj: Mora por retraso febrero 2026'),
    ('PeriodoInicio *',  'Inicio del período facturado.\nFormato: YYYY-MM-DD\nEj: 2026-01-01'),
    ('PeriodoFin *',     'Fin del período facturado.\nFormato: YYYY-MM-DD\nEj: 2026-01-31'),
    ('FechaVencimiento *','Fecha límite para pagar este cargo.\nFormato: YYYY-MM-DD'),
    ('MontoOriginal *',  'Monto total a cobrar en RD$. Sin símbolos ni comas.'),
    ('Estado *',         'Estado del cargo. Lista:\nPENDIENTE / EN_MORA / PAGADO / PAGADO_PARCIAL'),
    ('Notas',            'Observaciones adicionales. Campo libre.'),
]

write_instruction_row(ws5, len(cols5),
    'ℹ️  CxC_CARGOS — Cuentas por cobrar y cargos pendientes. '
    'Llena esta hoja ÚLTIMA, después de CONTRATOS. '
    'Para alquileres regulares el sistema los genera automáticamente; usa esta hoja para cargos históricos o manuales. '
    'La columna "Id" se genera automáticamente — déjala vacía.')
write_header_row(ws5, cols5)

ej5 = [
    ['', 'CTR-001', 'INQ-001', 'INM-001', 'ALQUILER',
     'Alquiler enero 2026', '2026-01-01', '2026-01-31', '2026-01-01',
     25000, 'PENDIENTE', 'Pendiente de cobro mes de enero'],
    ['', 'CTR-002', 'INQ-002', 'INM-002', 'ALQUILER',
     'Alquiler febrero 2026', '2026-02-01', '2026-02-28', '2026-02-05',
     35000, 'EN_MORA', 'Vencida con 15 días de mora acumulada'],
    ['', '', 'INQ-003', 'INM-003', 'CARGO_EXTRA',
     'Pintura exterior solicitada por propietario', '2026-03-01', '2026-03-31', '2026-03-15',
     8000, 'PENDIENTE', 'Cargo manual autorizado por propietario el 2026-02-20'],
]
write_example_rows(ws5, ej5)

set_col_widths(ws5, [12, 12, 12, 12, 14, 38, 14, 14, 16, 15, 16, 38])
add_autofilter(ws5, len(cols5))
freeze_at(ws5, 3)

add_dv(ws5, '"ALQUILER,MORA,DEPOSITO,CARGO_EXTRA,OTRO"', 5)
add_dv(ws5, '"PENDIENTE,EN_MORA,PAGADO,PAGADO_PARCIAL"', 11)


# ══════════════════════════════════════════════════════════════════════════
# Guardar
# ══════════════════════════════════════════════════════════════════════════
wb.active = ws0
wb.save(OUT)
print(f'OK: {OUT}')
