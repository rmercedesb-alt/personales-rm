import { Multiselect } from "./Multiselect";

export interface Filters {
  dias: Set<string>;
  centros: Set<string>;
  supervisores: Set<string>;
  vendedores: Set<string>;
  clientes: Set<string>;
  divisiones: Set<string>;
  tipos: Set<string>;
  tamanos: Set<string>;
  semanas: Set<string>;
  frecuencias: Set<string>;
  grupos: Set<string>;
}

interface Props {
  options: {
    dias: string[];
    centros: string[];
    supervisores: string[];
    vendedores: string[];
    clientes: string[];
    divisiones: string[];
    tipos: string[];
    tamanos: string[];
    semanas: string[];
    frecuencias: string[];
    grupos: string[];
  };
  filters: Filters;
  onChange: (next: Filters) => void;
  collapsed: boolean;
  onToggleCollapsed: () => void;
}

export function FilterBar({ options, filters, onChange, collapsed, onToggleCollapsed }: Props) {
  if (collapsed) {
    return (
      <div className="filterbar filterbar-collapsed">
        <button className="sidebar-expand-btn" onClick={onToggleCollapsed} title="Mostrar filtros">
          »
        </button>
      </div>
    );
  }

  return (
    <div className="filterbar">
      <button className="sidebar-collapse-btn" onClick={onToggleCollapsed} title="Ocultar filtros">
        «
      </button>
      <Multiselect
        label="Día"
        options={options.dias}
        selected={filters.dias}
        onChange={(dias) => onChange({ ...filters, dias })}
      />
      <Multiselect
        label="Semana"
        options={options.semanas}
        selected={filters.semanas}
        onChange={(semanas) => onChange({ ...filters, semanas })}
      />
      <Multiselect
        label="Frecuencia"
        options={options.frecuencias}
        selected={filters.frecuencias}
        onChange={(frecuencias) => onChange({ ...filters, frecuencias })}
      />
      <Multiselect
        label="Centro"
        options={options.centros}
        selected={filters.centros}
        onChange={(centros) => onChange({ ...filters, centros })}
      />
      <Multiselect
        label="Supervisor"
        options={options.supervisores}
        selected={filters.supervisores}
        onChange={(supervisores) => onChange({ ...filters, supervisores })}
      />
      <Multiselect
        label="Vendedor"
        options={options.vendedores}
        selected={filters.vendedores}
        onChange={(vendedores) => onChange({ ...filters, vendedores })}
        searchable
      />
      <Multiselect
        label="Cliente"
        options={options.clientes}
        selected={filters.clientes}
        onChange={(clientes) => onChange({ ...filters, clientes })}
        searchable
      />
      <Multiselect
        label="División"
        options={options.divisiones}
        selected={filters.divisiones}
        onChange={(divisiones) => onChange({ ...filters, divisiones })}
      />
      <Multiselect
        label="Tipo de Negocio"
        options={options.tipos}
        selected={filters.tipos}
        onChange={(tipos) => onChange({ ...filters, tipos })}
      />
      <Multiselect
        label="Tamaño"
        options={options.tamanos}
        selected={filters.tamanos}
        onChange={(tamanos) => onChange({ ...filters, tamanos })}
      />
      <Multiselect
        label="Grupo Clientes"
        options={options.grupos}
        selected={filters.grupos}
        onChange={(grupos) => onChange({ ...filters, grupos })}
        searchable
      />
    </div>
  );
}
