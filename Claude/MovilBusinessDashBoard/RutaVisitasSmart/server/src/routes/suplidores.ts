import { Router } from "express";
import { getPool } from "../db.js";

export const suplidoresRouter = Router();

interface SuplidorRow {
  id: number;
  nombre: string;
}

/** Catálogo de suplidores (Categoria3) — mismo patrón ya usado y probado en el
 * dashboard .NET hermano (MovilbusinessDashBoardSmart), misma BD. Se usa para
 * el diálogo de export de "Simular Nuevas Rutas" (ventas por suplidor). */
suplidoresRouter.get("/suplidores", async (_req, res) => {
  try {
    const pool = await getPool();
    const result = await pool.request().query<SuplidorRow>(
      `SELECT Cat3ID id, Cat3Descripcion nombre FROM Categoria3 WITH (NOLOCK)
       WHERE Cat3Descripcion IS NOT NULL AND LTRIM(RTRIM(Cat3Descripcion)) <> '' AND Cat3Descripcion <> 'no usar'
       ORDER BY Cat3Descripcion`
    );
    res.json({ data: result.recordset });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error consultando suplidores", detail: String(err) });
  }
});
