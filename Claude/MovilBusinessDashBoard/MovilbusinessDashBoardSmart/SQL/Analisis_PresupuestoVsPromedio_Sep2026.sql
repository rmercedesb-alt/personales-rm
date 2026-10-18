/* ═══════════════════════════════════════════════════════════════════════
   Análisis de SOLO LECTURA (2026-09-06, pedido explícito del usuario) — no
   inserta, actualiza ni borra nada. Compara, por Vendedor×Suplidor, el
   presupuesto YA PUBLICADO de Septiembre-2026 (MNTDCAT3VEN, Monto) contra el
   promedio de ventas reales de los últimos 3 meses (01/jun/2026-31/ago/2026).
   Devuelve SOLO los casos donde el presupuesto queda POR DEBAJO del promedio.

   Mismo criterio de atribución que usa el motor de "Presupuestos — Gestión"
   (clientesDetalle + universo de productos por rep vía
   RepresentantesGrupoProductos) para que estos números salgan IGUALES a lo
   que "5. Editar / Reajustar Presupuesto" generaría si lo corrieras con
   Base=01/06/2026-31/08/2026, Destino=2026-09, filtrado a cada Suplidor.

   Ejecutar en MovilBusiness5STD (producción) — solo SELECT.
   ═══════════════════════════════════════════════════════════════════════ */

;WITH Ventas3M AS (
    SELECT rv.RepCodigo, pr.Cat3ID,
           SUM(d.VenCantidad*d.VenPrecio - ISNULL(d.VenTotalDescuento,0)) AS MontoTotal3M
    FROM VWVentas v WITH (NOLOCK)
    INNER JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia=v.VenSecuencia AND d.RepCodigo=v.RepCodigo
    INNER JOIN clientesDetalle rv WITH (NOLOCK) ON rv.CliID = v.CliID
    INNER JOIN Representantes rpv WITH (NOLOCK) ON rpv.RepCodigo = rv.RepCodigo AND rpv.RepCargo IN ('PreVendedor','Vendedor')
    INNER JOIN Productos pr WITH (NOLOCK) ON pr.ProID = d.ProID
    WHERE v.VenEstatus<>0 AND v.VenFecha >= '2026-06-01' AND v.VenFecha < '2026-09-01'
      AND EXISTS (SELECT 1 FROM RepresentantesGrupoProductos rgp WITH (NOLOCK)
                  INNER JOIN GrupoProductosDetalle gpd WITH (NOLOCK) ON gpd.GrpCodigo=rgp.GrpCodigo
                  WHERE rgp.RepCodigo=rv.RepCodigo AND gpd.ProID=d.ProID)
    GROUP BY rv.RepCodigo, pr.Cat3ID
),
PresupuestoSep AS (
    SELECT h.RepCodigo, TRY_CAST(h.PreReferencia AS INT) AS Cat3ID, SUM(h.PrePresupuesto) AS PresupuestoSepRD
    FROM PresupuestosHistoricos h WITH (NOLOCK)
    WHERE h.PreTipo='MNTDCAT3VEN' AND h.PreAnio=2026 AND h.PreMes=9
    GROUP BY h.RepCodigo, TRY_CAST(h.PreReferencia AS INT)
)
SELECT
    p.RepCodigo,
    r.RepNombre,
    c3.Cat3ID,
    c3.Cat3Descripcion   AS Suplidor,
    p.PresupuestoSepRD,
    v.MontoTotal3M / 3.0 AS PromedioVentas3M,
    p.PresupuestoSepRD - (v.MontoTotal3M / 3.0) AS DiferenciaAbsoluta,
    CASE WHEN v.MontoTotal3M > 0
         THEN (p.PresupuestoSepRD - (v.MontoTotal3M / 3.0)) / (v.MontoTotal3M / 3.0)
         ELSE NULL END AS DiferenciaPct
FROM PresupuestoSep p
INNER JOIN Ventas3M v ON v.RepCodigo = p.RepCodigo AND v.Cat3ID = p.Cat3ID
INNER JOIN Representantes r WITH (NOLOCK) ON r.RepCodigo = p.RepCodigo
INNER JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = p.Cat3ID
WHERE p.PresupuestoSepRD < (v.MontoTotal3M / 3.0)
ORDER BY DiferenciaPct ASC;
