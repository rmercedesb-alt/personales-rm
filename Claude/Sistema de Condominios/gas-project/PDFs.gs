// ============================================================
// PDFs.gs — Generación de documentos PDF via Google Docs
// ============================================================

/**
 * Genera el recibo de pago en PDF y lo guarda en Drive.
 * Devuelve la URL del PDF.
 */
function generarReciboPDF(idPago) {
  var templateId = obtenerConfig('TEMPLATE_RECIBO_ID');
  if (!templateId) {
    Logger.log('Template de recibo no configurado. Saltando generación de PDF.');
    return '';
  }

  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
  var datosPagos = hojaPagos.getDataRange().getValues();

  // Buscar el pago
  var pago = null;
  var filaPago = -1;
  for (var i = 1; i < datosPagos.length; i++) {
    if (datosPagos[i][0] === idPago) {
      pago = datosPagos[i];
      filaPago = i + 1;
      break;
    }
  }
  if (!pago) throw new Error('Pago no encontrado para PDF: ' + idPago);

  // Buscar el CxC asociado para período
  var idCxC = pago[1];
  var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
  var datosCxC = hojaCxC.getDataRange().getValues();
  var cxc = null;
  for (var j = 1; j < datosCxC.length; j++) {
    if (datosCxC[j][0] === idCxC) { cxc = datosCxC[j]; break; }
  }

  // Buscar datos del inquilino
  var hojaInq = ss.getSheetByName('👥 INQUILINOS');
  var datosInq = hojaInq.getDataRange().getValues();
  var inquilino = null;
  for (var k = 1; k < datosInq.length; k++) {
    if (datosInq[k][0] === pago[3]) { inquilino = datosInq[k]; break; }
  }

  // Preparar marcadores
  var marcadores = {
    '{{EMPRESA_NOMBRE}}':   obtenerConfig('EMPRESA_NOMBRE') || 'Mi Empresa',
    '{{EMPRESA_RNC}}':      obtenerConfig('EMPRESA_RNC') || '',
    '{{EMPRESA_TELEFONO}}': obtenerConfig('EMPRESA_TELEFONO') || '',
    '{{RECIBO_NUM}}':       idPago,
    '{{FECHA_PAGO}}':       formatearFecha(pago[9]),
    '{{REGISTRADO_POR}}':   pago[16] || '',
    '{{INQUILINO_NOMBRE}}': pago[4] || '',
    '{{INQUILINO_CEDULA}}': inquilino ? inquilino[2] : '',
    '{{INQUILINO_WHATSAPP}}': inquilino ? inquilino[3] : '',
    '{{INMUEBLE_REFERENCIA}}': pago[6] || '',
    '{{INMUEBLE_DIRECCION}}': '',
    '{{PROPIETARIO_NOMBRE}}': pago[8] || '',
    '{{PERIODO}}': cxc ? (formatearFecha(cxc[9]) + ' al ' + formatearFecha(cxc[10])) : '',
    '{{TIPO_CARGO}}':       cxc ? cxc[8] : '',
    '{{MONTO_PRINCIPAL}}':  formatearMoneda(pago[12]),
    '{{MORA_APLICADA}}':    formatearMoneda(pago[11]),
    '{{TOTAL_PAGADO}}':     formatearMoneda(pago[10]),
    '{{FORMA_PAGO}}':       pago[13] || '',
    '{{REFERENCIA_PAGO}}':  pago[14] || '',
    '{{SALDO_PENDIENTE}}':  cxc ? formatearMoneda(cxc[16]) : ''
  };

  var url = generarPDFDesdeTemplate(templateId, marcadores, 'Recibo_' + idPago);

  // Actualizar URL en el pago
  if (url && filaPago > 0) {
    hojaPagos.getRange(filaPago, 24).setValue(url); // RECIBO_PDF_URL
  }

  return url;
}

/**
 * Genera el PDF de cierre de caja.
 */
function generarCierreCajaPDF(idSesion) {
  var templateId = obtenerConfig('TEMPLATE_CIERRE_CAJA_ID');
  if (!templateId) return '';

  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hojaSesiones = ss.getSheetByName('🏧 SESIONES_CAJA');
  var datos = hojaSesiones.getDataRange().getValues();

  var sesion = null;
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === idSesion) { sesion = datos[i]; break; }
  }
  if (!sesion) return '';

  // Obtener detalle de pagos de la sesión
  var pagos = obtenerPagosSesion(idSesion);
  var detallePagos = pagos.map(function(p) {
    return p.inquilino + ' | ' + p.inmueble + ' | ' + formatearMoneda(p.monto) + ' | ' + p.forma_pago;
  }).join('\n');

  var marcadores = {
    '{{EMPRESA_NOMBRE}}':     obtenerConfig('EMPRESA_NOMBRE') || '',
    '{{EMPRESA_RNC}}':        obtenerConfig('EMPRESA_RNC') || '',
    '{{SESION_ID}}':          idSesion,
    '{{FECHA_SESION}}':       formatearFecha(sesion[1]),
    '{{CAJERO}}':             sesion[2] || '',
    '{{ADMIN_CIERRE}}':       sesion[3] || '',
    '{{TOTAL_EFECTIVO}}':     formatearMoneda(sesion[4]),
    '{{TOTAL_TRANSFERENCIA}}': formatearMoneda(sesion[5]),
    '{{TOTAL_TARJETA}}':      formatearMoneda(sesion[6]),
    '{{TOTAL_CHEQUE}}':       formatearMoneda(sesion[7]),
    '{{TOTAL_SESION}}':       formatearMoneda(sesion[8]),
    '{{CANTIDAD_TRANS}}':     String(sesion[9]),
    '{{DETALLE_PAGOS}}':      detallePagos,
    '{{FECHA_CIERRE}}':       formatearFecha(sesion[14])
  };

  return generarPDFDesdeTemplate(templateId, marcadores, 'CierreCaja_' + idSesion);
}

/**
 * Genera el PDF de liquidación a propietario.
 */
function generarLiquidacionPDF(idLiquidacion) {
  var templateId = obtenerConfig('TEMPLATE_LIQUIDACION_ID');
  if (!templateId) return '';

  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hojaLiq = ss.getSheetByName('🔄 LIQUIDACIONES');
  var datos = hojaLiq.getDataRange().getValues();

  var liq = null;
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === idLiquidacion) { liq = datos[i]; break; }
  }
  if (!liq) return '';

  var marcadores = {
    '{{EMPRESA_NOMBRE}}':   obtenerConfig('EMPRESA_NOMBRE') || '',
    '{{LIQ_ID}}':           idLiquidacion,
    '{{PROPIETARIO}}':      liq[2] || '',
    '{{PERIODO_DESDE}}':    formatearFecha(liq[3]),
    '{{PERIODO_HASTA}}':    formatearFecha(liq[4]),
    '{{FILTRO}}':           liq[5] || 'Todos los inmuebles',
    '{{TOTAL_COBRADO}}':    formatearMoneda(liq[6]),
    '{{TOTAL_MORA}}':       formatearMoneda(liq[7]),
    '{{COMISION}}':         formatearMoneda(liq[8]),
    '{{MONTO_NETO}}':       formatearMoneda(liq[9]),
    '{{FECHA_LIQUIDACION}}': formatearFecha(liq[10])
  };

  return generarPDFDesdeTemplate(templateId, marcadores, 'Liquidacion_' + idLiquidacion);
}

/**
 * Función base: copia un Google Doc template, reemplaza marcadores y exporta a PDF.
 * Devuelve la URL del PDF en Drive.
 */
function generarPDFDesdeTemplate(templateId, marcadores, nombreArchivo) {
  var folderId = obtenerConfig('GOOGLE_DRIVE_FOLDER_ID');
  var folder = folderId
    ? DriveApp.getFolderById(folderId)
    : DriveApp.getRootFolder();

  // Copiar la plantilla
  var templateFile = DriveApp.getFileById(templateId);
  var copia = templateFile.makeCopy(nombreArchivo, folder);
  var doc = DocumentApp.openById(copia.getId());
  var body = doc.getBody();

  // Reemplazar marcadores
  Object.keys(marcadores).forEach(function(marcador) {
    body.replaceText(marcador, marcadores[marcador] || '');
  });

  doc.saveAndClose();

  // Exportar a PDF
  var pdfBlob = DriveApp.getFileById(copia.getId()).getAs('application/pdf');
  var pdfFile = folder.createFile(pdfBlob);
  pdfFile.setName(nombreArchivo + '.pdf');

  // Eliminar la copia del Doc (mantener solo el PDF)
  copia.setTrashed(true);

  return pdfFile.getUrl();
}

/**
 * Crea las plantillas de Google Docs en Drive si no existen.
 * Llama desde el menú Sistema → Configurar Plantillas.
 */
function crearPlantillasEnDrive() {
  try {
    verificarRol('ADMINISTRADOR');

    var folderId = obtenerConfig('GOOGLE_DRIVE_FOLDER_ID');
    var folder = folderId
      ? DriveApp.getFolderById(folderId)
      : crearCarpetaSistema();

    var templates = crearDocTemplates(folder);

    // Guardar IDs en CONFIG
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hojaConfig = ss.getSheetByName('⚙ CONFIG');
    var datos = hojaConfig.getDataRange().getValues();

    var mapa = {
      'TEMPLATE_RECIBO_ID':        templates.recibo,
      'TEMPLATE_CIERRE_CAJA_ID':   templates.cierre,
      'TEMPLATE_LIQUIDACION_ID':   templates.liquidacion,
      'GOOGLE_DRIVE_FOLDER_ID':    folder.getId()
    };

    for (var i = 1; i < datos.length; i++) {
      if (mapa[datos[i][0]]) {
        hojaConfig.getRange(i + 1, 2).setValue(mapa[datos[i][0]]);
      }
    }

    mostrarExito('✅ Plantillas creadas en Drive:\n• Recibo: ' + templates.recibo + '\n• Cierre Caja: ' + templates.cierre + '\n• Liquidación: ' + templates.liquidacion);

  } catch (e) {
    logError('crearPlantillasEnDrive', e);
    mostrarError(e.message);
  }
}

/**
 * Crea la carpeta del sistema en Drive.
 */
function crearCarpetaSistema() {
  var nombreCarpeta = 'Sistema Administración Inmuebles — PDFs';
  var carpeta = DriveApp.createFolder(nombreCarpeta);
  return carpeta;
}

/**
 * Crea los tres documentos plantilla en la carpeta de Drive.
 */
function crearDocTemplates(folder) {
  var empresa = obtenerConfig('EMPRESA_NOMBRE') || 'Mi Empresa';

  // Plantilla Recibo
  var docRecibo = DocumentApp.create('PLANTILLA_RECIBO');
  var bodyRecibo = docRecibo.getBody();
  bodyRecibo.setText(
    empresa + '\nRNC: {{EMPRESA_RNC}} | Tel: {{EMPRESA_TELEFONO}}\n\n' +
    'RECIBO DE PAGO Nº {{RECIBO_NUM}}\n' +
    'Fecha: {{FECHA_PAGO}} | Registrado por: {{REGISTRADO_POR}}\n\n' +
    'INQUILINO: {{INQUILINO_NOMBRE}}\n' +
    'Cédula: {{INQUILINO_CEDULA}} | WhatsApp: {{INQUILINO_WHATSAPP}}\n\n' +
    'INMUEBLE: {{INMUEBLE_REFERENCIA}}\n' +
    'Propietario: {{PROPIETARIO_NOMBRE}}\n\n' +
    'Período: {{PERIODO}}\n' +
    'Tipo de cargo: {{TIPO_CARGO}}\n\n' +
    'Monto principal:   {{MONTO_PRINCIPAL}}\n' +
    'Mora aplicada:     {{MORA_APLICADA}}\n' +
    'TOTAL PAGADO:      {{TOTAL_PAGADO}}\n\n' +
    'Forma de pago: {{FORMA_PAGO}}\n' +
    'Referencia: {{REFERENCIA_PAGO}}\n\n' +
    'Saldo pendiente: {{SALDO_PENDIENTE}}\n\n' +
    '_____________________\nFirma del administrador'
  );
  docRecibo.saveAndClose();
  var fileRecibo = DriveApp.getFileById(docRecibo.getId());
  fileRecibo.moveTo(folder);

  // Plantilla Cierre de Caja
  var docCierre = DocumentApp.create('PLANTILLA_CIERRE_CAJA');
  var bodyCierre = docCierre.getBody();
  bodyCierre.setText(
    empresa + ' — CIERRE DE CAJA\n\n' +
    'Sesión: {{SESION_ID}} | Fecha: {{FECHA_SESION}}\n' +
    'Cajero: {{CAJERO}} | Aprobado por: {{ADMIN_CIERRE}}\n\n' +
    'RESUMEN POR FORMA DE PAGO:\n' +
    'Efectivo:      {{TOTAL_EFECTIVO}}\n' +
    'Transferencia: {{TOTAL_TRANSFERENCIA}}\n' +
    'Tarjeta:       {{TOTAL_TARJETA}}\n' +
    'Cheque:        {{TOTAL_CHEQUE}}\n' +
    'TOTAL:         {{TOTAL_SESION}}\n' +
    'Transacciones: {{CANTIDAD_TRANS}}\n\n' +
    'DETALLE:\n{{DETALLE_PAGOS}}\n\n' +
    'Fecha cierre: {{FECHA_CIERRE}}\n\n' +
    '___________________\nFirma cajero\n\n' +
    '___________________\nFirma administrador'
  );
  docCierre.saveAndClose();
  var fileCierre = DriveApp.getFileById(docCierre.getId());
  fileCierre.moveTo(folder);

  // Plantilla Liquidación
  var docLiq = DocumentApp.create('PLANTILLA_LIQUIDACION');
  var bodyLiq = docLiq.getBody();
  bodyLiq.setText(
    empresa + '\n\nLIQUIDACIÓN A PROPIETARIO\n' +
    'Liquidación Nº: {{LIQ_ID}}\n\n' +
    'Propietario: {{PROPIETARIO}}\n' +
    'Período: {{PERIODO_DESDE}} al {{PERIODO_HASTA}}\n' +
    'Filtro aplicado: {{FILTRO}}\n\n' +
    'Total cobrado a inquilinos: {{TOTAL_COBRADO}}\n' +
    'Mora incluida:              {{TOTAL_MORA}}\n' +
    'Comisión administrador:     {{COMISION}}\n' +
    'MONTO NETO AL PROPIETARIO:  {{MONTO_NETO}}\n\n' +
    'Fecha de liquidación: {{FECHA_LIQUIDACION}}\n\n' +
    '___________________\nFirma propietario'
  );
  docLiq.saveAndClose();
  var fileLiq = DriveApp.getFileById(docLiq.getId());
  fileLiq.moveTo(folder);

  return {
    recibo:      docRecibo.getId(),
    cierre:      docCierre.getId(),
    liquidacion: docLiq.getId()
  };
}
