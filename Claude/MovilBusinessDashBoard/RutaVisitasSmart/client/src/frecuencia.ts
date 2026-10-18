/** Espejo de server/src/diaResolver.ts (resolveFrecuencia) — paquetes separados, sin código compartido. */
export function resolveFrecuencia(semanasActivas: number[]): string {
  const set = new Set(semanasActivas);
  if (set.size === 0) return "Sin Asignar";
  if (set.size === 4) return "Semanal";
  if (set.size === 2 && set.has(1) && set.has(3)) return "Quincenal";
  if (set.size === 2 && set.has(2) && set.has(4)) return "Quincenal";
  return "Personalizada";
}
