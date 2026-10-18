import { useEffect, useState } from 'react'
import { Plus, Search, MapPin, Pencil, CreditCard, Map } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import api, { type Inmueble } from '../lib/api'
import { estadoBadgeClass, formatCurrency, morosidadColor } from '../lib/utils'
import { useAuthStore } from '../store/authStore'
import InmuebleModal from '../components/modals/InmuebleModal'

export default function Inmuebles() {
  const { isAdmin } = useAuthStore()
  const navigate = useNavigate()
  const [list, setList]         = useState<Inmueble[]>([])
  const [loading, setLoading]   = useState(true)
  const [search, setSearch]     = useState('')
  const [modal, setModal]       = useState<Inmueble | null | 'nuevo'>(null)
  const [cardMenu, setCardMenu] = useState<Inmueble | null>(null)

  const load = () => {
    api.get<{ data: Inmueble[] }>('/inmuebles')
      .then(r => setList(r.data.data))
      .finally(() => setLoading(false))
  }
  useEffect(load, [])

  const filtered = list.filter(i =>
    i.descripcion.toLowerCase().includes(search.toLowerCase()) ||
    i.sector.toLowerCase().includes(search.toLowerCase()) ||
    i.nombrePropietario.toLowerCase().includes(search.toLowerCase())
  )

  const closeModal = () => setModal(null)
  const savedModal = () => { closeModal(); load() }

  return (
    <div className="space-y-4 max-w-7xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">Inmuebles</h1>
          <p className="text-slate-500 text-sm mt-0.5">{list.length} inmueble{list.length !== 1 ? 's' : ''}</p>
        </div>
        {isAdmin() && (
          <button onClick={() => setModal('nuevo')}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
            style={{ background: '#1B4F8C' }}>
            <Plus size={16} /> Nuevo inmueble
          </button>
        )}
      </div>

      <div className="relative">
        <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
        <input value={search} onChange={e => setSearch(e.target.value)}
          placeholder="Buscar por descripción, sector o propietario..."
          className="w-full pl-9 pr-4 py-2.5 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500" />
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <div className="w-7 h-7 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {filtered.length === 0 ? (
            <div className="col-span-3 text-center py-12 text-slate-400">Sin resultados</div>
          ) : filtered.map(inm => (
            <div key={inm.id}
              onClick={() => setCardMenu(inm)}
              className="bg-white rounded-xl border border-slate-200 p-4 hover:shadow-md transition-shadow cursor-pointer relative group">

              {/* Edit button */}
              {isAdmin() && (
                <button
                  onClick={e => { e.stopPropagation(); setModal(inm) }}
                  className="absolute top-3 right-3 p-1.5 rounded opacity-0 group-hover:opacity-100 hover:bg-slate-100 text-slate-400 hover:text-slate-700 transition-all">
                  <Pencil size={13} />
                </button>
              )}

              <div className="flex items-start justify-between mb-3 pr-6">
                <div className="flex-1 min-w-0">
                  <p className="font-medium text-slate-800 truncate">{inm.descripcion}</p>
                  <p className="text-xs text-slate-400 mt-0.5">{inm.id} · {inm.tipo}</p>
                </div>
                <div className="w-3 h-3 rounded-full ml-2 shrink-0 mt-1"
                  style={{ background: morosidadColor(inm.nivelMorosidad) }}
                  title={`Morosidad: ${inm.nivelMorosidad}`} />
              </div>

              <div className="flex items-center gap-1 text-xs text-slate-500 mb-3">
                <MapPin size={11} />
                <span className="truncate">{inm.direccion}, {inm.sector}</span>
              </div>

              <div className="flex items-center justify-between">
                <span className="text-sm font-semibold text-slate-800">{formatCurrency(inm.montoAlquiler)}</span>
                <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${estadoBadgeClass(inm.estado)}`}>
                  {inm.estado}
                </span>
              </div>
              <p className="text-xs text-slate-400 mt-2 truncate">Propietario: {inm.nombrePropietario}</p>
            </div>
          ))}
        </div>
      )}

      {/* Modal Editar/Nuevo */}
      {modal && (
        <InmuebleModal
          onClose={closeModal}
          onSaved={savedModal}
          inicial={modal === 'nuevo' ? null : modal}
        />
      )}

      {/* Dialog: card click → CxC o Mapa */}
      {cardMenu && (
        <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4"
          onClick={() => setCardMenu(null)}>
          <div className="bg-white rounded-2xl shadow-2xl w-full max-w-sm p-6"
            onClick={e => e.stopPropagation()}>
            <p className="font-semibold text-slate-800 mb-1">{cardMenu.descripcion}</p>
            <p className="text-xs text-slate-400 mb-5">{cardMenu.sector} · {cardMenu.ciudad}</p>
            <div className="grid grid-cols-2 gap-3">
              <button
                onClick={() => { navigate(`/cxc?idInmueble=${cardMenu.id}`); setCardMenu(null) }}
                className="flex flex-col items-center gap-2 p-4 rounded-xl border-2 border-slate-200 hover:border-blue-400 hover:bg-blue-50 transition-colors">
                <CreditCard size={22} className="text-blue-600" />
                <span className="text-sm font-medium text-slate-700">CxC / Cargos</span>
                <span className="text-xs text-slate-400">Ver cuentas por cobrar</span>
              </button>
              <button
                onClick={() => { navigate(`/mapa?idInmueble=${cardMenu.id}`); setCardMenu(null) }}
                className="flex flex-col items-center gap-2 p-4 rounded-xl border-2 border-slate-200 hover:border-green-400 hover:bg-green-50 transition-colors">
                <Map size={22} className="text-green-600" />
                <span className="text-sm font-medium text-slate-700">Ver en mapa</span>
                <span className="text-xs text-slate-400">Ubicación geográfica</span>
              </button>
            </div>
            <button onClick={() => setCardMenu(null)}
              className="mt-4 w-full text-center text-sm text-slate-400 hover:text-slate-600">
              Cancelar
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
