/* ============================================================================
   SeguimientoParametros_RM
   Tabla de parametros base (baseline) para el modulo "Seguimientos Semanales"
   -> IEGT por Suplidor / Categoria / Producto.

   Contiene, por SUPLIDOR (Cat3ID), CATEGORIA (LinID) y PRODUCTO (ProID),
   el promedio semanal por vendedor ASIGNADO de los ultimos 3 meses.
   Estos son los parametros INICIALES; se pueden editar desde la
   configuracion del tablero (columnas ...SemVend y EfectividadObj).

   Reglas de negocio:
   - "Asignado" = el vendedor tiene el producto en su grupo de productos
     (RepresentantesGrupoProductos -> GrupoProductosDetalle -> Productos).
   - Poblacion = vendedores activos, no supervisores, division 10/11 (RepTipe).
   - Construccion BOTTOM-UP: Suplidor y Categoria = suma de sus Productos,
     de modo que la suma de todos reconcilia con la configuracion final.

   IMPORTANTE (para TI):
   - El usuario de la app ("Ventas") es de solo lectura; por eso al final
     se hace GRANT para que la app pueda leer y AJUSTAR estos parametros.
   - El sufijo "_RM" identifica que es una tabla de uso propio (Rudy).
   Ejecutar 1 vez (crear) y luego el bloque de poblado cuando se quiera
   refrescar el baseline.
   ============================================================================ */

/* ------------------------------------------------------------------ *
 * 1) CREAR TABLA                                                     *
 * ------------------------------------------------------------------ */
IF OBJECT_ID('dbo.SeguimientoParametros_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SeguimientoParametros_RM
    (
        ParID            int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_SeguimientoParametros_RM PRIMARY KEY,
        Nivel            varchar(12)   NOT NULL,   -- 'Suplidor' | 'Categoria' | 'Producto'
        SuplidorID       int           NOT NULL,   -- Cat3ID
        SuplidorNombre   varchar(150)  NULL,
        CategoriaID      int           NULL,       -- LinID  (NULL en nivel Suplidor)
        CategoriaNombre  varchar(150)  NULL,
        ProductoID       int           NULL,       -- ProID  (NULL en Suplidor/Categoria)
        ProductoNombre   varchar(200)  NULL,
        RepsAsignados    int           NOT NULL CONSTRAINT DF_SegPar_Reps  DEFAULT(0),
        Semanas          int           NOT NULL CONSTRAINT DF_SegPar_Sem   DEFAULT(13),
        FechaDesde       date          NULL,
        FechaHasta       date          NULL,
        /* Totales del periodo (3 meses) -- referencia / recalculo */
        ValorTotal3m     decimal(18,2) NOT NULL CONSTRAINT DF_SegPar_VT DEFAULT(0),
        UnidadesTotal3m  decimal(18,2) NOT NULL CONSTRAINT DF_SegPar_UT DEFAULT(0),
        CajasTotal3m     decimal(18,2) NOT NULL CONSTRAINT DF_SegPar_CT DEFAULT(0),
        /* Metas EDITABLES: promedio semanal por vendedor asignado */
        ValorSemVend     decimal(18,2) NOT NULL CONSTRAINT DF_SegPar_VS DEFAULT(0),
        UnidadesSemVend  decimal(18,2) NOT NULL CONSTRAINT DF_SegPar_US DEFAULT(0),
        CajasSemVend     decimal(18,2) NOT NULL CONSTRAINT DF_SegPar_CS DEFAULT(0),
        EfectividadObj   decimal(5,2)  NULL,       -- % objetivo de efectividad de ventas (editable)
        /* Auditoria */
        Editado          bit           NOT NULL CONSTRAINT DF_SegPar_Ed DEFAULT(0), -- 1 = ajustado a mano
        UsuInicioSesion  varchar(64)   NULL,
        FechaCalculo     datetime      NOT NULL CONSTRAINT DF_SegPar_FC DEFAULT(GETDATE()),
        FechaActualizacion datetime    NOT NULL CONSTRAINT DF_SegPar_FA DEFAULT(GETDATE())
    );

    /* Una fila por combinacion nivel/ids */
    CREATE UNIQUE INDEX UX_SegPar_Item
        ON dbo.SeguimientoParametros_RM (Nivel, SuplidorID, CategoriaID, ProductoID);
END
GO

/* ------------------------------------------------------------------ *
 * 2) POBLAR / REFRESCAR BASELINE (promedio semanal 3 meses)          *
 *    Preserva filas editadas a mano (Editado = 1).                   *
 * ------------------------------------------------------------------ */
DECLARE @ini date = DATEADD(WEEK,-13, CAST(GETDATE() AS date)),
        @fin date = CAST(GETDATE() AS date),
        @sem int  = 13;

/* Borra solo lo NO editado a mano, para no perder ajustes del usuario */
DELETE FROM dbo.SeguimientoParametros_RM WHERE Editado = 0;

;WITH Asig AS (   -- vendedor <-> producto (via grupos de productos)
    SELECT DISTINCT rgp.RepCodigo, gpd.ProID, pr.Cat3ID, pr.LinID
    FROM RepresentantesGrupoProductos rgp
    INNER JOIN GrupoProductosDetalle gpd ON gpd.GrpCodigo = rgp.GrpCodigo
    INNER JOIN Productos pr              ON pr.ProID      = gpd.ProID
    INNER JOIN Representantes r          ON r.RepCodigo   = rgp.RepCodigo
                                        AND r.RepEstatus = 1
                                        AND ISNULL(r.RepCargo,'') <> 'Supervisor'
    INNER JOIN RepresentantesAdicionales ra ON ra.RepCodigo = r.RepCodigo
                                        AND ra.RepTipe IN ('10','11')   -- division preventa
),
Venta AS (        -- ventas del periodo por producto
    SELECT pd.ProID,
           SUM(pd.PedCantidad)                          AS Uni,
           SUM(pd.PedCantidad / NULLIF(pr.ProUnidades,0)) AS Caj,   -- Cajas = unidades / unid_x_caja
           SUM(pd.PedCantidad * pd.PedPrecio)           AS Val
    FROM VWPedidos p
    INNER JOIN VWPedidosDetalle pd ON pd.PedSecuencia = p.PedSecuencia AND pd.RepCodigo = p.RepCodigo
    INNER JOIN Productos pr        ON pr.ProID = pd.ProID
    WHERE CAST(p.PedFecha AS date) BETWEEN @ini AND @fin
    GROUP BY pd.ProID
),
ProdBase AS (     -- GRANO: producto con >=1 vendedor asignado
    SELECT prr.ProID, prr.Cat3ID, prr.LinID, prr.Reps,
           ISNULL(v.Val,0) Val, ISNULL(v.Uni,0) Uni, ISNULL(v.Caj,0) Caj
    FROM (SELECT ProID, MAX(Cat3ID) Cat3ID, MAX(LinID) LinID,
                 COUNT(DISTINCT RepCodigo) Reps
          FROM Asig GROUP BY ProID) prr
    LEFT JOIN Venta v ON v.ProID = prr.ProID
),
SupReps AS (SELECT Cat3ID,        COUNT(DISTINCT RepCodigo) Reps FROM Asig GROUP BY Cat3ID),
CatReps AS (SELECT Cat3ID, LinID, COUNT(DISTINCT RepCodigo) Reps FROM Asig GROUP BY Cat3ID, LinID)

INSERT INTO dbo.SeguimientoParametros_RM
    (Nivel, SuplidorID, SuplidorNombre, CategoriaID, CategoriaNombre, ProductoID, ProductoNombre,
     RepsAsignados, Semanas, FechaDesde, FechaHasta,
     ValorTotal3m, UnidadesTotal3m, CajasTotal3m,
     ValorSemVend, UnidadesSemVend, CajasSemVend, EfectividadObj)
/* ---- Nivel SUPLIDOR (suma de sus productos) ---- */
SELECT 'Suplidor', s.Cat3ID, c3.Cat3Descripcion, NULL, NULL, NULL, NULL,
       sr.Reps, @sem, @ini, @fin,
       s.Val, s.Uni, s.Caj,
       s.Val/NULLIF(sr.Reps*@sem,0), s.Uni/NULLIF(sr.Reps*@sem,0), s.Caj/NULLIF(sr.Reps*@sem,0), NULL
FROM (SELECT Cat3ID, SUM(Val) Val, SUM(Uni) Uni, SUM(Caj) Caj FROM ProdBase GROUP BY Cat3ID) s
INNER JOIN SupReps  sr ON sr.Cat3ID = s.Cat3ID
INNER JOIN Categoria3 c3 ON c3.Cat3ID = s.Cat3ID
WHERE NOT EXISTS (SELECT 1 FROM dbo.SeguimientoParametros_RM e
                  WHERE e.Nivel='Suplidor' AND e.SuplidorID=s.Cat3ID AND e.Editado=1)
UNION ALL
/* ---- Nivel CATEGORIA (suma de sus productos) ---- */
SELECT 'Categoria', c.Cat3ID, c3.Cat3Descripcion, c.LinID, l.LinDescripcion, NULL, NULL,
       cr.Reps, @sem, @ini, @fin,
       c.Val, c.Uni, c.Caj,
       c.Val/NULLIF(cr.Reps*@sem,0), c.Uni/NULLIF(cr.Reps*@sem,0), c.Caj/NULLIF(cr.Reps*@sem,0), NULL
FROM (SELECT Cat3ID, LinID, SUM(Val) Val, SUM(Uni) Uni, SUM(Caj) Caj FROM ProdBase GROUP BY Cat3ID, LinID) c
INNER JOIN CatReps  cr ON cr.Cat3ID = c.Cat3ID AND cr.LinID = c.LinID
INNER JOIN Categoria3 c3 ON c3.Cat3ID = c.Cat3ID
INNER JOIN Lineas   l  ON l.LinID = c.LinID
WHERE NOT EXISTS (SELECT 1 FROM dbo.SeguimientoParametros_RM e
                  WHERE e.Nivel='Categoria' AND e.SuplidorID=c.Cat3ID AND e.CategoriaID=c.LinID AND e.Editado=1)
UNION ALL
/* ---- Nivel PRODUCTO (grano) ---- */
SELECT 'Producto', pb.Cat3ID, c3.Cat3Descripcion, pb.LinID, l.LinDescripcion, pb.ProID, pr.ProDescripcion,
       pb.Reps, @sem, @ini, @fin,
       pb.Val, pb.Uni, pb.Caj,
       pb.Val/NULLIF(pb.Reps*@sem,0), pb.Uni/NULLIF(pb.Reps*@sem,0), pb.Caj/NULLIF(pb.Reps*@sem,0), NULL
FROM ProdBase pb
INNER JOIN Productos  pr ON pr.ProID  = pb.ProID
INNER JOIN Categoria3 c3 ON c3.Cat3ID = pb.Cat3ID
INNER JOIN Lineas     l  ON l.LinID   = pb.LinID
WHERE NOT EXISTS (SELECT 1 FROM dbo.SeguimientoParametros_RM e
                  WHERE e.Nivel='Producto' AND e.ProductoID=pb.ProID AND e.Editado=1);
GO

/* ------------------------------------------------------------------ *
 * 3) PERMISOS para la app (usuario de solo lectura "Ventas")         *
 *    Necesita leer y AJUSTAR los parametros desde la configuracion.  *
 * ------------------------------------------------------------------ */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SeguimientoParametros_RM TO [Ventas];
GO

/* ------------------------------------------------------------------ *
 * 4) REVISION rapida                                                 *
 * ------------------------------------------------------------------ */
-- SELECT Nivel, COUNT(*) Filas, SUM(ValorTotal3m) TotalValor
-- FROM dbo.SeguimientoParametros_RM GROUP BY Nivel;   -- los 3 totales deben coincidir
-- SELECT TOP 20 * FROM dbo.SeguimientoParametros_RM WHERE Nivel='Suplidor' ORDER BY ValorTotal3m DESC;
