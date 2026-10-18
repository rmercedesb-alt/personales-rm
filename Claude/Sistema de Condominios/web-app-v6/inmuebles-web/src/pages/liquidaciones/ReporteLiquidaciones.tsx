import React, { useState, useEffect, useMemo } from 'react'
import { BarChart2, Search, Printer, AlertTriangle, FileSpreadsheet } from 'lucide-react'
import api, { type Propietario, type LiquidacionListItem, type LiquidacionDetalle } from '../../lib/api'
import { formatCurrency, formatDate } from '../../lib/utils'
import { exportarTablaExcel } from '../../lib/exportExcel'
import MultiSelect from '../../components/MultiSelect'

const BRAND = '#1B4F8C'

export default function ReporteLiquidaciones() {
  const [filtros, setFiltros] = useState({
    desde: '', hasta: '',
    idLiqDesde: '', idLiqHasta: '', modo: 'RESUMEN' as 'RESUMEN' | 'DETALLADO'
  })
  const [fProp, setFProp] = useState<string[]>([])
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [generando, setGenerando] = useState(false)
  const [items, setItems]   = useState<LiquidacionListItem[]>([])
  const [detalles, setDetalles] = useState<LiquidacionDetalle[]>([])
  const [error, setError]   = useState('')

  useEffect(() => {
    api.get<{ data: Propietario[] }>('/propietarios')
      .then(r => setPropietarios(r.data.data)).catch(() => {})
  }, [])

  const opcionesProp = useMemo(
    () => propietarios.map(p => ({ value: p.id, label: p.nombre })).sort((a, b) => a.label.localeCompare(b.label)),
    [propietarios])

  const generarReporte = async () => {
    setGenerando(true)
    setError('')
    setItems([])
    setDetalles([])
    try {
      const params = new URLSearchParams()
      if (filtros.desde)         params.set('desde',         filtros.desde)
      if (filtros.hasta)         params.set('hasta',         filtros.hasta)
      params.set('incluirAnuladas', 'false')

      const r = await api.get<{ data: LiquidacionListItem[] }>(`/liquidaciones/v5/buscar?${params}`)
      let lista = r.data.data

      // Filtro por propietarios seleccionados (vacío = todos)
      if (fProp.length) lista = lista.filter(l => fProp.includes(l.idPropietario))
      // Filtro por rango de número
      if (filtros.idLiqDesde) lista = lista.filter(l => l.id >= filtros.idLiqDesde)
      if (filtros.idLiqHasta) lista = lista.filter(l => l.id <= filtros.idLiqHasta)

      setItems(lista)

      if (filtros.modo === 'DETALLADO') {
        const dets = await Promise.all(
          lista.map(l => api.get<{ data: LiquidacionDetalle }>(`/liquidaciones/${l.id}/detalle`)
            .then(r => r.data.data))
        )
        setDetalles(dets)
      }
    } catch (e: any) {
      setError(e.response?.data?.message ?? 'Error al generar el reporte')
    } finally { setGenerando(false) }
  }

  const totalCobrado = items.reduce((s, i) => s + i.totalCobrado, 0)
  const totalComision = items.reduce((s, i) => s + i.comision, 0)
  const totalNeto = items.reduce((s, i) => s + i.netoPropietario, 0)

  const exportar = () => {
    exportarTablaExcel('Liquidaciones',
      ['N° Liquidación', 'Propietario', 'Fecha', 'Pagos', 'Total cobrado', 'Comisión', 'Neto propietario'],
      items.map(i => [i.id, i.nombrePropietario, formatDate(i.fechaRegistro), i.cantidadPagos,
        i.totalCobrado, i.comision, i.netoPropietario]))
  }

  return (
    <div className="max-w-5xl space-y-5">
      <div className="no-print flex items-center gap-3">
        <BarChart2 size={22} style={{ color: BRAND }} />
        <h1 className="text-xl font-semibold text-slate-800">Reportes de Liquidaciones</h1>
      </div>

      {/* Filtros */}
      <div className="no-print bg-white rounded-xl border border-slate-200 p-5 space-y-4">
        <h2 className="text-sm font-semibold text-slate-700">Parámetros del reporte</h2>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
          <div>
            <label className="block text-xs text-slate-500 mb-1">Fecha desde</label>
            <input type="date"
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm"
              value={filtros.desde}
              onChange={e => setFiltros(f => ({ ...f, desde: e.target.value }))} />
          </div>
          <div>
            <label className="block text-xs text-slate-500 mb-1">Fecha hasta</label>
            <input type="date"
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm"
              value={filtros.hasta}
              onChange={e => setFiltros(f => ({ ...f, hasta: e.target.value }))} />
          </div>

          <div>
            <label className="block text-xs text-slate-500 mb-1">Propietario(s)</label>
            <MultiSelect options={opcionesProp} selected={fProp} onChange={setFProp} allLabel="Todos los propietarios" />
          </div>

          <div>
            <label className="block text-xs text-slate-500 mb-1">N° Liquidación desde</label>
            <input
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm"
              placeholder="LIQ-0001"
              value={filtros.idLiqDesde}
              onChange={e => setFiltros(f => ({ ...f, idLiqDesde: e.target.value }))} />
          </div>
          <div>
            <label className="block text-xs text-slate-500 mb-1">N° Liquidación hasta</label>
            <input
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm"
              placeholder="LIQ-9999"
              value={filtros.idLiqHasta}
              onChange={e => setFiltros(f => ({ ...f, idLiqHasta: e.target.value }))} />
          </div>

          <div>
            <label className="block text-xs text-slate-500 mb-1">Tipo de reporte</label>
            <select
              className="w-full border border-slate-200 rounded-lg px-3 py-2 text-sm"
              value={filtros.modo}
              onChange={e => setFiltros(f => ({ ...f, modo: e.target.value as any }))}>
              <option value="RESUMEN">Resumen</option>
              <option value="DETALLADO">Detallado (con líneas de pago)</option>
            </select>
          </div>
        </div>

        <div className="flex gap-2">
          <button onClick={generarReporte} disabled={generando}
            className="flex items-center gap-2 px-5 py-2 rounded-lg text-white text-sm"
            style={{ background: BRAND }}>
            <Search size={15} /> {generando ? 'Generando...' : 'Generar Reporte'}
          </button>
          {items.length > 0 && (
            <>
              <button onClick={exportar}
                className="flex items-center gap-2 px-4 py-2 rounded-lg border border-slate-200 text-slate-600 text-sm hover:bg-slate-50">
                <FileSpreadsheet size={15} /> Excel
              </button>
              <button onClick={() => window.print()}
                className="flex items-center gap-2 px-4 py-2 rounded-lg border border-slate-200 text-slate-600 text-sm hover:bg-slate-50">
                <Printer size={15} /> Imprimir / PDF
              </button>
            </>
          )}
        </div>
      </div>

      {error && (
        <div className="flex items-center gap-2 bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-700 no-print">
          <AlertTriangle size={15} /> {error}
        </div>
      )}

      {/* Reporte */}
      {items.length > 0 && (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden print:border-0">
          {/* Encabezado imprimible */}
          <div className="px-5 py-4 border-b border-slate-200">
            <h2 className="text-base font-bold text-slate-800">
              REPORTE DE LIQUIDACIONES — {filtros.modo}
            </h2>
            <div className="flex gap-6 text-xs text-slate-500 mt-1 flex-wrap">
              {filtros.desde && <span>Desde: {filtros.desde}</span>}
              {filtros.hasta && <span>Hasta: {filtros.hasta}</span>}
              {fProp.length > 0 && <span>Propietarios: {fProp.length} seleccionado(s)</span>}
              {filtros.idLiqDesde && <span>Liq. desde: {filtros.idLiqDesde}</span>}
              {filtros.idLiqHasta && <span>Liq. hasta: {filtros.idLiqHasta}</span>}
            </div>
          </div>

          {/* Tabla resumen */}
          <div className="overflow-auto max-h-[65vh] print:max-h-none print:overflow-visible">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 font-medium sticky top-0 z-10">
                <tr>
                  <th className="text-left px-4 py-2.5">N° Liquidación</th>
                  <th className="text-left px-3 py-2.5">Propietario</th>
                  <th className="text-center px-3 py-2.5">Fecha</th>
                  <th className="text-center px-3 py-2.5">Pagos</th>
                  <th className="text-right px-3 py-2.5">Total cobrado</th>
                  <th className="text-right px-3 py-2.5">Comisión</th>
                  <th className="text-right px-4 py-2.5">Neto propietario</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item, idx) => (
                  <React.Fragment key={item.id}>
                    <tr className={`border-t border-slate-100 ${idx % 2 === 0 ? '' : 'bg-slate-50'}`}>
                      <td className="px-4 py-2 font-mono font-semibold text-blue-700">{item.id}</td>
                      <td className="px-3 py-2 font-medium text-slate-700">{item.nombrePropietario}</td>
                      <td className="px-3 py-2 text-center text-slate-500">{formatDate(item.fechaRegistro)}</td>
                      <td className="px-3 py-2 text-center text-slate-500">{item.cantidadPagos}</td>
                      <td className="px-3 py-2 text-right text-slate-700">{formatCurrency(item.totalCobrado)}</td>
                      <td className="px-3 py-2 text-right text-orange-600">{formatCurrency(item.comision)}</td>
                      <td className="px-4 py-2 text-right font-semibold text-green-700">{formatCurrency(item.netoPropietario)}</td>
                    </tr>

                    {/* Detalle por liquidación (modo DETALLADO) */}
                    {filtros.modo === 'DETALLADO' && detalles[idx] && (
                      <tr className="border-t border-blue-100">
                        <td colSpan={7} className="px-4 py-2 bg-blue-50">
                          <table className="w-full text-xs">
                            <thead>
                              <tr className="text-slate-500">
                                <th className="text-left py-1">Inquilino</th>
                                <th className="text-left py-1">Inmueble</th>
                                <th className="text-center py-1">Fecha pago</th>
                                <th className="text-right py-1">Alquiler</th>
                                <th className="text-right py-1">Comisión</th>
                                <th className="text-right py-1">Neto</th>
                              </tr>
                            </thead>
                            <tbody>
                              {detalles[idx].lineas.map(l => (
                                <tr key={l.idPago} className="border-t border-blue-100">
                                  <td className="py-1 text-slate-700">{l.nombreInquilino}</td>
                                  <td className="py-1 text-slate-500">{l.descripcionInmueble}</td>
                                  <td className="py-1 text-center text-slate-500">{formatDate(l.fechaPago)}</td>
                                  <td className="py-1 text-right">{formatCurrency(l.montoAlquiler)}</td>
                                  <td className="py-1 text-right text-orange-600">{formatCurrency(l.comisionLinea)}</td>
                                  <td className="py-1 text-right text-green-700">{formatCurrency(l.netoLinea)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                ))}
              </tbody>
              <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-bold">
                <tr>
                  <td colSpan={4} className="px-4 py-2.5 text-sm text-slate-700">
                    TOTAL GENERAL — {items.length} liquidación(es)
                  </td>
                  <td className="px-3 py-2.5 text-right text-slate-800">{formatCurrency(totalCobrado)}</td>
                  <td className="px-3 py-2.5 text-right text-orange-700">{formatCurrency(totalComision)}</td>
                  <td className="px-4 py-2.5 text-right text-green-800 text-base">{formatCurrency(totalNeto)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      )}

      {items.length === 0 && !generando && !error && (
        <div className="text-center py-12 text-slate-400 text-sm">
          Configura los parámetros y presiona "Generar Reporte"
        </div>
      )}
    </div>
  )
}
