# encoding: utf-8
import pyodbc, sys, json
from datetime import date, timedelta
sys.path.insert(0, '.')
from config import CONN_STR

conn = pyodbc.connect(CONN_STR, timeout=60)
cur  = conn.cursor()

# ── Hoy y viernes anteriores
hoy = date(2026, 7, 17)
# Calcular los 4 viernes anteriores (dow=4 = viernes en python weekday)
viernes = []
d = hoy - timedelta(days=1)
while len(viernes) < 4:
    if d.weekday() == 4:   # viernes
        viernes.append(d)
    d -= timedelta(days=1)

print("HOY:", hoy)
print("Ultimos 4 viernes:", viernes)
print()

# ── 1. Columnas de la vista
cur.execute("SELECT TOP 0 * FROM VWpedidosResumenMesactual")
cols_all = [d[0] for d in cur.description]
print("COLUMNAS:", cols_all)
print()

# ── 2. Rango de fechas disponible y si hay datos de julio
cur.execute("""
    SELECT CAST(MIN(PedFecha) AS DATE) AS fi, CAST(MAX(PedFecha) AS DATE) AS ff, COUNT(*) AS n
    FROM VWpedidosResumenMesactual
""")
r = cur.fetchone()
print(f"Rango vista: {r[0]} a {r[1]}, filas: {r[2]}")
print()

# ── 3. Datos HOY por Supervisor
cur.execute("""
    SELECT
        CodigoSupervisor,
        Supervisorpreventas,
        COUNT(DISTINCT PedSecuencia) AS pedidos,
        SUM(ValorBrutoPedido)        AS valor
    FROM VWpedidosResumenMesactual
    WHERE CAST(PedFecha AS DATE) = ?
    GROUP BY CodigoSupervisor, Supervisorpreventas
    ORDER BY valor DESC
""", (str(hoy),))
cols = [d[0] for d in cur.description]
hoy_sup = [dict(zip(cols, r)) for r in cur.fetchall()]
print(f"HOY por Supervisor ({len(hoy_sup)} filas):")
for r in hoy_sup: print(" ", r)
print()

# ── 4. Datos HOY por Suplidor (necesito saber si la vista tiene suplidor)
#    Verifico si hay columna tipo 'Sup' o 'Cat3'
sup_col = [c for c in cols_all if 'sup' in c.lower() or 'cat3' in c.lower() or 'suplidor' in c.lower()]
print("Columnas de suplidor en vista:", sup_col)

# Si no hay, uno con Productos + Categoria3
tiene_suplidor = any('suplidor' in c.lower() or 'cat3' in c.lower() for c in cols_all)
pro_col = [c for c in cols_all if 'pro' in c.lower() and ('cod' in c.lower() or 'id' in c.lower())]
print("Columnas de producto:", pro_col)
print()

# ── 5. MTD dia a dia (mes actual)
cur.execute("""
    SELECT
        CAST(PedFecha AS DATE)       AS fecha,
        COUNT(DISTINCT PedSecuencia) AS pedidos,
        SUM(ValorBrutoPedido)        AS valor
    FROM VWpedidosResumenMesactual
    WHERE YEAR(PedFecha) = 2026 AND MONTH(PedFecha) = 7
    GROUP BY CAST(PedFecha AS DATE)
    ORDER BY fecha
""")
cols = [d[0] for d in cur.description]
mtd_dias = [dict(zip(cols, r)) for r in cur.fetchall()]
print(f"MTD Julio dia a dia ({len(mtd_dias)} dias):")
for r in mtd_dias: print(f"  {r['fecha']}  ped:{r['pedidos']}  val:{float(r['valor'] or 0):>12,.2f}")
print()

# ── 6. Ultimos 4 viernes totales
for v in viernes:
    cur.execute("""
        SELECT COUNT(DISTINCT PedSecuencia) AS ped, SUM(ValorBrutoPedido) AS val
        FROM VWpedidosResumenMesactual
        WHERE CAST(PedFecha AS DATE) = ?
    """, (str(v),))
    r = cur.fetchone()
    print(f"Viernes {v}: pedidos={r[0]}, valor={float(r[1] or 0):>12,.2f}")
print()

conn.close()
print("Done.")
