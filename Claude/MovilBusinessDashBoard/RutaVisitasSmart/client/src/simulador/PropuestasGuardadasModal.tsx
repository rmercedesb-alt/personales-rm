import { useRef, useState } from "react";
import { importarPropuesta, nombreArchivoPropuesta, type PropuestaGuardada } from "./propuestasGuardadas";
import { guardarArchivo } from "./guardarArchivo";

interface Props {
  propuestas: PropuestaGuardada[];
  puedeGuardar: boolean;
  onGuardar: (nombre: string) => void;
  onCargar: (p: PropuestaGuardada) => void;
  onEliminar: (id: string) => void;
  /** Se llama después de importar un archivo con éxito, para refrescar la lista. */
  onImportado: () => void;
  onCerrar: () => void;
}

function formatearFecha(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString("es-DO", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

/** Guardar la propuesta actual (para revisarla después y decidir si seguir
 * ajustándola o exportarla tal cual), cargar/eliminar propuestas guardadas
 * anteriores, y descargar/importar como archivo .json para llevarlas a otra
 * PC o guardarlas fuera de este navegador (ej. en OneDrive). */
export function PropuestasGuardadasModal({ propuestas, puedeGuardar, onGuardar, onCargar, onEliminar, onImportado, onCerrar }: Props) {
  const [nombre, setNombre] = useState("");
  const [mensaje, setMensaje] = useState<string | null>(null);
  const inputArchivoRef = useRef<HTMLInputElement>(null);

  function confirmarGuardar() {
    const limpio = nombre.trim();
    if (!limpio) return;
    onGuardar(limpio);
    setNombre("");
  }

  async function descargar(p: PropuestaGuardada) {
    setMensaje(null);
    try {
      const msg = await guardarArchivo(nombreArchivoPropuesta(p), JSON.stringify(p, null, 2), "application/json");
      setMensaje(msg);
    } catch (e) {
      setMensaje(e instanceof Error ? e.message : String(e));
    }
  }

  function abrirSelectorArchivo() {
    inputArchivoRef.current?.click();
  }

  async function onArchivoElegido(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = ""; // permite volver a elegir el mismo archivo después
    if (!file) return;
    setMensaje(null);
    try {
      const contenido = await file.text();
      const importada = importarPropuesta(contenido);
      onImportado();
      setMensaje(`Se importó "${importada.nombre}".`);
    } catch (err) {
      setMensaje(err instanceof Error ? err.message : String(err));
    }
  }

  return (
    <div className="sim-modal-backdrop">
      <div className="sim-modal sim-modal-ancho">
        <h3>💾 Propuestas guardadas</h3>
        <p className="sim-modal-hint">
          Guarda la propuesta actual para revisarla más tarde y decidir si seguir ajustándola o exportarla tal cual. Se guarda en
          este navegador — usa "⬇ Descargar" en cada fila para llevarla a otra PC o guardarla fuera del navegador (ej. OneDrive), y
          "⬆ Importar desde archivo" para volver a cargarla ahí.
        </p>
        {puedeGuardar ? (
          <div className="sim-field">
            <span>Nombre para esta propuesta</span>
            <div className="map-assign-row">
              <input
                type="text"
                value={nombre}
                onChange={(e) => setNombre(e.target.value)}
                placeholder="Ej. Reducir Zona Este de 3 a 2 vendedores"
                onKeyDown={(e) => e.key === "Enter" && confirmarGuardar()}
              />
              <button className="export-btn" disabled={!nombre.trim()} onClick={confirmarGuardar}>
                Guardar actual
              </button>
            </div>
          </div>
        ) : (
          <p className="sim-modal-hint">Generá una propuesta primero para poder guardarla.</p>
        )}

        <div className="map-assign-row">
          <button className="board-toggle-btn" onClick={abrirSelectorArchivo}>
            ⬆ Importar desde archivo
          </button>
          <input ref={inputArchivoRef} type="file" accept="application/json,.json" className="sim-input-oculto" onChange={onArchivoElegido} />
        </div>
        {mensaje && <p className="sim-modal-hint">{mensaje}</p>}

        <div className="sim-propuestas-lista">
          {propuestas.length === 0 && <p className="sim-modal-hint">Todavía no hay propuestas guardadas.</p>}
          {propuestas.map((p) => (
            <div className="sim-propuesta-row" key={p.id}>
              <div className="sim-propuesta-info">
                <div className="sim-propuesta-nombre">{p.nombre}</div>
                <div className="sim-propuesta-meta">
                  {formatearFecha(p.fecha)} · {p.resumenTexto}
                </div>
              </div>
              <div className="sim-propuesta-acciones">
                <button className="board-toggle-btn" onClick={() => onCargar(p)}>
                  📂 Cargar
                </button>
                <button className="board-toggle-btn" onClick={() => descargar(p)} title="Descargar como archivo .json">
                  ⬇ Descargar
                </button>
                <button className="board-toggle-btn sim-btn-peligro" onClick={() => onEliminar(p.id)} title="Eliminar esta propuesta guardada">
                  🗑
                </button>
              </div>
            </div>
          ))}
        </div>

        <div className="sim-modal-actions">
          <button className="board-toggle-btn" onClick={onCerrar}>
            Cerrar
          </button>
        </div>
      </div>
    </div>
  );
}
