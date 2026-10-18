import { DIAS_SEMANA, type AsignacionCliente, type ClienteRuta, type DiaSemana, type RutaPorDia } from "../types";

export function emptyRuta(): RutaPorDia {
  const base = {} as RutaPorDia;
  for (const d of DIAS_SEMANA) base[d] = [];
  return base;
}

export function emptySeleccion(): Record<DiaSemana, Set<number>> {
  const base = {} as Record<DiaSemana, Set<number>>;
  for (const d of DIAS_SEMANA) base[d] = new Set<number>();
  return base;
}

export function findDia(ruta: RutaPorDia, cliId: number): DiaSemana | null {
  for (const dia of DIAS_SEMANA) {
    if (ruta[dia].some((c) => c.cliId === cliId)) return dia;
  }
  return null;
}

/** Reconstruye el tablero Día→clientes de un vendedor a partir de las filas
 * de RutaVisitas ya cargadas (`all`), respetando el orden guardado. */
export function buildRutaInicial(all: ClienteRuta[], repCodigo: string): RutaPorDia {
  const base = emptyRuta();
  const diasValidos = new Set<string>(DIAS_SEMANA);
  const byDia = new Map<DiaSemana, ClienteRuta[]>();
  for (const r of all) {
    if (r.repCodigo !== repCodigo || !diasValidos.has(r.dia)) continue;
    const dia = r.dia as DiaSemana;
    if (!byDia.has(dia)) byDia.set(dia, []);
    byDia.get(dia)!.push(r);
  }
  for (const [dia, list] of byDia) {
    base[dia] = [...list]
      .sort((a, b) => (a.rutPosicion ?? 9999) - (b.rutPosicion ?? 9999))
      .map((r) => ({
        cliId: r.cliId,
        cliCodigo: r.cliCodigo,
        cliNombre: r.cliNombre,
        lat: r.lat,
        lng: r.lng,
        claDescripcion: r.claDescripcion,
        semanasActivas: r.semanasActivas.length > 0 ? r.semanasActivas : [1, 2, 3, 4],
        esPrimero: false,
        esUltimo: false,
      }));
  }
  return base;
}

export function moverBloque(items: AsignacionCliente[], seleccionados: Set<number>, direccion: "arriba" | "abajo"): AsignacionCliente[] {
  const arr = [...items];
  if (direccion === "arriba") {
    for (let i = 1; i < arr.length; i++) {
      if (seleccionados.has(arr[i].cliId) && !seleccionados.has(arr[i - 1].cliId)) {
        [arr[i - 1], arr[i]] = [arr[i], arr[i - 1]];
      }
    }
  } else {
    for (let i = arr.length - 2; i >= 0; i--) {
      if (seleccionados.has(arr[i].cliId) && !seleccionados.has(arr[i + 1].cliId)) {
        [arr[i], arr[i + 1]] = [arr[i + 1], arr[i]];
      }
    }
  }
  return arr;
}
