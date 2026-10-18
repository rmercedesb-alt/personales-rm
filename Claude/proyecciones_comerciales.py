"""
Proyecciones Comerciales - AM Comercial
Genera PDF con avances y proyecciones de ventas vs presupuesto
Ejecutar diariamente a las 6:00 AM
"""

import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
import matplotlib.image as mpimg
import numpy as np
from matplotlib.backends.backend_pdf import PdfPages
from datetime import date
import os, sys

# ─── CONFIGURACION ────────────────────────────────────────────────
RUTA_EXCEL = r"C:\Personales RM\Claude\Tabla de Presupuestos Claude Mes.xlsx"
HOJA       = "DATA MES"
CORREOS    = ["r.mercedesb@gmail.com", "rmercedes@amcomercial.com"]
REMITENTE  = "rmercedes@amcomercial.com"
RUTA_LOGO  = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logo_am_comercial.png")

hoy      = date.today()
ANIO_HOY = hoy.year
MES_HOY  = hoy.month
MESES_ES = {1:"Enero",2:"Febrero",3:"Marzo",4:"Abril",5:"Mayo",6:"Junio",
            7:"Julio",8:"Agosto",9:"Septiembre",10:"Octubre",11:"Noviembre",12:"Diciembre"}
NOMBRE_PDF = f"Proyecciones Comerciales a {hoy.strftime('%d-%m-%Y')}.pdf"
RUTA_PDF   = os.path.join(os.path.dirname(RUTA_EXCEL), NOMBRE_PDF)

# ─── COLORES ──────────────────────────────────────────────────────
VERDE   = "#27ae60"
AMARILLO= "#f39c12"
ROJO    = "#e74c3c"
AZUL    = "#2980b9"
OSCURO  = "#2c3e50"
GRIS    = "#888888"

def color_semaforo(pct):
    if pct is None or (isinstance(pct, float) and np.isnan(pct)): return "#cccccc"
    if pct < 0.80:   return ROJO
    elif pct < 0.90: return AMARILLO
    return VERDE

def fmt_pct(v):
    if v is None or (isinstance(v, float) and np.isnan(v)): return "—"
    if abs(v) > 10: return f"{v:.0%}"
    return f"{v:.1%}"

def fmt_mon(v):
    if v is None or (isinstance(v, float) and np.isnan(v)): return "—"
    if abs(v) >= 1_000_000_000: return f"RD$ {v/1_000_000_000:.1f}B"
    if abs(v) >= 1_000_000:     return f"RD$ {v/1_000_000:.1f}MM"
    return f"RD$ {v:,.0f}"

def fmt_mon_diario(v):
    if v is None or (isinstance(v, float) and np.isnan(v)): return "—"
    if abs(v) >= 1_000_000: return f"RD$ {v/1_000_000:.2f}MM/día"
    return f"RD$ {v:,.0f}/día"

# ─── VALIDAR Y FILTRAR ────────────────────────────────────────────
def validar_y_filtrar(df):
    df["PreAnio"] = pd.to_numeric(df["PreAnio"], errors="coerce")
    df["PreMes"]  = pd.to_numeric(df["PreMes"],  errors="coerce")
    df_mes = df[(df["PreAnio"] == ANIO_HOY) & (df["PreMes"] == MES_HOY)].copy()
    if df_mes.empty:
        print(f"ERROR: No hay datos para {MESES_ES[MES_HOY]} {ANIO_HOY}.")
        sys.exit(1)
    print(f"OK Datos: {MESES_ES[MES_HOY]} {ANIO_HOY} - {len(df_mes):,} registros")
    return df_mes

# ─── CALCULOS ─────────────────────────────────────────────────────
def calcular_grupo(df, col, dias_mes, dias_trans):
    g = df.groupby(col).agg(
        presupuesto_mes=("PrePresupuestoMonto",    "sum"),
        presupuesto_dia=("PresupueestoMontoaldia", "sum"),
        ejecutado      =("PreEjecutadoMonto",       "sum"),
        proyectado     =("Proyectado",              "sum"),
    ).reset_index()
    g["logro_dia"]     = g["ejecutado"] / g["presupuesto_dia"].replace(0, np.nan)
    g["dif_proyectada"]= g["presupuesto_mes"] - g["proyectado"]
    dias_rest = max(dias_mes - dias_trans, 1)
    g["pres_diario_faltante"] = (g["presupuesto_mes"] - g["ejecutado"]) / dias_rest
    return g.sort_values("presupuesto_mes", ascending=False)

def calcular_general(df, dias_mes, dias_trans):
    pm  = df["PrePresupuestoMonto"].sum()
    pdi = df["PresupueestoMontoaldia"].sum()
    ej  = df["PreEjecutadoMonto"].sum()
    pr  = df["Proyectado"].sum()
    dias_rest = max(dias_mes - dias_trans, 1)
    return {
        "presupuesto_mes"     : pm,
        "presupuesto_dia"     : pdi,
        "ejecutado"           : ej,
        "proyectado"          : pr,
        "logro_mes"           : ej/pm  if pm  > 0 else 0,
        "logro_dia"           : ej/pdi if pdi > 0 else 0,
        "dif_proyectada"      : pm - pr,
        "pres_diario_faltante": (pm - ej) / dias_rest,
        "valor_diario"        : pdi / dias_trans if dias_trans > 0 else 0,
        "dias_mes"            : dias_mes,
        "dias_trans"          : dias_trans,
    }

# ─── CABECERA CON LOGO ────────────────────────────────────────────
def cabecera(fig, titulo):
    # Logo
    if os.path.exists(RUTA_LOGO):
        try:
            logo = mpimg.imread(RUTA_LOGO)
            ax_logo = fig.add_axes([0.02, 0.91, 0.10, 0.07])
            ax_logo.imshow(logo)
            ax_logo.axis("off")
        except: pass
    fig.text(0.55, 0.965, "AM COMERCIAL — PROYECCIONES COMERCIALES",
             ha="center", fontsize=8, color=GRIS)
    fig.text(0.55, 0.940, titulo,
             ha="center", fontsize=13, fontweight="bold", color=OSCURO)
    fig.text(0.55, 0.918, f"Al {hoy.day} de {MESES_ES[MES_HOY]} de {ANIO_HOY}",
             ha="center", fontsize=9, color="#555555")

# ─── CONSEJO DEL DIA ──────────────────────────────────────────────
CONSEJOS = [
    "El seguimiento diario de las ventas no es solo una práctica operativa — es el hábito que separa a los equipos que reaccionan de los que anticipan. Cuando revisas tus números cada mañana, no estás mirando el pasado; estás construyendo la capacidad de predecir el futuro. Un equipo que conoce sus métricas en tiempo real tiene una ventaja competitiva que ningún presupuesto puede comprar.",
    "La diferencia entre un logro del 79% y uno del 91% no siempre es trabajo — muchas veces es información. El vendedor que sabe exactamente cuánto le falta para llegar a su meta actúa diferente al que solo siente que 'le falta algo'. Dale a tu equipo claridad numérica y verás cómo la motivación se convierte en acción concreta.",
    "Un presupuesto no cumplido no es un fracaso — es una señal. La pregunta correcta no es '¿por qué no llegamos?' sino '¿qué nos dice este número sobre el mercado, el cliente y nuestra ejecución?' Los mejores gerentes comerciales convierten cada desviación en inteligencia de negocio.",
    "La proyección es la brújula del mes. No te dice dónde estás — te dice dónde vas a terminar si sigues igual. Si tu proyectado está por debajo del presupuesto, tienes dos opciones: cambiar la ejecución o ajustar la expectativa. Ambas requieren honestidad, pero solo una construye resultados.",
    "El presupuesto diario faltante es el número más accionable de tu reporte. No es lo que debes vender en el mes — es lo que debes vender mañana. Cuando tu equipo entiende ese número, deja de pensar en meses y empieza a pensar en días. Y los meses se construyen un día a la vez.",
    "La rentabilidad no se mejora vendiendo más — se mejora vendiendo mejor. Revisar el mix de suplidores, el canal y el perfil de cliente es tan importante como revisar el total. Un equipo que vende mucho en categorías de bajo margen puede estar trabajando el doble para llegar a la misma utilidad.",
    "El mejor momento para corregir el rumbo es cuando todavía hay días del mes. Un logro del 75% al día 12 es una oportunidad — al día 28, es una historia. Los reportes diarios existen para darte tiempo de reaccionar, no para documentar lo que ya no se puede cambiar.",
]

def consejo_del_dia(fig, y_inicio=0.03):
    consejo = CONSEJOS[hoy.day % len(CONSEJOS)]
    ax = fig.add_axes([0.04, y_inicio, 0.92, 0.10])
    ax.set_xlim(0,1); ax.set_ylim(0,1); ax.axis("off")
    ax.add_patch(plt.Rectangle((0,0),1,1,
                 facecolor="#eaf3fb", edgecolor="#aed6f1", lw=0.8, zorder=0))
    ax.text(0.01, 0.85, "💡 Consejo del día", fontsize=7.5,
            fontweight="bold", color=AZUL, va="top")
    ax.text(0.01, 0.65, consejo, fontsize=6.8, color="#2c3e50",
            va="top", wrap=True,
            multialignment="left",
            transform=ax.transAxes)

# ─── TARJETAS RESUMEN ─────────────────────────────────────────────
def dibujar_tarjetas(fig, gen, y0=0.72, h=0.18):
    dias_mes   = int(gen["dias_mes"])
    dias_trans = int(gen["dias_trans"])

    tarjetas = [
        # (título, valor_principal, subtexto, color_sub)
        ("Presupuesto\nMes",
         fmt_mon(gen["presupuesto_mes"]),
         f"{dias_mes} días del mes",
         OSCURO),
        ("Presupuesto\nal Día",
         fmt_mon(gen["presupuesto_dia"]),
         fmt_mon_diario(gen["valor_diario"]),
         OSCURO),
        ("Ejecutado",
         fmt_mon(gen["ejecutado"]),
         None,   # especial — dos logros
         None),
        ("Proyectado",
         fmt_mon(gen["proyectado"]),
         f"Dif: {fmt_mon(gen['dif_proyectada'])}",
         ROJO if gen["dif_proyectada"] > 0 else AZUL),
        ("Pres. Diario\nFaltante",
         fmt_mon(gen["pres_diario_faltante"]),
         f"Para cubrir presupuesto\nen {dias_mes - dias_trans} días restantes",
         ROJO),
    ]

    n = len(tarjetas)
    w = 0.18
    gap = (1.0 - n*w - 0.04) / (n - 1)

    for i, (titulo, valor, sub, color_sub) in enumerate(tarjetas):
        x0 = 0.02 + i * (w + gap)
        ax = fig.add_axes([x0, y0, w, h])
        ax.set_xlim(0,1); ax.set_ylim(0,1); ax.axis("off")
        ax.add_patch(plt.Rectangle((0,0),1,1,
                     facecolor="#f4f6f9", edgecolor="#dde1e7", lw=0.8))

        # Título arriba izquierda
        ax.text(0.05, 0.92, titulo, fontsize=7.5, fontweight="bold",
                color=GRIS, va="top", ha="left")

        # Valor principal
        ax.text(0.5, 0.58, valor, fontsize=9.5, fontweight="bold",
                color=OSCURO, va="center", ha="center")

        # Subtexto especial para Ejecutado
        if titulo == "Ejecutado":
            ax.text(0.04, 0.20, f"Logro Mes\n{fmt_pct(gen['logro_mes'])}",
                    fontsize=7, color=color_semaforo(gen["logro_mes"]),
                    va="bottom", ha="left", fontweight="bold")
            ax.text(0.96, 0.20, f"Logro Día\n{fmt_pct(gen['logro_dia'])}",
                    fontsize=7, color=color_semaforo(gen["logro_dia"]),
                    va="bottom", ha="right", fontweight="bold")
        elif sub:
            ax.text(0.5, 0.15, sub, fontsize=7, color=color_sub,
                    va="bottom", ha="center", fontweight="bold")

# ─── TABLA GENERICA ───────────────────────────────────────────────
def tabla_en_figura(fig, datos, col_grupo, ax_pos, fontsize=7.2):
    ax = fig.add_axes(ax_pos)
    ax.axis("off")

    col_labels = [col_grupo.replace("_"," ").title(),
                  "Presu. Mes","Ejecutado","Logro Día",
                  "Presu. al Día","Proyectado","Dif. Proyectada","Pres. Diario Falt."]
    filas, colores = [], []
    for _, row in datos.iterrows():
        ld  = row["logro_dia"]
        dif = row["dif_proyectada"]
        pdf_ = row["pres_diario_faltante"]
        filas.append([
            str(row[col_grupo])[:22],
            fmt_mon(row["presupuesto_mes"]),
            fmt_mon(row["ejecutado"]),
            fmt_pct(ld),
            fmt_mon(row["presupuesto_dia"]),
            fmt_mon(row["proyectado"]),
            fmt_mon(dif),
            fmt_mon(pdf_),
        ])
        color_dif = ROJO if (isinstance(dif, float) and not np.isnan(dif) and dif > 0) else \
                    (AZUL if (isinstance(dif, float) and not np.isnan(dif) and dif <= 0) else "white")
        colores.append(["white","white","white",
                        color_semaforo(ld),"white","white",color_dif,"white"])

    if not filas:
        ax.text(0.5, 0.5, "Sin datos", ha="center"); return

    tabla = ax.table(cellText=filas, colLabels=col_labels,
                     cellLoc="center", loc="center")
    tabla.auto_set_font_size(False)
    tabla.set_fontsize(fontsize)
    tabla.scale(1, 1.25)

    for (ri, ci), cell in tabla.get_celld().items():
        if ri == 0:
            cell.set_facecolor(OSCURO)
            cell.set_text_props(color="white", fontweight="bold")
        else:
            c = colores[ri-1][ci]
            cell.set_facecolor(c)
            if c in (ROJO, VERDE, AMARILLO, AZUL):
                cell.set_text_props(color="white", fontweight="bold")
        cell.set_edgecolor("#dddddd")

# ─── TABLA VENDEDORES POR SUPERVISOR ──────────────────────────────
def paginas_vendedores(pdf, df, dias_mes, dias_trans):
    supervisores = df["Supervisor"].dropna().unique()
    dias_rest = max(dias_mes - dias_trans, 1)

    for sup in sorted(supervisores):
        df_sup = df[df["Supervisor"] == sup].copy()
        datos = df_sup.groupby("RepNombre").agg(
            presupuesto_mes=("PrePresupuestoMonto",    "sum"),
            presupuesto_dia=("PresupueestoMontoaldia", "sum"),
            ejecutado      =("PreEjecutadoMonto",       "sum"),
            proyectado     =("Proyectado",              "sum"),
        ).reset_index()
        datos["logro_dia"]          = datos["ejecutado"] / datos["presupuesto_dia"].replace(0, np.nan)
        datos["dif_proyectada"]     = datos["presupuesto_mes"] - datos["proyectado"]
        datos["pres_diario_faltante"] = (datos["presupuesto_mes"] - datos["ejecutado"]) / dias_rest
        datos = datos.sort_values("presupuesto_mes", ascending=False)

        fig = plt.figure(figsize=(11, 8.5))
        fig.patch.set_facecolor("white")
        cabecera(fig, f"Vendedores — {sup}")

        n = len(datos)
        h_tabla = min(0.72, 0.04 + n * 0.035)
        y_tabla = 0.88 - h_tabla

        tabla_en_figura(fig, datos, "RepNombre",
                        [0.02, y_tabla, 0.96, h_tabla], fontsize=6.8)

        # Consejo del día
        consejo_del_dia(fig, y_inicio=0.01)

        pdf.savefig(fig, bbox_inches="tight")
        plt.close(fig)

# ─── GENERAR PDF COMPLETO ─────────────────────────────────────────
def generar_pdf(df):
    dias_mes   = int(df["diasdelmes"].iloc[0])
    dias_trans = int(df["diasTranscurridos"].iloc[0])
    gen        = calcular_general(df, dias_mes, dias_trans)

    with PdfPages(RUTA_PDF) as pdf:

        # ── PÁGINA 1: Resumen + Centros + Canal ──
        fig = plt.figure(figsize=(11, 8.5))
        fig.patch.set_facecolor("white")
        cabecera(fig, f"Resumen General — {MESES_ES[MES_HOY]} {ANIO_HOY}")

        # Tarjetas
        dibujar_tarjetas(fig, gen, y0=0.74, h=0.17)

        # Tabla Centros
        datos_centro = calcular_grupo(df, "Centro", dias_mes, dias_trans)
        fig.text(0.5, 0.70, "Por Centro", ha="center", fontsize=10,
                 fontweight="bold", color=OSCURO)
        tabla_en_figura(fig, datos_centro, "Centro",
                        [0.02, 0.52, 0.96, 0.17])

        # Tabla Canal
        datos_canal = calcular_grupo(df, "Canal", dias_mes, dias_trans)
        fig.text(0.5, 0.49, "Por Canal", ha="center", fontsize=10,
                 fontweight="bold", color=OSCURO)
        tabla_en_figura(fig, datos_canal, "Canal",
                        [0.02, 0.31, 0.96, 0.17])

        # Consejo del día
        consejo_del_dia(fig, y_inicio=0.14)

        pdf.savefig(fig, bbox_inches="tight")
        plt.close(fig)

        # ── PÁGINA 2: Supervisores ──
        datos_sup = calcular_grupo(df, "Supervisor", dias_mes, dias_trans)
        fig = plt.figure(figsize=(11, 8.5))
        fig.patch.set_facecolor("white")
        cabecera(fig, f"Ventas vs Presupuesto — Supervisores")
        tabla_en_figura(fig, datos_sup, "Supervisor",
                        [0.02, 0.18, 0.96, 0.68])
        consejo_del_dia(fig, y_inicio=0.04)
        pdf.savefig(fig, bbox_inches="tight")
        plt.close(fig)

        # ── PÁGINAS 3+: Vendedores por Supervisor ──
        paginas_vendedores(pdf, df, dias_mes, dias_trans)

        # ── ÚLTIMA PÁGINA: Suplidores ──
        datos_sup = calcular_grupo(df, "Suplidor", dias_mes, dias_trans)
        fig = plt.figure(figsize=(11, 8.5))
        fig.patch.set_facecolor("white")
        cabecera(fig, "Ventas vs Presupuesto — Suplidores")
        tabla_en_figura(fig, datos_sup, "Suplidor",
                        [0.02, 0.14, 0.96, 0.75], fontsize=6.5)
        consejo_del_dia(fig, y_inicio=0.02)
        pdf.savefig(fig, bbox_inches="tight")
        plt.close(fig)

    print(f"OK PDF: {RUTA_PDF}")

# ─── ENVIAR CORREO ────────────────────────────────────────────────
def enviar_correo():
    try:
        import win32com.client
        outlook = win32com.client.Dispatch("Outlook.Application")
        mail = outlook.CreateItem(0)
        mail.SentOnBehalfOfName = REMITENTE
        mail.To = "; ".join(CORREOS)
        mail.Subject = f"Avances y Proyecciones Comerciales {MESES_ES[MES_HOY]} {ANIO_HOY}"
        mail.Body = "En el siguiente anexo se muestran los avances y proyecciones comerciales a la fecha."
        mail.Attachments.Add(RUTA_PDF)
        mail.Send()
        print("OK Correo enviado.")
    except Exception as e:
        print(f"ERROR correo: {e}")
        print("   Abre Outlook con rmercedes@amcomercial.com y ejecuta de nuevo.")

# ─── MAIN ─────────────────────────────────────────────────────────
def main():
    print(f"\n{'='*55}")
    print(f"  PROYECCIONES COMERCIALES — {hoy.strftime('%d/%m/%Y')}")
    print(f"  Periodo: {MESES_ES[MES_HOY]} {ANIO_HOY}")
    print(f"{'='*55}\n")
    print("Leyendo archivo Excel...")
    df = pd.read_excel(RUTA_EXCEL, sheet_name=HOJA)
    print("Filtrando mes actual...")
    df = validar_y_filtrar(df)
    for col in ["PrePresupuestoMonto","PresupueestoMontoaldia",
                "PreEjecutadoMonto","Proyectado",
                "diasdelmes","diasTranscurridos"]:
        df[col] = pd.to_numeric(df[col], errors="coerce").fillna(0)
    print("Generando PDF...")
    generar_pdf(df)
    print("Enviando correo (Outlook debe estar abierto)...")
    enviar_correo()
    print("\nProceso completado.\n")

if __name__ == "__main__":
    main()
