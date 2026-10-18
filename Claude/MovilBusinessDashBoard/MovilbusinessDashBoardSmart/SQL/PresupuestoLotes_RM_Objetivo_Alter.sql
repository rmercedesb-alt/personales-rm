/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoLotes_RM — ALTER (2026-09-06): persiste el Objetivo elegido al
   Generar como columnas estructuradas, en vez de solo texto libre embebido
   en Descripción. Pedido explícito del usuario dentro del rediseño "Objetivo
   por Mes + Total híbrido": el Excel de revisión (Bloque B) necesita poder
   mostrar "Objetivo seleccionado vs. suma actual del lote" sin tener que
   re-parsear la Descripción.

   ObjetivoModo: 'valor' (RD$) | 'unidades' | NULL (no se puso ningún objetivo).
   ObjetivoTotalRD: el Total combinado (el puesto directo, o la suma de los
     objetivos por mes si no hubo Total explícito) — de solo lectura para
     mostrar, NO se vuelve a usar para recalcular nada.
   ObjetivoPorMesJson: detalle por mes si se usó 'objetivosPorMes' (JSON simple
     [{"anio":2026,"mes":9,"valor":...}]) — NULL si solo se usó el Total.

   Ejecutar en MovilBusiness5STD (producción) — mismo criterio que los demás
   ALTER de este proyecto (PresupuestoActivacionMetas_RM_v3.sql, etc.).
   ═══════════════════════════════════════════════════════════════════════ */

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PresupuestoLotes_RM') AND name = 'ObjetivoModo')
BEGIN
    ALTER TABLE dbo.PresupuestoLotes_RM ADD ObjetivoModo varchar(10) NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PresupuestoLotes_RM') AND name = 'ObjetivoTotalRD')
BEGIN
    ALTER TABLE dbo.PresupuestoLotes_RM ADD ObjetivoTotalRD decimal(18,2) NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PresupuestoLotes_RM') AND name = 'ObjetivoPorMesJson')
BEGIN
    ALTER TABLE dbo.PresupuestoLotes_RM ADD ObjetivoPorMesJson varchar(1000) NULL;
END
GO
