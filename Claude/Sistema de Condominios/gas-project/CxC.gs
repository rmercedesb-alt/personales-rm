// ============================================================
// CxC.gs — Generación de CxC, mora y estados
// ============================================================

/**
 * Job diario automático. Se ejecuta via trigger.
 */
function cronDiario() {
  try {
    Logger.log('=== INICIO cronDiario ' + new Date().toISOString() + ' ===');

    paso1_GenerarCxC();
    paso2_ActualizarMora();
    paso3_CambiarEstadoEnMora();
    paso4_AlertaArt1762();
    paso5_VerificarSesionCaja();
    paso6_ActualizarDashboard();
    paso7_GenerarAvisosWhatsApp();

    Logger.log('=== FIN cronDiario OK ===');
  } catch (e) {
    logError('cronDiario', e);
  }
}

// ──────────────────────────────────────────────
// PASO 1: Generar CxC nuevos
// ──────────────────────────────────────────────
function paso1_GenerarCxC() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hojaContratos = ss.getSheetByName('📋 CONTRATOS');
  var datosContratos = hojaContratos.getDataRange().getValues();
  var hoy = new Date();
  var diaHoy = hoy.getDate();
  var generados = 0;

  for (var i = 1; i < datosContratos.length; i++) {
    var c = datosContratos[i];
    if (!c[0] || c[21] !== 'VIGENTE') continue; // Solo contratos vigentes

    var idContrato = c[0];
    var diaCobro1 = parseInt(c[10]) || 0;
    var diaCobro2 = parseInt(c[11]) || 0;
    var periodicidad = c[9];
    var canonMensual = parseFloat(c[13]) || 0;
    var diasGracia = parseInt(c[12]) || 5;

    var esDiaCobro = (diaHoy === diaCobro1) || (periodicidad === 'QUINCENAL' && diaHoy === diaCobro2);
    if (!esDiaCobro) continue;

    // Calcular período
    var periodoInicio = new Date(hoy.getFullYear(), hoy.getMonth(), diaCobro1);
    var periodoFin = new Date(hoy.getFullYear(), hoy.getMonth() + 1, diaCobro1 - 1);
    if (periodicidad === 'QUINCENAL' && diaHoy === diaCobro2) {
      periodoInicio = new Date(hoy.getFullYear(), hoy.getMonth(), diaCobro2);
      periodoFin = new Date(hoy.getFullYear(), hoy.getMonth(), diaCobro1 - 1);
    }

    // Verificar que no existe ya un CxC para este período
    if (existeCxCParaPeriodo(idContrato, 'ALQUILER', periodoInicio)) continue;

    // Crear CxC de alquiler
    var fechaVencimiento = new Date(hoy.getFullYear(), hoy.getMonth(), diaCobro1 + diasGracia);
    crearCxC({
      id_contrato: idContrato,
      tipo_cargo: 'ALQUILER',
      periodo_inicio: periodoInicio,
      periodo_fin: periodoFin,
      fecha_vencimiento: fechaVencimiento,
      monto_principal: canonMensual
    });
    generados++;

    // Procesar cargos adicionales
    var cargosAdicionales = obtenerCargosAdicionalesContrato(idContrato);
    cargosAdicionales.forEach(function(ca) {
      var esQuincenal = ca.periodicidad === 'QUINCENAL';
      var debeGenerar = (diaHoy === diaCobro1) || (esQuincenal && diaHoy === diaCobro2);
      if (!debeGenerar) return;
      if (existeCxCParaPeriodo(idContrato, ca.nombre, periodoInicio)) return;

      var tipoCargo = ca.incluir_en_alquiler === 'SI' ? 'ALQUILER' : ca.nombre.toUpperCase().substring(0, 15);
      crearCxC({
        id_contrato: idContrato,
        tipo_cargo: tipoCargo,
        periodo_inicio: periodoInicio,
        periodo_fin: periodoFin,
        fecha_vencimiento: fechaVencimiento,
        monto_principal: parseFloat(ca.monto)
      });
      generados++;
    });
  }
  Logger.log('PASO 1: ' + generados + ' CxC generados.');
}

/**
 * Verifica si ya existe un CxC para el contrato + tipo + período.
 */
function existeCxCParaPeriodo(idContrato, tipoCargo, periodoInicio) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💰 CXC_CARGOS');
  var datos = hoja.getDataRange().getValues();
  var periodoStr = periodoInicio.toDateString();

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][1] === idContrato &&
        datos[i][8] === tipoCargo &&
        datos[i][9] && new Date(datos[i][9]).toDateString() === periodoStr &&
        datos[i][18] !== 'ANULADO') {
      return true;
    }
  }
  return false;
}

/**
 * Crea un nuevo registro en CXC_CARGOS.
 */
function crearCxC(datos) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💰 CXC_CARGOS');

  // Obtener datos del contrato para desnormalizar
  var contrato = obtenerContrato(datos.id_contrato);
  if (!contrato) throw new Error('Contrato no encontrado: ' + datos.id_contrato);

  var id = generarIdUnico('CXC', '💰 CXC_CARGOS');
  var monto = parseFloat(datos.monto_principal) || 0;

  hoja.appendRow([
    id,
    datos.id_contrato,
    contrato['ID_INQUILINO'],
    contrato['NOMBRE_INQUILINO'],
    contrato['ID_INMUEBLE'],
    contrato['NOMBRE_INMUEBLE'],
    contrato['ID_PROPIETARIO'],
    contrato['NOMBRE_PROPIETARIO'],
    datos.tipo_cargo,
    datos.periodo_inicio,
    datos.periodo_fin,
    datos.fecha_vencimiento,
    monto,       // MONTO_PRINCIPAL
    0,           // MORA_ACUMULADA
    monto,       // MONTO_TOTAL
    0,           // MONTO_PAGADO
    monto,       // SALDO_PENDIENTE
    0,           // DIAS_MORA
    'PENDIENTE', // ESTADO
    '',          // FECHA_PAGO_TOTAL
    new Date(),  // FECHA_GENERACION
    datos.notas || ''
  ]);

  return id;
}

// ──────────────────────────────────────────────
// PASO 2: Actualizar mora
// ──────────────────────────────────────────────
function paso2_ActualizarMora() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
  var datos = hojaCxC.getDataRange().getValues();
  var hoy = new Date();
  var actualizados = 0;

  for (var i = 1; i < datos.length; i++) {
    var estado = datos[i][18];
    if (estado !== 'PENDIENTE' && estado !== 'PARCIAL' && estado !== 'EN_MORA') continue;
    if (!datos[i][0]) continue;

    var fechaVenc = new Date(datos[i][11]);
    if (isNaN(fechaVenc.getTime())) continue;

    // Obtener días de gracia del contrato
    var contrato = obtenerContrato(datos[i][1]);
    var diasGracia = contrato ? parseInt(contrato['DIAS_GRACIA']) || 5 : 5;

    var diasMora = Math.max(0, Math.floor((hoy - fechaVenc) / 86400000) - diasGracia);
    if (diasMora <= 0) continue;

    // Calcular mora según tipo del contrato
    var moraMensual = 0;
    if (contrato) {
      var tipomora = contrato['TIPO_MORA'];
      var montoPrincipal = parseFloat(datos[i][12]) || 0;
      if (tipomora === 'PORCENTAJE') {
        var pct = parseFloat(contrato['PCT_MORA_MENSUAL']) || parseFloat(obtenerConfig('PCT_MORA_DEFAULT')) || 0.03;
        moraMensual = montoPrincipal * pct * (diasMora / 30);
      } else {
        moraMensual = parseFloat(contrato['MONTO_MORA_FIJO']) || 0;
      }
    }
    var moraAcumulada = Math.round(moraMensual * 100) / 100;
    var montoPrincipal = parseFloat(datos[i][12]) || 0;
    var montoPagado = parseFloat(datos[i][15]) || 0;
    var montoTotal = montoPrincipal + moraAcumulada;
    var saldoPendiente = montoTotal - montoPagado;

    hojaCxC.getRange(i + 1, 14).setValue(moraAcumulada);  // MORA_ACUMULADA
    hojaCxC.getRange(i + 1, 15).setValue(montoTotal);     // MONTO_TOTAL
    hojaCxC.getRange(i + 1, 17).setValue(saldoPendiente); // SALDO_PENDIENTE
    hojaCxC.getRange(i + 1, 18).setValue(diasMora);       // DIAS_MORA
    actualizados++;
  }
  Logger.log('PASO 2: ' + actualizados + ' CxC con mora actualizada.');
}

// ──────────────────────────────────────────────
// PASO 3: Cambiar estado a EN_MORA
// ──────────────────────────────────────────────
function paso3_CambiarEstadoEnMora() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💰 CXC_CARGOS');
  var datos = hoja.getDataRange().getValues();
  var cambios = 0;

  for (var i = 1; i < datos.length; i++) {
    var diasMora = parseInt(datos[i][17]) || 0;
    var estado = datos[i][18];
    if (diasMora > 0 && (estado === 'PENDIENTE' || estado === 'PARCIAL')) {
      hoja.getRange(i + 1, 19).setValue('EN_MORA');
      cambios++;
    }
  }
  Logger.log('PASO 3: ' + cambios + ' CxC cambiados a EN_MORA.');
}

// ──────────────────────────────────────────────
// PASO 4: Alerta Art. 1762 (2+ períodos sin pago)
// ──────────────────────────────────────────────
function paso4_AlertaArt1762() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💰 CXC_CARGOS');
  var datos = hoja.getDataRange().getValues();

  // Agrupar CxC de alquiler pendientes/mora por inquilino
  var morosos = {};
  for (var i = 1; i < datos.length; i++) {
    var estado = datos[i][18];
    var tipo = datos[i][8];
    if (tipo !== 'ALQUILER') continue;
    if (estado !== 'PENDIENTE' && estado !== 'PARCIAL' && estado !== 'EN_MORA') continue;

    var idInquilino = datos[i][2];
    if (!morosos[idInquilino]) morosos[idInquilino] = [];
    morosos[idInquilino].push(datos[i][0]);
  }

  var alertas = [];
  Object.keys(morosos).forEach(function(inq) {
    if (morosos[inq].length >= 2) {
      alertas.push({ inquilino: inq, cxcs: morosos[inq].length });
    }
  });

  // Registrar en el array global de alertas del dashboard
  if (alertas.length > 0) {
    PropertiesService.getScriptProperties().setProperty(
      'ALERTAS_1762',
      JSON.stringify(alertas)
    );
  }
  Logger.log('PASO 4: ' + alertas.length + ' alertas Art. 1762.');
}

// ──────────────────────────────────────────────
// PASO 5: Verificar sesión de caja
// ──────────────────────────────────────────────
function paso5_VerificarSesionCaja() {
  var hora = new Date().getHours();
  if (hora < 20) return;

  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏧 SESIONES_CAJA');
  var datos = hoja.getDataRange().getValues();
  var hoy = new Date().toDateString();

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][10] === 'ABIERTA' && datos[i][1] && new Date(datos[i][1]).toDateString() === hoy) {
      PropertiesService.getScriptProperties().setProperty('ALERTA_SESION_ABIERTA', 'SI');
      return;
    }
  }
}

// ──────────────────────────────────────────────
// PASO 6: Actualizar Dashboard
// ──────────────────────────────────────────────
function paso6_ActualizarDashboard() {
  actualizarDashboard();
}

// ──────────────────────────────────────────────
// PASO 7: Generar avisos WhatsApp
// ──────────────────────────────────────────────
function paso7_GenerarAvisosWhatsApp() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💰 CXC_CARGOS');
  var datos = hoja.getDataRange().getValues();
  var hoy = new Date();
  var diasAviso = parseInt(obtenerConfig('DIAS_AVISO_PREVIO') || '5');
  var generados = 0;

  for (var i = 1; i < datos.length; i++) {
    if (!datos[i][0]) continue;
    var estado = datos[i][18];
    var fechaVenc = new Date(datos[i][11]);
    if (isNaN(fechaVenc.getTime())) continue;

    var diasParaVencer = Math.floor((fechaVenc - hoy) / 86400000);

    if (estado === 'PENDIENTE' || estado === 'PARCIAL') {
      if (diasParaVencer === diasAviso) {
        generarLinkWhatsApp(datos[i][0], 'VENCIMIENTO_PROXIMO');
        generados++;
      } else if (diasParaVencer === 0) {
        generarLinkWhatsApp(datos[i][0], 'VENCE_HOY');
        generados++;
      }
    } else if (estado === 'EN_MORA') {
      if (!yaEnvioAvisoReciente(datos[i][0], 7)) {
        generarLinkWhatsApp(datos[i][0], 'MORA_ACTIVA');
        generados++;
      }
    }
  }
  Logger.log('PASO 7: ' + generados + ' avisos WhatsApp generados.');
}

/**
 * Verifica si ya se generó un aviso para el CxC en los últimos N días.
 */
function yaEnvioAvisoReciente(idCxC, dias) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📢 NOTIFICACIONES');
  var datos = hoja.getDataRange().getValues();
  var hoy = new Date();
  var limite = new Date(hoy.getTime() - dias * 86400000);

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] && datos[i][0].toString().indexOf(idCxC) > -1) {
      var ts = new Date(datos[i][1]);
      if (ts >= limite) return true;
    }
  }
  return false;
}

/**
 * Recordatorio semanal (lunes) para mora activa.
 */
function generarRecordatoriosSemana() {
  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('💰 CXC_CARGOS');
    var datos = hoja.getDataRange().getValues();
    var generados = 0;

    for (var i = 1; i < datos.length; i++) {
      if (datos[i][18] === 'EN_MORA' && datos[i][0]) {
        generarLinkWhatsApp(datos[i][0], 'MORA_ACTIVA');
        generados++;
      }
    }
    Logger.log('Recordatorio semanal: ' + generados + ' avisos generados.');
  } catch (e) {
    logError('generarRecordatoriosSemana', e);
  }
}
