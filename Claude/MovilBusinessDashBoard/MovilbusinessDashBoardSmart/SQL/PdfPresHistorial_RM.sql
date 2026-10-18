-- ============================================================================
-- Tabla PdfPresHistorial_RM — historial/auditoría de los informes generados
-- desde "PDF Presentaciones" (Supervisión Dinámica → Informes Gerenciales).
-- Pedido explícito del usuario 2026-08-30: "quiero que estos informes lo
-- guardemos al generarse con fechas de generación".
--
-- Se guardan los FILTROS y el RESULTADO ya calculado, en JSON — NO el archivo
-- PDF en sí (decisión del usuario 2026-08-30: más liviano en BD; el PDF se
-- vuelve a armar en el navegador, al vuelo, a partir de estos mismos datos
-- cuando alguien reabre un registro del historial — determinístico, no hace
-- falta recalcular contra VWVentas/PresupuestosHistoricos de nuevo).
--
-- La aplicación se degrada solo si esta tabla no existe o "Ventas" no tiene
-- permiso de escritura aquí (el informe se sigue pudiendo generar y descargar
-- igual, solo que "Guardar en historial" falla con un aviso) — así que este
-- script se puede correr en cualquier momento, sin coordinar un despliegue.
-- ============================================================================
USE [MovilBusiness5STD];
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PdfPresHistorial_RM')
BEGIN
    CREATE TABLE dbo.PdfPresHistorial_RM (
        PphID               INT IDENTITY(1,1) PRIMARY KEY,
        PphReporte          VARCHAR(60)   NOT NULL,  -- ej. 'gobierno_comercial_ventas_proyectadas'
        PphUsuarioLogin     VARCHAR(50)   NOT NULL,
        PphUsuarioNombre    VARCHAR(150)  NULL,
        PphFechaGeneracion  DATETIME      NOT NULL CONSTRAINT DF_PdfPresHistorial_RM_Fecha DEFAULT GETDATE(),
        PphFiltrosJSON      NVARCHAR(MAX) NOT NULL,
        PphResultadoJSON    NVARCHAR(MAX) NOT NULL
    );
    CREATE INDEX IX_PdfPresHistorial_RM_Reporte ON dbo.PdfPresHistorial_RM (PphReporte, PphFechaGeneracion DESC);
END
GO

GRANT SELECT, INSERT ON dbo.PdfPresHistorial_RM TO [Ventas];
GO
