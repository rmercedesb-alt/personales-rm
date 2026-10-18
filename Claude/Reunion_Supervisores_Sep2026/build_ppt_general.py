# -*- coding: utf-8 -*-
"""PPT general - Reunion Semanal de Supervisores, Septiembre 2026 (max 12 laminas)."""
import pandas as pd, numpy as np
from pathlib import Path
from pptx.util import Inches, Pt
from pptx.enum.text import PP_ALIGN
from style import *

SC = Path(__file__).parent
IMG = SC / "img"
DEST = Path(r"C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Septiembre 2026\Seguimiento") / "Reunion_Supervisores_General_Sep2026.pptx"

def P(n): return pd.read_pickle(SC/f"{n}.pkl")

por_sup = P("r_por_supervisor")
por_canal = P("r_por_canal")
por_sup_canal = P("r_por_sup_canal")
minorista_sup_suplidor = P("r_minorista_sup_suplidor")
colombina_sup = P("r_colombina_sup")
r50 = P("r_resumen_top50_sup")
no_vis_sup = P("r_visitas_sup")
no_vis_detalle = P("r_no_visitados_detalle")
of_sup = P("r_oferta_por_sup")

ORDEN_SUP = ["Ramon E. Pimentel","Eliezer Martinez","Narciso Pina Bello","Cristiano Perez","Disponible Sur Corto"]
EXCL_INST = lambda df: df[df.SupervisorGrp != "Rudy Mercedes (Institucional)"]

prs = nueva_presentacion()
BLANK = prs.slide_layouts[6]

# ============ SLIDE 1: PORTADA ============
sl = prs.slides.add_slide(BLANK)
rect(sl, 0, 0, 13.333, 7.5, fill=AZUL)
rect(sl, 0, 4.55, 13.333, 0.09, fill=VERDE)
if LOGO.exists():
    sl.shapes.add_picture(str(LOGO), Inches(0.6), Inches(0.55), height=Inches(0.62))
tb(sl, 0.6, 2.55, 12.1, 1.1, "Reuni\u00f3n Semanal de Supervisores", size=38, bold=True, color=BLANCO)
tb(sl, 0.6, 3.45, 12.1, 0.6, "Presupuesto, capacidad de compra, visitas y plan de acci\u00f3n \u2013 Septiembre 2026",
   size=16, color=RGBColor(0xD8,0xE0,0xF5))
tb(sl, 0.6, 4.85, 12.1, 0.4, "Corte al 19 de septiembre 2026 (d\u00eda 19 de 30)  \u00b7  Fuente: MovilBusiness5STD",
   size=11.5, color=RGBColor(0xC7,0xD1,0xEE))
tb(sl, 0.6, 6.85, 12.1, 0.3, "AM Comercial \u00b7 Confidencial", size=9, color=RGBColor(0xAE,0xBA,0xE0))

# ============ SLIDE 2: Parte 1 - KPIs + presupuesto por supervisor ============
sl = prs.slides.add_slide(BLANK)
head(sl, "1. Logro de presupuesto vs. venta al d\u00eda", "Corte al d\u00eda 19 de septiembre. Presupuesto al d\u00eda = prorrateado seg\u00fan d\u00edas h\u00e1biles transcurridos del mes.", 2)
tot = por_sup[por_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"]
kpi(sl, 0.5, 1.35, 2.95, money(tot.PresupuestoAlDia.sum()/1e6)+"MM", "Presupuesto al d\u00eda (RD$, todo el equipo)", AZUL)
kpi(sl, 3.62, 1.35, 2.95, money(tot.Ejecutado.sum()/1e6)+"MM", "Ejecutado a la fecha (RD$)", VERDE)
kpi(sl, 6.74, 1.35, 2.95, pct(tot.Ejecutado.sum()/tot.PresupuestoAlDia.sum()*100), "Alcance general al d\u00eda", AMBAR)
kpi(sl, 9.86, 1.35, 2.99, money(tot.Proyectado.sum()/1e6)+"MM  ("+pct(tot.Proyectado.sum()/tot.PresupuestoMes.sum()*100)+")", "Proyecci\u00f3n de cierre vs. presupuesto del mes", ROJO)
sl.shapes.add_picture(str(IMG/"presu_supervisor.png"), Inches(0.5), Inches(2.55), width=Inches(7.7))
sl.shapes.add_picture(str(IMG/"presu_canal.png"), Inches(8.35), Inches(2.55), width=Inches(4.5))
lectura(sl, "Minorista alcanza 93% al d\u00eda; Mayorista (equipo de Cristiano) est\u00e1 en 53% \u2014 la brecha m\u00e1s grande de la compa\u00f1\u00eda.")

# ============ SLIDE 3: Foco Minorista + Colombina ============
sl = prs.slides.add_slide(BLANK)
head(sl, "1. Foco: Canal Minorista y suplidor Colombina", "Colombina fue el foco de gesti\u00f3n hasta hoy. Alcance al d\u00eda por supervisor dentro del canal Minorista.", 3)
rows = [["Supervisor","Presup. al d\u00eda","Ejecutado","Diferencia","% Alcance"]]
cc = {}
i=1
cs = colombina_sup[colombina_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"].set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
for _,r in cs.iterrows():
    rows.append([r.SupervisorGrp, money(r.PresupuestoAlDia), money(r.Ejecutado), money(r.Diferencia), pct(r.PctAlcance)])
    if r.PctAlcance<80: cc[(i,4)]=RCLARO
    elif r.PctAlcance<95: cc[(i,4)]=ACLARO
    else: cc[(i,4)]=GCLARO
    i+=1
tot_col = colombina_sup[colombina_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"]
rows.append(["TOTAL COLOMBINA", money(tot_col.PresupuestoAlDia.sum()), money(tot_col.Ejecutado.sum()), money(tot_col.Diferencia.sum()), pct(tot_col.Ejecutado.sum()/tot_col.PresupuestoAlDia.sum()*100)])
tb(sl, 0.5, 1.42, 6.0, 0.3, "COLOMBINA por supervisor (RD$)", size=11.5, bold=True, color=AZUL)
tabla(sl, 0.5, 1.75, 6.0, rows, [2.3,1.3,1.3,1.2,1.0], aligns=['l','r','r','r','c'], rowh=0.34, headh=0.5, fs=9, hfs=8.5, cell_colors=cc, bold_rows=(len(rows)-1,), fill_rows={len(rows)-1:RGBColor(0xE9,0xED,0xF7)})

rows2 = [["Suplidor (Minorista)","Presup. al d\u00eda","Ejecutado","% Alcance"]]
top_sup_min = minorista_sup_suplidor.groupby("Suplidor").agg(PresupuestoAlDia=("PresupuestoAlDia","sum"), Ejecutado=("Ejecutado","sum")).reset_index()
top_sup_min["Pct"] = top_sup_min.Ejecutado/top_sup_min.PresupuestoAlDia*100
top_sup_min = top_sup_min.sort_values("PresupuestoAlDia", ascending=False).head(8)
cc2={}
i=1
for _,r in top_sup_min.iterrows():
    rows2.append([r.Suplidor, money(r.PresupuestoAlDia), money(r.Ejecutado), pct(r.Pct)])
    cc2[(i,3)] = RCLARO if r.Pct<80 else (ACLARO if r.Pct<95 else GCLARO); i+=1
tb(sl, 6.85, 1.42, 6.0, 0.3, "Top suplidores dentro de Minorista (RD$)", size=11.5, bold=True, color=AZUL)
tabla(sl, 6.85, 1.75, 6.0, rows2, [2.6,1.3,1.3,1.0], aligns=['l','r','r','c'], rowh=0.315, headh=0.44, fs=8.7, hfs=8.3, cell_colors=cc2)
lectura(sl, "Colombina cierra el d\u00eda en 79% de alcance general; el hueco mayor est\u00e1 en Narciso Pi\u00f1a (65%) y Cristiano (73%).", top=6.15)

# ============ SLIDE 4: Resumen parte 1 ============
sl = prs.slides.add_slide(BLANK)
head(sl, "1. Qu\u00e9 debemos atender esta semana", "S\u00edntesis del logro de presupuesto vs. venta al d\u00eda.", 4)
peor = por_sup_canal[(por_sup_canal.SupervisorGrp=="Cristiano Perez")&(por_sup_canal.Canal=="Mayorista")].iloc[0]
puntos = [
 ("1","Mayorista (Cristiano P\u00e9rez) es la brecha m\u00e1s grande",
  f"53% de alcance al d\u00eda; faltan RD$ {money(-peor.Diferencia)} para llegar al ritmo del presupuesto. Es el \u00fanico equipo por debajo del 90%.", ROJO),
 ("2","Colombina no llega al ritmo en 4 de 5 equipos",
  "Solo Eliezer Mart\u00ednez (93%) y Disponible Sur Corto (96%) van al d\u00eda; Narciso (65%) y Cristiano (73%) son la prioridad de gesti\u00f3n.", AMBAR),
 ("3","Los equipos Minorista van bien en conjunto",
  "Ram\u00f3n E. Pimentel, Eliezer Mart\u00ednez y Narciso Pi\u00f1a promedian 94% de alcance al d\u00eda \u2014 mantener el ritmo, no aflojar la \u00faltima semana.", VERDE),
 ("4","Continental y Molinos Modernos son los de mayor peso",
  "Juntos representan RD$ 44.6MM de presupuesto del mes (50% del total); su ejecuci\u00f3n (89% y 93%) marca el resultado general.", AZUL),
]
y=1.5
for num,tit,txt,col in puntos:
    rect(sl, 0.5, y, 12.35, 1.15, fill=FONDO)
    rect(sl, 0.5, y, 0.055, 1.15, fill=col)
    rect(sl, 0.68, y+0.16, 0.4, 0.4, fill=col, radius=0.5)
    tb(sl, 0.68, y+0.24, 0.4, 0.28, num, size=12, bold=True, color=BLANCO, align=PP_ALIGN.CENTER)
    tb(sl, 1.28, y+0.12, 11.4, 0.32, tit, size=12.5, bold=True, color=col)
    tb(sl, 1.28, y+0.48, 11.4, 0.6, txt, size=10, color=GRIS, space=0)
    y += 1.28
lectura(sl, "Prioridad de la semana: acompa\u00f1amiento de Cristiano en Mayorista y refuerzo de Colombina con Narciso y con el propio Cristiano.", top=6.62)

# ============ SLIDE 5: Parte 2 - Top 50 clientes ============
sl = prs.slides.add_slide(BLANK)
head(sl, "2. Clientes de alta capacidad de compra vs. lo comprado en septiembre", "Top 50 clientes de mayor promedio mensual (marzo-agosto 2026) por supervisor, comparado contra lo comprado en septiembre a la fecha.", 5)
sl.shapes.add_picture(str(IMG/"top50_supervisor.png"), Inches(0.5), Inches(1.42), width=Inches(7.7))
rows = [["Supervisor","Clientes\nTop 50","Promedio\nmensual","Esperado\na la fecha","Comprado\nSept.","% Cumpl."]]
cc={}
i=1
rr = r50[r50.SupervisorGrp!="Rudy Mercedes (Institucional)"].set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
for _,r in rr.iterrows():
    rows.append([r.SupervisorGrp, int(r.Clientes), money(r.PromedioMensual), money(r.EsperadoAlDia), money(r.CompradoSep), pct(r.PctCumpl)])
    cc[(i,5)] = RCLARO if r.PctCumpl<80 else (ACLARO if r.PctCumpl<100 else GCLARO); i+=1
tb(sl, 8.35, 1.42, 4.5, 0.3, "Resumen Top 50 por supervisor (RD$)", size=10.5, bold=True, color=AZUL)
tabla(sl, 8.35, 1.75, 4.5, rows, [1.7,0.8,1.1,1.1,1.0,0.9], aligns=['l','c','r','r','r','c'], rowh=0.62, headh=0.6, fs=8, hfs=7.3, cell_colors=cc)
lectura(sl, "La cartera Top 50 de Cristiano tiene el mayor potencial (RD$ 8.4MM/mes) y el peor cumplimiento (21%): ah\u00ed est\u00e1 la oportunidad m\u00e1s grande y m\u00e1s urgente.", top=6.15, color=RCLARO, barra=ROJO)

# ============ SLIDE 6: Sub-analisis suplidores ============
sl = prs.slides.add_slide(BLANK)
head(sl, "2. Oportunidad por suplidor: Continental, Molinos, Colombina y Brinsa", "Universo de clientes con historial en los \u00faltimos 6 meses. Esperado a la fecha = promedio mensual x 19/30 d\u00edas.", 6)
rows=[["Suplidor","Clientes con\nhistorial","Promedio\nmensual","Esperado\na la fecha","Comprado\nSept.","Brecha","% Cumpl."]]
cc={}
i=1
for nombre,archivo in [("Continental de Negocios","continental"),("Molinos Modernos","molinos"),("Colombina","colombina"),("Brinsa Dominicana (Refisal)","brinsa")]:
    m = pd.read_pickle(SC/f"r_cap_{archivo}.pkl")
    prom=m.PromedioMensual.sum(); esp=m.EsperadoAlDia.sum(); comp=m.CompradoSep.sum(); brec=m.Brecha.sum()
    pc = comp/esp*100
    rows.append([nombre, len(m), money(prom), money(esp), money(comp), money(brec), pct(pc)])
    cc[(i,6)] = RCLARO if pc<80 else (ACLARO if pc<100 else GCLARO); i+=1
tabla(sl, 0.5, 1.55, 12.35, rows, [2.6,1.2,1.4,1.4,1.3,1.3,1.0], aligns=['l','c','r','r','r','r','c'], rowh=0.42, headh=0.55, fs=10, hfs=9)
tb(sl, 0.5, 3.85, 12.35, 1.6,
   "Lectura por suplidor:\n"
   "\u2022 Continental y Molinos concentran el mayor volumen de clientes activos (18.3k y 5.9k) y ya van pr\u00e1cticamente al ritmo esperado.\n"
   "\u2022 Colombina tiene 16.5k clientes con historial pero una brecha de RD$ 752 mil a la fecha \u2014 confirma la se\u00f1al de la parte 1.\n"
   "\u2022 Brinsa (Refisal) tiene una brecha menor (RD$ 254 mil) pero con 15.6k clientes de historial: campo f\u00e9rtil para venta cruzada.",
   size=11, color=GRIS, space=6)
lectura(sl, "La oportunidad no es de cobertura (miles de clientes ya compran estas marcas), es de frecuencia y monto por visita en la recta final del mes.")

# ============ SLIDE 7: No visitados ============
sl = prs.slides.add_slide(BLANK)
head(sl, "3. Clientes no visitados en lo que va de septiembre", "Cartera activa por vendedor vs. clientes con al menos una visita registrada del 1 al 19 de septiembre (Visitas + VisitasHistoricos).", 7)
sl.shapes.add_picture(str(IMG/"no_visitados.png"), Inches(0.5), Inches(1.42), width=Inches(6.1))
rows=[["Supervisor","Cartera","Visitados","No\nvisitados","% no\nvisitado"]]
cc={}
i=1
nv = no_vis_sup[no_vis_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"].set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
for _,r in nv.iterrows():
    rows.append([r.SupervisorGrp, int(r.Cartera), int(r.Visitados), int(r.NoVisitados), pct(r.PctNoVisitado)])
    cc[(i,4)] = RCLARO if r.PctNoVisitado>20 else (ACLARO if r.PctNoVisitado>10 else GCLARO); i+=1
tot_nv = no_vis_sup[no_vis_sup.SupervisorGrp!="Rudy Mercedes (Institucional)"]
rows.append(["TOTAL EQUIPO", int(tot_nv.Cartera.sum()), int(tot_nv.Visitados.sum()), int(tot_nv.NoVisitados.sum()), pct(tot_nv.NoVisitados.sum()/tot_nv.Cartera.sum()*100)])
tb(sl, 6.85, 1.42, 6.0, 0.3, "No visitados por supervisor", size=11, bold=True, color=AZUL)
tabla(sl, 6.85, 1.75, 6.0, rows, [2.3,1.0,1.1,1.1,1.0], aligns=['l','c','c','c','c'], rowh=0.4, headh=0.5, fs=9.5, hfs=8.7, cell_colors=cc, bold_rows=(len(rows)-1,), fill_rows={len(rows)-1:RGBColor(0xE9,0xED,0xF7)})
lectura(sl, f"228 de los 884 clientes de Cristiano (26%) no han sido visitados en 19 d\u00edas de mes \u2014 m\u00e1s del doble que cualquier otro equipo.", top=6.15, color=RCLARO, barra=ROJO)

# ============ SLIDE 8: Top clientes de valor sin visitar ============
sl = prs.slides.add_slide(BLANK)
head(sl, "3. Los clientes de mayor valor que no hemos visitado", "Top 15 clientes no visitados en septiembre con mayor promedio de compra mensual (marzo-agosto 2026).", 8)
rows=[["Supervisor","Vendedor","Cliente","Tipo","Promedio\nmensual RD$"]]
top15 = no_vis_detalle.head(15)
for _,r in top15.iterrows():
    rows.append([r.SupervisorGrp.replace(" Perez"," P\u00e9rez"), r.RepNombre, r.CliNombre[:30], r.TipoNegocio, money(r.PromedioMensual)])
tabla(sl, 0.5, 1.5, 12.35, rows, [2.0,2.0,3.4,1.7,1.6], aligns=['l','l','l','l','r'], rowh=0.315, headh=0.46, fs=9, hfs=8.7)
tb(sl, 0.5, 6.05, 12.35, 0.3, f"Potencial mensual total en riesgo por falta de visita (toda la cartera no visitada): RD$ {money(no_vis_detalle.PromedioMensual.sum())}", size=10, bold=True, color=ROJO)
lectura(sl, "14 de los 15 clientes de mayor valor sin visitar son de la cartera de Cristiano P\u00e9rez (Jhonny Rosario y Wascar Alvarez), sumando RD$ 941 mil de promedio mensual. Ah\u00ed est\u00e1 la visita especial de esta semana.", top=6.55, color=RCLARO, barra=ROJO)

# ============ SLIDE 9: Plan de accion - clientes de volumen ============
sl = prs.slides.add_slide(BLANK)
head(sl, "4. Plan de acci\u00f3n: visita especial a clientes de volumen sin atender", "Cruce de las partes 2 y 3: clientes de alta capacidad de compra con brecha o sin visita. Requiere visita del supervisor junto al vendedor.", 9)
rect(sl, 0.5, 1.45, 12.35, 1.75, fill=FONDO)
rect(sl, 0.5, 1.45, 0.06, 1.75, fill=ROJO)
tb(sl, 0.75, 1.62, 11.8, 1.5,
   "Prioridad #1 \u2013 Cristiano P\u00e9rez (Mayorista): 14 clientes de alto historial sin visitar en 19 d\u00edas "
   "(Supermercado Manuel Supmara, Farmacia Kasser, Nolimak Comercial, Surtidora La Bendici\u00f3n, Supermercado Ballena, entre otros) "
   "suman RD$ 941 mil de promedio mensual. Meta: visita conjunta supervisor-vendedor a los 14 antes del viernes.\n\n"
   "Prioridad #2 \u2013 Todos los supervisores: de los Top 50 clientes de cada equipo, activar a los que est\u00e1n por debajo del 60% de cumplimiento "
   "esperado a la fecha, empezando por Colombina, Continental y Brinsa (mayor cartera con historial, ver l\u00e1mina 6).",
   size=11.5, color=GRIS, space=6)
rows=[["Meta de la semana","Indicador","L\u00ednea base"]]
rows.append(["Visitar a los 14 clientes Top de Cristiano sin atender", "14 de 14 visitados", "0 visitados a la fecha"])
rows.append(["Cerrar brecha de Colombina en Narciso y Cristiano", "Alcance \u2265 90% al d\u00eda", "65% y 73%"])
rows.append(["Recuperar cumplimiento Top 50 en Cristiano", "\u2265 60% de lo esperado", "21% actual"])
tabla(sl, 0.5, 3.5, 12.35, rows, [5.5,3.5,3.35], aligns=['l','c','c'], rowh=0.42, headh=0.5, fs=10.5, hfs=9.5)
lectura(sl, "Estas metas se revisan en la reuni\u00f3n de la pr\u00f3xima semana con evidencia de visita (foto/registro) y venta generada.")

# ============ SLIDE 10: Plan de accion - oferta ============
sl = prs.slides.add_slide(BLANK)
head(sl, "4. Plan de acci\u00f3n: activaci\u00f3n de oferta \"3 Bolones + 1 Tipitin\"", "Oferta OfeID 3911 (3 BBB + 1 Tipitin), vigente del 3 al 30 de septiembre 2026. Clientes distintos activados del 1 al 19 de septiembre.", 10)
sl.shapes.add_picture(str(IMG/"oferta_activados.png"), Inches(0.5), Inches(1.45), width=Inches(6.1))
tot_of = int(of_sup.ClientesActivados.sum())
rect(sl, 6.85, 1.45, 6.0, 2.0, fill=FONDO)
rect(sl, 6.85, 1.45, 0.06, 2.0, fill=VERDE)
tb(sl, 7.05, 1.62, 5.6, 1.7,
   f"{tot_of:,} clientes distintos activados con la oferta en toda la compa\u00f1\u00eda.\n\n"
   "Cristiano P\u00e9rez solo ha activado 18 clientes \u2014 muy por debajo de cualquier otro equipo (537 a 1,027).\n\n"
   "Meta propuesta: activar 150 clientes adicionales con esta oferta en Mayorista (Cristiano) antes del cierre de mes, "
   "priorizando los clientes de volumen de la l\u00e1mina anterior.", size=11, color=GRIS, space=6)
lectura(sl, "La oferta es la palanca m\u00e1s r\u00e1pida para reactivar clientes de Cristiano: no requiere negociaci\u00f3n, solo visita y pedido.", top=6.55, color=GCLARO, barra=VERDE)

# ============ SLIDE 11: Cierre ============
sl = prs.slides.add_slide(BLANK)
head(sl, "5. Cierre y compromisos del equipo", "Resumen de la reuni\u00f3n y compromisos para la semana del 22 al 26 de septiembre.", 11)
rows=[["#","Compromiso","Responsable","Meta"]]
rows.append(["1","Visita conjunta a los 11 clientes Top de Cristiano sin atender","Cristiano P\u00e9rez + supervisor comercial","11/11 visitados esta semana"])
rows.append(["2","Reforzar Colombina en las rutas de menor alcance","Narciso Pi\u00f1a, Cristiano P\u00e9rez","Alcance \u2265 90% al d\u00eda"])
rows.append(["3","Activar 150 clientes con oferta 3 Bolones+1 Tipitin en Mayorista","Cristiano P\u00e9rez","+150 clientes activados"])
rows.append(["4","Mantener el ritmo actual (94% de alcance) en Minorista","Ram\u00f3n E. Pimentel, Eliezer Mart\u00ednez","No bajar de 90% al cierre"])
rows.append(["5","Reducir clientes no visitados por debajo del 10% en todos los equipos","Todos los supervisores","< 10% no visitado"])
tabla(sl, 0.5, 1.55, 12.35, rows, [0.5,5.6,3.1,3.15], aligns=['c','l','l','l'], rowh=0.62, headh=0.55, fs=10, hfs=9.5)
lectura(sl, "Pr\u00f3xima reuni\u00f3n: seguimiento de estos 5 compromisos con evidencia y resultado en RD$ de cada acci\u00f3n.", top=5.9, color=VCLARO, barra=VERDE)

prs.save(str(DEST))
print("OK ->", DEST)
