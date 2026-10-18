import { X, Printer } from 'lucide-react'
import type { CxCPago } from '../../lib/api'
import { formatCurrency, formatDate } from '../../lib/utils'
import { useEmpresaStore } from '../../store/empresaStore'

interface Props {
  pagos: CxCPago[]
  onClose: () => void
  reimpreso?: boolean
}

export default function PrintReciboModal({ pagos, onClose, reimpreso = false }: Props) {
  if (pagos.length === 0) return null

  const { config } = useEmpresaStore()
  const primerPago  = pagos[0]
  const idRecibo    = primerPago.idRecibo || primerPago.id
  const total       = pagos.reduce((s, p) => s + p.totalPagado, 0)
  const fechaTx     = primerPago.fechaPago
  const fechaCont   = primerPago.fechaRegistro
  const cxcLineas   = pagos.filter(p => p.idCxC)
  const ahoraStr    = new Date().toLocaleString('es-DO', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  })

  const handlePrint = () => {
    const logoHtml = config.logo
      ? `<img src="${config.logo}" alt="Logo" style="height:56px;object-fit:contain;" />`
      : `<div style="width:48px;height:48px;background:#1B4F8C;border-radius:8px;display:flex;align-items:center;justify-content:center;">
           <svg xmlns="http://www.w3.org/2000/svg" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
         </div>`

    const cxcRows = cxcLineas.map(p => `
      <tr>
        <td style="padding:5px 8px;font-family:monospace;color:#94a3b8;font-size:11px;">${p.idCxC}</td>
        <td style="padding:5px 8px;color:#475569;font-size:11px;">${p.notas && p.notas !== '' ? p.notas : 'Alquiler'}</td>
        <td style="padding:5px 8px;text-align:right;font-size:11px;">${formatCurrency(p.montoAlquiler)}</td>
        <td style="padding:5px 8px;text-align:right;color:#ea580c;font-size:11px;">${p.montoMora > 0 ? formatCurrency(p.montoMora) : '—'}</td>
        <td style="padding:5px 8px;text-align:right;font-weight:600;font-size:11px;">${formatCurrency(p.totalPagado)}</td>
      </tr>`).join('')

    const html = `<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <title>Recibo ${idRecibo}</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: Arial, sans-serif; font-size: 13px; color: #1e293b; padding: 24px; }
    .header { display: flex; align-items: center; gap: 16px; border-bottom: 2px solid #1B4F8C; padding-bottom: 12px; margin-bottom: 16px; }
    .empresa-nombre { font-weight: 700; font-size: 16px; color: #1e293b; }
    .empresa-sub { font-size: 11px; color: #64748b; margin-top: 2px; }
    .empresa-info { font-size: 11px; color: #94a3b8; margin-top: 2px; }
    .recibo-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 16px; }
    .recibo-id { font-size: 22px; font-weight: 700; font-family: monospace; color: #1e293b; }
    .recibo-label { font-size: 10px; font-weight: 600; color: #94a3b8; text-transform: uppercase; margin-bottom: 2px; }
    .fechas { text-align: right; font-size: 11px; color: #475569; line-height: 1.8; }
    .fechas strong { color: #1e293b; }
    .cliente-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px 16px; margin-bottom: 16px; }
    .cliente-label { font-size: 10px; font-weight: 600; color: #94a3b8; text-transform: uppercase; margin-bottom: 4px; }
    .cliente-nombre { font-weight: 700; font-size: 14px; color: #1e293b; }
    .cliente-forma { font-size: 11px; color: #64748b; margin-top: 3px; }
    table { width: 100%; border-collapse: collapse; margin-bottom: 16px; }
    th { padding: 6px 8px; font-size: 10px; font-weight: 700; color: #64748b; text-transform: uppercase; border-bottom: 2px solid #e2e8f0; }
    th:last-child, td:last-child { text-align: right; }
    th:first-child, td:first-child { text-align: left; }
    tr:not(:last-child) td { border-bottom: 1px solid #f1f5f9; }
    .total-line { display: flex; justify-content: space-between; align-items: center; border-top: 2px solid #1e293b; padding-top: 10px; margin-top: 4px; }
    .total-label { font-weight: 700; font-size: 14px; }
    .total-value { font-weight: 700; font-size: 18px; }
    .firmas { display: grid; grid-template-columns: 1fr 1fr; gap: 48px; margin-top: 48px; }
    .firma-line { border-top: 1px solid #94a3b8; padding-top: 8px; margin-top: 40px; font-size: 11px; color: #64748b; text-align: center; }
    .footer { text-align: center; font-size: 10px; color: #cbd5e1; margin-top: 24px; }
    .sello-reimpreso { display:inline-block; border: 2px solid #dc2626; color: #dc2626; font-weight:700; font-size:12px; padding: 2px 10px; border-radius:4px; letter-spacing:2px; transform:rotate(-2deg); margin-bottom:8px; }
    @media print { body { padding: 0; } @page { margin: 1.5cm; } }
  </style>
</head>
<body>
  <div class="header">
    ${logoHtml}
    <div>
      <div class="empresa-nombre">${config.nombre}</div>
      <div class="empresa-sub">Sistema de Administración de Inmuebles</div>
      <div class="empresa-info">
        ${config.rnc ? `RNC: ${config.rnc}` : ''}
        ${config.telefono ? ` &nbsp;·&nbsp; Tel: ${config.telefono}` : ''}
        ${config.email ? ` &nbsp;·&nbsp; ${config.email}` : ''}
      </div>
    </div>
    <div style="margin-left:auto;text-align:right;font-size:11px;color:#94a3b8;">
      <div>${new Date().toLocaleDateString('es-DO', { year: 'numeric', month: 'long', day: 'numeric' })}</div>
      <div>${new Date().toLocaleTimeString('es-DO', { hour: '2-digit', minute: '2-digit' })}</div>
    </div>
  </div>

  ${reimpreso ? `<div style="text-align:right;margin-bottom:8px;"><span class="sello-reimpreso">★ REIMPRESO</span><div style="font-size:10px;color:#64748b;margin-top:3px;">Reimpreso el ${ahoraStr}</div></div>` : ''}

  <div class="recibo-header">
    <div>
      <div class="recibo-label">Recibo de Pago</div>
      <div class="recibo-id">${idRecibo}</div>
    </div>
    <div class="fechas">
      <div><span style="color:#94a3b8;">F. Transacción:</span> <strong>${formatDate(fechaTx)}</strong></div>
      <div><span style="color:#94a3b8;">F. Contabilización:</span> <strong>${formatDate(fechaCont ?? fechaTx)}</strong></div>
    </div>
  </div>

  <div class="cliente-box">
    <div class="cliente-label">Cliente</div>
    <div class="cliente-nombre">${primerPago.nombreInquilino}</div>
    <div class="cliente-forma">
      Forma de pago: <strong>${primerPago.formaPago}</strong>
      ${primerPago.referencia ? ` &nbsp;·&nbsp; Ref: ${primerPago.referencia}` : ''}
    </div>
  </div>

  ${cxcLineas.length > 0 ? `
  <table>
    <thead>
      <tr>
        <th style="text-align:left;">CxC</th>
        <th style="text-align:left;">Concepto</th>
        <th style="text-align:right;">Alquiler</th>
        <th style="text-align:right;">Mora</th>
        <th style="text-align:right;">Total</th>
      </tr>
    </thead>
    <tbody>${cxcRows}</tbody>
  </table>` : ''}

  <div class="total-line">
    <span class="total-label">TOTAL PAGADO</span>
    <span class="total-value">${formatCurrency(total)}</span>
  </div>

  <div class="firmas">
    <div><div class="firma-line">Recibido por</div></div>
    <div><div class="firma-line">Firma del cliente</div></div>
  </div>

  <div class="footer">${idRecibo} &nbsp;·&nbsp; ${new Date().getFullYear()} ${config.nombre}</div>

  <script>window.onload = () => { window.print(); window.onafterprint = () => window.close(); }<\/script>
</body>
</html>`

    const win = window.open('', '_blank', 'width=700,height=900')
    if (win) {
      win.document.write(html)
      win.document.close()
    }
  }

  return (
    <div className="fixed inset-0 bg-black/50 z-[100] flex items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg max-h-[92vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100">
          <div>
            <h2 className="font-semibold text-slate-800">
              {reimpreso ? 'Reimpresión — ' : 'Recibo generado — '}{idRecibo}
            </h2>
            {reimpreso && <p className="text-xs text-red-600 font-medium mt-0.5">Reimpreso el {ahoraStr}</p>}
          </div>
          <div className="flex items-center gap-2">
            <button onClick={handlePrint}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-white text-sm font-medium hover:opacity-90"
              style={{ background: '#1B4F8C' }}>
              <Printer size={14} /> Imprimir
            </button>
            <button onClick={onClose} className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
          </div>
        </div>

        {/* Vista previa del recibo */}
        <div className="overflow-y-auto flex-1 p-6">
          {/* Encabezado empresa */}
          <div className="text-center mb-5 border-b pb-4">
            <p className="font-bold text-lg text-slate-800 uppercase tracking-wide">{config.nombre}</p>
            <p className="text-xs text-slate-500">Sistema de Administración de Inmuebles</p>
          </div>

          {/* Sello reimpreso */}
          {reimpreso && (
            <div className="flex justify-end mb-3">
              <div className="text-right">
                <span className="inline-block border-2 border-red-600 text-red-600 font-bold text-xs px-3 py-0.5 rounded tracking-widest rotate-[-2deg]">
                  ★ REIMPRESO
                </span>
                <p className="text-xs text-slate-400 mt-1">Reimpreso el {ahoraStr}</p>
              </div>
            </div>
          )}

          {/* Número de recibo y fechas */}
          <div className="flex items-center justify-between mb-4">
            <div>
              <p className="text-xs text-slate-500 uppercase font-medium">Recibo de Pago</p>
              <p className="text-xl font-bold text-slate-800 font-mono">{idRecibo}</p>
            </div>
            <div className="text-right text-xs text-slate-600 space-y-0.5">
              <p><span className="text-slate-400">F. Transacción:</span> <strong>{formatDate(fechaTx)}</strong></p>
              <p><span className="text-slate-400">F. Contabilización:</span> {formatDate(fechaCont ?? fechaTx)}</p>
            </div>
          </div>

          {/* Cliente */}
          <div className="bg-slate-50 rounded-lg px-4 py-3 mb-4">
            <p className="text-xs text-slate-400 uppercase font-medium mb-1">Cliente</p>
            <p className="font-semibold text-slate-800">{primerPago.nombreInquilino}</p>
            <p className="text-xs text-slate-500 mt-0.5">
              Forma de pago: <strong>{primerPago.formaPago}</strong>
              {primerPago.referencia && ` · Ref: ${primerPago.referencia}`}
            </p>
          </div>

          {/* Detalle de cargos */}
          {cxcLineas.length > 0 && (
            <table className="w-full text-sm mb-4">
              <thead>
                <tr className="border-b-2 border-slate-200">
                  <th className="text-left py-1.5 text-xs font-semibold text-slate-600">CxC</th>
                  <th className="text-left py-1.5 text-xs font-semibold text-slate-600">Concepto</th>
                  <th className="text-right py-1.5 text-xs font-semibold text-slate-600">Alquiler</th>
                  <th className="text-right py-1.5 text-xs font-semibold text-slate-600">Mora</th>
                  <th className="text-right py-1.5 text-xs font-semibold text-slate-600">Total</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {cxcLineas.map(p => (
                  <tr key={p.id}>
                    <td className="py-1.5 text-xs font-mono text-slate-400">{p.idCxC}</td>
                    <td className="py-1.5 text-xs text-slate-600">{p.notas && p.notas !== '' ? p.notas : 'Alquiler'}</td>
                    <td className="py-1.5 text-right text-xs">{formatCurrency(p.montoAlquiler)}</td>
                    <td className="py-1.5 text-right text-xs text-orange-600">{p.montoMora > 0 ? formatCurrency(p.montoMora) : '—'}</td>
                    <td className="py-1.5 text-right text-xs font-semibold">{formatCurrency(p.totalPagado)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {/* Total */}
          <div className="border-t-2 border-slate-800 pt-3 flex items-center justify-between">
            <span className="font-bold text-slate-800">TOTAL PAGADO</span>
            <span className="font-bold text-lg text-slate-800">{formatCurrency(total)}</span>
          </div>

          {/* Firmas */}
          <div className="mt-8 grid grid-cols-2 gap-8 text-center text-xs text-slate-500">
            <div><div className="border-t border-slate-400 pt-2 mt-8">Recibido por</div></div>
            <div><div className="border-t border-slate-400 pt-2 mt-8">Firma del cliente</div></div>
          </div>

          <p className="text-center text-xs text-slate-300 mt-4">
            {idRecibo} · {new Date().getFullYear()} {config.nombre}
          </p>
        </div>
      </div>
    </div>
  )
}
