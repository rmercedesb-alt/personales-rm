import { useEffect, useState } from 'react'
import api, { type CxCCargo, type CxCPago, type Inmueble } from '../lib/api'
import { formatCurrency, formatDate, estadoBadgeClass } from '../lib/utils'

export default function Reportes() {
  const [tab, setTab] = useState<'cobranza' | 'morosidad' | 'disponibilidad'>('cobranza')
  const [pagos, setPagos] = useState<CxCPago[]>([])
  const [cxcs, setCxcs] = useState<CxCCargo[]>([])
  const [inmuebles, setInmuebles] = useState<Inmueble[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      api.get<{ data: CxCPago[] }>('/pagos'),
      api.get<{ data: CxCCargo[] }>('/cxc'),
      api.get<{ data: Inmueble[] }>('/inmuebles')
    ]).then(([p, c, i]) => { setPagos(p.data.data); setCxcs(c.data.data); setInmuebles(i.data.data) })
      .catch(() => {})
      .finally(() => setLoading(false))
  }, [])

  const enMora = cxcs.filter(c => c.estado === 'EN_MORA' || c.diasMora > 0)
  const cobradoTotal = pagos.reduce((s, p) => s + p.totalPagado, 0)
  const pendienteTotal = cxcs.filter(c => c.estado !== 'PAGADO').reduce((s, c) => s + c.saldoPendiente, 0)
  const inmueblesOcupados = inmuebles.filter(i => i.estado === 'OCUPADO').sort((a, b) => a.descripcion.localeCompare(b.descripcion, 'es'))
  const inmueblesDisponibles = inmuebles.filter(i => i.estado === 'DISPONIBLE').sort((a, b) => a.descripcion.localeCompare(b.descripcion, 'es'))

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
        {([
          ['cobranza', 'Reporte de cobranza'],
          ['morosidad', 'Reporte de morosidad'],
          ['disponibilidad', 'Disponibilidad de inmuebles']
        ] as const).map(([t, label]) => (
          <button key={t} onClick={() => setTab(t)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors ${tab === t ? 'border-blue-600 text-blue-600' : 'border-transparent text-slate-500 hover:text-slate-700'}`}>
            {label}
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
      ) : tab === 'morosidad' ? (
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
      ) : (
        <div className="space-y-4">
          {/* Resumen */}
          <div className="grid grid-cols-3 gap-4">
            <div className="bg-amber-50 border border-amber-200 rounded-xl p-4 text-center">
              <p className="text-2xl font-bold text-amber-700">{inmueblesOcupados.length}</p>
              <p className="text-xs text-amber-600 font-medium mt-0.5">Ocupados</p>
              <p className="text-xs text-amber-500">{inmuebles.length > 0 ? Math.round(inmueblesOcupados.length / inmuebles.length * 100) : 0}%</p>
            </div>
            <div className="bg-green-50 border border-green-200 rounded-xl p-4 text-center">
              <p className="text-2xl font-bold text-green-700">{inmueblesDisponibles.length}</p>
              <p className="text-xs text-green-600 font-medium mt-0.5">Disponibles</p>
              <p className="text-xs text-green-500">{inmuebles.length > 0 ? Math.round(inmueblesDisponibles.length / inmuebles.length * 100) : 0}%</p>
            </div>
            <div className="bg-slate-50 border border-slate-200 rounded-xl p-4 text-center">
              <p className="text-2xl font-bold text-slate-700">{inmuebles.length}</p>
              <p className="text-xs text-slate-500 font-medium mt-0.5">Total inmuebles</p>
            </div>
          </div>
          {/* Tablas lado a lado */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
              <div className="px-4 py-3 bg-amber-50 border-b border-amber-200">
                <h3 className="text-sm font-semibold text-amber-800">Inmuebles ocupados ({inmueblesOcupados.length})</h3>
              </div>
              <table className="w-full text-sm">
                <thead className="bg-slate-50 border-b border-slate-200">
                  <tr>
                    <th className="text-left px-3 py-2 font-medium text-slate-600 text-xs">Descripción</th>
                    <th className="text-left px-3 py-2 font-medium text-slate-600 text-xs">Sector</th>
                    <th className="text-right px-3 py-2 font-medium text-slate-600 text-xs">Precio</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {inmueblesOcupados.length === 0
                    ? <tr><td colSpan={3} className="text-center py-6 text-slate-400 text-xs">Ninguno</td></tr>
                    : inmueblesOcupados.map(i => (
                      <tr key={i.id} className="hover:bg-slate-50">
                        <td className="px-3 py-2">
                          <p className="font-medium text-slate-800 text-xs">{i.descripcion}</p>
                          <p className="text-slate-400 text-xs">{i.id}</p>
                        </td>
                        <td className="px-3 py-2 text-xs text-slate-500">{i.sector || i.ciudad}</td>
                        <td className="px-3 py-2 text-right text-xs font-semibold text-slate-700">{formatCurrency(i.montoAlquiler)}</td>
                      </tr>
                    ))
                  }
                </tbody>
              </table>
            </div>
            <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
              <div className="px-4 py-3 bg-green-50 border-b border-green-200">
                <h3 className="text-sm font-semibold text-green-800">Inmuebles disponibles ({inmueblesDisponibles.length})</h3>
              </div>
              <table className="w-full text-sm">
                <thead className="bg-slate-50 border-b border-slate-200">
                  <tr>
                    <th className="text-left px-3 py-2 font-medium text-slate-600 text-xs">Descripción</th>
                    <th className="text-left px-3 py-2 font-medium text-slate-600 text-xs">Sector</th>
                    <th className="text-right px-3 py-2 font-medium text-slate-600 text-xs">Precio</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {inmueblesDisponibles.length === 0
                    ? <tr><td colSpan={3} className="text-center py-6 text-slate-400 text-xs">Ninguno</td></tr>
                    : inmueblesDisponibles.map(i => (
                      <tr key={i.id} className="hover:bg-slate-50">
                        <td className="px-3 py-2">
                          <p className="font-medium text-slate-800 text-xs">{i.descripcion}</p>
                          <p className="text-slate-400 text-xs">{i.id}</p>
                        </td>
                        <td className="px-3 py-2 text-xs text-slate-500">{i.sector || i.ciudad}</td>
                        <td className="px-3 py-2 text-right text-xs font-semibold text-slate-700">{formatCurrency(i.montoAlquiler)}</td>
                      </tr>
                    ))
                  }
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
