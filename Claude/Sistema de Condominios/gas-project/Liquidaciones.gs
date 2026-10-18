// ============================================================
// Liquidaciones.gs — Proceso de liquidación a propietarios
// ============================================================

/**
 * Procesa una liquidación a propietario.
 * Mueve pagos de EN_ADMINISTRACION → LIQUIDADO.
 */
function procesarLiquidacion(idPropietario, fechaDesde, fechaHasta, filtroAdicional) {
  try {
    verificarRol('ADMINISTRADOR');

    if (!idPropietario) throw new Error('El ID de propietario es requerido.');
    if (!fechaDesde || !fechaHasta) throw new Error('El rango de fechas es requerido.');

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
    var datosPagos = hojaPagos.getDataRange().getValues();

    var desdeFecha = new Date(fechaDesde);
    var hastaFecha = new Date(fechaHasta);
    hastaFecha.setHours(23, 59, 59);

    // Filtrar pagos EN_ADMINISTRACION del propietario en el rango
    var pagosALiquidar = [];
    for (var i = 1; i < datosPagos.length; i++) {
      var p = datosPagos[i];
      if (!p[0]) continue;
      if (p[7] !== idPropietario) continue;
      if (p[18] !== 'EN_ADMINISTRACION') continue;

      var fechaAdmin = new Date(p[21]);
      if (isNaN(fechaAdmin.getTime())) continue;
      if (fechaAdmin < desdeFecha || fechaAdmin > hastaFecha) continue;

      // Aplicar filtro adicional (ej: inmueble específico)
      if (filtroAdicional && p[5] !== filtroAdicional) continue;

      pagosALiquidar.push({ fila: i + 1, datos: p });
    }

    if (pagosALiquidar.length === 0) {
      throw new Error('No hay pagos EN_ADMINISTRACION del propietario ' + idPropietario + ' en el período indicado.');
    }

    // Calcular totales
    var totalCobrado = 0;
    var totalMora = 0;
    pagosALiquidar.forEach(function(pg) {
      totalCobrado += parseFloat(pg.datos[10]) || 0;
      totalMora += parseFloat(pg.datos[11]) || 0;
    });
    totalCobrado = Math.round(totalCobrado * 100) / 100;
    totalMora = Math.round(totalMora * 100) / 100;

    // Calcular comisión del propietario
    var propietario = obtenerPropietario(idPropietario);
    if (!propietario) throw new Error('Propietario no encontrado: ' + idPropietario);

    var comision = calcularComisionPropietario(propietario, totalCobrado);
    var montoNeto = Math.round((totalCobrado - comision) * 100) / 100;

    // Crear registro en LIQUIDACIONES
    var hojaLiq = ss.getSheetByName('🔄 LIQUIDACIONES');
    var idLiq = generarIdUnico('LIQ', '🔄 LIQUIDACIONES');
    var ahora = new Date();

    hojaLiq.appendRow([
      idLiq,
      idPropietario,
      propietario['NOMBRE_COMPLETO'],
      desdeFecha,
      hastaFecha,
      filtroAdicional || '',
      totalCobrado,
      totalMora,
      comision,
      montoNeto,
      ahora,
      '',           // FECHA_PAGO_PROPIETARIO — se completa al pagar
      '',           // REFERENCIA_PAGO
      'PENDIENTE',
      ''            // DOC_LIQUIDACION_PDF_URL
    ]);

    var emailAdmin = obtenerEmailUsuario();

    // Actualizar cada pago: LIQUIDADO
    pagosALiquidar.forEach(function(pg) {
      hojaPagos.getRange(pg.fila, 19).setValue('LIQUIDADO');          // ESTADO_CUSTODIA
      hojaPagos.getRange(pg.fila, 21).setValue(idLiq);               // ID_LIQUIDACION
      hojaPagos.getRange(pg.fila, 23).setValue(ahora);               // FECHA_LIQUIDADO

      insertarLogCustodia({
        id_pago: pg.datos[0],
        id_inquilino: pg.datos[3],
        nombre_inquilino: pg.datos[4],
        id_inmueble: pg.datos[5],
        id_propietario: pg.datos[7],
        estado_anterior: 'EN_ADMINISTRACION',
        estado_nuevo: 'LIQUIDADO',
        evento: 'LIQUIDACION',
        id_liquidacion: idLiq,
        monto_pago: pg.datos[10],
        usuario: emailAdmin
      });
    });

    // Generar PDF de liquidación
    var urlPDF = '';
    try {
      urlPDF = generarLiquidacionPDF(idLiq);
      // Actualizar URL en la fila de liquidación
      var dataLiq = hojaLiq.getDataRange().getValues();
      for (var j = 1; j < dataLiq.length; j++) {
        if (dataLiq[j][0] === idLiq) {
          hojaLiq.getRange(j + 1, 15).setValue(urlPDF);
          break;
        }
      }
    } catch (ePdf) {
      Logger.log('Advertencia: no se pudo generar PDF de liquidación: ' + ePdf.toString());
    }

    actualizarDashboard();

    return {
      exito: true,
      id_liquidacion: idLiq,
      total_cobrado: totalCobrado,
      comision: comision,
      monto_neto: montoNeto,
      pagos_incluidos: pagosALiquidar.length,
      url_pdf: urlPDF,
      mensaje: 'Liquidación ' + idLiq + ' procesada. Neto al propietario: ' + formatearMoneda(montoNeto)
    };

  } catch (e) {
    logError('procesarLiquidacion', e);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Obtiene los datos de un propietario por ID.
 */
function obtenerPropietario(idPropietario) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('👤 PROPIETARIOS');
  var datos = hoja.getDataRange().getValues();
  var enc = datos[0];
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === idPropietario) {
      var obj = {};
      enc.forEach(function(col, j) { obj[col] = datos[i][j]; });
      return obj;
    }
  }
  return null;
}

/**
 * Calcula la comisión del administrador según el esquema del propietario.
 */
function calcularComisionPropietario(propietario, totalCobrado) {
  var esquema = propietario['ESQUEMA_COMISION'];
  if (esquema === 'PORCENTAJE') {
    var pct = parseFloat(propietario['PCT_COMISION']) || 0;
    return Math.round(totalCobrado * pct * 100) / 100;
  } else if (esquema === 'FIJO') {
    return parseFloat(propietario['MONTO_FIJO_COMISION']) || 0;
  } else if (esquema === 'MIXTO') {
    var p = parseFloat(propietario['PCT_COMISION']) || 0;
    var f = parseFloat(propietario['MONTO_FIJO_COMISION']) || 0;
    return Math.round((totalCobrado * p + f) * 100) / 100;
  }
  return 0;
}

/**
 * Marca una liquidación como PAGADA al propietario.
 */
function marcarLiquidacionPagada(idLiquidacion, fechaPago, referenciaPago) {
  try {
    verificarRol('ADMINISTRADOR');

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('🔄 LIQUIDACIONES');
    var datos = hoja.getDataRange().getValues();

    for (var i = 1; i < datos.length; i++) {
      if (datos[i][0] === idLiquidacion) {
        hoja.getRange(i + 1, 12).setValue(new Date(fechaPago)); // FECHA_PAGO_PROPIETARIO
        hoja.getRange(i + 1, 13).setValue(referenciaPago || ''); // REFERENCIA_PAGO
        hoja.getRange(i + 1, 14).setValue('PAGADO');             // ESTADO
        return { exito: true, mensaje: 'Liquidación marcada como PAGADA.' };
      }
    }
    throw new Error('Liquidación no encontrada: ' + idLiquidacion);
  } catch (e) {
    logError('marcarLiquidacionPagada', e);
    return { exito: false, mensaje: e.message };
  }
}
