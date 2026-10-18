import { useEffect, useState } from 'react'
import { X, Loader2, MapPin } from 'lucide-react'
import { MapContainer, TileLayer, Marker, useMapEvents, useMap } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import api, { type Propietario, type Inmueble } from '../../lib/api'

interface Props { onClose: () => void; onSaved: () => void; inicial?: Inmueble | null }

const markerIcon = L.divIcon({
  html: `<div style="background:#1B4F8C;width:14px;height:14px;border-radius:50%;border:2px solid white;box-shadow:0 1px 4px rgba(0,0,0,.4)"></div>`,
  className: '', iconSize: [14, 14], iconAnchor: [7, 7],
})

function MapPicker({ lat, lng, onChange }: { lat: number; lng: number; onChange: (lat: number, lng: number) => void }) {
  useMapEvents({ click(e) { onChange(+e.latlng.lat.toFixed(6), +e.latlng.lng.toFixed(6)) } })
  return <Marker position={[lat, lng]} icon={markerIcon} draggable
    eventHandlers={{ dragend(e) { const p = (e.target as L.Marker).getLatLng(); onChange(+p.lat.toFixed(6), +p.lng.toFixed(6)) } }} />
}

function InvalidateOnMount() {
  const map = useMap()
  useEffect(() => {
    const t1 = setTimeout(() => map.invalidateSize(), 0)
    const t2 = setTimeout(() => map.invalidateSize(), 300)
    return () => { clearTimeout(t1); clearTimeout(t2) }
  }, [map])
  return null
}

const inp = 'w-full px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white'

export default function InmuebleModal({ onClose, onSaved, inicial }: Props) {
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [propSearch, setPropSearch] = useState('')
  const [propOpen, setPropOpen]   = useState(false)
  const [form, setForm] = useState({
    idPropietario: inicial?.idPropietario ?? '',
    tipo:          inicial?.tipo          ?? 'APARTAMENTO',
    descripcion:   inicial?.descripcion   ?? '',
    direccion:     inicial?.direccion     ?? '',
    sector:        inicial?.sector        ?? '',
    ciudad:        inicial?.ciudad        ?? 'La Romana',
    latitud:       inicial?.latitud       ?? 18.43,
    longitud:      inicial?.longitud      ?? -68.97,
    montoAlquiler: inicial?.montoAlquiler ?? 0,
    notas:         inicial?.notas         ?? '',
  })
  const [useMap, setUseMap]   = useState(false)
  const [loading, setLoading] = useState(false)
  const [error,   setError]   = useState('')

  useEffect(() => {
    api.get<{ data: Propietario[] }>('/propietarios').then(r => setPropietarios(r.data.data))
  }, [])

  const set = (k: string, v: string | number) => setForm(f => ({ ...f, [k]: v }))

  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setLoading(true); setError('')
    try {
      if (inicial) {
        await api.put(`/inmuebles/${inicial.id}`, form)
      } else {
        await api.post('/inmuebles', form)
      }
      onSaved()
    } catch (err: any) {
      setError(err.response?.data?.message || 'Error al guardar')
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[92vh] overflow-y-auto">
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <h2 className="font-semibold text-slate-800">{inicial ? 'Editar inmueble' : 'Nuevo inmueble'}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </div>

        <form onSubmit={submit} className="p-5 space-y-4">
          <div className="grid grid-cols-2 gap-3">

            <div className="col-span-2 relative">
              <label className="block text-xs font-medium text-slate-600 mb-1">Propietario *</label>
              {(() => {
                const sel = propietarios.find(p => p.id === form.idPropietario)
                const filtrados = propietarios
                  .filter(p => {
                    const q = propSearch.toLowerCase()
                    return !q || p.id.toLowerCase().includes(q) || p.nombre.toLowerCase().includes(q) || p.telefono.toLowerCase().includes(q)
                  })
                  .sort((a, b) => a.nombre.localeCompare(b.nombre, 'es'))
                return (
                  <>
                    <input
                      value={propOpen ? propSearch : (sel?.nombre ?? '')}
                      onFocus={() => { setPropOpen(true); setPropSearch('') }}
                      onBlur={() => setTimeout(() => setPropOpen(false), 150)}
                      onChange={e => setPropSearch(e.target.value)}
                      className={inp}
                      placeholder="Buscar por nombre, código o teléfono..."
                    />
                    <input type="text" required value={form.idPropietario} readOnly tabIndex={-1}
                      className="sr-only" onChange={() => {}} />
                    {propOpen && (
                      <div className="absolute z-50 top-full left-0 right-0 mt-1 bg-white border border-slate-200 rounded-lg shadow-lg max-h-52 overflow-y-auto">
                        {filtrados.length === 0
                          ? <p className="px-3 py-2 text-xs text-slate-400">Sin resultados</p>
                          : filtrados.map(p => (
                            <button key={p.id} type="button"
                              onMouseDown={() => { set('idPropietario', p.id); setPropOpen(false) }}
                              className={`w-full text-left px-3 py-2 text-sm hover:bg-blue-50 flex items-center gap-2 ${form.idPropietario === p.id ? 'bg-blue-50' : ''}`}>
                              <span className="font-medium text-slate-800 flex-1">{p.nombre}</span>
                              <span className="text-xs text-slate-400 shrink-0">{p.id} · {p.telefono}</span>
                            </button>
                          ))
                        }
                      </div>
                    )}
                  </>
                )
              })()}
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Tipo</label>
              <select value={form.tipo} onChange={e => set('tipo', e.target.value)} className={inp}>
                {['APARTAMENTO','CASA','LOCAL','OFICINA','VILLA','HABITACION','OTRO'].map(t => <option key={t}>{t}</option>)}
              </select>
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Monto alquiler (RD$)</label>
              <input type="number" min={0} required value={form.montoAlquiler}
                onChange={e => set('montoAlquiler', +e.target.value)} className={inp} />
            </div>

            <div className="col-span-2">
              <label className="block text-xs font-medium text-slate-600 mb-1">Descripción *</label>
              <input required value={form.descripcion} onChange={e => set('descripcion', e.target.value)}
                className={inp} placeholder="Apto 2BR Torre Los Alamos" />
            </div>

            <div className="col-span-2">
              <label className="block text-xs font-medium text-slate-600 mb-1">Dirección *</label>
              <input required value={form.direccion} onChange={e => set('direccion', e.target.value)}
                className={inp} placeholder="C/ Duarte #123" />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Sector</label>
              <input value={form.sector} onChange={e => set('sector', e.target.value)} className={inp} />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Ciudad</label>
              <select value={form.ciudad} onChange={e => set('ciudad', e.target.value)} className={inp}>
                {['La Romana','La Altagracia','San Pedro de Macorís','Santo Domingo','Santiago','Puerto Plata','Otra'].map(c => <option key={c}>{c}</option>)}
              </select>
            </div>
          </div>

          {/* Ubicación */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="text-xs font-medium text-slate-600 flex items-center gap-1"><MapPin size={12} /> Ubicación</label>
              <button type="button" onClick={() => setUseMap(v => !v)}
                className="text-xs text-blue-600 hover:underline">
                {useMap ? 'Usar coordenadas manuales' : 'Elegir en mapa'}
              </button>
            </div>

            {useMap ? (
              <div className="rounded-xl overflow-hidden border border-slate-200 mb-2" style={{ height: 220 }}>
                <MapContainer center={[form.latitud, form.longitud]} zoom={12}
                  style={{ height: '100%', width: '100%' }} scrollWheelZoom>
                  <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
                  <InvalidateOnMount />
                  <MapPicker lat={form.latitud} lng={form.longitud}
                    onChange={(lat, lng) => setForm(f => ({ ...f, latitud: lat, longitud: lng }))} />
                </MapContainer>
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Latitud</label>
                  <input type="number" step={0.000001} value={form.latitud}
                    onChange={e => set('latitud', +e.target.value)} className={inp} />
                </div>
                <div>
                  <label className="block text-xs font-medium text-slate-600 mb-1">Longitud</label>
                  <input type="number" step={0.000001} value={form.longitud}
                    onChange={e => set('longitud', +e.target.value)} className={inp} />
                </div>
              </div>
            )}
            <p className="text-xs text-slate-400 mt-1">Lat: {form.latitud} · Lng: {form.longitud}</p>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Notas</label>
            <textarea value={form.notas} onChange={e => set('notas', e.target.value)}
              className={inp + ' resize-none'} rows={2} />
          </div>

          <p className="text-sm text-red-600 bg-red-50 rounded-lg px-3 py-2"
            style={{ display: error ? '' : 'none' }}>{error}</p>

          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={onClose}
              className="px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
              Cancelar
            </button>
            <button type="submit" disabled={loading}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
              style={{ background: '#1B4F8C' }}>
              <Loader2 size={14} className="animate-spin" style={{ display: loading ? '' : 'none' }} />
              {inicial ? 'Guardar cambios' : 'Guardar inmueble'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
