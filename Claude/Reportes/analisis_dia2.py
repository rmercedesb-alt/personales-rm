# encoding: utf-8
import pyodbc, sys, json
from datetime import date, timedelta
from decimal import Decimal
sys.path.insert(0, '.')
from config import CONN_STR

conn = pyodbc.connect(CONN_STR, timeout=60)
cur  = conn.cursor()

hoy = date(2026, 7, 17)  # jueves
# Jueves anteriores en el mes
jueves_ant = [date(2026,7,9), date(2026,7,2)]
# Viernes disponibles en la vista
viernes_disp = [date(2026,7,10), date(2026,7,3)]

def q(sql, params=None):
    cur.execute(sql, params or [])
    cs = [d[0] for d in cur.description]
    return [dict(zip(cs, r)) for r in cur.fetchall()]

def fv(v):
    if v is None: return 0.0
    return float(v)

# ─────────────────────────────────────────
# 1. HOY por Supervisor
# ─────────────────────────────────────────
hoy_sup = q("""
    SELECT CodigoSupervisor AS cod, Supervisorpreventas AS nombre,
           COUNT(DISTINCT PedSecuencia) AS pedidos,
           SUM(ValorBrutoPedido) AS valor
    FROM VWpedidosResumenMesactual
    WHERE CAST(PedFecha AS DATE) = ?
    GROUP BY CodigoSupervisor, Supervisorpreventas
    ORDER BY valor DESC
""", (str(hoy),))

# ─────────────────────────────────────────
# 2. HOY por Suplidor (top)
# ─────────────────────────────────────────
hoy_sup_cat = q("""
    SELECT CodigoSuplidor AS cod, Suplidor AS nombre,
           COUNT(DISTINCT PedSecuencia) AS pedidos,
           SUM(ValorBrutoPedido) AS valor
    FROM VWpedidosResumenMesactual
    WHERE CAST(PedFecha AS DATE) = ?
    GROUP BY CodigoSuplidor, Suplidor
    ORDER BY valor DESC
""", (str(hoy),))

# ─────────────────────────────────────────
# 3. Jueves anteriores del mes por Supervisor
# ─────────────────────────────────────────
jue_sup = {}
for j in jueves_ant:
    rows = q("""
        SELECT CodigoSupervisor AS cod, Supervisorpreventas AS nombre,
               SUM(ValorBrutoPedido) AS valor
        FROM VWpedidosResumenMesactual
        WHERE CAST(PedFecha AS DATE) = ?
        GROUP BY CodigoSupervisor, Supervisorpreventas
    """, (str(j),))
    jue_sup[str(j)] = {r['cod']: fv(r['valor']) for r in rows}

# ─────────────────────────────────────────
# 4. Jueves anteriores del mes por Suplidor
# ─────────────────────────────────────────
jue_sup_cat = {}
for j in jueves_ant:
    rows = q("""
        SELECT CodigoSuplidor AS cod, Suplidor AS nombre,
               SUM(ValorBrutoPedido) AS valor
        FROM VWpedidosResumenMesactual
        WHERE CAST(PedFecha AS DATE) = ?
        GROUP BY CodigoSuplidor, Suplidor
    """, (str(j),))
    jue_sup_cat[str(j)] = {r['cod']: fv(r['valor']) for r in rows}

# ─────────────────────────────────────────
# 5. Viernes disponibles por Supervisor
# ─────────────────────────────────────────
vie_sup = {}
for v in viernes_disp:
    rows = q("""
        SELECT CodigoSupervisor AS cod, SUM(ValorBrutoPedido) AS valor
        FROM VWpedidosResumenMesactual
        WHERE CAST(PedFecha AS DATE) = ?
        GROUP BY CodigoSupervisor
    """, (str(v),))
    vie_sup[str(v)] = {r['cod']: fv(r['valor']) for r in rows}

# ─────────────────────────────────────────
# 6. MTD diario total (para gráfico)
# ─────────────────────────────────────────
mtd = q("""
    SELECT CAST(PedFecha AS DATE) AS fecha,
           COUNT(DISTINCT PedSecuencia) AS pedidos,
           SUM(ValorBrutoPedido) AS valor
    FROM VWpedidosResumenMesactual
    WHERE YEAR(PedFecha)=2026 AND MONTH(PedFecha)=7
    GROUP BY CAST(PedFecha AS DATE)
    ORDER BY fecha
""")

# ─────────────────────────────────────────
# 7. Detalle de HOY: Supervisor x Suplidor (para cruzar oportunidades)
# ─────────────────────────────────────────
hoy_cruce = q("""
    SELECT CodigoSupervisor AS sup_cod, Supervisorpreventas AS sup_nom,
           CodigoSuplidor AS scat_cod, Suplidor AS scat_nom,
           SUM(ValorBrutoPedido) AS valor
    FROM VWpedidosResumenMesactual
    WHERE CAST(PedFecha AS DATE) = ?
    GROUP BY CodigoSupervisor, Supervisorpreventas, CodigoSuplidor, Suplidor
    ORDER BY CodigoSupervisor, valor DESC
""", (str(hoy),))

# ─────────────────────────────────────────
# 8. Jueves ant: Supervisor x Suplidor (promedio de los 2 jueves)
# ─────────────────────────────────────────
jue_cruce_raw = {}
for j in jueves_ant:
    rows = q("""
        SELECT CodigoSupervisor AS sup_cod, CodigoSuplidor AS scat_cod,
               Suplidor AS scat_nom,
               SUM(ValorBrutoPedido) AS valor
        FROM VWpedidosResumenMesactual
        WHERE CAST(PedFecha AS DATE) = ?
        GROUP BY CodigoSupervisor, CodigoSuplidor, Suplidor
    """, (str(j),))
    for r in rows:
        k = (r['sup_cod'], r['scat_cod'])
        if k not in jue_cruce_raw:
            jue_cruce_raw[k] = {'nom': r['scat_nom'], 'vals': []}
        jue_cruce_raw[k]['vals'].append(fv(r['valor']))

jue_cruce_prom = {k: {'nom': v['nom'], 'prom': sum(v['vals'])/len(v['vals'])}
                  for k, v in jue_cruce_raw.items()}

# ─────────────────────────────────────────
# Serializar y guardar resultado
# ─────────────────────────────────────────
def serializa(obj):
    if isinstance(obj, Decimal): return float(obj)
    if isinstance(obj, date): return obj.isoformat()
    raise TypeError

data = {
    'hoy': str(hoy),
    'jueves_ant': [str(j) for j in jueves_ant],
    'viernes_disp': [str(v) for v in viernes_disp],
    'hoy_sup': hoy_sup,
    'hoy_suplidor': hoy_sup_cat,
    'jue_sup': jue_sup,
    'jue_suplidor': jue_sup_cat,
    'vie_sup': vie_sup,
    'mtd': mtd,
    'hoy_cruce': hoy_cruce,
    'jue_cruce_prom': {f"{k[0]}|{k[1]}": {'nom': v['nom'], 'prom': v['prom']}
                       for k, v in jue_cruce_prom.items()},
}

with open('analisis_data.json', 'w', encoding='utf-8') as f:
    json.dump(data, f, default=serializa, ensure_ascii=False, indent=2)

# Impresión rápida de resumen
print("=== HOY TOTAL ===")
tot_hoy = sum(fv(r['valor']) for r in hoy_sup)
tot_ped = sum(r['pedidos'] for r in hoy_sup)
print(f"Valor: {tot_hoy:>12,.2f}   Pedidos: {tot_ped}")

print("\n=== HOY POR SUPERVISOR ===")
for r in hoy_sup:
    print(f"  {r['nombre'][:30]:<30}  ped:{r['pedidos']:>4}  val:{fv(r['valor']):>12,.2f}")

print("\n=== HOY TOP 10 SUPLIDORES ===")
for r in hoy_sup_cat[:10]:
    print(f"  {r['nombre'][:35]:<35}  val:{fv(r['valor']):>12,.2f}")

print("\n=== JUEVES ANTERIORES TOTALES ===")
for j in jueves_ant:
    t = sum(jue_sup[str(j)].values())
    print(f"  {j}: {t:>12,.2f}")

print("\n=== VIERNES DISPONIBLES TOTALES ===")
for v in viernes_disp:
    rows = q("SELECT SUM(ValorBrutoPedido) AS v FROM VWpedidosResumenMesactual WHERE CAST(PedFecha AS DATE)=?", (str(v),))
    print(f"  {v}: {fv(rows[0]['v']):>12,.2f}")

conn.close()
print("\nDone. Datos en analisis_data.json")
