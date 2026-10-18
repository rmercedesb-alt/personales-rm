import { useEffect, useState } from 'react'
import { Plus, Search, Pencil } from 'lucide-react'
import api, { type Contrato } from '../lib/api'
import { estadoBadgeClass, formatCurrency, formatDate } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useSort } from '../lib/useSort'
import SortTh from '../components/SortTh'
import ContratoModal from '../components/modals/ContratoModal'
import PrintHeader from '../components/PrintHeader'

export default function Contratos() {
  const { isAdmin } = useAuthStore()
  const [list, setList] = useState<Contrato[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [soloVigentes, setSoloVigentes] = useState(true)
  const [modal, setModal] = useState<Contrato | null | 'nuevo'>(null)

  const load = () => {
    api.get<{ data: Contrato[] }>(`/contratos?soloVigentes=${soloVigentes}`)
      .then(r => setList(r.data.data)).finally(() => setLoading(false))
  }
  useEffect(load, [soloVigentes])

  const filtered = list.filter(c =>
    c.nombreInquilino.toLowerCase().includes(search.toLowerCase()) ||
    c.nombreInmueble.toLowerCase().includes(search.toLowerCase()) ||
    c.id.includes(search)
  )

  const { sorted, sortKey, sortDir, toggle } = useSort(filtered, 'nombreInquilino')
  const th = (label: string, key: keyof Contrato, cls = 'text-left') => (
    <SortTh label={label} sortKey={key as string} active={sortKey as string}
      dir={sortDir} onSort={k => toggle(k as keyof Contrato)} className={cls} />
  )

  return (
    <div className="space-y-4 max-w-7xl">
      <PrintHeader titulo="Contratos" filtros={{ Búsqueda: search, Estado: soloVigentes ? 'Solo vigentes' : 'Todos' }} />
      <div className="flex items-center justify-between no-print">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Contratos</h1>
          <p className="text-slate-500 text-sm mt-0.5">{filtered.length} contrato{filtered.length !== 1 ? 's' : ''}</p>
        </div>
        {isAdmin() && (
          <button onClick={() => setModal('nuevo')}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
            style={{ background: '#1B4F8C' }}>
            <Plus size={16} /> Nuevo contrato
          </button>
        )}
      </div>

      <div className="flex gap-3 no-print">
        <div className="relative flex-1">
          <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
          <input value={search} onChange={e => setSearch(e.target.value)}
            placeholder="Buscar por inquilino o inmueble..."
            className="w-full pl-9 pr-4 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <label className="flex items-center gap-2 px-3 py-2 bg-white rounded-lg border border-slate-200 text-sm text-slate-600 cursor-pointer">
          <input type="checkbox" checked={soloVigentes} onChange={e => setSoloVigentes(e.target.checked)} />
          Solo vigentes
        </label>
      </div>

      {loading ? (
        <div className="flex justify-center py-12"><div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : (
        <div className="sticky-table-wrap">
          <table className="w-full text-sm min-w-[700px]">
            <thead>
              <tr>
                {th('ID', 'id')}
                {th('Inquilino', 'nombreInquilino')}
                {th('Inmueble', 'nombreInmueble')}
                {th('Inicio', 'fechaInicio')}
                {th('Fin', 'fechaFin')}
                {th('Alquiler', 'montoAlquiler', 'text-right')}
                {th('Estado', 'estado')}
                {isAdmin() && <th className="px-4 py-3 w-10 no-print" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 bg-white">
              {sorted.length === 0 ? (
                <tr><td colSpan={isAdmin() ? 8 : 7} className="text-center py-10 text-slate-400">Sin contratos</td></tr>
              ) : sorted.map(c => (
                <tr key={c.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 text-slate-500 text-xs font-mono">{c.id}</td>
                  <td className="px-4 py-3 font-medium text-slate-800">{c.nombreInquilino}</td>
                  <td className="px-4 py-3 text-slate-600">{c.nombreInmueble}</td>
                  <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(c.fechaInicio)}</td>
                  <td className="px-4 py-3 text-slate-500 text-xs">{formatDate(c.fechaFin)}</td>
                  <td className="px-4 py-3 text-right font-medium">{formatCurrency(c.montoAlquiler)}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${estadoBadgeClass(c.estado)}`}>
                      {c.estado}
                    </span>
                  </td>
                  {isAdmin() && (
                    <td className="px-4 py-3 text-right no-print">
                      <button onClick={() => setModal(c)}
                        className="p-1.5 rounded hover:bg-slate-100 text-slate-400 hover:text-slate-700">
                        <Pencil size={14} />
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {modal && (
        <ContratoModal onClose={() => setModal(null)} onSaved={() => { setModal(null); load() }}
          inicial={modal === 'nuevo' ? null : modal} />
      )}
    </div>
  )
}
