/* ============================================================================
   TORRE DE CONTROL  -  Motor de monitores (3 tablas)
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Diseño EXTENSIBLE: la Torre no vigila 7 indicadores fijos, sino una lista de
   "monitores" que se pueden activar/desactivar y a la que se pueden agregar
   seguimientos puntuales (un producto, una oferta, un descuento, etc.) sin
   cambiar el esquema.

     1) TorreControlMonitores_RM  -> catalogo de monitores (nucleo + configurables).
     2) TorreControlSnapshots_RM  -> foto GENERICA cada 30 min (8:20-16:00):
        una fila por (corte, vendedor, monitor) con Valor / EnAlarma / Detalle.
     3) TorreControlContactos_RM  -> bitacora de contactos (llamadas de la Torre).

   Usuario de la app ("Ventas") es de solo lectura; al final se otorgan permisos.
   Ejecutar una sola vez. Sufijo _RM = tablas de uso propio.
   ============================================================================ */

/* ================================================================== *
 * 1) CATALOGO DE MONITORES                                           *
 * ================================================================== */
IF OBJECT_ID('dbo.TorreControlMonitores_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.TorreControlMonitores_RM
    (
        MonID          int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TCMon_RM PRIMARY KEY,
        Codigo         varchar(40)  NOT NULL,     -- clave estable (p.ej. 'FUERA_ENTORNO' o 'PROD_12345')
        Tipo           varchar(20)  NOT NULL,     -- nucleo | producto | oferta | descuento | presupuesto | proyeccion
        Nombre         varchar(150) NOT NULL,
        Descripcion    varchar(400) NULL,
        Activo         bit          NOT NULL CONSTRAINT DF_TCM_Act DEFAULT(1),
        Ambito         varchar(20)  NOT NULL CONSTRAINT DF_TCM_Amb DEFAULT('vendedor'), -- vendedor | suplidor | categoria
        ParametrosJSON varchar(1000) NULL,        -- p.ej. {"proId":12345} / {"ofertaId":88} / {"minMin":3,"maxMin":40}
        Umbral         decimal(18,2) NULL,        -- umbral generico (min / km / % / monto, segun el monitor)
        Direccion      varchar(10)  NULL,         -- falta | exceso | menor | mayor (para plantillas)
        Severidad      int          NOT NULL CONSTRAINT DF_TCM_Sev DEFAULT(1),
        Orden          int          NOT NULL CONSTRAINT DF_TCM_Ord DEFAULT(0),
        VigenciaDesde  date         NULL,         -- para seguimientos puntuales (NULL = siempre)
        VigenciaHasta  date         NULL,
        Color          varchar(10)  NULL,
        UsuInicioSesion varchar(64) NULL,
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_TCM_Fec DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_TCM_Codigo ON dbo.TorreControlMonitores_RM(Codigo);
END
GO

/* --- Sembrar los 7 monitores NUCLEO (umbrales por defecto, editables) --- */
IF NOT EXISTS (SELECT 1 FROM dbo.TorreControlMonitores_RM WHERE Tipo='nucleo')
INSERT INTO dbo.TorreControlMonitores_RM (Codigo,Tipo,Nombre,Descripcion,Umbral,Direccion,Severidad,Orden,ParametrosJSON) VALUES
 ('PRIMERA_VISITA_PEDIDO','nucleo','1ra visita y 1er pedido antes de 8:20','Hora del primer registro/pedido del dia. Alarma si es mas tarde del umbral.',500,'mayor',3,1,'{"minutosLimite":500}'),   -- 08:20 = 500 min
 ('TIEMPO_VISITA','nucleo','Tiempo promedio de visita','Duracion promedio de la visita. Alarma si es muy corta o muy larga.',NULL,NULL,1,2,'{"minMin":3,"maxMin":40}'),
 ('CUMPLIMIENTO','nucleo','Cumplimiento de visitas','Visitados vs los que deberia llevar a esta hora (ritmo de la jornada). Alarma bajo el umbral (%).',85,'menor',2,2,NULL),
 ('EFECTIVIDAD_VISITA','nucleo','Efectividad de la visita','Visitas efectivas (con pedido) / clientes visitados. Alarma bajo el umbral (%).',55,'menor',2,3,NULL),
 ('MUCHO_SIN_SYNC','nucleo','Mucho tiempo sin sincronizar','Alarma si los minutos sin sincronizar superan el umbral (min).',45,'mayor',3,5,'{"minutos":45}'),
 ('FUERA_ENTORNO','nucleo','Fuera de su entorno de trabajo','Km del GPS actual al cliente de ruta mas cercano. Alarma sobre el umbral (km).',3,'mayor',2,6,'{"km":3}'),
 ('PEDIDO_FUERA_DIA','nucleo','Pedidos fuera de su dia de visita','Cantidad de pedidos a clientes no programados hoy. Alarma si >= umbral.',1,'mayor',2,7,'{"cantidad":1}'),
 ('CUADRE_CERRADO','nucleo','Cuadre sin cerrar','Alarma si al terminar la jornada el vendedor no cerro su cuadre del dia (obligatorio).',NULL,NULL,2,8,NULL),
 ('ULTIMO_PEDIDO_TEMPRANO','nucleo','Ultimo pedido muy temprano','Cuando el vendedor termino sus visitas o cerro el cuadre, alarma si su ultimo pedido fue antes del umbral (min desde 00:00; 780=1:00 PM).',780,'menor',2,9,'{"minutosLimite":780}');
GO

/* ================================================================== *
 * 2) SNAPSHOTS GENERICOS (cada 30 min, 8:20-16:00)                   *
 *    Una fila por (corte, vendedor, monitor).                       *
 * ================================================================== */
IF OBJECT_ID('dbo.TorreControlSnapshots_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.TorreControlSnapshots_RM
    (
        SnapID           bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TCSnap_RM PRIMARY KEY,
        FechaCorte       datetime      NOT NULL,   -- momento del snapshot
        MonID            int           NOT NULL,   -- monitor (-> TorreControlMonitores_RM)
        Codigo           varchar(40)   NOT NULL,   -- codigo del monitor (denormalizado)
        RepCodigo        varchar(20)   NOT NULL,
        RepNombre        varchar(150)  NULL,
        SupCod           varchar(20)   NULL,
        SupervisorNombre varchar(150)  NULL,
        Centro           varchar(150)  NULL,
        Division         varchar(10)   NULL,
        Valor            decimal(18,2) NULL,        -- valor numerico del indicador
        ValorTexto       varchar(100)  NULL,        -- valor no numerico (p.ej. '08:47')
        EnAlarma         bit           NOT NULL CONSTRAINT DF_TCS_Al DEFAULT(0),
        Severidad        int           NOT NULL CONSTRAINT DF_TCS_Sev DEFAULT(0),
        Detalle          varchar(400)  NULL,        -- texto legible ('a 55 km de su ruta')
        FechaRegistro    datetime      NOT NULL CONSTRAINT DF_TCS_Reg DEFAULT(GETDATE())
    );
    CREATE INDEX IX_TCS_Corte  ON dbo.TorreControlSnapshots_RM(FechaCorte);
    CREATE INDEX IX_TCS_RepDia ON dbo.TorreControlSnapshots_RM(RepCodigo, FechaCorte);
    CREATE INDEX IX_TCS_Mon    ON dbo.TorreControlSnapshots_RM(MonID, FechaCorte);
END
GO

/* ================================================================== *
 * 3) BITACORA DE CONTACTOS                                           *
 * ================================================================== */
IF OBJECT_ID('dbo.TorreControlContactos_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.TorreControlContactos_RM
    (
        ConID            int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TCCon_RM PRIMARY KEY,
        FechaContacto    datetime      NOT NULL CONSTRAINT DF_TCC_Fecha DEFAULT(GETDATE()), -- hora de la llamada
        UsuarioTorre     varchar(64)   NULL,        -- quien registra (recurso Torre de Control)
        RepCodigo        varchar(20)   NOT NULL,
        RepNombre        varchar(150)  NULL,
        SupCod           varchar(20)   NULL,
        SupervisorNombre varchar(150)  NULL,
        Centro           varchar(150)  NULL,
        Alertas          varchar(400)  NULL,         -- monitores/alarmas que motivaron el contacto
        Canal            varchar(20)   NULL,         -- Llamada | WhatsApp | Presencial
        ContactoCon      varchar(20)   NULL,         -- Vendedor | Supervisor | Ambos
        Resumen          varchar(1000) NULL,         -- puntos tratados
        Compromiso       varchar(1000) NULL,         -- acuerdo adquirido
        FechaSeguimiento date          NULL,
        Estado           varchar(20)   NOT NULL CONSTRAINT DF_TCC_Estado DEFAULT('Abierto'), -- Abierto | Seguimiento | Resuelto
        Indicadores      varchar(600)  NULL,         -- foto de valores al momento (JSON/opcional)
        FechaRegistro    datetime      NOT NULL CONSTRAINT DF_TCC_Reg DEFAULT(GETDATE())
    );
    CREATE INDEX IX_TCC_RepFecha ON dbo.TorreControlContactos_RM(RepCodigo, FechaContacto);
    CREATE INDEX IX_TCC_Fecha    ON dbo.TorreControlContactos_RM(FechaContacto);
END
GO

/* ================================================================== *
 * 4) PERMISOS para la app (usuario "Ventas")                        *
 * ================================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TorreControlMonitores_RM TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TorreControlSnapshots_RM TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TorreControlContactos_RM TO [Ventas];
GO

/* Revision:
   SELECT * FROM dbo.TorreControlMonitores_RM ORDER BY Orden;
   SELECT TOP 50 * FROM dbo.TorreControlSnapshots_RM ORDER BY FechaCorte DESC;
   SELECT TOP 50 * FROM dbo.TorreControlContactos_RM ORDER BY FechaContacto DESC;
*/
