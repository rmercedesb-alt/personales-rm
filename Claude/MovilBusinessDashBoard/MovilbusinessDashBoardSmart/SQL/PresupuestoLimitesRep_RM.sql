/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoLimitesRep_RM (2026-09-01) — piso y techo por vendedor,
   pedido explícito del usuario dentro del rediseño de premisas del motor
   de Presupuestos Smart ("Minimos, Maximos" como parámetro al generar).

   Decidido con el usuario (AskUserQuestion 2026-09-01): el piso/techo es
   un % SOBRE LA PROPIA BASE HISTÓRICA de cada vendedor (ej. PctMinimo=0.90
   = no proponerle menos del 90% de lo que él mismo vendió en el Rango
   Base; PctMaximo=1.30 = no proponerle más del 130%) — NO un monto fijo
   en RD$, para que no haya que reconfigurar cada vez que cambian precios
   o el tamaño de cada vendedor.

   El motor (PresupuestoBuilderCore.CargarLimitesRep, hook en Generar
   DESPUÉS de activación/efectividad obligatorias y ANTES del reescalado a
   'Monto Total Sugerido') recorta proporcionalmente el propuesto total de
   un rep si cae fuera de [baseNetaRep*PctMinimo, baseNetaRep*PctMaximo].

   Ejecutar en MovilBusiness5STD (producción) — mismo criterio que
   SQL/PresupuestoActivacionMetas_RM.sql.
   ═══════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID('dbo.PresupuestoLimitesRep_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoLimitesRep_RM(
        RepCodigo           varchar(15)     NOT NULL PRIMARY KEY,
        PctMinimo           decimal(9,4)    NULL,   -- 0.90 = no bajar de 90% de su propia base histórica
        PctMaximo           decimal(9,4)    NULL,   -- 1.30 = no subir de 130% de su propia base histórica
        Activo              bit             NOT NULL CONSTRAINT DF_PresupuestoLimitesRep_RM_Activo DEFAULT(1),
        UsuInicioSesion     varchar(64)     NULL,
        FechaActualizacion  datetime        NOT NULL CONSTRAINT DF_PresupuestoLimitesRep_RM_Fecha DEFAULT(GETDATE())
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoLimitesRep_RM TO Ventas;
GO
