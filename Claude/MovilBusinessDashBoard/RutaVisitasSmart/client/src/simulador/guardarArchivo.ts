/** Descarga contenido como archivo, dejando elegir carpeta cuando el
 * navegador lo soporta (Chrome/Edge) — mismo patrón que exportarSimulacion(). */
export async function guardarArchivo(filename: string, contenido: string, mime: string): Promise<string> {
  const blob = new Blob([contenido], { type: mime });
  const ext = filename.includes(".") ? `.${filename.split(".").pop()}` : "";

  if (window.showSaveFilePicker) {
    try {
      const handle = await window.showSaveFilePicker({
        suggestedName: filename,
        types: [{ description: "Archivo", accept: { [mime]: [ext] } }],
      });
      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();
      return `Guardado como "${filename}" en la carpeta que elegiste.`;
    } catch (e) {
      if ((e as { name?: string })?.name === "AbortError") throw new Error("Descarga cancelada.");
      // Si falla por otro motivo, cae al método clásico.
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
  return `Descargado como "${filename}" — revisa la carpeta de Descargas del navegador.`;
}
