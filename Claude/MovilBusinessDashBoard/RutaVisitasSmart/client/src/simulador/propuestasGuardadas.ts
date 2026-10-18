import type { DiaSemana, ParametrosSimulacion, ZonaSimulada } from "../types";

const STORAGE_KEY = "rvs_simulador_propuestas";

export interface HuerfanoGuardado {
  dia: DiaSemana;
  cliente: ZonaSimulada["clientes"][number];
}

export interface PropuestaGuardada {
  id: string;
  nombre: string;
  fecha: string; // ISO
  seleccionVendedores: Array<[string, boolean]>;
  parametros: ParametrosSimulacion;
  excluidos: number[];
  zonas: ZonaSimulada[];
  huerfanos: HuerfanoGuardado[];
  resumenTexto: string; // "3 vendedor(es) · 933 clientes" — para mostrar en la lista sin recalcular
  /** Filtros de Día/Semana con los que se armó el territorio — sin ellos, al
   * recargar la propuesta el territorio incluiría de nuevo toda la ruta de los
   * vendedores y los clientes fuera del filtro aparecerían como "sin asignar". */
  filtroDias?: string[];
  filtroSemanas?: string[];
}

function leerTodas(): PropuestaGuardada[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const data = JSON.parse(raw);
    return Array.isArray(data) ? data : [];
  } catch {
    return [];
  }
}

function guardarTodas(propuestas: PropuestaGuardada[]) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(propuestas));
  } catch {
    // localStorage lleno o deshabilitado — no bloquea el uso del simulador
  }
}

export function listarPropuestas(): PropuestaGuardada[] {
  return leerTodas().sort((a, b) => b.fecha.localeCompare(a.fecha));
}

export function guardarPropuesta(datos: Omit<PropuestaGuardada, "id" | "fecha">): PropuestaGuardada {
  const nueva: PropuestaGuardada = {
    ...datos,
    id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    fecha: new Date().toISOString(),
  };
  const todas = leerTodas();
  todas.push(nueva);
  guardarTodas(todas);
  return nueva;
}

export function eliminarPropuesta(id: string) {
  guardarTodas(leerTodas().filter((p) => p.id !== id));
}

export function nombreArchivoPropuesta(p: PropuestaGuardada): string {
  const slug = p.nombre.trim().replace(/[^a-z0-9áéíóúñ]+/gi, "_").slice(0, 60) || "propuesta";
  return `Simulacion_${slug}.json`;
}

/** Valida y agrega una propuesta importada desde archivo (.json exportado con
 * "⬇ Descargar" desde esta misma pantalla) — se guarda igual que cualquier
 * otra, con un id nuevo para no chocar con las que ya haya en este navegador. */
export function importarPropuesta(contenido: string): PropuestaGuardada {
  let datos: unknown;
  try {
    datos = JSON.parse(contenido);
  } catch {
    throw new Error("El archivo no es un JSON válido.");
  }
  if (!datos || typeof datos !== "object") throw new Error("Archivo inválido.");
  const d = datos as Partial<PropuestaGuardada>;
  if (!Array.isArray(d.zonas) || !Array.isArray(d.seleccionVendedores)) {
    throw new Error("El archivo no tiene el formato esperado de una propuesta de Simular Nuevas Rutas.");
  }
  const importada: PropuestaGuardada = {
    id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    nombre: d.nombre ? `${d.nombre} (importada)` : "Propuesta importada",
    fecha: d.fecha ?? new Date().toISOString(),
    seleccionVendedores: d.seleccionVendedores,
    parametros: d.parametros ?? { clientesPorDia: 70, frecuenciaSemanalForzada: false, umbralDispersionKm: 9 },
    excluidos: Array.isArray(d.excluidos) ? d.excluidos : [],
    zonas: d.zonas,
    huerfanos: Array.isArray(d.huerfanos) ? d.huerfanos : [],
    resumenTexto: d.resumenTexto ?? "",
    filtroDias: Array.isArray(d.filtroDias) ? d.filtroDias : [],
    filtroSemanas: Array.isArray(d.filtroSemanas) ? d.filtroSemanas : [],
  };
  const todas = leerTodas();
  todas.push(importada);
  guardarTodas(todas);
  return importada;
}
