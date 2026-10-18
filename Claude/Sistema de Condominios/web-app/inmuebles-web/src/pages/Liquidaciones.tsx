import { useEffect, useState } from 'react'
import { Plus } from 'lucide-react'
import api, { type Liquidacion, type Propietario } from '../lib/api'
import { estadoBadgeClass, formatCurrency, formatDate } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

export default function Liquidaciones() {
  const { isAdmin } = useAuthStore()
  const [list, setList] = useState<Liquidacion[]>([])
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [loading, setLoading] = useState(true)
  const [showModal, setShowModal] = useState(false)

  const load = () => api.get<{ data: Liquidacion[] }>('/liquidaciones').then(r => setList(r.data.data)).finally(() => setLoading(false))
  useEffect(() => {
    load()
    api.get<{ data: Propietario[] }>('/propietarios').then(r => setPropietarios(r.data.data))
  }, [])

  return (
    <div className="space-y-4 max-w-7xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Liquidaciones</h1>
          <p className="text-slate-500 text-sm mt-0.5">{list.length} liquidacion{list.length !== 1 ? 'es' : ''}</p>
        </div>
        {isAdmin() && (
          <button onClick={() => setShowModal(true)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
            style={{ background: '#1B4F8C' }}>
            <Plus size={16} /> Procesar liquidación
          </button>
        )}
      </div>
      {loading ? (
        <div className="flex justify-center py-12"><div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-x-auto">
          <table className="w-full text-sm min-w-[700px]">
            <thead className="bg-slate-50 border-b border-slate-200">
              <tr>
                <th className="text-left px-4 py-3 font-medium text-slate-600">ID</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Propietario</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Período</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Total cobrado</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Comisión</th>
                <th className="text-right px-4 py-3 font-medium text-slate-600">Neto</th>
                <th className="text-left px-4 py-3 font-medium text-slate-600">Estado</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {list.length === 0 ? (
                <tr><td colSpan={7} className="text-center py-10 text-slate-400">Sin liquidaciones</td></tr>
              ) : list.map(l => (
                <tr key={l.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 text-xs font-mono text-slate-400">{l.id}</td>
                  <td className="px-4 py-3 font-medium text-slate-800">{l.nombrePropietario}</td>
                  <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(l.fechaDesde)} → {formatDate(l.fechaHasta)}</td>
                  <td className="px-4 py-3 text-right">{formatCurrency(l.totalCobrado)}</td>
                  <td className="px-4 py-3 text-right text-orange-600">{formatCurrency(l.comision)}</td>
                  <td className="px-4 py-3 text-right font-semibold text-green-700">{formatCurrency(l.netoPropietario)}</td>
                  <td className="px-4 py-3"><span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${estadoBadgeClass(l.estado)}`}>{l.estado}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {showModal && <LiquidacionModal propietarios={propietarios} onClose={() => setShowModal(false)} onSaved={() => { setShowModal(false); load() }} />}
    </div>
  )
}

function LiquidacionModal({ propietarios, onClose, onSaved }: { propietarios: Propietario[]; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ idPropietario: '', fechaDesde: '', fechaHasta: '' })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'

  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setLoading(true); setError('')
    try { await api.post('/liquidaciones', form); onSaved() }
    catch (err: any) { setError(err.response?.data?.message || 'Error al procesar') }
    finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Procesar liquidación</h2>
          <button onClick={onClose} className="text-slate-400">✕</button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-3">
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Propietario</label>
            <select required value={form.idPropietario} onChange={e => setForm(f => ({ ...f, idPropietario: e.target.value }))} className={inp}>
              <option value="">Seleccionar...</option>
              {propietarios.map(p => <option key={p.id} value={p.id}>{p.nombre}</option>)}
            </select>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Desde</label>
              <input type="date" required value={form.fechaDesde} onChange={e => setForm(f => ({ ...f, fechaDesde: e.target.value }))} className={inp} />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Hasta</label>
              <input type="date" required value={form.fechaHasta} onChange={e => setForm(f => ({ ...f, fechaHasta: e.target.value }))} className={inp} />
            </div>
          </div>
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading} className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90" style={{ background: '#1B4F8C' }}>
              Procesar
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
