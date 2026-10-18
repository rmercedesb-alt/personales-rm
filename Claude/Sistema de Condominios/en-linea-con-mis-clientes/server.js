// Panel web "En linea con mis clientes"
const express = require('express')
const multer = require('multer')
const path = require('path')
const fs = require('fs')
require('dotenv').config()

const db = require('./db')
const wa = require('./whatsapp')
const cfg = require('./config')
const grupos = require('./grupos')
const scheduler = require('./scheduler')
const { enviarRecordatorios } = require('./reminders')
const { formatChatId, armarMensaje } = require('./util')

const app = express()
app.use(express.json())
app.use(express.static(path.join(__dirname, 'public')))

const upload = multer({ dest: path.join(__dirname, 'data', 'adjuntos') })
const sleep = (ms) => new Promise(r => setTimeout(r, ms))

// --- Estado de la conexion WhatsApp (QR / conectado) ---
app.get('/api/status', (_req, res) => res.json(wa.getEstado()))

// --- Cerrar sesion de WhatsApp (para entrar con otro numero) ---
app.post('/api/logout', async (_req, res) => {
  try { await wa.cerrarSesion(); res.json({ ok: true }) }
  catch (e) { res.status(500).json({ error: e.message }) }
})

// --- Datos para armar destinatarios ---
app.get('/api/propietarios', async (_req, res) => {
  try { res.json(await db.getPropietarios()) }
  catch (e) { res.status(500).json({ error: e.message }) }
})

app.get('/api/destinatarios', async (req, res) => {
  try { res.json(await db.getDestinatarios(req.query.idPropietario || null)) }
  catch (e) { res.status(500).json({ error: e.message }) }
})

// --- Configuracion de recordatorios ---
app.get('/api/config', (_req, res) => res.json(cfg.load()))
app.post('/api/config', (req, res) => res.json(cfg.save(req.body || {})))

// --- Envio manual (carta masiva) a destinatarios seleccionados ---
// Campos: destinatarios (JSON [{telefono,nombre,id}]), mensaje, archivo (opcional)
app.post('/api/enviar', upload.single('archivo'), async (req, res) => {
  if (!wa.isReady()) return res.status(400).json({ error: 'WhatsApp no esta conectado' })
  let destinatarios = []
  try { destinatarios = JSON.parse(req.body.destinatarios || '[]') } catch {}
  const mensaje = req.body.mensaje || ''
  const ruta = req.file ? req.file.path : null
  const adjunto = req.file
    ? { path: req.file.path, mimetype: req.file.mimetype, filename: req.file.originalname }
    : null
  if (destinatarios.length === 0) return res.status(400).json({ error: 'No hay destinatarios' })

  let enviados = 0, errores = 0
  const detalle = []
  for (const d of destinatarios) {
    const chatId = formatChatId(d.telefono)
    if (!chatId) {
      errores++; detalle.push({ nombre: d.nombre, estado: 'ERROR', motivo: 'Telefono invalido' })
      await db.registrarEnvio({ tipo: 'MANUAL', idInquilino: d.id, nombre: d.nombre,
        telefono: d.telefono, mensaje, conAdjunto: !!ruta, estado: 'ERROR', detalle: 'Telefono invalido' })
      continue
    }
    try {
      await wa.enviar(chatId, mensaje, adjunto)
      enviados++; detalle.push({ nombre: d.nombre, estado: 'ENVIADO' })
      await db.registrarEnvio({ tipo: 'MANUAL', idInquilino: d.id, nombre: d.nombre,
        telefono: d.telefono, mensaje, conAdjunto: !!ruta, estado: 'ENVIADO' })
    } catch (err) {
      errores++; detalle.push({ nombre: d.nombre, estado: 'ERROR', motivo: String(err.message || err) })
      await db.registrarEnvio({ tipo: 'MANUAL', idInquilino: d.id, nombre: d.nombre,
        telefono: d.telefono, mensaje, conAdjunto: !!ruta, estado: 'ERROR', detalle: String(err.message || err) })
    }
    await sleep(3000 + Math.random() * 3000)
  }
  if (ruta) fs.unlink(ruta, () => {})   // borra el adjunto temporal
  res.json({ enviados, errores, detalle })
})

// --- Disparar recordatorios automaticos ahora (boton de prueba) ---
app.post('/api/enviar-recordatorios', async (_req, res) => {
  try { res.json(await enviarRecordatorios({ forzar: true })) }
  catch (e) { res.status(500).json({ error: e.message }) }
})

// --- Vista previa del recordatorio para un cliente vencido (sin enviar) ---
app.get('/api/preview-recordatorio', async (_req, res) => {
  try {
    const config = cfg.load()
    const clientes = await db.getClientesVencidos(config.soloFueraDeGracia)
    const ejemplo = clientes[0]
    res.json({
      cantidadClientes: clientes.length,
      ejemplo: ejemplo ? armarMensaje(config.plantilla, {
        nombre: ejemplo.nombre, periodos: ejemplo.periodos, total: ejemplo.total_vencido,
      }) : '(No hay clientes vencidos en este momento)',
    })
  } catch (e) { res.status(500).json({ error: e.message }) }
})

// --- Grupos de contactos manuales (guardar / cargar / eliminar) ---
app.get('/api/grupos', (_req, res) => res.json(grupos.load()))
app.post('/api/grupos', (req, res) => res.json(grupos.guardar(req.body || {})))
app.delete('/api/grupos/:id', (req, res) => { grupos.eliminar(req.params.id); res.json({ ok: true }) })

// --- Historial de envios ---
app.get('/api/historial', async (_req, res) => {
  try { res.json(await db.getHistorial()) }
  catch (e) { res.status(500).json({ error: e.message }) }
})

const PORT = Number(process.env.PORT) || 5050
;(async () => {
  // El panel arranca SIEMPRE, aunque la base de datos este apagada.
  try {
    await db.initHistorial()
    console.log('[BD] Conexion a la base de datos OK.')
  } catch (e) {
    console.log('[BD] AVISO: no se pudo conectar a la base de datos.')
    console.log('[BD] Asegurate de que PostgreSQL este encendido. Detalle:', e.message)
    console.log('[BD] El panel abrira igual; reintenta cuando la base este disponible.')
  }

  try { wa.init() } catch (e) { console.log('[WhatsApp] No se pudo iniciar:', e.message) }
  scheduler.iniciar()

  app.listen(PORT, () => {
    console.log('============================================================')
    console.log('  En linea con mis clientes  —  Fanny Mercedes & Asoc.')
    console.log(`  Panel:  http://localhost:${PORT}`)
    console.log('  (No cierres esta ventana mientras uses el sistema)')
    console.log('============================================================')
    // Abre el navegador automaticamente en el panel
    try { require('child_process').exec(`start "" http://localhost:${PORT}`) } catch {}
  })
})()

// Evita que un error inesperado cierre el proceso de golpe
process.on('unhandledRejection', (e) => console.log('[AVISO] Rechazo no manejado:', e?.message || e))
process.on('uncaughtException', (e) => console.log('[AVISO] Excepcion no manejada:', e?.message || e))
