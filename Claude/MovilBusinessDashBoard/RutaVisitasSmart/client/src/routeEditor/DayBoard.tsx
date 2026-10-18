import { DIAS_SEMANA, type RutaPorDia, type DiaSemana } from "../types";
import { DayColumn, type VendedorOpcion } from "./DayColumn";

interface Props {
  ruta: RutaPorDia;
  seleccion: Record<DiaSemana, Set<number>>;
  query: string;
  onToggleSeleccion: (dia: DiaSemana, cliId: number) => void;
  onMoverSeleccion: (dia: DiaSemana, direccion: "arriba" | "abajo") => void;
  onQuitarSeleccion: (dia: DiaSemana) => void;
  onTogglePrimero: (dia: DiaSemana, cliId: number) => void;
  onToggleUltimo: (dia: DiaSemana, cliId: number) => void;
  onToggleSemana: (dia: DiaSemana, cliId: number, semana: number) => void;
  onOrdenarCercania: (dia: DiaSemana) => void;
  /** Solo para "Simular Nuevas Rutas" — ver DayColumn. */
  vendedorOpciones?: VendedorOpcion[];
  vendedorPorCliId?: Map<number, string>;
  onCambiarVendedor?: (dia: DiaSemana, cliId: number, repCodigo: string) => void;
}

export function DayBoard({
  ruta,
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
  return (
    <div className="day-board">
      {DIAS_SEMANA.map((dia) => (
        <DayColumn
          key={dia}
          dia={dia}
          items={ruta[dia]}
          seleccion={seleccion[dia]}
          query={query}
          onToggleSeleccion={(cliId) => onToggleSeleccion(dia, cliId)}
          onMoverSeleccion={(direccion) => onMoverSeleccion(dia, direccion)}
          onQuitarSeleccion={() => onQuitarSeleccion(dia)}
          onTogglePrimero={(cliId) => onTogglePrimero(dia, cliId)}
          onToggleUltimo={(cliId) => onToggleUltimo(dia, cliId)}
          onToggleSemana={(cliId, semana) => onToggleSemana(dia, cliId, semana)}
          onOrdenarCercania={() => onOrdenarCercania(dia)}
          vendedorOpciones={vendedorOpciones}
          vendedorPorCliId={vendedorPorCliId}
          onCambiarVendedor={onCambiarVendedor ? (cliId, repCodigo) => onCambiarVendedor(dia, cliId, repCodigo) : undefined}
        />
      ))}
    </div>
  );
}
