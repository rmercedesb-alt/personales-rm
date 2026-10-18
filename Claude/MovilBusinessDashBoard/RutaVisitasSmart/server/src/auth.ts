import { Router, type NextFunction, type Request, type Response } from "express";
import crypto from "node:crypto";
import { getPool, sql } from "./db.js";

declare module "express-session" {
  interface SessionData {
    usuario?: string;
    nombre?: string;
  }
}

export const authRouter = Router();

interface UsuarioRow {
  UsuInicioSesion: string;
  UsuNombres: string | null;
  UsuApellidos: string | null;
  UsuEstatus: boolean | number;
  UsuClave: string | null;
  UsuIntentosClave: number | null;
  UsuUltimoIntento: Date | null;
}

const MAX_INTENTOS = 5;
const BLOQUEO_MINS = 15;

function md5Upper(text: string): string {
  return crypto.createHash("md5").update(text, "utf8").digest("hex").toUpperCase();
}

/**
 * Bloquea el acceso a todo lo que se registre DESPUÉS de este middleware.
 * Se monta en index.ts luego de authRouter (login/logout/session/empresa/logo
 * quedan públicos), así que cualquier otra ruta de /api requiere sesión.
 */
export function requireAuth(req: Request, res: Response, next: NextFunction) {
  if (req.session?.usuario) {
    next();
    return;
  }
  res.status(401).json({ error: "No autenticado" });
}

authRouter.post("/login", async (req, res) => {
  const usuario = typeof req.body?.usuario === "string" ? req.body.usuario.trim() : "";
  const clave = typeof req.body?.clave === "string" ? req.body.clave : "";
  if (!usuario || !clave) {
    res.json({ ok: false, error: "Completa usuario y contraseña" });
    return;
  }

  try {
    const pool = await getPool();
    const result = await pool
      .request()
      .input("u", sql.VarChar, usuario)
      .query<UsuarioRow>(
        `SELECT UsuInicioSesion, UsuNombres, UsuApellidos, UsuEstatus, UsuClave,
                ISNULL(UsuIntentosClave, 0) AS UsuIntentosClave, UsuUltimoIntento
         FROM UsuarioSistema WITH (NOLOCK)
         WHERE UsuInicioSesion = @u`
      );

    const row = result.recordset[0];
    if (!row) {
      res.json({ ok: false, error: "Usuario o contraseña incorrectos" });
      return;
    }

    if (!row.UsuEstatus) {
      res.json({ ok: false, error: "Usuario inactivo. Contacta al administrador" });
      return;
    }

    let intentos = row.UsuIntentosClave ?? 0;
    if (intentos >= MAX_INTENTOS && row.UsuUltimoIntento) {
      const minsPasados = (Date.now() - new Date(row.UsuUltimoIntento).getTime()) / 60000;
      if (minsPasados < BLOQUEO_MINS) {
        const restantes = Math.floor(BLOQUEO_MINS - minsPasados) + 1;
        res.json({ ok: false, error: `Cuenta bloqueada. Intenta en ${restantes} min` });
        return;
      }
      // Ventana de bloqueo vencida — se intenta resetear el contador. El
      // usuario Ventas es de solo lectura y no tiene permiso UPDATE sobre
      // esta tabla, así que esto normalmente falla en silencio (igual que en
      // el dashboard .NET hermano) y el contador queda desactualizado, pero
      // no bloquea el login: la comparación de clave sigue funcionando.
      try {
        await pool.request().input("u", sql.VarChar, usuario).query("UPDATE UsuarioSistema SET UsuIntentosClave = 0 WHERE UsuInicioSesion = @u");
      } catch {
        /* sin permiso UPDATE — se ignora */
      }
      intentos = 0;
    }

    const claveDb = (row.UsuClave ?? "").trim().toUpperCase();
    if (md5Upper(clave) !== claveDb) {
      try {
        await pool
          .request()
          .input("u", sql.VarChar, usuario)
          .query(
            `UPDATE UsuarioSistema SET UsuIntentosClave = ISNULL(UsuIntentosClave, 0) + 1, UsuUltimoIntento = GETDATE()
             WHERE UsuInicioSesion = @u`
          );
      } catch {
        /* sin permiso UPDATE — se ignora */
      }

      const restantes = MAX_INTENTOS - intentos - 1;
      const error =
        restantes <= 0
          ? `Cuenta bloqueada por ${BLOQUEO_MINS} minutos`
          : restantes <= 2
            ? `Usuario o contraseña incorrectos (${restantes} intento${restantes > 1 ? "s" : ""} restante${restantes > 1 ? "s" : ""})`
            : "Usuario o contraseña incorrectos";
      res.json({ ok: false, error });
      return;
    }

    try {
      await pool
        .request()
        .input("u", sql.VarChar, usuario)
        .query("UPDATE UsuarioSistema SET UsuIntentosClave = 0 WHERE UsuInicioSesion = @u");
    } catch {
      /* sin permiso UPDATE — el login sigue siendo válido */
    }

    const nombre = `${row.UsuNombres ?? ""} ${row.UsuApellidos ?? ""}`.trim();
    req.session.usuario = row.UsuInicioSesion;
    req.session.nombre = nombre;
    res.json({ ok: true });
  } catch (err) {
    res.json({ ok: false, error: err instanceof Error ? err.message : String(err) });
  }
});

authRouter.post("/logout", (req, res) => {
  req.session.destroy(() => {
    res.json({ ok: true });
  });
});

authRouter.get("/session", (req, res) => {
  if (req.session?.usuario) {
    res.json({ ok: true, usuario: req.session.usuario, nombre: req.session.nombre ?? "" });
    return;
  }
  res.status(401).json({ ok: false });
});

/**
 * Sin autenticación (se necesita ANTES de loguearse, para la pantalla de
 * login) — a diferencia del dashboard .NET hermano, acá una falla real de
 * conexión SÍ se distingue (`ok:false`) de "conectado pero sin nombre" — el
 * cliente usa esto para decidir si muestra branding o no.
 */
authRouter.get("/empresa", async (_req, res) => {
  try {
    const pool = await getPool();
    const result = await pool.request().query<{ EmpNombre: string | null }>("SELECT TOP 1 EmpNombre FROM Empresa WITH (NOLOCK) ORDER BY EmpID");
    const nombre = result.recordset[0]?.EmpNombre?.trim() ?? "";
    res.setHeader("Cache-Control", "public, max-age=3600");
    res.json({ ok: true, nombre });
  } catch {
    res.status(503).json({ ok: false });
  }
});

/** Logo de la empresa (tabla Empresa, columna EmpLogo) — 404 si no hay
 * conexión o no hay logo cargado; el front-end no muestra ningún logo de
 * respaldo en ese caso (a propósito, para no simular una marca que no se
 * pudo confirmar contra la base de datos real). */
authRouter.get("/logo", async (_req, res) => {
  try {
    const pool = await getPool();
    const result = await pool
      .request()
      .query<{ EmpLogo: Buffer | null }>("SELECT TOP 1 EmpLogo FROM Empresa WITH (NOLOCK) WHERE EmpLogo IS NOT NULL ORDER BY EmpID");
    const data = result.recordset[0]?.EmpLogo;
    if (!data || data.length < 4) {
      res.status(404).end();
      return;
    }
    const mime =
      data[0] === 0xff && data[1] === 0xd8
        ? "image/jpeg"
        : data[0] === 0x89 && data[1] === 0x50
          ? "image/png"
          : data[0] === 0x47 && data[1] === 0x49
            ? "image/gif"
            : data[0] === 0x42 && data[1] === 0x4d
              ? "image/bmp"
              : "image/jpeg";
    res.setHeader("Cache-Control", "public, max-age=86400");
    res.setHeader("Content-Type", mime);
    res.send(data);
  } catch {
    res.status(404).end();
  }
});
