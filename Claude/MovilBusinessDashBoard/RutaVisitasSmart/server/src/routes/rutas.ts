import { Router } from "express";
import { getPool, sql } from "../db.js";
import { resolveDia, resolveSemanasActivas, resolveFrecuencia } from "../diaResolver.js";

export const rutasRouter = Router();

interface RutaRow {
  RepCodigo: string;
  RepNombre: string;
  RepCargo: string | null;
  SupCodigo: string | null;
  SupNombre: string | null;
  CedCodigo: string | null;
  CedDescripcion: string | null;
  Division: string | null;
  CliID: number;
  CliCodigo: string | null;
  CliNombre: string | null;
  CliNombreComercial: string | null;
  Lat: number;
  Lng: number;
  RutPosicion: number | null;
  RutSemana1: string | null;
  RutSemana2: string | null;
  RutSemana3: string | null;
  RutSemana4: string | null;
  RutSemana5: string | null;
  ClaID: number | null;
  ClaDescripcion: string | null;
  TiNID: number | null;
  TinDescripcion: string | null;
  CanID: number | null;
  CanDescripcion: string | null;
  Grupos: string | null;
}

// Dataset plano para la visualización por Día/Supervisor. RutaVisitas es la
// tabla autoritativa (no RutaVisitasFechaHistoricos, que es bitácora).
rutasRouter.get("/rutas", async (_req, res) => {
  try {
    const pool = await getPool();
    const result = await pool.request().query<RutaRow>(`
      SELECT
        rv.RepCodigo, rep.RepNombre, rep.RepCargo,
        rep.RepSupervisor AS SupCodigo, sup.RepNombre AS SupNombre,
        rep.CedCodigo, cd.CedDescripcion,
        ra.RepTipe AS Division,
        rv.CliID, c.CliCodigo, c.CliNombre, c.CliNombreComercial,
        c.CliLatitud AS Lat, c.CliLongitud AS Lng,
        rv.RutPosicion,
        rv.RutSemana1, rv.RutSemana2, rv.RutSemana3, rv.RutSemana4, rv.RutSemana5,
        c.ClaID, cla.ClaDescripcion,
        c.TiNID, tn.TinDescripcion,
        c.CanID, can.CanDescripcion,
        gx.Grupos
      FROM RutaVisitas rv WITH (NOLOCK)
      JOIN Representantes rep WITH (NOLOCK) ON rep.RepCodigo = rv.RepCodigo
      LEFT JOIN Representantes sup WITH (NOLOCK) ON sup.RepCodigo = rep.RepSupervisor
      LEFT JOIN CentrosDistribucion cd WITH (NOLOCK) ON cd.CedCodigo = rep.CedCodigo
      LEFT JOIN RepresentantesAdicionales ra WITH (NOLOCK) ON ra.RepCodigo = rep.RepCodigo
      JOIN Clientes c WITH (NOLOCK) ON c.CliID = rv.CliID
      LEFT JOIN Clasifica cla WITH (NOLOCK) ON cla.ClaID = c.ClaID
      LEFT JOIN TiposNegocio tn WITH (NOLOCK) ON tn.TiNID = c.TiNID
      LEFT JOIN CanalDistribucion can WITH (NOLOCK) ON can.CanID = c.CanID
      LEFT JOIN (
        SELECT gcd.CliID, STRING_AGG(gc.GrcDescripcion, '|') AS Grupos
        FROM GrupoClientesDetalle gcd WITH (NOLOCK)
        JOIN GrupoClientes gc WITH (NOLOCK) ON gc.GrcCodigo = gcd.GrcCodigo
        GROUP BY gcd.CliID
      ) gx ON gx.CliID = c.CliID
      WHERE rep.RepEstatus = 1
        AND c.CliEstatus = 1
        AND c.CliLatitud IS NOT NULL AND c.CliLatitud <> 0
        AND c.CliLongitud IS NOT NULL AND c.CliLongitud <> 0
    `);

    const data = result.recordset.map((r) => {
      const semanas = [r.RutSemana1, r.RutSemana2, r.RutSemana3, r.RutSemana4];
      const semanasActivas = resolveSemanasActivas(semanas);
      return {
        repCodigo: r.RepCodigo,
        repNombre: r.RepNombre,
        repCargo: r.RepCargo,
        supCodigo: r.SupCodigo,
        supNombre: r.SupNombre ?? "(Sin Supervisor)",
        cedCodigo: r.CedCodigo,
        cedDescripcion: r.CedDescripcion ?? "(Sin Centro)",
        division: r.Division ?? "(Sin División)",
        cliId: r.CliID,
        cliCodigo: r.CliCodigo,
        cliNombre: r.CliNombreComercial?.trim() || r.CliNombre,
        lat: Number(r.Lat),
        lng: Number(r.Lng),
        rutPosicion: r.RutPosicion,
        dia: resolveDia(semanas) ?? "Sin Asignar",
        semanasActivas,
        frecuencia: resolveFrecuencia(semanasActivas),
        claId: r.ClaID,
        claDescripcion: r.ClaDescripcion ?? "Sin Clasificación",
        tiNId: r.TiNID,
        tinDescripcion: r.TinDescripcion ?? "Sin Tipo",
        canId: r.CanID,
        canDescripcion: r.CanDescripcion ?? "Sin Canal",
        grupos: r.Grupos ? r.Grupos.split("|") : [],
      };
    });

    res.json({ count: data.length, data });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Error consultando RutaVisitas", detail: String(err) });
  }
});
