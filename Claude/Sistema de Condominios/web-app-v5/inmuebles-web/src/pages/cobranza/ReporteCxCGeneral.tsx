import { useState, useEffect, useMemo } from 'react'
import { FileSpreadsheet, Printer, Table2 } from 'lucide-react'
import api, {
  type Propietario, type Inmueble, type Contrato, type CxCCargo,
} from '../../lib/api'
import { formatCurrency, formatDate } from '../../lib/utils'
import MultiSelect from '../../components/MultiSelect'

const BRAND = '#1B4F8C'

interface Fila {
  codProp: string; nomProp: string
  codInmueble: string; descInmueble: string
  codInquilino: string; nomInquilino: string
  codContrato: string; valorContrato: number
  fechaInicial: string; fechaFinal: string
  concepto: string; valorCxC: number; recargos: number
}

// Exporta filas a un archivo .xls que Excel abre con columnas y números reales
function exportarExcel(nombre: string, headers: string[], filas: (string | number)[][]) {
  const esc = (v: string | number) =>
    typeof v === 'number'
      ? `<td style="mso-number-format:'0.00'">${v}</td>`
      : `<td>${String(v).replace(/&/g, '&amp;').replace(/</g, '&lt;')}</td>`
  const thead = '<tr>' + headers.map(h => `<th style="background:#1B4F8C;color:#fff">${h}</th>`).join('') + '</tr>'
  const tbody = filas.map(f => '<tr>' + f.map(esc).join('') + '</tr>').join('')
  const html =
    `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">` +
    `<head><meta charset="utf-8"><!--[if gte mso 9]><xml><x:ExcelWorkbook><x:ExcelWorksheets><x:ExcelWorksheet>` +
    `<x:Name>CxC</x:Name><x:WorksheetOptions><x:DisplayGridlines/></x:WorksheetOptions></x:ExcelWorksheet>` +
    `</x:ExcelWorksheets></x:ExcelWorkbook></xml><![endif]--></head>` +
    `<body><table border="1">${thead}${tbody}</table></body></html>`
  const blob = new Blob(['﻿', html], { type: 'application/vnd.ms-excel' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${nombre}.xls`
  a.click()
  URL.revokeObjectURL(url)
}

export default function ReporteCxCGeneral() {
  const [propietarios, setPropietarios] = useState<Propietario[]>([])
  const [inmuebles, setInmuebles]       = useState<Inmueble[]>([])
  const [contratos, setContratos]       = useState<Contrato[]>([])
  const [cxc, setCxc]                   = useState<CxCCargo[]>([])
  const [cargando, setCargando]         = useState(true)
  const [fProp, setFProp]               = useState<string[]>([])
  const [soloPendientes, setSoloPendientes] = useState(true)

  useEffect(() => {
    (async () => {
      setCargando(true)
      try {
        const [p, m, c, x] = await Promise.all([
          api.get<{ data: Propietario[] }>('/propietarios'),
          api.get<{ data: Inmueble[] }>('/inmuebles'),
          api.get<{ data: Contrato[] }>('/contratos'),
          api.get<{ data: CxCCargo[] }>('/cxc'),
        ])
        setPropietarios(p.data.data)
        setInmuebles(m.data.data)
        setContratos(c.data.data)
        setCxc(x.data.data)
      } finally { setCargando(false) }
    })()
  }, [])

  const propById     = useMemo(() => new Map(propietarios.map(p => [p.id, p])), [propietarios])
  const inmuebleById = useMemo(() => new Map(inmuebles.map(i => [i.id, i])), [inmuebles])
  const contratoById = useMemo(() => new Map(contratos.map(c => [c.id, c])), [contratos])

  const opcionesProp = useMemo(
    () => propietarios.map(p => ({ value: p.id, label: p.nombre })).sort((a, b) => a.label.localeCompare(b.label)),
    [propietarios])

  const filas: Fila[] = useMemo(() => {
    let lista = cxc
    if (soloPendientes) lista = lista.filter(c => c.saldoPendiente > 0)
    return lista.map(c => {
      const contrato = contratoById.get(c.idContrato)
      const idProp   = contrato?.idPropietario ?? inmuebleById.get(c.idInmueble)?.idPropietario ?? ''
      const prop     = propById.get(idProp)
      const inmueble = inmuebleById.get(c.idInmueble)
      return {
        codProp: idProp,
        nomProp: prop?.nombre ?? inmueble?.nombrePropietario ?? '—',
        codInmueble: c.idInmueble,
        descInmueble: inmueble?.descripcion ?? '—',
        codInquilino: c.idInquilino,
        nomInquilino: c.nombreInquilino,
        codContrato: c.idContrato,
        valorContrato: contrato?.montoAlquiler ?? 0,
        fechaInicial: contrato?.fechaInicio ?? '',
        fechaFinal: contrato?.fechaFin ?? '',
        concepto: c.descripcion || c.tipoCargo,
        valorCxC: c.montoOriginal,
        recargos: c.montoMora,
      }
    })
    .filter(f => !fProp.length || fProp.includes(f.codProp))
    .sort((a, b) => a.nomProp.localeCompare(b.nomProp) || a.codInmueble.localeCompare(b.codInmueble))
  }, [cxc, soloPendientes, fProp, propById, inmuebleById, contratoById])

  const totValorCxC = filas.reduce((s, f) => s + f.valorCxC, 0)
  const totRecargos = filas.reduce((s, f) => s + f.recargos, 0)

  const HEADERS = [
    'Cód. Propietario', 'Nombre Propietario', 'Cód. Inmueble', 'Descripción Inmueble',
    'Cód. Inquilino', 'Nombre Inquilino', 'Cód. Contrato', 'Valor Contrato',
    'Fecha Inicial', 'Fecha Final', 'Concepto CxC', 'Valor CxC', 'Recargos Pendientes',
  ]

  const exportar = () => {
    const datos = filas.map(f => [
      f.codProp, f.nomProp, f.codInmueble, f.descInmueble,
      f.codInquilino, f.nomInquilino, f.codContrato, f.valorContrato,
      f.fechaInicial ? formatDate(f.fechaInicial) : '', f.fechaFinal ? formatDate(f.fechaFinal) : '',
      f.concepto, f.valorCxC, f.recargos,
    ])
    exportarExcel(`CxC_General_${new Date().toISOString().slice(0, 10)}`, HEADERS, datos)
  }

  return (
    <div className="max-w-full space-y-5">
      <div className="no-print flex items-center justify-between flex-wrap gap-2">
        <div className="flex items-center gap-3">
          <Table2 size={22} style={{ color: BRAND }} />
          <h1 className="text-xl font-semibold text-slate-800">Reporte General de Cuentas por Cobrar</h1>
        </div>
        <div className="flex gap-2">
          <button onClick={exportar} disabled={filas.length === 0}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm disabled:opacity-50"
            style={{ background: '#15803d' }}>
            <FileSpreadsheet size={15} /> Exportar a Excel
          </button>
          <button onClick={() => window.print()}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm" style={{ background: BRAND }}>
            <Printer size={15} /> Imprimir / PDF
          </button>
        </div>
      </div>

      {/* Filtros */}
      <div className="no-print bg-white rounded-xl border border-slate-200 p-4 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3 items-end">
        <div>
          <label className="block text-xs text-slate-500 mb-1">Propietario(s)</label>
          <MultiSelect options={opcionesProp} selected={fProp} onChange={setFProp} allLabel="Todos los propietarios" />
        </div>
        <label className="flex items-center gap-2 text-sm text-slate-600 cursor-pointer">
          <input type="checkbox" checked={soloPendientes} onChange={e => setSoloPendientes(e.target.checked)} className="rounded" />
          Solo cuentas con saldo pendiente
        </label>
        <div className="text-sm text-slate-500 text-right">
          {filas.length} registro(s)
        </div>
      </div>

      {cargando ? (
        <div className="flex justify-center py-16">
          <div className="w-8 h-8 border-4 border-blue-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden print:border-0">
          <div className="px-5 py-3 border-b border-slate-200">
            <h2 className="text-base font-bold text-slate-800">CUENTAS POR COBRAR — DETALLE GENERAL</h2>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-xs whitespace-nowrap">
              <thead className="bg-slate-50 border-b border-slate-200 text-slate-500">
                <tr>
                  <th className="text-left px-3 py-2">Cód. Prop.</th>
                  <th className="text-left px-3 py-2">Propietario</th>
                  <th className="text-left px-3 py-2">Cód. Inm.</th>
                  <th className="text-left px-3 py-2">Inmueble</th>
                  <th className="text-left px-3 py-2">Cód. Inq.</th>
                  <th className="text-left px-3 py-2">Inquilino</th>
                  <th className="text-left px-3 py-2">Cód. Contrato</th>
                  <th className="text-right px-3 py-2">Valor Contrato</th>
                  <th className="text-center px-3 py-2">F. Inicial</th>
                  <th className="text-center px-3 py-2">F. Final</th>
                  <th className="text-left px-3 py-2">Concepto</th>
                  <th className="text-right px-3 py-2">Valor CxC</th>
                  <th className="text-right px-3 py-2">Recargos</th>
                </tr>
              </thead>
              <tbody>
                {filas.map((f, i) => (
                  <tr key={`${f.codContrato}-${i}`} className={`border-t border-slate-100 ${i % 2 ? 'bg-slate-50/50' : ''}`}>
                    <td className="px-3 py-1.5 font-mono text-slate-500">{f.codProp}</td>
                    <td className="px-3 py-1.5 font-medium text-slate-700">{f.nomProp}</td>
                    <td className="px-3 py-1.5 font-mono text-slate-500">{f.codInmueble}</td>
                    <td className="px-3 py-1.5 text-slate-600">{f.descInmueble}</td>
                    <td className="px-3 py-1.5 font-mono text-slate-500">{f.codInquilino}</td>
                    <td className="px-3 py-1.5 text-slate-600">{f.nomInquilino}</td>
                    <td className="px-3 py-1.5 font-mono text-slate-500">{f.codContrato}</td>
                    <td className="px-3 py-1.5 text-right text-slate-600">{formatCurrency(f.valorContrato)}</td>
                    <td className="px-3 py-1.5 text-center text-slate-500">{f.fechaInicial ? formatDate(f.fechaInicial) : '—'}</td>
                    <td className="px-3 py-1.5 text-center text-slate-500">{f.fechaFinal ? formatDate(f.fechaFinal) : '—'}</td>
                    <td className="px-3 py-1.5 text-slate-600">{f.concepto}</td>
                    <td className="px-3 py-1.5 text-right font-semibold text-slate-700">{formatCurrency(f.valorCxC)}</td>
                    <td className="px-3 py-1.5 text-right text-orange-600">{formatCurrency(f.recargos)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot className="border-t-2 border-slate-300 bg-slate-100 font-bold">
                <tr>
                  <td colSpan={11} className="px-3 py-2 text-right text-slate-700">TOTALES</td>
                  <td className="px-3 py-2 text-right text-slate-800">{formatCurrency(totValorCxC)}</td>
                  <td className="px-3 py-2 text-right text-orange-700">{formatCurrency(totRecargos)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
