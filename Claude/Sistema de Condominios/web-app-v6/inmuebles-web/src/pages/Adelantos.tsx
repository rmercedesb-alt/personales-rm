import { useEffect, useRef, useState } from 'react'
import { Plus, Search, Pencil, Paperclip, Ban, Wallet, Trash2, Download, FileText, X } from 'lucide-react'
import api, { type Propietario, type Adelanto, type AdelantoDocumentoInfo } from '../lib/api'
import { formatCurrency, formatDate, hoyLocal } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useSort } from '../lib/useSort'
import SortTh from '../components/SortTh'
import PrintHeader from '../components/PrintHeader'

const inp = 'w-full px-3 py-2.5 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white'
const BRAND = '#1B4F8C'

const estadoBadge = (e: string) => ({
  PENDIENTE: 'bg-amber-100 text-amber-700',
  LIQUIDADO: 'bg-green-100 text-green-700',
  PAGADO:    'bg-blue-100 text-blue-700',
  ANULADO:   'bg-slate-200 text-slate-500',
}[e] ?? 'bg-slate-100 text-slate-600')

// ─── Modal crear / editar ───────────────────────────────────────────────────
function AdelantoModal({ onClose, onSaved, propietarios, inicial }: {
  onClose: () => void; onSaved: () => void; propietarios: Propietario[]; inicial?: Adelanto | null
}) {
  const esEdicion = !!inicial
  const [form, setForm] = useState({
    idPropietario: inicial?.idPropietario ?? '', fecha: inicial?.fecha?.slice(0, 10) ?? hoyLocal(),
    concepto: inicial?.concepto ?? '', monto: inicial ? String(inicial.monto) : '',
    notas: inicial?.notas ?? '', formaPago: inicial?.formaPago ?? 'EFECTIVO',
  })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [buscarProp, setBuscarProp] = useState('')
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) =>
    setForm(f => ({ ...f, [k]: e.target.value }))

  const propietariosFiltrados = propietarios.filter(p => {
    const t = buscarProp.toLowerCase()
    return !t || p.nombre.toLowerCase().includes(t) || p.cedula.includes(buscarProp) ||
      p.telefono.includes(buscarProp) || p.email.toLowerCase().includes(t) || p.id.toLowerCase().includes(t)
  })

  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setLoading(true); setError('')
    try {
      const body = { ...form, monto: parseFloat(form.monto) }
      if (esEdicion) await api.put(`/adelantos/${inicial!.id}`, body)
      else await api.post('/adelantos', body)
      onSaved()
    } catch (err: any) { setError(err.response?.data?.message || 'Error al guardar') }
    finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">{esEdicion ? 'Editar adelanto' : 'Nuevo adelanto a propietario'}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-4">
          {!esEdicion && (
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Propietario *</label>
              <input value={buscarProp} onChange={e => setBuscarProp(e.target.value)}
                placeholder="Buscar por nombre, cédula, teléfono o email..." className={inp + ' mb-1.5'} />
              <select required value={form.idPropietario} onChange={set('idPropietario')} className={inp}>
                <option value="">Seleccionar propietario...</option>
                {propietariosFiltrados.map(p => <option key={p.id} value={p.id}>{p.nombre} · {p.cedula || 's/cédula'} · {p.telefono}</option>)}
              </select>
            </div>
          )}
          {esEdicion && (
            <p className="text-xs text-slate-400">{inicial!.id} · {inicial!.nombrePropietario}</p>
          )}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Fecha *</label>
              <input type="date" required value={form.fecha} onChange={set('fecha')} className={inp} />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Monto (RD$) *</label>
              <input type="number" min={0} step="0.01" required value={form.monto} onChange={set('monto')} className={inp} placeholder="0.00" />
            </div>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Forma de entrega</label>
            <select value={form.formaPago} onChange={set('formaPago')} className={inp}>
              <option value="EFECTIVO">Efectivo</option>
              <option value="TRANSFERENCIA">Transferencia</option>
              <option value="CHEQUE">Cheque</option>
            </select>
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Concepto / justificación *</label>
            <input required value={form.concepto} onChange={set('concepto')} className={inp}
              placeholder="Ej: Adelanto para reparación de techo, pintura..." />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Notas adicionales</label>
            <textarea value={form.notas} onChange={set('notas')} className={inp + ' resize-none'} rows={2} />
          </div>
          <div className="bg-amber-50 border border-amber-200 rounded-lg p-3 text-xs text-amber-800">
            Es un comprobante de dinero entregado al propietario (de fondos del administrador, no de la caja). Queda <strong>PENDIENTE</strong> y se descuenta en la próxima liquidación del propietario. {!esEdicion && 'Los documentos se adjuntan luego desde la lista.'}
          </div>
          <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2" style={{ display: error ? '' : 'none' }}>{error}</p>
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading}
              className="px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90" style={{ background: BRAND }}>
              {loading ? 'Guardando...' : esEdicion ? 'Guardar cambios' : 'Registrar adelanto'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ─── Modal documentos ────────────────────────────────────────────────────────
function DocumentosModal({ adelanto, onClose, isAdmin }: { adelanto: Adelanto; onClose: () => void; isAdmin: boolean }) {
  const [docs, setDocs] = useState<AdelantoDocumentoInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [subiendo, setSubiendo] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)
  const editable = adelanto.estado === 'PENDIENTE' && isAdmin

  const load = () => {
    setLoading(true)
    api.get<{ data: AdelantoDocumentoInfo[] }>(`/adelantos/${adelanto.id}/documentos`)
      .then(r => setDocs(r.data.data)).catch(() => {}).finally(() => setLoading(false))
  }
  useEffect(load, [adelanto.id])

  const subir = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    setSubiendo(true); setError('')
    try {
      const fd = new FormData()
      Array.from(files).forEach(f => fd.append('archivos', f))
      await api.post(`/adelantos/${adelanto.id}/documentos`, fd, { headers: { 'Content-Type': 'multipart/form-data' } })
      load()
    } catch (err: any) { setError(err.response?.data?.message || 'Error al subir') }
    finally { setSubiendo(false); if (fileRef.current) fileRef.current.value = '' }
  }

  const descargar = async (d: AdelantoDocumentoInfo) => {
    const r = await api.get(`/adelantos/documentos/${d.id}`, { responseType: 'blob' })
    const url = URL.createObjectURL(r.data as Blob)
    const a = document.createElement('a'); a.href = url; a.download = d.nombreArchivo; a.click()
    URL.revokeObjectURL(url)
  }

  const eliminar = async (d: AdelantoDocumentoInfo) => {
    if (!confirm(`¿Eliminar el documento "${d.nombreArchivo}"?`)) return
    try { await api.delete(`/adelantos/documentos/${d.id}`); load() }
    catch (err: any) { setError(err.response?.data?.message || 'Error al eliminar') }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <div>
            <h2 className="font-semibold text-slate-800">Documentos del adelanto</h2>
            <p className="text-xs text-slate-400">{adelanto.id} · {adelanto.nombrePropietario}</p>
          </div>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <div className="p-5 space-y-3">
          {editable && (
            <div>
              <input ref={fileRef} type="file" multiple accept=".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png"
                onChange={e => subir(e.target.files)} className="hidden" />
              <button onClick={() => fileRef.current?.click()} disabled={subiendo}
                className="flex items-center gap-2 px-4 py-2 rounded-lg border border-dashed border-slate-300 text-sm text-slate-600 hover:bg-slate-50 w-full justify-center">
                <Paperclip size={15} /> {subiendo ? 'Subiendo...' : 'Adjuntar documento (PDF/JPG/PNG, máx 5 MB)'}
              </button>
            </div>
          )}
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          {loading ? (
            <div className="flex justify-center py-6"><div className="w-6 h-6 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
          ) : docs.length === 0 ? (
            <p className="text-center text-slate-400 text-sm py-6">Sin documentos adjuntos</p>
          ) : (
            <div className="space-y-2">
              {docs.map(d => (
                <div key={d.id} className="flex items-center gap-3 border border-slate-200 rounded-lg px-3 py-2">
                  <FileText size={18} className="text-slate-400 shrink-0" />
                  <div className="flex-1 min-w-0">
                    <p className="text-sm text-slate-700 truncate">{d.nombreArchivo}</p>
                    <p className="text-xs text-slate-400">{(d.tamano / 1024).toFixed(0)} KB · {formatDate(d.fechaSubida)}</p>
                  </div>
                  <button onClick={() => descargar(d)} title="Descargar" className="p-1.5 rounded hover:bg-blue-50 text-slate-400 hover:text-blue-600"><Download size={15} /></button>
                  {editable && (
                    <button onClick={() => eliminar(d)} title="Eliminar" className="p-1.5 rounded hover:bg-red-50 text-slate-400 hover:text-red-600"><Trash2 size={15} /></button>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ─── Modal pagar (devolución a caja) ──────────────────────────────────────────
function PagarModal({ adelanto, onClose, onDone }: { adelanto: Adelanto; onClose: () => void; onDone: () => void }) {
  const [forma, setForma] = useState('EFECTIVO')
  const [referencia, setReferencia] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async () => {
    setLoading(true); setError('')
    try {
      await api.post(`/adelantos/${adelanto.id}/pagar`, { formaPago: forma, referencia })
      onDone()
    } catch (err: any) { setError(err.response?.data?.message || 'Error al registrar el pago') }
    finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Registrar pago del propietario</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <div className="p-5 space-y-3">
          <div className="bg-blue-50 border border-blue-100 rounded-lg px-3 py-2 text-sm text-blue-800">
            El propietario <strong>{adelanto.nombrePropietario}</strong> devuelve <strong>{formatCurrency(adelanto.monto)}</strong>. Entra a la <strong>caja activa</strong> y el adelanto pasa a PAGADO (ya no se descuenta de la liquidación).
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Forma de pago</label>
              <select value={forma} onChange={e => setForma(e.target.value)} className={inp}>
                {['EFECTIVO', 'TRANSFERENCIA', 'TARJETA', 'CHEQUE'].map(f => <option key={f}>{f}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Referencia</label>
              <input value={referencia} onChange={e => setReferencia(e.target.value)} className={inp} />
            </div>
          </div>
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          <div className="flex justify-end gap-3 pt-1">
            <button onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button onClick={submit} disabled={loading}
              className="px-4 py-2 rounded-lg text-white text-sm font-medium" style={{ background: '#0d9488' }}>
              {loading ? 'Registrando...' : 'Registrar en caja'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}

// ─── Modal anular ─────────────────────────────────────────────────────────────
function AnularModal({ adelanto, onClose, onDone }: { adelanto: Adelanto; onClose: () => void; onDone: () => void }) {
  const [motivo, setMotivo] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!motivo.trim()) { setError('Debe indicar la justificación'); return }
    setLoading(true); setError('')
    try {
      await api.post(`/adelantos/${adelanto.id}/anular`, { motivoAnulacion: motivo })
      onDone()
    } catch (err: any) { setError(err.response?.data?.message || 'Error al anular') }
    finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Anular adelanto {adelanto.id}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-3">
          <div className="bg-amber-50 border border-amber-200 rounded-lg px-3 py-2 text-xs text-amber-800">
            La anulación deja el adelanto sin efecto (ya no se descuenta de liquidaciones). No mueve la caja. Queda registrada en el log con su justificación.
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Justificación de la anulación *</label>
            <textarea required value={motivo} onChange={e => setMotivo(e.target.value)}
              className={inp + ' resize-none'} rows={3} placeholder="Ej: error de digitación, adelanto duplicado, gasto revertido..." />
          </div>
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading} className="px-4 py-2 rounded-lg text-white text-sm font-medium bg-red-600">
              {loading ? 'Anulando...' : 'Anular adelanto'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ─── Página ───────────────────────────────────────────────────────────────────
export default function Adelantos() {
  const { isAdmin } = useAuthStore()
  const admin = isAdmin()
  const [list, setList]         = useState<Adelanto[]>([])
  const [propietarios, setProp] = useState<Propietario[]>([])
  const [loading, setLoading]   = useState(true)
  const [search, setSearch]     = useState('')
  const [estadoFilter, setEstado] = useState('')
  const [modal, setModal]       = useState(false)
  const [editar, setEditar]     = useState<Adelanto | null>(null)
  const [docs, setDocs]         = useState<Adelanto | null>(null)
  const [pagar, setPagar]       = useState<Adelanto | null>(null)
  const [anular, setAnular]     = useState<Adelanto | null>(null)

  const load = () => {
    setLoading(true)
    Promise.all([
      api.get<{ data: Adelanto[] }>('/adelantos'),
      api.get<{ data: Propietario[] }>('/propietarios'),
    ]).then(([ra, rp]) => { setList(ra.data.data); setProp(rp.data.data) }).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const filtered = list.filter(a => {
    const matchSearch = a.nombrePropietario.toLowerCase().includes(search.toLowerCase()) ||
      a.concepto.toLowerCase().includes(search.toLowerCase()) || a.id.toLowerCase().includes(search.toLowerCase())
    const matchEstado = !estadoFilter || a.estado === estadoFilter
    return matchSearch && matchEstado
  })

  const { sorted, sortKey, sortDir, toggle } = useSort(filtered, 'fecha', 'desc')
  const th = (label: string, key: keyof Adelanto, cls = 'text-left') => (
    <SortTh label={label} sortKey={key as string} active={sortKey as string}
      dir={sortDir} onSort={k => toggle(k as keyof Adelanto)} className={cls} />
  )

  const totalPendiente = filtered.filter(a => a.estado === 'PENDIENTE').reduce((s, a) => s + a.monto, 0)
  const totalRecuperado = filtered.filter(a => a.estado === 'LIQUIDADO' || a.estado === 'PAGADO').reduce((s, a) => s + a.monto, 0)

  return (
    <div className="space-y-4 max-w-7xl">
      <PrintHeader titulo="Adelantos a Propietarios" filtros={{ Estado: estadoFilter, Búsqueda: search }} />

      <div className="flex items-center justify-between no-print">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Adelantos a Propietarios</h1>
          <p className="text-slate-500 text-sm mt-0.5">Comprobantes de dinero entregado, se descuentan en la liquidación</p>
        </div>
        {admin && (
          <button onClick={() => setModal(true)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90" style={{ background: BRAND }}>
            <Plus size={16} /> Nuevo adelanto
          </button>
        )}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <p className="text-xs text-slate-500 mb-1">Pendientes (se descontarán)</p>
          <p className="text-2xl font-bold text-amber-600">{formatCurrency(totalPendiente)}</p>
        </div>
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <p className="text-xs text-slate-500 mb-1">Recuperados (liquidados o pagados)</p>
          <p className="text-2xl font-bold text-green-600">{formatCurrency(totalRecuperado)}</p>
        </div>
      </div>

      <div className="flex gap-3 no-print">
        <div className="relative flex-1">
          <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
          <input value={search} onChange={e => setSearch(e.target.value)}
            placeholder="Buscar por propietario, concepto o ID..."
            className="w-full pl-9 pr-4 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <select value={estadoFilter} onChange={e => setEstado(e.target.value)}
          className="px-3 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500">
          <option value="">Todos los estados</option>
          <option value="PENDIENTE">Pendiente</option>
          <option value="LIQUIDADO">Liquidado</option>
          <option value="PAGADO">Pagado</option>
          <option value="ANULADO">Anulado</option>
        </select>
      </div>

      {loading ? (
        <div className="flex justify-center py-12"><div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : (
        <div className="sticky-table-wrap">
          <table className="w-full text-sm">
            <thead>
              <tr>
                {th('ID', 'id')}
                {th('Propietario', 'nombrePropietario')}
                {th('Fecha', 'fecha')}
                {th('Concepto', 'concepto')}
                {th('Monto', 'monto', 'text-right')}
                {th('Estado', 'estado')}
                <th className="px-4 py-3 text-right font-medium text-slate-600 no-print">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 bg-white">
              {sorted.length === 0 ? (
                <tr><td colSpan={7} className="text-center py-10 text-slate-400">Sin adelantos registrados</td></tr>
              ) : sorted.map(a => {
                const pend = a.estado === 'PENDIENTE'
                return (
                  <tr key={a.id} className={`hover:bg-slate-50 ${a.estado === 'ANULADO' ? 'opacity-50' : ''}`}>
                    <td className="px-4 py-3 text-xs font-mono text-slate-400">{a.id}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{a.nombrePropietario}</td>
                    <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(a.fecha)}</td>
                    <td className="px-4 py-3 text-slate-600">{a.concepto}
                      {a.estado === 'ANULADO' && a.motivoAnulacion && <span className="block text-xs text-red-400">Anulado: {a.motivoAnulacion}</span>}
                    </td>
                    <td className="px-4 py-3 text-right font-semibold text-amber-700">{formatCurrency(a.monto)}</td>
                    <td className="px-4 py-3"><span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${estadoBadge(a.estado)}`}>{a.estado}</span></td>
                    <td className="px-4 py-3 text-right no-print">
                      <div className="flex items-center justify-end gap-1">
                        <button onClick={() => setDocs(a)} title="Documentos" className="p-1.5 rounded hover:bg-slate-100 text-slate-400 hover:text-slate-700"><Paperclip size={14} /></button>
                        {admin && pend && (
                          <>
                            <button onClick={() => setEditar(a)} title="Editar" className="p-1.5 rounded hover:bg-slate-100 text-slate-400 hover:text-slate-700"><Pencil size={14} /></button>
                            <button onClick={() => setPagar(a)} title="Registrar pago del propietario" className="p-1.5 rounded hover:bg-teal-50 text-slate-400 hover:text-teal-600"><Wallet size={14} /></button>
                            <button onClick={() => setAnular(a)} title="Anular" className="p-1.5 rounded hover:bg-red-50 text-slate-400 hover:text-red-600"><Ban size={14} /></button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {modal && <AdelantoModal propietarios={propietarios} onClose={() => setModal(false)} onSaved={() => { setModal(false); load() }} />}
      {editar && <AdelantoModal propietarios={propietarios} inicial={editar} onClose={() => setEditar(null)} onSaved={() => { setEditar(null); load() }} />}
      {docs && <DocumentosModal adelanto={docs} isAdmin={admin} onClose={() => setDocs(null)} />}
      {pagar && <PagarModal adelanto={pagar} onClose={() => setPagar(null)} onDone={() => { setPagar(null); load() }} />}
      {anular && <AnularModal adelanto={anular} onClose={() => setAnular(null)} onDone={() => { setAnular(null); load() }} />}
    </div>
  )
}
