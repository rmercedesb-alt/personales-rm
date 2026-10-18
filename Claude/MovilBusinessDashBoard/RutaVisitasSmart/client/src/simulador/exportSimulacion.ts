import type { ZonaSimulada } from "../types";
import type { ResumenPropuesta } from "./resumenPropuestaCalc";

export interface ExportSimulacionParams {
  zonas: ZonaSimulada[];
  salientes: string[];
  divisionPorRep: Map<string, string>;
  /** Nombres de TODOS los vendedores seleccionados (quedan + salientes) — para
   * poder mandar el nombre del vendedor/día anterior de cada cliente. */
  nombrePorCodigoTodos: Map<string, string>;
  desde: string;
  hasta: string;
  suplidores: number[];
  resumen: ResumenPropuesta;
  /** Todos los clientes que participaron de la simulación (territorio + excluidos
   * + asignados). El script solo "quita" de la ruta de un vendedor a los de esta
   * lista que ya no le quedaron; el resto de su ruta (días/semanas filtrados
   * afuera, o un representante de afuera al que solo se le asignaron algunos
   * clientes) no se toca. Sin esto el servidor limpia todo lo que no esté en la
   * propuesta, como hacía antes. */
  universoCliIds?: number[];
}

/** Devuelve un mensaje para mostrarle al usuario dónde quedó el archivo. */
export async function exportarSimulacion({
  zonas,
  salientes,
  divisionPorRep,
  nombrePorCodigoTodos,
  desde,
  hasta,
  suplidores,
  resumen,
  universoCliIds,
}: ExportSimulacionParams): Promise<string> {
  const body = {
    zonas: zonas.map((z) => ({
      dia: z.dia,
      repCodigo: z.repCodigo,
      repNombre: z.repNombre,
      division: divisionPorRep.get(z.repCodigo) ?? null,
      clientes: z.clientes.map((c) => ({
        cliId: c.cliId,
        cliCodigo: c.cliCodigo,
        cliNombre: c.cliNombre,
        lat: c.lat,
        lng: c.lng,
        semanasActivas: c.semanasActivas,
        repCodigoOriginal: c.repCodigoOriginal,
        repNombreOriginal: c.repCodigoOriginal ? nombrePorCodigoTodos.get(c.repCodigoOriginal) ?? null : null,
        diaOriginal: c.diaOriginal,
        claDescripcion: c.claDescripcion,
      })),
    })),
    salientes,
    desde,
    hasta,
    suplidores,
    resumen,
    universoCliIds,
  };

  const res = await fetch("/api/export-simulacion", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Error ${res.status} generando el export`);

  const blob = await res.blob();
  const filename = `Simulacion_Rutas_${new Date().toISOString().slice(0, 10)}.xlsx`;

  // Si el navegador soporta elegir carpeta de destino (Chrome/Edge), se le
  // pregunta al usuario dónde guardarlo — así siempre sabe dónde quedó.
  if (window.showSaveFilePicker) {
    try {
      const handle = await window.showSaveFilePicker({
        suggestedName: filename,
        types: [
          {
            description: "Libro de Excel",
            accept: { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": [".xlsx"] },
          },
        ],
      });
      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();
      return `Guardado como "${filename}" en la carpeta que elegiste.`;
    } catch (e) {
      if ((e as { name?: string })?.name === "AbortError") throw new Error("Exportación cancelada.");
      // Si falla por otro motivo (no soportado en este contexto, etc.), sigue con la descarga clásica.
    }
  }

  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
  return `Descargado como "${filename}" — revisa la carpeta de Descargas del navegador (o su historial de descargas, Ctrl+J en Chrome/Edge).`;
}
