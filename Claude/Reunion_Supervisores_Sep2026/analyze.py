# -*- coding: utf-8 -*-
import pandas as pd, numpy as np
from pathlib import Path
OUT = Path(__file__).parent

presu = pd.read_pickle(OUT/"presu.pkl")
ventas6m = pd.read_pickle(OUT/"ventas6m.pkl")
ventasSep = pd.read_pickle(OUT/"ventasSep.pkl")
cartera = pd.read_pickle(OUT/"cartera.pkl")
visitas = pd.read_pickle(OUT/"visitas.pkl")
rutaplan = pd.read_pickle(OUT/"rutaplan.pkl")
ofertas_focus = pd.read_pickle(OUT/"ofertas_focus.pkl")

pd.set_option("display.width", 200)
pd.set_option("display.max_columns", 20)

# ---- Mapeo de supervisores (codigo doble -> nombre unico) ----
SUP_MAP = {
    "S12": "Cristiano Perez", "S13": "Cristiano Perez",
    "S1": "Ramon E. Pimentel", "S4": "Ramon E. Pimentel",
    "S2": "Eliezer Martinez", "S5": "Eliezer Martinez",
    "S9": "Narciso Pina Bello",
    "S8": "Disponible Sur Corto",
    "S111": "Rudy Mercedes (Institucional)",
}
SUPERVISORES_REUNION = ["Cristiano Perez", "Ramon E. Pimentel", "Eliezer Martinez", "Narciso Pina Bello"]

presu["SupervisorGrp"] = presu.RepSupervisor.map(SUP_MAP).fillna(presu.Supervisor)
ventas6m["SupervisorGrp"] = ventas6m.CodigoSupervisor.map(SUP_MAP).fillna(ventas6m.SupervisorVenta)
ventasSep["SupervisorGrp"] = ventasSep.CodigoSupervisor.map(SUP_MAP).fillna(ventasSep.SupervisorVenta)
cartera["SupervisorGrp"] = cartera.RepSupervisor.map(SUP_MAP).fillna("Sin Supervisor")

DIAS_MES, DIAS_SEP = 30, 19
FACTOR_ALDIA = DIAS_SEP / DIAS_MES

print("="*80, "\nPARTE 1: PRESUPUESTO VS VENTA AL DIA\n", "="*80)

def resumen_presu(df, cols):
    g = df.groupby(cols, dropna=False).agg(
        PresupuestoMes=("PrePresupuestoMonto","sum"),
        PresupuestoAlDia=("PresupuestoMontoAlDia","sum"),
        Ejecutado=("PreEjecutadoMonto","sum"),
        Proyectado=("Proyectado","sum"),
    ).reset_index()
    g["Diferencia"] = g.Ejecutado - g.PresupuestoAlDia
    g["PctAlcance"] = np.where(g.PresupuestoAlDia>0, g.Ejecutado/g.PresupuestoAlDia*100, np.nan)
    g["PctProyVsMes"] = np.where(g.PresupuestoMes>0, g.Proyectado/g.PresupuestoMes*100, np.nan)
    return g.sort_values("PresupuestoMes", ascending=False)

por_supervisor = resumen_presu(presu, ["SupervisorGrp"])
print("\n-- Por supervisor --\n", por_supervisor.to_string(index=False))

por_canal = resumen_presu(presu, ["Canal"])
print("\n-- Por canal --\n", por_canal.to_string(index=False))

por_sup_canal = resumen_presu(presu, ["SupervisorGrp","Canal"])
print("\n-- Por supervisor x canal --\n", por_sup_canal.to_string(index=False))

por_suplidor = resumen_presu(presu, ["Suplidor"])
print("\n-- Por suplidor (top 15) --\n", por_suplidor.head(15).to_string(index=False))

minorista = presu[presu.Canal=="Minorista"]
minorista_sup_suplidor = resumen_presu(minorista, ["SupervisorGrp","Suplidor"])
print("\n-- MINORISTA por supervisor x suplidor (top 20) --\n", minorista_sup_suplidor.head(20).to_string(index=False))

colombina = presu[presu.Suplidor.str.contains("COLOMBINA", case=False, na=False)]
colombina_sup = resumen_presu(colombina, ["SupervisorGrp"])
print("\n-- COLOMBINA por supervisor --\n", colombina_sup.to_string(index=False))
colombina_vend = resumen_presu(colombina, ["SupervisorGrp","RepCodigo","RepNombre"])
print("\n-- COLOMBINA por vendedor --\n", colombina_vend.to_string(index=False))

por_vendedor = resumen_presu(presu, ["SupervisorGrp","RepCodigo","RepNombre","Canal"])
por_vendedor.to_pickle(OUT/"r_por_vendedor.pkl")
por_supervisor.to_pickle(OUT/"r_por_supervisor.pkl")
por_canal.to_pickle(OUT/"r_por_canal.pkl")
por_sup_canal.to_pickle(OUT/"r_por_sup_canal.pkl")
por_suplidor.to_pickle(OUT/"r_por_suplidor.pkl")
minorista_sup_suplidor.to_pickle(OUT/"r_minorista_sup_suplidor.pkl")
colombina_sup.to_pickle(OUT/"r_colombina_sup.pkl")
colombina_vend.to_pickle(OUT/"r_colombina_vend.pkl")

print("\n\n", "="*80, "\nPARTE 2: CAPACIDAD DE COMPRA (PROMEDIO 6M) VS SEPTIEMBRE\n", "="*80)

prom = ventas6m.groupby(["SupervisorGrp","CodigoVendedor","NombreVendedor","ClienteCodigo","ClienteNombre"]).agg(
    ValorTotal6M=("ValorBruto","sum")).reset_index()
prom["PromedioMensual"] = prom.ValorTotal6M / 6.0
sep = ventasSep.groupby(["ClienteCodigo"]).agg(CompradoSep=("ValorBruto","sum")).reset_index()
cap = prom.merge(sep, on="ClienteCodigo", how="left")
cap["CompradoSep"] = cap.CompradoSep.fillna(0)
cap["EsperadoAlDia"] = cap.PromedioMensual * FACTOR_ALDIA
cap["Brecha"] = cap.EsperadoAlDia - cap.CompradoSep
cap["PctCumpl"] = np.where(cap.EsperadoAlDia>0, cap.CompradoSep/cap.EsperadoAlDia*100, np.nan)
cap = cap.sort_values("PromedioMensual", ascending=False)
cap.to_pickle(OUT/"r_capacidad_clientes.pkl")

top50_general = cap.head(50)
print("\n-- TOP 50 clientes de mayor capacidad (general), resumen --")
print("Suma promedio mensual top50:", top50_general.PromedioMensual.sum())
print("Suma esperado a la fecha top50:", top50_general.EsperadoAlDia.sum())
print("Suma comprado sept top50:", top50_general.CompradoSep.sum())
print("Brecha top50:", top50_general.Brecha.sum())

cap_sorted = cap.sort_values(["SupervisorGrp","PromedioMensual"], ascending=[True,False])
top50_por_sup = cap_sorted.groupby("SupervisorGrp", group_keys=False).head(50)
resumen_top50_sup = top50_por_sup.groupby("SupervisorGrp").agg(
    Clientes=("ClienteCodigo","count"), PromedioMensual=("PromedioMensual","sum"),
    EsperadoAlDia=("EsperadoAlDia","sum"), CompradoSep=("CompradoSep","sum"), Brecha=("Brecha","sum")
).reset_index()
resumen_top50_sup["PctCumpl"] = resumen_top50_sup.CompradoSep/resumen_top50_sup.EsperadoAlDia*100
print("\n-- TOP50 por supervisor (agregado) --\n", resumen_top50_sup.to_string(index=False))
top50_por_sup.to_pickle(OUT/"r_top50_por_sup.pkl")
resumen_top50_sup.to_pickle(OUT/"r_resumen_top50_sup.pkl")

# Sub-analisis suplidores foco
for sup_name, filtro in [("Continental de Negocios","Continental"), ("Molinos Modernos","MOLINOS MODERNOS"),
                          ("Colombina","COLOMBINA"), ("Brinsa Dominicana","Brinsa")]:
    v6 = ventas6m[ventas6m.SUPLIDOR.str.contains(filtro, case=False, na=False)]
    vs = ventasSep[ventasSep.SUPLIDOR.str.contains(filtro, case=False, na=False)]
    p = v6.groupby(["ClienteCodigo","ClienteNombre","SupervisorGrp"]).agg(ValorTotal6M=("ValorBruto","sum")).reset_index()
    p["PromedioMensual"] = p.ValorTotal6M/6.0
    s = vs.groupby("ClienteCodigo").agg(CompradoSep=("ValorBruto","sum")).reset_index()
    m = p.merge(s, on="ClienteCodigo", how="left"); m.CompradoSep = m.CompradoSep.fillna(0)
    m["EsperadoAlDia"] = m.PromedioMensual*FACTOR_ALDIA
    m["Brecha"] = m.EsperadoAlDia - m.CompradoSep
    m = m.sort_values("PromedioMensual", ascending=False)
    m.to_pickle(OUT/f"r_cap_{sup_name.split()[0].lower()}.pkl")
    print(f"\n-- {sup_name}: clientes con historial {len(m)}, promedio mensual total {m.PromedioMensual.sum():,.0f}, "
          f"esperado a la fecha {m.EsperadoAlDia.sum():,.0f}, comprado sept {m.CompradoSep.sum():,.0f}, brecha {m.Brecha.sum():,.0f}")

print("\n\n", "="*80, "\nPARTE 3: CLIENTES NO VISITADOS EN SEPTIEMBRE\n", "="*80)

visitados = set(visitas.CliID.unique())
cart_activa = cartera[cartera.SupervisorGrp != "Sin Supervisor"].copy()
cart_activa["Visitado"] = cart_activa.CliID.isin(visitados)

resumen_visitas_vend = cart_activa.groupby(["SupervisorGrp","RepCodigo","RepNombre"]).agg(
    Cartera=("CliID","count"), Visitados=("Visitado","sum")
).reset_index()
resumen_visitas_vend["NoVisitados"] = resumen_visitas_vend.Cartera - resumen_visitas_vend.Visitados
resumen_visitas_vend["PctNoVisitado"] = resumen_visitas_vend.NoVisitados/resumen_visitas_vend.Cartera*100
resumen_visitas_vend = resumen_visitas_vend.sort_values("PctNoVisitado", ascending=False)
resumen_visitas_vend.to_pickle(OUT/"r_visitas_vend.pkl")
print("\n-- No visitados por vendedor (top 20 peor %) --\n", resumen_visitas_vend.head(20).to_string(index=False))

resumen_visitas_sup = cart_activa.groupby("SupervisorGrp").agg(
    Cartera=("CliID","count"), Visitados=("Visitado","sum")).reset_index()
resumen_visitas_sup["NoVisitados"] = resumen_visitas_sup.Cartera - resumen_visitas_sup.Visitados
resumen_visitas_sup["PctNoVisitado"] = resumen_visitas_sup.NoVisitados/resumen_visitas_sup.Cartera*100
resumen_visitas_sup.to_pickle(OUT/"r_visitas_sup.pkl")
print("\n-- No visitados por supervisor --\n", resumen_visitas_sup.to_string(index=False))

no_visitados_detalle = cart_activa[~cart_activa.Visitado][["SupervisorGrp","RepCodigo","RepNombre","CliID","CliCodigo","CliNombre","TipoNegocio"]]
prom_cli = ventas6m.groupby("ClienteCodigo").agg(PromedioMensual=("ValorBruto","sum")).reset_index()
prom_cli["PromedioMensual"] = prom_cli.PromedioMensual/6.0
no_visitados_detalle = no_visitados_detalle.merge(prom_cli, left_on="CliID", right_on="ClienteCodigo", how="left")
no_visitados_detalle["PromedioMensual"] = no_visitados_detalle.PromedioMensual.fillna(0)
no_visitados_detalle = no_visitados_detalle.sort_values("PromedioMensual", ascending=False)
no_visitados_detalle.to_pickle(OUT/"r_no_visitados_detalle.pkl")
print("\n-- Valor historico en riesgo por no-visita (potencial mensual) --")
print("Total potencial mensual de clientes no visitados:", no_visitados_detalle.PromedioMensual.sum())
print("\n-- Top 15 clientes no visitados con mayor historial --\n",
      no_visitados_detalle.head(15)[["SupervisorGrp","RepNombre","CliNombre","TipoNegocio","PromedioMensual"]].to_string(index=False))

print("\n\n", "="*80, "\nPARTE 4: OFERTA FOCO (3 BBB + 1 TIPITIN, OfeID 3911)\n", "="*80)
print(ofertas_focus.to_string(index=False))
of_sep = ventasSep[ventasSep.ofeid.isin([3909,3911])]
of_por_sup = of_sep.groupby("SupervisorGrp").agg(ClientesActivados=("ClienteCodigo","nunique")).reset_index()
of_por_sup.to_pickle(OUT/"r_oferta_por_sup.pkl")
print("\n-- Clientes activados con la oferta foco (Sept a la fecha) por supervisor --\n", of_por_sup.to_string(index=False))
print("Total clientes activados:", of_sep.ClienteCodigo.nunique())

print("\nANALYZE OK")
