import { Router } from "express";
import { getPool } from "../db.js";

export const vendedoresRouter = Router();

interface VendedorRow {
  RepCodigo: string;
  RepNombre: string;
  RepCargo: string | null;
  SupCodigo: string | null;
  SupNombre: string | null;
  CedCodigo: string | null;
  CedDescripcion: string | null;
  Division: string | null;
}

// Todos los vendedores activos, independiente de si ya tienen filas en
// RutaVisitas (a diferencia de /api/rutas, que hace INNER JOIN RutaVisitas
// y por eso no sirve para elegir un vendedor sin ruta todavía).
vendedoresRouter.get("/vendedores", async (_req, res) => {
  try {
    const pool = await getPool();
    const result = await pool.request().query<VendedorRow>(`
      SELECT
        rep.RepCodigo, rep.RepNombre, rep.RepCargo,
        rep.RepSupervisor AS SupCodigo, sup.RepNombre AS SupNombre,
        rep.CedCodigo, cd.CedDescripcion,
        ra.RepTipe AS Division
      FROM Representantes rep WITH (NOLOCK)
      LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo = rep.RepSupervisor
      LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo = rep.CedCodigo
      LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = rep.RepCodigo
      WHERE rep.RepEstatus = 1
      ORDER BY rep.RepNombre
    `);

    const data = result.recordset.map((r) => ({
      repCodigo: r.RepCodigo,
      repNombre: r.RepNombre,
      repCargo: r.RepCargo,
      supCodigo: r.SupCodigo,
      supNombre: r.SupNombre ?? "(Sin Supervisor)",
      cedCodigo: r.CedCodigo,
      cedDescripcion: r.CedDescripcion ?? "(Sin Centro)",
      division: r.Division ?? "(Sin División)",
    }));

    res.json({ count: data.length, data });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error consultando Representantes", detail: String(err) });
  }
});
