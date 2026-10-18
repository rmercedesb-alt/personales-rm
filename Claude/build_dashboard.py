import json
from collections import defaultdict

with open('presupuesto_data.json', encoding='utf-8') as f:
    d = json.load(f)

kpi = d['kpi']
vendedores = d['vendedores']
supervisores = d['supervisores']
suplidores = d['suplidores']
historico = d['historico']
ytd = d['ytd']
clientes_ruta = d['clientes_ruta']
clientes_ventas = d['clientes_ventas_2026']

# Filter active vendedores (exclude "Disponible" entries)
vendedores = [v for v in vendedores if 'Disponible' not in v['RepNombre']]
clientes_ruta = [c for c in clientes_ruta if 'Disponible' not in c['RepNombre'] and c['ClientesEnRuta'] > 0]

# Historico: top 8 vendors by total
totales = defaultdict(float)
nombres_map = {}
for h in historico:
    totales[h['CodVendedor']] += h['Ventas']
    nombres_map[h['CodVendedor']] = h['Vendedor'].strip()
top8_ids = [v[0] for v in sorted(totales.items(), key=lambda x: -x[1])[:8]]

# Build month labels for last 2 years
hist_data = {}
for cod in top8_ids:
    hist_data[cod] = {}
    for h in historico:
        if h['CodVendedor'] == cod:
            key = f"{h['Anio']}-{h['Mes']:02d}"
            hist_data[cod][key] = round(h['Ventas']/1000)  # in thousands

# Get all months sorted
all_months = sorted(set(f"{h['Anio']}-{h['Mes']:02d}" for h in historico))
month_labels = []
for m in all_months:
    y, mo = m.split('-')
    mnames = ['','Ene','Feb','Mar','Abr','May','Jun','Jul','Ago','Sep','Oct','Nov','Dic']
    month_labels.append(f"{mnames[int(mo)]} {y[2:]}")

# YTD: only vendedores with data in both years or significant 2026
ytd_by_vend = defaultdict(lambda: {'2025': 0, '2026': 0})
for y_row in ytd:
    ytd_by_vend[y_row['CodVendedor']][str(y_row['Anio'])] = y_row['Ventas']
    nombres_map[y_row['CodVendedor']] = y_row['Vendedor'].strip()

# Sort by 2026
ytd_sorted = sorted(ytd_by_vend.items(), key=lambda x: -x[1]['2026'])[:15]
ytd_labels = [nombres_map.get(c, c)[:20] for c,_ in ytd_sorted]
ytd_2025 = [round(v['2025']/1e6, 2) for _,v in ytd_sorted]
ytd_2026 = [round(v['2026']/1e6, 2) for _,v in ytd_sorted]

# Clientes ruta vs vendidos (May 2026)
cli_v_may = {c['CodVendedor']: c['ClientesDistintos'] for c in clientes_ventas if c['Mes']==5}
cli_v_jun = {c['CodVendedor']: c['ClientesDistintos'] for c in clientes_ventas if c['Mes']==6}

cli_data = []
for cr in clientes_ruta:
    cod = cr['RepCodigo']
    en_ruta = cr['ClientesEnRuta']
    may = cli_v_may.get(cod, 0)
    jun = cli_v_jun.get(cod, 0)
    nombre = cr['RepNombre'].strip()
    if en_ruta > 0:
        cli_data.append({'nom': nombre[:22], 'ruta': en_ruta, 'may': may, 'jun': jun})

cli_data.sort(key=lambda x: -x['ruta'])
cli_data = cli_data[:20]

# Suplidores at risk (Proyectado < Presupuesto, filter significant)
sup_risk = [s for s in suplidores if s['Presupuesto'] > 50000 and s['Proyectado'] < s['Presupuesto']]
sup_risk.sort(key=lambda x: (x['Proyectado']/x['Presupuesto'] if x['Presupuesto'] else 1))

# Vendedores chart data
vend_sorted = sorted(vendedores, key=lambda x: -x['Presupuesto'])
vend_names = [v['RepNombre'].strip()[:22] for v in vend_sorted]
vend_pres = [round(v['Presupuesto']/1e6, 2) for v in vend_sorted]
vend_aldi = [round(v['PresupuestoAlDia']/1e6, 2) for v in vend_sorted]
vend_ejec = [round(v['Ejecutado']/1e6, 2) for v in vend_sorted]
vend_proy = [round(v['Proyectado']/1e6, 2) for v in vend_sorted]

# Supervisores data
sup_names = [s['Supervisor'].split('(')[0].strip()[:25] for s in supervisores]
sup_pres = [round(s['Presupuesto']/1e6, 2) for s in supervisores]
sup_aldi = [round(s['PresupuestoAlDia']/1e6, 2) for s in supervisores]
sup_ejec = [round(s['Ejecutado']/1e6, 2) for s in supervisores]
sup_proy = [round(s['Proyectado']/1e6, 2) for s in supervisores]

# Output JS-ready data
output = {
    'kpi': kpi,
    'sup_names': sup_names,
    'sup_pres': sup_pres,
    'sup_aldi': sup_aldi,
    'sup_ejec': sup_ejec,
    'sup_proy': sup_proy,
    'sup_risk': [{'nom': s['Suplidor'][:25], 'pres': round(s['Presupuesto']/1e6,2),
                  'ejec': round(s['Ejecutado']/1e6,2), 'proy': round(s['Proyectado']/1e6,2),
                  'pct': round(s['Proyectado']/s['Presupuesto']*100 if s['Presupuesto'] else 0)}
                 for s in sup_risk],
    'vend_names': vend_names,
    'vend_pres': vend_pres,
    'vend_aldi': vend_aldi,
    'vend_ejec': vend_ejec,
    'vend_proy': vend_proy,
    'hist_months': all_months,
    'hist_labels': month_labels,
    'hist_top8': top8_ids,
    'hist_top8_names': [nombres_map.get(c, c)[:20] for c in top8_ids],
    'hist_data': {cod: [hist_data[cod].get(m, None) for m in all_months] for cod in top8_ids},
    'ytd_labels': ytd_labels,
    'ytd_2025': ytd_2025,
    'ytd_2026': ytd_2026,
    'cli_data': cli_data,
}

print(json.dumps(output, ensure_ascii=False, indent=2))
