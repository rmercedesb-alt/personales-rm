import * as turf from "@turf/turf";
import type { AsignacionCliente } from "../types";

/**
 * Vecino más cercano (greedy). Si hay un cliente marcado "primero" arranca
 * ahí; si hay uno marcado "último" se excluye del recorrido y se agrega al
 * final.
 */
export function ordenarPorCercania(items: AsignacionCliente[]): AsignacionCliente[] {
  if (items.length <= 2) return items;

  const ultimoIdx = items.findIndex((c) => c.esUltimo);
  const ultimo = ultimoIdx >= 0 ? items[ultimoIdx] : null;
  const primeroIdx = items.findIndex((c) => c.esPrimero && (ultimoIdx < 0 || c.cliId !== items[ultimoIdx].cliId));

  const remaining = items.filter((c) => (ultimo ? c.cliId !== ultimo.cliId : true));
  const ordered: AsignacionCliente[] = [];

  let current: AsignacionCliente;
  if (primeroIdx >= 0) {
    current = items[primeroIdx];
    remaining.splice(
      remaining.findIndex((c) => c.cliId === current.cliId),
      1
    );
  } else {
    current = remaining.shift()!;
  }
  ordered.push(current);

  while (remaining.length > 0) {
    let bestIdx = 0;
    let bestDist = Infinity;
    const from = turf.point([current.lng, current.lat]);
    for (let i = 0; i < remaining.length; i++) {
      const d = turf.distance(from, turf.point([remaining[i].lng, remaining[i].lat]));
      if (d < bestDist) {
        bestDist = d;
        bestIdx = i;
      }
    }
    current = remaining.splice(bestIdx, 1)[0];
    ordered.push(current);
  }

  if (ultimo) ordered.push(ultimo);
  return ordered;
}
