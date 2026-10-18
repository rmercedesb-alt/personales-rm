"""
Limpia y recarga la pestaña CONTRATOS con las columnas correctas:
A=Id B=IdInmueble C=NombreInmueble D=IdInquilino E=NombreInquilino
F=IdPropietario G=FechaInicio H=FechaFin I=MontoAlquiler J=Periodicidad
K=DiaCobro L=DiasGracia M=TipoMora N=MoraPorcentaje O=MontoMoraFija
P=Deposito Q=Estado R=Notas S=FechaRegistro
"""
import sys, io, time
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

from google.oauth2 import service_account
from googleapiclient.discovery import build

SPREADSHEET_ID   = '1Ir6aKzaL8HRSi0v8lUMa_1KYTQc0r4gtTVwHiHiYbAU'
CREDENTIALS_PATH = r'C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Sistema de Condominios\web-app\InmueblesAPI\credentials.json'
TAB         = '📋 CONTRATOS'
TAB_INM     = '🏢 INMUEBLES'
TAB_INQ     = '👥 INQUILINOS'
TAB_PROP    = '👤 PROPIETARIOS'
PAUSA = 0.8

creds  = service_account.Credentials.from_service_account_file(
    CREDENTIALS_PATH, scopes=['https://www.googleapis.com/auth/spreadsheets'])
svc    = build('sheets', 'v4', credentials=creds)
sheets = svc.spreadsheets()

def read_tab(tab, cols):
    resp = sheets.values().get(spreadsheetId=SPREADSHEET_ID, range=f"'{tab}'!A:{cols}").execute()
    return resp.get('values', [])

# Leer maestras para cruzar nombres
print('Leyendo maestras...')
inm_rows   = read_tab(TAB_INM,  'O')[1:]   # skip header
inq_rows   = read_tab(TAB_INQ,  'K')[1:]
prop_rows  = read_tab(TAB_PROP, 'N')[1:]
ctr_rows   = read_tab(TAB,      'S')[1:]   # contratos actuales (mal estructurados)

# Mapas: id → nombre / propietario
inm_desc   = {r[0]: r[4] for r in inm_rows if len(r) > 4}    # A=id, E=descripcion
inm_prop   = {r[0]: r[1] for r in inm_rows if len(r) > 1}    # A=id, B=idPropietario
inq_nombre = {r[0]: r[1] for r in inq_rows  if len(r) > 1}
prop_nombre= {r[0]: r[1] for r in prop_rows if len(r) > 1}

print(f'  {len(inm_desc)} inmuebles  {len(inq_nombre)} inquilinos  {len(prop_nombre)} propietarios')

# Los contratos actuales están en formato incorrecto:
# A=Id B=IdInmueble C=IdInquilino D=FechaInicio E=FechaFin F=Monto G=Periodicidad H=DiaCobro
# I=DiasGracia J=TipoMora K=MoraPct L=MontoMoraFija M=Deposito N=Estado O=Notas P=FechaReg
# Necesitamos reordenarlos al formato correcto del API
contratos = []
for r in ctr_rows:
    if not r or not r[0]: continue
    cid     = r[0]   if len(r) > 0 else ''
    iid     = r[1]   if len(r) > 1 else ''
    qid     = r[2]   if len(r) > 2 else ''
    f_ini   = r[3]   if len(r) > 3 else ''
    f_fin   = r[4]   if len(r) > 4 else ''
    monto   = r[5]   if len(r) > 5 else '0'
    period  = r[6]   if len(r) > 6 else 'MENSUAL'
    dia_cob = r[7]   if len(r) > 7 else '1'
    d_grac  = r[8]   if len(r) > 8 else '30'
    t_mora  = r[9]   if len(r) > 9 else 'PORCENTAJE'
    mora_pct= r[10]  if len(r) > 10 else '5'
    mora_fij= r[11]  if len(r) > 11 else '0'
    deposito= r[12]  if len(r) > 12 else monto
    estado  = r[13]  if len(r) > 13 else 'VIGENTE'
    notas   = r[14]  if len(r) > 14 else ''
    fecha_r = r[15]  if len(r) > 15 else '2024-01-01'

    nombre_inm  = inm_desc.get(iid, '')
    nombre_inq  = inq_nombre.get(qid, '')
    id_prop     = inm_prop.get(iid, '')

    contratos.append([
        cid, iid, nombre_inm, qid, nombre_inq, id_prop,
        f_ini, f_fin, monto, period, dia_cob, d_grac,
        t_mora, mora_pct, mora_fij, deposito, estado, notas, fecha_r
    ])

print(f'  {len(contratos)} contratos a resubir')
sin_prop = sum(1 for c in contratos if not c[5])
print(f'  Sin IdPropietario: {sin_prop}')
con_nombre_inm = sum(1 for c in contratos if c[2])
print(f'  Con NombreInmueble: {con_nombre_inm}')

# Limpiar y resubir
print(f'\nLimpiando {TAB}...')
sheets.values().clear(spreadsheetId=SPREADSHEET_ID, range=f"'{TAB}'!A2:ZZ").execute()
time.sleep(PAUSA)

print(f'Subiendo {len(contratos)} contratos...')
BATCH = 100
for i in range(0, len(contratos), BATCH):
    chunk = contratos[i:i+BATCH]
    sheets.values().append(
        spreadsheetId=SPREADSHEET_ID,
        range=f"'{TAB}'!A:A",
        valueInputOption='USER_ENTERED',
        insertDataOption='INSERT_ROWS',
        body={'values': [[str(v) if v is not None else '' for v in r] for r in chunk]}
    ).execute()
    print(f'  {min(i+BATCH, len(contratos))}/{len(contratos)}', end='\r')
    time.sleep(PAUSA)

print(f'\n✓ {len(contratos)} contratos subidos')
print('Columnas: A=Id B=IdInm C=NombreInm D=IdInq E=NombreInq F=IdProp G=FechaIni H=FechaFin I=Monto J=Period K=DiaCob L=DiasGracia M=TipoMora N=MoraPct O=MoraFija P=Deposito Q=Estado R=Notas S=FechaReg')
