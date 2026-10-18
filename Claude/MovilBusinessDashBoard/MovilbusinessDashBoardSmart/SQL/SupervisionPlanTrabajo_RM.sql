/* ============================================================================
   SUPERVISION DINAMICA — PLAN DE TRABAJO SEMANAL DEL SUPERVISOR  (tablas _RM)
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Pedido del usuario 2026-10-10. Guarda el plan de trabajo que cada supervisor crea
   (Supervision Dinamica > Plan > "Crear Plan de Trabajo semanal"), su ejecucion
   (trabajo realizado, fecha, vendedor/cliente atendido, resultado) y el cierre
   semanal (los viernes) con el % de logro.

   Modelo:
     SupervisionPlanTrabajo_RM    1 fila por plan (secuencia unica PT-00001, PT-00002...)
     SupervisionPlanTrabajoAct_RM  actividades planificadas (por dia/hora) + su estado actual
     SupervisionPlanTrabajoEje_RM  bitacora de ejecucion: cada vez que el supervisor registra
                                  lo realizado de una actividad (puede haber varias por actividad)

   % de logro = suma(Peso x valor) / suma(Peso) de las actividades del plan (sin canceladas);
   valor: Realizada=100, Parcial=50, Reprogramada/Pendiente/No realizada=0. Las actividades
   'extra' (no planificadas, Peso 0) no cuentan en el %.

   FLUJO (2026-10-10): Borrador (lo arma el supervisor) -> Por aprobar (lo envia) -> Activo (lo aprueba
   Gerencia, que puede ajustarlo antes) -> Cerrado (el supervisor lo cierra el viernes; si no lo cierra
   queda "Sin cerrar", estado calculado por la app). Gerencia puede devolverlo a Borrador con comentario.

   IMPORTANTE (2026-10-10): la tabla dbo.SupervisionPlan_RM YA EXISTIA (Plan de prioridades, julio 2026) y
   NO se toca. Las tablas de este modulo se llaman SupervisionPlanTrabajo_RM / ...Act_RM / ...Eje_RM.
   La primera version de este script (mismo dia) creo SupervisionPlanActividad_RM y SupervisionPlanEjecucion_RM
   (la cabecera se omitio porque el nombre ya existia): el paso 0 las elimina SOLO si estan vacias.

   Usuario "Ventas" es read-only sobre la BD; al final se otorgan permisos DML solo sobre
   estas tablas _RM. Re-ejecutable.
   ============================================================================ */

/* ============================================================== *
 * 0) LIMPIEZA de la primera version (solo si estan VACIAS)       *
 *    Eran SupervisionPlanActividad_RM / SupervisionPlanEjecucion_RM, con una *
 *    FK equivocada hacia la tabla vieja SupervisionPlan_RM.      *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionPlanEjecucion_RM','U') IS NOT NULL
   AND COL_LENGTH('dbo.SupervisionPlanEjecucion_RM','TrabajoRealizado') IS NOT NULL
BEGIN
    DECLARE @n int; EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.SupervisionPlanEjecucion_RM', N'@n int OUTPUT', @n OUTPUT;
    IF @n = 0 DROP TABLE dbo.SupervisionPlanEjecucion_RM;
    ELSE PRINT 'ATENCION: SupervisionPlanEjecucion_RM tiene datos; no se elimino. Avisar al desarrollador.';
END
GO
IF OBJECT_ID('dbo.SupervisionPlanActividad_RM','U') IS NOT NULL
   AND COL_LENGTH('dbo.SupervisionPlanActividad_RM','Fijo') IS NOT NULL
BEGIN
    DECLARE @m int; EXEC sp_executesql N'SELECT @m = COUNT(*) FROM dbo.SupervisionPlanActividad_RM', N'@m int OUTPUT', @m OUTPUT;
    IF @m = 0 DROP TABLE dbo.SupervisionPlanActividad_RM;
    ELSE PRINT 'ATENCION: SupervisionPlanActividad_RM tiene datos; no se elimino. Avisar al desarrollador.';
END
GO

/* ============================================================== *
 * 1) CABECERA DEL PLAN                                           *
 *    PlanNumero = secuencia unica legible, derivada del PlanID.  *
 *    Estatus: Borrador | Por aprobar | Activo | Cerrado          *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionPlanTrabajo_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionPlanTrabajo_RM
    (
        PlanID             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupPT_RM PRIMARY KEY,
        PlanNumero         AS ('PT-' + RIGHT('00000' + CONVERT(varchar(10), PlanID), 5)) PERSISTED,
        SupCod             varchar(20)  NOT NULL,
        SupNombre          varchar(100) NULL,
        FechaIni           date         NOT NULL,
        FechaFin           date         NOT NULL,
        Semana             varchar(10)  NULL,            -- ej. 2026-W42
        Objetivo           varchar(500) NULL,            -- objetivo de la semana
        Opcion             varchar(20)  NULL,            -- equilibrado | riesgo | cobertura | blanco | excel
        Estatus            varchar(12)  NOT NULL CONSTRAINT DF_SupPT_Est DEFAULT('Borrador'),   -- Borrador | Por aprobar | Activo | Cerrado
        Origen             varchar(10)  NOT NULL CONSTRAINT DF_SupPT_Ori DEFAULT('linea'),   -- linea | excel
        LogroPct           decimal(5,1) NULL,            -- congelado al cerrar el plan
        CierreLogros       varchar(1000) NULL,
        CierreAprendizajes varchar(1000) NULL,
        CierreCompromisos  varchar(1000) NULL,
        EnviadoPor         varchar(64)  NULL,            -- supervisor que lo envio a aprobacion
        FechaEnvio         datetime     NULL,
        AprobadoPor        varchar(64)  NULL,            -- Gerencia
        FechaAprobacion    datetime     NULL,
        ComentarioGerencia varchar(500) NULL,            -- comentario al aprobar o al devolver
        CreadoPor          varchar(64)  NOT NULL,
        FechaCreacion      datetime     NOT NULL CONSTRAINT DF_SupPT_FC DEFAULT(GETDATE()),
        ActualizadoPor     varchar(64)  NULL,
        FechaActualizacion datetime     NULL,
        CerradoPor         varchar(64)  NULL,
        FechaCierre        datetime     NULL
    );
    CREATE INDEX IX_SupPT_SupFecha ON dbo.SupervisionPlanTrabajo_RM(SupCod, FechaIni);
END
GO

/* ============================================================== *
 * 2) ACTIVIDADES DEL PLAN (planificado + estado actual)          *
 *    Tipo: calle | virtual | cobros | reunion | gestion | capacitacion | otro *
 *    Estado: Pendiente | Realizada | Parcial | No realizada | Reprogramada | Cancelada *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionPlanTrabajoAct_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionPlanTrabajoAct_RM
    (
        ActID              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupPTAct_RM PRIMARY KEY,
        PlanID             int          NOT NULL CONSTRAINT FK_SupPTAct_Plan REFERENCES dbo.SupervisionPlanTrabajo_RM(PlanID) ON DELETE CASCADE,
        Fecha              date         NOT NULL,
        HoraIni            varchar(5)   NULL,            -- HH:mm
        HoraFin            varchar(5)   NULL,
        Tipo               varchar(14)  NOT NULL,
        Subtipo            varchar(14)  NULL,            -- acomp | audit | visita | ...
        Titulo             varchar(200) NOT NULL,
        Detalle            varchar(500) NULL,
        RepCodigo          varchar(20)  NULL,            -- vendedor objetivo
        RepNombre          varchar(100) NULL,
        CliID              int          NULL,            -- cliente objetivo
        CliNombre          varchar(150) NULL,
        Meta               varchar(300) NULL,            -- que se espera lograr
        Prioridad          char(1)      NOT NULL CONSTRAINT DF_SupPTAct_Pri DEFAULT('M'),    -- A | M | B
        Peso               decimal(4,1) NOT NULL CONSTRAINT DF_SupPTAct_Peso DEFAULT(1),
        Fijo               bit          NOT NULL CONSTRAINT DF_SupPTAct_Fijo DEFAULT(0),     -- cobros 3:30, reunion 5:30, etc.
        Origen             varchar(8)   NOT NULL CONSTRAINT DF_SupPTAct_Ori DEFAULT('plan'), -- plan | extra
        Modalidad          varchar(10)  NULL,            -- presencial | virtual | llamada (colectiva)
        Alcance            varchar(10)  NULL,            -- vendedor | todos | grupo | division (con quien se reune o sale)
        AlcanceCod         varchar(20)  NULL,            -- codigo del grupo (supervisor) o de la division
        AlcanceNombre      varchar(100) NULL,
        Estado             varchar(14)  NOT NULL CONSTRAINT DF_SupPTAct_Est DEFAULT('Pendiente'),
        Orden              int          NOT NULL CONSTRAINT DF_SupPTAct_Ord DEFAULT(0),
        FechaActualizacion datetime     NOT NULL CONSTRAINT DF_SupPTAct_FA DEFAULT(GETDATE())
    );
    CREATE INDEX IX_SupPTAct_Plan ON dbo.SupervisionPlanTrabajoAct_RM(PlanID, Fecha, HoraIni);
END
GO

/* ============================================================== *
 * 3) EJECUCION: bitacora del trabajo realizado por actividad     *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionPlanTrabajoEje_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionPlanTrabajoEje_RM
    (
        EjeID             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupPTEje_RM PRIMARY KEY,
        ActID             int          NOT NULL CONSTRAINT FK_SupPTEje_Act REFERENCES dbo.SupervisionPlanTrabajoAct_RM(ActID) ON DELETE CASCADE,
        PlanID            int          NOT NULL,
        FechaRealizada    date         NOT NULL,
        HoraRealizada     varchar(5)   NULL,
        TrabajoRealizado  varchar(1000) NULL,
        RepAtendido       varchar(20)  NULL,             -- vendedor atendido
        RepAtendidoNombre varchar(100) NULL,
        CliID             int          NULL,             -- cliente atendido
        CliNombre         varchar(150) NULL,
        Resultado         varchar(1000) NULL,            -- resultado obtenido
        Estado            varchar(14)  NOT NULL,         -- estado de la actividad tras este registro
        RegistradoPor     varchar(64)  NOT NULL,
        FechaRegistro     datetime     NOT NULL CONSTRAINT DF_SupPTEje_FR DEFAULT(GETDATE())
    );
    CREATE INDEX IX_SupPTEje_Act ON dbo.SupervisionPlanTrabajoEje_RM(ActID);
    CREATE INDEX IX_SupPTEje_Plan ON dbo.SupervisionPlanTrabajoEje_RM(PlanID);
END
GO

/* ============================================================== *
 * 3b) Columnas nuevas en bases donde la tabla ya existia         *
 *     (alcance: Todos / Grupo / Division / Vendedor + modalidad)  *
 * ============================================================== */
IF COL_LENGTH('dbo.SupervisionPlanTrabajoAct_RM','Modalidad') IS NULL ALTER TABLE dbo.SupervisionPlanTrabajoAct_RM ADD Modalidad varchar(10) NULL;
IF COL_LENGTH('dbo.SupervisionPlanTrabajoAct_RM','Alcance') IS NULL ALTER TABLE dbo.SupervisionPlanTrabajoAct_RM ADD Alcance varchar(10) NULL;
IF COL_LENGTH('dbo.SupervisionPlanTrabajoAct_RM','AlcanceCod') IS NULL ALTER TABLE dbo.SupervisionPlanTrabajoAct_RM ADD AlcanceCod varchar(20) NULL;
IF COL_LENGTH('dbo.SupervisionPlanTrabajoAct_RM','AlcanceNombre') IS NULL ALTER TABLE dbo.SupervisionPlanTrabajoAct_RM ADD AlcanceNombre varchar(100) NULL;
GO

/* ============================================================== *
 * 3c) REUNIONES FIJAS SEMANALES del supervisor                   *
 *     (p. ej. "todos los jueves 5:00 pm por Teams con mis vendedores"). *
 *     Se incluyen solas en cada propuesta de plan. SupCod = codigo principal de la persona. *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionPlanTrabajoRec_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionPlanTrabajoRec_RM
    (
        RecID         int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupPTRec_RM PRIMARY KEY,
        SupCod        varchar(20)  NOT NULL,
        Dia           tinyint      NOT NULL,            -- 1=lunes ... 6=sabado
        HoraIni       varchar(5)   NULL,
        HoraFin       varchar(5)   NULL,
        Titulo        varchar(200) NOT NULL,
        Modalidad     varchar(10)  NULL,                -- presencial | virtual | llamada
        Alcance       varchar(10)  NULL,                -- todos | grupo | division | vendedor
        AlcanceCod    varchar(20)  NULL,
        AlcanceNombre varchar(100) NULL,
        Meta          varchar(300) NULL,
        Activo        bit          NOT NULL CONSTRAINT DF_SupPTRec_Act DEFAULT(1),
        CreadoPor     varchar(64)  NOT NULL,
        FechaCreacion datetime     NOT NULL CONSTRAINT DF_SupPTRec_FC DEFAULT(GETDATE())
    );
    CREATE INDEX IX_SupPTRec_Sup ON dbo.SupervisionPlanTrabajoRec_RM(SupCod, Dia);
END
GO

/* ============================================================== *
 * 4) PERMISOS para la app (usuario "Ventas")                    *
 * ============================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionPlanTrabajo_RM          TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionPlanTrabajoAct_RM TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionPlanTrabajoEje_RM TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionPlanTrabajoRec_RM TO [Ventas];
GO
