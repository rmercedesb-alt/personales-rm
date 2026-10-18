/* ═══════════════════════════════════════════════════════════════════════
   PresupuestoRappelMetas_RM (2026-09-06) — generaliza el "Modelo Rappel",
   pedido explícito del usuario: hoy META_RAPPEL (catálogo genérico) solo
   soporta 1 tipo de requerimiento (Monto RD$) por Suplidor. Esta tabla nueva
   agrega 3 tipos de requerimiento — Monto, Activaciones (conteo de clientes
   activos), Factura (conteo de facturas/pedidos distintos, PedSecuencia) —
   cada uno configurable por Suplidor O por Categoría (nunca ambos a la vez
   en la misma fila, mismo criterio que el resto del sistema). Pueden
   coexistir varios tipos para el mismo Suplidor/Categoría (confirmado con
   el usuario) — cada uno se evalúa independiente y actúa como PISO (mínimo):
   si lo natural/proyectado ya cumple, no cambia nada; si no llega, empuja el
   presupuesto hacia arriba (mismo mecanismo de "factor máximo" que ya usa
   Monto — Math.Max contra el crecimiento normal, nunca hace bajar).

   NO reemplaza META_RAPPEL (catálogo genérico, sigue funcionando igual para
   los 4 suplidores ya sembrados en 2026-08-30) — esta tabla es ADICIONAL,
   para cualquier caso nuevo que necesite Categoría, Activaciones o Factura.

   Cat3ID/Cat1ID: exactamente UNO poblado según AmbitoTipo (validado en el
   endpoint de guardado, no con un CHECK — mismo criterio que
   PresupuestoActivacionMetas_RM con sus 3 capas opcionales).

   Ejecutar en MovilBusiness5STD (producción) — mismo criterio que las demás
   tablas nuevas de este módulo.
   ═══════════════════════════════════════════════════════════════════════ */

IF OBJECT_ID('dbo.PresupuestoRappelMetas_RM') IS NULL
BEGIN
    CREATE TABLE dbo.PresupuestoRappelMetas_RM(
        Id                  int IDENTITY(1,1) PRIMARY KEY,
        TipoRequerimiento   varchar(20)     NOT NULL,   -- 'Monto' | 'Activaciones' | 'Factura'
        AmbitoTipo          varchar(10)     NOT NULL,   -- 'Suplidor' | 'Categoria'
        Cat3ID              int             NULL,       -- Suplidor; NULL si AmbitoTipo='Categoria'
        Cat1ID              int             NULL,       -- Categoría; NULL si AmbitoTipo='Suplidor'
        PeriodoAnio         smallint        NOT NULL,
        PeriodoMes          tinyint         NULL,       -- NULL = aplica a todo el año
        MetaValor           decimal(18,2)   NOT NULL,   -- RD$ (Monto) o conteo (Activaciones/Factura)
        Activo              bit             NOT NULL CONSTRAINT DF_PresupuestoRappelMetas_RM_Activo DEFAULT(1),
        UsuInicioSesion     varchar(64)     NULL,
        FechaActualizacion  datetime        NOT NULL CONSTRAINT DF_PresupuestoRappelMetas_RM_Fecha DEFAULT(GETDATE())
    );
    CREATE INDEX IX_PresupuestoRappelMetas_RM_Busqueda
        ON dbo.PresupuestoRappelMetas_RM(TipoRequerimiento, AmbitoTipo, Cat3ID, Cat1ID, PeriodoAnio, PeriodoMes);
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PresupuestoRappelMetas_RM TO Ventas;
GO
