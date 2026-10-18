/* ============================================================
   Vista: dbo.VWCLIENTESRUTAVISITAS
   Dimensión de Clientes para Power BI (asignación VIGENTE/actual
   de vendedor y supervisor, no histórica).
   Grano: 1 fila por cliente (CliID es único).
   ============================================================ */

CREATE OR ALTER VIEW dbo.VWCLIENTESRUTAVISITAS
AS
SELECT
    C.CliID            AS IDCLIENTE,
    C.CliCodigo        AS CodigoCliente,
    C.CliNombre        AS NombreCliente,
    C.RepCodigo        AS Vendedor,
    R.RepNombre        AS NombreVendedor,      -- agregado: sin esto "Vendedor" es solo un código
    C.ClaID            AS ClaID,
    CL.ClaDescripcion  AS TamañoCliente,
    S.RepNombre        AS NombreSupervisor
FROM dbo.Clientes C
LEFT JOIN dbo.Representantes R
    ON R.RepCodigo = C.RepCodigo
LEFT JOIN dbo.Representantes S
    ON S.RepCodigo = R.RepSupervisor
LEFT JOIN dbo.Clasifica CL
    ON CL.ClaID = C.ClaID;
GO

GRANT SELECT ON dbo.VWCLIENTESRUTAVISITAS TO Ventas;
GO
