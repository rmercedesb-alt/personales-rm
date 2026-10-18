/* ================================================================
   Vista: VW_OfertasClientesPorMes
   (mejora de VW_OfertasClientesMesActual)

   Analiza CUALQUIER mes del ANIO ACTUAL. Expone Anio y Mes para
   filtrar el mes, p.ej.  WHERE Anio = YEAR(GETDATE()) AND Mes = 3

   GRANO MENSUAL: una fila por
        Anio + Mes + Supervisor + Vendedor + Centro + Canal +
        Suplidor + Categoria + Oferta.
   ClientesDistintos = clientes UNICOS del mes.

   *** MUESTRA TAMBIEN LAS OFERTAS SIN VENTA ***
   LEFT JOIN desde las ofertas hacia los pedidos: si una oferta del anio
   no tuvo ninguna venta, igual aparece para saber que no impacto en
   nada. Filtrar las no vendidas con:
        WHERE ClientesDistintos = 0 AND ValorBrutoPedido = 0

   *** SUPLIDOR / CATEGORIA: UNO ESPECIFICO POR OFERTA ***
   Cada oferta pertenece a un suplidor y categoria especificos. Se
   determinan a partir del PRODUCTO de la oferta y se buscan en las
   ventas (VWpedidosResumenMesactual), de modo que esten presentes
   AUNQUE LA OFERTA NO TENGA VENTAS:
     1. Si la oferta trae ProID -> se usa ese producto.
     2. Si la oferta NO trae ProID -> se usa su GrpCodigo, se busca en
        GrupoProductosDetalle y se toma el PRIMER ProID del grupo.
     3. Con ese ProID representativo se obtienen Suplidor y Categoria
        (y sus codigos) desde la tabla de ventas.
   (Si el producto nunca se vendio, no estara en ventas y el suplidor/
    categoria quedaran NULL.)

   Centro, Canal, Supervisor y Vendedor salen del pedido (NULL cuando la
   oferta no tuvo ventas, porque no hay venta a la cual atribuirlos).

   ValorBrutoPedido = suma de lineas con IndicadorOferta = 0 (producto
   VENDIDO). Las lineas con IndicadorOferta = 1 son el producto REGALADO
   (valor 0) y solo cuentan clientes/pedidos.

   Fuente de OFERTAS: vista vwOfertas (activas + historicas), anio actual.
   ================================================================ */
CREATE OR ALTER VIEW dbo.VW_OfertasClientesPorMes
AS
WITH OfeBase AS (
    /* Una fila por OfeID con su ProID directo y/o su grupo. */
    SELECT
        OfeID,
        MAX(OfeDescripcion)               AS OfeDescripcion,
        MIN(OfeFechainicio)               AS OfeFechainicio,
        MAX(OfeFechaFin)                  AS OfeFechaFin,
        MIN(NULLIF(ProID, 0))             AS ProIDDirecto,
        MIN(NULLIF(NULLIF(GrpCodigo,''),'0')) AS GrpCodigo
    FROM dbo.vwOfertas
    WHERE YEAR(OfeFechainicio) = YEAR(GETDATE())
    GROUP BY OfeID
),
OfeProd AS (
    /* Resuelve el ProID representativo: directo o el primero del grupo. */
    SELECT
        b.OfeID, b.OfeDescripcion, b.OfeFechainicio, b.OfeFechaFin,
        COALESCE(
            b.ProIDDirecto,
            (SELECT MIN(g.ProID) FROM dbo.GrupoProductosDetalle g WHERE g.GrpCodigo = b.GrpCodigo)
        ) AS ProIDRep
    FROM OfeBase b
),
ProdDim AS (
    /* Atributos del producto (suplidor/categoria) tomados de las ventas. */
    SELECT
        ProductoCodigo,
        MAX(CodigoSuplidor)  AS CodigoSuplidor,
        MAX(Suplidor)        AS Suplidor,
        MAX(CodigoCategoria) AS CodigoCategoria,
        MAX(Categoria)       AS Categoria
    FROM dbo.VWpedidosResumenMesactual
    GROUP BY ProductoCodigo
)
SELECT
    /* Para ofertas vendidas el mes sale del pedido; para las NO vendidas
       sale de la fecha de inicio de la oferta. */
    COALESCE(YEAR(v.PedFecha),  YEAR(op.OfeFechainicio))  AS Anio,
    COALESCE(MONTH(v.PedFecha), MONTH(op.OfeFechainicio)) AS Mes,
    v.CodigoSupervisor,
    v.Supervisorpreventas   AS Supervisor,
    v.Codigopreventas       AS CodigoVendedor,
    v.NombrePreventa        AS Vendedor,
    v.Centro,
    v.Canal,
    /* Suplidor/Categoria especificos de la oferta (desde su producto) */
    pd.CodigoSuplidor,
    pd.Suplidor,
    pd.CodigoCategoria,
    pd.Categoria,
    op.OfeID                AS NumeroOferta,
    op.OfeDescripcion,
    op.OfeFechainicio,
    op.OfeFechaFin,
    op.ProIDRep             AS ProductoOferta,
    /* Clientes y pedidos que TOMARON la oferta (linea del regalo, ind = 1) */
    COUNT(DISTINCT CASE WHEN v.IndicadorOferta = 1 THEN v.ClienteCodigo END) AS ClientesDistintos,
    COUNT(DISTINCT CASE WHEN v.IndicadorOferta = 1 THEN v.PedSecuencia  END) AS Pedidos,
    /* Volumen y valor REALMENTE VENDIDO (lineas con ind = 0) */
    ISNULL(SUM(CASE WHEN v.IndicadorOferta = 0 THEN v.CajasPedido      END), 0) AS CajasVendidas,
    ISNULL(SUM(CASE WHEN v.IndicadorOferta = 0 THEN v.ValorBrutoPedido END), 0) AS ValorBrutoPedido
FROM OfeProd op
LEFT JOIN ProdDim pd
       ON pd.ProductoCodigo = op.ProIDRep
LEFT JOIN dbo.VWpedidosResumenMesactual v
       ON  v.OfeID = op.OfeID
       AND CAST(v.PedFecha AS DATE)
             BETWEEN CAST(op.OfeFechainicio AS DATE) AND CAST(op.OfeFechaFin AS DATE)
       AND YEAR(v.PedFecha) = YEAR(GETDATE())
GROUP BY
    COALESCE(YEAR(v.PedFecha),  YEAR(op.OfeFechainicio)),
    COALESCE(MONTH(v.PedFecha), MONTH(op.OfeFechainicio)),
    v.CodigoSupervisor, v.Supervisorpreventas,
    v.Codigopreventas,  v.NombrePreventa,
    v.Centro, v.Canal,
    pd.CodigoSuplidor, pd.Suplidor, pd.CodigoCategoria, pd.Categoria,
    op.OfeID, op.OfeDescripcion, op.OfeFechainicio, op.OfeFechaFin, op.ProIDRep;
GO

/* ---------------- Ejemplos de uso ----------------

-- 1) Ofertas SIN impacto en el mes (no vendieron nada) con su suplidor/categoria
SELECT NumeroOferta, OfeDescripcion, Suplidor, Categoria, OfeFechainicio, OfeFechaFin
FROM dbo.VW_OfertasClientesPorMes
WHERE Mes = 6 AND ClientesDistintos = 0 AND ValorBrutoPedido = 0;

-- 2) Detalle del mes
SELECT * FROM dbo.VW_OfertasClientesPorMes
WHERE Mes = 6
ORDER BY ClientesDistintos DESC;

-- 3) Resumen del mes por oferta
SELECT NumeroOferta, OfeDescripcion, Suplidor, Categoria,
       SUM(ClientesDistintos) AS Clientes,
       SUM(ValorBrutoPedido)  AS ValorVendido
FROM dbo.VW_OfertasClientesPorMes
WHERE Mes = 6
GROUP BY NumeroOferta, OfeDescripcion, Suplidor, Categoria
ORDER BY ValorVendido DESC;

-- 4) Resumen por Suplidor / Categoria
SELECT Suplidor, Categoria,
       COUNT(DISTINCT NumeroOferta) AS Ofertas,
       SUM(ValorBrutoPedido)        AS ValorVendido
FROM dbo.VW_OfertasClientesPorMes
WHERE Mes = 6
GROUP BY Suplidor, Categoria
ORDER BY ValorVendido DESC;
*/
