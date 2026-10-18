// Cliente de WhatsApp (no oficial, gratis) usando whatsapp-web.js.
// Mantiene la sesion activa con un numero dedicado escaneado por QR una sola vez.
const { Client, LocalAuth, MessageMedia } = require('whatsapp-web.js')
const qrcode = require('qrcode')
const fs = require('fs')

let client = null
let estado = 'INICIANDO'   // INICIANDO | QR | CONECTADO | DESCONECTADO
let qrDataUrl = null

// Usa el Chrome instalado en el equipo (evita descargar Chromium aparte).
// Se puede sobreescribir con la variable de entorno CHROME_PATH.
function buscarChrome() {
  const LOCAL = process.env.LOCALAPPDATA || ''
  const candidatos = [
    process.env.CHROME_PATH,
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    LOCAL ? LOCAL + '\\Google\\Chrome\\Application\\chrome.exe' : null,
    // Microsoft Edge (viene preinstalado en Windows 10/11)
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  ].filter(Boolean)
  return candidatos.find(p => { try { return fs.existsSync(p) } catch { return false } })
}

function init() {
  const chrome = buscarChrome()
  if (chrome) console.log('[WhatsApp] Usando navegador:', chrome)
  else console.log('[WhatsApp] AVISO: no se encontro Chrome/Edge; se intentara el navegador por defecto.')

  client = new Client({
    authStrategy: new LocalAuth({ dataPath: './data/wa-session' }),
    puppeteer: {
      headless: true,
      executablePath: chrome || undefined,
      args: ['--no-sandbox', '--disable-setuid-sandbox'],
    },
  })

  client.on('qr', async (qr) => {
    estado = 'QR'
    qrDataUrl = await qrcode.toDataURL(qr)
    console.log('[WhatsApp] Escanea el codigo QR desde el panel web para conectar.')
  })

  client.on('ready', () => {
    estado = 'CONECTADO'
    qrDataUrl = null
    console.log('[WhatsApp] Conectado y listo para enviar mensajes.')
  })

  client.on('disconnected', (r) => {
    estado = 'DESCONECTADO'
    console.log('[WhatsApp] Desconectado:', r)
  })

  client.initialize()
}

function getEstado() { return { estado, qr: qrDataUrl } }
function isReady() { return estado === 'CONECTADO' }

// Envia un mensaje de texto y, opcionalmente, un archivo adjunto.
// adjunto = { path, mimetype, filename } para conservar el tipo y nombre real.
async function enviar(chatId, texto, adjunto) {
  if (!isReady()) throw new Error('WhatsApp no esta conectado')
  if (adjunto && adjunto.path) {
    let media
    if (adjunto.mimetype) {
      // Construye el media con el tipo y nombre originales (foto, PDF, etc.)
      const data = fs.readFileSync(adjunto.path).toString('base64')
      media = new MessageMedia(adjunto.mimetype, data, adjunto.filename || 'archivo')
    } else {
      media = MessageMedia.fromFilePath(adjunto.path)
    }
    // sendMediaAsDocument para PDFs y similares; las imagenes van como foto normal
    const esImagen = (adjunto.mimetype || '').startsWith('image/')
    return client.sendMessage(chatId, media, {
      caption: texto || '',
      sendMediaAsDocument: !esImagen,
    })
  }
  return client.sendMessage(chatId, texto)
}

// Cierra la sesion actual de WhatsApp y reinicia para mostrar un QR nuevo
// (para conectar con OTRO numero).
async function cerrarSesion() {
  estado = 'INICIANDO'
  qrDataUrl = null
  try { if (client) await client.logout() } catch (e) { console.log('[WhatsApp] logout:', e.message) }
  try { if (client) await client.destroy() } catch {}
  // Borra la sesion guardada para forzar un QR nuevo
  try { fs.rmSync('./data/wa-session', { recursive: true, force: true }) } catch {}
  console.log('[WhatsApp] Sesion cerrada. Generando nuevo QR...')
  // Reinicia el cliente (genera un QR nuevo para escanear otro numero)
  setTimeout(() => { try { init() } catch (e) { console.log('[WhatsApp] reinit:', e.message) } }, 1500)
  return true
}

module.exports = { init, getEstado, isReady, enviar, cerrarSesion }
