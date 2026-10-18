/* ============================================================
   Vista: VW_OfertasClientesMesActual
   Analisis de ofertas vigentes y clientes alcanzados por
   supervisor, vendedor, numero de oferta y descripcion.

   Fuente de ofertas : tabla Ofertas (OfeFechaFin > '20260601')
   Fuente de pedidos : VWpedidosResumenMesactual (IndicadorOferta = 1)
   Granularidad      : una fila por Supervisor + Vendedor + Oferta
   Metrica principal : ClientesDistintos (clientes que tomaron la oferta)
   ============================================================ */
CREATE OR ALTER VIEW dbo.VW_OfertasClientesMesActual
AS
SELECT
    v.CodigoSupervisor,
    v.Supervisorpreventas              AS Supervisor,
    v.Codigopreventas                  AS CodigoVendedor,
    v.NombrePreventa                   AS Vendedor,
    o.OfeID                            AS NumeroOferta,
    o.OfeDescripcion,
    o.OfeFechainicio,
    o.OfeFechaFin,
    o.OfeTipo,
    COUNT(DISTINCT v.ClienteCodigo)    AS ClientesDistintos,
    COUNT(DISTINCT v.PedSecuencia)     AS Pedidos,
    SUM(v.CajasPedido)                 AS CajasPedido,
    SUM(v.ValorBrutoPedido)            AS ValorBrutoPedido
FROM dbo.Ofertas o
JOIN dbo.VWpedidosResumenMesactual v
       ON v.OfeID = o.OfeID
WHERE o.OfeFechaFin >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)  -- ofertas vigentes en el mes en curso
  AND v.PedFecha   >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)  -- pedidos del mes en curso
  AND v.PedFecha    < DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
  AND v.IndicadorOferta = 1           -- solo lineas de pedido con oferta aplicada
GROUP BY
    v.CodigoSupervisor, v.Supervisorpreventas,
    v.Codigopreventas,  v.NombrePreventa,
    o.OfeID, o.OfeDescripcion, o.OfeFechainicio, o.OfeFechaFin, o.OfeTipo;
GO

/* ---- Ejemplos de uso ----

-- 1) Detalle completo (lo que alimenta el PPT)
SELECT * FROM dbo.VW_OfertasClientesMesActual
ORDER BY ClientesDistintos DESC;

-- 2) Resumen por oferta
SELECT NumeroOferta, OfeDescripcion,
       SUM(ClientesDistintos) AS Clientes,
       SUM(Pedidos)           AS Pedidos
FROM dbo.VW_OfertasClientesMesActual
GROUP BY NumeroOferta, OfeDescripcion
ORDER BY Clientes DESC;

-- 3) Resumen por supervisor
SELECT CodigoSupervisor, Supervisor,
       SUM(ClientesDistintos) AS Clientes
FROM dbo.VW_OfertasClientesMesActual
GROUP BY CodigoSupervisor, Supervisor
ORDER BY Clientes DESC;

-- 4) Resumen por vendedor
SELECT CodigoVendedor, Vendedor,
       SUM(ClientesDistintos) AS Clientes
FROM dbo.VW_OfertasClientesMesActual
GROUP BY CodigoVendedor, Vendedor
ORDER BY Clientes DESC;
*/
