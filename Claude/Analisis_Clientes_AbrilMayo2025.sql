/* ============================================================
   ANÁLISIS DE CLIENTES — ABRIL Y MAYO 2025
   Vista  : dbo.vwventasResumen2025
   Filtro : tipoventa = 1  |  Meses 04 y 05 del año 2025
   Objeto : Validar política de pedido mínimo $500.00

   COLUMNAS ASUMIDAS EN LA VISTA — ajustar si difieren:
     fecha, centrodistribucion, supervisor, ruta,
     nombrevendedor, codigocliente, nombrecliente,
     nofactura, valorventa, tipoventa

   Para verificar: SELECT TOP 0 * FROM dbo.vwventasResumen2025
   ============================================================ */


/* ============================================================
   PARTE 1 — DETALLE DIARIO
   ─────────────────────────────────────────────────────────────
   • Muestra día a día: Supervisor > Ruta > Vendedor > Cliente
   • Solo clientes cuya venta TOTAL en esa ruta supera $500
     (si el mismo cliente fue facturado por 2 vendedores en la
      misma ruta, se consolida su total antes de evaluar $500)
   • Incluye subtotal por Ruta, total del Día y Promedio diario
   ============================================================ */

WITH base AS (
    SELECT
        CONVERT(DATE, fecha)  AS dia,
        centrodistribucion,
        supervisor,
        ruta,
        nombrevendedor,
        codigocliente,
        nombrecliente,
        nofactura,
        valorventa
    FROM dbo.vwventasResumen2025
    WHERE YEAR(fecha)  = 2025
      AND MONTH(fecha) IN (4, 5)
      AND tipoventa    = 1
),

/*  Suma todo lo que facturó el cliente en esa ruta ese día.
    Si Vendedor A y Vendedor B le vendieron al mismo cliente,
    ambas facturas se suman en un solo registro aquí.         */
cliente_ruta_dia AS (
    SELECT
        dia,
        centrodistribucion,
        supervisor,
        ruta,
        codigocliente,
        MIN(nombrecliente)         AS nombrecliente,
        COUNT(DISTINCT nofactura)  AS facturas,
        SUM(valorventa)            AS total_cliente
    FROM base
    GROUP BY dia, centrodistribucion, supervisor, ruta, codigocliente
    HAVING SUM(valorventa) > 500                 -- política: > $500
),

/*  Detalle por vendedor para los clientes que calificaron.
    Muestra cuántas facturas hizo CADA VENDEDOR y su monto,
    más la columna "total_cliente" que es el consolidado de
    todos los vendedores de esa ruta en ese día.              */
detalle_vnd AS (
    SELECT
        b.dia,
        b.centrodistribucion,
        b.supervisor,
        b.ruta,
        b.nombrevendedor,
        b.codigocliente,
        c.nombrecliente,
        COUNT(DISTINCT b.nofactura)  AS facturas_vendedor,
        CAST(SUM(b.valorventa)       AS DECIMAL(18,2))  AS venta_vendedor,
        CAST(c.total_cliente         AS DECIMAL(18,2))  AS total_cliente_ruta
    FROM base b
    INNER JOIN cliente_ruta_dia c
        ON  b.dia           = c.dia
        AND b.ruta          = c.ruta
        AND b.codigocliente = c.codigocliente
    GROUP BY
        b.dia, b.centrodistribucion, b.supervisor, b.ruta,
        b.nombrevendedor, b.codigocliente,
        c.nombrecliente, c.total_cliente
),

/*  Subtotal por Ruta/Día
    Clientes únicos + todas las facturas + total sin duplicar */
sub_ruta AS (
    SELECT
        dia, centrodistribucion, supervisor, ruta,
        COUNT(codigocliente)   AS clientes,
        SUM(facturas)          AS facturas,
        CAST(SUM(total_cliente) AS DECIMAL(18,2))  AS valor_venta
    FROM cliente_ruta_dia
    GROUP BY dia, centrodistribucion, supervisor, ruta
),

/*  Total del Día — suma de todas las rutas ese día           */
sub_dia AS (
    SELECT
        dia,
        COUNT(DISTINCT codigocliente)  AS clientes,
        SUM(facturas)                  AS facturas,
        CAST(SUM(total_cliente)        AS DECIMAL(18,2))  AS valor_venta
    FROM cliente_ruta_dia
    GROUP BY dia
),

/*  Promedio diario del período completo (Abril + Mayo)       */
promedio AS (
    SELECT
        CAST(AVG(CAST(clientes AS FLOAT)) AS DECIMAL(10,1))  AS prom_clientes,
        CAST(AVG(CAST(facturas AS FLOAT)) AS DECIMAL(10,1))  AS prom_facturas,
        CAST(AVG(valor_venta)             AS DECIMAL(18,2))  AS prom_venta
    FROM sub_dia
)

SELECT
    ISNULL(CONVERT(VARCHAR(10), dia, 103), '')  AS [Día],
    ISNULL(centrodistribucion, '')               AS [C. Distribución],
    ISNULL(supervisor, '')                       AS [Supervisor],
    ISNULL(ruta, '')                             AS [Ruta],
    ISNULL(nombrevendedor, '')                   AS [Vendedor],
    ISNULL(CAST(codigocliente AS VARCHAR(20)),'')AS [Cód. Cliente],
    ISNULL(nombrecliente, '')                    AS [Cliente],
    facturas_vendedor                            AS [Facturas],
    venta_vendedor                               AS [Venta Vendedor],
    total_cliente_ruta                           AS [Total Cliente/Ruta],
    NULL                                         AS [# Clientes],
    'Detalle'                                    AS [Tipo]
FROM detalle_vnd

UNION ALL

-- Subtotal por Ruta
SELECT
    CONVERT(VARCHAR(10), dia, 103),
    centrodistribucion, supervisor, ruta,
    '>> SUBTOTAL RUTA', '', '',
    facturas,
    valor_venta,
    valor_venta,
    clientes,
    'SubTotal_Ruta'
FROM sub_ruta

UNION ALL

-- Total del Día
SELECT
    CONVERT(VARCHAR(10), dia, 103),
    '', '', '',
    '=== TOTAL DÍA ===', '', '',
    facturas,
    valor_venta,
    valor_venta,
    clientes,
    'Total_Día'
FROM sub_dia

UNION ALL

-- Promedio del período
SELECT
    '',  '', '', '',
    '*** PROMEDIO DIARIO (Abr-May) ***', '', '',
    prom_facturas,
    prom_venta,
    prom_venta,
    prom_clientes,
    'Promedio_Período'
FROM promedio

ORDER BY
    [Día],
    [C. Distribución],
    [Supervisor],
    [Ruta],
    CASE [Tipo]
        WHEN 'Detalle'           THEN 1
        WHEN 'SubTotal_Ruta'     THEN 2
        WHEN 'Total_Día'         THEN 3
        WHEN 'Promedio_Período'  THEN 4
    END,
    [Vendedor],
    [Cód. Cliente];

GO


/* ============================================================
   PARTE 2 — RESUMEN POR CENTRO DE DISTRIBUCIÓN
   ─────────────────────────────────────────────────────────────
   Valida cuántos clientes facturaron $500 o MENOS durante
   Abril-Mayo 2025 (incumplimiento de política).

   • Una fila por Centro de Distribución + fila de TOTAL GENERAL
   • Columnas: clientes totales, clientes que incumplieron,
     cantidad y monto de facturas bajo $500,
     promedio por factura y % de incumplimiento
   ============================================================ */

WITH fac_base AS (
    /* Un registro por factura con su valor total             */
    SELECT
        centrodistribucion,
        supervisor,
        ruta,
        codigocliente,
        MIN(nombrecliente)  AS nombrecliente,
        nofactura,
        SUM(valorventa)     AS valor_factura
    FROM dbo.vwventasResumen2025
    WHERE YEAR(fecha)  = 2025
      AND MONTH(fecha) IN (4, 5)
      AND tipoventa    = 1
    GROUP BY centrodistribucion, supervisor, ruta, codigocliente, nofactura
),

por_cliente AS (
    SELECT
        centrodistribucion,
        codigocliente,
        MIN(nombrecliente)                                              AS nombrecliente,
        COUNT(nofactura)                                                AS total_facturas,
        CAST(SUM(valor_factura) AS DECIMAL(18,2))                       AS total_comprado,
        SUM(CASE WHEN valor_factura <= 500 THEN 1     ELSE 0 END)       AS fact_bajo_500,
        CAST(SUM(CASE WHEN valor_factura <= 500
                      THEN valor_factura ELSE 0 END)  AS DECIMAL(18,2)) AS monto_bajo_500,
        CAST(AVG(valor_factura) AS DECIMAL(18,2))                       AS prom_factura
    FROM fac_base
    GROUP BY centrodistribucion, codigocliente
)

SELECT
    ISNULL(centrodistribucion, '** TOTAL GENERAL **')                   AS [Centro de Distribución],
    COUNT(DISTINCT codigocliente)                                        AS [Total Clientes],
    COUNT(DISTINCT CASE WHEN fact_bajo_500 > 0
                        THEN codigocliente END)                          AS [Clientes con Fact. <= $500],
    SUM(total_facturas)                                                  AS [Total Facturas],
    SUM(fact_bajo_500)                                                   AS [# Facturas <= $500],
    CAST(SUM(monto_bajo_500)   AS DECIMAL(18,2))                        AS [Monto Total <= $500],
    CAST(AVG(prom_factura)     AS DECIMAL(18,2))                        AS [Promedio x Factura],
    CAST(
        100.0
        * COUNT(DISTINCT CASE WHEN fact_bajo_500 > 0 THEN codigocliente END)
        / NULLIF(COUNT(DISTINCT codigocliente), 0)
        AS DECIMAL(5,1)
    )                                                                    AS [% Incumplimiento]
FROM por_cliente
GROUP BY ROLLUP(centrodistribucion)
ORDER BY GROUPING(centrodistribucion), centrodistribucion;

GO


/* ============================================================
   PARTE 2b — DETALLE DE CLIENTES QUE INCUMPLIERON (<= $500)
   ─────────────────────────────────────────────────────────────
   Lista cada cliente que tuvo al menos UNA factura <= $500.
   Ordenado: C. Distribución > Supervisor > Ruta
   ============================================================ */

WITH fac_base AS (
    SELECT
        centrodistribucion,
        supervisor,
        ruta,
        codigocliente,
        MIN(nombrecliente)  AS nombrecliente,
        nofactura,
        SUM(valorventa)     AS valor_factura
    FROM dbo.vwventasResumen2025
    WHERE YEAR(fecha)  = 2025
      AND MONTH(fecha) IN (4, 5)
      AND tipoventa    = 1
    GROUP BY centrodistribucion, supervisor, ruta, codigocliente, nofactura
)

SELECT
    centrodistribucion                                                    AS [C. Distribución],
    supervisor                                                            AS [Supervisor],
    ruta                                                                  AS [Ruta],
    CAST(codigocliente AS VARCHAR(20))                                    AS [Cód. Cliente],
    MIN(nombrecliente)                                                    AS [Cliente],
    COUNT(nofactura)                                                      AS [Total Facturas],
    CAST(SUM(valor_factura)    AS DECIMAL(18,2))                         AS [Total Comprado],
    SUM(CASE WHEN valor_factura <= 500 THEN 1 ELSE 0 END)                AS [# Fact. <= $500],
    CAST(SUM(CASE WHEN valor_factura <= 500
                  THEN valor_factura ELSE 0 END) AS DECIMAL(18,2))       AS [Monto Fact. <= $500],
    CAST(AVG(valor_factura) AS DECIMAL(18,2))                            AS [Promedio x Factura],
    CAST(
        100.0
        * SUM(CASE WHEN valor_factura <= 500 THEN 1 ELSE 0 END)
        / NULLIF(COUNT(nofactura), 0)
        AS DECIMAL(5,1)
    )                                                                     AS [% Fact. Bajo Mín.]
FROM fac_base
GROUP BY centrodistribucion, supervisor, ruta, codigocliente
HAVING SUM(CASE WHEN valor_factura <= 500 THEN 1 ELSE 0 END) > 0
ORDER BY
    centrodistribucion,
    supervisor,
    ruta,
    SUM(CASE WHEN valor_factura <= 500 THEN valor_factura ELSE 0 END) DESC;

GO
