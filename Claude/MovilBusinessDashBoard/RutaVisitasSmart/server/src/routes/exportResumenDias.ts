import { Router } from "express";
import ExcelJS from "exceljs";

export const exportResumenDiasRouter = Router();

interface ClienteRutaBody {
  repCodigo: string;
  repNombre: string;
  supCodigo: string | null;
  supNombre: string;
  cliId: number;
  cliCodigo: string | null;
  cliNombre: string | null;
  dia: string;
  semanasActivas: number[];
  frecuencia: string;
  rutPosicion: number | null;
  claDescripcion: string;
  tinDescripcion: string;
}

const DIAS = ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"];
const SEMANAS = [1, 2, 3, 4];

/**
 * Excel del resumen de "Por Día / Supervisor" — a diferencia de los exports
 * de la ruta, esto no relee nada de la base: el cliente ya tiene los datos
 * filtrados en pantalla (vienen de `/api/rutas`, cacheados) y solo hace falta
 * formatearlos. Hoja "Resumen" (conteos + matriz Semana×Día) + hoja "Detalle"
 * (todas las filas filtradas, sin el tope de 500 que tiene la tabla en pantalla).
 */
exportResumenDiasRouter.post("/export-resumen-dias", async (req, res) => {
  try {
    const data = req.body?.data as ClienteRutaBody[] | undefined;
    if (!Array.isArray(data)) {
      res.status(400).json({ error: "Body inválido: se esperaba { data: ClienteRuta[] }" });
      return;
    }

    const supervisores = new Set<string>();
    const vendedores = new Set<string>();
    const clientes = new Set<number>();
    const porSemana = new Map<number, Set<number>>();
    const porSemanaDia = new Map<string, Set<number>>();
    const semanalesPorDia = new Map<string, Set<number>>();
    const quincenalesPorDia = new Map<string, Set<number>>();
    const totalPorDia = new Map<string, Set<number>>();

    // Totales DISTINTOS por fila (columna "Total" después de Domingo) — mismo
    // universo que las columnas de día (excluye "Sin Asignar"), contado por
    // Set para no duplicar a un cliente que aparece en varios días/semanas.
    const porSemanaTotal = new Map<number, Set<number>>();
    const semanalesTotal = new Set<number>();
    const quincenalesTotal = new Set<number>();
    const granTotal = new Set<number>();

    function add(map: Map<string, Set<number>>, key: string, cliId: number) {
      if (!map.has(key)) map.set(key, new Set());
      map.get(key)!.add(cliId);
    }
    function addSemana(map: Map<number, Set<number>>, key: number, cliId: number) {
      if (!map.has(key)) map.set(key, new Set());
      map.get(key)!.add(cliId);
    }

    for (const r of data) {
      clientes.add(r.cliId);
      vendedores.add(r.repCodigo);
      if (r.supCodigo) supervisores.add(r.supCodigo);
      for (const s of r.semanasActivas) {
        if (!porSemana.has(s)) porSemana.set(s, new Set());
        porSemana.get(s)!.add(r.cliId);
      }
      if (!DIAS.includes(r.dia)) continue;
      for (const s of r.semanasActivas) {
        add(porSemanaDia, `${s}|${r.dia}`, r.cliId);
        addSemana(porSemanaTotal, s, r.cliId);
      }
      add(totalPorDia, r.dia, r.cliId);
      granTotal.add(r.cliId);
      if (r.frecuencia === "Semanal") {
        add(semanalesPorDia, r.dia, r.cliId);
        semanalesTotal.add(r.cliId);
      } else if (r.frecuencia === "Quincenal") {
        add(quincenalesPorDia, r.dia, r.cliId);
        quincenalesTotal.add(r.cliId);
      }
    }

    const workbook = new ExcelJS.Workbook();

    const resumen = workbook.addWorksheet("Resumen");
    resumen.columns = [
      { header: "Indicador", key: "indicador", width: 24 },
      ...DIAS.map((d) => ({ header: d, key: d, width: 11 })),
      { header: "Total", key: "Total", width: 11 },
    ];
    resumen.getRow(1).font = { bold: true };
    resumen.addRow({ indicador: "Supervisores", [DIAS[0]]: supervisores.size });
    resumen.addRow({ indicador: "Vendedores", [DIAS[0]]: vendedores.size });
    resumen.addRow({ indicador: "Clientes", [DIAS[0]]: clientes.size });
    resumen.addRow({});
    for (const s of SEMANAS) {
      const fila: Record<string, string | number> = { indicador: `Semana ${s}`, Total: porSemanaTotal.get(s)?.size ?? 0 };
      for (const d of DIAS) fila[d] = porSemanaDia.get(`${s}|${d}`)?.size ?? 0;
      resumen.addRow(fila);
    }
    const filaDe = (label: string, map: Map<string, Set<number>>, totalSet: Set<number>) => {
      const fila: Record<string, string | number> = { indicador: label, Total: totalSet.size };
      for (const d of DIAS) fila[d] = map.get(d)?.size ?? 0;
      return fila;
    };
    resumen.addRow(filaDe("Semanales", semanalesPorDia, semanalesTotal));
    resumen.addRow(filaDe("Quincenales", quincenalesPorDia, quincenalesTotal));
    const filaTotal = resumen.addRow(filaDe("Total", totalPorDia, granTotal));
    filaTotal.font = { bold: true };

    const detalle = workbook.addWorksheet("Detalle");
    detalle.columns = [
      { header: "Código", key: "cliCodigo", width: 14 },
      { header: "Cliente", key: "cliNombre", width: 32 },
      { header: "Vendedor", key: "repNombre", width: 24 },
      { header: "Supervisor", key: "supNombre", width: 24 },
      { header: "Día", key: "dia", width: 12 },
      { header: "Frecuencia", key: "frecuencia", width: 14 },
      { header: "RutPosición", key: "rutPosicion", width: 12 },
      { header: "Tamaño", key: "claDescripcion", width: 16 },
      { header: "Tipo", key: "tinDescripcion", width: 18 },
    ];
    detalle.getRow(1).font = { bold: true };
    for (const r of data) {
      detalle.addRow({
        cliCodigo: r.cliCodigo,
        cliNombre: r.cliNombre,
        repNombre: r.repNombre,
        supNombre: r.supNombre,
        dia: r.dia,
        frecuencia: r.frecuencia,
        rutPosicion: r.rutPosicion ?? "",
        claDescripcion: r.claDescripcion,
        tinDescripcion: r.tinDescripcion,
      });
    }

    const buffer = await workbook.xlsx.writeBuffer();
    res.setHeader("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    res.setHeader("Content-Disposition", `attachment; filename="Resumen_PorDia.xlsx"`);
    res.send(Buffer.from(buffer));
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error generando el Excel de resumen", detail: String(err) });
  }
});
