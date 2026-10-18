/* ============================================================================
   SUPERVISION DINAMICA — Cronograma semanal + prioridad  (tablas _RM)
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Complementa a Supervision_RM.sql. Guarda, por representante, el TIPO de
   supervision sugerido por dia de la semana (Lun..Sab) y si es PRIORITARIO.

   Modelo confirmado (ago-2026):
     - Cadencia ideal por vendedor/semana: 2 Presencial + 1 Auditoria + 3 Virtual.
     - El plan es el IDEAL por vendedor; en el dia, la PRIORIDAD manda (a los
       prioritarios / peor IEGT se les acompana presencial; al resto, virtual).
     - Config manual (rejilla rep x dia) + sugerencia automatica por IEGT/alertas.
     - Prioridad = Si/No por vendedor.

   Si un rep no tiene filas en Cronograma, la app usa un default en codigo
   (Lun=Virtual, Mar=Presencial, Mie=Virtual, Jue=Presencial, Vie=Auditoria,
    Sab=Virtual). Estas tablas guardan solo los ajustes.

   Usuario "Ventas" es read-only sobre la BD; al final se otorgan permisos DML
   solo sobre estas tablas _RM. Ejecutar una sola vez (re-ejecutable).
   ============================================================================ */

/* ============================================================== *
 * 1) CRONOGRAMA: tipo de supervision por (rep, dia de semana)    *
 *    DiaSemana: 1=Lunes ... 6=Sabado                             *
 *    Tipo: presencial | auditoria | virtual                     *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionCronograma_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionCronograma_RM
    (
        CroID     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupCro_RM PRIMARY KEY,
        RepCodigo varchar(20) NOT NULL,
        DiaSemana tinyint      NOT NULL,   -- 1..6 (Lun..Sab)
        Tipo      varchar(12)  NOT NULL,   -- presencial | auditoria | virtual
        SupCod    varchar(20)  NULL,       -- supervisor (referencia)
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupCro_Fec DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_SupCro_RepDia ON dbo.SupervisionCronograma_RM(RepCodigo, DiaSemana);
END
GO

/* ============================================================== *
 * 2) CONFIG POR REPRESENTANTE: prioridad (Si/No)                 *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionRepConfig_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionRepConfig_RM
    (
        RepCodigo   varchar(20) NOT NULL CONSTRAINT PK_SupRepCfg_RM PRIMARY KEY,
        Prioritario bit         NOT NULL CONSTRAINT DF_SupRepCfg_Pri DEFAULT(0),
        SupCod      varchar(20) NULL,
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupRepCfg_Fec DEFAULT(GETDATE())
    );
END
GO

/* ============================================================== *
 * 3) DIAS POR SUPERVISOR: patron semanal (Lun..Sab) por sup.    *
 *    Es el DEFAULT del equipo (para no configurar rep por rep). *
 *    Default base (si el sup no lo define): Lun=V, Mar=P, Mie=P, *
 *    Jue=Auditoria, Vie=V, Sab=V.                               *
 * ============================================================== */
IF OBJECT_ID('dbo.SupervisionSupDias_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionSupDias_RM
    (
        SupCod    varchar(20) NOT NULL,
        DiaSemana tinyint     NOT NULL,   -- 1..6 (Lun..Sab)
        Tipo      varchar(12) NOT NULL,   -- presencial | auditoria | virtual
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupDias_Fec DEFAULT(GETDATE()),
        CONSTRAINT PK_SupDias_RM PRIMARY KEY (SupCod, DiaSemana)
    );
END
GO

/* ============================================================== *
 * 4) PERMISOS para la app (usuario "Ventas")                    *
 * ============================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionCronograma_RM TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionRepConfig_RM  TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionSupDias_RM    TO [Ventas];
GO

/* Revision:
   SELECT * FROM dbo.SupervisionCronograma_RM ORDER BY RepCodigo, DiaSemana;
   SELECT * FROM dbo.SupervisionRepConfig_RM WHERE Prioritario=1;
*/
