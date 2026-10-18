import { useEffect, useState } from 'react'
import { Plus, Search, Pencil } from 'lucide-react'
import api, { type Propietario } from '../lib/api'
import { formatCurrency, formatDate, estadoBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useSort } from '../lib/useSort'
import SortTh from '../components/SortTh'
import PrintHeader from '../components/PrintHeader'

interface Adelanto {
  id: string
  idPropietario: string
  nombrePropietario: string
  fecha: string
  concepto: string
  monto: number
  estado: 'PENDIENTE' | 'APLICADO'
  idLiquidacion: string
  usuario: string
  fechaRegistro: string
}

interface AdelantoForm {
  idPropietario: string
  fecha: string
  concepto: string
  monto: string
  notas: string
}

const inp = 'w-full px-3 py-2.5 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white'

function AdelantoModal({ onClose, onSaved, propietarios }: {
  onClose: () => void; onSaved: () => void; propietarios: Propietario[]
}) {
  const [form, setForm] = useState<AdelantoForm>({
    idPropietario: '', fecha: new Date().toISOString().split('T')[0],
    concepto: '', monto: '', notas: ''
  })
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [buscarProp, setBuscarProp] = useState('')
  const set = (k: keyof AdelantoForm) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) =>
    setForm(f => ({ ...f, [k]: e.target.value }))

  const propietariosFiltrados = propietarios.filter(p => {
    const t = buscarProp.toLowerCase()
    return !t || p.nombre.toLowerCase().includes(t) || p.cedula.includes(buscarProp) ||
      p.telefono.includes(buscarProp) || p.email.toLowerCase().includes(t) || p.id.toLowerCase().includes(t)
  })

  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setLoading(true); setError('')
    try {
      await api.post('/adelantos', { ...form, monto: parseFloat(form.monto) })
      onSaved()
    } catch (err: any) { setError(err.response?.data?.message || 'Error al guardar') }
    finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">Nuevo adelanto a propietario</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 text-xl leading-none">×</button>
        </div>
        <form onSubmit={submit} className="p-5 space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Propietario *</label>
            <input value={buscarProp} onChange={e => setBuscarProp(e.target.value)}
              placeholder="Buscar por nombre, cédula, teléfono o email..."
              className={inp + ' mb-1.5'} />
            <select required value={form.idPropietario} onChange={set('idPropietario')} className={inp}>
              <option value="">Seleccionar propietario...</option>
              {propietariosFiltrados.map(p => <option key={p.id} value={p.id}>{p.nombre} · {p.cedula || 's/cédula'} · {p.telefono}</option>)}
            </select>
          </div>
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
            <label className="block text-xs font-medium text-slate-600 mb-1">Concepto / justificación *</label>
            <input required value={form.concepto} onChange={set('concepto')} className={inp}
              placeholder="Ej: Adelanto para reparación de techo, pintura..." />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Notas adicionales</label>
            <textarea value={form.notas} onChange={set('notas')} className={inp + ' resize-none'} rows={2} />
          </div>
          <div className="bg-amber-50 border border-amber-200 rounded-lg p-3 text-xs text-amber-800">
            Este adelanto quedará en estado <strong>PENDIENTE</strong> y será descontado automáticamente al procesar la próxima liquidación del propietario.
          </div>
          {error && <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2">{error}</p>}
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">Cancelar</button>
            <button type="submit" disabled={loading}
              className="px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
              style={{ background: '#1B4F8C' }}>
              {loading ? 'Guardando...' : 'Registrar adelanto'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

export default function Adelantos() {
  const { isAdmin } = useAuthStore()
  const [list, setList]             = useState<Adelanto[]>([])
  const [propietarios, setProp]     = useState<Propietario[]>([])
  const [loading, setLoading]       = useState(true)
  const [search, setSearch]         = useState('')
  const [estadoFilter, setEstado]   = useState('')
  const [modal, setModal]           = useState(false)

  const load = () => {
    setLoading(true)
    Promise.all([
      api.get<{ data: Adelanto[] }>('/adelantos'),
      api.get<{ data: Propietario[] }>('/propietarios'),
    ]).then(([ra, rp]) => {
      setList(ra.data.data)
      setProp(rp.data.data)
    }).finally(() => setLoading(false))
  }
  useEffect(load, [])

  const filtered = list.filter(a => {
    const matchSearch = a.nombrePropietario.toLowerCase().includes(search.toLowerCase()) ||
      a.concepto.toLowerCase().includes(search.toLowerCase())
    const matchEstado = !estadoFilter || a.estado === estadoFilter
    return matchSearch && matchEstado
  })

  const { sorted, sortKey, sortDir, toggle } = useSort(filtered, 'fecha', 'desc')
  const th = (label: string, key: keyof Adelanto, cls = 'text-left') => (
    <SortTh label={label} sortKey={key as string} active={sortKey as string}
      dir={sortDir} onSort={k => toggle(k as keyof Adelanto)} className={cls} />
  )

  const totalPendiente = filtered.filter(a => a.estado === 'PENDIENTE').reduce((s, a) => s + a.monto, 0)
  const totalAplicado  = filtered.filter(a => a.estado === 'APLICADO').reduce((s, a) => s + a.monto, 0)

  return (
    <div className="space-y-4 max-w-7xl">
      <PrintHeader titulo="Adelantos a Propietarios"
        filtros={{ Estado: estadoFilter, Búsqueda: search }} />

      <div className="flex items-center justify-between no-print">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Adelantos a Propietarios</h1>
          <p className="text-slate-500 text-sm mt-0.5">
            Pagos anticipados que se descuentan en la liquidación
          </p>
        </div>
        {isAdmin() && (
          <button onClick={() => setModal(true)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
            style={{ background: '#1B4F8C' }}>
            <Plus size={16} /> Nuevo adelanto
          </button>
        )}
      </div>

      {/* KPIs */}
      <div className="grid grid-cols-2 gap-4">
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <p className="text-xs text-slate-500 mb-1">Pendientes de aplicar</p>
          <p className="text-2xl font-bold text-amber-600">{formatCurrency(totalPendiente)}</p>
        </div>
        <div className="bg-white rounded-xl border border-slate-200 p-4">
          <p className="text-xs text-slate-500 mb-1">Ya descontados en liquidación</p>
          <p className="text-2xl font-bold text-green-600">{formatCurrency(totalAplicado)}</p>
        </div>
      </div>

      <div className="flex gap-3 no-print">
        <div className="relative flex-1">
          <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
          <input value={search} onChange={e => setSearch(e.target.value)}
            placeholder="Buscar por propietario o concepto..."
            className="w-full pl-9 pr-4 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <select value={estadoFilter} onChange={e => setEstado(e.target.value)}
          className="px-3 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500">
          <option value="">Todos los estados</option>
          <option value="PENDIENTE">Pendiente</option>
          <option value="APLICADO">Aplicado</option>
        </select>
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
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
                <th className="px-4 py-3 text-left font-medium text-slate-600">Liquidación</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 bg-white">
              {sorted.length === 0 ? (
                <tr><td colSpan={7} className="text-center py-10 text-slate-400">Sin adelantos registrados</td></tr>
              ) : sorted.map(a => (
                <tr key={a.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 text-xs font-mono text-slate-400">{a.id}</td>
                  <td className="px-4 py-3 font-medium text-slate-800">{a.nombrePropietario}</td>
                  <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(a.fecha)}</td>
                  <td className="px-4 py-3 text-slate-600">{a.concepto}</td>
                  <td className="px-4 py-3 text-right font-semibold text-amber-700">{formatCurrency(a.monto)}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${
                      a.estado === 'APLICADO' ? 'bg-green-100 text-green-700' : 'bg-amber-100 text-amber-700'
                    }`}>{a.estado}</span>
                  </td>
                  <td className="px-4 py-3 text-xs font-mono text-slate-400">{a.idLiquidacion || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {modal && (
        <AdelantoModal
          propietarios={propietarios}
          onClose={() => setModal(false)}
          onSaved={() => { setModal(false); load() }}
        />
      )}
    </div>
  )
}
