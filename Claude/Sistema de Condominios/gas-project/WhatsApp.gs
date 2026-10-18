// ============================================================
// WhatsApp.gs — Generación de links wa.me y registro de notificaciones
// ============================================================

/**
 * Plantillas de mensajes WhatsApp.
 */
var PLANTILLAS_WA = {
  VENCIMIENTO_PROXIMO: 'Estimado/a [NOMBRE], le recordamos que su pago de alquiler de [INMUEBLE] por RD$[MONTO] vence el [FECHA]. Puede contactarnos para coordinar el pago. Gracias.',
  VENCE_HOY:           'Estimado/a [NOMBRE], su pago de alquiler de [INMUEBLE] por RD$[MONTO] vence HOY. Por favor realice su pago a la brevedad para evitar cargos por mora.',
  MORA_ACTIVA:         'Estimado/a [NOMBRE], su cuenta de [INMUEBLE] tiene un saldo pendiente de RD$[SALDO] con [DIAS] días de atraso. Mora acumulada: RD$[MORA]. Le solicitamos regularizar su situación.',
  PAGO_CONFIRMADO:     'Estimado/a [NOMBRE], confirmamos la recepción de su pago de RD$[MONTO] correspondiente a [PERIODO] del inmueble [INMUEBLE]. Saldo pendiente: RD$[SALDO]. Gracias.',
  RENOVACION_CONTRATO: 'Estimado/a [NOMBRE], su contrato de arrendamiento de [INMUEBLE] vence el [FECHA_VENC]. Por favor contáctenos para coordinar la renovación.'
};

/**
 * Genera el link wa.me para un CxC o pago y lo registra en NOTIFICACIONES.
 * @param {string} idCxC - ID del cargo CxC
 * @param {string} tipo  - Tipo de mensaje (VENCIMIENTO_PROXIMO, MORA_ACTIVA, etc.)
 * @param {string} idPago - ID del pago (solo para PAGO_CONFIRMADO)
 */
function generarLinkWhatsApp(idCxC, tipo, idPago) {
  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
    var datosCxC = hojaCxC.getDataRange().getValues();

    var cxc = null;
    for (var i = 1; i < datosCxC.length; i++) {
      if (datosCxC[i][0] === idCxC) { cxc = datosCxC[i]; break; }
    }
    if (!cxc) return '';

    // Obtener teléfono del inquilino
    var hojaInq = ss.getSheetByName('👥 INQUILINOS');
    var datosInq = hojaInq.getDataRange().getValues();
    var telefono = '';
    var nombreInquilino = cxc[3];

    for (var j = 1; j < datosInq.length; j++) {
      if (datosInq[j][0] === cxc[2]) {
        telefono = datosInq[j][3] || '';
        nombreInquilino = datosInq[j][1] || cxc[3];
        break;
      }
    }

    if (!telefono) return '';

    // Construir mensaje
    var plantilla = PLANTILLAS_WA[tipo] || PLANTILLAS_WA['VENCIMIENTO_PROXIMO'];
    var saldo = parseFloat(cxc[16]) || 0;
    var mora = parseFloat(cxc[13]) || 0;
    var diasMora = parseInt(cxc[17]) || 0;
    var periodoStr = formatearFecha(cxc[9]) + ' al ' + formatearFecha(cxc[10]);
    var montoTotal = parseFloat(cxc[14]) || 0;

    // Para PAGO_CONFIRMADO, tomar monto del pago
    var montoPago = montoTotal;
    if (tipo === 'PAGO_CONFIRMADO' && idPago) {
      var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
      var datosPagos = hojaPagos.getDataRange().getValues();
      for (var k = 1; k < datosPagos.length; k++) {
        if (datosPagos[k][0] === idPago) {
          montoPago = parseFloat(datosPagos[k][10]) || 0;
          break;
        }
      }
    }

    var mensaje = plantilla
      .replace('[NOMBRE]', nombreInquilino)
      .replace('[INMUEBLE]', cxc[5] || '')
      .replace('[MONTO]', formatearNumero(montoTotal))
      .replace('[FECHA]', formatearFecha(cxc[11]))
      .replace('[SALDO]', formatearNumero(saldo))
      .replace('[MORA]', formatearNumero(mora))
      .replace('[DIAS]', String(diasMora))
      .replace('[PERIODO]', periodoStr)
      .replace('[FECHA_VENC]', formatearFecha(cxc[11]));

    if (tipo === 'PAGO_CONFIRMADO') {
      mensaje = mensaje.replace('[MONTO]', formatearNumero(montoPago));
    }

    // Construir URL wa.me
    var telefonoLimpio = telefono.replace(/[^0-9]/g, '');
    var mensajeCodificado = encodeURIComponent(mensaje);
    var waLink = 'https://wa.me/' + telefonoLimpio + '?text=' + mensajeCodificado;

    // Registrar en NOTIFICACIONES
    registrarNotificacion({
      tipo: tipo,
      id_inquilino: cxc[2],
      nombre_inquilino: nombreInquilino,
      id_inmueble: cxc[4],
      nombre_inmueble: cxc[5],
      telefono: telefono,
      wa_link: waLink,
      mensaje: mensaje
    });

    return waLink;

  } catch (e) {
    Logger.log('Error en generarLinkWhatsApp: ' + e.toString());
    return '';
  }
}

/**
 * Formatea un número como entero con comas (sin símbolo RD$).
 */
function formatearNumero(n) {
  return (parseFloat(n) || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Registra una notificación en la pestaña NOTIFICACIONES.
 */
function registrarNotificacion(datos) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📢 NOTIFICACIONES');
  var id = generarIdUnico('NOT', '📢 NOTIFICACIONES');

  hoja.appendRow([
    id,
    new Date(),
    datos.tipo || '',
    datos.id_inquilino || '',
    datos.nombre_inquilino || '',
    datos.id_inmueble || '',
    datos.nombre_inmueble || '',
    datos.telefono || '',
    datos.wa_link || '',
    datos.mensaje || '',
    'PENDIENTE',
    ''
  ]);
}

/**
 * Marca una notificación como enviada.
 */
function marcarNotificacionEnviada(idNotificacion) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📢 NOTIFICACIONES');
  var datos = hoja.getDataRange().getValues();
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === idNotificacion) {
      hoja.getRange(i + 1, 11).setValue('ENVIADO');
      hoja.getRange(i + 1, 12).setValue(new Date());
      return;
    }
  }
}
