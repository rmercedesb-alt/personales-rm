import * as turf from "@turf/turf";
import { DIAS_SEMANA, type ClienteCandidato, type DiaSemana, type RutaPorDia } from "../types";

export interface SugerenciaDia {
  dia: DiaSemana;
  distanciaKm: number;
}

/**
 * Para cada cliente sin asignar, sugiere el día del propio vendedor cuyo
 * cliente ya-asignado más cercano queda a menor distancia ("el día de sus
 * vecinos"). Es vecino-más-cercano puro (no clusteriza ni promedia), a
 * propósito: en zonas con dos bolsones del mismo vendedor lejos entre sí,
 * cada cliente sin asignar debe pegarse al bolsón físicamente más próximo,
 * no al centroide de todos los días.
 *
 * Si el vendedor todavía no tiene ningún cliente asignado (ruta vacía), no
 * hay con qué comparar y no se genera sugerencia para nadie.
 */
export function sugerirDiaPorCercania(sinAsignar: ClienteCandidato[], ruta: RutaPorDia): Map<number, SugerenciaDia> {
  const referencias: Array<{ dia: DiaSemana; point: ReturnType<typeof turf.point> }> = [];
  for (const dia of DIAS_SEMANA) {
    for (const c of ruta[dia]) referencias.push({ dia, point: turf.point([c.lng, c.lat]) });
  }

  const out = new Map<number, SugerenciaDia>();
  if (referencias.length === 0) return out;

  for (const cli of sinAsignar) {
    const from = turf.point([cli.lng, cli.lat]);
    let bestDia: DiaSemana = referencias[0].dia;
    let bestDist = Infinity;
    for (const ref of referencias) {
      const d = turf.distance(from, ref.point);
      if (d < bestDist) {
        bestDist = d;
        bestDia = ref.dia;
      }
    }
    out.set(cli.cliId, { dia: bestDia, distanciaKm: bestDist });
  }
  return out;
}
