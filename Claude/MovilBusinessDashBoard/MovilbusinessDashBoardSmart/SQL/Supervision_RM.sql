/* ============================================================================
   SUPERVISION DINAMICA  -  Tablas de uso propio (_RM)
   MovilBusiness Dashboard - AM COMERCIAL
   ----------------------------------------------------------------------------
   Modulo para que los SUPERVISORES evaluen a sus vendedores y la GERENCIA
   tenga historial. Tres esquemas que forman un Indice Global:
       IEGT 50%  +  Calle Acompanamiento 35%  +  Calle Auditoria 15%

   Tablas:
     1) SupervisionEsquemas_RM      -> peso de los 3 esquemas (editable).
     2) SupervisionFactores_RM      -> factores de cada esquema de calle + peso/escala (editable).
     3) SupervisionUsuarioMapa_RM   -> usuario de login -> supervisor + rol (Supervisor/Gerencia).
     4) SupervisionEvaluaciones_RM  -> cabecera de cada evaluacion de CALLE (acomp/audit) + firmas.
     5) SupervisionDetalle_RM       -> puntaje por (evaluacion, cliente, factor).
     6) SupervisionFotos_RM         -> fotos (evidencia) por evaluacion/cliente.
     7) SupervisionPlan_RM          -> rejilla semanal (rep, dia, modo calle/virtual). Opcional.

   Los contactos de la Supervision VIRTUAL reutilizan TorreControlContactos_RM
   (ver TorreControl_RM.sql) y las alertas salen de TorreControlSnapshots_RM.

   El usuario de la app ("Ventas") es de solo lectura sobre la BD; al final se
   otorgan permisos DML solo sobre estas tablas _RM. Ejecutar una sola vez.
   ============================================================================ */

/* ================================================================== *
 * 1) PESO DE LOS ESQUEMAS  (deben sumar 100)                         *
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionEsquemas_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionEsquemas_RM
    (
        EsqID   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupEsq_RM PRIMARY KEY,
        Codigo  varchar(20)  NOT NULL,   -- iegt | acomp | audit
        Nombre  varchar(100) NOT NULL,
        Peso    decimal(9,2) NOT NULL,   -- % (los 3 deben sumar 100)
        Orden   int          NOT NULL CONSTRAINT DF_SupEsq_Ord DEFAULT(0),
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupEsq_Fec DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_SupEsq_Cod ON dbo.SupervisionEsquemas_RM(Codigo);
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SupervisionEsquemas_RM)
INSERT INTO dbo.SupervisionEsquemas_RM (Codigo,Nombre,Peso,Orden) VALUES
 ('iegt' ,'IEGT (Supervision Virtual)'   ,50,1),
 ('acomp','Calle - Acompanamiento'       ,35,2),
 ('audit','Calle - Auditoria'            ,15,3);
GO

/* ================================================================== *
 * 2) FACTORES DE CADA ESQUEMA DE CALLE  (cada esquema suma 100)      *
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionFactores_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionFactores_RM
    (
        FacID   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupFac_RM PRIMARY KEY,
        Esquema varchar(20)  NOT NULL,   -- acomp | audit
        Codigo  varchar(40)  NOT NULL,   -- clave estable del factor
        Nombre  varchar(160) NOT NULL,
        Grupo   varchar(80)  NULL,       -- subtitulo (p.ej. '3. Identificacion de necesidades')
        Escala  varchar(10)  NOT NULL CONSTRAINT DF_SupFac_Esc DEFAULT('0-100'), -- 0-100 | 1-10 | sino
        Peso    decimal(9,2) NOT NULL,   -- % dentro del esquema
        Orden   int          NOT NULL CONSTRAINT DF_SupFac_Ord DEFAULT(0),
        Activo  bit          NOT NULL CONSTRAINT DF_SupFac_Act DEFAULT(1),
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupFac_Fec DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_SupFac_Cod ON dbo.SupervisionFactores_RM(Esquema, Codigo);
END
GO
/* --- Acompanamiento: 5 pasos oficiales (paso 3 dividido) + gestion. Suma 100 --- */
IF NOT EXISTS (SELECT 1 FROM dbo.SupervisionFactores_RM WHERE Esquema='acomp')
INSERT INTO dbo.SupervisionFactores_RM (Esquema,Codigo,Nombre,Grupo,Escala,Peso,Orden) VALUES
 ('acomp','PREPARACION' ,'1. Preparacion antes de la visita'   ,NULL,'0-100',12,1),
 ('acomp','SALUDO'      ,'2. Saludo y conexion'                ,NULL,'0-100',15,2),
 ('acomp','INVENTARIO'  ,'3.1 Revisa los inventarios'          ,'3. Identificacion de necesidades','0-100', 8,3),
 ('acomp','AFICHES'     ,'3.2 Coloca afiches / material POP'    ,'3. Identificacion de necesidades','0-100', 5,4),
 ('acomp','FOTO_EXITO'  ,'3.3 Realiza foto de exito'           ,'3. Identificacion de necesidades','0-100', 5,5),
 ('acomp','PRESENTACION','4. Presentacion de la oferta (Venta)',NULL,'0-100',22,6),
 ('acomp','CIERRE'      ,'5. Cierre del pedido'                ,NULL,'0-100',20,7),
 ('acomp','HERRAMIENTA' ,'Uso de la herramienta (app)'         ,NULL,'0-100', 8,8),
 ('acomp','DATOS_CLI'   ,'Datos del cliente actualizados'      ,NULL,'0-100', 5,9);
GO
/* --- Auditoria: servicio y gestion desde el cliente. Suma 100 --- */
IF NOT EXISTS (SELECT 1 FROM dbo.SupervisionFactores_RM WHERE Esquema='audit')
INSERT INTO dbo.SupervisionFactores_RM (Esquema,Codigo,Nombre,Grupo,Escala,Peso,Orden) VALUES
 ('audit','CONOCE_VEND','Conoce a su vendedor'          ,'Servicio al cliente'      ,'sino',10,1),
 ('audit','PUNTOS_VEND','Puntos que da al vendedor'     ,'Servicio al cliente'      ,'1-10',20,2),
 ('audit','ENTREGAS'   ,'Como considera las entregas'   ,'Servicio al cliente'      ,'1-10',15,3),
 ('audit','MANEJA_PROD','Maneja todos los productos'    ,'Gestion del vendedor (PDV)','1-10',20,4),
 ('audit','PRECIOS'    ,'Precios de venta aceptables'   ,'Gestion del vendedor (PDV)','1-10',15,5),
 ('audit','POP'        ,'Tiene material POP'            ,'Gestion del vendedor (PDV)','sino',10,6),
 ('audit','EXHIBIDOR'  ,'Tiene exhibidor'               ,'Gestion del vendedor (PDV)','sino',10,7);
GO

/* ================================================================== *
 * 3) MAPA usuario de login -> supervisor(es) + ROL                   *
 *    RELACION 1 -> N: un usuario puede manejar VARIOS codigos de     *
 *    supervisor (cada supervisor tiene varios grupos/zonas). Una     *
 *    fila por cada par (usuario, SupCod).                            *
 *    Rol 'Gerencia'   -> ve todo (una fila con SupCod NULL).         *
 *    Rol 'Supervisor' -> ve los reps con RepSupervisor IN (sus SupCod).*
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionUsuarioMapa_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionUsuarioMapa_RM
    (
        MapID           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupMap_RM PRIMARY KEY,
        UsuInicioSesion varchar(64)  NOT NULL,   -- = UsuarioSistema.UsuInicioSesion
        SupCod          varchar(20)  NULL,       -- codigo de supervisor (Representantes.RepSupervisor); NULL = Gerencia (ve todo)
        Nombre          varchar(150) NULL,       -- nombre de la persona (para mostrar)
        Rol             varchar(20)  NOT NULL CONSTRAINT DF_SupMap_Rol DEFAULT('Supervisor'), -- Supervisor | Gerencia
        Activo          bit          NOT NULL CONSTRAINT DF_SupMap_Act DEFAULT(1),
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupMap_Fec DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_SupMap_UsuSup ON dbo.SupervisionUsuarioMapa_RM(UsuInicioSesion, SupCod);
    CREATE INDEX IX_SupMap_Usu ON dbo.SupervisionUsuarioMapa_RM(UsuInicioSesion);
END
GO
/* --- Auto-correccion: si la tabla ya existia con el indice unico VIEJO
       (UX_SupMap_Usu sobre UsuInicioSesion solo), lo reemplaza por el correcto
       (compuesto UsuInicioSesion+SupCod) para permitir 1 usuario -> N codigos. --- */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_SupMap_Usu' AND object_id=OBJECT_ID('dbo.SupervisionUsuarioMapa_RM'))
    DROP INDEX UX_SupMap_Usu ON dbo.SupervisionUsuarioMapa_RM;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_SupMap_UsuSup' AND object_id=OBJECT_ID('dbo.SupervisionUsuarioMapa_RM'))
    CREATE UNIQUE INDEX UX_SupMap_UsuSup ON dbo.SupervisionUsuarioMapa_RM(UsuInicioSesion, SupCod);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SupMap_Usu' AND object_id=OBJECT_ID('dbo.SupervisionUsuarioMapa_RM'))
    CREATE INDEX IX_SupMap_Usu ON dbo.SupervisionUsuarioMapa_RM(UsuInicioSesion);
GO
/* --- REGLA DE ACCESO (la aplica la app en cada consulta):
       * Si el usuario tiene ALGUNA fila con Rol='Gerencia'  -> VE TODO (sin filtro).
       * En caso contrario                                    -> LIMITADO:
             ... WHERE r.RepSupervisor IN
                 (SELECT SupCod FROM dbo.SupervisionUsuarioMapa_RM
                  WHERE UsuInicioSesion=@user AND Activo=1 AND SupCod IS NOT NULL)
       Asi, "acceso a todo o limitado" lo controla el campo Rol; y "limitado a N
       supervisores" se logra con N filas (una por SupCod).
   --- SEMBRADO (jul-2026). La agrupacion de codigos por persona esta VERIFICADA en
       Representantes; el USUARIO de login lo confirma AM Comercial.
       << CONFIRMAR los 2 logins marcados y agregar el codigo #10 que falte >> --- */
IF NOT EXISTS (SELECT 1 FROM dbo.SupervisionUsuarioMapa_RM)
INSERT INTO dbo.SupervisionUsuarioMapa_RM (UsuInicioSesion,SupCod,Nombre,Rol) VALUES
 ('EMARTINEZ' ,'S5' ,'Eliezer Martinez'  ,'Supervisor'),   -- Eliezer Martinez: S5 (DN) + S2 (SDO)
 ('EMARTINEZ' ,'S2' ,'Eliezer Martinez'  ,'Supervisor'),
 ('Sup_VTA'   ,'S7' ,'Ramon E. Pimentel' ,'Supervisor'),   -- Ramon E. Pimentel: S7 (BC) + S1 (SDE) + S4 (SDN)  [CONFIRMAR login: Sup_VTA vs 240]
 ('Sup_VTA'   ,'S1' ,'Ramon E. Pimentel' ,'Supervisor'),
 ('Sup_VTA'   ,'S4' ,'Ramon E. Pimentel' ,'Supervisor'),
 ('NPINA'     ,'S9' ,'Narciso Pina Bello','Supervisor'),   -- Narciso Pina Bello: S9
 ('coordinador','S13','Cristiano Perez'  ,'Supervisor'),   -- Cristiano Perez: S13  [CONFIRMAR login: coordinador vs 237]
 ('rmercedes' ,NULL ,'Rudy Mercedes'     ,'Gerencia');     -- Gerencia = VE TODO (SupCod NULL)
-- ¿Falta un 10mo codigo de supervisor? Agregalo aqui, p.ej.:
--   INSERT INTO dbo.SupervisionUsuarioMapa_RM (UsuInicioSesion,SupCod,Nombre,Rol) VALUES ('<login>','<SupCod>','<Nombre>','Supervisor');
GO

/* ================================================================== *
 * 4) EVALUACIONES DE CALLE (cabecera)  -  acomp | audit              *
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionEvaluaciones_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionEvaluaciones_RM
    (
        EvaID            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupEva_RM PRIMARY KEY,
        Tipo             varchar(10)  NOT NULL,   -- acomp | audit
        UsuInicioSesion  varchar(64)  NULL,       -- supervisor que evalua (quien registra)
        SupCod           varchar(20)  NULL,
        SupervisorNombre varchar(150) NULL,
        RepCodigo        varchar(20)  NOT NULL,   -- vendedor evaluado
        RepNombre        varchar(150) NULL,
        Centro           varchar(150) NULL,
        Division         varchar(10)  NULL,
        Fecha            date         NOT NULL,
        Suplidores       varchar(400) NULL,       -- 'Todos' o lista de suplidores
        FechaAcceso      datetime     NULL,       -- cuando entro al modulo/cargo la evaluacion
        HoraInicio       datetime     NULL,
        HoraFin          datetime     NULL,
        ClientesEval     int          NOT NULL CONSTRAINT DF_SupEva_Cli DEFAULT(0),
        Indice           decimal(9,2) NULL,       -- 0-100 ponderado (consolidado)
        Banda            varchar(20)  NULL,       -- Solida|Aceptable|Irregular|Deficiente
        Comentario       varchar(2000) NULL,
        Compromiso       varchar(2000) NULL,      -- solo acompanamiento
        FirmaVendedor    nvarchar(max) NULL,      -- PNG data-URL (base64)
        FirmaSupervisor  nvarchar(max) NULL,
        FechaRegistro    datetime     NOT NULL CONSTRAINT DF_SupEva_Reg DEFAULT(GETDATE())
    );
    CREATE INDEX IX_SupEva_RepFecha ON dbo.SupervisionEvaluaciones_RM(RepCodigo, Fecha);
    CREATE INDEX IX_SupEva_SupFecha ON dbo.SupervisionEvaluaciones_RM(SupCod, Fecha);
    CREATE INDEX IX_SupEva_Fecha    ON dbo.SupervisionEvaluaciones_RM(Fecha);
END
GO

/* ================================================================== *
 * 5) DETALLE  ->  puntaje por (evaluacion, cliente, factor)          *
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionDetalle_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionDetalle_RM
    (
        DetID       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupDet_RM PRIMARY KEY,
        EvaID       int          NOT NULL,   -- -> SupervisionEvaluaciones_RM
        CliID       int          NULL,
        CliCodigo   varchar(20)  NULL,
        CliNombre   varchar(150) NULL,
        FactorCod   varchar(40)  NOT NULL,   -- -> SupervisionFactores_RM.Codigo
        Puntaje     decimal(9,2) NOT NULL    -- 0-100 (sino=0/100; 1-10 guardado x10)
    );
    CREATE INDEX IX_SupDet_Eva ON dbo.SupervisionDetalle_RM(EvaID);
END
GO

/* ================================================================== *
 * 6) FOTOS (evidencia)  por evaluacion / cliente                     *
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionFotos_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionFotos_RM
    (
        FotoID      bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupFoto_RM PRIMARY KEY,
        EvaID       int           NOT NULL,
        CliID       int           NULL,
        CliCodigo   varchar(20)   NULL,
        Imagen      nvarchar(max) NOT NULL,   -- JPEG/PNG data-URL (base64)
        FechaRegistro datetime    NOT NULL CONSTRAINT DF_SupFoto_Fec DEFAULT(GETDATE())
    );
    CREATE INDEX IX_SupFoto_Eva ON dbo.SupervisionFotos_RM(EvaID);
END
GO

/* ================================================================== *
 * 7) PLAN semanal (rejilla)  -  opcional                             *
 * ================================================================== */
IF OBJECT_ID('dbo.SupervisionPlan_RM','U') IS NULL
BEGIN
    CREATE TABLE dbo.SupervisionPlan_RM
    (
        PlanID     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SupPlan_RM PRIMARY KEY,
        SupCod     varchar(20)  NOT NULL,
        RepCodigo  varchar(20)  NOT NULL,
        Fecha      date         NOT NULL,
        Modo       varchar(10)  NOT NULL,   -- calle | virtual
        FechaActualizacion datetime NOT NULL CONSTRAINT DF_SupPlan_Fec DEFAULT(GETDATE())
    );
    CREATE UNIQUE INDEX UX_SupPlan ON dbo.SupervisionPlan_RM(SupCod, RepCodigo, Fecha);
END
GO

/* ================================================================== *
 * 8) PERMISOS para la app (usuario "Ventas")                        *
 * ================================================================== */
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionEsquemas_RM     TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionFactores_RM     TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionUsuarioMapa_RM  TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionEvaluaciones_RM TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionDetalle_RM      TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionFotos_RM        TO [Ventas];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SupervisionPlan_RM         TO [Ventas];
GO

/* Revision:
   SELECT * FROM dbo.SupervisionEsquemas_RM ORDER BY Orden;
   SELECT * FROM dbo.SupervisionFactores_RM ORDER BY Esquema, Orden;
   SELECT * FROM dbo.SupervisionUsuarioMapa_RM;
   SELECT TOP 50 * FROM dbo.SupervisionEvaluaciones_RM ORDER BY FechaRegistro DESC;
*/
