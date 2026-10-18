# -*- coding: utf-8 -*-
import sys
from pathlib import Path
from docx_style import *
from supervisor_data import bundle, SUPERVISORES

DESTDIR = Path(r"C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Septiembre 2026\Seguimiento")

def money(x): return "{:,.0f}".format(x)
def pct(x): return "{:.0f}%".format(x) if x==x else "n/d"
def shade_pct(v, ok=95, warn=80):
    if v < warn: return "FAE5E3"
    if v < ok: return "FDF2E4"
    return "E3F3E9"

def build(key):
    b = bundle(key); cfg = b["cfg"]; nombre = cfg["nombre"]
    dest = DESTDIR / f"Informe_{key.capitalize()}_Sep2026.docx"

    doc = nuevo_doc()
    portada(doc, f"Reuni\u00f3n con {nombre}", f"{cfg['detalle']} \u2013 Informe de seguimiento",
            "Corte al 19 de septiembre de 2026 (d\u00eda 19 de 30)  \u00b7  Fuente: MovilBusiness5STD  \u00b7  AM Comercial, confidencial")
    doc.add_page_break()

    # PARTE 1
    h1(doc, "1. Logro de presupuesto vs. venta al d\u00eda")
    t = b["tot"]
    parrafo(doc, f"Presupuesto del mes: RD$ {money(t.PresupuestoMes)}. Presupuesto al d\u00eda: RD$ {money(t.PresupuestoAlDia)}. "
                 f"Ejecutado: RD$ {money(t.Ejecutado)} ({pct(t.PctAlcance)} de alcance). Proyecci\u00f3n de cierre: RD$ {money(t.Proyectado)} ({pct(t.PctProyVsMes)} del mes).")
    h2(doc, "1.1 Por vendedor")
    headers = ["Vendedor","Canal","Presup. del mes","Presup. al d\u00eda","Ejecutado","Diferencia","% Alcance"]
    rows=[]; shade={}
    for i,r in enumerate(b["por_vend"].itertuples(), start=1):
        rows.append([r.RepNombre, r.Canal, money(r.PresupuestoMes), money(r.PresupuestoAlDia), money(r.Ejecutado), money(r.Diferencia), pct(r.PctAlcance)])
        shade[(i,6)] = shade_pct(r.PctAlcance)
    tabla(doc, headers, rows, cell_shade=shade)
    doc.add_paragraph()

    h2(doc, "1.2 Por suplidor (foco Colombina)")
    headers = ["Suplidor","Presup. al d\u00eda","Ejecutado","Diferencia","% Alcance"]
    rows=[]; shade={}
    for i,r in enumerate(b["por_suplidor"].itertuples(), start=1):
        rows.append([r.Suplidor, money(r.PresupuestoAlDia), money(r.Ejecutado), money(r.Diferencia), pct(r.PctAlcance)])
        shade[(i,4)] = shade_pct(r.PctAlcance)
    tabla(doc, headers, rows, cell_shade=shade)
    col = b["por_suplidor"][b["por_suplidor"].Suplidor.str.contains("COLOMBINA", case=False, na=False)]
    if len(col):
        c = col.iloc[0]
        callout(doc, f"Colombina: {pct(c.PctAlcance)} de alcance al d\u00eda (RD$ {money(c.Ejecutado)} de RD$ {money(c.PresupuestoAlDia)} esperado).",
                color=ROJO if c.PctAlcance<80 else (AMBAR if c.PctAlcance<95 else VERDE))
    doc.add_page_break()

    # PARTE 2
    h1(doc, "2. Clientes de alta capacidad de compra vs. septiembre")
    top = b["topN"]
    parrafo(doc, f"Se identificaron {len(top)} clientes con mayor promedio mensual de compra (marzo-agosto 2026). "
                 f"En conjunto representan RD$ {money(top.PromedioMensual.sum())}/mes; lo esperado a la fecha (19 de 30 d\u00edas) es RD$ {money(top.EsperadoAlDia.sum())} "
                 f"y llevan comprado RD$ {money(top.CompradoSep.sum())} ({pct(top.CompradoSep.sum()/top.EsperadoAlDia.sum()*100)} de cumplimiento).")
    headers=["Vendedor","Cliente","Tipo","Promedio mensual","Esperado a la fecha","Comprado Sept.","% Cumpl."]
    rows=[]; shade={}
    for i,r in enumerate(top.itertuples(), start=1):
        rows.append([r.NombreVendedor, r.ClienteNombre, "", money(r.PromedioMensual), money(r.EsperadoAlDia), money(r.CompradoSep), pct(r.PctCumpl)])
        shade[(i,6)] = shade_pct(r.PctCumpl, ok=100, warn=50)
    tabla(doc, headers, rows, cell_shade=shade)
    doc.add_page_break()

    # PARTE 3
    h1(doc, "3. Clientes no visitados en lo que va de septiembre")
    pctnv = b["tot_novis"]/b["tot_cartera"]*100
    parrafo(doc, f"Cartera activa: {b['tot_cartera']} clientes. Visitados del 1 al 19 de septiembre: {b['tot_visitados']}. "
                 f"Sin visitar: {b['tot_novis']} ({pct(pctnv)} de la cartera).")
    h2(doc, "3.1 Por vendedor")
    headers=["Vendedor","Cartera","Visitados","No visitados","% no visitado"]
    rows=[]; shade={}
    for i,r in enumerate(b["por_vend_vis"].itertuples(), start=1):
        rows.append([r.RepNombre, int(r.Cartera), int(r.Visitados), int(r.NoVisitados), pct(r.PctNoVisitado)])
        shade[(i,4)] = shade_pct(100-r.PctNoVisitado, ok=90, warn=80)
    tabla(doc, headers, rows, cell_shade=shade)
    doc.add_paragraph()
    h2(doc, "3.2 Clientes de mayor valor sin visitar")
    top_nv = b["no_vis"].head(15)
    headers=["Vendedor","Cliente","Tipo de negocio","Promedio mensual RD$"]
    rows=[[r.RepNombre, r.CliNombre, r.TipoNegocio, money(r.PromedioMensual)] for r in top_nv.itertuples()]
    tabla(doc, headers, rows)
    callout(doc, f"Potencial mensual en riesgo por falta de visita (toda la cartera no visitada): RD$ {money(b['no_vis'].PromedioMensual.sum())}.", color=ROJO)
    doc.add_page_break()

    # PARTE 4
    h1(doc, "4. Plan de acci\u00f3n para lo que resta del mes")
    peor_vend = b["por_vend"].sort_values("PctAlcance").iloc[0]
    n_visita = min(10, len(b["no_vis"])); monto_visita = b["no_vis"].head(n_visita).PromedioMensual.sum()
    headers=["Prioridad","Detalle","Meta"]
    rows=[
        ["Cerrar brecha de presupuesto", f"{peor_vend.RepNombre} tiene el menor alcance ({pct(peor_vend.PctAlcance)}); acompa\u00f1amiento del supervisor esta semana.", f"Alcance \u2265 90%"],
        ["Visita especial a clientes de valor", f"{n_visita} clientes sin visitar suman RD$ {money(monto_visita)}/mes de promedio.", f"{n_visita}/{n_visita} visitados"],
        ["Activar oferta 3 Bolones + 1 Tipitin", f"{b['of_total']} clientes activados a la fecha (OfeID 3909/3911, vigente hasta el 30-sep).", "Sumar activaciones nuevas en clientes de valor"],
        ["Sostener el ritmo del equipo", f"Alcance general {pct(t.PctAlcance)}; proyecci\u00f3n de cierre RD$ {money(t.Proyectado)} ({pct(t.PctProyVsMes)} del mes).", "No bajar del ritmo actual"],
    ]
    tabla(doc, headers, rows)
    doc.add_page_break()

    # PARTE 5
    h1(doc, "5. Cierre y compromisos")
    parrafo(doc, f"Resumen de la reuni\u00f3n con {nombre}: se revisaron presupuesto vs. venta al d\u00eda, capacidad de compra de los clientes m\u00e1s grandes, "
                 "cobertura de visitas y un plan de acci\u00f3n concreto para lo que resta de septiembre.")
    callout(doc, "Compromiso del equipo: dar seguimiento semanal a estos 4 puntos con evidencia de visita y resultado en RD$.", color=VERDE)

    doc.save(str(dest))
    print("OK ->", dest)

if __name__ == "__main__":
    keys = sys.argv[1:] or list(SUPERVISORES.keys())
    for k in keys:
        build(k)
