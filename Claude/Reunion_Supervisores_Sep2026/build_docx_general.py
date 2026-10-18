# -*- coding: utf-8 -*-
import pandas as pd
from pathlib import Path
from docx_style import *

SC = Path(__file__).parent
DEST = Path(r"C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Septiembre 2026\Seguimiento") / "Informe_Supervisores_General_Sep2026.docx"

def P(n): return pd.read_pickle(SC/f"{n}.pkl")
def money(x): return "{:,.0f}".format(x)
def pct(x): return "{:.0f}%".format(x) if x==x else "n/d"
def shade_pct(v, ok=95, warn=80):
    if v < warn: return "FAE5E3"
    if v < ok: return "FDF2E4"
    return "E3F3E9"

ORDEN_SUP = ["Ramon E. Pimentel","Eliezer Martinez","Narciso Pina Bello","Cristiano Perez","Disponible Sur Corto"]
NOM = {"Ramon E. Pimentel":"Ram\u00f3n E. Pimentel (SDE+SDN)","Eliezer Martinez":"Eliezer Mart\u00ednez (SDO+DN)",
       "Narciso Pina Bello":"Narciso Pi\u00f1a Bello (Sur Largo)","Cristiano Perez":"Cristiano P\u00e9rez (Mini Market+Mayorista)",
       "Disponible Sur Corto":"Sur Corto (supervisor por asignar)"}

por_sup = P("r_por_supervisor"); por_canal = P("r_por_canal"); por_sup_canal = P("r_por_sup_canal")
colombina_sup = P("r_colombina_sup"); minorista_sup_suplidor = P("r_minorista_sup_suplidor")
r50 = P("r_resumen_top50_sup"); no_vis_sup = P("r_visitas_sup"); no_vis_det = P("r_no_visitados_detalle")
of_sup = P("r_oferta_por_sup")

doc = nuevo_doc()
portada(doc, "Reuni\u00f3n Semanal de Supervisores",
        "Presupuesto, capacidad de compra, visitas y plan de acci\u00f3n \u2013 Informe General",
        "Corte al 19 de septiembre de 2026 (d\u00eda 19 de 30)  \u00b7  Fuente: MovilBusiness5STD  \u00b7  AM Comercial, confidencial")
doc.add_page_break()

# ============ PARTE 1 ============
h1(doc, "1. Logro de presupuesto vs. venta al d\u00eda")
parrafo(doc, "Presupuesto al d\u00eda = presupuesto del mes prorrateado seg\u00fan los d\u00edas h\u00e1biles transcurridos (15 de 26 al 17-sep, "
             "seg\u00fan c\u00e1lculo del sistema). Ejecutado = venta facturada a la fecha. Proyecci\u00f3n = cierre estimado de mes seg\u00fan el ritmo actual.")

h2(doc, "1.1 Resumen por supervisor")
tot = por_sup[por_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"].set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
headers = ["Supervisor","Presup. del mes","Presup. al d\u00eda","Ejecutado","Diferencia","% Alcance","Proyecci\u00f3n","% vs. mes"]
rows=[]; shade={}
for i,r in enumerate(tot.itertuples(), start=1):
    rows.append([NOM[r.SupervisorGrp], money(r.PresupuestoMes), money(r.PresupuestoAlDia), money(r.Ejecutado),
                 money(r.Diferencia), pct(r.PctAlcance), money(r.Proyectado), pct(r.PctProyVsMes)])
    shade[(i,5)] = shade_pct(r.PctAlcance)
tabla(doc, headers, rows, cell_shade=shade)
doc.add_paragraph()

h2(doc, "1.2 Por canal de venta")
headers=["Canal","Presup. al d\u00eda","Ejecutado","Diferencia","% Alcance"]
rows=[]; shade={}
for i,r in enumerate(por_canal.itertuples(), start=1):
    rows.append([r.Canal, money(r.PresupuestoAlDia), money(r.Ejecutado), money(r.Diferencia), pct(r.PctAlcance)])
    shade[(i,4)] = shade_pct(r.PctAlcance)
tabla(doc, headers, rows, cell_shade=shade)
callout(doc, "Minorista alcanza 93% al d\u00eda; Mayorista (equipo de Cristiano P\u00e9rez) est\u00e1 en 53% \u2014 la brecha m\u00e1s grande de la compa\u00f1\u00eda.", color=ROJO)
doc.add_paragraph()

h2(doc, "1.3 Foco: Canal Minorista y suplidor Colombina")
parrafo(doc, "Colombina fue el suplidor foco de gesti\u00f3n hasta hoy. Alcance por supervisor:")
headers=["Supervisor","Presup. al d\u00eda","Ejecutado","Diferencia","% Alcance"]
rows=[]; shade={}
cs = colombina_sup[colombina_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"].set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
for i,r in enumerate(cs.itertuples(), start=1):
    rows.append([NOM[r.SupervisorGrp], money(r.PresupuestoAlDia), money(r.Ejecutado), money(r.Diferencia), pct(r.PctAlcance)])
    shade[(i,4)] = shade_pct(r.PctAlcance)
tabla(doc, headers, rows, cell_shade=shade)
doc.add_paragraph()
parrafo(doc, "Top suplidores dentro del canal Minorista (RD$):", bold=True)
top_sup_min = minorista_sup_suplidor.groupby("Suplidor").agg(PresupuestoAlDia=("PresupuestoAlDia","sum"), Ejecutado=("Ejecutado","sum")).reset_index()
top_sup_min["Pct"] = top_sup_min.Ejecutado/top_sup_min.PresupuestoAlDia*100
top_sup_min = top_sup_min.sort_values("PresupuestoAlDia", ascending=False).head(8)
headers=["Suplidor","Presup. al d\u00eda","Ejecutado","% Alcance"]
rows=[]; shade={}
for i,r in enumerate(top_sup_min.itertuples(), start=1):
    rows.append([r.Suplidor, money(r.PresupuestoAlDia), money(r.Ejecutado), pct(r.Pct)])
    shade[(i,3)] = shade_pct(r.Pct)
tabla(doc, headers, rows, cell_shade=shade)
callout(doc, "Colombina cierra en 79% de alcance general; los huecos mayores est\u00e1n en Narciso Pi\u00f1a (65%) y Cristiano P\u00e9rez (73%).", color=AMBAR)
doc.add_paragraph()

h2(doc, "1.4 Qu\u00e9 debemos atender esta semana")
peor = por_sup_canal[(por_sup_canal.SupervisorGrp=="Cristiano Perez")&(por_sup_canal.Canal=="Mayorista")].iloc[0]
for txt in [
    f"Mayorista (Cristiano P\u00e9rez) es la brecha m\u00e1s grande: 53% de alcance al d\u00eda; faltan RD$ {money(-peor.Diferencia)} para llegar al ritmo del presupuesto.",
    "Colombina no llega al ritmo en 4 de 5 equipos; Narciso Pi\u00f1a (65%) y Cristiano P\u00e9rez (73%) son la prioridad de gesti\u00f3n.",
    "Los equipos Minorista (Ram\u00f3n E. Pimentel, Eliezer Mart\u00ednez, Narciso Pi\u00f1a) promedian 94% de alcance: mantener el ritmo en la \u00faltima semana.",
    "Continental de Negocios y Molinos Modernos representan RD$ 44.6MM (50% del presupuesto del mes); su ejecuci\u00f3n (89% y 93%) marca el resultado general.",
]:
    callout(doc, txt)
doc.add_page_break()

# ============ PARTE 2 ============
h1(doc, "2. Clientes de alta capacidad de compra vs. lo comprado en septiembre")
parrafo(doc, "Promedio mensual = venta bruta promedio marzo-agosto 2026 por cliente. Esperado a la fecha = promedio mensual x (19/30 d\u00edas). "
             "Se analizan los 50 clientes de mayor promedio mensual (Top 50) de cada supervisor.")
h2(doc, "2.1 Resumen Top 50 por supervisor")
headers=["Supervisor","Clientes","Promedio mensual","Esperado a la fecha","Comprado Sept.","% Cumplimiento"]
rows=[]; shade={}
rr = r50[r50.SupervisorGrp!="Rudy Mercedes (Institucional)"].set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
for i,r in enumerate(rr.itertuples(), start=1):
    rows.append([NOM[r.SupervisorGrp], int(r.Clientes), money(r.PromedioMensual), money(r.EsperadoAlDia), money(r.CompradoSep), pct(r.PctCumpl)])
    shade[(i,5)] = shade_pct(r.PctCumpl, ok=100, warn=80)
tabla(doc, headers, rows, cell_shade=shade)
callout(doc, "La cartera Top 50 de Cristiano tiene el mayor potencial (RD$ 8.4MM/mes) y el peor cumplimiento (21%): la oportunidad m\u00e1s grande y m\u00e1s urgente.", color=ROJO)
doc.add_paragraph()

h2(doc, "2.2 Sub-an\u00e1lisis por suplidor foco")
parrafo(doc, "Universo de clientes con historial de compra en los \u00faltimos 6 meses, por suplidor:")
headers=["Suplidor","Clientes con historial","Promedio mensual","Esperado a la fecha","Comprado Sept.","Brecha","% Cumpl."]
rows=[]; shade={}
i=1
for nombre,archivo in [("Continental de Negocios","continental"),("Molinos Modernos","molinos"),("Colombina","colombina"),("Brinsa Dominicana (Refisal)","brinsa")]:
    m = pd.read_pickle(SC/f"r_cap_{archivo}.pkl")
    prom=m.PromedioMensual.sum(); esp=m.EsperadoAlDia.sum(); comp=m.CompradoSep.sum(); brec=m.Brecha.sum(); pcc=comp/esp*100
    rows.append([nombre, len(m), money(prom), money(esp), money(comp), money(brec), pct(pcc)])
    shade[(i,6)] = shade_pct(pcc, ok=100, warn=80); i+=1
tabla(doc, headers, rows, cell_shade=shade)
for txt in [
    "Continental y Molinos concentran el mayor volumen de clientes activos (18.3k y 5.9k) y ya van pr\u00e1cticamente al ritmo esperado.",
    "Colombina tiene 16.5k clientes con historial pero una brecha de RD$ 752 mil a la fecha, confirmando la se\u00f1al de la parte 1.",
    "Brinsa (Refisal) tiene una brecha menor (RD$ 254 mil) con 15.6k clientes de historial: campo f\u00e9rtil para venta cruzada en la \u00faltima semana.",
]:
    callout(doc, txt)
doc.add_page_break()

# ============ PARTE 3 ============
h1(doc, "3. Clientes no visitados en lo que va de septiembre")
parrafo(doc, "Cartera activa por vendedor (clientes.CliEstatus=1) comparada contra clientes con al menos una visita registrada "
             "del 1 al 19 de septiembre (tablas Visitas + VisitasHistoricos).")
h2(doc, "3.1 Resumen por supervisor")
headers=["Supervisor","Cartera","Visitados","No visitados","% no visitado"]
rows=[]; shade={}
nv = no_vis_sup[no_vis_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"].set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
for i,r in enumerate(nv.itertuples(), start=1):
    rows.append([NOM[r.SupervisorGrp], int(r.Cartera), int(r.Visitados), int(r.NoVisitados), pct(r.PctNoVisitado)])
    shade[(i,4)] = shade_pct(100-r.PctNoVisitado, ok=90, warn=80)
tot_nv = no_vis_sup[no_vis_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"]
rows.append(["TOTAL EQUIPO", int(tot_nv.Cartera.sum()), int(tot_nv.Visitados.sum()), int(tot_nv.NoVisitados.sum()), pct(tot_nv.NoVisitados.sum()/tot_nv.Cartera.sum()*100)])
tabla(doc, headers, rows, cell_shade=shade, bold_last_row=True)
callout(doc, "228 de los 884 clientes de Cristiano (26%) no han sido visitados en 19 d\u00edas de mes: m\u00e1s del doble que cualquier otro equipo.", color=ROJO)
doc.add_paragraph()

h2(doc, "3.2 Top 15 clientes de mayor valor sin visitar")
headers=["Supervisor","Vendedor","Cliente","Tipo de negocio","Promedio mensual RD$"]
rows=[]
for r in no_vis_det.head(15).itertuples():
    rows.append([NOM[r.SupervisorGrp], r.RepNombre, r.CliNombre, r.TipoNegocio, money(r.PromedioMensual)])
tabla(doc, headers, rows)
callout(doc, f"Potencial mensual total en riesgo por falta de visita (toda la cartera no visitada): RD$ {money(no_vis_det.PromedioMensual.sum())}.", color=ROJO)
callout(doc, "14 de los 15 clientes de mayor valor sin visitar son de la cartera de Cristiano P\u00e9rez (Jhonny Rosario y Wascar Alvarez), sumando RD$ 941 mil de promedio mensual.", color=ROJO)
doc.add_page_break()

# ============ PARTE 4 ============
h1(doc, "4. Plan de acci\u00f3n para lo que resta del mes")
h2(doc, "4.1 Visita especial a clientes de volumen sin atender")
parrafo(doc, "Cruce de las partes 2 y 3: clientes de alta capacidad de compra con brecha o sin visita registrada. Requiere visita "
             "del supervisor junto al vendedor, no solo del vendedor.")
headers=["Meta de la semana","Indicador","L\u00ednea base"]
rows=[
 ["Visitar a los 14 clientes Top de Cristiano sin atender","14 de 14 visitados","0 visitados a la fecha"],
 ["Cerrar brecha de Colombina en Narciso y Cristiano","Alcance \u2265 90% al d\u00eda","65% y 73%"],
 ["Recuperar cumplimiento Top 50 en Cristiano","\u2265 60% de lo esperado","21% actual"],
]
tabla(doc, headers, rows)
doc.add_paragraph()

h2(doc, "4.2 Activaci\u00f3n de oferta \"3 Bolones + 1 Tipitin\"")
tot_of = int(of_sup.ClientesActivados.sum())
parrafo(doc, f"Oferta OfeID 3911 (3 BBB + 1 Tipitin), vigente del 3 al 30 de septiembre 2026, comparada contra el detalle de venta del mes (VWVENTASRESUMEN2026, IndicadorOferta). "
             f"{tot_of:,} clientes distintos han sido activados con la oferta en toda la compa\u00f1\u00eda del 1 al 19 de septiembre.")
headers=["Supervisor","Clientes activados (Sept. 1-19)"]
rows=[]
ofx = of_sup.set_index("SupervisorGrp").reindex(ORDEN_SUP).fillna(0).reset_index()
for r in ofx.itertuples():
    rows.append([NOM[r.SupervisorGrp], int(r.ClientesActivados)])
tabla(doc, headers, rows)
callout(doc, "Cristiano P\u00e9rez solo ha activado 18 clientes con esta oferta, muy por debajo de cualquier otro equipo (537 a 1,027). "
             "Es la palanca m\u00e1s r\u00e1pida para reactivar su cartera: no requiere negociaci\u00f3n, solo visita y pedido.", color=VERDE)
parrafo(doc, "Meta propuesta: activar 150 clientes adicionales con esta oferta en Mayorista (Cristiano) antes del cierre de mes, priorizando "
             "los clientes de volumen identificados en la secci\u00f3n 3.2.")
doc.add_page_break()

# ============ PARTE 5 ============
h1(doc, "5. Cierre y compromisos del equipo")
headers=["#","Compromiso","Responsable","Meta"]
rows=[
 ["1","Visita conjunta a los 14 clientes Top de Cristiano sin atender","Cristiano P\u00e9rez + supervisor comercial","14/14 visitados esta semana"],
 ["2","Reforzar Colombina en las rutas de menor alcance","Narciso Pi\u00f1a Bello, Cristiano P\u00e9rez","Alcance \u2265 90% al d\u00eda"],
 ["3","Activar 150 clientes con oferta 3 Bolones+1 Tipitin en Mayorista","Cristiano P\u00e9rez","+150 clientes activados"],
 ["4","Mantener el ritmo actual (94% de alcance) en Minorista","Ram\u00f3n E. Pimentel, Eliezer Mart\u00ednez","No bajar de 90% al cierre"],
 ["5","Reducir clientes no visitados por debajo del 10% en todos los equipos","Todos los supervisores","< 10% no visitado"],
]
tabla(doc, headers, rows)
doc.add_paragraph()
callout(doc, "Pr\u00f3xima reuni\u00f3n: seguimiento de estos 5 compromisos con evidencia de visita y resultado en RD$ de cada acci\u00f3n.", color=VERDE)

doc.save(str(DEST))
print("OK ->", DEST)
