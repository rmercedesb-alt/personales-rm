# -*- coding: utf-8 -*-
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from pathlib import Path
OUT = Path(__file__).parent / "img"; OUT.mkdir(exist_ok=True)

AZUL="#203F99"; VERDE="#0C9444"; GRIS="#3A3A3A"; GRIS2="#6E6E6E"; ROJO="#C0392B"; AMBAR="#E67E22"
plt.rcParams.update({'font.family':'DejaVu Sans','font.size':9,'axes.edgecolor':'#D5DAD7',
                     'axes.labelcolor':GRIS,'xtick.color':GRIS2,'ytick.color':GRIS2})

def presu_por_vendedor(key, por_vend):
    d = por_vend.sort_values("PresupuestoMes", ascending=False)
    nombres = [n.split(" (")[0][:14] for n in d.RepNombre]
    fig, ax = plt.subplots(figsize=(10.6, 3.1), dpi=200)
    x = np.arange(len(d)); w=0.36
    ax.bar(x-w/2, d.PresupuestoAlDia/1e6, width=w, color=GRIS2, label="Presup. al día")
    ax.bar(x+w/2, d.Ejecutado/1e6, width=w, color=AZUL, label="Ejecutado")
    for i,row in enumerate(d.itertuples()):
        color = ROJO if row.PctAlcance<80 else (AMBAR if row.PctAlcance<95 else VERDE)
        ax.text(i, max(row.PresupuestoAlDia,row.Ejecutado)/1e6+0.05, f"{row.PctAlcance:.0f}%", ha='center', fontsize=8, fontweight='bold', color=color)
    ax.set_xticks(x); ax.set_xticklabels(nombres, fontsize=7.5, rotation=28, ha='right')
    ax.set_ylabel("RD$ Millones", fontsize=8.5)
    ax.legend(fontsize=8, frameon=False)
    ax.grid(axis='y', color='#EAEDEB', lw=.8); ax.set_axisbelow(True)
    for s in ['top','right']: ax.spines[s].set_visible(False)
    fig.tight_layout(); fig.savefig(OUT/f"{key}_presu_vend.png", transparent=True); plt.close(fig)

def novis_por_vendedor(key, por_vend_vis):
    d = por_vend_vis.sort_values("PctNoVisitado", ascending=False).head(12)
    nombres = [n.split(" (")[0][:14] for n in d.RepNombre]
    fig, ax = plt.subplots(figsize=(10.6, 2.7), dpi=200)
    cols = [ROJO if p>20 else (AMBAR if p>10 else VERDE) for p in d.PctNoVisitado]
    b = ax.bar(nombres, d.PctNoVisitado, color=cols, width=0.55)
    for r,val,n in zip(b, d.PctNoVisitado, d.NoVisitados):
        ax.text(r.get_x()+r.get_width()/2, val+0.5, f"{val:.0f}%({n})", ha='center', fontsize=7.5, color=GRIS, fontweight='bold')
    ax.set_ylabel("% cartera sin visitar", fontsize=8.5)
    ax.tick_params(axis='x', labelsize=7.5, rotation=28)
    for lbl in ax.get_xticklabels(): lbl.set_ha('right')
    for s in ['top','right']: ax.spines[s].set_visible(False)
    ax.grid(axis='y', color='#EAEDEB', lw=.8); ax.set_axisbelow(True)
    fig.tight_layout(); fig.savefig(OUT/f"{key}_novis_vend.png", transparent=True); plt.close(fig)
