/* ═══════════════════════════════════════════════════════════════════════
   Carga inicial de metas de activación ideal — "3. Tabla de Activación
   Objetivo.xlsx" (Rudy, 2026-09-01), 31 pares Suplidor×Categoría.

   Decisiones confirmadas con el usuario antes de generar este script:
   1. Salsas (Molinos) = Cat1ID 283 "SALSAS" (no 199 "Grupo P (Salsas)").
   2. Alcance: CanID=2000 (Minorista) explícito en cada fila — el Excel
      indica que estos % están calibrados solo para ruta Minorista; NO deben
      afectar Mayorista (936 clientes activos hoy) ni Institucional.
   3. Modo='Ideal' — solo referencia, no fuerza el presupuesto todavía. El
      usuario aún no ha aprobado estas metas como definitivas ("META es la
      columna a decidir con Rudy", nota del propio Excel). Cuando las
      apruebe, basta un UPDATE a Modo='Obligatorio' fila por fila (o desde
      la pantalla ⚙️) — no hace falta volver a correr este script.

   Todos los Cat1ID fueron verificados contra Productos.Cat3ID/Cat1ID reales
   en producción (no solo coincidencia de nombre en Categoria1), para evitar
   mapear a una categoría homónima equivocada (ej. "Agua" tiene 3 candidatos
   por nombre pero solo 1 realmente asociado a Agua Crystal; "Dimar" tiene 2
   candidatos pero solo 1 con productos reales bajo ese Cat3ID).

   Ejecutar en MovilBusiness5STD. Tabla ya existe (v2/v3 corridas). Sin
   filas previas en PresupuestoActivacionMetas_RM (verificado: 0 antes de
   este script), así que es INSERT puro, no hay riesgo de duplicados.
   ═══════════════════════════════════════════════════════════════════════ */

INSERT INTO PresupuestoActivacionMetas_RM
  (Cat3ID, Cat1ID, MetaPct, Modo, BloqueaPublicar, Activo, CanID, UsuInicioSesion)
VALUES
  -- Continental de Negocios (101)
  (101, 105, 0.60, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- NOSOTRAS
  (101, 107, 0.18, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- FAMILIA (higiénicos/cocina)
  (101, 136, 0.16, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- NOSOTRAS DIARIOS (protectores)
  (101, 135, 0.14, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- CUIDADO DE BEBE (húmedos)
  (101, 115, 0.01, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- INCONTINENCIA
  (101, 124, 0.01, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- Nosotras Liquidos

  -- MOLINOS MODERNOS, SA (978)
  (978, 281, 0.70, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- PASTAS
  (978, 282, 0.72, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- GALLETAS
  (978, 280, 0.28, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- HARINAS FAMILIARES
  (978, 279, 0.10, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- MASCOTAS
  (978, 284, 0.12, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- SOPAS
  (978, 283, 0.03, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- SALSAS

  -- Brinsa Dominicana (201)
  (201, 113, 0.38, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- REFISAL (sal)
  (201, 114, 0.22, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- BLANCOX (cloro)

  -- COLOMBINA (954)
  (954, 207, 0.40, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- BOLONES
  (954, 208, 0.28, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- CARAMELOS DUROS
  (954, 209, 0.26, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- PALETAS
  (954, 285, 0.15, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- GRISSLY SNACK
  (954, 211, 0.10, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- CONFITERIA CICHLET (chicles)
  (954, 210, 0.08, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- CONFITERIA GOMAS
  (954, 203, 0.05, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- CHOCOLATERIA
  (954, 206, 0.02, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- CARAMELOS BLANDOS

  -- Comercial Fila (501)
  (501, 138, 0.15, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- INSECTICIDAS
  (501, 278, 0.02, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- AMBIENTADORES

  -- Agua Crystal (971)
  (971, 167, 0.15, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- AGUA

  -- DIMAR DOMINICANA (609)
  (609, 117, 0.18, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- DIMAR
  (609, 244, 0.05, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- PAÑALES DESECHABLES

  -- AMPOULE (974)
  (974, 277, 0.10, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- ILUMINACION

  -- Corporacion Poiroto SRL (109)
  (109, 193, 0.10, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- Snack

  -- P.Q.P. (702)
  (702, 190, 0.10, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09'),  -- ULTREX

  -- GALISA GROUP (977)
  (977, 132, 0.02, 'Ideal', 0, 1, 2000, 'carga-umbrales-2026-09');  -- CIGARROS
GO

-- Verificación rápida post-carga:
-- SELECT COUNT(*) FROM PresupuestoActivacionMetas_RM;  -- debe dar 31
