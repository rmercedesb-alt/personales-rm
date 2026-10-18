import { useMemo, useState } from "react";
import { DIAS_SEMANA, type Vendedor } from "../types";
import { Multiselect } from "../components/Multiselect";

const SIN_SUPERVISOR = "(Sin Supervisor)";

/** Cliente que hoy no tiene día en RutaVisitas (solo está en el maestro de Clientes). */
export const SIN_DIA_TERRITORIO = "Sin día";
export const OPCIONES_DIA_TERRITORIO: string[] = [...DIAS_SEMANA, SIN_DIA_TERRITORIO];
export const OPCIONES_SEMANA_TERRITORIO = ["1", "2", "3", "4"];

interface Props {
  vendedores: Vendedor[];
  /** repCodigo -> saliente. Un vendedor presente aquí está "en el territorio". */
  seleccion: Map<string, boolean>;
  onToggleIncluir: (v: Vendedor) => void;
  onToggleSaliente: (repCodigo: string) => void;
  /** Días/semanas de visita ACTUALES (RutaVisitas) que definen qué clientes entran
   * a la simulación. Vacío = todos (comportamiento de siempre). */
  filtroDias: Set<string>;
  onFiltroDias: (next: Set<string>) => void;
  filtroSemanas: Set<string>;
  onFiltroSemanas: (next: Set<string>) => void;
}

export function FiltroTerritorio({
  vendedores,
  seleccion,
  onToggleIncluir,
  onToggleSaliente,
  filtroDias,
  onFiltroDias,
  filtroSemanas,
  onFiltroSemanas,
}: Props) {
  const [supervisores, setSupervisores] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState("");

  const supervisorOptions = useMemo(
    () => [...new Set(vendedores.map((v) => v.supNombre || SIN_SUPERVISOR))].sort(),
    [vendedores]
  );

  const candidatos = useMemo(() => {
    const q = query.trim().toLowerCase();
    return vendedores
      .filter((v) => supervisores.size === 0 || supervisores.has(v.supNombre || SIN_SUPERVISOR))
      .filter((v) => !q || `${v.repCodigo} ${v.repNombre}`.toLowerCase().includes(q))
      .sort((a, b) => a.repNombre.localeCompare(b.repNombre));
  }, [vendedores, supervisores, query]);

  const seleccionados = useMemo(() => vendedores.filter((v) => seleccion.has(v.repCodigo)), [vendedores, seleccion]);
  const salientesN = [...seleccion.values()].filter(Boolean).length;

  return (
    <div className="sim-filtro">
      <div className="route-toolbar">
        <Multiselect label="Supervisor" options={supervisorOptions} selected={supervisores} onChange={setSupervisores} />
        <Multiselect label="Día de visita" options={OPCIONES_DIA_TERRITORIO} selected={filtroDias} onChange={onFiltroDias} searchable={false} />
        <Multiselect label="Semana" options={OPCIONES_SEMANA_TERRITORIO} selected={filtroSemanas} onChange={onFiltroSemanas} searchable={false} />
        <input
          className="ms-search sim-search"
          placeholder="Buscar vendedor por código o nombre…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <span className="stat">
          Territorio: <b>{seleccionados.length}</b> vendedor(es) · <b>{salientesN}</b> saliente(s) ·{" "}
          <b>{seleccionados.length - salientesN}</b> quedan
        </span>
        {(filtroDias.size > 0 || filtroSemanas.size > 0) && (
          <span className="stat">
            Solo se simulan los clientes que hoy visitan{" "}
            <b>{filtroDias.size > 0 ? [...filtroDias].join(", ") : "cualquier día"}</b>
            {filtroSemanas.size > 0 && (
              <>
                {" "}
                en la(s) semana(s) <b>{[...filtroSemanas].sort().join(", ")}</b>
              </>
            )}
            ; el resto de la ruta de cada vendedor no se toca.
          </span>
        )}
      </div>
      <div className="sim-vendedor-lista">
        {candidatos.map((v) => {
          const incluido = seleccion.has(v.repCodigo);
          const saliente = seleccion.get(v.repCodigo) ?? false;
          return (
            <div key={v.repCodigo} className={`vendedor-row sim-vendedor-row ${incluido ? "active" : ""}`}>
              <label className="sim-check">
                <input type="checkbox" checked={incluido} onChange={() => onToggleIncluir(v)} />
              </label>
              <div className="sim-vendedor-info">
                <div className="vendedor-nombre">
                  {v.repCodigo} - {v.repNombre}
                </div>
                <div className="vendedor-meta">
                  {v.supNombre || SIN_SUPERVISOR} · {v.cedDescripcion}
                </div>
              </div>
              {incluido && (
                <label className="sim-saliente-check" title="Este vendedor sale del equipo: su territorio se fusiona con los que quedan">
                  <input type="checkbox" checked={saliente} onChange={() => onToggleSaliente(v.repCodigo)} />
                  Saliente
                </label>
              )}
            </div>
          );
        })}
        {candidatos.length === 0 && <div className="ms-hint">Sin vendedores para ese filtro.</div>}
      </div>
    </div>
  );
}
