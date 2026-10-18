/* =============================================================================
   REBAJA presupuesto OCTUBRE 2026 — Vendedor 36 (Hanel Girón) — Cesar Iglesias
   Motivo: 3 clientes mayoristas ya no pueden comprar Cesar Iglesias:
           Supermercado Saomy SRL (25568), Supermercado INES (3388), Supermercado INES 2 (37422).
   Rebaja = promedio mensual de unidades Ene–Ago 2026 de esos 3 clientes, por producto
            (tope: el presupuesto de octubre del producto; nunca queda negativo).
   -----------------------------------------------------------------------------
   Tabla: dbo.PresupuestosHistoricos  |  RepCodigo='36', PreAnio=2026, PreMes=10
     - 'Unddprod'   (unidades)       : se le resta la rebaja.
     - 'MNTDPROD'   (RD$ por producto): se ajusta en la misma proporción (@AjustarMontoYCajas=1).
     - 'UNDDPRODCJ' (cajas)          : se ajusta en la misma proporción (@AjustarMontoYCajas=1).
   PreTipo se filtra con COLLATE Latin1_General_CS_AS: la tabla también tiene filas 'UNDDPROD'
   (mayúsculas) que la intercalación normal confundiría con 'Unddprod'.

   Esperado al aplicar (verificado 09-oct-2026, solo lectura):
     Unddprod Rep 36 oct : 47,392.84 -> 38,848.81 unidades  (176 productos)
     MNTDPROD Rep 36 oct : RD$6,439,354.94 -> RD$5,479,745.30
   USO: correr con @Aplicar = 0 (vista previa, ROLLBACK). Si cuadra, @Aplicar = 1.
   Después: correr sp_PresupuestosPRODActualizar para que MNTDCAT / MNTDCAT3VEN / MNTGVEN
   (totales por categoría/suplidor/vendedor) reflejen el cambio.
   ============================================================================= */
USE [MovilBusiness5STD];   -- base de producción (sus columnas usan Modern_Spanish_CI_AS)
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Rep varchar(15) = '36';
DECLARE @Anio smallint = 2026;
DECLARE @Mes smallint = 10;
DECLARE @Usuario varchar(64) = 'rmercedes';
DECLARE @AjustarMontoYCajas bit = 1;   -- 1 = también MNTDPROD y UNDDPRODCJ en proporción
DECLARE @Aplicar bit = 0;              -- 0 = vista previa (ROLLBACK), 1 = aplicar (COMMIT)

IF OBJECT_ID('tempdb..#Rebaja') IS NOT NULL DROP TABLE #Rebaja;
-- La columna se fija en Modern_Spanish_CI_AS (la de PresupuestosHistoricos): tempdb/servidor usan SQL_Latin1_General_CP1_CI_AS
CREATE TABLE #Rebaja (ProID varchar(20) COLLATE Modern_Spanish_CI_AS NOT NULL PRIMARY KEY, Rebaja decimal(18,2) NOT NULL);
INSERT INTO #Rebaja (ProID, Rebaja) VALUES
    ('900894', 1.50),  -- AXE AER BS EPIC FRESH 12X97G/150ML -  (oct actual 13.64)
    ('900936', 13.50),  -- AXE BS AQUA CITRUS 12X96G/150ML -  (oct actual 50.01)
    ('900934', 13.50),  -- AXE BS BLUE LAVENDER 12X96G/150ML -  (oct actual 63.65)
    ('900935', 10.38),  -- AXE BS GREEN GERANIUM 12X96G/150ML -  (oct actual 50.01)
    ('900170', 13.50),  -- AXE DEO AER AP BLACK 12X90GR/152ML - 586698 (oct actual 45.46)
    ('953004', 19.50),  -- AXE DEO AER AP DARK TEMPT 12X88G/152ML - 6844 (oct actual 81.83)
    ('900913', 1.50),  -- AXE DEO AER AP EPIC FRESH 12X88G/152ML -  (oct actual 9.09)
    ('953005', 22.75),  -- AXE DEO AER AP EXCITE 12X88G/152ML - 68444065 (oct actual 90.92)
    ('900163', 33.00),  -- AXE DEO AER BS M BLACK 12X97G/150ML - 6844154 (oct actual 77.29)
    ('900174', 28.50),  -- AXE DEO AP APOLLO 12X90G/152ML - 67581503 (oct actual 81.83)
    ('900165', 31.50),  -- AXE DEO BS AER DARK TEMPT 12X97G/150ML - 6758 (oct actual 100.01)
    ('900168', 27.00),  -- AXE DEO BS M APOLLO 12X97G/150ML - 68441503 (oct actual 90.92)
    ('900166', 21.00),  -- AXE DEO BS M EXCITE 12X97G/150ML - 67581304 (oct actual 72.74)
    ('900179', 3.00),  -- AXE DEO STICK AP DARK TEMPTAT 12X50G - 676532 (oct actual 68.19)
    ('900952', 1.50),  -- CIF BIOACT BAÑOS OLORES GTL 12X500ML -  (oct actual 18.19)
    ('900616', 18.13),  -- CIF CR BIOACTIVE ORIGINAL BOT 12X750G - 68817 (oct actual 50.39)
    ('900564', 1.00),  -- BABY DOVE TIP TO TOE WASH CALMING MOISTURE -  (oct actual 7.58)
    ('900104', 13.50),  -- DMC Bar Deep Clean 24X2X7.5z - 67908301 (oct actual 27.28)
    ('900105', 34.50),  -- DMC Bar Extra Fresh 24X2X7.5z - 67908302 (oct actual 72.74)
    ('900641', 4.55),  -- DMC IS STAIN DEFENSE CLEAN 12P 2.7Z - 6790830 (oct actual 4.55)
    ('900377', 37.50),  -- DOVE ADV CLEAR FINISH 12X2.6OZ - 67148215 (oct actual 113.65)
    ('900203', 51.00),  -- DOVE ADVANCED CARE SHEA BUTTER 12X2.6OZ - 679 (oct actual 86.37)
    ('900924', 18.00),  -- DOVE AERO AP ROSAS 87G/150ML -  (oct actual 50.01)
    ('900119', 10.50),  -- DOVE BODY WASH DEEP MOISTURE 6X12OZ - 6789832 (oct actual 47.73)
    ('900125', 10.75),  -- DOVE BODY WASH EXFOLIATING 4X22OZ - 67903079 (oct actual 34.85)
    ('900116', 14.25),  -- DOVE BODY WASH EXTRA FRESH 6X13.5OZ - 6814538 (oct actual 40.91)
    ('900899', 1.00),  -- DOVE BW CARE + PROTECT 4P 22Z - 69673056 (oct actual 8.71)
    ('900121', 2.50),  -- DOVE BW SHEA BUTTER 4X22OZ - 84168445 (oct actual 23.87)
    ('900884', 43.50),  -- DOVE DEO AER AP CLEAR TONE 12X89G - 67746867 (oct actual 122.75)
    ('900879', 73.25),  -- DOVE DEO AER AP DERMO ACLARANT 12X89G -  (oct actual 190.93)
    ('900191', 54.00),  -- DOVE DEO AER AP ORIGINAL 12X89G - 84165195 (oct actual 179.58)
    ('900927', 16.50),  -- DOVE DEO AER AP ORQUIDEA 12X87G/150ML - 69627 (oct actual 109.11)
    ('900640', 22.50),  -- DOVE DEO ROL AP ORQUIDEA 12X50ML -  (oct actual 136.38)
    ('900631', 56.63),  -- DOVE DEO ROL AP ROSAS 12X50ML -  (oct actual 190.93)
    ('900815', 33.00),  -- DOVE DEO ROL DERMO ACLARANT 12X50ML - 6774689 (oct actual 165.94)
    ('900814', 46.50),  -- DOVE DEO ROLLON INVISIBLE DRY 12X50ML - 84168 (oct actual 127.30)
    ('900813', 52.50),  -- DOVE DEO ROLLON ORIGINAL 12X50ML - 84165241 (oct actual 172.76)
    ('900812', 36.13),  -- DOVE DEO ROLLON PROT TOTAL 12X50ML - 84168486 (oct actual 140.93)
    ('800765', 37.50),  -- DOVE DEO STI AP INVISIBLE DRY 12X45G - 683545 (oct actual 109.11)
    ('800766', 36.00),  -- DOVE DEO STI AP ORIGINAL 12X45G - 68354536 (oct actual 136.38)
    ('900208', 34.38),  -- DOVE DEO STI MEN CARE CLEAN COMF12X2.7OZ - 67 (oct actual 113.65)
    ('900209', 32.25),  -- DOVE DEO STI MEN CARE E FRESH 12X2.7OZ - 8415 (oct actual 145.47)
    ('800764', 49.50),  -- DOVE DEO STICK CALENDULA 12X45G - 68354545 (oct actual 156.84)
    ('900938', 9.63),  -- DOVE FM CLIN AER AP CUIDADO TOTAL 12X91G -  (oct actual 25.38)
    ('900937', 4.50),  -- DOVE FW CLIN AER AP ORIGINA CLEAN 12X91G -  (oct actual 40.91)
    ('900199', 21.75),  -- DOVE IS POWDER 6(2) 2.6 OZ. - 84155924 (oct actual 52.28)
    ('800768', 84.00),  -- DOVE JAB BLANCO 16X3X90G - 68687973 (oct actual 230.33)
    ('900114', 177.00),  -- DOVE JAB BLANCO 48X90G - 84137854 (oct actual 472.80)
    ('800802', 30.00),  -- DOVE JAB BLANCO 8X6X90G - 68687972 (oct actual 69.71)
    ('800767', 36.50),  -- DOVE JAB CUID PROTEGE 16X3X90G - 68621647 (oct actual 122.75)
    ('900809', 4.00),  -- DOVE JAB CUIDA PROTEGE 8X6X90G - 68429681 (oct actual 18.19)
    ('900095', 22.00),  -- DOVE JAB KARITE Y VAINILLA 8X6X90G - 67921822 (oct actual 56.45)
    ('800769', 74.63),  -- DOVE JABON KARITE Y VAINILLA 16X3X90G - 68687 (oct actual 200.03)
    ('900947', 3.00),  -- DOVE M CLIN DEO CR SOTF SOLID 12X58G -  (oct actual 9.09)
    ('900896', 7.50),  -- DOVE M DEO AER AP EFRESH V 12X89G/150ML - 696 (oct actual 36.37)
    ('900895', 3.00),  -- DOVE M DEO AER AP PRO TOT V 12X89G/150ML - 84 (oct actual 22.73)
    ('900897', 7.50),  -- DOVE MEN DEO AER AP INVIS DRY V 12X87G/150ML  (oct actual 36.37)
    ('900649', 6.00),  -- DOVE SERUM CORP E SECA HIALU 12X400ML -  (oct actual 36.37)
    ('900653', 1.50),  -- DOVE SERUM CORP EX ASP PANTENOL 12X200ML -  (oct actual 9.09)
    ('900648', 3.00),  -- DOVE SERUM CORP EX ASP PANTENOL 12X400ML -  (oct actual 18.19)
    ('900644', 6.00),  -- DOVE SERUM CORP EX SEC HIALUR 12X200ML -  (oct actual 18.19)
    ('900652', 1.50),  -- DOVE SERUM CORP FIRM PRO RETIN 12X400ML -  (oct actual 21.21)
    ('900646', 6.00),  -- DOVE SERUM CORP FIRM PRO-RETIN 12X200ML -  (oct actual 22.73)
    ('900650', 4.50),  -- DOVE SERUM CORP SEC PRO CERAM 12X400ML -  (oct actual 13.64)
    ('900645', 4.50),  -- DOVE SERUM CORP SEC PRO-CERAM 12X200ML -  (oct actual 9.09)
    ('900647', 10.50),  -- DOVE SERUM CORP TONO UNIF NIACI 12X200ML -  (oct actual 75.77)
    ('900651', 7.50),  -- DOVE SERUM CORP TONO UNIF NIACI 12X400ML -  (oct actual 95.47)
    ('900192', 16.50),  -- DOVE STICK POWDER 12X1.6OZ - 84155901 (oct actual 84.48)
    ('900950', 3.00),  -- DOVE W CREAM AP CLINICAL TONO 12X58G -  (oct actual 22.73)
    ('900949', 6.00),  -- DOVE W ROL AP CLINICAL TONO 12X50ML -  (oct actual 36.37)
    ('900642', 12.00),  -- DV ADV COOL ESSENTIALS 6(2) 2.6Z (2 IPK) - 67 (oct actual 43.19)
    ('900195', 39.00),  -- Dove ADV Cool Essentials  12 X 2 . 6 Oz. - 84 (oct actual 95.47)
    ('900900', 6.38),  -- Dove BW Relaxing Lavender 4p 20z/591mL - 6967 (oct actual 21.97)
    ('900500', 2700.00),  -- MAIZENA DURYEA FEC MAIZ REG 150X95GR - 841451 (oct actual 5516.76)
    ('900501', 750.00),  -- MAIZENA DURYEA FEC MAIZ REG 40X425GR - 675097 (oct actual 1517.66)
    ('900552', 109.50),  -- MAIZENA FEC MAIZ SABOR CHOCOLATE 24X90G - 676 (oct actual 236.40)
    ('900363', 120.00),  -- MAIZENA FEC MAIZ SABOR FRESA 24X94G - 6765342 (oct actual 254.58)
    ('900549', 96.00),  -- MAIZENA FEC MAIZ VAINILLA 24X94G - 67653421 (oct actual 245.49)
    ('900628', 31.50),  -- MISTOLIN DES CAMPOS DE FLORES 15X770ML - 6856 (oct actual 135.25)
    ('900630', 35.63),  -- MISTOLIN DES EXPLOSION FRUTAS 15X770ML - 6856 (oct actual 125.02)
    ('900053', 27.00),  -- MISTOLIN DES LIMP BRISA MARINA 24X410ML - 685 (oct actual 99.26)
    ('900037', 2.00),  -- MISTOLIN DES LIMP BRISA MARINA 4X3,74LT - 685 (oct actual 60.61)
    ('900043', 11.25),  -- MISTOLIN DES LIMP BRISA MARINA 6X1.84L - 6856 (oct actual 61.37)
    ('900051', 33.00),  -- MISTOLIN DES LIMP CAMPO DE FLORES 24X410ML -  (oct actual 135.63)
    ('900036', 2.00),  -- MISTOLIN DES LIMP CAMPOS DE FLORES 4X3,74LT - (oct actual 77.29)
    ('900042', 8.25),  -- MISTOLIN DES LIMP CAMPOS DE FLORES 6X1,84LT - (oct actual 52.28)
    ('900040', 2.00),  -- MISTOLIN DES LIMP EXPLOSION FRUTAS 4X3,74LT - (oct actual 83.35)
    ('900044', 11.25),  -- MISTOLIN DES LIMP ROCIO DE LAVANDA 6X1,84LT - (oct actual 54.55)
    ('900039', 2.00),  -- MISTOLIN DES LIMP ROCIO LAVANDA 4X3,74LT - 68 (oct actual 86.37)
    ('900629', 28.13),  -- MISTOLIN DES ROCIO DE LAVANDA 15X770ML - 6856 (oct actual 142.07)
    ('900054', 30.00),  -- MISTOLIN DES ROCIO LAVANDA 24X410ML - 6856818 (oct actual 117.44)
    ('900627', 33.38),  -- MISTOLIN LIMP BRISA MARINA 15X770ML - 6856822 (oct actual 153.44)
    ('900223', 9.00),  -- PONDS CR C PEPINO 24X50G - 84163161 (oct actual 63.65)
    ('900581', 48.00),  -- PONDS CR CLARANT B3 FPS30 12X100G - 67828548 (oct actual 438.71)
    ('900226', 78.00),  -- PONDS CR CLARANT B3 GRASA 24X100G - 84164100 (oct actual 522.80)
    ('900222', 99.00),  -- PONDS CR CLARANT B3 GRASA 24X50G - 84163148 (oct actual 566.00)
    ('900227', 72.00),  -- PONDS CR CLARANT B3 SECA 24X100G - 84164099 (oct actual 445.53)
    ('900221', 42.00),  -- PONDS CR CLARANT B3 SECA 24X50G - 84163149 (oct actual 347.79)
    ('900220', 16.50),  -- PONDS CR REJUVENESS 24X50G - 84163166 (oct actual 111.38)
    ('900224', 13.50),  -- PONDS CR REJUVENESS DIA 24X100G - 84163162 (oct actual 93.19)
    ('900582', 16.50),  -- PONDS CR REJUVENESS FPS30 12X100G - 67828553 (oct actual 122.75)
    ('900580', 19.50),  -- PONDS CR S HUMECTANTE FPS30 12X100G - 6782854 (oct actual 95.47)
    ('900229', 19.50),  -- PONDS CR S HUMECTANTE NUTRITIVA 24X100G - 841 (oct actual 54.55)
    ('900219', 16.50),  -- PONDS CR S HUMECTANTE NUTRITIVA 24X50G - 8416 (oct actual 36.37)
    ('900700', 14.25),  -- PONDS GEL FRUITY HYDRAT FRE SAND 8X110G - 679 (oct actual 57.59)
    ('900701', 8.00),  -- PONDS GEL FRUITY HYDRAT FRESH ALOE 8X110G - 6 (oct actual 30.31)
    ('900944', 3.03),  -- PONDS GEL H ACT 8X110G -  (oct actual 3.03)
    ('900940', 8.00),  -- PONDS GEL VS MANCHAS 8X110G -  (oct actual 12.12)
    ('900703', 5.00),  -- PONDS LIMP FAC FRU  HYD FREs SAND 8X200ML - 6 (oct actual 39.40)
    ('900941', 4.00),  -- PONDS LIMP FAC VS MANCHAS 8X200ML -  (oct actual 30.31)
    ('900704', 12.25),  -- PONDS LIMP FACFRU  HYD FRE ALOE 8X200ML - 679 (oct actual 54.55)
    ('900639', 27.00),  -- DOVE DEO STI AP ORQUIDEA 12X45G -  (oct actual 81.83)
    ('900449', 25.50),  -- REXONA AER AP MEN ANTIBAC INV 12X90G/150ML -  (oct actual 122.75)
    ('900448', 25.50),  -- REXONA AER AP WOMEN ANTIBAC INV 12X90G/150ML  (oct actual 118.20)
    ('800773', 28.50),  -- REXONA CLIN DEO AER AP CLASSIC 12X91G - 67806 (oct actual 163.66)
    ('800771', 32.00),  -- REXONA CLIN DEO AER AP CLEAN 12X91G - 6780673 (oct actual 79.56)
    ('900638', 42.00),  -- REXONA CLIN EXP DEO STI MEN CLEAN 12X46G -  (oct actual 127.30)
    ('800772', 49.50),  -- REXONA CLIN STI WM STR 12CT 46G LA - 68268053 (oct actual 186.40)
    ('900132', 54.00),  -- REXONA DEO AER AP ACT EMOT 12X90G/150ML - 675 (oct actual 154.57)
    ('900128', 26.63),  -- REXONA DEO AER AP ANTIBACTERIAL 12X150ML - 67 (oct actual 89.79)
    ('900137', 40.50),  -- REXONA DEO AER AP BAMBOO 12X90G/150ML - 67529 (oct actual 218.22)
    ('900131', 81.00),  -- REXONA DEO AER AP POWDER 12X90G/150ML - 67529 (oct actual 295.50)
    ('900871', 18.00),  -- REXONA DEO AER AP POWDER 6X2X89g - 68658761 (oct actual 72.74)
    ('900134', 43.50),  -- REXONA DEO AER AP V8 12X90G/150ML - 84160215 (oct actual 195.48)
    ('900890', 27.75),  -- REXONA DEO AER AP V8 6X2X89G - 84162865 (oct actual 50.01)
    ('900604', 40.50),  -- REXONA DEO AER AP XTRA COOL 12X90G/150ML - 67 (oct actual 136.38)
    ('900160', 114.00),  -- REXONA DEO ROL ACTIVE EMOT12X50ML - 84150754 (oct actual 490.99)
    ('900872', 20.25),  -- REXONA DEO ROL AP POWDER 6X2X50ML - 68683085 (oct actual 54.55)
    ('900873', 29.25),  -- REXONA DEO ROL AP V8 6X2X50ML - 68683086 (oct actual 88.65)
    ('900158', 81.63),  -- REXONA DEO ROL BAMBOO W/ALOE VERA 12X50ML - 6 (oct actual 420.14)
    ('900928', 4.50),  -- REXONA DEO ROL CLIN CLEAN 12X50ML - 67681095 (oct actual 50.01)
    ('900637', 60.00),  -- REXONA DEO ROL CLINIC CLASSIC 12X50ML - 68683 (oct actual 176.54)
    ('900453', 94.50),  -- REXONA DEO ROL MEN ANTIBAC INVIS 12X50ML - 84 (oct actual 186.40)
    ('900903', 60.00),  -- REXONA DEO ROL TONO PERF+ANTIBAC 12X50ML - 84 (oct actual 113.65)
    ('900452', 63.00),  -- REXONA DEO ROL WM ANTIBAC INVIS 12X50ML - 841 (oct actual 140.93)
    ('900605', 112.50),  -- REXONA DEO ROL XTRA COOL 12X50ML - 67644985 (oct actual 440.98)
    ('900161', 106.50),  -- REXONA DEO ROLLON AP POWDER 12X50ML - 8415086 (oct actual 1116.08)
    ('900157', 132.00),  -- REXONA DEO ROLLON AP V8 12X50ML - 84152921 (oct actual 1173.29)
    ('900876', 68.75),  -- REXONA DEO STI AP ACTIVE EMOTION 12X45G - 678 (oct actual 209.12)
    ('900417', 66.00),  -- REXONA DEO STI AP ACTR BAMBOO 12X45G - 677573 (oct actual 209.12)
    ('900877', 111.00),  -- REXONA DEO STI AP POWDER 12X45G - 67757356 (oct actual 397.79)
    ('982015', 76.50),  -- REXONA DEO STI AP V8 12X45G -  (oct actual 313.68)
    ('900875', 10.50),  -- REXONA DEO STI POWDER 6X2X45G - 68653834 (oct actual 61.37)
    ('900606', 36.00),  -- REXONA DEO STICK AP XTRA COOL 12X50G - 676449 (oct actual 205.72)
    ('900930', 9.00),  -- REXONA M CLIN DEO AER IN FR 12X91G/150ML - 69 (oct actual 54.55)
    ('900874', 19.50),  -- REXONA MEN DEO STI AP MIX 6X2X50g - 68653831 (oct actual 47.73)
    ('900451', 49.50),  -- REXONA STICK MEN ANTIBAC INVIS 12X45G - 84171 (oct actual 213.67)
    ('900450', 46.50),  -- REXONA STICK WM ANTIBAC INVIS 12X50G - 841715 (oct actual 131.84)
    ('900155', 82.50),  -- REXONA TEENS DEO ROL TROP ENERGY 12X50ML - 68 (oct actual 463.70)
    ('900931', 19.75),  -- REXONA W CLIN DEO AER REFRE 12X91G/150ML - 69 (oct actual 72.74)
    ('900494', 52.50),  -- REXONA W DEO AER ANTIB 12X90G/150ML+ACL - 675 (oct actual 131.84)
    ('900495', 42.00),  -- REXONA WOMEN DEO STI AP ANTIB 12X45G+ACL - 67 (oct actual 140.93)
    ('900248', 13.50),  -- SEDAL AC CERAMIDAS 12X340ML - 67707825 (oct actual 63.27)
    ('900242', 17.50),  -- SEDAL AC KERATINA C/ ANTIOX 12X340ML - 679525 (oct actual 71.22)
    ('900244', 16.13),  -- SEDAL AC RIZOS DEFINIDOS 12X340ML - 67952545 (oct actual 45.46)
    ('900253', 10.50),  -- SEDAL CR PEIN KERATINA C/ ANTIOX 12X300ML - 6 (oct actual 63.65)
    ('900252', 19.50),  -- SEDAL CR PEIN RIZOS DEFINIDOS 12X300ML - 6828 (oct actual 81.83)
    ('900257', 12.00),  -- SEDAL CR PEINAR CERAMIDAS 12X300ML - 68287759 (oct actual 31.82)
    ('900240', 15.00),  -- SEDAL SH CERAMIDAS 12X340ML - 67952528 (oct actual 75.39)
    ('900235', 19.50),  -- SEDAL SH KERATI C/ ANTIOX 12X340ML - 67708043 (oct actual 75.02)
    ('900237', 12.00),  -- SEDAL SH RIZOS DEFINIDOS 12X340ML - 68473509 (oct actual 40.91)
    ('900910', 1.50),  -- TRES CD 24HR Vol 6p 28z/828mL - 68381841 (oct actual 6.82)
    ('900296', 2.63),  -- TRES CD ANTI-BRKG 6p 28z - 68381841 (oct actual 11.37)
    ('900291', 1.50),  -- TRES CD MOIST RICH 6p 28z - 68380635 (oct actual 6.82)
    ('900297', 1.50),  -- TRES CD SMOOTH SLKY 6p 28z - 68380638 (oct actual 11.37)
    ('900911', 2.25),  -- TRES SH 24HR Vol 6p 28z/828mL - 68381841 (oct actual 4.55)
    ('900287', 2.00),  -- TRES SH ANTI-BRKG 6p 28z - 68381844 (oct actual 4.55)
    ('900283', 3.00),  -- TRES SH MOIST RICH 6p 28z - 68380633 (oct actual 9.09)
    ('900289', 2.25),  -- TRES SH SMOOTH SLKY 6p 28z - 68380636 (oct actual 2.27)
    ('900904', 0.75),  -- TRES Two HS Extra Hold 6p 11z - 68381841 (oct actual 4.55)
    ('900960', 4.50),  -- TRESEMME AC CAUT REPARADORA ST 12X500ML -  (oct actual 9.09)
    ('900932', 0.75),  -- TRESEMME MASC TRAT REPAR INT C 6X300G NE -  (oct actual 4.55)
    ('900959', 1.50),  -- TRESEMME SH CAUT REPARADORA ST 12X500ML -  (oct actual 4.55)
    ('900290', 0.75),  -- TRESEMME SH COLOR REVITALIZE 6X28OZ/828ML - 6 (oct actual 2.27)
    ('900212', 7.50),  -- VASENOL CR COCOA GLOW 12X400ML - 591780 (oct actual 40.91)
    ('900211', 6.75)  -- VASENOL CR HUMECTACION TOTAL12X400ML - 591783 (oct actual 29.55)
;

/* ---------- Estado actual por producto ---------- */
IF OBJECT_ID('tempdb..#Antes') IS NOT NULL DROP TABLE #Antes;
SELECT r.ProID, r.Rebaja,
       u.PrePresupuesto AS Und_Actual,
       m.PrePresupuesto AS RD_Actual,
       c.PrePresupuesto AS Cj_Actual,
       CASE WHEN u.PrePresupuesto - r.Rebaja < 0 THEN 0 ELSE u.PrePresupuesto - r.Rebaja END AS Und_Nuevo
INTO #Antes
FROM #Rebaja r
JOIN dbo.PresupuestosHistoricos u
  ON u.RepCodigo = @Rep AND u.PreAnio = @Anio AND u.PreMes = @Mes AND u.PreReferencia = r.ProID
 AND u.PreTipo COLLATE Latin1_General_CS_AS = 'Unddprod'
LEFT JOIN dbo.PresupuestosHistoricos m
  ON m.RepCodigo = @Rep AND m.PreAnio = @Anio AND m.PreMes = @Mes AND m.PreReferencia = r.ProID
 AND m.PreTipo COLLATE Latin1_General_CS_AS = 'MNTDPROD'
LEFT JOIN dbo.PresupuestosHistoricos c
  ON c.RepCodigo = @Rep AND c.PreAnio = @Anio AND c.PreMes = @Mes AND c.PreReferencia = r.ProID
 AND c.PreTipo COLLATE Latin1_General_CS_AS = 'UNDDPRODCJ';

SELECT a.ProID, p.ProDescripcion, a.Und_Actual, a.Rebaja, a.Und_Nuevo,
       a.RD_Actual, CASE WHEN a.Und_Actual = 0 THEN a.RD_Actual ELSE a.RD_Actual * a.Und_Nuevo / a.Und_Actual END AS RD_Nuevo,
       a.Cj_Actual, CASE WHEN a.Und_Actual = 0 THEN a.Cj_Actual ELSE a.Cj_Actual * a.Und_Nuevo / a.Und_Actual END AS Cj_Nuevo
FROM #Antes a LEFT JOIN dbo.Productos p ON CAST(p.ProID AS varchar(20)) COLLATE Modern_Spanish_CI_AS = a.ProID
ORDER BY a.RD_Actual DESC;

SELECT DB_NAME() AS Base, COUNT(*) AS Productos_en_lista, (SELECT COUNT(*) FROM #Antes) AS Productos_encontrados_en_oct FROM #Rebaja;  -- deben ser iguales

/* Totales del vendedor ANTES */
SELECT 'ANTES' AS Momento, PreTipo, COUNT(*) AS Filas, SUM(PrePresupuesto) AS Total
FROM dbo.PresupuestosHistoricos
WHERE RepCodigo = @Rep AND PreAnio = @Anio AND PreMes = @Mes
  AND PreTipo COLLATE Latin1_General_CS_AS IN ('Unddprod','MNTDPROD','UNDDPRODCJ')
GROUP BY PreTipo;

/* ---------- Respaldo ---------- */
IF OBJECT_ID('dbo.PresupuestosHistoricos_Bkp_R36_CI_Oct2026') IS NULL
    SELECT h.*, GETDATE() AS FechaRespaldo
    INTO dbo.PresupuestosHistoricos_Bkp_R36_CI_Oct2026
    FROM dbo.PresupuestosHistoricos h
    WHERE h.RepCodigo = @Rep AND h.PreAnio = @Anio AND h.PreMes = @Mes
      AND h.PreTipo COLLATE Latin1_General_CS_AS IN ('Unddprod','MNTDPROD','UNDDPRODCJ')
      AND h.PreReferencia IN (SELECT ProID FROM #Rebaja);

BEGIN TRAN;

    UPDATE h SET h.PrePresupuesto = a.Und_Nuevo, h.UsuInicioSesion = @Usuario, h.PreFechaActualizacion = GETDATE()
    FROM dbo.PresupuestosHistoricos h JOIN #Antes a ON a.ProID = h.PreReferencia
    WHERE h.RepCodigo = @Rep AND h.PreAnio = @Anio AND h.PreMes = @Mes
      AND h.PreTipo COLLATE Latin1_General_CS_AS = 'Unddprod';
    PRINT CONCAT('Unddprod actualizados: ', @@ROWCOUNT);

    IF @AjustarMontoYCajas = 1
    BEGIN
        UPDATE h SET h.PrePresupuesto = ROUND(h.PrePresupuesto * a.Und_Nuevo / a.Und_Actual, 2),
                     h.UsuInicioSesion = @Usuario, h.PreFechaActualizacion = GETDATE()
        FROM dbo.PresupuestosHistoricos h JOIN #Antes a ON a.ProID = h.PreReferencia
        WHERE h.RepCodigo = @Rep AND h.PreAnio = @Anio AND h.PreMes = @Mes AND a.Und_Actual > 0
          AND h.PreTipo COLLATE Latin1_General_CS_AS IN ('MNTDPROD','UNDDPRODCJ');
        PRINT CONCAT('MNTDPROD/UNDDPRODCJ actualizados: ', @@ROWCOUNT);
    END

    SELECT 'DESPUES' AS Momento, PreTipo, COUNT(*) AS Filas, SUM(PrePresupuesto) AS Total
    FROM dbo.PresupuestosHistoricos
    WHERE RepCodigo = @Rep AND PreAnio = @Anio AND PreMes = @Mes
      AND PreTipo COLLATE Latin1_General_CS_AS IN ('Unddprod','MNTDPROD','UNDDPRODCJ')
    GROUP BY PreTipo;

IF @Aplicar = 1
BEGIN
    COMMIT TRAN;
    PRINT 'APLICADO (COMMIT). Ahora correr sp_PresupuestosPRODActualizar.';
END
ELSE
BEGIN
    ROLLBACK TRAN;
    PRINT 'VISTA PREVIA: no se guardó nada (ROLLBACK). Cambie @Aplicar = 1 para aplicar.';
END

/* ---------- Para revertir ----------
UPDATE h SET h.PrePresupuesto = b.PrePresupuesto, h.UsuInicioSesion = b.UsuInicioSesion,
             h.PreFechaActualizacion = b.PreFechaActualizacion
FROM dbo.PresupuestosHistoricos h
JOIN dbo.PresupuestosHistoricos_Bkp_R36_CI_Oct2026 b
  ON b.RepCodigo = h.RepCodigo AND b.PreTipo = h.PreTipo AND b.PreAnio = h.PreAnio
 AND b.PreMes = h.PreMes AND b.PreReferencia = h.PreReferencia;
------------------------------------ */
