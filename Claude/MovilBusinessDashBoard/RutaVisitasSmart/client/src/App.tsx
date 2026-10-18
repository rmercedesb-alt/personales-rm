import { useEffect, useMemo, useState } from "react";
import { Sidebar } from "./components/Sidebar";
import { FilterBar, type Filters } from "./components/FilterBar";
import { BreakdownBar } from "./components/BreakdownBar";
import { ViewBar, type VistaModo } from "./components/ViewBar";
import { MapView } from "./components/MapView";
import { SidePanel } from "./components/SidePanel";
import { DetailTable } from "./components/DetailTable";
import { RutaEditor } from "./routeEditor/RutaEditor";
import { ModificarRutaEditor } from "./routeEditor/ModificarRutaEditor";
import { SimuladorEditor } from "./simulador/SimuladorEditor";
import { fetchRutas } from "./api";
import type { ClienteRuta, DrillLevel, Selection } from "./types";
import { scopeForLevel } from "./scope";
import type { Sesion } from "./auth/api";

const DIA_ORDEN = ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo", "Sin Asignar"];
const FRECUENCIA_ORDEN = ["Semanal", "Quincenal", "Mensual", "Personalizada", "Sin Asignar"];
const SEMANA_OPCIONES = ["Semana 1", "Semana 2", "Semana 3", "Semana 4"];

const EMPTY_SELECTION: Selection = { dia: null, supCodigo: null, supNombre: null, repCodigo: null, repNombre: null };

function repLabel(r: ClienteRuta) {
  return `${r.repCodigo} - ${r.repNombre}`;
}

function cliLabel(r: ClienteRuta) {
  return `${r.cliCodigo} - ${r.cliNombre}`;
}

function distinctSorted<T>(data: T[], pick: (r: T) => string): string[] {
  return [...new Set(data.map(pick))].sort();
}

const MODULE_KEY = "rvs_module";
const SIDEBAR_COLLAPSED_KEY = "rvs_sidebar_collapsed";
const FILTROS_COLLAPSED_KEY = "rvs_filtros_collapsed";

interface Props {
  sesion: Sesion;
  onLogout: () => void;
}

export default function App({ sesion, onLogout }: Props) {
  // Recordar el módulo activo entre refrescos de página (localStorage, no hay
  // enrutamiento por URL todavía).
  const [module, setModule] = useState(() => localStorage.getItem(MODULE_KEY) || "dia-supervisor");

  useEffect(() => {
    localStorage.setItem(MODULE_KEY, module);
  }, [module]);

  // Colapsar el menú principal es una preferencia global (todos los módulos),
  // independiente de cualquier colapso propio de cada pantalla (ej. el pool
  // "Sin asignar" de Crear Ruta).
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === "1");
  useEffect(() => {
    localStorage.setItem(SIDEBAR_COLLAPSED_KEY, sidebarCollapsed ? "1" : "0");
  }, [sidebarCollapsed]);

  // Igual que el menú principal: preferencia global, independiente del resto.
  const [filtrosCollapsed, setFiltrosCollapsed] = useState(() => localStorage.getItem(FILTROS_COLLAPSED_KEY) === "1");
  useEffect(() => {
    localStorage.setItem(FILTROS_COLLAPSED_KEY, filtrosCollapsed ? "1" : "0");
  }, [filtrosCollapsed]);

  const [all, setAll] = useState<ClienteRuta[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [filters, setFilters] = useState<Filters>({
    dias: new Set(),
    centros: new Set(),
    supervisores: new Set(),
    vendedores: new Set(),
    clientes: new Set(),
    divisiones: new Set(),
    tipos: new Set(),
    tamanos: new Set(),
    semanas: new Set(),
    frecuencias: new Set(),
    grupos: new Set(),
  });

  const [level, setLevel] = useState<DrillLevel>("overview");
  const [selection, setSelection] = useState<Selection>(EMPTY_SELECTION);

  const [vista, setVista] = useState<VistaModo>("resumido");
  const [mapaModo, setMapaModo] = useState<Set<string>>(new Set(["Polígono"]));

  useEffect(() => {
    fetchRutas()
      .then((data) => {
        setAll(data);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, []);

  // Cascada jerárquica Centro → Supervisor → Vendedor → Cliente: cada nivel
  // solo ofrece opciones presentes dentro de lo que dejan pasar los niveles
  // de arriba. Día/Semana/Frecuencia/División/Tipo/Tamaño/Grupo son facetas
  // independientes (no acotan ni son acotadas por la jerarquía).
  const options = useMemo(() => {
    const afterCentro = filters.centros.size > 0 ? all.filter((r) => filters.centros.has(r.cedDescripcion)) : all;
    const afterSupervisor =
      filters.supervisores.size > 0 ? afterCentro.filter((r) => filters.supervisores.has(r.supNombre)) : afterCentro;
    const afterVendedor =
      filters.vendedores.size > 0 ? afterSupervisor.filter((r) => filters.vendedores.has(repLabel(r))) : afterSupervisor;

    const grupos = new Set<string>();
    for (const r of all) for (const g of r.grupos) grupos.add(g);

    return {
      dias: distinctSorted(all, (r) => r.dia).sort((a, b) => DIA_ORDEN.indexOf(a) - DIA_ORDEN.indexOf(b)),
      centros: distinctSorted(all, (r) => r.cedDescripcion),
      supervisores: distinctSorted(afterCentro, (r) => r.supNombre),
      vendedores: distinctSorted(afterSupervisor, repLabel),
      clientes: distinctSorted(afterVendedor, cliLabel),
      divisiones: distinctSorted(all, (r) => r.division),
      tipos: distinctSorted(all, (r) => r.tinDescripcion),
      tamanos: distinctSorted(all, (r) => r.claDescripcion),
      semanas: SEMANA_OPCIONES,
      frecuencias: distinctSorted(all, (r) => r.frecuencia).sort((a, b) => FRECUENCIA_ORDEN.indexOf(a) - FRECUENCIA_ORDEN.indexOf(b)),
      grupos: [...grupos].sort(),
    };
  }, [all, filters.centros, filters.supervisores, filters.vendedores]);

  // Si un nivel superior de la cascada cambia y una selección de abajo deja
  // de existir en las opciones válidas, se descarta automáticamente.
  useEffect(() => {
    setFilters((prev) => {
      const supervisores = new Set([...prev.supervisores].filter((s) => options.supervisores.includes(s)));
      const vendedores = new Set([...prev.vendedores].filter((v) => options.vendedores.includes(v)));
      const clientes = new Set([...prev.clientes].filter((c) => options.clientes.includes(c)));
      if (
        supervisores.size === prev.supervisores.size &&
        vendedores.size === prev.vendedores.size &&
        clientes.size === prev.clientes.size
      ) {
        return prev;
      }
      return { ...prev, supervisores, vendedores, clientes };
    });
  }, [options.supervisores, options.vendedores, options.clientes]);

  const filtered = useMemo(() => {
    const semanasNums = new Set([...filters.semanas].map((s) => Number(s.replace("Semana ", ""))));
    return all.filter((r) => {
      if (filters.dias.size > 0 && !filters.dias.has(r.dia)) return false;
      if (filters.centros.size > 0 && !filters.centros.has(r.cedDescripcion)) return false;
      if (filters.supervisores.size > 0 && !filters.supervisores.has(r.supNombre)) return false;
      if (filters.vendedores.size > 0 && !filters.vendedores.has(repLabel(r))) return false;
      if (filters.clientes.size > 0 && !filters.clientes.has(cliLabel(r))) return false;
      if (filters.divisiones.size > 0 && !filters.divisiones.has(r.division)) return false;
      if (filters.tipos.size > 0 && !filters.tipos.has(r.tinDescripcion)) return false;
      if (filters.tamanos.size > 0 && !filters.tamanos.has(r.claDescripcion)) return false;
      if (filters.frecuencias.size > 0 && !filters.frecuencias.has(r.frecuencia)) return false;
      if (semanasNums.size > 0 && !r.semanasActivas.some((s) => semanasNums.has(s))) return false;
      if (filters.grupos.size > 0 && !r.grupos.some((g) => filters.grupos.has(g))) return false;
      return true;
    });
  }, [all, filters]);

  function drillSupervisor(dia: string, supCodigo: string, supNombre: string) {
    setSelection({ dia, supCodigo, supNombre, repCodigo: null, repNombre: null });
    setLevel("supervisor");
  }

  function drillRep(repCodigo: string, repNombre: string) {
    setSelection((sel) => ({ ...sel, repCodigo, repNombre }));
    setLevel("rep");
  }

  function backToOverview() {
    setSelection(EMPTY_SELECTION);
    setLevel("overview");
  }

  function backToSupervisor() {
    setSelection((sel) => ({ ...sel, repCodigo: null, repNombre: null }));
    setLevel("supervisor");
  }

  const scoped = useMemo(() => scopeForLevel(filtered, level, selection), [filtered, level, selection]);
  const showPoligono = mapaModo.has("Polígono") || mapaModo.has("Ambos");
  const showGeoref = mapaModo.has("Georeferencia") || mapaModo.has("Ambos");

  return (
    <div className="app-shell">
      <Sidebar
        active={module}
        onSelect={setModule}
        sesion={sesion}
        onLogout={onLogout}
        collapsed={sidebarCollapsed}
        onToggleCollapsed={() => setSidebarCollapsed((v) => !v)}
      />
      <div className="main">
        {module === "dia-supervisor" && (
          <>
            <FilterBar
              options={options}
              filters={filters}
              onChange={setFilters}
              collapsed={filtrosCollapsed}
              onToggleCollapsed={() => setFiltrosCollapsed((v) => !v)}
            />
            <BreakdownBar data={filtered} />
            <ViewBar vista={vista} onVistaChange={setVista} mapaModo={mapaModo} onMapaModoChange={setMapaModo} />
            <div className="breadcrumb">
              <a onClick={backToOverview}>Todos los días</a>
              {level !== "overview" && (
                <>
                  <span className="sep">›</span>
                  <a onClick={backToSupervisor}>
                    {selection.dia} · {selection.supNombre}
                  </a>
                </>
              )}
              {level === "rep" && (
                <>
                  <span className="sep">›</span>
                  <span>{selection.repNombre}</span>
                </>
              )}
            </div>
            <div className="map-area">
              {loading && <div className="loading">Cargando rutas desde MovilBusiness5STD…</div>}
              {error && <div className="loading">Error: {error}</div>}
              {!loading && !error && (
                <>
                  <MapView
                    data={filtered}
                    level={level}
                    selection={selection}
                    showPoligono={showPoligono}
                    showGeoref={showGeoref}
                    onDrillSupervisor={drillSupervisor}
                    onDrillRep={drillRep}
                  />
                  {vista === "resumido" && <SidePanel data={filtered} level={level} selection={selection} onDrillRep={drillRep} />}
                  {vista === "detallado" && <DetailTable data={scoped} />}
                </>
              )}
            </div>
          </>
        )}
        {module === "crear-ruta" && <RutaEditor all={all} />}
        {module === "modificar-ruta" && <ModificarRutaEditor all={all} />}
        {module === "simular-rutas" && <SimuladorEditor all={all} />}
      </div>
    </div>
  );
}
