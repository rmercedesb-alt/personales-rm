// Programador: corre cada hora en punto y, si hoy es uno de los dias
// configurados y la hora coincide, dispara los recordatorios automaticos.
const cron = require('node-cron')
const cfg = require('./config')
const { enviarRecordatorios } = require('./reminders')

function iniciar() {
  // Minuto 0 de cada hora
  cron.schedule('0 * * * *', async () => {
    const config = cfg.load()
    if (!config.recordatorioActivo) return
    const ahora = new Date()
    const dia = ahora.getDay()       // 0=Dom ... 6=Sab
    const hora = ahora.getHours()
    if (!config.dias.includes(dia)) return
    if (hora !== Number(config.hora)) return

    console.log(`[Programador] Disparando recordatorios automaticos (${ahora.toLocaleString('es-DO')})`)
    try {
      const r = await enviarRecordatorios()
      console.log('[Programador] Resultado:', r)
    } catch (e) {
      console.error('[Programador] Error:', e.message)
    }
  })
  console.log('[Programador] Activo. Revisa cada hora si corresponde enviar.')
}

module.exports = { iniciar }
