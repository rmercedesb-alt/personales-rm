import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import * as L from "leaflet";
import { MapContainer, TileLayer, CircleMarker, Polyline, Popup } from "react-leaflet";
import { DIAS_SEMANA, type CandidatoMapa, type ClienteCandidato, type DiaSemana, type RutaPorDia } from "../types";
import { DIA_COLORS, colorForRep } from "../colors";
import { AutoInvalidate, BoxSelect, FitBounds, InvalidateOnChange } from "../leafletHelpers";
import { ClienteMarker, formaParaSemanas } from "../clienteMarker";
import type { SugerenciaDia } from "./sugerirDia";

/** Sentinel para "sacar del día" desde el mismo selector de Asignar — no es un
 * DiaSemana real, así que nunca choca con los 7 días de verdad. */
export const SIN_ASIGNAR_DIA = "Sin Asignar" as const;
export type DiaOSinAsignar = DiaSemana | typeof SIN_ASIGNAR_DIA;

/** Debe coincidir con posicionParaIndice en server/src/routes/exportRuta.ts. */
function posicionParaIndice(idx: number): number {
  return idx === 0 ? 1 : idx * 5;
}

const SEMANAS = [1, 2, 3, 4];
// Renderizar los ~27k clientes sin asignar de una sola vez cuelga el navegador
// varios segundos (cada CircleMarker es un componente React real, no un pixel
// de canvas gratis). Se capa y se avisa, mismo criterio que el resto de la app.
const SIN_ASIGNAR_CAP = 3000;

interface Props {
  ruta: RutaPorDia;
  sinAsignar: ClienteCandidato[];
  candidatos: CandidatoMapa[];
  boardCollapsed: boolean;
  seleccionActiva: boolean;
  onToggleSeleccionActiva: () => void;
  puntosSeleccionados: Set<number>;
  onCambiarSeleccion: (ids: Set<number>) => void;
  /** `dia` puede ser el sentinel "Sin Asignar" (ver `SIN_ASIGNAR_DIA`) — quita
   * a los seleccionados de cualquier día en el que estén, sin ponerlos en
   * ninguno nuevo. */
  onAsignarSeleccion: (dia: DiaOSinAsignar, semanas: number[]) => void;
  /** "Modificar Ruta": los ya-asignados se ven de fondo/atenuados (referencia
   * geográfica) y solo los grises "sin asignar" son seleccionables — a
   * diferencia de Crear Ruta, donde todo lo dibujado es seleccionable. */
  modoModificar?: boolean;
  /** Sugerencia de día por cercanía a los ya-asignados del mismo vendedor (ver sugerirDia.ts). */
  sugerencias?: Map<number, SugerenciaDia>;
  /** Asigna cada punto seleccionado a SU PROPIO día sugerido (en vez de un único día para todos). */
  onAsignarSugerencias?: (semanas: number[]) => void;
}

export function RutaMapLive({
  ruta,
  sinAsignar,
  candidatos,
  boardCollapsed,
  seleccionActiva,
  onToggleSeleccionActiva,
  puntosSeleccionados,
  onCambiarSeleccion,
  onAsignarSeleccion,
  modoModificar = false,
  sugerencias,
  onAsignarSugerencias,
}: Props) {
  const [diaAsignar, setDiaAsignar] = useState<DiaOSinAsignar>("Lunes");
  const [semanasAsignar, setSemanasAsignar] = useState<number[]>([1, 2, 3, 4]);
  const [fullscreen, setFullscreen] = useState(false);
  const mapRef = useRef<L.Map | null>(null);
  const wrapRef = useRef<HTMLDivElement>(null);

  const sinAsignarVisible = useMemo(() => sinAsignar.slice(0, SIN_ASIGNAR_CAP), [sinAsignar]);

  useEffect(() => {
    function onFsChange() {
      setFullscreen(document.fullscreenElement === wrapRef.current);
      setTimeout(() => mapRef.current?.invalidateSize(), 120);
    }
    document.addEventListener("fullscreenchange", onFsChange);
    return () => document.removeEventListener("fullscreenchange", onFsChange);
  }, []);

  function handleToggleFullscreen() {
    if (document.fullscreenElement) {
      document.exitFullscreen();
    } else {
      wrapRef.current?.requestFullscreen();
    }
  }

  const allPoints = useMemo(() => {
    const pts: Array<[number, number]> = [];
    for (const dia of DIAS_SEMANA) for (const c of ruta[dia]) pts.push([c.lat, c.lng]);
    for (const c of candidatos) pts.push([c.lat, c.lng]);
    return pts;
  }, [ruta, candidatos]);

  const diasConDatos = useMemo(() => DIAS_SEMANA.filter((d) => ruta[d].length > 0), [ruta]);

  const origenesConDatos = useMemo(() => {
    const map = new Map<string, { nombre: string; count: number }>();
    for (const c of candidatos) {
      const e = map.get(c.origenRepCodigo);
      if (e) e.count++;
      else map.set(c.origenRepCodigo, { nombre: c.origenRepNombre, count: 1 });
    }
    return [...map.entries()];
  }, [candidatos]);

  function handleBoxSelect(bounds: L.LatLngBounds) {
    const ids = new Set<number>();
    // `ruta` ya viene filtrada por los chips Semana/Día cuando modoModificar
    // está prendido (ver `rutaFondo` en ModificarRutaEditor) — por eso alcanza
    // con escanearla siempre: lo que está "apagado" en los filtros ya ni
    // siquiera llega acá, y lo que está prendido pasa a ser seleccionable
    // (antes en Modificar Ruta solo se podían tomar los "sin asignar"; ahora
    // también los ya-asignados que el filtro deje ver, para poder moverlos de
    // día/semana o resecuenciarlos).
    for (const dia of DIAS_SEMANA) for (const c of ruta[dia]) if (bounds.contains([c.lat, c.lng])) ids.add(c.cliId);
    if (!modoModificar) {
      for (const c of candidatos) if (bounds.contains([c.lat, c.lng])) ids.add(c.cliId);
    }
    for (const c of sinAsignarVisible) if (bounds.contains([c.lat, c.lng])) ids.add(c.cliId);
    onCambiarSeleccion(ids);
  }

  function toggleSemanaAsignar(s: number) {
    setSemanasAsignar((prev) => (prev.includes(s) ? prev.filter((x) => x !== s) : [...prev, s].sort()));
  }

  function handleRefresh() {
    const map = mapRef.current;
    if (!map) return;
    map.invalidateSize();
    if (allPoints.length === 1) map.setView(allPoints[0], 14);
    else if (allPoints.length > 1) map.fitBounds(allPoints, { padding: [40, 40] });
  }

  return (
    <div className="route-map-wrap" ref={wrapRef}>
      <div className="map-btn-row">
        <button
          className={`map-refresh-btn ${seleccionActiva ? "active" : ""}`}
          onClick={onToggleSeleccionActiva}
          title={modoModificar ? "Seleccionar con un rectángulo (sin asignar + lo que esté prendido en los filtros de Semana/Día)" : "Seleccionar clientes con un rectángulo (incluye los grises sin asignar)"}
        >
          {seleccionActiva ? "✕ Cancelar selección" : "▭ Seleccionar en mapa"}
        </button>
        <button className="map-refresh-btn" onClick={handleRefresh} title="Recalcular tamaño y reencuadrar el mapa">
          🔄 Refrescar mapa
        </button>
        <button className="map-refresh-btn" onClick={handleToggleFullscreen} title="Pantalla completa">
          {fullscreen ? "🗗 Salir de pantalla completa" : "⛶ Pantalla completa"}
        </button>
      </div>
      <MapContainer ref={mapRef} center={[18.48, -69.9]} zoom={11} style={{ width: "100%", height: "100%" }} preferCanvas>
        <TileLayer attribution="&copy; OpenStreetMap contributors" url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
        <FitBounds points={allPoints} />
        <AutoInvalidate />
        <InvalidateOnChange trigger={boardCollapsed} />
        <InvalidateOnChange trigger={fullscreen} />
        <BoxSelect active={seleccionActiva} onSelect={handleBoxSelect} />

        {sinAsignarVisible.map((c) => {
          const sel = puntosSeleccionados.has(c.cliId);
          const sugerencia = sugerencias?.get(c.cliId);
          const base = sugerencia ? (DIA_COLORS[sugerencia.dia] ?? "#c9d1d9") : "#c9d1d9";
          return (
            <CircleMarker
              key={`sa-${c.cliId}`}
              center={[c.lat, c.lng]}
              radius={modoModificar ? (sel ? 9 : 6) : sel ? 7 : 3}
              pathOptions={{
                color: sel ? "#ffffff" : base,
                fillColor: base,
                fillOpacity: modoModificar ? (sel ? 0.95 : 0.75) : sel ? 0.95 : 0.4,
                weight: sel ? 3 : modoModificar ? 1.5 : 1,
                interactive: modoModificar,
              }}
            >
              {modoModificar && sugerencia && (
                <Popup>
                  <div className="popup-title">{c.cliNombre}</div>
                  <div className="popup-line">Código: {c.cliCodigo}</div>
                  <div className="popup-line">Sugerido: {sugerencia.dia} ({sugerencia.distanciaKm.toFixed(1)} km del más cercano)</div>
                </Popup>
              )}
            </CircleMarker>
          );
        })}

        {DIAS_SEMANA.map((dia) => {
          const items = ruta[dia];
          if (items.length === 0) return null;
          const color = DIA_COLORS[dia] ?? "#8b949e";
          const linea = items.map((c) => [c.lat, c.lng] as [number, number]);
          return (
            <Fragment key={dia}>
              {items.length > 1 && (
                <Polyline positions={linea} pathOptions={{ color, weight: 2, opacity: modoModificar ? 0.25 : 0.6, dashArray: "4 6" }} />
              )}
              {items.map((c, i) => {
                const sel = puntosSeleccionados.has(c.cliId);
                return (
                  <ClienteMarker
                    key={c.cliId}
                    forma={formaParaSemanas(c.semanasActivas)}
                    center={[c.lat, c.lng]}
                    radius={modoModificar ? (sel ? 9 : 4) : sel ? 9 : 6}
                    color={sel ? "#ffffff" : color}
                    fillColor={color}
                    fillOpacity={modoModificar ? (sel ? 0.85 : 0.22) : 0.85}
                    weight={sel ? 3 : modoModificar ? 1 : 1.5}
                  >
                    <Popup>
                      <div className="popup-title">{c.cliNombre}</div>
                      <div className="popup-line">Código: {c.cliCodigo}</div>
                      <div className="popup-line">Día: {dia}</div>
                      <div className="popup-line">Posición: {posicionParaIndice(i)}</div>
                    </Popup>
                  </ClienteMarker>
                );
              })}
            </Fragment>
          );
        })}

        {candidatos.map((c) => {
          const color = colorForRep(c.origenRepCodigo);
          const sel = puntosSeleccionados.has(c.cliId);
          return (
            <CircleMarker
              key={`cand-${c.cliId}`}
              center={[c.lat, c.lng]}
              radius={sel ? 8 : 5}
              pathOptions={{
                color: sel ? "#ffffff" : color,
                fillColor: color,
                fillOpacity: sel ? 0.9 : 0.35,
                weight: sel ? 3 : 2,
                dashArray: sel ? undefined : "2 2",
              }}
            >
              <Popup>
                <div className="popup-title">{c.cliNombre}</div>
                <div className="popup-line">Código: {c.cliCodigo}</div>
                <div className="popup-line">Vendedor origen: {c.origenRepCodigo} - {c.origenRepNombre}</div>
                <div className="popup-line">{c.enRuta ? `Ya en ruta: ${c.dia}` : "Sin ruta (dueño en maestro de Clientes)"}</div>
              </Popup>
            </CircleMarker>
          );
        })}
      </MapContainer>

      {(diasConDatos.length > 0 || origenesConDatos.length > 0 || sinAsignar.length > 0) && (
        <div className="legend">
          {sinAsignar.length > 0 && (
            <div className="legend-item">
              <span className="legend-dot" style={{ background: "#c9d1d9" }} />
              Sin asignar ({sinAsignarVisible.length.toLocaleString()}
              {sinAsignar.length > SIN_ASIGNAR_CAP ? ` de ${sinAsignar.length.toLocaleString()}` : ""})
            </div>
          )}
          {diasConDatos.map((dia) => (
            <div className="legend-item" key={dia}>
              <span className="legend-dot" style={{ background: DIA_COLORS[dia] ?? "#8b949e" }} />
              {dia} ({ruta[dia].length})
            </div>
          ))}
          {diasConDatos.length > 0 && (
            <>
              <div className="legend-sep">Forma = frecuencia</div>
              <div className="legend-item">● Semanal</div>
              <div className="legend-item">▲ Quincenal (Sem. 1 y 3)</div>
              <div className="legend-item">■ Quincenal (Sem. 2 y 4)</div>
            </>
          )}
          {origenesConDatos.length > 0 && (
            <>
              <div className="legend-sep">Cargados (contorno punteado)</div>
              {origenesConDatos.map(([codigo, info]) => (
                <div className="legend-item" key={codigo}>
                  <span className="legend-dot" style={{ background: colorForRep(codigo) }} />
                  {codigo} - {info.nombre} ({info.count})
                </div>
              ))}
            </>
          )}
        </div>
      )}

      {seleccionActiva && puntosSeleccionados.size === 0 && (
        <div className="map-select-hint">Arrastrá un rectángulo sobre el mapa para seleccionar clientes</div>
      )}

      {puntosSeleccionados.size > 0 && (
        <div className="map-assign-bar">
          <div className="map-assign-title">{puntosSeleccionados.size} clientes seleccionados</div>
          <div className="map-assign-row">
            <select value={diaAsignar} onChange={(e) => setDiaAsignar(e.target.value as DiaOSinAsignar)}>
              <option value={SIN_ASIGNAR_DIA}>(Sin Asignar)</option>
              {DIAS_SEMANA.map((d) => (
                <option key={d} value={d}>{d}</option>
              ))}
            </select>
            <span className="week-toggle">
              {SEMANAS.map((s) => (
                <button key={s} className={semanasAsignar.includes(s) ? "active" : ""} onClick={() => toggleSemanaAsignar(s)}>
                  {s}
                </button>
              ))}
            </span>
          </div>
          <div className="map-assign-row">
            <button
              className="export-btn"
              disabled={diaAsignar !== SIN_ASIGNAR_DIA && semanasAsignar.length === 0}
              onClick={() => onAsignarSeleccion(diaAsignar, semanasAsignar)}
            >
              {diaAsignar === SIN_ASIGNAR_DIA ? "Quitar del día" : `Asignar a ${diaAsignar}`}
            </button>
            {modoModificar && sugerencias && (
              <button
                className="export-btn"
                disabled={semanasAsignar.length === 0 || ![...puntosSeleccionados].every((id) => sugerencias.has(id))}
                onClick={() => onAsignarSugerencias?.(semanasAsignar)}
                title="A cada cliente lo asigna al día que le fue sugerido (no a un único día para todos)"
              >
                ✓ Asignar cada uno a su sugerido
              </button>
            )}
            <button className="board-toggle-btn" onClick={() => onCambiarSeleccion(new Set())}>
              Limpiar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
