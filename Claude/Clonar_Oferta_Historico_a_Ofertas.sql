/* =====================================================================
   CLONAR UNA OFERTA DESDE OfertasHistoricos HACIA Ofertas (MovilBusiness5STD)
   ---------------------------------------------------------------------
   Origen : OfeID = @OfeIDOrigen (por defecto 4004) en las tablas *Historicos
   Destino: nueva oferta con OfeID = (maximo existente + 1) en las tablas vivas

   Tablas que copia (solo lo que tiene datos reales de la oferta):
     OfertasHistoricos                       -> Ofertas
     OfertasDetalleHistoricos                -> OfertasDetalle
     OfertasDetalleTiposNegocioHistoricos    -> OfertasDetalleTiposNegocio
     OfertasRepresentantesHistoricos         -> OfertasRepresentantes
     OfertasClientesExcepcionesHistoricos    -> OfertasClientesExcepciones (la 4004 no tiene)
   Ademas actualiza el contador Secuencias ('Ofertas') para que la app no
   vuelva a generar el mismo OfeID.

   Notas:
   - OfeID NO es identity: se calcula como MAX(Ofertas, OfertasHistoricos, Secuencias.Ofertas) + 1.
   - rowguid se genera nuevo (NEWID) en cada fila; OfeFechaActualizacion = GETDATE()
     para que la replicacion/sincronizacion mande la oferta a los dispositivos.
   - Columnas que solo existen en un lado (OfeNumeroERP en historicos; OfeMontoInicial,
     OfeMontoFinal, OfeTransporte, OfeRango en las vivas) se manejan explicitamente.
   - La oferta original NO se toca (queda en historicos).
   - Corre todo en UNA transaccion. @Confirmar = 0 -> simula y hace ROLLBACK (no deja nada).
     @Confirmar = 1 -> COMMIT.  Primero corrala con 0, revise los resultados y luego con 1.
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

------------------------------------------------------------------------
-- PARAMETROS (ajustar aqui)
------------------------------------------------------------------------
DECLARE @OfeIDOrigen      INT          = 4004;               -- oferta en OfertasHistoricos
DECLARE @FechaInicio      DATETIME     = CAST(CAST(GETDATE() AS DATE) AS DATETIME);  -- nuevo inicio (hoy)
DECLARE @FechaFin         DATETIME     = '2026-10-31';       -- NULL = misma duracion que la original
DECLARE @Descripcion      VARCHAR(500) = NULL;               -- NULL = misma descripcion
DECLARE @SoloRepsActivos  BIT          = 0;                  -- 1 = copiar solo representantes activos
DECLARE @Confirmar        BIT          = 0;                  -- 0 = simulacion (ROLLBACK), 1 = COMMIT

------------------------------------------------------------------------
-- VALIDACIONES PREVIAS
------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM OfertasHistoricos WHERE OfeID = @OfeIDOrigen)
BEGIN
    RAISERROR('La oferta %d no existe en OfertasHistoricos.', 16, 1, @OfeIDOrigen);
    RETURN;
END

IF EXISTS (SELECT 1 FROM Ofertas WHERE OfeID = @OfeIDOrigen)
BEGIN
    RAISERROR('La oferta %d ya existe en Ofertas (no es necesario clonarla desde historicos).', 16, 1, @OfeIDOrigen);
    RETURN;
END

DECLARE @OrigIni DATETIME, @OrigFin DATETIME, @GrpCodigo VARCHAR(50), @GrpOferta VARCHAR(50);
SELECT @OrigIni   = OfeFechainicio,
       @OrigFin   = OfeFechaFin,
       @GrpCodigo = GrpCodigo,
       @GrpOferta = grpCodigoOferta
FROM OfertasHistoricos WHERE OfeID = @OfeIDOrigen;

IF @FechaFin IS NULL
    SET @FechaFin = DATEADD(DAY, DATEDIFF(DAY, @OrigIni, @OrigFin), @FechaInicio);

IF @FechaFin < @FechaInicio
BEGIN
    RAISERROR('La fecha fin es menor que la fecha inicio.', 16, 1);
    RETURN;
END

-- Los grupos de productos que usa la oferta deben existir
IF ISNULL(@GrpCodigo, '0') NOT IN ('0', '') AND NOT EXISTS (SELECT 1 FROM GrupoProductos WHERE GrpCodigo = @GrpCodigo)
BEGIN
    RAISERROR('El grupo de productos GrpCodigo=%s no existe en GrupoProductos.', 16, 1, @GrpCodigo);
    RETURN;
END
IF ISNULL(@GrpOferta, '0') NOT IN ('0', '') AND NOT EXISTS (SELECT 1 FROM GrupoProductos WHERE GrpCodigo = @GrpOferta)
BEGIN
    RAISERROR('El grupo de productos grpCodigoOferta=%s no existe en GrupoProductos.', 16, 1, @GrpOferta);
    RETURN;
END

------------------------------------------------------------------------
BEGIN TRY
    BEGIN TRAN;

    --------------------------------------------------------------------
    -- NUEVO OfeID = maximo existente + 1 (bloquea el contador mientras dura la transaccion)
    --------------------------------------------------------------------
    DECLARE @MaxOfertas INT, @MaxHist INT, @SecActual INT, @NuevoOfeID INT;
    SELECT @MaxOfertas = ISNULL(MAX(OfeID), 0) FROM Ofertas WITH (UPDLOCK, HOLDLOCK);
    SELECT @MaxHist    = ISNULL(MAX(OfeID), 0) FROM OfertasHistoricos;
    SELECT @SecActual  = ISNULL(MAX(SecSecuencia), 0) FROM Secuencias WITH (UPDLOCK, HOLDLOCK) WHERE SecTabla = 'Ofertas';

    SELECT @NuevoOfeID = MAX(v) + 1 FROM (VALUES (@MaxOfertas), (@MaxHist), (@SecActual)) AS t(v);

    -- Seguridad: que ese ID no lo use ninguna transaccion viva
    IF EXISTS (SELECT 1 FROM PedidosDetalle WHERE OfeID = @NuevoOfeID)
       OR EXISTS (SELECT 1 FROM VentasDetalle WHERE OfeID = @NuevoOfeID)
        RAISERROR('El OfeID %d ya aparece en PedidosDetalle/VentasDetalle. Revise la secuencia.', 16, 1, @NuevoOfeID);

    PRINT 'Origen (historico): ' + CAST(@OfeIDOrigen AS VARCHAR(20)) + '  ->  Nueva oferta: ' + CAST(@NuevoOfeID AS VARCHAR(20));
    PRINT 'Vigencia nueva: ' + CONVERT(VARCHAR(10), @FechaInicio, 120) + ' a ' + CONVERT(VARCHAR(10), @FechaFin, 120);

    --------------------------------------------------------------------
    -- 1) Cabecera: OfertasHistoricos -> Ofertas
    --------------------------------------------------------------------
    INSERT INTO Ofertas
        (OfeID, CliID, CanID, TinID, LinID, CatID, ProID, OfeCantidadMax, OfeIndicadorGrupo,
         GrpCodigo, GrcCodigo, OfeDescripcion, OfeFechainicio, OfeFechaFin, OfeTipo, ProNoVendido,
         UsuInicioSesion, OfeFechaActualizacion, rowguid, ConID, OfeIndicadorRebajaVenta,
         grpCodigoOferta, OfeNivel, OfeFuente, OfeTipoMezcla, OfeCantidadMezcla,
         OfeCantidadMaximaTransaccion, OfeCaracteristicas, UnmCodigo, OfeTipoPromocion,
         OfeMontoFinal, OfeMontoInicial)
    SELECT
         @NuevoOfeID, h.CliID, h.CanID, h.TinID, h.LinID, h.CatID, h.ProID, h.OfeCantidadMax, h.OfeIndicadorGrupo,
         h.GrpCodigo, h.GrcCodigo, ISNULL(@Descripcion, h.OfeDescripcion), @FechaInicio, @FechaFin, h.OfeTipo, h.ProNoVendido,
         h.UsuInicioSesion, GETDATE(), NEWID(), h.ConID, h.OfeIndicadorRebajaVenta,
         h.grpCodigoOferta, h.OfeNivel, h.OfeFuente, h.OfeTipoMezcla, h.OfeCantidadMezcla,
         h.OfeCantidadMaximaTransaccion, h.OfeCaracteristicas, h.UnmCodigo, h.OfeTipoPromocion,
         NULL, NULL
    FROM OfertasHistoricos h
    WHERE h.OfeID = @OfeIDOrigen;
    DECLARE @nCab INT = @@ROWCOUNT;

    --------------------------------------------------------------------
    -- 2) Detalle: OfertasDetalleHistoricos -> OfertasDetalle
    --------------------------------------------------------------------
    INSERT INTO OfertasDetalle
        (OfeID, OfeSecuencia, ProID, OfeCantidad, OfeCantidadOferta, OfePrecio, OfeCantidadDetalle,
         OfeCantidadDetalleOferta, OfePorciento, rowguid, UsuInicioSesion, OfeFechaActualizacion,
         UnmCodigo, OfeTransporte, OfeRango)
    SELECT
         @NuevoOfeID, d.OfeSecuencia, d.ProID, d.OfeCantidad, d.OfeCantidadOferta, d.OfePrecio, d.OfeCantidadDetalle,
         d.OfeCantidadDetalleOferta, d.OfePorciento, NEWID(), d.UsuInicioSesion, GETDATE(),
         d.UnmCodigo, NULL, NULL
    FROM OfertasDetalleHistoricos d
    WHERE d.OfeID = @OfeIDOrigen;
    DECLARE @nDet INT = @@ROWCOUNT;

    --------------------------------------------------------------------
    -- 3) Tipos de negocio: OfertasDetalleTiposNegocioHistoricos -> OfertasDetalleTiposNegocio
    --------------------------------------------------------------------
    INSERT INTO OfertasDetalleTiposNegocio (OfeID, TinID, rowguid, UsuInicioSesion, OfeFechaActualizacion)
    SELECT @NuevoOfeID, t.TinID, NEWID(), t.UsuInicioSesion, GETDATE()
    FROM OfertasDetalleTiposNegocioHistoricos t
    WHERE t.OfeID = @OfeIDOrigen;
    DECLARE @nTin INT = @@ROWCOUNT;

    --------------------------------------------------------------------
    -- 4) Representantes: OfertasRepresentantesHistoricos -> OfertasRepresentantes
    --------------------------------------------------------------------
    INSERT INTO OfertasRepresentantes (OfeID, RepCodigo, UsuInicioSesion, OfeFechaActualizacion, rowguid)
    SELECT @NuevoOfeID, r.RepCodigo, r.UsuInicioSesion, GETDATE(), NEWID()
    FROM OfertasRepresentantesHistoricos r
    WHERE r.OfeID = @OfeIDOrigen
      AND (@SoloRepsActivos = 0
           OR EXISTS (SELECT 1 FROM representantes rp WHERE rp.RepCodigo = r.RepCodigo AND rp.RepEstatus = 1));
    DECLARE @nRep INT = @@ROWCOUNT;

    --------------------------------------------------------------------
    -- 5) Clientes con excepcion: OfertasClientesExcepcionesHistoricos -> OfertasClientesExcepciones
    --------------------------------------------------------------------
    INSERT INTO OfertasClientesExcepciones (CliID, OfeFechaActualizacion, OfeID, rowguid, UsuInicioSesion)
    SELECT x.CliID, GETDATE(), @NuevoOfeID, NEWID(), x.UsuInicioSesion
    FROM OfertasClientesExcepcionesHistoricos x
    WHERE x.OfeID = @OfeIDOrigen;
    DECLARE @nExc INT = @@ROWCOUNT;

    --------------------------------------------------------------------
    -- 6) Contador de secuencias: dejar el maximo usado
    --------------------------------------------------------------------
    UPDATE Secuencias
       SET SecSecuencia = @NuevoOfeID, SecFechaActualizacion = GETDATE()
     WHERE SecTabla = 'Ofertas' AND SecSecuencia < @NuevoOfeID;

    --------------------------------------------------------------------
    -- VERIFICACION: origen vs. clon
    --------------------------------------------------------------------
    SELECT 'Cabecera (Ofertas)'              AS Tabla,
           (SELECT COUNT(*) FROM OfertasHistoricos                    WHERE OfeID = @OfeIDOrigen) AS Origen,
           @nCab AS Clonadas
    UNION ALL SELECT 'Detalle (OfertasDetalle)',
           (SELECT COUNT(*) FROM OfertasDetalleHistoricos             WHERE OfeID = @OfeIDOrigen), @nDet
    UNION ALL SELECT 'Tipos de negocio',
           (SELECT COUNT(*) FROM OfertasDetalleTiposNegocioHistoricos WHERE OfeID = @OfeIDOrigen), @nTin
    UNION ALL SELECT 'Representantes',
           (SELECT COUNT(*) FROM OfertasRepresentantesHistoricos      WHERE OfeID = @OfeIDOrigen), @nRep
    UNION ALL SELECT 'Clientes con excepcion',
           (SELECT COUNT(*) FROM OfertasClientesExcepcionesHistoricos WHERE OfeID = @OfeIDOrigen), @nExc;

    SELECT OfeID, OfeDescripcion, OfeFechainicio, OfeFechaFin, OfeTipo, GrpCodigo, grpCodigoOferta, OfeFuente
    FROM Ofertas WHERE OfeID = @NuevoOfeID;

    -- Representantes copiados que hoy estan inactivos o ya no existen (informativo)
    SELECT o.RepCodigo, rp.RepNombre, rp.RepEstatus
    FROM OfertasRepresentantes o
    LEFT JOIN representantes rp ON rp.RepCodigo = o.RepCodigo
    WHERE o.OfeID = @NuevoOfeID AND (rp.RepCodigo IS NULL OR rp.RepEstatus <> 1);

    IF @Confirmar = 1
    BEGIN
        COMMIT TRAN;
        PRINT 'OK: oferta clonada y confirmada. Nuevo OfeID = ' + CAST(@NuevoOfeID AS VARCHAR(20));
    END
    ELSE
    BEGIN
        ROLLBACK TRAN;
        PRINT 'SIMULACION: se hizo ROLLBACK, no se guardo nada. Ponga @Confirmar = 1 para guardar.';
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    DECLARE @msg NVARCHAR(2000) = ERROR_MESSAGE();
    RAISERROR('Error clonando la oferta (se hizo ROLLBACK): %s', 16, 1, @msg);
END CATCH
