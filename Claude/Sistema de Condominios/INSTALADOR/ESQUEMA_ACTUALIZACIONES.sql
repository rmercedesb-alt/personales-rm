-- ============================================================================
--  SINCRONIZACIÓN DE ESQUEMA  (inmuebles_rd)
--  Aplica las columnas nuevas que el sistema necesita, SIN borrar datos.
--  Es idempotente: se puede correr cuantas veces se quiera (IF NOT EXISTS).
--  Cada vez que una mejora agregue una columna, se añade aquí.
-- ============================================================================

-- Liquidaciones v5 (anulación)
ALTER TABLE liquidaciones ADD COLUMN IF NOT EXISTS "Anulado"          boolean   NOT NULL DEFAULT false;
ALTER TABLE liquidaciones ADD COLUMN IF NOT EXISTS "MotivoAnulacion"  text      NOT NULL DEFAULT '';
ALTER TABLE liquidaciones ADD COLUMN IF NOT EXISTS "FechaAnulacion"   timestamptz NULL;
ALTER TABLE liquidaciones ADD COLUMN IF NOT EXISTS "UsuarioAnulacion" text      NOT NULL DEFAULT '';

-- Descuento / condonación de mora
ALTER TABLE cxc_cargos ADD COLUMN IF NOT EXISTS "DescuentoMora"        numeric   NOT NULL DEFAULT 0;
ALTER TABLE cxc_cargos ADD COLUMN IF NOT EXISTS "MotivoDescuentoMora"  text      NOT NULL DEFAULT '';
ALTER TABLE cxc_cargos ADD COLUMN IF NOT EXISTS "UsuarioDescuentoMora" text      NOT NULL DEFAULT '';
ALTER TABLE cxc_cargos ADD COLUMN IF NOT EXISTS "FechaDescuentoMora"   timestamptz NULL;

-- Inquilino: cotejo de envío de WhatsApp
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS "EnviarWhatsApp" boolean NOT NULL DEFAULT false;

-- Pagos Varios / Otros Ingresos
ALTER TABLE cxc_pagos ADD COLUMN IF NOT EXISTS "EsOtroIngreso" boolean NOT NULL DEFAULT false;

-- Sesión de caja: fecha de registro
ALTER TABLE sesiones_caja ADD COLUMN IF NOT EXISTS "FechaRegistro" timestamptz NULL;

-- ===== V6 =====
-- Pasaporte en propietarios e inquilinos
ALTER TABLE propietarios ADD COLUMN IF NOT EXISTS "Pasaporte" text NOT NULL DEFAULT '';
ALTER TABLE inquilinos   ADD COLUMN IF NOT EXISTS "Pasaporte" text NOT NULL DEFAULT '';

-- Pago de DEPOSITO: entra completo al propietario (sin comisión)
ALTER TABLE cxc_pagos ADD COLUMN IF NOT EXISTS "EsDeposito" boolean NOT NULL DEFAULT false;

-- Liquidación: adelantos descontados (neto = cobrado - comisión - adelantos)
ALTER TABLE liquidaciones ADD COLUMN IF NOT EXISTS "TotalAdelantos" numeric NOT NULL DEFAULT 0;

-- #7 Adelantos: forma de entrega, anulación (con justificación) y pago/devolución a caja
ALTER TABLE adelantos ADD COLUMN IF NOT EXISTS "FormaPago"        text        NOT NULL DEFAULT 'EFECTIVO';
ALTER TABLE adelantos ADD COLUMN IF NOT EXISTS "MotivoAnulacion"  text        NOT NULL DEFAULT '';
ALTER TABLE adelantos ADD COLUMN IF NOT EXISTS "UsuarioAnulacion" text        NOT NULL DEFAULT '';
ALTER TABLE adelantos ADD COLUMN IF NOT EXISTS "FechaAnulacion"   timestamptz NULL;
ALTER TABLE adelantos ADD COLUMN IF NOT EXISTS "IdSesionCajaPago" text        NOT NULL DEFAULT '';
ALTER TABLE adelantos ADD COLUMN IF NOT EXISTS "FechaPago"        timestamptz NULL;

-- #7 CxC: anulación de cargo (solo cargos sin pagos)
ALTER TABLE cxc_cargos ADD COLUMN IF NOT EXISTS "MotivoAnulacion"  text        NOT NULL DEFAULT '';
ALTER TABLE cxc_cargos ADD COLUMN IF NOT EXISTS "UsuarioAnulacion" text        NOT NULL DEFAULT '';
ALTER TABLE cxc_cargos ADD COLUMN IF NOT EXISTS "FechaAnulacion"   timestamptz NULL;

-- #7 Documentos adjuntos a adelantos (evidencia, guardados en la BD)
CREATE TABLE IF NOT EXISTS adelanto_documentos (
    "Id"            text PRIMARY KEY,
    "IdAdelanto"    text NOT NULL,
    "NombreArchivo" text NOT NULL DEFAULT '',
    "TipoMime"      text NOT NULL DEFAULT '',
    "Tamano"        bigint NOT NULL DEFAULT 0,
    "Contenido"     bytea NOT NULL,
    "Usuario"       text NOT NULL DEFAULT '',
    "FechaSubida"   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_adelanto_documentos_idadelanto ON adelanto_documentos ("IdAdelanto");
