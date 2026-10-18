-- ============================================================
-- Tabla: empresa
-- Descripción: Datos generales de la empresa / administradora
-- Base de datos: PostgreSQL (inmuebles_rd)
-- ============================================================

CREATE TABLE IF NOT EXISTS empresa (
    id               SERIAL       PRIMARY KEY,
    nombre           VARCHAR(200) DEFAULT '',
    direccion        VARCHAR(300) DEFAULT '',
    direccion2       VARCHAR(300) DEFAULT '',
    rnc              VARCHAR(30)  DEFAULT '',
    telefono         VARCHAR(30)  DEFAULT '',
    fax              VARCHAR(30)  DEFAULT '',
    email            VARCHAR(150) DEFAULT '',
    logo             TEXT         DEFAULT '',   -- base64 data URL (png/jpg)
    fecha_actualizacion TIMESTAMP DEFAULT NOW()
);

-- Insertar fila por defecto si la tabla está vacía
INSERT INTO empresa (nombre, fecha_actualizacion)
SELECT 'Fanny Mercedes & Asoc.', NOW()
WHERE NOT EXISTS (SELECT 1 FROM empresa LIMIT 1);
