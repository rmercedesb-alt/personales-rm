import { useMemo } from "react";
import type { ClienteTerritorio } from "../types";

interface Props {
  clientes: ClienteTerritorio[];
}

/**
 * KPIs de arriba: total de clientes + desglose por tamaño. A propósito NO
 * muestra nada de "cómo es la ruta hoy" (día/frecuencia actual) — esa
 * información el algoritmo la usa por dentro, pero no se expone aquí como si
 * fuera el esquema vigente.
 */
export function ResumenTerritorio({ clientes }: Props) {
  const porTamano = useMemo(() => {
    const m = new Map<string, number>();
    for (const c of clientes) {
      const t = c.claDescripcion || "Sin Clasificación";
      m.set(t, (m.get(t) ?? 0) + 1);
    }
    return [...m.entries()].sort((a, b) => b[1] - a[1]);
  }, [clientes]);

  if (clientes.length === 0) return null;

  return (
    <div className="route-stats">
      <span className="chip">
        Total Clientes: <b>{clientes.length.toLocaleString()}</b>
      </span>
      {porTamano.map(([tam, n]) => (
        <span className="chip" key={tam}>
          {tam}: <b>{n.toLocaleString()}</b>
        </span>
      ))}
    </div>
  );
}
