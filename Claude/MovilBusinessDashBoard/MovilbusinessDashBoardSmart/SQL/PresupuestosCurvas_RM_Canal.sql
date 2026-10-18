-- ============================================================================
-- Agrega el eje Canal a PresupuestosCurvas_RM (patrón de 4 franjas del "Estimado" de
-- Presupuestos por Canal, además de Suplidor/Categoría — pedido explícito del usuario
-- 2026-09-05: "las estimaciones tienen que venir de un patrón de ventas que se realce
-- en el canal, suplidor y categoría").
--
-- CurCanID=0 = "todos los canales" — el ÚNICO valor que existía antes de esta migración,
-- así que las filas ya guardadas siguen siendo válidas y se interpretan exactamente igual
-- (el DEFAULT 0 se las asigna automáticamente al agregar la columna). Con un canal
-- específico filtrado (ej. Mayorista=3000), la app calcula y guarda una fila NUEVA con
-- CurCanID=3000 — no reemplaza ni afecta la fila CurCanID=0 ya existente para ese mismo
-- (CurTipo,CurClaveRef,CurAnio,CurMes).
--
-- La aplicación se degrada sola si este script no se corre todavía (sigue leyendo/
-- escribiendo con CurCanID=0 vía el parámetro por default, hasta que la columna exista;
-- ver LeerCurvaDeTabla/GuardarCurvaEnTabla en Services/PresupuestosCore.cs, que atrapan
-- cualquier error de columna faltante y devuelven "no persistido" en vez de romper).
-- ============================================================================
USE [MovilBusiness5STD];
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PresupuestosCurvas_RM') AND name = 'CurCanID')
BEGIN
    ALTER TABLE dbo.PresupuestosCurvas_RM ADD CurCanID INT NOT NULL CONSTRAINT DF_PresupuestosCurvas_RM_CanID DEFAULT 0;

    ALTER TABLE dbo.PresupuestosCurvas_RM DROP CONSTRAINT PK_PresupuestosCurvas_RM;
    ALTER TABLE dbo.PresupuestosCurvas_RM ADD CONSTRAINT PK_PresupuestosCurvas_RM
        PRIMARY KEY (CurTipo, CurClaveRef, CurCanID, CurAnio, CurMes);
END
GO
