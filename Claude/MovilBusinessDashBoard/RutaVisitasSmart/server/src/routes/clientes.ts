import { Router } from "express";
import { getPool } from "../db.js";

export const clientesRouter = Router();

interface ClienteRow {
  CliID: number;
  CliCodigo: string | null;
  CliNombre: string | null;
  CliNombreComercial: string | null;
  Lat: number;
  Lng: number;
  ClaID: number | null;
  ClaDescripcion: string | null;
  TiNID: number | null;
  TinDescripcion: string | null;
  CanID: number | null;
  CanDescripcion: string | null;
  Grupos: string | null;
  DuenoCodigo: string | null;
  DuenoNombre: string | null;
}

// Universo completo de clientes activos con GPS, independiente de
// RutaVisitas (a diferencia de /api/rutas, que solo trae los que ya tienen
// una fila ahí). Es el candidato para el panel de "sin asignar" del editor
// de ruta: el frontend cruza esto contra /api/rutas para saber quién ya
// está en una ruta (de este vendedor o de otro).
clientesRouter.get("/clientes", async (_req, res) => {
  try {
    const pool = await getPool();
    const result = await pool.request().query<ClienteRow>(`
      SELECT
        c.CliID, c.CliCodigo, c.CliNombre, c.CliNombreComercial,
        c.CliLatitud AS Lat, c.CliLongitud AS Lng,
        c.ClaID, cla.ClaDescripcion,
        c.TiNID, tn.TinDescripcion,
        c.CanID, can.CanDescripcion,
        gx.Grupos,
        c.RepCodigo AS DuenoCodigo, rep.RepNombre AS DuenoNombre
      FROM Clientes c WITH (NOLOCK)
      LEFT JOIN Clasifica cla WITH (NOLOCK) ON cla.ClaID = c.ClaID
      LEFT JOIN TiposNegocio tn WITH (NOLOCK) ON tn.TiNID = c.TiNID
      LEFT JOIN CanalDistribucion can WITH (NOLOCK) ON can.CanID = c.CanID
      LEFT JOIN Representantes rep WITH (NOLOCK) ON rep.RepCodigo = c.RepCodigo
      LEFT JOIN (
        SELECT gcd.CliID, STRING_AGG(gc.GrcDescripcion, '|') AS Grupos
        FROM GrupoClientesDetalle gcd WITH (NOLOCK)
        JOIN GrupoClientes gc WITH (NOLOCK) ON gc.GrcCodigo = gcd.GrcCodigo
        GROUP BY gcd.CliID
      ) gx ON gx.CliID = c.CliID
      WHERE c.CliEstatus = 1
        AND c.CliLatitud IS NOT NULL AND c.CliLatitud <> 0
        AND c.CliLongitud IS NOT NULL AND c.CliLongitud <> 0
    `);

    const data = result.recordset.map((r) => ({
      cliId: r.CliID,
      cliCodigo: r.CliCodigo,
      cliNombre: r.CliNombreComercial?.trim() || r.CliNombre,
      lat: Number(r.Lat),
      lng: Number(r.Lng),
      claId: r.ClaID,
      claDescripcion: r.ClaDescripcion ?? "Sin Clasificación",
      tiNId: r.TiNID,
      tinDescripcion: r.TinDescripcion ?? "Sin Tipo",
      canId: r.CanID,
      canDescripcion: r.CanDescripcion ?? "Sin Canal",
      grupos: r.Grupos ? r.Grupos.split("|") : [],
      duenoCodigo: r.DuenoCodigo,
      duenoNombre: r.DuenoNombre,
    }));

    res.json({ count: data.length, data });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error consultando Clientes", detail: String(err) });
  }
});
