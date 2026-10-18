import { useState, useEffect, useMemo, Fragment } from 'react'
import { Printer, Users, UserCog, Home, ChevronDown, ChevronRight, FileSpreadsheet } from 'lucide-react'
import api, { type Propietario, type Inquilino, type Inmueble, type CoPropietario } from '../../lib/api'
import { formatCurrency, estadoBadgeClass } from '../../lib/utils'
import { exportarTablaExcel } from '../../lib/exportExcel'
import MultiSelect from '../../components/MultiSelect'

const BRAND = '#1B4F8C'
type Tab = 'propietarios' | 'inquilinos' | 'inmuebles'

const esFamiliar = (tipo: string) => (tipo || '').toUpperCase().startsWith('FAMIL')

// Orden de disponibilidad para inmuebles
const ORDEN_ESTADO: Record<string, number> = { DISPONIBLE: 0, OCUPADO: 1, MANTENIMIENTO: 2, INACTIVO: 3 }

export default function InformesMaestras() {
  const [tab, setTab] = useState<Tab>('propietarios')
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [inquilinos, setInquilinos]     = useState<Inquilino[]>([])
  const [inmuebles, setInmuebles]       = useState<Inmueble[]>([])
  const [cargando, setCargando]         = useState(true)

  // Filtros de inmuebles
  const [filtroProp, setFiltroProp]     = useState<string[]>([])
  const [filtroEstado, setFiltroEstado] = useState<string[]>([])

  // Propietarios: filtro tipo dueño + detalle copropietarios
  const [filtroTipo, setFiltroTipo]     = useState('')   // '' = todos
  const [expandProp, setExpandProp]     = useState<string | null>(null)
  const [coProps, setCoProps]           = useState<Record<string, CoPropietario[]>>({})

  const toggleProp = async (p: Propietario) => {
    if (!esFamiliar(p.tipoDueno)) return
    if (expandProp === p.id) { setExpandProp(null); return }
    setExpandProp(p.id)
    if (!coProps[p.id]) {
      try {
        const r = await api.get<{ data: CoPropietario[] }>(`/propietarios/${p.id}/copropietarios`)
        setCoProps(prev => ({ ...prev, [p.id]: r.data.data }))
      } catch { setCoProps(prev => ({ ...prev, [p.id]: [] })) }
    }
  }

  const propietariosFiltrados = useMemo(() => {
    if (!filtroTipo) return propietarios
    return propietarios.filter(p =>
      filtroTipo === 'FAMILIAR' ? esFamiliar(p.tipoDueno) : !esFamiliar(p.tipoDueno))
  }, [propietarios, filtroTipo])

  useEffect(() => {
    (async () => {
      setCargando(true)
      try {
        const [p, i, m] = await Promise.all([
          api.get<{ data: Propietario[] }>('/propietarios'),
          api.get<{ data: Inquilino[] }>('/inquilinos'),
          api.get<{ data: Inmueble[] }>('/inmuebles'),
        ])
        setPropietarios(p.data.data)
        setInquilinos(i.data.data)
        setInmuebles(m.data.data)
      } finally { setCargando(false) }
    })()
  }, [])

  const opcionesProp = useMemo(
    () => propietarios.map(p => ({ value: p.id, label: p.nombre })).sort((a, b) => a.label.localeCompare(b.label)),
    [propietarios])
  const opcionesEstadoInm = useMemo(
    () => Array.from(new Set(inmuebles.map(i => i.estado))).map(e => ({ value: e, label: e })),
    [inmuebles])

  // Inmuebles filtrados, agrupados por propietario, ordenados por disponibilidad
  const inmueblesAgrupados = useMemo(() => {
    let lista = inmuebles
    if (filtroProp.length)   lista = lista.filter(i => filtroProp.includes(i.idPropietario))
    if (filtroEstado.length) lista = lista.filter(i => filtroEstado.includes(i.estado))
    const grupos = new Map<string, Inmueble[]>()
    lista.forEach(i => {
      const k = i.nombrePropietario || 'Sin propietario'
      if (!grupos.has(k)) grupos.set(k, [])
      grupos.get(k)!.push(i)
    })
    return Array.from(grupos.entries())
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([nombre, items]) => ({
        nombre,
        items: items.sort((a, b) =>
          (ORDEN_ESTADO[a.estado] ?? 9) - (ORDEN_ESTADO[b.estado] ?? 9) ||
          a.descripcion.localeCompare(b.descripcion)),
      }))
  }, [inmuebles, filtroProp, filtroEstado])

  const tabs: { key: Tab; label: string; icon: React.ElementType }[] = [
    { key: 'propietarios', label: 'Propietarios', icon: UserCog },
    { key: 'inquilinos',   label: 'Inquilinos',   icon: Users },
    { key: 'inmuebles',    label: 'Inmuebles',    icon: Home },
  ]

  const exportar = () => {
    if (tab === 'propietarios') {
      exportarTablaExcel('Propietarios',
        ['Código','Nombre','Cédula/RNC','Pasaporte','Teléfono','Email','Tipo dueño','Comisión','Estado'],
        propietariosFiltrados.map(p => [p.id, p.nombre, p.cedula, p.pasaporte ?? '', p.telefono, p.email,
          esFamiliar(p.tipoDueno) ? 'Familiar' : 'Único',
          `${p.tipoComision}${p.comisionPorcentaje > 0 ? ' ' + p.comisionPorcentaje + '%' : ''}`, p.estado]))
    } else if (tab === 'inquilinos') {
      exportarTablaExcel('Inquilinos',
        ['Código','Nombre','Cédula','Pasaporte','Teléfono','Email','Ocupación','Estado'],
        inquilinos.map(q => [q.id, q.nombre, q.cedula, q.pasaporte ?? '', q.telefono, q.email, q.ocupacion, q.estado]))
    } else {
      const filas: (string|number)[][] = []
      inmueblesAgrupados.forEach(g => g.items.forEach(m =>
        filas.push([m.id, g.nombre, m.descripcion, m.tipo, m.sector, m.ciudad, m.montoAlquiler, m.estado])))
      exportarTablaExcel('Inmuebles',
        ['Código','Propietario','Descripción','Tipo','Sector','Ciudad','Alquiler','Estado'], filas)
    }
  }

  return (
    <div className="max-w-6xl space-y-5">
      <div className="no-print flex items-center justify-between">
        <h1 className="text-xl font-semibold text-slate-800">Informes de Maestras</h1>
        <div className="flex gap-2">
          <button onClick={exportar}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm" style={{ background: '#15803d' }}>
            <FileSpreadsheet size={15} /> Excel
          </button>
          <button onClick={() => window.print()}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm" style={{ background: BRAND }}>
            <Printer size={15} /> Imprimir / PDF
          </button>
        </div>
      </div>

      {/* Tabs */}
      <div className="no-print flex gap-1 border-b border-slate-200">
        {tabs.map(t => (
          <button key={t.key} onClick={() => setTab(t.key)}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
              tab === t.key ? 'border-blue-600 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}>
            <t.icon size={15} /> {t.label}
          </button>
        ))}
      </div>

      {cargando ? (
        <div className="flex justify-center py-16">
          <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden print:border-0">
          {/* Encabezado imprimible */}
          <div className="px-5 py-4 border-b border-slate-200">
            <h2 className="text-base font-bold text-slate-800">
              {tab === 'propietarios' && `LISTADO DE PROPIETARIOS (${propietariosFiltrados.length})`}
              {tab === 'inquilinos'   && `LISTADO DE INQUILINOS (${inquilinos.length})`}
              {tab === 'inmuebles'    && 'LISTADO DE INMUEBLES POR PROPIETARIO'}
            </h2>
          </div>

          {/* PROPIETARIOS */}
          {tab === 'propietarios' && (
            <>
              <div className="no-print px-5 py-3 border-b border-slate-100 bg-slate-50 flex items-center gap-3">
                <label className="text-xs text-slate-500">Tipo de dueño:</label>
                <select value={filtroTipo} onChange={e => setFiltroTipo(e.target.value)}
                  className="border border-slate-300 rounded-lg px-3 py-1.5 text-sm">
                  <option value="">Todos</option>
                  <option value="UNICO">Único</option>
                  <option value="FAMILIAR">Familiar</option>
                </select>
              </div>
              <div className="overflow-auto max-h-[65vh]">
                <table className="w-full text-sm">
                  <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 sticky top-0 z-10">
                    <tr>
                      <th className="px-2 py-2.5 w-8"></th>
                      <th className="text-left px-3 py-2.5">Nombre</th>
                      <th className="text-left px-3 py-2.5">Cédula/RNC</th>
                      <th className="text-left px-3 py-2.5">Teléfono</th>
                      <th className="text-center px-3 py-2.5">Tipo dueño</th>
                      <th className="text-left px-3 py-2.5">Comisión</th>
                      <th className="text-center px-4 py-2.5">Estado</th>
                    </tr>
                  </thead>
                  <tbody>
                    {propietariosFiltrados.map((p, i) => {
                      const fam = esFamiliar(p.tipoDueno)
                      const abierto = expandProp === p.id
                      return (
                        <Fragment key={p.id}>
                          <tr onClick={() => toggleProp(p)}
                            className={`border-t border-slate-100 ${i % 2 ? 'bg-slate-50/50' : ''} ${fam ? 'cursor-pointer hover:bg-blue-50' : ''}`}>
                            <td className="px-2 py-2 text-center text-slate-400">
                              {fam && (
                                <>
                                  <ChevronDown size={14} className="mx-auto" style={{ display: abierto ? '' : 'none' }} />
                                  <ChevronRight size={14} className="mx-auto" style={{ display: abierto ? 'none' : '' }} />
                                </>
                              )}
                            </td>
                            <td className="px-3 py-2 font-medium text-slate-700">{p.nombre}</td>
                            <td className="px-3 py-2 text-slate-500">{p.cedula}</td>
                            <td className="px-3 py-2 text-slate-500">{p.telefono}</td>
                            <td className="px-3 py-2 text-center">
                              <span className={`text-xs px-2 py-0.5 rounded-full ${fam ? 'bg-purple-100 text-purple-700' : 'bg-slate-100 text-slate-600'}`}>
                                {fam ? 'Familiar' : 'Único'}
                              </span>
                            </td>
                            <td className="px-3 py-2 text-slate-500">
                              {p.tipoComision}{p.comisionPorcentaje > 0 ? ` ${p.comisionPorcentaje}%` : ''}
                              {p.comisionFija > 0 ? ` + ${formatCurrency(p.comisionFija)}` : ''}
                            </td>
                            <td className="px-4 py-2 text-center">
                              <span className={`text-xs px-2 py-0.5 rounded-full ${estadoBadgeClass(p.estado)}`}>{p.estado}</span>
                            </td>
                          </tr>
                          {fam && abierto && (
                            <tr className="bg-purple-50/40">
                              <td></td>
                              <td colSpan={6} className="px-3 py-2">
                                <p className="text-xs font-semibold text-purple-700 mb-1">Copropietarios</p>
                                {!coProps[p.id] ? (
                                  <p className="text-xs text-slate-400">Cargando...</p>
                                ) : coProps[p.id].length === 0 ? (
                                  <p className="text-xs text-slate-400">Sin copropietarios registrados.</p>
                                ) : (
                                  <table className="w-full text-xs">
                                    <thead className="text-slate-400">
                                      <tr>
                                        <th className="text-left py-1">Nombre</th>
                                        <th className="text-left py-1">Cédula</th>
                                        <th className="text-left py-1">Teléfono</th>
                                        <th className="text-right py-1">Peso %</th>
                                      </tr>
                                    </thead>
                                    <tbody>
                                      {coProps[p.id].map(cp => (
                                        <tr key={cp.id} className="border-t border-purple-100">
                                          <td className="py-1 font-medium text-slate-700">{cp.nombre}</td>
                                          <td className="py-1 text-slate-500">{cp.cedula}</td>
                                          <td className="py-1 text-slate-500">{cp.telefono}</td>
                                          <td className="py-1 text-right font-semibold text-purple-700">{cp.pesoPorc}%</td>
                                        </tr>
                                      ))}
                                    </tbody>
                                  </table>
                                )}
                              </td>
                            </tr>
                          )}
                        </Fragment>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </>
          )}

          {/* INQUILINOS */}
          {tab === 'inquilinos' && (
            <div className="overflow-auto max-h-[65vh]">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 sticky top-0 z-10">
                  <tr>
                    <th className="text-left px-4 py-2.5">Nombre</th>
                    <th className="text-left px-3 py-2.5">Cédula</th>
                    <th className="text-left px-3 py-2.5">Teléfono</th>
                    <th className="text-left px-3 py-2.5">Email</th>
                    <th className="text-left px-3 py-2.5">Ocupación</th>
                    <th className="text-center px-4 py-2.5">Estado</th>
                  </tr>
                </thead>
                <tbody>
                  {inquilinos.map((q, i) => (
                    <tr key={q.id} className={`border-t border-slate-100 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                      <td className="px-4 py-2 font-medium text-slate-700">{q.nombre}</td>
                      <td className="px-3 py-2 text-slate-500">{q.cedula}</td>
                      <td className="px-3 py-2 text-slate-500">{q.telefono}</td>
                      <td className="px-3 py-2 text-slate-500">{q.email}</td>
                      <td className="px-3 py-2 text-slate-500">{q.ocupacion}</td>
                      <td className="px-4 py-2 text-center">
                        <span className={`text-xs px-2 py-0.5 rounded-full ${estadoBadgeClass(q.estado)}`}>{q.estado}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* INMUEBLES */}
          {tab === 'inmuebles' && (
            <>
              <div className="no-print px-5 py-3 border-b border-slate-100 grid grid-cols-1 md:grid-cols-2 gap-3 bg-slate-50">
                <div>
                  <label className="block text-xs text-slate-500 mb-1">Propietario</label>
                  <MultiSelect options={opcionesProp} selected={filtroProp} onChange={setFiltroProp} allLabel="Todos los propietarios" />
                </div>
                <div>
                  <label className="block text-xs text-slate-500 mb-1">Estado / disponibilidad</label>
                  <MultiSelect options={opcionesEstadoInm} selected={filtroEstado} onChange={setFiltroEstado} allLabel="Todos los estados" />
                </div>
              </div>

              {inmueblesAgrupados.length === 0 ? (
                <p className="text-center text-slate-400 py-10 text-sm">No hay inmuebles con esos filtros.</p>
              ) : inmueblesAgrupados.map(grupo => (
                <div key={grupo.nombre} className="border-t border-slate-100">
                  <div className="px-5 py-2 bg-blue-50 font-semibold text-blue-800 text-sm flex justify-between">
                    <span>{grupo.nombre}</span>
                    <span className="text-xs text-blue-600">{grupo.items.length} inmueble(s)</span>
                  </div>
                  <table className="w-full text-sm">
                    <thead className="text-xs text-slate-400">
                      <tr>
                        <th className="text-left px-5 py-1.5">Descripción</th>
                        <th className="text-left px-3 py-1.5">Tipo</th>
                        <th className="text-left px-3 py-1.5">Sector / Ciudad</th>
                        <th className="text-right px-3 py-1.5">Alquiler</th>
                        <th className="text-center px-5 py-1.5">Estado</th>
                      </tr>
                    </thead>
                    <tbody>
                      {grupo.items.map((m, i) => (
                        <tr key={m.id} className={`border-t border-slate-50 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                          <td className="px-5 py-2 font-medium text-slate-700">{m.descripcion}</td>
                          <td className="px-3 py-2 text-slate-500">{m.tipo}</td>
                          <td className="px-3 py-2 text-slate-500">{m.sector}{m.ciudad ? ` · ${m.ciudad}` : ''}</td>
                          <td className="px-3 py-2 text-right text-slate-700">{formatCurrency(m.montoAlquiler)}</td>
                          <td className="px-5 py-2 text-center">
                            <span className={`text-xs px-2 py-0.5 rounded-full ${estadoBadgeClass(m.estado)}`}>{m.estado}</span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ))}
            </>
          )}
        </div>
      )}
    </div>
  )
}
