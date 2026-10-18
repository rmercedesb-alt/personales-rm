/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoTopeActivacion_RM_Seed (2026-09-06) — 1 variable NUEVA dentro
   del catálogo genérico YA EXISTENTE PresupuestoVariables_RM/Valores_RM, NO
   una tabla nueva (mismo criterio que UMBRAL_MINIMO_RD/UMBRAL_MAXIMO_RD).

   Pedido explícito del usuario: el incremento que fuerza Activación mínima
   Obligatoria (Suplidor×Categoría) no debe incidir en más del 5% del
   presupuesto TOTAL del lote que se está generando. El motor (Generar,
   PASO 1b en ApiPresupuestoGestionController.cs) calcula en RD$ cuánto pide
   Activación en total (dry-run, antes de aplicar nada) y si excede este %
   del presupuesto natural del lote, recorta proporcionalmente todos los
   grupos forzados para encajar en el tope.

   Si no hay ninguna fila configurada, el motor usa 5 (%) como default
   neutro — no bloquea nada si el usuario no la toca.

   Ejecutar en MovilBusiness5STD (producción) — mismo criterio que
   SQL/PresupuestoUmbralesRD_Seed.sql.
   ═══════════════════════════════════════════════════════════════════════ */

IF NOT EXISTS (SELECT 1 FROM PresupuestoVariables_RM WHERE VarCodigo = 'TOPE_ACTIVACION_PCT')
BEGIN
    INSERT INTO PresupuestoVariables_RM(VarCodigo, VarNombre, VarDescripcion, VarTipoDato, VarAmbitoPermitido, Activo)
    VALUES('TOPE_ACTIVACION_PCT', 'Tope de Activación (%)',
           'Porcentaje MÁXIMO del presupuesto total del lote que puede venir del incremento forzado por metas de Activación mínima Obligatoria (Suplidor×Categoría). Si el total pedido por todas las categorías forzadas excede este %, se recorta proporcionalmente entre ellas. Ámbito Global únicamente.',
           'decimal', 'Global', 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM PresupuestoVariablesValores_RM WHERE VarCodigo='TOPE_ACTIVACION_PCT' AND AmbitoTipo='Global' AND PeriodoTipo='Anio' AND PeriodoAnio=2026)
BEGIN
    INSERT INTO PresupuestoVariablesValores_RM(VarCodigo, AmbitoTipo, AmbitoValor, PeriodoTipo, PeriodoAnio, PeriodoTrimestre, PeriodoMes, Valor, UsuInicioSesion)
    VALUES('TOPE_ACTIVACION_PCT', 'Global', NULL, 'Anio', 2026, NULL, NULL, 5, 'seed_2026-09-06');
END
GO
