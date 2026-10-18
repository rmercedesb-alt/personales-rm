/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoActivacionMetas_RM — v3 (2026-09-01): agrega una 3ra capa de
   override, por Representante (Vendedor) individual — pedido explícito del
   usuario: "esto me debería llevar a asignar presupuestos categoría/Vendedor
   ...en los casos que tenga que dar seguimiento específico".

   Cascada de resolución final (más específico gana):
   RepCodigo > RepTipe (División) > CanID (Canal) > default (los 3 en NULL).

   Igual que Canal/División: a lo sumo UNO de los 3 campos de override por
   fila (nunca combinados) — el motor (PresupuestoBuilderCore.
   ResolverActivacionMeta) valida esto al resolver, y el endpoint de guardado
   lo valida al escribir.

   La tabla YA EXISTE en producción con CanID/RepTipe (v2, corrida). Este
   script es un ALTER más, sin recrear nada. Ejecutar en MovilBusiness5STD.
   ═══════════════════════════════════════════════════════════════════════ */

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PresupuestoActivacionMetas_RM') AND name = 'RepCodigo')
BEGIN
    ALTER TABLE dbo.PresupuestoActivacionMetas_RM ADD RepCodigo varchar(15) NULL; -- Vendedor específico; NULL = no aplica a nivel de rep individual
END
GO

-- Reemplaza el índice único de la v2 (Cat3ID, Cat1ID, CanID, RepTipe) por uno
-- que también incluya RepCodigo — mismo criterio ya usado en v2: sin ISNULL()
-- en la lista de columnas (Msg 102, "Incorrect syntax near '('" si se intenta
-- — CREATE INDEX no acepta expresiones ahí). SQL Server ya trata NULL como
-- valor comparable en un índice único compuesto, así que no hace falta.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_PresupuestoActivacionMetas_RM_v2' AND object_id = OBJECT_ID('dbo.PresupuestoActivacionMetas_RM'))
BEGIN
    DROP INDEX UQ_PresupuestoActivacionMetas_RM_v2 ON dbo.PresupuestoActivacionMetas_RM;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_PresupuestoActivacionMetas_RM_v3' AND object_id = OBJECT_ID('dbo.PresupuestoActivacionMetas_RM'))
BEGIN
    CREATE UNIQUE INDEX UQ_PresupuestoActivacionMetas_RM_v3 ON dbo.PresupuestoActivacionMetas_RM(Cat3ID, Cat1ID, CanID, RepTipe, RepCodigo);
END
GO
