import { useState, useEffect, useMemo } from 'react'
import { TrendingUp, Printer, FileSpreadsheet } from 'lucide-react'
import api, { type Propietario, type Contrato, type CxCPago } from '../../lib/api'
import { formatCurrency, hoyLocal, primerDiaMesLocal } from '../../lib/utils'
import { exportarTablaExcel } from '../../lib/exportExcel'
import MultiSelect from '../../components/MultiSelect'

const BRAND = '#1B4F8C'

export default function ReporteBeneficios() {
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [contratos, setContratos]       = useState<Contrato[]>([])
  const [pagos, setPagos]               = useState<CxCPago[]>([])
  const [cargando, setCargando]         = useState(true)

  const [desde, setDesde]   = useState(primerDiaMesLocal())
  const [hasta, setHasta]   = useState(hoyLocal())
  const [fProp, setFProp]   = useState<string[]>([])
  const [fInq, setFInq]     = useState<string[]>([])
  const [fCon, setFCon]     = useState<string[]>([])

  useEffect(() => {
    (async () => {
      setCargando(true)
      try {
        const [p, c, pg] = await Promise.all([
          api.get<{ data: Propietario[] }>('/propietarios'),
          api.get<{ data: Contrato[] }>('/contratos'),
          api.get<{ data: CxCPago[] }>('/pagos'),
        ])
        setPropietarios(p.data.data)
        setContratos(c.data.data)
        setPagos(pg.data.data)
      } finally { setCargando(false) }
    })()
  }, [])

  const propById     = useMemo(() => new Map(propietarios.map(p => [p.id, p])), [propietarios])
  const contratoById = useMemo(() => new Map(contratos.map(c => [c.id, c])), [contratos])
  const propDeContrato = (idC: string) => contratoById.get(idC)?.idPropietario ?? ''

  const opcionesProp = useMemo(
    () => propietarios.map(p => ({ value: p.id, label: p.nombre })).sort((a, b) => a.label.localeCompare(b.label)),
    [propietarios])
  const opcionesCon = useMemo(
    () => contratos.map(c => ({ value: c.id, label: `${c.nombreInmueble} · ${c.nombreInquilino}` })),
    [contratos])
  const opcionesInq = useMemo(() => {
    const m = new Map<string, string>()
    pagos.forEach(p => m.set(p.idInquilino, p.nombreInquilino))
    return Array.from(m.entries()).map(([value, label]) => ({ value, label })).sort((a, b) => a.label.localeCompare(b.label))
  }, [pagos])

  // Cálculo por propietario
  const reporte = useMemo(() => {
    let lista = pagos.filter(p => !p.anulado && !p.esOtroIngreso)
    if (desde) lista = lista.filter(p => p.fechaPago.slice(0, 10) >= desde)
    if (hasta) lista = lista.filter(p => p.fechaPago.slice(0, 10) <= hasta)
    if (fCon.length)  lista = lista.filter(p => fCon.includes(p.idContrato))
    if (fInq.length)  lista = lista.filter(p => fInq.includes(p.idInquilino))
    if (fProp.length) lista = lista.filter(p => fProp.includes(propDeContrato(p.idContrato)))

    const grupos = new Map<string, CxCPago[]>()
    lista.forEach(p => {
      const idp = propDeContrato(p.idContrato)
      if (!grupos.has(idp)) grupos.set(idp, [])
      grupos.get(idp)!.push(p)
    })

    const filas = Array.from(grupos.entries()).map(([idp, pgs]) => {
      const prop = propById.get(idp)
      const totalAlquiler = pgs.reduce((s, p) => s + p.montoAlquiler, 0)
      const totalCobrado  = pgs.reduce((s, p) => s + p.totalPagado, 0)
      const totalRecargos = pgs.reduce((s, p) => s + p.montoMora, 0)
      const totalLiquidado = pgs.filter(p => p.estadoCustodia === 'LIQUIDADO').reduce((s, p) => s + p.montoAlquiler, 0)
      const tipo = prop?.tipoComision ?? 'PORCENTAJE'
      const porc = prop?.comisionPorcentaje ?? 0
      const fija = prop?.comisionFija ?? 0
      // V6 #5: los pagos de depósito entran completos al propietario, sin comisión.
      const comisionVar = tipo === 'FIJO' ? 0 : pgs.reduce((s, p) => s + (p.esDeposito ? 0 : Math.round(p.montoAlquiler * porc / 100 * 100) / 100), 0)
      const comisionFija = (tipo === 'FIJO' || tipo === 'MIXTO') && pgs.length > 0 ? fija : 0
      const comision = comisionVar + comisionFija
      return {
        idProp: idp, nombre: prop?.nombre ?? '—',
        totalAlquiler, totalCobrado, totalLiquidado, totalRecargos,
        comision, beneficioGeneral: comision + totalRecargos,
        count: pgs.length,
      }
    }).sort((a, b) => a.nombre.localeCompare(b.nombre))

    const tot = filas.reduce((acc, f) => ({
      totalAlquiler: acc.totalAlquiler + f.totalAlquiler,
      totalCobrado:  acc.totalCobrado + f.totalCobrado,
      totalLiquidado: acc.totalLiquidado + f.totalLiquidado,
      totalRecargos: acc.totalRecargos + f.totalRecargos,
      comision: acc.comision + f.comision,
      beneficioGeneral: acc.beneficioGeneral + f.beneficioGeneral,
    }), { totalAlquiler: 0, totalCobrado: 0, totalLiquidado: 0, totalRecargos: 0, comision: 0, beneficioGeneral: 0 })

    return { filas, tot }
  }, [pagos, desde, hasta, fProp, fInq, fCon, propById, contratoById])

  const cards = [
    { label: 'Total Alquiler',          valor: reporte.tot.totalAlquiler,    color: 'text-slate-700' },
    { label: 'Total Cobrado',           valor: reporte.tot.totalCobrado,     color: 'text-blue-700' },
    { label: 'Total Liquidado',         valor: reporte.tot.totalLiquidado,   color: 'text-purple-700' },
    { label: 'Comisiones (Beneficio)',  valor: reporte.tot.comision,         color: 'text-green-700' },
    { label: 'Total Recargos',          valor: reporte.tot.totalRecargos,    color: 'text-orange-600' },
    { label: 'Beneficio General',       valor: reporte.tot.beneficioGeneral, color: 'text-green-800' },
  ]

  const exportar = () => {
    exportarTablaExcel('Beneficios_Comisiones',
      ['Propietario', 'Pagos', 'Total Alquiler', 'Total Cobrado', 'Total Liquidado', 'Comisión', 'Recargos', 'Beneficio General'],
      reporte.filas.map(f => [f.nombre, f.count, f.totalAlquiler, f.totalCobrado, f.totalLiquidado,
        f.comision, f.totalRecargos, f.beneficioGeneral]))
  }

  return (
    <div className="max-w-6xl space-y-5">
      <div className="no-print flex items-center justify-between flex-wrap gap-2">
        <div className="flex items-center gap-3">
          <TrendingUp size={22} style={{ color: BRAND }} />
          <h1 className="text-xl font-semibold text-slate-800">Beneficios y Comisiones</h1>
        </div>
        <div className="flex gap-2">
          <button onClick={exportar}
            className="flex items-center gap-2 px-4 py-2 rounded-lg border border-slate-200 text-sm text-slate-600 hover:bg-slate-50">
            <FileSpreadsheet size={15} /> Excel
          </button>
          <button onClick={() => window.print()}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm" style={{ background: BRAND }}>
            <Printer size={15} /> Imprimir / PDF
          </button>
        </div>
      </div>

      {/* Filtros */}
      <div className="no-print bg-white rounded-xl border border-slate-200 p-4 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-3">
        <div>
          <label className="block text-xs text-slate-500 mb-1">Fecha desde</label>
          <input type="date" className="w-full border border-slate-300 rounded-lg px-3 py-2 text-sm"
            value={desde} onChange={e => setDesde(e.target.value)} />
        </div>
        <div>
          <label className="block text-xs text-slate-500 mb-1">Fecha hasta</label>
          <input type="date" className="w-full border border-slate-300 rounded-lg px-3 py-2 text-sm"
            value={hasta} onChange={e => setHasta(e.target.value)} />
        </div>
        <div>
          <label className="block text-xs text-slate-500 mb-1">Propietario(s)</label>
          <MultiSelect options={opcionesProp} selected={fProp} onChange={setFProp} allLabel="Todos" />
        </div>
        <div>
          <label className="block text-xs text-slate-500 mb-1">Inquilino(s)</label>
          <MultiSelect options={opcionesInq} selected={fInq} onChange={setFInq} allLabel="Todos" />
        </div>
        <div>
          <label className="block text-xs text-slate-500 mb-1">Contrato(s)</label>
          <MultiSelect options={opcionesCon} selected={fCon} onChange={setFCon} allLabel="Todos" />
        </div>
      </div>

      {cargando ? (
        <div className="flex justify-center py-16">
          <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : (
        <>
          {/* Tarjetas resumen */}
          <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3">
            {cards.map(c => (
              <div key={c.label} className="bg-white rounded-xl border border-slate-200 p-4">
                <p className="text-xs text-slate-500">{c.label}</p>
                <p className={`text-lg font-bold mt-1 ${c.color}`}>{formatCurrency(c.valor)}</p>
              </div>
            ))}
          </div>

          {/* Detalle por propietario */}
          <div className="bg-white rounded-xl border border-slate-200 overflow-hidden print:border-0">
            <div className="px-5 py-3 border-b border-slate-200 flex justify-between items-center">
              <h2 className="text-base font-bold text-slate-800">DETALLE POR PROPIETARIO</h2>
              <span className="text-xs text-slate-500">{desde} al {hasta}</span>
            </div>
            <div className="overflow-auto max-h-[60vh] print:max-h-none print:overflow-visible">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 sticky top-0 z-10">
                  <tr>
                    <th className="text-left px-4 py-2.5">Propietario</th>
                    <th className="text-center px-3 py-2.5">Pagos</th>
                    <th className="text-right px-3 py-2.5">Total Alquiler</th>
                    <th className="text-right px-3 py-2.5">Total Cobrado</th>
                    <th className="text-right px-3 py-2.5">Total Liquidado</th>
                    <th className="text-right px-3 py-2.5">Comisión</th>
                    <th className="text-right px-3 py-2.5">Recargos</th>
                    <th className="text-right px-4 py-2.5">Beneficio General</th>
                  </tr>
                </thead>
                <tbody>
                  {reporte.filas.length === 0 ? (
                    <tr><td colSpan={8} className="text-center text-slate-400 py-10 text-sm">No hay cobros con esos filtros.</td></tr>
                  ) : reporte.filas.map((f, i) => (
                    <tr key={f.idProp} className={`border-t border-slate-100 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                      <td className="px-4 py-2 font-medium text-slate-700">{f.nombre}</td>
                      <td className="px-3 py-2 text-center text-slate-500">{f.count}</td>
                      <td className="px-3 py-2 text-right text-slate-600">{formatCurrency(f.totalAlquiler)}</td>
                      <td className="px-3 py-2 text-right text-blue-700">{formatCurrency(f.totalCobrado)}</td>
                      <td className="px-3 py-2 text-right text-purple-700">{formatCurrency(f.totalLiquidado)}</td>
                      <td className="px-3 py-2 text-right text-green-700">{formatCurrency(f.comision)}</td>
                      <td className="px-3 py-2 text-right text-orange-600">{formatCurrency(f.totalRecargos)}</td>
                      <td className="px-4 py-2 text-right font-bold text-green-800">{formatCurrency(f.beneficioGeneral)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-bold">
                  <tr>
                    <td colSpan={2} className="px-4 py-2.5 text-slate-700">TOTALES</td>
                    <td className="px-3 py-2.5 text-right text-slate-700">{formatCurrency(reporte.tot.totalAlquiler)}</td>
                    <td className="px-3 py-2.5 text-right text-blue-800">{formatCurrency(reporte.tot.totalCobrado)}</td>
                    <td className="px-3 py-2.5 text-right text-purple-800">{formatCurrency(reporte.tot.totalLiquidado)}</td>
                    <td className="px-3 py-2.5 text-right text-green-800">{formatCurrency(reporte.tot.comision)}</td>
                    <td className="px-3 py-2.5 text-right text-orange-700">{formatCurrency(reporte.tot.totalRecargos)}</td>
                    <td className="px-4 py-2.5 text-right text-green-900 text-base">{formatCurrency(reporte.tot.beneficioGeneral)}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
