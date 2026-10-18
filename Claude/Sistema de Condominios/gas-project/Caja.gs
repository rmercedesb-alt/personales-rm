// ============================================================
// Caja.gs — Sesiones de caja y cuadre
// ============================================================

/**
 * Obtiene la sesión de caja activa del día, o la crea si no existe.
 */
function obtenerOCrearSesionCaja() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏧 SESIONES_CAJA');
  var datos = hoja.getDataRange().getValues();
  var hoy = new Date().toDateString();
  var email = obtenerEmailUsuario();

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] && datos[i][10] === 'ABIERTA' && new Date(datos[i][1]).toDateString() === hoy) {
      return {
        id: datos[i][0],
        total: parseFloat(datos[i][8]) || 0,
        cantidad: parseInt(datos[i][9]) || 0
      };
    }
  }

  // Crear nueva sesión
  var idSesion = generarIdUnico('CAJA', '🏧 SESIONES_CAJA');
  hoja.appendRow([
    idSesion,
    new Date(),
    email,
    '',     // USUARIO_CIERRE
    0,      // TOTAL_EFECTIVO
    0,      // TOTAL_TRANSFERENCIA
    0,      // TOTAL_TARJETA
    0,      // TOTAL_CHEQUE
    0,      // TOTAL_SESION
    0,      // CANTIDAD_TRANSACCIONES
    'ABIERTA',
    '',     // OBSERVACIONES
    '',     // DOC_CIERRE_PDF_URL
    new Date(), // FECHA_APERTURA
    ''      // FECHA_CIERRE
  ]);

  return { id: idSesion, total: 0, cantidad: 0 };
}

/**
 * Devuelve la sesión activa del día sin crearla.
 */
function obtenerSesionCajaActiva() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏧 SESIONES_CAJA');
  var datos = hoja.getDataRange().getValues();
  var hoy = new Date().toDateString();

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] && datos[i][10] === 'ABIERTA' && new Date(datos[i][1]).toDateString() === hoy) {
      return {
        id: datos[i][0],
        fecha: formatearFecha(datos[i][1]),
        cajero: datos[i][2],
        total_efectivo: datos[i][4],
        total_transferencia: datos[i][5],
        total_tarjeta: datos[i][6],
        total_cheque: datos[i][7],
        total: datos[i][8],
        cantidad: datos[i][9],
        estado: datos[i][10]
      };
    }
  }
  return null;
}

/**
 * Actualiza los totales de una sesión de caja al registrar un pago.
 */
function actualizarTotalesSesion(idSesion, formaPago, monto) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏧 SESIONES_CAJA');
  var datos = hoja.getDataRange().getValues();

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === idSesion) {
      var colMapa = { 'EFECTIVO': 5, 'TRANSFERENCIA': 6, 'TARJETA': 7, 'CHEQUE': 8 };
      var col = colMapa[formaPago] || 6;
      var valorActual = parseFloat(datos[i][col - 1]) || 0;
      hoja.getRange(i + 1, col).setValue(valorActual + monto);

      // Recalcular total
      var efectivo    = parseFloat(datos[i][4]) || 0;
      var transferenc = parseFloat(datos[i][5]) || 0;
      var tarjeta     = parseFloat(datos[i][6]) || 0;
      var cheque      = parseFloat(datos[i][7]) || 0;
      if (col === 5) efectivo    += monto;
      if (col === 6) transferenc += monto;
      if (col === 7) tarjeta     += monto;
      if (col === 8) cheque      += monto;

      hoja.getRange(i + 1, 9).setValue(efectivo + transferenc + tarjeta + cheque);
      hoja.getRange(i + 1, 10).setValue((parseInt(datos[i][9]) || 0) + 1);
      return;
    }
  }
}

/**
 * Aprueba el cuadre de una sesión de caja.
 * Mueve todos los pagos de COBRADO → EN_ADMINISTRACION.
 */
function aprobarCuadreCaja(idSesion) {
  try {
    verificarRol('ADMINISTRADOR');

    var ss = SpreadsheetApp.getActiveSpreadsheet();

    // Validar sesión
    var hojaSesiones = ss.getSheetByName('🏧 SESIONES_CAJA');
    var datosSesiones = hojaSesiones.getDataRange().getValues();
    var filaSesion = -1;
    var sesionData = null;

    for (var i = 1; i < datosSesiones.length; i++) {
      if (datosSesiones[i][0] === idSesion) {
        filaSesion = i + 1;
        sesionData = datosSesiones[i];
        break;
      }
    }
    if (!sesionData) throw new Error('Sesión no encontrada: ' + idSesion);
    if (sesionData[10] !== 'ABIERTA' && sesionData[10] !== 'PENDIENTE_AJUSTE') {
      throw new Error('La sesión ' + idSesion + ' no está en estado válido para aprobar. Estado actual: ' + sesionData[10]);
    }

    var emailAdmin = obtenerEmailUsuario();
    var ahora = new Date();

    // Leer todos los pagos COBRADO de esta sesión
    var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
    var datosPagos = hojaPagos.getDataRange().getValues();
    var pagosActualizados = 0;

    for (var j = 1; j < datosPagos.length; j++) {
      if (datosPagos[j][19] === idSesion && datosPagos[j][18] === 'COBRADO') {
        // Actualizar a EN_ADMINISTRACION
        hojaPagos.getRange(j + 1, 19).setValue('EN_ADMINISTRACION'); // ESTADO_CUSTODIA
        hojaPagos.getRange(j + 1, 22).setValue(ahora);               // FECHA_EN_ADMINISTRACION

        // LOG_CUSTODIA
        insertarLogCustodia({
          id_pago: datosPagos[j][0],
          id_inquilino: datosPagos[j][3],
          nombre_inquilino: datosPagos[j][4],
          id_inmueble: datosPagos[j][5],
          id_propietario: datosPagos[j][7],
          estado_anterior: 'COBRADO',
          estado_nuevo: 'EN_ADMINISTRACION',
          evento: 'CUADRE_CAJA',
          id_sesion_caja: idSesion,
          monto_pago: datosPagos[j][10],
          usuario: emailAdmin
        });
        pagosActualizados++;
      }
    }

    // Actualizar sesión
    hojaSesiones.getRange(filaSesion, 4).setValue(emailAdmin); // USUARIO_CIERRE
    hojaSesiones.getRange(filaSesion, 11).setValue('APROBADA'); // ESTADO
    hojaSesiones.getRange(filaSesion, 15).setValue(ahora);      // FECHA_CIERRE

    // Generar PDF de cierre
    var urlCierre = '';
    try {
      urlCierre = generarCierreCajaPDF(idSesion);
      hojaSesiones.getRange(filaSesion, 13).setValue(urlCierre); // DOC_CIERRE_PDF_URL
    } catch (ePdf) {
      Logger.log('Advertencia: no se pudo generar PDF de cierre: ' + ePdf.toString());
    }

    actualizarDashboard();

    mostrarExito('✅ Cuadre de caja aprobado.\n' + pagosActualizados + ' pagos movidos a EN_ADMINISTRACION.\nURL cierre: ' + (urlCierre || 'No generado'));
    return { exito: true, pagos_actualizados: pagosActualizados };

  } catch (e) {
    logError('aprobarCuadreCaja', e);
    mostrarError(e.message);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Devuelve los pagos de una sesión de caja para visualización.
 */
function obtenerPagosSesion(idSesion) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💳 CXC_PAGOS');
  var datos = hoja.getDataRange().getValues();
  var pagos = [];

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][19] === idSesion) {
      pagos.push({
        id: datos[i][0],
        inquilino: datos[i][4],
        inmueble: datos[i][6],
        fecha: formatearFecha(datos[i][9]),
        monto: datos[i][10],
        forma_pago: datos[i][13],
        referencia: datos[i][14],
        estado: datos[i][18]
      });
    }
  }
  return pagos;
}

/**
 * Reportes disponibles desde el menú.
 */
function generarReporteCobranza() {
  try { abrirSidebar('reporte_cobranza'); }
  catch (e) { mostrarError(e.message); }
}
function generarReporteMorosidad() {
  try { abrirSidebar('reporte_morosidad'); }
  catch (e) { mostrarError(e.message); }
}
function generarEstadoCuentaReporte() {
  try { abrirSidebar('estado_cuenta'); }
  catch (e) { mostrarError(e.message); }
}
function generarReporteCustodia() {
  try { abrirSidebar('reporte_custodia'); }
  catch (e) { mostrarError(e.message); }
}
