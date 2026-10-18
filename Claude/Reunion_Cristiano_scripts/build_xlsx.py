# -*- coding: utf-8 -*-
"""Anexo de datos para la reunion con Cristiano Perez."""
import pandas as pd, numpy as np
from pathlib import Path

SC = Path(__file__).parent
DEST = Path(r"C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude") / "Reunion_Cristiano_Perez_Datos_Sep2026.xlsx"
P = lambda n: pd.read_pickle(SC / (n + '.pkl'))
cartera, ventas, visitas, inv = P('cartera'), P('ventas'), P('visitas'), P('inv')
MESES = ['2026-03', '2026-04', '2026-05', '2026-06', '2026-07', '2026-08']
NOM = {'25': 'Jhonny Rosario (25)', '36': 'Hanel Giron (36)',
       '106': 'Wascar Alvarez (106)', '185': 'Fernando Martinez (185)'}
cset = set(cartera.CliID)
v = ventas[ventas.CliID.isin(cset)]
vis = visitas[visitas.CliID.isin(cset)]
cartera['Vendedor'] = cartera.RepCodigo.map(NOM)

# 1 Cartera por tipo de negocio y vendedor
t1 = pd.crosstab(cartera.TipoNegocio, cartera.Vendedor, margins=True, margins_name='TOTAL')

# 2 Cobertura y suplidores por tipo de negocio
comp = v.groupby('CliID').Valor.sum().rename('Venta6M')
c = cartera.merge(comp, on='CliID', how='left')
c['Compro'] = c.Venta6M.notna()
t2 = c.groupby('TipoNegocio').agg(Cartera=('CliID', 'count'), ClientesConCompra=('Compro', 'sum'),
                                  VentaRD=('Venta6M', 'sum')).sort_values('Cartera', ascending=False)
t2['Cobertura%'] = (t2.ClientesConCompra / t2.Cartera * 100).round(1)

m = v.merge(cartera[['CliID', 'TipoNegocio']], on='CliID')
base = cartera.groupby('TipoNegocio').CliID.count()
piv = m.groupby(['TipoNegocio', 'Suplidor']).CliID.nunique().unstack(fill_value=0)
t3 = (piv.div(base, axis=0) * 100).round(1)

# 3 Visitas mensuales
t4 = pd.DataFrame({
    'ClientesVisitados': vis.groupby(['RepCodigo', 'Mes']).CliID.nunique(),
    'VisitasTotales': vis.groupby(['RepCodigo', 'Mes']).Visitas.sum(),
    'ClientesConCompra': v.groupby(['RepCodigo', 'Mes']).CliID.nunique(),
    'VentaRD': v.groupby(['RepCodigo', 'Mes']).Valor.sum(),
}).reset_index()
t4['Vendedor'] = t4.RepCodigo.map(NOM)
t4['Cartera'] = t4.RepCodigo.map(cartera.groupby('RepCodigo').CliID.count())
t4['Cobertura%'] = (t4.ClientesVisitados / t4.Cartera * 100).round(1)
t4['Efectividad%'] = (t4.ClientesConCompra / t4.ClientesVisitados * 100).round(1)
t4 = t4[['Vendedor', 'Mes', 'Cartera', 'ClientesVisitados', 'Cobertura%', 'VisitasTotales',
         'ClientesConCompra', 'Efectividad%', 'VentaRD']]

# 4 Clientes sin compra en 6 meses
visnum = vis.groupby('CliID').Visitas.sum().rename('VisitasRecibidas')
mesesvis = vis.groupby('CliID').Mes.nunique().rename('MesesVisitado')
t5 = (c[~c.Compro][['CliID', 'CliNombre', 'Vendedor', 'TipoNegocio', 'Clasificacion', 'CliFechaUltimaVenta']]
      .merge(visnum, on='CliID', how='left').merge(mesesvis, on='CliID', how='left')
      .sort_values(['Vendedor', 'VisitasRecibidas'], ascending=[True, False]))

# 5 Pareto y desviacion (todos los clientes Pareto, no solo el top 3)
cm = v.groupby(['RepCodigo', 'CliID', 'Mes']).Valor.sum().unstack(fill_value=0).reindex(columns=MESES, fill_value=0)
cm['Venta6M'] = cm.sum(axis=1)
nm = cartera.set_index('CliID').CliNombre
tn = cartera.set_index('CliID').TipoNegocio
out = []
for rep, g in cm.groupby(level=0):
    g = g.sort_values('Venta6M', ascending=False)
    cum = g.Venta6M.cumsum() / g.Venta6M.sum()
    n = int((cum <= 0.8).sum()) + 1
    d = g.iloc[:n]
    ids = d.index.get_level_values('CliID')
    r = pd.DataFrame({
        'Vendedor': NOM[rep], 'CliID': ids, 'Cliente': [nm.get(i) for i in ids],
        'TipoNegocio': [tn.get(i) for i in ids], 'Venta6M': d.Venta6M.values,
        'PromedioMes': d[MESES].mean(axis=1).values,
        'DesvEstandar': d[MESES].std(axis=1, ddof=0).values,
        'MesesConCompra': (d[MESES] > 0).sum(axis=1).values,
        'MarMay': d[MESES[:3]].sum(axis=1).values, 'JunAgo': d[MESES[3:]].sum(axis=1).values})
    for mm in MESES:
        r[mm] = d[mm].values
    out.append(r)
t6 = pd.concat(out)
t6['CV%'] = (t6.DesvEstandar / t6.PromedioMes * 100).round(0)
t6['Variacion'] = t6.JunAgo - t6.MarMay
t6['Var%'] = np.where(t6.MarMay > 0, (t6.Variacion / t6.MarMay * 100).round(0), np.nan)
t6 = t6.sort_values(['Vendedor', 'Variacion'])

# 6 Oportunidades de venta cruzada (listas accionables)
sup = v.groupby(['CliID', 'Suplidor']).Valor.sum().unstack(fill_value=0)
CI, CN, BR, CO = 'Cesar Iglesias (Unilever)', 'Continental de Negocios', 'Brinsa Dominicana', 'COLOMBINA'
s = sup.join(cartera.set_index('CliID')[['CliNombre', 'Vendedor', 'TipoNegocio']])
cross = []
for etiq, mask in [
    ('Compra Cesar Iglesias, NO compra Continental', (s[CI] > 0) & (s[CN] == 0)),
    ('Compra Cesar Iglesias, NO compra Brinsa', (s[CI] > 0) & (s[BR] == 0)),
    ('Compra Continental, NO compra Colombina', (s[CN] > 0) & (s[CO] == 0))]:
    d = s[mask].copy(); d['Oportunidad'] = etiq
    d['VentaTotal6M'] = s.loc[mask, [c_ for c_ in sup.columns]].sum(axis=1)
    cross.append(d.reset_index()[['Oportunidad', 'CliID', 'CliNombre', 'Vendedor', 'TipoNegocio', 'VentaTotal6M']])
t7 = pd.concat(cross).sort_values(['Oportunidad', 'VentaTotal6M'], ascending=[True, False])

# 7 Inventarios fisicos
t8 = inv.merge(cartera[['CliID', 'CliNombre']], on='CliID', how='left')
t8['Vendedor'] = t8.RepCodigo.map(NOM)
t8 = t8[['Vendedor', 'Mes', 'CliID', 'CliNombre', 'Inventarios']]

with pd.ExcelWriter(DEST, engine='openpyxl') as w:
    t1.to_excel(w, sheet_name='1 Cartera x tipo negocio')
    t2.to_excel(w, sheet_name='2 Cobertura x tipo negocio')
    t3.to_excel(w, sheet_name='3 Incidencia % x suplidor')
    t4.to_excel(w, sheet_name='4 Visitas mensuales', index=False)
    t5.to_excel(w, sheet_name='5 Clientes sin compra 6M', index=False)
    t6.to_excel(w, sheet_name='6 Pareto y desviacion', index=False)
    t7.to_excel(w, sheet_name='7 Venta cruzada', index=False)
    t8.to_excel(w, sheet_name='8 Inventarios fisicos', index=False)
print("OK ->", DEST)
for n, d in [('t1', t1), ('t5', t5), ('t6', t6), ('t7', t7)]:
    print(n, d.shape)
