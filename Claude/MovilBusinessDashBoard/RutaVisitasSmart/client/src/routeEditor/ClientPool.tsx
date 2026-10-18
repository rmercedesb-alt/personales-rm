import { useEffect, useMemo, useState } from "react";
import { useDraggable, useDroppable } from "@dnd-kit/core";
import type { ClienteCandidato, Vendedor } from "../types";

const RENDER_CAP = 150;
const SIN_DUENO_KEY = "__sin_dueno__";
const SIN_SUPERVISOR_LABEL = "(Sin Supervisor)";

interface Owner {
  repCodigo: string;
  repNombre: string;
}

interface Props {
  clientes: ClienteCandidato[];
  asignadosIds: Set<number>;
  ownerByCliId: Map<number, Owner>;
  vendedorActualCodigo: string | null;
  vendedores: Vendedor[];
  query: string;
  onQueryChange: (q: string) => void;
  /** Vendedores marcados para desplegar en el mapa (candidatos: rutados + sin ruta). */
  vendedoresCargados: Set<string>;
  onToggleVendedorCargado: (repCodigo: string) => void;
  /** Clientes sueltos marcados para desplegar en el mapa, sin cargar todo su vendedor. */
  clientesCargados: Set<number>;
  onToggleClienteCargado: (cliId: number) => void;
  collapsed: boolean;
  onToggleCollapsed: () => void;
}

function PoolRow({
  cliente,
  owner,
  vendedorActualCodigo,
  cargable,
  cargado,
  onToggleCargado,
}: {
  cliente: ClienteCandidato;
  owner?: Owner;
  vendedorActualCodigo: string | null;
  cargable: boolean;
  cargado: boolean;
  onToggleCargado?: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: String(cliente.cliId),
    data: { from: "pool" },
  });
  const style = transform
    ? { transform: `translate3d(${transform.x}px, ${transform.y}px, 0)`, zIndex: 1000 }
    : undefined;

  const conflicto = owner && owner.repCodigo !== vendedorActualCodigo;
  const propioSinDia = owner && owner.repCodigo === vendedorActualCodigo;

  return (
    <div ref={setNodeRef} style={style} {...listeners} {...attributes} className={`pool-row ${isDragging ? "dragging" : ""}`}>
      {cargable && (
        <input
          type="checkbox"
          className="pool-row-check"
          checked={cargado}
          onPointerDown={(e) => e.stopPropagation()}
          onClick={(e) => e.stopPropagation()}
          onChange={onToggleCargado}
          title="Mostrar este cliente en el mapa"
        />
      )}
      <div className="pool-row-body">
        <div className="pool-row-nombre">{cliente.cliCodigo} - {cliente.cliNombre}</div>
        <div className="pool-row-meta">
          {cliente.claDescripcion}
          {conflicto && <span className="badge-owner">En RutaVisitas con {owner!.repCodigo} - {owner!.repNombre}</span>}
          {propioSinDia && <span className="badge-owner mine">Ya en tu RutaVisitas, sin día activo</span>}
        </div>
      </div>
    </div>
  );
}

interface GrupoVendedor {
  key: string;
  label: string;
  items: ClienteCandidato[];
}

interface GrupoSupervisor {
  key: string;
  label: string;
  total: number;
  vendedores: GrupoVendedor[];
}

export function ClientPool({
  clientes,
  asignadosIds,
  ownerByCliId,
  vendedorActualCodigo,
  vendedores,
  query,
  onQueryChange,
  vendedoresCargados,
  onToggleVendedorCargado,
  clientesCargados,
  onToggleClienteCargado,
  collapsed,
  onToggleCollapsed,
}: Props) {
  const [supervisoresAbiertos, setSupervisoresAbiertos] = useState<Set<string>>(new Set());
  const [vendedoresAbiertos, setVendedoresAbiertos] = useState<Set<string>>(new Set());

  // Al cambiar de vendedor destino, colapsar todo — su propio grupo (arriba,
  // fuera de la jerarquía Supervisor→Vendedor) ya se ve directo.
  useEffect(() => {
    setSupervisoresAbiertos(new Set());
    setVendedoresAbiertos(new Set());
  }, [vendedorActualCodigo]);

  const supervisorPorRepCodigo = useMemo(
    () => new Map(vendedores.map((v) => [v.repCodigo, { supCodigo: v.supCodigo, supNombre: v.supNombre }])),
    [vendedores]
  );

  const disponibles = useMemo(() => clientes.filter((c) => !asignadosIds.has(c.cliId)), [clientes, asignadosIds]);

  const filtrados = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return disponibles;
    return disponibles.filter((c) => `${c.cliCodigo ?? ""} ${c.cliNombre ?? ""}`.toLowerCase().includes(q));
  }, [disponibles, query]);

  // Grupo fijo arriba: los propios del vendedor que se está editando.
  const grupoPropio = useMemo<GrupoVendedor | null>(() => {
    if (!vendedorActualCodigo) return null;
    const items = filtrados.filter((c) => c.duenoCodigo === vendedorActualCodigo);
    if (items.length === 0) return null;
    const v = vendedores.find((x) => x.repCodigo === vendedorActualCodigo);
    return { key: vendedorActualCodigo, label: `${vendedorActualCodigo} - ${v?.repNombre ?? "?"}`, items };
  }, [filtrados, vendedorActualCodigo, vendedores]);

  // Resto agrupado Supervisor → Vendedor.
  const gruposSupervisor = useMemo<GrupoSupervisor[]>(() => {
    const bySup = new Map<string, { label: string; vendMap: Map<string, GrupoVendedor> }>();
    for (const c of filtrados) {
      if (c.duenoCodigo === vendedorActualCodigo) continue; // ya está en grupoPropio
      const duenoKey = c.duenoCodigo ?? SIN_DUENO_KEY;
      const duenoLabel = c.duenoCodigo ? `${c.duenoCodigo} - ${c.duenoNombre ?? "?"}` : "Sin vendedor asignado";
      const info = c.duenoCodigo ? supervisorPorRepCodigo.get(c.duenoCodigo) : undefined;
      const supKey = c.duenoCodigo ? (info?.supCodigo ?? SIN_SUPERVISOR_LABEL) : SIN_DUENO_KEY;
      const supLabel = c.duenoCodigo ? (info?.supNombre ?? SIN_SUPERVISOR_LABEL) : "Sin vendedor asignado";

      let sup = bySup.get(supKey);
      if (!sup) {
        sup = { label: supLabel, vendMap: new Map() };
        bySup.set(supKey, sup);
      }
      let vend = sup.vendMap.get(duenoKey);
      if (!vend) {
        vend = { key: duenoKey, label: duenoLabel, items: [] };
        sup.vendMap.set(duenoKey, vend);
      }
      vend.items.push(c);
    }

    const list: GrupoSupervisor[] = [...bySup.entries()].map(([key, { label, vendMap }]) => {
      const vendedoresList = [...vendMap.values()].sort((a, b) => a.label.localeCompare(b.label));
      const total = vendedoresList.reduce((sum, v) => sum + v.items.length, 0);
      return { key, label, total, vendedores: vendedoresList };
    });

    list.sort((a, b) => {
      if (a.key === SIN_DUENO_KEY) return 1;
      if (b.key === SIN_DUENO_KEY) return -1;
      if (a.key === SIN_SUPERVISOR_LABEL) return 1;
      if (b.key === SIN_SUPERVISOR_LABEL) return -1;
      return a.label.localeCompare(b.label);
    });
    return list;
  }, [filtrados, vendedorActualCodigo, supervisorPorRepCodigo]);

  const buscando = query.trim().length > 0;

  function toggleSupervisor(key: string) {
    setSupervisoresAbiertos((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function toggleVendedor(key: string) {
    setVendedoresAbiertos((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function handleToggleSupervisorCargado(sup: GrupoSupervisor) {
    const codigos = sup.vendedores.map((v) => v.key).filter((k) => k !== SIN_DUENO_KEY);
    if (codigos.length === 0) return;
    const todosPrendidos = codigos.every((c) => vendedoresCargados.has(c));
    for (const c of codigos) {
      const prendido = vendedoresCargados.has(c);
      if (todosPrendidos ? prendido : !prendido) onToggleVendedorCargado(c);
    }
  }

  const { setNodeRef, isOver } = useDroppable({ id: "pool" });

  if (collapsed) {
    return (
      <div className="client-pool client-pool-collapsed">
        <button className="sidebar-expand-btn" onClick={onToggleCollapsed} title={`Mostrar sin asignar (${disponibles.length})`}>
          »
        </button>
      </div>
    );
  }

  return (
    <div className="client-pool">
      <div className="client-pool-header">
        <div className="client-pool-title-row">
          <div className="client-pool-title">Sin asignar ({disponibles.length})</div>
          <button className="sidebar-collapse-btn" onClick={onToggleCollapsed} title="Ocultar panel">
            «
          </button>
        </div>
        <input
          className="ms-search"
          placeholder="Buscar por nombre o código (incluye ya asignados)…"
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
        />
      </div>
      <div ref={setNodeRef} className={`client-pool-list ${isOver ? "over" : ""}`}>
        {grupoPropio && (
          <div className="pool-group mine">
            <div className="pool-group-header pool-group-header-static">
              <span className="pool-group-label">★ {grupoPropio.label}</span>
              <span className="pool-group-count">{grupoPropio.items.length}</span>
            </div>
            <div className="pool-group-body">
              {grupoPropio.items.slice(0, RENDER_CAP).map((c) => (
                <PoolRow
                  key={c.cliId}
                  cliente={c}
                  owner={ownerByCliId.get(c.cliId)}
                  vendedorActualCodigo={vendedorActualCodigo}
                  cargable={false}
                  cargado={false}
                />
              ))}
              {grupoPropio.items.length > RENDER_CAP && (
                <div className="ms-hint">Mostrando {RENDER_CAP} de {grupoPropio.items.length} — refine la búsqueda</div>
              )}
            </div>
          </div>
        )}

        {gruposSupervisor.map((sup) => {
          const supAbierto = buscando || supervisoresAbiertos.has(sup.key);
          const codigosSup = sup.vendedores.map((v) => v.key).filter((k) => k !== SIN_DUENO_KEY);
          const supTodosPrendidos = codigosSup.length > 0 && codigosSup.every((c) => vendedoresCargados.has(c));
          const supAlgunoPrendido = codigosSup.some((c) => vendedoresCargados.has(c));
          return (
            <div key={sup.key} className="pool-group pool-group-supervisor">
              <div className="pool-group-header">
                {codigosSup.length > 0 && (
                  <input
                    type="checkbox"
                    checked={supTodosPrendidos}
                    ref={(el) => {
                      if (el) el.indeterminate = !supTodosPrendidos && supAlgunoPrendido;
                    }}
                    onChange={() => handleToggleSupervisorCargado(sup)}
                    title="Mostrar todos los vendedores de este supervisor en el mapa"
                  />
                )}
                <span className="pool-group-caret-label" onClick={() => toggleSupervisor(sup.key)}>
                  <span className="pool-group-caret">{supAbierto ? "▾" : "▸"}</span>
                  <span className="pool-group-label">👤 {sup.label}</span>
                </span>
                <span className="pool-group-count">{sup.total}</span>
              </div>
              {supAbierto && (
                <div className="pool-group-body pool-group-body-nested">
                  {sup.vendedores.map((v) => {
                    const vendAbierto = buscando || vendedoresAbiertos.has(v.key);
                    const rendered = v.items.slice(0, RENDER_CAP);
                    const esCargable = v.key !== SIN_DUENO_KEY;
                    const vendCargado = esCargable && vendedoresCargados.has(v.key);
                    return (
                      <div key={v.key} className="pool-group">
                        <div className="pool-group-header pool-group-header-sub">
                          {esCargable && (
                            <input
                              type="checkbox"
                              checked={vendCargado}
                              onChange={() => onToggleVendedorCargado(v.key)}
                              title="Mostrar este vendedor en el mapa"
                            />
                          )}
                          <span className="pool-group-caret-label" onClick={() => toggleVendedor(v.key)}>
                            <span className="pool-group-caret">{vendAbierto ? "▾" : "▸"}</span>
                            <span className="pool-group-label">{v.label}</span>
                          </span>
                          <span className="pool-group-count">{v.items.length}</span>
                        </div>
                        {vendAbierto && (
                          <div className="pool-group-body">
                            {rendered.map((c) => (
                              <PoolRow
                                key={c.cliId}
                                cliente={c}
                                owner={ownerByCliId.get(c.cliId)}
                                vendedorActualCodigo={vendedorActualCodigo}
                                cargable
                                cargado={clientesCargados.has(c.cliId)}
                                onToggleCargado={() => onToggleClienteCargado(c.cliId)}
                              />
                            ))}
                            {v.items.length > RENDER_CAP && (
                              <div className="ms-hint">Mostrando {RENDER_CAP} de {v.items.length} — refine la búsqueda</div>
                            )}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}

        {!grupoPropio && gruposSupervisor.length === 0 && <div className="ms-hint">Sin resultados</div>}
      </div>
    </div>
  );
}
