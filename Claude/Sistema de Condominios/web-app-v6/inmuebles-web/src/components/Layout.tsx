import { useState, useEffect } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'
import { useEmpresaStore } from '../store/empresaStore'
import {
  LayoutDashboard, Building2, Users, FileText, CreditCard,
  DollarSign, Landmark, Map, LogOut, Menu, X,
  ChevronDown, Settings, UserCog, Tags, Home, Printer, Wallet, RefreshCw,
  Receipt, ListChecks, BarChart2, TrendingUp, Coins
} from 'lucide-react'
import { cn } from '../lib/utils'

const BRAND_BLUE = '#1B4F8C'
const BRAND_DARK = '#0e2f55'

type NavItem = { to: string; label: string; icon: React.ElementType }
type NavGroup = { label: string; icon: React.ElementType; key: string; items: NavItem[] }
type NavEntry = NavItem | NavGroup

function isGroup(e: NavEntry): e is NavGroup { return 'items' in e }

const NAV: NavEntry[] = [
  { to: '/',    label: 'Tablero', icon: LayoutDashboard },
  { to: '/mapa', label: 'Mapa',     icon: Map },

  {
    key: 'mantenimiento', label: 'Mantenimiento', icon: Settings,
    items: [
      { to: '/mantenimiento/propietarios', label: 'Propietarios', icon: UserCog },
      { to: '/mantenimiento/inquilinos',   label: 'Inquilinos',   icon: Users },
      { to: '/mantenimiento/inmuebles',    label: 'Inmuebles',    icon: Home },
      { to: '/mantenimiento/usuarios',     label: 'Usuarios',     icon: UserCog },
      { to: '/mantenimiento/estados',      label: 'Estados',      icon: Tags },
      { to: '/mantenimiento/empresa',      label: 'Empresa',      icon: Building2 },
      { to: '/mantenimiento/procesos',     label: 'Procesos',     icon: RefreshCw },
      { to: '/mantenimiento/informes',     label: 'Informes',     icon: BarChart2 },
      { to: '/mantenimiento/reporte-propietario', label: 'Reporte por Propietario', icon: BarChart2 },
      { to: '/mantenimiento/configuracion', label: 'Configuración', icon: Settings },
    ],
  },

  { to: '/contratos',    label: 'Contratos',      icon: FileText },

  {
    key: 'cobranza', label: 'Cobranza', icon: DollarSign,
    items: [
      { to: '/cxc',          label: 'CxC / Cargos',  icon: CreditCard },
      { to: '/pagos',        label: 'Pagos',          icon: DollarSign },
      { to: '/caja',         label: 'Sesión de Caja', icon: Landmark },
      { to: '/adelantos',    label: 'Adelantos',      icon: Wallet },
      { to: '/cobranza/informes', label: 'Informes',  icon: BarChart2 },
      { to: '/cobranza/reporte-cxc', label: 'Reporte General CxC', icon: FileText },
    ],
  },

  {
    key: 'liquidaciones', label: 'Liquidaciones', icon: Receipt,
    items: [
      { to: '/liquidaciones/generar',  label: 'Generar Liquidación', icon: Receipt },
      { to: '/liquidaciones/consulta', label: 'Consultar',           icon: ListChecks },
      { to: '/liquidaciones/reportes', label: 'Reportes',            icon: BarChart2 },
      { to: '/liquidaciones/beneficios', label: 'Beneficios y Comisiones', icon: TrendingUp },
      { to: '/liquidaciones/otros-ingresos', label: 'Liquidar Otros Ingresos', icon: Coins },
    ],
  },

]

function NavItemLink({ to, label, icon: Icon, indent = false, onClick }: NavItem & { indent?: boolean; onClick: () => void }) {
  return (
    <NavLink
      to={to}
      end={to === '/'}
      onClick={onClick}
      className={({ isActive }) => cn(
        'flex items-center gap-3 py-2 rounded-lg text-sm transition-colors',
        indent ? 'px-3 mx-2 pl-8' : 'px-4 mx-2',
        isActive
          ? 'bg-white/15 text-white font-medium'
          : 'text-blue-100/70 hover:bg-white/10 hover:text-white'
      )}
    >
      <Icon size={15} />
      <span>{label}</span>
    </NavLink>
  )
}

function NavGroupSection({ group, closeSidebar }: { group: NavGroup; closeSidebar: () => void }) {
  const [expanded, setExpanded] = useState(false)
  const Icon = group.icon

  return (
    <div>
      <button
        onClick={() => setExpanded(v => !v)}
        className="w-full flex items-center gap-3 px-4 mx-0 py-2.5 text-sm text-blue-100/70 hover:bg-white/10 hover:text-white transition-colors rounded-lg mx-2"
        style={{ width: 'calc(100% - 1rem)', marginLeft: '0.5rem', marginRight: '0.5rem' }}
      >
        <Icon size={16} />
        <span className="flex-1 text-left">{group.label}</span>
        <ChevronDown size={14} className={cn('transition-transform', expanded && 'rotate-180')} />
      </button>

      {expanded && (
        <div className="mt-0.5 mb-1">
          {group.items.map(item => (
            <NavItemLink key={item.to} {...item} indent onClick={closeSidebar} />
          ))}
        </div>
      )}
    </div>
  )
}

export default function Layout() {
  const { nombre, rol, logout } = useAuthStore()
  const empresa = useEmpresaStore(s => s.config)
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [apiOnline, setApiOnline] = useState<boolean | null>(null)

  useEffect(() => {
    useEmpresaStore.getState().loadFromApi()
  }, [])

  useEffect(() => {
    const check = () => {
      fetch('/api/procesos/estado', {
        headers: { Authorization: `Bearer ${useAuthStore.getState().token}` }
      }).then(() => setApiOnline(true)).catch(() => setApiOnline(false))
    }
    check()
    const t = setInterval(check, 15000)
    return () => clearInterval(t)
  }, [])

  const close = () => setOpen(false)
  const handleLogout = () => { logout(); navigate('/login') }

  return (
    <div className="flex h-screen overflow-hidden">
      {open && (
        <div className="fixed inset-0 bg-black/40 z-20 lg:hidden" onClick={close} />
      )}

      <aside
        className={cn(
          'fixed top-0 left-0 h-full w-60 z-30 flex flex-col transition-transform duration-200 lg:static lg:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full'
        )}
        style={{ background: BRAND_DARK }}
      >
        {/* Logo */}
        <div className="flex items-center gap-3 px-4 py-4 border-b border-white/10">
          {empresa.logo
            ? <img src={empresa.logo} alt="Logo" className="w-8 h-8 rounded-lg object-contain bg-white p-0.5" />
            : <div className="w-8 h-8 rounded-lg flex items-center justify-center shrink-0" style={{ background: BRAND_BLUE }}>
                <Building2 size={18} className="text-white" />
              </div>}
          <div className="min-w-0">
            <p className="text-white font-semibold text-sm leading-tight truncate">{empresa.nombre}</p>
            <p className="text-blue-200 text-xs">Rep. Dominicana</p>
          </div>
          <button onClick={close} className="ml-auto text-white/60 lg:hidden">
            <X size={18} />
          </button>
        </div>

        {/* Nav */}
        <nav className="flex-1 py-3 overflow-y-auto space-y-0.5">
          {NAV.map(entry =>
            isGroup(entry)
              ? <NavGroupSection key={entry.key} group={entry} closeSidebar={close} />
              : <NavItemLink key={entry.to} {...entry} onClick={close} />
          )}
        </nav>

        {/* Version badge */}
        <div className="px-4 pb-1 pt-0.5">
          <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-semibold"
            style={{ background: 'rgba(27,79,140,0.6)', color: '#7dd3fc', border: '1px solid rgba(125,211,252,0.25)' }}>
            <span className={`w-1.5 h-1.5 rounded-full inline-block ${
              apiOnline === null ? 'bg-sky-400 animate-pulse' :
              apiOnline ? 'bg-green-400' : 'bg-red-400 animate-pulse'
            }`} />
            v6.0 · {apiOnline === null ? 'Verificando...' : apiOnline ? 'API Conectado' : 'API Sin conexión'}
          </span>
        </div>

        {/* User */}
        <div className="border-t border-white/10 p-3">
          <div className="flex items-center gap-2 px-2 py-1.5 rounded-lg hover:bg-white/10 transition-colors">
            <div className="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold text-white"
              style={{ background: BRAND_BLUE }}>
              {nombre?.[0] ?? 'U'}
            </div>
            <div className="flex-1 min-w-0">
              <p className="text-white text-xs font-medium truncate">{nombre}</p>
              <p className="text-blue-200 text-xs truncate">{rol}</p>
            </div>
            <button onClick={handleLogout} className="text-blue-200 hover:text-red-300 transition-colors" title="Cerrar sesión">
              <LogOut size={14} />
            </button>
          </div>
        </div>
      </aside>

      {/* Main */}
      <div className="flex-1 flex flex-col overflow-hidden min-w-0">
        <header className="flex items-center gap-3 px-4 py-3 bg-white border-b border-slate-200 shrink-0">
          <button onClick={() => setOpen(true)} className="lg:hidden p-1 rounded text-slate-600 hover:bg-slate-100">
            <Menu size={20} />
          </button>
          <div className="flex-1" />
          <button onClick={() => window.print()}
            className="no-print flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50"
            title="Imprimir página actual">
            <Printer size={15} /> Imprimir
          </button>
          <div className="flex items-center gap-1 text-sm text-slate-500 ml-2">
            <div className="w-2 h-2 rounded-full bg-green-500" />
            <span>Conectado</span>
          </div>
        </header>

        <main className="flex-1 overflow-y-auto p-4 lg:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
