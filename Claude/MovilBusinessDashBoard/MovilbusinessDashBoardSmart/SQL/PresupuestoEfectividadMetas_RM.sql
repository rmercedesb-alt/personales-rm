/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoEfectividadMetas_RM (2026-09-01) — metas de efectividad
   mínima/ideal por División (RepTipe), pedido explícito del usuario dentro
   del rediseño de premisas del motor: "un entorno para configurar la
   efectividad minima ideal, en este caso esto debe ser por division
   (Reptipe)".

   Mismo patrón que PresupuestoActivacionMetas_RM (Modo Ideal/Obligatorio,
   BloqueaPublicar) pero con 1 sola dimensión (División) — decisión
   confirmada con el usuario vía AskUserQuestion: "Obligatorio" fuerza un
   piso en el monto propuesto (mismo mecanismo de reparto proporcional que
   ya usa Activación); "Ideal" es solo informativo.

   RepTipe='' (cadena vacía) representa la fila DEFAULT — aplica a
   cualquier división que no tenga su propia fila específica.

   Ejecutar en MovilBusiness5STD (producción).
   ═══════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID('dbo.PresupuestoEfectividadMetas_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoEfectividadMetas_RM(
        Id                  int IDENTITY(1,1) PRIMARY KEY,
        RepTipe             varchar(10)     NOT NULL,   -- División; '' = default (todas las divisiones sin fila propia)
        MetaPct             decimal(9,4)    NOT NULL,   -- 0..1 (ej. 0.70 = 70% clientes con pedido / visitados)
        Modo                varchar(12)     NOT NULL CONSTRAINT DF_PresupuestoEfectividadMetas_RM_Modo DEFAULT('Ideal'), -- 'Ideal' | 'Obligatorio'
        BloqueaPublicar     bit             NOT NULL CONSTRAINT DF_PresupuestoEfectividadMetas_RM_Bloquea DEFAULT(0),
        Activo              bit             NOT NULL CONSTRAINT DF_PresupuestoEfectividadMetas_RM_Activo DEFAULT(1),
        UsuInicioSesion     varchar(64)     NULL,
        FechaActualizacion  datetime        NOT NULL CONSTRAINT DF_PresupuestoEfectividadMetas_RM_Fecha DEFAULT(GETDATE()),
        CONSTRAINT UQ_PresupuestoEfectividadMetas_RM UNIQUE(RepTipe)
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoEfectividadMetas_RM TO Ventas;
GO
