// ============================================================
// Migracion.gs — Carga inicial de CxC y pagos históricos
// ============================================================

/**
 * Lee la pestaña CARGA_CXC, procesa cada fila y crea registros en CXC_CARGOS.
 * Las filas con error quedan en rojo para revisión manual.
 */
function cargarCxCPendientes() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hojaCarga = ss.getSheetByName('CARGA_CXC');
  if (!hojaCarga) {
    mostrarError('La pestaña CARGA_CXC no existe. Use el menú Sistema → Verificar Integridad.');
    return;
  }

  var datos = hojaCarga.getDataRange().getValues();
  if (datos.length <= 1) {
    mostrarError('La pestaña CARGA_CXC está vacía. Pegue los datos en las filas desde la 2.');
    return;
  }

  var procesados = 0;
  var errores = 0;
  var hoy = new Date();

  for (var i = 1; i < datos.length; i++) {
    var fila = datos[i];
    if (!fila[0] || fila[0] === '') continue; // Saltar filas vacías

    try {
      var idContrato  = fila[0].toString().trim();
      var tipoCargo   = fila[1].toString().trim().toUpperCase() || 'ALQUILER';
      var periodoIni  = fila[2] ? new Date(fila[2]) : null;
      var periodoFin  = fila[3] ? new Date(fila[3]) : null;
      var fechaVenc   = fila[4] ? new Date(fila[4]) : null;
      var montoPrinc  = parseFloat(fila[5]) || 0;
      var montoPagado = parseFloat(fila[6]) || 0;
      var observ      = fila[7] ? fila[7].toString() : 'MIGRACION';

      // Validaciones
      var contrato = obtenerContrato(idContrato);
      if (!contrato) throw new Error('Contrato no encontrado: ' + idContrato);
      if (contrato['ESTADO'] !== 'VIGENTE') throw new Error('Contrato no vigente: ' + idContrato);
      if (!fechaVenc) throw new Error('Fecha de vencimiento requerida');
      if (montoPrinc <= 0) throw new Error('Monto principal debe ser > 0');

      // Calcular días de mora
      var diasGracia = parseInt(contrato['DIAS_GRACIA']) || 5;
      var diasMora = Math.max(0, Math.floor((hoy - fechaVenc) / 86400000) - diasGracia);

      // Calcular mora acumulada
      var moraAcumulada = 0;
      if (diasMora > 0) {
        var tipomora = contrato['TIPO_MORA'];
        if (tipomora === 'PORCENTAJE') {
          var pct = parseFloat(contrato['PCT_MORA_MENSUAL']) || parseFloat(obtenerConfig('PCT_MORA_DEFAULT')) || 0.03;
          moraAcumulada = montoPrinc * pct * (diasMora / 30);
        } else {
          moraAcumulada = parseFloat(contrato['MONTO_MORA_FIJO']) || 0;
        }
        moraAcumulada = Math.round(moraAcumulada * 100) / 100;
      }

      var montoTotal    = montoPrinc + moraAcumulada;
      var saldoPendiente = Math.max(0, montoTotal - montoPagado);

      // Determinar estado
      var estadoCxC;
      if (saldoPendiente <= 0) {
        estadoCxC = 'PAGADO';
      } else if (diasMora > 0) {
        estadoCxC = montoPagado > 0 ? 'EN_MORA' : 'EN_MORA';
      } else if (montoPagado > 0 && saldoPendiente > 0) {
        estadoCxC = 'PARCIAL';
      } else {
        estadoCxC = 'PENDIENTE';
      }

      // Crear CxC
      var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
      var idCxC = generarIdUnico('CXC', '💰 CXC_CARGOS');

      hojaCxC.appendRow([
        idCxC,
        idContrato,
        contrato['ID_INQUILINO'],
        contrato['NOMBRE_INQUILINO'],
        contrato['ID_INMUEBLE'],
        contrato['NOMBRE_INMUEBLE'],
        contrato['ID_PROPIETARIO'],
        contrato['NOMBRE_PROPIETARIO'],
        tipoCargo,
        periodoIni || '',
        periodoFin || '',
        fechaVenc,
        montoPrinc,
        moraAcumulada,
        montoTotal,
        montoPagado,
        saldoPendiente,
        diasMora,
        estadoCxC,
        estadoCxC === 'PAGADO' ? hoy : '',
        new Date(),
        'MIGRACION: ' + (observ || '')
      ]);

      // Marcar fila como procesada (verde)
      hojaCarga.getRange(i + 1, 1, 1, datos[0].length).setBackground('#E8F5E9');
      procesados++;

    } catch (eRow) {
      // Marcar fila en rojo con el error
      hojaCarga.getRange(i + 1, 1, 1, datos[0].length).setBackground('#FFEBEE');
      hojaCarga.getRange(i + 1, datos[0].length + 1).setValue('ERROR: ' + eRow.message);
      Logger.log('Error fila ' + (i + 1) + ': ' + eRow.message);
      errores++;
    }
  }

  actualizarDashboard();

  mostrarExito(
    '✅ Migración de CxC completada.\n' +
    'Procesados: ' + procesados + '\n' +
    'Errores: ' + errores + (errores > 0 ? '\n\nRevise las filas marcadas en rojo en CARGA_CXC.' : '')
  );
}

/**
 * Lee la pestaña CARGA_PAGOS y procesa pagos históricos.
 */
function cargarPagosSinLiquidar() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hojaCarga = ss.getSheetByName('CARGA_PAGOS');
  if (!hojaCarga) {
    mostrarError('La pestaña CARGA_PAGOS no existe.');
    return;
  }

  var datos = hojaCarga.getDataRange().getValues();
  if (datos.length <= 1) {
    mostrarError('La pestaña CARGA_PAGOS está vacía.');
    return;
  }

  var procesados = 0;
  var errores = 0;

  for (var i = 1; i < datos.length; i++) {
    var fila = datos[i];
    if (!fila[0] || fila[0] === '') continue;

    try {
      var idCxC           = fila[0].toString().trim();
      var fechaPago       = fila[1] ? new Date(fila[1]) : new Date();
      var montoPagado     = parseFloat(fila[2]) || 0;
      var formaPago       = fila[3].toString().trim().toUpperCase() || 'EFECTIVO';
      var referencia      = fila[4] ? fila[4].toString() : '';
      var estadoCustodia  = fila[5].toString().trim().toUpperCase() || 'EN_ADMINISTRACION';
      var observ          = fila[6] ? fila[6].toString() : 'MIGRACION';

      // Validaciones
      if (!['COBRADO','EN_ADMINISTRACION'].includes(estadoCustodia)) {
        throw new Error('Estado de custodia inválido: ' + estadoCustodia + '. Use COBRADO o EN_ADMINISTRACION.');
      }
      if (montoPagado <= 0) throw new Error('Monto debe ser > 0');

      // Buscar el CxC
      var hojaCxC = ss.getSheetByName('💰 CXC_CARGOS');
      var datosCxC = hojaCxC.getDataRange().getValues();
      var cxcFila = -1;
      var cxcData = null;
      for (var k = 1; k < datosCxC.length; k++) {
        if (datosCxC[k][0] === idCxC) { cxcFila = k + 1; cxcData = datosCxC[k]; break; }
      }
      if (!cxcData) throw new Error('CxC no encontrado: ' + idCxC);

      // Calcular aplicación del pago
      var moraActual = parseFloat(cxcData[13]) || 0;
      var montoPagadoAnterior = parseFloat(cxcData[15]) || 0;
      var aplicadoAMora = Math.min(montoPagado, Math.max(0, moraActual - montoPagadoAnterior));
      var aplicadoAPrincipal = montoPagado - aplicadoAMora;

      var nuevoMontoPagado = montoPagadoAnterior + montoPagado;
      var montoTotal = parseFloat(cxcData[14]) || 0;
      var nuevoSaldo = Math.max(0, montoTotal - nuevoMontoPagado);

      var nuevoEstado = nuevoSaldo <= 0 ? 'PAGADO' :
        (cxcData[18] === 'EN_MORA' ? 'EN_MORA' : (nuevoMontoPagado > 0 ? 'PARCIAL' : cxcData[18]));

      // Actualizar CxC
      hojaCxC.getRange(cxcFila, 16).setValue(nuevoMontoPagado);
      hojaCxC.getRange(cxcFila, 17).setValue(nuevoSaldo);
      hojaCxC.getRange(cxcFila, 19).setValue(nuevoEstado);
      if (nuevoEstado === 'PAGADO') hojaCxC.getRange(cxcFila, 20).setValue(fechaPago);

      // Insertar en CXC_PAGOS
      var hojaPagos = ss.getSheetByName('💳 CXC_PAGOS');
      var idPago = generarIdUnico('PAG', '💳 CXC_PAGOS');
      var ahora = new Date();

      hojaPagos.appendRow([
        idPago,
        idCxC,
        cxcData[1],
        cxcData[2],
        cxcData[3],
        cxcData[4],
        cxcData[5],
        cxcData[6],
        cxcData[7],
        fechaPago,
        montoPagado,
        aplicadoAMora,
        aplicadoAPrincipal,
        formaPago,
        referencia,
        '',
        'MIGRACION',
        'MIGRACION: ' + observ,
        estadoCustodia,
        'MIGRACION',   // ID_SESION_CAJA especial
        '',            // ID_LIQUIDACION
        estadoCustodia === 'EN_ADMINISTRACION' ? fechaPago : '',
        '',
        '',
        ahora
      ]);

      // LOG_CUSTODIA: REGISTRO_PAGO
      insertarLogCustodia({
        id_pago: idPago,
        id_inquilino: cxcData[2],
        nombre_inquilino: cxcData[3],
        id_inmueble: cxcData[4],
        id_propietario: cxcData[6],
        estado_anterior: '',
        estado_nuevo: estadoCustodia,
        evento: 'REGISTRO_PAGO',
        id_sesion_caja: 'MIGRACION',
        monto_pago: montoPagado,
        usuario: 'MIGRACION',
        observaciones: observ
      });

      // Si ya está EN_ADMINISTRACION, agregar log adicional
      if (estadoCustodia === 'EN_ADMINISTRACION') {
        insertarLogCustodia({
          id_pago: idPago,
          id_inquilino: cxcData[2],
          nombre_inquilino: cxcData[3],
          id_inmueble: cxcData[4],
          id_propietario: cxcData[6],
          estado_anterior: 'COBRADO',
          estado_nuevo: 'EN_ADMINISTRACION',
          evento: 'CUADRE_CAJA',
          id_sesion_caja: 'MIGRACION',
          monto_pago: montoPagado,
          usuario: 'MIGRACION',
          observaciones: 'Migración histórica'
        });
      }

      // Marcar fila como procesada
      hojaCarga.getRange(i + 1, 1, 1, datos[0].length).setBackground('#E8F5E9');
      procesados++;

    } catch (eRow) {
      hojaCarga.getRange(i + 1, 1, 1, datos[0].length).setBackground('#FFEBEE');
      hojaCarga.getRange(i + 1, datos[0].length + 1).setValue('ERROR: ' + eRow.message);
      Logger.log('Error fila ' + (i + 1) + ': ' + eRow.message);
      errores++;
    }
  }

  actualizarDashboard();

  mostrarExito(
    '✅ Migración de pagos completada.\n' +
    'Procesados: ' + procesados + '\n' +
    'Errores: ' + errores + (errores > 0 ? '\n\nRevise las filas marcadas en rojo en CARGA_PAGOS.' : '')
  );
}
