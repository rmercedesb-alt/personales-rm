# -*- coding: utf-8 -*-
import pandas as pd, numpy as np, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from pathlib import Path
OUT=Path(__file__).parent/"img"; OUT.mkdir(exist_ok=True)
AZUL="#203F99"; VERDE="#0C9444"; GRIS="#3A3A3A"; GRIS2="#6E6E6E"; ROJO="#C0392B"; NARANJA="#E67E22"
plt.rcParams.update({'font.family':'DejaVu Sans','font.size':9,'axes.edgecolor':'#D5DAD7',
                     'axes.labelcolor':GRIS,'xtick.color':GRIS2,'ytick.color':GRIS2,'axes.titlecolor':GRIS})
P=lambda n: pd.read_pickle(n+'.pkl')
cartera,ventas,visitas,diasrep,sem=[P(x) for x in ['cartera','ventas','visitas','diasrep','sem']]
MESES=['2026-03','2026-04','2026-05','2026-06','2026-07','2026-08']
ETIQ=['Mar','Abr','May','Jun','Jul','Ago']
NOM={'25':'Jhonny Rosario','36':'Hanel Giron','106':'Wascar Alvarez','185':'Fernando Martinez'}
COL={'25':AZUL,'36':VERDE,'106':NARANJA,'185':GRIS2}
cset=set(cartera.CliID); v=ventas[ventas.CliID.isin(cset)]; vis=visitas[visitas.CliID.isin(cset)]
base=cartera.groupby('RepCodigo').CliID.count()

# --- 1. Cobertura de visitas mensual por vendedor ---
cov=vis.groupby(['RepCodigo','Mes']).CliID.nunique().unstack().reindex(columns=MESES)
covp=(cov.div(base,axis=0)*100)
fig,ax=plt.subplots(figsize=(5.6,2.35),dpi=200)
for r in ['106','36','25','185']:
    ax.plot(ETIQ,covp.loc[r].values,marker='o',ms=4,lw=2,color=COL[r],label=f"{NOM[r]} ({base[r]})")
ax.set_ylim(0,110); ax.set_ylabel("% cartera visitada",fontsize=8)
ax.yaxis.set_major_formatter(lambda x,p:f"{x:.0f}%")
ax.grid(axis='y',color='#EAEDEB',lw=.8); ax.set_axisbelow(True)
for s in ['top','right']: ax.spines[s].set_visible(False)
ax.legend(fontsize=6.6,frameon=False,ncol=2,loc='upper center',bbox_to_anchor=(.5,-.20),columnspacing=1.2,handlelength=1.4)
fig.subplots_adjust(left=.13,right=.99,top=.97,bottom=.34)
fig.savefig(OUT/"cobertura_visitas.png",transparent=True); plt.close(fig)

# --- 2. Efectividad: clientes que compraron / clientes visitados ---
sold=v.groupby(['RepCodigo','Mes']).CliID.nunique().unstack().reindex(columns=MESES)
ef=(sold/cov*100)
fig,ax=plt.subplots(figsize=(5.6,2.35),dpi=200)
for r in ['106','36','25','185']:
    ax.plot(ETIQ,ef.loc[r].values,marker='o',ms=4,lw=2,color=COL[r])
ax.axhline(70,color=ROJO,ls='--',lw=1); ax.text(0.02,72,"meta 70%",fontsize=6.5,color=ROJO)
ax.set_ylim(0,100); ax.set_ylabel("% visitados que compran",fontsize=8)
ax.yaxis.set_major_formatter(lambda x,p:f"{x:.0f}%")
ax.grid(axis='y',color='#EAEDEB',lw=.8); ax.set_axisbelow(True)
for s in ['top','right']: ax.spines[s].set_visible(False)
fig.subplots_adjust(left=.13,right=.99,top=.97,bottom=.13)
fig.savefig(OUT/"efectividad.png",transparent=True); plt.close(fig)

# --- 3. Concentracion semanal de la venta ---
s=sem.groupby('SemanaMes').Valor.sum(); s=s/s.sum()*100
fig,ax=plt.subplots(figsize=(5.3,2.5),dpi=200)
cols=[GRIS2,GRIS2,GRIS2,ROJO]
b=ax.bar(['Sem 1\n(1-7)','Sem 2\n(8-14)','Sem 3\n(15-21)','Sem 4\n(22-fin)'],s.values,color=cols,width=.6)
for r,val in zip(b,s.values):
    ax.text(r.get_x()+r.get_width()/2,val+1.2,f"{val:.0f}%",ha='center',fontsize=9,color=GRIS,fontweight='bold')
ax.set_ylim(0,62); ax.set_yticks([])
for sp in ['top','right','left']: ax.spines[sp].set_visible(False)
ax.tick_params(axis='x',length=0,labelsize=8)
fig.tight_layout(); fig.savefig(OUT/"semanas.png",transparent=True); plt.close(fig)

# --- 4. Venta mensual por vendedor ---
vm=v.groupby(['RepCodigo','Mes']).Valor.sum().unstack().reindex(columns=MESES).fillna(0)/1e6
fig,ax=plt.subplots(figsize=(5.6,2.05),dpi=200)
bot=np.zeros(6)
for r in ['25','36','106','185']:
    ax.bar(ETIQ,vm.loc[r].values,bottom=bot,color=COL[r],width=.62,label=NOM[r])
    bot+=vm.loc[r].values
for i,t in enumerate(bot): ax.text(i,t+.3,f"{t:.1f}",ha='center',fontsize=7.5,color=GRIS,fontweight='bold')
ax.set_ylabel("RD$ MM",fontsize=8); ax.set_ylim(0,bot.max()*1.18)
ax.grid(axis='y',color='#EAEDEB',lw=.8); ax.set_axisbelow(True)
for sp in ['top','right']: ax.spines[sp].set_visible(False)
ax.legend(fontsize=6.2,frameon=False,ncol=4,loc='upper center',bbox_to_anchor=(.5,-.15),columnspacing=.9,handlelength=1.1)
fig.subplots_adjust(left=.10,right=.99,top=.96,bottom=.26)
fig.savefig(OUT/"venta_mensual.png",transparent=True); plt.close(fig)
print("charts OK")
