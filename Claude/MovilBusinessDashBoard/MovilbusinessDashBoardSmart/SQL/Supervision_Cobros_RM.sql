/* ============================================================================
   SUPERVISION DINAMICA — "Cobros del dia" (tablas _RM)
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Nueva area del modulo Supervision Dinamica, entorno igual a Virtual: alertas
   configurables de metas DIARIAS (semaforo Verde/Amarillo/Rojo por vendedor) +
   contacto por recurso (vendedor o supervisor) + bitacora de 3 cortes al dia
   (9:50 / 11:50 / 15:30) + reporte de contactos.

   Modelo confirmado (ago-2026, AskUserQuestion):
     - Fase 1: 3 KPIs calculados de verdad:
         1) presupuesto  -> PresupuestosHistoricos, PreTipo (ej 'MNTDCAT1DIA').
         2) pedidos      -> VWPedidos, COUNT(DISTINCT PedSecuencia) del dia,
                             meta FIJA por vendedor (ExcluirSupCods = supervisores
                             que NO aplican, ej Cristiano Perez S12/S13 = 42 no aplica).
         3) producto     -> VWPedidosDetalle+VWPedidos filtrado por ProID, meta FIJA,
                             puede acotarse a una Division y a una vigencia (desde/hasta).
         4) activaciones -> COUNT(DISTINCT CliID) del dia (clientes distintos activados),
                             medido por 'producto'(ProID), 'categoria'(LinID) o
                             'suplidor'(Cat3ID, desde Categoria3), segun columna
                             Medicion. Meta FIJA. (agregado ago-17-2026)
       Los demas Tipo (unidades, impactos/facturas, montos, fotos)
       quedan preparados en Configuracion para cuando se defina su fuente real.
     - Semaforo: >=90% Verde, >=80% Amarillo, <80% Rojo (umbrales editables por
       config). En los cortes 9:50/11:50 se compara la PROYECCION al cierre
       (regla de 3 desde las 8:00 hasta las 15:30); pasadas las 15:30 se compara
       el LOGRO REAL (ya no se proyecta).
     - Contacto por RECURSO (vendedor o supervisor): al "cargar" un recurso se
       listan TODAS las alarmas configuradas que esten activas (Rojo/Amarillo)
       para el; se guarda UN contacto (comentario+compromiso) con la lista de
       alarmas involucradas. Tras contactar, ese recurso queda en pausa (mute)
       60 min (mismo muteMin de Torre de Control) y luego reactiva con las
       alarmas que sigan vigentes a esa hora.
     - Orden = el que va peor primero (prioriza al peor resultado).

   Verificado directo en BD (ago-2026):
     - PresupuestosHistoricos.PreReferencia para PreTipo='MNTDCAT1DIA' tiene
       formato 'DD-Cat1ID' (ej '15-105' = dia 15, categoria 105); Cat1ID
       referencia Categoria1 (NO Categoria3/suplidor).
     - "Pedidos distintos" = COUNT(DISTINCT PedSecuencia) en VWPedidos/VWPedidosDetalle
       (mismo patron que TorrePersonalizados.cs, metrica 'impactos').
     - Producto "GALL CKR MAX QUESO SEXTENA 24X6X30 - 36002411" = ProID 983092.
     - Cristiano Perez = supervisores S12 (Mini Market) y S13 (Tradicional).
     - Division 11 = 13 vendedores activos (RepresentantesAdicionales.RepTipe='11').

   Usuario "Ventas" es read-only sobre la BD; al final se otorgan permisos DML
   solo sobre estas tablas _RM. Ejecutar una sola vez (re-ejecutable).
   ============================================================================ */

/* ============================================================== *
 * 1) CONFIGURACION: catalogo de alertas/KPIs de Cobros del dia   *
 *    Tipo: presupuesto | pedidos | producto | unidades |         *
 *          activaciones | impactos | montos | fotos |            *
 *          personalizado                                         *
 *    PreTipo: solo aplica cuando Tipo='presupuesto'              *
 *    MetaFija: meta diaria fija por vendedor (Tipo<>'presupuesto')*
 *    ProID: solo aplica cuando Tipo='producto'                    *
 *    Division: opcional, acota a RepresentantesAdicionales.RepTipe*
 *    ExcluirSupCods: opcional, CSV de SupCod que NO aplican       *
 *    VigenciaDesde/Hasta: opcional, ventana de fechas de la alerta*
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionCobrosConfig_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionCobrosConfig_RM
    (
        CfgID           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupCobrosCfg_RM PRIMARY KEY,
        Nombre          varchar(150) NOT NULL,
        Tipo            varchar(30)  NOT NULL,
        PreTipo         varchar(15)  NULL,
        MetaFija        decimal(18,2) NULL,
        ProID           int          NULL,
        Division        varchar(10)  NULL,
        ExcluirSupCods  varchar(200) NULL,
        Medicion        varchar(15)  NULL,   -- Tipo='activaciones': 'producto'(ProID) | 'categoria'(LinID) | 'suplidor'(Cat3ID)
        LinID           int          NULL,   -- Tipo='activaciones' + Medicion='categoria' (Productos.LinID)
        Cat3ID          int          NULL,   -- Tipo='activaciones' + Medicion='suplidor' (Categoria3.Cat3ID)
        VigenciaDesde   date         NULL,
        VigenciaHasta   date         NULL,
        Activo          bit          NOT NULL CONSTRAINT DF_SupCobrosCfg_Act DEFAULT(1),
        Orden           int          NOT NULL CONSTRAINT DF_SupCobrosCfg_Ord DEFAULT(0),
        UmbralVerde     decimal(5,2) NOT NULL CONSTRAINT DF_SupCobrosCfg_UV  DEFAULT(90),
        UmbralAmarillo  decimal(5,2) NOT NULL CONSTRAINT DF_SupCobrosCfg_UA  DEFAULT(80),
        UsuInicioSesion varchar(64)  NULL,
        FechaActualizacion datetime  NOT NULL CONSTRAINT DF_SupCobrosCfg_Fec DEFAULT(GETDATE())
    );
END
GO

-- Por si la tabla YA existia de una corrida anterior con un esquema mas viejo
-- (sin estas columnas): agrega la que falte, sin tocar filas existentes. Esto
-- es lo que causaba "Invalid column name 'MetaFija'/'ExcluirSupCods'/'ProID'".
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','PreTipo') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD PreTipo varchar(15) NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','MetaFija') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD MetaFija decimal(18,2) NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','ProID') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD ProID int NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','Division') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD Division varchar(10) NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','ExcluirSupCods') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD ExcluirSupCods varchar(200) NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','VigenciaDesde') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD VigenciaDesde date NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','VigenciaHasta') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD VigenciaHasta date NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','Medicion') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD Medicion varchar(15) NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','LinID') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD LinID int NULL;
IF COL_LENGTH('dbo.SupervisionCobrosConfig_RM','Cat3ID') IS NULL
    ALTER TABLE dbo.SupervisionCobrosConfig_RM ADD Cat3ID int NULL;
GO

-- Semilla 1: presupuesto diario (Categoria1/Monto).
IF NOT EXISTS (SELECT 1 FROM dbo.SupervisionCobrosConfig_RM WHERE Tipo='presupuesto' AND PreTipo='MNTDCAT1DIA')
    INSERT INTO dbo.SupervisionCobrosConfig_RM (Nombre,Tipo,PreTipo,Activo,Orden,UmbralVerde,UmbralAmarillo)
    VALUES ('Presupuesto del dia (Categoria / Monto)','presupuesto','MNTDCAT1DIA',1,1,90,80);
GO

-- Semilla 2: pedidos distintos del dia, meta fija 42, no aplica a Cristiano Perez (S12/S13).
IF NOT EXISTS (SELECT 1 FROM dbo.SupervisionCobrosConfig_RM WHERE Tipo='pedidos')
    INSERT INTO dbo.SupervisionCobrosConfig_RM (Nombre,Tipo,MetaFija,ExcluirSupCods,Activo,Orden,UmbralVerde,UmbralAmarillo)
    VALUES ('Pedidos del dia (distintos)','pedidos',42,'S12,S13',1,2,90,80);
GO

-- Semilla 3: producto especifico, division 11, meta 10/dia, vigente 15-31 agosto 2026.
IF NOT EXISTS (SELECT 1 FROM dbo.SupervisionCobrosConfig_RM WHERE Tipo='producto' AND ProID=983092)
    INSERT INTO dbo.SupervisionCobrosConfig_RM (Nombre,Tipo,MetaFija,ProID,Division,VigenciaDesde,VigenciaHasta,Activo,Orden,UmbralVerde,UmbralAmarillo)
    VALUES ('Producto: GALL CKR MAX QUESO SEXTENA 24X6X30','producto',10,983092,'11','2026-08-15','2026-08-31',1,3,90,80);
GO

/* ============================================================== *
 * 2) BITACORA: 3 cortes al dia (9:50 / 11:50 / 15:30) por        *
 *    vendedor x config. El backend recalcula y persiste al      *
 *    momento de guardar cada corte (no confia en el cliente).    *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionCobrosBitacora_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionCobrosBitacora_RM
    (
        BitID           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupCobrosBit_RM PRIMARY KEY,
        Fecha           date          NOT NULL,
        Corte           varchar(10)   NOT NULL,   -- '09:50' | '11:50' | '15:30'
        CfgID           int           NOT NULL,
        RepCodigo       varchar(20)   NOT NULL,
        RepNombre       varchar(150)  NULL,
        SupCod          varchar(20)   NULL,
        Supervisor      varchar(150)  NULL,
        Meta            decimal(18,2) NOT NULL,
        Logro           decimal(18,2) NOT NULL,
        LogroPct        decimal(9,4)  NOT NULL,   -- logro/meta*100 (real, sin proyectar)
        ProyeccionPct   decimal(9,4)  NULL,       -- null en el corte final (15:30, ya no se proyecta)
        Semaforo        varchar(10)   NOT NULL,   -- verde | amarillo | rojo
        UsuInicioSesion varchar(64)   NULL,
        FechaRegistro   datetime      NOT NULL CONSTRAINT DF_SupCobrosBit_Reg DEFAULT(GETDATE()),
        CONSTRAINT UX_SupCobrosBit_RM UNIQUE (Fecha,Corte,CfgID,RepCodigo)
    );
    CREATE INDEX IX_SupCobrosBit_Fecha ON dbo.SupervisionCobrosBitacora_RM(Fecha,CfgID);
END
GO

/* ============================================================== *
 * 3) CONTACTOS: contacto con un RECURSO (vendedor o supervisor)  *
 *    por las alarmas de Cobros del dia que esten activas.        *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionCobrosContactos_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionCobrosContactos_RM
    (
        ConID           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupCobrosCon_RM PRIMARY KEY,
        FechaContacto   datetime      NOT NULL CONSTRAINT DF_SupCobrosCon_Fec DEFAULT(GETDATE()),
        TipoRecurso     varchar(12)   NOT NULL,   -- vendedor | supervisor
        RepCodigo       varchar(20)   NULL,       -- solo cuando TipoRecurso='vendedor'
        RepNombre       varchar(150)  NULL,
        SupCod          varchar(20)   NULL,       -- siempre (supervisor del rep, o el supervisor contactado)
        Supervisor      varchar(150)  NULL,
        Alertas         varchar(400)  NULL,       -- nombres de las alarmas activas al momento (para el reporte)
        CfgIds          varchar(200)  NULL,       -- CfgID involucrados (csv)
        Canal           varchar(20)   NULL,
        ContactoCon     varchar(20)   NULL,
        Comentario      varchar(1000) NULL,
        Compromiso      varchar(1000) NULL,
        UsuarioTorre    varchar(64)   NULL,
        FechaRegistro   datetime      NOT NULL CONSTRAINT DF_SupCobrosCon_Reg DEFAULT(GETDATE())
    );
    CREATE INDEX IX_SupCobrosCon_Rep   ON dbo.SupervisionCobrosContactos_RM(RepCodigo, FechaContacto);
    CREATE INDEX IX_SupCobrosCon_Sup   ON dbo.SupervisionCobrosContactos_RM(SupCod, FechaContacto);
    CREATE INDEX IX_SupCobrosCon_Fecha ON dbo.SupervisionCobrosContactos_RM(FechaContacto);
END
GO

/* ============================================================== *
 * 4) PERMISOS para la app (usuario "Ventas")                     *
 * ============================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionCobrosConfig_RM    TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionCobrosBitacora_RM  TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionCobrosContactos_RM TO [Ventas];
GO

/* Revision:
   SELECT * FROM dbo.SupervisionCobrosConfig_RM ORDER BY Orden;
   SELECT * FROM dbo.SupervisionCobrosBitacora_RM WHERE Fecha=CAST(GETDATE() AS date) ORDER BY Corte, LogroPct;
   SELECT * FROM dbo.SupervisionCobrosContactos_RM WHERE CAST(FechaContacto AS date)=CAST(GETDATE() AS date) ORDER BY FechaContacto DESC;
*/
