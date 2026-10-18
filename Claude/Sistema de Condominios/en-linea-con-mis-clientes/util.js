// Utilidades: formato de telefono dominicano y armado del mensaje

// Convierte un telefono a formato WhatsApp: 1809XXXXXXX@c.us
function formatChatId(telefono) {
  if (!telefono) return null
  let d = String(telefono).replace(/\D/g, '')   // solo digitos
  if (d.length === 10) d = '1' + d               // 809XXXXXXX -> 1809XXXXXXX
  if (d.length === 11 && d.startsWith('1')) return `${d}@c.us`
  if (d.length >= 11) return `${d}@c.us`          // ya trae codigo de pais
  return null                                     // numero invalido
}

const MESES = ['Enero','Febrero','Marzo','Abril','Mayo','Junio',
               'Julio','Agosto','Septiembre','Octubre','Noviembre','Diciembre']

function nombreMes(fecha) {
  const d = new Date(fecha)
  return `${MESES[d.getMonth()]} ${d.getFullYear()}`
}

function listaMeses(periodos) {
  const nombres = (periodos || []).map(nombreMes)
  const unicos = [...new Set(nombres)]
  if (unicos.length <= 1) return unicos.join('')
  return unicos.slice(0, -1).join(', ') + ' y ' + unicos[unicos.length - 1]
}

function formatRD(monto) {
  const n = Number(monto || 0)
  return 'RD$ ' + n.toLocaleString('es-DO', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

// Reemplaza las variables {nombre} {meses} {monto} en la plantilla
function armarMensaje(plantilla, { nombre, periodos, total }) {
  return plantilla
    .replace(/\{nombre\}/g, nombre || '')
    .replace(/\{meses\}/g, listaMeses(periodos))
    .replace(/\{monto\}/g, formatRD(total))
}

module.exports = { formatChatId, listaMeses, formatRD, armarMensaje, nombreMes }
