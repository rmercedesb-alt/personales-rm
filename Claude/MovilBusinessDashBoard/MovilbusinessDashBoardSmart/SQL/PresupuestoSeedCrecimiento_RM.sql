/* ═══════════════════════════════════════════════════════════════════════
   Semilla de configuración real — Presupuestos Smart (2026-08-30)
   ═══════════════════════════════════════════════════════════════════════
   Pedido explícito del usuario: cargar en PresupuestoVariablesValores_RM
   (PRODUCCIÓN, MovilBusiness5STD) las metas de rappel y las curvas de
   crecimiento mes-a-mes por Suplidor que ya trae el archivo entregado por el
   consultor externo (Presupuestos Sugerido Septiembre - Diciembre\
   modelo_params.py) para Sep-Dic 2026. Quedan editables/borrables desde el
   ⚙️ "Configuración de variables/metodologías" como cualquier otro valor —
   nada queda hardcodeado en C#.

   Cat3ID verificados en vivo contra Categoria3 (producción) el 2026-08-30:
     101 = Continental de Negocios
     978 = MOLINOS MODERNOS, SA
     201 = Brinsa Dominicana
     106 = Cesar Iglesias (Unilever)
     954 = COLOMBINA

   NOTA: el archivo original también trae FAC_MAYOR (Mayorista) y
   FAC_FERNANDO (Especial/MiniMarket) — esos son factores por FUERZA DE VENTA
   /CANAL, no por Suplidor, y el motor de Presupuestos Smart hoy solo resuelve
   FACTOR_CRECIMIENTO_MES por Suplidor/Categoría/RutaSuplidor/Global (no por
   Canal ni por "fuerza"). Quedan FUERA de esta semilla — si se necesitan más
   adelante, requieren un AmbitoTipo nuevo ('Canal' o 'Fuerza') en el motor,
   no solo datos.

   Ejecutar en MovilBusiness5STD (producción) — mismo criterio que
   SQL/PresupuestosGestion_RM.sql. Seguro de re-ejecutar: cada INSERT valida
   que no exista ya una fila idéntica (VarCodigo+AmbitoTipo+AmbitoValor+
   PeriodoTipo+PeriodoAnio+PeriodoMes) antes de insertar.
   ═══════════════════════════════════════════════════════════════════════ */

DECLARE @u varchar(64) = 'seed_2026-08-30';

-- ── FACTOR_CRECIMIENTO_MES — curva mensual Sep→Dic 2026 por Suplidor ───────
DECLARE @Factores TABLE (Cat3ID varchar(50), Mes tinyint, Valor decimal(18,5));
INSERT INTO @Factores (Cat3ID, Mes, Valor) VALUES
-- Continental de Negocios (101) — activación "Nosotras" gradual
('101', 9, 1.005), ('101', 10, 1.010), ('101', 11, 1.015), ('101', 12, 1.020),
-- MOLINOS MODERNOS SA (978) — espejo plano (rappel sell-in vía inventario)
('978', 9, 1.000), ('978', 10, 1.000), ('978', 11, 1.000), ('978', 12, 1.000),
-- Brinsa Dominicana (201) — Refisal/Blancox gradual
('201', 9, 1.007), ('201', 10, 1.014), ('201', 11, 1.021), ('201', 12, 1.028),
-- COLOMBINA (954) — gradual desde octubre
('954', 9, 1.000), ('954', 10, 1.005), ('954', 11, 1.010), ('954', 12, 1.015),
-- Cesar Iglesias / Unilever (106) — rueda con el mayorista, plano por Suplidor
('106', 9, 1.000), ('106', 10, 1.000), ('106', 11, 1.000), ('106', 12, 1.000);

INSERT INTO dbo.PresupuestoVariablesValores_RM(VarCodigo, AmbitoTipo, AmbitoValor, PeriodoTipo, PeriodoAnio, PeriodoTrimestre, PeriodoMes, Valor, UsuInicioSesion)
SELECT 'FACTOR_CRECIMIENTO_MES', 'Suplidor', f.Cat3ID, 'Mes', 2026, NULL, f.Mes, f.Valor, @u
FROM @Factores f
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.PresupuestoVariablesValores_RM v
    WHERE v.VarCodigo='FACTOR_CRECIMIENTO_MES' AND v.AmbitoTipo='Suplidor' AND v.AmbitoValor=f.Cat3ID
      AND v.PeriodoTipo='Mes' AND v.PeriodoAnio=2026 AND v.PeriodoMes=f.Mes
);

-- ── META_RAPPEL — meta fija RD$/mes por Suplidor (aplica a todo 2026) ──────
DECLARE @Rappel TABLE (Cat3ID varchar(50), Valor decimal(18,5));
INSERT INTO @Rappel (Cat3ID, Valor) VALUES
('978', 22800000),  -- Molinos
('101', 22410192),  -- Continental
('954', 8695146),   -- Colombina
('106', 9000000);   -- Unilever

INSERT INTO dbo.PresupuestoVariablesValores_RM(VarCodigo, AmbitoTipo, AmbitoValor, PeriodoTipo, PeriodoAnio, PeriodoTrimestre, PeriodoMes, Valor, UsuInicioSesion)
SELECT 'META_RAPPEL', 'Suplidor', r.Cat3ID, 'Anio', 2026, NULL, NULL, r.Valor, @u
FROM @Rappel r
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.PresupuestoVariablesValores_RM v
    WHERE v.VarCodigo='META_RAPPEL' AND v.AmbitoTipo='Suplidor' AND v.AmbitoValor=r.Cat3ID
      AND v.PeriodoTipo='Anio' AND v.PeriodoAnio=2026
);

SELECT VarCodigo, AmbitoValor, PeriodoTipo, PeriodoMes, Valor
FROM dbo.PresupuestoVariablesValores_RM
WHERE UsuInicioSesion=@u
ORDER BY VarCodigo, AmbitoValor, PeriodoMes;
