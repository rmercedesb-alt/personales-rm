import { DIAS_SEMANA } from "../types";
import { SIN_RUTA_PREVIA, type ResumenPropuesta as ResumenPropuestaData } from "./resumenPropuestaCalc";

interface Props {
  resumen: ResumenPropuestaData;
}

const SEMANAS = [1, 2, 3, 4];

/** Pantalla aparte con la tabla resumida de la propuesta ya generada,
 * comparando antes (RutaVisitas hoy) vs. ahora (la propuesta) — los mismos
 * números se incluyen en la hoja "Resumen" del Excel exportado. */
export function ResumenPropuesta({ resumen }: Props) {
  // Domingo solo aparece si hay algún cliente ahí (antes o ahora) — ahora se puede
  // asignar a cualquier día, pero casi nunca se usa y no hace falta una columna vacía.
  const hayDomingo = (resumen.clientesPorDiaAntes["Domingo"] ?? 0) > 0 || (resumen.clientesPorDiaAhora["Domingo"] ?? 0) > 0;
  const diasMostrar = DIAS_SEMANA.filter((d) => d !== "Domingo" || hayDomingo);
  const porTamano = Object.entries(resumen.clientesPorTamano).sort((a, b) => b[1] - a[1]);
  const diaSinPrevio = resumen.clientesPorDiaAntes["Sin día previo"] ?? 0;
  const semanalAntes = resumen.clientesPorFrecuenciaAntes["Semanal"] ?? 0;
  const quincenalAntes = resumen.clientesPorFrecuenciaAntes["Quincenal"] ?? 0;
  const personalizadaAntes = resumen.clientesPorFrecuenciaAntes["Personalizada"] ?? 0;
  const sinAsignarAntes = resumen.clientesPorFrecuenciaAntes["Sin Asignar"] ?? 0;

  return (
    <div className="sim-resumen">
      <div className="route-stats">
        <span className="chip">
          Vendedores: <b>{resumen.totalVendedoresAntes}</b> → <b>{resumen.totalVendedoresAhora}</b>
        </span>
        <span className="chip">
          Total Clientes: <b>{resumen.totalClientes.toLocaleString()}</b>
        </span>
        <span className="chip">
          Semanales: <b>{semanalAntes.toLocaleString()}</b> → <b>{resumen.clientesSemanalesAhora.toLocaleString()}</b>
        </span>
        <span className="chip">
          Quincenales: <b>{quincenalAntes.toLocaleString()}</b> → <b>{resumen.clientesQuincenalesAhora.toLocaleString()}</b>
        </span>
        {personalizadaAntes > 0 && (
          <span className="chip">
            Frecuencia personalizada (antes): <b>{personalizadaAntes.toLocaleString()}</b>
          </span>
        )}
        {sinAsignarAntes > 0 && (
          <span className="chip">
            Sin ruta previa (antes): <b>{sinAsignarAntes.toLocaleString()}</b>
          </span>
        )}
      </div>

      <div className="sim-resumen-cards">
        <div className="sim-resumen-card">
          <div className="sim-resumen-card-titulo">Clientes por día (antes → ahora)</div>
          <table className="detail-table">
            <thead>
              <tr>
                <th>Día</th>
                <th>Antes</th>
                <th>Ahora</th>
              </tr>
            </thead>
            <tbody>
              {diasMostrar.map((d) => (
                <tr key={d}>
                  <td>{d}</td>
                  <td>{(resumen.clientesPorDiaAntes[d] ?? 0).toLocaleString()}</td>
                  <td>{(resumen.clientesPorDiaAhora[d] ?? 0).toLocaleString()}</td>
                </tr>
              ))}
              {diaSinPrevio > 0 && (
                <tr>
                  <td>Sin día previo</td>
                  <td>{diaSinPrevio.toLocaleString()}</td>
                  <td>—</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="sim-resumen-card">
          <div className="sim-resumen-card-titulo">Clientes por semana (antes → ahora)</div>
          <table className="detail-table">
            <thead>
              <tr>
                <th>Semana</th>
                <th>Antes</th>
                <th>Ahora</th>
              </tr>
            </thead>
            <tbody>
              {SEMANAS.map((s) => (
                <tr key={s}>
                  <td>Semana {s}</td>
                  <td>{(resumen.clientesPorSemanaAntes[s] ?? 0).toLocaleString()}</td>
                  <td>{(resumen.clientesPorSemanaAhora[s] ?? 0).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="sim-resumen-card">
          <div className="sim-resumen-card-titulo">Clientes por tamaño (no cambia con la simulación)</div>
          <table className="detail-table">
            <thead>
              <tr>
                <th>Tamaño</th>
                <th>Clientes</th>
              </tr>
            </thead>
            <tbody>
              {porTamano.map(([tam, n]) => (
                <tr key={tam}>
                  <td>{tam}</td>
                  <td>{n.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="sim-resumen-card sim-resumen-vendedores">
        <div className="sim-resumen-card-titulo">Resumen por vendedor — clientes antes vs. ahora, por día y por frecuencia (ahora)</div>
        <div className="sim-resumen-scroll">
          <table className="detail-table">
            <thead>
              <tr>
                <th>Vendedor</th>
                <th>Antes</th>
                <th>Ahora</th>
                {diasMostrar.map((d) => (
                  <th key={d}>{d}</th>
                ))}
                <th>Semanales</th>
                <th>Quincenales</th>
              </tr>
            </thead>
            <tbody>
              {resumen.porVendedor.map((v) => (
                <tr key={v.repCodigo}>
                  <td>
                    {v.repCodigo === SIN_RUTA_PREVIA ? v.repNombre : `${v.repCodigo} - ${v.repNombre}`}
                    {v.saliente && <span className="sim-saliente-tag">Saliente</span>}
                  </td>
                  <td>{v.antes.toLocaleString()}</td>
                  <td>{v.ahora.toLocaleString()}</td>
                  {diasMostrar.map((d) => (
                    <td key={d}>{(v.porDia[d] ?? 0).toLocaleString()}</td>
                  ))}
                  <td>{v.semanales.toLocaleString()}</td>
                  <td>{v.quincenales.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
