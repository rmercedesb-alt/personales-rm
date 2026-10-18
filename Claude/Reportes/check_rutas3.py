# encoding: utf-8
import pyodbc, sys
sys.path.insert(0, '.')
from config import CONN_STR

conn = pyodbc.connect(CONN_STR, timeout=30)
cur  = conn.cursor()

def q(sql, n=5):
    cur.execute(sql)
    cs = [d[0] for d in cur.description]
    rows = cur.fetchmany(n)
    return cs, rows

# 1. Verificar ClaID en clientes
cs, rows = q("""
    SELECT DISTINCT c.ClaID, COUNT(*) AS n
    FROM clientes c
    JOIN representantes r ON r.RepCodigo = c.RepCodigo
    WHERE r.RepSupervisor IN ('S8','S9')
    GROUP BY c.ClaID ORDER BY n DESC
""", 10)
print("=== ClaID valores S8/S9 ===")
for r in rows: print(r)

# tabla de clasificacion?
try:
    cur.execute("SELECT TOP 5 * FROM ClasificacionesClientes")
    cs2 = [d[0] for d in cur.description]
    print("\n=== ClasificacionesClientes ===", cs2)
    for r in cur.fetchall(): print(r)
except Exception as e: print("ClasificacionesClientes:", e)

try:
    cur.execute("SELECT TOP 5 * FROM Clasificaciones")
    cs2 = [d[0] for d in cur.description]
    print("\n=== Clasificaciones ===", cs2)
    for r in cur.fetchall(): print(r)
except Exception as e: print("Clasificaciones:", e)

# 2. Obtener TamCte desde VW (una sola fila por CliID)
cs, rows = q("""
    SELECT DISTINCT ClienteCodigo, MAX([Tama\xf1oCte]) AS tamano
    FROM VWpedidosResumenMesactual
    WHERE YEAR(PedFecha) = 2026
    GROUP BY ClienteCodigo
""")
print("\n=== TamCte desde VW (muestra) ===")
for r in rows: print(r)
print()

# 3. Dia de visita desde historico (DATEPART WEEKDAY en SQL Server: 1=Dom, 2=Lun, ... 7=Sab)
cs, rows = q("""
    SELECT TOP 5 h.RepCodigo, h.CliID, h.RutFecha,
           DATENAME(WEEKDAY, h.RutFecha) AS DiaNombre,
           DATEPART(WEEKDAY, h.RutFecha) AS DiaNro
    FROM RutaVisitasFechaHistoricos h
    JOIN representantes r ON r.RepCodigo = h.RepCodigo
    WHERE h.RutFecha BETWEEN '2026-06-01' AND '2026-06-30'
      AND r.RepSupervisor IN ('S8','S9')
""")
print("=== Nombre dia semana (DATENAME) ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# 4. Verificar el mismo RepCodigo en clientes y en historico
# (cliente debe pertenecer al mismo vendedor)
cs, rows = q("""
    SELECT TOP 3 h.RepCodigo, h.CliID, c.RepCodigo AS CliRepCodigo, c.CliNombre
    FROM RutaVisitasFechaHistoricos h
    JOIN clientes c ON c.CliID = h.CliID
    JOIN representantes r ON r.RepCodigo = h.RepCodigo
    WHERE h.RutFecha BETWEEN '2026-06-01' AND '2026-06-30'
      AND r.RepSupervisor IN ('S8','S9')
      AND h.RepCodigo != c.RepCodigo  -- casos donde rep en historico != rep en clientes
""")
print("=== Historico rep != clientes rep (muestra) ===")
print("Cols:", cs)
for r in rows: print(r)
print()

conn.close()
print("Done.")
