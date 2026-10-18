"""
Limpia y recarga SOLO la pestaña INMUEBLES con las columnas correctas:
A=Id, B=IdPropietario, C=NombrePropietario, D=Tipo, E=Descripcion,
F=Direccion, G=Sector, H=Ciudad, I=Latitud, J=Longitud,
K=MontoAlquiler, L=Estado, M=NivelMorosidad, N=Notas, O=FechaRegistro
"""
import random, sys, io, time
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
random.seed(42)

from google.oauth2 import service_account
from googleapiclient.discovery import build

SPREADSHEET_ID   = '1Ir6aKzaL8HRSi0v8lUMa_1KYTQc0r4gtTVwHiHiYbAU'
CREDENTIALS_PATH = r'C:\Personales RM\Claude\Sistema de Condominios\web-app\InmueblesAPI\credentials.json'
TAB = '🏢 INMUEBLES'
TAB_PROP = '👤 PROPIETARIOS'
PAUSA = 0.8

creds  = service_account.Credentials.from_service_account_file(
    CREDENTIALS_PATH, scopes=['https://www.googleapis.com/auth/spreadsheets'])
svc    = build('sheets', 'v4', credentials=creds)
sheets = svc.spreadsheets()

# ── Leer propietarios existentes para obtener nombres ───────────────────────
print('Leyendo propietarios...')
resp = sheets.values().get(spreadsheetId=SPREADSHEET_ID, range=f"'{TAB_PROP}'!A:B").execute()
prop_rows = resp.get('values', [])[1:]   # skip header
prop_nombre = {r[0]: r[1] for r in prop_rows if len(r) >= 2}
prop_ids = list(prop_nombre.keys())
print(f'  {len(prop_ids)} propietarios encontrados')

# ── Catálogos ────────────────────────────────────────────────────────────────
APELLIDOS = ['García','Rodríguez','Martínez','López','González','Pérez','Sánchez',
             'Ramírez','Torres','Flores','Rivera','Jiménez','Morales','Herrera',
             'Medina','Cruz','Reyes','Vargas','Castillo','Cabrera','Mercedes',
             'Féliz','Matos','Espinal','Almonte','Disla','Polanco','Encarnación']

SECTORES_LR = ['La Romana Centro','Villa Hermosa','Barrio Blanco','Santa Rosa',
               'Los Americanos','La Unión','Res. Independencia','Barrio Obrero',
               'El Caliche','La Cumbre','Res. Los Alamos','Ensanche Libertad']
SECTORES_LA = ['Higüey Centro','Bávaro','Punta Cana','Las Américas',
               'Verón','La Otra Banda','Los Palmares','El Seibo']

TIPOS_INM = ['APARTAMENTO','APARTAMENTO','APARTAMENTO','CASA','CASA','LOCAL','OFICINA']
PREFIJOS  = {
    'APARTAMENTO': ['Apto','Apartamento','Suite'],
    'CASA':        ['Casa','Residencia','Villa'],
    'LOCAL':       ['Local','Plaza'],
    'OFICINA':     ['Oficina','Suite Ejecutiva'],
}
MONTOS = {
    'APARTAMENTO': (8000,  45000),
    'CASA':        (12000, 60000),
    'LOCAL':       (15000, 80000),
    'OFICINA':     (20000, 70000),
}

def coords_lr():
    return round(random.uniform(18.38, 18.52), 6), round(random.uniform(-69.05, -68.82), 6)
def coords_la():
    return round(random.uniform(18.50, 18.82), 6), round(random.uniform(-68.95, -68.45), 6)

def monto(tipo):
    lo, hi = MONTOS[tipo]
    return round(random.randint(lo, hi) / 500) * 500

# ── Distribuir inmuebles entre propietarios ──────────────────────────────────
pesos = [random.randint(5, 25) for _ in prop_ids]
total_p = sum(pesos)

inmuebles = []
counter = 1

for idx, pid in enumerate(prop_ids):
    n = max(1, round(300 * pesos[idx] / total_p))
    for _ in range(n):
        if counter > 300:
            break
        iid  = f'INM-{counter:03d}'
        tipo = random.choice(TIPOS_INM)
        desc = f'{random.choice(PREFIJOS[tipo])} {random.randint(1,20)}'
        if random.random() < 0.70:
            lat, lng = coords_lr()
            sector, ciudad = random.choice(SECTORES_LR), 'La Romana'
        else:
            lat, lng = coords_la()
            sector, ciudad = random.choice(SECTORES_LA), 'La Altagracia'
        estado = 'OCUPADO' if counter <= 200 else 'DISPONIBLE'
        nivel  = 'VERDE'   if counter <= 200 else 'GRIS'
        inmuebles.append([
            iid, pid, prop_nombre.get(pid, ''), tipo, desc,
            f'C/ {random.choice(APELLIDOS)} #{random.randint(1,300)}',
            sector, ciudad, lat, lng,
            monto(tipo), estado, nivel, '', '2024-01-01'
        ])
        counter += 1
    if counter > 300:
        break

# Rellenar si quedaron menos de 300
while counter <= 300:
    pid  = random.choice(prop_ids)
    iid  = f'INM-{counter:03d}'
    tipo = random.choice(TIPOS_INM)
    desc = f'{random.choice(PREFIJOS[tipo])} {random.randint(1,20)}'
    if random.random() < 0.70:
        lat, lng = coords_lr(); sector, ciudad = random.choice(SECTORES_LR), 'La Romana'
    else:
        lat, lng = coords_la(); sector, ciudad = random.choice(SECTORES_LA), 'La Altagracia'
    estado = 'DISPONIBLE'; nivel = 'GRIS'
    inmuebles.append([iid, pid, prop_nombre.get(pid,''), tipo, desc,
        f'C/ {random.choice(APELLIDOS)} #{random.randint(1,300)}',
        sector, ciudad, lat, lng, monto(tipo), estado, nivel, '', '2024-01-01'])
    counter += 1

print(f'  {len(inmuebles)} inmuebles generados')
with_coords = sum(1 for r in inmuebles if r[8] and r[9])
print(f'  {with_coords} con coordenadas (cols I y J)')

# ── Limpiar pestaña ──────────────────────────────────────────────────────────
print(f'\nLimpiando {TAB}...')
sheets.values().clear(spreadsheetId=SPREADSHEET_ID, range=f"'{TAB}'!A2:ZZ").execute()
time.sleep(PAUSA)
print('  ✓ Limpiada')

# ── Subir ────────────────────────────────────────────────────────────────────
print(f'Subiendo {len(inmuebles)} inmuebles...')
BATCH = 100
for i in range(0, len(inmuebles), BATCH):
    chunk = inmuebles[i:i+BATCH]
    sheets.values().append(
        spreadsheetId=SPREADSHEET_ID,
        range=f"'{TAB}'!A:A",
        valueInputOption='USER_ENTERED',
        insertDataOption='INSERT_ROWS',
        body={'values': [[str(v) if v is not None else '' for v in r] for r in chunk]}
    ).execute()
    print(f'  {min(i+BATCH, len(inmuebles))}/{len(inmuebles)}', end='\r')
    time.sleep(PAUSA)

print(f'\n✓ {len(inmuebles)} inmuebles subidos correctamente')
print(f'  Columnas: A=Id B=IdProp C=NombreProp D=Tipo E=Desc F=Dir G=Sector H=Ciudad I=Lat J=Lng K=Monto L=Estado M=Nivel')
