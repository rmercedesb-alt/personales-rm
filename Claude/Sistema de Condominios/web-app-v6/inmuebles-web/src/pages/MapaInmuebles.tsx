import { useEffect, useMemo, useState } from 'react'
import { useSearchParams, useNavigate } from 'react-router-dom'
import { MapContainer, TileLayer, Marker, Popup, useMap } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { ArrowLeft, ChevronDown } from 'lucide-react'
import api, { type Inmueble, type Propietario, type CxCCargo, type CxCPago } from '../lib/api'
import { formatCurrency, morosidadColor } from '../lib/utils'

const LEYENDA = [
  { nivel: 'ROJO',     label: 'Vencido (fuera de gracia)', color: '#ef4444' },
  { nivel: 'AMARILLO', label: 'En gracia / por vencer',     color: '#eab308' },
  { nivel: 'VERDE',    label: 'Al día / pago reciente',     color: '#22c55e' },
  { nivel: 'GRIS',     label: 'Disponible / vacío',         color: '#94a3b8' },
]

function makeHouseIcon(color: string, ocupado: boolean, destacado = false) {
  const pinDot = ocupado
    ? `<circle cx="26" cy="6" r="5" fill="${color}" stroke="white" stroke-width="1.5"/>`
    : ''

  const size = destacado ? 44 : 32
  const h    = destacado ? 50 : 36
  const ring = destacado
    ? `<circle cx="${size/2}" cy="${h/2}" r="${size/2 - 1}" fill="none" stroke="${color}" stroke-width="2.5" opacity="0.4"/>`
    : ''

  const svg = `
    <svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${h}" viewBox="0 0 32 36">
      ${ring}
      <ellipse cx="16" cy="34" rx="8" ry="2.5" fill="rgba(0,0,0,0.18)"/>
      <path d="M4 18 L16 6 L28 18 L28 32 Q28 34 26 34 L6 34 Q4 34 4 32 Z"
            fill="${color}" stroke="white" stroke-width="${destacado ? 2.5 : 1.5}"/>
      <path d="M4 18 L16 6 L28 18 Z"
            fill="${color}" stroke="white" stroke-width="${destacado ? 2.5 : 1.5}"
            style="filter:brightness(0.82)"/>
      <rect x="12" y="24" width="8" height="10" rx="2"
            fill="white" opacity="0.55"/>
      ${pinDot}
    </svg>`

  return L.divIcon({
    html: svg,
    className: '',
    iconSize:    [size, h],
    iconAnchor:  [size / 2, h],
    popupAnchor: [0, -h],
  })
}

function FlyTo({ lat, lng }: { lat: number; lng: number }) {
  const map = useMap()
  useEffect(() => { map.flyTo([lat, lng], 16, { duration: 1.2 }) }, [lat, lng])
  return null
}

function InvalidateOnMount() {
  const map = useMap()
  useEffect(() => {
    const t1 = setTimeout(() => map.invalidateSize(), 0)
    const t2 = setTimeout(() => map.invalidateSize(), 300)
    const onResize = () => map.invalidateSize()
    window.addEventListener('resize', onResize)
    return () => { clearTimeout(t1); clearTimeout(t2); window.removeEventListener('resize', onResize) }
  }, [map])
  return null
}

interface Estatus { nivel: string; color: string; diasMora: number; enGracia: boolean; ultimoPago: string | null }

export default function MapaInmuebles() {
  const [searchParams] = useSearchParams()
  const navigate       = useNavigate()
  const idFiltro        = searchParams.get('idInmueble')

  const [inmuebles, setInmuebles]     = useState<Inmueble[]>([])
  const [propietarios, setProp]       = useState<Propietario[]>([])
  const [cxcs, setCxcs]                = useState<CxCCargo[]>([])
  const [pagos, setPagos]              = useState<CxCPago[]>([])
  const [loading, setLoading]          = useState(true)

  const [topN, setTopN]                       = useState(10)
  const [propsFiltro, setPropsFiltro]         = useState<string[]>([])
  const [inmueblesFiltro, setInmueblesFiltro] = useState<string[]>([])
  const [propOpen, setPropOpen]               = useState(false)
  const [inmOpen, setInmOpen]                 = useState(false)

  useEffect(() => {
    setLoading(true)
    Promise.all([
      api.get<{ data: Inmueble[] }>('/inmuebles'),
      api.get<{ data: Propietario[] }>('/propietarios'),
      api.get<{ data: CxCCargo[] }>('/cxc'),
      api.get<{ data: CxCPago[] }>('/pagos'),
    ]).then(([ri, rp, rc, rpg]) => {
      setInmuebles(ri.data.data)
      setProp(rp.data.data)
      setCxcs(rc.data.data)
      setPagos(rpg.data.data)
    }).finally(() => setLoading(false))
  }, [])

  const todos = inmuebles.filter(i => i.latitud && i.longitud)

  // ─── Estatus dinámico por inmueble (no depende del campo nivelMorosidad almacenado) ───
  const estatusPorInmueble = useMemo(() => {
    const map = new Map<string, Estatus>()
    for (const inm of todos) {
      const cargosInm = cxcs.filter(c => c.idInmueble === inm.id && c.estado !== 'PAGADO')
      const maxDiasMora = Math.max(0, ...cargosInm.map(c => c.diasMora), 0)
      const enGracia = cargosInm.some(c => c.enGracia)
      const pagosInm = pagos.filter(p => p.idInmueble === inm.id && !p.anulado)
      const ultimoPago = pagosInm.length > 0
        ? pagosInm.reduce((a, b) => +new Date(a.fechaPago) > +new Date(b.fechaPago) ? a : b).fechaPago
        : null

      let nivel = 'GRIS', color = '#94a3b8'
      if (inm.estado === 'OCUPADO') {
        if (maxDiasMora > 0) { nivel = 'ROJO'; color = morosidadColor('ROJO') }
        else if (enGracia) { nivel = 'AMARILLO'; color = morosidadColor('AMARILLO') }
        else { nivel = 'VERDE'; color = morosidadColor('VERDE') }
      }
      map.set(inm.id, { nivel, color, diasMora: maxDiasMora, enGracia, ultimoPago })
    }
    return map
  }, [todos, cxcs, pagos])

  // Top N: más vencidos (rojo) y pagos más recientes (verde)
  const topVencidos = useMemo(() =>
    [...todos]
      .filter(i => (estatusPorInmueble.get(i.id)?.diasMora ?? 0) > 0)
      .sort((a, b) => (estatusPorInmueble.get(b.id)?.diasMora ?? 0) - (estatusPorInmueble.get(a.id)?.diasMora ?? 0))
      .slice(0, topN).map(i => i.id),
  [todos, estatusPorInmueble, topN])

  const topRecientes = useMemo(() =>
    [...todos]
      .filter(i => estatusPorInmueble.get(i.id)?.ultimoPago)
      .sort((a, b) => +new Date(estatusPorInmueble.get(b.id)!.ultimoPago!) - +new Date(estatusPorInmueble.get(a.id)!.ultimoPago!))
      .slice(0, topN).map(i => i.id),
  [todos, estatusPorInmueble, topN])

  const hayFiltrosManual = propsFiltro.length > 0 || inmueblesFiltro.length > 0

  const seleccionado = idFiltro ? todos.find(i => i.id === idFiltro) ?? null : null

  const lista = seleccionado
    ? [seleccionado]
    : hayFiltrosManual
      ? todos.filter(i =>
          (propsFiltro.length === 0 || propsFiltro.includes(i.idPropietario)) &&
          (inmueblesFiltro.length === 0 || inmueblesFiltro.includes(i.id)))
      : todos.filter(i => topVencidos.includes(i.id) || topRecientes.includes(i.id))

  const centro: [number, number] = seleccionado
    ? [seleccionado.latitud!, seleccionado.longitud!]
    : [18.47, -68.93]

  const zoomInicial = seleccionado ? 16 : 10

  const togglePropFiltro = (id: string) =>
    setPropsFiltro(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id])
  const toggleInmFiltro = (id: string) =>
    setInmueblesFiltro(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id])

  return (
    <div className="space-y-4 max-w-7xl">
      <div className="flex items-center gap-3">
        {idFiltro && (
          <button
            onClick={() => navigate('/inmuebles')}
            className="flex items-center gap-1.5 text-sm text-blue-600 hover:underline">
            <ArrowLeft size={15} /> Volver a inmuebles
          </button>
        )}
        <div>
          <h1 className="text-2xl font-semibold text-slate-800">
            {seleccionado ? seleccionado.descripcion : 'Mapa de Inmuebles'}
          </h1>
          <p className="text-slate-500 text-sm mt-0.5">
            {seleccionado
              ? `${seleccionado.sector} · ${seleccionado.ciudad}`
              : 'Semáforo de morosidad por ubicación'}
          </p>
        </div>
      </div>

      {!seleccionado && (
        <div className="flex flex-wrap items-end gap-3 bg-white rounded-xl border border-slate-200 p-3">
          <div className="relative">
            <label className="block text-xs font-medium text-slate-600 mb-1">Propietarios</label>
            <button type="button" onClick={() => setPropOpen(o => !o)}
              className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm flex items-center gap-2 min-w-[180px] justify-between">
              <span>{propsFiltro.length === 0 ? 'Todos' : `${propsFiltro.length} seleccionado(s)`}</span>
              <ChevronDown size={14} />
            </button>
            {propOpen && (
              <div className="absolute z-[1100] mt-1 bg-white border border-slate-200 rounded-lg shadow-lg p-2 w-64 max-h-64 overflow-y-auto">
                {propietarios.map(p => (
                  <label key={p.id} className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-slate-50 cursor-pointer text-sm">
                    <input type="checkbox" checked={propsFiltro.includes(p.id)} onChange={() => togglePropFiltro(p.id)} />
                    {p.nombre}
                  </label>
                ))}
              </div>
            )}
          </div>

          <div className="relative">
            <label className="block text-xs font-medium text-slate-600 mb-1">Inmuebles</label>
            <button type="button" onClick={() => setInmOpen(o => !o)}
              className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm flex items-center gap-2 min-w-[180px] justify-between">
              <span>{inmueblesFiltro.length === 0 ? 'Top N (automático)' : `${inmueblesFiltro.length} seleccionado(s)`}</span>
              <ChevronDown size={14} />
            </button>
            {inmOpen && (
              <div className="absolute z-[1100] mt-1 bg-white border border-slate-200 rounded-lg shadow-lg p-2 w-72 max-h-64 overflow-y-auto">
                {todos.map(i => (
                  <label key={i.id} className="flex items-center gap-2 px-2 py-1.5 rounded hover:bg-slate-50 cursor-pointer text-sm">
                    <input type="checkbox" checked={inmueblesFiltro.includes(i.id)} onChange={() => toggleInmFiltro(i.id)} />
                    {i.descripcion}
                  </label>
                ))}
              </div>
            )}
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Top N (por defecto)</label>
            <input type="number" min={1} max={100} value={topN} onChange={e => setTopN(+e.target.value || 10)}
              className="px-3 py-2 rounded-lg border border-slate-200 bg-white text-sm w-24" />
          </div>

          {hayFiltrosManual && (
            <button onClick={() => { setPropsFiltro([]); setInmueblesFiltro([]) }}
              className="px-3 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
              Limpiar filtros
            </button>
          )}

          <p className="text-xs text-slate-400 ml-auto">
            {!hayFiltrosManual && `Mostrando top ${topN} más vencidos y top ${topN} con pago más reciente`}
          </p>
        </div>
      )}

      <div className="relative bg-white rounded-xl border border-slate-200 overflow-hidden" style={{ height: 580 }}>
        {loading ? (
          <div className="flex items-center justify-center h-full">
            <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
        <MapContainer center={centro} zoom={zoomInicial} style={{ height: '100%', width: '100%' }} scrollWheelZoom>
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <InvalidateOnMount />

          {seleccionado && (
            <FlyTo lat={seleccionado.latitud!} lng={seleccionado.longitud!} />
          )}

          {lista.map(inm => {
            const est        = estatusPorInmueble.get(inm.id)
            const ocupado    = inm.estado === 'OCUPADO'
            const color      = est?.color ?? '#94a3b8'
            const destacado  = seleccionado?.id === inm.id
            const icon       = makeHouseIcon(color, ocupado, destacado)

            return (
              <Marker key={inm.id} position={[inm.latitud!, inm.longitud!]} icon={icon}>
                <Popup>
                  <div className="text-xs min-w-[190px]">
                    <div className="flex items-center gap-1.5 mb-2">
                      <div className="w-3 h-3 rounded-full flex-shrink-0" style={{ background: color }} />
                      <span className="font-semibold text-slate-800">{inm.descripcion}</span>
                    </div>
                    <p className="text-slate-500 mb-2">{inm.direccion}, {inm.sector}</p>
                    <div className="space-y-1 border-t border-slate-100 pt-2">
                      <div className="flex justify-between">
                        <span className="text-slate-500">Alquiler</span>
                        <span className="font-medium">{formatCurrency(inm.montoAlquiler)}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-slate-500">Estado</span>
                        <span className={`font-semibold ${ocupado ? 'text-orange-600' : 'text-slate-400'}`}>
                          {inm.estado}
                        </span>
                      </div>
                      {ocupado && (
                        <div className="flex justify-between">
                          <span className="text-slate-500">Estatus</span>
                          <span className="font-semibold" style={{ color }}>
                            {est?.nivel}{est && est.diasMora > 0 ? ` (${est.diasMora}d)` : ''}
                          </span>
                        </div>
                      )}
                      <div className="flex justify-between">
                        <span className="text-slate-500">Propietario</span>
                        <span className="font-medium text-right max-w-[110px] truncate">{inm.nombrePropietario}</span>
                      </div>
                    </div>
                  </div>
                </Popup>
              </Marker>
            )
          })}
        </MapContainer>
        )}

        {/* Leyenda */}
        <div className="absolute bottom-4 left-4 z-[1000] bg-white/95 rounded-xl shadow-lg border border-slate-200 p-3 text-xs space-y-2">
          <p className="font-semibold text-slate-700">Semáforo de morosidad</p>
          {LEYENDA.map(l => (
            <div key={l.nivel} className="flex items-center gap-2">
              <svg width="18" height="20" viewBox="0 0 32 36">
                <path d="M4 18 L16 6 L28 18 L28 32 Q28 34 26 34 L6 34 Q4 34 4 32 Z"
                      fill={l.color} stroke="white" strokeWidth="2"/>
                <path d="M4 18 L16 6 L28 18 Z" fill={l.color} stroke="white" strokeWidth="2"
                      style={{ filter: 'brightness(0.82)' }}/>
                <rect x="12" y="24" width="8" height="10" rx="2" fill="white" opacity="0.55"/>
                {l.nivel !== 'GRIS' &&
                  <circle cx="26" cy="6" r="5" fill={l.color} stroke="white" strokeWidth="1.5"/>}
              </svg>
              <span className="text-slate-600">{l.label}</span>
            </div>
          ))}
        </div>

        {/* Conteo */}
        <div className="absolute top-4 left-4 z-[1000] bg-white/95 rounded-lg shadow border border-slate-200 px-3 py-1.5 text-xs text-slate-600">
          {seleccionado ? (
            <span>Mostrando 1 inmueble · <button className="text-blue-600 hover:underline" onClick={() => navigate('/mapa')}>Ver todos</button></span>
          ) : (
            <>{lista.length} de {todos.length} mostrados</>
          )}
        </div>
      </div>
    </div>
  )
}
