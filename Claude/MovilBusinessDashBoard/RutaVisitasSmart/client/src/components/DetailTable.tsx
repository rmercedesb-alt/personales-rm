import { useMemo } from "react";
import type { ClienteRuta } from "../types";

const ROW_CAP = 500;

interface Props {
  data: ClienteRuta[];
}

export function DetailTable({ data }: Props) {
  const rows = useMemo(() => [...data].sort((a, b) => (a.rutPosicion ?? 9999) - (b.rutPosicion ?? 9999)).slice(0, ROW_CAP), [data]);

  return (
    <div className="detail-drawer">
      <div className="detail-drawer-header">
        Detalle · {data.length.toLocaleString()} clientes
        {data.length > ROW_CAP && ` (mostrando ${ROW_CAP} — refine los filtros para ver el resto)`}
      </div>
      <div className="detail-drawer-body">
        <table className="detail-table">
          <thead>
            <tr>
              <th>Código</th>
              <th>Cliente</th>
              <th>Vendedor</th>
              <th>Supervisor</th>
              <th>Día</th>
              <th>Frecuencia</th>
              <th>RutPos.</th>
              <th>Tamaño</th>
              <th>Tipo</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={`${r.repCodigo}-${r.cliId}`}>
                <td>{r.cliCodigo}</td>
                <td>{r.cliNombre}</td>
                <td>{r.repNombre}</td>
                <td>{r.supNombre}</td>
                <td>{r.dia}</td>
                <td>{r.frecuencia}</td>
                <td>{r.rutPosicion ?? "-"}</td>
                <td>{r.claDescripcion}</td>
                <td>{r.tinDescripcion}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
