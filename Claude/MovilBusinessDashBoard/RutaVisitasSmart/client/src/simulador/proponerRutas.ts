import * as turf from "@turf/turf";
import {
  DIAS_SEMANA,
  type AsignacionSimulada,
  type ClienteTerritorio,
  type DiaSemana,
  type ParametrosSimulacion,
  type Vendedor,
  type ZonaSimulada,
} from "../types";
import { calcularPoligono } from "./poligono";

interface Punto {
  lat: number;
  lng: number;
}

function centroide(puntos: Punto[]): Punto {
  const n = puntos.length || 1;
  return {
    lat: puntos.reduce((s, p) => s + p.lat, 0) / n,
    lng: puntos.reduce((s, p) => s + p.lng, 0) / n,
  };
}

function distanciaKm(a: Punto, b: Punto): number {
  return turf.distance(turf.point([a.lng, a.lat]), turf.point([b.lng, b.lat]), { units: "kilometers" });
}

/** Radio p90 respecto al centroide — proxy barato de dispersión (evita O(n²) de comparar todos contra todos). */
function dispersionKm(puntos: Punto[]): number {
  if (puntos.length < 2) return 0;
  const c = centroide(puntos);
  const distancias = puntos.map((p) => distanciaKm(c, p)).sort((a, b) => a - b);
  const idx = Math.min(distancias.length - 1, Math.floor(distancias.length * 0.9));
  return distancias[idx];
}

function varianza(nums: number[]): number {
  const m = nums.reduce((s, x) => s + x, 0) / (nums.length || 1);
  return nums.reduce((s, x) => s + (x - m) ** 2, 0) / (nums.length || 1);
}

/** Tamaños grandes/medianos (candidatos a Semanal por tamaño) — según las
 * descripciones reales de `Clasifica` (A Grande, B Mediano, C Pequeño, D1..D4
 * Muy Pequeño, Sin Clasificación). */
function esGrande(claDescripcion: string): boolean {
  const t = (claDescripcion || "").trim().toUpperCase();
  return t.startsWith("A") || t.startsWith("B");
}

/** Compra en al menos 4 de los últimos 6 meses = cliente frecuente — 2do
 * criterio (después del tamaño) para candidatear a Semanal. */
function esFrecuente(mesesConCompra: number | undefined): boolean {
  return (mesesConCompra ?? 0) >= 4;
}

/** Split simple en 2 mitades geográficamente coherentes: ordena por la
 * coordenada de mayor varianza y parte por la mediana. Suficiente para armar
 * las 2 sub-rutas quincenales alternas (no hace falta un k-means real aquí). */
function partirEnMitades<T extends Punto>(clientes: T[]): [T[], T[]] {
  if (clientes.length <= 1) return [clientes, []];
  const lats = clientes.map((c) => c.lat);
  const lngs = clientes.map((c) => c.lng);
  const porLat = varianza(lats) >= varianza(lngs);
  const ordenados = [...clientes].sort((a, b) => (porLat ? a.lat - b.lat : a.lng - b.lng));
  const mitad = Math.ceil(ordenados.length / 2);
  return [ordenados.slice(0, mitad), ordenados.slice(mitad)];
}

export function asAsignacion(c: ClienteTerritorio): AsignacionSimulada {
  return {
    cliId: c.cliId,
    cliCodigo: c.cliCodigo,
    cliNombre: c.cliNombre,
    lat: c.lat,
    lng: c.lng,
    claDescripcion: c.claDescripcion,
    semanasActivas: [1, 2, 3, 4],
    esPrimero: false,
    esUltimo: false,
    repCodigoOriginal: c.repCodigoActual,
    diaOriginal: c.diaActual,
    semanasActivasOriginal: c.semanasActivasActual,
  };
}

function construirZona(
  dia: DiaSemana,
  vendedor: Vendedor,
  frecuencia: "Semanal" | "Quincenal",
  semanasActivas: number[],
  clientes: AsignacionSimulada[]
): ZonaSimulada {
  const clientesConSemana = clientes.map((c) => ({ ...c, semanasActivas }));
  const polygon = calcularPoligono(clientes);
  return {
    id: `${dia}-${vendedor.repCodigo}-${semanasActivas.join("")}`,
    dia,
    repCodigo: vendedor.repCodigo,
    repNombre: vendedor.repNombre,
    frecuencia,
    semanasActivas,
    polygon,
    clientes: clientesConSemana,
  };
}

/**
 * Propuesta lineal: el DÍA de cada cliente se respeta tal cual está hoy (no
 * se cambia de día a nadie) — el pool de clientes de "Lunes" sigue siendo
 * exactamente el mismo, sin importar de qué vendedor eran. Lo único que la
 * simulación decide es QUÉ VENDEDOR atiende a cada quién dentro de ese pool
 * fijo del día: día por día, vendedor por vendedor (empezando cerca de donde
 * ya tenía su propia base de clientes), cada vendedor va reclamando el
 * cliente más cercano a su racimo hasta llegar al máximo por ruta — recién
 * ahí sigue el próximo vendedor con lo que quedó del mismo día. Esto evita
 * que 2+ vendedores queden mezclados en el mismo entorno geográfico.
 */
export function proponerRutas(territorio: ClienteTerritorio[], vendedoresQuedan: Vendedor[], parametros: ParametrosSimulacion): ZonaSimulada[] {
  if (vendedoresQuedan.length === 0 || territorio.length === 0) return [];
  const mesesPorCliId = new Map<number, number>();
  for (const c of territorio) if (c.mesesConCompra != null) mesesPorCliId.set(c.cliId, c.mesesConCompra);

  // Pool por día: el día de cada cliente NO se toca — se respeta tal cual está
  // hoy. Los que todavía no tienen día (recién incorporados desde el maestro
  // de Clientes) se ubican en el día cuyo pool ya armado esté geográficamente
  // más cerca.
  const poolPorDia = new Map<DiaSemana, ClienteTerritorio[]>();
  for (const d of DIAS_SEMANA) poolPorDia.set(d, []);
  const sinDia: ClienteTerritorio[] = [];
  for (const c of territorio) {
    if (c.diaActual) poolPorDia.get(c.diaActual)!.push(c);
    else sinDia.push(c);
  }
  let diasActivos = DIAS_SEMANA.filter((d) => poolPorDia.get(d)!.length > 0);
  if (diasActivos.length === 0) diasActivos = DIAS_SEMANA.filter((d) => d !== "Domingo");

  for (const c of sinDia) {
    let mejorDia = diasActivos[0];
    let mejorScore = Infinity;
    for (const d of diasActivos) {
      const grupo = poolPorDia.get(d)!;
      const score = grupo.length === 0 ? 0 : distanciaKm(centroide(grupo), c);
      const total = score + grupo.length * 0.0001; // desempate suave: reparte antes de amontonar
      if (total < mejorScore) {
        mejorScore = total;
        mejorDia = d;
      }
    }
    poolPorDia.get(mejorDia)!.push(c);
  }

  // Centroide "base" de cada vendedor que queda: el de TODOS sus clientes
  // actuales (en cualquier día) — es la semilla desde donde arranca a
  // reclamar cada día, así su ruta tiende a quedar cerca de donde ya opera.
  const centroideBasePorVendedor = new Map<string, Punto>();
  for (const v of vendedoresQuedan) {
    const propios = territorio.filter((c) => c.repCodigoActual === v.repCodigo);
    if (propios.length > 0) centroideBasePorVendedor.set(v.repCodigo, centroide(propios));
  }
  const centroideTerritorio = centroide(territorio);

  const porZona = new Map<string, AsignacionSimulada[]>(); // key = `${dia}|${repCodigo}`
  const sumaPorZona = new Map<string, { lat: number; lng: number; n: number }>();
  const countPorDiaVendedor = new Map<string, number>();
  const countPorVendedor = new Map<string, number>();
  for (const v of vendedoresQuedan) countPorVendedor.set(v.repCodigo, 0);

  function centroideVivo(dia: DiaSemana, repCodigo: string, indiceVendedor: number): Punto {
    const suma = sumaPorZona.get(`${dia}|${repCodigo}`);
    if (suma && suma.n > 0) return { lat: suma.lat / suma.n, lng: suma.lng / suma.n };
    if (centroideBasePorVendedor.has(repCodigo)) return centroideBasePorVendedor.get(repCodigo)!;
    // Vendedor sin ningún cliente propio todavía: punto de arranque cerca del
    // centro del territorio, con un pequeño offset para no colisionar todos
    // en el mismo punto.
    return { lat: centroideTerritorio.lat + indiceVendedor * 0.0005, lng: centroideTerritorio.lng + indiceVendedor * 0.0005 };
  }

  function registrarAsignacion(dia: DiaSemana, repCodigo: string, cliente: ClienteTerritorio) {
    const key = `${dia}|${repCodigo}`;
    if (!porZona.has(key)) porZona.set(key, []);
    porZona.get(key)!.push(asAsignacion(cliente));
    countPorDiaVendedor.set(key, (countPorDiaVendedor.get(key) ?? 0) + 1);
    countPorVendedor.set(repCodigo, (countPorVendedor.get(repCodigo) ?? 0) + 1);
    const suma = sumaPorZona.get(key) ?? { lat: 0, lng: 0, n: 0 };
    sumaPorZona.set(key, { lat: suma.lat + cliente.lat, lng: suma.lng + cliente.lng, n: suma.n + 1 });
  }

  // "Bolsa" por ruta (día+vendedor): el doble del máximo pedido en el wizard,
  // porque de los clientes que caen ahí algunos terminarán Quincenal (solo la
  // mitad visita ese día real) — la cuenta fina que sí respeta el máximo real
  // se hace más abajo, en el reparto semanal/quincenal por zona.
  const cupoDiaBolsa = Math.max(1, Math.round(parametros.clientesPorDia * 2));
  const cupoVendedorGlobal = Math.max(1, Math.ceil(territorio.length / vendedoresQuedan.length));

  diasActivos.forEach((dia, diaIdx) => {
    const pool = [...poolPorDia.get(dia)!]; // copia mutable, se va vaciando con splice
    // Rota quién "empieza primero" cada día para no privilegiar siempre al
    // mismo vendedor con el racimo más central/grande.
    const offset = diaIdx % vendedoresQuedan.length;
    for (let vi = 0; vi < vendedoresQuedan.length; vi++) {
      const indiceVendedor = (vi + offset) % vendedoresQuedan.length;
      const v = vendedoresQuedan[indiceVendedor];
      const key = `${dia}|${v.repCodigo}`;
      let cupoRestante = Math.min(
        cupoDiaBolsa - (countPorDiaVendedor.get(key) ?? 0),
        cupoVendedorGlobal - (countPorVendedor.get(v.repCodigo) ?? 0)
      );
      while (cupoRestante > 0 && pool.length > 0) {
        const centro = centroideVivo(dia, v.repCodigo, indiceVendedor);
        let mejorIdx = 0;
        let mejorDist = Infinity;
        for (let pi = 0; pi < pool.length; pi++) {
          const d = distanciaKm(centro, pool[pi]);
          if (d < mejorDist) {
            mejorDist = d;
            mejorIdx = pi;
          }
        }
        const [cliente] = pool.splice(mejorIdx, 1);
        registrarAsignacion(dia, v.repCodigo, cliente);
        cupoRestante--;
      }
    }
    // Remanente del día (la suma de cupos no alcanzó): se reparte igual, al
    // vendedor con el racimo más cercano ESE MISMO día, para no perder a
    // nadie ni moverlo de día.
    for (const cliente of pool) {
      let mejorRep = vendedoresQuedan[0].repCodigo;
      let mejorDist = Infinity;
      vendedoresQuedan.forEach((v, i) => {
        const d = distanciaKm(centroideVivo(dia, v.repCodigo, i), cliente);
        if (d < mejorDist) {
          mejorDist = d;
          mejorRep = v.repCodigo;
        }
      });
      registrarAsignacion(dia, mejorRep, cliente);
    }
  });

  // Paso final: frecuencia por zona (+ partir en quincenas alternas si aplica).
  // Prioridad: 1) dispersión geográfica (si es muy grande, todo Quincenal —
  // ninguna ruta debería obligar a recorrer demasiados km cada semana);
  // 2) el "máximo por ruta" del wizard es lo que se VISITA en un día real —
  // los semanales cuentan siempre, pero de los quincenales solo cae la mitad
  // en cualquier semana dada (se alternan {1,3}/{2,4}). Por eso, para un lote
  // de `total` clientes con máximo `cupoDiaObjetivo`, la cantidad máxima que
  // puede ir Semanal es la que deja `semanales + quincenales/2 <= máximo`:
  //   maxSemanales = 2*máximo - total
  // Ej. máximo=70, total=100 → maxSemanales=40 (quedan 60 Quincenal, 30 por
  // semana alterna, 40+30=70 ✓). Si el lote ya cabe completo (total <=
  // máximo), no hay tope real y puede ir todo Semanal. Es dinámico por cada
  // zona: unas rutas pueden salir con más Semanales que otras según cuántos
  // clientes A/B o de compra frecuente tengan y cuánto quepa ese día.
  // 3) dentro de ese tope, se priorizan primero los A/B, luego los de mayor
  // frecuencia de compra real; 4) si el resultado es un grupo muy chico, no da
  // la logística para una ruta semanal dedicada — todos a Quincenal.
  const MIN_CLIENTES_SEMANAL = 5;
  const cupoDiaObjetivo = Math.max(1, parametros.clientesPorDia);
  const zonas: ZonaSimulada[] = [];
  for (const [key, clientes] of porZona) {
    const [dia, repCodigo] = key.split("|") as [DiaSemana, string];
    const vendedor = vendedoresQuedan.find((v) => v.repCodigo === repCodigo)!;

    if (parametros.frecuenciaSemanalForzada) {
      zonas.push(construirZona(dia, vendedor, "Semanal", [1, 2, 3, 4], clientes));
      continue;
    }

    const disp = dispersionKm(clientes);
    if (disp > parametros.umbralDispersionKm) {
      const [mitadA, mitadB] = partirEnMitades(clientes);
      if (mitadA.length) zonas.push(construirZona(dia, vendedor, "Quincenal", [1, 3], mitadA));
      if (mitadB.length) zonas.push(construirZona(dia, vendedor, "Quincenal", [2, 4], mitadB));
    } else {
      const candidatos = clientes
        .filter((c) => esGrande(c.claDescripcion) || esFrecuente(mesesPorCliId.get(c.cliId)))
        .sort((a, b) => {
          const ag = esGrande(a.claDescripcion) ? 1 : 0;
          const bg = esGrande(b.claDescripcion) ? 1 : 0;
          if (ag !== bg) return bg - ag; // A/B primero
          return (mesesPorCliId.get(b.cliId) ?? 0) - (mesesPorCliId.get(a.cliId) ?? 0); // luego más frecuentes
        });
      const maxSemanales = Math.max(0, Math.floor(2 * cupoDiaObjetivo - clientes.length));
      let k = Math.min(candidatos.length, maxSemanales);
      if (k < MIN_CLIENTES_SEMANAL) k = 0; // no da la logística para una ruta semanal dedicada

      const idsSemanal = new Set(candidatos.slice(0, k).map((c) => c.cliId));
      const semanales = clientes.filter((c) => idsSemanal.has(c.cliId));
      const resto = clientes.filter((c) => !idsSemanal.has(c.cliId));

      if (semanales.length) zonas.push(construirZona(dia, vendedor, "Semanal", [1, 2, 3, 4], semanales));
      if (resto.length) {
        const [mitadA, mitadB] = partirEnMitades(resto);
        if (mitadA.length) zonas.push(construirZona(dia, vendedor, "Quincenal", [1, 3], mitadA));
        if (mitadB.length) zonas.push(construirZona(dia, vendedor, "Quincenal", [2, 4], mitadB));
      }
    }
  }

  return zonas;
}
