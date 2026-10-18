import { useEffect, useMemo, useState } from 'react'
import { Search, X, Loader2, Ban, ChevronDown, ChevronRight } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import api, { type CxCCargo, type CxCPago, type Inquilino, type AbonoCuenta } from '../lib/api'
import { formatCurrency, formatDate } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

export default function Pagos() {
  const [searchParams] = useSearchParams()
  const { isAsistente, isAdmin } = useAuthStore()
  const [pagos, setPagos]           = useState<CxCPago[]>([])
  const [inquilinos, setInquilinos] = useState<Inquilino[]>([])
  const [cxcs, setCxcs]             = useState<CxCCargo[]>([])
  const [loading, setLoading]       = useState(true)
  const [search, setSearch]         = useState('')
  const [tab, setTab]               = useState<'cobrar' | 'historial'>('cobrar')
  const [expanded, setExpanded]     = useState<Set<string>>(new Set(
    searchParams.get('idInquilino') ? [searchParams.get('idInquilino')!] : []
  ))
  const [anularModal, setAnularModal] = useState<string | null>(null)

  const load = () => {
    setLoading(true)
    Promise.all([
      api.get<{ data: CxCPago[] }>('/pagos'),
      api.get<{ data: Inquilino[] }>('/inquilinos'),
      api.get<{ data: CxCCargo[] }>('/cxc'),
    ]).then(([rp, ri, rc]) => {
      setPagos(rp.data.data)
      setInquilinos(ri.data.data)
      setCxcs(rc.data.data)
    }).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const term = search.toLowerCase()
  const inquilinosFiltrados = inquilinos.filter(i =>
    !term || i.nombre.toLowerCase().includes(term) || i.cedula.includes(search) ||
    i.telefono.includes(search) || cxcs.some(c => c.idInquilino === i.id && c.id.toLowerCase().includes(term))
  )

  const cxcsPendientesPorInquilino = useMemo(() => {
    const map = new Map<string, CxCCargo[]>()
    for (const c of cxcs) {
      if (c.estado === 'PAGADO') continue
      if (!map.has(c.idInquilino)) map.set(c.idInquilino, [])
      map.get(c.idInquilino)!.push(c)
    }
    return map
  }, [cxcs])

  const inquilinosConSaldo = inquilinosFiltrados.filter(i => (cxcsPendientesPorInquilino.get(i.id)?.length ?? 0) > 0)

  const toggleExpand = (id: string) =>
    setExpanded(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })

  // Recibos agrupados (historial)
  const recibos = useMemo(() => {
    const map = new Map<string, CxCPago[]>()
    for (const p of pagos) {
      const key = p.idRecibo || p.id
      if (!map.has(key)) map.set(key, [])
      map.get(key)!.push(p)
    }
    return Array.from(map.entries())
      .map(([idRecibo, lineas]) => ({
        idRecibo, lineas,
        nombreInquilino: lineas[0].nombreInquilino,
        fechaPago: lineas[0].fechaPago,
        total: lineas.reduce((s, l) => s + l.totalPagado, 0),
        anulado: lineas.every(l => l.anulado),
        formaPago: lineas[0].formaPago,
      }))
      .filter(r => !term || r.nombreInquilino.toLowerCase().includes(term) || r.idRecibo.toLowerCase().includes(term) ||
        r.lineas.some(l => l.idCxC.toLowerCase().includes(term)))
      .sort((a, b) => +new Date(b.fechaPago) - +new Date(a.fechaPago))
  }, [pagos, term])

  return (
    <div className="space-y-4 max-w-7xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Pagos</h1>
          <p className="text-slate-500 text-sm mt-0.5">Registro de pagos por cliente</p>
        </div>
      </div>

      <div className="flex gap-2 border-b border-slate-200">
        {(['cobrar', 'historial'] as const).map(t => (
          <button key={t} onClick={() => setTab(t)}
            className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
              tab === t ? 'border-blue-600 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}>
            {t === 'cobrar' ? 'Por cobrar' : 'Historial de recibos'}
          </button>
        ))}
      </div>

      <div className="relative">
        <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
        <input value={search} onChange={e => setSearch(e.target.value)}
          placeholder="Buscar por inquilino, cédula, teléfono, ID de CxC o recibo..."
          className="w-full pl-9 pr-4 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
      </div>

      {loading ? (
        <div className="flex justify-center py-12"><div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : tab === 'cobrar' ? (
        inquilinosConSaldo.length === 0 ? (
          <div className="bg-white rounded-xl border border-slate-200 py-12 text-center text-slate-400">Sin cargos pendientes</div>
        ) : (
          <div className="space-y-3">
            {inquilinosConSaldo.map(inq => {
              const cargos = cxcsPendientesPorInquilino.get(inq.id) ?? []
              const isOpen = expanded.has(inq.id)
              const saldoTotal = cargos.reduce((s, c) => s + c.saldoPendiente, 0)
              return (
                <div key={inq.id} className="bg-white rounded-xl border border-slate-200 overflow-hidden">
                  <button onClick={() => toggleExpand(inq.id)}
                    className="w-full flex items-center justify-between px-4 py-3 hover:bg-slate-50 text-left">
                    <div className="flex items-center gap-2">
                      {isOpen ? <ChevronDown size={16} className="text-slate-400" /> : <ChevronRight size={16} className="text-slate-400" />}
                      <div>
                        <p className="font-semibold text-slate-800">{inq.nombre}</p>
                        <p className="text-xs text-slate-400">{inq.cedula} · {inq.telefono} · {cargos.length} cargo(s)</p>
                      </div>
                    </div>
                    <span className="font-semibold text-slate-800">{formatCurrency(saldoTotal)}</span>
                  </button>
                  {isOpen && (
                    <PagoCliente
                      inquilino={inq}
                      cargos={cargos}
                      preselectId={searchParams.get('idInquilino') === inq.id ? searchParams.get('idCxC') : null}
                      canPay={isAsistente()}
                      onPaid={load}
                    />
                  )}
                </div>
              )
            })}
          </div>
        )
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-x-auto">
          <table className="w-full text-sm min-w-[800px]">
            <thead className="bg-slate-50 border-b border-slate-200">
              <tr>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Recibo</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Inquilino</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Fecha</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Forma</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">CxC pagadas</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Total</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Estado</th>
                {isAdmin() && <th className="px-4 py-3 w-10" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {recibos.length === 0 ? (
                <tr><td colSpan={8} className="text-center py-10 text-slate-400">Sin pagos registrados</td></tr>
              ) : recibos.map(r => (
                <tr key={r.idRecibo} className={`hover:bg-slate-50 ${r.anulado ? 'opacity-50' : ''}`}>
                  <td className="px-4 py-3 text-xs font-mono text-slate-400">{r.idRecibo}</td>
                  <td className="px-4 py-3 font-medium text-slate-800">{r.nombreInquilino}</td>
                  <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(r.fechaPago)}</td>
                  <td className="px-4 py-3 text-slate-600 text-xs">{r.formaPago}</td>
                  <td className="px-4 py-3 text-xs text-slate-500">
                    {r.lineas.some(l => l.idCxC) ? r.lineas.map(l => l.idCxC).filter(Boolean).join(', ') : <span className="italic text-blue-600">Abono a cuenta</span>}
                    {r.lineas.some(l => l.montoAbonoAplicado > 0) && (
                      <span className="ml-1.5 px-1.5 py-0.5 rounded-full text-xs bg-blue-100 text-blue-700">
                        +{formatCurrency(r.lineas.reduce((s, l) => s + l.montoAbonoAplicado, 0))} de abono usado
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-right font-semibold">{formatCurrency(r.total)}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${
                      r.anulado ? 'bg-red-100 text-red-700' : 'bg-green-100 text-green-700'
                    }`}>{r.anulado ? 'ANULADO' : 'ACTIVO'}</span>
                  </td>
                  {isAdmin() && (
                    <td className="px-4 py-3 text-right">
                      {!r.anulado && (
                        <button onClick={() => setAnularModal(r.idRecibo)} title="Anular recibo"
                          className="p-1.5 rounded hover:bg-red-50 text-slate-400 hover:text-red-600">
                          <Ban size={14} />
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {anularModal && (
        <AnularModal idRecibo={anularModal} onClose={() => setAnularModal(null)} onDone={() => { setAnularModal(null); load() }} />
      )}
    </div>
  )
}

function PagoCliente({ inquilino, cargos, preselectId, canPay, onPaid }: {
  inquilino: Inquilino; cargos: CxCCargo[]; preselectId: string | null; canPay: boolean; onPaid: () => void
}) {
  const [seleccion, setSeleccion] = useState<Set<string>>(new Set(preselectId ? [preselectId] : []))
  const [formaPago, setFormaPago] = useState('EFECTIVO')
  const [referencia, setReferencia] = useState('')
  const [notas, setNotas] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [abonos, setAbonos] = useState<AbonoCuenta[]>([])
  const [abonosSel, setAbonosSel] = useState<Set<string>>(new Set())
  const [montoRecibido, setMontoRecibido] = useState<string | null>(null)

  useEffect(() => {
    api.get<{ data: AbonoCuenta[] }>(`/abonos?idInquilino=${inquilino.id}`)
      .then(r => setAbonos(r.data.data.filter(a => a.estado !== 'AGOTADO')))
  }, [inquilino.id])

  const toggle = (id: string) =>
    setSeleccion(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })
  const toggleAbono = (id: string) =>
    setAbonosSel(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })

  const totalSeleccionado = cargos.filter(c => seleccion.has(c.id)).reduce((s, c) => s + c.saldoPendiente, 0)
  const recibido = montoRecibido === null ? totalSeleccionado : (parseFloat(montoRecibido) || 0)
  const creditoAbonosSel = abonos.filter(a => abonosSel.has(a.id)).reduce((s, a) => s + a.saldoDisponible, 0)
  const faltaCubrir = Math.max(0, totalSeleccionado - recibido)
  const abonoUsadoEstimado = Math.min(faltaCubrir, creditoAbonosSel)
  const fondosTotales = recibido + abonoUsadoEstimado
  const excedente = Math.max(0, fondosTotales - totalSeleccionado)

  const confirmar = async () => {
    if (seleccion.size === 0 && recibido <= 0) { setError('Seleccione al menos una CxC o indique un monto a abonar'); return }
    setLoading(true); setError('')
    try {
      await api.post('/pagos/multiple', {
        idInquilino: inquilino.id, idsCxC: Array.from(seleccion),
        formaPago, referencia, notas,
        montoRecibido: recibido,
        idsAbonosAplicar: Array.from(abonosSel),
      })
      onPaid()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al registrar el pago')
    } finally { setLoading(false) }
  }

  return (
    <div className="border-t border-slate-100">
      <table className="w-full text-sm">
        <thead className="bg-slate-50">
          <tr>
            <th className="px-4 py-2 w-10" />
            <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">ID</th>
            <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">Tipo</th>
            <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">Vencimiento</th>
            <th className="text-right px-4 py-2 font-medium text-slate-600 text-xs">Saldo</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-50">
          {cargos.map(c => (
            <tr key={c.id} className="hover:bg-slate-50">
              <td className="px-4 py-2"><input type="checkbox" checked={seleccion.has(c.id)} onChange={() => toggle(c.id)} disabled={!canPay} /></td>
              <td className="px-4 py-2 text-xs font-mono text-slate-400">{c.id}</td>
              <td className="px-4 py-2 text-slate-600">{c.tipoCargo}</td>
              <td className="px-4 py-2 text-slate-500 text-xs">
                {formatDate(c.fechaVencimiento)}
                {c.diasMora > 0 && <span className="ml-1.5 px-1.5 py-0.5 rounded-full text-xs bg-red-100 text-red-700">{c.diasMora}d</span>}
              </td>
              <td className="px-4 py-2 text-right font-semibold">{formatCurrency(c.saldoPendiente)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {abonos.length > 0 && (
        <div className="px-4 py-3 bg-blue-50 border-t border-blue-100">
          <p className="text-xs font-semibold text-blue-800 mb-1.5">Abonos a cuenta disponibles (cotejar contra este pago)</p>
          <div className="flex flex-wrap gap-2">
            {abonos.map(a => (
              <label key={a.id} className={`flex items-center gap-2 px-3 py-1.5 rounded-lg border text-xs cursor-pointer ${
                abonosSel.has(a.id) ? 'border-blue-500 bg-white' : 'border-blue-200 bg-blue-50/50'
              }`}>
                <input type="checkbox" checked={abonosSel.has(a.id)} onChange={() => toggleAbono(a.id)} disabled={!canPay} />
                {a.id} · {formatCurrency(a.saldoDisponible)} disponible
              </label>
            ))}
          </div>
        </div>
      )}

      {canPay && (
        <div className="px-4 py-3 bg-slate-50 border-t border-slate-100 flex flex-wrap items-end gap-3">
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Forma de pago</label>
            <select value={formaPago} onChange={e => setFormaPago(e.target.value)}
              className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm">
              {['EFECTIVO', 'TRANSFERENCIA', 'TARJETA', 'CHEQUE'].map(f => <option key={f}>{f}</option>)}
            </select>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Referencia</label>
            <input value={referencia} onChange={e => setReferencia(e.target.value)}
              className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-40" />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Monto recibido (RD$)</label>
            <input type="number" min={0} step="0.01"
              value={montoRecibido === null ? totalSeleccionado.toFixed(2) : montoRecibido}
              onChange={e => setMontoRecibido(e.target.value)}
              className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-36" />
          </div>
          <div className="flex-1 min-w-[160px]">
            <label className="block text-xs font-medium text-slate-600 mb-1">Notas</label>
            <input value={notas} onChange={e => setNotas(e.target.value)}
              className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-full"
              placeholder="Ej: abono a cuenta sin factura específica..." />
          </div>
          <div className="ml-auto text-right">
            <p className="text-xs text-slate-500">{seleccion.size > 0 ? 'Deuda seleccionada' : 'Sin CxC seleccionada'}</p>
            <p className="text-lg font-bold text-slate-800">{formatCurrency(totalSeleccionado)}</p>
          </div>
          <button onClick={confirmar} disabled={loading}
            className="flex items-center gap-2 px-4 py-2.5 rounded-lg text-white text-sm font-medium hover:opacity-90 disabled:opacity-50"
            style={{ background: '#1B4F8C' }}>
            {loading && <Loader2 size={14} className="animate-spin" />}
            Confirmar pago
          </button>

          {abonoUsadoEstimado > 0 && (
            <p className="w-full text-xs text-blue-700 bg-blue-50 rounded-lg px-3 py-2">
              Se aplicarán {formatCurrency(abonoUsadoEstimado)} de abono(s) a cuenta para cubrir la diferencia.
            </p>
          )}
          {excedente > 0 && (
            <p className="w-full text-xs text-amber-700 bg-amber-50 rounded-lg px-3 py-2">
              El monto recibido supera la deuda seleccionada por {formatCurrency(excedente)}. Ese excedente quedará registrado como nuevo abono a cuenta para el cliente.
            </p>
          )}
          {error && <p className="w-full text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
        </div>
      )}
    </div>
  )
}

function AnularModal({ idRecibo, onClose, onDone }: { idRecibo: string; onClose: () => void; onDone: () => void }) {
  const [motivo, setMotivo] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!motivo.trim()) { setError('Debe indicar el motivo de la anulación'); return }
    setLoading(true); setError('')
    try {
      await api.post(`/pagos/recibo/${idRecibo}/anular`, { motivoAnulacion: motivo })
      onDone()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al anular')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Anular recibo {idRecibo}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-3">
          <div className="bg-amber-50 border border-amber-200 rounded-lg px-3 py-2 text-xs text-amber-800">
            Esto devolverá el saldo a las CxC asociadas. El recibo quedará visible marcado como ANULADO y su numeración no se reutilizará.
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Motivo de la anulación *</label>
            <textarea required value={motivo} onChange={e => setMotivo(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm resize-none" rows={3}
              placeholder="Ej: Pago duplicado, error en el monto..." />
          </div>
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90 bg-red-600">
              {loading && <Loader2 size={14} className="animate-spin" />}
              Anular recibo
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
