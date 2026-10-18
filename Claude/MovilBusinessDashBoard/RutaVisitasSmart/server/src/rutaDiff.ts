import { sql } from "./db.js";
import { DIAS, resolveDia, resolveFrecuencia } from "./diaResolver.js";

/**
 * Diff de RutaVisitas compartido por `POST /export-ruta` (un vendedor) y
 * `POST /export-simulacion` (varios vendedores a la vez). Nunca escribe a la
 * BD: relee el estado actual, compara contra lo deseado, y arma los
 * statements SQL como texto para que TI los aplique manualmente.
 */

const ZERO_CODE = "0000000";

export interface AsignacionEntrada {
  cliId: number;
  cliCodigo?: string | null;
  cliNombre?: string | null;
  semanasActivas: number[];
}

export interface RutaVisitaRow {
  CliID: number;
  RutSemana1: string | null;
  RutSemana2: string | null;
  RutSemana3: string | null;
  RutSemana4: string | null;
  RutPosicion: number | null;
}

export interface Desired {
  cliId: number;
  cliCodigo: string | null;
  cliNombre: string | null;
  dia: string;
  posicion: number;
  semanas: [string, string, string, string];
  semanasActivas: number[];
}

export interface DiffResultado {
  repCodigo: string;
  inserts: Desired[];
  updates: Desired[];
  cleared: number[];
  sinCambios: Desired[];
  statements: string[];
}

export interface OpcionesDiff {
  /** Si viene, solo se "quitan" (bucket `cleared`) las filas existentes cuyo CliID
   * esté en este set; el resto de la ruta del vendedor queda intacta. Sin esto
   * (comportamiento de siempre) se quita todo lo que no esté en lo deseado — que
   * solo es correcto cuando lo deseado cubre TODA la ruta del vendedor. */
  limpiarSoloDentroDe?: Set<number>;
}

/** `sql.ConnectionPool` y `sql.Transaction` cumplen esta forma — permite
 * correr `calcularDiff`/`ejecutarDiff` tanto fuera de una transacción (lectura
 * para el preview) como dentro de una (para aplicar de verdad, todo o nada). */
export interface SqlExecutor {
  request(): sql.Request;
}

/** 1, 5, 10, 15, 20… (posición "de 5 en 5" pedida por el usuario). Se usa
 * cuando no hay ningún vecino existente de referencia (día nuevo, o tramo sin
 * anclas a ningún lado). */
export function posicionParaIndice(idx: number): number {
  return idx === 0 ? 1 : idx * 5;
}

/** Reparte `n` posiciones enteras, en orden, estrictamente entre `desde`
 * (exclusivo, null = sin piso) y `hasta` (exclusivo, null = sin techo). Con
 * ambos null (día sin ninguna posición previa que respetar) reproduce
 * `posicionParaIndice`. Si el hueco heredado es demasiado angosto para caber
 * los `n` enteros (caso raro con datos legado muy apretados), hace el mejor
 * esfuerzo con enteros consecutivos — puede llegar a igualar o pasar `hasta`,
 * pero nunca toca la posición de clientes fuera de este tramo puntual. */
function repartirPosiciones(desde: number | null, hasta: number | null, n: number): number[] {
  if (n === 0) return [];
  if (desde == null && hasta == null) {
    return Array.from({ length: n }, (_, i) => posicionParaIndice(i));
  }
  const inicio = desde ?? 0;
  if (hasta == null) {
    return Array.from({ length: n }, (_, i) => inicio + (i + 1) * 5);
  }
  const disponibles = hasta - inicio - 1;
  if (disponibles >= n) {
    const paso = disponibles / (n + 1);
    const out: number[] = [];
    for (let i = 1; i <= n; i++) out.push(Math.round(inicio + paso * i));
    for (let i = 1; i < out.length; i++) if (out[i] <= out[i - 1]) out[i] = out[i - 1] + 1;
    return out;
  }
  return Array.from({ length: n }, (_, i) => inicio + 1 + i);
}

/**
 * Posición mínima-toque para un día: los clientes cuya posición existente (EN
 * ESE MISMO DÍA — si venían de otro día no cuenta) ya forma una subsecuencia
 * creciente compatible con el orden deseado CONSERVAN su número tal cual está
 * en la base, aunque no sea múltiplo de 5. Solo los que quedan afuera de esa
 * subsecuencia (nuevos, movidos de otro día, o reordenados a mano) reciben una
 * posición nueva — pedido explícito del usuario tras ver que "Aplicar" volvía
 * a numerar casi toda la ruta de un vendedor por un cambio de 2 clientes.
 */
function calcularPosicionesDelDia(posicionExistenteSiMismoDia: Array<number | null>): number[] {
  const n = posicionExistenteSiMismoDia.length;
  const dp = new Array<number>(n).fill(1);
  const prev = new Array<number>(n).fill(-1);
  let mejorFinal = -1;
  for (let i = 0; i < n; i++) {
    if (posicionExistenteSiMismoDia[i] == null) continue;
    for (let j = 0; j < i; j++) {
      if (posicionExistenteSiMismoDia[j] == null) continue;
      if (posicionExistenteSiMismoDia[j]! < posicionExistenteSiMismoDia[i]! && dp[j] + 1 > dp[i]) {
        dp[i] = dp[j] + 1;
        prev[i] = j;
      }
    }
    if (mejorFinal === -1 || dp[i] > dp[mejorFinal]) mejorFinal = i;
  }

  const conservado = new Array<boolean>(n).fill(false);
  if (mejorFinal !== -1) {
    let k: number | undefined = mejorFinal;
    while (k !== undefined && k !== -1) {
      conservado[k] = true;
      k = prev[k];
    }
  }

  const posiciones = new Array<number>(n).fill(0);
  let i = 0;
  while (i < n) {
    if (conservado[i]) {
      posiciones[i] = posicionExistenteSiMismoDia[i]!;
      i++;
      continue;
    }
    const inicioTramo = i;
    while (i < n && !conservado[i]) i++;
    const finTramo = i; // exclusivo
    const desde = inicioTramo > 0 ? posiciones[inicioTramo - 1] : null;
    const hasta = finTramo < n ? posicionExistenteSiMismoDia[finTramo] : null;
    const nuevos = repartirPosiciones(desde, hasta, finTramo - inicioTramo);
    for (let k = 0; k < nuevos.length; k++) posiciones[inicioTramo + k] = nuevos[k];
  }
  return posiciones;
}

export function buildCodigoDia(diaIndex: number): string {
  const chars = ZERO_CODE.split("");
  chars[diaIndex] = "1";
  return chars.join("");
}

function esCodigoActivo(codigo: string | null | undefined): boolean {
  return !!codigo && codigo !== ZERO_CODE && codigo.trim() !== "";
}

export function sqlQuote(value: string): string {
  return value.replace(/'/g, "''");
}

/** Relee RutaVisitas en vivo para `repCodigo`, arma el diff contra `dias`
 * (estado deseado) y devuelve los buckets + statements SQL. */
export async function calcularDiff(
  pool: SqlExecutor,
  repCodigo: string,
  dias: Partial<Record<(typeof DIAS)[number], AsignacionEntrada[]>>,
  opciones?: OpcionesDiff
): Promise<DiffResultado> {
  const existingResult = await pool
    .request()
    .input("rep", sql.VarChar, repCodigo)
    .query<RutaVisitaRow>(
      `SELECT CliID, RutSemana1, RutSemana2, RutSemana3, RutSemana4, RutPosicion
       FROM RutaVisitas WITH (NOLOCK)
       WHERE RepCodigo = @rep`
    );
  const existingByCliId = new Map(existingResult.recordset.map((r) => [r.CliID, r]));

  const desiredByCliId = new Map<number, Desired>();
  for (const dia of DIAS) {
    const entradas = dias[dia] ?? [];
    const diaIndex = DIAS.indexOf(dia);

    // La posición existente de un cliente solo cuenta como "ancla" si su fila
    // actual YA está en ESTE MISMO día — si viene de otro día (o es nuevo), no
    // hay nada que conservar y debe recibir una posición nueva.
    const posicionExistenteSiMismoDia = entradas.map((entrada) => {
      const existing = existingByCliId.get(entrada.cliId);
      if (!existing) return null;
      const diaExistente = resolveDia([existing.RutSemana1, existing.RutSemana2, existing.RutSemana3, existing.RutSemana4]);
      return diaExistente === dia ? (existing.RutPosicion ?? null) : null;
    });
    const posiciones = calcularPosicionesDelDia(posicionExistenteSiMismoDia);

    entradas.forEach((entrada, idx) => {
      const semanasActivas = entrada.semanasActivas.length > 0 ? entrada.semanasActivas : [1, 2, 3, 4];
      const activo = buildCodigoDia(diaIndex);
      const semanas: [string, string, string, string] = [1, 2, 3, 4].map((w) =>
        semanasActivas.includes(w) ? activo : ZERO_CODE
      ) as [string, string, string, string];
      desiredByCliId.set(entrada.cliId, {
        cliId: entrada.cliId,
        cliCodigo: entrada.cliCodigo ?? null,
        cliNombre: entrada.cliNombre ?? null,
        dia,
        posicion: posiciones[idx],
        semanas,
        semanasActivas,
      });
    });
  }

  const inserts: Desired[] = [];
  const updates: Desired[] = [];
  const cleared: number[] = [];
  const sinCambios: Desired[] = [];

  for (const [cliId, desired] of desiredByCliId) {
    const existing = existingByCliId.get(cliId);
    if (!existing) {
      inserts.push(desired);
      continue;
    }
    const cambioSemanas =
      (existing.RutSemana1 ?? ZERO_CODE) !== desired.semanas[0] ||
      (existing.RutSemana2 ?? ZERO_CODE) !== desired.semanas[1] ||
      (existing.RutSemana3 ?? ZERO_CODE) !== desired.semanas[2] ||
      (existing.RutSemana4 ?? ZERO_CODE) !== desired.semanas[3];
    const cambioPosicion = (existing.RutPosicion ?? null) !== desired.posicion;
    if (cambioSemanas || cambioPosicion) updates.push(desired);
    else sinCambios.push(desired);
  }

  for (const [cliId, existing] of existingByCliId) {
    if (desiredByCliId.has(cliId)) continue;
    if (opciones?.limpiarSoloDentroDe && !opciones.limpiarSoloDentroDe.has(cliId)) continue;
    const yaVacio =
      !esCodigoActivo(existing.RutSemana1) &&
      !esCodigoActivo(existing.RutSemana2) &&
      !esCodigoActivo(existing.RutSemana3) &&
      !esCodigoActivo(existing.RutSemana4) &&
      existing.RutPosicion == null;
    if (!yaVacio) cleared.push(cliId);
  }

  const statements: string[] = [];
  for (const d of inserts) {
    statements.push(
      `-- nuevo: ${d.cliCodigo ?? d.cliId} ${d.cliNombre ?? ""} → ${d.dia}\n` +
        `INSERT INTO RutaVisitas (RepCodigo, CliID, RutSemana1, RutSemana2, RutSemana3, RutSemana4, RutPosicion) VALUES ` +
        `('${sqlQuote(repCodigo)}', ${d.cliId}, '${d.semanas[0]}', '${d.semanas[1]}', '${d.semanas[2]}', '${d.semanas[3]}', ${d.posicion});`
    );
  }
  for (const d of updates) {
    statements.push(
      `-- actualiza: ${d.cliCodigo ?? d.cliId} ${d.cliNombre ?? ""} → ${d.dia}\n` +
        `UPDATE RutaVisitas SET RutSemana1='${d.semanas[0]}', RutSemana2='${d.semanas[1]}', RutSemana3='${d.semanas[2]}', RutSemana4='${d.semanas[3]}', RutPosicion=${d.posicion} ` +
        `WHERE RepCodigo='${sqlQuote(repCodigo)}' AND CliID=${d.cliId};`
    );
  }
  for (const cliId of cleared) {
    // RutPosicion es NOT NULL en la tabla real (confirmado contra la BD) — no
    // se toca al vaciar, solo las 4 semanas. Antes este script decía
    // "RutPosicion=NULL", que hubiera fallado si TI lo llegaba a correr tal cual.
    statements.push(
      `-- quita del día (CliID ${cliId})\n` +
        `UPDATE RutaVisitas SET RutSemana1='${ZERO_CODE}', RutSemana2='${ZERO_CODE}', RutSemana3='${ZERO_CODE}', RutSemana4='${ZERO_CODE}' ` +
        `WHERE RepCodigo='${sqlQuote(repCodigo)}' AND CliID=${cliId};`
    );
  }

  return { repCodigo, inserts, updates, cleared, sinCambios, statements };
}

/**
 * Aplica DE VERDAD el diff contra RutaVisitas (a diferencia de `statements`,
 * que es solo texto para que TI lo lea/corra manualmente). Usa siempre
 * parámetros (`.input`), nunca concatena valores en el SQL — a diferencia del
 * texto de `statements`, que es para que un humano lo lea, esto se ejecuta.
 * Deja constancia de quién y cuándo en `UsuInicioSesion`/`RutFechaActualizacion`
 * (existían en la tabla pero no se usaban). No hace commit/rollback — eso lo
 * decide quien llama (normalmente dentro de una `sql.Transaction`).
 */
export async function ejecutarDiff(executor: SqlExecutor, repCodigo: string, resultado: DiffResultado, usuario: string): Promise<void> {
  for (const d of resultado.inserts) {
    // Estructura completa de la fila — antes solo mandábamos 9 de las 13
    // columnas (faltaban rowguid, RutSemana5, RutLatitud, RutLongitud). Ninguna
    // de las 4 es obligatoria (rowguid tiene default propio y las otras 3 son
    // nullable), pero se completan explícitas para no depender de defaults que
    // no podemos inspeccionar (permiso de VIEW DEFINITION denegado) y para que
    // el trigger de replicación vea la fila tal como la arma cualquier otra
    // herramienta que ya escribe acá. `RutSemana5` no lo usa esta app — se deja
    // en el mismo código "apagado" que las semanas 1-4 en vez de NULL.
    await executor
      .request()
      .input("rep", sql.VarChar, repCodigo)
      .input("cli", sql.Int, d.cliId)
      .input("s1", sql.VarChar, d.semanas[0])
      .input("s2", sql.VarChar, d.semanas[1])
      .input("s3", sql.VarChar, d.semanas[2])
      .input("s4", sql.VarChar, d.semanas[3])
      .input("s5", sql.VarChar, ZERO_CODE)
      .input("pos", sql.SmallInt, d.posicion)
      .input("usu", sql.VarChar, usuario)
      .query(
        `INSERT INTO RutaVisitas (
           RepCodigo, CliID, RutSemana1, RutSemana2, RutSemana3, RutSemana4, RutSemana5,
           RutPosicion, RutLatitud, RutLongitud, UsuInicioSesion, RutFechaActualizacion, rowguid
         ) VALUES (
           @rep, @cli, @s1, @s2, @s3, @s4, @s5,
           @pos, NULL, NULL, @usu, GETDATE(), NEWID()
         )`
      );
  }
  for (const d of resultado.updates) {
    await executor
      .request()
      .input("rep", sql.VarChar, repCodigo)
      .input("cli", sql.Int, d.cliId)
      .input("s1", sql.VarChar, d.semanas[0])
      .input("s2", sql.VarChar, d.semanas[1])
      .input("s3", sql.VarChar, d.semanas[2])
      .input("s4", sql.VarChar, d.semanas[3])
      .input("pos", sql.SmallInt, d.posicion)
      .input("usu", sql.VarChar, usuario)
      .query(
        `UPDATE RutaVisitas SET RutSemana1=@s1, RutSemana2=@s2, RutSemana3=@s3, RutSemana4=@s4, RutPosicion=@pos,
                UsuInicioSesion=@usu, RutFechaActualizacion=GETDATE()
         WHERE RepCodigo=@rep AND CliID=@cli`
      );
  }
  for (const cliId of resultado.cleared) {
    await executor
      .request()
      .input("rep", sql.VarChar, repCodigo)
      .input("cli", sql.Int, cliId)
      .input("zero", sql.VarChar, ZERO_CODE)
      .input("usu", sql.VarChar, usuario)
      .query(
        `UPDATE RutaVisitas SET RutSemana1=@zero, RutSemana2=@zero, RutSemana3=@zero, RutSemana4=@zero,
                UsuInicioSesion=@usu, RutFechaActualizacion=GETDATE()
         WHERE RepCodigo=@rep AND CliID=@cli`
      );
  }
}

export function filaDetalleXlsx(repLabel: string, d: Desired, accion: string) {
  return {
    vendedor: repLabel,
    cliId: d.cliId,
    cliCodigo: d.cliCodigo,
    cliNombre: d.cliNombre,
    dia: d.dia,
    posicion: d.posicion,
    frecuencia: resolveFrecuencia(d.semanasActivas),
    semanas: d.semanasActivas.join(","),
    accion,
  };
}
