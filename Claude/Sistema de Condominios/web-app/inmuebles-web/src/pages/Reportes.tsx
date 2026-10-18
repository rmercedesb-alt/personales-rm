import { useEffect, useState } from 'react'
import api, { type CxCCargo, type CxCPago } from '../lib/api'
import { formatCurrency, formatDate, estadoBadgeClass } from '../lib/utils'

export default function Reportes() {
  const [tab, setTab] = useState<'cobranza' | 'morosidad'>('cobranza')
  const [pagos, setPagos] = useState<CxCPago[]>([])
  const [cxcs, setCxcs] = useState<CxCCargo[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      api.get<{ data: CxCPago[] }>('/pagos'),
      api.get<{ data: CxCCargo[] }>('/cxc')
    ]).then(([p, c]) => { setPagos(p.data.data); setCxcs(c.data.data) }).finally(() => setLoading(false))
  }, [])

  const enMora = cxcs.filter(c => c.estado === 'EN_MORA' || c.diasMora > 0)
  const cobradoTotal = pagos.reduce((s, p) => s + p.totalPagado, 0)
  const pendienteTotal = cxcs.filter(c => c.estado !== 'PAGADO').reduce((s, c) => s + c.saldoPendiente, 0)

  return (
    <div className="space-y-4 max-w-7xl">
      <div>
        <h1 className="text-2xl font-semibold text-slate-800">Reportes</h1>
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <p className="text-xs text-slate-500 mb-1">Total cobrado</p>
          <p className="text-xl font-semibold text-green-700">{formatCurrency(cobradoTotal)}</p>
        </div>
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <p className="text-xs text-slate-500 mb-1">Pendiente total</p>
          <p className="text-xl font-semibold text-red-600">{formatCurrency(pendienteTotal)}</p>
        </div>
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <p className="text-xs text-slate-500 mb-1">En mora</p>
          <p className="text-xl font-semibold text-orange-600">{enMora.length} cargos</p>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex border-b border-slate-200">
        {(['cobranza', 'morosidad'] as const).map(t => (
          <button key={t} onClick={() => setTab(t)}
            className={`px-4 py-2.5 text-sm font-medium capitalize border-b-2 transition-colors ${tab === t ? 'border-blue-600 text-blue-600' : 'border-transparent text-slate-500 hover:text-slate-700'}`}>
            {t === 'cobranza' ? 'Reporte de cobranza' : 'Reporte de morosidad'}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="flex justify-center py-12"><div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : tab === 'cobranza' ? (
        <div className="bg-white rounded-xl border border-slate-200 overflow-x-auto">
          <table className="w-full text-sm min-w-[600px]">
            <thead className="bg-slate-50 border-b border-slate-200">
              <tr>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Fecha</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Inquilino</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Total</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Forma pago</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Custodia</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {pagos.map(p => (
                <tr key={p.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(p.fechaPago)}</td>
                  <td className="px-4 py-3 font-medium">{p.nombreInquilino}</td>
                  <td className="px-4 py-3 text-right font-semibold">{formatCurrency(p.totalPagado)}</td>
                  <td className="px-4 py-3 text-slate-600 text-xs">{p.formaPago}</td>
                  <td className="px-4 py-3"><span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${estadoBadgeClass(p.estadoCustodia)}`}>{p.estadoCustodia}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-x-auto">
          <table className="w-full text-sm min-w-[600px]">
            <thead className="bg-slate-50 border-b border-slate-200">
              <tr>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Inquilino</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Vencimiento</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Días mora</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Saldo</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Mora</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {enMora.length === 0 ? (
                <tr><td colSpan={5} className="text-center py-8 text-slate-400">Sin cargos en mora</td></tr>
              ) : enMora.map(c => (
                <tr key={c.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium">{c.nombreInquilino}</td>
                  <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(c.fechaVencimiento)}</td>
                  <td className="px-4 py-3 text-right font-semibold text-red-600">{c.diasMora}</td>
                  <td className="px-4 py-3 text-right">{formatCurrency(c.saldoPendiente)}</td>
                  <td className="px-4 py-3 text-right text-orange-600">{formatCurrency(c.montoMora)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
