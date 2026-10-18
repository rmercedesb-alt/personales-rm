// Exporta una tabla a un archivo .xls que Excel abre con columnas y números reales.
export function exportarTablaExcel(nombre: string, headers: string[], filas: (string | number)[][]) {
  const esc = (v: string | number) =>
    typeof v === 'number'
      ? `<td style="mso-number-format:'0.00'">${v}</td>`
      : `<td>${String(v ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;')}</td>`
  const thead = '<tr>' + headers.map(h => `<th style="background:#1B4F8C;color:#fff">${h}</th>`).join('') + '</tr>'
  const tbody = filas.map(f => '<tr>' + f.map(esc).join('') + '</tr>').join('')
  const html =
    `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">` +
    `<head><meta charset="utf-8"><!--[if gte mso 9]><xml><x:ExcelWorkbook><x:ExcelWorksheets><x:ExcelWorksheet>` +
    `<x:Name>Reporte</x:Name><x:WorksheetOptions><x:DisplayGridlines/></x:WorksheetOptions></x:ExcelWorksheet>` +
    `</x:ExcelWorksheets></x:ExcelWorkbook></xml><![endif]--></head>` +
    `<body><table border="1">${thead}${tbody}</table></body></html>`
  const blob = new Blob(['﻿', html], { type: 'application/vnd.ms-excel' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${nombre}_${new Date().toISOString().slice(0, 10)}.xls`
  a.click()
  URL.revokeObjectURL(url)
}
