# -*- coding: utf-8 -*-
"""Esquema ejecutivo para reunion con Cristiano Perez (canal mayorista).
Fuente: MovilBusiness5STD | Periodo: marzo-agosto 2026."""
import pandas as pd, numpy as np, json
from pathlib import Path
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE

SC = Path(__file__).parent
IMG = SC / "img"
BASE = Path(r"C:\Personales RM\Claude")
DEST = BASE / "Reunion_Cristiano_Perez_Mayorista_Sep2026.pptx"
LOGO = SC / "logo_a.png"

AZUL = RGBColor(0x20, 0x3F, 0x99); VERDE = RGBColor(0x0C, 0x94, 0x44)
GRIS = RGBColor(0x3A, 0x3A, 0x3A); GRIS2 = RGBColor(0x6E, 0x6E, 0x6E)
FONDO = RGBColor(0xF2, 0xF5, 0xF3); VCLARO = RGBColor(0xE8, 0xF5, 0xEC)
BLANCO = RGBColor(0xFF, 0xFF, 0xFF); ROJO = RGBColor(0xC0, 0x39, 0x2B)
LINEA = RGBColor(0xD5, 0xDA, 0xD7); AMBAR = RGBColor(0xE6, 0x7E, 0x22)
RCLARO = RGBColor(0xFA, 0xE5, 0xE3); ACLARO = RGBColor(0xFD, 0xF2, 0xE4)
GCLARO = RGBColor(0xE3, 0xF3, 0xE9)

prs = Presentation(); prs.slide_width = Inches(13.333); prs.slide_height = Inches(7.5)
BLANK = prs.slide_layouts[6]


def tb(slide, l, t, w, h, text, size=11, bold=False, color=GRIS,
       align=PP_ALIGN.LEFT, space=2, anchor=MSO_ANCHOR.TOP, italic=False):
    s = slide.shapes.add_textbox(Inches(l), Inches(t), Inches(w), Inches(h))
    f = s.text_frame
    f.word_wrap = True
    f.margin_left = 0; f.margin_right = 0; f.margin_top = 0; f.margin_bottom = 0
    f.vertical_anchor = anchor
    for i, ln in enumerate(text.split("\n")):
        p = f.paragraphs[0] if i == 0 else f.add_paragraph()
        p.alignment = align; p.space_after = Pt(space)
        r = p.add_run(); r.text = ln
        r.font.size = Pt(size); r.font.bold = bold; r.font.italic = italic
        r.font.color.rgb = color; r.font.name = "Calibri"
    return s


def rect(slide, l, t, w, h, fill=None, line=None, radius=None):
    shp = slide.shapes.add_shape(
        MSO_SHAPE.ROUNDED_RECTANGLE if radius else MSO_SHAPE.RECTANGLE,
        Inches(l), Inches(t), Inches(w), Inches(h))
    if radius is not None:
        try: shp.adjustments[0] = radius
        except Exception: pass
    if fill is None: shp.fill.background()
    else:
        shp.fill.solid(); shp.fill.fore_color.rgb = fill
    if line is None: shp.line.fill.background()
    else:
        shp.line.color.rgb = line; shp.line.width = Pt(0.75)
    shp.shadow.inherit = False
    if shp.has_text_frame: shp.text_frame.text = ""
    return shp


def head(slide, titulo, bajada, num):
    tb(slide, 0.5, 0.30, 10.4, 0.55, titulo, size=21, bold=True, color=AZUL)
    if LOGO.exists():
        slide.shapes.add_picture(str(LOGO), Inches(11.62), Inches(0.32), height=Inches(0.46))
    rect(slide, 0.5, 0.92, 12.35, 0.045, fill=AZUL)
    rect(slide, 0.5, 0.92, 1.6, 0.045, fill=VERDE)
    tb(slide, 0.5, 1.03, 12.4, 0.36, bajada, size=10.5, color=GRIS2)
    rect(slide, 0.5, 7.12, 12.35, 0.012, fill=LINEA)
    tb(slide, 0.5, 7.20, 9.0, 0.28,
       "AM Comercial \u00b7 Confidencial \u00b7 Fuente: MovilBusiness \u00b7 Periodo: marzo\u2013agosto 2026",
       size=8, color=GRIS2)
    tb(slide, 11.8, 7.20, 1.05, 0.28, str(num), size=8, color=GRIS2, align=PP_ALIGN.RIGHT)


def lectura(slide, txt, top=6.55, color=VCLARO, barra=VERDE, size=10.5):
    rect(slide, 0.5, top, 12.35, 0.48, fill=color)
    rect(slide, 0.5, top, 0.06, 0.48, fill=barra)
    tb(slide, 0.72, top + 0.08, 12.0, 0.36, txt, size=size, bold=True, color=GRIS)


def kpi(slide, l, t, w, valor, etiq, color=AZUL, h=0.92):
    rect(slide, l, t, w, h, fill=FONDO)
    rect(slide, l, t, 0.055, h, fill=color)
    tb(slide, l + 0.2, t + 0.10, w - 0.3, 0.42, valor, size=19, bold=True, color=color)
    tb(slide, l + 0.2, t + 0.55, w - 0.3, 0.3, etiq, size=8.5, color=GRIS2)


def tabla(slide, l, t, w, rows, colw, head_fill=AZUL, rowh=0.285, headh=0.44,
          fs=9, hfs=8.5, aligns=None, bold_rows=(), cell_colors=None, fill_rows=None):
    tot = sum(colw); colw = [c * w / tot for c in colw]
    y = t
    for i, row in enumerate(rows):
        h = headh if i == 0 else rowh
        x = l
        if i == 0:
            rect(slide, l, y, w, h, fill=head_fill)
        elif fill_rows and i in fill_rows:
            rect(slide, l, y, w, h, fill=fill_rows[i])
        elif i % 2 == 0:
            rect(slide, l, y, w, h, fill=RGBColor(0xF7, 0xF9, 0xF8))
        for j, cell in enumerate(row):
            if cell_colors and (i, j) in cell_colors:
                rect(slide, x + 0.02, y + 0.02, colw[j] - 0.04, h - 0.04, fill=cell_colors[(i, j)])
            a = 'l' if aligns is None else aligns[j]
            al = PP_ALIGN.LEFT if a == 'l' else (PP_ALIGN.RIGHT if a == 'r' else PP_ALIGN.CENTER)
            nlines = str(cell).count("\n") + 1
            th = 0.22 * nlines
            tb(slide, x + 0.10, y + (h - th) / 2, colw[j] - 0.20, th, str(cell),
               size=hfs if i == 0 else fs, bold=(i == 0 or i in bold_rows),
               color=BLANCO if i == 0 else GRIS, align=al, space=0)
            x += colw[j]
        if i > 0:
            rect(slide, l, y + h, w, 0.008, fill=RGBColor(0xE6, 0xEA, 0xE8))
        y += h
    return y


# ============ DATOS ============
s1 = pd.read_pickle(SC / 'slide1.pkl')
meta = json.load(open(SC / 'slide1.json'))
s3 = pd.read_pickle(SC / 'slide3.pkl')

# ---------------- HOJA 1 ----------------
sl = prs.slides.add_slide(BLANK)
head(sl, "1. Cartera asignada, cobertura de venta e incidencia por suplidor",
     "Canal Mayorista \u2013 Cristiano P\u00e9rez (S12 + S13) \u00b7 4 vendedores activos \u00b7 clientes activos asignados vs. clientes con al menos una factura en los \u00faltimos 6 meses.", 1)
kpi(sl, 0.5, 1.45, 2.95, "763", "Clientes activos asignados a los 4 vendedores", AZUL)
kpi(sl, 3.62, 1.45, 2.95, "540   (71%)", "Clientes con compra en 6 meses", VERDE)
kpi(sl, 6.74, 1.45, 2.95, "223", "Clientes SIN una sola compra en 6 meses", ROJO)
kpi(sl, 9.86, 1.45, 2.99, "RD$ 105.5 MM", "Venta bruta del canal, mar\u2013ago 2026", AZUL)

rows = [["Tipo de negocio", "Clientes\ncartera", "Clientes\ncon compra", "Cobertura\n%", "Venta\nRD$MM",
         "C. Iglesias\n(Unilever)", "Continental", "Brinsa", "Colombina", "Com. Fila", "Dimar"]]
cc = {}
i = 1
for tn in meta['ord']:
    r = s1.loc[tn]
    fila = [tn.title(), int(r.Cartera), int(r.Compraron), "%.0f%%" % r['Cob%'], "%.1f" % r['RD$MM']]
    for j, s in enumerate(meta['sup']):
        val = r[s]
        fila.append("%.0f%%" % val)
        col = 5 + j
        if val == 0: cc[(i, col)] = RCLARO
        elif val < 20: cc[(i, col)] = ACLARO
        elif val >= 45: cc[(i, col)] = GCLARO
    rows.append(fila); i += 1
tot = meta['tot']
rows.append(["TOTAL CANAL", tot[0], tot[1], "%.0f%%" % tot[2], "%.1f" % tot[3]] +
            ["%.0f%%" % x for x in meta['totsup']])
tabla(sl, 0.5, 2.62, 12.35, rows, [2.25, 1.0, 1.05, 0.95, 0.95, 1.15, 1.05, 0.9, 1.0, 1.0, 0.85],
      aligns=['l', 'c', 'c', 'c', 'r', 'c', 'c', 'c', 'c', 'c', 'c'],
      rowh=0.35, headh=0.54, fs=9.5, hfs=8, bold_rows=(len(rows) - 1,), cell_colors=cc,
      fill_rows={len(rows) - 1: RGBColor(0xE9, 0xED, 0xF7)})
tb(sl, 0.5, 6.08, 12.35, 0.28,
   "Incidencia % = clientes que compraron ese suplidor \u00f7 clientes de la cartera de ese tipo de negocio.    Verde \u2265 45%   \u00b7   \u00c1mbar < 20%   \u00b7   Rojo = 0%",
   size=8, color=GRIS2, italic=True)
lectura(sl, "Almacenes y Supermercados son el 54% de la cartera y el 73% de la venta. El hueco no est\u00e1 en visitar: 223 clientes activos (29%) no compraron nada en 6 meses y ning\u00fan suplidor pasa del 50% de incidencia.")

# ---------------- HOJA 2 ----------------
sl = prs.slides.add_slide(BLANK)
head(sl, "2. Visitas mensuales y toma de inventario en el cliente",
     "Cobertura = clientes distintos visitados en el mes \u00f7 cartera asignada.   Efectividad = de los clientes visitados, cu\u00e1ntos terminaron comprando ese mes.", 2)
tb(sl, 0.5, 1.42, 6.1, 0.3, "Cobertura de visitas: % de la cartera visitado cada mes",
   size=11, bold=True, color=AZUL)
sl.shapes.add_picture(str(IMG / "cobertura_visitas.png"), Inches(0.5), Inches(1.72), width=Inches(6.15))
tb(sl, 6.85, 1.42, 6.1, 0.3, "Efectividad: % de clientes visitados que compran",
   size=11, bold=True, color=AZUL)
sl.shapes.add_picture(str(IMG / "efectividad.png"), Inches(6.85), Inches(1.72), width=Inches(6.15))

rows = [["Vendedor", "Cartera", "Visitados\n6 meses", "Sin\nvisitar", "Visitas/d\u00eda\npromedio",
         "Efectividad\npromedio", "Clientes con inventario\nf\u00edsico en 6 meses"],
        ["Wascar Alvarez (106)", 277, 276, 1, "20.5", "56%", "0"],
        ["Hanel Gir\u00f3n (36)", 147, 147, 0, "20.6", "51%", "4"],
        ["Jhonny Rosario (25)", 172, 164, 8, "15.0", "51%", "9"],
        ["Fernando Mart\u00ednez (185) *", 167, 166, 1, "18.3", "45%", "0"],
        ["TOTAL CANAL", 763, 753, 10, "18.6", "52%", "13   de 763 clientes"]]
cc = {(1, 6): RCLARO, (2, 6): ACLARO, (3, 6): ACLARO, (4, 6): RCLARO, (5, 6): RCLARO}
tabla(sl, 0.5, 4.42, 12.35, rows, [2.6, 0.95, 1.15, 0.95, 1.25, 1.25, 2.0],
      aligns=['l', 'c', 'c', 'c', 'c', 'c', 'c'],
      rowh=0.29, headh=0.46, fs=9.5, hfs=8, bold_rows=(5,), cell_colors=cc,
      fill_rows={5: RGBColor(0xE9, 0xED, 0xF7)})
tb(sl, 0.5, 6.30, 12.35, 0.25,
   "* Fernando Mart\u00ednez inicia ruta en junio 2026 (3 meses de historia).      Referencia: en todo AM Comercial la tabla InventariosFisicos s\u00f3lo tiene 55 registros en 6 meses, de 8 vendedores.",
   size=8, color=GRIS2, italic=True)
lectura(sl, "La visita s\u00ed se est\u00e1 haciendo: 753 de 763 clientes visitados y 80\u2013100% de cobertura mensual. Lo que no se hace es la toma de inventario \u2014 13 clientes en 6 meses entre los 4. Sin inventario no hay pedido sugerido y la venta se vuelve reactiva.",
        color=RCLARO, barra=ROJO)

# ---------------- HOJA 3 ----------------
sl = prs.slides.add_slide(BLANK)
head(sl, "3. Clientes Pareto con mayor desviaci\u00f3n en la venta",
     "Pareto = clientes que acumulan el 80% de la venta de cada vendedor.   Desviaci\u00f3n = venta jun\u2013ago contra mar\u2013may, y en cu\u00e1ntos de los 6 meses el cliente compr\u00f3.", 3)
r2 = [["Vendedor", "Cartera", "Clientes\nque compran", "Clientes Pareto\n(80% de la venta)", "% de la\ncartera", "Venta 6M\nRD$MM"],
      ["Jhonny Rosario (25)", 172, 113, 39, "23%", "35.8"],
      ["Hanel Gir\u00f3n (36)", 147, 103, 30, "20%", "33.5"],
      ["Wascar Alvarez (106)", 277, 225, 94, "34%", "35.3"],
      ["Fernando Mart\u00ednez (185)", 167, 99, 44, "26%", "0.9"]]
tabla(sl, 0.5, 1.45, 5.9, r2, [2.0, 0.8, 1.05, 1.3, 0.85, 0.95],
      aligns=['l', 'c', 'c', 'c', 'c', 'r'], rowh=0.32, headh=0.55, fs=9, hfs=7.5)
tb(sl, 6.85, 1.42, 6.1, 0.3, "Venta mensual del canal por vendedor (RD$ MM)",
   size=10.5, bold=True, color=AZUL)
sl.shapes.add_picture(str(IMG / "venta_mensual.png"), Inches(6.85), Inches(1.68), width=Inches(5.95))

tb(sl, 0.5, 3.74, 12.35, 0.3,
   "Los 3 clientes Pareto de cada vendedor con la mayor ca\u00edda de jun\u2013ago frente a mar\u2013may",
   size=11.5, bold=True, color=AZUL)
rows = [["Vendedor", "Cliente", "Venta 6M\nRD$", "Promedio\nmes RD$", "Meses con\ncompra (de 6)",
         "Variaci\u00f3n\nRD$", "Var.\n%"]]
cc = {}
i = 1
for _, r in s3.iterrows():
    rows.append([r.Rep, r.Cliente, "{:,.0f}".format(r.Total), "{:,.0f}".format(r.Prom),
                 int(r.MesesConCompra), "{:,.0f}".format(r.Var_RD), "%.0f%%" % r['Var%']])
    cc[(i, 5)] = RCLARO; cc[(i, 6)] = RCLARO
    if r.MesesConCompra <= 3: cc[(i, 4)] = ACLARO
    i += 1
tabla(sl, 0.5, 4.06, 12.35, rows, [1.85, 3.3, 1.35, 1.35, 1.4, 1.35, 0.8],
      aligns=['l', 'l', 'r', 'r', 'c', 'r', 'r'], rowh=0.235, headh=0.44,
      fs=9, hfs=7.5, cell_colors=cc)
lectura(sl, "Estos 9 clientes solos explican RD$ 3.8 MM menos de venta en el \u00faltimo trimestre. Jhonny concentra el 80% de RD$ 35.8 MM en 39 clientes: cada ca\u00edda pesa. Wascar tiene el patr\u00f3n contrario \u2014 clientes que compran 2 o 3 meses de 6 y desaparecen.")

# ---------------- HOJA 4 ----------------
sl = prs.slides.add_slide(BLANK)
head(sl, "4. Las cinco oportunidades para trabajar hoy con Cristiano",
     "Ordenadas por tama\u00f1o del premio. Cada una con n\u00famero de clientes, valor estimado y una acci\u00f3n medible en la pr\u00f3xima reuni\u00f3n.", 4)
tb(sl, 0.5, 1.40, 6.1, 0.3, "La venta sigue concentrada en la \u00faltima semana del mes",
   size=10.5, bold=True, color=AZUL)
sl.shapes.add_picture(str(IMG / "semanas.png"), Inches(0.5), Inches(1.70), width=Inches(5.75))
rect(sl, 0.5, 4.58, 5.9, 1.90, fill=FONDO)
rect(sl, 0.5, 4.58, 0.055, 1.90, fill=ROJO)
tb(sl, 0.75, 4.74, 5.45, 1.60,
   "51% de la venta entra del 22 al fin de mes y s\u00f3lo 8% en la primera semana.\n"
   "A 5 d\u00edas del cierre todav\u00eda falta la mitad del mes: por eso no se sabe si se llega.\n"
   "\n"
   "Semana 1 + 2 = 29% de la venta del canal.   Meta propuesta: 45%.\n"
   "\n"
   "Es el problema de fondo detr\u00e1s de las otras cuatro oportunidades: sin inventario\n"
   "tomado en el cliente no hay pedido sugerido temprano, y sin pedido temprano la\n"
   "reactivaci\u00f3n y la venta cruzada no caben en la agenda de la \u00faltima semana.",
   size=9.5, color=GRIS, space=2)

ops = [("1", "Reactivar los 223 clientes activos sin compra",
        "Visitados durante 6 meses y nunca facturaron: 77 almacenes, 48 colmados, 25 supermercados. Convertir 1 de cada 4 a la venta mediana = RD$ 4.4 MM.", VERDE),
       ("2", "Venta cruzada Continental y Brinsa en la cartera Unilever",
        "De los 163 clientes que compran C\u00e9sar Iglesias con Jhonny y Hanel, 97 no compran Continental y 127 no compran Brinsa. Potencial: RD$ 9.8 MM y RD$ 6.9 MM.", VERDE),
       ("3", "Adelantar la venta a la 1\u00aa y 2\u00aa semana del mes",
        "Meta: llevar Sem 1+2 de 29% a 45% de la venta. Requiere pedido sugerido con inventario en el cliente y foco de cobranza temprana.", AMBAR),
       ("4", "Recuperar los clientes Pareto que se est\u00e1n cayendo",
        "9 clientes = RD$ 3.8 MM menos en el trimestre. Visita del supervisor junto al vendedor a los 3 de cada ruta antes de cerrar septiembre.", AMBAR),
       ("5", "Colombina en la cartera de Wascar y Fernando",
        "166 clientes del centro 01 compran Continental pero no Colombina. Es el suplidor con m\u00e1s recorrido en almacenes y colmados: RD$ 3.5 MM potenciales.", AZUL)]
y = 1.42
for num, tit, txt, col in ops:
    rect(sl, 6.85, y, 6.0, 0.96, fill=FONDO)
    rect(sl, 6.85, y, 0.055, 0.96, fill=col)
    rect(sl, 7.02, y + 0.14, 0.34, 0.34, fill=col, radius=0.5)
    tb(sl, 7.02, y + 0.21, 0.34, 0.24, num, size=10.5, bold=True, color=BLANCO, align=PP_ALIGN.CENTER)
    tb(sl, 7.48, y + 0.09, 5.27, 0.28, tit, size=10.5, bold=True, color=col)
    tb(sl, 7.48, y + 0.37, 5.27, 0.56, txt, size=8.3, color=GRIS, space=0)
    y += 1.02

lectura(sl, "Compromiso de la reuni\u00f3n: cerrar septiembre con Sem 1+2 \u2265 40% de la venta, 60 clientes reactivados de los 223 y toma de inventario en los 30 clientes Pareto de cada ruta.",
        top=6.55, color=VCLARO, barra=VERDE)

prs.save(str(DEST))
print("OK ->", DEST)
