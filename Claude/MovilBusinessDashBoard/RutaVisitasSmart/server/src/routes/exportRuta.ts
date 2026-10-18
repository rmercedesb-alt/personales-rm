import { Router } from "express";
import ExcelJS from "exceljs";
import { getPool } from "../db.js";
import { DIAS } from "../diaResolver.js";
import { calcularDiff, filaDetalleXlsx, type AsignacionEntrada } from "../rutaDiff.js";

export const exportRutaRouter = Router();

interface ExportBody {
  repCodigo: string;
  repNombre: string;
  dias: Partial<Record<(typeof DIAS)[number], AsignacionEntrada[]>>;
}

/**
 * Nunca escribe contra RutaVisitas: relee el estado actual, arma el diff y
 * devuelve un .xlsx con el detalle y el script SQL sugerido para que TI lo
 * aplique manualmente.
 */
exportRutaRouter.post("/export-ruta", async (req, res) => {
  try {
    const body = req.body as ExportBody;
    if (!body?.repCodigo || !body?.dias) {
      res.status(400).json({ error: "Body inválido: se esperaba { repCodigo, repNombre, dias }" });
      return;
    }

    const pool = await getPool();
    const { inserts, updates, cleared, sinCambios, statements } = await calcularDiff(pool, body.repCodigo, body.dias);
    const repLabel = `${body.repCodigo} - ${body.repNombre}`;

    const workbook = new ExcelJS.Workbook();

    const detalle = workbook.addWorksheet("Detalle");
    detalle.columns = [
      { header: "Vendedor", key: "vendedor", width: 24 },
      { header: "CliID", key: "cliId", width: 10 },
      { header: "Código", key: "cliCodigo", width: 14 },
      { header: "Nombre", key: "cliNombre", width: 32 },
      { header: "Día", key: "dia", width: 12 },
      { header: "Posición", key: "posicion", width: 10 },
      { header: "Frecuencia", key: "frecuencia", width: 14 },
      { header: "Semanas activas", key: "semanas", width: 16 },
      { header: "Acción", key: "accion", width: 16 },
    ];
    for (const d of inserts) detalle.addRow(filaDetalleXlsx(repLabel, d, "Nuevo"));
    for (const d of updates) detalle.addRow(filaDetalleXlsx(repLabel, d, "Actualizado"));
    for (const d of sinCambios) detalle.addRow(filaDetalleXlsx(repLabel, d, "Sin cambios"));
    for (const cliId of cleared) {
      detalle.addRow({
        vendedor: repLabel,
        cliId,
        cliCodigo: "",
        cliNombre: "",
        dia: "",
        posicion: "",
        frecuencia: "",
        semanas: "",
        accion: "Quitado del día",
      });
    }
    detalle.getRow(1).font = { bold: true };

    const scriptSheet = workbook.addWorksheet("Script SQL");
    scriptSheet.columns = [{ header: `Script para RepCodigo=${body.repCodigo} (${body.repNombre})`, key: "sql", width: 140 }];
    scriptSheet.getRow(1).font = { bold: true };
    if (statements.length === 0) {
      scriptSheet.addRow({ sql: "-- Sin cambios: nada para aplicar." });
    } else {
      for (const s of statements) {
        for (const line of s.split("\n")) scriptSheet.addRow({ sql: line });
        scriptSheet.addRow({ sql: "" });
      }
    }

    const buffer = await workbook.xlsx.writeBuffer();
    const filename = `Ruta_${body.repCodigo}_${new Date().toISOString().slice(0, 10)}.xlsx`;
    res.setHeader("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    res.setHeader("Content-Disposition", `attachment; filename="${filename}"`);
    res.send(Buffer.from(buffer));
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error generando el export de ruta", detail: String(err) });
  }
});
