# -*- coding: utf-8 -*-
# MOTOR (fijo). Narrativa y números vienen de data.py (NARR + config). No editar por mes.
from reportlab.lib.pagesizes import letter
from reportlab.lib.units import inch
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import (BaseDocTemplate,PageTemplate,Frame,Paragraph,Spacer,Image,
 Table,TableStyle,PageBreak,NextPageTemplate,KeepTogether)
from reportlab.lib.enums import TA_LEFT,TA_CENTER
import os,data as D
_B=os.path.dirname(os.path.abspath(__file__))
OUT=os.path.dirname(_B)+"/"; CH=_B+"/charts/"; B=_B+"/"
VERDE=colors.HexColor("#0C9444"); AZUL=colors.HexColor("#203F99"); GRIS=colors.HexColor("#6E6E6E")
CARBON=colors.HexColor("#3A3A3A"); AZCLARO=colors.HexColor("#E7EBF5")
G=colors.HexColor("#1E8449"); Y=colors.HexColor("#E1A100"); R=colors.HexColor("#C0392B"); LINEA=colors.HexColor("#D9D9D9")
MN=D.MES_NOMBRE; MA=D.MES_ABBR; MES=D.MES
def rd(x): return f"RD${x:,.0f}"
def pct(x): return f"{x*100:.1f}%"
GD="<font color='#1E8449'>●</font>"; YD="<font color='#E1A100'>●</font>"; RDT="<font color='#C0392B'>●</font>"
def semd(p): return GD if p>=1 else (YD if p>=.85 else RDT)
def tok(s): return s.replace("{G}",GD).replace("{Y}",YD).replace("{R}",RDT)
_CELL=ParagraphStyle("cell",fontName="Helvetica",fontSize=7.5,leading=9,alignment=1)
def dc(m): return Paragraph(m,_CELL)

# metrics
sdo=[D.serie_2026[m][0] for m in range(1,MES+1)]; azua=[D.serie_2026[m][1] for m in range(1,MES+1)]; tot=[a+b for a,b in zip(sdo,azua)]
presu={c:(D.presu_jul[(c,"Mayorista")][0]+D.presu_jul[(c,"Minorista")][0]) for c in ["Santo Domingo","Azua"]}
ejec ={c:(D.presu_jul[(c,"Mayorista")][1]+D.presu_jul[(c,"Minorista")][1]) for c in ["Santo Domingo","Azua"]}
pt=sum(presu.values()); et=sum(ejec.values()); L=MES-1
yoy={"Total AM":tot[L]/(D.serie_2025[MES][0]+D.serie_2025[MES][1])-1,"Santo Domingo":sdo[L]/D.serie_2025[MES][0]-1,"Azua":azua[L]/D.serie_2025[MES][1]-1}
exmol=(D.tot_jul26-D.mol_jul26)/D.tot_jul25-1

def st(name,sz,color=CARBON,bold=False,lead=None,align=TA_LEFT,space=4):
    return ParagraphStyle(name,fontName="Helvetica-Bold" if bold else "Helvetica",fontSize=sz,leading=lead or sz*1.25,textColor=color,alignment=align,spaceAfter=space)
H1=st("H1",19,CARBON,True); H2=st("H2",13,VERDE,True,space=6)
SMALL=st("SMALL",7.5,GRIS); KPILBL=st("KL",7.5,GRIS,align=TA_CENTER)
INTRO=ParagraphStyle("intro",fontName="Helvetica",fontSize=9.3,leading=12.8,textColor=CARBON,spaceAfter=7)
CAP=ParagraphStyle("cap",fontName="Helvetica",fontSize=8.4,leading=11,textColor=colors.HexColor("#333333"),spaceBefore=3,spaceAfter=9,leftIndent=2)
SUBT=st("subt",10.5,CARBON,True,space=4)
WRAP=ParagraphStyle("wrap",fontName="Helvetica",fontSize=7.3,leading=8.8,textColor=CARBON,alignment=0)
def cap(txt): return Paragraph("<b>Lectura.</b> "+tok(txt),CAP)

PW,PH=letter; MX=0.6*inch
def hf(canvas,doc):
    canvas.saveState(); canvas.setFillColor(VERDE); canvas.rect(0,PH-0.16*inch,PW,0.16*inch,fill=1,stroke=0)
    canvas.setFillColor(GRIS); canvas.setFont("Helvetica",7)
    canvas.drawString(MX,0.42*inch,f"AM Comercial — Informe Ejecutivo de Cierre · {MN} 2026 · Confidencial")
    canvas.drawRightString(PW-MX,0.42*inch,f"Pág. {doc.page}")
    canvas.setStrokeColor(LINEA); canvas.line(MX,0.58*inch,PW-MX,0.58*inch); canvas.restoreState()
frame=Frame(MX,0.7*inch,PW-2*MX,PH-1.5*inch,id='main'); cover=Frame(MX,0.7*inch,PW-2*MX,PH-1.2*inch,id='cover')
doc=BaseDocTemplate(OUT+f"AM_Cierre_{MN}2026.pdf",pagesize=letter,leftMargin=MX,rightMargin=MX,topMargin=0.8*inch,bottomMargin=0.7*inch)
doc.addPageTemplates([PageTemplate(id='cover',frames=[cover],onPage=lambda c,d:None),PageTemplate(id='main',frames=[frame],onPage=hf)])
E=[]
def kpi_card(valor,label,color):
    t=Table([[Paragraph(valor,ParagraphStyle('v',fontName="Helvetica-Bold",fontSize=12.5,textColor=color,alignment=TA_CENTER))],[Paragraph(label,KPILBL)]],colWidths=[1.55*inch],rowHeights=[0.34*inch,0.3*inch])
    t.setStyle(TableStyle([("BOX",(0,0),(-1,-1),0.6,LINEA),("BACKGROUND",(0,0),(-1,-1),colors.white),("VALIGN",(0,0),(-1,-1),"MIDDLE"),("LINEBELOW",(0,0),(0,0),2,color)])); return t
def tabla(data,widths,font=8):
    ts=[("FONTNAME",(0,0),(-1,-1),"Helvetica"),("FONTSIZE",(0,0),(-1,-1),font),("GRID",(0,0),(-1,-1),0.4,LINEA),("VALIGN",(0,0),(-1,-1),"MIDDLE"),("TOPPADDING",(0,0),(-1,-1),3),("BOTTOMPADDING",(0,0),(-1,-1),3),("LEFTPADDING",(0,0),(-1,-1),4),("RIGHTPADDING",(0,0),(-1,-1),4),("BACKGROUND",(0,0),(-1,0),VERDE),("TEXTCOLOR",(0,0),(-1,0),colors.white),("FONTNAME",(0,0),(-1,0),"Helvetica-Bold"),("ALIGN",(1,0),(-1,-1),"RIGHT")]
    t=Table(data,colWidths=widths); t.setStyle(TableStyle(ts)); return t

# ===== COVER =====
E.append(Spacer(1,0.3*inch))
try: E.append(Image(B+"logo_am_comercial.png",width=2.2*inch,height=2.2*inch*0.32))
except: pass
E.append(Spacer(1,0.35*inch))
E.append(Paragraph("INFORME EJECUTIVO DE CIERRE DE VENTAS",H1))
E.append(Paragraph(f"{MN} 2026 · Lectura trimestral móvil ({D.TRIM})",st("s",12,GRIS)))
E.append(Spacer(1,0.15*inch))
E.append(Paragraph("Presentación a Presidencia — Miguel Molinari",st("s",10,CARBON,True)))
E.append(Paragraph("Presentan: Giancarlo Molinari (VP) · Rudy Mercedes (Gcia. Comercial)",st("s",9,GRIS)))
E.append(Spacer(1,0.3*inch))
E.append(Paragraph(f"Fuente: Movil Business (VWVENTASRESUMEN · VWPresupuestos · vwVisitas · RutaVisitas). Base: venta NETA sin ITBIS. Excluye Inversiones Caobo. Cálculo: {D.FECHA_CALC}.",SMALL))
E.append(NextPageTemplate('main')); E.append(PageBreak())

# ===== CAPA 1 =====
E.append(Paragraph("Capa 1 · Plataforma general",H2))
E.append(Paragraph(D.NARR["CAPA1_INTRO"],INTRO))
kpirow=Table([[kpi_card(f"RD${et/1e6:.1f}M","Total AM ejecutado",CARBON),
               kpi_card(pct(et/pt)+" "+("●" if False else "●"),"% del Forecast",Y if et/pt<1 else G),
               kpi_card(f"{yoy['Total AM']*100:+.1f}%",f"Total vs {MA} 2025",G if yoy['Total AM']>=0 else R),
               kpi_card(f"{D.LFL['yoy_total']*100:+.1f}%","Base like-for-like vs 2025",R if D.LFL['yoy_total']<0 else G)]],colWidths=[1.7*inch]*4)
kpirow.setStyle(TableStyle([("ALIGN",(0,0),(-1,-1),"CENTER")])); E.append(kpirow); E.append(Spacer(1,0.1*inch))
kpi=[["Vista",f"Neto {MA} 2026","Forecast","% Fcst","Sem.","Δ% Interan.","Sem."],
     ["Total AM",rd(et),rd(pt),pct(et/pt),dc(semd(et/pt)),f"{yoy['Total AM']*100:+.1f}%",dc(GD if yoy['Total AM']>=0 else RDT)],
     ["Santo Domingo",rd(ejec["Santo Domingo"]),rd(presu["Santo Domingo"]),pct(ejec["Santo Domingo"]/presu["Santo Domingo"]),dc(semd(ejec["Santo Domingo"]/presu["Santo Domingo"])),f"{yoy['Santo Domingo']*100:+.1f}%",dc(GD if yoy['Santo Domingo']>=0 else RDT)],
     ["Azua (Sur)",rd(ejec["Azua"]),rd(presu["Azua"]),pct(ejec["Azua"]/presu["Azua"]),dc(semd(ejec["Azua"]/presu["Azua"])),f"{yoy['Azua']*100:+.1f}%",dc(GD if yoy['Azua']>=0 else RDT)]]
t=tabla(kpi,[1.5*inch,1.15*inch,1.15*inch,0.7*inch,0.5*inch,1.0*inch,0.5*inch])
t.setStyle(TableStyle([("BACKGROUND",(0,1),(-1,1),AZCLARO),("FONTNAME",(0,1),(-1,1),"Helvetica-Bold")])); E.append(t)
E.append(cap(D.NARR["CAP_KPI"]))
E.append(KeepTogether([Image(CH+"capa1_bridge.png",width=6.0*inch,height=6.0*inch*0.52),cap(D.NARR["CAP_BRIDGE"])]))
E.append(KeepTogether([Table([[Image(CH+"capa1_series.png",width=3.45*inch,height=3.45*inch*0.51),Image(CH+"capa1_forecast.png",width=3.35*inch,height=3.35*inch*0.55)]],colWidths=[3.5*inch,3.4*inch]),cap(D.NARR["CAP_SERIES"])]))
E.append(Paragraph(f"Fuente: presupuesto oficial para % Forecast (ex-Institucional/Caobo); venta neta ex-Caobo para tendencia e interanual. Pera con pera = mismo portafolio en ambos años (excluye galletería y marcas descontinuadas del 2025, y Molinos/nuevas del 2026).",SMALL))
E.append(PageBreak())

def bloque_pdf(centro,titulo,provc,gaussc,abcdc,catc,signal):
    io=D.ind_op[centro]; venta=D.serie_2026[MES][0] if centro=="Santo Domingo" else D.serie_2026[MES][1]
    facts=sum(p[3] for p in D.prov_jul[centro]); ticket=venta/facts; c=D.NARR["CAPS"][centro]
    E.append(Paragraph(titulo,H2)); E.append(Paragraph(c["intro"],INTRO))
    ind=[["Indicador","Valor"],[f"Venta neta {MN}",rd(venta)],[f"Productividad / día hábil (÷{D.DIAS_HABILES})",rd(venta/D.DIAS_HABILES)],
         ["Ticket promedio",rd(ticket)],["Clientes activos",f"{io['activos']:,}"],["Cartera (distinta)",f"{io['cartera']:,}"],
         ["Activación de cartera",pct(io['activacion'])],["Efectividad de visita",pct(io['efectividad'])]]
    rowi=Table([[tabla(ind,[2.0*inch,1.2*inch]),Image(CH+"operacion.png",width=3.4*inch,height=3.4*inch*0.57)]],colWidths=[3.3*inch,3.6*inch])
    rowi.setStyle(TableStyle([("VALIGN",(0,0),(-1,-1),"TOP")]))
    E.append(KeepTogether([Paragraph("1 · Indicadores de operación",SUBT),rowi,cap(c["op"])]))
    E.append(KeepTogether([Paragraph("2 · Proveedores (Top 4 + Otros) y categorías",SUBT),Table([[Image(CH+provc,width=3.45*inch,height=3.45*inch*0.5),Image(CH+catc,width=3.35*inch,height=3.35*inch*0.61)]],colWidths=[3.5*inch,3.4*inch]),cap(c["prov"])]))
    E.append(KeepTogether([Paragraph("3 · Clientes A·B·C·D y campana de cumplimiento de cuota",SUBT),Table([[Image(CH+abcdc,width=3.35*inch,height=3.35*inch*0.55),Image(CH+gaussc,width=3.45*inch,height=3.45*inch*0.55)]],colWidths=[3.4*inch,3.5*inch]),cap(c["abcd"])]+([signal] if signal else [])))

sig_sdo=Paragraph(tok(D.NARR["SIG_SDO"]),st("s",8.5,colors.HexColor('#8A6D00'),True,space=2))
bloque_pdf("Santo Domingo","Bloque A · Santo Domingo (SDO) — una fuerza de venta","prov_sdo.png","gauss_sdo.png","abcd_sdo.png","cat_sdo.png",sig_sdo)
E.append(PageBreak())
sig_azua=Paragraph(tok(D.NARR["SIG_AZUA"]),st("s",8.5,colors.HexColor('#8A6D00'),True,space=2))
bloque_pdf("Azua","Bloque B · CEDIS Sur / Azua — dos fuerzas de venta (FFVV 1 y FFVV 2) + Mayorista","prov_azua.png","gauss_azua.png","abcd_azua.png","cat_azua.png",None)
grp_tot={}
for v in D.vendedores:
    if v[1]=="Azua": grp_tot.setdefault(v[2],[0,0]); grp_tot[v[2]][0]+=1; grp_tot[v[2]][1]+=v[5]
tot_az=sum(g[1] for g in grp_tot.values())
ff=[["Fuerza de venta","N° vend.",f"Ejecutado {MA}","% del centro"]]+[[lbl,str(grp_tot[g][0]),rd(grp_tot[g][1]),pct(grp_tot[g][1]/tot_az)] for g,lbl in D.FFVV_AZUA]
E.append(KeepTogether([Paragraph("4 · Apertura por fuerza de venta (FFVV 1 / FFVV 2 / Mayorista)",SUBT),tabla(ff,[2.6*inch,0.9*inch,1.5*inch,1.0*inch]),cap(D.NARR["FFVV_CAP"]),sig_azua]))
E.append(PageBreak())

# COLOMBINA + CIERRE
E.append(Paragraph("Apéndice · Colombina — Top 5 SKU por CEDIS",H2))
E.append(Paragraph(D.NARR["COLO_INTRO"],INTRO))
E.append(Image(CH+"colombina.png",width=6.6*inch,height=6.6*inch*0.38))
col=[["CEDIS","Top 5 (RD$)","Colombina total","Top5 % del prov."],
     ["Santo Domingo",rd(sum(x[1] for x in D.colombina_sdo)),rd(D.colombina_total_sdo),pct(sum(x[1] for x in D.colombina_sdo)/D.colombina_total_sdo)],
     ["Azua",rd(sum(x[1] for x in D.colombina_azua)),rd(D.colombina_total_azua),pct(sum(x[1] for x in D.colombina_azua)/D.colombina_total_azua)]]
E.append(Spacer(1,0.05*inch)); E.append(tabla(col,[1.6*inch,1.5*inch,1.5*inch,1.4*inch])); E.append(cap(D.NARR["COLO_CAP"]))
E.append(Spacer(1,0.12*inch))
E.append(Paragraph("Cierre · Conclusiones y recomendaciones accionables",H2))
E.append(Paragraph("Las señales de las capas anteriores, traducidas a acción: qué pasó, dónde, qué hacer, con dueño y fecha.",INTRO))
def W(t): return Paragraph(tok(t),WRAP)
acc=[["Señal","Qué pasó / Dónde","Qué hacer","Resp.","Fecha"]]+[[W(a),W(b),W(c),W(d),W(e)] for a,b,c,d,e in D.NARR["CIERRE"]]
E.append(tabla(acc,[0.9*inch,2.45*inch,2.3*inch,0.85*inch,0.55*inch],font=7.5)); E.append(Spacer(1,0.1*inch))
if getattr(D,"caobo_prov",None):
    E.append(PageBreak())
    E.append(Paragraph("Apéndice · Ventas a Inversiones Caobo (intercompañía) por suplidor",H2))
    E.append(Paragraph(D.NARR.get("CAOBO_INTRO",""),INTRO))
    _cp=sorted(D.caobo_prov,key=lambda r:-r[1]); _tn=sum(r[1] for r in _cp); _ti=sum(r[2] for r in _cp); _t25=sum(r[3] for r in _cp)
    _rows=[["Suplidor","Neto 2026","Con ITBIS","Neto 2025","Δ% interan."]]
    for nm,n,i,n25 in _cp:
        _d=(n/n25-1) if n25 else None
        _rows.append([nm,rd(n),rd(i),rd(n25),(f"{_d*100:+.1f}%" if _d is not None else "—")])
    _dT=(_tn/_t25-1) if _t25 else None
    _rows.append(["TOTAL",rd(_tn),rd(_ti),rd(_t25),(f"{_dT*100:+.1f}%" if _dT is not None else "—")])
    _t=tabla(_rows,[1.9*inch,1.3*inch,1.3*inch,1.3*inch,1.0*inch])
    _t.setStyle(TableStyle([("BACKGROUND",(0,-1),(-1,-1),AZCLARO),("FONTNAME",(0,-1),(-1,-1),"Helvetica-Bold")]))
    E.append(_t); E.append(cap(D.NARR.get("CAOBO_CAP","")))
    try: E.append(Image(CH+"caobo.png",width=5.6*inch,height=5.6*inch*0.45))
    except Exception as e: print("caobo img",e)
    E.append(Spacer(1,0.12*inch))
E.append(Paragraph("Transformando mientras operamos.  ·  AM Comercial — Confidencial",st("s",8,GRIS,align=TA_CENTER)))
doc.build(E); print("PDF motor generado:",MN)
