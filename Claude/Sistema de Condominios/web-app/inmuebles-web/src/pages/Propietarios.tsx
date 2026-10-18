import { useEffect, useState } from 'react'
import { Plus, Search, Phone, Mail, Pencil } from 'lucide-react'
import api, { type Propietario } from '../lib/api'
import { estadoBadgeClass } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import { useSort } from '../lib/useSort'
import SortTh from '../components/SortTh'
import PropietarioModal from '../components/modals/PropietarioModal'
import PrintHeader from '../components/PrintHeader'

export default function Propietarios() {
  const { isAdmin } = useAuthStore()
  const [list, setList]       = useState<Propietario[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch]   = useState('')
  const [modal, setModal]     = useState<Propietario | null | 'nuevo'>(null)

  const load = () => {
    api.get<{ data: Propietario[] }>('/propietarios')
      .then(r => setList(r.data.data))
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  const filtered = list.filter(p =>
    p.nombre.toLowerCase().includes(search.toLowerCase()) ||
    p.cedula.includes(search) || p.telefono.includes(search)
  )

  const { sorted, sortKey, sortDir, toggle } = useSort(filtered, 'nombre')
  const th = (label: string, key: keyof Propietario, cls = 'text-left') => (
    <SortTh label={label} sortKey={key as string} active={sortKey as string}
      dir={sortDir} onSort={k => toggle(k as keyof Propietario)} className={cls} />
  )

  return (
    <div className="space-y-4 max-w-7xl">
      <PrintHeader titulo="Propietarios" filtros={{ Búsqueda: search }} />

      <div className="flex items-center justify-between no-print">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Propietarios</h1>
          <p className="text-slate-500 text-sm mt-0.5">{list.length} propietario{list.length !== 1 ? 's' : ''} registrado{list.length !== 1 ? 's' : ''}</p>
        </div>
        {isAdmin() && (
          <button onClick={() => setModal('nuevo')}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
            style={{ background: '#1B4F8C' }}>
            <Plus size={16} /> Nuevo propietario
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
                {th('Propietario', 'nombre')}
                {th('Cédula', 'cedula', 'text-left hidden md:table-cell')}
                <th className="px-4 py-3 font-medium text-slate-600 text-left hidden lg:table-cell">Contacto</th>
                {th('Comisión', 'tipoComision', 'text-left hidden lg:table-cell')}
                {th('Dueño', 'tipoDueno', 'text-left hidden lg:table-cell')}
                {th('Estado', 'estado')}
                {isAdmin() && <th className="px-4 py-3 w-10" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 bg-white">
              {sorted.length === 0 ? (
                <tr><td colSpan={7} className="text-center py-10 text-slate-400 text-sm">Sin resultados</td></tr>
              ) : sorted.map(p => (
                <tr key={p.id} className="hover:bg-slate-50 transition-colors">
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-3">
                      <div className="w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold text-white shrink-0"
                        style={{ background: '#1B4F8C' }}>{p.nombre[0]}</div>
                      <div>
                        <p className="font-medium text-slate-800">{p.nombre}</p>
                        <p className="text-xs text-slate-400">{p.id}</p>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-slate-600 hidden md:table-cell">{p.cedula}</td>
                  <td className="px-4 py-3 hidden lg:table-cell">
                    <div className="flex flex-col gap-0.5 text-xs">
                      <span className="flex items-center gap-1 text-slate-600"><Phone size={11} />{p.telefono}</span>
                      <span className="flex items-center gap-1 text-slate-500"><Mail size={11} />{p.email}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-slate-600 hidden lg:table-cell">
                    {p.tipoComision === 'PORCENTAJE' ? `${p.comisionPorcentaje}%` :
                     p.tipoComision === 'FIJO' ? `RD$ ${p.comisionFija}` :
                     `${p.comisionPorcentaje}% + RD$ ${p.comisionFija}`}
                  </td>
                  <td className="px-4 py-3 hidden lg:table-cell">
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${
                      p.tipoDueno === 'FAMILIAR' ? 'bg-purple-100 text-purple-700' : 'bg-slate-100 text-slate-600'
                    }`}>
                      {p.tipoDueno === 'FAMILIAR' ? '👨‍👩‍👧 Familiar' : '👤 Único'}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${estadoBadgeClass(p.estado)}`}>
                      {p.estado}
                    </span>
                  </td>
                  {isAdmin() && (
                    <td className="px-4 py-3 text-right no-print">
                      <button onClick={() => setModal(p)}
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
        <PropietarioModal onClose={() => setModal(null)} onSaved={() => { setModal(null); load() }}
          inicial={modal === 'nuevo' ? null : modal} />
      )}
    </div>
  )
}
