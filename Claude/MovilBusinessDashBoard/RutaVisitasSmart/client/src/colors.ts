export const DIA_COLORS: Record<string, string> = {
  Lunes: "#58a6ff",
  Martes: "#3fb950",
  Miércoles: "#d29922",
  Jueves: "#f778ba",
  Viernes: "#a371f7",
  Sábado: "#ff7b72",
  Domingo: "#79c0ff",
  "Sin Asignar": "#8b949e",
};

const REP_PALETTE = [
  "#58a6ff", "#3fb950", "#d29922", "#f778ba", "#a371f7",
  "#ff7b72", "#79c0ff", "#56d4dd", "#e3b341", "#bc8cff",
];

export function colorForRep(repCodigo: string): string {
  let hash = 0;
  for (let i = 0; i < repCodigo.length; i++) {
    hash = (hash * 31 + repCodigo.charCodeAt(i)) >>> 0;
  }
  return REP_PALETTE[hash % REP_PALETTE.length];
}
