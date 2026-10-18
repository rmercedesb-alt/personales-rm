import { useDroppable } from "@dnd-kit/core";
import { SortableContext, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import type { AsignacionCliente, DiaSemana } from "../types";
import { resolveFrecuencia } from "../frecuencia";

const SEMANAS = [1, 2, 3, 4];

/** 1, 5, 10, 15, 20… (posición "de 5 en 5" pedida por el usuario) — debe coincidir con posicionParaIndice en server/src/routes/exportRuta.ts. */
function posicionParaIndice(idx: number): number {
  return idx === 0 ? 1 : idx * 5;
}

/** Vendedor destino de un cliente, para el selector opcional de "Simular Nuevas Rutas". */
export interface VendedorOpcion {
  repCodigo: string;
  repNombre: string;
  /** `false` = representante fuera de la simulación (no seleccionado en el
   * territorio) pero asignable igual. Omitido/true = está en la simulación. */
  enSimulacion?: boolean;
}

interface Props {
  dia: DiaSemana;
  items: AsignacionCliente[];
  seleccion: Set<number>;
  query: string;
  onToggleSeleccion: (cliId: number) => void;
  onMoverSeleccion: (direccion: "arriba" | "abajo") => void;
  onQuitarSeleccion: () => void;
  onTogglePrimero: (cliId: number) => void;
  onToggleUltimo: (cliId: number) => void;
  onToggleSemana: (cliId: number, semana: number) => void;
  onOrdenarCercania: () => void;
  /** Solo para "Simular Nuevas Rutas": lista de vendedores entre los que se puede
   * reasignar cada cliente, y a qué vendedor pertenece hoy en la propuesta. Si
   * se omite (Crear Ruta), no se muestra ningún selector — cero cambio de
   * comportamiento. */
  vendedorOpciones?: VendedorOpcion[];
  vendedorPorCliId?: Map<number, string>;
  onCambiarVendedor?: (cliId: number, repCodigo: string) => void;
}

function Row({
  item,
  index,
  seleccionado,
  onToggleSeleccion,
  onTogglePrimero,
  onToggleUltimo,
  onToggleSemana,
  vendedorOpciones,
  vendedorActual,
  onCambiarVendedor,
}: {
  item: AsignacionCliente;
  index: number;
  seleccionado: boolean;
  onToggleSeleccion: () => void;
  onTogglePrimero: () => void;
  onToggleUltimo: () => void;
  onToggleSemana: (semana: number) => void;
  vendedorOpciones?: VendedorOpcion[];
  vendedorActual?: string;
  onCambiarVendedor?: (repCodigo: string) => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: String(item.cliId),
  });
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.4 : 1,
  };

  return (
    <div ref={setNodeRef} style={style} className={`day-row ${seleccionado ? "selected" : ""}`}>
      <span className="day-row-drag" {...attributes} {...listeners}>
        ⠿
      </span>
      <input type="checkbox" checked={seleccionado} onChange={onToggleSeleccion} />
      <span className="day-row-pos">{posicionParaIndice(index)}</span>
      <div className="day-row-body">
        <div className="day-row-nombre">{item.cliCodigo} - {item.cliNombre}</div>
        <div className="day-row-meta">
          {item.claDescripcion} · {resolveFrecuencia(item.semanasActivas)}
          <span className="week-toggle">
            {SEMANAS.map((s) => (
              <button
                key={s}
                className={item.semanasActivas.includes(s) ? "active" : ""}
                onClick={() => onToggleSemana(s)}
                title={`Semana ${s}`}
              >
                {s}
              </button>
            ))}
          </span>
        </div>
      </div>
      {vendedorOpciones && onCambiarVendedor && (
        <select
          className="sim-vendedor-select"
          value={vendedorActual ?? ""}
          onChange={(e) => onCambiarVendedor(e.target.value)}
          onPointerDown={(e) => e.stopPropagation()}
          title="Cambiar de vendedor"
        >
          {vendedorOpciones.map((v) => (
            <option key={v.repCodigo} value={v.repCodigo}>
              {v.repCodigo} - {v.repNombre}
            </option>
          ))}
        </select>
      )}
      <div className="day-row-pins">
        <button className={`pin-btn ${item.esPrimero ? "active" : ""}`} onClick={onTogglePrimero} title="Marcar como primero">
          1º
        </button>
        <button className={`pin-btn ${item.esUltimo ? "active" : ""}`} onClick={onToggleUltimo} title="Marcar como último">
          Últ
        </button>
      </div>
    </div>
  );
}

export function DayColumn({
  dia,
  items,
  seleccion,
  query,
  onToggleSeleccion,
  onMoverSeleccion,
  onQuitarSeleccion,
  onTogglePrimero,
  onToggleUltimo,
  onToggleSemana,
  onOrdenarCercania,
  vendedorOpciones,
  vendedorPorCliId,
  onCambiarVendedor,
}: Props) {
  const { setNodeRef, isOver } = useDroppable({ id: dia });

  const porSemana = SEMANAS.map((s) => items.filter((c) => c.semanasActivas.includes(s)).length);

  // La posición mostrada/exportada siempre es la del índice REAL en la lista
  // completa (no del subconjunto filtrado) — el buscador solo oculta filas,
  // nunca renumera.
  const q = query.trim().toLowerCase();
  const buscando = q.length > 0;
  const visibles = items
    .map((item, index) => ({ item, index }))
    .filter(({ item }) => !buscando || `${item.cliCodigo ?? ""} ${item.cliNombre ?? ""}`.toLowerCase().includes(q));

  return (
    <div className={`day-column ${isOver ? "over" : ""}`}>
      <div className="day-column-header">
        <span>{dia}</span>
        <span className="day-column-count">{items.length} distintos</span>
      </div>
      <div className="day-column-semanas">
        {SEMANAS.map((s, i) => (
          <span key={s} className="day-column-semana-chip">
            S{s}: {porSemana[i]}
          </span>
        ))}
      </div>
      <div className="day-column-toolbar">
        <button onClick={onOrdenarCercania} disabled={items.length < 2}>
          📍 Cercanía
        </button>
        {seleccion.size > 0 && (
          <>
            <button onClick={() => onMoverSeleccion("arriba")}>▲</button>
            <button onClick={() => onMoverSeleccion("abajo")}>▼</button>
            <button onClick={onQuitarSeleccion}>Quitar ({seleccion.size})</button>
          </>
        )}
      </div>
      <div ref={setNodeRef} className="day-column-list">
        <SortableContext items={visibles.map(({ item }) => String(item.cliId))} strategy={verticalListSortingStrategy}>
          {visibles.map(({ item, index }) => (
            <Row
              key={item.cliId}
              item={item}
              index={index}
              seleccionado={seleccion.has(item.cliId)}
              onToggleSeleccion={() => onToggleSeleccion(item.cliId)}
              onTogglePrimero={() => onTogglePrimero(item.cliId)}
              onToggleUltimo={() => onToggleUltimo(item.cliId)}
              onToggleSemana={(s) => onToggleSemana(item.cliId, s)}
              vendedorOpciones={vendedorOpciones}
              vendedorActual={vendedorPorCliId?.get(item.cliId)}
              onCambiarVendedor={onCambiarVendedor ? (repCodigo) => onCambiarVendedor(item.cliId, repCodigo) : undefined}
            />
          ))}
        </SortableContext>
        {items.length === 0 && <div className="ms-hint">Arrastrá clientes acá</div>}
        {items.length > 0 && buscando && visibles.length === 0 && <div className="ms-hint">Sin coincidencias en {dia}</div>}
      </div>
    </div>
  );
}
