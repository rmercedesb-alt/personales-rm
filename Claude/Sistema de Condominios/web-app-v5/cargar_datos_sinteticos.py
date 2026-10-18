"""
1. Borra todos los datos existentes en Google Sheets (filas 2+)
2. Genera datos sintéticos completos y los sube
   - 20 propietarios
   - 300 inmuebles (70% La Romana, 30% La Altagracia)
   - 200 inquilinos / 200 contratos / 100 inmuebles disponibles
   - CxC Ene-Jun 2026 (12% atrasos), pagos con 5% mora en tardíos
"""
import random, datetime, calendar, sys, io, time
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
random.seed(42)

from google.oauth2 import service_account
from googleapiclient.discovery import build

SPREADSHEET_ID   = '1Ir6aKzaL8HRSi0v8lUMa_1KYTQc0r4gtTVwHiHiYbAU'
CREDENTIALS_PATH = r'C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Sistema de Condominios\web-app\InmueblesAPI\credentials.json'

HOY    = datetime.date(2026, 6, 14)
MESES  = list(range(1, 7))          # Ene-Jun 2026

# ── Auth ────────────────────────────────────────────────────────────────────
creds   = service_account.Credentials.from_service_account_file(
    CREDENTIALS_PATH, scopes=['https://www.googleapis.com/auth/spreadsheets'])
svc     = build('sheets', 'v4', credentials=creds)
sheets  = svc.spreadsheets()

TABS = {
    'propietarios':  '👤 PROPIETARIOS',
    'inmuebles':     '🏢 INMUEBLES',
    'inquilinos':    '👥 INQUILINOS',
    'contratos':     '📋 CONTRATOS',
    'cxc_cargos':   '💰 CXC_CARGOS',
    'cxc_pagos':    '💳 CXC_PAGOS',
    'sesiones':     '🏧 SESIONES_CAJA',
    'liquidaciones':'🔄 LIQUIDACIONES',
    'log_custodia': '📊 LOG_CUSTODIA',
}

PAUSA = 0.8   # segundos entre llamadas

def sheets_clear(tab):
    try:
        info = sheets.values().get(
            spreadsheetId=SPREADSHEET_ID, range=f"'{tab}'!A2:ZZ"
        ).execute()
        rows = info.get('values', [])
        if not rows:
            print(f'  {tab}: ya vacía')
            return
        sheets.values().clear(
            spreadsheetId=SPREADSHEET_ID, range=f"'{tab}'!A2:ZZ"
        ).execute()
        print(f'  {tab}: {len(rows)} filas borradas')
        time.sleep(PAUSA)
    except Exception as e:
        print(f'  ⚠ {tab}: {e}')

def sheets_append(tab, rows, batch=100):
    total = 0
    for i in range(0, len(rows), batch):
        chunk = rows[i:i+batch]
        sheets.values().append(
            spreadsheetId=SPREADSHEET_ID,
            range=f"'{tab}'!A:A",
            valueInputOption='USER_ENTERED',
            insertDataOption='INSERT_ROWS',
            body={'values': [[str(v) if v is not None else '' for v in r] for r in chunk]}
        ).execute()
        total += len(chunk)
        print(f'    {int(total/len(rows)*100)}% ({total}/{len(rows)})', end='\r')
        time.sleep(PAUSA)
    print(f'    ✓ {len(rows)} filas → {tab}                  ')

def fmt(d):
    if d is None: return ''
    return d.strftime('%Y-%m-%d')

# ── Catálogos ────────────────────────────────────────────────────────────────
NOMBRES_M = ['José','Juan','Carlos','Miguel','Rafael','Pedro','Luis','Antonio',
             'Manuel','Francisco','Ramón','Fernando','Augusto','Héctor','Eduardo',
             'Roberto','Víctor','Orlando','Diego','Andrés','Raúl','Alberto',
             'Nelson','Rubén','Ángel','Cristian','Elvis','Isaías','Wilkin','Yorki',
             'Richard','Edgardo','Bienvenido','Enmanuel','Franklin']
NOMBRES_F = ['María','Ana','Carmen','Rosa','Elena','Isabel','Luz','Patricia',
             'Sandra','Margarita','Altagracia','Mercedes','Yolanda','Marlene',
             'Yessenia','Lisette','Karina','Rosario','Beatriz','Claudia',
             'Xiomara','Dolores','Juana','Esperanza','Gisela','Leidi','Dasiris',
             'Rossenailys','Milagros','Germania','Arelis','Glenny','Darlenis']
APELLIDOS = ['García','Rodríguez','Martínez','López','González','Pérez','Sánchez',
             'Ramírez','Torres','Flores','Rivera','Jiménez','Morales','Herrera',
             'Medina','Cruz','Reyes','Vargas','Castillo','Cabrera','Mercedes',
             'Féliz','Matos','Espinal','Almonte','Disla','Polanco','Encarnación',
             'Marte','Ureña','De León','Báez','Taveras','Guzmán','Pichardo',
             'Ortega','Montero','Valdez','Concepción','Nolasco','Carpio','Abreu']
BANCOS = ['BanReservas','Popular','BHD León','Scotiabank','Banistmo','Asociación Cibao']

def nombre_completo():
    if random.random() < 0.5:
        return f"{random.choice(NOMBRES_M)} {random.choice(APELLIDOS)} {random.choice(APELLIDOS)}"
    return f"{random.choice(NOMBRES_F)} {random.choice(APELLIDOS)} {random.choice(APELLIDOS)}"

def cedula():
    return f"{random.randint(1,402):03d}-{random.randint(1000000,9999999)}-{random.randint(0,9)}"

def telefono():
    area = random.choice(['809','829','849'])
    return f"{area}-{random.randint(200,999)}-{random.randint(1000,9999)}"

def cuenta():
    return f"{random.randint(1,9):02d}-{random.randint(100,999)}-{random.randint(10000000,99999999)}-{random.randint(0,9)}"

# ── Coordenadas ──────────────────────────────────────────────────────────────
SECTORES_LR = [
    'La Romana Centro','Villa Hermosa','Barrio Blanco','Santa Rosa',
    'Los Americanos','La Unión','Residencial Independencia','Barrio Obrero',
    'El Caliche','La Cumbre','Res. Los Alamos','Ensanche Libertad',
]
SECTORES_LA = [
    'Higüey Centro','Bávaro','Punta Cana','Las Américas',
    'Verón','La Otra Banda','Los Palmares','El Seibo',
]

def coords_lr():
    return round(random.uniform(18.38, 18.52), 6), round(random.uniform(-69.05, -68.82), 6)
def coords_la():
    return round(random.uniform(18.50, 18.82), 6), round(random.uniform(-68.95, -68.45), 6)

TIPOS_INM   = ['APARTAMENTO','APARTAMENTO','APARTAMENTO','CASA','CASA','LOCAL','OFICINA']
PREFIJOS_INM = {
    'APARTAMENTO': ['Apto','Apartamento','Suite','Piso'],
    'CASA':        ['Casa','Residencia','Villa'],
    'LOCAL':       ['Local','Plaza','Comercial'],
    'OFICINA':     ['Oficina','Suite Ejecutiva','Of.'],
}

def monto_por_tipo(tipo):
    rangos = {
        'APARTAMENTO': (8000,  45000),
        'CASA':        (12000, 60000),
        'LOCAL':       (15000, 80000),
        'OFICINA':     (20000, 70000),
    }
    lo, hi = rangos[tipo]
    return round(random.randint(lo, hi) / 500) * 500   # múltiplo de 500

# ════════════════════════════════════════════════════════════════════════════
# PASO 1 — BORRAR DATOS EXISTENTES
# ════════════════════════════════════════════════════════════════════════════
print('\n══════════════════════════════════════════════')
print('PASO 1 — BORRAR DATOS EXISTENTES')
print('══════════════════════════════════════════════')
for tab in TABS.values():
    sheets_clear(tab)

# ════════════════════════════════════════════════════════════════════════════
# PASO 2 — GENERAR DATOS
# ════════════════════════════════════════════════════════════════════════════
print('\n══════════════════════════════════════════════')
print('PASO 2 — GENERANDO DATOS SINTÉTICOS')
print('══════════════════════════════════════════════')

# ── Propietarios (20) ────────────────────────────────────────────────────────
propietarios = []
for i in range(1, 21):
    pid  = f'PROP-{i:03d}'
    tipo = random.choice(['PORCENTAJE','PORCENTAJE','PORCENTAJE','FIJO','MIXTO'])
    pct  = 10 if tipo in ('PORCENTAJE','MIXTO') else ''
    fijo = random.choice([3000,4000,5000]) if tipo in ('FIJO','MIXTO') else ''
    propietarios.append([
        pid, nombre_completo(), cedula(), telefono(),
        f'prop{i}@correo.com', random.choice(BANCOS), cuenta(), 'AHORRO',
        tipo, pct, fijo, 'ACTIVO', f'Propietario #{i}', '2024-01-01'
    ])
print(f'  ✓ {len(propietarios)} propietarios')

# ── Inmuebles (300) ──────────────────────────────────────────────────────────
# Distribuir propietarios: algunos tienen más inmuebles que otros
prop_ids = [p[0] for p in propietarios]
# Pesos aleatorios para simular propietarios grandes y pequeños
pesos  = [random.randint(5,25) for _ in prop_ids]
total_p = sum(pesos)

inmuebles = []
inm_counter = 1
prop_inm_map = {pid: [] for pid in prop_ids}  # propietario → lista de INM ids

for idx, pid in enumerate(prop_ids):
    # Cuántos inmuebles le tocan a este propietario
    n = max(1, round(300 * pesos[idx] / total_p))
    for _ in range(n):
        if inm_counter > 300:
            break
        iid  = f'INM-{inm_counter:03d}'
        tipo = random.choice(TIPOS_INM)
        num  = random.randint(1, 20)
        pref = random.choice(PREFIJOS_INM[tipo])
        desc = f'{pref} {num}'

        # 70% La Romana, 30% La Altagracia
        if random.random() < 0.70:
            lat, lng = coords_lr()
            sector   = random.choice(SECTORES_LR)
            ciudad   = 'La Romana'
        else:
            lat, lng = coords_la()
            sector   = random.choice(SECTORES_LA)
            ciudad   = 'La Altagracia'

        monto  = monto_por_tipo(tipo)
        estado = 'OCUPADO'   # se ajusta después
        nivel  = 'VERDE'

        inmuebles.append([
            iid, pid, tipo, desc,
            f'C/ {random.choice(APELLIDOS)} #{random.randint(1,300)}', sector, ciudad,
            lat, lng, monto, estado, nivel, '', '2024-01-01'
        ])
        prop_inm_map[pid].append(iid)
        inm_counter += 1
    if inm_counter > 300:
        break

# Rellenar hasta 300 si quedaron menos
while inm_counter <= 300:
    pid  = random.choice(prop_ids)
    iid  = f'INM-{inm_counter:03d}'
    tipo = random.choice(TIPOS_INM)
    num  = random.randint(1,20)
    desc = f'{random.choice(PREFIJOS_INM[tipo])} {num}'
    if random.random() < 0.70:
        lat, lng = coords_lr(); sector = random.choice(SECTORES_LR); ciudad='La Romana'
    else:
        lat, lng = coords_la(); sector = random.choice(SECTORES_LA); ciudad='La Altagracia'
    monto = monto_por_tipo(tipo)
    inmuebles.append([iid, pid, tipo, desc,
        f'C/ {random.choice(APELLIDOS)} #{random.randint(1,300)}', sector, ciudad,
        lat, lng, monto, 'OCUPADO', 'VERDE', '', '2024-01-01'])
    prop_inm_map[pid].append(iid)
    inm_counter += 1

print(f'  ✓ {len(inmuebles)} inmuebles generados')

# Escoger 200 para contratos, 100 quedan disponibles
inm_ids_todos = [r[0] for r in inmuebles]
random.shuffle(inm_ids_todos)
inm_ocupados   = inm_ids_todos[:200]
inm_disponibles = inm_ids_todos[200:]

# Marcar disponibles
inm_monto_map  = {r[0]: r[9]  for r in inmuebles}
inm_tipo_map   = {r[0]: r[2]  for r in inmuebles}

for r in inmuebles:
    if r[0] in inm_disponibles:
        r[10] = 'DISPONIBLE'
        r[11] = 'GRIS'

# ── Inquilinos (200) ─────────────────────────────────────────────────────────
inquilinos = []
for i in range(1, 201):
    qid = f'INQ-{i:03d}'
    inquilinos.append([
        qid, nombre_completo(), cedula(), telefono(),
        f'inq{i}@mail.com', random.choice(['Empleado','Comerciante','Médico','Ingeniero','Maestro','Empresario','']),
        '', '', 'ACTIVO', '', '2024-01-01'
    ])
print(f'  ✓ {len(inquilinos)} inquilinos')

# ── Contratos (200) ──────────────────────────────────────────────────────────
# Fechas de inicio variadas para simular contratos en diferentes etapas
INICIOS = [
    datetime.date(2024, 1, 1), datetime.date(2024, 4, 1), datetime.date(2024, 7, 1),
    datetime.date(2025, 1, 1), datetime.date(2025, 4, 1), datetime.date(2025, 7, 1),
    datetime.date(2026, 1, 1), datetime.date(2026, 3, 1),
]
contratos = []
ctr_inm_map = {}   # ctr_id → inm_id
ctr_inq_map = {}
ctr_monto_map = {}
ctr_prop_map = {}

inm_prop_map = {r[0]: r[1] for r in inmuebles}

for i, (iid, qid) in enumerate(zip(inm_ocupados, [r[0] for r in inquilinos]), 1):
    cid    = f'CTR-{i:03d}'
    inicio = random.choice(INICIOS)
    # 1 año de contrato
    fin    = datetime.date(inicio.year + 1, inicio.month, inicio.day) - datetime.timedelta(days=1)
    monto  = inm_monto_map[iid]
    prop   = inm_prop_map[iid]

    contratos.append([
        cid, iid, qid,
        fmt(inicio), fmt(fin),
        monto, 'MENSUAL', 1, 30,
        'PORCENTAJE', 5, '',
        monto, 'VIGENTE', '', fmt(inicio)
    ])
    ctr_inm_map[cid]   = iid
    ctr_inq_map[cid]   = qid
    ctr_monto_map[cid] = monto
    ctr_prop_map[cid]  = prop
print(f'  ✓ {len(contratos)} contratos')

# ── CxC y Pagos (Ene-Jun 2026) ───────────────────────────────────────────────
# 12% atraso = 12% de CxC de meses pasados están EN_MORA (no pagaron)
# De los que sí pagaron: 80% a tiempo, 20% tarde (con mora 5%)
# Junio 2026: todos PENDIENTE (día 14, dentro de los 30 días de gracia)

cxc_cargos = []
cxc_pagos  = []
cxc_counter = 1
pag_counter = 1

inq_nombre_map = {r[0]: r[1] for r in inquilinos}

# Precalcular si un contrato paga un mes dado (consistencia por contrato)
# Algunos contratos son "buenos pagadores" (pagan siempre) y otros no
# Definir un "perfil" por contrato: 0=siempre paga, 1=atrasado ocasional, 2=moroso
perfiles = {}
for c in contratos:
    cid = c[0]
    r = random.random()
    if r < 0.60: perfiles[cid] = 'puntual'     # 60% siempre pagan
    elif r < 0.88: perfiles[cid] = 'tardio'     # 28% pagan tarde
    else: perfiles[cid] = 'moroso'              # 12% no pagan (o pagan con mora severa)

for c in contratos:
    cid    = c[0]
    iid    = ctr_inm_map[cid]
    qid    = ctr_inq_map[cid]
    monto  = ctr_monto_map[cid]
    nombre_inq = inq_nombre_map.get(qid, '')
    perfil = perfiles[cid]

    for mes in MESES:
        ini_mes  = datetime.date(2026, mes, 1)
        ult_dia  = calendar.monthrange(2026, mes)[1]
        fin_mes  = datetime.date(2026, mes, ult_dia)
        venc_mes = datetime.date(2026, mes, 1)
        dias_desde_venc = (HOY - venc_mes).days
        cxc_id = f'CXC-{cxc_counter:05d}'
        cxc_counter += 1

        if mes == 6:
            # Junio: todos PENDIENTE (estamos en día 14, dentro de gracia)
            cxc_cargos.append([
                cxc_id, cid, qid, nombre_inq, iid,
                'ALQUILER', f'Alquiler {ini_mes.strftime("%B %Y")}',
                fmt(ini_mes), fmt(fin_mes), fmt(venc_mes),
                monto, 0, 0, monto, 'PENDIENTE', 0, '', '', fmt(ini_mes)
            ])
        else:
            # Ene-May: según perfil
            if perfil == 'moroso':
                # No paga → EN_MORA con mora acumulada
                dias_mora = max(0, dias_desde_venc - 30)
                mora      = round(monto * 0.05, 2) if dias_mora > 0 else 0
                saldo     = monto + mora
                cxc_cargos.append([
                    cxc_id, cid, qid, nombre_inq, iid,
                    'ALQUILER', f'Alquiler {ini_mes.strftime("%B %Y")}',
                    fmt(ini_mes), fmt(fin_mes), fmt(venc_mes),
                    monto, mora, 0, saldo, 'EN_MORA', dias_mora, '', '', fmt(ini_mes)
                ])
                # Actualizar nivel de mora del inmueble
                for r in inmuebles:
                    if r[0] == iid:
                        r[11] = 'ROJO' if mes <= 3 else 'NARANJA'

            elif perfil == 'tardio':
                # Paga pero tarde (después del día 30)
                dias_retraso = random.randint(31, 60)
                fecha_pago   = venc_mes + datetime.timedelta(days=dias_retraso)
                if fecha_pago > HOY: fecha_pago = HOY - datetime.timedelta(days=random.randint(1,5))
                mora         = round(monto * 0.05, 2)
                total_pago   = monto + mora

                cxc_cargos.append([
                    cxc_id, cid, qid, nombre_inq, iid,
                    'ALQUILER', f'Alquiler {ini_mes.strftime("%B %Y")}',
                    fmt(ini_mes), fmt(fin_mes), fmt(venc_mes),
                    monto, mora, total_pago, 0, 'PAGADO', 0, fmt(fecha_pago), 'Pago tardío con mora', fmt(ini_mes)
                ])
                pag_id   = f'PAG-{pag_counter:05d}'
                pag_counter += 1
                custodia = 'LIQUIDADO' if fecha_pago.month <= 3 else 'EN_ADMINISTRACION'
                cxc_pagos.append([
                    pag_id, cxc_id, cid, qid, nombre_inq, iid,
                    'SES-2026', fmt(fecha_pago),
                    monto, mora, total_pago, 'EFECTIVO', f'REC-{pag_id}',
                    custodia, 'LIQ-2026' if custodia=='LIQUIDADO' else '',
                    f'Pago tardío — mora 5%', fmt(fecha_pago)
                ])
                for r in inmuebles:
                    if r[0] == iid and r[11] == 'VERDE':
                        r[11] = 'AMARILLO'

            else:
                # Puntual: paga dentro de los primeros 15 días
                dias_pago  = random.randint(1, 15)
                fecha_pago = venc_mes + datetime.timedelta(days=dias_pago)
                if fecha_pago > HOY: fecha_pago = HOY - datetime.timedelta(days=1)

                cxc_cargos.append([
                    cxc_id, cid, qid, nombre_inq, iid,
                    'ALQUILER', f'Alquiler {ini_mes.strftime("%B %Y")}',
                    fmt(ini_mes), fmt(fin_mes), fmt(venc_mes),
                    monto, 0, monto, 0, 'PAGADO', 0, fmt(fecha_pago), '', fmt(ini_mes)
                ])
                pag_id   = f'PAG-{pag_counter:05d}'
                pag_counter += 1
                custodia = 'LIQUIDADO' if fecha_pago.month <= 3 else 'EN_ADMINISTRACION'
                cxc_pagos.append([
                    pag_id, cxc_id, cid, qid, nombre_inq, iid,
                    'SES-2026', fmt(fecha_pago),
                    monto, 0, monto, 'EFECTIVO', f'REC-{pag_id}',
                    custodia, 'LIQ-2026' if custodia=='LIQUIDADO' else '',
                    '', fmt(fecha_pago)
                ])

print(f'  ✓ {len(cxc_cargos)} CxC generadas')
print(f'  ✓ {len(cxc_pagos)} pagos generados')

# Estadísticas rápidas
pagadas   = sum(1 for r in cxc_cargos if r[14] == 'PAGADO')
en_mora   = sum(1 for r in cxc_cargos if r[14] == 'EN_MORA')
pendiente = sum(1 for r in cxc_cargos if r[14] == 'PENDIENTE')
print(f'    PAGADO={pagadas}  EN_MORA={en_mora}  PENDIENTE={pendiente}')
print(f'    % mora efectiva = {en_mora/(len(cxc_cargos)-pendiente)*100:.1f}%')

# ════════════════════════════════════════════════════════════════════════════
# PASO 3 — SUBIR A GOOGLE SHEETS
# ════════════════════════════════════════════════════════════════════════════
print('\n══════════════════════════════════════════════')
print('PASO 3 — SUBIENDO A GOOGLE SHEETS')
print('══════════════════════════════════════════════')

datasets = [
    (TABS['propietarios'],  propietarios,  'Propietarios'),
    (TABS['inmuebles'],     inmuebles,     'Inmuebles'),
    (TABS['inquilinos'],    inquilinos,    'Inquilinos'),
    (TABS['contratos'],     contratos,     'Contratos'),
    (TABS['cxc_cargos'],   cxc_cargos,   'CxC Cargos'),
    (TABS['cxc_pagos'],    cxc_pagos,    'CxC Pagos'),
]

errores = []
for tab, data, nombre in datasets:
    print(f'\n  [{nombre}] → {len(data)} registros')
    try:
        sheets_append(tab, data)
    except Exception as e:
        print(f'  ✗ ERROR: {e}')
        errores.append((nombre, str(e)))

# ════════════════════════════════════════════════════════════════════════════
print('\n══════════════════════════════════════════════')
if errores:
    print(f'COMPLETADO CON {len(errores)} ERROR(ES):')
    for n, e in errores: print(f'  ✗ {n}: {e[:80]}')
else:
    print('✓ CARGA COMPLETADA SIN ERRORES')
print('══════════════════════════════════════════════')
print(f'\nRESUMEN FINAL:')
print(f'  Propietarios : {len(propietarios)}')
print(f'  Inmuebles    : {len(inmuebles)} (200 ocupados, 100 disponibles)')
print(f'  Inquilinos   : {len(inquilinos)}')
print(f'  Contratos    : {len(contratos)}')
print(f'  CxC total    : {len(cxc_cargos)} (pagadas={pagadas}, mora={en_mora}, pendiente jun={pendiente})')
print(f'  Pagos        : {len(cxc_pagos)}')
pct_mora = en_mora/(len(cxc_cargos)-pendiente)*100
print(f'  % atrasos    : {pct_mora:.1f}% (objetivo 12%)')
