// ============================================================
// Dashboard.gs — Panel de KPIs y alertas del día
// ============================================================

/**
 * Actualiza la pestaña INICIO con KPIs y alertas actuales.
 * Llamada por cronDiario y por cada operación crítica.
 */
function actualizarDashboard() {
  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('🏠 INICIO');
    if (!hoja) return;

    hoja.clearContents();
    hoja.clearFormats();

    var kpis = calcularKPIs();
    var hoy = new Date();
    var empresa = obtenerConfig('EMPRESA_NOMBRE') || 'Sistema de Administración de Inmuebles';
    var version = obtenerConfig('VERSION_SISTEMA') || 'Beta 1.0';

    // ─────────────────────────────────────────────
    // FILAS 1-3: Encabezado
    // ─────────────────────────────────────────────
    hoja.getRange('A1:H1').merge().setValue(empresa)
      .setBackground('#1B4F8C').setFontColor('#FFFFFF')
      .setFontSize(16).setFontWeight('bold').setHorizontalAlignment('center');
    hoja.getRange('A2').setValue('Fecha: ' + formatearFecha(hoy)).setFontColor('#555555');
    hoja.getRange('E2').setValue('Versión: ' + version).setFontColor('#555555');
    hoja.setRowHeight(1, 40);

    // ─────────────────────────────────────────────
    // FILAS 5-10: KPIs en 3 columnas
    // ─────────────────────────────────────────────
    var kpiData = [
      ['💰 CxC Generado (mes)',          formatearMoneda(kpis.cxc_mes),          kpis.cxc_mes > 0 ? '#E8F5E9' : '#FAFAFA'],
      ['✅ Total Cobrado (mes)',           formatearMoneda(kpis.cobrado_mes),       kpis.cobrado_mes > 0 ? '#E8F5E9' : '#FFF9C4'],
      ['🔵 En Custodia (COBRADO)',        formatearMoneda(kpis.en_cobrado),        kpis.en_cobrado > 0 ? '#E3F2FD' : '#FAFAFA'],
      ['🟡 En Administración',            formatearMoneda(kpis.en_administracion), kpis.en_administracion > 0 ? '#FFF9C4' : '#FAFAFA'],
      ['🟢 Liquidado (mes)',              formatearMoneda(kpis.liquidado_mes),     kpis.liquidado_mes > 0 ? '#E8F5E9' : '#FAFAFA'],
      ['🔴 Pendiente de Cobro',          formatearMoneda(kpis.pendiente_cobro),   kpis.pendiente_cobro > 0 ? '#FFEBEE' : '#E8F5E9'],
      ['📊 Tasa de Cobro',               Math.round(kpis.tasa_cobro * 100) + '%', kpis.tasa_cobro >= 0.8 ? '#E8F5E9' : kpis.tasa_cobro >= 0.5 ? '#FFF9C4' : '#FFEBEE'],
      ['🏢 Inmuebles Ocupados',          String(kpis.ocupados) + ' inmuebles',    '#FAFAFA'],
      ['📅 Sesión de Caja Hoy',          kpis.sesion_hoy,                         kpis.sesion_hoy === 'APROBADA' ? '#E8F5E9' : kpis.sesion_hoy === 'ABIERTA' ? '#FFF9C4' : '#FAFAFA']
    ];

    var filaKPI = 5;
    // 3 columnas: A-B, D-E, G-H
    var cols = [[1,2],[4,5],[7,8]];
    var col = 0;
    var fila = filaKPI;

    kpiData.forEach(function(kpi, idx) {
      if (idx > 0 && idx % 3 === 0) {
        fila++;
        col = 0;
      }
      var c1 = cols[col][0];
      var c2 = cols[col][1];
      hoja.getRange(fila, c1).setValue(kpi[0]).setBackground('#ECEFF1').setFontWeight('bold').setFontSize(9);
      hoja.getRange(fila, c2).setValue(kpi[1]).setBackground(kpi[2]).setFontSize(11).setFontWeight('bold').setHorizontalAlignment('right');
      col++;
    });

    // ─────────────────────────────────────────────
    // FILA 12: Separador ALERTAS
    // ─────────────────────────────────────────────
    var filaAlertas = 12;
    hoja.getRange(filaAlertas, 1, 1, 8).merge()
      .setValue('⚠ ALERTAS DEL DÍA')
      .setBackground('#37474F').setFontColor('#FFFFFF')
      .setFontWeight('bold').setFontSize(11).setHorizontalAlignment('center');

    // ─────────────────────────────────────────────
    // FILAS 13-30: Panel de alertas
    // ─────────────────────────────────────────────
    var alertas = obtenerAlertas(kpis);
    var filaAlerta = filaAlertas + 1;

    if (alertas.length === 0) {
      hoja.getRange(filaAlerta, 1, 1, 8).merge()
        .setValue('✅ Sin alertas pendientes')
        .setBackground('#E8F5E9').setFontColor('#2E7D32').setHorizontalAlignment('center');
    } else {
      alertas.forEach(function(a) {
        if (filaAlerta > 30) return;
        hoja.getRange(filaAlerta, 1, 1, 8).merge()
          .setValue(a.texto)
          .setBackground(a.critica ? '#FFEBEE' : '#FFF9C4')
          .setFontColor(a.critica ? '#B71C1C' : '#E65100');
        filaAlerta++;
      });
    }

    // ─────────────────────────────────────────────
    // FILA 32: Sesión de caja
    // ─────────────────────────────────────────────
    hoja.getRange(32, 1, 1, 8).merge()
      .setValue('💳 SESIÓN DE CAJA HOY')
      .setBackground('#37474F').setFontColor('#FFFFFF')
      .setFontWeight('bold').setFontSize(11).setHorizontalAlignment('center');

    var sesion = obtenerSesionCajaActiva();
    if (sesion) {
      hoja.getRange('A33').setValue('ID Sesión:').setFontWeight('bold');
      hoja.getRange('B33').setValue(sesion.id);
      hoja.getRange('C33').setValue('Estado:').setFontWeight('bold');
      hoja.getRange('D33').setValue(sesion.estado).setBackground(sesion.estado === 'ABIERTA' ? '#FFF9C4' : '#E8F5E9');
      hoja.getRange('A34').setValue('Efectivo:').setFontWeight('bold');
      hoja.getRange('B34').setValue(formatearMoneda(sesion.total_efectivo));
      hoja.getRange('C34').setValue('Transferencia:').setFontWeight('bold');
      hoja.getRange('D34').setValue(formatearMoneda(sesion.total_transferencia));
      hoja.getRange('A35').setValue('Tarjeta:').setFontWeight('bold');
      hoja.getRange('B35').setValue(formatearMoneda(sesion.total_tarjeta));
      hoja.getRange('C35').setValue('Cheque:').setFontWeight('bold');
      hoja.getRange('D35').setValue(formatearMoneda(sesion.total_cheque));
      hoja.getRange('A36').setValue('TOTAL:').setFontWeight('bold').setFontSize(12);
      hoja.getRange('B36').setValue(formatearMoneda(sesion.total)).setFontWeight('bold').setFontSize(12);
      hoja.getRange('C36').setValue('Transacciones:').setFontWeight('bold');
      hoja.getRange('D36').setValue(sesion.cantidad);
    } else {
      hoja.getRange('A33:H33').merge().setValue('No hay sesión de caja activa hoy.').setFontColor('#9E9E9E');
    }

    // ─────────────────────────────────────────────
    // FILA 40+: Accesos rápidos
    // ─────────────────────────────────────────────
    hoja.getRange(40, 1, 1, 8).merge()
      .setValue('🚀 ACCESOS RÁPIDOS')
      .setBackground('#37474F').setFontColor('#FFFFFF')
      .setFontWeight('bold').setFontSize(11).setHorizontalAlignment('center');
    hoja.getRange('A41').setValue('Para registrar pagos, crear contratos y gestionar el sistema, use el menú ⚙ Admin Inmuebles.')
      .setFontColor('#555555').setFontStyle('italic');

  } catch (e) {
    Logger.log('Error en actualizarDashboard: ' + e.toString());
  }
}

/**
 * Calcula todos los KPIs del sistema.
 */
function calcularKPIs() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoy = new Date();
  var primerDiaMes = new Date(hoy.getFullYear(), hoy.getMonth(), 1);
  var ultimoDiaMes = new Date(hoy.getFullYear(), hoy.getMonth() + 1, 0);

  // CxC
  var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
  var datosCxC = hojaCxC ? hojaCxC.getDataRange().getValues() : [];
  var cxcMes = 0;
  var pendienteCobro = 0;

  for (var i = 1; i < datosCxC.length; i++) {
    if (!datosCxC[i][0]) continue;
    var estado = datosCxC[i][18];
    var fechaGen = new Date(datosCxC[i][20]);
    if (fechaGen >= primerDiaMes && fechaGen <= ultimoDiaMes) {
      cxcMes += parseFloat(datosCxC[i][14]) || 0;
    }
    if (estado !== 'PAGADO' && estado !== 'ANULADO') {
      pendienteCobro += parseFloat(datosCxC[i][16]) || 0;
    }
  }

  // Pagos
  var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
  var datosPagos = hojaPagos ? hojaPagos.getDataRange().getValues() : [];
  var cobradoMes = 0, enCobrado = 0, enAdmin = 0, liquidadoMes = 0;

  for (var j = 1; j < datosPagos.length; j++) {
    if (!datosPagos[j][0]) continue;
    var custodia = datosPagos[j][18];
    var monto = parseFloat(datosPagos[j][10]) || 0;
    var fechaPago = new Date(datosPagos[j][9]);

    if (fechaPago >= primerDiaMes && fechaPago <= ultimoDiaMes) {
      cobradoMes += monto;
    }
    if (custodia === 'COBRADO') enCobrado += monto;
    if (custodia === 'EN_ADMINISTRACION') enAdmin += monto;
    if (custodia === 'LIQUIDADO') {
      var fechaLiq = new Date(datosPagos[j][22]);
      if (fechaLiq >= primerDiaMes && fechaLiq <= ultimoDiaMes) liquidadoMes += monto;
    }
  }

  // Inmuebles ocupados
  var hojaInm = ss.getSheetByName('🏢 INMUEBLES');
  var datosInm = hojaInm ? hojaInm.getDataRange().getValues() : [];
  var ocupados = 0;
  for (var k = 1; k < datosInm.length; k++) {
    if (datosInm[k][20] === 'OCUPADO') ocupados++;
  }

  // Sesión de caja
  var sesion = obtenerSesionCajaActiva();
  var sesionHoy = sesion ? sesion.estado : 'Sin sesión';

  var tasaCobro = cxcMes > 0 ? cobradoMes / cxcMes : 0;

  return {
    cxc_mes: Math.round(cxcMes * 100) / 100,
    cobrado_mes: Math.round(cobradoMes * 100) / 100,
    en_cobrado: Math.round(enCobrado * 100) / 100,
    en_administracion: Math.round(enAdmin * 100) / 100,
    liquidado_mes: Math.round(liquidadoMes * 100) / 100,
    pendiente_cobro: Math.round(pendienteCobro * 100) / 100,
    tasa_cobro: Math.round(tasaCobro * 1000) / 1000,
    ocupados: ocupados,
    sesion_hoy: sesionHoy
  };
}

/**
 * Obtiene la lista de alertas del día.
 */
function obtenerAlertas(kpis) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoy = new Date();
  var alertas = [];

  // CxC que vencen HOY sin pago
  var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
  var datosCxC = hojaCxC ? hojaCxC.getDataRange().getValues() : [];
  var vencenHoy = [];

  for (var i = 1; i < datosCxC.length; i++) {
    if (!datosCxC[i][0]) continue;
    var estado = datosCxC[i][18];
    if (estado !== 'PENDIENTE' && estado !== 'PARCIAL') continue;
    var fechaVenc = new Date(datosCxC[i][11]);
    if (fechaVenc.toDateString() === hoy.toDateString()) {
      vencenHoy.push(datosCxC[i][3] + ' — ' + datosCxC[i][5] + ' — ' + formatearMoneda(datosCxC[i][16]));
    }
  }
  if (vencenHoy.length > 0) {
    alertas.push({ texto: '📅 Vencen HOY: ' + vencenHoy.join(' | '), critica: true });
  }

  // CxC en mora
  var enMoraHoy = datosCxC.filter(function(r) { return r[18] === 'EN_MORA' && r[0]; }).length;
  if (enMoraHoy > 0) {
    alertas.push({ texto: '🔴 CxC en mora: ' + enMoraHoy + ' cargos', critica: true });
  }

  // Alerta Art. 1762
  var alertas1762 = PropertiesService.getScriptProperties().getProperty('ALERTAS_1762');
  if (alertas1762) {
    var a = JSON.parse(alertas1762);
    if (a.length > 0) {
      alertas.push({ texto: '⚖ Art. 1762: ' + a.length + ' inquilinos con 2+ períodos sin pago', critica: true });
    }
  }

  // Sesión de caja abierta después de 8PM
  var hora = hoy.getHours();
  if (hora >= 20 && kpis.sesion_hoy === 'ABIERTA') {
    alertas.push({ texto: '⚠ Sesión de caja ABIERTA después de las 8 PM — falta cuadrar', critica: true });
  }

  // Pagos COBRADO de días anteriores
  var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
  var datosPagos = hojaPagos ? hojaPagos.getDataRange().getValues() : [];
  var cobradoAntiguo = 0;
  for (var j = 1; j < datosPagos.length; j++) {
    if (datosPagos[j][18] === 'COBRADO') {
      var fechaReg = new Date(datosPagos[j][24]);
      if (fechaReg.toDateString() !== hoy.toDateString()) cobradoAntiguo++;
    }
  }
  if (cobradoAntiguo > 0) {
    alertas.push({ texto: '💰 ' + cobradoAntiguo + ' pagos COBRADO de días anteriores sin cuadrar', critica: true });
  }

  // Contratos que vencen en 30 días
  var hojaContratos = ss.getSheetByName('📋 CONTRATOS');
  var datosContratos = hojaContratos ? hojaContratos.getDataRange().getValues() : [];
  var en30dias = new Date(hoy.getTime() + 30 * 86400000);
  var vencenContratos = [];
  for (var k = 1; k < datosContratos.length; k++) {
    if (datosContratos[k][21] !== 'VIGENTE') continue;
    var fechaVencContrato = new Date(datosContratos[k][8]);
    if (fechaVencContrato <= en30dias && fechaVencContrato >= hoy) {
      vencenContratos.push(datosContratos[k][2] + ' / ' + datosContratos[k][4]);
    }
  }
  if (vencenContratos.length > 0) {
    alertas.push({ texto: '📋 Contratos que vencen en 30 días: ' + vencenContratos.join(', '), critica: false });
  }

  // Avisos WhatsApp pendientes
  var hojaNotif = ss.getSheetByName('📢 NOTIFICACIONES');
  var datosNotif = hojaNotif ? hojaNotif.getDataRange().getValues() : [];
  var wasPendientes = datosNotif.filter(function(r) { return r[10] === 'PENDIENTE' && r[0]; }).length;
  if (wasPendientes > 0) {
    alertas.push({ texto: '📱 ' + wasPendientes + ' avisos WhatsApp generados pendientes de enviar', critica: false });
  }

  return alertas;
}

/**
 * Crea el layout inicial de la pestaña INICIO.
 */
function crearDashboardInicio() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏠 INICIO');
  if (!hoja) return;

  hoja.setColumnWidths(1, 8, 160);
  hoja.setRowHeight(1, 50);

  // Llamar actualizarDashboard para poblar con datos iniciales
  actualizarDashboard();
}
