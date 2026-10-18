import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import * as L from "leaflet";
import { MapContainer, TileLayer, CircleMarker, Marker, Polygon, Popup } from "react-leaflet";
import { DIAS_SEMANA, type ClienteTerritorio, type DiaSemana, type ZonaSimulada } from "../types";
import { DIA_COLORS } from "../colors";
import { AutoInvalidate, BoxSelect, FitBounds, InvalidateOnChange } from "../leafletHelpers";
import { crearVendedorIcon, vendorSvg, SHAPE_NAMES } from "./vendorIcons";
import { Multiselect } from "../components/Multiselect";
import type { VendedorOpcion } from "../routeEditor/DayColumn";

const verticeIcon = L.divIcon({
  html: `<div class="sim-vertex-handle"></div>`,
  className: "",
  iconSize: [12, 12],
  iconAnchor: [6, 6],
});

const SEMANAS = [1, 2, 3, 4];
const SIN_ASIGNAR = "Sin asignar";
const SIN_SUPERVISOR = "(Sin Supervisor)";
const SIN_DIA_ACTUAL = "Sin día previo";
/** Por encima de esta cantidad de clientes aún sin asignar, se dibujan como
 * círculos de canvas (coloreados por día actual) en vez de un ícono DOM por
 * vendedor — miles de íconos SVG a la vez traban el navegador. */
const UMBRAL_ICONOS_SIN_ASIGNAR = 3500;
/** Sentinel del selector de día del panel de asignar — no es un DiaSemana real:
 * significa "sacar de la ruta sugerida" (mismo efecto que "Excluir de la
 * propuesta", solo que accesible desde el mismo flujo de asignar). */
const SIN_DIA = "__SIN_DIA__";

interface Props {
  territorio: ClienteTerritorio[];
  zonas: ZonaSimulada[] | null;
  /** Orden estable de vendedores (define el índice de ícono de cada uno). */
  vendedorCodigos: string[];
  vendedorNombrePorCodigo: Map<string, string>;
  /** repCodigo -> nombre del supervisor (o "(Sin Supervisor)"), para el filtro. */
  vendedorSupervisorPorCodigo?: Map<string, string>;
  /** Cubre TAMBIÉN a los salientes — hace falta para mostrar el nombre del
   * vendedor/día "anterior" de un cliente en el popup, aunque ese vendedor ya
   * no esté entre los que quedan. */
  nombrePorCodigoTodos?: Map<string, string>;
  /** Arrastrar un vértice de un polígono de zona reasigna clientes (ver polygonMembership.ts). */
  onVertexDrag?: (zonaId: string, vertexIdx: number, lat: number, lng: number) => void;
  mostrarPoligonos?: boolean;
  /**
   * Excluir clientes directo en el mapa (seleccionar con rectángulo y sacarlos
   * de la propuesta). Si no se pasa, no se muestra el botón de selección.
   */
  onExcluir?: (cliIds: number[]) => void;
  /** Asignar clientes seleccionados a cualquier vendedor/día/semanas, directo en el mapa. */
  onAsignar?: (cliIds: number[], dia: DiaSemana, repCodigo: string, semanasActivas: number[]) => void;
  vendedorOpciones?: VendedorOpcion[];
}

export function SimuladorMapa({
  territorio,
  zonas,
  vendedorCodigos,
  vendedorNombrePorCodigo,
  vendedorSupervisorPorCodigo,
  nombrePorCodigoTodos,
  onVertexDrag,
  mostrarPoligonos = true,
  onExcluir,
  onAsignar,
  vendedorOpciones,
}: Props) {
  const [fullscreen, setFullscreen] = useState(false);
  const [modoSeleccion, setModoSeleccion] = useState(false);
  const [seleccion, setSeleccion] = useState<Set<number>>(new Set());
  const [diaAsignar, setDiaAsignar] = useState<string>("Lunes");
  const [repAsignar, setRepAsignar] = useState<string>("");
  const [semanasAsignar, setSemanasAsignar] = useState<number[]>([1, 2, 3, 4]);
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

  // ---------------------------------------------------------------- filtros
  const [filtroSemanas, setFiltroSemanas] = useState<Set<string>>(new Set());
  const [filtroDias, setFiltroDias] = useState<Set<string>>(new Set());
  const [filtroVendedores, setFiltroVendedores] = useState<Set<string>>(new Set());
  const [filtroSupervisores, setFiltroSupervisores] = useState<Set<string>>(new Set());
  const [filtroFrecuencias, setFiltroFrecuencias] = useState<Set<string>>(new Set());

  const hayFiltrosActivos =
    filtroSemanas.size > 0 || filtroDias.size > 0 || filtroVendedores.size > 0 || filtroSupervisores.size > 0 || filtroFrecuencias.size > 0;

  function limpiarFiltros() {
    setFiltroSemanas(new Set());
    setFiltroDias(new Set());
    setFiltroVendedores(new Set());
    setFiltroSupervisores(new Set());
    setFiltroFrecuencias(new Set());
  }

  const vendedorLabel = (repCodigo: string) => `${repCodigo} - ${vendedorNombrePorCodigo.get(repCodigo) ?? repCodigo}`;
  const supervisorDe = (repCodigo: string) => vendedorSupervisorPorCodigo?.get(repCodigo) || SIN_SUPERVISOR;

  // `vendedorCodigos` incluye a todos los seleccionados (para dar ícono incluso a
  // los clientes que aún no tienen vendedor propuesto) — pero filtrar la
  // propuesta por un vendedor sin zonas no devolvería nada, así que las opciones
  // salen solo de los que ya tienen clientes asignados.
  const codigosConZona = useMemo(() => new Set((zonas ?? []).map((z) => z.repCodigo)), [zonas]);
  const opcionesVendedor = useMemo(
    () => vendedorCodigos.filter((c) => codigosConZona.has(c)).map(vendedorLabel),
    [vendedorCodigos, codigosConZona, vendedorNombrePorCodigo]
  );
  const opcionesSupervisor = useMemo(
    () => [...new Set(vendedorCodigos.filter((c) => codigosConZona.has(c)).map(supervisorDe))].sort(),
    [vendedorCodigos, codigosConZona, vendedorSupervisorPorCodigo]
  );
  const opcionesDia = useMemo<string[]>(() => [...DIAS_SEMANA, SIN_ASIGNAR], []);
  const opcionesFrecuencia = ["Semanal", "Quincenal"];

  /** Una zona pasa el filtro si cumple las 5 dimensiones activas a la vez. */
  function zonaPasaFiltro(z: ZonaSimulada): boolean {
    if (filtroSemanas.size > 0 && !z.semanasActivas.some((s) => filtroSemanas.has(String(s)))) return false;
    if (filtroDias.size > 0 && !filtroDias.has(z.dia)) return false;
    if (filtroVendedores.size > 0 && !filtroVendedores.has(vendedorLabel(z.repCodigo))) return false;
    if (filtroSupervisores.size > 0 && !filtroSupervisores.has(supervisorDe(z.repCodigo))) return false;
    if (filtroFrecuencias.size > 0 && !filtroFrecuencias.has(z.frecuencia)) return false;
    return true;
  }
  /** Los "sin asignar" (grises) solo pueden cumplir el filtro de Día (si se
   * pidió explícitamente "Sin asignar") — no tienen vendedor/frecuencia/semana
   * propios todavía, así que cualquier otro filtro activo los descarta. */
  const sinAsignarPasaFiltro =
    filtroSemanas.size === 0 &&
    filtroVendedores.size === 0 &&
    filtroSupervisores.size === 0 &&
    filtroFrecuencias.size === 0 &&
    (filtroDias.size === 0 || filtroDias.has(SIN_ASIGNAR));

  // Clientes que NO están en ninguna zona todavía — se muestran siempre como
  // puntos neutros (antes de generar cualquier propuesta, es el territorio
  // completo; con una propuesta a medio armar a mano — "Clientes sin
  // Propuesta" — son los que faltan por asignar; con una propuesta del
  // algoritmo, normalmente no queda ninguno salvo algún huérfano).
  const asignadosIds = useMemo(() => {
    const s = new Set<number>();
    if (zonas) for (const z of zonas) for (const c of z.clientes) s.add(c.cliId);
    return s;
  }, [zonas]);
  const sinAsignar = useMemo(() => territorio.filter((c) => !asignadosIds.has(c.cliId)), [territorio, asignadosIds]);
  const sinAsignarVisible = sinAsignarPasaFiltro ? sinAsignar : [];

  // Zonas (y dentro de ellas, ninguna filtra cliente a cliente: la frecuencia,
  // semanas, vendedor y día son propiedades de LA ZONA, comparten todos sus
  // clientes) — filtrar es simplemente incluir o no la zona completa.
  const zonasVisibles = useMemo(() => (zonas ? zonas.filter(zonaPasaFiltro) : []), [
    zonas,
    filtroSemanas,
    filtroDias,
    filtroVendedores,
    filtroSupervisores,
    filtroFrecuencias,
  ]);

  const allPoints = useMemo<Array<[number, number]>>(() => {
    const zonaPts = zonasVisibles.flatMap((z) => z.clientes.map((c) => [c.lat, c.lng] as [number, number]));
    const sinAsignarPts = sinAsignarVisible.map((c) => [c.lat, c.lng] as [number, number]);
    return [...zonaPts, ...sinAsignarPts];
  }, [zonasVisibles, sinAsignarVisible]);

  // Puntos realmente visibles ahora mismo (asignados + sin asignar, YA con el
  // filtro aplicado) — es lo que la selección por rectángulo del mapa debe
  // poder marcar: si algo está oculto por un filtro, no se puede seleccionar.
  const puntosVisibles = useMemo<Array<{ cliId: number; lat: number; lng: number }>>(() => {
    const zonaPts = zonasVisibles.flatMap((z) => z.clientes.map((c) => ({ cliId: c.cliId, lat: c.lat, lng: c.lng })));
    const sinAsignarPts = sinAsignarVisible.map((c) => ({ cliId: c.cliId, lat: c.lat, lng: c.lng }));
    return [...zonaPts, ...sinAsignarPts];
  }, [zonasVisibles, sinAsignarVisible]);

  // Conteos FILTRADOS para la leyenda — "cuántos clientes visibles ahora
  // mismo caen en cada día / cada vendedor", no el total de la propuesta.
  const conteoPorDia = useMemo(() => {
    const m = new Map<string, number>();
    for (const z of zonasVisibles) m.set(z.dia, (m.get(z.dia) ?? 0) + z.clientes.length);
    if (sinAsignarVisible.length > 0) m.set(SIN_ASIGNAR, (m.get(SIN_ASIGNAR) ?? 0) + sinAsignarVisible.length);
    return m;
  }, [zonasVisibles, sinAsignarVisible]);
  const conteoPorVendedor = useMemo(() => {
    const m = new Map<string, number>();
    for (const z of zonasVisibles) m.set(z.repCodigo, (m.get(z.repCodigo) ?? 0) + z.clientes.length);
    return m;
  }, [zonasVisibles]);
  const totalFiltrado = sinAsignarVisible.length + zonasVisibles.reduce((n, z) => n + z.clientes.length, 0);

  // Si cambia el universo visible (nueva propuesta generada, o se excluyó algo),
  // la selección vieja puede apuntar a clientes que ya no están — se limpia.
  useEffect(() => {
    setSeleccion(new Set());
  }, [territorio, zonas]);

  useEffect(() => {
    if (!vendedorOpciones || vendedorOpciones.length === 0) return;
    if (!vendedorOpciones.some((v) => v.repCodigo === repAsignar)) setRepAsignar(vendedorOpciones[0].repCodigo);
  }, [vendedorOpciones, repAsignar]);

  // Destinos posibles: los que están en la simulación y, aparte, cualquier otro
  // representante (aunque no esté seleccionado en el territorio).
  const opcionesEnSim = useMemo(() => (vendedorOpciones ?? []).filter((v) => v.enSimulacion !== false), [vendedorOpciones]);
  const opcionesFueraSim = useMemo(() => (vendedorOpciones ?? []).filter((v) => v.enSimulacion === false), [vendedorOpciones]);

  function handleBoxSelect(bounds: L.LatLngBounds) {
    setSeleccion((prev) => {
      const ids = new Set(prev);
      for (const p of puntosVisibles) if (bounds.contains([p.lat, p.lng])) ids.add(p.cliId);
      return ids;
    });
  }

  function handleExcluir() {
    if (seleccion.size === 0 || !onExcluir) return;
    onExcluir([...seleccion]);
    setSeleccion(new Set());
    setModoSeleccion(false);
  }

  function toggleClick(cliId: number) {
    if (!modoSeleccion) return;
    setSeleccion((prev) => {
      const next = new Set(prev);
      if (next.has(cliId)) next.delete(cliId);
      else next.add(cliId);
      return next;
    });
  }

  function toggleSemanaAsignar(s: number) {
    setSemanasAsignar((prev) => (prev.includes(s) ? prev.filter((x) => x !== s) : [...prev, s].sort()));
  }

  function handleAsignar() {
    if (seleccion.size === 0) return;
    // "Sin día" no es un DiaSemana real: usa el mismo camino que "Excluir de
    // la propuesta" — el cliente queda sin ruta sugerida.
    if (diaAsignar === SIN_DIA) {
      handleExcluir();
      return;
    }
    if (!onAsignar || !repAsignar || semanasAsignar.length === 0) return;
    onAsignar([...seleccion], diaAsignar as DiaSemana, repAsignar, semanasAsignar);
    setSeleccion(new Set());
    setModoSeleccion(false);
  }

  function handleRefresh() {
    const map = mapRef.current;
    if (!map) return;
    map.invalidateSize();
    if (allPoints.length === 1) map.setView(allPoints[0], 14);
    else if (allPoints.length > 1) map.fitBounds(allPoints, { padding: [40, 40] });
  }

  // Un vendedor puede aparecer con distinto color según el día — el ícono se
  // arma por (forma del vendedor, color del día), con una caché para no crear
  // un divIcon nuevo por cada cliente.
  const iconCache = useRef(new Map<string, L.DivIcon>());
  function iconoVendedor(repCodigo: string | null, dia: string | null, tenue = false): L.DivIcon {
    const idxRaw = repCodigo ? vendedorCodigos.indexOf(repCodigo) : -1;
    const idx = idxRaw < 0 ? 0 : idxRaw;
    const color = (dia && DIA_COLORS[dia]) || "#8b949e";
    const key = `${idx}|${color}|${tenue ? 1 : 0}`;
    let icon = iconCache.current.get(key);
    if (!icon) {
      icon = crearVendedorIcon(idx, color, false, tenue);
      iconCache.current.set(key, icon);
    }
    return icon;
  }

  // Los clientes que todavía no tienen zona se dibujan igual con su día y su
  // vendedor ACTUALES (tenues) — así, si el territorio se armó con varios días,
  // cada día se ve de un color y cada vendedor con su ícono desde el principio.
  const conteoActualPorDia = useMemo(() => {
    const m = new Map<string, number>();
    for (const c of sinAsignarVisible) {
      const k = c.diaActual ?? SIN_DIA_ACTUAL;
      m.set(k, (m.get(k) ?? 0) + 1);
    }
    return m;
  }, [sinAsignarVisible]);
  const conteoActualPorVendedor = useMemo(() => {
    const m = new Map<string, number>();
    for (const c of sinAsignarVisible) {
      const k = c.repCodigoActual ?? "";
      m.set(k, (m.get(k) ?? 0) + 1);
    }
    return m;
  }, [sinAsignarVisible]);
  const diasActualesConDatos = useMemo(
    () => [...DIAS_SEMANA, SIN_DIA_ACTUAL].filter((d) => conteoActualPorDia.has(d)),
    [conteoActualPorDia]
  );
  const vendedoresActualesConDatos = useMemo(
    () => [...conteoActualPorVendedor.keys()].filter((k) => k !== "").sort(),
    [conteoActualPorVendedor]
  );
  const usarIconosSinAsignar = sinAsignarVisible.length <= UMBRAL_ICONOS_SIN_ASIGNAR;
  const hayZonas = !!zonas && zonas.length > 0;

  const diasConDatos = useMemo(() => DIAS_SEMANA.filter((d) => conteoPorDia.has(d)), [conteoPorDia]);
  const vendedoresConDatos = useMemo(
    () => vendedorCodigos.filter((c) => conteoPorVendedor.has(c)),
    [vendedorCodigos, conteoPorVendedor]
  );

  return (
    <div className="route-map-wrap" ref={wrapRef}>
      {zonas && zonas.length > 0 && (
        <div className="sim-mapa-filtros">
          <Multiselect label="Semana" options={SEMANAS.map(String)} selected={filtroSemanas} onChange={setFiltroSemanas} />
          <Multiselect label="Día" options={opcionesDia} selected={filtroDias} onChange={setFiltroDias} />
          <Multiselect label="Vendedor" options={opcionesVendedor} selected={filtroVendedores} onChange={setFiltroVendedores} />
          <Multiselect label="Supervisor" options={opcionesSupervisor} selected={filtroSupervisores} onChange={setFiltroSupervisores} />
          <Multiselect label="Frecuencia" options={opcionesFrecuencia} selected={filtroFrecuencias} onChange={setFiltroFrecuencias} />
          {hayFiltrosActivos && (
            <button className="board-toggle-btn" onClick={limpiarFiltros}>
              ✕ Quitar filtros
            </button>
          )}
          <span className="stat">
            Mostrando <b>{totalFiltrado}</b> cliente(s){hayFiltrosActivos ? " (filtrado)" : ""}
          </span>
        </div>
      )}
      <div className="sim-mapa-area">
      <div className="map-btn-row">
        <button className="map-refresh-btn" onClick={handleRefresh} title="Recalcular tamaño y reencuadrar el mapa">
          🔄 Refrescar mapa
        </button>
        <button className="map-refresh-btn" onClick={handleToggleFullscreen} title="Pantalla completa">
          {fullscreen ? "🗗 Salir de pantalla completa" : "⛶ Pantalla completa"}
        </button>
        {(onExcluir || onAsignar) && (
          <button
            className={`map-refresh-btn ${modoSeleccion ? "active" : ""}`}
            onClick={() => {
              setModoSeleccion((v) => !v);
              setSeleccion(new Set());
            }}
            title="Seleccionar clientes en el mapa (clic individual o arrastrar un rectángulo)"
          >
            {modoSeleccion ? "✕ Cancelar selección" : "▭ Seleccionar en mapa"}
          </button>
        )}
      </div>

      <MapContainer
        ref={mapRef}
        center={[18.48, -69.9]}
        zoom={11}
        style={{ width: "100%", height: "100%", flex: 1, minHeight: 0 }}
        preferCanvas
      >
        <TileLayer attribution="&copy; OpenStreetMap contributors" url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
        <FitBounds points={allPoints} />
        <AutoInvalidate />
        <InvalidateOnChange trigger={fullscreen} />
        {(onExcluir || onAsignar) && <BoxSelect active={modoSeleccion} onSelect={handleBoxSelect} />}

        {sinAsignarVisible.map((c) => {
          const sel = seleccion.has(c.cliId);
          const colorDia = (c.diaActual && DIA_COLORS[c.diaActual]) || "#8b949e";
          const handlers = modoSeleccion ? { click: () => toggleClick(c.cliId) } : undefined;
          const popup = !modoSeleccion && (
            <Popup>
              <div className="popup-title">{c.cliNombre}</div>
              <div className="popup-line">Código: {c.cliCodigo}</div>
              <div className="popup-line">Tamaño: {c.claDescripcion}</div>
              <div className="popup-line">
                Vendedor actual:{" "}
                {c.repCodigoActual
                  ? `${c.repCodigoActual} - ${nombrePorCodigoTodos?.get(c.repCodigoActual) ?? c.repCodigoActual}`
                  : "Sin vendedor"}
              </div>
              <div className="popup-line">Día actual: {c.diaActual ?? "Sin día"}</div>
              <div className="popup-line">Semanas actuales: {c.semanasActivasActual.length > 0 ? c.semanasActivasActual.join(", ") : "—"}</div>
            </Popup>
          );
          if (!usarIconosSinAsignar) {
            return (
              <CircleMarker
                key={c.cliId}
                center={[c.lat, c.lng]}
                radius={sel ? 9 : 4}
                pathOptions={{ color: sel ? "#ff4d4d" : colorDia, fillColor: sel ? "#ff4d4d" : colorDia, fillOpacity: sel ? 0.85 : 0.55, weight: sel ? 3 : 1 }}
                eventHandlers={handlers}
              >
                {popup}
              </CircleMarker>
            );
          }
          return (
            <Fragment key={c.cliId}>
              {sel && (
                <CircleMarker
                  center={[c.lat, c.lng]}
                  radius={14}
                  pathOptions={{ color: "#ff4d4d", fillColor: "#ff4d4d", fillOpacity: 0.25, weight: 2, interactive: false }}
                />
              )}
              <Marker
                position={[c.lat, c.lng]}
                icon={iconoVendedor(c.repCodigoActual, c.diaActual, true)}
                zIndexOffset={-500}
                eventHandlers={handlers}
              >
                {popup}
              </Marker>
            </Fragment>
          );
        })}

        {zonasVisibles.map((z) => {
          const icon = iconoVendedor(z.repCodigo, z.dia);
          const color = DIA_COLORS[z.dia] ?? "#8b949e";
          return (
            <Fragment key={z.id}>
              {mostrarPoligonos && z.polygon && z.polygon.length >= 3 && (
                <Polygon positions={z.polygon} pathOptions={{ color, weight: 2, opacity: 0.7, fillOpacity: 0.08 }} />
              )}
              {mostrarPoligonos &&
                onVertexDrag &&
                z.polygon?.map((v, i) => (
                  <Marker
                    key={`${z.id}-v${i}`}
                    position={v}
                    icon={verticeIcon}
                    draggable
                    eventHandlers={{
                      dragend: (e) => {
                        const ll = (e.target as L.Marker).getLatLng();
                        onVertexDrag(z.id, i, ll.lat, ll.lng);
                      },
                    }}
                  />
                ))}
              {z.clientes.map((c) => {
                const sel = seleccion.has(c.cliId);
                return (
                  <Fragment key={c.cliId}>
                    {sel && (
                      <CircleMarker
                        center={[c.lat, c.lng]}
                        radius={14}
                        pathOptions={{ color: "#ff4d4d", fillColor: "#ff4d4d", fillOpacity: 0.25, weight: 2, interactive: false }}
                      />
                    )}
                    <Marker
                      position={[c.lat, c.lng]}
                      icon={icon}
                      eventHandlers={modoSeleccion ? { click: () => toggleClick(c.cliId) } : undefined}
                    >
                      {!modoSeleccion && (
                        <Popup>
                          <div className="popup-title">{c.cliNombre}</div>
                          <div className="popup-line">Código: {c.cliCodigo}</div>
                          <div className="popup-line">
                            <b>Se propone:</b> {z.repCodigo} - {z.repNombre} · {z.dia} · {z.frecuencia}
                          </div>
                          <div className="popup-line popup-anterior">
                            Antes: {c.repCodigoOriginal ? `${c.repCodigoOriginal} - ${nombrePorCodigoTodos?.get(c.repCodigoOriginal) ?? c.repCodigoOriginal}` : "Sin ruta previa"} ·{" "}
                            {c.diaOriginal ?? "Sin día previo"}
                          </div>
                        </Popup>
                      )}
                    </Marker>
                  </Fragment>
                );
              })}
            </Fragment>
          );
        })}
      </MapContainer>

      {(hayZonas || sinAsignarVisible.length > 0) && (
        <div className="legend">
          {hayZonas && (
            <>
              <div className="legend-sep">Día (color) — cantidad filtrada</div>
              {diasConDatos.map((dia) => (
                <div className="legend-item" key={dia}>
                  <span className="legend-dot" style={{ background: DIA_COLORS[dia] ?? "#8b949e" }} />
                  {dia} ({conteoPorDia.get(dia) ?? 0})
                </div>
              ))}
              <div className="legend-sep">Vendedor (ícono) — cantidad filtrada</div>
              {vendedoresConDatos.map((codigo) => {
                const idx = vendedorCodigos.indexOf(codigo);
                return (
                  <div className="legend-item" key={codigo}>
                    <span
                      className="sim-legend-shape"
                      dangerouslySetInnerHTML={{ __html: vendorSvg(idx < 0 ? 0 : idx, "#8b949e", 14) }}
                    />
                    {codigo} - {vendedorNombrePorCodigo.get(codigo) ?? codigo} ({SHAPE_NAMES[(idx < 0 ? 0 : idx) % SHAPE_NAMES.length]}) —{" "}
                    {conteoPorVendedor.get(codigo) ?? 0}
                  </div>
                );
              })}
            </>
          )}
          {sinAsignarVisible.length > 0 && hayZonas && (
            <>
              <div className="legend-sep">{SIN_ASIGNAR} ({sinAsignarVisible.length}) — puntos tenues</div>
              <div className="legend-item">color = día actual · forma = vendedor actual</div>
            </>
          )}
          {sinAsignarVisible.length > 0 && !hayZonas && (
            <>
              <div className="legend-sep">Día actual (color) — {sinAsignarVisible.length} cliente(s)</div>
              {diasActualesConDatos.map((dia) => (
                <div className="legend-item" key={dia}>
                  <span className="legend-dot" style={{ background: DIA_COLORS[dia] ?? "#8b949e" }} />
                  {dia} ({conteoActualPorDia.get(dia) ?? 0})
                </div>
              ))}
              {usarIconosSinAsignar ? (
                <>
                  <div className="legend-sep">Vendedor actual (ícono)</div>
                  {vendedoresActualesConDatos.map((codigo) => {
                    const idx = vendedorCodigos.indexOf(codigo);
                    return (
                      <div className="legend-item" key={codigo}>
                        <span
                          className="sim-legend-shape"
                          dangerouslySetInnerHTML={{ __html: vendorSvg(idx < 0 ? 0 : idx, "#8b949e", 14) }}
                        />
                        {codigo} - {vendedorNombrePorCodigo.get(codigo) ?? codigo} ({SHAPE_NAMES[(idx < 0 ? 0 : idx) % SHAPE_NAMES.length]}) —{" "}
                        {conteoActualPorVendedor.get(codigo) ?? 0}
                      </div>
                    );
                  })}
                </>
              ) : (
                <div className="legend-item">
                  Más de {UMBRAL_ICONOS_SIN_ASIGNAR.toLocaleString()} clientes: se dibujan como círculos (por rendimiento). Filtrá por día o
                  semana para ver los íconos por vendedor.
                </div>
              )}
            </>
          )}
        </div>
      )}

      {modoSeleccion && seleccion.size === 0 && (
        <div className="map-select-hint">Hacé clic en un cliente, o arrastrá un rectángulo para seleccionar varios</div>
      )}

      {seleccion.size > 0 && (
        <div className="map-assign-bar">
          <div className="map-assign-title">{seleccion.size} cliente(s) seleccionados</div>
          {(onAsignar || onExcluir) && (
            <>
              <div className="map-assign-row">
                {onAsignar && vendedorOpciones && vendedorOpciones.length > 0 && (
                  <select
                    value={repAsignar}
                    onChange={(e) => setRepAsignar(e.target.value)}
                    disabled={diaAsignar === SIN_DIA}
                    title="Cualquier representante: los de la simulación primero, y abajo los que no están seleccionados"
                  >
                    {opcionesFueraSim.length > 0 ? (
                      <>
                        <optgroup label="En la simulación (quedan)">
                          {opcionesEnSim.map((v) => (
                            <option key={v.repCodigo} value={v.repCodigo}>
                              {v.repCodigo} - {v.repNombre}
                            </option>
                          ))}
                        </optgroup>
                        <optgroup label="Otros representantes (fuera de la simulación)">
                          {opcionesFueraSim.map((v) => (
                            <option key={v.repCodigo} value={v.repCodigo}>
                              {v.repCodigo} - {v.repNombre}
                            </option>
                          ))}
                        </optgroup>
                      </>
                    ) : (
                      opcionesEnSim.map((v) => (
                        <option key={v.repCodigo} value={v.repCodigo}>
                          {v.repCodigo} - {v.repNombre}
                        </option>
                      ))
                    )}
                  </select>
                )}
                <select value={diaAsignar} onChange={(e) => setDiaAsignar(e.target.value)}>
                  {DIAS_SEMANA.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                  {onExcluir && (
                    <option value={SIN_DIA}>Sin día (sin ruta sugerida)</option>
                  )}
                </select>
              </div>
              {onAsignar && diaAsignar !== SIN_DIA && (
                <div className="map-assign-row">
                  <span className="stat">Semanas (todas o algunas):</span>
                  <span className="week-toggle">
                    {SEMANAS.map((s) => (
                      <button key={s} className={semanasAsignar.includes(s) ? "active" : ""} onClick={() => toggleSemanaAsignar(s)}>
                        {s}
                      </button>
                    ))}
                  </span>
                </div>
              )}
              <div className="map-assign-row">
                <button className="export-btn" disabled={diaAsignar !== SIN_DIA && (semanasAsignar.length === 0 || !repAsignar)} onClick={handleAsignar}>
                  {diaAsignar === SIN_DIA ? "🚫 Quitar de la ruta (Sin día)" : `✓ Asignar a ${repAsignar} · ${diaAsignar}`}
                </button>
              </div>
            </>
          )}
          <div className="map-assign-row">
            {onExcluir && (
              <button className="export-btn" onClick={handleExcluir}>
                🚫 Excluir de la propuesta
              </button>
            )}
            <button className="board-toggle-btn" onClick={() => setSeleccion(new Set())}>
              Limpiar
            </button>
          </div>
        </div>
      )}
      </div>
    </div>
  );
}
