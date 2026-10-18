/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoLotes_RM.Descripcion — ampliar de varchar(200) a varchar(4000)
   (2026-09-06). Bug real reportado por el usuario EN VIVO al probar el
   rediseño de reglas (19ª parte de PROGRESS.md): "String or binary data
   would be truncated... column 'Descripcion'".

   Causa: la Descripción acumula, además del texto del usuario, TODAS las
   notas que arma Generar (comparación, objetivo/redistribución por mes,
   activación forzada por grupo, tope de activación, efectividad forzada por
   categoría, bloqueos) — con el rediseño de reglas esas notas crecieron
   mucho más que antes (varias líneas por cada categoría/mes que se fuerza),
   y 200 caracteres ya no alcanzan ni con pocos grupos forzados.

   El código (ApiPresupuestoGestionController.cs, 'descripcionFinal') además
   corta defensivamente a 4000 caracteres antes de insertar, así que este
   ALTER es el tope real — no hace falta más grande.

   Ejecutar en MovilBusiness5STD (producción) — mismo criterio que
   SQL/PresupuestoLotes_RM_Objetivo_Alter.sql (ya corrido).
   ═══════════════════════════════════════════════════════════════════════ */

IF EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.PresupuestoLotes_RM') AND c.name = 'Descripcion' AND c.max_length < 4000
)
BEGIN
    ALTER TABLE dbo.PresupuestoLotes_RM ALTER COLUMN Descripcion varchar(4000) NULL;
END
GO
