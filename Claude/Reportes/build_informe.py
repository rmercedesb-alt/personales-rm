# encoding: utf-8
import json
from datetime import date

with open('analisis_data.json', encoding='utf-8') as f:
    data = json.load(f)

# ── Suplidores a excluir de comparacion (mayoristas/especiales)
EXCLUIR_SUP = ['S111']  # Rudy Mercedes – pedidos mayoristas no comparables

# ── Cálculos base
hoy_v = sum(r['valor'] for r in data['hoy_sup'])
j1_v  = sum(data['jue_sup']['2026-07-09'].values())
j2_v  = sum(data['jue_sup']['2026-07-02'].values())
prom_jue = (j1_v + j2_v) / 2

mtd_total  = sum(r['valor'] for r in data['mtd'])
mtd_dias   = len(data['mtd'])
prom_diario = mtd_total / mtd_dias

def pct(a, b):
    if b == 0: return 0
    return (a - b) / b * 100

def fmt(v, dec=0):
    if dec == 0:
        return f"{v:>14,.0f}"
    return f"{v:>10.1f}%"

# ── Tabla supervisores con desvíos
print("=== SUPERVISOR: hoy vs prom-jueves ===")
sup_table = []
for r in data['hoy_sup']:
    cod = r['cod']
    v_hoy = r['valor']
    v_j1 = data['jue_sup']['2026-07-09'].get(cod, 0)
    v_j2 = data['jue_sup']['2026-07-02'].get(cod, 0)
    prom = (v_j1 + v_j2) / 2 if v_j1 or v_j2 else 0
    dv = pct(v_hoy, prom) if prom else None
    sup_table.append({
        'cod': cod, 'nombre': r['nombre'], 'pedidos': r['pedidos'],
        'hoy': v_hoy, 'j1': v_j1, 'j2': v_j2, 'prom_jue': prom,
        'desv': dv
    })
    tag = f"{'▲' if (dv or 0)>5 else '▼' if (dv or 0)<-5 else '='}"
    print(f"  {tag} {r['nombre'][:28]:<28}  hoy:{fmt(v_hoy)}  promJue:{fmt(prom)}  dif:{(dv or 0):+.1f}%")

# ── Tabla suplidores con desvíos
print("\n=== SUPLIDOR: hoy vs prom-jueves ===")
scat_table = []
for r in data['hoy_suplidor']:
    cod = r['cod']
    v_hoy = r['valor']
    v_j1 = data['jue_suplidor']['2026-07-09'].get(cod, 0)
    v_j2 = data['jue_suplidor']['2026-07-02'].get(cod, 0)
    prom = (v_j1 + v_j2) / 2 if v_j1 or v_j2 else 0
    dv = pct(v_hoy, prom) if prom else None
    scat_table.append({
        'cod': cod, 'nombre': r['nombre'], 'pedidos': r['pedidos'],
        'hoy': v_hoy, 'j1': v_j1, 'j2': v_j2, 'prom_jue': prom,
        'desv': dv
    })
    tag = f"{'▲' if (dv or 0)>5 else '▼' if (dv or 0)<-5 else '?'}"
    print(f"  {tag} {r['nombre'][:32]:<32}  hoy:{fmt(v_hoy)}  promJue:{fmt(prom)}  dif:{(dv or 0) if dv is not None else '?':}")

# ── Guardar para el HTML
with open('analisis_calc.json', 'w', encoding='utf-8') as f:
    json.dump({
        'hoy_v': hoy_v, 'j1_v': j1_v, 'j2_v': j2_v, 'prom_jue': prom_jue,
        'mtd_total': mtd_total, 'mtd_dias': mtd_dias, 'prom_diario': prom_diario,
        'sup_table': sup_table, 'scat_table': scat_table,
        'mtd': data['mtd'],
        'hoy_cruce': data['hoy_cruce'],
        'jueves_ant': data['jueves_ant'],
        'viernes_disp': data['viernes_disp'],
    }, f, ensure_ascii=False, indent=2)
print("\nDone => analisis_calc.json")
