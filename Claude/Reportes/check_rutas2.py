# encoding: utf-8
import pyodbc, sys
sys.path.insert(0, '.')
from config import CONN_STR

conn = pyodbc.connect(CONN_STR, timeout=20)
cur  = conn.cursor()

def q(sql, n=10):
    cur.execute(sql)
    cs = [d[0] for d in cur.description]
    rows = cur.fetchmany(n)
    return cs, rows

# 1. Verificar campo RutFecha en historicos y rango de junio
cs, rows = q("""
    SELECT TOP 3 RepCodigo, CliID, RutFecha, RutPosicion
    FROM RutaVisitasFechaHistoricos
    WHERE RutFecha BETWEEN '2026-06-01' AND '2026-06-30'
    ORDER BY RutFecha DESC
""")
print("=== Historicos junio con RutFecha ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# 2. Contar historicos de junio S8/S9
cs, rows = q("""
    SELECT COUNT(*) AS total, COUNT(DISTINCT h.CliID) AS clientes_distintos
    FROM RutaVisitasFechaHistoricos h
    JOIN representantes r ON r.RepCodigo = h.RepCodigo
    WHERE h.RutFecha BETWEEN '2026-06-01' AND '2026-06-30'
      AND r.RepSupervisor IN ('S8','S9')
""", 1)
print("=== Historicos junio S8/S9 ===")
for r in rows: print(dict(zip(cs,r)))
print()

# 3. Campo TiNID en clientes - qué representa
cs, rows = q("""
    SELECT TOP 3 c.CliID, c.CliNombre, c.TiNID, c.CliTipoLocal, c.CliTipoCliente,
                 c.CliTipoCliente1, c.CliTipoCliente2
    FROM clientes c
    JOIN representantes r ON r.RepCodigo = c.RepCodigo
    WHERE r.RepSupervisor IN ('S8','S9')
""")
print("=== Clientes S8/S9 muestra ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# 4. TiNID - qué es?
try:
    cs, rows = q("SELECT TOP 5 * FROM TiposNegocio")
    print("=== TiposNegocio ===")
    print("Cols:", cs)
    for r in rows: print(r)
except: pass

try:
    cs, rows = q("SELECT TOP 5 * FROM TiposCliente")
    print("=== TiposCliente ===")
    for r in rows: print(r)
except: pass

# 5. VWpedidosResumenMesactual tiene TamCte - ver relacion con clientes
cs, rows = q("""
    SELECT TOP 3 v.ClienteCodigo, v.ClienteNombre, v.[Tama\xf1oCte]
    FROM VWpedidosResumenMesactual v
    WHERE CAST(v.PedFecha AS DATE) = '2026-07-17'
""")
print("=== VW TamCte muestra ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# 6. Valores distintos de TamCte
cs, rows = q("""
    SELECT DISTINCT [Tama\xf1oCte], COUNT(*) AS n
    FROM VWpedidosResumenMesactual
    WHERE YEAR(PedFecha) = 2026
    GROUP BY [Tama\xf1oCte]
    ORDER BY n DESC
""", 20)
print("=== TamCte valores ===")
for r in rows: print(r)
print()

# 7. Todos los RepCodigos de S8 y S9
cs, rows = q("""
    SELECT RepCodigo, RepNombre, RepSupervisor
    FROM representantes
    WHERE RepSupervisor IN ('S8','S9')
    ORDER BY RepSupervisor, RepNombre
""", 50)
print("=== Todos representantes S8/S9 ===")
for r in rows: print(r)
print()

# 8. Entender RutSemana: qué posicion es qué dia
# '1000000' en posicion 0 = dia 1 = Lunes?
cs, rows = q("""
    SELECT TOP 3 rv.RepCodigo, rv.CliID, rv.RutSemana1, rv.RutSemana2, rv.RutSemana3, rv.RutSemana4, rv.RutSemana5
    FROM RutaVisitas rv
    JOIN representantes r ON r.RepCodigo = rv.RepCodigo
    WHERE r.RepSupervisor IN ('S8','S9')
""")
print("=== RutaVisitas muestra S8/S9 ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# 9. Clientes que estaban en historico junio pero NO en RutaVisitas actualmente (S8/S9)
cs, rows = q("""
    SELECT COUNT(*) AS total
    FROM (
        SELECT DISTINCT h.RepCodigo, h.CliID
        FROM RutaVisitasFechaHistoricos h
        JOIN representantes r ON r.RepCodigo = h.RepCodigo
        WHERE h.RutFecha BETWEEN '2026-06-01' AND '2026-06-30'
          AND r.RepSupervisor IN ('S8','S9')
    ) hist
    WHERE NOT EXISTS (
        SELECT 1 FROM RutaVisitas rv
        WHERE rv.RepCodigo = hist.RepCodigo
          AND rv.CliID = hist.CliID
    )
""", 1)
print("=== Clientes en historico pero NO en RutaVisitas (S8/S9) ===")
print(rows)

conn.close()
print("\nDone.")
