# -*- coding: utf-8 -*-
import openpyxl
from openpyxl.styles import Font,PatternFill,Alignment,Border,Side
from openpyxl.drawing.image import Image as XLImage
from openpyxl.utils import get_column_letter
import os,data as D
_B=os.path.dirname(os.path.abspath(__file__))

VERDE="0C9444"; AZUL="203F99"; GRIS="6E6E6E"; CARBON="3A3A3A"; CLARO="EAF3EC"; AZCLARO="E7EBF5"
WHITE="FFFFFF"
M=1_000_000; DIAS=D.DIAS_HABILES_JUL
RD='"RD$"#,##0;("RD$"#,##0);-'
PCT='0.0%'
NUM='#,##0'
OUT=os.path.dirname(_B)+"/"
CH=_B+"/charts/"

def sem(p):
    return "🟢" if p>=1.0 else ("🟡" if p>=0.85 else "🔴")

wb=openpyxl.Workbook()
thin=Side(style="thin",color="D9D9D9")
border=Border(left=thin,right=thin,top=thin,bottom=thin)
def A(f="Arial",sz=10,b=False,color="000000"): return Font(name=f,size=sz,bold=b,color=color)
def fill(c): return PatternFill("solid",fgColor=c)
def hdr(cell,txt,bg=VERDE,fg=WHITE,sz=10):
    cell.value=txt; cell.font=A(sz=sz,b=True,color=fg); cell.fill=fill(bg)
    cell.alignment=Alignment(horizontal="center",vertical="center",wrap_text=True); cell.border=border
def cval(cell,v,fmt=None,b=False,color="000000",align="right",bg=None):
    cell.value=v; cell.font=A(b=b,color=color)
    if fmt:cell.number_format=fmt
    cell.alignment=Alignment(horizontal=align,vertical="center"); cell.border=border
    if bg:cell.fill=fill(bg)

# precompute totals
sdo=[D.serie_2026[m][0] for m in range(1,D.MES+1)]; azua=[D.serie_2026[m][1] for m in range(1,D.MES+1)]
tot=[a+b for a,b in zip(sdo,azua)]
sdo25=[D.serie_2025[m][0] for m in range(1,D.MES+1)]; azua25=[D.serie_2025[m][1] for m in range(1,D.MES+1)]
presu={c:(D.presu_jul[(c,"Mayorista")][0]+D.presu_jul[(c,"Minorista")][0]) for c in ["Santo Domingo","Azua"]}
ejec ={c:(D.presu_jul[(c,"Mayorista")][1]+D.presu_jul[(c,"Minorista")][1]) for c in ["Santo Domingo","Azua"]}
presu_t=sum(presu.values()); ejec_t=sum(ejec.values())
L=D.MES-1; exmol=D.LFL['yoy_total']
MESES_ABBR=["Ene","Feb","Mar","Abr","May","Jun","Jul","Ago","Sep","Oct","Nov","Dic"]
act={c:sum(D.abcd[c][D.MES][s][1] for s in ["A","B","C","D","SC"]) for c in ["Santo Domingo","Azua"]}

# ============ HOJA 1: RESUMEN ============
ws=wb.active; ws.title="Resumen"
ws.sheet_view.showGridLines=False
for col,w in zip("ABCDEFGHI",[3,26,15,15,13,13,10,12,12]): ws.column_dimensions[col].width=w
try:
    logo=XLImage(_B+"/logo_am_comercial.png")
    logo.height=48; logo.width=int(48*logo.width/logo.height); ws.add_image(logo,"B1")
except Exception as e: print("logo",e)
ws.row_dimensions[1].height=42
ws["E1"].value="INFORME EJECUTIVO DE CIERRE DE VENTAS"; ws["E1"].font=A(sz=14,b=True,color=CARBON)
ws["E2"].value=f"{D.MES_NOMBRE} 2026 · Presentación a Presidencia"; ws["E2"].font=A(sz=11,color=GRIS)
ws["E3"].value=f"Fuente: Movil Business (VWVENTASRESUMEN·VWPresupuestos) · Cálculo: {D.FECHA_CALC} · Base: venta NETA sin ITBIS · Excluye Inversiones Caobo"; ws["E3"].font=A(sz=8,color=GRIS)

# KPI table (directo, sin recuadro de titular)
r=6
hdrs=["Vista",f"Neto {D.MES_ABBR} 2026",f"Forecast {D.MES_ABBR}","% Forecast","Sem.","Δ% Interanual","Sem."]
for j,h in enumerate(hdrs): hdr(ws.cell(r,2+j),h)
rows=[("Total AM",tot[L],presu_t,ejec_t/presu_t,(tot[L]/(sdo25[L]+azua25[L])-1)),
      ("Santo Domingo",sdo[L],presu["Santo Domingo"],ejec["Santo Domingo"]/presu["Santo Domingo"],(sdo[L]/sdo25[L]-1)),
      ("Azua (Sur)",azua[L],presu["Azua"],ejec["Azua"]/presu["Azua"],(azua[L]/azua25[L]-1))]
# use ejecutado del presupuesto para %forecast (fuente oficial)
rows=[("Total AM",ejec_t,presu_t,ejec_t/presu_t,(tot[L]/(sdo25[L]+azua25[L])-1)),
      ("Santo Domingo",ejec["Santo Domingo"],presu["Santo Domingo"],ejec["Santo Domingo"]/presu["Santo Domingo"],(sdo[L]/sdo25[L]-1)),
      ("Azua (Sur)",ejec["Azua"],presu["Azua"],ejec["Azua"]/presu["Azua"],(azua[L]/azua25[L]-1))]
for i,(nm,ne,pf,pc,yy) in enumerate(rows):
    rr=r+1+i; bg=AZCLARO if i==0 else None
    cval(ws.cell(rr,2),nm,b=(i==0),align="left",bg=bg)
    cval(ws.cell(rr,3),ne,RD,bg=bg); cval(ws.cell(rr,4),pf,RD,bg=bg)
    cval(ws.cell(rr,5),pc,PCT,bg=bg); cval(ws.cell(rr,6),sem(pc),align="center",bg=bg)
    cval(ws.cell(rr,7),yy,PCT,color=(VERDE if yy>=0 else "C0392B"),bg=bg)
    cval(ws.cell(rr,8),("🟢" if yy>=0 else "🔴"),align="center",bg=bg)
r=r+len(rows)+2
ws.cell(r,2).value="Nota: % Forecast usa el Ejecutado oficial del presupuesto (ex-Institucional/Caobo). Δ% Interanual sobre venta neta ex-Caobo. Base pera-con-pera: {:+.1f}% interanual (mismo portafolio; excluye galletería y marcas descontinuadas del 2025, y Molinos/nuevas del 2026).".format(exmol*100)+""
ws.cell(r,2).font=A(sz=8,color=GRIS); ws.merge_cells(start_row=r,start_column=2,end_row=r,end_column=9)

# charts
r+=2
for img,pos in [("capa1_bridge.png","B"+str(r)),("capa1_series.png","B"+str(r+22)),("capa1_forecast.png","B"+str(r+44)),("capa1_yoy.png","F"+str(r+44))]:
    try:
        im=XLImage(CH+img); im.width=int(im.width*0.62); im.height=int(im.height*0.62); ws.add_image(im,pos)
    except Exception as e:print(img,e)

# ============ HOJA 2: CAPA 1 ============
ws=wb.create_sheet("Capa 1 - General"); ws.sheet_view.showGridLines=False
for col,w in zip("ABCDEFGHIJKLMN",[3,14]+[11]*12): ws.column_dimensions[col].width=w
ws["B1"].value="CAPA 1 · Plataforma general — Serie mensual neta (RD$, ex-Caobo)"; ws["B1"].font=A(sz=12,b=True,color=CARBON)
ws["B2"].value="Qué muestra: el pulso del negocio total y por CEDIS. La serie 2026 vs 2025 con su PM, y el cumplimiento vs forecast por canal. Recuerde: el +17% reportado es portafolio (Molinos entra, galletería/marcas salen); la base pera-con-pera cae {:+.1f}%.".format(exmol*100)+""; ws["B2"].font=A(sz=9,color=GRIS); ws["B2"].alignment=Alignment(wrap_text=False)
r=3; meses=MESES_ABBR[:D.MES]; PMCOL=3+D.MES
hdr(ws.cell(r,2),"Serie / Mes",bg=VERDE)
for j,mm in enumerate(meses): hdr(ws.cell(r,3+j),mm)
hdr(ws.cell(r,PMCOL),f"PM {D.MES_ABBR} (3m)")
series=[("Total AM 2026",tot),("Santo Domingo 2026",sdo),("Azua 2026",azua),
        ("Total AM 2025",[a+b for a,b in zip(sdo25,azua25)]),("Santo Domingo 2025",sdo25),("Azua 2025",azua25)]
for i,(nm,vals) in enumerate(series):
    rr=r+1+i; cval(ws.cell(rr,2),nm,align="left",b=("Total" in nm))
    for j,v in enumerate(vals): cval(ws.cell(rr,3+j),v,RD)
    pm=f"=AVERAGE({get_column_letter(D.MES)}{rr}:{get_column_letter(D.MES+2)}{rr})"
    cval(ws.cell(rr,PMCOL),pm,RD,b=True)
r=r+len(series)+2
ws.cell(r,2).value=f"Forecast {D.MES_NOMBRE} 2026 por centro y canal (RD$)"; ws.cell(r,2).font=A(sz=11,b=True,color=CARBON); r+=1
for j,h in enumerate(["Centro","Canal","Presupuesto","Ejecutado","% Cumpl.","Sem."]): hdr(ws.cell(r,2+j),h)
canal_rows=[("Santo Domingo","Minorista"),("Santo Domingo","Mayorista"),("Santo Domingo","Institucional (Caobo)*"),
            ("Azua","Minorista"),("Azua","Mayorista")]
data_map={("Santo Domingo","Minorista"):D.presu_jul[("Santo Domingo","Minorista")],
          ("Santo Domingo","Mayorista"):D.presu_jul[("Santo Domingo","Mayorista")],
          ("Santo Domingo","Institucional (Caobo)*"):D.presu_jul[("Santo Domingo","Institucional")],
          ("Azua","Minorista"):D.presu_jul[("Azua","Minorista")],
          ("Azua","Mayorista"):D.presu_jul[("Azua","Mayorista")]}
r0=r+1
for i,key in enumerate(canal_rows):
    rr=r0+i; p,e=data_map[key]; pc=e/p
    cval(ws.cell(rr,2),key[0],align="left"); cval(ws.cell(rr,3),key[1],align="left")
    cval(ws.cell(rr,4),p,RD); cval(ws.cell(rr,5),e,RD)
    cval(ws.cell(rr,6),f"=E{rr}/D{rr}",PCT); cval(ws.cell(rr,7),sem(pc),align="center")
rr=r0+len(canal_rows)
cval(ws.cell(rr,2),"TOTAL (ex-Caobo)",align="left",b=True,bg=AZCLARO); cval(ws.cell(rr,3),"",bg=AZCLARO)
cval(ws.cell(rr,4),f"=D{r0}+D{r0+1}+D{r0+3}+D{r0+4}",RD,b=True,bg=AZCLARO)
cval(ws.cell(rr,5),f"=E{r0}+E{r0+1}+E{r0+3}+E{r0+4}",RD,b=True,bg=AZCLARO)
cval(ws.cell(rr,6),f"=E{rr}/D{rr}",PCT,b=True,bg=AZCLARO); cval(ws.cell(rr,7),f'=IF(F{rr}>=1,"🟢",IF(F{rr}>=0.85,"🟡","🔴"))',align="center",bg=AZCLARO)
cval(ws.cell(rr+2,2),"* Institucional = Inversiones Caobo (aliado): se EXCLUYE del doble comparativo por regla del guión.",align="left",color=GRIS)
ws.cell(rr+2,2).font=A(sz=8,color=GRIS)

print("Hojas 1-2 OK")
wb.save(OUT+f"AM_Cierre_{D.MES_NOMBRE}2026.xlsx")
print("saved partial")
