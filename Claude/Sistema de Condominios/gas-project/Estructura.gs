// ============================================================
// Estructura.gs — Creación del Google Sheet completo
// ============================================================

/**
 * Función principal: crea todas las pestañas con su estructura completa.
 * Llamar UNA SOLA VEZ al inicializar el sistema.
 */
function crearEstructuraCompleta() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();

  mostrarToast('Creando estructura del sistema...', 'Inicialización', 30);

  // Definición de todas las pestañas en orden
  var pestanas = [
    { nombre: '🏠 INICIO',              color: '#1B4F8C', tipo: 'dashboard' },
    { nombre: '👤 PROPIETARIOS',        color: '#607D8B', tipo: 'datos' },
    { nombre: '🏢 INMUEBLES',           color: '#607D8B', tipo: 'datos' },
    { nombre: '👥 INQUILINOS',          color: '#607D8B', tipo: 'datos' },
    { nombre: '📋 CONTRATOS',           color: '#2E7D32', tipo: 'datos' },
    { nombre: '➕ CARGOS_ADICIONALES',  color: '#2E7D32', tipo: 'datos' },
    { nombre: '💰 CXC_CARGOS',         color: '#E65100', tipo: 'operativo' },
    { nombre: '💳 CXC_PAGOS',          color: '#E65100', tipo: 'operativo' },
    { nombre: '🏧 SESIONES_CAJA',      color: '#4A148C', tipo: 'operativo' },
    { nombre: '🔄 LIQUIDACIONES',       color: '#4A148C', tipo: 'operativo' },
    { nombre: '📢 NOTIFICACIONES',      color: '#006064', tipo: 'operativo' },
    { nombre: '📊 LOG_CUSTODIA',        color: '#B71C1C', tipo: 'auditoria' },
    { nombre: '📈 REPORTE',             color: '#F57F17', tipo: 'reporte' },
    { nombre: '⚙ CONFIG',              color: '#455A64', tipo: 'sistema' },
    { nombre: '🔒 ROLES',              color: '#455A64', tipo: 'sistema' },
    { nombre: 'CARGA_CXC',             color: '#78909C', tipo: 'carga' },
    { nombre: 'CARGA_PAGOS',           color: '#78909C', tipo: 'carga' }
  ];

  // Crear o reordenar pestañas
  var hojaActual = ss.getSheets()[0];
  hojaActual.setName('_TEMP_');

  for (var i = 0; i < pestanas.length; i++) {
    var p = pestanas[i];
    var hoja = ss.getSheetByName(p.nombre);
    if (!hoja) hoja = ss.insertSheet(p.nombre, i);
    hoja.setTabColor(p.color);
    ss.setActiveSheet(hoja);
    ss.moveActiveSheet(i + 1);
  }

  // Eliminar hoja temporal
  var temp = ss.getSheetByName('_TEMP_');
  if (temp) ss.deleteSheet(temp);

  // Crear esquemas de cada pestaña
  crearEsquemaPropietarios();
  crearEsquemaInmuebles();
  crearEsquemaInquilinos();
  crearEsquemaContratos();
  crearEsquemaCargosAdicionales();
  crearEsquemaCxCCargos();
  crearEsquemaCxCPagos();
  crearEsquemaSesionesCaja();
  crearEsquemaLiquidaciones();
  crearEsquemaNotificaciones();
  crearEsquemaLogCustodia();
  crearEsquemaConfig();
  crearEsquemaRoles();
  crearEsquemaCargaCxC();
  crearEsquemaCargaPagos();
  crearDashboardInicio();

  // Protecciones
  aplicarProtecciones();

  mostrarExito('✅ Estructura del sistema creada correctamente.\n\nSiguiente paso: registrar los datos maestros (Propietarios, Inmuebles, Inquilinos, Contratos).');
}

// ──────────────────────────────────────────────
// Función auxiliar para encabezado estándar
// ──────────────────────────────────────────────
function crearEncabezados(hoja, encabezados) {
  if (encabezados.length === 0) return;
  var rango = hoja.getRange(1, 1, 1, encabezados.length);
  rango.setValues([encabezados]);
  rango.setBackground('#1B4F8C');
  rango.setFontColor('#FFFFFF');
  rango.setFontWeight('bold');
  rango.setFontSize(10);
  hoja.setFrozenRows(1);
  hoja.setColumnWidths(1, encabezados.length, 140);
}

// ──────────────────────────────────────────────
// 3.1 PROPIETARIOS
// ──────────────────────────────────────────────
function crearEsquemaPropietarios() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('👤 PROPIETARIOS');
  hoja.clearContents();

  var enc = ['ID_PROPIETARIO','NOMBRE_COMPLETO','CEDULA_RNC','TELEFONO','EMAIL',
    'BANCO','TIPO_CUENTA','NUMERO_CUENTA','ESQUEMA_COMISION',
    'PCT_COMISION','MONTO_FIJO_COMISION','ACTIVO','FECHA_REGISTRO'];
  crearEncabezados(hoja, enc);

  // Dropdowns
  var bancos = ['BHD León','Banreservas','Popular','Scotiabank','Santa Cruz','Otro'];
  hoja.getRange('F2:F1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(bancos, true).build());
  hoja.getRange('G2:G1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['AHORROS','CORRIENTE'], true).build());
  hoja.getRange('I2:I1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['PORCENTAJE','FIJO','MIXTO'], true).build());
  hoja.getRange('L2:L1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['SI','NO'], true).build());

  // Formatos
  hoja.getRange('J2:J1000').setNumberFormat('0.00%');
  hoja.getRange('K2:K1000').setNumberFormat('#,##0.00');
  hoja.getRange('M2:M1000').setNumberFormat('DD/MM/YYYY');

  hoja.setColumnWidth(1, 110);
  hoja.setColumnWidth(2, 200);
}

// ──────────────────────────────────────────────
// 3.2 INMUEBLES
// ──────────────────────────────────────────────
function crearEsquemaInmuebles() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏢 INMUEBLES');
  hoja.clearContents();

  var enc = ['ID_INMUEBLE','ID_PROPIETARIO','NOMBRE_PROPIETARIO','TIPO','NOMBRE_REFERENCIA',
    'CALLE_NUMERO','APT_LOCAL_HAB','NOMBRE_EDIFICIO','URBANIZACION_SECTOR','MUNICIPIO',
    'PROVINCIA','REFERENCIA_UBICACION','LATITUD','LONGITUD','AREA_M2',
    'HABITACIONES','BANOS','PARQUEO','CANON_BASE','DEPOSITO_REQUERIDO',
    'ESTADO','FECHA_REGISTRO','MAPA_URL'];
  crearEncabezados(hoja, enc);

  // Dropdown propietario
  hoja.getRange('B2:B1000').setDataValidation(
    SpreadsheetApp.newDataValidation()
      .requireValueInRange(ss.getSheetByName('👤 PROPIETARIOS').getRange('A2:A1000'), true)
      .build());

  // VLOOKUP propietario
  for (var i = 2; i <= 1000; i++) {
    hoja.getRange('C' + i).setFormula('=IF(B' + i + '<>"",IFERROR(VLOOKUP(B' + i + ',\'👤 PROPIETARIOS\'!A:B,2,0),""),"")');
  }

  // Dropdowns
  var tipos = ['APARTAMENTO','CASA','LOCAL','OFICINA','PENSION'];
  hoja.getRange('D2:D1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(tipos, true).build());
  var municipios = ['Santo Domingo','Santiago','San Pedro de Macorís','La Romana','Puerto Plata','Otro'];
  hoja.getRange('J2:J1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(municipios, true).build());
  hoja.getRange('R2:R1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['SI','NO'], true).build());
  var estados = ['DISPONIBLE','OCUPADO','MANTENIMIENTO','INACTIVO'];
  hoja.getRange('U2:U1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(estados, true).build());

  // Formatos numéricos
  hoja.getRange('M2:N1000').setNumberFormat('0.00000000');
  hoja.getRange('O2:O1000').setNumberFormat('#,##0.00');
  hoja.getRange('S2:T1000').setNumberFormat('#,##0.00');
  hoja.getRange('V2:V1000').setNumberFormat('DD/MM/YYYY');

  // Fórmula MAPA_URL
  for (var j = 2; j <= 1000; j++) {
    hoja.getRange('W' + j).setFormula('=IF(M' + j + '<>"","https://maps.google.com/?q="&M' + j + '&","&N' + j + ',"")');
  }

  hoja.setColumnWidth(1, 110);
  hoja.setColumnWidth(5, 200);
}

// ──────────────────────────────────────────────
// 3.3 INQUILINOS
// ──────────────────────────────────────────────
function crearEsquemaInquilinos() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('👥 INQUILINOS');
  hoja.clearContents();

  var enc = ['ID_INQUILINO','NOMBRE_COMPLETO','CEDULA','TELEFONO_WHATSAPP','EMAIL',
    'EMPLEADOR','TELEFONO_EMPLEADOR','REFERENCIA_NOMBRE','REFERENCIA_TELEFONO',
    'ACTIVO','FECHA_REGISTRO','WA_LINK'];
  crearEncabezados(hoja, enc);

  hoja.getRange('J2:J1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['SI','NO'], true).build());
  hoja.getRange('K2:K1000').setNumberFormat('DD/MM/YYYY');

  // Fórmula WA_LINK
  for (var i = 2; i <= 1000; i++) {
    hoja.getRange('L' + i).setFormula('=IF(D' + i + '<>"","https://wa.me/"&SUBSTITUTE(D' + i + ',"+",""),"")');
  }

  hoja.setColumnWidth(2, 200);
  hoja.setColumnWidth(12, 250);
}

// ──────────────────────────────────────────────
// 3.4 CONTRATOS
// ──────────────────────────────────────────────
function crearEsquemaContratos() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📋 CONTRATOS');
  hoja.clearContents();

  var enc = ['ID_CONTRATO','ID_INQUILINO','NOMBRE_INQUILINO','ID_INMUEBLE','NOMBRE_INMUEBLE',
    'ID_PROPIETARIO','NOMBRE_PROPIETARIO','FECHA_INICIO','FECHA_VENCIMIENTO','PERIODICIDAD',
    'DIA_COBRO_1','DIA_COBRO_2','DIAS_GRACIA','CANON_MENSUAL','DEPOSITO_RECIBIDO',
    'TIPO_MORA','PCT_MORA_MENSUAL','MONTO_MORA_FIJO','ESQUEMA_COMISION',
    'PCT_COMISION','MONTO_FIJO_COMISION','ESTADO','FECHA_REGISTRO'];
  crearEncabezados(hoja, enc);

  hoja.getRange('B2:B1000').setDataValidation(
    SpreadsheetApp.newDataValidation()
      .requireValueInRange(ss.getSheetByName('👥 INQUILINOS').getRange('A2:A1000'), true)
      .build());
  hoja.getRange('D2:D1000').setDataValidation(
    SpreadsheetApp.newDataValidation()
      .requireValueInRange(ss.getSheetByName('🏢 INMUEBLES').getRange('A2:A1000'), true)
      .build());

  for (var i = 2; i <= 1000; i++) {
    hoja.getRange('C' + i).setFormula('=IF(B' + i + '<>"",IFERROR(VLOOKUP(B' + i + ',\'👥 INQUILINOS\'!A:B,2,0),""),"")');
    hoja.getRange('E' + i).setFormula('=IF(D' + i + '<>"",IFERROR(VLOOKUP(D' + i + ',\'🏢 INMUEBLES\'!A:E,5,0),""),"")');
    hoja.getRange('F' + i).setFormula('=IF(D' + i + '<>"",IFERROR(VLOOKUP(D' + i + ',\'🏢 INMUEBLES\'!A:B,2,0),""),"")');
    hoja.getRange('G' + i).setFormula('=IF(F' + i + '<>"",IFERROR(VLOOKUP(F' + i + ',\'👤 PROPIETARIOS\'!A:B,2,0),""),"")');
  }

  hoja.getRange('J2:J1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['MENSUAL','QUINCENAL'], true).build());
  hoja.getRange('P2:P1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['PORCENTAJE','MONTO_FIJO'], true).build());
  hoja.getRange('S2:S1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['HEREDA','PORCENTAJE','FIJO'], true).build());
  hoja.getRange('V2:V1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['VIGENTE','VENCIDO','RESCINDIDO','EN_DESAHUCIO'], true).build());

  hoja.getRange('H2:I1000').setNumberFormat('DD/MM/YYYY');
  hoja.getRange('N2:O1000').setNumberFormat('#,##0.00');
  hoja.getRange('Q2:Q1000').setNumberFormat('0.00%');
  hoja.getRange('R2:R1000').setNumberFormat('#,##0.00');
  hoja.getRange('T2:T1000').setNumberFormat('0.00%');
  hoja.getRange('U2:U1000').setNumberFormat('#,##0.00');
  hoja.getRange('W2:W1000').setNumberFormat('DD/MM/YYYY');
}

// ──────────────────────────────────────────────
// 3.5 CARGOS_ADICIONALES
// ──────────────────────────────────────────────
function crearEsquemaCargosAdicionales() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('➕ CARGOS_ADICIONALES');
  hoja.clearContents();

  var enc = ['ID_CARGO_ADICIONAL','ID_CONTRATO','NOMBRE_CARGO','MONTO','PERIODICIDAD',
    'INCLUIR_EN_CXC_ALQUILER','ACTIVO','FECHA_DESDE','FECHA_HASTA'];
  crearEncabezados(hoja, enc);

  hoja.getRange('B2:B1000').setDataValidation(
    SpreadsheetApp.newDataValidation()
      .requireValueInRange(ss.getSheetByName('📋 CONTRATOS').getRange('A2:A1000'), true)
      .build());
  hoja.getRange('E2:E1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['MENSUAL','QUINCENAL'], true).build());
  hoja.getRange('F2:F1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['SI','NO'], true).build());
  hoja.getRange('G2:G1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['SI','NO'], true).build());
  hoja.getRange('D2:D1000').setNumberFormat('#,##0.00');
  hoja.getRange('H2:I1000').setNumberFormat('DD/MM/YYYY');
}

// ──────────────────────────────────────────────
// 3.6 CXC_CARGOS
// ──────────────────────────────────────────────
function crearEsquemaCxCCargos() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💰 CXC_CARGOS');
  hoja.clearContents();

  var enc = ['ID_CXC','ID_CONTRATO','ID_INQUILINO','NOMBRE_INQUILINO','ID_INMUEBLE',
    'NOMBRE_INMUEBLE','ID_PROPIETARIO','NOMBRE_PROPIETARIO','TIPO_CARGO',
    'PERIODO_INICIO','PERIODO_FIN','FECHA_VENCIMIENTO','MONTO_PRINCIPAL',
    'MORA_ACUMULADA','MONTO_TOTAL','MONTO_PAGADO','SALDO_PENDIENTE',
    'DIAS_MORA','ESTADO','FECHA_PAGO_TOTAL','FECHA_GENERACION','NOTAS'];
  crearEncabezados(hoja, enc);

  var tiposCargo = ['ALQUILER','MANTENIMIENTO','PARQUEO','AGUA','MORA','OTRO'];
  hoja.getRange('I2:I5000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(tiposCargo, true).build());
  var estadosCxC = ['PENDIENTE','PARCIAL','EN_MORA','PAGADO','ANULADO'];
  hoja.getRange('S2:S5000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(estadosCxC, true).build());

  hoja.getRange('J2:L5000').setNumberFormat('DD/MM/YYYY');
  hoja.getRange('M2:Q5000').setNumberFormat('#,##0.00');
  hoja.getRange('T2:U5000').setNumberFormat('DD/MM/YYYY HH:mm:ss');

  // Formato condicional por estado
  var reglas = [];
  var estados = [
    { valor: 'PENDIENTE',  fondo: '#FFFFFF', texto: '#000000' },
    { valor: 'PARCIAL',    fondo: '#FFF9C4', texto: '#000000' },
    { valor: 'EN_MORA',    fondo: '#FFE0B2', texto: '#E65100' },
    { valor: 'PAGADO',     fondo: '#E8F5E9', texto: '#2E7D32' },
    { valor: 'ANULADO',    fondo: '#ECEFF1', texto: '#9E9E9E' }
  ];
  var rangoEstado = hoja.getRange('A2:V5000');
  estados.forEach(function(e) {
    var regla = SpreadsheetApp.newConditionalFormatRule()
      .whenFormulaSatisfied('=$S2="' + e.valor + '"')
      .setBackground(e.fondo)
      .setFontColor(e.texto)
      .setRanges([rangoEstado])
      .build();
    reglas.push(regla);
  });
  hoja.setConditionalFormatRules(reglas);
}

// ──────────────────────────────────────────────
// 3.7 CXC_PAGOS
// ──────────────────────────────────────────────
function crearEsquemaCxCPagos() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💳 CXC_PAGOS');
  hoja.clearContents();

  var enc = ['ID_PAGO','ID_CXC','ID_CONTRATO','ID_INQUILINO','NOMBRE_INQUILINO',
    'ID_INMUEBLE','NOMBRE_INMUEBLE','ID_PROPIETARIO','NOMBRE_PROPIETARIO',
    'FECHA_PAGO','MONTO_PAGADO','APLICADO_A_MORA','APLICADO_A_PRINCIPAL',
    'FORMA_PAGO','REFERENCIA_PAGO','BANCO_ORIGEN','REGISTRADO_POR','OBSERVACIONES',
    'ESTADO_CUSTODIA','ID_SESION_CAJA','ID_LIQUIDACION',
    'FECHA_EN_ADMINISTRACION','FECHA_LIQUIDADO','RECIBO_PDF_URL','FECHA_REGISTRO'];
  crearEncabezados(hoja, enc);

  hoja.getRange('N2:N5000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['EFECTIVO','TRANSFERENCIA','TARJETA','CHEQUE'], true).build());
  hoja.getRange('S2:S5000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['COBRADO','EN_ADMINISTRACION','LIQUIDADO'], true).build());

  hoja.getRange('J2:J5000').setNumberFormat('DD/MM/YYYY');
  hoja.getRange('K2:M5000').setNumberFormat('#,##0.00');
  hoja.getRange('V2:W5000').setNumberFormat('DD/MM/YYYY HH:mm:ss');
  hoja.getRange('Y2:Y5000').setNumberFormat('DD/MM/YYYY HH:mm:ss');
}

// ──────────────────────────────────────────────
// 3.8 SESIONES_CAJA
// ──────────────────────────────────────────────
function crearEsquemaSesionesCaja() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏧 SESIONES_CAJA');
  hoja.clearContents();

  var enc = ['ID_SESION','FECHA_SESION','USUARIO_CAJERO','USUARIO_CIERRE',
    'TOTAL_EFECTIVO','TOTAL_TRANSFERENCIA','TOTAL_TARJETA','TOTAL_CHEQUE',
    'TOTAL_SESION','CANTIDAD_TRANSACCIONES','ESTADO','OBSERVACIONES',
    'DOC_CIERRE_PDF_URL','FECHA_APERTURA','FECHA_CIERRE'];
  crearEncabezados(hoja, enc);

  hoja.getRange('K2:K1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['ABIERTA','PENDIENTE_AJUSTE','APROBADA'], true).build());
  hoja.getRange('B2:B1000').setNumberFormat('DD/MM/YYYY');
  hoja.getRange('E2:I1000').setNumberFormat('#,##0.00');
  hoja.getRange('N2:O1000').setNumberFormat('DD/MM/YYYY HH:mm:ss');
}

// ──────────────────────────────────────────────
// 3.9 LIQUIDACIONES
// ──────────────────────────────────────────────
function crearEsquemaLiquidaciones() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🔄 LIQUIDACIONES');
  hoja.clearContents();

  var enc = ['ID_LIQUIDACION','ID_PROPIETARIO','NOMBRE_PROPIETARIO','PERIODO_DESDE',
    'PERIODO_HASTA','FILTRO_ADICIONAL','TOTAL_COBRADO','TOTAL_MORA_COBRADA',
    'COMISION_ADMINISTRADOR','MONTO_NETO_PROPIETARIO','FECHA_LIQUIDACION',
    'FECHA_PAGO_PROPIETARIO','REFERENCIA_PAGO','ESTADO','DOC_LIQUIDACION_PDF_URL'];
  crearEncabezados(hoja, enc);

  hoja.getRange('N2:N1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['PENDIENTE','PAGADO'], true).build());
  hoja.getRange('D2:E1000').setNumberFormat('DD/MM/YYYY');
  hoja.getRange('G2:J1000').setNumberFormat('#,##0.00');
  hoja.getRange('K2:L1000').setNumberFormat('DD/MM/YYYY');
}

// ──────────────────────────────────────────────
// NOTIFICACIONES
// ──────────────────────────────────────────────
function crearEsquemaNotificaciones() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📢 NOTIFICACIONES');
  hoja.clearContents();

  var enc = ['ID_NOTIFICACION','TIMESTAMP','TIPO','ID_INQUILINO','NOMBRE_INQUILINO',
    'ID_INMUEBLE','NOMBRE_INMUEBLE','TELEFONO_WHATSAPP','WA_LINK','MENSAJE',
    'ESTADO','FECHA_ENVIO'];
  crearEncabezados(hoja, enc);

  hoja.getRange('K2:K5000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['PENDIENTE','ENVIADO','NO_APLICA'], true).build());
  hoja.getRange('B2:B5000').setNumberFormat('DD/MM/YYYY HH:mm:ss');
  hoja.getRange('L2:L5000').setNumberFormat('DD/MM/YYYY');
  hoja.setColumnWidth(9, 300);
  hoja.setColumnWidth(10, 400);
}

// ──────────────────────────────────────────────
// 3.10 LOG_CUSTODIA
// ──────────────────────────────────────────────
function crearEsquemaLogCustodia() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📊 LOG_CUSTODIA');
  hoja.clearContents();

  var enc = ['ID_LOG','TIMESTAMP','ID_PAGO','ID_INQUILINO','NOMBRE_INQUILINO',
    'ID_INMUEBLE','ID_PROPIETARIO','ESTADO_ANTERIOR','ESTADO_NUEVO','EVENTO',
    'ID_SESION_CAJA','ID_LIQUIDACION','MONTO_PAGO','USUARIO','OBSERVACIONES'];
  crearEncabezados(hoja, enc);

  hoja.getRange('B2:B10000').setNumberFormat('DD/MM/YYYY HH:mm:ss');
  hoja.getRange('M2:M10000').setNumberFormat('#,##0.00');
  hoja.setColumnWidth(2, 160);
}

// ──────────────────────────────────────────────
// 3.11 CONFIG
// ──────────────────────────────────────────────
function crearEsquemaConfig() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('⚙ CONFIG');
  hoja.clearContents();

  // Encabezado
  hoja.getRange('A1:C1').setValues([['CLAVE','VALOR','DESCRIPCION']]);
  hoja.getRange('A1:C1').setBackground('#1B4F8C').setFontColor('#FFFFFF').setFontWeight('bold');
  hoja.setFrozenRows(1);

  // Valores predeterminados
  var config = [
    ['DIAS_GRACIA_DEFAULT',       5,                              'Días de gracia globales'],
    ['PCT_MORA_DEFAULT',          0.03,                           'Tasa de mora mensual predeterminada (3%)'],
    ['DIAS_AVISO_PREVIO',         5,                              'Días antes del vencimiento para enviar aviso'],
    ['EMPRESA_NOMBRE',            'Mi Empresa Administradora',    'Para los encabezados de PDF'],
    ['EMPRESA_RNC',               '',                             'RNC de la empresa administradora'],
    ['EMPRESA_TELEFONO',          '',                             'Teléfono para los documentos'],
    ['EMPRESA_EMAIL',             '',                             'Email de contacto'],
    ['GOOGLE_DRIVE_FOLDER_ID',    '',                             'Carpeta en Drive donde se guardan los PDFs'],
    ['TRIGGER_CRON_HORA',         0,                              'Hora del día para ejecutar el cron (0 = medianoche)'],
    ['ALERTA_DIAS_ADMIN',         5,                              'Alertar si saldos EN_ADMINISTRACION más de X días'],
    ['VERSION_SISTEMA',           'Beta 1.0',                     'Versión actual'],
    ['TEMPLATE_RECIBO_ID',        '',                             'ID del Google Doc plantilla de recibo'],
    ['TEMPLATE_CIERRE_CAJA_ID',   '',                             'ID del Google Doc plantilla cierre de caja'],
    ['TEMPLATE_LIQUIDACION_ID',   '',                             'ID del Google Doc plantilla liquidación'],
    ['ULTIMO_ERROR_CRON',         '',                             'Último error del cron (auto-actualizado)']
  ];
  hoja.getRange(2, 1, config.length, 3).setValues(config);
  hoja.getRange('B4').setNumberFormat('0.00%');
  hoja.setColumnWidth(1, 200);
  hoja.setColumnWidth(2, 300);
  hoja.setColumnWidth(3, 300);
}

// ──────────────────────────────────────────────
// ROLES
// ──────────────────────────────────────────────
function crearEsquemaRoles() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🔒 ROLES');
  hoja.clearContents();

  var enc = ['EMAIL','NOMBRE','ROL','ACTIVO'];
  crearEncabezados(hoja, enc);

  hoja.getRange('C2:C100').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['ADMINISTRADOR','ASISTENTE','LECTURA'], true).build());
  hoja.getRange('D2:D100').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['SI','NO'], true).build());

  // Insertar el usuario actual como Administrador
  var email = Session.getActiveUser().getEmail();
  hoja.getRange('A2:D2').setValues([[email, 'Administrador Principal', 'ADMINISTRADOR', 'SI']]);

  // Ocultar la hoja
  hoja.hideSheet();
  hoja.setColumnWidth(1, 250);
}

// ──────────────────────────────────────────────
// CARGA_CXC y CARGA_PAGOS (pestañas de migración)
// ──────────────────────────────────────────────
function crearEsquemaCargaCxC() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('CARGA_CXC');
  hoja.clearContents();

  var enc = ['ID_CONTRATO','TIPO_CARGO','PERIODO_INICIO','PERIODO_FIN',
    'FECHA_VENCIMIENTO','MONTO_PRINCIPAL','MONTO_PAGADO','OBSERVACIONES_MIGRACION'];
  crearEncabezados(hoja, enc);
  hoja.getRange('C2:E1000').setNumberFormat('DD/MM/YYYY');
  hoja.getRange('F2:G1000').setNumberFormat('#,##0.00');

  // Instrucciones en fila 2 de color azul claro
  var instruccion = hoja.getRange('A1:H1');
  // Ya tiene encabezados; agregar nota
  hoja.getRange('A1').setNote('PESTAÑA DE MIGRACIÓN: Pegue aquí los datos de CxC pendientes. Luego vaya a Menú → Carga Inicial → Cargar CxC Pendientes.');
}

function crearEsquemaCargaPagos() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('CARGA_PAGOS');
  hoja.clearContents();

  var enc = ['ID_CXC','FECHA_PAGO','MONTO_PAGADO','FORMA_PAGO',
    'REFERENCIA_PAGO','ESTADO_CUSTODIA_INICIAL','OBSERVACIONES_MIGRACION'];
  crearEncabezados(hoja, enc);
  hoja.getRange('B2:B1000').setNumberFormat('DD/MM/YYYY');
  hoja.getRange('C2:C1000').setNumberFormat('#,##0.00');
  hoja.getRange('D2:D1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['EFECTIVO','TRANSFERENCIA','TARJETA','CHEQUE'], true).build());
  hoja.getRange('F2:F1000').setDataValidation(
    SpreadsheetApp.newDataValidation().requireValueInList(['COBRADO','EN_ADMINISTRACION'], true).build());
  hoja.getRange('A1').setNote('PESTAÑA DE MIGRACIÓN: Pegue aquí los pagos históricos. Luego vaya a Menú → Carga Inicial → Cargar Pagos Históricos sin Liquidar.');
}

// ──────────────────────────────────────────────
// Protecciones de rango
// ──────────────────────────────────────────────
function aplicarProtecciones() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var email = Session.getActiveUser().getEmail();

  // LOG_CUSTODIA — protección total (ni el admin puede editar)
  var hojaLog = ss.getSheetByName('📊 LOG_CUSTODIA');
  var protLog = hojaLog.protect().setDescription('Log inmutable — solo Apps Script');
  protLog.removeEditors(protLog.getEditors());
  protLog.addEditor(email); // El propietario puede gestionar pero el script es quien escribe

  // CONFIG y ROLES — solo admin
  var hojaConfig = ss.getSheetByName('⚙ CONFIG');
  var protConfig = hojaConfig.protect().setDescription('Solo Administrador');

  var hojaRoles = ss.getSheetByName('🔒 ROLES');
  var protRoles = hojaRoles.protect().setDescription('Solo Administrador');

  // CXC_CARGOS — proteger columnas de cálculo (solo lectura)
  var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
  var protCxC = hojaCxC.protect().setDescription('CxC — solo Apps Script');
  protCxC.removeEditors(protCxC.getEditors());
  protCxC.addEditor(email);
}
