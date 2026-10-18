import type { ClienteRuta, DrillLevel, Selection } from "./types";

/** Subconjunto de datos correspondiente al nivel de drill-down actual. */
export function scopeForLevel(data: ClienteRuta[], level: DrillLevel, selection: Selection): ClienteRuta[] {
  if (level === "overview") return data;
  const enDia = (r: ClienteRuta) => r.dia === selection.dia && (r.supCodigo ?? "") === (selection.supCodigo ?? "");
  if (level === "supervisor") return data.filter(enDia);
  return data.filter((r) => enDia(r) && r.repCodigo === selection.repCodigo);
}
