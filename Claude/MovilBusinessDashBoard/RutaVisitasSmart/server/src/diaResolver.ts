const DIAS = ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"] as const;

/**
 * RutSemanaN es un código de 7 posiciones (Lun..Dom) donde un dígito no-cero
 * marca el día de visita. Algunos códigos legacy traen dígitos >1 (sufijo de
 * variante) en vez de exactamente "1" — cualquier dígito no-cero cuenta como
 * "visita ese día".
 */
function firstDia(codigo: string | null | undefined): string | null {
  if (!codigo || codigo.length < 7) return null;
  for (let i = 0; i < 7; i++) {
    if (codigo[i] !== "0") return DIAS[i];
  }
  return null;
}

function isActiva(codigo: string | null | undefined): boolean {
  return firstDia(codigo) !== null;
}

/** Día representativo: el de la primera semana (1..4) que tenga patrón activo. */
export function resolveDia(semanas: Array<string | null | undefined>): string | null {
  for (const s of semanas) {
    const d = firstDia(s);
    if (d) return d;
  }
  return null;
}

/** Semanas del mes (1..4) en las que el cliente tiene visita asignada. */
export function resolveSemanasActivas(semanas: Array<string | null | undefined>): number[] {
  const activas: number[] = [];
  semanas.forEach((s, idx) => {
    if (isActiva(s)) activas.push(idx + 1);
  });
  return activas;
}

/** Frecuencia según el patrón de semanas activas (confirmado por el usuario). */
export function resolveFrecuencia(semanasActivas: number[]): string {
  const set = new Set(semanasActivas);
  if (set.size === 0) return "Sin Asignar";
  if (set.size === 4) return "Semanal";
  if (set.size === 2 && set.has(1) && set.has(3)) return "Quincenal";
  if (set.size === 2 && set.has(2) && set.has(4)) return "Quincenal";
  return "Personalizada";
}

export { DIAS };
