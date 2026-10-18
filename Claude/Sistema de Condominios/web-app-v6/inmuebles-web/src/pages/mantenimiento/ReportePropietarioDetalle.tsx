import { useState, useEffect, useMemo } from 'react'
import { Printer, FileSpreadsheet, Users2 } from 'lucide-react'
import api, { type Propietario, type Inmueble, type Contrato } from '../../lib/api'
import { formatCurrency } from '../../lib/utils'
import { exportarTablaExcel } from '../../lib/exportExcel'
import MultiSelect from '../../components/MultiSelect'

const BRAND = '#1B4F8C'
const esVigente = (e: string) => e === 'VIGENTE' || e === 'ACTIVO'

interface FilaInm {
  idInmueble: string; descInmueble: string; estado: string
  idInquilino: string; nombreInquilino: string
  alquiler: number; deposito: number
}
interface Grupo {
  idProp: string; nombre: string
  inmuebles: FilaInm[]
  totInmuebles: number; ocupados: number; disponibles: number
  inquilinos: number; totAlquiler: number; totDeposito: number
}

export default function ReportePropietarioDetalle() {
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [inmuebles, setInmuebles]       = useState<Inmueble[]>([])
  const [contratos, setContratos]       = useState<Contrato[]>([])
  const [cargando, setCargando]         = useState(true)
  const [fProp, setFProp]               = useState<string[]>([])

  useEffect(() => {
    (async () => {
      setCargando(true)
      try {
        const [p, m, c] = await Promise.all([
          api.get<{ data: Propietario[] }>('/propietarios'),
          api.get<{ data: Inmueble[] }>('/inmuebles'),
          api.get<{ data: Contrato[] }>('/contratos'),
        ])
        setPropietarios(p.data.data); setInmuebles(m.data.data); setContratos(c.data.data)
      } finally { setCargando(false) }
    })()
  }, [])

  const opcionesProp = useMemo(
    () => propietarios.map(p => ({ value: p.id, label: p.nombre })).sort((a, b) => a.label.localeCompare(b.label)),
    [propietarios])

  // Contrato vigente por inmueble
  const contratoVigentePorInmueble = useMemo(() => {
    const m = new Map<string, Contrato>()
    contratos.filter(c => esVigente(c.estado)).forEach(c => { if (!m.has(c.idInmueble)) m.set(c.idInmueble, c) })
    return m
  }, [contratos])

  const grupos: Grupo[] = useMemo(() => {
    let props = propietarios
    if (fProp.length) props = props.filter(p => fProp.includes(p.id))
    return props.map(p => {
      const inms = inmuebles.filter(i => i.idPropietario === p.id)
      const filas: FilaInm[] = inms.map(i => {
        const c = contratoVigentePorInmueble.get(i.id)
        const ocupado = !!c
        return {
          idInmueble: i.id, descInmueble: i.descripcion, estado: ocupado ? 'OCUPADO' : 'DISPONIBLE',
          idInquilino: c?.idInquilino ?? '', nombreInquilino: c?.nombreInquilino ?? '—',
          alquiler: c?.montoAlquiler ?? i.montoAlquiler ?? 0, deposito: c?.deposito ?? 0,
        }
      }).sort((a, b) => a.descInmueble.localeCompare(b.descInmueble))
      const ocupados = filas.filter(f => f.estado === 'OCUPADO')
      const inquilinosSet = new Set(ocupados.map(f => f.idInquilino).filter(Boolean))
      return {
        idProp: p.id, nombre: p.nombre, inmuebles: filas,
        totInmuebles: filas.length, ocupados: ocupados.length, disponibles: filas.length - ocupados.length,
        inquilinos: inquilinosSet.size,
        totAlquiler: ocupados.reduce((s, f) => s + f.alquiler, 0),
        totDeposito: ocupados.reduce((s, f) => s + f.deposito, 0),
      }
    }).filter(g => g.totInmuebles > 0).sort((a, b) => a.nombre.localeCompare(b.nombre))
  }, [propietarios, inmuebles, contratoVigentePorInmueble, fProp])

  const tot = useMemo(() => grupos.reduce((a, g) => ({
    inmuebles: a.inmuebles + g.totInmuebles, ocupados: a.ocupados + g.ocupados,
    disponibles: a.disponibles + g.disponibles, inquilinos: a.inquilinos + g.inquilinos,
    alquiler: a.alquiler + g.totAlquiler, deposito: a.deposito + g.totDeposito,
  }), { inmuebles: 0, ocupados: 0, disponibles: 0, inquilinos: 0, alquiler: 0, deposito: 0 }), [grupos])

  const exportar = () => {
    const filas: (string | number)[][] = []
    grupos.forEach(g => g.inmuebles.forEach(f =>
      filas.push([g.nombre, f.idInmueble, f.descInmueble, f.estado, f.nombreInquilino, f.alquiler, f.deposito])))
    exportarTablaExcel('Propietario_Inmuebles_Inquilinos',
      ['Propietario','Cód. Inmueble','Inmueble','Estado','Inquilino','Alquiler','Depósito'], filas)
  }

  return (
    <div className="max-w-6xl space-y-5">
      <div className="no-print flex items-center justify-between flex-wrap gap-2">
        <div className="flex items-center gap-3">
          <Users2 size={22} style={{ color: BRAND }} />
          <h1 className="text-xl font-semibold text-slate-800">Reporte por Propietario (Inmuebles, Inquilinos, Alquiler y Depósitos)</h1>
        </div>
        <div className="flex gap-2">
          <button onClick={exportar} className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm" style={{ background: '#15803d' }}>
            <FileSpreadsheet size={15} /> Excel
          </button>
          <button onClick={() => window.print()} className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm" style={{ background: BRAND }}>
            <Printer size={15} /> Imprimir / PDF
          </button>
        </div>
      </div>

      <div className="no-print bg-white rounded-xl border border-slate-200 p-4">
        <label className="block text-xs text-slate-500 mb-1">Propietario(s)</label>
        <div className="max-w-md">
          <MultiSelect options={opcionesProp} selected={fProp} onChange={setFProp} allLabel="Todos los propietarios" />
        </div>
      </div>

      {cargando ? (
        <div className="flex justify-center py-16"><div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" /></div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden print:border-0">
          {grupos.length === 0 ? (
            <p className="text-center text-slate-400 py-12 text-sm">No hay propietarios con inmuebles para esos filtros.</p>
          ) : grupos.map(g => (
            <div key={g.idProp} className="border-t border-slate-100 first:border-t-0">
              {/* Cabecera del propietario */}
              <div className="px-5 py-2.5 bg-blue-50 flex justify-between items-center flex-wrap gap-2">
                <span className="font-semibold text-blue-800">{g.nombre}</span>
                <span className="text-xs text-blue-700">
                  {g.totInmuebles} inmuebles ({g.ocupados} ocupados / {g.disponibles} disp.) · {g.inquilinos} inquilinos ·
                  Alquiler <strong>{formatCurrency(g.totAlquiler)}</strong> · Depósitos <strong>{formatCurrency(g.totDeposito)}</strong>
                </span>
              </div>
              <table className="w-full text-sm">
                <thead className="text-xs text-slate-400">
                  <tr>
                    <th className="text-left px-5 py-1.5">Inmueble</th>
                    <th className="text-center px-3 py-1.5">Estado</th>
                    <th className="text-left px-3 py-1.5">Inquilino</th>
                    <th className="text-right px-3 py-1.5">Alquiler</th>
                    <th className="text-right px-5 py-1.5">Depósito</th>
                  </tr>
                </thead>
                <tbody>
                  {g.inmuebles.map((f, i) => (
                    <tr key={f.idInmueble} className={`border-t border-slate-50 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                      <td className="px-5 py-2 font-medium text-slate-700">{f.descInmueble} <span className="text-xs text-slate-400">{f.idInmueble}</span></td>
                      <td className="px-3 py-2 text-center">
                        <span className={`text-xs px-2 py-0.5 rounded-full ${f.estado === 'OCUPADO' ? 'bg-orange-100 text-orange-700' : 'bg-green-100 text-green-700'}`}>{f.estado}</span>
                      </td>
                      <td className="px-3 py-2 text-slate-600">{f.nombreInquilino}</td>
                      <td className="px-3 py-2 text-right text-slate-700">{f.estado === 'OCUPADO' ? formatCurrency(f.alquiler) : '—'}</td>
                      <td className="px-5 py-2 text-right text-slate-700">{f.estado === 'OCUPADO' ? formatCurrency(f.deposito) : '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}

          {/* TOTAL GENERAL */}
          {grupos.length > 0 && (
            <div className="px-5 py-4 border-t-2 border-slate-300 bg-slate-100">
              <div className="flex flex-wrap gap-x-8 gap-y-2 text-sm font-semibold text-slate-700">
                <span>TOTALES:</span>
                <span>Inmuebles: <strong>{tot.inmuebles}</strong></span>
                <span className="text-orange-700">Ocupados: <strong>{tot.ocupados}</strong></span>
                <span className="text-green-700">Disponibles: <strong>{tot.disponibles}</strong></span>
                <span>Inquilinos: <strong>{tot.inquilinos}</strong></span>
                <span>Alquiler (activos): <strong>{formatCurrency(tot.alquiler)}</strong></span>
                <span>Depósitos: <strong>{formatCurrency(tot.deposito)}</strong></span>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
