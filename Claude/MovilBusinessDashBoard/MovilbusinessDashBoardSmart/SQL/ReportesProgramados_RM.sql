/* ============================================================================
   ReportesProgramados_RM  +  ReportesProgramadosDestinatarios_RM
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Generaliza el envío programado por correo — pedido explícito del usuario
   2026-09-03, tras probar la primera versión (que solo mandaba "Cierre Diario"
   a las 6pm fijo, con filtros por defecto): "sugiero que esta herramienta la
   pongamos en cada base de los reportes, pero que tengamos un entorno de cuál
   reporte queremos que se envíe y a qué hora... debemos tener un botón en el
   cual cuando genere los filtros de un reporte pueda decirle guardar para
   enviar... y decidir cuál reporte le envío a quién".

   Reemplaza el diseño anterior (columnas ReporteCierreDiario/Semanal/Mensual
   en CierreDiarioDestinatarios_RM, que asumían "3 casillas fijas") por N
   programaciones, cada una con SU PROPIO reporte+filtros+hora+destinatarios.
   CierreDiarioDestinatarios_RM NO se toca (sigue siendo la libreta de
   direcciones general — Nombre/Email/Teléfono/Activo); sus 3 columnas de
   reporte quedan sin uso desde ahora, superadas por la tabla de abajo.

   Tablas:
     1) ReportesProgramados_RM      -> una fila por combinación reporte+filtros
        guardada, con su hora de envío y el resultado del último intento
        (visible en pantalla, para no depender de logs del servidor).
     2) ReportesProgramadosDestinatarios_RM -> a quién de la libreta le llega
        CADA programación (N a N).

   Usuario "Ventas" es solo lectura sobre la BD; al final se otorgan permisos DML
   solo sobre estas tablas _RM. Ejecutar una sola vez (re-ejecutable).
   ============================================================================ */

/* ============================================================== *
 * 1) PROGRAMACIONES: reporte + filtros + hora + último resultado *
 * ============================================================== */
IF OBJECT_ID('dbo.ReportesProgramados_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.ReportesProgramados_RM
    (
        ProgID           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReportesProg_RM PRIMARY KEY,
        Reporte          varchar(60)   NOT NULL,   -- 'cierres_cierre_diario' | 'gobierno_comercial_ventas_proyectadas' | futuros
        Nombre           varchar(150)  NOT NULL,   -- nombre libre que el usuario le pone a ESTA combinación
        FiltrosJSON      nvarchar(max) NULL,        -- suplidor/categoria/centro/canal/supervisor/division/vendedor (SIN fechas — se recalculan siempre al momento del envío)
        HoraEnvio        time          NOT NULL,   -- configurable, ya no fija a las 18:00
        Activo           bit           NOT NULL CONSTRAINT DF_ReportesProg_Act DEFAULT(1),
        UltimoEnvio      datetime      NULL,        -- último envío EXITOSO (evita reenviar 2 veces el mismo día)
        UltimoError      nvarchar(500) NULL,        -- motivo del último intento fallido (visible en pantalla, NULL si el último intento fue exitoso)
        UsuInicioSesion  varchar(64)   NULL,
        FechaActualizacion datetime    NOT NULL CONSTRAINT DF_ReportesProg_Fec DEFAULT(GETDATE())
    );
    CREATE INDEX IX_ReportesProg_Activo ON dbo.ReportesProgramados_RM(Activo, HoraEnvio);
END
GO

/* ============================================================== *
 * 2) A QUIÉN le llega cada programación (N a N con la libreta)   *
 * ============================================================== */
IF OBJECT_ID('dbo.ReportesProgramadosDestinatarios_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.ReportesProgramadosDestinatarios_RM
    (
        ProgID  int NOT NULL,   -- -> ReportesProgramados_RM.ProgID
        DesID   int NOT NULL,   -- -> CierreDiarioDestinatarios_RM.DesID (libreta general)
        CONSTRAINT PK_ReportesProgDest_RM PRIMARY KEY (ProgID, DesID)
    );
END
GO

/* ============================================================== *
 * 3) PERMISOS para la app (usuario "Ventas")                     *
 * ============================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ReportesProgramados_RM             TO [Ventas];
GRANT SELECT, INSERT, DELETE         ON dbo.ReportesProgramadosDestinatarios_RM TO [Ventas];
GO

/* Revision:
   SELECT * FROM dbo.ReportesProgramados_RM ORDER BY HoraEnvio;
   SELECT p.ProgID, p.Nombre, d.DesID, dd.Nombre AS Destinatario, dd.Email
   FROM dbo.ReportesProgramados_RM p
   JOIN dbo.ReportesProgramadosDestinatarios_RM d ON d.ProgID=p.ProgID
   JOIN dbo.CierreDiarioDestinatarios_RM dd ON dd.DesID=d.DesID;
*/
