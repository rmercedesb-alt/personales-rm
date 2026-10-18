import { useEffect, useMemo, useRef, useState } from "react";
import * as L from "leaflet";
import { MapContainer, TileLayer, GeoJSON, Popup, Tooltip, useMap } from "react-leaflet";
import type { ClienteRuta, DrillLevel, Selection } from "../types";
import { DIA_COLORS, colorForRep } from "../colors";
import { hullFor } from "../geo";
import { scopeForLevel } from "../scope";
import { ClienteMarker, formaParaSemanas } from "../clienteMarker";
import { AutoInvalidate, InvalidateOnChange } from "../leafletHelpers";

/** Abre/cierra el Popup ya existente en hover — así "ponerse encima" alcanza
 * para ver de qué se trata, sin tener que hacer clic (el clic sigue haciendo
 * drill-down, no se toca). */
const hoverAbrePopup = {
  mouseover: (e: L.LeafletMouseEvent) => e.target.openPopup(),
  mouseout: (e: L.LeafletMouseEvent) => e.target.closePopup(),
};

interface Group {
  key: string;
  label: string;
  color: string;
  semana: number;
  points: Array<[number, number]>;
  cliIds: Set<number>;
  items: ClienteRuta[];
}

/** Cada fila se "explota" en una instancia por cada semana (1-4) en la que
 * tiene visita activa — antes el polígono de un día mezclaba las 4 semanas
 * juntas, y como pueden ser zonas geográficas totalmente distintas (ej. un
 * cliente Quincenal 1-3 lejos de otro Quincenal 2-4 del mismo día), el hull
 * combinado salía absurdo (una franja que cruza todo el mapa). Separado por
 * semana, cada polígono representa la ruta real de esa semana puntual. */
function groupBy(
  data: ClienteRuta[],
  keyFn: (r: ClienteRuta, semana: number) => string,
  labelFn: (r: ClienteRuta, semana: number) => string,
  colorFn: (r: ClienteRuta) => string
): Group[] {
  const map = new Map<string, Group>();
  for (const r of data) {
    const semanas = r.semanasActivas.length > 0 ? r.semanasActivas : [0];
    for (const semana of semanas) {
      const key = keyFn(r, semana);
      let g = map.get(key);
      if (!g) {
        g = { key, label: labelFn(r, semana), color: colorFn(r), semana, points: [], cliIds: new Set(), items: [] };
        map.set(key, g);
      }
      g.points.push([r.lng, r.lat]);
      g.cliIds.add(r.cliId);
      g.items.push(r);
    }
  }
  return [...map.values()];
}

function FitBounds({ points }: { points: Array<[number, number]> }) {
  const map = useMap();
  useEffect(() => {
    if (points.length === 0) return;
    const latlngs = points.map(([lng, lat]) => [lat, lng] as [number, number]);
    if (latlngs.length === 1) {
      map.setView(latlngs[0], 14);
    } else {
      map.fitBounds(latlngs, { padding: [40, 40] });
    }
  }, [points, map]);
  return null;
}

interface Props {
  data: ClienteRuta[];
  level: DrillLevel;
  selection: Selection;
  showPoligono: boolean;
  showGeoref: boolean;
  onDrillSupervisor: (dia: string, supCodigo: string, supNombre: string) => void;
  onDrillRep: (repCodigo: string, repNombre: string) => void;
}

export function MapView({ data, level, selection, showPoligono, showGeoref, onDrillSupervisor, onDrillRep }: Props) {
  const [fullscreen, setFullscreen] = useState(false);
  const mapRef = useRef<L.Map | null>(null);
  const wrapRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function onFsChange() {
      setFullscreen(document.fullscreenElement === wrapRef.current);
      setTimeout(() => mapRef.current?.invalidateSize(), 120);
    }
    document.addEventListener("fullscreenchange", onFsChange);
    return () => document.removeEventListener("fullscreenchange", onFsChange);
  }, []);

  function handleToggleFullscreen() {
    if (document.fullscreenElement) document.exitFullscreen();
    else wrapRef.current?.requestFullscreen();
  }

  const scoped = useMemo(() => scopeForLevel(data, level, selection), [data, level, selection]);

  const groups = useMemo(() => {
    if (level === "overview") {
      return groupBy(
        scoped,
        (r, semana) => `${r.dia}||${semana}||${r.supCodigo ?? ""}`,
        (r, semana) => `${r.dia} · Semana ${semana} · ${r.supNombre}`,
        (r) => DIA_COLORS[r.dia] ?? "#8b949e"
      );
    }
    if (level === "supervisor") {
      return groupBy(
        scoped,
        (r, semana) => `${r.repCodigo}||${semana}`,
        (r, semana) => `${r.repNombre} · Semana ${semana}`,
        (r) => colorForRep(r.repCodigo)
      );
    }
    return [];
  }, [scoped, level]);

  // turf.convex + turf.buffer por grupo es caro: con "Todos" son cientos de polígonos y
  // antes se recalculaban en CADA render (abrir un desplegable bastaba para congelar la
  // página). Solo se recalcula cuando cambian los grupos, y solo si el polígono se muestra.
  const hulls = useMemo(() => {
    if (!showPoligono) return [];
    return groups.flatMap((g) => {
      const hull = hullFor(g.points);
      return hull ? [{ g, hull }] : [];
    });
  }, [groups, showPoligono]);

  const pointColor = (r: ClienteRuta) => (level === "overview" ? (DIA_COLORS[r.dia] ?? "#8b949e") : colorForRep(r.repCodigo));

  function handlePointClick(r: ClienteRuta) {
    if (level === "overview") onDrillSupervisor(r.dia, r.supCodigo ?? "", r.supNombre);
    else if (level === "supervisor") onDrillRep(r.repCodigo, r.repNombre);
  }

  const renderPoligono = showPoligono && (level === "overview" || level === "supervisor");
  const renderPuntos = level === "rep" || showGeoref;

  const allPoints = useMemo(() => scoped.map((r) => [r.lng, r.lat] as [number, number]), [scoped]);

  return (
    <div className="route-map-wrap" ref={wrapRef}>
      <div className="map-btn-row">
        <button className="map-refresh-btn" onClick={handleToggleFullscreen} title="Pantalla completa">
          {fullscreen ? "🗗 Salir de pantalla completa" : "⛶ Pantalla completa"}
        </button>
      </div>
      <MapContainer ref={mapRef} center={[18.48, -69.9]} zoom={11} style={{ width: "100%", height: "100%" }} preferCanvas>
        <TileLayer
          attribution='&copy; OpenStreetMap contributors'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />
        <FitBounds points={allPoints} />
        <AutoInvalidate />
        <InvalidateOnChange trigger={fullscreen} />

        {renderPoligono &&
          hulls.map(({ g, hull }) => {
            return (
              <GeoJSON
                key={g.key}
                data={hull}
                style={{ color: g.color, weight: 2, fillColor: g.color, fillOpacity: 0.25 }}
                eventHandlers={{
                  ...hoverAbrePopup,
                  click: () => {
                    const first = g.items[0];
                    if (level === "overview") onDrillSupervisor(first.dia, first.supCodigo ?? "", first.supNombre);
                    else onDrillRep(first.repCodigo, first.repNombre);
                  },
                }}
              >
                {showPoligono && !showGeoref && (
                  <Tooltip permanent direction="center" className="poligono-count-tooltip" interactive={false}>
                    {g.cliIds.size}
                  </Tooltip>
                )}
                <Popup>
                  <div className="popup-title">{g.label}</div>
                  {level === "supervisor" && (
                    <>
                      <div className="popup-line">Vendedor: {g.items[0].repNombre}</div>
                      <div className="popup-line">Día de visita: {g.items[0].dia}</div>
                    </>
                  )}
                  <div className="popup-line">{g.cliIds.size} clientes distintos (Semana {g.semana})</div>
                  {level === "overview" && (
                    <div className="popup-line">{new Set(g.items.map((i) => i.repCodigo)).size} vendedores</div>
                  )}
                  <div className="popup-line">Click para ver detalle →</div>
                </Popup>
              </GeoJSON>
            );
          })}

        {renderPuntos &&
          scoped.map((c) => (
            <ClienteMarker
              key={`${c.repCodigo}-${c.cliId}`}
              forma={formaParaSemanas(c.semanasActivas)}
              center={[c.lat, c.lng]}
              radius={level === "rep" ? 7 : 5}
              color={pointColor(c)}
              fillColor={pointColor(c)}
              fillOpacity={0.85}
              weight={1.5}
              eventHandlers={level !== "rep" ? { ...hoverAbrePopup, click: () => handlePointClick(c) } : hoverAbrePopup}
            >
              <Popup>
                <div className="popup-title">{c.cliNombre}</div>
                <div className="popup-line">Código: {c.cliCodigo}</div>
                <div className="popup-line">Vendedor: {c.repNombre}</div>
                <div className="popup-line">Día de visita: {c.dia}</div>
                <div className="popup-line">RutPosición: {c.rutPosicion ?? "-"}</div>
                <div className="popup-line">Frecuencia: {c.frecuencia}</div>
                <div className="popup-line">Tamaño: {c.claDescripcion}</div>
                <div className="popup-line">Tipo: {c.tinDescripcion}</div>
                {level !== "rep" && <div className="popup-line">Click para ver detalle →</div>}
              </Popup>
            </ClienteMarker>
          ))}
      </MapContainer>

      {(level === "overview" || renderPuntos) && (
        <div className="legend">
          {level === "overview" && (
            <>
              <div className="legend-sep">Día (color)</div>
              {Object.entries(DIA_COLORS)
                .filter(([dia]) => dia !== "Sin Asignar")
                .map(([dia, color]) => (
                  <div className="legend-item" key={dia}>
                    <span className="legend-dot" style={{ background: color }} />
                    {dia}
                  </div>
                ))}
            </>
          )}
          {renderPuntos && (
            <>
              <div className="legend-sep">Forma = frecuencia</div>
              <div className="legend-item">● Semanal</div>
              <div className="legend-item">▲ Quincenal (Sem. 1 y 3)</div>
              <div className="legend-item">■ Quincenal (Sem. 2 y 4)</div>
            </>
          )}
        </div>
      )}
    </div>
  );
}
