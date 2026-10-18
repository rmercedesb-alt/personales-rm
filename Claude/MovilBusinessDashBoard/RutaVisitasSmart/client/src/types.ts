export interface ClienteRuta {
  repCodigo: string;
  repNombre: string;
  repCargo: string | null;
  supCodigo: string | null;
  supNombre: string;
  cedCodigo: string | null;
  cedDescripcion: string;
  division: string;
  cliId: number;
  cliCodigo: string | null;
  cliNombre: string | null;
  lat: number;
  lng: number;
  rutPosicion: number | null;
  dia: string;
  semanasActivas: number[];
  frecuencia: string;
  claId: number | null;
  claDescripcion: string;
  tiNId: number | null;
  tinDescripcion: string;
  canId: number | null;
  canDescripcion: string;
  grupos: string[];
}

export type DrillLevel = "overview" | "supervisor" | "rep";

export interface Selection {
  dia: string | null;
  supCodigo: string | null;
  supNombre: string | null;
  repCodigo: string | null;
  repNombre: string | null;
}

export const DIAS_SEMANA = ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"] as const;
export type DiaSemana = (typeof DIAS_SEMANA)[number];

export interface Vendedor {
  repCodigo: string;
  repNombre: string;
  repCargo: string | null;
  supCodigo: string | null;
  supNombre: string;
  cedCodigo: string | null;
  cedDescripcion: string;
  division: string;
}

export interface ClienteCandidato {
  cliId: number;
  cliCodigo: string | null;
  cliNombre: string | null;
  lat: number;
  lng: number;
  claId: number | null;
  claDescripcion: string;
  tiNId: number | null;
  tinDescripcion: string;
  canId: number | null;
  canDescripcion: string;
  grupos: string[];
  /** Dueño según Clientes.RepCodigo (maestro) — puede diferir de quién lo tiene hoy en RutaVisitas. */
  duenoCodigo: string | null;
  duenoNombre: string | null;
}

/** Un cliente colocado en una columna de día dentro del editor de ruta. */
export interface AsignacionCliente {
  cliId: number;
  cliCodigo: string | null;
  cliNombre: string | null;
  lat: number;
  lng: number;
  claDescripcion: string;
  semanasActivas: number[];
  esPrimero: boolean;
  esUltimo: boolean;
}

export type RutaPorDia = Record<DiaSemana, AsignacionCliente[]>;

/**
 * Punto "cargado" en el mapa del editor desde OTRO vendedor (para comparar
 * territorios y arrastrar/seleccionar clientes hacia el vendedor destino).
 * `enRuta=true` si viene de RutaVisitas (ya tiene día); `false` si es un
 * cliente del maestro de Clientes (RepCodigo) que todavía no está en ruta.
 */
export interface CandidatoMapa {
  cliId: number;
  cliCodigo: string | null;
  cliNombre: string | null;
  lat: number;
  lng: number;
  claDescripcion: string;
  origenRepCodigo: string;
  origenRepNombre: string;
  enRuta: boolean;
  dia?: string;
}

// ── Simular Nuevas Rutas ─────────────────────────────────────────────────

/** Vendedor elegido en el filtro de territorio, con el flag de si sale del equipo. */
export interface VendedorSimulado extends Vendedor {
  saliente: boolean;
}

/** Parámetros del wizard: meta de clientes/día, si se fuerza Semanal, y el umbral
 * de dispersión (km) que decide si una zona pasa a Quincenal total. */
export interface ParametrosSimulacion {
  clientesPorDia: number;
  frecuenciaSemanalForzada: boolean;
  umbralDispersionKm: number;
}

/** Un cliente de una zona simulada: como AsignacionCliente + de qué vendedor/día/
 * semanas venía originalmente (para poder comparar "antes vs. ahora" en el
 * resumen y en el Excel exportado). */
export interface AsignacionSimulada extends AsignacionCliente {
  repCodigoOriginal: string | null;
  diaOriginal: DiaSemana | null;
  semanasActivasOriginal: number[];
}

/** Cliente del territorio antes de correr el algoritmo (universo elegido en el
 * filtro, con su día/vendedor actual si ya estaba en RutaVisitas). */
export interface ClienteTerritorio {
  cliId: number;
  cliCodigo: string | null;
  cliNombre: string | null;
  lat: number;
  lng: number;
  claDescripcion: string;
  diaActual: DiaSemana | null;
  repCodigoActual: string | null;
  semanasActivasActual: number[];
  /** Meses (de los últimos 6) con al menos un pedido — 0 a 6. Se completa
   * después de cargar el territorio, vía /api/frecuencia-compra. */
  mesesConCompra?: number;
}

/** Zona = combinación Vendedor(que queda) + Día — y hasta 2 si se partió en
 * quincenas alternas (mismo vendedor/día, semanas {1,3} vs {2,4}). */
export interface ZonaSimulada {
  id: string;
  dia: DiaSemana;
  repCodigo: string;
  repNombre: string;
  frecuencia: "Semanal" | "Quincenal";
  semanasActivas: number[];
  polygon: [number, number][] | null;
  clientes: AsignacionSimulada[];
}
