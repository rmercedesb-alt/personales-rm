# -*- coding: utf-8 -*-
# MOTOR (fijo) - no editar por mes. Lee todo de data.py.
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import FuncFormatter
import numpy as np
import os,data as D
_B=os.path.dirname(os.path.abspath(__file__))

plt.rcParams.update({"font.family":"sans-serif","font.sans-serif":["Arial","DejaVu Sans"],
 "font.size":11,"axes.edgecolor":"#BBBBBB","axes.linewidth":0.8,"figure.dpi":150})
VERDE="#0C9444"; AZUL="#203F99"; GRIS="#6E6E6E"; CARBON="#3A3A3A"
G="#1E8449"; Y="#E1A100"; R="#C0392B"
OUT=_B+"/charts/"; os.makedirs(OUT,exist_ok=True)
M=1_000_000
MESES_ABBR=["Ene","Feb","Mar","Abr","May","Jun","Jul","Ago","Sep","Oct","Nov","Dic"]
N=max(D.serie_2026);  MN=D.MES_NOMBRE; MA=D.MES_ABBR; LIDX=N-1  # last index (0-based)
meses=MESES_ABBR[:N]
def milfmt(x,_): return f"{x/M:.0f}"
def save(fig,name): fig.tight_layout(); fig.savefig(OUT+name,bbox_inches="tight",facecolor="white"); plt.close(fig)
def pm3(v):
    o=[None]*len(v)
    for i in range(2,len(v)): o[i]=sum(v[i-2:i+1])/3
    return o
sdo=[D.serie_2026[m][0] for m in range(1,N+1)]; azua=[D.serie_2026[m][1] for m in range(1,N+1)]
tot=[a+b for a,b in zip(sdo,azua)]

# 1. Serie + PM
fig,ax=plt.subplots(figsize=(9,4.6)); x=range(N)
ax.plot(x,tot,"-o",color=CARBON,lw=2.4,label="Total AM",zorder=5)
ax.plot(x,sdo,"-o",color=AZUL,lw=1.8,label="Santo Domingo")
ax.plot(x,azua,"-o",color=VERDE,lw=1.8,label="Azua (Sur)")
ax.plot(x,pm3(tot),"--",color=GRIS,lw=1.6,label="PM trimestral (Total)")
ax.set_xticks(list(x)); ax.set_xticklabels(meses)
ax.yaxis.set_major_formatter(FuncFormatter(milfmt)); ax.set_ylabel("RD$ millones (neto)"); ax.set_ylim(0,max(tot)*1.18)
ax.legend(ncol=2,fontsize=9,frameon=False,loc="upper center")
ax.set_title("Venta neta mensual 2026 y Promedio Móvil trimestral",color=CARBON,fontweight="bold",loc="left")
ax.grid(axis="y",color="#EEEEEE"); [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]
ax.annotate(f"{tot[LIDX]/M:.1f}M",(LIDX,tot[LIDX]),textcoords="offset points",xytext=(0,8),ha="center",fontsize=9,fontweight="bold",color=CARBON)
save(fig,"capa1_series.png")

# 2. Forecast vs Ejecutado por centro
ps={c:D.presu_jul[(c,"Mayorista")][0]+D.presu_jul[(c,"Minorista")][0] for c in ["Santo Domingo","Azua"]}
es={c:D.presu_jul[(c,"Mayorista")][1]+D.presu_jul[(c,"Minorista")][1] for c in ["Santo Domingo","Azua"]}
P=[ps["Santo Domingo"]+ps["Azua"],ps["Santo Domingo"],ps["Azua"]]; E=[es["Santo Domingo"]+es["Azua"],es["Santo Domingo"],es["Azua"]]
fig,ax=plt.subplots(figsize=(8,4.4)); xi=np.arange(3); w=0.38
ax.bar(xi-w/2,P,w,label="Forecast",color="#C9CEDB"); ax.bar(xi+w/2,E,w,label="Ejecutado",color=VERDE)
for i in range(3):
    pc=E[i]/P[i]; c=G if pc>=1 else (Y if pc>=.85 else R)
    ax.annotate(f"{pc*100:.1f}%",(xi[i]+w/2,E[i]),textcoords="offset points",xytext=(0,6),ha="center",fontweight="bold",color=c)
ax.set_xticks(xi); ax.set_xticklabels(["Total AM","Santo Domingo","Azua (Sur)"]); ax.yaxis.set_major_formatter(FuncFormatter(milfmt))
ax.set_ylabel("RD$ millones"); ax.legend(frameon=False,fontsize=9)
ax.set_title(f"Cumplimiento vs Forecast — {MN} 2026 (ex-Caobo)",color=CARBON,fontweight="bold",loc="left")
ax.grid(axis="y",color="#EEEEEE"); [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]
save(fig,"capa1_forecast.png")

# 3. YoY
a25=[D.serie_2025[N][0]+D.serie_2025[N][1],D.serie_2025[N][0],D.serie_2025[N][1]]; a26=[tot[LIDX],sdo[LIDX],azua[LIDX]]
fig,ax=plt.subplots(figsize=(8,4.4)); xi=np.arange(3)
ax.bar(xi-w/2,a25,w,label=f"{MN} 2025",color="#C9CEDB"); ax.bar(xi+w/2,a26,w,label=f"{MN} 2026",color=AZUL)
for i in range(3):
    dd=(a26[i]/a25[i]-1); c=G if dd>=0 else R
    ax.annotate(f"{dd*100:+.1f}%",(xi[i]+w/2,a26[i]),textcoords="offset points",xytext=(0,6),ha="center",fontweight="bold",color=c)
ax.set_xticks(xi); ax.set_xticklabels(["Total AM","Santo Domingo","Azua (Sur)"]); ax.yaxis.set_major_formatter(FuncFormatter(milfmt))
ax.set_ylabel("RD$ millones"); ax.legend(frameon=False,fontsize=9)
ax.set_title(f"Variación interanual — {MN} (neto, ex-Caobo)",color=CARBON,fontweight="bold",loc="left")
ax.grid(axis="y",color="#EEEEEE"); [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]
save(fig,"capa1_yoy.png")

# 4. Categorías
def cat_chart(cats,name,color,titulo):
    top=cats[:10][::-1]; fig,ax=plt.subplots(figsize=(7.5,4.6))
    ax.barh([c[0] for c in top],[c[1] for c in top],color=color)
    ax.xaxis.set_major_formatter(FuncFormatter(milfmt)); ax.set_xlabel("RD$ millones")
    ax.set_title(titulo,color=CARBON,fontweight="bold",loc="left")
    [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="x",color="#EEEEEE"); save(fig,name)
cat_chart(D.cat_sdo,"cat_sdo.png",AZUL,f"Santo Domingo — Top categorías ({MA} 2026)")
cat_chart(D.cat_azua,"cat_azua.png",VERDE,f"Azua — Top categorías ({MA} 2026)")

# 5. Proveedores
def prov_chart(centro,name,color):
    lst=D.prov_jul[centro]; top=lst[:4]; otros=sum(p[1] for p in lst[4:])
    rows=([(p[0],p[1]) for p in top]+[("Otros",otros)])[::-1]
    fig,ax=plt.subplots(figsize=(7.6,3.8)); tot_c=sum(p[1] for p in lst)
    cols=["#C9CEDB" if r[0]=="Otros" else color for r in rows]
    ax.barh([r[0] for r in rows],[r[1] for r in rows],color=cols)
    for i,r in enumerate(rows): ax.annotate(f" {r[1]/M:.1f}M · {r[1]/tot_c*100:.0f}%",(r[1],i),va="center",fontsize=9,color=CARBON)
    ax.xaxis.set_major_formatter(FuncFormatter(milfmt)); ax.set_xlabel("RD$ millones"); ax.set_xlim(0,max(r[1] for r in rows)*1.25)
    ax.set_title(f"{centro} — Proveedores Top 4 + Otros ({MA} 2026)",color=CARBON,fontweight="bold",loc="left")
    [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="x",color="#EEEEEE"); save(fig,name)
prov_chart("Santo Domingo","prov_sdo.png",AZUL); prov_chart("Azua","prov_azua.png",VERDE)

# 6. ABCD (trimestre)
def abcd_chart(centro,name):
    d=D.abcd[centro]; segs=["A","B","C","D"]; xi=np.arange(4); w=0.26; mm3=D.TRIM_MESES
    cols={mm3[0]:"#C9CEDB",mm3[1]:"#7E93C9",mm3[2]:AZUL if centro=="Santo Domingo" else VERDE}
    fig,ax=plt.subplots(figsize=(7.6,4.2))
    for j,mm in enumerate(mm3):
        ax.bar(xi+(j-1)*w,[d[mm][s][0] for s in segs],w,label=MESES_ABBR[mm-1],color=cols[mm])
    ax.set_xticks(xi); ax.set_xticklabels(["A Grande","B Mediano","C Pequeño","D Muy peq."])
    ax.yaxis.set_major_formatter(FuncFormatter(milfmt)); ax.set_ylabel("RD$ millones"); ax.legend(frameon=False,fontsize=9)
    ax.set_title(f"{centro} — Clientes A·B·C·D ({MESES_ABBR[mm3[0]-1]}–{MESES_ABBR[mm3[2]-1]}, PM trimestral)",color=CARBON,fontweight="bold",loc="left")
    [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="y",color="#EEEEEE"); save(fig,name)
abcd_chart("Santo Domingo","abcd_sdo.png"); abcd_chart("Azua","abcd_azua.png")

# 7. Gauss cuota
def gauss_chart(centro,name,grupos,titulo):
    vs=[v for v in D.vendedores if v[1]==centro and v[2] in grupos and v[4]>0]; cump=[v[5]/v[4]*100 for v in vs]
    fig,ax=plt.subplots(figsize=(7.8,4.3)); bins=np.arange(50,140,10)
    n,b,patches=ax.hist(cump,bins=bins,edgecolor="white")
    for p,left in zip(patches,bins[:-1]): p.set_facecolor(R if left<85 else (Y if left<100 else G))
    mu=np.mean(cump); ax.axvline(mu,color=CARBON,lw=1.8,ls="--"); ax.axvline(100,color=GRIS,lw=1,ls=":")
    ax.annotate(f"Media {mu:.0f}%",(mu,ax.get_ylim()[1]*0.9),ha="center",fontsize=9,fontweight="bold")
    nc=sum(1 for c in cump if c>=100)
    ax.set_xlabel("Cumplimiento de cuota (%)"); ax.set_ylabel("N° vendedores")
    ax.set_title(titulo+f"  ·  {nc}/{len(cump)} sobre cuota",color=CARBON,fontweight="bold",loc="left",fontsize=11)
    [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="y",color="#EEEEEE"); save(fig,name)
gauss_chart("Santo Domingo","gauss_sdo.png",D.GRUPOS_SDO,"SDO — Campana de cumplimiento de cuota")
gauss_chart("Azua","gauss_azua.png",D.GRUPOS_AZUA,"Azua — Campana de cumplimiento de cuota")

# 8. Colombina
def colo_chart():
    fig,axs=plt.subplots(1,2,figsize=(10,3.8))
    for ax,(data,centro,color) in zip(axs,[(D.colombina_sdo,"Santo Domingo",AZUL),(D.colombina_azua,"Azua",VERDE)]):
        dd=data[::-1]; ax.barh([r[0] for r in dd],[r[1] for r in dd],color=color)
        ax.xaxis.set_major_formatter(FuncFormatter(lambda x,_:f"{x/1000:.0f}")); ax.set_xlabel("RD$ miles")
        ax.set_title(f"Colombina — Top 5 SKU · {centro}",color=CARBON,fontweight="bold",loc="left",fontsize=10)
        [s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="x",color="#EEEEEE")
    save(fig,"colombina.png")
colo_chart()

# 9. Operación
fig,ax=plt.subplots(figsize=(7.0,4.0)); xi=np.arange(2); w=0.36
sd=[D.ind_op["Santo Domingo"]["efectividad"]*100,D.ind_op["Santo Domingo"]["activacion"]*100]
az=[D.ind_op["Azua"]["efectividad"]*100,D.ind_op["Azua"]["activacion"]*100]
b1=ax.bar(xi-w/2,sd,w,label="Santo Domingo",color=AZUL); b2=ax.bar(xi+w/2,az,w,label="Azua (Sur)",color=VERDE)
for bars in (b1,b2):
    for b in bars: ax.annotate(f"{b.get_height():.1f}%",(b.get_x()+b.get_width()/2,b.get_height()),textcoords="offset points",xytext=(0,4),ha="center",fontsize=9,fontweight="bold")
ax.set_xticks(xi); ax.set_xticklabels(["Efectividad de visita","Activación de cartera"]); ax.set_ylabel("%"); ax.set_ylim(0,110)
ax.legend(frameon=False,fontsize=9); ax.set_title(f"Indicadores de operación — {MN} 2026",color=CARBON,fontweight="bold",loc="left")
[s.set_visible(False) for s in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="y",color="#EEEEEE"); save(fig,"operacion.png")

# 10. Puente like-for-like (pera con pera)
L=D.LFL; m=1_000_000
vals=[L["r2025"]/m,L["desc"]/m,L["base"]/m,L["nuevas"]/m,L["r2026"]/m]
fig,ax=plt.subplots(figsize=(9.2,4.6))
ax.bar(0,vals[0],color=GRIS)
run=vals[0]
ax.bar(1,vals[1],bottom=run,color="#9AA6B2"); run+=vals[1]
ax.bar(2,vals[2],bottom=run,color=R); run+=vals[2]
ax.bar(3,vals[3],bottom=run,color=G); run+=vals[3]
ax.bar(4,vals[4],color=CARBON)
tops=[vals[0],vals[0],vals[0]+vals[1],vals[0]+vals[1]+vals[2]+vals[3],vals[4]]
labs=[f"{vals[0]:.1f}",f"{vals[1]:+.1f}",f"{vals[2]:+.1f}  ({L['yoy_total']*100:+.1f}%)",f"{vals[3]:+.1f}",f"{vals[4]:.1f}"]
cols=[CARBON,"#5B6670",R,G,CARBON]
for i2,(t,lb,c) in enumerate(zip(tops,labs,cols)):
    ax.annotate(lb,(i2,t),textcoords="offset points",xytext=(0,7 if i2!=1 and i2!=2 else -13),ha="center",fontsize=9,fontweight="bold",color=c)
ax.set_xticks(range(5))
ax.set_xticklabels([f"{MA} 2025\nreportado","− Descontinuadas\n(galletería +\nmarcas salidas)","Δ base continua\n(like-for-like)","+ Nuevas\n(Molinos)",f"{MA} 2026\nreportado"],fontsize=8)
ax.yaxis.set_major_formatter(FuncFormatter(lambda x,_:f"{x:.0f}")); ax.set_ylabel("RD$ millones (neto)"); ax.set_ylim(0,max(vals)*1.14)
ax.set_title("Puente interanual pera con pera — el negocio continuo cae solo −6%; el resto es portafolio (Molinos entra, galletería/marcas salen)",color=CARBON,fontweight="bold",loc="left",fontsize=9.5)
[sp.set_visible(False) for sp in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="y",color="#EEEEEE"); save(fig,"capa1_bridge.png")
print("Bridge LFL OK  total %.1f%% SDO %.1f%% Azua %.1f%%"%(L['yoy_total']*100,L['yoy_sdo']*100,L['yoy_azua']*100))

# 11. Apéndice Caobo por suplidor
if getattr(D,"caobo_prov",None):
    cp=sorted(D.caobo_prov,key=lambda r:-r[1])
    fig,ax=plt.subplots(figsize=(7.6,3.4)); yy=np.arange(len(cp))[::-1]
    ax.barh(yy,[r[1] for r in cp],color=AZUL,label=f"{MN} 2026 (neto)")
    ax.barh(yy,[r[3] for r in cp],color="#C9CEDB",height=0.4,label=f"{MN} 2025")
    ax.set_yticks(yy); ax.set_yticklabels([r[0] for r in cp])
    ax.xaxis.set_major_formatter(FuncFormatter(lambda x,_:f"{x/1e6:.1f}")); ax.set_xlabel("RD$ millones (neto)")
    ax.legend(frameon=False,fontsize=8,loc="lower right")
    ax.set_title(f"Ventas a Inversiones Caobo por suplidor — {MN} 2026",color=CARBON,fontweight="bold",loc="left",fontsize=10)
    [sp.set_visible(False) for sp in [ax.spines['top'],ax.spines['right']]]; ax.grid(axis="x",color="#EEEEEE"); save(fig,"caobo.png")
    print("Caobo chart OK")
