import "dotenv/config";
import express from "express";
import cors from "cors";
import session from "express-session";
import { authRouter, requireAuth } from "./auth.js";
import { rutasRouter } from "./routes/rutas.js";
import { vendedoresRouter } from "./routes/vendedores.js";
import { clientesRouter } from "./routes/clientes.js";
import { exportRutaRouter } from "./routes/exportRuta.js";
import { aplicarRutaRouter } from "./routes/aplicarRuta.js";
import { suplidoresRouter } from "./routes/suplidores.js";
import { exportSimulacionRouter } from "./routes/exportSimulacion.js";
import { frecuenciaCompraRouter } from "./routes/frecuenciaCompra.js";
import { exportResumenDiasRouter } from "./routes/exportResumenDias.js";

const app = express();
app.use(cors());
// El export de "Simular Nuevas Rutas" manda el detalle completo de cada
// cliente en cada zona (puede ser miles de filas) — el límite por defecto de
// express.json() es 100kb y se queda corto, devuelve 413.
app.use(express.json({ limit: "25mb" }));
app.use(
  session({
    secret: process.env.SESSION_SECRET ?? "rutavisitas-dev-secret",
    resave: false,
    saveUninitialized: false,
    cookie: { httpOnly: true, sameSite: "lax", maxAge: 8 * 60 * 60 * 1000 },
  })
);

app.get("/health", (_req, res) => res.json({ ok: true }));

// Público (login, logout, sesión actual, branding para la pantalla de login)
app.use("/api", authRouter);
// Todo lo que se registre DESPUÉS de esta línea requiere sesión iniciada.
app.use("/api", requireAuth);
app.use("/api", rutasRouter);
app.use("/api", vendedoresRouter);
app.use("/api", clientesRouter);
app.use("/api", exportRutaRouter);
app.use("/api", aplicarRutaRouter);
app.use("/api", suplidoresRouter);
app.use("/api", exportSimulacionRouter);
app.use("/api", frecuenciaCompraRouter);
app.use("/api", exportResumenDiasRouter);

const port = Number(process.env.PORT ?? 4000);
app.listen(port, () => {
  console.log(`RutaVisitasSmart API escuchando en http://localhost:${port}`);
});
