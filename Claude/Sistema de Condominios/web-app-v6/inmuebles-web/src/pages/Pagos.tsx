import React, { useEffect, useMemo, useState } from 'react'
import { Ban, ChevronDown, ChevronRight, Loader2, Plus, Printer, Search, X } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import api, { type CxCCargo, type CxCPago, type Inquilino, type AbonoCuenta, type SesionCaja } from '../lib/api'
import { formatCurrency, formatDate, hoyLocal } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import PrintReciboModal from '../components/modals/PrintReciboModal'

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
  const [anularModal, setAnularModal]   = useState<string | null>(null)
  const [printPagos, setPrintPagos]     = useState<CxCPago[] | null>(null)
  const [printReimpreso, setPrintReimpreso] = useState(false)
  const [sesionCaja, setSesionCaja]     = useState<SesionCaja | null>(null)
  const [showPagoModal, setShowPagoModal] = useState(false)
  const [showPagoVario, setShowPagoVario] = useState(false)

  const load = () => {
    setLoading(true)
    Promise.all([
      api.get<{ data: CxCPago[] }>('/pagos'),
      api.get<{ data: Inquilino[] }>('/inquilinos'),
      api.get<{ data: CxCCargo[] }>('/cxc'),
      api.get<{ data: SesionCaja | null }>('/caja/sesion-activa'),
    ]).then(([rp, ri, rc, rs]) => {
      setPagos(rp.data.data)
      setInquilinos(ri.data.data)
      setCxcs(rc.data.data)
      setSesionCaja(rs.data.data)
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
      if (c.estado === 'PAGADO' || c.estado === 'ANULADO') continue
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
        estadoCustodia: lineas[0].estadoCustodia,
      }))
      .filter(r => !term || r.nombreInquilino.toLowerCase().includes(term) || r.idRecibo.toLowerCase().includes(term) ||
        r.lineas.some(l => l.idCxC.toLowerCase().includes(term)))
      .sort((a, b) => +new Date(b.fechaPago) - +new Date(a.fechaPago))
  }, [pagos, term])

  const canPay = isAsistente() && !!sesionCaja

  return (
    <div className="space-y-4 max-w-7xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Pagos</h1>
          <p className="text-slate-500 text-sm mt-0.5">Registro de pagos por cliente</p>
        </div>
        <div className="flex gap-2">
          {!!sesionCaja && (
            <button onClick={() => setShowPagoVario(true)}
              className="flex items-center gap-2 px-4 py-2.5 rounded-lg text-white text-sm font-semibold hover:opacity-90 shadow-sm"
              style={{ background: '#0d9488' }}>
              <Plus size={16} /> Pago Vario
            </button>
          )}
          {canPay && (
            <button onClick={() => setShowPagoModal(true)}
              className="flex items-center gap-2 px-4 py-2.5 rounded-lg text-white text-sm font-semibold hover:opacity-90 shadow-sm"
              style={{ background: '#1B4F8C' }}>
              <Plus size={16} /> Realizar Pago
            </button>
          )}
        </div>
      </div>

      {/* Banners caja */}
      <div className="bg-amber-50 border border-amber-200 rounded-xl px-4 py-3"
        style={{ display: !loading && !sesionCaja ? '' : 'none' }}>
        <p className="text-sm font-semibold text-amber-800">La caja está cerrada</p>
        <p className="text-xs text-amber-600 mt-0.5">El administrador debe abrir la caja antes de registrar pagos. Ve a <strong>Caja</strong> para abrirla.</p>
      </div>
      <div className="bg-green-50 border border-green-200 rounded-lg px-4 py-2 flex items-center gap-3"
        style={{ display: !loading && sesionCaja ? '' : 'none' }}>
        <div className="w-2 h-2 rounded-full bg-green-500 animate-pulse shrink-0" />
        <p className="text-xs text-green-700">
          Caja abierta · <strong>{sesionCaja?.id}</strong> · Fecha de trabajo: <strong>{formatDate(sesionCaja?.fecha ?? '')}</strong>
        </p>
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
                      canPay={canPay}
                      sesionFecha={sesionCaja?.fecha?.split('T')[0]}
                      onPaid={load}
                      onPrint={ps => setPrintPagos(ps)}
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
                <th className="px-4 py-3 w-20" />
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
                    <EstadoCustodiaChip anulado={r.anulado} estado={r.estadoCustodia} />
                  </td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-1">
                      {!r.anulado && (
                        <button
                          onClick={() => { setPrintReimpreso(true); setPrintPagos(r.lineas) }}
                          title="Reimprimir recibo"
                          className="p-1.5 rounded hover:bg-blue-50 text-slate-400 hover:text-blue-600">
                          <Printer size={14} />
                        </button>
                      )}
                      {isAdmin() && !r.anulado && (
                        <button onClick={() => setAnularModal(r.idRecibo)} title="Anular recibo"
                          className="p-1.5 rounded hover:bg-red-50 text-slate-400 hover:text-red-600">
                          <Ban size={14} />
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {anularModal && (
        <AnularModal idRecibo={anularModal} onClose={() => setAnularModal(null)} onDone={() => { setAnularModal(null); load() }} />
      )}
      {printPagos && (
        <PrintReciboModal
          pagos={printPagos}
          reimpreso={printReimpreso}
          onClose={() => { setPrintPagos(null); setPrintReimpreso(false); load() }}
        />
      )}
      {showPagoModal && (
        <RealizarPagoModal
          inquilinos={inquilinos}
          cxcsPorInquilino={cxcsPendientesPorInquilino}
          sesionCaja={sesionCaja}
          onClose={() => setShowPagoModal(false)}
          onPrint={ps => { setShowPagoModal(false); setPrintReimpreso(false); setPrintPagos(ps) }}
          onPaid={() => { setShowPagoModal(false); load() }}
        />
      )}
      {showPagoVario && (
        <PagoVarioModal
          sesionCaja={sesionCaja}
          onClose={() => setShowPagoVario(false)}
          onSaved={() => { setShowPagoVario(false); load() }}
        />
      )}
    </div>
  )
}

// ─── Modal Pago Vario / Otro Ingreso ──────────────────────────────────────────
function PagoVarioModal({ sesionCaja, onClose, onSaved }: {
  sesionCaja: SesionCaja | null
  onClose: () => void
  onSaved: () => void
}) {
  const [form, setForm] = useState({ nombre: '', cedula: '', concepto: '', monto: '', formaPago: 'EFECTIVO' })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm(f => ({ ...f, [k]: e.target.value }))

  const guardar = async () => {
    if (!form.nombre.trim()) { setError('Indique el nombre de la persona'); return }
    if (!(parseFloat(form.monto) > 0)) { setError('Indique un monto válido'); return }
    if (!form.concepto.trim()) { setError('Indique el concepto del depósito'); return }
    setLoading(true); setError('')
    try {
      await api.post('/pagos/otro-ingreso', {
        nombre: form.nombre, cedula: form.cedula, concepto: form.concepto,
        monto: parseFloat(form.monto), formaPago: form.formaPago,
      })
      onSaved()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al registrar el pago vario')
    } finally { setLoading(false) }
  }

  const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500'
  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Pago Vario (Otro Ingreso)</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <div className="p-5 space-y-3">
          {sesionCaja && (
            <p className="text-xs text-teal-700 bg-teal-50 rounded-lg px-3 py-2">
              Entra a caja <strong>{sesionCaja.id}</strong>. Al cerrar la caja pasa a administración.
            </p>
          )}
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Nombre de la persona *</label>
            <input className={inp} value={form.nombre} onChange={set('nombre')} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Cédula</label>
              <input className={inp} value={form.cedula} onChange={set('cedula')} placeholder="000-0000000-0" />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Monto *</label>
              <input type="number" min={0} step="0.01" className={inp} value={form.monto} onChange={set('monto')} />
            </div>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Concepto del depósito *</label>
            <input className={inp} value={form.concepto} onChange={set('concepto')} placeholder="Ej: Depósito de garantía, pago de servicio..." />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Forma de pago</label>
            <select className={inp} value={form.formaPago} onChange={set('formaPago')}>
              <option value="EFECTIVO">Efectivo</option>
              <option value="TRANSFERENCIA">Transferencia</option>
              <option value="CHEQUE">Cheque</option>
              <option value="TARJETA">Tarjeta</option>
            </select>
          </div>
          <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2" style={{ display: error ? '' : 'none' }}>{error}</p>
          <div className="flex justify-end gap-2 pt-1">
            <button onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button onClick={guardar} disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium disabled:opacity-50"
              style={{ background: '#0d9488' }}>
              <Loader2 size={14} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              Registrar
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

// ─── Modal Realizar Pago ──────────────────────────────────────────────────────

function RealizarPagoModal({ inquilinos, cxcsPorInquilino, sesionCaja, onClose, onPrint, onPaid }: {
  inquilinos: Inquilino[]
  cxcsPorInquilino: Map<string, CxCCargo[]>
  sesionCaja: SesionCaja | null
  onClose: () => void
  onPrint: (pagos: CxCPago[]) => void
  onPaid: () => void
}) {
  const [search, setSearch]           = useState('')
  const [inqSel, setInqSel]           = useState<Inquilino | null>(null)
  const [showDropdown, setShowDropdown] = useState(false)

  const term = search.toLowerCase()
  const filtrados = useMemo(() => {
    if (!term) return inquilinos
    return inquilinos.filter(i =>
      i.nombre.toLowerCase().includes(term) ||
      i.cedula.includes(search) ||
      i.telefono.includes(search) ||
      i.email.toLowerCase().includes(term)
    )
  }, [inquilinos, search])

  const seleccionar = (i: Inquilino) => {
    setInqSel(i)
    setSearch(i.nombre)
    setShowDropdown(false)
  }

  const cargos = inqSel ? (cxcsPorInquilino.get(inqSel.id) ?? []) : []

  return (
    <div className="fixed inset-0 bg-black/50 z-50 flex items-start justify-center p-4 pt-12">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-3xl max-h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <div>
            <h2 className="font-semibold text-slate-800 text-lg">Realizar Pago</h2>
            {sesionCaja && (
              <p className="text-xs text-slate-400 mt-0.5">
                Caja: <strong>{sesionCaja.id}</strong> · Fecha: <strong>{formatDate(sesionCaja.fecha)}</strong>
              </p>
            )}
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 p-1">
            <X size={20} />
          </button>
        </div>

        {/* Buscador inquilino */}
        <div className="px-5 py-4 border-b border-slate-100">
          <label className="block text-xs font-medium text-slate-600 mb-1.5">
            Buscar inquilino (nombre, cédula, teléfono, email)
          </label>
          <div className="relative">
            <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              autoFocus
              value={search}
              onChange={e => { setSearch(e.target.value); setInqSel(null); setShowDropdown(true) }}
              onFocus={() => setShowDropdown(true)}
              placeholder="Ej: Juan Pérez, 001-1234567-8, 809-555-1234..."
              className="w-full pl-9 pr-9 py-2.5 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            {inqSel && (
              <button onClick={() => { setInqSel(null); setSearch(''); setShowDropdown(false) }}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600">
                <X size={14} />
              </button>
            )}
          </div>

          {showDropdown && search && !inqSel && (
            <div className="absolute z-20 mt-1 bg-white border border-slate-200 rounded-xl shadow-lg max-h-52 overflow-y-auto w-[calc(100%-2.5rem)]">
              {filtrados.length === 0 ? (
                <p className="px-4 py-3 text-sm text-slate-400">Sin resultados para "{search}"</p>
              ) : filtrados.slice(0, 12).map(i => (
                <button key={i.id} onClick={() => seleccionar(i)}
                  className="w-full text-left px-4 py-2.5 hover:bg-slate-50 flex items-center justify-between">
                  <div>
                    <p className="font-medium text-slate-800 text-sm">{i.nombre}</p>
                    <p className="text-xs text-slate-400">{i.cedula} · {i.telefono}</p>
                  </div>
                  {(cxcsPorInquilino.get(i.id)?.length ?? 0) > 0 && (
                    <span className="text-xs px-2 py-0.5 rounded-full bg-amber-100 text-amber-700 font-medium">
                      {cxcsPorInquilino.get(i.id)!.length} cargo(s)
                    </span>
                  )}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Formulario de pago */}
        <div className="flex-1 overflow-y-auto">
          {!inqSel ? (
            <div className="flex flex-col items-center justify-center py-16 text-slate-400">
              <Search size={32} className="mb-2 opacity-30" />
              <p className="text-sm">Busca y selecciona un inquilino para ver sus cargos</p>
            </div>
          ) : (
            <PagoCliente
              inquilino={inqSel}
              cargos={cargos}
              preselectId={null}
              canPay={!!sesionCaja}
              sesionFecha={sesionCaja?.fecha?.split('T')[0]}
              onPaid={onPaid}
              onPrint={onPrint}
              inModal
            />
          )}
        </div>
      </div>
    </div>
  )
}

// ─── PagoCliente ──────────────────────────────────────────────────────────────

function PagoCliente({ inquilino, cargos, preselectId, canPay, sesionFecha, onPaid, onPrint, inModal }: {
  inquilino: Inquilino; cargos: CxCCargo[]; preselectId: string | null
  canPay: boolean; sesionFecha?: string; onPaid: () => void
  onPrint: (pagos: CxCPago[]) => void; inModal?: boolean
}) {
  const hoy = hoyLocal()
  const [seleccion, setSeleccion]         = useState<Set<string>>(new Set(preselectId ? [preselectId] : []))
  const [descuentosMora, setDescuentosMora] = useState<Record<string, string>>({})
  const [formaPago, setFormaPago]         = useState('EFECTIVO')
  const [referencia, setReferencia]       = useState('')
  const [notas, setNotas]                 = useState('')
  const [fechaTransaccion, setFechaTx]    = useState(sesionFecha ?? hoy)
  const [loading, setLoading]             = useState(false)
  const [error, setError]                 = useState('')
  const [abonos, setAbonos]               = useState<AbonoCuenta[]>([])
  const [abonosSel, setAbonosSel]         = useState<Set<string>>(new Set())
  const [montoRecibido, setMontoRecibido] = useState<string | null>(null)
  useEffect(() => {
    api.get<{ data: AbonoCuenta[] }>(`/abonos?idInquilino=${inquilino.id}`)
      .then(r => setAbonos(r.data.data.filter(a => a.estado !== 'AGOTADO')))
  }, [inquilino.id])

  const toggle = (id: string) => {
    setSeleccion(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })
    setDescuentosMora(prev => { const n = { ...prev }; delete n[id]; return n })
    setMontoRecibido(null)
  }
  const toggleAbono = (id: string) =>
    setAbonosSel(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })

  const cargosSeleccionados = cargos.filter(c => seleccion.has(c.id))

  // Totales desglosados
  const totalAlquiler = cargosSeleccionados.reduce((s, c) => s + Math.max(0, c.montoOriginal - c.montoPagado), 0)
  const totalMoraOriginal = cargosSeleccionados.reduce((s, c) => s + c.montoMora, 0)
  const totalDescuentos = cargosSeleccionados.reduce((s, c) => {
    const d = Math.min(c.montoMora, Math.max(0, parseFloat(descuentosMora[c.id] ?? '0') || 0))
    return s + d
  }, 0)
  const totalMoraEfectiva = totalMoraOriginal - totalDescuentos
  const totalRecibo = totalAlquiler + totalMoraEfectiva

  const recibido = montoRecibido === null ? totalRecibo : (parseFloat(montoRecibido) || 0)
  const creditoAbonosSel = abonos.filter(a => abonosSel.has(a.id)).reduce((s, a) => s + a.saldoDisponible, 0)
  const faltaCubrir = Math.max(0, totalRecibo - recibido)
  const abonoUsadoEstimado = Math.min(faltaCubrir, creditoAbonosSel)
  const fondosTotales = recibido + abonoUsadoEstimado
  const excedente = Math.max(0, fondosTotales - totalRecibo)
  const saldoRestante = Math.max(0, totalRecibo - fondosTotales)

  const confirmar = async () => {
    if (seleccion.size === 0 && recibido <= 0) { setError('Seleccione al menos una CxC o indique un monto a abonar'); return }
    setLoading(true); setError('')
    try {
      // Construir mapa de descuentos (solo los que tienen valor > 0)
      const descMap: Record<string, number> = {}
      for (const c of cargosSeleccionados) {
        const d = Math.min(c.montoMora, Math.max(0, parseFloat(descuentosMora[c.id] ?? '0') || 0))
        if (d > 0) descMap[c.id] = d
      }
      const res = await api.post<{ data: CxCPago[] }>('/pagos/multiple', {
        idInquilino: inquilino.id,
        idsCxC: Array.from(seleccion),
        formaPago, referencia, notas,
        montoRecibido: recibido,
        idsAbonosAplicar: Array.from(abonosSel),
        fechaTransaccion,
        descuentosMora: Object.keys(descMap).length > 0 ? descMap : undefined,
      })
      onPaid()
      onPrint(res.data.data)
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al registrar el pago')
    } finally { setLoading(false) }
  }

  const wrapCls = inModal ? '' : 'border-t border-slate-100'

  const inpDesc = 'w-28 px-2 py-1 rounded border border-red-200 bg-white text-xs text-right focus:outline-none focus:ring-1 focus:ring-red-400'

  return (
    <div className={wrapCls}>
      {/* Info inquilino en modal */}
      {inModal && (
        <div className="px-5 py-3 bg-blue-50 border-b border-blue-100">
          <p className="font-semibold text-slate-800">{inquilino.nombre}</p>
          <p className="text-xs text-slate-500">{inquilino.cedula} · {inquilino.telefono}
            {cargos.length === 0 && <span className="ml-2 text-green-600 font-medium">Sin cargos pendientes</span>}
            {cargos.length > 0 && <span className="ml-2 text-amber-600">{cargos.length} cargo(s) pendiente(s)</span>}
          </p>
        </div>
      )}

      {cargos.length === 0 && !inModal ? null : (
        <table className="w-full text-sm">
          <thead className="bg-slate-50">
            <tr>
              <th className="px-4 py-2 w-10" />
              <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">ID CxC</th>
              <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">Tipo / Descripción</th>
              <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">Vencimiento</th>
              <th className="text-right px-4 py-2 font-medium text-slate-600 text-xs">Saldo</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-50">
            {cargos.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-4 py-4 text-center text-xs text-slate-400">
                  Sin cargos pendientes — el pago se registrará como abono/adelanto a alquiler
                </td>
              </tr>
            ) : cargos.map(c => {
              const saldoAlq = Math.max(0, c.montoOriginal - c.montoPagado)
              const descVal  = Math.min(c.montoMora, Math.max(0, parseFloat(descuentosMora[c.id] ?? '0') || 0))
              const moraEfec = c.montoMora - descVal
              const isSel    = seleccion.has(c.id)
              return (
                <React.Fragment key={c.id}>
                  <tr className="hover:bg-slate-50">
                    <td className="px-4 py-2.5">
                      <input type="checkbox" checked={isSel} onChange={() => toggle(c.id)} disabled={!canPay} />
                    </td>
                    <td className="px-4 py-2.5 text-xs font-mono text-slate-400">{c.id}</td>
                    <td className="px-4 py-2.5 text-slate-700 font-medium">
                      {c.tipoCargo || 'ALQUILER'}{c.descripcion ? ` — ${c.descripcion}` : ''}
                    </td>
                    <td className="px-4 py-2.5 text-slate-500 text-xs">{formatDate(c.fechaVencimiento)}</td>
                    <td className="px-4 py-2.5 text-right font-semibold">{formatCurrency(saldoAlq)}</td>
                  </tr>
                  {c.montoMora > 0 && isSel && (
                    <tr className="bg-red-50/70 border-t-0">
                      <td className="pl-8 pr-2 py-2 text-red-300 text-base leading-none">↳</td>
                      <td className="px-2 py-2">
                        <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-xs font-semibold bg-red-100 text-red-700">
                          MORA · {c.diasMora}d
                        </span>
                      </td>
                      <td className="px-2 py-2">
                        <div className="flex items-center gap-2" style={{ display: canPay ? 'flex' : 'none' }}>
                          <span className="text-xs text-red-600 whitespace-nowrap">Descuento:</span>
                          <input
                            type="number" min={0} max={c.montoMora} step="0.01"
                            value={descuentosMora[c.id] ?? '0'}
                            onChange={e => {
                              setDescuentosMora(p => ({ ...p, [c.id]: e.target.value }))
                              setMontoRecibido(null)
                            }}
                            className={inpDesc}
                          />
                          <span className="text-xs text-red-400">/ {formatCurrency(c.montoMora)}</span>
                        </div>
                      </td>
                      <td className="px-2 py-2" />
                      <td className="px-4 py-2 text-right">
                        <span className="font-semibold text-red-700">{formatCurrency(moraEfec)}</span>
                        <span className="block text-xs text-red-300 line-through"
                          style={{ display: descVal > 0 ? '' : 'none' }}>
                          {formatCurrency(c.montoMora)}
                        </span>
                      </td>
                    </tr>
                  )}
                </React.Fragment>
              )
            })}
          </tbody>
        </table>
      )}

      <div className="px-4 py-3 bg-blue-50 border-t border-blue-100"
        style={{ display: abonos.length > 0 ? '' : 'none' }}>
        <p className="text-xs font-semibold text-blue-800 mb-1.5">Abonos a cuenta disponibles</p>
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

      {canPay && (
        <div className="px-4 py-3 bg-slate-50 border-t border-slate-100 space-y-3">
          {/* Desglose del recibo */}
          <div className="flex gap-6 items-start flex-wrap">
            {/* Campos de pago */}
            <div className="flex flex-wrap items-end gap-3 flex-1">
              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Fecha transacción</label>
                <input type="date" value={fechaTransaccion} onChange={e => setFechaTx(e.target.value)}
                  max={hoy}
                  className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-40" />
              </div>
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
                  className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-36" />
              </div>
              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Monto recibido (RD$)</label>
                <input type="number" min={0} step="0.01"
                  value={montoRecibido === null ? totalRecibo.toFixed(2) : montoRecibido}
                  onChange={e => setMontoRecibido(e.target.value)}
                  className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-36" />
              </div>
              <div className="flex-1 min-w-[160px]">
                <label className="block text-xs font-medium text-slate-600 mb-1">Notas</label>
                <input value={notas} onChange={e => setNotas(e.target.value)}
                  className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-full"
                  placeholder="Ej: abono sin factura específica..." />
              </div>
            </div>

            {/* Resumen del recibo */}
            <div className="bg-white border border-slate-200 rounded-xl px-4 py-3 min-w-[220px] text-sm space-y-1 shrink-0">
              <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Detalle del recibo</p>
              <div className="flex justify-between text-slate-600">
                <span>Alquiler</span>
                <span className="font-medium">{formatCurrency(totalAlquiler)}</span>
              </div>
              <div className="flex justify-between text-red-600"
                style={{ display: totalMoraOriginal > 0 ? '' : 'none' }}>
                <span>Mora</span>
                <span className="font-medium">{formatCurrency(totalMoraOriginal)}</span>
              </div>
              <div className="flex justify-between text-green-700"
                style={{ display: totalDescuentos > 0 ? '' : 'none' }}>
                <span>Desc. mora</span>
                <span className="font-medium">- {formatCurrency(totalDescuentos)}</span>
              </div>
              <div className="border-t border-slate-100 mt-1 pt-1 flex justify-between font-bold text-slate-800">
                <span>Total recibo</span>
                <span>{formatCurrency(totalRecibo)}</span>
              </div>
              <div className="flex justify-between text-slate-500 text-xs"
                style={{ display: recibido !== totalRecibo ? '' : 'none' }}>
                <span>Recibido</span>
                <span>{formatCurrency(recibido)}</span>
              </div>
              <div className="flex justify-between text-amber-700 text-xs font-medium"
                style={{ display: saldoRestante > 0 ? '' : 'none' }}>
                <span>Saldo pendiente</span>
                <span>{formatCurrency(saldoRestante)}</span>
              </div>
            </div>
          </div>

          {/* Botón confirmar */}
          <div className="flex items-center gap-3 flex-wrap">
            <button onClick={confirmar} disabled={loading}
              className="flex items-center gap-2 px-5 py-2.5 rounded-lg text-white text-sm font-medium hover:opacity-90 disabled:opacity-50"
              style={{ background: '#1B4F8C' }}>
              <Loader2 size={14} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              Confirmar pago
            </button>

            <p className="text-xs text-blue-700 bg-blue-50 rounded-lg px-3 py-2"
              style={{ display: abonoUsadoEstimado > 0 ? '' : 'none' }}>
              Se aplicarán {formatCurrency(abonoUsadoEstimado)} de abono(s) a cuenta.
            </p>
            <p className="text-xs text-amber-700 bg-amber-50 rounded-lg px-3 py-2"
              style={{ display: excedente > 0 ? '' : 'none' }}>
              Excedente de {formatCurrency(excedente)} quedará como <strong>adelanto a alquiler</strong>.
            </p>
            <p className="text-xs text-blue-700 bg-blue-50 rounded-lg px-3 py-2"
              style={{ display: cargos.length === 0 && recibido > 0 ? '' : 'none' }}>
              Sin CxC seleccionada: {formatCurrency(recibido)} se registrará como <strong>adelanto a alquiler</strong>.
            </p>
            <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
              style={{ display: error ? '' : 'none' }}>{error}</p>
          </div>
        </div>
      )}
    </div>
  )
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function EstadoCustodiaChip({ anulado, estado }: { anulado: boolean; estado: string }) {
  if (anulado) return <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-700">ANULADO</span>
  const cls: Record<string, string> = {
    COBRADO:           'bg-blue-100 text-blue-700',
    EN_ADMINISTRACION: 'bg-amber-100 text-amber-700',
    LIQUIDADO:         'bg-green-100 text-green-700',
  }
  const label: Record<string, string> = {
    COBRADO:           'COBRADO',
    EN_ADMINISTRACION: 'EN ADMIN.',
    LIQUIDADO:         'LIQUIDADO',
  }
  return (
    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${cls[estado] ?? 'bg-slate-100 text-slate-600'}`}>
      {label[estado] ?? estado}
    </span>
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
            Esto devolverá el saldo a las CxC asociadas. El recibo quedará marcado como ANULADO y su numeración no se reutilizará.
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Motivo de la anulación *</label>
            <textarea required value={motivo} onChange={e => setMotivo(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm resize-none" rows={3}
              placeholder="Ej: Pago duplicado, error en el monto..." />
          </div>
          <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
            style={{ display: error ? '' : 'none' }}>{error}</p>
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90 bg-red-600">
              <Loader2 size={14} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              Anular recibo
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
