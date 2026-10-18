/* ============================================================================
   SUPERVISION DINAMICA — "Cobros del dia" : ASIGNACION de alertas a vendedores
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Tabla de "cotejar / descotejar" (checklist) por alerta x vendedor.

   POR QUE existe:
     Hoy cada alerta (SupervisionCobrosConfig_RM) alcanza a los vendedores por
     Division (RepresentantesAdicionales.RepTipe) + ExcluirSupCods. Eso es un
     filtro GRUESO. El usuario necesita, ademas, un MANTENIMIENTO fino donde
     pueda cotejar/descotejar CUALQUIER vendedor para CUALQUIER alerta.

   MODELO (autoridad por-alerta):
     - Una alerta que NUNCA ha pasado por el mantenimiento => NO tiene filas aqui
       => sigue con la logica actual (Division + ExcluirSupCods). Retro-compatible.
     - En cuanto se guarda el mantenimiento de una alerta, se escribe UNA fila por
       cada vendedor del universo (divisiones 10 y 11) con Asignado=1/0. A partir
       de ahi esta tabla es la AUTORIDAD: la alerta solo evalua los Asignado=1.
     - Al crear una alerta nueva, la app puede sembrar aqui todo el universo
       (div 10 y 11) con Asignado=1 (auto-asignada a todos), tal como pidio el
       usuario; luego se cotejan/descotejan.

   Usuario "Ventas" es read-only sobre la BD; al final se otorgan permisos DML
   solo sobre esta tabla _RM. Ejecutar una sola vez (re-ejecutable).
   ============================================================================ */

IF OBJECT_ID('dbo.SupervisionCobrosAsignacion_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionCobrosAsignacion_RM
    (
        CfgID           int          NOT NULL,   -- FK logica a SupervisionCobrosConfig_RM.CfgID
        RepCodigo       varchar(20)  NOT NULL,   -- Representantes.RepCodigo
        Asignado        bit          NOT NULL CONSTRAINT DF_SupCobrosAsig_Asig DEFAULT(1),
        UsuInicioSesion varchar(64)  NULL,
        FechaActualizacion datetime  NOT NULL CONSTRAINT DF_SupCobrosAsig_Fec DEFAULT(GETDATE()),
        CONSTRAINT PK_SupCobrosAsig_RM PRIMARY KEY (CfgID, RepCodigo)
    );
    CREATE INDEX IX_SupCobrosAsig_Cfg ON dbo.SupervisionCobrosAsignacion_RM(CfgID, Asignado);
END
GO

/* ============================================================== *
 *  PERMISOS para la app (usuario "Ventas")                       *
 * ============================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionCobrosAsignacion_RM TO [Ventas];
GO

/* Revision:
   -- vendedores cotejados de una alerta:
   SELECT a.RepCodigo, r.RepNombre, a.Asignado
   FROM dbo.SupervisionCobrosAsignacion_RM a
   JOIN Representantes r ON r.RepCodigo=a.RepCodigo
   WHERE a.CfgID = 1
   ORDER BY a.Asignado DESC, r.RepNombre;

   -- cuantas alertas tienen mantenimiento (autoridad) ya guardado:
   SELECT CfgID, COUNT(*) filas, SUM(CAST(Asignado AS int)) cotejados
   FROM dbo.SupervisionCobrosAsignacion_RM GROUP BY CfgID;
*/
