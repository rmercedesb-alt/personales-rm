import { useState, useEffect } from 'react'
import { Coins, CheckSquare, Square, Printer, AlertTriangle } from 'lucide-react'
import api, { type OtroIngresoItem, type OtrosIngresosLiquidacion } from '../../lib/api'
import { formatCurrency, formatDate } from '../../lib/utils'

const BRAND = '#1B4F8C'

export default function LiquidarOtrosIngresos() {
  const [items, setItems]           = useState<OtroIngresoItem[]>([])
  const [sel, setSel]               = useState<Set<string>>(new Set())
  const [cargando, setCargando]     = useState(true)
  const [procesando, setProcesando] = useState(false)
  const [comprobante, setComprobante] = useState<OtrosIngresosLiquidacion | null>(null)
  const [error, setError]           = useState('')

  const cargar = () => {
    setCargando(true)
    api.get<{ data: OtroIngresoItem[] }>('/otros-ingresos/pendientes')
      .then(r => { setItems(r.data.data); setSel(new Set(r.data.data.map(i => i.idPago))) })
      .catch(() => setError('Error al cargar otros ingresos'))
      .finally(() => setCargando(false))
  }
  useEffect(cargar, [])

  const toggle = (id: string) => setSel(p => { const n = new Set(p); n.has(id) ? n.delete(id) : n.add(id); return n })
  const toggleTodos = () => setSel(s => s.size === items.length ? new Set() : new Set(items.map(i => i.idPago)))

  const totalSel = items.filter(i => sel.has(i.idPago)).reduce((s, i) => s + i.monto, 0)

  const liquidar = async () => {
    if (sel.size === 0) return
    if (!confirm(`¿Liquidar ${sel.size} ingreso(s) por ${formatCurrency(totalSel)}?`)) return
    setProcesando(true); setError('')
    try {
      const r = await api.post<{ data: OtrosIngresosLiquidacion }>('/otros-ingresos/liquidar',
        { idsPagos: Array.from(sel) })
      setComprobante(r.data.data)
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Error al liquidar')
    } finally { setProcesando(false) }
  }

  // ── Comprobante ─────────────────────────────────────────────────────────────
  if (comprobante) {
    return (
      <div className="max-w-3xl space-y-4">
        <div className="no-print flex items-center justify-between">
          <h1 className="text-xl font-semibold text-slate-800">Comprobante generado</h1>
          <div className="flex gap-2">
            <button onClick={() => window.print()} className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm" style={{ background: BRAND }}>
              <Printer size={15} /> Imprimir
            </button>
            <button onClick={() => { setComprobante(null); cargar() }}
              className="px-4 py-2 rounded-lg border border-slate-200 text-slate-600 text-sm hover:bg-slate-50">Volver</button>
          </div>
        </div>
        <div className="bg-white rounded-xl border border-slate-200 p-6 print:border-0">
          <div className="flex items-start justify-between border-b border-slate-200 pb-4 mb-4">
            <div>
              <h2 className="text-lg font-bold text-slate-800">LIQUIDACIÓN DE OTROS INGRESOS</h2>
              <p className="text-2xl font-bold mt-1" style={{ color: BRAND }}>#{comprobante.id}</p>
              <p className="text-xs text-slate-500 mt-1">Fecha: {formatDate(comprobante.fecha)}</p>
            </div>
            <div className="text-right">
              <p className="text-xs text-slate-500 uppercase">Total liquidado</p>
              <p className="text-xl font-bold text-green-700">{formatCurrency(comprobante.total)}</p>
            </div>
          </div>
          <table className="w-full text-sm">
            <thead><tr className="border-b-2 border-slate-300 text-xs text-slate-500 uppercase">
              <th className="text-left py-2">Fecha</th><th className="text-left py-2">Nombre</th>
              <th className="text-left py-2">Cédula</th><th className="text-left py-2">Concepto</th>
              <th className="text-right py-2">Monto</th>
            </tr></thead>
            <tbody>
              {comprobante.items.map((it, i) => (
                <tr key={it.idPago} className={`border-b border-slate-100 ${i % 2 ? 'bg-slate-50' : ''}`}>
                  <td className="py-2 text-slate-500">{formatDate(it.fecha)}</td>
                  <td className="py-2 font-medium text-slate-700">{it.nombre}</td>
                  <td className="py-2 text-slate-500">{it.cedula}</td>
                  <td className="py-2 text-slate-600">{it.concepto}</td>
                  <td className="py-2 text-right font-semibold text-green-700">{formatCurrency(it.monto)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr className="border-t-2 border-slate-300 font-bold">
              <td colSpan={4} className="py-2.5 text-slate-600 text-xs uppercase">{comprobante.items.length} ingreso(s)</td>
              <td className="py-2.5 text-right text-green-700 text-base">{formatCurrency(comprobante.total)}</td>
            </tr></tfoot>
          </table>
          <div className="flex justify-between items-end mt-10 pt-4 border-t border-slate-200">
            <div className="text-center"><div className="border-b border-slate-400 w-48 mb-1" /><p className="text-xs text-slate-500">Entregado por</p></div>
            <div className="text-center"><div className="border-b border-slate-400 w-48 mb-1" /><p className="text-xs text-slate-500">Recibido por</p></div>
          </div>
        </div>
      </div>
    )
  }

  // ── Lista ───────────────────────────────────────────────────────────────────
  return (
    <div className="max-w-4xl space-y-5">
      <div className="flex items-center gap-3">
        <Coins size={22} style={{ color: BRAND }} />
        <h1 className="text-xl font-semibold text-slate-800">Liquidar Otros Ingresos</h1>
      </div>

      {error && (
        <div className="flex items-center gap-2 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-700">
          <AlertTriangle size={15} /> {error}
        </div>
      )}

      {cargando ? (
        <div className="flex justify-center py-16"><div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : items.length === 0 ? (
        <div className="text-center py-16 text-slate-400 text-sm">No hay otros ingresos en administración pendientes de liquidar.</div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
          <div className="px-5 py-3 border-b border-slate-100 flex items-center justify-between">
            <p className="text-sm font-semibold text-slate-700">{items.length} ingreso(s) en administración</p>
            <button onClick={toggleTodos} className="text-xs text-blue-600 hover:underline font-medium">
              {sel.size === items.length ? 'Deseleccionar todos' : 'Seleccionar todos'}
            </button>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500">
                <tr>
                  <th className="px-4 py-2.5 w-10"></th>
                  <th className="text-center px-3 py-2.5">Fecha</th>
                  <th className="text-left px-3 py-2.5">Nombre</th>
                  <th className="text-left px-3 py-2.5">Cédula</th>
                  <th className="text-left px-3 py-2.5">Concepto</th>
                  <th className="text-center px-3 py-2.5">Forma</th>
                  <th className="text-right px-4 py-2.5">Monto</th>
                </tr>
              </thead>
              <tbody>
                {items.map(it => {
                  const marcado = sel.has(it.idPago)
                  return (
                    <tr key={it.idPago} onClick={() => toggle(it.idPago)}
                      className={`border-t border-slate-100 cursor-pointer ${marcado ? 'bg-blue-50 hover:bg-blue-100' : 'hover:bg-slate-50'}`}>
                      <td className="px-4 py-2.5 text-center">
                        <CheckSquare size={16} className="text-blue-600 mx-auto" style={{ display: marcado ? '' : 'none' }} />
                        <Square size={16} className="text-slate-300 mx-auto" style={{ display: marcado ? 'none' : '' }} />
                      </td>
                      <td className="px-3 py-2.5 text-center text-slate-500">{formatDate(it.fecha)}</td>
                      <td className="px-3 py-2.5 font-medium text-slate-700">{it.nombre}</td>
                      <td className="px-3 py-2.5 text-slate-500">{it.cedula}</td>
                      <td className="px-3 py-2.5 text-slate-600">{it.concepto}</td>
                      <td className="px-3 py-2.5 text-center">
                        <span className="px-2 py-0.5 rounded-full text-xs bg-slate-100 text-slate-600">{it.formaPago}</span>
                      </td>
                      <td className="px-4 py-2.5 text-right font-semibold text-slate-700">{formatCurrency(it.monto)}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div className="px-5 py-4 border-t border-slate-200 bg-slate-50 flex items-center justify-between gap-4 flex-wrap">
            <div className="flex gap-6 text-sm">
              <div><p className="text-xs text-slate-500">Seleccionados</p><p className="font-semibold text-slate-700">{sel.size} de {items.length}</p></div>
              <div><p className="text-xs text-slate-500">Total a liquidar</p><p className="font-bold text-green-700 text-base">{formatCurrency(totalSel)}</p></div>
            </div>
            <button onClick={liquidar} disabled={sel.size === 0 || procesando}
              className="flex items-center gap-2 px-6 py-2.5 rounded-lg text-white font-medium text-sm disabled:opacity-50"
              style={{ background: BRAND }}>
              <Coins size={16} /> {procesando ? 'Procesando...' : 'Liquidar seleccionados'}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
