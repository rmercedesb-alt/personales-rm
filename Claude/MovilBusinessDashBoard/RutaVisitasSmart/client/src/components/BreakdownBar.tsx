import { useMemo, useState } from "react";
import type { ClienteRuta } from "../types";
import { ExportButtons } from "./ExportButtons";

const DIA_ORDEN = ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo", "Sin Asignar"];
const DIAS_MATRIZ = ["Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"];
const SEMANAS_MATRIZ = [1, 2, 3, 4];

type Modo = "total" | "semana" | "dia";

interface Props {
  data: ClienteRuta[];
}

export function BreakdownBar({ data }: Props) {
  const [activos, setActivos] = useState<Set<Modo>>(new Set(["total"]));

  function toggle(m: Modo) {
    setActivos((prev) => {
      const next = new Set(prev);
      if (next.has(m)) next.delete(m);
      else next.add(m);
      return next;
    });
  }

  const stats = useMemo(() => {
    const clientes = new Set<number>();
    const vendedores = new Set<string>();
    const supervisores = new Set<string>();
    for (const r of data) {
      clientes.add(r.cliId);
      vendedores.add(r.repCodigo);
      if (r.supCodigo) supervisores.add(r.supCodigo);
    }
    return { clientes: clientes.size, vendedores: vendedores.size, supervisores: supervisores.size };
  }, [data]);

  const porSemana = useMemo(() => {
    const sets: Record<number, Set<number>> = { 1: new Set(), 2: new Set(), 3: new Set(), 4: new Set() };
    for (const r of data) {
      for (const s of r.semanasActivas) {
        if (sets[s]) sets[s].add(r.cliId);
      }
    }
    return [1, 2, 3, 4].map((s) => ({ semana: s, count: sets[s].size }));
  }, [data]);

  const porDia = useMemo(() => {
    const sets = new Map<string, Set<number>>();
    for (const r of data) {
      if (!sets.has(r.dia)) sets.set(r.dia, new Set());
      sets.get(r.dia)!.add(r.cliId);
    }
    return DIA_ORDEN.filter((d) => sets.has(d)).map((d) => ({ dia: d, count: sets.get(d)!.size }));
  }, [data]);

  // Matriz Semana(1-4) × Día — cruza ambas dimensiones en vez de mostrarlas
  // por separado, para ver de un vistazo cuántos clientes hay en cada
  // combinación real de semana+día. Al final, 3 filas de totales: Semanales
  // (frecuencia Semanal) por día, Quincenales por día, y el Total general por
  // día (igual al de "Por Día" de arriba). Y una columna "Total" después de
  // Domingo con el total DISTINTO de cada fila (no la suma de las columnas —
  // se cuenta por Set, así que un cliente no se duplica) — la esquina
  // inferior derecha (fila Total × columna Total) es el total general.
  const matriz = useMemo(() => {
    const porSemanaDia = new Map<string, Set<number>>(); // key `${semana}|${dia}`
    const semanalesPorDia = new Map<string, Set<number>>();
    const quincenalesPorDia = new Map<string, Set<number>>();
    const totalPorDia = new Map<string, Set<number>>();

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
      if (!DIAS_MATRIZ.includes(r.dia)) continue;
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

    const filasSemana = SEMANAS_MATRIZ.map((s) => ({
      etiqueta: `Semana ${s}`,
      valores: DIAS_MATRIZ.map((d) => porSemanaDia.get(`${s}|${d}`)?.size ?? 0),
      total: porSemanaTotal.get(s)?.size ?? 0,
    }));
    const filaDe = (map: Map<string, Set<number>>, etiqueta: string, totalSet: Set<number>) => ({
      etiqueta,
      valores: DIAS_MATRIZ.map((d) => map.get(d)?.size ?? 0),
      total: totalSet.size,
    });

    return {
      filas: [
        ...filasSemana,
        filaDe(semanalesPorDia, "Semanales", semanalesTotal),
        filaDe(quincenalesPorDia, "Quincenales", quincenalesTotal),
        filaDe(totalPorDia, "Total", granTotal),
      ],
    };
  }, [data]);

  // Segunda matriz: Tamaño de cliente × (Día, Semana, Frecuencia) — mismo
  // universo que la matriz de arriba (excluye "Sin Asignar"). Una fila por
  // tamaño de clasificación de cliente (A Grande, B Mediano, etc.), con
  // bloques de columnas: por día, por semana, por frecuencia, y el total
  // distinto de esa fila al final.
  const matrizTamano = useMemo(() => {
    const porTamanoDia = new Map<string, Set<number>>();
    const porTamanoSemana = new Map<string, Set<number>>();
    const porTamanoFrec = new Map<string, Set<number>>();
    const porTamanoTotal = new Map<string, Set<number>>();

    function add(map: Map<string, Set<number>>, key: string, cliId: number) {
      if (!map.has(key)) map.set(key, new Set());
      map.get(key)!.add(cliId);
    }

    const tamanos = new Set<string>();
    for (const r of data) {
      if (!DIAS_MATRIZ.includes(r.dia)) continue;
      tamanos.add(r.claDescripcion);
      add(porTamanoDia, `${r.claDescripcion}|${r.dia}`, r.cliId);
      for (const s of r.semanasActivas) add(porTamanoSemana, `${r.claDescripcion}|${s}`, r.cliId);
      if (r.frecuencia === "Semanal" || r.frecuencia === "Quincenal") add(porTamanoFrec, `${r.claDescripcion}|${r.frecuencia}`, r.cliId);
      add(porTamanoTotal, r.claDescripcion, r.cliId);
    }

    const filas = [...tamanos].sort().map((t) => ({
      etiqueta: t,
      dias: DIAS_MATRIZ.map((d) => porTamanoDia.get(`${t}|${d}`)?.size ?? 0),
      semanas: SEMANAS_MATRIZ.map((s) => porTamanoSemana.get(`${t}|${s}`)?.size ?? 0),
      semanal: porTamanoFrec.get(`${t}|Semanal`)?.size ?? 0,
      quincenal: porTamanoFrec.get(`${t}|Quincenal`)?.size ?? 0,
      total: porTamanoTotal.get(t)?.size ?? 0,
    }));

    return { filas };
  }, [data]);

  return (
    <div className="breakdownbar" id="breakdownbar-imprimible">
      <div className="toggle-group">
        <button className={activos.has("total") ? "active" : ""} onClick={() => toggle("total")}>
          Total
        </button>
        <button className={activos.has("semana") ? "active" : ""} onClick={() => toggle("semana")}>
          Por Semana
        </button>
        <button className={activos.has("dia") ? "active" : ""} onClick={() => toggle("dia")}>
          Por Día
        </button>
        <span className="no-imprimir" style={{ marginLeft: "auto", display: "flex", gap: 6 }}>
          <ExportButtons data={data} />
        </span>
      </div>
      <div className="chips">
        {activos.has("total") && (
          <>
            <span className="chip">
              Supervisores: <b>{stats.supervisores}</b>
            </span>
            <span className="chip">
              Vendedores: <b>{stats.vendedores}</b>
            </span>
            <span className="chip">
              Clientes: <b>{stats.clientes}</b>
            </span>
          </>
        )}
        {activos.has("semana") &&
          porSemana.map((s) => (
            <span className="chip" key={s.semana}>
              Semana {s.semana}: <b>{s.count}</b> Clientes
            </span>
          ))}
      </div>
      {activos.has("dia") && (
        <div className="matrices-row">
          <div className="matriz-dias-wrap">
            <table className="matriz-dias">
              <thead>
                <tr>
                  <th></th>
                  {DIAS_MATRIZ.map((d) => (
                    <th key={d}>{d}</th>
                  ))}
                  <th className="matriz-total-col">Total</th>
                </tr>
              </thead>
              <tbody>
                {matriz.filas.map((f) => (
                  <tr key={f.etiqueta} className={f.etiqueta === "Total" ? "matriz-total" : undefined}>
                    <td className="matriz-etiqueta">{f.etiqueta}</td>
                    {f.valores.map((v, i) => (
                      <td key={i}>{v}</td>
                    ))}
                    <td className="matriz-total-col">{f.total}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="matriz-dias-wrap">
            <table className="matriz-dias">
              <thead>
                <tr>
                  <th rowSpan={2}>Tamaño</th>
                  <th colSpan={DIAS_MATRIZ.length}>Día</th>
                  <th colSpan={SEMANAS_MATRIZ.length}>Semana</th>
                  <th colSpan={2}>Frecuencia</th>
                  <th rowSpan={2} className="matriz-total-col">Total</th>
                </tr>
                <tr>
                  {DIAS_MATRIZ.map((d) => (
                    <th key={d}>{d.slice(0, 3)}</th>
                  ))}
                  {SEMANAS_MATRIZ.map((s) => (
                    <th key={s}>S{s}</th>
                  ))}
                  <th>Semanal</th>
                  <th>Quinc.</th>
                </tr>
              </thead>
              <tbody>
                {matrizTamano.filas.map((f) => (
                  <tr key={f.etiqueta}>
                    <td className="matriz-etiqueta">{f.etiqueta}</td>
                    {f.dias.map((v, i) => (
                      <td key={`d${i}`}>{v}</td>
                    ))}
                    {f.semanas.map((v, i) => (
                      <td key={`s${i}`}>{v}</td>
                    ))}
                    <td>{f.semanal}</td>
                    <td>{f.quincenal}</td>
                    <td className="matriz-total-col">{f.total}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
