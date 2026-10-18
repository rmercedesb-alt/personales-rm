-- ============================================================================
-- Otorga INSERT/UPDATE/DELETE al usuario de BD "Ventas" sobre 2 tablas puntuales:
--   dbo.PresupuestosHistoricos
--   dbo.RutaVisitas
-- Pedido explícito del usuario (2026-08-29). Solo agrega estos 3 permisos — SELECT
-- ya lo tiene (Ventas es de solo lectura hoy) y no se toca aquí.
--
-- ADVERTENCIA — leer antes de correr: el login "Ventas" es COMPARTIDO por 2
-- aplicaciones distintas (misma cadena de conexión en ambas, verificado en vivo):
--   - MovilbusinessDashBoardSmart (este dashboard, .NET)
--   - RutaVisitasSmart (Node/Express + React, carpeta hermana en este mismo repo)
-- RutaVisitasSmart tiene documentada en su propio CLAUDE.md la regla "la app NUNCA
-- escribe directo a la BD de producción — cualquier cambio de ruta se entrega como
-- Excel con script SQL para que TI lo aplique". Esa regla hoy es una garantía SOLO de
-- convención de código, reforzada porque Ventas no podía escribir a nivel de BD. Al
-- correr este script, esa 2ª capa de protección desaparece para TODA la BD que Ventas
-- toque (no solo para estas 2 tablas si en el futuro se agregan más). El usuario
-- confirmó que quiere proceder así, sobre este mismo login compartido (no uno nuevo).
--
-- Ejecutar conectado a la base de datos correcta (ajustar el nombre si el servidor de
-- destino usa otro): la app de Presupuestos usa "MovilBusiness5STD" según
-- appsettings.json / RutaVisitasSmart/server/.env.
-- ============================================================================
USE [MovilBusiness5STD];
GO

GRANT INSERT, UPDATE, DELETE ON dbo.PresupuestosHistoricos TO [Ventas];
GO

GRANT INSERT, UPDATE, DELETE ON dbo.RutaVisitas TO [Ventas];
GO

-- Verificación: lista los permisos efectivos de Ventas sobre ambas tablas después de
-- correr el script — debe mostrar SELECT (ya existente) + INSERT/UPDATE/DELETE (nuevos).
SELECT
    dp.name       AS Usuario,
    obj.name      AS Tabla,
    perm.permission_name AS Permiso,
    perm.state_desc       AS Estado
FROM sys.database_permissions perm
JOIN sys.database_principals dp ON dp.principal_id = perm.grantee_principal_id
JOIN sys.objects obj ON obj.object_id = perm.major_id
WHERE dp.name = 'Ventas'
  AND obj.name IN ('PresupuestosHistoricos', 'RutaVisitas')
ORDER BY obj.name, perm.permission_name;
GO
