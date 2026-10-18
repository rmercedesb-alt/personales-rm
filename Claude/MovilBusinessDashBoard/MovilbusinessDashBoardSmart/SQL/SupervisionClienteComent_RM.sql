/* ================================================================== *
 *  SUPERVISIÓN DINÁMICA — Comentario por cliente (tabla _RM)
 * ================================================================== *
 *  Guarda el comentario que el supervisor escribe para CADA cliente
 *  dentro de una evaluación de calle (para reimprimir el reporte con
 *  el detalle por cliente). El comentario GENERAL de la evaluación
 *  sigue en SupervisionEvaluaciones_RM.Comentario/Compromiso.
 *
 *  El usuario 'Ventas' (solo lectura en el resto de la BD) recibe
 *  permisos DML solo sobre esta tabla _RM. Ejecutar una sola vez.
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionClienteComent_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionClienteComent_RM
    (
        EvaID      int          NOT NULL,   -- -> SupervisionEvaluaciones_RM.EvaID
        CliID      int          NOT NULL,   -- -> cliente evaluado
        Comentario nvarchar(1000) NULL,
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupCliCom_Fec DEFAULT(GETDATE()),
        CONSTRAINT PK_SupCliCom_RM PRIMARY KEY (EvaID, CliID)
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionClienteComent_RM TO [Ventas];
GO
