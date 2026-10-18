import { useMemo } from "react";
import type { ClienteRuta, DrillLevel, Selection } from "../types";
import { colorForRep } from "../colors";

interface Props {
  data: ClienteRuta[];
  level: DrillLevel;
  selection: Selection;
  onDrillRep: (repCodigo: string, repNombre: string) => void;
}

/** A nivel "rep" no se lista más el detalle de clientes acá al costado — ya
 * se ve pasando el mouse por encima de cada punto en el mapa (Popup en
 * hover), y esa lista larga tapaba la leyenda de colores/formas. */
export function SidePanel({ data, level, selection, onDrillRep }: Props) {
  const reps = useMemo(() => {
    if (level !== "supervisor") return [];
    const scoped = data.filter((r) => r.dia === selection.dia && (r.supCodigo ?? "") === (selection.supCodigo ?? ""));
    const map = new Map<string, { repCodigo: string; repNombre: string; cliIds: Set<number> }>();
    for (const r of scoped) {
      let e = map.get(r.repCodigo);
      if (!e) {
        e = { repCodigo: r.repCodigo, repNombre: r.repNombre, cliIds: new Set() };
        map.set(r.repCodigo, e);
      }
      e.cliIds.add(r.cliId);
    }
    return [...map.values()].sort((a, b) => b.cliIds.size - a.cliIds.size);
  }, [data, level, selection]);

  if (level !== "supervisor") return null;

  return (
    <div className="side-panel">
      <h4>Vendedores · {selection.dia}</h4>
      {reps.map((r) => (
        <div key={r.repCodigo} className="rep-row" onClick={() => onDrillRep(r.repCodigo, r.repNombre)}>
          <span className="dot" style={{ background: colorForRep(r.repCodigo) }} />
          <span className="name">{r.repNombre}</span>
          <span className="count">{r.cliIds.size}</span>
        </div>
      ))}
    </div>
  );
}
