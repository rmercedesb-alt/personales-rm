-- ============================================================================
-- Tabla PresupuestosCurvas_RM — persiste las curvas de estacionalidad usadas para
-- calcular "Estimado"/"Est.%" en el módulo Presupuestos, en vez de recalcularlas
-- contra VWVentas/VWVentasDetalle (~17s por curva, sin datos aquí) cada vez que la
-- caché en memoria expira o el proceso se reinicia (reciclado de IIS). Pedido
-- explícito del usuario 2026-08-29.
--
-- Por qué es seguro guardarla una sola vez, sin vencimiento: cada curva es un
-- promedio de los 6 MESES ANTERIORES al (Año,Mes) de referencia — todos meses YA
-- CERRADOS para cuando alguien la pide. Una vez calculada, es estable (no cambia
-- salvo que alguien corrija datos históricos muy atrás en el tiempo).
--
-- La aplicación se degrada solo si esta tabla no existe o "Ventas" no tiene permiso
-- de escritura aquí (sigue funcionando exactamente igual que antes, recalculando
-- contra VWVentas cada vez) — así que este script se puede correr en cualquier
-- momento, sin coordinar un despliegue.
-- ============================================================================
USE [MovilBusiness5STD];
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PresupuestosCurvas_RM')
BEGIN
    CREATE TABLE dbo.PresupuestosCurvas_RM (
        CurTipo         VARCHAR(20)   NOT NULL,  -- 'general' | 'cat1' (Categoría) | 'cat3' (Suplidor)
        CurClaveRef     VARCHAR(20)   NOT NULL,  -- Cat1ID/Cat3ID como texto, o 'GENERAL' si CurTipo='general'
        CurAnio         INT           NOT NULL,
        CurMes          INT           NOT NULL,
        CurValorJSON    NVARCHAR(MAX) NOT NULL,  -- array de 32 posiciones (día 1..31), serializado como JSON
        CurFechaCalculo DATETIME      NOT NULL CONSTRAINT DF_PresupuestosCurvas_RM_Fecha DEFAULT GETDATE(),
        CONSTRAINT PK_PresupuestosCurvas_RM PRIMARY KEY (CurTipo, CurClaveRef, CurAnio, CurMes)
    );
END
GO

-- Ventas necesita SELECT (ya lo tiene) + INSERT/UPDATE aquí para poder guardar las
-- curvas que calcule — la app degrada sola si este GRANT no se aplica (ver arriba).
GRANT SELECT, INSERT, UPDATE ON dbo.PresupuestosCurvas_RM TO [Ventas];
GO
