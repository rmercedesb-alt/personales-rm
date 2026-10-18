/* ============================================================
   Ventas semanales (PEDIDOS) - Canal Minorista - ultimas 8 semanas
   Cortes: Total / Centro / Supervisor / Suplidor
   Semana = Lunes a Domingo. La semana actual se proyecta:
     - dias < hoy            -> real
     - hoy                   -> MAX(real a la hora, promedio de ese dia de semana)
     - dias futuros de la sem-> promedio de ese dia de semana
     (promedio = mismo dia de semana de las ultimas 4 semanas completas;
      se divide entre 4 aunque ese dia no haya tenido venta. Se calcula al grano
      Centro x Supervisor x Suplidor y se suma a cada corte, para que todos cuadren)
   Cada semana se compara contra la anterior (la primera semana mostrada
   se compara contra una 9a semana que solo se trae para eso).

   Definiciones (iguales a la pestana Pedidos de Historicos):
     Fuente  : VWPedidos + VWPedidosDetalle, PedEstatus IN (2,7,11)
     Monto   : SUM(PedCantidad * (PedPrecio - ISNULL(PedDescuento,0)))  (neto, sin ITBIS)
     Canal   : canal del CLIENTE (Clientes.CanID) cuya descripcion empieza con 'Minorista'
               (2000 Minorista, 4000 CDN, 5000 Alternos, 6000 CDN NOSOTRAS, 9000 Sur)
     Excluye supervisores S111 (Rudy Mercedes) y S13 (Cristiano Perez - Tradicional)
     Centro / Supervisor : del representante del pedido (p.RepCodigo) - asignacion actual
     Suplidor: Productos.Cat3ID -> Categoria3
   ============================================================ */
SET NOCOUNT ON;

DECLARE @Hoy       date = CAST(GETDATE() AS date);
DECLARE @WdHoy     int  = DATEDIFF(DAY,'19000101',@Hoy) % 7;       -- 0=Lun ... 6=Dom (no depende de DATEFIRST)
DECLARE @SemActual date = DATEADD(DAY,-@WdHoy,@Hoy);               -- lunes de la semana actual
DECLARE @Ini       date = DATEADD(WEEK,-8,@SemActual);             -- 8 previas + actual = 9 semanas (la 1a solo para comparar)
DECLARE @Fin       date = DATEADD(WEEK,1,@SemActual);              -- exclusivo
DECLARE @RefIni    date = DATEADD(WEEK,-4,@SemActual);             -- ventana de promedios: 4 semanas completas

IF OBJECT_ID('tempdb..#L') IS NOT NULL DROP TABLE #L;
IF OBJECT_ID('tempdb..#D') IS NOT NULL DROP TABLE #D;
IF OBJECT_ID('tempdb..#P') IS NOT NULL DROP TABLE #P;
IF OBJECT_ID('tempdb..#E') IS NOT NULL DROP TABLE #E;
IF OBJECT_ID('tempdb..#ED') IS NOT NULL DROP TABLE #ED;

/* 1) Pedidos a nivel Fecha x Centro x Supervisor x Suplidor */
SELECT CAST(p.PedFecha AS date)                        AS Fecha,
       ISNULL(cd.CedDescripcion,'(Sin centro)')        AS Centro,
       ISNULL(sup.RepNombre,'(Sin supervisor)')        AS Supervisor,
       ISNULL(s.Cat3Descripcion,'(Sin suplidor)')      AS Suplidor,
       SUM(pd.PedCantidad * (pd.PedPrecio - ISNULL(pd.PedDescuento,0))) AS Monto
INTO #L
FROM VWPedidos p WITH (NOLOCK)
INNER JOIN VWPedidosDetalle pd WITH (NOLOCK) ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
INNER JOIN Productos pr WITH (NOLOCK)        ON pr.ProID = pd.ProID
LEFT  JOIN Categoria3 s WITH (NOLOCK)        ON s.Cat3ID = pr.Cat3ID
INNER JOIN Clientes cl WITH (NOLOCK)         ON cl.CliID = p.CliID
INNER JOIN CanalDistribucion ca WITH (NOLOCK) ON ca.CanID = cl.CanID AND ca.CanDescripcion LIKE 'Minorista%'
LEFT  JOIN Representantes r WITH (NOLOCK)    ON r.RepCodigo = p.RepCodigo
LEFT  JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo = r.CedCodigo
LEFT  JOIN Representantes sup WITH (NOLOCK)  ON sup.RepCodigo = r.RepSupervisor
WHERE p.PedFecha >= @Ini AND p.PedFecha < @Fin
  AND p.PedEstatus IN (2,7,11)
  AND ISNULL(r.RepSupervisor,'') NOT IN ('S111','S13')   -- excluidos (pedido del usuario): S111 Rudy Mercedes, S13 Cristiano Perez (Tradicional)
GROUP BY CAST(p.PedFecha AS date), cd.CedDescripcion, sup.RepNombre, s.Cat3Descripcion;

/* 2) Diario por cada corte (Total / Centro / Supervisor / Suplidor) */
SELECT Dimension, Item, Fecha, SUM(Monto) AS Monto
INTO #D
FROM (
    SELECT 'Total'      AS Dimension, 'Minorista' AS Item, Fecha, Monto FROM #L
    UNION ALL SELECT 'Centro',      Centro,      Fecha, Monto FROM #L
    UNION ALL SELECT 'Supervisor',  Supervisor,  Fecha, Monto FROM #L
    UNION ALL SELECT 'Suplidor',    Suplidor,    Fecha, Monto FROM #L
) x
GROUP BY Dimension, Item, Fecha;

/* 3) Estimado de la semana actual al grano fino (Centro x Supervisor x Suplidor) y luego se suma
      hacia cada corte -> asi Total = suma de Centros = suma de Supervisores = suma de Suplidores.
      Promedio por dia de semana = ult. 4 semanas completas.                                      */
SELECT Centro, Supervisor, Suplidor, DATEDIFF(DAY,'19000101',Fecha) % 7 AS Wd, SUM(Monto) / 4.0 AS PromDia
INTO #P
FROM #L
WHERE Fecha >= @RefIni AND Fecha < @SemActual
GROUP BY Centro, Supervisor, Suplidor, DATEDIFF(DAY,'19000101',Fecha) % 7;

SELECT p.Centro, p.Supervisor, p.Suplidor,
       SUM(CASE WHEN p.Wd > @WdHoy THEN p.PromDia ELSE 0 END)                         -- dias que faltan
     + SUM(CASE WHEN p.Wd = @WdHoy AND p.PromDia > ISNULL(h.RealHoy,0)
                THEN p.PromDia - ISNULL(h.RealHoy,0) ELSE 0 END) AS Est               -- resto de hoy
INTO #E
FROM #P p
LEFT JOIN (SELECT Centro, Supervisor, Suplidor, SUM(Monto) AS RealHoy
           FROM #L WHERE Fecha = @Hoy GROUP BY Centro, Supervisor, Suplidor) h
       ON h.Centro = p.Centro AND h.Supervisor = p.Supervisor AND h.Suplidor = p.Suplidor
GROUP BY p.Centro, p.Supervisor, p.Suplidor;

SELECT Dimension, Item, SUM(Est) AS Est
INTO #ED
FROM (
    SELECT 'Total' AS Dimension, 'Minorista' AS Item, Est FROM #E
    UNION ALL SELECT 'Centro',     Centro,     Est FROM #E
    UNION ALL SELECT 'Supervisor', Supervisor, Est FROM #E
    UNION ALL SELECT 'Suplidor',   Suplidor,   Est FROM #E
) x
GROUP BY Dimension, Item;

/* 4) Rejilla Item x Semana, real + estimado, y comparacion vs semana anterior */
;WITH Items AS (
    SELECT DISTINCT Dimension, Item FROM #D
), Semanas AS (
    SELECT DATEADD(WEEK, n, @SemActual) AS SemanaInicio
    FROM (VALUES (-8),(-7),(-6),(-5),(-4),(-3),(-2),(-1),(0)) v(n)
), RealSem AS (
    SELECT Dimension, Item,
           DATEADD(DAY, -(DATEDIFF(DAY,'19000101',Fecha) % 7), Fecha) AS SemanaInicio,
           SUM(Monto) AS VentasReal,
           SUM(CASE WHEN Fecha = @Hoy THEN Monto ELSE 0 END) AS RealHoy
    FROM #D
    GROUP BY Dimension, Item, DATEADD(DAY, -(DATEDIFF(DAY,'19000101',Fecha) % 7), Fecha)
), Grid AS (
    SELECT i.Dimension, i.Item, w.SemanaInicio,
           ISNULL(rs.VentasReal,0) AS VentasReal,
           CASE WHEN w.SemanaInicio = @SemActual THEN ISNULL(ed.Est,0) ELSE 0 END AS VentasEstimado
    FROM Items i
    CROSS JOIN Semanas w
    LEFT JOIN RealSem rs ON rs.Dimension = i.Dimension AND rs.Item = i.Item AND rs.SemanaInicio = w.SemanaInicio
    LEFT JOIN #ED ed     ON ed.Dimension = i.Dimension AND ed.Item = i.Item
), Cmp AS (
    SELECT g.*, g.VentasReal + g.VentasEstimado AS Ventas,
           LAG(g.VentasReal + g.VentasEstimado) OVER (PARTITION BY g.Dimension, g.Item ORDER BY g.SemanaInicio) AS VentasSemAnt
    FROM Grid g
)
SELECT Dimension, Item,
       'Sem ' + CAST(DATEPART(ISO_WEEK, SemanaInicio) AS varchar(2)) + ' (' + LEFT(CONVERT(varchar(10), SemanaInicio, 103), 5) + ')' AS Semana,
       SemanaInicio,
       CAST(VentasReal     AS decimal(18,2)) AS VentasReal,
       CAST(VentasEstimado AS decimal(18,2)) AS VentasEstimado,
       CAST(Ventas         AS decimal(18,2)) AS Ventas,
       CAST(VentasSemAnt   AS decimal(18,2)) AS VentasSemanaAnterior,
       CAST(CASE WHEN VentasSemAnt > 0 THEN (Ventas - VentasSemAnt) * 100.0 / VentasSemAnt END AS decimal(9,1)) AS VarPct,
       CASE WHEN Ventas > VentasSemAnt THEN NCHAR(9650)      -- ▲ crece
            WHEN Ventas < VentasSemAnt THEN NCHAR(9660)      -- ▼ decrece
            ELSE '=' END AS Tendencia,
       CASE WHEN SemanaInicio = @SemActual THEN 'Estimada' ELSE 'Cerrada' END AS Estado
FROM Cmp
WHERE SemanaInicio >= DATEADD(WEEK,-7,@SemActual)         -- muestra 8 semanas (la 9a solo sirvio para comparar)
ORDER BY CASE Dimension WHEN 'Total' THEN 1 WHEN 'Centro' THEN 2 WHEN 'Supervisor' THEN 3 ELSE 4 END,
         Item, SemanaInicio;
