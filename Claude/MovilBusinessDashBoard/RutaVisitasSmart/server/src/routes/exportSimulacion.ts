import { Router } from "express";
import ExcelJS from "exceljs";
import { getPool, sql } from "../db.js";
import { DIAS } from "../diaResolver.js";
import { calcularDiff, filaDetalleXlsx, type AsignacionEntrada } from "../rutaDiff.js";

export const exportSimulacionRouter = Router();

interface ClienteZonaBody extends AsignacionEntrada {
  lat: number;
  lng: number;
  /** Estado ANTES de esta simulación (RutaVisitas hoy) — para comparar en el Excel. */
  repCodigoOriginal?: string | null;
  repNombreOriginal?: string | null;
  diaOriginal?: string | null;
  claDescripcion?: string | null;
}

interface ZonaBody {
  dia: (typeof DIAS)[number];
  repCodigo: string;
  repNombre: string;
  division?: string | null;
  clientes: ClienteZonaBody[];
}

interface VendedorResumenRowBody {
  repCodigo: string;
  repNombre: string;
  saliente: boolean;
  antes: number;
  ahora: number;
  porDia: Partial<Record<(typeof DIAS)[number], number>>;
  semanales: number;
  quincenales: number;
}

interface ResumenPropuestaBody {
  totalVendedoresAntes: number;
  totalVendedoresAhora: number;
  totalClientes: number;
  clientesPorSemanaAntes: Record<string, number>;
  clientesPorSemanaAhora: Record<string, number>;
  clientesPorDiaAntes: Record<string, number>;
  clientesPorDiaAhora: Partial<Record<(typeof DIAS)[number], number>>;
  clientesPorFrecuenciaAntes: Record<string, number>;
  clientesSemanalesAhora: number;
  clientesQuincenalesAhora: number;
  clientesPorTamano: Record<string, number>;
  porVendedor: VendedorResumenRowBody[];
}

interface ExportSimulacionBody {
  zonas: ZonaBody[];
  /** RepCodigo de vendedores que salen del equipo — se limpia su RutaVisitas también. */
  salientes: string[];
  desde?: string;
  hasta?: string;
  suplidores?: number[];
  /** Mismos números que se muestran en pantalla (pantalla "Resumen") — se vuelcan tal cual. */
  resumen?: ResumenPropuestaBody;
  /** CliIDs que participaron de la simulación (territorio + excluidos + asignados).
   * Si viene, el script solo "quita" de la ruta de un vendedor a los clientes de
   * esta lista que ya no le quedaron — no toca el resto de su ruta (días/semanas
   * filtrados fuera de la simulación, o un representante de afuera al que solo se
   * le asignaron algunos clientes). Si no viene, comportamiento de siempre. */
  universoCliIds?: number[];
}

interface VentaSuplidorRow {
  CliID: number;
  Suplidor: string;
  Unidades: number;
  Monto: number;
}

interface FacturaRow {
  CliID: number;
  Facturas: number;
}

const redondear = (n: number): number => Math.round(n * 100) / 100;

/** Cuenta los meses CALENDARIO distintos que toca el rango [desde, hastaInclusive]
 * (ej. 2026-07-19 a 2026-08-19 → 2: julio y agosto) — es el divisor de
 * "Promedio Mensual" en el reporte de ventas. */
function contarMesesSeleccionados(desde: Date, hastaInclusive: Date): number {
  const meses = new Set<string>();
  const cursor = new Date(desde.getFullYear(), desde.getMonth(), 1);
  const fin = new Date(hastaInclusive.getFullYear(), hastaInclusive.getMonth(), 1);
  while (cursor <= fin) {
    meses.add(`${cursor.getFullYear()}-${cursor.getMonth()}`);
    cursor.setMonth(cursor.getMonth() + 1);
  }
  return Math.max(1, meses.size);
}

/**
 * Nunca escribe contra RutaVisitas: relee el estado actual de CADA vendedor
 * involucrado (los que quedan + los salientes), arma el diff de cada uno, y
 * devuelve un .xlsx con Detalle (+ Lat/Lng/División), Ventas por Suplidor
 * (rango de fechas + suplidores elegidos) y el Script SQL consolidado.
 */
exportSimulacionRouter.post("/export-simulacion", async (req, res) => {
  try {
    const body = req.body as ExportSimulacionBody;
    if (!body?.zonas || !Array.isArray(body.zonas)) {
      res.status(400).json({ error: "Body inválido: se esperaba { zonas, salientes, desde, hasta, suplidores }" });
      return;
    }

    const pool = await getPool();

    // Agrupar zonas -> por vendedor (que queda) -> por día -> AsignacionEntrada[]
    // (varias zonas pueden compartir día+vendedor si se partió en quincenas; se
    // concatenan en el orden recibido, la posición sigue de corrido).
    const diasPorRep = new Map<string, Partial<Record<(typeof DIAS)[number], AsignacionEntrada[]>>>();
    const nombrePorRep = new Map<string, string>();
    const divisionPorRep = new Map<string, string>();
    const clienteInfo = new Map<number, { cliCodigo: string | null; cliNombre: string | null; claDescripcion: string | null }>();
    const cliIdsTerritorio = new Set<number>();
    const origenPorCliId = new Map<number, { repCodigo: string | null; repNombre: string | null; dia: string | null }>();
    // Estado "ahora" (la propuesta) de cada cliente — para agrupar el reporte
    // de ventas por día/vendedor/semana/tamaño más abajo.
    const zonaPorCliId = new Map<
      number,
      { dia: string; repCodigo: string; repNombre: string; semanasActivas: number[]; claDescripcion: string | null }
    >();

    for (const z of body.zonas) {
      nombrePorRep.set(z.repCodigo, z.repNombre);
      if (z.division) divisionPorRep.set(z.repCodigo, z.division);
      if (!diasPorRep.has(z.repCodigo)) diasPorRep.set(z.repCodigo, {});
      const dias = diasPorRep.get(z.repCodigo)!;
      const previos = dias[z.dia] ?? [];
      dias[z.dia] = [...previos, ...z.clientes];
      for (const c of z.clientes) {
        clienteInfo.set(c.cliId, { cliCodigo: c.cliCodigo ?? null, cliNombre: c.cliNombre ?? null, claDescripcion: c.claDescripcion ?? null });
        cliIdsTerritorio.add(c.cliId);
        origenPorCliId.set(c.cliId, {
          repCodigo: c.repCodigoOriginal ?? null,
          repNombre: c.repNombreOriginal ?? null,
          dia: c.diaOriginal ?? null,
        });
        zonaPorCliId.set(c.cliId, {
          dia: z.dia,
          repCodigo: z.repCodigo,
          repNombre: z.repNombre,
          semanasActivas: c.semanasActivas,
          claDescripcion: c.claDescripcion ?? null,
        });
      }
    }
    // Los salientes también se recalculan (contra su estado actual real) — su
    // "deseado" es vacío en todos los días, así el diff genera el "quita".
    for (const repCodigo of body.salientes ?? []) {
      if (!diasPorRep.has(repCodigo)) diasPorRep.set(repCodigo, {});
    }

    const workbook = new ExcelJS.Workbook();

    // ── Hoja Resumen (mismos números que la pantalla "Resumen" del simulador,
    // comparando antes/RutaVisitas hoy vs. ahora/la propuesta) ──────────────
    if (body.resumen) {
      const r = body.resumen;
      const resumenSheet = workbook.addWorksheet("Resumen");
      resumenSheet.columns = [
        { header: "Indicador", key: "indicador", width: 32 },
        { header: "Antes", key: "antes", width: 14 },
        { header: "Ahora", key: "ahora", width: 14 },
      ];
      resumenSheet.getRow(1).font = { bold: true };

      const semanalAntes = r.clientesPorFrecuenciaAntes["Semanal"] ?? 0;
      const quincenalAntes = r.clientesPorFrecuenciaAntes["Quincenal"] ?? 0;
      const personalizadaAntes = r.clientesPorFrecuenciaAntes["Personalizada"] ?? 0;
      const sinAsignarAntes = r.clientesPorFrecuenciaAntes["Sin Asignar"] ?? 0;

      type FilaResumen = [string, string | number, string | number];
      const encabezados = new Set<string>();
      const filas: FilaResumen[] = [
        ["Vendedores", r.totalVendedoresAntes, r.totalVendedoresAhora],
        ["Total clientes", r.totalClientes, r.totalClientes],
        ["Clientes semanales", semanalAntes, r.clientesSemanalesAhora],
        ["Clientes quincenales", quincenalAntes, r.clientesQuincenalesAhora],
      ];
      if (personalizadaAntes > 0) filas.push(["Frecuencia personalizada (solo antes)", personalizadaAntes, 0]);
      if (sinAsignarAntes > 0) filas.push(["Sin ruta previa (solo antes)", sinAsignarAntes, 0]);

      filas.push(["", "", ""], ["Clientes por día", "", ""]);
      encabezados.add("Clientes por día");
      for (const d of DIAS) {
        const antes = r.clientesPorDiaAntes[d] ?? 0;
        const ahora = r.clientesPorDiaAhora[d] ?? 0;
        if (antes > 0 || ahora > 0) filas.push([d, antes, ahora]);
      }
      const sinDiaPrevio = r.clientesPorDiaAntes["Sin día previo"] ?? 0;
      if (sinDiaPrevio > 0) filas.push(["Sin día previo", sinDiaPrevio, 0]);

      filas.push(["", "", ""], ["Clientes por semana", "", ""]);
      encabezados.add("Clientes por semana");
      for (const s of [1, 2, 3, 4]) filas.push([`Semana ${s}`, r.clientesPorSemanaAntes[s] ?? 0, r.clientesPorSemanaAhora[s] ?? 0]);

      filas.push(["", "", ""], ["Clientes por tamaño (no cambia con la simulación)", "", ""]);
      encabezados.add("Clientes por tamaño (no cambia con la simulación)");
      for (const [tam, n] of Object.entries(r.clientesPorTamano).sort((a, b) => b[1] - a[1])) filas.push([tam, n, n]);

      filas.forEach(([indicador, antes, ahora], i) => {
        resumenSheet.addRow({ indicador, antes, ahora });
        if (encabezados.has(indicador)) resumenSheet.getRow(i + 2).font = { bold: true };
      });

      // ── Hoja "Por Vendedor" (antes/ahora + desglose por día y frecuencia,
      // todo referido a la propuesta "ahora") ─────────────────────────────
      const porVendedorSheet = workbook.addWorksheet("Por Vendedor");
      porVendedorSheet.columns = [
        { header: "Vendedor", key: "vendedor", width: 30 },
        { header: "Saliente", key: "saliente", width: 10 },
        { header: "Antes", key: "antes", width: 10 },
        { header: "Ahora", key: "ahora", width: 10 },
        ...DIAS.map((d) => ({ header: d, key: d, width: 11 })),
        { header: "Semanales", key: "semanales", width: 12 },
        { header: "Quincenales", key: "quincenales", width: 12 },
      ];
      porVendedorSheet.getRow(1).font = { bold: true };
      for (const v of r.porVendedor) {
        const etiqueta = v.repCodigo.startsWith("__") ? v.repNombre : `${v.repCodigo} - ${v.repNombre}`;
        const fila: Record<string, string | number> = {
          vendedor: etiqueta,
          saliente: v.saliente ? "Sí" : "",
          antes: v.antes,
          ahora: v.ahora,
          semanales: v.semanales,
          quincenales: v.quincenales,
        };
        for (const d of DIAS) fila[d] = v.porDia[d] ?? 0;
        porVendedorSheet.addRow(fila);
      }
    }

    // ── Hoja Detalle (con Lat/Lng/División + Vendedor/Día anterior vs. actual) ──
    const detalle = workbook.addWorksheet("Detalle");
    detalle.columns = [
      { header: "Vendedor Anterior", key: "vendedorAnterior", width: 24 },
      { header: "Vendedor Actual", key: "vendedor", width: 24 },
      { header: "División", key: "division", width: 12 },
      { header: "CliID", key: "cliId", width: 10 },
      { header: "Código", key: "cliCodigo", width: 14 },
      { header: "Nombre", key: "cliNombre", width: 32 },
      { header: "Día Propuesto", key: "dia", width: 14 },
      { header: "Día Anterior", key: "diaAnterior", width: 14 },
      { header: "Posición", key: "posicion", width: 10 },
      { header: "Frecuencia", key: "frecuencia", width: 14 },
      { header: "Semanas activas", key: "semanas", width: 16 },
      { header: "Acción", key: "accion", width: 16 },
      { header: "Lat", key: "lat", width: 12 },
      { header: "Lng", key: "lng", width: 12 },
      { header: "Tamaño", key: "tamano", width: 16 },
    ];

    const latLngByCliId = new Map<number, { lat: number; lng: number }>();
    for (const z of body.zonas) for (const c of z.clientes) latLngByCliId.set(c.cliId, { lat: c.lat, lng: c.lng });

    const etiquetaOrigen = (cliId: number): string => {
      const o = origenPorCliId.get(cliId);
      if (!o || !o.repCodigo) return "Sin ruta previa";
      return `${o.repCodigo} - ${o.repNombre ?? ""}`;
    };
    const diaOrigen = (cliId: number): string => origenPorCliId.get(cliId)?.dia ?? "Sin día previo";

    const universo = Array.isArray(body.universoCliIds)
      ? new Set(body.universoCliIds.filter((n) => Number.isInteger(n)))
      : null;
    const opcionesDiff = universo ? { limpiarSoloDentroDe: universo } : undefined;

    const allStatements: string[] = [];
    for (const [repCodigo, dias] of diasPorRep) {
      const repNombre = nombrePorRep.get(repCodigo) ?? repCodigo;
      const division = divisionPorRep.get(repCodigo) ?? "";
      const repLabel = `${repCodigo} - ${repNombre}`;
      const { inserts, updates, cleared, sinCambios, statements } = await calcularDiff(pool, repCodigo, dias, opcionesDiff);

      const conExtras = (fila: ReturnType<typeof filaDetalleXlsx>, cliId: number) => ({
        ...fila,
        division,
        vendedorAnterior: etiquetaOrigen(cliId),
        diaAnterior: diaOrigen(cliId),
        lat: latLngByCliId.get(cliId)?.lat ?? "",
        lng: latLngByCliId.get(cliId)?.lng ?? "",
        tamano: clienteInfo.get(cliId)?.claDescripcion ?? "",
      });
      for (const d of inserts) detalle.addRow(conExtras(filaDetalleXlsx(repLabel, d, "Nuevo"), d.cliId));
      for (const d of updates) detalle.addRow(conExtras(filaDetalleXlsx(repLabel, d, "Actualizado"), d.cliId));
      for (const d of sinCambios) detalle.addRow(conExtras(filaDetalleXlsx(repLabel, d, "Sin cambios"), d.cliId));
      for (const cliId of cleared) {
        const info = clienteInfo.get(cliId);
        detalle.addRow({
          vendedorAnterior: etiquetaOrigen(cliId),
          vendedor: repLabel,
          division,
          cliId,
          cliCodigo: info?.cliCodigo ?? "",
          cliNombre: info?.cliNombre ?? "",
          dia: "",
          diaAnterior: diaOrigen(cliId),
          posicion: "",
          frecuencia: "",
          semanas: "",
          accion: "Quitado (saliente o reasignado)",
          lat: latLngByCliId.get(cliId)?.lat ?? "",
          lng: latLngByCliId.get(cliId)?.lng ?? "",
          tamano: info?.claDescripcion ?? "",
        });
      }

      if (statements.length > 0) {
        allStatements.push(`-- ══════ RepCodigo=${repCodigo} (${repNombre}) ══════`, ...statements);
      }
    }
    detalle.getRow(1).font = { bold: true };

    // ── Hoja Ventas por Suplidor (rango de fechas + suplidores elegidos) ────
    const ventasSheet = workbook.addWorksheet("Ventas por Suplidor");
    ventasSheet.columns = [
      { header: "CliID", key: "cliId", width: 10 },
      { header: "Código", key: "cliCodigo", width: 14 },
      { header: "Nombre", key: "cliNombre", width: 32 },
      { header: "Suplidor", key: "suplidor", width: 26 },
      { header: "Unidades", key: "unidades", width: 12 },
      { header: "Monto", key: "monto", width: 14 },
      { header: "Promedio Mensual", key: "promedioMensual", width: 16 },
    ];
    ventasSheet.getRow(1).font = { bold: true };
    // Total de Monto por cliente (a través de todos sus suplidores en el
    // rango elegido) — insumo de la hoja "Resumen Ventas" más abajo.
    const montoPorCliId = new Map<number, number>();

    const cliIds = [...cliIdsTerritorio].filter((n) => Number.isInteger(n));
    const suplidorIds = (body.suplidores ?? []).filter((n) => Number.isInteger(n));
    if (body.desde && body.hasta && suplidorIds.length > 0 && cliIds.length > 0) {
      const hastaExclusive = new Date(body.hasta);
      hastaExclusive.setDate(hastaExclusive.getDate() + 1);
      const cliInClause = cliIds.join(",");
      const supInClause = suplidorIds.join(",");
      const ventasResult = await pool
        .request()
        .input("desde", sql.Date, new Date(body.desde))
        .input("hasta", sql.Date, hastaExclusive)
        .query<VentaSuplidorRow>(
          `SELECT d.CliID, c3.Cat3Descripcion Suplidor,
                  SUM(ISNULL(d.PedCantidad,0)) Unidades,
                  SUM(ISNULL(d.PedCantidad,0) * ISNULL(d.PedPrecio,0)) Monto
           FROM VWPedidosDetalle d WITH (NOLOCK)
           JOIN VWPedidos p WITH (NOLOCK) ON p.PedSecuencia = d.PedSecuencia AND p.RepCodigo = d.RepCodigo
           JOIN Productos pr WITH (NOLOCK) ON pr.ProID = d.ProID
           JOIN Categoria3 c3 WITH (NOLOCK) ON c3.Cat3ID = pr.Cat3ID
           WHERE p.PedFecha >= @desde AND p.PedFecha < @hasta
             AND d.CliID IN (${cliInClause})
             AND pr.Cat3ID IN (${supInClause})
           GROUP BY d.CliID, c3.Cat3Descripcion
           ORDER BY d.CliID, c3.Cat3Descripcion`
        );
      // Facturas = ventas realmente facturadas (VWVentas/VWVentasDetalle, NO
      // VWPedidos que son solo pedidos tomados) — VenEstatus=81 identifica
      // devoluciones (VenSecuencia/VenCantidad negativos) y se excluyen a
      // propósito, igual que VenEstatus=0 (anuladas). Es un conteo POR
      // CLIENTE (una factura puede incluir varios suplidores a la vez, así que
      // no tiene sentido partirla por suplidor).
      const facturasResult = await pool
        .request()
        .input("desde", sql.Date, new Date(body.desde))
        .input("hasta", sql.Date, hastaExclusive)
        .query<FacturaRow>(
          `SELECT v.CliID, COUNT(DISTINCT v.VenSecuencia) AS Facturas
           FROM VWVentas v WITH (NOLOCK)
           JOIN VWVentasDetalle d WITH (NOLOCK) ON d.VenSecuencia = v.VenSecuencia AND d.RepCodigo = v.RepCodigo
           JOIN Productos pr WITH (NOLOCK) ON pr.ProID = d.ProID
           WHERE v.VenFecha >= @desde AND v.VenFecha < @hasta
             AND v.VenEstatus <> 0 AND v.VenEstatus <> 81
             AND v.CliID IN (${cliInClause})
             AND pr.Cat3ID IN (${supInClause})
           GROUP BY v.CliID`
        );
      const facturasPorCliId = new Map<number, number>(facturasResult.recordset.map((r) => [r.CliID, r.Facturas]));

      const mesesSeleccionados = contarMesesSeleccionados(new Date(body.desde), new Date(body.hasta));
      for (const r of ventasResult.recordset) {
        const info = clienteInfo.get(r.CliID);
        ventasSheet.addRow({
          cliId: r.CliID,
          cliCodigo: info?.cliCodigo ?? "",
          cliNombre: info?.cliNombre ?? "",
          suplidor: r.Suplidor,
          unidades: r.Unidades,
          monto: r.Monto,
          promedioMensual: redondear(r.Monto / mesesSeleccionados),
        });
        montoPorCliId.set(r.CliID, (montoPorCliId.get(r.CliID) ?? 0) + r.Monto);
      }
      if (ventasResult.recordset.length === 0) {
        ventasSheet.addRow({
          cliId: "",
          cliCodigo: "",
          cliNombre: "",
          suplidor: "Sin ventas en el rango/suplidores elegidos.",
          unidades: "",
          monto: "",
          promedioMensual: "",
        });
      } else {
        // ── Hoja "Resumen Ventas" (Total Monto + Total Facturas, cada uno con
        // su Promedio Mensual, agrupado por día/semana/tamaño/vendedor —
        // mismas dimensiones que la hoja "Resumen") ─────────────────────────
        const resumenVentasSheet = workbook.addWorksheet("Resumen Ventas");
        resumenVentasSheet.columns = [
          { header: "Indicador", key: "indicador", width: 32 },
          { header: "Total Monto", key: "totalMonto", width: 16 },
          { header: "Monto Prom. Mensual", key: "montoPromedioMensual", width: 18 },
          { header: "Total Facturas", key: "totalFacturas", width: 14 },
          { header: "Facturas Prom. Mensual", key: "facturasPromedioMensual", width: 20 },
        ];
        resumenVentasSheet.getRow(1).font = { bold: true };

        const totalGeneralMonto = [...montoPorCliId.values()].reduce((s, m) => s + m, 0);
        const totalGeneralFacturas = [...facturasPorCliId.values()].reduce((s, f) => s + f, 0);
        const porDiaMonto = new Map<string, number>();
        const porSemanaMonto = new Map<number, number>();
        const porTamanoMonto = new Map<string, number>();
        const porVendedorMonto = new Map<string, number>();
        const porDiaFacturas = new Map<string, number>();
        const porSemanaFacturas = new Map<number, number>();
        const porTamanoFacturas = new Map<string, number>();
        const porVendedorFacturas = new Map<string, number>();
        // El universo de clientes es la unión de los que tienen Monto y/o
        // Facturas (normalmente el mismo conjunto, pero no hay garantía).
        const cliIdsConDatos = new Set([...montoPorCliId.keys(), ...facturasPorCliId.keys()]);
        for (const cliId of cliIdsConDatos) {
          const z = zonaPorCliId.get(cliId);
          if (!z) continue;
          const monto = montoPorCliId.get(cliId) ?? 0;
          const facturas = facturasPorCliId.get(cliId) ?? 0;
          const tam = z.claDescripcion || "Sin clasificar";
          const vendKey = `${z.repCodigo} - ${z.repNombre}`;
          porDiaMonto.set(z.dia, (porDiaMonto.get(z.dia) ?? 0) + monto);
          porTamanoMonto.set(tam, (porTamanoMonto.get(tam) ?? 0) + monto);
          porVendedorMonto.set(vendKey, (porVendedorMonto.get(vendKey) ?? 0) + monto);
          porDiaFacturas.set(z.dia, (porDiaFacturas.get(z.dia) ?? 0) + facturas);
          porTamanoFacturas.set(tam, (porTamanoFacturas.get(tam) ?? 0) + facturas);
          porVendedorFacturas.set(vendKey, (porVendedorFacturas.get(vendKey) ?? 0) + facturas);
          for (const s of z.semanasActivas) {
            porSemanaMonto.set(s, (porSemanaMonto.get(s) ?? 0) + monto);
            porSemanaFacturas.set(s, (porSemanaFacturas.get(s) ?? 0) + facturas);
          }
        }

        type FilaResumenVentas = [string, string | number, string | number, string | number, string | number];
        const encabezadosVentas = new Set<string>();
        const fila = (
          indicador: string,
          monto: number,
          facturas: number
        ): FilaResumenVentas => [
          indicador,
          redondear(monto),
          redondear(monto / mesesSeleccionados),
          facturas,
          redondear(facturas / mesesSeleccionados),
        ];
        const filasVentas: FilaResumenVentas[] = [fila("Total general", totalGeneralMonto, totalGeneralFacturas)];

        filasVentas.push(["", "", "", "", ""], ["Ventas por día", "", "", "", ""]);
        encabezadosVentas.add("Ventas por día");
        for (const d of DIAS) {
          const m = porDiaMonto.get(d) ?? 0;
          const f = porDiaFacturas.get(d) ?? 0;
          if (m > 0 || f > 0) filasVentas.push(fila(d, m, f));
        }

        filasVentas.push(["", "", "", "", ""], ["Ventas por semana", "", "", "", ""]);
        encabezadosVentas.add("Ventas por semana");
        for (const s of [1, 2, 3, 4]) filasVentas.push(fila(`Semana ${s}`, porSemanaMonto.get(s) ?? 0, porSemanaFacturas.get(s) ?? 0));

        filasVentas.push(["", "", "", "", ""], ["Ventas por tamaño", "", "", "", ""]);
        encabezadosVentas.add("Ventas por tamaño");
        for (const [tam, m] of [...porTamanoMonto.entries()].sort((a, b) => b[1] - a[1])) {
          filasVentas.push(fila(tam, m, porTamanoFacturas.get(tam) ?? 0));
        }

        filasVentas.push(["", "", "", "", ""], ["Ventas por vendedor", "", "", "", ""]);
        encabezadosVentas.add("Ventas por vendedor");
        for (const [vend, m] of [...porVendedorMonto.entries()].sort((a, b) => b[1] - a[1])) {
          filasVentas.push(fila(vend, m, porVendedorFacturas.get(vend) ?? 0));
        }

        filasVentas.forEach(([indicador, totalMonto, montoPromedioMensual, totalFacturas, facturasPromedioMensual], i) => {
          resumenVentasSheet.addRow({ indicador, totalMonto, montoPromedioMensual, totalFacturas, facturasPromedioMensual });
          if (encabezadosVentas.has(indicador)) resumenVentasSheet.getRow(i + 2).font = { bold: true };
        });

        // ── Hoja "Facturas por Cliente" — cuántas facturas (sin devoluciones)
        // hace cada cliente en el rango, y su promedio mensual. ─────────────
        if (facturasPorCliId.size > 0) {
          const facturasClienteSheet = workbook.addWorksheet("Facturas por Cliente");
          facturasClienteSheet.columns = [
            { header: "CliID", key: "cliId", width: 10 },
            { header: "Código", key: "cliCodigo", width: 14 },
            { header: "Nombre", key: "cliNombre", width: 32 },
            { header: "Facturas", key: "facturas", width: 12 },
            { header: "Facturas Prom. Mensual", key: "facturasPromedioMensual", width: 20 },
          ];
          facturasClienteSheet.getRow(1).font = { bold: true };
          for (const [cliId, facturas] of [...facturasPorCliId.entries()].sort((a, b) => a[0] - b[0])) {
            const info = clienteInfo.get(cliId);
            facturasClienteSheet.addRow({
              cliId,
              cliCodigo: info?.cliCodigo ?? "",
              cliNombre: info?.cliNombre ?? "",
              facturas,
              facturasPromedioMensual: redondear(facturas / mesesSeleccionados),
            });
          }
        }
      }
    } else {
      ventasSheet.addRow({
        cliId: "",
        cliCodigo: "",
        cliNombre: "",
        suplidor: "No se eligió rango de fechas y/o suplidores al exportar.",
        unidades: "",
        monto: "",
        promedioMensual: "",
      });
    }

    // ── Hoja Script SQL (consolidado, con encabezado por RepCodigo) ─────────
    const scriptSheet = workbook.addWorksheet("Script SQL");
    scriptSheet.columns = [{ header: "Script consolidado — Simular Nuevas Rutas", key: "sql", width: 140 }];
    scriptSheet.getRow(1).font = { bold: true };
    if (allStatements.length === 0) {
      scriptSheet.addRow({ sql: "-- Sin cambios: nada para aplicar." });
    } else {
      for (const s of allStatements) {
        for (const line of s.split("\n")) scriptSheet.addRow({ sql: line });
        scriptSheet.addRow({ sql: "" });
      }
    }

    const buffer = await workbook.xlsx.writeBuffer();
    const filename = `Simulacion_Rutas_${new Date().toISOString().slice(0, 10)}.xlsx`;
    res.setHeader("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    res.setHeader("Content-Disposition", `attachment; filename="${filename}"`);
    res.send(Buffer.from(buffer));
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error generando el export de la simulación", detail: String(err) });
  }
});
