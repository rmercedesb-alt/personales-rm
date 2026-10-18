import json

with open('presupuesto_data.json', encoding='utf-8') as f:
    d = json.load(f)

print("=== SUPLIDORES ===")
for s in d['suplidores']:
    pct = s['Proyectado']/s['Presupuesto']*100 if s['Presupuesto'] else 0
    print(f"  {s['Suplidor'][:30]}: Pres={s['Presupuesto']/1e6:.2f}M Ejec={s['Ejecutado']/1e6:.2f}M Proy={s['Proyectado']/1e6:.2f}M ({pct:.1f}%)")

print("\n=== HISTORICO - top vendedores por total ===")
from collections import defaultdict
totales = defaultdict(float)
for h in d['historico']:
    totales[h['CodVendedor']] += h['Ventas']
top_vend = sorted(totales.items(), key=lambda x: -x[1])[:8]
top_ids = {v[0] for v in top_vend}
print("Top vendedores:", [(v, f"{t/1e6:.1f}M") for v,t in top_vend])
nombres = {h['CodVendedor']: h['Vendedor'] for h in d['historico']}

print("\n=== YTD COMPARATIVO ===")
ytd_by_vend = defaultdict(lambda: {'2025': 0, '2026': 0})
for y in d['ytd']:
    ytd_by_vend[y['CodVendedor']][str(y['Anio'])] = y['Ventas']
    nombres[y['CodVendedor']] = y['Vendedor']
# Sort by 2026 ventas
ytd_sorted = sorted(ytd_by_vend.items(), key=lambda x: -x[1]['2026'])[:12]
for cod, vals in ytd_sorted:
    crec = (vals['2026']/vals['2025']-1)*100 if vals['2025'] else 0
    print(f"  {nombres.get(cod,'?')[:25]}: 2025={vals['2025']/1e6:.1f}M 2026={vals['2026']/1e6:.1f}M ({crec:+.1f}%)")

print("\n=== CLIENTES RUTA vs VENDIDOS ===")
# vendidos por mes 2026 - get latest available month
from collections import defaultdict as dd
cli_v = dd(lambda: {})
for c in d['clientes_ventas_2026']:
    cod = c['CodVendedor']
    mes = c['Mes']
    cli_v[cod][mes] = c['ClientesDistintos']

# Get ruta clients
cli_r = {c['RepCodigo']: c['ClientesEnRuta'] for c in d['clientes_ruta']}

# Show for each vendedor
for cr in d['clientes_ruta']:
    cod = cr['RepCodigo']
    en_ruta = cr['ClientesEnRuta']
    vendidos_may = cli_v.get(cod, {}).get(5, 0)
    vendidos_jun = cli_v.get(cod, {}).get(6, 0)
    if en_ruta > 0 or vendidos_may > 0:
        pct = vendidos_may/en_ruta*100 if en_ruta else 0
        print(f"  {cr['RepNombre'][:25]}: Ruta={en_ruta} May={vendidos_may} Jun={vendidos_jun} ({pct:.0f}%)")

print("\n=== VENDEDORES KPI ===")
for v in d['vendedores'][:26]:
    pct_dia = v['Ejecutado']/v['PresupuestoAlDia']*100 if v['PresupuestoAlDia'] else 0
    pct_proy = v['Proyectado']/v['Presupuesto']*100 if v['Presupuesto'] else 0
    print(f"  {v['RepNombre'][:28]}: Ejec={v['Ejecutado']/1e6:.2f}M AlDia={v['PresupuestoAlDia']/1e6:.2f}M ({pct_dia:.0f}%) Proy={pct_proy:.0f}%")
