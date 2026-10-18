import { useEffect, useMemo, useState } from "react";
import { fetchClientes, fetchFrecuenciaCompra, fetchVendedores } from "../api";
import {
  DIAS_SEMANA,
  type AsignacionSimulada,
  type ClienteCandidato,
  type ClienteRuta,
  type ClienteTerritorio,
  type DiaSemana,
  type ParametrosSimulacion,
  type RutaPorDia,
  type Vendedor,
  type ZonaSimulada,
} from "../types";
import { FiltroTerritorio, SIN_DIA_TERRITORIO } from "./FiltroTerritorio";
import { ResumenTerritorio } from "./ResumenTerritorio";
import { WizardParametros } from "./WizardParametros";
import { asAsignacion, proponerRutas } from "./proponerRutas";
import { DayBoard } from "../routeEditor/DayBoard";
import { ordenarPorCercania } from "../routeEditor/nearestNeighbor";
import type { VendedorOpcion } from "../routeEditor/DayColumn";
import { SimuladorMapa } from "./SimuladorMapa";
import { recalcularMembresia } from "./polygonMembership";
import { exportarSimulacion } from "./exportSimulacion";
import { ExportSimulacionModal } from "./ExportSimulacionModal";
import { calcularResumenPropuesta } from "./resumenPropuestaCalc";
import { ResumenPropuesta } from "./ResumenPropuesta";
import { calcularPoligono } from "./poligono";
import { eliminarPropuesta, guardarPropuesta, listarPropuestas, type PropuestaGuardada } from "./propuestasGuardadas";
import { PropuestasGuardadasModal } from "./PropuestasGuardadasModal";

interface Huerfano {
  dia: DiaSemana;
  cliente: AsignacionSimulada;
}

interface Props {
  all: ClienteRuta[];
}

const PARAMS_DEFAULT: ParametrosSimulacion = {
  clientesPorDia: 70,
  frecuenciaSemanalForzada: false,
  umbralDispersionKm: 9,
};

function emptySeleccion(): Record<DiaSemana, Set<number>> {
  const base = {} as Record<DiaSemana, Set<number>>;
  for (const d of DIAS_SEMANA) base[d] = new Set<number>();
  return base;
}

/** Encuentra en qué zona (día+vendedor) está un cliente, dado el día. */
function ubicarZona(zonas: ZonaSimulada[], dia: DiaSemana, cliId: number): ZonaSimulada | undefined {
  return zonas.find((z) => z.dia === dia && z.clientes.some((c) => c.cliId === cliId));
}

export function SimuladorEditor({ all }: Props) {
  const [vendedores, setVendedores] = useState<Vendedor[]>([]);
  const [clientesMaestro, setClientesMaestro] = useState<ClienteCandidato[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [seleccionVendedores, setSeleccionVendedores] = useState<Map<string, boolean>>(new Map());
  // Día(s)/semana(s) de visita ACTUALES que definen qué clientes entran a la
  // simulación (vacío = todos). Ver el memo de `territorio`.
  const [filtroDiasTerritorio, setFiltroDiasTerritorio] = useState<Set<string>>(new Set());
  const [filtroSemanasTerritorio, setFiltroSemanasTerritorio] = useState<Set<string>>(new Set());
  const [parametros, setParametros] = useState<ParametrosSimulacion>(PARAMS_DEFAULT);
  const [zonas, setZonas] = useState<ZonaSimulada[] | null>(null);
  const [seleccionTablero, setSeleccionTablero] = useState<Record<DiaSemana, Set<number>>>(emptySeleccion());
  const [query, setQuery] = useState("");
  const [vista, setVista] = useState<"tablero" | "mapa" | "resumen">("tablero");
  const [huerfanos, setHuerfanos] = useState<Huerfano[]>([]);
  const [mostrarExport, setMostrarExport] = useState(false);
  const [exportando, setExportando] = useState(false);
  const [exportMsg, setExportMsg] = useState<string | null>(null);
  const [filtrosColapsados, setFiltrosColapsados] = useState(false);
  const [huerfanosColapsados, setHuerfanosColapsados] = useState(false);
  const [excluidos, setExcluidos] = useState<Set<number>>(new Set());
  const [generando, setGenerando] = useState(false);
  const [generarError, setGenerarError] = useState<string | null>(null);
  // Cache simple: evita repetir la consulta de frecuencia de compra para un
  // cliente ya consultado en una regeneración anterior de la misma sesión.
  const [frecuenciaCache] = useState(() => new Map<number, number>());
  const [mostrarPropuestas, setMostrarPropuestas] = useState(false);
  const [propuestasGuardadas, setPropuestasGuardadas] = useState<PropuestaGuardada[]>([]);

  useEffect(() => {
    Promise.all([fetchVendedores(), fetchClientes()])
      .then(([v, c]) => {
        setVendedores(v);
        setClientesMaestro(c);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, []);

  function toggleIncluir(v: Vendedor) {
    setSeleccionVendedores((prev) => {
      const next = new Map(prev);
      if (next.has(v.repCodigo)) next.delete(v.repCodigo);
      else next.set(v.repCodigo, false);
      return next;
    });
    setZonas(null);
    setExcluidos(new Set());
  }

  function toggleSaliente(repCodigo: string) {
    setSeleccionVendedores((prev) => {
      const next = new Map(prev);
      next.set(repCodigo, !next.get(repCodigo));
      return next;
    });
    setZonas(null);
    setExcluidos(new Set());
  }

  // Cambiar qué días/semanas entran cambia el territorio completo, así que —igual
  // que al marcar/desmarcar un vendedor— la propuesta anterior ya no corresponde.
  function cambiarFiltroDias(next: Set<string>) {
    setFiltroDiasTerritorio(next);
    setZonas(null);
    setExcluidos(new Set());
    setHuerfanos([]);
  }

  function cambiarFiltroSemanas(next: Set<string>) {
    setFiltroSemanasTerritorio(next);
    setZonas(null);
    setExcluidos(new Set());
    setHuerfanos([]);
  }

  // Territorio = unión de clientes de los vendedores seleccionados: los que ya
  // están en RutaVisitas (con día/semana actual) + los del maestro Clientes que
  // son suyos pero todavía no tienen fila en RutaVisitas.
  const territorio = useMemo<ClienteTerritorio[]>(() => {
    if (seleccionVendedores.size === 0) return [];
    const codigos = new Set(seleccionVendedores.keys());
    const porCliId = new Map<number, ClienteTerritorio>();
    for (const r of all) {
      if (!codigos.has(r.repCodigo)) continue;
      if (!(DIAS_SEMANA as readonly string[]).includes(r.dia)) continue;
      porCliId.set(r.cliId, {
        cliId: r.cliId,
        cliCodigo: r.cliCodigo,
        cliNombre: r.cliNombre,
        lat: r.lat,
        lng: r.lng,
        claDescripcion: r.claDescripcion,
        diaActual: r.dia as DiaSemana,
        repCodigoActual: r.repCodigo,
        semanasActivasActual: r.semanasActivas,
      });
    }
    for (const c of clientesMaestro) {
      if (!c.duenoCodigo || !codigos.has(c.duenoCodigo) || porCliId.has(c.cliId)) continue;
      porCliId.set(c.cliId, {
        cliId: c.cliId,
        cliCodigo: c.cliCodigo,
        cliNombre: c.cliNombre,
        lat: c.lat,
        lng: c.lng,
        claDescripcion: c.claDescripcion,
        diaActual: null,
        repCodigoActual: c.duenoCodigo,
        semanasActivasActual: [],
      });
    }
    if (excluidos.size > 0) for (const cliId of excluidos) porCliId.delete(cliId);
    // Filtro de día/semana de visita ACTUALES (RutaVisitas): sin ninguno marcado
    // entran todos los clientes de los vendedores; con alguno, solo los que hoy
    // caen en esos días y/o semanas (ANY, igual que los filtros de Por Día).
    const filtraDia = filtroDiasTerritorio.size > 0;
    const filtraSemana = filtroSemanasTerritorio.size > 0;
    if (!filtraDia && !filtraSemana) return [...porCliId.values()];
    return [...porCliId.values()].filter((c) => {
      if (filtraDia && !filtroDiasTerritorio.has(c.diaActual ?? SIN_DIA_TERRITORIO)) return false;
      if (filtraSemana && !c.semanasActivasActual.some((s) => filtroSemanasTerritorio.has(String(s)))) return false;
      return true;
    });
  }, [all, clientesMaestro, seleccionVendedores, excluidos, filtroDiasTerritorio, filtroSemanasTerritorio]);

  const vendedoresQuedan = useMemo(
    () => vendedores.filter((v) => seleccionVendedores.get(v.repCodigo) === false),
    [vendedores, seleccionVendedores]
  );
  const divisionPorRep = useMemo(() => new Map(vendedores.map((v) => [v.repCodigo, v.division])), [vendedores]);
  const vendedorPorCodigo = useMemo(() => new Map(vendedores.map((v) => [v.repCodigo, v])), [vendedores]);
  // Cubre TODOS los vendedores del sistema (no solo los seleccionados) — hace
  // falta para mostrar el nombre del vendedor "anterior" de un cliente aunque
  // ese vendedor haya salido del equipo, y para los representantes a los que se
  // les asigna a mano aunque no estén en la simulación.
  const nombrePorCodigoTodos = useMemo(() => new Map(vendedores.map((v) => [v.repCodigo, v.repNombre])), [vendedores]);
  const vendedorSupervisorPorCodigo = useMemo(
    () => new Map(vendedores.map((v) => [v.repCodigo, v.supNombre || "(Sin Supervisor)"])),
    [vendedores]
  );
  const salientesCodigos = useMemo(
    () => [...seleccionVendedores.entries()].filter(([, saliente]) => saliente).map(([repCodigo]) => repCodigo),
    [seleccionVendedores]
  );
  const salientesSet = useMemo(() => new Set(salientesCodigos), [salientesCodigos]);
  const repsEnZonas = useMemo(() => new Set((zonas ?? []).map((z) => z.repCodigo)), [zonas]);

  // Cualquier representante (salvo los salientes, que se van del equipo) se puede
  // elegir como destino, esté o no seleccionado en el territorio — los que
  // quedan en la simulación van primero.
  const vendedorOpcionesTodos = useMemo<VendedorOpcion[]>(
    () =>
      vendedores
        .filter((v) => !salientesSet.has(v.repCodigo))
        .map((v) => ({ repCodigo: v.repCodigo, repNombre: v.repNombre, enSimulacion: seleccionVendedores.get(v.repCodigo) === false }))
        .sort((a, b) => Number(b.enSimulacion) - Number(a.enSimulacion) || a.repNombre.localeCompare(b.repNombre)),
    [vendedores, salientesSet, seleccionVendedores]
  );
  // Selector por fila del tablero de Días: lista corta (los que quedan + los de
  // afuera que ya recibieron clientes) — con ~200 representantes por fila el
  // DOM se volvería enorme; para elegir uno cualquiera se usa el panel de asignar
  // del mapa o el de "sin asignar".
  const vendedorOpciones = useMemo(
    () => vendedorOpcionesTodos.filter((v) => v.enSimulacion || repsEnZonas.has(v.repCodigo)),
    [vendedorOpcionesTodos, repsEnZonas]
  );
  // Orden estable (por código) — define qué ícono le toca a cada vendedor, para
  // que no cambie de forma entre renders. Incluye a todos los seleccionados
  // (el cliente se dibuja con el ícono de su vendedor ACTUAL antes de asignarlo)
  // y a cualquier representante de afuera que haya recibido clientes.
  const vendedorCodigos = useMemo(() => [...new Set([...seleccionVendedores.keys(), ...repsEnZonas])].sort(), [seleccionVendedores, repsEnZonas]);

  // Quedan en la propuesta = los marcados "quedan" + los de afuera con clientes.
  const vendedoresAhoraN = useMemo(
    () => new Set([...vendedoresQuedan.map((v) => v.repCodigo), ...repsEnZonas]).size,
    [vendedoresQuedan, repsEnZonas]
  );

  const resumen = useMemo(
    () =>
      zonas
        ? calcularResumenPropuesta(zonas, seleccionVendedores.size, vendedoresAhoraN, nombrePorCodigoTodos, salientesCodigos)
        : null,
    [zonas, seleccionVendedores, vendedoresAhoraN, nombrePorCodigoTodos, salientesCodigos]
  );

  // Todos los clientes "dentro" de la simulación (territorio + excluidos + los
  // ya asignados): el servidor solo "quita" de la ruta de un vendedor a los de
  // esta lista que ya no le quedaron — nunca toca el resto de su ruta (clientes
  // de días/semanas que el filtro dejó afuera, o de un representante de afuera
  // al que solo se le asignaron algunos clientes).
  const universoCliIds = useMemo(() => {
    const s = new Set<number>(territorio.map((c) => c.cliId));
    for (const id of excluidos) s.add(id);
    for (const z of zonas ?? []) for (const c of z.clientes) s.add(c.cliId);
    return [...s];
  }, [territorio, excluidos, zonas]);

  async function handleExportar(desde: string, hasta: string, suplidores: number[]) {
    if (!zonas || !resumen) return;
    setExportando(true);
    setExportMsg(null);
    try {
      const msg = await exportarSimulacion({
        zonas,
        salientes: salientesCodigos,
        divisionPorRep,
        nombrePorCodigoTodos,
        desde,
        hasta,
        suplidores,
        resumen,
        universoCliIds,
      });
      setMostrarExport(false);
      setExportMsg(msg);
    } catch (e) {
      setExportMsg(e instanceof Error ? e.message : String(e));
    } finally {
      setExportando(false);
    }
  }

  function abrirPropuestasGuardadas() {
    setPropuestasGuardadas(listarPropuestas());
    setMostrarPropuestas(true);
  }

  // Guardar la propuesta actual (localStorage, no toca el servidor) para
  // revisarla más tarde y decidir si se sigue ajustando o se exporta tal cual.
  function handleGuardarPropuesta(nombre: string) {
    if (!zonas) return;
    const resumenTexto = resumen
      ? `${resumen.totalVendedoresAntes} → ${resumen.totalVendedoresAhora} vendedor(es) · ${resumen.totalClientes.toLocaleString()} clientes`
      : `${territorio.length.toLocaleString()} clientes`;
    guardarPropuesta({
      nombre,
      seleccionVendedores: [...seleccionVendedores.entries()],
      parametros,
      excluidos: [...excluidos],
      zonas,
      huerfanos,
      resumenTexto,
      filtroDias: [...filtroDiasTerritorio],
      filtroSemanas: [...filtroSemanasTerritorio],
    });
    setPropuestasGuardadas(listarPropuestas());
  }

  function handleCargarPropuesta(p: PropuestaGuardada) {
    setSeleccionVendedores(new Map(p.seleccionVendedores));
    setFiltroDiasTerritorio(new Set(p.filtroDias ?? []));
    setFiltroSemanasTerritorio(new Set(p.filtroSemanas ?? []));
    setParametros(p.parametros);
    setExcluidos(new Set(p.excluidos));
    setZonas(p.zonas);
    setHuerfanos(p.huerfanos);
    setSeleccionTablero(emptySeleccion());
    setVista("tablero");
    setMostrarPropuestas(false);
  }

  function handleEliminarPropuesta(id: string) {
    eliminarPropuesta(id);
    setPropuestasGuardadas(listarPropuestas());
  }

  async function generarPropuesta() {
    if (vendedoresQuedan.length === 0 || territorio.length === 0) return;
    setGenerando(true);
    setGenerarError(null);
    try {
      const faltantes = territorio.filter((c) => !frecuenciaCache.has(c.cliId)).map((c) => c.cliId);
      if (faltantes.length > 0) {
        const nuevos = await fetchFrecuenciaCompra(faltantes);
        for (const [cliId, meses] of nuevos) frecuenciaCache.set(cliId, meses);
        // Los que no compraron nada en la ventana no vienen en la respuesta — se
        // marcan en 0 para no volver a consultarlos en la próxima regeneración.
        for (const cliId of faltantes) if (!frecuenciaCache.has(cliId)) frecuenciaCache.set(cliId, 0);
      }
      const territorioConFrecuencia = territorio.map((c) => ({ ...c, mesesConCompra: frecuenciaCache.get(c.cliId) ?? 0 }));
      setZonas(proponerRutas(territorioConFrecuencia, vendedoresQuedan, parametros));
      setSeleccionTablero(emptySeleccion());
      setHuerfanos([]);
    } catch (e) {
      setGenerarError(e instanceof Error ? e.message : String(e));
    } finally {
      setGenerando(false);
    }
  }

  // Alternativa a "Generar propuesta": no corre el algoritmo — carga el
  // territorio completo en el mapa sin asignar día ni vendedor a nadie, para
  // que el usuario decida cliente por cliente (o en lote, seleccionando varios)
  // a mano, viendo siempre a quién/qué día pertenecía antes cada uno.
  function iniciarSinPropuesta() {
    if (vendedoresQuedan.length === 0 || territorio.length === 0) return;
    setZonas([]);
    setSeleccionTablero(emptySeleccion());
    setHuerfanos([]);
    setVista("mapa");
  }

  // Excluir clientes seleccionados en el mapa: los saca del territorio (no se
  // vuelven a proponer) y, si ya había una propuesta generada, también los saca
  // de las zonas al toque — pero la redistribución real se deja para el próximo
  // "Generar propuesta" que presione el usuario.
  function excluirClientes(cliIds: number[]) {
    if (cliIds.length === 0) return;
    const idsSet = new Set(cliIds);
    setExcluidos((prev) => {
      const next = new Set(prev);
      for (const id of cliIds) next.add(id);
      return next;
    });
    setZonas((prev) =>
      prev
        ? prev
            .map((z) => {
              const clientes = z.clientes.filter((c) => !idsSet.has(c.cliId));
              if (clientes.length === z.clientes.length) return z;
              return { ...z, clientes, polygon: calcularPoligono(clientes) };
            })
            .filter((z) => z.clientes.length > 0)
        : prev
    );
  }

  function restaurarExcluidos() {
    setExcluidos(new Set());
  }

  function handleVertexDrag(zonaId: string, vertexIdx: number, lat: number, lng: number) {
    setZonas((prev) => {
      if (!prev) return prev;
      const conNuevoVertice = prev.map((z) => {
        if (z.id !== zonaId || !z.polygon) return z;
        const polygon: [number, number][] = [...z.polygon];
        polygon[vertexIdx] = [lat, lng];
        return { ...z, polygon };
      });
      const zonaEditada = conNuevoVertice.find((z) => z.id === zonaId)!;
      const { zonas: recalculadas, huerfanos: nuevos } = recalcularMembresia(conNuevoVertice, zonaId);
      if (nuevos.length > 0) {
        setHuerfanos((prevH) => {
          const nuevosH = nuevos.map((cliente) => ({ dia: zonaEditada.dia, cliente }));
          const combinados = [...prevH, ...nuevosH];
          return combinados.filter((h, i) => combinados.findIndex((x) => x.cliente.cliId === h.cliente.cliId) === i);
        });
      }
      return recalculadas;
    });
  }

  function asignarHuerfano(huerfano: Huerfano, repCodigo: string) {
    const vendedor = vendedorPorCodigo.get(repCodigo);
    if (!vendedor) return;
    const { dia, cliente } = huerfano;
    setZonas((prev) => {
      const base = prev ?? [];
      const destino = base.find(
        (z) => z.dia === dia && z.repCodigo === repCodigo && z.semanasActivas.join(",") === cliente.semanasActivas.join(",")
      );
      if (destino) {
        const clientes = [...destino.clientes, cliente];
        return base.map((z) => (z === destino ? { ...z, clientes, polygon: calcularPoligono(clientes) } : z));
      }
      return [
        ...base,
        {
          id: `${dia}-${repCodigo}-${cliente.semanasActivas.join("")}-huerfano-${cliente.cliId}`,
          dia,
          repCodigo,
          repNombre: vendedor.repNombre,
          frecuencia: cliente.semanasActivas.length === 4 ? ("Semanal" as const) : ("Quincenal" as const),
          semanasActivas: cliente.semanasActivas,
          polygon: null,
          clientes: [cliente],
        },
      ];
    });
    setHuerfanos((prev) => prev.filter((h) => h.cliente.cliId !== cliente.cliId));
  }

  // ── Vista de tablero: zonas -> RutaPorDia (varias zonas de vendedores
  // distintos pueden compartir el mismo día; se muestran todas juntas). ──
  const rutaPorDia = useMemo<RutaPorDia>(() => {
    const base: RutaPorDia = {} as RutaPorDia;
    for (const d of DIAS_SEMANA) base[d] = [];
    for (const z of zonas ?? []) base[z.dia] = [...base[z.dia], ...z.clientes];
    return base;
  }, [zonas]);

  const vendedorPorCliId = useMemo(() => {
    const m = new Map<number, string>();
    for (const z of zonas ?? []) for (const c of z.clientes) m.set(c.cliId, z.repCodigo);
    return m;
  }, [zonas]);

  function mutarCliente(dia: DiaSemana, cliId: number, mut: (c: AsignacionSimulada) => AsignacionSimulada) {
    setZonas((prev) => {
      if (!prev) return prev;
      return prev.map((z) => {
        if (z.dia !== dia) return z;
        const idx = z.clientes.findIndex((c) => c.cliId === cliId);
        if (idx === -1) return z;
        const clientes = [...z.clientes];
        clientes[idx] = mut(clientes[idx]);
        return { ...z, clientes };
      });
    });
  }

  function toggleSemana(dia: DiaSemana, cliId: number, semana: number) {
    mutarCliente(dia, cliId, (c) => {
      const activas = c.semanasActivas.includes(semana) ? c.semanasActivas.filter((s) => s !== semana) : [...c.semanasActivas, semana].sort();
      return { ...c, semanasActivas: activas };
    });
  }

  function togglePrimero(dia: DiaSemana, cliId: number) {
    setZonas((prev) => {
      if (!prev) return prev;
      return prev.map((z) => {
        if (z.dia !== dia || !z.clientes.some((c) => c.cliId === cliId)) return z;
        const activo = z.clientes.find((c) => c.cliId === cliId)!.esPrimero;
        const limpio = z.clientes.map((c) => ({ ...c, esPrimero: false }));
        if (activo) return { ...z, clientes: limpio };
        const idx = limpio.findIndex((c) => c.cliId === cliId);
        const [item] = limpio.splice(idx, 1);
        return { ...z, clientes: [{ ...item, esPrimero: true }, ...limpio] };
      });
    });
  }

  function toggleUltimo(dia: DiaSemana, cliId: number) {
    setZonas((prev) => {
      if (!prev) return prev;
      return prev.map((z) => {
        if (z.dia !== dia || !z.clientes.some((c) => c.cliId === cliId)) return z;
        const activo = z.clientes.find((c) => c.cliId === cliId)!.esUltimo;
        const limpio = z.clientes.map((c) => ({ ...c, esUltimo: false }));
        if (activo) return { ...z, clientes: limpio };
        const idx = limpio.findIndex((c) => c.cliId === cliId);
        const [item] = limpio.splice(idx, 1);
        return { ...z, clientes: [...limpio, { ...item, esUltimo: true }] };
      });
    });
  }

  function cambiarVendedor(dia: DiaSemana, cliId: number, repCodigo: string) {
    setZonas((prev) => {
      if (!prev) return prev;
      const origen = ubicarZona(prev, dia, cliId);
      if (!origen || origen.repCodigo === repCodigo) return prev;
      const cliente = origen.clientes.find((c) => c.cliId === cliId)!;
      const clientesOrigen = origen.clientes.filter((c) => c.cliId !== cliId);
      let next = prev.map((z) => (z === origen ? { ...z, clientes: clientesOrigen, polygon: calcularPoligono(clientesOrigen) } : z));

      const destino = next.find(
        (z) => z.dia === dia && z.repCodigo === repCodigo && z.frecuencia === origen.frecuencia && z.semanasActivas.join(",") === cliente.semanasActivas.join(",")
      );
      const vendedor = vendedorPorCodigo.get(repCodigo);
      if (destino) {
        const clientesDestino = [...destino.clientes, cliente];
        next = next.map((z) => (z === destino ? { ...z, clientes: clientesDestino, polygon: calcularPoligono(clientesDestino) } : z));
      } else if (vendedor) {
        next = [
          ...next,
          {
            id: `${dia}-${repCodigo}-${cliente.semanasActivas.join("")}-manual`,
            dia,
            repCodigo,
            repNombre: vendedor.repNombre,
            frecuencia: origen.frecuencia,
            semanasActivas: cliente.semanasActivas,
            polygon: null,
            clientes: [cliente],
          },
        ];
      }
      return next.filter((z) => z.clientes.length > 0);
    });
  }

  // Asignación manual desde el mapa: uno o varios clientes (de cualquier zona
  // actual) a cualquier vendedor/día/semanas — igual que "cambiarVendedor" pero
  // en lote y también puede cambiar el día (no solo el vendedor).
  function asignarClientesAMano(cliIds: number[], dia: DiaSemana, repCodigo: string, semanasActivas: number[]) {
    const vendedor = vendedorPorCodigo.get(repCodigo);
    if (!vendedor || cliIds.length === 0) return;
    const idsSet = new Set(cliIds);
    // Desde el mapa previo a "Generar propuesta" todavía no hay zonas: se empieza
    // una lista vacía (igual que "Clientes sin Propuesta") y se pasa a la vista de mapa.
    if (!zonas) setVista("mapa");
    setZonas((prevZonas) => {
      const prev = prevZonas ?? [];
      const movidos: AsignacionSimulada[] = [];
      const encontrados = new Set<number>();
      let next = prev.map((z) => {
        const quedan = z.clientes.filter((c) => !idsSet.has(c.cliId));
        if (quedan.length === z.clientes.length) return z;
        const removidos = z.clientes.filter((c) => idsSet.has(c.cliId));
        movidos.push(...removidos);
        for (const c of removidos) encontrados.add(c.cliId);
        return { ...z, clientes: quedan, polygon: calcularPoligono(quedan) };
      });
      // Clientes que no estaban en NINGUNA zona todavía (el pool "sin asignar"
      // de "Clientes sin Propuesta") — se arman desde el territorio en vez de
      // tratar de sacarlos de una zona que nunca los tuvo.
      for (const cliId of cliIds) {
        if (encontrados.has(cliId)) continue;
        const c = territorio.find((t) => t.cliId === cliId);
        if (c) movidos.push(asAsignacion(c));
      }
      if (movidos.length === 0) return prev;
      const actualizados = movidos.map((c) => ({ ...c, semanasActivas }));
      const frecuencia: "Semanal" | "Quincenal" = semanasActivas.length === 4 ? "Semanal" : "Quincenal";
      const destino = next.find((z) => z.dia === dia && z.repCodigo === repCodigo && z.semanasActivas.join(",") === semanasActivas.join(","));
      if (destino) {
        const clientesDestino = [...destino.clientes, ...actualizados];
        next = next.map((z) => (z === destino ? { ...z, clientes: clientesDestino, polygon: calcularPoligono(clientesDestino) } : z));
      } else {
        next = [
          ...next,
          {
            id: `${dia}-${repCodigo}-${semanasActivas.join("")}-manual-${cliIds[0]}`,
            dia,
            repCodigo,
            repNombre: vendedor.repNombre,
            frecuencia,
            semanasActivas,
            polygon: calcularPoligono(actualizados),
            clientes: actualizados,
          },
        ];
      }
      return next.filter((z) => z.clientes.length > 0);
    });
  }

  function quitarSeleccion(dia: DiaSemana) {
    const ids = seleccionTablero[dia];
    if (ids.size === 0) return;
    setZonas((prev) =>
      (prev ?? [])
        .map((z) => {
          if (z.dia !== dia) return z;
          const clientes = z.clientes.filter((c) => !ids.has(c.cliId));
          return clientes.length === z.clientes.length ? z : { ...z, clientes, polygon: calcularPoligono(clientes) };
        })
        .filter((z) => z.clientes.length > 0)
    );
    setSeleccionTablero((prev) => ({ ...prev, [dia]: new Set() }));
  }

  function moverSeleccion(dia: DiaSemana, direccion: "arriba" | "abajo") {
    const ids = seleccionTablero[dia];
    if (ids.size === 0) return;
    setZonas((prev) =>
      (prev ?? []).map((z) => {
        if (z.dia !== dia) return z;
        const arr = [...z.clientes];
        if (direccion === "arriba") {
          for (let i = 1; i < arr.length; i++) {
            if (ids.has(arr[i].cliId) && !ids.has(arr[i - 1].cliId)) [arr[i - 1], arr[i]] = [arr[i], arr[i - 1]];
          }
        } else {
          for (let i = arr.length - 2; i >= 0; i--) {
            if (ids.has(arr[i].cliId) && !ids.has(arr[i + 1].cliId)) [arr[i], arr[i + 1]] = [arr[i + 1], arr[i]];
          }
        }
        return { ...z, clientes: arr };
      })
    );
  }

  function ordenarCercania(dia: DiaSemana) {
    // Ordena cada zona (vendedor) de ese día por vecino más cercano, por separado
    // — las posiciones/orden son por vendedor, no tiene sentido mezclar entre ellos.
    setZonas((prev) => (prev ?? []).map((z) => (z.dia === dia ? { ...z, clientes: ordenarPorCercania(z.clientes) as AsignacionSimulada[] } : z)));
  }

  function toggleSeleccion(dia: DiaSemana, cliId: number) {
    setSeleccionTablero((prev) => {
      const next = new Set(prev[dia]);
      if (next.has(cliId)) next.delete(cliId);
      else next.add(cliId);
      return { ...prev, [dia]: next };
    });
  }

  if (loading) return <div className="route-empty">Cargando vendedores y clientes…</div>;
  if (error) return <div className="route-empty">Error: {error}</div>;

  return (
    <div className="route-editor">
      <div className="route-toolbar">
        <div className="sim-titulo">🧭 Simular Nuevas Rutas</div>
        <span className="stat">Elige supervisor/vendedores, marca quién sale del equipo, y genera una propuesta editable.</span>
        <button className="board-toggle-btn" onClick={() => setFiltrosColapsados((v) => !v)}>
          {filtrosColapsados ? "▼ Mostrar filtros" : "▲ Ocultar filtros (ver mapa)"}
        </button>
        {filtrosColapsados && (
          <button
            className="export-btn"
            onClick={generarPropuesta}
            disabled={territorio.length === 0 || vendedoresQuedan.length === 0 || generando}
            title="Vuelve a generar la propuesta con el territorio actual (por ejemplo, tras excluir clientes en el mapa)"
          >
            {generando ? "⏳ Calculando…" : "🧭 Generar propuesta"}
          </button>
        )}
        {filtrosColapsados && (
          <button
            className="board-toggle-btn"
            onClick={iniciarSinPropuesta}
            disabled={territorio.length === 0 || vendedoresQuedan.length === 0 || generando}
            title="Carga todos los clientes en el mapa sin asignarles día ni vendedor — vos decidís cliente por cliente."
          >
            🗒 Clientes sin Propuesta
          </button>
        )}
        {generarError && <span className="stat">Error: {generarError}</span>}
        {zonas && (
          <button className={`board-toggle-btn ${vista === "tablero" ? "active" : ""}`} onClick={() => setVista("tablero")}>
            📋 Días
          </button>
        )}
        {zonas && (
          <button className={`board-toggle-btn ${vista === "mapa" ? "active" : ""}`} onClick={() => setVista("mapa")}>
            🗺 Mapa
          </button>
        )}
        {zonas && (
          <button className={`board-toggle-btn ${vista === "resumen" ? "active" : ""}`} onClick={() => setVista("resumen")}>
            📊 Resumen
          </button>
        )}
        {zonas && (
          <button className="export-btn" onClick={() => setMostrarExport(true)}>
            ⬇ Exportar propuesta
          </button>
        )}
        <button className="board-toggle-btn" onClick={abrirPropuestasGuardadas}>
          💾 Propuestas guardadas
        </button>
        {exportMsg && <span className="stat">{exportMsg}</span>}
      </div>
      {!filtrosColapsados && (
        <>
          <FiltroTerritorio
            vendedores={vendedores}
            seleccion={seleccionVendedores}
            onToggleIncluir={toggleIncluir}
            onToggleSaliente={toggleSaliente}
            filtroDias={filtroDiasTerritorio}
            onFiltroDias={cambiarFiltroDias}
            filtroSemanas={filtroSemanasTerritorio}
            onFiltroSemanas={cambiarFiltroSemanas}
          />
          <ResumenTerritorio clientes={territorio} />
          <WizardParametros
            parametros={parametros}
            onChange={setParametros}
            onGenerar={generarPropuesta}
            onSinPropuesta={iniciarSinPropuesta}
            disabled={territorio.length === 0 || vendedoresQuedan.length === 0}
            generando={generando}
          />
        </>
      )}
      {excluidos.size > 0 && (
        <div className="sim-huerfanos">
          <div className="sim-huerfanos-titulo">
            🚫 {excluidos.size} cliente(s) excluido(s) de la propuesta (seleccionados en el mapa) — quedarán sin utilizar hasta que los restaures.
          </div>
          <div className="sim-huerfano-row">
            <button className="board-toggle-btn" onClick={restaurarExcluidos}>
              ↺ Restaurar excluidos
            </button>
            {zonas && <span className="stat">Recuerda presionar "Generar propuesta" para redistribuir tras excluir/restaurar.</span>}
          </div>
        </div>
      )}
      {!zonas && (
        <div className="route-empty">
          {seleccionVendedores.size === 0
            ? "Elige al menos 2 vendedores en el filtro de arriba."
            : vendedoresQuedan.length === 0
              ? 'Marca al menos un vendedor que se queda (déjalo sin el check de "Saliente").'
              : 'Ajusta los parámetros y presiona "Generar propuesta".'}
        </div>
      )}
      {huerfanos.length > 0 && (
        <div className="sim-huerfanos">
          <div className="sim-huerfanos-titulo">
            <span>
              ⚠ {huerfanos.length} cliente(s) sin asignar tras editar un polígono — elige a quién se los das:
            </span>
            <button
              className="board-toggle-btn sim-huerfanos-toggle"
              onClick={() => setHuerfanosColapsados((v) => !v)}
              title={huerfanosColapsados ? "Mostrar la lista de clientes sin asignar" : "Ocultar la lista para ver mejor el mapa"}
            >
              {huerfanosColapsados ? "▼ Mostrar" : "▲ Ocultar"}
            </button>
          </div>
          {!huerfanosColapsados && (
            <div className="sim-huerfanos-lista">
              {huerfanos.map((h) => (
                <div className="sim-huerfano-row" key={h.cliente.cliId}>
                  <span>
                    {h.cliente.cliCodigo} - {h.cliente.cliNombre} ({h.dia})
                  </span>
                  <select defaultValue="" onChange={(e) => e.target.value && asignarHuerfano(h, e.target.value)}>
                    <option value="" disabled>
                      Asignar a…
                    </option>
                    {vendedorOpcionesTodos.map((v) => (
                      <option key={v.repCodigo} value={v.repCodigo}>
                        {v.repCodigo} - {v.repNombre}
                        {v.enSimulacion ? "" : " (fuera de la simulación)"}
                      </option>
                    ))}
                  </select>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
      {zonas && vista === "tablero" && (
        <div className="route-body">
          <div className="route-board-map">
            <div className="route-map-toolbar">
              <input
                className="ms-search"
                placeholder="Buscar cliente por código o nombre…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
            </div>
            <DayBoard
              ruta={rutaPorDia}
              seleccion={seleccionTablero}
              query={query}
              onToggleSeleccion={toggleSeleccion}
              onMoverSeleccion={moverSeleccion}
              onQuitarSeleccion={quitarSeleccion}
              onTogglePrimero={togglePrimero}
              onToggleUltimo={toggleUltimo}
              onToggleSemana={toggleSemana}
              onOrdenarCercania={ordenarCercania}
              vendedorOpciones={vendedorOpciones}
              vendedorPorCliId={vendedorPorCliId}
              onCambiarVendedor={cambiarVendedor}
            />
          </div>
        </div>
      )}
      {zonas && vista === "mapa" && (
        <div className="route-body">
          <div className="route-map">
            <SimuladorMapa
              territorio={territorio}
              zonas={zonas}
              vendedorCodigos={vendedorCodigos}
              vendedorNombrePorCodigo={nombrePorCodigoTodos}
              vendedorSupervisorPorCodigo={vendedorSupervisorPorCodigo}
              nombrePorCodigoTodos={nombrePorCodigoTodos}
              onVertexDrag={handleVertexDrag}
              onExcluir={excluirClientes}
              onAsignar={asignarClientesAMano}
              vendedorOpciones={vendedorOpcionesTodos}
            />
          </div>
        </div>
      )}
      {zonas && vista === "resumen" && resumen && (
        <div className="route-body">
          <ResumenPropuesta resumen={resumen} />
        </div>
      )}
      {!zonas && territorio.length > 0 && vendedoresQuedan.length > 0 && (
        <div className="route-body">
          <div className="route-map">
            <SimuladorMapa
              territorio={territorio}
              zonas={null}
              vendedorCodigos={vendedorCodigos}
              vendedorNombrePorCodigo={nombrePorCodigoTodos}
              nombrePorCodigoTodos={nombrePorCodigoTodos}
              onExcluir={excluirClientes}
              onAsignar={asignarClientesAMano}
              vendedorOpciones={vendedorOpcionesTodos}
            />
          </div>
        </div>
      )}
      {mostrarExport && (
        <ExportSimulacionModal onCancelar={() => setMostrarExport(false)} onConfirmar={handleExportar} exportando={exportando} />
      )}
      {mostrarPropuestas && (
        <PropuestasGuardadasModal
          propuestas={propuestasGuardadas}
          puedeGuardar={!!zonas}
          onGuardar={handleGuardarPropuesta}
          onCargar={handleCargarPropuesta}
          onEliminar={handleEliminarPropuesta}
          onImportado={() => setPropuestasGuardadas(listarPropuestas())}
          onCerrar={() => setMostrarPropuestas(false)}
        />
      )}
    </div>
  );
}
