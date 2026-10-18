# -*- coding: utf-8 -*-
"""Arma el bundle de datos por supervisor (grano vendedor) para informe y PPT individual."""
import pandas as pd, numpy as np
from pathlib import Path
SC = Path(__file__).parent

FACTOR_ALDIA = 19/30.0

SUPERVISORES = {
    "cristiano": {"nombre": "Cristiano Pérez", "codigos": ["S12","S13"], "detalle": "Mini Market (S12) + Mayorista/Tradicional (S13)"},
    "ramon": {"nombre": "Ramón E. Pimentel", "codigos": ["S1","S4"], "detalle": "SDE (S1) + SDN (S4)"},
    "eliezer": {"nombre": "Eliezer Martínez", "codigos": ["S2","S5"], "detalle": "SDO (S2) + DN (S5)"},
    "narciso": {"nombre": "Narciso Piña Bello", "codigos": ["S9"], "detalle": "Sur Largo (S9)"},
    "disponible": {"nombre": "Equipo Sur Corto", "codigos": ["S8"], "detalle": "Sur Corto (S8) – supervisor por asignar"},
}

def bundle(key):
    cfg = SUPERVISORES[key]
    cods = cfg["codigos"]

    presu = pd.read_pickle(SC/"presu.pkl")
    presu_s = presu[presu.RepSupervisor.isin(cods)]
    por_vend = presu_s.groupby(["RepCodigo","RepNombre","Canal"]).agg(
        PresupuestoMes=("PrePresupuestoMonto","sum"), PresupuestoAlDia=("PresupuestoMontoAlDia","sum"),
        Ejecutado=("PreEjecutadoMonto","sum"), Proyectado=("Proyectado","sum")).reset_index()
    por_vend["Diferencia"] = por_vend.Ejecutado - por_vend.PresupuestoAlDia
    por_vend["PctAlcance"] = np.where(por_vend.PresupuestoAlDia>0, por_vend.Ejecutado/por_vend.PresupuestoAlDia*100, np.nan)
    por_vend = por_vend.sort_values("PresupuestoMes", ascending=False)

    por_suplidor = presu_s.groupby("Suplidor").agg(
        PresupuestoMes=("PrePresupuestoMonto","sum"), PresupuestoAlDia=("PresupuestoMontoAlDia","sum"),
        Ejecutado=("PreEjecutadoMonto","sum"), Proyectado=("Proyectado","sum")).reset_index()
    por_suplidor["Diferencia"] = por_suplidor.Ejecutado - por_suplidor.PresupuestoAlDia
    por_suplidor["PctAlcance"] = np.where(por_suplidor.PresupuestoAlDia>0, por_suplidor.Ejecutado/por_suplidor.PresupuestoAlDia*100, np.nan)
    por_suplidor = por_suplidor.sort_values("PresupuestoMes", ascending=False)

    tot = por_vend[["PresupuestoMes","PresupuestoAlDia","Ejecutado","Proyectado","Diferencia"]].sum()
    tot["PctAlcance"] = tot.Ejecutado/tot.PresupuestoAlDia*100 if tot.PresupuestoAlDia>0 else np.nan
    tot["PctProyVsMes"] = tot.Proyectado/tot.PresupuestoMes*100 if tot.PresupuestoMes>0 else np.nan

    # capacidad de compra
    ventas6m = pd.read_pickle(SC/"ventas6m.pkl")
    ventasSep = pd.read_pickle(SC/"ventasSep.pkl")
    v6 = ventas6m[ventas6m.CodigoSupervisor.isin(cods)]
    vs = ventasSep[ventasSep.CodigoSupervisor.isin(cods)]
    prom = v6.groupby(["CodigoVendedor","NombreVendedor","ClienteCodigo","ClienteNombre"]).agg(ValorTotal6M=("ValorBruto","sum")).reset_index()
    prom["PromedioMensual"] = prom.ValorTotal6M/6.0
    sep = vs.groupby("ClienteCodigo").agg(CompradoSep=("ValorBruto","sum")).reset_index()
    cap = prom.merge(sep, on="ClienteCodigo", how="left"); cap["CompradoSep"] = cap.CompradoSep.fillna(0)
    cap["EsperadoAlDia"] = cap.PromedioMensual*FACTOR_ALDIA
    cap["Brecha"] = cap.EsperadoAlDia - cap.CompradoSep
    cap["PctCumpl"] = np.where(cap.EsperadoAlDia>0, cap.CompradoSep/cap.EsperadoAlDia*100, np.nan)
    cap = cap.sort_values("PromedioMensual", ascending=False)
    topN = cap.head(30 if len(cap)>=30 else len(cap))

    # no visitados
    cartera = pd.read_pickle(SC/"cartera.pkl")
    visitas = pd.read_pickle(SC/"visitas.pkl")
    cart_s = cartera[cartera.RepSupervisor.isin(cods)].copy()
    visitados = set(visitas.CliID.unique())
    cart_s["Visitado"] = cart_s.CliID.isin(visitados)
    por_vend_vis = cart_s.groupby(["RepCodigo","RepNombre"]).agg(Cartera=("CliID","count"), Visitados=("Visitado","sum")).reset_index()
    por_vend_vis["NoVisitados"] = por_vend_vis.Cartera - por_vend_vis.Visitados
    por_vend_vis["PctNoVisitado"] = por_vend_vis.NoVisitados/por_vend_vis.Cartera*100
    por_vend_vis = por_vend_vis.sort_values("PctNoVisitado", ascending=False)

    prom_cli = ventas6m.groupby("ClienteCodigo").agg(PromedioMensual=("ValorBruto","sum")).reset_index()
    prom_cli["PromedioMensual"] = prom_cli.PromedioMensual/6.0
    no_vis = cart_s[~cart_s.Visitado][["RepCodigo","RepNombre","CliID","CliNombre","TipoNegocio"]]
    no_vis = no_vis.merge(prom_cli, left_on="CliID", right_on="ClienteCodigo", how="left")
    no_vis["PromedioMensual"] = no_vis.PromedioMensual.fillna(0)
    no_vis = no_vis.sort_values("PromedioMensual", ascending=False)

    tot_cartera = int(cart_s.CliID.count()); tot_visitados = int(cart_s.Visitado.sum())
    tot_novis = tot_cartera - tot_visitados

    # oferta foco por vendedor
    of = vs[vs.ofeid.isin([3909,3911])].groupby(["CodigoVendedor","NombreVendedor"]).agg(ClientesActivados=("ClienteCodigo","nunique")).reset_index()
    of_total = int(vs[vs.ofeid.isin([3909,3911])].ClienteCodigo.nunique())

    return dict(cfg=cfg, por_vend=por_vend, por_suplidor=por_suplidor, tot=tot, cap=cap, topN=topN,
                por_vend_vis=por_vend_vis, no_vis=no_vis, tot_cartera=tot_cartera, tot_visitados=tot_visitados,
                tot_novis=tot_novis, of_vend=of, of_total=of_total)
