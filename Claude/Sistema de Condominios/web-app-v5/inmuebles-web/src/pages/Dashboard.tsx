import React, { useEffect, useMemo, useState } from 'react'
import { TrendingUp, Building2, AlertTriangle, DollarSign, CheckCircle, Clock, BarChart3, ChevronRight, ChevronDown, Loader2 } from 'lucide-react'
import api, { type DashboardKPIs, type Inmueble, type MoraDetalleItem } from '../lib/api'
import { formatCurrency, formatDate } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

export default function Dashboard() {
  const { nombre } = useAuthStore()
  const [kpis, setKpis] = useState<DashboardKPIs | null>(null)
  const [loading, setLoading] = useState(true)
  const [dashError, setDashError] = useState('')
  const [inmuebles, setInmuebles] = useState<Inmueble[]>([])
  const [pieFilter, setPieFilter] = useState<'OCUPADO' | 'DISPONIBLE' | null>(null)
  const [moraPanel, setMoraPanel] = useState<'MORA' | 'ART1762' | null>(null)

  useEffect(() => {
    setLoading(true)
    api.get<{ data: DashboardKPIs }>('/dashboard')
      .then(d => setKpis(d.data.data))
      .catch(e => setDashError(e.response?.data?.message ?? 'Error al cargar el tablero'))
      .finally(() => setLoading(false))
    api.get<{ data: Inmueble[] }>('/inmuebles')
      .then(i => setInmuebles(i.data.data))
      .catch(() => {})
  }, [])

  if (loading) return (
    <div className="flex items-center justify-center h-48">
      <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>
  )

  if (dashError) return (
    <div className="space-y-4 max-w-2xl">
      <h1 className="text-2xl font-semibold text-slate-800">Tablero</h1>
      <div className="bg-red-50 border border-red-200 rounded-xl p-5">
        <p className="font-semibold text-red-700 mb-1">Error al cargar el tablero</p>
        <p className="text-sm text-red-600 font-mono">{dashError}</p>
        <p className="text-xs text-red-500 mt-3">
          Ejecuta <strong>PUBLICAR_V5.bat</strong> y reinicia el sistema con <strong>INICIAR_V5.bat</strong>.
        </p>
        <button onClick={() => { setDashError(''); setLoading(true); api.get<{ data: DashboardKPIs }>('/dashboard').then(d => setKpis(d.data.data)).catch(e => setDashError(e.response?.data?.message ?? 'Error')).finally(() => setLoading(false)) }}
          className="mt-3 px-4 py-2 rounded-lg text-white text-sm" style={{ background: '#1B4F8C' }}>
          Reintentar
        </button>
      </div>
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
        <KPICard icon={AlertTriangle} label="En mora" value={String(kpis?.enMora ?? 0)} sub={kpis?.montoEnMora ? formatCurrency(kpis.montoEnMora) : 'contratos'} color="red" />
        <KPICard icon={Clock} label="Por vencer (30d)" value={String(kpis?.contratosPorVencer ?? 0)} sub="contratos" color="amber" />
        <KPICard icon={BarChart3} label="Caja hoy" value={formatCurrency(kpis?.sesionHoy ?? 0)} color="green" />
      </div>

      {/* Alertas */}
      <div className="bg-white rounded-xl border border-slate-200 p-4"
        style={{ display: (kpis?.alertas?.length ?? 0) > 0 ? '' : 'none' }}>
        <h2 className="text-sm font-semibold text-slate-700 mb-3 flex items-center gap-2">
          <AlertTriangle size={15} className="text-amber-500" />
          Alertas del sistema
        </h2>
        <div className="space-y-2">
          {kpis?.alertas.map((a, i) => (
            <AlertRow
              key={i}
              tipo={a.tipo}
              mensaje={a.mensaje}
              monto={a.tipo === 'MORA' ? kpis.montoEnMora : a.tipo === 'ART1762' ? kpis.montoDeudaArt1762 : undefined}
              expanded={moraPanel === a.tipo}
              onClick={a.tipo === 'MORA' || a.tipo === 'ART1762'
                ? () => setMoraPanel(p => p === a.tipo ? null : a.tipo as 'MORA' | 'ART1762')
                : undefined}
            />
          ))}
        </div>
      </div>

      {/* Panel mora inline — siempre montado, sin insertBefore */}
      <MoraDetallePanel tipo={moraPanel} />

      {/* Disponibilidad de inmuebles */}
      <div className="bg-white rounded-xl border border-slate-200 p-5"
        style={{ display: (kpis?.inmueblesTotal ?? 0) > 0 ? '' : 'none' }}>
          <h2 className="text-sm font-semibold text-slate-700 mb-4">Disponibilidad de inmuebles</h2>
          <div className="flex flex-col lg:flex-row items-center gap-6">
            <InmueblesPie
              ocupados={kpis!.inmueblesOcupados}
              total={kpis!.inmueblesTotal}
              activeFilter={pieFilter}
              onSegmentClick={seg => setPieFilter(f => f === seg ? null : seg)}
            />
            <div className="flex-1 w-full">
              {pieFilter ? (
                <>
                  <p className="text-xs font-semibold text-slate-500 uppercase mb-2">
                    {pieFilter === 'OCUPADO' ? 'Inmuebles ocupados' : 'Inmuebles disponibles'}
                    <button onClick={() => setPieFilter(null)} className="ml-2 text-blue-500 normal-case font-normal hover:underline">(ver todos)</button>
                  </p>
                  <div className="space-y-1.5 max-h-64 overflow-y-auto pr-1">
                    {inmuebles.filter(i => i.estado === pieFilter).sort((a, b) => a.descripcion.localeCompare(b.descripcion, 'es')).map(i => (
                      <div key={i.id} className="flex items-center justify-between px-3 py-2 rounded-lg bg-slate-50 text-sm">
                        <div>
                          <span className="font-medium text-slate-800">{i.descripcion}</span>
                          <span className="text-xs text-slate-400 ml-2">{i.id}</span>
                        </div>
                        <span className="font-semibold text-slate-700">{formatCurrency(i.montoAlquiler)}</span>
                      </div>
                    ))}
                  </div>
                </>
              ) : (
                <div className="grid grid-cols-2 gap-3">
                  <div className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-center cursor-pointer hover:bg-amber-100 transition-colors" onClick={() => setPieFilter('OCUPADO')}>
                    <p className="text-2xl font-bold text-amber-700">{kpis!.inmueblesOcupados}</p>
                    <p className="text-xs text-amber-600 font-medium mt-0.5">Ocupados</p>
                    <p className="text-xs text-amber-500">{Math.round(kpis!.inmueblesOcupados / kpis!.inmueblesTotal * 100)}%</p>
                  </div>
                  <div className="rounded-xl border border-green-200 bg-green-50 p-3 text-center cursor-pointer hover:bg-green-100 transition-colors" onClick={() => setPieFilter('DISPONIBLE')}>
                    <p className="text-2xl font-bold text-green-700">{kpis!.inmueblesTotal - kpis!.inmueblesOcupados}</p>
                    <p className="text-xs text-green-600 font-medium mt-0.5">Disponibles</p>
                    <p className="text-xs text-green-500">{Math.round((kpis!.inmueblesTotal - kpis!.inmueblesOcupados) / kpis!.inmueblesTotal * 100)}%</p>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>

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

function AlertRow({ tipo, mensaje, monto, expanded, onClick }: {
  tipo: string; mensaje: string; monto?: number; expanded?: boolean; onClick?: () => void
}) {
  const colors: Record<string, string> = {
    MORA:        'text-red-700 bg-red-50 border-red-200 hover:bg-red-100',
    ART1762:     'text-red-700 bg-red-50 border-red-200 hover:bg-red-100',
    VENCIMIENTO: 'text-amber-700 bg-amber-50 border-amber-200',
    CAJA:        'text-blue-700 bg-blue-50 border-blue-200',
  }
  const base = `text-xs px-3 py-2 rounded-lg border ${colors[tipo] ?? 'text-slate-700 bg-slate-50 border-slate-200'}`
  if (!onClick) return <div className={base}>{mensaje}</div>
  return (
    <button onClick={onClick} className={`${base} w-full text-left flex items-center justify-between gap-3 transition-colors`}>
      <span>{mensaje}</span>
      <span className="flex items-center gap-2 shrink-0">
        <span className="font-semibold" style={{ display: monto !== undefined && monto > 0 ? '' : 'none' }}>
          {formatCurrency(monto ?? 0)}
        </span>
        {/* Ambos íconos siempre presentes — nunca ternario entre componentes distintos */}
        <ChevronDown size={13} style={{ display: expanded ? '' : 'none' }} />
        <ChevronRight size={13} style={{ display: expanded ? 'none' : '' }} />
      </span>
    </button>
  )
}

// ─── Panel mora inline ────────────────────────────────────────────────────────

function MoraDetallePanel({ tipo }: { tipo: 'MORA' | 'ART1762' | null }) {
  const [items, setItems]             = useState<MoraDetalleItem[]>([])
  const [loadingData, setLoadingData] = useState(false)
  const [error, setError]             = useState('')
  const [loadedTipo, setLoadedTipo]   = useState<string | null>(null)
  // propOpen: propietario expandido → muestra sus inquilinos
  // inqOpen:  clave prop::inq expandida → muestra sus CxC
  const [propOpen, setPropOpen] = useState<Set<string>>(new Set())
  const [inqOpen,  setInqOpen]  = useState<Set<string>>(new Set())

  useEffect(() => {
    if (!tipo) return
    if (tipo === loadedTipo) return
    setLoadingData(true)
    setError('')
    setPropOpen(new Set())
    setInqOpen(new Set())
    const endpoint = tipo === 'MORA' ? '/dashboard/mora-detalle' : '/dashboard/art1762-detalle'
    api.get<{ data: MoraDetalleItem[] }>(endpoint)
      .then(r => { setItems(r.data.data); setLoadedTipo(tipo) })
      .catch(() => setError('Error al cargar el detalle'))
      .finally(() => setLoadingData(false))
  }, [tipo, loadedTipo])

  const byProp = useMemo(() => {
    const map = new Map<string, { nombre: string; items: MoraDetalleItem[] }>()
    for (const it of items) {
      if (!map.has(it.idPropietario))
        map.set(it.idPropietario, { nombre: it.nombrePropietario, items: [] })
      map.get(it.idPropietario)!.items.push(it)
    }
    return Array.from(map.entries()).map(([id, v]) => ({ id, ...v }))
  }, [items])

  const totalDeuda  = items.reduce((s, i) => s + i.montoDeuda, 0)
  const totalMora   = items.reduce((s, i) => s + i.montoMora, 0)
  const totalGeneral = items.reduce((s, i) => s + i.totalAdeudado, 0)

  const inqKey = (idProp: string, idInq: string) => `${idProp}::${idInq}`
  const toggleProp = (id: string) =>
    setPropOpen(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })
  const toggleInq = (key: string) =>
    setInqOpen(prev => { const n = new Set(prev); n.has(key) ? n.delete(key) : n.add(key); return n })

  const titulo = tipo === 'MORA' ? 'Contratos en mora' : 'Art. 1762 — 2+ meses sin pagar'
  const showTable = !loadingData && !error && items.length > 0

  return (
    <div className="bg-white rounded-xl border border-red-200 overflow-hidden"
      style={{ display: tipo ? '' : 'none' }}>
      <div className="px-4 py-3 bg-red-50 border-b border-red-100 flex items-center gap-2 flex-wrap">
        <AlertTriangle size={14} className="text-red-500 shrink-0" />
        <span className="text-sm font-semibold text-red-700">{titulo}</span>
        <span className="px-1.5 py-0.5 rounded bg-red-200 text-red-800 text-xs font-semibold">{items.length}</span>
        <span className="text-xs text-red-400">· {byProp.length} propietario(s)</span>
        <span className="ml-auto text-xs text-red-700 font-semibold"
          style={{ display: totalGeneral > 0 ? '' : 'none' }}>
          Total: {formatCurrency(totalGeneral)}
        </span>
      </div>

      <div className="overflow-x-auto">
        {/* Spinner */}
        <div className="flex justify-center py-10" style={{ display: loadingData ? '' : 'none' }}>
          <Loader2 size={24} className="animate-spin text-blue-600" />
        </div>
        {/* Error */}
        <p className="text-sm text-red-600 text-center py-8"
          style={{ display: !loadingData && error ? '' : 'none' }}>{error}</p>
        {/* Sin registros */}
        <p className="text-sm text-slate-400 text-center py-8"
          style={{ display: !loadingData && !error && items.length === 0 && !!tipo ? '' : 'none' }}>Sin registros</p>

        {/* Tabla — nivel: propietario → inquilino → CxC */}
        <table className="w-full text-sm min-w-[700px]"
          style={{ display: showTable ? '' : 'none' }}>
          <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 font-medium">
            <tr>
              <th className="text-left px-4 py-2">Propietario / Inquilino / CxC</th>
              <th className="text-center px-3 py-2 w-20">Vto.</th>
              <th className="text-center px-3 py-2 w-14">Días</th>
              <th className="text-center px-3 py-2 w-14">Meses</th>
              <th className="text-right px-3 py-2">Deuda</th>
              <th className="text-right px-3 py-2">Mora</th>
              <th className="text-right px-4 py-2">Total adeudado</th>
            </tr>
          </thead>
          <tbody>
            {byProp.map(prop => {
              const propDeuda  = prop.items.reduce((s, i) => s + i.montoDeuda, 0)
              const propMora   = prop.items.reduce((s, i) => s + i.montoMora, 0)
              const propTotal  = prop.items.reduce((s, i) => s + i.totalAdeudado, 0)
              const isPropOpen = propOpen.has(prop.id)

              const porInquilino = new Map<string, MoraDetalleItem[]>()
              for (const it of prop.items) {
                if (!porInquilino.has(it.idInquilino)) porInquilino.set(it.idInquilino, [])
                porInquilino.get(it.idInquilino)!.push(it)
              }
              const inqList = Array.from(porInquilino.entries())

              return (
                <React.Fragment key={prop.id}>
                  {/* Fila propietario — siempre visible */}
                  <tr className="bg-blue-50 border-t border-blue-100 hover:bg-blue-100 cursor-pointer"
                    onClick={() => toggleProp(prop.id)}>
                    <td className="px-4 py-2.5 font-semibold text-slate-700">
                      <span className="flex items-center gap-2">
                        {/* íconos siempre presentes, sin ternario entre tipos distintos */}
                        <ChevronDown size={14} style={{ display: isPropOpen ? '' : 'none' }} />
                        <ChevronRight size={14} style={{ display: isPropOpen ? 'none' : '' }} />
                        {prop.nombre || prop.id}
                        <span className="px-1.5 py-0.5 rounded bg-blue-200 text-blue-800 text-xs font-semibold">
                          {prop.items.length}
                        </span>
                      </span>
                    </td>
                    <td colSpan={3} />
                    <td className="px-3 py-2.5 text-right text-slate-700 font-medium">{formatCurrency(propDeuda)}</td>
                    <td className="px-3 py-2.5 text-right text-red-600 font-medium">{formatCurrency(propMora)}</td>
                    <td className="px-4 py-2.5 text-right text-slate-800 font-bold">{formatCurrency(propTotal)}</td>
                  </tr>

                  {/* Filas inquilinos — visibles solo si propietario expandido */}
                  {inqList.map(([idInq, inqItems]) => {
                    const iqKey    = inqKey(prop.id, idInq)
                    const isIqOpen = inqOpen.has(iqKey)
                    const inqDeuda = inqItems.reduce((s, i) => s + i.montoDeuda, 0)
                    const inqMora  = inqItems.reduce((s, i) => s + i.montoMora, 0)
                    const inqTotal = inqItems.reduce((s, i) => s + i.totalAdeudado, 0)

                    return (
                      <React.Fragment key={iqKey}>
                        <tr className="border-t border-slate-100 bg-slate-50 hover:bg-slate-100 cursor-pointer"
                          style={{ display: isPropOpen ? '' : 'none' }}
                          onClick={() => toggleInq(iqKey)}>
                          <td className="px-4 py-2 pl-10">
                            <span className="flex items-center gap-2 text-slate-600">
                              <ChevronDown size={12} style={{ display: isIqOpen ? '' : 'none' }} />
                              <ChevronRight size={12} style={{ display: isIqOpen ? 'none' : '' }} />
                              {inqItems[0].nombreInquilino}
                              <span className="px-1.5 py-0.5 rounded bg-slate-200 text-slate-700 text-xs font-semibold">
                                {inqItems.length}
                              </span>
                            </span>
                          </td>
                          <td colSpan={3} />
                          <td className="px-3 py-2 text-right text-slate-600 text-xs">{formatCurrency(inqDeuda)}</td>
                          <td className="px-3 py-2 text-right text-red-500 text-xs">{formatCurrency(inqMora)}</td>
                          <td className="px-4 py-2 text-right text-slate-700 font-semibold text-xs">{formatCurrency(inqTotal)}</td>
                        </tr>

                        {/* Filas CxC — visibles solo si propietario e inquilino expandidos */}
                        {inqItems.map(it => (
                          <tr key={it.idCxC}
                            className="border-t border-slate-50 hover:bg-amber-50/40"
                            style={{ display: isPropOpen && isIqOpen ? '' : 'none' }}>
                            <td className="px-4 py-1 pl-16 text-xs text-slate-500">
                              <span className="font-mono text-slate-400 mr-2">{it.idCxC}</span>
                              {it.descripcion}
                            </td>
                            <td className="px-3 py-1 text-center text-xs text-slate-400">{formatDate(it.fechaVencimiento)}</td>
                            <td className="px-3 py-1 text-center">
                              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${
                                it.diasVencido > 60 ? 'bg-red-100 text-red-700' :
                                it.diasVencido > 30 ? 'bg-orange-100 text-orange-700' :
                                'bg-amber-100 text-amber-700'
                              }`}>{it.diasVencido}d</span>
                            </td>
                            <td className="px-3 py-1 text-center text-xs text-slate-500">{it.mesesVencidos}m</td>
                            <td className="px-3 py-1 text-right text-xs text-slate-600">{formatCurrency(it.montoDeuda)}</td>
                            <td className="px-3 py-1 text-right text-xs text-red-500">{it.montoMora > 0 ? formatCurrency(it.montoMora) : '—'}</td>
                            <td className="px-4 py-1 text-right text-xs font-semibold text-slate-700">{formatCurrency(it.totalAdeudado)}</td>
                          </tr>
                        ))}
                      </React.Fragment>
                    )
                  })}
                </React.Fragment>
              )
            })}
          </tbody>
          <tfoot className="border-t-2 border-slate-200 bg-slate-50">
            <tr>
              <td className="px-4 py-2 text-xs font-bold text-slate-600 uppercase tracking-wide">
                {byProp.length} propietario(s) · {new Set(items.map(i => i.idInquilino)).size} inquilino(s)
              </td>
              <td colSpan={3} />
              <td className="px-3 py-2 text-right text-sm font-bold text-slate-800">{formatCurrency(totalDeuda)}</td>
              <td className="px-3 py-2 text-right text-sm font-bold text-red-600">{formatCurrency(totalMora)}</td>
              <td className="px-4 py-2 text-right text-sm font-bold text-slate-900">{formatCurrency(totalGeneral)}</td>
            </tr>
          </tfoot>
        </table>
      </div>
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

function InmueblesPie({ ocupados, total, activeFilter, onSegmentClick }: {
  ocupados: number; total: number
  activeFilter: 'OCUPADO' | 'DISPONIBLE' | null
  onSegmentClick: (seg: 'OCUPADO' | 'DISPONIBLE') => void
}) {
  const disponibles = total - ocupados
  const cx = 90; const cy = 90; const R = 72; const r = 44
  const pctOcup = ocupados / total

  function arc(pct: number, offsetPct: number) {
    const start = offsetPct * 2 * Math.PI - Math.PI / 2
    const end = (offsetPct + pct) * 2 * Math.PI - Math.PI / 2
    const x1 = cx + R * Math.cos(start); const y1 = cy + R * Math.sin(start)
    const x2 = cx + R * Math.cos(end);   const y2 = cy + R * Math.sin(end)
    const xi1 = cx + r * Math.cos(end);  const yi1 = cy + r * Math.sin(end)
    const xi2 = cx + r * Math.cos(start); const yi2 = cy + r * Math.sin(start)
    const large = pct > 0.5 ? 1 : 0
    return `M ${x1} ${y1} A ${R} ${R} 0 ${large} 1 ${x2} ${y2} L ${xi1} ${yi1} A ${r} ${r} 0 ${large} 0 ${xi2} ${yi2} Z`
  }

  function labelPos(pct: number, offsetPct: number) {
    const mid = (offsetPct + pct / 2) * 2 * Math.PI - Math.PI / 2
    const lr = (R + r) / 2
    return { x: cx + lr * Math.cos(mid), y: cy + lr * Math.sin(mid) }
  }

  const lOcup = labelPos(pctOcup, 0)
  const lDisp = labelPos(1 - pctOcup, pctOcup)

  return (
    <svg width={180} height={180} className="shrink-0">
      <path d={arc(pctOcup, 0)} fill={activeFilter === 'DISPONIBLE' ? '#fde68a' : '#f59e0b'}
        className="cursor-pointer transition-all hover:opacity-80"
        onClick={() => onSegmentClick('OCUPADO')} />
      <path d={arc(1 - pctOcup, pctOcup)} fill={activeFilter === 'OCUPADO' ? '#86efac' : '#22c55e'}
        className="cursor-pointer transition-all hover:opacity-80"
        onClick={() => onSegmentClick('DISPONIBLE')} />
      <text x={lOcup.x} y={lOcup.y - 5} textAnchor="middle" className="fill-white text-xs font-bold" fontSize={11} fontWeight={700} style={{ display: pctOcup > 0.08 ? '' : 'none' }}>{ocupados}</text>
      <text x={lOcup.x} y={lOcup.y + 8} textAnchor="middle" className="fill-white" fontSize={9} style={{ display: pctOcup > 0.08 ? '' : 'none' }}>{Math.round(pctOcup * 100)}%</text>
      <text x={lDisp.x} y={lDisp.y - 5} textAnchor="middle" className="fill-white text-xs font-bold" fontSize={11} fontWeight={700} style={{ display: (1 - pctOcup) > 0.08 ? '' : 'none' }}>{disponibles}</text>
      <text x={lDisp.x} y={lDisp.y + 8} textAnchor="middle" className="fill-white" fontSize={9} style={{ display: (1 - pctOcup) > 0.08 ? '' : 'none' }}>{Math.round((1 - pctOcup) * 100)}%</text>
      <text x={cx} y={cy - 4} textAnchor="middle" fontSize={13} fontWeight={700} fill="#1e293b">{total}</text>
      <text x={cx} y={cy + 11} textAnchor="middle" fontSize={9} fill="#64748b">total</text>
    </svg>
  )
}
