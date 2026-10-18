/* ============================================================================
   ReportesProgramados_RM — HoraEnvio: time -> varchar(200) (varias horas por día)
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Pedido explícito del usuario 2026-09-03: un mismo reporte (mismo filtro y
   mismos destinatarios) a veces necesita enviarse VARIAS veces al día (ej.
   avances 10:00/12:00/3:00 PM + cierre 5:15 PM) — antes cada programación solo
   admitía UNA hora (columna `time`). Ahora `HoraEnvio` guarda una lista de
   horas separadas por coma en un solo campo de texto (ej. "10:00,12:00,15:00,
   17:15"), validada/normalizada por el backend al guardar (ver
   ApiPdfPresController.GuardarProgramado). El scheduler
   (ReportesProgramadosSchedulerService) ahora compara cada hora de la lista
   contra la hora actual en C#, no en SQL — por eso el índice viejo
   (Activo, HoraEnvio) ya no sirve para ese filtro y se reemplaza por uno solo
   sobre Activo.

   La tabla `ReportesProgramados_RM` está VACÍA en este momento (confirmado
   antes de escribir este script — nadie ha guardado ninguna programación
   todavía) — no hay filas que migrar, es un cambio de esquema sin riesgo de
   pérdida de datos. Usuario "Ventas" NO tiene permiso ALTER — requiere que TI
   lo ejecute. Reejecutable (verifica el tipo de columna antes de tocar nada).
   ============================================================================ */

IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='ReportesProgramados_RM' AND COLUMN_NAME='HoraEnvio' AND DATA_TYPE='time'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ReportesProg_Activo' AND object_id=OBJECT_ID('dbo.ReportesProgramados_RM'))
        DROP INDEX IX_ReportesProg_Activo ON dbo.ReportesProgramados_RM;

    ALTER TABLE dbo.ReportesProgramados_RM ALTER COLUMN HoraEnvio varchar(200) NOT NULL;

    CREATE INDEX IX_ReportesProg_Activo ON dbo.ReportesProgramados_RM(Activo);
END
GO

/* Revision:
   SELECT ProgID, Nombre, HoraEnvio, Activo FROM dbo.ReportesProgramados_RM;
   SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_NAME='ReportesProgramados_RM' AND COLUMN_NAME='HoraEnvio'; -- debe salir varchar, 200
*/
