import * as L from "leaflet";

/** Formas SVG simples, una por índice de vendedor (cicla si hay más vendedores que formas). */
const SHAPES: Array<(size: number) => string> = [
  (s) => `<circle cx="${s / 2}" cy="${s / 2}" r="${s / 2 - 1}" />`,
  (s) => `<rect x="1" y="1" width="${s - 2}" height="${s - 2}" />`,
  (s) => `<polygon points="${s / 2},1 ${s - 1},${s - 1} 1,${s - 1}" />`,
  (s) => `<polygon points="${s / 2},1 ${s - 1},${s / 2} ${s / 2},${s - 1} 1,${s / 2}" />`,
  (s) => {
    const cx = s / 2;
    const cy = s / 2;
    const rOuter = s / 2 - 1;
    const rInner = rOuter * 0.45;
    let pts = "";
    for (let i = 0; i < 10; i++) {
      const r = i % 2 === 0 ? rOuter : rInner;
      const ang = -Math.PI / 2 + (i * Math.PI) / 5;
      pts += `${cx + r * Math.cos(ang)},${cy + r * Math.sin(ang)} `;
    }
    return `<polygon points="${pts.trim()}" />`;
  },
];

export const SHAPE_NAMES = ["Círculo", "Cuadrado", "Triángulo", "Rombo", "Estrella"];

export function vendorSvg(shapeIdx: number, color: string, size = 16, seleccionado = false, opacity = 1): string {
  const stroke = seleccionado ? "#ffffff" : "#0d1117";
  const strokeWidth = seleccionado ? 2 : 1;
  return `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg"><g opacity="${opacity}" fill="${color}" stroke="${stroke}" stroke-width="${strokeWidth}">${SHAPES[shapeIdx % SHAPES.length](size)}</g></svg>`;
}

/** `tenue` = cliente que todavía no tiene asignación en la propuesta: se dibuja
 * más chico y desvanecido para que no se confunda con uno ya asignado. */
export function crearVendedorIcon(shapeIdx: number, color: string, seleccionado = false, tenue = false): L.DivIcon {
  const size = seleccionado ? 22 : tenue ? 14 : 16;
  return L.divIcon({
    html: vendorSvg(shapeIdx, color, size, seleccionado, tenue && !seleccionado ? 0.5 : 1),
    className: "sim-vendor-marker",
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2],
  });
}
