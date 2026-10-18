import { useEffect, useState } from 'react'
import { CheckCircle, Loader2, Printer } from 'lucide-react'
import api, { type SesionCaja, type CxCPago } from '../lib/api'
import { formatCurrency, formatDate } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import PrintHeader from '../components/PrintHeader'

export default function Caja() {
  const { isAdmin } = useAuthStore()
  const [sesion, setSesion]   = useState<SesionCaja | null>(null)
  const [pagos, setPagos]     = useState<CxCPago[]>([])
  const [loading, setLoading] = useState(true)
  const [approving, setApproving] = useState(false)

  const load = async () => {
    setLoading(true)
    try {
      const r = await api.get<{ data: SesionCaja | null }>('/caja/sesion-activa')
      const s = r.data.data
      setSesion(s)
      if (s) {
        const rp = await api.get<{ data: CxCPago[] }>(`/pagos?idSesionCaja=${s.id}`)
        setPagos(rp.data.data)
      }
    } finally { setLoading(false) }
  }
  useEffect(() => { load() }, [])

  const aprobar = async () => {
    if (!sesion) return
    setApproving(true)
    try {
      await api.post('/caja/aprobar', { idSesion: sesion.id })
      load()
    } finally { setApproving(false) }
  }

  if (loading) return (
    <div className="flex justify-center py-16">
      <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
    </div>
  )

  const byForma = (forma: string) => pagos.filter(p => p.formaPago === forma)
  const subtotal = (forma: string) => byForma(forma).reduce((s, p) => s + p.totalPagado, 0)

  return (
    <div className="space-y-4 max-w-4xl">
      <PrintHeader
        titulo="Cuadre de Caja"
        filtros={{ Fecha: new Date().toLocaleDateString('es-DO'), Sesión: sesion?.id ?? '' }}
      />

      <div className="flex items-center justify-between no-print">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Cuadre de Caja</h1>
          <p className="text-slate-500 text-sm mt-0.5">{formatDate(new Date().toISOString())}</p>
        </div>
        {sesion && (
          <button onClick={() => window.print()}
            className="no-print flex items-center gap-2 px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
            <Printer size={15} /> Imprimir
          </button>
        )}
      </div>

      {!sesion ? (
        <div className="bg-white rounded-xl border border-slate-200 p-8 text-center">
          <p className="text-slate-500">No hay sesión de caja para hoy.</p>
          <p className="text-sm text-slate-400 mt-1">Se creará automáticamente al registrar el primer pago.</p>
        </div>
      ) : (
        <div className="space-y-4">
          {/* Header de sesión */}
          <div className="bg-white rounded-xl border border-slate-200 px-5 py-4 flex items-center justify-between">
            <div>
              <p className="font-semibold text-slate-800">{sesion.id}</p>
              <p className="text-xs text-slate-400 mt-0.5">Apertura: {sesion.usuarioApertura}</p>
            </div>
            <span className={`inline-flex px-3 py-1 rounded-full text-xs font-medium ${
              sesion.estado === 'ABIERTA' ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-600'
            }`}>
              {sesion.estado}
            </span>
          </div>

          {/* Resumen por forma de pago */}
          <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
            <div className="px-5 py-3 border-b border-slate-100 bg-slate-50">
              <h2 className="text-sm font-semibold text-slate-700">Resumen por forma de pago</h2>
            </div>
            <div className="p-5 space-y-2">
              <SummaryRow label="Efectivo" value={sesion.totalEfectivo} count={byForma('EFECTIVO').length} />
              <SummaryRow label="Transferencia" value={sesion.totalTransferencia} count={byForma('TRANSFERENCIA').length} />
              <SummaryRow label="Tarjeta" value={sesion.totalTarjeta} count={byForma('TARJETA').length} />
              <SummaryRow label="Cheque" value={sesion.totalCheque} count={byForma('CHEQUE').length} />
              <div className="border-t border-slate-200 pt-3 mt-2">
                <div className="flex items-center justify-between">
                  <span className="text-sm font-bold text-slate-800">Total general</span>
                  <span className="text-sm font-bold text-slate-800">{formatCurrency(sesion.totalGeneral)}</span>
                </div>
                <div className="flex items-center justify-between mt-0.5">
                  <span className="text-xs text-slate-500">Total transacciones</span>
                  <span className="text-xs font-medium text-slate-600">{sesion.cantidadTransacciones}</span>
                </div>
              </div>
            </div>
          </div>

          {/* Detalle de transacciones */}
          {pagos.length > 0 && (
            <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
              <div className="px-5 py-3 border-b border-slate-100 bg-slate-50">
                <h2 className="text-sm font-semibold text-slate-700">
                  Detalle de transacciones ({pagos.length})
                </h2>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-sm min-w-[640px]">
                  <thead className="bg-slate-50 border-b border-slate-100">
                    <tr>
                      <th className="text-left px-4 py-2.5 font-medium text-slate-600 text-xs">ID</th>
                      <th className="text-left px-4 py-2.5 font-medium text-slate-600 text-xs">Inquilino</th>
                      <th className="text-left px-4 py-2.5 font-medium text-slate-600 text-xs">Hora</th>
                      <th className="text-left px-4 py-2.5 font-medium text-slate-600 text-xs">Forma</th>
                      <th className="text-left px-4 py-2.5 font-medium text-slate-600 text-xs">Referencia</th>
                      <th className="text-right px-4 py-2.5 font-medium text-slate-600 text-xs">Alquiler</th>
                      <th className="text-right px-4 py-2.5 font-medium text-slate-600 text-xs">Mora</th>
                      <th className="text-right px-4 py-2.5 font-medium text-slate-600 text-xs">Total</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-50">
                    {pagos.map(p => (
                      <tr key={p.id} className="hover:bg-slate-50">
                        <td className="px-4 py-2.5 text-xs font-mono text-slate-400">{p.id}</td>
                        <td className="px-4 py-2.5 font-medium text-slate-800">{p.nombreInquilino}</td>
                        <td className="px-4 py-2.5 text-slate-500 text-xs">
                          {new Date(p.fechaPago).toLocaleTimeString('es-DO', { hour: '2-digit', minute: '2-digit' })}
                        </td>
                        <td className="px-4 py-2.5">
                          <FormaChip forma={p.formaPago} />
                        </td>
                        <td className="px-4 py-2.5 text-xs text-slate-500">{p.referencia || '—'}</td>
                        <td className="px-4 py-2.5 text-right text-slate-700">{formatCurrency(p.montoAlquiler)}</td>
                        <td className="px-4 py-2.5 text-right text-orange-600">
                          {p.montoMora > 0 ? formatCurrency(p.montoMora) : '—'}
                        </td>
                        <td className="px-4 py-2.5 text-right font-semibold text-slate-800">{formatCurrency(p.totalPagado)}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="border-t-2 border-slate-200 bg-slate-50">
                    <tr>
                      <td colSpan={5} className="px-4 py-2.5 text-xs font-bold text-slate-700">TOTAL</td>
                      <td className="px-4 py-2.5 text-right text-xs font-bold text-slate-700">
                        {formatCurrency(pagos.reduce((s, p) => s + p.montoAlquiler, 0))}
                      </td>
                      <td className="px-4 py-2.5 text-right text-xs font-bold text-orange-600">
                        {formatCurrency(pagos.reduce((s, p) => s + p.montoMora, 0))}
                      </td>
                      <td className="px-4 py-2.5 text-right text-xs font-bold text-slate-800">
                        {formatCurrency(sesion.totalGeneral)}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </div>
          )}

          {/* Nota sobre adelantos */}
          <div className="bg-amber-50 border border-amber-200 rounded-lg px-4 py-3 text-xs text-amber-800 no-print">
            Los adelantos a propietarios <strong>no se deducen del cuadre de caja</strong>. Se descontarán del neto del propietario al procesar la liquidación correspondiente.
          </div>

          {/* Aprobación */}
          {isAdmin() && sesion.estado === 'ABIERTA' && (
            <div className="no-print">
              <button onClick={aprobar} disabled={approving}
                className="flex items-center gap-2 w-full justify-center py-2.5 rounded-lg text-white text-sm font-medium hover:opacity-90 disabled:opacity-60"
                style={{ background: '#1B4F8C' }}>
                {approving ? <Loader2 size={15} className="animate-spin" /> : <CheckCircle size={15} />}
                Aprobar cuadre de caja
              </button>
              <p className="text-xs text-slate-400 text-center mt-2">
                Moverá todos los pagos COBRADO → EN ADMINISTRACIÓN
              </p>
            </div>
          )}

          {/* Firma para impresión */}
          <div className="hidden print:block mt-8 pt-6 border-t border-slate-300">
            <div className="grid grid-cols-3 gap-8 text-center">
              <div>
                <div className="border-t border-slate-400 pt-2 mt-8 text-xs text-slate-600">Elaborado por</div>
              </div>
              <div>
                <div className="border-t border-slate-400 pt-2 mt-8 text-xs text-slate-600">Revisado por</div>
              </div>
              <div>
                <div className="border-t border-slate-400 pt-2 mt-8 text-xs text-slate-600">Aprobado por</div>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function SummaryRow({ label, value, count }: { label: string; value: number; count: number }) {
  return (
    <div className="flex items-center justify-between">
      <div className="flex items-center gap-2">
        <span className="text-sm text-slate-600">{label}</span>
        {count > 0 && <span className="text-xs text-slate-400">({count} transac.)</span>}
      </div>
      <span className={`text-sm font-medium ${value > 0 ? 'text-slate-800' : 'text-slate-400'}`}>
        {formatCurrency(value)}
      </span>
    </div>
  )
}

function FormaChip({ forma }: { forma: string }) {
  const cls: Record<string, string> = {
    EFECTIVO:      'bg-green-100 text-green-700',
    TRANSFERENCIA: 'bg-blue-100 text-blue-700',
    TARJETA:       'bg-purple-100 text-purple-700',
    CHEQUE:        'bg-orange-100 text-orange-700',
  }
  return (
    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${cls[forma] ?? 'bg-slate-100 text-slate-600'}`}>
      {forma}
    </span>
  )
}
