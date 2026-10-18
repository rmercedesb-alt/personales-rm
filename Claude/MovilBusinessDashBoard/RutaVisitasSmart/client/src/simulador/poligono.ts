import * as turf from "@turf/turf";

/** Envolvente convexa de un grupo de clientes — el polígono que se dibuja por
 * zona en el mapa. Se recalcula cada vez que cambia la membresía de una zona
 * (reasignar vendedor, excluir, quitar, etc.) para que el polígono siempre
 * refleje quién está de verdad adentro. */
export function calcularPoligono(clientes: Array<{ lat: number; lng: number }>): [number, number][] | null {
  if (clientes.length < 3) return null;
  try {
    const fc = turf.featureCollection(clientes.map((c) => turf.point([c.lng, c.lat])));
    const hull = turf.convex(fc);
    if (!hull) return null;
    return hull.geometry.coordinates[0].map(([lng, lat]) => [lat, lng] as [number, number]);
  } catch {
    return null;
  }
}
