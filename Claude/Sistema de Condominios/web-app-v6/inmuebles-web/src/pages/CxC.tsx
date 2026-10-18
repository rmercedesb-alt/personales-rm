import { useEffect, useMemo, useState } from 'react'
import { Search, Plus, Pencil, ChevronDown, ChevronRight, CreditCard, Ban, X } from 'lucide-react'
import { useSearchParams, useNavigate } from 'react-router-dom'
import api, { type CxCCargo } from '../lib/api'
import { formatCurrency, formatDate } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import CxCModal from '../components/modals/CxCModal'
import PrintHeader from '../components/PrintHeader'

const ESTADOS = ['PENDIENTE', 'EN_MORA', 'PAGADO_PARCIAL', 'PAGADO', 'ANULADO']
const ESTADOS_CON_SALDO = ['PENDIENTE', 'EN_MORA', 'PAGADO_PARCIAL']

export default function CxCPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { isAsistente, isAdmin } = useAuthStore()
  const [anularCxC, setAnularCxC] = useState<CxCCargo | null>(null)
  const idInmuebleFiltro = searchParams.get('idInmueble')

  const [list, setList]             = useState<CxCCargo[]>([])
  const [loading, setLoading]       = useState(true)
  const [search, setSearch]         = useState('')
  const [estados, setEstados]       = useState<string[]>(ESTADOS_CON_SALDO)
  const [estadoOpen, setEstadoOpen] = useState(false)
  const [modal, setModal]           = useState<CxCCargo | null | 'nuevo'>(null)
  const [expanded, setExpanded]     = useState<Set<string>>(new Set())

  const load = () => {
    setLoading(true)
    api.get<{ data: CxCCargo[] }>('/cxc')
      .then(r => setList(r.data.data))
      .catch(() => {})
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  const toggleEstado = (e: string) =>
    setEstados(prev => prev.includes(e) ? prev.filter(x => x !== e) : [...prev, e])

  const filtered = list.filter(c => {
    const matchSearch =
      c.nombreInquilino.toLowerCase().includes(search.toLowerCase()) ||
      c.idInmueble.includes(search) || c.id.includes(search) ||
      c.tipoCargo.toLowerCase().includes(search.toLowerCase())
    const matchInmueble = !idInmuebleFiltro || c.idInmueble === idInmuebleFiltro
    const matchEstado = estados.length === 0 || estados.includes(c.estado)
    return matchSearch && matchInmueble && matchEstado
  })

  // Agrupar por cliente (inquilino)
  const grupos = useMemo(() => {
    const map = new Map<string, { idInquilino: string; nombre: string; cargos: CxCCargo[] }>()
    for (const c of filtered) {
      if (!map.has(c.idInquilino)) map.set(c.idInquilino, { idInquilino: c.idInquilino, nombre: c.nombreInquilino, cargos: [] })
      map.get(c.idInquilino)!.cargos.push(c)
    }
    return Array.from(map.values())
      .map(g => ({
        ...g,
        cargos: g.cargos.sort((a, b) => +new Date(a.fechaVencimiento) - +new Date(b.fechaVencimiento)),
        saldoTotal: g.cargos.reduce((s, c) => s + c.saldoPendiente, 0),
        maxDiasMora: Math.max(0, ...g.cargos.map(c => c.diasMora)),
      }))
      .sort((a, b) => b.maxDiasMora - a.maxDiasMora || a.nombre.localeCompare(b.nombre))
  }, [filtered])

  const toggleExpand = (id: string) =>
    setExpanded(prev => { const n = new Set(prev); n.has(id) ? n.delete(id) : n.add(id); return n })

  const closeModal = () => setModal(null)
  const savedModal = () => { closeModal(); load() }

  const irAPagar = (idInquilino: string, idCxC: string) =>
    navigate(`/pagos?idInquilino=${idInquilino}&idCxC=${idCxC}`)

  return (
    <div className="space-y-4 max-w-7xl">
      <PrintHeader titulo="Cuentas por Cobrar"
        filtros={{ Estados: estados.join(', '), Búsqueda: search, Inmueble: idInmuebleFiltro ?? '' }} />
      <div className="flex items-center justify-between no-print">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Cuentas por Cobrar</h1>
          <p className="text-slate-500 text-sm mt-0.5">
            {grupos.length} cliente{grupos.length !== 1 ? 's' : ''} · {filtered.length} cargo{filtered.length !== 1 ? 's' : ''}
            {idInmuebleFiltro && <span className="ml-1 text-blue-600">· Inmueble: {idInmuebleFiltro}</span>}
          </p>
        </div>
        {isAsistente() && (
          <button onClick={() => setModal('nuevo')}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
            style={{ background: '#1B4F8C' }}>
            <Plus size={16} /> Nueva CxC
          </button>
        )}
      </div>

      <div className="flex gap-3 no-print">
        <div className="relative flex-1">
          <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
          <input value={search} onChange={e => setSearch(e.target.value)}
            placeholder="Buscar por inquilino, inmueble o tipo..."
            className="w-full pl-9 pr-4 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <div className="relative">
          <button type="button" onClick={() => setEstadoOpen(o => !o)}
            className="px-3 py-2.5 rounded-lg border border-slate-200 bg-white text-sm flex items-center gap-2 min-w-[180px] justify-between">
            <span>{estados.length === 0 ? 'Ningún estado' : estados.length === ESTADOS.length ? 'Todos los estados' : `${estados.length} estado(s)`}</span>
            <ChevronDown size={14} />
          </button>
          {estadoOpen && (
            <div className="absolute right-0 mt-1 z-10 bg-white border border-slate-200 rounded-lg shadow-lg p-2 w-52">
              {ESTADOS.map(e => (
                <label key={e} className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-slate-50 cursor-pointer text-sm">
                  <input type="checkbox" checked={estados.includes(e)} onChange={() => toggleEstado(e)} />
                  {e}
                </label>
              ))}
              <div className="border-t border-slate-100 mt-1 pt-1 flex justify-between text-xs">
                <button type="button" onClick={() => setEstados(ESTADOS_CON_SALDO)} className="text-blue-600">Con saldo</button>
                <button type="button" onClick={() => setEstados(ESTADOS)} className="text-blue-600">Todos</button>
              </div>
            </div>
          )}
        </div>
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : grupos.length === 0 ? (
        <div className="bg-white rounded-xl border border-slate-200 py-12 text-center text-slate-400">Sin cargos</div>
      ) : (
        <div className="space-y-3">
          {grupos.map(g => {
            const isOpen = expanded.has(g.idInquilino)
            return (
              <div key={g.idInquilino} className="bg-white rounded-xl border border-slate-200 overflow-hidden">
                <button onClick={() => toggleExpand(g.idInquilino)}
                  className="w-full flex items-center justify-between px-4 py-3 hover:bg-slate-50 text-left">
                  <div className="flex items-center gap-2">
                    {isOpen ? <ChevronDown size={16} className="text-slate-400" /> : <ChevronRight size={16} className="text-slate-400" />}
                    <div>
                      <p className="font-semibold text-slate-800">{g.nombre}</p>
                      <p className="text-xs text-slate-400">{g.cargos.length} cargo{g.cargos.length !== 1 ? 's' : ''}</p>
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    {g.maxDiasMora > 0 && (
                      <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-700">
                        {g.maxDiasMora}d vencido
                      </span>
                    )}
                    <span className="font-semibold text-slate-800">{formatCurrency(g.saldoTotal)}</span>
                    {isAsistente() && g.saldoTotal > 0 && (
                      <button onClick={(e) => { e.stopPropagation(); navigate(`/pagos?idInquilino=${g.idInquilino}`) }}
                        title="Registrar pago"
                        className="p-1.5 rounded hover:bg-blue-50 text-blue-500">
                        <CreditCard size={16} />
                      </button>
                    )}
                  </div>
                </button>
                {isOpen && (
                  <table className="w-full text-sm border-t border-slate-100">
                    <thead className="bg-slate-50">
                      <tr>
                        <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">ID</th>
                        <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">Tipo</th>
                        <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">Vencimiento</th>
                        <th className="text-right px-4 py-2 font-medium text-slate-600 text-xs">Original</th>
                        <th className="text-right px-4 py-2 font-medium text-slate-600 text-xs">Mora</th>
                        <th className="text-right px-4 py-2 font-medium text-slate-600 text-xs">Saldo</th>
                        <th className="text-left px-4 py-2 font-medium text-slate-600 text-xs">Estado</th>
                        {isAsistente() && <th className="px-4 py-2 w-16 no-print" />}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-50">
                      {g.cargos.map(c => (
                        <tr key={c.id} className={`hover:bg-slate-50 ${c.estado === 'ANULADO' ? 'opacity-50' : 'cursor-pointer'}`}
                          onClick={() => c.estado !== 'PAGADO' && c.estado !== 'ANULADO' && isAsistente() && irAPagar(c.idInquilino, c.id)}>
                          <td className="px-4 py-2.5 text-xs font-mono text-slate-400">{c.id}</td>
                          <td className="px-4 py-2.5 text-slate-600">{c.tipoCargo}</td>
                          <td className="px-4 py-2.5 text-slate-500 text-xs">
                            {formatDate(c.fechaVencimiento)}
                            {c.enGracia ? (
                              <span className="ml-1.5 px-1.5 py-0.5 rounded-full text-xs font-medium bg-amber-100 text-amber-700">en gracia ({c.diasMora}d)</span>
                            ) : c.diasMora > 0 ? (
                              <span className="ml-1.5 px-1.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-700">{c.diasMora}d vencido</span>
                            ) : (
                              <span className="ml-1.5 px-1.5 py-0.5 rounded-full text-xs font-medium bg-slate-100 text-slate-500" title="Días para el cierre del ciclo">{c.diasMora}d</span>
                            )}
                          </td>
                          <td className="px-4 py-2.5 text-right">{formatCurrency(c.montoOriginal)}</td>
                          <td className="px-4 py-2.5 text-right text-red-600">{c.montoMora > 0 ? formatCurrency(c.montoMora) : '-'}</td>
                          <td className="px-4 py-2.5 text-right font-semibold">{formatCurrency(c.saldoPendiente)}</td>
                          <td className="px-4 py-2.5">
                            <EstadoChip estado={c.estado} />
                          </td>
                          {isAsistente() && (
                            <td className="px-4 py-2.5 text-right no-print whitespace-nowrap" onClick={e => e.stopPropagation()}>
                              {c.estado !== 'PAGADO' && c.estado !== 'ANULADO' && (
                                <button onClick={() => setModal(c)} title="Ajustar CxC"
                                  className="p-1.5 rounded hover:bg-slate-100 text-slate-400 hover:text-slate-700">
                                  <Pencil size={13} />
                                </button>
                              )}
                              {isAdmin() && c.estado !== 'PAGADO' && c.estado !== 'ANULADO' && c.montoPagado === 0 && (
                                <button onClick={() => setAnularCxC(c)} title="Anular cargo"
                                  className="p-1.5 rounded hover:bg-red-50 text-slate-400 hover:text-red-600">
                                  <Ban size={13} />
                                </button>
                              )}
                            </td>
                          )}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            )
          })}
        </div>
      )}

      {modal && (
        <CxCModal
          onClose={closeModal}
          onSaved={savedModal}
          inicial={modal === 'nuevo' ? null : modal}
        />
      )}

      {anularCxC && (
        <AnularCxCModal cargo={anularCxC} onClose={() => setAnularCxC(null)} onDone={() => { setAnularCxC(null); load() }} />
      )}
    </div>
  )
}

function AnularCxCModal({ cargo, onClose, onDone }: { cargo: CxCCargo; onClose: () => void; onDone: () => void }) {
  const [motivo, setMotivo] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!motivo.trim()) { setError('Debe indicar la justificación'); return }
    setLoading(true); setError('')
    try {
      await api.post(`/cxc/${cargo.id}/anular`, { motivoAnulacion: motivo })
      onDone()
    } catch (err: any) { setError(err.response?.data?.message || 'Error al anular') }
    finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Anular cargo {cargo.id}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-3">
          <div className="bg-amber-50 border border-amber-200 rounded-lg px-3 py-2 text-xs text-amber-800">
            Anula el cargo <strong>{cargo.tipoCargo}</strong> de {cargo.nombreInquilino} por {formatCurrency(cargo.montoOriginal)}. Solo se permite si no tiene pagos aplicados. Queda en el log con su justificación.
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Justificación de la anulación *</label>
            <textarea required value={motivo} onChange={e => setMotivo(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-slate-200 text-sm resize-none" rows={3}
              placeholder="Ej: cargo generado por error, contrato cancelado..." />
          </div>
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading} className="px-4 py-2 rounded-lg text-white text-sm font-medium bg-red-600">
              {loading ? 'Anulando...' : 'Anular cargo'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

function EstadoChip({ estado }: { estado: string }) {
  const cls: Record<string, string> = {
    PENDIENTE: 'bg-slate-100 text-slate-600',
    EN_MORA: 'bg-red-100 text-red-700',
    PAGADO_PARCIAL: 'bg-amber-100 text-amber-700',
    PAGADO: 'bg-green-100 text-green-700',
    ANULADO: 'bg-slate-200 text-slate-500',
  }
  return <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${cls[estado] ?? 'bg-slate-100 text-slate-600'}`}>{estado}</span>
}
