import React, { useState } from 'react'
import { Search, ListChecks, Eye, XCircle, Printer, AlertTriangle, ChevronLeft } from 'lucide-react'
import api, { type Propietario, type LiquidacionListItem, type LiquidacionDetalle } from '../../lib/api'
import { formatCurrency, formatDate } from '../../lib/utils'
import { LiquidacionRecibo } from './GenerarLiquidacion'

const BRAND = '#1B4F8C'

export default function ConsultaLiquidaciones() {
  const [filtros, setFiltros] = useState({
    idLiq: '', idPropietario: '', nomProp: '', desde: '', hasta: '', inclAnuladas: false
  })
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [buscandoProp, setBuscandoProp] = useState(false)
  const [resultados, setResultados] = useState<LiquidacionListItem[]>([])
  const [buscando, setBuscando] = useState(false)
  const [detalle, setDetalle] = useState<LiquidacionDetalle | null>(null)
  const [cargandoDet, setCargandoDet] = useState(false)
  const [anulando, setAnulando] = useState<string | null>(null)
  const [motivoAnulacion, setMotivoAnulacion] = useState('')
  const [error, setError] = useState('')
  const [msg, setMsg] = useState('')

  const buscarPropietarios = async (q: string) => {
    if (!q.trim()) { setPropietarios([]); return }
    setBuscandoProp(true)
    try {
      const r = await api.get<{ data: Propietario[] }>('/propietarios')
      const ql = q.toLowerCase()
      setPropietarios(r.data.data.filter(p =>
        p.nombre.toLowerCase().includes(ql) || p.cedula.toLowerCase().includes(ql)
      ).slice(0, 8))
    } finally { setBuscandoProp(false) }
  }

  const seleccionarProp = (p: Propietario) => {
    setFiltros(f => ({ ...f, idPropietario: p.id, nomProp: p.nombre }))
    setPropietarios([])
  }

  const buscar = async () => {
    setBuscando(true)
    setError('')
    setResultados([])
    try {
      const params = new URLSearchParams()
      if (filtros.idLiq)          params.set('idLiq',          filtros.idLiq)
      if (filtros.idPropietario)  params.set('idPropietario',  filtros.idPropietario)
      if (filtros.desde)          params.set('desde',          filtros.desde)
      if (filtros.hasta)          params.set('hasta',          filtros.hasta)
      if (filtros.inclAnuladas)   params.set('incluirAnuladas','true')
      const r = await api.get<{ data: LiquidacionListItem[] }>(`/liquidaciones/v5/buscar?${params}`)
      setResultados(r.data.data)
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Error al buscar')
    } finally { setBuscando(false) }
  }

  const verDetalle = async (id: string) => {
    setCargandoDet(true)
    setDetalle(null)
    try {
      const r = await api.get<{ data: LiquidacionDetalle }>(`/liquidaciones/${id}/detalle`)
      setDetalle(r.data.data)
    } catch { setError('Error al cargar detalle') }
    finally { setCargandoDet(false) }
  }

  const confirmarAnulacion = async () => {
    if (!anulando || !motivoAnulacion.trim()) return
    try {
      await api.put(`/liquidaciones/${anulando}/anular`, { motivoAnulacion })
      setMsg('Liquidación anulada. Los pagos volvieron a EN_ADMINISTRACIÓN.')
      setAnulando(null)
      setMotivoAnulacion('')
      setDetalle(null)
      buscar()
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Error al anular')
    }
  }

  // ── Vista detalle ──────────────────────────────────────────────────────────
  if (cargandoDet) return (
    <div className="flex justify-center py-20">
      <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>
  )

  if (detalle) {
    return (
      <div className="max-w-4xl space-y-4">
        <div className="no-print flex items-center justify-between">
          <button onClick={() => setDetalle(null)}
            className="flex items-center gap-2 text-slate-600 hover:text-slate-800 text-sm">
            <ChevronLeft size={16} /> Volver a consulta
          </button>
          <div className="flex gap-2">
            {!detalle.liquidacion.anulado && (
              <button onClick={() => setAnulando(detalle.liquidacion.id)}
                className="flex items-center gap-2 px-3 py-1.5 rounded-lg border border-red-200 text-red-600 text-sm hover:bg-red-50">
                <XCircle size={15} /> Anular
              </button>
            )}
            <button onClick={() => window.print()}
              className="flex items-center gap-2 px-4 py-1.5 rounded-lg text-white text-sm"
              style={{ background: BRAND }}>
              <Printer size={15} /> Imprimir
            </button>
          </div>
        </div>

        <LiquidacionRecibo liquidacion={detalle} />

        {/* Modal anulación */}
        {anulando && (
          <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
            <div className="bg-white rounded-xl shadow-2xl p-6 w-full max-w-md space-y-4">
              <h3 className="font-semibold text-red-700 flex items-center gap-2">
                <AlertTriangle size={18} /> Anular Liquidación {anulando}
              </h3>
              <p className="text-sm text-slate-600">
                Al anular, todos los pagos incluidos regresarán a estado <strong>EN_ADMINISTRACIÓN</strong>.
              </p>
              <textarea
                className="w-full border border-slate-200 rounded-lg p-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-red-400"
                rows={3}
                placeholder="Motivo de anulación (requerido)"
                value={motivoAnulacion}
                onChange={e => setMotivoAnulacion(e.target.value)}
              />
              <div className="flex gap-2 justify-end">
                <button onClick={() => { setAnulando(null); setMotivoAnulacion('') }}
                  className="px-4 py-2 rounded-lg border border-slate-200 text-slate-600 text-sm hover:bg-slate-50">
                  Cancelar
                </button>
                <button onClick={confirmarAnulacion}
                  disabled={!motivoAnulacion.trim()}
                  className="px-4 py-2 rounded-lg bg-red-600 text-white text-sm disabled:opacity-50">
                  Confirmar anulación
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    )
  }

  // ── Vista búsqueda ─────────────────────────────────────────────────────────
  return (
    <div className="max-w-5xl space-y-5">
      <div className="flex items-center gap-3">
        <ListChecks size={22} style={{ color: BRAND }} />
        <h1 className="text-xl font-semibold text-slate-800">Consulta de Liquidaciones</h1>
      </div>

      {/* Filtros */}
      <div className="bg-white rounded-xl border border-slate-200 p-5 space-y-4">
        <h2 className="text-sm font-semibold text-slate-700">Filtros de búsqueda</h2>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
          <div>
            <label className="block text-xs text-slate-500 mb-1">N° Liquidación</label>
            <input
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="LIQ-0001"
              value={filtros.idLiq}
              onChange={e => setFiltros(f => ({ ...f, idLiq: e.target.value }))}
              onKeyDown={e => e.key === 'Enter' && buscar()}
            />
          </div>

          <div className="relative">
            <label className="block text-xs text-slate-500 mb-1">Propietario</label>
            <input
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="Buscar propietario..."
              value={filtros.nomProp}
              onChange={e => {
                setFiltros(f => ({ ...f, nomProp: e.target.value, idPropietario: '' }))
                buscarPropietarios(e.target.value)
              }}
            />
            {propietarios.length > 0 && (
              <div className="absolute top-full left-0 right-0 z-10 bg-white border border-slate-200 rounded-lg shadow-lg mt-1">
                {propietarios.map(p => (
                  <button key={p.id} onClick={() => seleccionarProp(p)}
                    className="w-full text-left px-3 py-2 text-sm hover:bg-blue-50 border-b border-slate-100 last:border-0">
                    <p className="font-medium text-slate-800">{p.nombre}</p>
                    <p className="text-xs text-slate-400">{p.cedula}</p>
                  </button>
                ))}
              </div>
            )}
          </div>

          <div>
            <label className="block text-xs text-slate-500 mb-1">Fecha desde</label>
            <input type="date"
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              value={filtros.desde}
              onChange={e => setFiltros(f => ({ ...f, desde: e.target.value }))}
            />
          </div>

          <div>
            <label className="block text-xs text-slate-500 mb-1">Fecha hasta</label>
            <input type="date"
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              value={filtros.hasta}
              onChange={e => setFiltros(f => ({ ...f, hasta: e.target.value }))}
            />
          </div>

          <div className="flex items-end">
            <label className="flex items-center gap-2 cursor-pointer text-sm text-slate-600">
              <input type="checkbox"
                checked={filtros.inclAnuladas}
                onChange={e => setFiltros(f => ({ ...f, inclAnuladas: e.target.checked }))}
                className="rounded" />
              Incluir anuladas
            </label>
          </div>
        </div>

        <div className="flex gap-2">
          <button onClick={buscar} disabled={buscando}
            className="flex items-center gap-2 px-5 py-2 rounded-lg text-white text-sm"
            style={{ background: BRAND }}>
            <Search size={15} /> {buscando ? 'Buscando...' : 'Buscar'}
          </button>
          <button onClick={() => {
            setFiltros({ idLiq: '', idPropietario: '', nomProp: '', desde: '', hasta: '', inclAnuladas: false })
            setResultados([])
          }}
            className="px-4 py-2 rounded-lg border border-slate-200 text-slate-600 text-sm hover:bg-slate-50">
            Limpiar
          </button>
        </div>
      </div>

      {error && (
        <div className="flex items-center gap-2 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-700">
          <AlertTriangle size={15} /> {error}
        </div>
      )}

      {msg && (
        <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-3 text-sm text-green-700">
          {msg}
        </div>
      )}

      {/* Resultados */}
      {resultados.length > 0 && (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
          <div className="px-5 py-3 border-b border-slate-100">
            <p className="text-sm font-semibold text-slate-700">{resultados.length} liquidación(es) encontrada(s)</p>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 font-medium">
                <tr>
                  <th className="text-left px-4 py-2.5">N° Liquidación</th>
                  <th className="text-left px-3 py-2.5">Propietario</th>
                  <th className="text-center px-3 py-2.5">Fecha</th>
                  <th className="text-center px-3 py-2.5">Pagos</th>
                  <th className="text-right px-3 py-2.5">Total cobrado</th>
                  <th className="text-right px-3 py-2.5">Comisión</th>
                  <th className="text-right px-3 py-2.5">Neto propietario</th>
                  <th className="text-center px-3 py-2.5">Estado</th>
                  <th className="px-4 py-2.5"></th>
                </tr>
              </thead>
              <tbody>
                {resultados.map(r => (
                  <tr key={r.id} className={`border-t border-slate-100 hover:bg-slate-50 ${r.anulado ? 'opacity-50' : ''}`}>
                    <td className="px-4 py-2.5 font-mono font-semibold text-blue-700">{r.id}</td>
                    <td className="px-3 py-2.5 font-medium text-slate-700">{r.nombrePropietario}</td>
                    <td className="px-3 py-2.5 text-center text-slate-500">{formatDate(r.fechaRegistro)}</td>
                    <td className="px-3 py-2.5 text-center">
                      <span className="px-2 py-0.5 rounded-full bg-slate-100 text-slate-600 text-xs">{r.cantidadPagos}</span>
                    </td>
                    <td className="px-3 py-2.5 text-right text-slate-700">{formatCurrency(r.totalCobrado)}</td>
                    <td className="px-3 py-2.5 text-right text-orange-600">{formatCurrency(r.comision)}</td>
                    <td className="px-3 py-2.5 text-right font-semibold text-green-700">{formatCurrency(r.netoPropietario)}</td>
                    <td className="px-3 py-2.5 text-center">
                      <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                        r.anulado ? 'bg-red-100 text-red-700' : 'bg-green-100 text-green-700'
                      }`}>
                        {r.anulado ? 'ANULADA' : r.estado}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-center">
                      <button onClick={() => verDetalle(r.id)}
                        className="flex items-center gap-1 text-xs text-blue-600 hover:underline font-medium mx-auto">
                        <Eye size={13} /> Ver detalle
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {resultados.length === 0 && !buscando && (
        <div className="text-center py-12 text-slate-400 text-sm">
          Utiliza los filtros para buscar liquidaciones
        </div>
      )}
    </div>
  )
}
