import type { ParametrosSimulacion } from "../types";

interface Props {
  parametros: ParametrosSimulacion;
  onChange: (p: ParametrosSimulacion) => void;
  onGenerar: () => void;
  onSinPropuesta: () => void;
  disabled: boolean;
  generando?: boolean;
}

export function WizardParametros({ parametros, onChange, onGenerar, onSinPropuesta, disabled, generando }: Props) {
  return (
    <div className="route-toolbar sim-wizard">
      <label className="sim-field">
        <span>¿Cuántos clientes máximo por ruta (vendedor + día)?</span>
        <input
          type="number"
          min={1}
          value={parametros.clientesPorDia}
          onChange={(e) => onChange({ ...parametros, clientesPorDia: Math.max(1, Number(e.target.value) || 1) })}
        />
      </label>
      <label className="sim-field">
        <span>¿Frecuencia Semanal para todos?</span>
        <select
          value={parametros.frecuenciaSemanalForzada ? "si" : "no"}
          onChange={(e) => onChange({ ...parametros, frecuenciaSemanalForzada: e.target.value === "si" })}
        >
          <option value="no">No — decide por tamaño/dispersión</option>
          <option value="si">Sí</option>
        </select>
      </label>
      <details className="sim-avanzado">
        <summary>Avanzado</summary>
        <label className="sim-field">
          <span>Umbral de dispersión (km) para sugerir Quincenal total</span>
          <input
            type="number"
            min={1}
            step={0.5}
            value={parametros.umbralDispersionKm}
            onChange={(e) => onChange({ ...parametros, umbralDispersionKm: Math.max(1, Number(e.target.value) || 1) })}
          />
        </label>
      </details>
      <button className="export-btn" onClick={onGenerar} disabled={disabled || generando}>
        {generando ? "⏳ Calculando frecuencia de compra…" : "🧭 Generar propuesta"}
      </button>
      <button
        className="board-toggle-btn"
        onClick={onSinPropuesta}
        disabled={disabled || generando}
        title="Carga todos los clientes en el mapa sin asignarles día ni vendedor — vos decidís cliente por cliente."
      >
        🗒 Clientes sin Propuesta
      </button>
    </div>
  );
}
