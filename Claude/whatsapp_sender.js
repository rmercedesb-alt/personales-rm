/**
 * WhatsApp Sender - AM Comercial
 * Envía mensajes, PDFs e imágenes por WhatsApp automáticamente
 * Usa whatsapp-web.js con tu sesión existente
 */

const { Client, LocalAuth, MessageMedia } = require('whatsapp-web.js');
const qrcode = require('qrcode-terminal');
const fs = require('fs');
const path = require('path');
const readline = require('readline');

// ─── CONFIGURACION ────────────────────────────────────────────────
const RUTA_BASE      = 'C:\\Personales RM\\Claude';
const RUTA_CONTACTOS = path.join(RUTA_BASE, 'contactos.json');

// ─── CARGAR CONTACTOS ─────────────────────────────────────────────
function cargarContactos() {
    if (!fs.existsSync(RUTA_CONTACTOS)) {
        console.log(`ERROR: No se encontró ${RUTA_CONTACTOS}`);
        process.exit(1);
    }
    return JSON.parse(fs.readFileSync(RUTA_CONTACTOS, 'utf8'));
}

function buscarNumero(termino, datos) {
    // Número directo
    if (termino.startsWith('+')) return [termino];

    // Buscar en grupos
    for (const g of datos.grupos) {
        if (g.nombre.toLowerCase() === termino.toLowerCase()) {
            return g.miembros;
        }
    }

    // Buscar exacto en contactos
    for (const c of datos.contactos) {
        if (c.nombre.toLowerCase() === termino.toLowerCase()) {
            return [c.numero_principal || c.numero];
        }
    }

    // Búsqueda parcial
    const coincidencias = datos.contactos.filter(c =>
        c.nombre.toLowerCase().includes(termino.toLowerCase())
    );

    if (coincidencias.length === 0) {
        console.log(`⚠️  No se encontró: ${termino}`);
        return [];
    } else if (coincidencias.length === 1) {
        const num = coincidencias[0].numero_principal || coincidencias[0].numero;
        console.log(`✅ Encontrado: ${coincidencias[0].nombre} — ${num}`);
        return [num];
    } else {
        console.log(`\n🔍 ${coincidencias.length} coincidencias:`);
        coincidencias.slice(0, 10).forEach((c, i) => {
            const num = c.numero_principal || c.numero;
            console.log(`  ${i+1}. ${c.nombre} — ${num}`);
        });
        return null; // señal de que hay que elegir
    }
}

function formatearNumero(numero) {
    // WhatsApp requiere formato: 18092849797@c.us
    const limpio = numero.replace(/\D/g, '');
    return `${limpio}@c.us`;
}

// ─── READLINE HELPER ──────────────────────────────────────────────
const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout
});

function preguntar(texto) {
    return new Promise(resolve => rl.question(texto, resolve));
}

function listarContactos(datos) {
    console.log('\n📋 CONTACTOS (primeros 20):');
    console.log('─'.repeat(45));
    datos.contactos.slice(0, 20).forEach(c => {
        const num = c.numero_principal || c.numero || '—';
        console.log(`  👤 ${c.nombre} — ${num}`);
    });
    console.log(`\n  ... y ${datos.contactos.length - 20} más`);
    console.log('\n📋 GRUPOS:');
    console.log('─'.repeat(45));
    datos.grupos.forEach(g => {
        console.log(`  👥 ${g.nombre} — ${g.miembros.length} miembros`);
    });
    console.log();
}

// ─── ENVIO ────────────────────────────────────────────────────────
async function enviarMensaje(client, numero, mensaje) {
    try {
        const chatId = formatearNumero(numero);
        await client.sendMessage(chatId, mensaje);
        console.log(`  ✅ Mensaje enviado a ${numero}`);
        return true;
    } catch (e) {
        console.log(`  ❌ Error enviando a ${numero}: ${e.message}`);
        return false;
    }
}

async function enviarArchivo(client, numero, rutaArchivo, caption = '') {
    try {
        if (!fs.existsSync(rutaArchivo)) {
            console.log(`  ❌ Archivo no encontrado: ${rutaArchivo}`);
            return false;
        }
        const chatId  = formatearNumero(numero);
        const media   = MessageMedia.fromFilePath(rutaArchivo);
        await client.sendMessage(chatId, media, { caption });
        console.log(`  ✅ Archivo enviado a ${numero}`);
        return true;
    } catch (e) {
        console.log(`  ❌ Error enviando archivo a ${numero}: ${e.message}`);
        return false;
    }
}

async function enviarALista(client, numeros, tipo, mensaje = '', rutaArchivo = '', caption = '') {
    let exitosos = 0;
    for (const num of numeros) {
        console.log(`\n  📤 Enviando a ${num}...`);
        let ok = false;
        if (tipo === 'texto') {
            ok = await enviarMensaje(client, num, mensaje);
        } else if (tipo === 'archivo') {
            ok = await enviarArchivo(client, num, rutaArchivo, caption);
        } else if (tipo === 'ambos') {
            const ok1 = await enviarMensaje(client, num, mensaje);
            await new Promise(r => setTimeout(r, 2000));
            const ok2 = await enviarArchivo(client, num, rutaArchivo, caption);
            ok = ok1 && ok2;
        }
        if (ok) exitosos++;
        await new Promise(r => setTimeout(r, 3000)); // pausa entre envíos
    }
    return exitosos;
}

// ─── MAIN ─────────────────────────────────────────────────────────
async function main() {
    const ahora = new Date().toLocaleString('es-DO');
    console.log(`\n${'='.repeat(55)}`);
    console.log(`  WHATSAPP SENDER — AM COMERCIAL`);
    console.log(`  ${ahora}`);
    console.log(`${'='.repeat(55)}\n`);

    // Inicializar cliente WhatsApp
    console.log('🔄 Iniciando WhatsApp...');
    const client = new Client({
        authStrategy: new LocalAuth({ dataPath: path.join(RUTA_BASE, '.wwebjs_auth') }),
        puppeteer: { headless: true, args: ['--no-sandbox'] }
    });

    client.on('qr', qr => {
        console.log('\n📱 Escanea este QR con tu WhatsApp (solo la primera vez):');
        qrcode.generate(qr, { small: true });
    });

    client.on('ready', async () => {
        console.log('✅ WhatsApp conectado correctamente\n');

        const datos = cargarContactos();

        console.log('¿Qué deseas hacer?');
        console.log('  1. Enviar mensaje de texto');
        console.log('  2. Enviar archivo (PDF o imagen)');
        console.log('  3. Enviar texto + archivo');
        console.log('  4. Ver contactos disponibles');
        console.log();

        const opcion = (await preguntar('Selecciona una opción (1-4): ')).trim();

        if (opcion === '4') {
            listarContactos(datos);
            rl.close();
            await client.destroy();
            return;
        }

        const termino = (await preguntar('¿A quién enviar? (nombre, grupo o +1809XXXXXXX): ')).trim();
        let numeros = buscarNumero(termino, datos);

        // Si hay múltiples coincidencias, pedir selección
        if (numeros === null) {
            const coincidencias = datos.contactos.filter(c =>
                c.nombre.toLowerCase().includes(termino.toLowerCase())
            ).slice(0, 10);
            const sel = (await preguntar('\nElige el número (1-10): ')).trim();
            if (!isNaN(sel) && sel >= 1 && sel <= coincidencias.length) {
                const c = coincidencias[parseInt(sel) - 1];
                numeros = [c.numero_principal || c.numero];
            } else {
                console.log('❌ Selección inválida.');
                rl.close();
                await client.destroy();
                return;
            }
        }

        if (!numeros || numeros.length === 0) {
            console.log('❌ No se encontraron destinatarios.');
            rl.close();
            await client.destroy();
            return;
        }

        console.log(`\n✅ Destinatarios: ${numeros.length}`);
        numeros.forEach(n => console.log(`   → ${n}`));

        let mensaje = '', rutaArch = '', caption = '';

        if (['1', '3'].includes(opcion)) {
            mensaje = (await preguntar('\nEscribe el mensaje: ')).trim();
        }

        if (['2', '3'].includes(opcion)) {
            rutaArch = (await preguntar('\nRuta del archivo: ')).trim().replace(/"/g, '');
            caption  = (await preguntar('Caption (Enter para omitir): ')).trim();
        }

        const tipoMap = { '1': 'texto', '2': 'archivo', '3': 'ambos' };
        const tipo = tipoMap[opcion] || 'texto';

        console.log(`\n🚀 Iniciando envío a ${numeros.length} destinatario(s)...\n`);
        const exitosos = await enviarALista(client, numeros, tipo, mensaje, rutaArch, caption);

        console.log(`\n${'='.repeat(55)}`);
        console.log(`  ✅ Completados: ${exitosos}/${numeros.length}`);
        console.log(`${'='.repeat(55)}\n`);

        rl.close();
        await client.destroy();
    });

    client.on('auth_failure', () => {
        console.log('❌ Error de autenticación. Borra la carpeta .wwebjs_auth y vuelve a intentar.');
    });

    client.initialize();
}

main().catch(console.error);
