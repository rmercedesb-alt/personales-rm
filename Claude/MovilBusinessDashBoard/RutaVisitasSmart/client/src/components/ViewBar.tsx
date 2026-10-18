import { Multiselect } from "./Multiselect";

export type VistaModo = "resumido" | "detallado";

const MAPA_OPCIONES = ["Polígono", "Georeferencia", "Ambos"];

interface Props {
  vista: VistaModo;
  onVistaChange: (v: VistaModo) => void;
  mapaModo: Set<string>;
  onMapaModoChange: (next: Set<string>) => void;
}

export function ViewBar({ vista, onVistaChange, mapaModo, onMapaModoChange }: Props) {
  return (
    <div className="viewbar">
      <div className="toggle-group">
        <button className={vista === "resumido" ? "active" : ""} onClick={() => onVistaChange("resumido")}>
          Resumido
        </button>
        <button className={vista === "detallado" ? "active" : ""} onClick={() => onVistaChange("detallado")}>
          Detallado
        </button>
      </div>
      <Multiselect label="Mapa" options={MAPA_OPCIONES} selected={mapaModo} onChange={onMapaModoChange} searchable={false} />
    </div>
  );
}
