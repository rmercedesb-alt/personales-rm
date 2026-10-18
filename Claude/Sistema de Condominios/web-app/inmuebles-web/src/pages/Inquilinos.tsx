import { useEffect, useState } from 'react'
import { Plus, Search, Phone, Mail, Pencil } from 'lucide-react'
import api, { type Inquilino } from '../lib/api'
import { estadoBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useSort } from '../lib/useSort'
import SortTh from '../components/SortTh'
import InquilinoModal from '../components/modals/InquilinoModal'
import PrintHeader from '../components/PrintHeader'

export default function Inquilinos() {
  const { isAsistente } = useAuthStore()
  const [list, setList]       = useState<Inquilino[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch]   = useState('')
  const [modal, setModal]     = useState<Inquilino | null | 'nuevo'>(null)

  const load = () => {
    api.get<{ data: Inquilino[] }>('/inquilinos')
      .then(r => setList(r.data.data))
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  const filtered = list.filter(i =>
    i.nombre.toLowerCase().includes(search.toLowerCase()) ||
    i.cedula.includes(search) || i.telefono.includes(search)
  )

  const { sorted, sortKey, sortDir, toggle } = useSort(filtered, 'nombre')
  const th = (label: string, key: keyof Inquilino, cls = 'text-left') => (
    <SortTh label={label} sortKey={key as string} active={sortKey as string}
      dir={sortDir} onSort={k => toggle(k as keyof Inquilino)} className={cls} />
  )

  return (
    <div className="space-y-4 max-w-7xl">
      <PrintHeader titulo="Inquilinos" filtros={{ Búsqueda: search }} />

      <div className="flex items-center justify-between no-print">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Inquilinos</h1>
          <p className="text-slate-500 text-sm mt-0.5">{list.length} inquilino{list.length !== 1 ? 's' : ''}</p>
        </div>
        {isAsistente() && (
          <button onClick={() => setModal('nuevo')}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
            style={{ background: '#1B4F8C' }}>
            <Plus size={16} /> Nuevo inquilino
          </button>
        )}
      </div>

      <div className="relative no-print">
        <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
        <input value={search} onChange={e => setSearch(e.target.value)}
          placeholder="Buscar por nombre, cédula o teléfono..."
          className="w-full pl-9 pr-4 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
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
                {th('Inquilino', 'nombre')}
                {th('Cédula', 'cedula', 'text-left hidden md:table-cell')}
                <th className="px-4 py-3 font-medium text-slate-600 text-left hidden lg:table-cell">Contacto</th>
                {th('Estado', 'estado')}
                {isAsistente() && <th className="px-4 py-3 w-10 no-print" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 bg-white">
              {sorted.length === 0 ? (
                <tr><td colSpan={5} className="text-center py-10 text-slate-400">Sin resultados</td></tr>
              ) : sorted.map(i => (
                <tr key={i.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-3">
                      <div className="w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold text-white shrink-0"
                        style={{ background: '#1B4F8C' }}>{i.nombre[0]}</div>
                      <div>
                        <p className="font-medium text-slate-800">{i.nombre}</p>
                        <p className="text-xs text-slate-400">{i.id}</p>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-slate-600 hidden md:table-cell">{i.cedula}</td>
                  <td className="px-4 py-3 hidden lg:table-cell">
                    <div className="flex flex-col gap-0.5 text-xs">
                      <span className="flex items-center gap-1 text-slate-600"><Phone size={10} />{i.telefono}</span>
                      <span className="flex items-center gap-1 text-slate-400"><Mail size={10} />{i.email}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${estadoBadgeClass(i.estado)}`}>
                      {i.estado}
                    </span>
                  </td>
                  {isAsistente() && (
                    <td className="px-4 py-3 text-right no-print">
                      <button onClick={() => setModal(i)}
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
        <InquilinoModal onClose={() => setModal(null)} onSaved={() => { setModal(null); load() }}
          inicial={modal === 'nuevo' ? null : modal} />
      )}
    </div>
  )
}
