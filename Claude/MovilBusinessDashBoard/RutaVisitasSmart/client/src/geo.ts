import * as turf from "@turf/turf";
import type { Feature, Polygon } from "geojson";

/** Envolvente (convex hull) de un grupo de puntos, con relleno para que sea clicable. */
export function hullFor(points: Array<[number, number]>): Feature<Polygon> | null {
  if (points.length === 0) return null;

  const fc = turf.featureCollection(points.map((p) => turf.point(p)));

  if (points.length < 3) {
    const center = points.length === 1 ? turf.point(points[0]) : turf.center(fc);
    return turf.circle(center, 0.25, { units: "kilometers", steps: 24 });
  }

  const hull = turf.convex(fc);
  if (!hull) {
    const center = turf.center(fc);
    return turf.circle(center, 0.25, { units: "kilometers", steps: 24 });
  }
  return turf.buffer(hull, 0.08, { units: "kilometers" }) as Feature<Polygon>;
}
