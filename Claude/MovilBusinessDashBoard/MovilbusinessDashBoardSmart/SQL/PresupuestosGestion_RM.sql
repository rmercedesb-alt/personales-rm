/* ============================================================================
   Módulo "Presupuestos Smart" — Crear/Editar/Eliminar/Revisar presupuestos
   ============================================================================
   5 tablas nuevas (sufijo _RM, misma convención que las demás tablas a medida
   de este proyecto: SupervisionCobrosConfig_RM, etc.). Ver plan completo en
   la conversación / bubbly-spinning-blum.md.

   Ejecutar PRIMERO en MovilBusiness5STD_Test (donde se construye y prueba el
   módulo). El mismo script queda listo para correr en MovilBusiness5STD
   (producción) cuando el usuario lo apruebe — solo cambia el USE de abajo.

   El login "Ventas" (compartido con RutaVisitasSmart/MovilbusinessDashBoardSmart)
   NO tiene permiso CREATE TABLE (verificado en vivo) — este script requiere un
   usuario con más privilegios (TI / dbo). Al final se le otorga a "Ventas" el
   SELECT/INSERT/UPDATE/DELETE que necesita para operar el módulo día a día.
   ============================================================================ */
USE [MovilBusiness5STD_Test];
GO

/* 1) Catálogo de variables ajustables — extensible sin tocar código ni volver
      a correr un script: para agregar una variable nueva basta un INSERT aquí. */
IF OBJECT_ID('dbo.PresupuestoVariables_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoVariables_RM(
        VarCodigo           varchar(30)  NOT NULL PRIMARY KEY,
        VarNombre           varchar(100) NOT NULL,
        VarDescripcion      varchar(300) NULL,
        VarTipoDato         varchar(20)  NOT NULL,   -- 'decimal' | 'int' | 'texto'
        VarAmbitoPermitido  varchar(50)  NOT NULL,   -- csv, ej. 'Suplidor,Categoria,RutaSuplidor,Global'
        Activo              bit          NOT NULL CONSTRAINT DF_PresupuestoVariables_RM_Activo DEFAULT(1),
        UsuInicioSesion     varchar(64)  NULL,
        FechaActualizacion  datetime     NOT NULL CONSTRAINT DF_PresupuestoVariables_RM_Fecha DEFAULT(GETDATE())
    );
END
GO

/* 2) Valores concretos de cada variable, con alcance (a qué aplica) y vigencia
      (cuándo aplica). Resolución en cascada al calcular: más específico gana
      — Mes > Trimestre > Año; RutaSuplidor > Suplidor/Categoria > Global. Sin
      fila que aplique, el motor usa un valor neutro (1.0 para factores). */
IF OBJECT_ID('dbo.PresupuestoVariablesValores_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoVariablesValores_RM(
        Id                  int IDENTITY(1,1) PRIMARY KEY,
        VarCodigo           varchar(30)     NOT NULL,
        AmbitoTipo          varchar(20)     NOT NULL,   -- 'Suplidor' | 'Categoria' | 'RutaSuplidor' | 'Global'
        AmbitoValor         varchar(50)     NULL,       -- Cat3ID, Cat1ID, 'RepCodigo|Cat3ID', o NULL si Global
        PeriodoTipo         varchar(10)     NOT NULL,   -- 'Anio' | 'Trimestre' | 'Mes'
        PeriodoAnio         smallint        NOT NULL,
        PeriodoTrimestre    tinyint         NULL,
        PeriodoMes          tinyint         NULL,
        Valor               decimal(18,5)   NOT NULL,
        UsuInicioSesion     varchar(64)     NULL,
        FechaActualizacion  datetime        NOT NULL CONSTRAINT DF_PresupuestoVariablesValores_RM_Fecha DEFAULT(GETDATE()),
        CONSTRAINT FK_PresupuestoVariablesValores_RM_Variable FOREIGN KEY (VarCodigo) REFERENCES dbo.PresupuestoVariables_RM(VarCodigo)
    );
    CREATE INDEX IX_PresupuestoVariablesValores_RM_Busqueda
        ON dbo.PresupuestoVariablesValores_RM(VarCodigo, AmbitoTipo, AmbitoValor, PeriodoAnio, PeriodoTrimestre, PeriodoMes);
END
GO

/* 3) Cabecera de cada corrida de staging (una "propuesta" generada). */
IF OBJECT_ID('dbo.PresupuestoLotes_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoLotes_RM(
        LoteId              uniqueidentifier NOT NULL CONSTRAINT DF_PresupuestoLotes_RM_LoteId DEFAULT(NEWID()) PRIMARY KEY,
        Descripcion         varchar(200)     NULL,
        BaseFechaInicio     date             NOT NULL,
        BaseFechaFin        date             NOT NULL,
        DestinoDetalle      varchar(200)     NOT NULL,   -- ej. "2026-09,2026-10,2026-11,2026-12"
        PreTiposIncluidos   varchar(100)     NOT NULL,   -- ej. "Unddprod,UNDDLINACT"
        Estado              varchar(20)      NOT NULL CONSTRAINT DF_PresupuestoLotes_RM_Estado DEFAULT('Borrador'), -- Borrador|Publicado|Descartado
        UsuInicioSesion     varchar(64)      NOT NULL,
        FechaCreacion       datetime         NOT NULL CONSTRAINT DF_PresupuestoLotes_RM_FechaCreacion DEFAULT(GETDATE()),
        UsuPublico          varchar(64)      NULL,
        FechaPublicacion    datetime         NULL
    );
END
GO

/* 4) Detalle propuesto fila por fila, antes de publicar — mismo grano que
      PresupuestosHistoricos (Rep/PreTipo/Año/Mes/Referencia) más el valor
      ACTUAL (snapshot de lo ya publicado) para poder mostrar el diff y
      detectar anomalías sin volver a consultar PresupuestosHistoricos. */
IF OBJECT_ID('dbo.PresupuestoStaging_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoStaging_RM(
        Id                          int IDENTITY(1,1) PRIMARY KEY,
        LoteId                      uniqueidentifier NOT NULL,
        RepCodigo                   varchar(15)      NOT NULL,
        PreTipo                     varchar(20)      NOT NULL,
        PreAnio                     smallint         NOT NULL,
        PreMes                      smallint         NOT NULL,
        PreReferencia               varchar(30)      NOT NULL,
        PrePresupuestoPropuesto     decimal(18,4)    NOT NULL,
        PrePresupuestoActual        decimal(18,4)    NULL,
        Anomalia                    bit              NOT NULL CONSTRAINT DF_PresupuestoStaging_RM_Anomalia DEFAULT(0),
        AnomaliaDetalle             varchar(300)     NULL,
        CONSTRAINT FK_PresupuestoStaging_RM_Lote FOREIGN KEY (LoteId) REFERENCES dbo.PresupuestoLotes_RM(LoteId)
    );
    CREATE INDEX IX_PresupuestoStaging_RM_Lote ON dbo.PresupuestoStaging_RM(LoteId);
END
GO

/* 5) Bitácora mínima de publicaciones/ediciones — el "audit" ligero pedido
      (no versión campo-por-campo; un log de quién/cuándo/cuántas filas/monto
      alcanza; el detalle fino se ofrece como archivo descargable al usuario). */
IF OBJECT_ID('dbo.PresupuestoPublicaciones_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoPublicaciones_RM(
        Id                  int IDENTITY(1,1) PRIMARY KEY,
        LoteId              uniqueidentifier NULL,   -- NULL = edición puntual fuera de un lote
        UsuInicioSesion     varchar(64)      NOT NULL,
        FechaPublicacion    datetime         NOT NULL CONSTRAINT DF_PresupuestoPublicaciones_RM_Fecha DEFAULT(GETDATE()),
        FilasAfectadas      int              NOT NULL,
        SumaTotal           decimal(18,2)    NULL,
        Detalle             varchar(500)     NULL
    );
END
GO

/* 6) Semillas del catálogo de variables (v1) — agregar más adelante es un
      simple INSERT, no requiere cambiar esta tabla ni el código. */
IF NOT EXISTS (SELECT 1 FROM dbo.PresupuestoVariables_RM WHERE VarCodigo='FACTOR_CRECIMIENTO_MES')
INSERT INTO dbo.PresupuestoVariables_RM(VarCodigo,VarNombre,VarDescripcion,VarTipoDato,VarAmbitoPermitido,UsuInicioSesion) VALUES
('FACTOR_CRECIMIENTO_MES','Factor de crecimiento mensual','Multiplicador sobre la base histórica neta (1.02 = +2%). Se puede fijar por Suplidor/Categoría y por Año, Trimestre o Mes.','decimal','Suplidor,Categoria,Global','sistema'),
('META_RAPPEL','Meta de rappel (RD$/mes)','Monto fijo mensual objetivo para un Suplidor. El motor calcula el factor implícito = meta / base agregada de ese suplidor.','decimal','Suplidor','sistema'),
('PISO_MINIMO_PCT','Piso mínimo (%)','Nunca proponer menos de este % de la base histórica neta (ej. 100 = nunca bajar de la base).','decimal','Suplidor,Categoria,RutaSuplidor,Global','sistema'),
('FACTOR_RUTA_PROVEEDOR','Factor fino por ruta×proveedor','Ajuste puntual para una ruta y proveedor específicos, por encima del factor general del proveedor.','decimal','RutaSuplidor','sistema'),
('UMBRAL_ANOMALIA_PCT','Umbral de anomalía (%)','Si el propuesto se desvía de la base más de este %, se marca como anomalía para revisión (no bloquea).','decimal','Global','sistema');
GO

/* 7) Otorgar a "Ventas" lo que necesita para operar el módulo (no DDL, solo DML). */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoVariables_RM         TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoVariablesValores_RM  TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoLotes_RM             TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoStaging_RM           TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoPublicaciones_RM     TO [Ventas];
GO

/* 8) Verificación */
SELECT 'PresupuestoVariables_RM' Tabla, COUNT(*) Filas FROM dbo.PresupuestoVariables_RM
UNION ALL SELECT 'PresupuestoVariablesValores_RM', COUNT(*) FROM dbo.PresupuestoVariablesValores_RM
UNION ALL SELECT 'PresupuestoLotes_RM', COUNT(*) FROM dbo.PresupuestoLotes_RM
UNION ALL SELECT 'PresupuestoStaging_RM', COUNT(*) FROM dbo.PresupuestoStaging_RM
UNION ALL SELECT 'PresupuestoPublicaciones_RM', COUNT(*) FROM dbo.PresupuestoPublicaciones_RM;

SELECT dp.name Usuario, obj.name Tabla, perm.permission_name Permiso, perm.state_desc Estado
FROM sys.database_permissions perm
JOIN sys.database_principals dp ON dp.principal_id = perm.grantee_principal_id
JOIN sys.objects obj ON obj.object_id = perm.major_id
WHERE dp.name = 'Ventas' AND obj.name LIKE 'Presupuesto%_RM'
ORDER BY obj.name, perm.permission_name;
GO
