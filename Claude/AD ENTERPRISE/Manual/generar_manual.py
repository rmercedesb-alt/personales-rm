"""
Manual de Usuario — AD ALL DIGITAL ENTERPRISE v1.1
Genera Manual_Usuario_AD_Enterprise.pdf con reportlab
"""
from reportlab.lib.pagesizes import A4
from reportlab.lib import colors
from reportlab.lib.units import cm
from reportlab.platypus import (SimpleDocTemplate, Paragraph, Spacer, Table,
                                 TableStyle, PageBreak, HRFlowable)
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_RIGHT, TA_JUSTIFY
from reportlab.platypus import Flowable
import datetime, os

# ── Colores ───────────────────────────────────────────────────────────────────
AZUL_MARINO  = colors.HexColor('#1a3a5c')
AZUL_MEDIO   = colors.HexColor('#2d5f8a')
AZUL_CLARO   = colors.HexColor('#e8f0fe')
VERDE        = colors.HexColor('#198754')
VERDE_CLARO  = colors.HexColor('#d1e7dd')
GRIS_CLARO   = colors.HexColor('#f8f9fa')
GRIS_MEDIO   = colors.HexColor('#dee2e6')
BLANCO       = colors.white
ACERO        = colors.HexColor('#5b8db8')   # neutro complementario
ROJO         = colors.HexColor('#dc3545')
NARANJA      = colors.HexColor('#fd7e14')
MORADO       = colors.HexColor('#6610f2')

OUTPUT = (r"C:\Personales RM\Claude"
          r"\AD ENTERPRISE\Manual\Manual_Usuario_AD_Enterprise.pdf")

w_page, h_page = A4   # 595 x 842 pts

# ── Pie de pagina ─────────────────────────────────────────────────────────────
class NumeracionPaginas:
    def pie(self, canvas, doc):
        canvas.saveState()
        canvas.setFillColor(AZUL_MARINO)
        canvas.rect(0, 0, w_page, 1.1*cm, fill=1, stroke=0)
        canvas.setFillColor(BLANCO)
        canvas.setFont('Helvetica', 8)
        canvas.drawString(1.5*cm, 0.35*cm,
                          'Fanny Mercedes & Asoc. — AD ALL DIGITAL ENTERPRISE')
        canvas.drawRightString(w_page - 1.5*cm, 0.35*cm,
                               f'Pag. {doc.page}  |  v1.1')
        canvas.restoreState()

# ── Pantalla simulada ─────────────────────────────────────────────────────────
class PantallaSimulada(Flowable):
    def __init__(self, titulo_nav, elementos, ancho=16*cm, alto=5.5*cm):
        Flowable.__init__(self)
        self.titulo_nav = titulo_nav
        self.elementos  = elementos
        self.ancho      = ancho
        self.alto       = alto
        self.width      = ancho
        self.height     = alto + 0.65*cm   # espacio para la barra nav

    def draw(self):
        c   = self.canv
        w   = self.ancho
        h   = self.alto
        NAV = 0.65*cm      # altura barra de navegacion

        # --- sombra
        c.setFillColor(colors.HexColor('#cccccc'))
        c.roundRect(0.15*cm, -0.15*cm, w, h + NAV, 6, fill=1, stroke=0)

        # --- fondo pantalla
        c.setFillColor(GRIS_CLARO)
        c.setStrokeColor(GRIS_MEDIO)
        c.setLineWidth(0.5)
        c.roundRect(0, 0, w, h + NAV, 6, fill=1, stroke=1)

        # --- barra nav
        c.setFillColor(AZUL_MARINO)
        c.roundRect(0, h, w, NAV, 6, fill=1, stroke=0)
        c.rect(0, h, w, NAV/2, fill=1, stroke=0)

        # dots (trafico)
        for i, col in enumerate([colors.HexColor('#ff5f57'),
                                   colors.HexColor('#ffbd2e'),
                                   colors.HexColor('#28c840')]):
            c.setFillColor(col)
            c.circle(w - (2-i)*0.38*cm - 0.25*cm, h + NAV*0.5, 0.09*cm,
                     fill=1, stroke=0)

        c.setFillColor(BLANCO)
        c.setFont('Helvetica-Bold', 7)
        c.drawString(0.35*cm, h + NAV*0.28, self.titulo_nav)

        # --- elementos
        for el in self.elementos:
            tp  = el.get('tipo', 'caja')
            ex  = el.get('x', 0) * cm
            ey  = el.get('y', 0) * cm
            ew  = el.get('w', 2) * cm
            eh  = el.get('h', 0.5) * cm
            ecol = el.get('color', AZUL_CLARO)
            etxt = el.get('texto', '')
            efc  = el.get('fc', AZUL_MARINO)
            efont = el.get('font', 6)

            if tp == 'caja':
                c.setFillColor(ecol)
                c.setStrokeColor(GRIS_MEDIO)
                c.setLineWidth(0.3)
                c.roundRect(ex, ey, ew, eh, 3, fill=1, stroke=1)
                if etxt:
                    c.setFillColor(efc)
                    c.setFont('Helvetica', efont)
                    c.drawString(ex + 0.15*cm, ey + eh/2 - 0.09*cm, etxt[:60])

            elif tp == 'btn':
                c.setFillColor(ecol)
                c.roundRect(ex, ey, ew, eh, 3, fill=1, stroke=0)
                c.setFillColor(BLANCO)
                c.setFont('Helvetica-Bold', 5.5)
                tw = c.stringWidth(etxt, 'Helvetica-Bold', 5.5)
                c.drawString(ex + (ew-tw)/2, ey + eh/2 - 0.08*cm, etxt)

            elif tp == 'titulo':
                c.setFillColor(efc)
                c.setFont('Helvetica-Bold', efont if efont != 6 else 9)
                c.drawString(ex, ey + 0.05*cm, etxt)

            elif tp == 'sub':
                c.setFillColor(colors.HexColor('#555555'))
                c.setFont('Helvetica', efont if efont != 6 else 6.5)
                c.drawString(ex, ey, etxt)

            elif tp == 'badge':
                c.setFillColor(ecol)
                c.roundRect(ex, ey, ew, eh, 2, fill=1, stroke=0)
                c.setFillColor(BLANCO)
                c.setFont('Helvetica-Bold', 5)
                tw = c.stringWidth(etxt, 'Helvetica-Bold', 5)
                c.drawString(ex + (ew-tw)/2, ey + eh/2 - 0.07*cm, etxt)

            elif tp == 'linea':
                c.setStrokeColor(GRIS_MEDIO)
                c.setLineWidth(0.3)
                c.line(ex, ey, ex + ew, ey)

            elif tp == 'card':
                c.setFillColor(BLANCO)
                c.setStrokeColor(GRIS_MEDIO)
                c.setLineWidth(0.4)
                c.roundRect(ex, ey, ew, eh, 4, fill=1, stroke=1)
                c.setFillColor(ecol)
                bar_h = 0.42*cm
                c.roundRect(ex, ey + eh - bar_h, ew, bar_h, 4, fill=1, stroke=0)
                c.rect(ex, ey + eh - bar_h, ew, bar_h/2, fill=1, stroke=0)
                c.setFillColor(BLANCO)
                c.setFont('Helvetica-Bold', 5.5)
                c.drawString(ex + 0.2*cm, ey + eh - bar_h*0.55, etxt)

            elif tp == 'card_num':
                # tarjeta estilo KPI con numero grande
                c.setFillColor(ecol)
                c.roundRect(ex, ey, ew, eh, 5, fill=1, stroke=0)
                c.setFillColor(BLANCO)
                c.setFont('Helvetica-Bold', 5.5)
                c.drawString(ex + 0.2*cm, ey + eh - 0.45*cm, etxt)
                num = el.get('num', '')
                c.setFont('Helvetica-Bold', 11)
                c.drawString(ex + 0.2*cm, ey + 0.15*cm, str(num))

            elif tp == 'nav_item':
                # item de barra de navegacion
                c.setFillColor(ecol)
                c.roundRect(ex, ey, ew, eh, 2, fill=1, stroke=0)
                c.setFillColor(BLANCO)
                c.setFont('Helvetica-Bold', 5.5)
                tw = c.stringWidth(etxt, 'Helvetica-Bold', 5.5)
                c.drawString(ex + (ew-tw)/2, ey + eh/2 - 0.08*cm, etxt)

            elif tp == 'flecha':
                c.setFillColor(efc)
                c.setFont('Helvetica-Bold', 10)
                c.drawString(ex, ey, etxt)

# ── Estilos ───────────────────────────────────────────────────────────────────
def E(nombre, **kw):
    return ParagraphStyle(nombre, **kw)

EST_SUBSEC = E('sub', fontSize=10, textColor=AZUL_MEDIO,
               fontName='Helvetica-Bold', spaceBefore=7, spaceAfter=2)
EST_CUERPO = E('body', fontSize=8.5, textColor=colors.HexColor('#333333'),
               fontName='Helvetica', leading=13, spaceAfter=3,
               alignment=TA_JUSTIFY)
EST_PASO   = E('paso', fontSize=8.5, textColor=colors.HexColor('#222222'),
               fontName='Helvetica', leading=13, leftIndent=14)
EST_INDICE = E('idx', fontSize=9, textColor=AZUL_MARINO, fontName='Helvetica', leading=16)
EST_CAP    = E('cap', fontSize=10, textColor=AZUL_MARINO, fontName='Helvetica-Bold', leading=18)

# ── Helpers ───────────────────────────────────────────────────────────────────
def encabezado_seccion(numero, texto):
    data = [[Paragraph(f'<font color="white"><b>{numero}</b></font>',
                        E('x', fontSize=11, textColor=BLANCO,
                          fontName='Helvetica-Bold', alignment=TA_CENTER)),
             Paragraph(texto, E('y', fontSize=12, textColor=BLANCO,
                                fontName='Helvetica-Bold'))]]
    t = Table(data, colWidths=[1*cm, 15*cm])
    t.setStyle(TableStyle([
        ('BACKGROUND',   (0,0),(-1,-1), AZUL_MARINO),
        ('VALIGN',       (0,0),(-1,-1), 'MIDDLE'),
        ('TOPPADDING',   (0,0),(-1,-1), 6),
        ('BOTTOMPADDING',(0,0),(-1,-1), 6),
        ('LEFTPADDING',  (1,0),(1,0),  8),
    ]))
    return t

def paso(n, texto):
    nums = ['①','②','③','④','⑤','⑥','⑦','⑧','⑨','⑩']
    ic   = nums[n-1] if n <= 10 else f'{n}.'
    return Paragraph(f'<font color="#1a3a5c"><b>{ic}</b></font>  {texto}', EST_PASO)

def tip(texto):
    d = [[Paragraph(f'<font color="#198754"><b>Tip:</b></font>  {texto}',
                     E('nt', fontSize=7.5, textColor=colors.HexColor('#155724'),
                       fontName='Helvetica', leading=12))]]
    t = Table(d, colWidths=[16*cm])
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,-1),VERDE_CLARO),
                            ('TOPPADDING',(0,0),(-1,-1),5),
                            ('BOTTOMPADDING',(0,0),(-1,-1),5),
                            ('LEFTPADDING',(0,0),(-1,-1),8)]))
    return t

def alerta(texto):
    d = [[Paragraph(f'<font color="#664d03"><b>Importante:</b></font>  {texto}',
                     E('al', fontSize=7.5, textColor=colors.HexColor('#664d03'),
                       fontName='Helvetica', leading=12))]]
    t = Table(d, colWidths=[16*cm])
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,-1),colors.HexColor('#fff3cd')),
                            ('TOPPADDING',(0,0),(-1,-1),5),
                            ('BOTTOMPADDING',(0,0),(-1,-1),5),
                            ('LEFTPADDING',(0,0),(-1,-1),8)]))
    return t

def info(texto, col=AZUL_CLARO, fc=AZUL_MARINO):
    d = [[Paragraph(f'<font color="#1a3a5c"><b>Info:</b></font>  {texto}',
                     E('inf', fontSize=7.5, textColor=fc,
                       fontName='Helvetica', leading=12))]]
    t = Table(d, colWidths=[16*cm])
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,-1),col),
                            ('TOPPADDING',(0,0),(-1,-1),5),
                            ('BOTTOMPADDING',(0,0),(-1,-1),5),
                            ('LEFTPADDING',(0,0),(-1,-1),8)]))
    return t

def fila_indice(num, titulo, pagina, cap=False):
    s_n = E('fn',  fontSize=10 if cap else 8.5, textColor=AZUL_MARINO,
            fontName='Helvetica-Bold' if cap else 'Helvetica')
    s_t = E('ft',  fontSize=10 if cap else 8.5,
            textColor=AZUL_MARINO if cap else colors.HexColor('#333333'),
            fontName='Helvetica-Bold' if cap else 'Helvetica')
    s_p = E('fp',  fontSize=10 if cap else 8.5, textColor=AZUL_MARINO,
            fontName='Helvetica-Bold', alignment=TA_RIGHT)
    bg  = AZUL_CLARO if cap else BLANCO
    d   = [[Paragraph(num, s_n), Paragraph(titulo, s_t), Paragraph(str(pagina), s_p)]]
    t   = Table(d, colWidths=[1*cm, 13*cm, 2*cm])
    t.setStyle(TableStyle([
        ('BACKGROUND',(0,0),(-1,-1),bg),
        ('TOPPADDING',(0,0),(-1,-1), 4 if cap else 3),
        ('BOTTOMPADDING',(0,0),(-1,-1), 4 if cap else 3),
        ('LEFTPADDING',(0,0),(0,0),6),
        ('LEFTPADDING',(1,0),(1,0), 4 if cap else 12),
        ('LINEBELOW',(0,0),(-1,-1),0.3,GRIS_MEDIO),
    ]))
    return t

# ═════════════════════════════════════════════════════════════════════════════
# HISTORIA DEL DOCUMENTO
# ═════════════════════════════════════════════════════════════════════════════
numeracion = NumeracionPaginas()
doc = SimpleDocTemplate(
    OUTPUT, pagesize=A4,
    rightMargin=1.5*cm, leftMargin=1.5*cm,
    topMargin=1.5*cm,   bottomMargin=1.8*cm,
    title='Manual de Usuario — AD ALL DIGITAL ENTERPRISE',
    author='AD ALL DIGITAL ENTERPRISE',
)
story = []

# ─────────────────────────────────────────────────────────────────────────────
# PAG 1 — PORTADA
# ─────────────────────────────────────────────────────────────────────────────
class Portada(Flowable):
    def __init__(self):
        Flowable.__init__(self)
        self.width  = w_page - 3*cm
        self.height = 25*cm

    def draw(self):
        c = self.canv
        w = self.width

        # fondo completo
        c.setFillColor(AZUL_MARINO)
        c.rect(-1.5*cm, -1.5*cm, w_page, h_page, fill=1, stroke=0)

        # banda superior decorativa
        c.setFillColor(AZUL_MEDIO)
        c.rect(-1.5*cm, h_page - 2.5*cm, w_page, 1.4*cm, fill=1, stroke=0)

        # circulos decorativos (tonos azul marino mas claro)
        c.setFillColor(colors.HexColor('#243f5e'))
        c.circle(w - 0.5*cm, h_page - 1*cm, 4.5*cm, fill=1, stroke=0)
        c.setFillColor(colors.HexColor('#1e3550'))
        c.circle(-1*cm, 3*cm, 6*cm, fill=1, stroke=0)

        # panel central
        c.setFillColor(colors.HexColor('#203550'))
        c.roundRect(1*cm, 8*cm, w - 2*cm, 12*cm, 10, fill=1, stroke=0)

        # recuadro logo
        c.setStrokeColor(colors.HexColor('#4a7090'))
        c.setFillColor(colors.HexColor('#1e3a58'))
        c.setLineWidth(1.5)
        c.roundRect(w/2 - 3*cm, 17.5*cm, 6*cm, 2.8*cm, 8, fill=1, stroke=1)
        c.setFillColor(BLANCO)
        c.setFont('Helvetica-Bold', 10)
        c.drawCentredString(w/2, 18.9*cm, 'FANNY MERCEDES & ASOC.')
        c.setFont('Helvetica', 7.5)
        c.setFillColor(colors.HexColor('#aaccee'))
        c.drawCentredString(w/2, 18.3*cm, 'Bufete de Abogados')

        # titulo
        c.setFillColor(BLANCO)
        c.setFont('Helvetica-Bold', 26)
        c.drawCentredString(w/2, 16*cm, 'MANUAL DE USUARIO')

        # nombre sistema
        c.setFillColor(ACERO)
        c.setFont('Helvetica-Bold', 16)
        c.drawCentredString(w/2, 14.8*cm, 'AD ALL DIGITAL ENTERPRISE')

        # descripcion
        c.setFillColor(colors.HexColor('#cce0ff'))
        c.setFont('Helvetica', 10)
        c.drawCentredString(w/2, 13.8*cm, 'Sistema de Facturacion y Cobros')
        c.setFont('Helvetica', 8.5)
        c.drawCentredString(w/2, 13.2*cm, 'Gestion integral de facturas, pagos y reportes')

        # linea
        c.setStrokeColor(ACERO)
        c.setLineWidth(1.5)
        c.line(w/2 - 4.5*cm, 12.6*cm, w/2 + 4.5*cm, 12.6*cm)

        # version y fecha
        c.setFillColor(BLANCO)
        c.setFont('Helvetica', 9)
        c.drawCentredString(w/2, 11.8*cm,
                            f'Version 1.1   |   {datetime.datetime.now().strftime("%B %Y")}')

        # URL acceso
        c.setFillColor(VERDE)
        c.roundRect(w/2 - 3.5*cm, 10.5*cm, 7*cm, 0.9*cm, 5, fill=1, stroke=0)
        c.setFillColor(BLANCO)
        c.setFont('Helvetica-Bold', 9)
        c.drawCentredString(w/2, 10.85*cm, 'http://rudymercedes:5000')

        # pie portada
        c.setFillColor(colors.HexColor('#8899aa'))
        c.setFont('Helvetica', 7)
        c.drawCentredString(w/2, 1.5*cm,
                            'Powered by AD ALL DIGITAL ENTERPRISE  |  r.mercedesb@gmail.com')

story.append(Portada())
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 2 — INDICE
# ─────────────────────────────────────────────────────────────────────────────
hdr_idx = Table([[Paragraph('<font color="white"><b>INDICE DE CONTENIDO</b></font>',
                              E('hi', fontSize=14, textColor=BLANCO,
                                fontName='Helvetica-Bold', alignment=TA_CENTER))]],
                colWidths=[16*cm])
hdr_idx.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,-1),AZUL_MARINO),
                               ('TOPPADDING',(0,0),(-1,-1),10),
                               ('BOTTOMPADDING',(0,0),(-1,-1),10)]))
story.append(hdr_idx)
story.append(Spacer(1, 0.5*cm))

story.append(fila_indice('1', 'ACCESO AL SISTEMA', 3, cap=True))
story.append(fila_indice('1.1', 'Iniciar sesion (Login)', 3))
story.append(fila_indice('1.2', 'Menu Principal de Navegacion', 3))
story.append(fila_indice('1.3', 'Dashboard — Panel Principal', 3))
story.append(Spacer(1, 0.1*cm))
story.append(fila_indice('2', 'FACTURACION', 4, cap=True))
story.append(fila_indice('2.1', 'Nueva Factura', 4))
story.append(fila_indice('2.2', 'Consulta de Facturas', 4))
story.append(Spacer(1, 0.1*cm))
story.append(fila_indice('3', 'PAGOS — REGISTRAR RECIBO', 5, cap=True))
story.append(fila_indice('3.1', 'Paso a paso: crear un recibo de pago', 5))
story.append(fila_indice('3.2', 'Abono a Cuenta: que es y como funciona', 6))
story.append(fila_indice('3.3', 'Efecto del Abono a Cuenta en el sistema', 7))
story.append(Spacer(1, 0.1*cm))
story.append(fila_indice('4', 'CONSULTA DE PAGOS', 8, cap=True))
story.append(fila_indice('4.1', 'Ver, editar e imprimir recibos', 8))
story.append(fila_indice('4.2', 'Eliminar un recibo completo', 8))
story.append(Spacer(1, 0.1*cm))
story.append(fila_indice('5', 'REPORTES', 9, cap=True))
story.append(fila_indice('5.1', 'Tipos de reportes disponibles', 9))
story.append(fila_indice('5.2', 'Exportar a Excel e Imprimir', 9))
story.append(Spacer(1, 0.1*cm))
story.append(fila_indice('6', 'MANTENIMIENTO  (Administrador)', 10, cap=True))
story.append(fila_indice('6.1', 'Companias, Clientes e Importar', 10))
story.append(fila_indice('6.2', 'Backup, Restauracion y Usuarios', 10))

story.append(Spacer(1, 0.5*cm))
story.append(tip(
    'Para acceder desde celular: activa Tailscale y navega a http://rudymercedes:5000. '
    'Funciona en Chrome, Firefox y Safari.'))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 3 — ACCESO AL SISTEMA + MENU + DASHBOARD
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('1', '🔐  ACCESO AL SISTEMA'))
story.append(Spacer(1, 0.25*cm))

# 1.1 Login
story.append(Paragraph('1.1  Iniciar Sesion (Login)', EST_SUBSEC))
story.append(Paragraph(
    'Al abrir el navegador e ingresar la URL del sistema, aparece la pantalla de '
    'inicio de sesion. Ingrese su usuario y contrasena para acceder.', EST_CUERPO))

login_elems = [
    {'tipo':'titulo','texto':'AD ALL DIGITAL ENTERPRISE','x':2.8,'y':4.2,'fc':AZUL_MARINO,'font':10},
    {'tipo':'sub',   'texto':'Sistema de Facturacion — Fanny Mercedes & Asoc.',  'x':2.5,'y':3.75},
    {'tipo':'sub',   'texto':'Usuario',       'x':3.5,'y':3.35,'font':7},
    {'tipo':'caja',  'texto':'administrador', 'x':3.5,'y':2.75,'w':9,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Contrasena',    'x':3.5,'y':2.35,'font':7},
    {'tipo':'caja',  'texto':'••••••••••',    'x':3.5,'y':1.75,'w':9,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'btn',   'texto':'INICIAR SESION','x':3.5,'y':0.95,'w':9,'h':0.65,'color':AZUL_MARINO},
    {'tipo':'sub',   'texto':'v1.1  |  Acceso seguro con sesion de 8 horas','x':4.2,'y':0.35},
]
story.append(PantallaSimulada(
    'http://rudymercedes:5000  —  Inicio de Sesion', login_elems, alto=5*cm))
story.append(Spacer(1, 0.15*cm))

for n, t in [(1,'Abre el navegador e ingresa: <b>http://rudymercedes:5000</b>'),
             (2,'Escribe tu <b>nombre de usuario</b> (ej: administrador)'),
             (3,'Escribe tu <b>contrasena</b> y haz clic en <b>INICIAR SESION</b>'),
             (4,'El sistema te redirige al <b>Dashboard</b> automaticamente')]:
    story.append(paso(n, t))

story.append(Spacer(1, 0.25*cm))

# 1.2 Menu principal
story.append(Paragraph('1.2  Menu Principal de Navegacion', EST_SUBSEC))
story.append(Paragraph(
    'Luego del login, en la parte superior siempre aparece la barra de navegacion '
    'con acceso rapido a todas las secciones del sistema.', EST_CUERPO))

menu_elems = [
    # navbar completa simulada (fondo marino ya lo da el titulo_nav)
    {'tipo':'sub',  'texto':'Menu principal — siempre visible en la parte superior de cada pagina',
                    'x':0.3,'y':3.8},
    # items del menu
    {'tipo':'nav_item','texto':'Dashboard',     'x':0.3, 'y':2.8,'w':2.2,'h':0.6,'color':AZUL_MEDIO},
    {'tipo':'nav_item','texto':'Facturacion',   'x':2.7, 'y':2.8,'w':2.4,'h':0.6,'color':AZUL_MARINO},
    {'tipo':'nav_item','texto':'Pagos',         'x':5.3, 'y':2.8,'w':1.8,'h':0.6,'color':AZUL_MARINO},
    {'tipo':'nav_item','texto':'Reportes',      'x':7.3, 'y':2.8,'w':2.0,'h':0.6,'color':AZUL_MARINO},
    {'tipo':'nav_item','texto':'Mantenimiento', 'x':9.5, 'y':2.8,'w':2.8,'h':0.6,'color':AZUL_MARINO},
    {'tipo':'nav_item','texto':'Admin Rosa',    'x':12.5,'y':2.8,'w':2.2,'h':0.6,'color':ACERO},
    {'tipo':'nav_item','texto':'Salir',         'x':14.9,'y':2.8,'w':1.3,'h':0.6,'color':ROJO},
    # submenu facturacion expandido
    {'tipo':'sub',  'texto':'Submenu Facturacion:', 'x':2.5,'y':2.3,'font':7},
    {'tipo':'caja', 'texto':'Nueva Factura',  'x':2.7,'y':1.65,'w':3,'h':0.5,'color':BLANCO},
    {'tipo':'caja', 'texto':'Consulta',       'x':2.7,'y':1.05,'w':3,'h':0.5,'color':BLANCO},
    {'tipo':'sub',  'texto':'Submenu Pagos:', 'x':5.1,'y':2.3,'font':7},
    {'tipo':'caja', 'texto':'Registrar Pago', 'x':5.3,'y':1.65,'w':3,'h':0.5,'color':BLANCO},
    {'tipo':'caja', 'texto':'Consulta',       'x':5.3,'y':1.05,'w':3,'h':0.5,'color':BLANCO},
    {'tipo':'sub',  'texto':'El item resaltado indica la pagina activa', 'x':0.3,'y':0.3},
]
story.append(PantallaSimulada(
    'Barra de Navegacion Superior — siempre visible', menu_elems, alto=4.5*cm))
story.append(Spacer(1, 0.15*cm))
story.append(tip(
    'Haz clic en cualquier opcion del menu para navegar. '
    'El boton <b>Salir</b> (rojo) cierra tu sesion de forma segura.'))

story.append(Spacer(1, 0.25*cm))

# 1.3 Dashboard
story.append(Paragraph('1.3  Dashboard — Panel Principal', EST_SUBSEC))
story.append(Paragraph(
    'El Dashboard muestra un resumen en tiempo real del estado del negocio: '
    'facturas pendientes, monto cobrado este mes, total de clientes activos y '
    'grafica de cobros por periodo.', EST_CUERPO))

dash_elems = [
    {'tipo':'card_num','texto':'Facturas Pendientes','x':0.2,'y':2.8,'w':3.6,'h':2.0,
     'color':AZUL_MARINO,'num':'47'},
    {'tipo':'card_num','texto':'Cobrado Este Mes (RD$)','x':4.1,'y':2.8,'w':4.0,'h':2.0,
     'color':VERDE,'num':'1,248,500'},
    {'tipo':'card_num','texto':'Total Facturas','x':8.3,'y':2.8,'w':3.6,'h':2.0,
     'color':AZUL_MEDIO,'num':'312'},
    {'tipo':'card_num','texto':'Clientes Activos','x':12.1,'y':2.8,'w':3.9,'h':2.0,
     'color':ACERO,'num':'38'},
    {'tipo':'card','texto':'Cobros por Mes (RD$)','x':0.2,'y':0.3,'w':9.5,'h':2.3,'color':AZUL_MARINO},
    {'tipo':'card','texto':'Ultimas Facturas Creadas','x':9.9,'y':0.3,'w':6.1,'h':2.3,'color':AZUL_MARINO},
    # datos inventados en grafica (barras)
    {'tipo':'caja','texto':'Ene  Feb  Mar  Abr  May  Jun','x':0.4,'y':0.4,'w':9.1,'h':0.3,'color':GRIS_CLARO,'fc':colors.HexColor('#999999')},
    {'tipo':'caja','texto':'#22168  Lanco Dominicana  RD$5,188','x':10.1,'y':1.6,'w':5.7,'h':0.38,'color':GRIS_CLARO,'fc':colors.HexColor('#333333')},
    {'tipo':'caja','texto':'#22167  ALTICE  RD$42,780','x':10.1,'y':1.1,'w':5.7,'h':0.38,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'caja','texto':'#22166  EGE Haina  RD$18,640','x':10.1,'y':0.6,'w':5.7,'h':0.38,'color':GRIS_CLARO,'fc':colors.HexColor('#333333')},
]
story.append(PantallaSimulada('Dashboard — Panel Principal', dash_elems, alto=5*cm))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 4 — FACTURACION
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('2', '🧾  FACTURACION'))
story.append(Spacer(1, 0.25*cm))

story.append(Paragraph('2.1  Nueva Factura', EST_SUBSEC))
story.append(Paragraph(
    'Ve al menu <b>Facturacion → Nueva Factura</b>. Completa los campos y guarda. '
    'El N° de Ticket se asigna automaticamente.', EST_CUERPO))

nf_elems = [
    {'tipo':'titulo','texto':'Nueva Factura','x':0.3,'y':4.5,'fc':AZUL_MARINO,'font':10},
    {'tipo':'sub',   'texto':'N° Ticket (automatico)','x':0.3,'y':4.0,'font':7},
    {'tipo':'caja',  'texto':'#22169  [auto-generado]','x':0.3,'y':3.4,'w':3.8,'h':0.52,'color':GRIS_CLARO,'fc':AZUL_MEDIO},
    {'tipo':'sub',   'texto':'Compania','x':4.3,'y':4.0,'font':7},
    {'tipo':'caja',  'texto':'Lanco Dominicana  (ADCOM001)','x':4.3,'y':3.4,'w':5.2,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Cliente','x':9.7,'y':4.0,'font':7},
    {'tipo':'caja',  'texto':'Rosa Mercedes  (ADCTE012)','x':9.7,'y':3.4,'w':5.5,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Monto (RD$)','x':0.3,'y':2.9,'font':7},
    {'tipo':'caja',  'texto':'RD$  5,188.16','x':0.3,'y':2.3,'w':4.2,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Fecha de Factura','x':4.7,'y':2.9,'font':7},
    {'tipo':'caja',  'texto':'07/06/2026','x':4.7,'y':2.3,'w':4.2,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Estado inicial','x':9.1,'y':2.9,'font':7},
    {'tipo':'badge', 'texto':'Pendiente','x':9.1,'y':2.3,'w':2.5,'h':0.52,'color':AZUL_MEDIO},
    {'tipo':'sub',   'texto':'Notas / Descripcion','x':0.3,'y':1.85,'font':7},
    {'tipo':'caja',  'texto':'Servicio legal mensual — Licencia operacion comercial enero-junio 2026',
                     'x':0.3,'y':1.25,'w':14.9,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'btn',   'texto':'GUARDAR FACTURA','x':10.5,'y':0.3,'w':4.7,'h':0.65,'color':VERDE},
    {'tipo':'btn',   'texto':'Cancelar',       'x':0.3, 'y':0.3,'w':2.8,'h':0.65,'color':AZUL_MEDIO},
]
story.append(PantallaSimulada('Facturacion → Nueva Factura', nf_elems, alto=5.2*cm))
story.append(Spacer(1, 0.15*cm))

for n, t in [
    (1,'Ve al menu <b>Facturacion → Nueva Factura</b>'),
    (2,'El <b>N° de Ticket</b> (#22169) se asigna solo — no lo modifiques'),
    (3,'Selecciona la <b>Compania</b> y el <b>Cliente</b> en los desplegables'),
    (4,'Ingresa el <b>Monto</b> (ej: RD$ 5,188.16) y la <b>Fecha</b> de la factura'),
    (5,'Agrega una <b>Nota</b> descriptiva del servicio prestado'),
    (6,'Haz clic en <b>GUARDAR FACTURA</b> — el estado inicial sera <b>Pendiente</b>'),
]:
    story.append(paso(n, t))

story.append(Spacer(1, 0.3*cm))

# 2.2 Consulta facturas
story.append(Paragraph('2.2  Consulta de Facturas', EST_SUBSEC))
story.append(Paragraph(
    'En <b>Facturacion → Consulta</b> puedes ver todas las facturas, filtrar por '
    'estado o empresa, y acceder al detalle de cada una.', EST_CUERPO))

cf_elems = [
    {'tipo':'caja', 'texto':'Buscar ticket, empresa o cliente...','x':0.3,'y':4.0,'w':6.5,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#999999')},
    {'tipo':'badge','texto':'Pendiente (47)', 'x':7.0, 'y':4.0,'w':2.3,'h':0.52,'color':AZUL_MEDIO},
    {'tipo':'badge','texto':'Pagada (248)',   'x':9.5, 'y':4.0,'w':2.1,'h':0.52,'color':VERDE},
    {'tipo':'badge','texto':'Vencida (12)',   'x':11.8,'y':4.0,'w':1.9,'h':0.52,'color':ROJO},
    {'tipo':'badge','texto':'Parcial (5)',    'x':13.9,'y':4.0,'w':1.6,'h':0.52,'color':ACERO},
    # encabezado tabla
    {'tipo':'caja', 'texto':'Ticket  |  Compania               |  Cliente           |  Monto      |  Pagado     |  Estado    |  Fecha',
                    'x':0.3,'y':3.3,'w':14.9,'h':0.45,'color':AZUL_MARINO,'fc':BLANCO,'font':6},
    # filas con datos reales inventados
    {'tipo':'caja', 'texto':'#22168  |  Lanco Dominicana       |  Rosa Mercedes     |  RD$5,188   |  RD$0       |  Pendiente |  05/06/2026',
                    'x':0.3,'y':2.75,'w':14.9,'h':0.45,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'caja', 'texto':'#22167  |  ALTICE Dominicana      |  Juan C. Perez     |  RD$42,780  |  RD$42,780  |  Pagada    |  02/06/2026',
                    'x':0.3,'y':2.2,'w':14.9,'h':0.45,'color':AZUL_CLARO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'caja', 'texto':'#22166  |  EGE Haina              |  Maria T. Gomez    |  RD$18,640  |  RD$10,000  |  Parcial   |  01/06/2026',
                    'x':0.3,'y':1.65,'w':14.9,'h':0.45,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'caja', 'texto':'#22165  |  Banco Popular          |  Carlos E. Ruiz    |  RD$3,148   |  RD$0       |  Vencida   |  15/05/2026',
                    'x':0.3,'y':1.1,'w':14.9,'h':0.45,'color':colors.HexColor('#fff5f5'),'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'sub',  'texto':'Haz clic en cualquier fila para ver el detalle o editar la factura','x':0.3,'y':0.55},
    {'tipo':'btn',  'texto':'+ Nueva Factura','x':11.8,'y':0.3,'w':3.4,'h':0.52,'color':AZUL_MARINO},
]
story.append(PantallaSimulada('Facturacion → Consulta de Facturas', cf_elems, alto=5*cm))
story.append(Spacer(1, 0.15*cm))
story.append(tip(
    'Los colores de estado te guian: <b>azul=Pendiente</b>, <b>verde=Pagada</b>, '
    '<b>rojo=Vencida</b>, <b>gris=Parcial</b>. '
    'Puedes editar o eliminar facturas que aun no han sido cobradas.'))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 5 — PAGOS: REGISTRAR RECIBO
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('3', '💰  PAGOS — REGISTRAR RECIBO'))
story.append(Spacer(1, 0.25*cm))

story.append(Paragraph('3.1  Paso a paso: Crear un Recibo de Pago', EST_SUBSEC))
story.append(Paragraph(
    'Un <b>Recibo</b> agrupa uno o varios pagos de facturas bajo un mismo instrumento '
    '(cheque, transferencia o deposito). El numero se genera automaticamente: '
    '<b>REC-YYYYMMDD-NNNN</b>. Puedes pagar facturas de distintos clientes en un '
    'mismo recibo siempre que sean del mismo instrumento.', EST_CUERPO))

reg_elems = [
    # encabezado del instrumento
    {'tipo':'titulo','texto':'Encabezado del Instrumento de Pago','x':0.3,'y':5.0,'fc':AZUL_MARINO,'font':8},
    {'tipo':'sub',   'texto':'Fecha','x':0.3,'y':4.6,'font':6.5},
    {'tipo':'caja',  'texto':'07/06/2026','x':0.3,'y':4.0,'w':3.0,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Forma de Pago','x':3.5,'y':4.6,'font':6.5},
    {'tipo':'caja',  'texto':'Cheque','x':3.5,'y':4.0,'w':2.8,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Monto Total del Instrumento','x':6.5,'y':4.6,'font':6.5},
    {'tipo':'caja',  'texto':'RD$  500,000.00','x':6.5,'y':4.0,'w':4.5,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'N° Cheque','x':11.2,'y':4.6,'font':6.5},
    {'tipo':'caja',  'texto':'350515','x':11.2,'y':4.0,'w':3.5,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    # columna izquierda: facturas pendientes
    {'tipo':'card',  'texto':'Facturas Pendientes (47 disponibles)','x':0.3,'y':0.4,'w':7.8,'h':3.4,'color':AZUL_MARINO},
    {'tipo':'caja',  'texto':'#22168  Lanco Dom.  Rosa M.  RD$5,188.16','x':0.4,'y':2.55,'w':6.2,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'btn',   'texto':'+ Agregar','x':6.7,'y':2.55,'w':1.2,'h':0.42,'color':VERDE},
    {'tipo':'caja',  'texto':'#22165  Bco. Popular  Carlos R.  RD$3,148.52','x':0.4,'y':2.05,'w':6.2,'h':0.42,'color':AZUL_CLARO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'btn',   'texto':'+ Agregar','x':6.7,'y':2.05,'w':1.2,'h':0.42,'color':VERDE},
    {'tipo':'caja',  'texto':'#22164  EGE Haina  Maria G.  RD$18,640.00','x':0.4,'y':1.55,'w':6.2,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'btn',   'texto':'+ Agregar','x':6.7,'y':1.55,'w':1.2,'h':0.42,'color':VERDE},
    {'tipo':'caja',  'texto':'#22163  ALTICE  Carmen L.  RD$8,960.00','x':0.4,'y':1.05,'w':6.2,'h':0.42,'color':AZUL_CLARO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'btn',   'texto':'+ Agregar','x':6.7,'y':1.05,'w':1.2,'h':0.42,'color':VERDE},
    # columna derecha: detalle del recibo
    {'tipo':'card',  'texto':'Detalle del Recibo','x':8.3,'y':0.4,'w':6.9,'h':3.4,'color':AZUL_MARINO},
    {'tipo':'caja',  'texto':'1  #22167  ALTICE  RD$42,780.00','x':8.4,'y':2.6,'w':6.6,'h':0.42,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    {'tipo':'caja',  'texto':'2  #22166  EGE Haina  RD$18,640.00','x':8.4,'y':2.1,'w':6.6,'h':0.42,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    {'tipo':'linea', 'x':8.4,'y':1.92,'w':6.4},
    {'tipo':'caja',  'texto':'Aplicado: RD$61,420.00   Balance: RD$438,580.00','x':8.4,'y':1.38,'w':6.6,'h':0.42,'color':AZUL_CLARO,'fc':AZUL_MARINO,'font':6},
    {'tipo':'btn',   'texto':'+ Abono a Cuenta','x':8.4,'y':0.82,'w':4.2,'h':0.45,'color':ACERO},
    {'tipo':'btn',   'texto':'CONFIRMAR RECIBO','x':9.5,'y':0.2,'w':5.5,'h':0.52,'color':VERDE},
]
story.append(PantallaSimulada('Pagos → Registrar Recibo de Pago', reg_elems, alto=5.7*cm))
story.append(Spacer(1, 0.15*cm))

for n, t in [
    (1,'Ve al menu <b>Pagos → Registrar Pago</b>'),
    (2,'Completa el <b>Encabezado</b>: fecha, forma de pago, monto total del instrumento (ej: RD$ 500,000) y N° cheque/referencia'),
    (3,'En la lista izquierda, busca la factura y presiona <b>+ Agregar</b> — pasa al detalle del recibo'),
    (4,'Puedes modificar el monto aplicado si el cliente pago un valor diferente al de la factura'),
    (5,'Agrega <b>todas las facturas</b> que cubre ese instrumento, sin importar la empresa o cliente'),
    (6,'Haz clic en <b>CONFIRMAR RECIBO</b> — el sistema genera el numero y muestra el PDF'),
]:
    story.append(paso(n, t))
story.append(Spacer(1, 0.1*cm))
story.append(alerta(
    'El total aplicado debe cuadrar exactamente con el monto del instrumento. '
    'Si hay saldo sobrante, usa <b>+ Abono a Cuenta</b> (ver siguiente seccion).'))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 6 — ABONO A CUENTA (QUE ES Y COMO SE USA)
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('3.2', '📥  ABONO A CUENTA — Que es y como usarlo'))
story.append(Spacer(1, 0.2*cm))

story.append(Paragraph(
    'El <b>Abono a Cuenta</b> se usa cuando el cliente envia un pago mayor al total '
    'de las facturas aplicadas en el recibo. El excedente se registra como un '
    '<b>credito a favor</b> de ese cliente, quedando disponible para aplicarse en '
    'futuros pagos.', EST_CUERPO))

story.append(Spacer(1, 0.1*cm))

# ejemplo numerico
ejemplo_data = [
    [Paragraph('<b>Ejemplo practico</b>', E('ep', fontSize=8.5, textColor=AZUL_MARINO,
                fontName='Helvetica-Bold')),
     Paragraph('', E('ep2', fontSize=8.5))],
    [Paragraph('Monto del cheque recibido', E('el', fontSize=8, fontName='Helvetica')),
     Paragraph('<b>RD$ 500,000.00</b>', E('er', fontSize=8, fontName='Helvetica-Bold',
               textColor=AZUL_MARINO, alignment=TA_RIGHT))],
    [Paragraph('Facturas aplicadas en el recibo', E('el', fontSize=8, fontName='Helvetica')),
     Paragraph('<b>RD$ 61,420.00</b>', E('er', fontSize=8, fontName='Helvetica-Bold',
               textColor=AZUL_MARINO, alignment=TA_RIGHT))],
    [Paragraph('Balance sobrante', E('el', fontSize=8, fontName='Helvetica',
               textColor=colors.HexColor('#555555'))),
     Paragraph('<b>RD$ 438,580.00</b>', E('er', fontSize=8, fontName='Helvetica-Bold',
               textColor=ROJO, alignment=TA_RIGHT))],
    [Paragraph('<b>Abono a cuenta registrado</b>', E('el', fontSize=8,
               fontName='Helvetica-Bold', textColor=VERDE)),
     Paragraph('<b>RD$ 438,580.00</b>', E('er', fontSize=8, fontName='Helvetica-Bold',
               textColor=VERDE, alignment=TA_RIGHT))],
]
t_ej = Table(ejemplo_data, colWidths=[11*cm, 5*cm])
t_ej.setStyle(TableStyle([
    ('BACKGROUND',   (0,0),(-1,0), AZUL_MARINO),
    ('TEXTCOLOR',    (0,0),(-1,0), BLANCO),
    ('BACKGROUND',   (0,4),(-1,4), VERDE_CLARO),
    ('ROWBACKGROUNDS',(0,1),(-1,3), [BLANCO, AZUL_CLARO, BLANCO]),
    ('GRID',         (0,0),(-1,-1), 0.3, GRIS_MEDIO),
    ('TOPPADDING',   (0,0),(-1,-1), 4),
    ('BOTTOMPADDING',(0,0),(-1,-1), 4),
    ('LEFTPADDING',  (0,0),(-1,-1), 8),
    ('LINEABOVE',    (0,4),(-1,4), 1, VERDE),
]))
story.append(t_ej)
story.append(Spacer(1, 0.2*cm))

# PASO A: balance sobrante visible
story.append(Paragraph(
    '<b>Paso A — El sistema detecta el balance sobrante</b>', EST_SUBSEC))
story.append(Paragraph(
    'Cuando las facturas aplicadas no cubren el total del instrumento, el sistema '
    'muestra el balance en rojo indicando que hay dinero sin aplicar.', EST_CUERPO))

balance_elems = [
    {'tipo':'card',  'texto':'Detalle del Recibo','x':0.3,'y':0.4,'w':15.2,'h':4.6,'color':AZUL_MARINO},
    # filas del detalle
    {'tipo':'caja',  'texto':'N°  Ticket   Empresa / Cliente                  Monto Aplicado       Estado',
                     'x':0.4,'y':4.0,'w':15.0,'h':0.42,'color':AZUL_MARINO,'fc':BLANCO,'font':6},
    {'tipo':'caja',  'texto':'1   #22167   ALTICE / Juan C. Perez             RD$ 42,780.00        Pagada',
                     'x':0.4,'y':3.5,'w':15.0,'h':0.42,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    {'tipo':'caja',  'texto':'2   #22166   EGE Haina / Maria T. Gomez         RD$ 18,640.00        Pagada',
                     'x':0.4,'y':3.0,'w':15.0,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    # linea separadora
    {'tipo':'linea', 'x':0.4,'y':2.82,'w':14.9},
    # totales
    {'tipo':'caja',  'texto':'Total Instrumento:  RD$ 500,000.00      Total Aplicado:  RD$ 61,420.00',
                     'x':0.4,'y':2.25,'w':15.0,'h':0.45,'color':AZUL_CLARO,'fc':AZUL_MARINO,'font':6},
    {'tipo':'caja',  'texto':'BALANCE SIN APLICAR:   RD$ 438,580.00  ← Este monto debe quedar en cero para confirmar',
                     'x':0.4,'y':1.72,'w':15.0,'h':0.45,'color':colors.HexColor('#f8d7da'),'fc':ROJO,'font':6},
    {'tipo':'btn',   'texto':'+ Abono a Cuenta','x':0.4,'y':1.05,'w':4.5,'h':0.52,'color':ACERO},
    {'tipo':'sub',   'texto':'Haz clic en "+ Abono a Cuenta" para registrar el excedente como credito del cliente',
                     'x':5.2,'y':1.1},
    {'tipo':'btn',   'texto':'CONFIRMAR RECIBO (deshabilitado hasta que balance = 0)',
                     'x':6.5,'y':0.3,'w':9,'h':0.52,'color':GRIS_MEDIO},
]
story.append(PantallaSimulada('Pagos → Registrar Recibo — Balance sobrante detectado',
                               balance_elems, alto=5.2*cm))
story.append(Spacer(1, 0.15*cm))
story.append(alerta(
    'El boton <b>CONFIRMAR RECIBO</b> permanece deshabilitado mientras exista '
    'un balance sin aplicar. Debes agregar un Abono a Cuenta o ajustar los montos.'))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 7 — ABONO A CUENTA (COMO SE REGISTRA Y EFECTO EN EL SISTEMA)
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('3.3', '💳  ABONO A CUENTA — Registro y Efecto en el Sistema'))
story.append(Spacer(1, 0.2*cm))

# PASO B: formulario de abono
story.append(Paragraph(
    '<b>Paso B — Llenar los datos del Abono a Cuenta</b>', EST_SUBSEC))
story.append(Paragraph(
    'Al hacer clic en <b>+ Abono a Cuenta</b>, aparece una fila adicional en el '
    'detalle del recibo donde debes indicar a que cliente y empresa pertenece el '
    'credito, asignarle un N° de ticket y el monto del excedente.', EST_CUERPO))

abono_elems = [
    # detalle con la fila de abono
    {'tipo':'caja',  'texto':'N°  Ticket   Empresa / Cliente                  Monto Aplicado       Estado',
                     'x':0.3,'y':4.6,'w':15.1,'h':0.42,'color':AZUL_MARINO,'fc':BLANCO,'font':6},
    {'tipo':'caja',  'texto':'1   #22167   ALTICE / Juan C. Perez             RD$ 42,780.00        Pagada',
                     'x':0.3,'y':4.1,'w':15.1,'h':0.42,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    {'tipo':'caja',  'texto':'2   #22166   EGE Haina / Maria T. Gomez         RD$ 18,640.00        Pagada',
                     'x':0.3,'y':3.6,'w':15.1,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    # fila de abono (resaltada)
    {'tipo':'caja',  'texto':'3   ABONO A CUENTA — Completar datos abajo:',
                     'x':0.3,'y':3.1,'w':15.1,'h':0.42,'color':colors.HexColor('#fff3cd'),'fc':colors.HexColor('#664d03'),'font':6},
    # formulario abono
    {'tipo':'sub',   'texto':'Compania del abono','x':0.3,'y':2.7,'font':6.5},
    {'tipo':'caja',  'texto':'Lanco Dominicana  (ADCOM001)','x':0.3,'y':2.1,'w':4.8,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Cliente del abono','x':5.3,'y':2.7,'font':6.5},
    {'tipo':'caja',  'texto':'Rosa Mercedes  (ADCTE012)','x':5.3,'y':2.1,'w':4.5,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'N° Ticket asignar','x':10.0,'y':2.7,'font':6.5},
    {'tipo':'caja',  'texto':'#22170','x':10.0,'y':2.1,'w':2.5,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    {'tipo':'sub',   'texto':'Monto abono','x':12.7,'y':2.7,'font':6.5},
    {'tipo':'caja',  'texto':'RD$ 438,580.00','x':12.7,'y':2.1,'w':2.7,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333')},
    # totales actualizados
    {'tipo':'linea', 'x':0.3,'y':1.92,'w':15.0},
    {'tipo':'caja',  'texto':'Total Instrumento:  RD$ 500,000.00      Total Aplicado:  RD$ 500,000.00',
                     'x':0.3,'y':1.35,'w':15.1,'h':0.45,'color':AZUL_CLARO,'fc':AZUL_MARINO,'font':6},
    {'tipo':'caja',  'texto':'BALANCE:   RD$ 0.00  ✓ Listo para confirmar',
                     'x':0.3,'y':0.82,'w':15.1,'h':0.45,'color':VERDE_CLARO,'fc':VERDE,'font':6},
    {'tipo':'btn',   'texto':'CONFIRMAR RECIBO','x':10.5,'y':0.15,'w':5.0,'h':0.55,'color':VERDE},
]
story.append(PantallaSimulada('Pagos → Registrar Recibo — Fila de Abono a Cuenta completada',
                               abono_elems, alto=5.3*cm))
story.append(Spacer(1, 0.15*cm))
story.append(tip(
    'El monto del abono puede ser cualquier valor hasta igualar el balance. '
    'Puedes agregar <b>varios abonos</b> para distintos clientes en el mismo recibo.'))

story.append(Spacer(1, 0.25*cm))

# PASO C: efecto en el sistema
story.append(Paragraph(
    '<b>Paso C — Efecto del Abono a Cuenta en el sistema</b>', EST_SUBSEC))
story.append(Paragraph(
    'Al confirmar el recibo, el sistema crea automaticamente una <b>factura de tipo '
    'CREDITO</b> a nombre del cliente. Esta aparece en la consulta de facturas con '
    'monto negativo y estado especial, indicando que ese cliente tiene saldo a favor.', EST_CUERPO))

efecto_elems = [
    {'tipo':'caja',  'texto':'Ticket   Compania / Cliente                  Monto         Estado         Tipo',
                     'x':0.3,'y':4.6,'w':15.1,'h':0.42,'color':AZUL_MARINO,'fc':BLANCO,'font':6},
    {'tipo':'caja',  'texto':'#22169   Lanco Dom. / Rosa Mercedes           RD$5,188.16   Pendiente      Factura normal',
                     'x':0.3,'y':4.1,'w':15.1,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'caja',  'texto':'#22168   ALTICE / Juan C. Perez               RD$42,780.00  Pagada         Factura normal',
                     'x':0.3,'y':3.6,'w':15.1,'h':0.42,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    # fila de credito (resaltada diferente)
    {'tipo':'caja',  'texto':'#22170   Lanco Dom. / Rosa Mercedes           -RD$438,580   Credito        ABONO A CUENTA',
                     'x':0.3,'y':3.1,'w':15.1,'h':0.42,'color':colors.HexColor('#e8f4fd'),'fc':ACERO,'font':6},
    {'tipo':'badge', 'texto':'CREDITO','x':13.4,'y':3.15,'w':1.9,'h':0.32,'color':ACERO},
    {'tipo':'linea', 'x':0.3,'y':2.92,'w':15.0},
    # explicacion
    {'tipo':'sub',   'texto':'La factura de CREDITO (#22170) representa el saldo a favor de Rosa Mercedes','x':0.3,'y':2.55},
    {'tipo':'sub',   'texto':'para Lanco Dominicana.  Al registrar el proximo pago, este credito aparecera','x':0.3,'y':2.25},
    {'tipo':'sub',   'texto':'disponible y podra aplicarse como parte del recibo.','x':0.3,'y':1.95},
    # flecha y caja de resultado
    {'tipo':'flecha','texto':'▶','x':0.3,'y':1.35,'fc':VERDE},
    {'tipo':'caja',  'texto':'Proximo recibo: la factura #22170 aparece como "Credito disponible" en la lista de pendientes',
                     'x':0.8,'y':1.12,'w':14.6,'h':0.52,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    {'tipo':'caja',  'texto':'Al agregarla al siguiente recibo, el sistema aplica el credito reduciendo el balance a pagar',
                     'x':0.8,'y':0.52,'w':14.6,'h':0.52,'color':AZUL_CLARO,'fc':AZUL_MARINO,'font':6},
]
story.append(PantallaSimulada('Facturacion → Consulta — Factura de Credito creada automaticamente',
                               efecto_elems, alto=5.3*cm))
story.append(Spacer(1, 0.15*cm))
story.append(info(
    'El abono a cuenta <b>NO descuenta dinero</b> de ninguna factura existente. '
    'Crea una nueva entrada de CREDITO que el cliente puede usar en futuros pagos. '
    'El historial queda registrado en el Log de Transacciones.'))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 8 — CONSULTA DE PAGOS
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('4', '🔍  CONSULTA DE PAGOS'))
story.append(Spacer(1, 0.25*cm))

story.append(Paragraph('4.1  Ver, Editar e Imprimir Recibos', EST_SUBSEC))
story.append(Paragraph(
    'En <b>Pagos → Consulta</b> los recibos se muestran agrupados por numero. '
    'Cada grupo se puede expandir para ver el detalle.', EST_CUERPO))

cp_elems = [
    {'tipo':'caja', 'texto':'N° Recibo / Empresa / Cliente...','x':0.3,'y':4.5,'w':9,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#999999')},
    {'tipo':'btn',  'texto':'Filtrar','x':9.5,'y':4.5,'w':2,'h':0.52,'color':AZUL_MARINO},
    {'tipo':'btn',  'texto':'+ Nuevo Recibo','x':13.3,'y':4.5,'w':2.9,'h':0.52,'color':VERDE},
    # recibo 1 expandido
    {'tipo':'caja', 'texto':'REC-20260607-0002  |  Cheque #350515  |  07/06/2026  |  Instrumento: RD$500,000  |  Aplicado: RD$500,000  |  Admin',
                    'x':0.3,'y':3.8,'w':14.2,'h':0.52,'color':AZUL_CLARO,'fc':AZUL_MARINO,'font':6},
    {'tipo':'btn',  'texto':'🖨','x':14.6,'y':3.8,'w':0.6,'h':0.52,'color':AZUL_MEDIO},
    {'tipo':'btn',  'texto':'✏','x':15.3,'y':3.8,'w':0.6,'h':0.52,'color':VERDE},
    # detalle expandido
    {'tipo':'caja', 'texto':'  #   Ticket   Empresa               Cliente          Aplicado       Estado',
                    'x':0.3,'y':3.25,'w':15.1,'h':0.42,'color':AZUL_MARINO,'fc':BLANCO,'font':6},
    {'tipo':'caja', 'texto':'  1   #22167   ALTICE Dom.           Juan C. Perez    RD$42,780.00   Pagada',
                    'x':0.3,'y':2.75,'w':15.1,'h':0.42,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    {'tipo':'caja', 'texto':'  2   #22166   EGE Haina             Maria T. Gomez   RD$18,640.00   Pagada',
                    'x':0.3,'y':2.25,'w':15.1,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'caja', 'texto':'  3   #22170   Lanco Dom.            Rosa Mercedes    -RD$438,580    Credito',
                    'x':0.3,'y':1.75,'w':15.1,'h':0.42,'color':colors.HexColor('#e8f4fd'),'fc':ACERO,'font':6},
    # recibo 2 cerrado
    {'tipo':'caja', 'texto':'REC-20260605-0001  |  Transferencia TRF-2026-0048  |  05/06/2026  |  RD$85,200  |  Admin',
                    'x':0.3,'y':1.2,'w':15.1,'h':0.45,'color':GRIS_CLARO,'fc':colors.HexColor('#666666'),'font':6},
    {'tipo':'sub',  'texto':'Haz clic en el encabezado de un recibo para expandir o colapsar su detalle',
                    'x':0.3,'y':0.55},
]
story.append(PantallaSimulada('Pagos → Consulta de Pagos', cp_elems, alto=5*cm))
story.append(Spacer(1, 0.2*cm))

story.append(Paragraph('4.2  Eliminar un Recibo Completo', EST_SUBSEC))

data_ev = [
    [Paragraph('<b>✏  Editar Recibo</b>',
                E('ev', fontSize=8.5, textColor=BLANCO, fontName='Helvetica-Bold')),
     Paragraph('<b>🗑  Eliminar Recibo</b>',
                E('ev2', fontSize=8.5, textColor=BLANCO, fontName='Helvetica-Bold'))],
    [Paragraph('Haz clic en <b>✏</b>. Podras modificar: '
               'fecha, forma de pago, N° cheque, notas y monto del instrumento. '
               'El detalle de facturas es de solo lectura.', EST_CUERPO),
     Paragraph('En la pantalla de edicion, usa el boton rojo <b>Eliminar Recibo '
               'Completo</b>. El sistema revertira el estado de <b>TODAS</b> las facturas '
               'del recibo a como estaban antes del pago.', EST_CUERPO)],
]
t_ev = Table(data_ev, colWidths=[7.8*cm, 8.2*cm])
t_ev.setStyle(TableStyle([
    ('BACKGROUND',(0,0),(0,0),AZUL_MARINO), ('BACKGROUND',(1,0),(1,0),ROJO),
    ('BACKGROUND',(0,1),(0,1),AZUL_CLARO),  ('BACKGROUND',(1,1),(1,1),colors.HexColor('#fff5f5')),
    ('VALIGN',(0,0),(-1,-1),'TOP'),
    ('TOPPADDING',(0,0),(-1,-1),5), ('BOTTOMPADDING',(0,0),(-1,-1),5),
    ('LEFTPADDING',(0,0),(-1,-1),8), ('GRID',(0,0),(-1,-1),0.4,GRIS_MEDIO),
]))
story.append(t_ev)
story.append(Spacer(1, 0.15*cm))
story.append(tip(
    'Para imprimir el recibo en PDF: haz clic en <b>🖨</b>. Se abre el recibo en '
    'nueva pestana con el logo de la empresa. Presiona <b>Ctrl+P</b> y selecciona '
    '"Guardar como PDF".'))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 9 — REPORTES
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('5', '📊  REPORTES'))
story.append(Spacer(1, 0.25*cm))

story.append(Paragraph('5.1  Tipos de Reportes Disponibles', EST_SUBSEC))

data_rep = [
    [Paragraph('<b>Reporte</b>', E('rh', fontSize=8.5, textColor=BLANCO, fontName='Helvetica-Bold')),
     Paragraph('<b>Descripcion</b>', E('rh2', fontSize=8.5, textColor=BLANCO, fontName='Helvetica-Bold')),
     Paragraph('<b>Filtros principales</b>', E('rh3', fontSize=8.5, textColor=BLANCO, fontName='Helvetica-Bold'))],
    ['Reporte General',      'Todas las facturas con estado y saldos pendientes', 'Periodo, empresa, cliente, estado'],
    ['Por Compania',         'Resumen agrupado y totales por empresa',            'Periodo, empresa'],
    ['Por Cliente',          'Resumen agrupado y totales por cliente',            'Periodo, cliente'],
    ['Por Periodo',          'Analisis de ingresos por rango de fechas',          'Fecha inicio y fin'],
    ['Facturas Pendientes',  'Solo facturas sin cobrar o con pago parcial',       'Empresa, cliente'],
    ['Historial de Pagos',   'Todos los cobros registrados en el sistema',        'Periodo, empresa, cliente, N° Recibo'],
    ['Log de Transacciones', 'Cada operacion del sistema con usuario y fecha',    'Periodo'],
]
t_rep = Table(data_rep, colWidths=[3.8*cm, 7.7*cm, 4.5*cm])
t_rep.setStyle(TableStyle([
    ('BACKGROUND',    (0,0),(-1,0), AZUL_MARINO),
    ('TEXTCOLOR',     (0,0),(-1,0), BLANCO),
    ('FONTNAME',      (0,0),(-1,0), 'Helvetica-Bold'),
    ('FONTSIZE',      (0,0),(-1,-1), 8),
    ('ROWBACKGROUNDS',(0,1),(-1,-1), [BLANCO, AZUL_CLARO]),
    ('GRID',          (0,0),(-1,-1), 0.3, GRIS_MEDIO),
    ('TOPPADDING',    (0,0),(-1,-1), 4), ('BOTTOMPADDING',(0,0),(-1,-1), 4),
    ('LEFTPADDING',   (0,0),(-1,-1), 6),
]))
story.append(t_rep)

story.append(Spacer(1, 0.3*cm))
story.append(Paragraph('5.2  Exportar a Excel e Imprimir', EST_SUBSEC))

rpt_elems = [
    {'tipo':'caja', 'texto':'Desde: 01/06/2026   Hasta: 07/06/2026   Compania: [Todas]   Cliente: [Todos]   N° Recibo: [...]',
                    'x':0.3,'y':4.4,'w':11,'h':0.52,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'btn',  'texto':'Filtrar',       'x':11.5,'y':4.4,'w':1.8,'h':0.52,'color':AZUL_MARINO},
    {'tipo':'btn',  'texto':'Excel',         'x':0.3, 'y':3.7,'w':2.2,'h':0.55,'color':VERDE},
    {'tipo':'btn',  'texto':'Imprimir/PDF',  'x':2.7, 'y':3.7,'w':2.8,'h':0.55,'color':AZUL_MEDIO},
    # KPIs
    {'tipo':'card_num','texto':'Total Cobrado (RD$)', 'x':0.3,'y':1.65,'w':4.5,'h':1.8,'color':VERDE, 'num':'RD$1,248,500'},
    {'tipo':'card_num','texto':'N° de Recibos',       'x':5.0,'y':1.65,'w':3.5,'h':1.8,'color':AZUL_MARINO,'num':'23'},
    {'tipo':'card_num','texto':'Clientes Cobrados',   'x':8.7,'y':1.65,'w':3.4,'h':1.8,'color':AZUL_MEDIO,'num':'15'},
    {'tipo':'card_num','texto':'Pendiente',           'x':12.3,'y':1.65,'w':3.2,'h':1.8,'color':ROJO,'num':'RD$98,420'},
    # tabla historial
    {'tipo':'caja', 'texto':'N° Recibo            Ticket  Empresa         Fecha        Forma      Monto       Usuario',
                    'x':0.3,'y':1.1,'w':15.1,'h':0.42,'color':AZUL_MARINO,'fc':BLANCO,'font':6},
    {'tipo':'caja', 'texto':'REC-20260607-0002    #22167  ALTICE Dom.     07/06/2026   Cheque350515  RD$42,780  Admin',
                    'x':0.3,'y':0.6,'w':15.1,'h':0.42,'color':VERDE_CLARO,'fc':colors.HexColor('#155724'),'font':6},
    {'tipo':'caja', 'texto':'REC-20260607-0002    #22166  EGE Haina       07/06/2026   Cheque350515  RD$18,640  Admin',
                    'x':0.3,'y':0.1,'w':15.1,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
]
story.append(PantallaSimulada('Reportes → Historial de Pagos', rpt_elems, alto=5*cm))
story.append(Spacer(1, 0.15*cm))

for n, t in [
    (1,'Selecciona el tipo de reporte en el menu <b>Reportes</b>'),
    (2,'Establece los filtros: rango de fechas, empresa, cliente o N° recibo'),
    (3,'Haz clic en <b>Filtrar</b> para aplicar y ver los resultados con KPIs'),
    (4,'Descarga en <b>Excel</b> o imprime en PDF con Ctrl+P'),
]:
    story.append(paso(n, t))
story.append(PageBreak())

# ─────────────────────────────────────────────────────────────────────────────
# PAG 10 — MANTENIMIENTO (ADMIN)
# ─────────────────────────────────────────────────────────────────────────────
story.append(encabezado_seccion('6', '⚙  MANTENIMIENTO  (Solo Administrador)'))
story.append(Spacer(1, 0.2*cm))
story.append(alerta(
    'Esta seccion solo es visible para usuarios con perfil <b>Administrador</b>. '
    'Los cambios aqui afectan la configuracion global del sistema.'))
story.append(Spacer(1, 0.2*cm))

mant_elems = [
    {'tipo':'card','texto':'Companias',   'x':0.2,'y':3.1,'w':3.7,'h':2.1,'color':AZUL_MARINO},
    {'tipo':'card','texto':'Clientes',    'x':4.1,'y':3.1,'w':3.7,'h':2.1,'color':AZUL_MEDIO},
    {'tipo':'card','texto':'Importar',    'x':8.0,'y':3.1,'w':3.7,'h':2.1,'color':VERDE},
    {'tipo':'card','texto':'Backup',      'x':11.9,'y':3.1,'w':3.5,'h':2.1,'color':NARANJA},
    # datos de companias
    {'tipo':'caja','texto':'ADCOM001  Lanco Dominicana   RNC:131-12345-6   (809)555-0101','x':0.3,'y':2.6,'w':3.6,'h':0.38,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':5.5},
    {'tipo':'caja','texto':'ADCOM002  ALTICE Dom.        RNC:131-67890-1   (809)555-0202','x':0.3,'y':2.15,'w':3.6,'h':0.38,'color':AZUL_CLARO,'fc':colors.HexColor('#333333'),'font':5.5},
    # datos de clientes
    {'tipo':'caja','texto':'ADCTE012  Rosa Mercedes    C:001-1234567-8','x':4.2,'y':2.6,'w':3.6,'h':0.38,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':5.5},
    {'tipo':'caja','texto':'ADCTE013  Juan C. Perez    C:001-9876543-2','x':4.2,'y':2.15,'w':3.6,'h':0.38,'color':AZUL_CLARO,'fc':colors.HexColor('#333333'),'font':5.5},
    # botones
    {'tipo':'btn','texto':'+ Nueva Compania','x':0.3,'y':3.3,'w':3.4,'h':0.42,'color':AZUL_MARINO},
    {'tipo':'btn','texto':'+ Nuevo Cliente', 'x':4.2,'y':3.3,'w':3.4,'h':0.42,'color':AZUL_MEDIO},
    {'tipo':'btn','texto':'Importar Excel',  'x':8.1,'y':3.3,'w':3.4,'h':0.42,'color':VERDE},
    {'tipo':'btn','texto':'Crear Backup',    'x':12.0,'y':3.3,'w':3.2,'h':0.42,'color':NARANJA},
    {'tipo':'sub','texto':'Administracion → Usuarios: gestionar accesos y perfiles del sistema','x':0.3,'y':1.3},
    {'tipo':'linea','x':0.3,'y':1.2,'w':15.0},
    {'tipo':'caja','texto':'USR001  Fanny Mercedes  [Administrador]  Estado: Activo','x':0.3,'y':0.65,'w':7.2,'h':0.42,'color':AZUL_CLARO,'fc':AZUL_MARINO,'font':6},
    {'tipo':'caja','texto':'USR002  Rosa Admin      [Operativo]      Estado: Activo','x':7.7,'y':0.65,'w':7.2,'h':0.42,'color':BLANCO,'fc':colors.HexColor('#333333'),'font':6},
    {'tipo':'btn', 'texto':'+ Nuevo Usuario','x':0.3,'y':0.1,'w':3.4,'h':0.42,'color':AZUL_MARINO},
]
story.append(PantallaSimulada('Mantenimiento — Vista General (Administrador)', mant_elems, alto=4.4*cm))
story.append(Spacer(1, 0.2*cm))

data_mant = [
    [Paragraph('<b>Seccion</b>', E('mh', fontSize=8.5, textColor=BLANCO, fontName='Helvetica-Bold')),
     Paragraph('<b>Que puedes hacer</b>', E('mh2', fontSize=8.5, textColor=BLANCO, fontName='Helvetica-Bold'))],
    ['6.1  Companias',
     'Agregar, editar o desactivar companias. Cada una tiene ID unico (ADCOM###), '
     'RNC, telefono, correo y condicion de pago en dias.'],
    ['6.2  Clientes',
     'Agregar, editar o desactivar clientes. ID unico (ADCTE###), '
     'nombre completo, cedula, telefono y correo.'],
    ['6.3  Importar Datos',
     'Cargar facturas y pagos historicos desde un archivo Excel con el formato predefinido.'],
    ['6.4  Backup',
     'Crear copias de seguridad, restaurar a un punto anterior y exportar Excel completo. '
     'El sistema guarda un backup automatico antes de cada restauracion.'],
    ['6.5  Usuarios',
     'Crear nuevos usuarios, asignar perfil (Administrador / Operativo) '
     'y cambiar contrasenas.'],
]
t_m = Table(data_mant, colWidths=[4*cm, 12*cm])
t_m.setStyle(TableStyle([
    ('BACKGROUND',    (0,0),(-1,0), AZUL_MARINO),
    ('TEXTCOLOR',     (0,0),(-1,0), BLANCO),
    ('FONTNAME',      (0,0),(-1,0), 'Helvetica-Bold'),
    ('FONTSIZE',      (0,0),(-1,-1), 8),
    ('ROWBACKGROUNDS',(0,1),(-1,-1), [BLANCO, AZUL_CLARO]),
    ('GRID',          (0,0),(-1,-1), 0.3, GRIS_MEDIO),
    ('TOPPADDING',    (0,0),(-1,-1), 5), ('BOTTOMPADDING',(0,0),(-1,-1), 5),
    ('LEFTPADDING',   (0,0),(-1,-1), 6),
    ('VALIGN',        (0,0),(-1,-1), 'TOP'),
    ('FONTNAME',      (0,1),(0,-1), 'Helvetica-Bold'),
    ('TEXTCOLOR',     (0,1),(0,-1), AZUL_MARINO),
]))
story.append(t_m)

story.append(Spacer(1, 0.3*cm))

cierre = Table([[Paragraph(
    '<font color="white"><b>Necesitas ayuda?</b>  '
    'Contacta al administrador del sistema: <b>r.mercedesb@gmail.com</b>  |  '
    'Powered by AD ALL DIGITAL ENTERPRISE  v1.1</font>',
    E('ci', fontSize=8, textColor=BLANCO, fontName='Helvetica', alignment=TA_CENTER))]],
    colWidths=[16*cm])
cierre.setStyle(TableStyle([
    ('BACKGROUND',(0,0),(-1,-1),AZUL_MARINO),
    ('TOPPADDING',(0,0),(-1,-1),8), ('BOTTOMPADDING',(0,0),(-1,-1),8),
]))
story.append(cierre)

# ─────────────────────────────────────────────────────────────────────────────
os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
doc.build(story, onFirstPage=numeracion.pie, onLaterPages=numeracion.pie)
print(f"PDF generado: {OUTPUT}")
