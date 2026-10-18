import { Router } from "express";
import { getPool, sql } from "../db.js";

export const frecuenciaCompraRouter = Router();

interface FrecuenciaRow {
  CliID: number;
  Meses: number;
}

interface FrecuenciaCompraBody {
  cliIds: number[];
}

const MESES_VENTANA = 6;

/**
 * Frecuencia de compra real por cliente: en cuántos meses distintos (de los
 * últimos 6) tuvo al menos un pedido. 0 = no compró nada en la ventana, 6 =
 * compró todos los meses. Se usa en "Simular Nuevas Rutas" como 2do criterio
 * (después del tamaño) para decidir Semanal vs. Quincenal.
 */
frecuenciaCompraRouter.post("/frecuencia-compra", async (req, res) => {
  try {
    const body = req.body as FrecuenciaCompraBody;
    const cliIds = (body?.cliIds ?? []).filter((n) => Number.isInteger(n));
    if (cliIds.length === 0) {
      res.json({ data: [] });
      return;
    }

    const pool = await getPool();
    const desde = new Date();
    desde.setMonth(desde.getMonth() - MESES_VENTANA);

    const cliInClause = cliIds.join(",");
    const result = await pool
      .request()
      .input("desde", sql.Date, desde)
      .query<FrecuenciaRow>(
        `SELECT CliID,
                COUNT(DISTINCT CAST(YEAR(PedFecha) AS varchar(4)) + RIGHT('0' + CAST(MONTH(PedFecha) AS varchar(2)), 2)) AS Meses
         FROM VWPedidos WITH (NOLOCK)
         WHERE PedFecha >= @desde AND CliID IN (${cliInClause})
         GROUP BY CliID`
      );

    res.json({ data: result.recordset.map((r) => ({ cliId: r.CliID, mesesConCompra: r.Meses })) });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error consultando frecuencia de compra", detail: String(err) });
  }
});
