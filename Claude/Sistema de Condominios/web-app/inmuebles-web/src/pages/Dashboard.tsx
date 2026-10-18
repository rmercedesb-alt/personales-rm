import { useEffect, useState } from 'react'
import { TrendingUp, Building2, AlertTriangle, DollarSign, CheckCircle, Clock, BarChart3 } from 'lucide-react'
import api, { type DashboardKPIs } from '../lib/api'
import { formatCurrency } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

export default function Dashboard() {
  const { nombre } = useAuthStore()
  const [kpis, setKpis] = useState<DashboardKPIs | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    api.get<{ data: DashboardKPIs }>('/dashboard')
      .then(r => setKpis(r.data.data))
      .finally(() => setLoading(false))
  }, [])

  if (loading) return (
    <div className="flex items-center justify-center h-48">
      <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>
  )

  const today = new Date().toLocaleDateString('es-DO', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })

  return (
    <div className="space-y-6 max-w-7xl">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Buen día, {nombre?.split(' ')[0]} 👋</h1>
        <p className="text-slate-500 text-sm capitalize mt-0.5">{today}</p>
      </div>

      {/* KPI cards */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <KPICard icon={DollarSign} label="CxC del mes" value={formatCurrency(kpis?.cxCDelMes ?? 0)} color="blue" />
        <KPICard icon={CheckCircle} label="Cobrado mes" value={formatCurrency(kpis?.cobradoMes ?? 0)} color="green" />
        <KPICard icon={Clock} label="En administración" value={formatCurrency(kpis?.enAdministracion ?? 0)} color="amber" />
        <KPICard icon={TrendingUp} label="Tasa de cobro" value={`${kpis?.tasaCobro ?? 0}%`} color="purple" />
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <KPICard icon={Building2} label="Inmuebles" value={`${kpis?.inmueblesOcupados ?? 0} / ${kpis?.inmueblesTotal ?? 0}`} sub="ocupados" color="blue" />
        <KPICard icon={AlertTriangle} label="En mora" value={String(kpis?.enMora ?? 0)} sub="contratos" color="red" />
        <KPICard icon={Clock} label="Por vencer (30d)" value={String(kpis?.contratosPorVencer ?? 0)} sub="contratos" color="amber" />
        <KPICard icon={BarChart3} label="Caja hoy" value={formatCurrency(kpis?.sesionHoy ?? 0)} color="green" />
      </div>

      {/* Alertas */}
      {(kpis?.alertas?.length ?? 0) > 0 && (
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <h2 className="text-sm font-semibold text-slate-700 mb-3 flex items-center gap-2">
            <AlertTriangle size={15} className="text-amber-500" />
            Alertas del sistema
          </h2>
          <div className="space-y-2">
            {kpis?.alertas.map((a, i) => (
              <AlertRow key={i} tipo={a.tipo} mensaje={a.mensaje} />
            ))}
          </div>
        </div>
      )}

      {/* Resumen custodia */}
      <div className="bg-white rounded-xl border border-slate-200 p-4">
        <h2 className="text-sm font-semibold text-slate-700 mb-3">Flujo de custodia</h2>
        <div className="flex items-center gap-3 flex-wrap">
          <CustodiaBox label="COBRADO" value={formatCurrency(kpis?.cobradoMes ?? 0)} color="bg-blue-50 text-blue-700 border-blue-200" />
          <span className="text-slate-300 text-lg">→</span>
          <CustodiaBox label="EN ADMINISTRACIÓN" value={formatCurrency(kpis?.enAdministracion ?? 0)} color="bg-amber-50 text-amber-700 border-amber-200" />
          <span className="text-slate-300 text-lg">→</span>
          <CustodiaBox label="LIQUIDADO" value={formatCurrency(kpis?.liquidadoMes ?? 0)} color="bg-green-50 text-green-700 border-green-200" />
        </div>
      </div>
    </div>
  )
}

function KPICard({ icon: Icon, label, value, sub, color }: {
  icon: React.ElementType; label: string; value: string; sub?: string; color: string
}) {
  const colorMap: Record<string, string> = {
    blue: 'bg-blue-50 text-blue-600', green: 'bg-green-50 text-green-600',
    amber: 'bg-amber-50 text-amber-600', purple: 'bg-purple-50 text-purple-600',
    red: 'bg-red-50 text-red-600'
  }
  return (
    <div className="bg-white rounded-xl border border-slate-200 p-4">
      <div className="flex items-start justify-between mb-3">
        <p className="text-xs text-slate-500">{label}</p>
        <div className={`w-7 h-7 rounded-lg flex items-center justify-center ${colorMap[color]}`}>
          <Icon size={14} />
        </div>
      </div>
      <p className="text-xl font-semibold text-slate-800">{value}</p>
      {sub && <p className="text-xs text-slate-400 mt-0.5">{sub}</p>}
    </div>
  )
}

function AlertRow({ tipo, mensaje }: { tipo: string; mensaje: string }) {
  const colors: Record<string, string> = {
    MORA: 'text-red-700 bg-red-50 border-red-200',
    ART1762: 'text-red-700 bg-red-50 border-red-200',
    VENCIMIENTO: 'text-amber-700 bg-amber-50 border-amber-200',
    CAJA: 'text-blue-700 bg-blue-50 border-blue-200',
  }
  return (
    <div className={`text-xs px-3 py-2 rounded-lg border ${colors[tipo] ?? 'text-slate-700 bg-slate-50 border-slate-200'}`}>
      {mensaje}
    </div>
  )
}

function CustodiaBox({ label, value, color }: { label: string; value: string; color: string }) {
  return (
    <div className={`border rounded-lg px-4 py-2.5 text-center ${color}`}>
      <p className="text-xs font-medium">{label}</p>
      <p className="text-sm font-semibold mt-0.5">{value}</p>
    </div>
  )
}
