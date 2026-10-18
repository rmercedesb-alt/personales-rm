import { useState, useEffect, useMemo } from 'react'
import { Printer, FileText, Clock, DollarSign, Wallet, FileSpreadsheet } from 'lucide-react'
import api, {
  type Propietario, type Contrato, type CxCCargo, type CxCPago, type Adelanto,
} from '../../lib/api'
import { formatCurrency, formatDate, estadoBadgeClass, hoyLocal, primerDiaMesLocal } from '../../lib/utils'
import { exportarTablaExcel } from '../../lib/exportExcel'
import MultiSelect from '../../components/MultiSelect'

const BRAND = '#1B4F8C'
type Tab = 'contratos' | 'antiguedad' | 'cobranzas' | 'adelantos'

const diasEntre = (a: string, b: string) =>
  Math.round((new Date(b).getTime() - new Date(a).getTime()) / 86400000)

export default function InformesCobranza() {
  const [tab, setTab] = useState<Tab>('contratos')
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [contratos, setContratos]       = useState<Contrato[]>([])
  const [cxc, setCxc]                   = useState<CxCCargo[]>([])
  const [pagos, setPagos]               = useState<CxCPago[]>([])
  const [adelantos, setAdelantos]       = useState<Adelanto[]>([])
  const [cargando, setCargando]         = useState(true)

  // Filtros compartidos / por reporte
  const [fProp, setFProp]       = useState<string[]>([])
  const [fInq, setFInq]         = useState<string[]>([])
  const [fEstado, setFEstado]   = useState<string[]>([])
  const [desde, setDesde]       = useState(primerDiaMesLocal())
  const [hasta, setHasta]       = useState(hoyLocal())

  useEffect(() => {
    (async () => {
      setCargando(true)
      try {
        const [p, c, x, pg, ad] = await Promise.all([
          api.get<{ data: Propietario[] }>('/propietarios'),
          api.get<{ data: Contrato[] }>('/contratos'),
          api.get<{ data: CxCCargo[] }>('/cxc'),
          api.get<{ data: CxCPago[] }>('/pagos'),
          api.get<{ data: Adelanto[] }>('/adelantos'),
        ])
        setPropietarios(p.data.data)
        setContratos(c.data.data)
        setCxc(x.data.data)
        setPagos(pg.data.data)
        setAdelantos(ad.data.data)
      } finally { setCargando(false) }
    })()
  }, [])

  // Mapas auxiliares
  const propById     = useMemo(() => new Map(propietarios.map(p => [p.id, p])), [propietarios])
  const contratoById = useMemo(() => new Map(contratos.map(c => [c.id, c])), [contratos])
  const cxcById      = useMemo(() => new Map(cxc.map(c => [c.id, c])), [cxc])

  const propDeContrato = (idContrato: string) => contratoById.get(idContrato)?.idPropietario ?? ''
  const nombreProp = (id: string) => propById.get(id)?.nombre ?? '—'

  const opcionesProp = useMemo(
    () => propietarios.map(p => ({ value: p.id, label: p.nombre })).sort((a, b) => a.label.localeCompare(b.label)),
    [propietarios])
  const opcionesInq = useMemo(() => {
    const m = new Map<string, string>()
    cxc.forEach(c => m.set(c.idInquilino, c.nombreInquilino))
    return Array.from(m.entries()).map(([value, label]) => ({ value, label })).sort((a, b) => a.label.localeCompare(b.label))
  }, [cxc])

  const tabs: { key: Tab; label: string; icon: React.ElementType }[] = [
    { key: 'contratos',  label: 'Contratos',           icon: FileText },
    { key: 'antiguedad', label: 'Antigüedad de Saldo', icon: Clock },
    { key: 'cobranzas',  label: 'Cobranzas / Pagos',   icon: DollarSign },
    { key: 'adelantos',  label: 'Adelantos',           icon: Wallet },
  ]

  // ── 1. CONTRATOS: filtro propietario, ordenado por propietario + estado ────
  const contratosReporte = useMemo(() => {
    let lista = contratos
    if (fProp.length) lista = lista.filter(c => fProp.includes(c.idPropietario))
    return lista.map(c => ({ ...c, nombreProp: nombreProp(c.idPropietario) }))
      .sort((a, b) => a.nombreProp.localeCompare(b.nombreProp) || a.estado.localeCompare(b.estado))
  }, [contratos, fProp, propById])

  // ── 2. ANTIGÜEDAD DE SALDO: CxC con saldo, por propietario + inquilino ─────
  const antiguedad = useMemo(() => {
    let lista = cxc.filter(c => c.saldoPendiente > 0)
    if (fProp.length) lista = lista.filter(c => fProp.includes(propDeContrato(c.idContrato)))
    if (fInq.length)  lista = lista.filter(c => fInq.includes(c.idInquilino))
    const grupos = new Map<string, CxCCargo[]>()
    lista.forEach(c => {
      const idp = propDeContrato(c.idContrato)
      if (!grupos.has(idp)) grupos.set(idp, [])
      grupos.get(idp)!.push(c)
    })
    return Array.from(grupos.entries())
      .map(([idp, items]) => ({
        idProp: idp, nombre: nombreProp(idp),
        items: items.sort((a, b) => b.diasMora - a.diasMora),
        totalSaldo: items.reduce((s, i) => s + i.saldoPendiente, 0),
        promDias: items.length ? Math.round(items.reduce((s, i) => s + i.diasMora, 0) / items.length) : 0,
      }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre))
  }, [cxc, fProp, fInq, contratoById, propById])

  // ── 3. COBRANZAS / PAGOS: rango fecha, propietario, estado custodia ────────
  const opcionesEstadoPago = useMemo(
    () => Array.from(new Set(pagos.map(p => p.estadoCustodia).filter(Boolean))).map(e => ({ value: e, label: e })),
    [pagos])

  const cobranzas = useMemo(() => {
    let lista = pagos.filter(p => !p.anulado && !p.esOtroIngreso)
    if (desde) lista = lista.filter(p => p.fechaPago.slice(0, 10) >= desde)
    if (hasta) lista = lista.filter(p => p.fechaPago.slice(0, 10) <= hasta)
    if (fProp.length)   lista = lista.filter(p => fProp.includes(propDeContrato(p.idContrato)))
    if (fEstado.length) lista = lista.filter(p => fEstado.includes(p.estadoCustodia))

    const conRetorno = lista.map(p => {
      const venc = cxcById.get(p.idCxC)?.fechaVencimiento
      const diasRetorno = venc ? diasEntre(venc, p.fechaPago) : 0
      return { ...p, idProp: propDeContrato(p.idContrato), diasRetorno }
    })
    const grupos = new Map<string, typeof conRetorno>()
    conRetorno.forEach(p => {
      if (!grupos.has(p.idProp)) grupos.set(p.idProp, [])
      grupos.get(p.idProp)!.push(p)
    })
    const porProp = Array.from(grupos.entries()).map(([idp, items]) => ({
      idProp: idp, nombre: nombreProp(idp), items,
      total: items.reduce((s, i) => s + i.totalPagado, 0),
      promRetorno: items.length ? Math.round(items.reduce((s, i) => s + i.diasRetorno, 0) / items.length) : 0,
    })).sort((a, b) => a.nombre.localeCompare(b.nombre))

    const totalEmpresa = conRetorno.reduce((s, i) => s + i.totalPagado, 0)
    const promEmpresa  = conRetorno.length ? Math.round(conRetorno.reduce((s, i) => s + i.diasRetorno, 0) / conRetorno.length) : 0
    return { porProp, totalEmpresa, promEmpresa, count: conRetorno.length }
  }, [pagos, desde, hasta, fProp, fEstado, contratoById, cxcById, propById])

  // ── 4. ADELANTOS: propietario, rango fecha, estado ─────────────────────────
  const opcionesEstadoAd = useMemo(
    () => Array.from(new Set(adelantos.map(a => a.estado).filter(Boolean))).map(e => ({ value: e, label: e })),
    [adelantos])

  const adelantosReporte = useMemo(() => {
    let lista = adelantos
    if (desde) lista = lista.filter(a => a.fecha.slice(0, 10) >= desde)
    if (hasta) lista = lista.filter(a => a.fecha.slice(0, 10) <= hasta)
    if (fProp.length)   lista = lista.filter(a => fProp.includes(a.idPropietario))
    if (fEstado.length) lista = lista.filter(a => fEstado.includes(a.estado))
    return lista.sort((a, b) => a.nombrePropietario.localeCompare(b.nombrePropietario) || a.fecha.localeCompare(b.fecha))
  }, [adelantos, desde, hasta, fProp, fEstado])

  const exportar = () => {
    if (tab === 'contratos') {
      exportarTablaExcel('Contratos',
        ['Propietario', 'Inmueble', 'Inquilino', 'Inicio', 'Fin', 'Alquiler', 'Estado'],
        contratosReporte.map(c => [c.nombreProp, c.nombreInmueble, c.nombreInquilino,
          formatDate(c.fechaInicio), formatDate(c.fechaFin), c.montoAlquiler, c.estado]))
    } else if (tab === 'antiguedad') {
      const filas: (string | number)[][] = []
      antiguedad.forEach(g => g.items.forEach(c =>
        filas.push([g.nombre, c.nombreInquilino, c.descripcion, formatDate(c.fechaVencimiento), c.diasMora, c.saldoPendiente])))
      exportarTablaExcel('Antiguedad_Saldo',
        ['Propietario', 'Inquilino', 'Concepto', 'Vencimiento', 'Días vencido', 'Saldo'], filas)
    } else if (tab === 'cobranzas') {
      const filas: (string | number)[][] = []
      cobranzas.porProp.forEach(g => g.items.forEach(p =>
        filas.push([g.nombre, p.idRecibo, p.nombreInquilino, formatDate(p.fechaPago), p.diasRetorno, p.estadoCustodia, p.totalPagado])))
      exportarTablaExcel('Cobranzas',
        ['Propietario', 'Recibo', 'Inquilino', 'Fecha pago', 'Días retorno', 'Estado', 'Total pagado'], filas)
    } else {
      exportarTablaExcel('Adelantos',
        ['Propietario', 'Fecha', 'Concepto', 'Estado', 'Monto'],
        adelantosReporte.map(a => [a.nombrePropietario, formatDate(a.fecha), a.concepto, a.estado, a.monto]))
    }
  }

  return (
    <div className="max-w-6xl space-y-5">
      <div className="no-print flex items-center justify-between">
        <h1 className="text-xl font-semibold text-slate-800">Informes de Cobranza</h1>
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

      {/* Tabs */}
      <div className="no-print flex gap-1 border-b border-slate-200 flex-wrap">
        {tabs.map(t => (
          <button key={t.key} onClick={() => { setTab(t.key); setFEstado([]) }}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors ${
              tab === t.key ? 'border-blue-600 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700'
            }`}>
            <t.icon size={15} /> {t.label}
          </button>
        ))}
      </div>

      {/* Filtros por reporte */}
      <div className="no-print bg-white rounded-xl border border-slate-200 p-4 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
        <div>
          <label className="block text-xs text-slate-500 mb-1">Propietario</label>
          <MultiSelect options={opcionesProp} selected={fProp} onChange={setFProp} allLabel="Todos los propietarios" />
        </div>
        {tab === 'antiguedad' && (
          <div>
            <label className="block text-xs text-slate-500 mb-1">Inquilino</label>
            <MultiSelect options={opcionesInq} selected={fInq} onChange={setFInq} allLabel="Todos los inquilinos" />
          </div>
        )}
        {(tab === 'cobranzas' || tab === 'adelantos') && (
          <>
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
              <label className="block text-xs text-slate-500 mb-1">Estado</label>
              <MultiSelect
                options={tab === 'cobranzas' ? opcionesEstadoPago : opcionesEstadoAd}
                selected={fEstado} onChange={setFEstado} allLabel="Todos los estados" />
            </div>
          </>
        )}
      </div>

      {cargando ? (
        <div className="flex justify-center py-16">
          <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-auto max-h-[70vh] print:max-h-none print:overflow-visible print:border-0">
          {/* ── CONTRATOS ── */}
          {tab === 'contratos' && (
            <>
              <div className="px-5 py-4 border-b border-slate-200">
                <h2 className="text-base font-bold text-slate-800">REPORTE DE CONTRATOS ({contratosReporte.length})</h2>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 sticky top-0 z-10">
                    <tr>
                      <th className="text-left px-4 py-2.5">Propietario</th>
                      <th className="text-left px-3 py-2.5">Inmueble</th>
                      <th className="text-left px-3 py-2.5">Inquilino</th>
                      <th className="text-center px-3 py-2.5">Inicio</th>
                      <th className="text-center px-3 py-2.5">Fin</th>
                      <th className="text-right px-3 py-2.5">Alquiler</th>
                      <th className="text-center px-4 py-2.5">Estado</th>
                    </tr>
                  </thead>
                  <tbody>
                    {contratosReporte.map((c, i) => (
                      <tr key={c.id} className={`border-t border-slate-100 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                        <td className="px-4 py-2 font-medium text-slate-700">{c.nombreProp}</td>
                        <td className="px-3 py-2 text-slate-500">{c.nombreInmueble}</td>
                        <td className="px-3 py-2 text-slate-500">{c.nombreInquilino}</td>
                        <td className="px-3 py-2 text-center text-slate-500">{formatDate(c.fechaInicio)}</td>
                        <td className="px-3 py-2 text-center text-slate-500">{formatDate(c.fechaFin)}</td>
                        <td className="px-3 py-2 text-right text-slate-700">{formatCurrency(c.montoAlquiler)}</td>
                        <td className="px-4 py-2 text-center">
                          <span className={`text-xs px-2 py-0.5 rounded-full ${estadoBadgeClass(c.estado)}`}>{c.estado}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}

          {/* ── ANTIGÜEDAD DE SALDO ── */}
          {tab === 'antiguedad' && (
            <>
              <div className="px-5 py-4 border-b border-slate-200">
                <h2 className="text-base font-bold text-slate-800">ANTIGÜEDAD DE SALDO (Cuentas por Cobrar)</h2>
              </div>
              {antiguedad.length === 0 ? (
                <p className="text-center text-slate-400 py-10 text-sm">No hay cuentas con saldo pendiente.</p>
              ) : antiguedad.map(g => (
                <div key={g.idProp} className="border-t border-slate-100">
                  <div className="px-5 py-2 bg-blue-50 text-sm flex justify-between items-center sticky top-0 z-10">
                    <span className="font-semibold text-blue-800">{g.nombre}</span>
                    <span className="text-xs text-blue-700">
                      Prom. días vencido: <strong>{g.promDias}</strong> · Saldo total: <strong>{formatCurrency(g.totalSaldo)}</strong>
                    </span>
                  </div>
                  <table className="w-full text-sm">
                    <thead className="text-xs text-slate-400">
                      <tr>
                        <th className="text-left px-5 py-1.5">Inquilino</th>
                        <th className="text-left px-3 py-1.5">Concepto</th>
                        <th className="text-center px-3 py-1.5">Vencimiento</th>
                        <th className="text-center px-3 py-1.5">Días vencido</th>
                        <th className="text-right px-5 py-1.5">Saldo</th>
                      </tr>
                    </thead>
                    <tbody>
                      {g.items.map((c, i) => (
                        <tr key={c.id} className={`border-t border-slate-50 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                          <td className="px-5 py-2 font-medium text-slate-700">{c.nombreInquilino}</td>
                          <td className="px-3 py-2 text-slate-500">{c.descripcion}</td>
                          <td className="px-3 py-2 text-center text-slate-500">{formatDate(c.fechaVencimiento)}</td>
                          <td className="px-3 py-2 text-center">
                            <span className={`font-semibold ${c.diasMora > 30 ? 'text-red-600' : c.diasMora > 0 ? 'text-orange-600' : 'text-slate-500'}`}>
                              {c.diasMora}
                            </span>
                          </td>
                          <td className="px-5 py-2 text-right font-semibold text-slate-700">{formatCurrency(c.saldoPendiente)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ))}
            </>
          )}

          {/* ── COBRANZAS / PAGOS ── */}
          {tab === 'cobranzas' && (
            <>
              <div className="px-5 py-4 border-b border-slate-200 flex justify-between items-center flex-wrap gap-2">
                <h2 className="text-base font-bold text-slate-800">REPORTE DE COBRANZAS ({cobranzas.count})</h2>
                <span className="text-xs text-slate-600">
                  Total empresa: <strong>{formatCurrency(cobranzas.totalEmpresa)}</strong> ·
                  Prom. días retorno empresa: <strong>{cobranzas.promEmpresa}</strong>
                </span>
              </div>
              {cobranzas.porProp.length === 0 ? (
                <p className="text-center text-slate-400 py-10 text-sm">No hay cobros con esos filtros.</p>
              ) : cobranzas.porProp.map(g => (
                <div key={g.idProp} className="border-t border-slate-100">
                  <div className="px-5 py-2 bg-blue-50 text-sm flex justify-between items-center sticky top-0 z-10">
                    <span className="font-semibold text-blue-800">{g.nombre}</span>
                    <span className="text-xs text-blue-700">
                      Prom. días retorno: <strong>{g.promRetorno}</strong> · Total: <strong>{formatCurrency(g.total)}</strong>
                    </span>
                  </div>
                  <table className="w-full text-sm">
                    <thead className="text-xs text-slate-400">
                      <tr>
                        <th className="text-left px-5 py-1.5">Recibo</th>
                        <th className="text-left px-3 py-1.5">Inquilino</th>
                        <th className="text-center px-3 py-1.5">Fecha pago</th>
                        <th className="text-center px-3 py-1.5">Días retorno</th>
                        <th className="text-center px-3 py-1.5">Estado</th>
                        <th className="text-right px-5 py-1.5">Total pagado</th>
                      </tr>
                    </thead>
                    <tbody>
                      {g.items.map((p, i) => (
                        <tr key={p.id} className={`border-t border-slate-50 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                          <td className="px-5 py-2 font-mono text-xs text-slate-600">{p.idRecibo}</td>
                          <td className="px-3 py-2 text-slate-700">{p.nombreInquilino}</td>
                          <td className="px-3 py-2 text-center text-slate-500">{formatDate(p.fechaPago)}</td>
                          <td className="px-3 py-2 text-center">
                            <span className={`font-semibold ${p.diasRetorno > 30 ? 'text-red-600' : p.diasRetorno > 0 ? 'text-orange-600' : 'text-green-600'}`}>
                              {p.diasRetorno}
                            </span>
                          </td>
                          <td className="px-3 py-2 text-center">
                            <span className={`text-xs px-2 py-0.5 rounded-full ${estadoBadgeClass(p.estadoCustodia)}`}>{p.estadoCustodia}</span>
                          </td>
                          <td className="px-5 py-2 text-right font-semibold text-green-700">{formatCurrency(p.totalPagado)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ))}
            </>
          )}

          {/* ── ADELANTOS ── */}
          {tab === 'adelantos' && (
            <>
              <div className="px-5 py-4 border-b border-slate-200 flex justify-between items-center flex-wrap gap-2">
                <h2 className="text-base font-bold text-slate-800">REPORTE DE ADELANTOS ({adelantosReporte.length})</h2>
                <span className="text-xs text-slate-600">
                  Total: <strong>{formatCurrency(adelantosReporte.reduce((s, a) => s + a.monto, 0))}</strong>
                </span>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 sticky top-0 z-10">
                    <tr>
                      <th className="text-left px-4 py-2.5">Propietario</th>
                      <th className="text-center px-3 py-2.5">Fecha</th>
                      <th className="text-left px-3 py-2.5">Concepto</th>
                      <th className="text-center px-3 py-2.5">Estado</th>
                      <th className="text-right px-4 py-2.5">Monto</th>
                    </tr>
                  </thead>
                  <tbody>
                    {adelantosReporte.map((a, i) => (
                      <tr key={a.id} className={`border-t border-slate-100 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                        <td className="px-4 py-2 font-medium text-slate-700">{a.nombrePropietario}</td>
                        <td className="px-3 py-2 text-center text-slate-500">{formatDate(a.fecha)}</td>
                        <td className="px-3 py-2 text-slate-500">{a.concepto}</td>
                        <td className="px-3 py-2 text-center">
                          <span className={`text-xs px-2 py-0.5 rounded-full ${estadoBadgeClass(a.estado)}`}>{a.estado}</span>
                        </td>
                        <td className="px-4 py-2 text-right font-semibold text-slate-700">{formatCurrency(a.monto)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>
      )}
    </div>
  )
}
