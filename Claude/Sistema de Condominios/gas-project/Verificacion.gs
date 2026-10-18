// ============================================================
// Verificacion.gs — Diagnóstico e integridad del sistema
// ============================================================

/**
 * Verifica que el sistema está correctamente configurado.
 * Devuelve HTML para mostrar en un modal.
 */
function verificarIntegridad() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var resultados = [];
  var errores = 0;
  var ok = 0;

  // 1. Verificar todas las pestañas requeridas
  var pestanasRequeridas = [
    '🏠 INICIO','👤 PROPIETARIOS','🏢 INMUEBLES','👥 INQUILINOS',
    '📋 CONTRATOS','➕ CARGOS_ADICIONALES','💰 CXC_CARGOS','💳 CXC_PAGOS',
    '🏧 SESIONES_CAJA','🔄 LIQUIDACIONES','📢 NOTIFICACIONES','📊 LOG_CUSTODIA',
    '📈 REPORTE','⚙ CONFIG','🔒 ROLES','CARGA_CXC','CARGA_PAGOS'
  ];

  pestanasRequeridas.forEach(function(nombre) {
    var hoja = ss.getSheetByName(nombre);
    if (hoja) {
      var filas = contarFilasConDatos(hoja);
      resultados.push({ ok: true, texto: '✅ ' + nombre + ' (' + filas + ' registros)' });
      ok++;
    } else {
      resultados.push({ ok: false, texto: '❌ FALTA: ' + nombre });
      errores++;
    }
  });

  // 2. Verificar triggers instalados
  var triggers = ScriptApp.getProjectTriggers();
  var nombresTriggers = triggers.map(function(t) { return t.getHandlerFunction(); });
  var triggersReq = ['onOpen','onEdit','cronDiario','generarRecordatoriosSemana'];
  triggersReq.forEach(function(t) {
    if (nombresTriggers.indexOf(t) > -1) {
      resultados.push({ ok: true, texto: '✅ Trigger: ' + t });
      ok++;
    } else {
      resultados.push({ ok: false, texto: '⚠ Trigger no instalado: ' + t + ' — Use Menú → Instalar Triggers' });
      errores++;
    }
  });

  // 3. Verificar carpeta de Drive
  var folderId = obtenerConfig('GOOGLE_DRIVE_FOLDER_ID');
  if (folderId) {
    try {
      DriveApp.getFolderById(folderId);
      resultados.push({ ok: true, texto: '✅ Carpeta Drive configurada: ' + folderId });
      ok++;
    } catch (e) {
      resultados.push({ ok: false, texto: '❌ Carpeta Drive no accesible: ' + folderId });
      errores++;
    }
  } else {
    resultados.push({ ok: false, texto: '⚠ Carpeta Drive no configurada en CONFIG — PDFs no se generarán' });
  }

  // 4. Verificar plantillas PDF
  var templates = ['TEMPLATE_RECIBO_ID','TEMPLATE_CIERRE_CAJA_ID','TEMPLATE_LIQUIDACION_ID'];
  templates.forEach(function(t) {
    var val = obtenerConfig(t);
    if (val) {
      resultados.push({ ok: true, texto: '✅ Plantilla configurada: ' + t });
      ok++;
    } else {
      resultados.push({ ok: false, texto: '⚠ Plantilla no configurada: ' + t });
    }
  });

  // 5. Conteo de registros (solo filas con ID en columna A, ignorando fórmulas vacías)
  var hojas = ['👤 PROPIETARIOS','🏢 INMUEBLES','👥 INQUILINOS','📋 CONTRATOS','💰 CXC_CARGOS','💳 CXC_PAGOS'];
  hojas.forEach(function(nombre) {
    var h = ss.getSheetByName(nombre);
    if (h) {
      var n = contarFilasConDatos(h);
      resultados.push({ ok: true, texto: 'ℹ ' + nombre + ': ' + n + ' registros' });
    }
  });

  // 6. Verificar FKs huérfanas
  var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
  var hojaContratos = ss.getSheetByName('📋 CONTRATOS');
  if (hojaCxC && hojaContratos) {
    var datosCxC = hojaCxC.getDataRange().getValues();
    var idsContratos = hojaContratos.getDataRange().getValues().slice(1).map(function(r) { return r[0]; });
    var huerfanos = 0;
    for (var i = 1; i < datosCxC.length; i++) {
      if (datosCxC[i][0] && idsContratos.indexOf(datosCxC[i][1]) === -1) huerfanos++;
    }
    if (huerfanos > 0) {
      resultados.push({ ok: false, texto: '⚠ ' + huerfanos + ' CxC con contrato no encontrado (FK huérfana)' });
      errores++;
    } else {
      resultados.push({ ok: true, texto: '✅ No hay FKs huérfanas en CXC_CARGOS' });
    }
  }

  // 7. Info del sistema
  var email = Session.getActiveUser().getEmail();
  resultados.push({ ok: true, texto: 'ℹ Usuario: ' + email });
  resultados.push({ ok: true, texto: 'ℹ Versión: ' + (obtenerConfig('VERSION_SISTEMA') || 'N/A') });

  // Generar HTML de resultado
  var html = '<html><body style="font-family:Arial;font-size:13px;padding:15px;">' +
    '<h2 style="color:#1B4F8C;">Verificación de Integridad del Sistema</h2>' +
    '<p>' + ok + ' verificaciones OK | ' + errores + ' errores/advertencias</p><hr>' +
    '<ul style="list-style:none;padding:0;">';

  resultados.forEach(function(r) {
    html += '<li style="padding:4px 0;color:' + (r.ok ? '#2E7D32' : '#B71C1C') + '">' + r.texto + '</li>';
  });

  html += '</ul>';

  if (errores === 0) {
    html += '<p style="color:#2E7D32;font-weight:bold;">✅ Sistema listo para producción.</p>';
  } else {
    html += '<p style="color:#E65100;font-weight:bold;">⚠ Resolver los errores antes de usar en producción.</p>';
  }

  html += '</body></html>';
  return html;
}

/**
 * Cuenta filas que tienen un valor real en la columna A (ignora fórmulas vacías).
 */
function contarFilasConDatos(hoja) {
  var lastRow = hoja.getLastRow();
  if (lastRow <= 1) return 0;
  var colA = hoja.getRange(2, 1, lastRow - 1, 1).getValues();
  var count = 0;
  for (var i = 0; i < colA.length; i++) {
    if (colA[i][0] !== '' && colA[i][0] !== null) count++;
  }
  return count;
}
