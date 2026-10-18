/* ============================================================
   Vista: dbo.VWVENTASRESUMENMESVENDEDORCLIENTE
   Origen: dbo.Clientes + dbo.VWVENTASRESUMEN2025
   Grano: Cliente x Año x Mes x Suplidor x Categoria
   Nota: Ventas/CajasVendidas se agregan "no importa quien vendió"
         porque el join es por Cliente (ClienteCodigo = CliID),
         sin filtrar por el vendedor que registró la transacción.
   DiaVisitaS1/DiaVisitaS2 y Centro provienen de VWVENTASRESUMEN2025
         (semana1, semana2, Centro), no de la tabla Clientes.
   ============================================================ */

CREATE OR ALTER VIEW dbo.VWVENTASRESUMENMESVENDEDORCLIENTE
AS
SELECT
    S.RepNombre           AS SupervisorVentas,      -- supervisor del RepCodigo del cliente
    C.RepCodigo           AS RepCodigo,              -- vendedor/ruta asignado al cliente
    R.RepNombre           AS RepNombre,
    C.CliCodigo           AS ClienteCodigo,
    C.CliNombre           AS ClienteNombre,
    CL.ClaDescripcion     AS TamañoCliente,
    V.semana1              AS DiaVisitaS1,
    V.semana2              AS DiaVisitaS2,
    V.Centro                AS Centro,
    V.Año                 AS Año,
    V.Mes                  AS Mes,
    V.SUPLIDOR              AS Suplidor,
    V.Categoria             AS Categoria,
    V.CodigoCategoria       AS CodigoCategoria,
    SUM(V.ValorBruto)      AS Ventas,
    SUM(V.CajasVendidas)   AS CajasVendidas
FROM dbo.Clientes C
INNER JOIN dbo.VWVENTASRESUMEN2025 V
    ON V.ClienteCodigo = C.CliID
LEFT JOIN dbo.Representantes R
    ON R.RepCodigo = C.RepCodigo
LEFT JOIN dbo.Representantes S
    ON S.RepCodigo = R.RepSupervisor
LEFT JOIN dbo.Clasifica CL
    ON CL.ClaID = C.ClaID
WHERE V.VenFechaSinHora >= '20250101'
GROUP BY
    S.RepNombre,
    C.RepCodigo,
    R.RepNombre,
    C.CliCodigo,
    C.CliNombre,
    CL.ClaDescripcion,
    V.semana1,
    V.semana2,
    V.Centro,
    V.Año,
    V.Mes,
    V.SUPLIDOR,
    V.Categoria,
    V.CodigoCategoria;
GO

GRANT SELECT ON dbo.VWVENTASRESUMENMESVENDEDORCLIENTE TO Ventas;
GO
