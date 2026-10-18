/* ============================================================================
   CierreDiarioDestinatarios_RM
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Lista de personas que reciben por correo (y a futuro, WhatsApp) los reportes
   de "Cierres" (Diario/Semanal/Mensual) de Supervisión Dinámica -> PDF
   Presentaciones. Pedido explícito del usuario 2026-09-03: "cualquier [reporte]
   que se configure en la parte principal debería poder enviarse de manera
   programada... a los representantes que se designen vía correo".

   No se reutiliza Representantes (ese catálogo no tiene columna de correo, solo
   RepTelefono1/2) — esta es una lista de mantenimiento propia, no ligada 1:1 a
   un vendedor (puede incluir gerentes, supervisores o cualquier destinatario).

   Un flag Booleano por TIPO de reporte (ReporteCierreDiario/Semanal/Mensual) en
   vez de una tabla de relación aparte: simple de mantener a mano, y ya cubre a
   futuro Semanal/Mensual sin cambiar el esquema cuando se construyan.

   Usuario "Ventas" es solo lectura sobre la BD; al final se otorgan permisos DML
   solo sobre esta tabla _RM. Ejecutar una sola vez (re-ejecutable).
   ============================================================================ */

IF OBJECT_ID('dbo.CierreDiarioDestinatarios_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.CierreDiarioDestinatarios_RM
    (
        DesID                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CierreDiarioDest_RM PRIMARY KEY,
        Nombre                varchar(150) NOT NULL,
        Email                 varchar(200) NOT NULL,
        Telefono              varchar(20)  NULL,        -- para WhatsApp a futuro (ver appsettings "WhatsApp")
        Activo                bit          NOT NULL CONSTRAINT DF_CierreDiarioDest_Act  DEFAULT(1),
        ReporteCierreDiario   bit          NOT NULL CONSTRAINT DF_CierreDiarioDest_Dia  DEFAULT(1),
        ReporteCierreSemanal  bit          NOT NULL CONSTRAINT DF_CierreDiarioDest_Sem  DEFAULT(0),
        ReporteCierreMensual  bit          NOT NULL CONSTRAINT DF_CierreDiarioDest_Mes  DEFAULT(0),
        UsuInicioSesion       varchar(64)  NULL,
        FechaActualizacion    datetime     NOT NULL CONSTRAINT DF_CierreDiarioDest_Fec  DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_CierreDiarioDest_Email ON dbo.CierreDiarioDestinatarios_RM(Email);
END
GO

/* ============================================================== *
 *  PERMISOS para la app (usuario "Ventas")                       *
 * ============================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CierreDiarioDestinatarios_RM TO [Ventas];
GO

/* Revision:
   SELECT * FROM dbo.CierreDiarioDestinatarios_RM WHERE Activo=1 AND ReporteCierreDiario=1;
*/
