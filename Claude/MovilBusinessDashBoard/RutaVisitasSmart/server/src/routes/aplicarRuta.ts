import { Router } from "express";
import { getPool, getPoolConCredenciales, sql } from "../db.js";
import { DIAS } from "../diaResolver.js";
import { calcularDiff, ejecutarDiff, type AsignacionEntrada } from "../rutaDiff.js";

export const aplicarRutaRouter = Router();

interface AplicarBody {
  repCodigo: string;
  repNombre: string;
  dias: Partial<Record<(typeof DIAS)[number], AsignacionEntrada[]>>;
  /** false/ausente = solo calcula y devuelve el resumen (no toca la base, usa
   * la conexión de siempre — alcanza con permiso de lectura). true = escribe
   * de verdad — requiere `dbUsuario`/`dbPassword` (ver abajo). */
  confirmar?: boolean;
  /** Credenciales de SQL Server pedidas AL MOMENTO de escribir (la conexión
   * de siempre, `Ventas`, no tiene los permisos de replicación que hacen
   * falta) — solo se usan para esta escritura puntual, nunca se guardan. */
  dbUsuario?: string;
  dbPassword?: string;
}

function resumenDe(resultado: Awaited<ReturnType<typeof calcularDiff>>) {
  return {
    inserts: resultado.inserts.length,
    updates: resultado.updates.length,
    cleared: resultado.cleared.length,
    sinCambios: resultado.sinCambios.length,
  };
}

/**
 * A diferencia de `/export-ruta` (que SOLO genera el Excel/script para que TI
 * lo aplique manualmente), esto escribe de verdad contra RutaVisitas.
 *
 * El preview (`confirmar` ausente/false) usa la conexión de siempre
 * (`Ventas`) — alcanza, es solo lectura. Para escribir de verdad
 * (`confirmar: true`) hace falta un usuario de SQL Server con los permisos de
 * replicación que `Ventas` no tiene (ver PROGRESS.md) — en vez de guardar esas
 * credenciales en el `.env` del servidor, se piden en el momento desde el
 * cliente y se usan SOLO para esa escritura puntual: se abre una conexión
 * aparte, se hace todo dentro de una transacción, y la conexión se cierra
 * enseguida pase lo que pase. Nunca se loguean ni se guardan en ningún lado.
 *
 * Habilitado desde Crear Ruta y Modificar Ruta (un vendedor a la vez en
 * ambas). El diff siempre queda acotado a `repCodigo` — nunca toca filas de
 * otro vendedor, aunque el cliente venga de "candidatos" cargados de otro
 * vendedor en Crear Ruta: la fila vieja de ese otro vendedor ni se lee ni se
 * escribe en esta llamada. El endpoint en sí no impone qué módulo lo usa —
 * igual que /export-ruta, cualquier repCodigo/dias válido funciona.
 */
aplicarRutaRouter.post("/aplicar-ruta", async (req, res) => {
  try {
    const body = req.body as AplicarBody;
    if (!body?.repCodigo || !body?.dias) {
      res.status(400).json({ error: "Body inválido: se esperaba { repCodigo, repNombre, dias, confirmar }" });
      return;
    }
    const usuario = req.session?.usuario;
    if (!usuario) {
      res.status(401).json({ error: "No autenticado" });
      return;
    }

    if (!body.confirmar) {
      const pool = await getPool();
      const resultado = await calcularDiff(pool, body.repCodigo, body.dias);
      res.json({ aplicado: false, resumen: resumenDe(resultado) });
      return;
    }

    if (!body.dbUsuario || !body.dbPassword) {
      res.status(400).json({ error: "Faltan las credenciales de SQL Server para escribir (usuario/contraseña)" });
      return;
    }

    let pool: sql.ConnectionPool;
    try {
      pool = await getPoolConCredenciales(body.dbUsuario, body.dbPassword);
    } catch {
      // No se loguea el error tal cual: mssql normalmente no incluye la
      // contraseña en el mensaje, pero mejor no arriesgarse a filtrar nada.
      res.status(401).json({ error: "No se pudo conectar con esas credenciales de SQL Server (usuario/contraseña incorrectos, o sin acceso a la base)" });
      return;
    }

    try {
      const transaction = new sql.Transaction(pool);
      await transaction.begin();
      try {
        // Se recalcula DENTRO de la transacción — si algo cambió en RutaVisitas
        // entre el preview y este clic (otra persona, otra pestaña), el diff
        // que se aplica es el fresco, no el que se le mostró hace un rato.
        const resultado = await calcularDiff(transaction, body.repCodigo, body.dias);
        await ejecutarDiff(transaction, body.repCodigo, resultado, usuario);
        await transaction.commit();
        res.json({ aplicado: true, resumen: resumenDe(resultado) });
      } catch (err) {
        // Si la escritura falla (ej. un trigger de la tabla sin permiso para
        // correr su propia lógica interna), la conexión suele quedar en un
        // estado en el que el propio rollback() también lanza error ("Transaction
        // has been aborted") — eso NO debe tapar el error real: se intenta el
        // rollback pero siempre se reporta `err` (el original), nunca el del rollback.
        try {
          await transaction.rollback();
        } catch (rollbackErr) {
          console.error("Rollback también falló (se ignora, se reporta el error original de abajo):", rollbackErr);
        }
        throw err;
      }
    } finally {
      // Conexión de un solo uso — nunca se reutiliza ni se cachea, se cierra
      // siempre, haya salido bien o mal.
      await pool.close();
    }
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error aplicando cambios en RutaVisitas", detail: String(err) });
  }
});
