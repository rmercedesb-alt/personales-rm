/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoActivacionMetas_RM — v2 (2026-09-01): agrega override en
   cascada por Canal y por División (RepTipe), pedido explícito del usuario:
   "necesito que agregar parametros por activaciones % Categoria/suplidor
   pero por canales y division de vendedores (Reptipe) para que cada uno
   tenga su propio excenario".

   Decidido con el usuario (AskUserQuestion 2026-09-01): NO es una llave
   combinada Cat3×Cat1×Canal×División (eso obligaría a llenar cada
   combinación exacta) — son 2 CAPAS DE OVERRIDE OPCIONALES sobre el mismo
   default Cat3×Cat1 de siempre: una fila puede especificar SOLO CanID
   (aplica a ese canal, cualquier división), o SOLO RepTipe (aplica a esa
   división, cualquier canal), o ninguno de los dos (fila default). El
   motor (PresupuestoBuilderCore.ResolverActivacionMeta) resuelve en
   cascada: RepTipe del rep > CanID del rep > default — más específico
   gana, mismo espíritu que ya usa ResolverVariableEnMemoria para
   PresupuestoVariablesValores_RM.

   La tabla YA EXISTE en producción (TI la corrió con la v1, 0 filas hoy)
   — este script es un ALTER, no una recreación. Ejecutar en
   MovilBusiness5STD (producción).
   ═══════════════════════════════════════════════════════════════════════ */

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PresupuestoActivacionMetas_RM') AND name = 'CanID')
BEGIN
    ALTER TABLE dbo.PresupuestoActivacionMetas_RM ADD CanID int NULL;   -- Canal (CanalDistribucion.CanID); NULL = todos los canales
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PresupuestoActivacionMetas_RM') AND name = 'RepTipe')
BEGIN
    ALTER TABLE dbo.PresupuestoActivacionMetas_RM ADD RepTipe varchar(10) NULL; -- División (RepresentantesAdicionales.RepTipe); NULL = todas las divisiones
END
GO

-- La UQ vieja (Cat3ID, Cat1ID) no permite más de 1 fila por Suplidor×Categoría
-- — hay que reemplazarla para que quepan las filas de override (canal/división)
-- junto a la fila default, sin permitir 2 filas default duplicadas ni 2 filas
-- iguales para el mismo canal o la misma división.
--
-- Nota: CREATE INDEX no acepta expresiones (ISNULL(...)) en la lista de
-- columnas — error "Incorrect syntax near '('" (Msg 102) si se intenta. No
-- hace falta de todos modos: SQL Server SÍ trata NULL como un valor más para
-- verificar unicidad en un índice único compuesto (a diferencia del estándar
-- ANSI) — dos filas con el mismo Cat3ID/Cat1ID y AMBAS con CanID/RepTipe NULL
-- ya se consideran duplicadas sin necesidad de normalizar con ISNULL.
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_PresupuestoActivacionMetas_RM')
BEGIN
    ALTER TABLE dbo.PresupuestoActivacionMetas_RM DROP CONSTRAINT UQ_PresupuestoActivacionMetas_RM;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_PresupuestoActivacionMetas_RM_v2' AND object_id = OBJECT_ID('dbo.PresupuestoActivacionMetas_RM'))
BEGIN
    CREATE UNIQUE INDEX UQ_PresupuestoActivacionMetas_RM_v2 ON dbo.PresupuestoActivacionMetas_RM(Cat3ID, Cat1ID, CanID, RepTipe);
END
GO
