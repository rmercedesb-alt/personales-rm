// ============================================================
// Pagos.gs — Registro de pagos y aplicación a CxC
// ============================================================

/**
 * Registra un pago de inquilino.
 * Llamado desde el sidebar HTML.
 */
function registrarPago(datos) {
  try {
    verificarRol('ASISTENTE');

    // Validaciones
    if (!datos.id_cxc) throw new Error('ID de CxC es requerido.');
    if (!datos.monto || parseFloat(datos.monto) <= 0) throw new Error('El monto debe ser mayor a cero.');
    if (!datos.forma_pago) throw new Error('La forma de pago es requerida.');
    if (!datos.fecha_pago) throw new Error('La fecha de pago es requerida.');

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
    var datosCxC = hojaCxC.getDataRange().getValues();

    // Buscar el CxC
    var filaCxC = -1;
    var cxcData = null;
    for (var i = 1; i < datosCxC.length; i++) {
      if (datosCxC[i][0] === datos.id_cxc) {
        filaCxC = i + 1;
        cxcData = datosCxC[i];
        break;
      }
    }
    if (!cxcData) throw new Error('CxC no encontrado: ' + datos.id_cxc);
    if (cxcData[18] === 'PAGADO') throw new Error('Este cargo ya está PAGADO.');
    if (cxcData[18] === 'ANULADO') throw new Error('Este cargo está ANULADO y no puede recibir pagos.');

    // Obtener o crear sesión de caja del día
    var sesion = obtenerOCrearSesionCaja();

    // Aplicar el pago: primero a mora, luego a principal
    var moraAcumulada = parseFloat(cxcData[13]) || 0;
    var montoPagadoAnterior = parseFloat(cxcData[15]) || 0;
    var montoTotal = parseFloat(cxcData[14]) || 0;
    var montoPago = parseFloat(datos.monto);

    var aplicadoAMora = Math.min(montoPago, Math.max(0, moraAcumulada - montoPagadoAnterior));
    var aplicadoAPrincipal = montoPago - aplicadoAMora;

    var nuevoMontoPagado = montoPagadoAnterior + montoPago;
    var nuevoSaldo = montoTotal - nuevoMontoPagado;
    nuevoSaldo = Math.round(nuevoSaldo * 100) / 100;

    // Determinar nuevo estado del CxC
    var nuevoEstado;
    var fechaPagoTotal = '';
    if (nuevoSaldo <= 0) {
      nuevoEstado = 'PAGADO';
      fechaPagoTotal = new Date(datos.fecha_pago);
      nuevoSaldo = 0;
    } else if (nuevoMontoPagado > 0) {
      nuevoEstado = cxcData[18] === 'EN_MORA' ? 'EN_MORA' : 'PARCIAL';
    } else {
      nuevoEstado = cxcData[18];
    }

    // Actualizar CxC
    hojaCxC.getRange(filaCxC, 16).setValue(nuevoMontoPagado); // MONTO_PAGADO
    hojaCxC.getRange(filaCxC, 17).setValue(nuevoSaldo);       // SALDO_PENDIENTE
    hojaCxC.getRange(filaCxC, 19).setValue(nuevoEstado);      // ESTADO
    if (fechaPagoTotal) hojaCxC.getRange(filaCxC, 20).setValue(fechaPagoTotal); // FECHA_PAGO_TOTAL

    // Insertar en CXC_PAGOS
    var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
    var idPago = generarIdUnico('PAG', '💳 CXC_PAGOS');
    var emailUsuario = obtenerEmailUsuario();
    var ahora = new Date();

    hojaPagos.appendRow([
      idPago,
      datos.id_cxc,
      cxcData[1],   // ID_CONTRATO
      cxcData[2],   // ID_INQUILINO
      cxcData[3],   // NOMBRE_INQUILINO
      cxcData[4],   // ID_INMUEBLE
      cxcData[5],   // NOMBRE_INMUEBLE
      cxcData[6],   // ID_PROPIETARIO
      cxcData[7],   // NOMBRE_PROPIETARIO
      new Date(datos.fecha_pago),
      montoPago,
      aplicadoAMora,
      aplicadoAPrincipal,
      datos.forma_pago,
      datos.referencia_pago || '',
      datos.banco_origen || '',
      emailUsuario,
      datos.observaciones || '',
      'COBRADO',
      sesion.id,
      '',           // ID_LIQUIDACION — vacío hasta liquidar
      '',           // FECHA_EN_ADMINISTRACION
      '',           // FECHA_LIQUIDADO
      '',           // RECIBO_PDF_URL — se llena al generar PDF
      ahora
    ]);

    // Insertar en LOG_CUSTODIA
    insertarLogCustodia({
      id_pago: idPago,
      id_inquilino: cxcData[2],
      nombre_inquilino: cxcData[3],
      id_inmueble: cxcData[4],
      id_propietario: cxcData[6],
      estado_anterior: '',
      estado_nuevo: 'COBRADO',
      evento: 'REGISTRO_PAGO',
      id_sesion_caja: sesion.id,
      monto_pago: montoPago,
      usuario: emailUsuario,
      observaciones: ''
    });

    // Actualizar totales de la sesión
    actualizarTotalesSesion(sesion.id, datos.forma_pago, montoPago);

    // Generar recibo PDF
    var urlRecibo = '';
    try {
      urlRecibo = generarReciboPDF(idPago);
    } catch (ePdf) {
      Logger.log('Advertencia: no se pudo generar PDF del recibo: ' + ePdf.toString());
    }

    // Generar link WhatsApp de confirmación
    try {
      generarLinkWhatsApp(datos.id_cxc, 'PAGO_CONFIRMADO', idPago);
    } catch (eWa) {
      Logger.log('Advertencia: no se pudo generar link WA: ' + eWa.toString());
    }

    // Actualizar dashboard
    actualizarDashboard();

    return {
      exito: true,
      id_pago: idPago,
      saldo_pendiente: nuevoSaldo,
      estado_cxc: nuevoEstado,
      recibo_url: urlRecibo,
      mensaje: 'Pago ' + idPago + ' registrado. Saldo pendiente: ' + formatearMoneda(nuevoSaldo)
    };

  } catch (e) {
    logError('registrarPago', e);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Inserta un registro en LOG_CUSTODIA.
 */
function insertarLogCustodia(datos) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📊 LOG_CUSTODIA');
  var id = generarIdUnico('LOG', '📊 LOG_CUSTODIA');

  hoja.appendRow([
    id,
    new Date(),
    datos.id_pago || '',
    datos.id_inquilino || '',
    datos.nombre_inquilino || '',
    datos.id_inmueble || '',
    datos.id_propietario || '',
    datos.estado_anterior || '',
    datos.estado_nuevo || '',
    datos.evento || '',
    datos.id_sesion_caja || '',
    datos.id_liquidacion || '',
    datos.monto_pago || 0,
    datos.usuario || obtenerEmailUsuario(),
    datos.observaciones || ''
  ]);
}

/**
 * Obtiene los CxC pendientes de un inquilino para el sidebar.
 */
function obtenerCxCInquilino(idInquilino) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('💰 CXC_CARGOS');
  var datos = hoja.getDataRange().getValues();
  var lista = [];

  for (var i = 1; i < datos.length; i++) {
    if (datos[i][2] !== idInquilino) continue;
    var estado = datos[i][18];
    if (estado === 'PAGADO' || estado === 'ANULADO') continue;

    lista.push({
      id: datos[i][0],
      tipo: datos[i][8],
      periodo_inicio: formatearFecha(datos[i][9]),
      periodo_fin: formatearFecha(datos[i][10]),
      fecha_vencimiento: formatearFecha(datos[i][11]),
      monto_principal: datos[i][12],
      mora_acumulada: datos[i][13],
      monto_total: datos[i][14],
      monto_pagado: datos[i][15],
      saldo_pendiente: datos[i][16],
      dias_mora: datos[i][17],
      estado: estado,
      inmueble: datos[i][5]
    });
  }
  return lista;
}

/**
 * Devuelve el estado de cuenta completo de un inquilino.
 */
function estadoCuentaInquilino(idInquilino) {
  var cxcs = obtenerCxCInquilino(idInquilino);
  var totalDeuda = cxcs.reduce(function(s, c) { return s + (parseFloat(c.saldo_pendiente) || 0); }, 0);
  var totalMora = cxcs.reduce(function(s, c) { return s + (parseFloat(c.mora_acumulada) || 0); }, 0);

  return {
    cxcs: cxcs,
    total_deuda: totalDeuda,
    total_mora: totalMora,
    cantidad_cargos: cxcs.length
  };
}
