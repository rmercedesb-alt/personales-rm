# -*- coding: utf-8 -*-
# AM Comercial - Cierre SEPTIEMBRE 2026. Fuente: Movil Business. Base: venta NETA sin ITBIS (ValorBruto). Excluye Inversiones Caobo.
MES, ANIO = 9, 2026
MES_NOMBRE="Septiembre"; MES_ABBR="Sep"; TRIM="Jul–Ago–Sep"; TRIM_MESES=[7,8,9]
FECHA_CALC = "1 de octubre de 2026"
DIAS_HABILES_JUL = 26  # septiembre 2026: 30 dias - 4 domingos (6,13,20,27)

serie_2026 = {  # mes:(SDO,Azua)
 1:(38306506.49,35384155.85),2:(32531572.57,39680163.45),3:(37370107.90,47410204.02),
 4:(30209723.14,43785247.46),5:(30343919.73,43950564.66),6:(31834904.44,44865653.65),
 7:(29657239.20,45094500.74),8:(30487779.07,43968343.27),9:(33026307.65,44293689.13)}
serie_2025 = {
 1:(41336822.32,24534117.47),2:(37383137.16,23761945.12),3:(40779931.86,28236235.72),
 4:(38685941.24,25779079.50),5:(40220165.64,28414242.25),6:(36412615.18,23405854.48),
 7:(37793703.45,25103303.74),8:(36960678.47,23958889.81),9:(41473281.29,24602185.77),
 10:(37765926.75,25674242.03),11:(34654344.56,27966191.57),12:(36635876.63,26415637.11)}

presu_jul = {  # Septiembre (presupuesto, ejecutado)
 ("Santo Domingo","Institucional"):(2974001.97,4233712.52),
 ("Santo Domingo","Mayorista"):(6078592.38,6628181.82),
 ("Santo Domingo","Minorista"):(28429116.56,26361416.44),
 ("Azua","Mayorista"):(13655788.95,9869692.34),
 ("Azua","Minorista"):(37400949.16,34518172.81)}

cat_sdo=[("NOSOTRAS",11161023.68),("REFISAL",4926814.69),("AGUA",3591787.16),("FAMILIA",2111753.13),
 ("BOLONES",1988400.65),("BLANCOX",1711139.04),("INSECTICIDAS",1378753.25),("DIMAR",1325455.95),
 ("CARAMELOS DUROS",1188537.72),("NOSOTRAS DIARIOS",773978.47),("CUIDADO DE BEBE",560047.26),
 ("GRISSLY SNACK",357727.69),("PALETAS",327936.68),("CONFITERIA CICHLET",248585.55),("Snack",205221.65),
 ("ALIMENTOS",179549.48),("ILUMINACION",161609.96),("Otros",600000.0)]
cat_azua=[("PASTAS",11666293.68),("GALLETAS",6989219.83),("NOSOTRAS",5584513.79),("REFISAL",3048015.76),
 ("BOLONES",1770293.93),("HARINAS FAMILIARES",1725673.54),("REXONA",1666382.55),("DOVE",1382846.51),
 ("PONDS",1209829.60),("FAMILIA",1206034.99),("INSECTICIDAS",943462.99),("DIMAR",875685.72),
 ("CARAMELOS DUROS",765593.30),("SEDAL",699464.20),("MAIZENA",631661.28),("BLANCOX",493348.92),
 ("NOSOTRAS DIARIOS",405163.06),("MASCOTAS",356556.75),("Otros",2600000.0)]

# ABCD meses 7,8,9 (NOTA: el campo TamañoCliente fue re-segmentado; el tier A creció vs cortes previos)
abcd = {
 "Santo Domingo":{
  7:{"A":(21605730.43,5766),"B":(5262337.40,2302),"C":(1125660.06,571),"D":(1261222.23,211),"SC":(225990.09,29)},
  8:{"A":(19377480.97,5614),"B":(6499572.62,2307),"C":(2282463.19,667),"D":(1953985.74,405),"SC":(283696.73,25)},
  9:{"A":(21979770.68,5695),"B":(6277116.59,2443),"C":(2330994.06,711),"D":(2075594.81,501),"SC":(236988.15,32)}},
 "Azua":{
  7:{"A":(34081838.27,2654),"B":(7295294.04,1814),"C":(1682692.48,636),"D":(1459671.28,281),"SC":(575004.67,5)},
  8:{"A":(31925842.28,2626),"B":(8496423.39,1778),"C":(1798658.90,608),"D":(1604478.24,313),"SC":(142940.46,4)},
  9:{"A":(31985692.39,2639),"B":(8298241.22,1807),"C":(2218484.17,652),"D":(1344840.65,348),"SC":(446430.70,6)}}}

prov_jul = {  # Septiembre (nombre, neto, clientes, facturas) - lineas positivas
 "Santo Domingo":[("Continental de Negocios",15502620.92,7034,10668),("Brinsa Dominicana",6867452.48,5371,7444),
  ("COLOMBINA",4621558.06,6364,9514),("Agua Crystal",3706653.99,1532,2082),("DIMAR DOMINICANA",1603697.55,1505,1816),
  ("Comercial Fila",1562685.39,1054,1141),("Corporacion Poiroto",225153.87,633,748),("Pimentel & Co.",190190.00,112,126),
  ("AMPOULE",174694.26,221,234),("P.Q.P.",161532.42,209,268),("GALISA GROUP",63990.22,46,60),("ETERNA",36694.50,90,94)],
 "Azua":[("MOLINOS MODERNOS",22043011.89,4642,10843),("Continental de Negocios",8036303.60,3450,5592),
  ("Cesar Iglesias (Unilever)",6987994.58,112,156),("COLOMBINA",3905019.35,3360,5601),("Brinsa Dominicana",3751071.50,2171,3046),
  ("Comercial Fila",1050347.51,632,701),("DIMAR DOMINICANA",1025816.41,1034,1239),("Corporacion Poiroto",345840.43,445,556),
  ("P.Q.P.",203166.98,303,356),("AMPOULE",103267.07,147,156),("MAXIMO GOMEZ",19507.56,29,30),("ETERNA",17651.67,64,66)]}

# ventas Sep: code -> (centro, grupo, nombre, neto)
_vt={
 "25":("Azua","15","Jhonny Rosario (May)",6423339.51),"36":("Azua","15","Hanel Giron",4839122.26),
 "206":("Azua","11","Jonathan Contreras",3632485.07),"6":("Azua","11","Humberto Herrera",1956199.79),
 "112":("Azua","11","Ruben D. Perez",1771923.70),"111":("Azua","11","Alejandro Solano",1725055.74),
 "130":("Azua","11","Alexander Bocio",1716186.07),"157":("Azua","11","Odalis Garcias",1712351.66),
 "129":("Azua","12","Elian Rosario",1666879.86),"132":("Azua","12","Juan Francisco Felix",1621858.90),
 "113":("Azua","11","Rosa M. Ogando",1550576.50),"162":("Azua","11","Onny Gomez Piña",1498041.35),
 "158":("Azua","11","Francisco J. de los Santos",1433183.08),"114":("Azua","11","Franklin O. Castillo",1373938.75),
 "88":("Azua","11","Cristofer Joaquin Nuñez",1325438.63),"207":("Azua","11","Ronny Perez",1258280.73),
 "128":("Azua","12","Paul de los Santos",1257845.20),"115":("Azua","11","Yermin Rosario",1245096.94),
 "77":("Azua","12","Lisardo A. Placencio",1226246.67),"155":("Azua","12","Jhostin Olivo",1158784.74),
 "160":("Azua","12","Manuel Donal Tejada",1067436.29),"74":("Azua","12","Winston Rosario",943995.92),
 "118":("Azua","12","Gustavo Montero",942088.16),"98":("Azua","12","Jefry Candelario",939771.10),
 "76":("Azua","12","Julio Cesar Sosa",889694.72),"131":("Azua","12","Brayan Lopez",888944.77),
 "24":("Azua","12","Ezequiel Beltre",885471.54),"181":("Azua","12","Kelvin Bladimir Muñoz",707415.20),
 "106":("Santo Domingo","15","Wascar Alvarez (May)",6770296.59),"78":("Santo Domingo","10","Angel Rodriguez",1645891.59),
 "102":("Santo Domingo","10","Joan M. Liriano",1565636.39),"84":("Santo Domingo","10","Miguel A. Berroa",1511982.26),
 "198":("Santo Domingo","10","Sandra Reyes Polanco",1426226.27),"199":("Santo Domingo","10","Giancarlo Tavarez",1356823.78),
 "43":("Santo Domingo","10","Melvin Mariñez",1316791.87),"50":("Santo Domingo","10","Katherine Moronta",1217880.21),
 "97":("Santo Domingo","10","Heriberto Nuñez",1213640.02),"56":("Santo Domingo","10","Ronny Perez (SDO)",1115335.90),
 "7":("Santo Domingo","10","Antonio Delgado",1110727.89),"8":("Santo Domingo","10","Maria del Carmen",1103626.74),
 "94":("Santo Domingo","10","Vicente Cruz",1084209.25),"14":("Santo Domingo","10","Bernabet Reyes",1069175.36),
 "60":("Santo Domingo","10","Martina Ramirez",1067664.35),"85":("Santo Domingo","10","Josue Fabian",1057568.85),
 "18":("Santo Domingo","10","Irvin de los Santos",1000768.70),"93":("Santo Domingo","10","Junior M. Ramos",993144.97),
 "91":("Santo Domingo","10","Enrrique Morillo",954769.18),"66":("Santo Domingo","10","Miguel Acosta",951142.27),
 "185":("Santo Domingo","20","Fernando Martinez (MiniMkt)",936528.16),"11":("Santo Domingo","10","Yeuri Montaño",927811.85),
 "72":("Santo Domingo","10","Carla Valdez",879846.58),"70":("Santo Domingo","10","Dilcio Navarro",852231.09),
 "80":("Santo Domingo","10","Anderson Gomez",803567.76),"59":("Santo Domingo","10","Wilson M. Quezada",784989.84)}
# presupuesto Sep: code -> (cuota, ejec)
_pr={"25":(7481272.59,5656798.24),"36":(6174516.36,4212894.10),"206":(3399239.77,3484806.50),"112":(2164615.74,1725271.75),
 "132":(1933665.75,1501613.73),"130":(1915002.41,1677745.30),"111":(1833925.86,1664516.77),"157":(1811072.77,1654496.63),
 "6":(1726095.13,1905290.67),"129":(1611683.72,1572376.51),"88":(1608050.40,1269202.70),"113":(1597162.90,1447314.90),
 "162":(1523780.50,1430751.81),"114":(1433182.99,1280367.06),"115":(1395790.23,1114270.56),"158":(1377238.16,1396176.73),
 "207":(1373794.86,1182133.38),"128":(1333584.37,1216593.57),"155":(1187814.62,1070977.01),"77":(1022534.72,1096787.61),
 "160":(1019207.47,1052837.30),"131":(1000265.70,840097.85),"24":(947839.61,853946.20),"118":(920924.53,921840.60),
 "181":(855107.52,676495.89),"98":(834412.93,887404.02),"74":(823670.62,809568.61),"76":(751285.88,785289.15),
 "106":(6078592.38,6628181.82),"102":(1580919.91,1508484.64),"78":(1481139.92,1596981.12),"185":(1460759.29,837694.83),
 "84":(1375058.67,1468693.89),"97":(1295150.04,1163699.59),"198":(1236208.13,1360998.53),"199":(1186223.03,1285000.33),
 "7":(1178253.00,1007816.12),"43":(1160353.39,1259404.50),"85":(1142730.49,958233.57),"72":(1140394.76,748361.43),
 "11":(1127998.75,871844.78),"59":(1093881.27,726708.77),"66":(1087012.65,905731.80),"94":(1040556.05,1030395.49),
 "8":(1031488.96,1054548.74),"70":(1009890.71,791561.66),"50":(1001350.17,1137112.55),"60":(995920.60,1036604.71),
 "14":(993344.92,1023906.80),"56":(991973.64,987329.88),"18":(980046.36,934267.91),"93":(963416.19,962523.73),
 "91":(953910.20,925175.75),"80":(921135.46,778335.32)}
vendedores=[]
for code,(cuota,ejec) in _pr.items():
    if code in _vt:
        centro,grupo,name,_=_vt[code]
    else:
        centro="Santo Domingo" if _pr[code][0]>0 else "Azua"; grupo="10"; name="Cod "+code
    vendedores.append((code,centro,grupo,name,cuota,ejec))

colombina_sdo=[("BBB Cherry Jumbo",995737.49,2435),("Coffee Delight",586583.75,2194),("BBB Fresa Jumbo",422293.04,887),
 ("BBB Surtido Jumbo",380282.71,1428),("Tipitin Besos",236557.99,1415)]
colombina_azua=[("BBB Fresa Jumbo",685789.68,1448),("BBB Surtido Jumbo",395624.83,984),("BBB Cherry Jumbo",311169.56,451),
 ("Coffee Delight",271528.50,1011),("Tutti Frutti Mix",225386.32,377)]
colombina_total_sdo=4621558.06; colombina_total_azua=3905019.35

# Molinos (linea nueva desde ene-2026; en sep-2025 = RD$0) - para el puente
tot_jul25=66075467.06; tot_jul26=77319996.78
mol_jul26=21041245.89+412.59
sd_base=33026307.65-41473281.29-412.59
azua_base=44293689.13-24602185.77-21041245.89

# Efectividad Sep (presencial): code -> (realizadas, con_pedido)
efect={"206":(526,333),"97":(682,319),"198":(621,308),"102":(586,268),"111":(464,262),"157":(464,261),"56":(572,261),
 "7":(577,260),"78":(532,256),"112":(514,255),"155":(582,254),"199":(671,253),"50":(600,252),"158":(443,248),
 "14":(522,246),"162":(558,236),"18":(573,233),"94":(414,233),"207":(559,229),"129":(506,226),"43":(504,225),
 "6":(371,224),"114":(380,224),"132":(523,223),"72":(553,219),"66":(633,216),"77":(426,215),"85":(599,215),
 "160":(426,214),"93":(553,213),"88":(448,211),"74":(405,205),"131":(584,205),"113":(360,205),"115":(419,203),
 "60":(526,201),"80":(561,200),"8":(474,196),"11":(561,185),"24":(542,182),"128":(321,181),"98":(340,178),
 "181":(520,177),"59":(568,177),"91":(552,175),"70":(463,174),"118":(410,172),"130":(388,168),"76":(308,137),
 "84":(184,106),"185":(156,57),"106":(107,46),"25":(77,31),"36":(98,25)}
cartera_dist={"Santo Domingo":13654,"Azua":5472}

def _actnum(centro):
    return sum(abcd[centro][9][s][1] for s in ["A","B","C","D","SC"])
_cenmap={c[0]:c[1] for c in vendedores}
def _ind(centro,grupos):
    codes=[code for code,(ce,gr,nm,nt) in _vt.items() if ce==centro and gr in grupos]
    real=sum(efect[c][0] for c in codes if c in efect); ped=sum(efect[c][1] for c in codes if c in efect)
    cart=cartera_dist[centro]; act=_actnum(centro)
    return {"efectividad":ped/real,"activacion":act/cart,"cartera":cart,"activos":act,"realizadas":real,"conped":ped}
ind_op={"Santo Domingo":_ind("Santo Domingo",("10","15","20")),"Azua":_ind("Azua",("11","12","15"))}

# ================== CONFIG DE MES (lo que Claude ajusta cada cierre) ==================
DIAS_HABILES = DIAS_HABILES_JUL  # alias claro
GRUPOS_SDO = {"10","15","20"}          # fuerza minorista SDO + mayorista(15) + minimarket(20)
GRUPOS_AZUA = {"11","12","15"}         # FFVV2(11) + FFVV1(12) + mayorista(15); confirmar códigos cada mes
FFVV_AZUA = [("12","FFVV 1 · Minorista 1"),("11","FFVV 2 · Minorista 2 (Molinos)"),("15","Mayorista (Cristiano)")]
GRPNAME = {"10":"Minorista 1","11":"Minorista 2 (Molinos)","12":"Minorista 1","15":"Mayorista","20":"MiniMarket"}

# Narrativa del mes (tokens {G}{Y}{R} = semáforo verde/ámbar/rojo). Claude la reescribe según las señales reales.
NARR = {
 "CAPA1_INTRO":"Esta capa da el pulso del negocio total y de cada CEDIS: cuánto se vendió, cómo cerró contra el forecast y contra el año pasado, y qué tan sano es ese resultado una vez aislada la línea nueva de Molinos. Léala de arriba hacia abajo: primero los KPIs, luego el puente que explica el crecimiento, y al final la tendencia y el cumplimiento por centro.",
 "CAP_KPI":"Total AM cerró en 90.4% del forecast ({Y}). El +17.0% reportado es portafolio: entra Molinos (línea nueva) y salen galletería y marcas descontinuadas. Comparando el MISMO portafolio (pera con pera), la base cae solo −6.1% interanual — Santo Domingo −12.5% (el punto débil) y Azua +4.7% (el negocio continuo crece).",
 "CAP_BRIDGE":"De izquierda a derecha: de los RD$66.1M de sep-2025 se restan RD$6.3M de líneas que ya no existen (galletería, Nestlé/Purina y otras marcas salidas); el negocio continuo cae RD$3.7M (−6.1% pera con pera); entran RD$21.2M de líneas nuevas (Molinos) y se llega a los RD$77.3M reportados. La caída real de la base es moderada (−6%), no −15%: el grueso del movimiento es cambio de portafolio, no pérdida de tracción.",
 "CAP_SERIES":"Izquierda: Santo Domingo (azul) repunta en septiembre (RD$33.0M, su mejor mes del semestre) mientras Azua (verde) se aplana. Derecha: contra presupuesto ambos quedan en amarillo (SDO 95.6%, Azua 86.9%); el Mayorista de Azua (72%) es el punto rojo del mes.",
 "CAPS":{
  "Santo Domingo":{
   "intro":"Santo Domingo es una sola fuerza de venta. Viene recuperándose mes a mes (RD$29.7M → 30.5M → 33.0M) aunque todavía cierra ~20% por debajo del año pasado. Esta sección abre su operación para ver si la recuperación tiene base real.",
   "op":"SD mueve RD$33.0M (RD$1.27M por día hábil) con ticket de ~RD$966. La activación de cartera sube a 68.7% (venía en 64.8%) — buena señal de reactivación — pero su efectividad de visita (41.2%) sigue siendo la más baja: aún cierra poco por visita.",
   "prov":"SD depende fuerte de Continental (~43% del centro) y de la categoría Nosotras; Agua Crystal y Colombina recuperan algo de terreno. La concentración en pocos proveedores sigue siendo el riesgo estructural.",
   "abcd":"El tier A concentra la venta (la re-segmentación reciente del maestro infló ese tramo; no comparar con cortes viejos). La campana mejora apenas: 10 de 26 vendedores sobre cuota (media 93.5%). Cumplimiento aún débil y transversal."},
  "Azua":{
   "intro":"Azua opera con dos fuerzas de venta — FFVV 1 (grupo 12, Minorista 1) y FFVV 2 (grupo 11, Minorista 2 / Molinos) — más mayorista. Venía siendo el motor del año, pero en septiembre quedó en 86.9% del forecast: esta sección busca dónde aflojó.",
   "op":"Azua mueve RD$44.3M (RD$1.70M por día) con activación casi total (99.6%) y la mejor efectividad de visita (47.5%). El problema no es cobertura: el mes quedó corto frente a un presupuesto más alto, sobre todo en el canal Mayorista.",
   "prov":"Molinos (~44% del centro) sigue siendo el motor pero es la línea nueva; Cesar Iglesias/Unilever baja a RD$7.0M (venía ~RD$8M) y Continental sube. La alta dependencia de Molinos es el punto a cuidar.",
   "abcd":"El tier A concentra casi toda la venta (re-segmentación del maestro). La campana se deterioró fuerte: solo 8 de 28 vendedores sobre cuota (media 91.3%, vs 22/28 en julio). El presupuesto subió y la fuerza no lo alcanzó."}},
 "SIG_SDO":"Señal SDO: {Y} — 95.6% del forecast y tres meses consecutivos al alza, pero aún −20.4% interanual y solo 10/26 sobre cuota. Recuperación incipiente: hay que sostenerla y seguir reactivando la cartera dormida (31%).",
 "SIG_AZUA":"Señal Azua: {Y} — 86.9% del forecast (Mayorista 72% {R}) y la campana cayó a 8/28 sobre cuota. Pero en pera con pera el negocio continuo de Azua CRECE +4.7% (ex-Molinos y ex-marcas salidas): el problema del mes es cumplimiento vs un presupuesto más alto, no el portafolio base. Revisar Mayorista y calibración de cuotas.",
 "FFVV_CAP":"FFVV 2 (grupo 11, Minorista 2 / Molinos) sigue siendo la fuerza más grande (RD$21.2M); FFVV 1 (grupo 12) aporta RD$13.3M y el mayorista RD$9.9M. El peso del centro sigue en la línea nueva — por eso cuando Molinos no acelera, Azua se aplana.",
 "COLO_INTRO":"Colombina es la marca de mayor exposición minorista y el proveedor con más SKU en ruta. Aquí se ve dónde se concentra su venta y qué tan dependiente es de unos pocos productos.",
 "COLO_CAP":"El Top 5 es ~48% de Colombina en SD y ~43% en Azua: alta concentración en BBB, Coffee Delight y Tipitin. Sin galletería (fuera desde mar-2026), el crecimiento descansa en BBB/Grissly — vigilar sell-out y la sobre-colocación de exhibidores Grissly, que llevan incentivo por exhibidor colocado.",
 "CIERRE":[
  ("{Y} Azua aflojó","86.9% forecast (Mayorista 72% {R}); la campana cayó a 8/28 sobre cuota. El motor del año perdió fuerza este mes","Revisar cartera y cuotas del Mayorista (Jhonny, Hanel) y la caída de cumplimiento de la FFVV","Rudy / Sup. Azua","10-oct"),
  ("{Y} Base like-for-like","Comparando el mismo portafolio (ex-galletería, ex-marcas salidas, ex-Molinos), la base cae solo −6.1% interanual (SDO −12.5%, Azua +4.7%), no −15%","Monitorear SDO, el único en caída real; el +17% reportado es portafolio (Molinos entra, galletería/marcas salen), no tracción de base","Rudy / GM","15-oct"),
  ("{G} SDO recupera","95.6% forecast y 3 meses al alza; activación sube a 68.7%. Aún −20.4% interanual","Sostener la recuperación; seguir reactivando la cartera dormida (31%)","Rudy / Sup. SDO","Continuo"),
  ("{R} Mayorista Azua","Canal Mayorista de Azua en 72% del forecast — el más flojo del mes","Revisar quiebres de inventario y cartera mayorista en Azua","Compras / Comercial","10-oct"),
  ("{Y} Cuotas vs real","Cumplimiento débil en ambos (SDO 10/26, Azua 8/28); el presupuesto subió y la fuerza no lo alcanza","Validar si las cuotas 2026 están calibradas a la operación real por vendedor","Comercial","15-oct")]}

# ================== COMPARABLE "PERA CON PERA" (like-for-like) ==================
# Excluye de AMBOS años: galletería (discontinuada mar-2026) y marcas/suplidores que salieron del portafolio,
# y del 2026 las líneas NUEVAS (Molinos, Pimentel). Así se compara el mismo portafolio en ambos años.
CMP_2025={"Santo Domingo":37536718.19,"Azua":22200682.86}   # Sep 2025 portafolio continuo
CMP_2026={"Santo Domingo":32830378.44,"Azua":23253782.94}   # Sep 2026 portafolio continuo
DESCONTINUADAS=["GALLETERIA (Colombina)","Nestlé/Purina","F&T Industries","Grupo Rodríguez/Chupetín","Congrupo","Domica","Máximo Gómez"]
NUEVAS_2026=["Molinos Modernos","Pimentel & Co."]
# Puente like-for-like (millones): 2025 reportado -> -descontinuadas -> Δ base continua -> +nuevas -> 2026 reportado
_r25=tot_jul25; _r26=tot_jul26
_cmp25=sum(CMP_2025.values()); _cmp26=sum(CMP_2026.values())
LFL = {"r2025":_r25,"desc":_cmp25-_r25,"base":_cmp26-_cmp25,"nuevas":_r26-_cmp26,"r2026":_r26,
       "yoy_total":_cmp26/_cmp25-1,
       "yoy_sdo":CMP_2026["Santo Domingo"]/CMP_2025["Santo Domingo"]-1,
       "yoy_azua":CMP_2026["Azua"]/CMP_2025["Azua"]-1}

# ================== APÉNDICE: VENTAS A INVERSIONES CAOBO (intercompañía) por suplidor ==================
# EXCEPCIÓN a la regla de excluir Caobo: aquí SÍ se muestra, abierto por suplidor, para Presidencia.
# (suplidor, neto {ANIO}, con ITBIS {ANIO}, neto {ANIO_ANT})
caobo_prov=[("CONGRUPO",2404554.72,2837375.0,3009645.32),("ETERNA",1703154.44,2009724.0,923853.12),("Agua Crystal",160.0,189.0,0.0)]
NARR["CAOBO_INTRO"]="Apéndice para Presidencia: ventas intercompañía a Inversiones Caobo (que el resto del informe EXCLUYE), abiertas por suplidor. Caobo revende estas líneas a supermercados y cadenas nacionales."
NARR["CAOBO_CAP"]="En septiembre AM facturó RD$4.1M neto a Caobo (RD$4.8M con ITBIS), +4.4% vs 2025. Se concentra en CONGRUPO (RD$2.4M) y ETERNA (RD$1.7M)."
