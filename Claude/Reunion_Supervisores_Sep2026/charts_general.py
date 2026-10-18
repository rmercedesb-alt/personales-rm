# -*- coding: utf-8 -*-
import pandas as pd, numpy as np, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from pathlib import Path
OUT = Path(__file__).parent / "img"; OUT.mkdir(exist_ok=True)
SC = Path(__file__).parent

AZUL="#203F99"; VERDE="#0C9444"; GRIS="#3A3A3A"; GRIS2="#6E6E6E"; ROJO="#C0392B"; AMBAR="#E67E22"
plt.rcParams.update({'font.family':'DejaVu Sans','font.size':9,'axes.edgecolor':'#D5DAD7',
                     'axes.labelcolor':GRIS,'xtick.color':GRIS2,'ytick.color':GRIS2,'axes.titlecolor':GRIS})

ORDEN_SUP = ["Ramon E. Pimentel","Eliezer Martinez","Narciso Pina Bello","Cristiano Perez","Disponible Sur Corto"]
NOMBRE_CORTO = {"Ramon E. Pimentel":"Ramon E.\nPimentel","Eliezer Martinez":"Eliezer\nMartinez",
                "Narciso Pina Bello":"Narciso\nPiña","Cristiano Perez":"Cristiano\nPérez",
                "Disponible Sur Corto":"Disp. Sur\nCorto (sin sup.)"}

def P(n): return pd.read_pickle(SC/f"{n}.pkl")

# 1. Presupuesto al dia vs Ejecutado por supervisor (barras agrupadas) + % alcance
por_sup = P("r_por_supervisor")
por_sup = por_sup[por_sup.SupervisorGrp != "Rudy Mercedes (Institucional)"]
por_sup = por_sup.set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
fig, ax = plt.subplots(figsize=(7.6, 3.1), dpi=200)
x = np.arange(len(por_sup)); w = 0.36
b1 = ax.bar(x-w/2, por_sup.PresupuestoAlDia/1e6, width=w, color=GRIS2, label="Presupuesto al día")
b2 = ax.bar(x+w/2, por_sup.Ejecutado/1e6, width=w, color=AZUL, label="Ejecutado")
for i,row in por_sup.iterrows():
    color = ROJO if row.PctAlcance < 80 else (AMBAR if row.PctAlcance < 95 else VERDE)
    ax.text(i, max(row.PresupuestoAlDia,row.Ejecutado)/1e6+0.35, f"{row.PctAlcance:.0f}%",
            ha='center', fontsize=10, fontweight='bold', color=color)
ax.set_xticks(x); ax.set_xticklabels([NOMBRE_CORTO[s] for s in por_sup.SupervisorGrp], fontsize=8)
ax.set_ylabel("RD$ Millones", fontsize=8.5)
ax.legend(fontsize=8, frameon=False, loc='upper right')
ax.grid(axis='y', color='#EAEDEB', lw=.8); ax.set_axisbelow(True)
for s in ['top','right']: ax.spines[s].set_visible(False)
fig.tight_layout(); fig.savefig(OUT/"presu_supervisor.png", transparent=True); plt.close(fig)

# 2. Por canal
por_canal = P("r_por_canal")
fig, ax = plt.subplots(figsize=(5.6, 3.0), dpi=200)
x = np.arange(len(por_canal)); w=0.36
ax.bar(x-w/2, por_canal.PresupuestoAlDia/1e6, width=w, color=GRIS2, label="Presup. al día")
ax.bar(x+w/2, por_canal.Ejecutado/1e6, width=w, color=VERDE, label="Ejecutado")
for i,row in por_canal.iterrows():
    color = ROJO if row.PctAlcance<80 else (AMBAR if row.PctAlcance<95 else VERDE)
    ax.text(i, max(row.PresupuestoAlDia,row.Ejecutado)/1e6+0.8, f"{row.PctAlcance:.0f}%", ha='center', fontsize=10, fontweight='bold', color=color)
ax.set_xticks(x); ax.set_xticklabels(por_canal.Canal, fontsize=9)
ax.set_ylabel("RD$ Millones", fontsize=8.5)
ax.legend(fontsize=8, frameon=False)
ax.grid(axis='y', color='#EAEDEB', lw=.8); ax.set_axisbelow(True)
for s in ['top','right']: ax.spines[s].set_visible(False)
fig.tight_layout(); fig.savefig(OUT/"presu_canal.png", transparent=True); plt.close(fig)

# 3. Top50 clientes: esperado vs comprado por supervisor
r50 = P("r_resumen_top50_sup")
r50 = r50[r50.SupervisorGrp != "Rudy Mercedes (Institucional)"]
r50 = r50.set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
fig, ax = plt.subplots(figsize=(7.6, 3.1), dpi=200)
x = np.arange(len(r50)); w=0.36
ax.bar(x-w/2, r50.EsperadoAlDia/1e6, width=w, color=GRIS2, label="Esperado a la fecha (según promedio)")
ax.bar(x+w/2, r50.CompradoSep/1e6, width=w, color=AMBAR, label="Comprado en septiembre")
for i,row in r50.iterrows():
    color = ROJO if row.PctCumpl<80 else (AMBAR if row.PctCumpl<100 else VERDE)
    ax.text(i, max(row.EsperadoAlDia,row.CompradoSep)/1e6+0.15, f"{row.PctCumpl:.0f}%", ha='center', fontsize=10, fontweight='bold', color=color)
ax.set_xticks(x); ax.set_xticklabels([NOMBRE_CORTO[s] for s in r50.SupervisorGrp], fontsize=8)
ax.set_ylabel("RD$ Millones", fontsize=8.5)
ax.legend(fontsize=7.5, frameon=False, loc='upper right')
ax.grid(axis='y', color='#EAEDEB', lw=.8); ax.set_axisbelow(True)
for s in ['top','right']: ax.spines[s].set_visible(False)
fig.tight_layout(); fig.savefig(OUT/"top50_supervisor.png", transparent=True); plt.close(fig)

# 4. % No visitados por supervisor
vis = P("r_visitas_sup")
vis = vis[vis.SupervisorGrp != "Rudy Mercedes (Institucional)"]
vis = vis.set_index("SupervisorGrp").reindex(ORDEN_SUP).reset_index()
fig, ax = plt.subplots(figsize=(6.0, 3.0), dpi=200)
cols = [ROJO if p>20 else (AMBAR if p>10 else VERDE) for p in vis.PctNoVisitado]
b = ax.bar([NOMBRE_CORTO[s] for s in vis.SupervisorGrp], vis.PctNoVisitado, color=cols, width=0.55)
for r,val,n in zip(b, vis.PctNoVisitado, vis.NoVisitados):
    ax.text(r.get_x()+r.get_width()/2, val+0.6, f"{val:.0f}%\n({n})", ha='center', fontsize=8.5, color=GRIS, fontweight='bold')
ax.set_ylabel("% de la cartera sin visitar en septiembre", fontsize=8.5)
ax.set_ylim(0, max(vis.PctNoVisitado)*1.35)
ax.tick_params(axis='x', labelsize=8)
for s in ['top','right']: ax.spines[s].set_visible(False)
ax.grid(axis='y', color='#EAEDEB', lw=.8); ax.set_axisbelow(True)
fig.tight_layout(); fig.savefig(OUT/"no_visitados.png", transparent=True); plt.close(fig)

# 5. Oferta foco: clientes activados por supervisor
of = P("r_oferta_por_sup")
of = of.set_index("SupervisorGrp").reindex(ORDEN_SUP).fillna(0).reset_index()
fig, ax = plt.subplots(figsize=(6.0, 2.9), dpi=200)
b = ax.bar([NOMBRE_CORTO[s] for s in of.SupervisorGrp], of.ClientesActivados, color=VERDE, width=0.55)
for r,val in zip(b, of.ClientesActivados):
    ax.text(r.get_x()+r.get_width()/2, val+8, f"{int(val)}", ha='center', fontsize=9.5, color=GRIS, fontweight='bold')
ax.set_ylabel("Clientes activados con la oferta (Sept. 1-19)", fontsize=8.5)
ax.tick_params(axis='x', labelsize=8)
for s in ['top','right']: ax.spines[s].set_visible(False)
ax.grid(axis='y', color='#EAEDEB', lw=.8); ax.set_axisbelow(True)
fig.tight_layout(); fig.savefig(OUT/"oferta_activados.png", transparent=True); plt.close(fig)

print("charts_general OK")
