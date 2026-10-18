import type { Sesion } from "../auth/api";

interface NavItemDef {
  key: string;
  label: string;
  enabled: boolean;
}

const ITEMS: NavItemDef[] = [
  { key: "dia-supervisor", label: "Por Día / Supervisor", enabled: true },
  { key: "sup-vendedor-dia", label: "Por Supervisor / Vendedor / Día", enabled: false },
  { key: "sup-tamano-dia", label: "Por Supervisor / Tamaño / Día", enabled: false },
  { key: "crear-ruta", label: "Crear Ruta", enabled: true },
  { key: "modificar-ruta", label: "Modificar Ruta", enabled: true },
  { key: "simular-rutas", label: "Simular Nuevas Rutas", enabled: true },
  { key: "reportes", label: "Reportes", enabled: false },
];

interface Props {
  active: string;
  onSelect: (key: string) => void;
  sesion: Sesion;
  onLogout: () => void;
  collapsed: boolean;
  onToggleCollapsed: () => void;
}

export function Sidebar({ active, onSelect, sesion, onLogout, collapsed, onToggleCollapsed }: Props) {
  if (collapsed) {
    return (
      <div className="sidebar sidebar-collapsed">
        <button className="sidebar-expand-btn" onClick={onToggleCollapsed} title="Mostrar menú">
          »
        </button>
      </div>
    );
  }

  return (
    <div className="sidebar">
      <div className="sidebar-title">
        <div className="sidebar-title-row">
          RutaVisitasSmart
          <button className="sidebar-collapse-btn" onClick={onToggleCollapsed} title="Ocultar menú">
            «
          </button>
        </div>
        <span>Mantenimiento y visualización de rutas</span>
      </div>
      {ITEMS.map((item) => (
        <div
          key={item.key}
          className={`nav-item ${active === item.key ? "active" : ""} ${!item.enabled ? "disabled" : ""}`}
          onClick={() => item.enabled && onSelect(item.key)}
        >
          {item.label}
          {!item.enabled && <span className="badge">Próximamente</span>}
        </div>
      ))}
      <div className="sidebar-footer">
        <div className="sidebar-footer-usuario" title={sesion.usuario}>
          {sesion.nombre || sesion.usuario}
        </div>
        <button className="sidebar-footer-logout" onClick={onLogout}>
          Cerrar sesión
        </button>
      </div>
    </div>
  );
}
