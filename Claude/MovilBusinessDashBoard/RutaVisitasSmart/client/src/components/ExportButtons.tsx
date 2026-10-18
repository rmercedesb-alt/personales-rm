import { useState } from "react";
import { exportarResumenDias } from "../api";
import type { ClienteRuta } from "../types";

interface Props {
  data: ClienteRuta[];
}

/** "Imprimir PDF" = abre el diálogo de impresión del navegador (con
 * "Guardar como PDF" el usuario elige el destino) sobre una vista impresa
 * limpia — ver `@media print` en theme.css, que oculta todo lo que no sea el
 * resumen/matriz. No genera el PDF del lado del servidor: es justo lo que
 * pidió el usuario ("imprimir en PDF"), sin agregar una librería nueva. */
export function ExportButtons({ data }: Props) {
  const [exportando, setExportando] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  function handleImprimir() {
    window.print();
  }

  async function handleExcel() {
    setExportando(true);
    setMsg(null);
    try {
      await exportarResumenDias(data);
      setMsg("Excel generado ✓");
    } catch (e) {
      setMsg(`Error: ${String(e)}`);
    } finally {
      setExportando(false);
    }
  }

  return (
    <>
      <button className="board-toggle-btn" onClick={handleImprimir} title="Abre el diálogo de impresión del navegador — elegí 'Guardar como PDF' como destino">
        🖨 Imprimir PDF
      </button>
      <button className="board-toggle-btn" onClick={handleExcel} disabled={exportando}>
        {exportando ? "Generando…" : "⬇ Excel"}
      </button>
      {msg && <span className="stat">{msg}</span>}
    </>
  );
}
