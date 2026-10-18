import { resolveFrecuencia } from "../frecuencia";
import { type DiaSemana, type ZonaSimulada } from "../types";

export const SIN_RUTA_PREVIA = "__SIN_RUTA_PREVIA__";

export interface VendedorResumenRow {
  repCodigo: string;
  repNombre: string;
  saliente: boolean;
  antes: number;
  ahora: number;
  /** Desglose de la propuesta (ahora) para este vendedor — por día y frecuencia. */
  porDia: Partial<Record<DiaSemana, number>>;
  semanales: number;
  quincenales: number;
}

/** Tabla resumida de una propuesta ya generada, comparando antes (estado
 * actual de RutaVisitas) vs. ahora (lo que propone el algoritmo) — se muestra
 * en pantalla y se incluye tal cual (mismos números) en la hoja "Resumen" del
 * Excel exportado. */
export interface ResumenPropuesta {
  totalVendedoresAntes: number;
  totalVendedoresAhora: number;
  totalClientes: number;
  clientesPorSemanaAntes: Record<number, number>;
  clientesPorSemanaAhora: Record<number, number>;
  clientesPorDiaAntes: Record<string, number>; // incluye "Sin día previo"
  clientesPorDiaAhora: Partial<Record<DiaSemana, number>>;
  clientesPorFrecuenciaAntes: Record<string, number>; // Semanal/Quincenal/Personalizada/Sin Asignar
  clientesSemanalesAhora: number;
  clientesQuincenalesAhora: number;
  clientesPorTamano: Record<string, number>;
  porVendedor: VendedorResumenRow[];
}

export function calcularResumenPropuesta(
  zonas: ZonaSimulada[],
  totalVendedoresAntes: number,
  totalVendedoresAhora: number,
  nombrePorCodigoTodos: Map<string, string>,
  salientesCodigos: string[]
): ResumenPropuesta {
  const clientesPorSemanaAntes: Record<number, number> = { 1: 0, 2: 0, 3: 0, 4: 0 };
  const clientesPorSemanaAhora: Record<number, number> = { 1: 0, 2: 0, 3: 0, 4: 0 };
  const clientesPorDiaAntes: Record<string, number> = {};
  const clientesPorDiaAhora: Partial<Record<DiaSemana, number>> = {};
  const clientesPorFrecuenciaAntes: Record<string, number> = {};
  const clientesPorTamano: Record<string, number> = {};
  let clientesSemanalesAhora = 0;
  let clientesQuincenalesAhora = 0;
  const antesPorVendedor = new Map<string, number>();
  const ahoraPorVendedor = new Map<string, number>();
  const diaPorVendedor = new Map<string, Partial<Record<DiaSemana, number>>>();
  const semanalPorVendedor = new Map<string, number>();
  const quincenalPorVendedor = new Map<string, number>();
  const vistos = new Set<number>();

  for (const z of zonas) {
    clientesPorDiaAhora[z.dia] = (clientesPorDiaAhora[z.dia] ?? 0) + z.clientes.length;
    ahoraPorVendedor.set(z.repCodigo, (ahoraPorVendedor.get(z.repCodigo) ?? 0) + z.clientes.length);
    const diaMap = diaPorVendedor.get(z.repCodigo) ?? {};
    diaMap[z.dia] = (diaMap[z.dia] ?? 0) + z.clientes.length;
    diaPorVendedor.set(z.repCodigo, diaMap);
    if (z.frecuencia === "Semanal") {
      clientesSemanalesAhora += z.clientes.length;
      semanalPorVendedor.set(z.repCodigo, (semanalPorVendedor.get(z.repCodigo) ?? 0) + z.clientes.length);
    } else {
      clientesQuincenalesAhora += z.clientes.length;
      quincenalPorVendedor.set(z.repCodigo, (quincenalPorVendedor.get(z.repCodigo) ?? 0) + z.clientes.length);
    }
    for (const s of z.semanasActivas) clientesPorSemanaAhora[s] = (clientesPorSemanaAhora[s] ?? 0) + z.clientes.length;

    for (const c of z.clientes) {
      if (vistos.has(c.cliId)) continue;
      vistos.add(c.cliId);

      const tam = c.claDescripcion || "Sin clasificar";
      clientesPorTamano[tam] = (clientesPorTamano[tam] ?? 0) + 1;

      const antesKey = c.repCodigoOriginal ?? SIN_RUTA_PREVIA;
      antesPorVendedor.set(antesKey, (antesPorVendedor.get(antesKey) ?? 0) + 1);

      const diaAntesKey = c.diaOriginal ?? "Sin día previo";
      clientesPorDiaAntes[diaAntesKey] = (clientesPorDiaAntes[diaAntesKey] ?? 0) + 1;

      for (const s of c.semanasActivasOriginal) clientesPorSemanaAntes[s] = (clientesPorSemanaAntes[s] ?? 0) + 1;

      const frecAntes = resolveFrecuencia(c.semanasActivasOriginal);
      clientesPorFrecuenciaAntes[frecAntes] = (clientesPorFrecuenciaAntes[frecAntes] ?? 0) + 1;
    }
  }

  const salientesSet = new Set(salientesCodigos);
  const codigos = new Set([...antesPorVendedor.keys(), ...ahoraPorVendedor.keys()].filter((k) => k !== SIN_RUTA_PREVIA));
  const porVendedor: VendedorResumenRow[] = [...codigos]
    .map((repCodigo) => ({
      repCodigo,
      repNombre: nombrePorCodigoTodos.get(repCodigo) ?? repCodigo,
      saliente: salientesSet.has(repCodigo),
      antes: antesPorVendedor.get(repCodigo) ?? 0,
      ahora: ahoraPorVendedor.get(repCodigo) ?? 0,
      porDia: diaPorVendedor.get(repCodigo) ?? {},
      semanales: semanalPorVendedor.get(repCodigo) ?? 0,
      quincenales: quincenalPorVendedor.get(repCodigo) ?? 0,
    }))
    .sort((a, b) => b.antes - a.antes);
  if (antesPorVendedor.has(SIN_RUTA_PREVIA)) {
    porVendedor.push({
      repCodigo: SIN_RUTA_PREVIA,
      repNombre: "Sin ruta previa (clientes nuevos)",
      saliente: false,
      antes: antesPorVendedor.get(SIN_RUTA_PREVIA) ?? 0,
      ahora: 0,
      porDia: {},
      semanales: 0,
      quincenales: 0,
    });
  }

  return {
    totalVendedoresAntes,
    totalVendedoresAhora,
    totalClientes: vistos.size,
    clientesPorSemanaAntes,
    clientesPorSemanaAhora,
    clientesPorDiaAntes,
    clientesPorDiaAhora,
    clientesPorFrecuenciaAntes,
    clientesSemanalesAhora,
    clientesQuincenalesAhora,
    clientesPorTamano,
    porVendedor,
  };
}
