// Rutina de recordatorios automaticos a clientes con cartera vencida
const db = require('./db')
const wa = require('./whatsapp')
const cfg = require('./config')
const { formatChatId, armarMensaje } = require('./util')

// Pequena pausa entre envios para reducir riesgo de bloqueo del numero
const sleep = (ms) => new Promise(r => setTimeout(r, ms))

async function enviarRecordatorios({ forzar = false } = {}) {
  const config = cfg.load()
  if (!wa.isReady()) return { ok: false, motivo: 'WhatsApp no conectado', enviados: 0, errores: 0 }

  const clientes = await db.getClientesVencidos(config.soloFueraDeGracia)
  let enviados = 0, errores = 0, omitidos = 0

  for (const c of clientes) {
    // No repetir el mismo dia (salvo que sea un envio forzado de prueba)
    if (!forzar && await db.yaEnviadoHoy(c.id)) { omitidos++; continue }

    const chatId = formatChatId(c.telefono)
    const mensaje = armarMensaje(config.plantilla, {
      nombre: c.nombre, periodos: c.periodos, total: c.total_vencido,
    })

    if (!chatId) {
      errores++
      await db.registrarEnvio({ tipo: 'AUTOMATICO', idInquilino: c.id, nombre: c.nombre,
        telefono: c.telefono, mensaje, estado: 'ERROR', detalle: 'Telefono invalido' })
      continue
    }

    try {
      await wa.enviar(chatId, mensaje)
      enviados++
      await db.registrarEnvio({ tipo: 'AUTOMATICO', idInquilino: c.id, nombre: c.nombre,
        telefono: c.telefono, mensaje, estado: 'ENVIADO' })
    } catch (err) {
      errores++
      await db.registrarEnvio({ tipo: 'AUTOMATICO', idInquilino: c.id, nombre: c.nombre,
        telefono: c.telefono, mensaje, estado: 'ERROR', detalle: String(err.message || err) })
    }
    await sleep(4000 + Math.random() * 3000)   // 4-7s entre mensajes
  }

  return { ok: true, total: clientes.length, enviados, errores, omitidos }
}

module.exports = { enviarRecordatorios }
