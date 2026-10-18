from flask import Flask, render_template, request, jsonify, session, redirect, url_for
import pyodbc
import hashlib
import json
from datetime import datetime, date, timedelta
from config import CONN_STR, SECRET_KEY
import webbrowser
import threading
import os

app = Flask(__name__)
app.secret_key = SECRET_KEY


# ─────────────────────────────────────────
# BD helpers
# ─────────────────────────────────────────

def get_conn():
    return pyodbc.connect(CONN_STR, timeout=20)


def query(sql, params=None):
    with get_conn() as conn:
        cur = conn.cursor()
        cur.execute(sql, params or [])
        cols = [d[0] for d in cur.description]
        return [dict(zip(cols, row)) for row in cur.fetchall()]


def query_one(sql, params=None):
    rows = query(sql, params)
    return rows[0] if rows else None


def _serial(v):
    if isinstance(v, (datetime, date)):
        return v.isoformat()
    return v


def login_required(f):
    from functools import wraps
    @wraps(f)
    def decorated(*args, **kwargs):
        if "usuario" not in session:
            return redirect(url_for("login"))
        return f(*args, **kwargs)
    return decorated


# ─────────────────────────────────────────
# Auth
# ─────────────────────────────────────────

@app.route("/", methods=["GET"])
def index():
    return redirect(url_for("main") if "usuario" in session else url_for("login"))


@app.route("/login", methods=["GET", "POST"])
def login():
    error = None
    if request.method == "POST":
        usuario = request.form.get("usuario", "").strip()
        clave   = request.form.get("clave", "").strip()
        md5_clave  = hashlib.md5(clave.encode()).hexdigest().upper()
        sha1_clave = hashlib.sha1(clave.encode()).hexdigest().upper()
        row = query_one(
            """SELECT UsuInicioSesion FROM usuariosistema
               WHERE UsuInicioSesion = ?
                 AND (UsuClave = ? OR UPPER(UsuClave) = ? OR UPPER(UsuClave) = ?)
                 AND UsuEstatus = 1""",
            (usuario, clave, md5_clave, sha1_clave)
        )
        if row:
            session["usuario"] = usuario
            return redirect(url_for("main"))
        error = "Usuario o contraseña incorrectos."
    return render_template("login.html", error=error)


@app.route("/logout")
def logout():
    session.clear()
    return redirect(url_for("login"))


@app.route("/main")
@login_required
def main():
    return render_template("index.html", usuario=session.get("usuario"))


# ─────────────────────────────────────────
# API – Empresa
# ─────────────────────────────────────────

@app.route("/api/empresa")
@login_required
def api_empresa():
    row = query_one("SELECT TOP 1 EmpNombre, EmpLogo FROM EMPRESA")
    if row:
        logo_b64 = None
        v = row.get("EmpLogo")
        if v and isinstance(v, (bytes, bytearray)):
            import base64
            logo_b64 = "data:image/png;base64," + base64.b64encode(v).decode()
        elif v and isinstance(v, str) and len(v) > 10:
            logo_b64 = v
        return jsonify({"nombre": row.get("EmpNombre", ""), "logo": logo_b64})
    return jsonify({"nombre": "", "logo": None})


# ─────────────────────────────────────────
# API – Filtros
# ─────────────────────────────────────────

@app.route("/api/suplidores")
@login_required
def api_suplidores():
    rows = query("""
        SELECT DISTINCT cat3.Cat3ID AS id, cat3.Cat3Descripcion AS nombre
        FROM Categoria3 cat3
        JOIN Productos pro ON pro.Cat3ID = cat3.Cat3ID
        ORDER BY cat3.Cat3Descripcion
    """)
    return jsonify(rows)


@app.route("/api/categorias")
@login_required
def api_categorias():
    suplidor_ids = request.args.get("suplidores", "")
    if suplidor_ids and suplidor_ids != "TODOS":
        ids = [s.strip() for s in suplidor_ids.split(",") if s.strip()]
        placeholders = ",".join(["?"] * len(ids))
        rows = query(f"""
            SELECT DISTINCT l.LinID AS id, l.LinDescripcion AS nombre
            FROM lineas l
            JOIN Productos pro ON pro.LinID = l.LinID
            WHERE pro.Cat3ID IN ({placeholders})
            ORDER BY l.LinDescripcion
        """, ids)
    else:
        rows = query("""
            SELECT DISTINCT l.LinID AS id, l.LinDescripcion AS nombre
            FROM lineas l
            JOIN Productos pro ON pro.LinID = l.LinID
            ORDER BY l.LinDescripcion
        """)
    return jsonify(rows)


@app.route("/api/supervisores")
@login_required
def api_supervisores():
    rows = query("""
        SELECT DISTINCT s.RepCodigo AS id, s.RepNombre AS nombre
        FROM representantes r
        JOIN representantes s ON s.RepCodigo = r.RepSupervisor
        WHERE r.RepSupervisor IS NOT NULL AND r.RepSupervisor <> ''
        ORDER BY s.RepNombre
    """)
    return jsonify(rows)


@app.route("/api/representantes")
@login_required
def api_representantes():
    supervisor_ids = request.args.get("supervisores", "")
    if supervisor_ids and supervisor_ids != "TODOS":
        ids = [s.strip() for s in supervisor_ids.split(",") if s.strip()]
        placeholders = ",".join(["?"] * len(ids))
        rows = query(f"""
            SELECT RepCodigo AS id, RepNombre AS nombre
            FROM representantes
            WHERE RepSupervisor IN ({placeholders})
            ORDER BY RepNombre
        """, ids)
    else:
        rows = query("""
            SELECT RepCodigo AS id, RepNombre AS nombre
            FROM representantes
            ORDER BY RepNombre
        """)
    return jsonify(rows)


@app.route("/api/centros")
@login_required
def api_centros():
    rows = query("""
        SELECT CedCodigo AS id, CedDescripcion AS nombre
        FROM CentrosDistribucion
        ORDER BY CedDescripcion
    """)
    return jsonify(rows)


@app.route("/api/clientes")
@login_required
def api_clientes():
    q = request.args.get("q", "")
    if q:
        rows = query("""
            SELECT TOP 300 CAST(CliID AS VARCHAR) AS id, CliNombre AS nombre
            FROM clientes
            WHERE CliNombre LIKE ? OR CAST(CliID AS VARCHAR) LIKE ?
            ORDER BY CliNombre
        """, (f"%{q}%", f"%{q}%"))
    else:
        rows = query("""
            SELECT TOP 500 CAST(CliID AS VARCHAR) AS id, CliNombre AS nombre
            FROM clientes
            ORDER BY CliNombre
        """)
    return jsonify(rows)


# ─────────────────────────────────────────
# API – Reporte: Pedidos vs Facturado vs Devoluciones
# ─────────────────────────────────────────

@app.route("/api/reporte/pedidos_facturacion", methods=["POST"])
@login_required
def reporte_pedidos_facturacion():
    filtros = request.get_json()

    fecha_ini    = filtros.get("fecha_ini", "")
    fecha_fin    = filtros.get("fecha_fin", "")
    suplidores   = filtros.get("suplidores",   [])
    categorias   = filtros.get("categorias",   [])
    supervisores = filtros.get("supervisores", [])
    reps         = filtros.get("representantes", [])
    centros      = filtros.get("centros",      [])
    clientes     = filtros.get("clientes",     [])

    if not fecha_ini or not fecha_fin:
        return jsonify({"error": "Debe indicar rango de fechas."}), 400

    params = [fecha_ini, fecha_fin]
    where_extra = []

    def add_in(field, values):
        vals = [v for v in values if v != "TODOS"]
        if vals:
            ph = ",".join(["?"] * len(vals))
            params.extend(vals)
            where_extra.append(f"{field} IN ({ph})")

    # Supervisores
    add_in("r.RepSupervisor", supervisores)
    # Representantes (CodVendedor en la vista)
    add_in("v.CodVendedor", reps)
    # Centro de distribución (CedCodigo del representante)
    add_in("r.CedCodigo", centros)
    # Clientes
    add_in("p.CliID", clientes)

    # Suplidores y categorías → EXISTS en líneas del pedido
    sup_vals = [v for v in suplidores if v != "TODOS"]
    cat_vals = [v for v in categorias if v != "TODOS"]

    exists_sql = ""
    if sup_vals or cat_vals:
        exists_cond = []
        if sup_vals:
            ph = ",".join(["?"] * len(sup_vals))
            params.extend(sup_vals)
            exists_cond.append(f"pro.Cat3ID IN ({ph})")
        if cat_vals:
            ph = ",".join(["?"] * len(cat_vals))
            params.extend(cat_vals)
            exists_cond.append(f"pro.LinID IN ({ph})")
        exists_sql = f"""
        AND EXISTS (
            SELECT 1 FROM Productos pro
            WHERE pro.ProID = v.ProId
              AND {' AND '.join(exists_cond)}
        )"""

    extra_sql = (" AND " + " AND ".join(where_extra)) if where_extra else ""

    sql = f"""
    SELECT
        CAST(p.PedFecha AS DATE)                                            AS pedfecha,
        ISNULL(sup.RepNombre, 'Sin Supervisor')                             AS supervisor_nombre,
        v.CodVendedor                                                        AS repcodigo,
        v.Vendedor                                                           AS rep_nombre,
        p.CliID                                                              AS cod_cliente,
        c.CliNombre                                                          AS nombre_cliente,
        v.PedSecuencia,
        SUM(ISNULL(v.MontoBrutoPedido,    0))                               AS ValorBrutoPedido,
        SUM(ISNULL(v.MontoBrutoFacturado, 0))                               AS ValorFacturado,
        SUM(ISNULL(v.MontoBrutoDevuelto,  0))                               AS ValorDevuelto,
        SUM(ISNULL(v.MontoBrutoFacturado, 0))
            - SUM(ISNULL(v.MontoBrutoDevuelto, 0))                          AS NetoEntregado
    FROM VwPedidosVsFacutadosMenosDevolucionRM v
    JOIN VwPedidos p
          ON  p.PedSecuencia = v.PedSecuencia
    JOIN clientes c
          ON  c.CliID        = p.CliID
    JOIN representantes r
          ON  r.RepCodigo    = v.CodVendedor
    LEFT JOIN representantes sup
          ON  sup.RepCodigo  = r.RepSupervisor
    WHERE CAST(p.PedFecha AS DATE) BETWEEN ? AND ?
    {extra_sql}
    {exists_sql}
    GROUP BY
        CAST(p.PedFecha AS DATE),
        sup.RepNombre,
        v.CodVendedor,
        v.Vendedor,
        p.CliID,
        c.CliNombre,
        v.PedSecuencia
    ORDER BY
        CAST(p.PedFecha AS DATE),
        ISNULL(sup.RepNombre, ''),
        v.CodVendedor,
        p.CliID,
        v.PedSecuencia
    """

    try:
        rows = query(sql, params)
        result = []
        for row in rows:
            r2 = {}
            for k, v in row.items():
                if isinstance(v, (datetime, date)):
                    r2[k] = v.isoformat()
                elif hasattr(v, '__float__'):
                    r2[k] = float(v)
                else:
                    r2[k] = v
            result.append(r2)
        return jsonify(result)
    except Exception as e:
        return jsonify({"error": str(e)}), 500


# ─────────────────────────────────────────
# Arranque
# ─────────────────────────────────────────

def open_browser():
    webbrowser.open("http://127.0.0.1:5050")


if __name__ == "__main__":
    if os.environ.get("WERKZEUG_RUN_MAIN") != "true":
        threading.Timer(1.5, open_browser).start()
    app.run(host="127.0.0.1", port=5050, debug=False)
