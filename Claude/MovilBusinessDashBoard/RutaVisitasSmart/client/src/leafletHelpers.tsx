import { useEffect, useRef } from "react";
import { useMap } from "react-leaflet";
import * as L from "leaflet";

/** Encuadra el mapa a los puntos dados (o hace zoom a un único punto). */
export function FitBounds({ points }: { points: Array<[number, number]> }) {
  const map = useMap();
  useEffect(() => {
    if (points.length === 0) return;
    if (points.length === 1) {
      map.setView(points[0], 14);
    } else {
      map.fitBounds(points, { padding: [40, 40] });
    }
  }, [points, map]);
  return null;
}

/**
 * Leaflet no se entera solo cuando su contenedor cambia de tamaño (ej. al
 * ocultar/mostrar el tablero de días) — sigue pintando con las medidas
 * viejas y los puntos quedan mal ubicados o "vacíos". Un ResizeObserver
 * sobre el contenedor real lo corrige automáticamente, para cualquier causa
 * de resize (no solo el toggle de días).
 */
export function AutoInvalidate() {
  const map = useMap();
  useEffect(() => {
    const container = map.getContainer();
    const ro = new ResizeObserver(() => map.invalidateSize());
    ro.observe(container);
    return () => ro.disconnect();
  }, [map]);
  return null;
}

/**
 * Complemento a AutoInvalidate para casos EXACTOS y conocidos (colapsar el
 * tablero de días, entrar/salir de pantalla completa): dispara
 * invalidateSize apenas cambia `trigger`, sin depender de que el
 * ResizeObserver llegue a disparar. Usa un pequeño setTimeout porque el
 * cambio de tamaño por CSS (transition) todavía no terminó en el mismo tick
 * en que React re-renderiza.
 */
export function InvalidateOnChange({ trigger }: { trigger: unknown }) {
  const map = useMap();
  useEffect(() => {
    const t = setTimeout(() => map.invalidateSize(), 180);
    return () => clearTimeout(t);
  }, [trigger, map]);
  return null;
}

/** Herramienta de selección por rectángulo: arrastra con el mouse sobre el mapa. */
export function BoxSelect({ active, onSelect }: { active: boolean; onSelect: (bounds: L.LatLngBounds) => void }) {
  const map = useMap();
  const dragRef = useRef<{ startPx: [number, number]; div: HTMLDivElement } | null>(null);

  useEffect(() => {
    const container = map.getContainer();
    if (!active) {
      map.dragging.enable();
      container.classList.remove("map-select-mode");
      return;
    }
    map.dragging.disable();
    container.classList.add("map-select-mode");

    function toLocal(e: MouseEvent): [number, number] {
      const rect = container.getBoundingClientRect();
      return [e.clientX - rect.left, e.clientY - rect.top];
    }

    function onMouseDown(e: MouseEvent) {
      const [x, y] = toLocal(e);
      const div = document.createElement("div");
      div.className = "map-select-box";
      div.style.left = `${x}px`;
      div.style.top = `${y}px`;
      container.appendChild(div);
      dragRef.current = { startPx: [x, y], div };
    }

    function onMouseMove(e: MouseEvent) {
      if (!dragRef.current) return;
      const [x, y] = toLocal(e);
      const [sx, sy] = dragRef.current.startPx;
      Object.assign(dragRef.current.div.style, {
        left: `${Math.min(sx, x)}px`,
        top: `${Math.min(sy, y)}px`,
        width: `${Math.abs(x - sx)}px`,
        height: `${Math.abs(y - sy)}px`,
      });
    }

    function onMouseUp(e: MouseEvent) {
      if (!dragRef.current) return;
      const [x, y] = toLocal(e);
      const [sx, sy] = dragRef.current.startPx;
      dragRef.current.div.remove();
      const moved = Math.abs(x - sx) > 4 && Math.abs(y - sy) > 4;
      const p1 = map.containerPointToLatLng([sx, sy] as unknown as L.Point);
      const p2 = map.containerPointToLatLng([x, y] as unknown as L.Point);
      dragRef.current = null;
      if (moved) onSelect(L.latLngBounds(p1, p2));
    }

    container.addEventListener("mousedown", onMouseDown);
    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
    return () => {
      container.removeEventListener("mousedown", onMouseDown);
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
      dragRef.current?.div.remove();
      dragRef.current = null;
    };
  }, [active, map]);

  return null;
}
