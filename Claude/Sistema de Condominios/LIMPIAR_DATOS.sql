-- ============================================================================
--  LIMPIAR DATOS DE LA BASE DE DATOS  (inmuebles_rd)
--  Borra TODOS los datos transaccionales y maestros, CONSERVANDO:
--    - usuarios        (los logins del sistema)
--    - configuracion   (configuracion/empresa)  <- recomendado conservar
--    - __EFMigrationsHistory (estructura; NO se toca)
--
--  ⚠️  OPERACION IRREVERSIBLE. Haz un RESPALDO antes (ver abajo).
-- ============================================================================

-- ----------------------------------------------------------------------------
--  PASO 1 (MUY RECOMENDADO): Respaldo previo desde una terminal CMD/PowerShell
--  (NO dentro de pgAdmin). Cambia la ruta si lo deseas:
--
--    "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe" -h localhost -U postgres ^
--       -d inmuebles_rd -F c -f "C:\Respaldo_inmuebles_ANTES_DE_LIMPIAR.backup"
--
--  (te pedira la contrasena de postgres)
-- ----------------------------------------------------------------------------

-- ----------------------------------------------------------------------------
--  PASO 2: Ejecuta este bloque en pgAdmin (Query Tool) sobre la base inmuebles_rd.
--  TRUNCATE vacia las tablas, reinicia los contadores y respeta las relaciones.
-- ----------------------------------------------------------------------------
TRUNCATE TABLE
    abonos_cuenta,
    adelantos,
    co_propietarios,
    contratos,
    cxc_cargos,
    cxc_pagos,
    inmuebles,
    inquilinos,
    liquidaciones,
    log_custodia,
    propietarios,
    sesiones_caja,
    whatsapp_envios
RESTART IDENTITY CASCADE;

-- ----------------------------------------------------------------------------
--  NOTAS / OPCIONES
--
--  * Se CONSERVAN por diseno: usuarios, configuracion.
--
--  * Si TAMBIEN quieres borrar la configuracion/empresa, ejecuta ademas:
--        -- TRUNCATE TABLE configuracion RESTART IDENTITY CASCADE;
--
--  * whatsapp_envios es el historial del proceso "En linea con mis clientes".
--    Si NO quieres borrar ese historial, quita esa linea del TRUNCATE de arriba.
-- ----------------------------------------------------------------------------
