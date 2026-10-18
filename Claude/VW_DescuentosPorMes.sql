/* ================================================================
   Vista: VW_DescuentosPorMes
   (analisis de descuentos, en paralelo a VW_OfertasClientesPorMes)

   Analiza CUALQUIER mes desde 2026 en adelante. Expone Anio y Mes
   para filtrar, p.ej.  WHERE Anio = 2026 AND Mes = 3

   GRANO MENSUAL: una fila por
        Anio + Mes + Supervisor + Vendedor + Centro + Canal +
        Suplidor + Categoria + DesID (descuento).

   CADENA DE RELACION:
     PedidosDescuentos  -> detalle REAL del descuento aplicado por
                           pedido/linea (monto en PedDescuento).
     VWpedidosResumenMesactual -> dimensiones:
                           * por PEDIDO (PedSecuencia): Supervisor,
                             Vendedor, Cliente, Fecha, Centro, Canal.
                           * por PRODUCTO (ProductoCodigo): Suplidor,
                             Categoria.
                           Se toman deduplicadas en CTEs para no inflar
                           el monto con las lineas repetidas del VW.
     DescuentosRecargos -> catalogo del descuento (DesDescripcion).
                           LEFT JOIN: hay DesID aplicados que no estan
                           catalogados (descuentos automaticos); se
                           etiquetan 'Sin descripcion / automatico'.

   MONTO: PedDescuento (el campo DesValorDescuento esta 100% NULL).
   Filtro de anio: >= 2026 (en lugar del anio en curso).
   No se filtra por vigencia: se reporta el descuento tal como quedo
   aplicado en el pedido.
   ================================================================ */
CREATE OR ALTER VIEW dbo.VW_DescuentosPorMes
AS
WITH Ped AS (
    /* Una fila por PEDIDO. Centro/Canal son constantes por pedido. */
    SELECT
        v.PedSecuencia,
        MIN(v.PedFecha)            AS PedFecha,
        MAX(v.CodigoSupervisor)    AS CodigoSupervisor,
        MAX(v.Supervisorpreventas) AS Supervisor,
        MAX(v.Codigopreventas)     AS CodigoVendedor,
        MAX(v.NombrePreventa)      AS Vendedor,
        MAX(v.ClienteCodigo)       AS ClienteCodigo,
        MAX(v.Centro)              AS Centro,
        MAX(v.Canal)               AS Canal
    FROM dbo.VWpedidosResumenMesactual v
    WHERE YEAR(v.PedFecha) >= 2026
    GROUP BY v.PedSecuencia
),
ProdDim AS (
    /* Una fila por PRODUCTO. Suplidor/Categoria son constantes por producto. */
    SELECT
        ProductoCodigo,
        MAX(CodigoSuplidor)  AS CodigoSuplidor,
        MAX(Suplidor)        AS Suplidor,
        MAX(CodigoCategoria) AS CodigoCategoria,
        MAX(Categoria)       AS Categoria
    FROM dbo.VWpedidosResumenMesactual
    WHERE YEAR(PedFecha) >= 2026
    GROUP BY ProductoCodigo
)
SELECT
    YEAR(p.PedFecha)   AS Anio,
    MONTH(p.PedFecha)  AS Mes,
    p.CodigoSupervisor,
    p.Supervisor,
    p.CodigoVendedor,
    p.Vendedor,
    p.Centro,
    p.Canal,
    pr.CodigoSuplidor,
    pr.Suplidor,
    pr.CodigoCategoria,
    pr.Categoria,
    pd.DesID,
    COALESCE(dr.DesDescripcion, 'Sin descripcion / automatico') AS DesDescripcion,
    COUNT(DISTINCT p.PedSecuencia)  AS Pedidos,
    COUNT(DISTINCT p.ClienteCodigo) AS ClientesDistintos,
    SUM(pd.PedDescuento)            AS TotalDescuento
FROM Ped p
JOIN dbo.PedidosDescuentos pd
       ON pd.PedSecuencia = p.PedSecuencia
LEFT JOIN ProdDim pr
       ON pr.ProductoCodigo = pd.ProID
LEFT JOIN dbo.DescuentosRecargos dr
       ON dr.DesID = pd.DesID
GROUP BY
    YEAR(p.PedFecha), MONTH(p.PedFecha),
    p.CodigoSupervisor, p.Supervisor,
    p.CodigoVendedor,   p.Vendedor,
    p.Centro, p.Canal,
    pr.CodigoSuplidor, pr.Suplidor, pr.CodigoCategoria, pr.Categoria,
    pd.DesID, COALESCE(dr.DesDescripcion, 'Sin descripcion / automatico');
GO

/* ---------------- Ejemplos de uso ----------------

-- 1) Detalle del mes
SELECT * FROM dbo.VW_DescuentosPorMes
WHERE Anio = 2026 AND Mes = 6
ORDER BY TotalDescuento DESC;

-- 2) Descuentos por DesID (consolidado)  -> "por DesID"
SELECT DesID, DesDescripcion,
       SUM(Pedidos)        AS Pedidos,
       SUM(TotalDescuento) AS TotalDescuento
FROM dbo.VW_DescuentosPorMes
WHERE Anio = 2026 AND Mes = 6
GROUP BY DesID, DesDescripcion
ORDER BY TotalDescuento DESC;

-- 3) Descuentos por Suplidor / Categoria / Centro / Canal
SELECT Suplidor, Categoria, Centro, Canal,
       SUM(TotalDescuento) AS TotalDescuento
FROM dbo.VW_DescuentosPorMes
WHERE Anio = 2026 AND Mes = 6
GROUP BY Suplidor, Categoria, Centro, Canal
ORDER BY TotalDescuento DESC;

-- 4) Descuentos por vendedor
SELECT CodigoVendedor, Vendedor,
       SUM(TotalDescuento) AS TotalDescuento,
       SUM(Pedidos)        AS Pedidos
FROM dbo.VW_DescuentosPorMes
WHERE Anio = 2026 AND Mes = 6
GROUP BY CodigoVendedor, Vendedor
ORDER BY TotalDescuento DESC;
*/
