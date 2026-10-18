/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoActivacionMetas_RM (2026-08-30) — metas de activación mínima
   por Suplidor×Categoría, pedido explícito del usuario: "el tema de las
   activaciones por suplidor mínimas... debería impactar en algunos casos de
   manera obligatoria y en otros de manera ideal".

   Decidido con el usuario (AskUserQuestion 2026-08-30): "Obligatorio" fuerza
   el piso en el presupuesto propuesto (sube lo necesario, repartido entre los
   SKU de esa categoría/suplidor según su mezcla actual) — configurable por
   fila si además debe BLOQUEAR Publicar cuando ni forzando se alcanza la
   meta (BloqueaPublicar=1; el default es 0, o sea solo forzar el número).
   "Ideal" nunca fuerza ni bloquea, solo se muestra como brecha informativa.

   Ejecutar en MovilBusiness5STD (producción) — mismo criterio que
   SQL/PresupuestosGestion_RM.sql.
   ═══════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID('dbo.PresupuestoActivacionMetas_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoActivacionMetas_RM(
        Id                  int IDENTITY(1,1) PRIMARY KEY,
        Cat3ID              int             NOT NULL,   -- Suplidor
        Cat1ID              int             NOT NULL,   -- Categoría
        MetaPct             decimal(9,4)    NOT NULL,   -- 0..1 (ej. 0.60 = 60% de la cartera)
        Modo                varchar(12)     NOT NULL CONSTRAINT DF_PresupuestoActivacionMetas_RM_Modo DEFAULT('Ideal'), -- 'Ideal' | 'Obligatorio'
        BloqueaPublicar     bit             NOT NULL CONSTRAINT DF_PresupuestoActivacionMetas_RM_Bloquea DEFAULT(0),
        Activo              bit             NOT NULL CONSTRAINT DF_PresupuestoActivacionMetas_RM_Activo DEFAULT(1),
        UsuInicioSesion     varchar(64)     NULL,
        FechaActualizacion  datetime        NOT NULL CONSTRAINT DF_PresupuestoActivacionMetas_RM_Fecha DEFAULT(GETDATE()),
        CONSTRAINT UQ_PresupuestoActivacionMetas_RM UNIQUE(Cat3ID, Cat1ID)
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoActivacionMetas_RM TO Ventas;
GO
