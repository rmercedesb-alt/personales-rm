import React, { useState, useEffect, useMemo } from 'react'
import { Receipt, CheckSquare, Square, Printer, AlertTriangle, ChevronDown, ChevronRight, Users } from 'lucide-react'
import api, { type PropietarioPendienteLiquidacion, type LiquidacionDetalle, type CoPropietario } from '../../lib/api'
import { formatCurrency, formatDate } from '../../lib/utils'

const esFamiliar = (tipo?: string) => (tipo || '').toUpperCase().startsWith('FAMIL')

/** Reparte un neto entre copropietarios por peso %, cuadrando centavos (método del resto mayor). */
function distribuirNeto(neto: number, copros: CoPropietario[]) {
  if (!copros.length) return []
  const sumaPesos = copros.reduce((s, c) => s + c.pesoPorc, 0)
  const cents = Math.round(neto * 100)
  const base = sumaPesos > 0 ? sumaPesos : copros.length
  const calc = copros.map(c => {
    const exact = cents * (sumaPesos > 0 ? c.pesoPorc / base : 1 / copros.length)
    return { c, exact, piso: Math.floor(exact) }
  })
  let restante = cents - calc.reduce((s, r) => s + r.piso, 0)
  const orden = [...calc].sort((a, b) => (b.exact - b.piso) - (a.exact - a.piso))
  for (let i = 0; i < restante; i++) orden[i % orden.length].piso += 1
  return calc.map(r => ({ nombre: r.c.nombre, pesoPorc: r.c.pesoPorc, monto: r.piso / 100 }))
}

const BRAND = '#1B4F8C'

export default function GenerarLiquidacion() {
  const [busqueda, setBusqueda]       = useState('')
  const [todos, setTodos]             = useState<PropietarioPendienteLiquidacion[]>([])
  const [cargando, setCargando]       = useState(true)
  const [propAbierto, setPropAbierto] = useState<string | null>(null)
  const [seleccionados, setSeleccionados] = useState<Record<string, Set<string>>>({})
  const [generando, setGenerando]     = useState<string | null>(null)
  const [liquidacion, setLiquidacion] = useState<LiquidacionDetalle | null>(null)
  const [confirmacion, setConfirmacion] = useState<{ prop: PropietarioPendienteLiquidacion; idPreview: string } | null>(null)
  const [fechaLiq, setFechaLiq]       = useState('')
  const [error, setError]             = useState('')

  // Cargar todos los propietarios con pagos pendientes al entrar
  useEffect(() => {
    const cargar = async () => {
      setCargando(true)
      try {
        const r = await api.get<{ data: PropietarioPendienteLiquidacion[] }>(
          '/liquidaciones/v5/pagos-pendientes-todos'
        )
        const lista = r.data.data
        setTodos(lista)
        // Seleccionar todos los pagos de todos los propietarios por defecto
        const sel: Record<string, Set<string>> = {}
        lista.forEach(p => { sel[p.idPropietario] = new Set(p.pagos.map(pg => pg.idPago)) })
        setSeleccionados(sel)
      } catch {
        setError('Error al cargar pagos pendientes')
      } finally { setCargando(false) }
    }
    cargar()
  }, [])

  const filtrados = todos.filter(p =>
    !busqueda.trim() ||
    p.nombrePropietario.toLowerCase().includes(busqueda.toLowerCase()) ||
    p.telefono.toLowerCase().includes(busqueda.toLowerCase())
  )

  const togglePago = (idProp: string, idPago: string) => {
    setSeleccionados(prev => {
      const set = new Set(prev[idProp] ?? [])
      set.has(idPago) ? set.delete(idPago) : set.add(idPago)
      return { ...prev, [idProp]: set }
    })
  }

  const toggleTodosProp = (p: PropietarioPendienteLiquidacion) => {
    setSeleccionados(prev => {
      const set = prev[p.idPropietario] ?? new Set()
      const nuevo = set.size === p.pagos.length
        ? new Set<string>()
        : new Set(p.pagos.map(pg => pg.idPago))
      return { ...prev, [p.idPropietario]: nuevo }
    })
  }

  const calcTotales = (p: PropietarioPendienteLiquidacion) => {
    const sel = seleccionados[p.idPropietario] ?? new Set()
    const pagos = p.pagos.filter(pg => sel.has(pg.idPago))
    const totalAlq = pagos.reduce((s, pg) => s + pg.montoAlquiler, 0)
    const totalCom = (() => {
      if (p.tipoComision === 'FIJO') return sel.size > 0 ? p.comisionFija : 0
      const base = pagos.reduce((s, pg) => s + pg.comisionLinea, 0)
      return p.tipoComision === 'MIXTO' ? base + p.comisionFija : base
    })()
    return { totalAlq, totalCom, totalNeto: totalAlq - totalCom, count: sel.size }
  }

  // Abrir pantalla de confirmación con número, fecha editable y detalles
  const abrirConfirmacion = async (p: PropietarioPendienteLiquidacion) => {
    const sel = seleccionados[p.idPropietario] ?? new Set()
    if (sel.size === 0) return
    setGenerando(p.idPropietario)
    setError('')
    try {
      const r = await api.get<{ data: string }>('/liquidaciones/v5/siguiente-id')
      const hoy = new Date()
      const ymd = `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-${String(hoy.getDate()).padStart(2, '0')}`
      setFechaLiq(ymd)
      setConfirmacion({ prop: p, idPreview: r.data.data })
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Error al preparar la liquidación')
    } finally { setGenerando(null) }
  }

  const confirmarGenerar = async () => {
    if (!confirmacion) return
    const p = confirmacion.prop
    const sel = seleccionados[p.idPropietario] ?? new Set()
    if (sel.size === 0) return
    setGenerando(p.idPropietario)
    setError('')
    try {
      const r = await api.post<{ data: LiquidacionDetalle }>('/liquidaciones/v5/generar', {
        idPropietario: p.idPropietario,
        idsPagos: Array.from(sel),
        fecha: fechaLiq,
      })
      setLiquidacion(r.data.data)
      setConfirmacion(null)
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Error al generar la liquidación')
    } finally { setGenerando(null) }
  }

  // ── Pantalla de liquidación generada ──────────────────────────────────────
  if (liquidacion) {
    return (
      <div className="max-w-4xl space-y-4">
        <div className="no-print flex items-center justify-between">
          <h1 className="text-xl font-semibold text-slate-800">Liquidación generada</h1>
          <div className="flex gap-2">
            <button onClick={() => window.print()}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm"
              style={{ background: BRAND }}>
              <Printer size={15} /> Imprimir
            </button>
            <button onClick={() => { setLiquidacion(null); setCargando(true); setTodos([]); setSeleccionados({})
              api.get<{ data: PropietarioPendienteLiquidacion[] }>('/liquidaciones/v5/pagos-pendientes-todos')
                .then(r => {
                  setTodos(r.data.data)
                  const sel: Record<string, Set<string>> = {}
                  r.data.data.forEach(p => { sel[p.idPropietario] = new Set(p.pagos.map(pg => pg.idPago)) })
                  setSeleccionados(sel)
                }).finally(() => setCargando(false))
            }}
              className="px-4 py-2 rounded-lg border border-slate-200 text-slate-600 text-sm hover:bg-slate-50">
              Nueva liquidación
            </button>
          </div>
        </div>
        <LiquidacionRecibo liquidacion={liquidacion} />
      </div>
    )
  }

  // ── Pantalla de confirmación (número, fecha editable, detalles) ───────────
  if (confirmacion) {
    const p = confirmacion.prop
    const sel = seleccionados[p.idPropietario] ?? new Set()
    const pagosSel = p.pagos.filter(pg => sel.has(pg.idPago))
    const t = calcTotales(p)
    return (
      <div className="max-w-4xl space-y-4">
        <div className="flex items-center gap-3">
          <Receipt size={22} style={{ color: BRAND }} />
          <h1 className="text-xl font-semibold text-slate-800">Confirmar Liquidación</h1>
        </div>

        {error && (
          <div className="flex items-center gap-2 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-700">
            <AlertTriangle size={15} /> {error}
          </div>
        )}

        <div className="bg-white rounded-xl border border-slate-200 p-6 space-y-5">
          {/* Encabezado: número + fecha + propietario */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 pb-4 border-b border-slate-200">
            <div>
              <p className="text-xs text-slate-500 uppercase font-medium mb-1">N° Liquidación</p>
              <p className="text-2xl font-bold" style={{ color: BRAND }}>#{confirmacion.idPreview}</p>
            </div>
            <div>
              <label className="block text-xs text-slate-500 uppercase font-medium mb-1">Fecha de la liquidación</label>
              <input type="date"
                className="w-full border border-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                value={fechaLiq}
                onChange={e => setFechaLiq(e.target.value)} />
            </div>
            <div className="text-right md:text-left">
              <p className="text-xs text-slate-500 uppercase font-medium mb-1">Propietario</p>
              <p className="font-bold text-slate-800">{p.nombrePropietario}</p>
              <p className="text-sm text-slate-500">{p.telefono}</p>
            </div>
          </div>

          {/* Detalle de pagos */}
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b-2 border-slate-300 text-xs text-slate-500 uppercase">
                <tr>
                  <th className="text-left py-2">Inquilino</th>
                  <th className="text-left py-2">Inmueble</th>
                  <th className="text-center py-2">Fecha pago</th>
                  <th className="text-right py-2">Alquiler</th>
                  <th className="text-right py-2">Comisión</th>
                  <th className="text-right py-2">Neto</th>
                </tr>
              </thead>
              <tbody>
                {pagosSel.map((pg, i) => (
                  <tr key={pg.idPago} className={`border-b border-slate-100 ${i % 2 === 0 ? '' : 'bg-slate-50'}`}>
                    <td className="py-2 font-medium text-slate-700">{pg.nombreInquilino}</td>
                    <td className="py-2 text-slate-500 text-xs">{pg.descripcionInmueble}</td>
                    <td className="py-2 text-center text-slate-500 text-xs">{formatDate(pg.fechaPago)}</td>
                    <td className="py-2 text-right">{formatCurrency(pg.montoAlquiler)}</td>
                    <td className="py-2 text-right text-orange-600">{formatCurrency(pg.comisionLinea)}</td>
                    <td className="py-2 text-right font-semibold text-green-700">{formatCurrency(pg.netoLinea)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t-2 border-slate-300 font-bold text-sm">
                  <td colSpan={3} className="py-2.5 text-slate-600 text-xs uppercase">{pagosSel.length} pago(s)</td>
                  <td className="py-2.5 text-right">{formatCurrency(t.totalAlq)}</td>
                  <td className="py-2.5 text-right text-orange-600">{formatCurrency(t.totalCom)}</td>
                  <td className="py-2.5 text-right text-green-700 text-base">{formatCurrency(t.totalNeto)}</td>
                </tr>
              </tfoot>
            </table>
          </div>

          {/* Botones */}
          <div className="flex justify-end gap-2 pt-2">
            <button onClick={() => { setConfirmacion(null); setError('') }}
              className="px-5 py-2.5 rounded-lg border border-slate-200 text-slate-600 text-sm hover:bg-slate-50">
              Cancelar
            </button>
            <button onClick={confirmarGenerar}
              disabled={generando === p.idPropietario || !fechaLiq}
              className="flex items-center gap-2 px-6 py-2.5 rounded-lg text-white font-medium text-sm disabled:opacity-50"
              style={{ background: BRAND }}>
              <Receipt size={16} />
              {generando === p.idPropietario ? 'Generando...' : 'Confirmar y Generar'}
            </button>
          </div>
        </div>
      </div>
    )
  }

  // ── Lista principal ────────────────────────────────────────────────────────
  return (
    <div className="max-w-5xl space-y-5">
      <div className="flex items-center gap-3">
        <Receipt size={22} style={{ color: BRAND }} />
        <h1 className="text-xl font-semibold text-slate-800">Generar Liquidación a Propietario</h1>
      </div>

      {/* Buscador */}
      <div className="bg-white rounded-xl border border-slate-200 px-5 py-4">
        <input
          className="w-full border border-slate-200 rounded-lg px-4 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          placeholder="Filtrar por nombre o teléfono del propietario..."
          value={busqueda}
          onChange={e => setBusqueda(e.target.value)}
        />
      </div>

      {error && (
        <div className="flex items-center gap-2 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-700">
          <AlertTriangle size={15} /> {error}
        </div>
      )}

      {cargando && (
        <div className="flex justify-center py-16">
          <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      )}

      {!cargando && filtrados.length === 0 && (
        <div className="text-center py-16 text-slate-400 text-sm">
          No hay propietarios con pagos pendientes de liquidar.
        </div>
      )}

      {/* Tarjeta por propietario */}
      {filtrados.map(p => {
        const abierto  = propAbierto === p.idPropietario
        const sel      = seleccionados[p.idPropietario] ?? new Set()
        const totales  = calcTotales(p)

        return (
          <div key={p.idPropietario} className="bg-white rounded-xl border border-slate-200 overflow-hidden">
            {/* Cabecera del propietario */}
            <button
              className="w-full flex items-center gap-3 px-5 py-4 hover:bg-slate-50 transition-colors text-left"
              onClick={() => setPropAbierto(abierto ? null : p.idPropietario)}>
              <div style={{ color: BRAND }}>
                <ChevronDown size={18} style={{ display: abierto ? '' : 'none' }} />
                <ChevronRight size={18} style={{ display: abierto ? 'none' : '' }} />
              </div>
              <div className="flex-1">
                <p className="font-semibold text-slate-800">{p.nombrePropietario}</p>
                <p className="text-xs text-slate-500">{p.telefono} · {p.tipoComision}{p.comisionPorcentaje > 0 ? ` ${p.comisionPorcentaje}%` : ''}{p.comisionFija > 0 ? ` + ${formatCurrency(p.comisionFija)}` : ''}</p>
              </div>
              <div className="flex gap-6 text-right text-sm">
                <div>
                  <p className="text-xs text-slate-400">Pagos</p>
                  <p className="font-semibold text-slate-700">{p.pagos.length}</p>
                </div>
                <div>
                  <p className="text-xs text-slate-400">Total alquiler</p>
                  <p className="font-semibold text-slate-700">{formatCurrency(p.totalAlquiler)}</p>
                </div>
                <div>
                  <p className="text-xs text-slate-400">Neto propietario</p>
                  <p className="font-bold text-green-700">{formatCurrency(p.totalNeto)}</p>
                </div>
              </div>
            </button>

            {/* Detalle expandido */}
            {abierto && (
              <>
                <div className="border-t border-slate-100 px-5 py-2 flex items-center justify-between bg-slate-50">
                  <p className="text-xs text-slate-500">Selecciona los pagos a incluir</p>
                  <button onClick={() => toggleTodosProp(p)}
                    className="text-xs text-blue-600 hover:underline font-medium">
                    {sel.size === p.pagos.length ? 'Deseleccionar todos' : 'Seleccionar todos'}
                  </button>
                </div>

                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500">
                      <tr>
                        <th className="px-4 py-2 w-10"></th>
                        <th className="text-left px-3 py-2">Inquilino</th>
                        <th className="text-left px-3 py-2">Inmueble</th>
                        <th className="text-center px-3 py-2">Fecha pago</th>
                        <th className="text-center px-3 py-2">Forma pago</th>
                        <th className="text-right px-3 py-2">Alquiler</th>
                        <th className="text-right px-3 py-2">Comisión</th>
                        <th className="text-right px-4 py-2">Neto</th>
                      </tr>
                    </thead>
                    <tbody>
                      {p.pagos.map(pg => {
                        const marcado = sel.has(pg.idPago)
                        return (
                          <tr key={pg.idPago}
                            onClick={() => togglePago(p.idPropietario, pg.idPago)}
                            className={`border-t border-slate-100 cursor-pointer transition-colors ${marcado ? 'bg-blue-50 hover:bg-blue-100' : 'hover:bg-slate-50'}`}>
                            <td className="px-4 py-2 text-center">
                              <CheckSquare size={15} className="text-blue-600 mx-auto" style={{ display: marcado ? '' : 'none' }} />
                              <Square size={15} className="text-slate-300 mx-auto" style={{ display: marcado ? 'none' : '' }} />
                            </td>
                            <td className="px-3 py-2 font-medium text-slate-700">{pg.nombreInquilino}</td>
                            <td className="px-3 py-2 text-slate-500 text-xs">{pg.descripcionInmueble}</td>
                            <td className="px-3 py-2 text-center text-slate-500">{formatDate(pg.fechaPago)}</td>
                            <td className="px-3 py-2 text-center">
                              <span className="px-2 py-0.5 rounded-full text-xs bg-slate-100 text-slate-600">{pg.formaPago}</span>
                            </td>
                            <td className="px-3 py-2 text-right font-medium text-slate-700">{formatCurrency(pg.montoAlquiler)}</td>
                            <td className="px-3 py-2 text-right text-orange-600">{formatCurrency(pg.comisionLinea)}</td>
                            <td className="px-4 py-2 text-right font-semibold text-green-700">{formatCurrency(pg.netoLinea)}</td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>

                {/* Pie con totales y botón generar */}
                <div className="px-5 py-4 border-t border-slate-200 bg-slate-50 flex items-center justify-between gap-4 flex-wrap">
                  <div className="flex gap-6 text-sm">
                    <div>
                      <p className="text-xs text-slate-500">Pagos seleccionados</p>
                      <p className="font-semibold text-slate-700">{totales.count} de {p.pagos.length}</p>
                    </div>
                    <div>
                      <p className="text-xs text-slate-500">Total alquiler</p>
                      <p className="font-semibold text-slate-700">{formatCurrency(totales.totalAlq)}</p>
                    </div>
                    <div>
                      <p className="text-xs text-slate-500">Comisión</p>
                      <p className="font-semibold text-orange-600">{formatCurrency(totales.totalCom)}</p>
                    </div>
                    <div>
                      <p className="text-xs text-slate-500">Neto propietario</p>
                      <p className="font-bold text-green-700 text-base">{formatCurrency(totales.totalNeto)}</p>
                    </div>
                  </div>
                  <button
                    onClick={() => abrirConfirmacion(p)}
                    disabled={totales.count === 0 || generando === p.idPropietario}
                    className="flex items-center gap-2 px-6 py-2.5 rounded-lg text-white font-medium text-sm disabled:opacity-50"
                    style={{ background: BRAND }}>
                    <Receipt size={16} />
                    {generando === p.idPropietario ? 'Procesando...' : 'Generar Liquidación'}
                  </button>
                </div>
              </>
            )}
          </div>
        )
      })}
    </div>
  )
}

// ── Recibo imprimible ──────────────────────────────────────────────────────────
export function LiquidacionRecibo({ liquidacion }: { liquidacion: LiquidacionDetalle }) {
  const { liquidacion: liq, propietario: prop, lineas } = liquidacion
  const totalAlq  = lineas.reduce((s, l) => s + l.montoAlquiler, 0)
  const totalCom  = lineas.reduce((s, l) => s + l.comisionLinea, 0)
  const totalNeto = lineas.reduce((s, l) => s + l.netoLinea, 0)

  // Distribución a copropietarios (solo si el propietario es familiar)
  const familiar = esFamiliar(prop.tipoDueno)
  const [copros, setCopros] = useState<CoPropietario[]>([])
  useEffect(() => {
    if (!familiar) { setCopros([]); return }
    api.get<{ data: CoPropietario[] }>(`/propietarios/${prop.id}/copropietarios`)
      .then(r => setCopros(r.data.data)).catch(() => setCopros([]))
  }, [prop.id, familiar])

  const sumaPesos   = copros.reduce((s, c) => s + c.pesoPorc, 0)
  const distribucion = useMemo(() => distribuirNeto(totalNeto, copros), [totalNeto, copros])

  return (
    <div className="bg-white rounded-xl border border-slate-200 p-6 print:border-0 print:rounded-none print:p-8">
      <div className="flex items-start justify-between border-b border-slate-200 pb-4 mb-4">
        <div>
          <h2 className="text-lg font-bold text-slate-800">LIQUIDACIÓN DE PROPIETARIO</h2>
          <p className="text-2xl font-bold mt-1" style={{ color: '#1B4F8C' }}>#{liq.id}</p>
          <p className="text-xs text-slate-500 mt-1">Fecha: {formatDate(liq.fechaRegistro)}</p>
        </div>
        <div className="text-right">
          <p className="text-xs text-slate-500 uppercase font-medium">Propietario</p>
          <p className="font-bold text-slate-800">{prop.nombre}</p>
          <p className="text-sm text-slate-500">{prop.telefono}</p>
          <p className="text-sm text-slate-500">{prop.cedula}</p>
        </div>
      </div>

      {liq.anulado && (
        <div className="mb-4 px-4 py-2 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700 font-semibold text-center">
          ⚠ ANULADA — {liq.motivoAnulacion}
        </div>
      )}

      <table className="w-full text-sm mb-4">
        <thead>
          <tr className="border-b-2 border-slate-300 text-xs text-slate-500 font-semibold uppercase">
            <th className="text-left py-2">Inquilino</th>
            <th className="text-left py-2">Inmueble</th>
            <th className="text-center py-2">Fecha</th>
            <th className="text-center py-2">Forma pago</th>
            <th className="text-right py-2">Alquiler</th>
            <th className="text-right py-2">Comisión</th>
            <th className="text-right py-2">Neto</th>
          </tr>
        </thead>
        <tbody>
          {lineas.map((l, i) => (
            <tr key={l.idPago} className={`border-b border-slate-100 ${i % 2 === 0 ? '' : 'bg-slate-50'}`}>
              <td className="py-2 font-medium text-slate-700">{l.nombreInquilino}</td>
              <td className="py-2 text-slate-500 text-xs">{l.descripcionInmueble}</td>
              <td className="py-2 text-center text-slate-500 text-xs">{formatDate(l.fechaPago)}</td>
              <td className="py-2 text-center text-xs text-slate-500">{l.formaPago}</td>
              <td className="py-2 text-right">{formatCurrency(l.montoAlquiler)}</td>
              <td className="py-2 text-right text-orange-600">{formatCurrency(l.comisionLinea)}</td>
              <td className="py-2 text-right font-semibold text-green-700">{formatCurrency(l.netoLinea)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr className="border-t-2 border-slate-300 font-bold text-sm">
            <td colSpan={4} className="py-2.5 text-slate-600 text-xs uppercase">
              {lineas.length} pago(s) — {formatDate(liq.fechaDesde)} al {formatDate(liq.fechaHasta)}
            </td>
            <td className="py-2.5 text-right">{formatCurrency(totalAlq)}</td>
            <td className="py-2.5 text-right text-orange-600">{formatCurrency(totalCom)}</td>
            <td className="py-2.5 text-right text-green-700 text-base">{formatCurrency(totalNeto)}</td>
          </tr>
        </tfoot>
      </table>

      {/* Distribución a copropietarios (propietario familiar) */}
      {familiar && (
        <div className="mt-2 mb-4 border border-purple-200 rounded-lg overflow-hidden">
          <div className="px-4 py-2 bg-purple-50 flex items-center gap-2 border-b border-purple-200">
            <Users size={15} className="text-purple-700" />
            <span className="text-sm font-semibold text-purple-800">Distribución del neto a copropietarios</span>
          </div>
          {copros.length === 0 ? (
            <p className="px-4 py-3 text-sm text-slate-400">Sin copropietarios registrados.</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="text-xs text-slate-500 border-b border-slate-200">
                  <th className="text-left px-4 py-2">Copropietario</th>
                  <th className="text-right px-3 py-2">Peso %</th>
                  <th className="text-right px-4 py-2">Monto a recibir</th>
                </tr>
              </thead>
              <tbody>
                {distribucion.map((d, i) => (
                  <tr key={i} className="border-b border-slate-50">
                    <td className="px-4 py-2 font-medium text-slate-700">{d.nombre}</td>
                    <td className="px-3 py-2 text-right text-slate-500">{d.pesoPorc}%</td>
                    <td className="px-4 py-2 text-right font-semibold text-purple-700">{formatCurrency(d.monto)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t-2 border-slate-300 font-bold text-sm bg-purple-50/40">
                  <td className="px-4 py-2 text-slate-700">TOTAL</td>
                  <td className="px-3 py-2 text-right text-slate-600">{sumaPesos}%</td>
                  <td className="px-4 py-2 text-right text-purple-800">{formatCurrency(totalNeto)}</td>
                </tr>
              </tfoot>
            </table>
          )}
          {copros.length > 0 && sumaPesos !== 100 && (
            <p className="px-4 py-2 text-xs text-amber-700 bg-amber-50 border-t border-amber-200">
              ⚠ Los pesos suman {sumaPesos}% (no 100%). El monto se repartió de forma proporcional a los pesos registrados.
            </p>
          )}
        </div>
      )}

      <div className="flex justify-between items-end mt-8 pt-4 border-t border-slate-200">
        <div className="text-center">
          <div className="border-b border-slate-400 w-48 mb-1"></div>
          <p className="text-xs text-slate-500">Firma administrador</p>
        </div>
        <div className="text-center">
          <div className="border-b border-slate-400 w-48 mb-1"></div>
          <p className="text-xs text-slate-500">Recibí conforme — {prop.nombre}</p>
          <p className="text-xs text-slate-400">{prop.telefono}</p>
        </div>
      </div>
    </div>
  )
}
