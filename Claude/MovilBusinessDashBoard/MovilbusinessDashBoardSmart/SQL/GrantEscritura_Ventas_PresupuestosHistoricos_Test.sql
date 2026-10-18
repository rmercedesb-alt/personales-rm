-- ============================================================================
-- Otorga INSERT/UPDATE/DELETE al usuario de BD "Ventas" sobre:
--   MovilBusiness5STD_Test.dbo.PresupuestosHistoricos
--
-- Contexto: el nuevo módulo "Presupuestos Smart" (creación/edición/eliminación
-- de presupuestos desde cero, por producto/unidades/activaciones/facturas) va a
-- construir y probar todo contra la base MovilBusiness5STD_Test, NO contra
-- producción (MovilBusiness5STD) — así que necesita su propio permiso de
-- escritura, independiente del ya otorgado en producción (ver
-- GrantEscritura_Ventas_PresupuestosHistoricos_RutaVisitas.sql, que solo cubre
-- MovilBusiness5STD).
--
-- Verificado en vivo (2026-08-29): 'Ventas' hoy solo tiene rol db_datareader en
-- MovilBusiness5STD_Test (SELECT funciona, INSERT fue rechazado por el server
-- con "The INSERT permission was denied on the object 'PresupuestosHistoricos',
-- database 'MovilBusiness5STD_Test'"). Este script corrige eso.
--
-- Mismo login compartido "Ventas" ya usado por MovilbusinessDashBoardSmart y
-- RutaVisitasSmart — no crea un usuario nuevo.
-- ============================================================================
USE [MovilBusiness5STD_Test];
GO

GRANT INSERT, UPDATE, DELETE ON dbo.PresupuestosHistoricos TO [Ventas];
GO

-- Verificación: debe mostrar SELECT (ya existente vía db_datareader, no aparece
-- aquí porque es de rol, no GRANT directo) + INSERT/UPDATE/DELETE (nuevos, GRANT directo).
SELECT
    dp.name       AS Usuario,
    obj.name      AS Tabla,
    perm.permission_name AS Permiso,
    perm.state_desc       AS Estado
FROM sys.database_permissions perm
JOIN sys.database_principals dp ON dp.principal_id = perm.grantee_principal_id
JOIN sys.objects obj ON obj.object_id = perm.major_id
WHERE dp.name = 'Ventas'
  AND obj.name = 'PresupuestosHistoricos'
ORDER BY perm.permission_name;
GO
