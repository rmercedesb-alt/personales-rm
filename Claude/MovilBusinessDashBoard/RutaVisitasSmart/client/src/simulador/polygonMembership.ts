import * as turf from "@turf/turf";
import type { AsignacionSimulada, ZonaSimulada } from "../types";
import { calcularPoligono } from "./poligono";

function puntoEnPoligono(lat: number, lng: number, polygon: [number, number][]): boolean {
  if (polygon.length < 3) return false;
  const coords: [number, number][] = polygon.map(([la, ln]) => [ln, la]); // turf usa [lng, lat]
  const first = coords[0];
  const last = coords[coords.length - 1];
  const cerrado = first[0] === last[0] && first[1] === last[1] ? coords : [...coords, first];
  try {
    return turf.booleanPointInPolygon(turf.point([lng, lat]), turf.polygon([cerrado]));
  } catch {
    return false;
  }
}

/**
 * Recalcula membresía tras editar el polígono de `zonaEditadaId`: cualquier
 * cliente (de esta u otra zona) que ahora caiga dentro del polígono editado se
 * mueve ahí. Los que estaban en la zona editada y quedaron fuera, sin que
 * ningún OTRO polígono los reclame, se devuelven en `huerfanos` (no
 * desaparecen del todo: hay que ubicarlos a mano).
 */
export function recalcularMembresia(zonas: ZonaSimulada[], zonaEditadaId: string): { zonas: ZonaSimulada[]; huerfanos: AsignacionSimulada[] } {
  const editada = zonas.find((z) => z.id === zonaEditadaId);
  if (!editada || !editada.polygon) return { zonas, huerfanos: [] };
  const polygon = editada.polygon;

  const porCliId = new Map<number, AsignacionSimulada>();
  for (const z of zonas) for (const c of z.clientes) porCliId.set(c.cliId, c);

  const dentroIds = new Set<number>();
  for (const c of porCliId.values()) if (puntoEnPoligono(c.lat, c.lng, polygon)) dentroIds.add(c.cliId);

  const huerfanos: AsignacionSimulada[] = [];
  for (const c of editada.clientes) {
    if (dentroIds.has(c.cliId)) continue; // sigue dentro del polígono editado
    const otraLoReclama = zonas.some((z) => z.id !== zonaEditadaId && z.polygon && puntoEnPoligono(c.lat, c.lng, z.polygon));
    if (!otraLoReclama) huerfanos.push(c);
  }

  const nextZonas = zonas
    .map((z) => {
      if (z.id === zonaEditadaId) {
        // La zona editada conserva el polígono tal cual lo dejó el usuario
        // (es lo que acaba de dibujar/arrastrar) — solo se actualizan sus clientes.
        const clientes = [...porCliId.values()].filter((c) => dentroIds.has(c.cliId));
        return { ...z, clientes };
      }
      // Las demás zonas pueden haber perdido puntos a manos de la editada —
      // su polígono se recalcula para que siga reflejando quién quedó adentro.
      const clientes = z.clientes.filter((c) => !dentroIds.has(c.cliId));
      if (clientes.length === z.clientes.length) return z;
      return { ...z, clientes, polygon: calcularPoligono(clientes) };
    })
    .filter((z) => z.clientes.length > 0);

  return { zonas: nextZonas, huerfanos };
}
