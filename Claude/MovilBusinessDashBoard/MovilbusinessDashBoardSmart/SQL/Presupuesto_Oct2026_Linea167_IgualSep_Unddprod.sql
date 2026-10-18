/* =============================================================================
   Presupuesto OCTUBRE 2026 = SEPTIEMBRE 2026 para la línea 167 (AGUA)
   Tipo: 'Unddprod' (unidades por producto). Tabla: dbo.PresupuestosHistoricos
   -----------------------------------------------------------------------------
   Qué hace (para TODOS los productos de Productos.LinID = 167 que tengan
   presupuesto en septiembre):
     1) ACTUALIZA las filas de octubre que ya existen  -> PrePresupuesto = el de sep.
     2) INSERTA en octubre las combinaciones Vendedor × Producto que existen en
        septiembre y no en octubre.
     3) Opcional (@PonerEnCeroSinSep = 1): las filas de octubre que NO existen en
        septiembre quedan en 0 (hoy no hay ninguna).
   No toca PreEjecutado, ni otros PreTipo, ni otros meses o líneas.

   Estado verificado el 30-sep-2026 (consulta de solo lectura):
     - Productos con presupuesto: 982003 (BOTELLITAS AGUA CRYSTAL 1.5 L) y
       982005 (BOTELLITA AGUA CRYSTAL 0.5 L).
     - Septiembre: 54 filas / 19,630.22 unidades.
     - Octubre:    52 filas / 16,053.79 unidades.
     - Solo en septiembre (se insertan): Rep 36 y Rep 89 (Oficina), producto 982005.

   USO: correr primero con @Aplicar = 0 (vista previa, revierte todo).
        Si los números cuadran, cambiar a @Aplicar = 1 y correr de nuevo.
   Después: correr sp_PresupuestosPRODActualizar para que se recalculen los
   presupuestos en dinero/categoría (MNTDCAT, etc.) del mes 10.
   ============================================================================= */
USE [MovilBusiness5STD];   -- base de producción (sus columnas usan Modern_Spanish_CI_AS)
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Anio            smallint    = 2026;
DECLARE @MesOrigen       smallint    = 9;
DECLARE @MesDestino      smallint    = 10;
DECLARE @LinID           int         = 167;
DECLARE @PreTipo         varchar(15) = 'Unddprod';
DECLARE @Usuario         varchar(64) = 'rmercedes';
DECLARE @PonerEnCeroSinSep bit       = 0;   -- 1 = filas de oct sin equivalente en sep -> 0
DECLARE @Aplicar         bit         = 0;   -- 0 = vista previa (ROLLBACK), 1 = aplicar (COMMIT)

/* ---------- Universo: productos de la línea ---------- */
IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;
SELECT CAST(p.ProID AS varchar(20)) COLLATE Modern_Spanish_CI_AS AS ProRef, p.ProDescripcion
INTO #Prod
FROM dbo.Productos p
WHERE p.LinID = @LinID;

/* ---------- Origen: septiembre ---------- */
IF OBJECT_ID('tempdb..#Origen') IS NOT NULL DROP TABLE #Origen;
SELECT h.RepCodigo, h.PreReferencia, h.PrePresupuesto, h.RepSupervisor, h.mbversion
INTO #Origen
FROM dbo.PresupuestosHistoricos h
JOIN #Prod pr ON pr.ProRef = LTRIM(RTRIM(h.PreReferencia))
WHERE h.PreTipo = @PreTipo AND h.PreAnio = @Anio AND h.PreMes = @MesOrigen;

/* ---------- VISTA PREVIA: antes / después por vendedor y producto ---------- */
SELECT  COALESCE(o.RepCodigo, d.RepCodigo)         AS RepCodigo,
        COALESCE(o.PreReferencia, d.PreReferencia) AS ProID,
        pr.ProDescripcion,
        d.PrePresupuesto                           AS Oct_Actual,
        CASE WHEN o.RepCodigo IS NOT NULL THEN o.PrePresupuesto
             WHEN @PonerEnCeroSinSep = 1 THEN 0
             ELSE d.PrePresupuesto END             AS Oct_Nuevo,
        CASE WHEN d.RepCodigo IS NULL THEN 'INSERTAR'
             WHEN o.RepCodigo IS NULL THEN CASE WHEN @PonerEnCeroSinSep = 1 THEN 'PONER EN 0' ELSE 'SIN CAMBIO (no está en sep)' END
             WHEN ISNULL(d.PrePresupuesto,0) <> ISNULL(o.PrePresupuesto,0) THEN 'ACTUALIZAR'
             ELSE 'SIN CAMBIO' END                 AS Accion
FROM #Origen o
FULL JOIN (SELECT h.RepCodigo, h.PreReferencia, h.PrePresupuesto
           FROM dbo.PresupuestosHistoricos h
           JOIN #Prod pr2 ON pr2.ProRef = LTRIM(RTRIM(h.PreReferencia))
           WHERE h.PreTipo = @PreTipo AND h.PreAnio = @Anio AND h.PreMes = @MesDestino) d
       ON d.RepCodigo = o.RepCodigo AND d.PreReferencia = o.PreReferencia
LEFT JOIN #Prod pr ON pr.ProRef = LTRIM(RTRIM(COALESCE(o.PreReferencia, d.PreReferencia)))
ORDER BY Accion, RepCodigo, ProID;

/* Totales antes */
SELECT 'ANTES' AS Momento, h.PreMes, COUNT(*) AS Filas, SUM(h.PrePresupuesto) AS Unidades
FROM dbo.PresupuestosHistoricos h
JOIN #Prod pr ON pr.ProRef = LTRIM(RTRIM(h.PreReferencia))
WHERE h.PreTipo = @PreTipo AND h.PreAnio = @Anio AND h.PreMes IN (@MesOrigen, @MesDestino)
GROUP BY h.PreMes;

/* ---------- Respaldo de octubre (antes de tocarlo) ---------- */
IF OBJECT_ID('dbo.PresupuestosHistoricos_Bkp_L167_Oct2026') IS NULL
BEGIN
    SELECT h.*, GETDATE() AS FechaRespaldo
    INTO dbo.PresupuestosHistoricos_Bkp_L167_Oct2026
    FROM dbo.PresupuestosHistoricos h
    JOIN #Prod pr ON pr.ProRef = LTRIM(RTRIM(h.PreReferencia))
    WHERE h.PreTipo = @PreTipo AND h.PreAnio = @Anio AND h.PreMes = @MesDestino;
END

BEGIN TRAN;

    /* 1) Actualizar las que ya existen en octubre */
    UPDATE d
       SET d.PrePresupuesto        = o.PrePresupuesto,
           d.UsuInicioSesion       = @Usuario,
           d.PreFechaActualizacion = GETDATE()
    FROM dbo.PresupuestosHistoricos d
    JOIN #Origen o ON o.RepCodigo = d.RepCodigo AND o.PreReferencia = d.PreReferencia
    WHERE d.PreTipo = @PreTipo AND d.PreAnio = @Anio AND d.PreMes = @MesDestino;
    PRINT CONCAT('Filas actualizadas: ', @@ROWCOUNT);

    /* 2) Insertar las que están en septiembre y faltan en octubre */
    INSERT INTO dbo.PresupuestosHistoricos
        (RepCodigo, PreTipo, PreAnio, PreMes, PreReferencia, PrePresupuesto, PreEjecutado,
         UsuInicioSesion, PreFechaActualizacion, rowguid, mbversion, RepSupervisor)
    SELECT o.RepCodigo, @PreTipo, @Anio, @MesDestino, o.PreReferencia, o.PrePresupuesto, 0,
           @Usuario, GETDATE(), NEWID(), COALESCE(NULLIF(o.mbversion,'Null'),'PresupSmart'),
           NULLIF(o.RepSupervisor,'Null')
    FROM #Origen o
    WHERE NOT EXISTS (SELECT 1 FROM dbo.PresupuestosHistoricos d
                      WHERE d.RepCodigo = o.RepCodigo AND d.PreTipo = @PreTipo AND d.PreAnio = @Anio
                        AND d.PreMes = @MesDestino AND d.PreReferencia = o.PreReferencia);
    PRINT CONCAT('Filas insertadas: ', @@ROWCOUNT);

    /* 3) Opcional: octubre sin equivalente en septiembre -> 0 */
    IF @PonerEnCeroSinSep = 1
    BEGIN
        UPDATE d
           SET d.PrePresupuesto = 0, d.UsuInicioSesion = @Usuario, d.PreFechaActualizacion = GETDATE()
        FROM dbo.PresupuestosHistoricos d
        JOIN #Prod pr ON pr.ProRef = LTRIM(RTRIM(d.PreReferencia))
        WHERE d.PreTipo = @PreTipo AND d.PreAnio = @Anio AND d.PreMes = @MesDestino
          AND NOT EXISTS (SELECT 1 FROM #Origen o WHERE o.RepCodigo = d.RepCodigo AND o.PreReferencia = d.PreReferencia);
        PRINT CONCAT('Filas puestas en 0: ', @@ROWCOUNT);
    END

    /* Verificación: octubre debe quedar igual a septiembre */
    SELECT 'DESPUES' AS Momento, h.PreMes, COUNT(*) AS Filas, SUM(h.PrePresupuesto) AS Unidades
    FROM dbo.PresupuestosHistoricos h
    JOIN #Prod pr ON pr.ProRef = LTRIM(RTRIM(h.PreReferencia))
    WHERE h.PreTipo = @PreTipo AND h.PreAnio = @Anio AND h.PreMes IN (@MesOrigen, @MesDestino)
    GROUP BY h.PreMes;

    SELECT COUNT(*) AS Diferencias_Sep_vs_Oct   -- debe dar 0
    FROM #Origen o
    LEFT JOIN dbo.PresupuestosHistoricos d
           ON d.RepCodigo = o.RepCodigo AND d.PreReferencia = o.PreReferencia
          AND d.PreTipo = @PreTipo AND d.PreAnio = @Anio AND d.PreMes = @MesDestino
    WHERE d.RepCodigo IS NULL OR ISNULL(d.PrePresupuesto,0) <> ISNULL(o.PrePresupuesto,0);

IF @Aplicar = 1
BEGIN
    COMMIT TRAN;
    PRINT 'APLICADO (COMMIT). Ahora correr sp_PresupuestosPRODActualizar para recalcular el mes 10.';
END
ELSE
BEGIN
    ROLLBACK TRAN;
    PRINT 'VISTA PREVIA: no se guardó nada (ROLLBACK). Cambie @Aplicar = 1 para aplicar.';
END

/* ---------- Para revertir (si hiciera falta) ----------
UPDATE d SET d.PrePresupuesto = b.PrePresupuesto, d.UsuInicioSesion = b.UsuInicioSesion,
             d.PreFechaActualizacion = b.PreFechaActualizacion
FROM dbo.PresupuestosHistoricos d
JOIN dbo.PresupuestosHistoricos_Bkp_L167_Oct2026 b
  ON b.RepCodigo = d.RepCodigo AND b.PreTipo = d.PreTipo AND b.PreAnio = d.PreAnio
 AND b.PreMes = d.PreMes AND b.PreReferencia = d.PreReferencia;

DELETE d FROM dbo.PresupuestosHistoricos d
JOIN dbo.Productos p ON CAST(p.ProID AS varchar(20)) = LTRIM(RTRIM(d.PreReferencia)) AND p.LinID = 167
WHERE d.PreTipo = 'Unddprod' AND d.PreAnio = 2026 AND d.PreMes = 10
  AND NOT EXISTS (SELECT 1 FROM dbo.PresupuestosHistoricos_Bkp_L167_Oct2026 b
                  WHERE b.RepCodigo = d.RepCodigo AND b.PreReferencia = d.PreReferencia);
--------------------------------------------------------- */
