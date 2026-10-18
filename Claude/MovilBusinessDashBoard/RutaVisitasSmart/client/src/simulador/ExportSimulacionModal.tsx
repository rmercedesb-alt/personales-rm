import { useEffect, useState } from "react";
import { fetchSuplidores, type Suplidor } from "../api";
import { Multiselect } from "../components/Multiselect";

interface Props {
  onCancelar: () => void;
  onConfirmar: (desde: string, hasta: string, suplidores: number[]) => void;
  exportando: boolean;
}

const hoy = () => new Date().toISOString().slice(0, 10);
const haceUnMes = () => {
  const d = new Date();
  d.setMonth(d.getMonth() - 1);
  return d.toISOString().slice(0, 10);
};

export function ExportSimulacionModal({ onCancelar, onConfirmar, exportando }: Props) {
  const [suplidores, setSuplidores] = useState<Suplidor[]>([]);
  const [desde, setDesde] = useState(haceUnMes());
  const [hasta, setHasta] = useState(hoy());
  const [seleccion, setSeleccion] = useState<Set<string>>(new Set());

  useEffect(() => {
    fetchSuplidores()
      .then(setSuplidores)
      .catch(() => setSuplidores([]));
  }, []);

  const opciones = suplidores.map((s) => s.nombre);
  const idsPorNombre = new Map(suplidores.map((s) => [s.nombre, s.id]));

  function confirmar() {
    const ids = [...seleccion].map((n) => idsPorNombre.get(n)).filter((n): n is number => n != null);
    onConfirmar(desde, hasta, ids);
  }

  return (
    <div className="sim-modal-backdrop">
      <div className="sim-modal">
        <h3>⬇ Exportar propuesta</h3>
        <p className="sim-modal-hint">
          Genera el Excel (Detalle con Lat/Lng/División, Ventas por Suplidor del rango elegido, y el Script SQL para que TI lo
          aplique).
        </p>
        <label className="sim-field">
          <span>Ventas desde</span>
          <input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        </label>
        <label className="sim-field">
          <span>Ventas hasta</span>
          <input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
        </label>
        <div className="sim-field">
          <span>Suplidores (para la hoja de ventas)</span>
          <Multiselect label="Suplidores" options={opciones} selected={seleccion} onChange={setSeleccion} searchable emptyMeansAll={false} />
        </div>
        <div className="sim-modal-actions">
          <button className="board-toggle-btn" onClick={onCancelar} disabled={exportando}>
            Cancelar
          </button>
          <button className="export-btn" onClick={confirmar} disabled={exportando}>
            {exportando ? "Generando…" : "⬇ Generar Excel"}
          </button>
        </div>
      </div>
    </div>
  );
}
