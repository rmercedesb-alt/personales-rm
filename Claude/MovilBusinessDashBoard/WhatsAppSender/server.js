// ============================================================================
// WhatsAppSender -- sidecar local del "Cierre Diario" (MovilBusiness Dashboard Smart)
// ----------------------------------------------------------------------------
// Pedido explícito del usuario 2026-09-19: enviar el Cierre Diario por WhatsApp,
// mecanismo elegido = WhatsApp Web NO OFICIAL vía Baileys (vinculado por QR a un
// número real, sin costo, con el riesgo de bloqueo ya asumido por el usuario).
//
// Este proceso es un "pipe tonto": NO conoce nada de presupuestos/ventas. Todo el
// dominio de negocio y el HTML del reporte los arma el .NET (ver
// ReportesProgramadosSchedulerService.ArmarHtmlWhatsAppCierreDiario). Este sidecar
// solo hace 2 cosas:
//   1) Renderiza el HTML recibido a una imagen PNG (Puppeteer headless).
//   2) Manda esa imagen por WhatsApp (Baileys), a un número recibido por parámetro.
//
// Escucha SOLO en 127.0.0.1 -- nunca se expone a internet. El .NET (ApiPdfPresController
// /whatsapp/status, ReportesProgramadosSchedulerService.EnviarPorWhatsApp) es el único
// llamador esperado, siempre desde el mismo servidor.
//
// Uso:
//   npm install
//   node server.js            (o INICIAR_WHATSAPP_SENDER.bat)
//   -> la primera vez, abre Supervisión Dinámica -> Cierres -> botón "📱 WhatsApp"
//      en el Dashboard para ver el QR y vincularlo (una sola vez).
//
// En el servidor de producción este proceso debe quedar corriendo SIEMPRE (como el
// propio Dashboard) -- registrarlo como Servicio de Windows (p.ej. con `nssm` o
// `node-windows`) es tarea de TI, no de este script.
// ============================================================================

const express = require('express');
const fs = require('fs');
const QRCode = require('qrcode');
const {
  default: makeWASocket,
  useMultiFileAuthState,
  DisconnectReason,
} = require('@whiskeysockets/baileys');

const PORT = process.env.WHATSAPP_SENDER_PORT || 5099;
const AUTH_DIR = require('path').join(__dirname, 'auth');

let sock = null;
let conectado = false;
let ultimoQrDataUrl = null;
let browserPromise = null; // Puppeteer, un solo browser reusado entre envíos

async function iniciarWhatsApp() {
  const { state, saveCreds } = await useMultiFileAuthState(AUTH_DIR);
  sock = makeWASocket({ auth: state, printQRInTerminal: false });

  sock.ev.on('creds.update', saveCreds);

  sock.ev.on('connection.update', async (u) => {
    const { connection, lastDisconnect, qr } = u;
    if (qr) {
      ultimoQrDataUrl = await QRCode.toDataURL(qr);
      conectado = false;
    }
    if (connection === 'open') {
      conectado = true;
      ultimoQrDataUrl = null;
      console.log('[WhatsAppSender] Vinculado y conectado.');
    }
    if (connection === 'close') {
      conectado = false;
      const motivo = lastDisconnect?.error?.output?.statusCode;
      const cerroSesion = motivo === DisconnectReason.loggedOut;
      console.log('[WhatsAppSender] Conexión cerrada' + (cerroSesion ? ' (sesión cerrada desde el teléfono -- hay que re-escanear el QR).' : ', reintentando...'));
      if (!cerroSesion) setTimeout(iniciarWhatsApp, 3000);
    }
  });
}

// Puppeteer trae su PROPIO Chrome, pero descargarlo requiere que su postinstall corra
// (bloqueado en esta máquina por la protección de scripts de npm) y salir a internet a
// bajarlo (frecuentemente bloqueado por firewall/antivirus corporativo) -- 2026-09-20,
// error real: "Could not find Chrome". En vez de pelear con esa descarga, se usa el
// Chrome/Edge que YA está instalado en Windows (Edge viene de fábrica en Windows 10/11,
// es Chromium y Puppeteer lo maneja igual vía executablePath).
function detectarNavegadorSistema() {
  const candidatos = [
    process.env.PUPPETEER_EXECUTABLE_PATH,
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  ].filter(Boolean);
  return candidatos.find((c) => fs.existsSync(c)) || null;
}

async function getBrowser() {
  if (!browserPromise) {
    const puppeteer = require('puppeteer');
    const executablePath = detectarNavegadorSistema();
    const opts = { headless: 'new', args: ['--no-sandbox', '--disable-setuid-sandbox'] };
    if (executablePath) { opts.executablePath = executablePath; console.log('[WhatsAppSender] Usando navegador del sistema:', executablePath); }
    else console.log('[WhatsAppSender] No se encontró Chrome/Edge del sistema -- se usará el Chrome propio de Puppeteer (debe estar instalado con "npx puppeteer browsers install chrome").');
    // Si el launch falla, NO se cachea la promesa rota -- si no, todo intento
    // siguiente (sin reiniciar el proceso) repetiría el mismo error para
    // siempre en vez de volver a intentar. Bug real encontrado 2026-09-20.
    browserPromise = puppeteer.launch(opts).catch((ex) => { browserPromise = null; throw ex; });
  }
  return browserPromise;
}

// Renderiza el HTML recibido a un PNG -- ancho fijo (el propio HTML de
// ArmarHtmlWhatsAppCierreDiario ya trae width:520px, ancho desde 2026-09-20 para que
// quepa el desglose por Centro/Canal/Supervisor/Suplidor de cada punto), alto según el
// contenido real (puede ser una imagen larga si hay varios puntos marcados).
async function renderHtmlAPng(html) {
  const browser = await getBrowser();
  const page = await browser.newPage();
  try {
    await page.setViewport({ width: 560, height: 800, deviceScaleFactor: 2 });
    await page.setContent(html, { waitUntil: 'networkidle0' });
    const raw = await page.screenshot({ type: 'png', fullPage: true });
    // Puppeteer reciente devuelve Uint8Array (no un Buffer real de Node) -- Baileys hace
    // Buffer.isBuffer(item) para reconocer una imagen ya en memoria; con un Uint8Array
    // "suelto" esa comprobación da false y termina cayendo en su rama de archivo/URL,
    // donde revienta leyendo item.url.toString() (item.url es undefined). Causa real
    // encontrada 2026-09-20 con la traza completa del error. Buffer.from() es gratis si
    // 'raw' ya es un Buffer (Buffer extiende Uint8Array, Node lo detecta y no copia de más).
    return Buffer.isBuffer(raw) ? raw : Buffer.from(raw);
  } finally {
    await page.close();
  }
}

function telefonoAJid(telefono) {
  const digitos = String(telefono).replace(/\D/g, '');
  return `${digitos}@s.whatsapp.net`;
}

// Reintenta un envío de Baileys hasta 3 veces, esperando a que la reconexión automática de
// iniciarWhatsApp() (arriba, se dispara sola al detectar 'close') tenga chance de resolver
// un "Connection Closed" transitorio antes de rendirse. Error real encontrado 2026-09-20.
async function enviarConReintento(fn, intentos = 3, esperaMs = 2500) {
  let ultimoError;
  for (let i = 0; i < intentos; i++) {
    if (i > 0) {
      console.log(`[WhatsAppSender] Reintentando envío (${i + 1}/${intentos}) tras esperar ${esperaMs}ms...`);
      await new Promise((r) => setTimeout(r, esperaMs));
    }
    if (!conectado || !sock) { ultimoError = new Error('WhatsApp no está conectado (la sesión se cayó y todavía no termina de reconectar).'); continue; }
    try { return await fn(); } catch (ex) { ultimoError = ex; console.warn(`[WhatsAppSender] Intento ${i + 1}/${intentos} falló:`, ex && ex.message); }
  }
  throw ultimoError;
}

const app = express();
app.use(express.json({ limit: '5mb' }));

app.get('/status', (req, res) => {
  res.json({ conectado, qr: conectado ? null : ultimoQrDataUrl });
});

// Lista los grupos del número YA vinculado -- para que el Dashboard ofrezca un select en
// vez de que el usuario tenga que buscar/pegar el ID del grupo a mano. Pedido explícito
// 2026-09-20 ("configurar si lo envía a un grupo o a una sola persona").
app.get('/grupos', async (req, res) => {
  if (!conectado || !sock) return res.status(503).json({ ok: false, error: 'WhatsApp no está vinculado todavía.' });
  try {
    const mapa = await sock.groupFetchAllParticipating();
    const grupos = Object.values(mapa).map((g) => ({ id: g.id, nombre: g.subject }));
    res.json({ ok: true, grupos });
  } catch (ex) {
    res.status(500).json({ ok: false, error: String(ex && ex.message || ex) });
  }
});

// destino: { telefono } para un contacto, o { grupoId } para un grupo (ya viene con el
// sufijo "@g.us", tal cual lo devuelve /grupos) -- pedido 2026-09-20.
// Las 2 fases (render de imagen / envío por Baileys) se capturan POR SEPARADO -- 2026-09-20,
// tras un error real ("Cannot read properties of undefined (reading 'toString')") que no
// decía en cuál de las 2 pasó. Se manda ex.stack (no solo ex.message) para ver exactamente
// la línea que revienta sin tener que ir a buscar la terminal del sidecar.
// textoIntro (opcional): si viene, se manda como un mensaje de TEXTO aparte, ANTES de la
// imagen -- "que luego traiga las imágenes que se van a compartir" (pedido 2026-09-20).
app.post('/enviar', async (req, res) => {
  const { telefono, grupoId, html, caption, textoIntro } = req.body || {};
  if ((!telefono && !grupoId) || !html) return res.status(400).json({ ok: false, error: 'Falta telefono/grupoId o html.' });
  if (!conectado || !sock) return res.status(503).json({ ok: false, error: 'WhatsApp no está vinculado todavía (ver /status).' });

  let png;
  try {
    png = await renderHtmlAPng(html);
  } catch (ex) {
    console.error('[WhatsAppSender] Error RENDERIZANDO la imagen (Puppeteer/Chrome):', ex);
    return res.status(500).json({ ok: false, error: 'Generando la imagen (Puppeteer/Chrome): ' + String((ex && ex.stack) || (ex && ex.message) || ex) });
  }

  try {
    const jid = grupoId || telefonoAJid(telefono);
    // "Connection Closed" real encontrado 2026-09-20: la sesión de WhatsApp Web se cayó
    // justo entre que se generó la imagen (Puppeteer, unos segundos) y el envío -- común
    // en librerías no oficiales, la reconexión automática de iniciarWhatsApp() (arriba)
    // suele resolverlo sola en pocos segundos. En vez de fallar de una, se reintenta cada
    // mensaje por separado (para no duplicar el texto de intro si solo la imagen falla).
    if (textoIntro) await enviarConReintento(() => sock.sendMessage(jid, { text: textoIntro }));
    await enviarConReintento(() => sock.sendMessage(jid, { image: png, caption: caption || '' }));
    res.json({ ok: true });
  } catch (ex) {
    console.error('[WhatsAppSender] Error ENVIANDO el mensaje (Baileys/WhatsApp) a', grupoId || telefono, ex);
    res.status(500).json({ ok: false, error: 'Enviando el mensaje (Baileys/WhatsApp): ' + String((ex && ex.stack) || (ex && ex.message) || ex) });
  }
});

// Desvincula la sesión actual (borra la carpeta auth/) para poder escanear un número
// distinto -- pedido explícito 2026-09-20 ("si quiero cambiarlo o configurar otro").
app.post('/logout', async (req, res) => {
  try { if (sock) await sock.logout(); } catch { /* puede fallar si ya estaba caída -- no importa, igual se borra auth/ */ }
  conectado = false; ultimoQrDataUrl = null;
  try { fs.rmSync(AUTH_DIR, { recursive: true, force: true }); } catch (ex) { console.warn('[WhatsAppSender] No se pudo borrar auth/:', ex.message); }
  setTimeout(() => iniciarWhatsApp().catch((ex) => console.error('[WhatsAppSender] No se pudo reiniciar tras logout:', ex)), 500);
  res.json({ ok: true });
});

app.listen(PORT, '127.0.0.1', () => console.log(`[WhatsAppSender] Escuchando en http://127.0.0.1:${PORT}`));
iniciarWhatsApp().catch((ex) => console.error('[WhatsAppSender] No se pudo iniciar Baileys:', ex));
