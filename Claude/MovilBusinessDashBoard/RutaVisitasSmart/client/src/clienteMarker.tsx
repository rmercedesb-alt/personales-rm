import { useMemo, type ReactNode } from "react";
import * as L from "leaflet";
import { CircleMarker, Marker } from "react-leaflet";

export type FormaCliente = "circulo" | "triangulo" | "cuadrado";

/**
 * Semanal (las 4 semanas activas) = círculo (forma de siempre, sin cambios).
 * Quincenal Semana 1+3 = triángulo. Quincenal Semana 2+4 = cuadrado. Cualquier
 * otra combinación (personalizada, o sin semanas activas) se deja como
 * círculo — la regla solo la pidió el usuario para distinguir los dos
 * patrones de quincena entre sí.
 */
export function formaParaSemanas(semanasActivas: number[]): FormaCliente {
  const set = new Set(semanasActivas);
  if (set.size === 2 && set.has(1) && set.has(3)) return "triangulo";
  if (set.size === 2 && set.has(2) && set.has(4)) return "cuadrado";
  return "circulo";
}

function trazoForma(forma: FormaCliente, size: number, r: number): string {
  const c = size / 2;
  if (forma === "triangulo") {
    const h = r * 1.7;
    const topY = c - h * 0.55;
    const botY = c + h * 0.45;
    const halfW = h * 0.55;
    return `<polygon points="${c},${topY} ${c - halfW},${botY} ${c + halfW},${botY}" />`;
  }
  if (forma === "cuadrado") {
    const s = r * 1.6;
    return `<rect x="${c - s / 2}" y="${c - s / 2}" width="${s}" height="${s}" />`;
  }
  return `<circle cx="${c}" cy="${c}" r="${r}" />`;
}

interface FormaIconOpts {
  radius: number;
  color: string;
  fillColor: string;
  fillOpacity: number;
  weight: number;
}

function formaIcon(forma: FormaCliente, opts: FormaIconOpts): L.DivIcon {
  // Un poco más grande que el radio "real" — un triángulo/cuadrado inscrito
  // en el mismo radio que un círculo se ve visualmente más chico.
  const size = Math.ceil(opts.radius * 2.5) + opts.weight * 2;
  const r = size / 2 - opts.weight;
  const html = `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg" style="display:block">
    <g fill="${opts.fillColor}" fill-opacity="${opts.fillOpacity}" stroke="${opts.color}" stroke-width="${opts.weight}">
      ${trazoForma(forma, size, r)}
    </g>
  </svg>`;
  return L.divIcon({ html, className: "cliente-shape-icon", iconSize: [size, size], iconAnchor: [size / 2, size / 2] });
}

interface ClienteMarkerProps {
  forma: FormaCliente;
  center: [number, number];
  radius: number;
  color: string;
  fillColor: string;
  fillOpacity: number;
  weight: number;
  interactive?: boolean;
  eventHandlers?: L.LeafletEventHandlerFnMap;
  children?: ReactNode;
}

/** Círculo = `CircleMarker` de siempre (más liviano). Triángulo/cuadrado =
 * `Marker` con un ícono SVG armado al vuelo — mismo look (color/relleno/
 * grosor) que un CircleMarker equivalente, para que seleccionar/resaltar se
 * vea igual sea cual sea la forma.
 *
 * OJO: `interactive` tiene que llegar SIEMPRE como boolean explícito acá
 * abajo — pasarle `undefined` a `<Marker interactive={undefined}>` no es lo
 * mismo que omitir la prop: Leaflet mergea las opciones con `L.extend`, que
 * SÍ copia claves con valor `undefined`, pisando el default `true` de Marker
 * y dejando el marcador sin clic ni hover (bug real que apareció al agregar
 * el hover-popup: los triángulo/cuadrado quedaban completamente inertes). */
export function ClienteMarker({ forma, center, radius, color, fillColor, fillOpacity, weight, interactive = true, eventHandlers, children }: ClienteMarkerProps) {
  const icon = useMemo(
    () => (forma === "circulo" ? null : formaIcon(forma, { radius, color, fillColor, fillOpacity, weight })),
    [forma, radius, color, fillColor, fillOpacity, weight]
  );

  if (icon) {
    return (
      <Marker position={center} icon={icon} interactive={interactive} eventHandlers={eventHandlers}>
        {children}
      </Marker>
    );
  }
  return (
    <CircleMarker center={center} radius={radius} pathOptions={{ color, fillColor, fillOpacity, weight, interactive }} eventHandlers={eventHandlers}>
      {children}
    </CircleMarker>
  );
}
