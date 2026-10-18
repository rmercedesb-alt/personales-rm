# encoding: utf-8
import pyodbc, sys
sys.path.insert(0, '.')
from config import CONN_STR

conn = pyodbc.connect(CONN_STR, timeout=20)
cur  = conn.cursor()

def cols(t):
    try:
        cur.execute(f"SELECT TOP 0 * FROM [{t}]")
        return [d[0] for d in cur.description]
    except Exception as e:
        return f"ERROR: {e}"

def sample(sql, n=3):
    try:
        cur.execute(sql)
        cs = [d[0] for d in cur.description]
        rows = cur.fetchmany(n)
        return cs, rows
    except Exception as e:
        return [], [str(e)]

print("=== RutaVisitas ===")
print(cols('RutaVisitas'))
cs, rows = sample("SELECT TOP 3 * FROM RutaVisitas")
for r in rows: print(dict(zip(cs,r)))
print()

print("=== RutaVisitasFechaHistoricos ===")
print(cols('RutaVisitasFechaHistoricos'))
cs, rows = sample("SELECT TOP 3 * FROM RutaVisitasFechaHistoricos ORDER BY 1 DESC")
for r in rows: print(dict(zip(cs,r)))
print()

# Rango de fechas en historicos
cs, rows = sample("SELECT MIN(RvfFecha) AS fi, MAX(RvfFecha) AS ff, COUNT(*) AS n FROM RutaVisitasFechaHistoricos")
print("Rango historicos:", rows)
print()

# Clientes - campos relevantes
print("=== clientes (columnas relevantes) ===")
c = cols('clientes')
print(c)
# Buscar campo TamanoCliente / Tamano
tam = [x for x in c if 'tam' in x.lower() or 'tama' in x.lower() or 'size' in x.lower()]
print("Campos tamaño:", tam)
print()

# Representantes S8/S9
cs, rows = sample("""
    SELECT RepCodigo, RepNombre, RepSupervisor
    FROM representantes
    WHERE RepSupervisor IN ('S8','S9')
    ORDER BY RepSupervisor, RepCodigo
""", 20)
print("=== Representantes S8/S9 ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# ¿Los historicos tienen RepCodigo y CliID?
cs, rows = sample("""
    SELECT TOP 5 * FROM RutaVisitasFechaHistoricos
    WHERE RvfFecha BETWEEN '2026-06-01' AND '2026-06-30'
    ORDER BY RvfFecha DESC
""")
print("=== Historicos junio 2026 ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# ¿RutaVisitas tiene RepCodigo y CliID?
cs, rows = sample("SELECT TOP 5 * FROM RutaVisitas")
print("=== RutaVisitas muestra ===")
print("Cols:", cs)
for r in rows: print(r)
print()

# Contar historicos de junio con supervisores S8/S9
cs, rows = sample("""
    SELECT COUNT(*) AS n_hist, COUNT(DISTINCT h.CliID) AS clientes
    FROM RutaVisitasFechaHistoricos h
    JOIN representantes r ON r.RepCodigo = h.RepCodigo
    WHERE h.RvfFecha BETWEEN '2026-06-01' AND '2026-06-30'
      AND r.RepSupervisor IN ('S8','S9')
""")
print("=== Historicos junio S8/S9 ===")
print(rows)

conn.close()
print("\nDone.")
