/* ================================================================== *
 *  CONFIGURACION DE LA APP (clave/valor)  -  tabla _RM
 * ================================================================== *
 *  Config editable por la Gerencia desde la app (no requiere editar
 *  archivos ni redeploy). Hoy guarda el APODO del termino "IEGT"
 *  (ej. AMCO-GT); por defecto vacio = se muestra "IEGT".
 *
 *  El usuario 'Ventas' (solo lectura en el resto de la BD) recibe
 *  permisos DML solo sobre esta tabla _RM. Ejecutar una sola vez.
 * ================================================================== */
IF OBJECT_ID('dbo.ConfiguracionApp_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.ConfiguracionApp_RM
    (
        Clave  varchar(60)   NOT NULL CONSTRAINT PK_ConfigApp_RM PRIMARY KEY,
        Valor  nvarchar(400) NULL,
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_ConfigApp_Fec DEFAULT(GETDATE())
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ConfiguracionApp_RM TO [Ventas];
GO
/* Apodo del IEGT por defecto VACIO (cualquier instalacion). Para AM Comercial, la
   Gerencia lo pone en 'AMCO-GT' desde Supervision > Configuracion (o aqui):        */
-- MERGE/seed opcional (dejar vacio por defecto):
IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionApp_RM WHERE Clave='IegtApodo')
    INSERT INTO dbo.ConfiguracionApp_RM (Clave, Valor) VALUES ('IegtApodo', '');
GO
