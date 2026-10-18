// ============================================================
// Maestros.gs — CRUD de datos maestros (Propietarios, Inmuebles, Inquilinos, Contratos, Cargos)
// ============================================================

/**
 * Abre el sidebar HTML principal con la vista indicada.
 */
function abrirSidebar(vista) {
  var html = HtmlService.createTemplateFromFile('Sidebar');
  html.vista = vista || 'menu';
  var output = html.evaluate()
    .setTitle('⚙ Admin Inmuebles')
    .setWidth(400);
  SpreadsheetApp.getUi().showSidebar(output);
}

// ──────────────────────────────────────────────
// PROPIETARIOS
// ──────────────────────────────────────────────

/**
 * Crea un nuevo propietario desde el sidebar.
 */
function crearPropietario(datos) {
  try {
    verificarRol('ADMINISTRADOR');
    if (!datos.nombre || !datos.cedula) throw new Error('Nombre y Cédula/RNC son obligatorios.');

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('👤 PROPIETARIOS');
    var id = generarIdUnico('PROP', '👤 PROPIETARIOS');
    var ahora = new Date();

    hoja.appendRow([
      id,
      datos.nombre,
      datos.cedula,
      datos.telefono || '',
      datos.email || '',
      datos.banco || '',
      datos.tipo_cuenta || '',
      datos.numero_cuenta || '',
      datos.esquema_comision || 'PORCENTAJE',
      parseFloat(datos.pct_comision || 0),
      parseFloat(datos.monto_fijo_comision || 0),
      datos.activo || 'SI',
      ahora
    ]);

    return { exito: true, id: id, mensaje: 'Propietario ' + id + ' creado correctamente.' };
  } catch (e) {
    logError('crearPropietario', e);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Devuelve la lista de propietarios activos para dropdowns.
 */
function listarPropietarios() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('👤 PROPIETARIOS');
  var datos = hoja.getDataRange().getValues();
  var lista = [];
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] && datos[i][11] === 'SI') {
      lista.push({ id: datos[i][0], nombre: datos[i][1] });
    }
  }
  return lista;
}

// ──────────────────────────────────────────────
// INMUEBLES
// ──────────────────────────────────────────────

/**
 * Crea un nuevo inmueble desde el sidebar.
 */
function crearInmueble(datos) {
  try {
    verificarRol('ADMINISTRADOR');
    if (!datos.id_propietario || !datos.tipo || !datos.nombre_referencia) {
      throw new Error('Propietario, Tipo y Nombre de Referencia son obligatorios.');
    }

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('🏢 INMUEBLES');
    var id = generarIdUnico('INM', '🏢 INMUEBLES');
    var ahora = new Date();
    var fila = hoja.getLastRow() + 1;

    hoja.appendRow([
      id,
      datos.id_propietario,
      '', // VLOOKUP auto
      datos.tipo,
      datos.nombre_referencia,
      datos.calle_numero || '',
      datos.apt_local_hab || '',
      datos.nombre_edificio || '',
      datos.urbanizacion_sector || '',
      datos.municipio || '',
      datos.provincia || '',
      datos.referencia_ubicacion || '',
      parseFloat(datos.latitud || 0) || '',
      parseFloat(datos.longitud || 0) || '',
      parseFloat(datos.area_m2 || 0) || '',
      parseInt(datos.habitaciones || 0) || '',
      parseInt(datos.banos || 0) || '',
      datos.parqueo || 'NO',
      parseFloat(datos.canon_base || 0),
      parseFloat(datos.deposito_requerido || 0),
      datos.estado || 'DISPONIBLE',
      ahora,
      '' // MAPA_URL por fórmula
    ]);

    return { exito: true, id: id, mensaje: 'Inmueble ' + id + ' creado correctamente.' };
  } catch (e) {
    logError('crearInmueble', e);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Devuelve la lista de inmuebles para dropdowns.
 */
function listarInmuebles(soloDisponibles) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏢 INMUEBLES');
  var datos = hoja.getDataRange().getValues();
  var lista = [];
  for (var i = 1; i < datos.length; i++) {
    if (!datos[i][0]) continue;
    if (soloDisponibles && datos[i][20] !== 'DISPONIBLE') continue;
    lista.push({ id: datos[i][0], nombre: datos[i][4], estado: datos[i][20], propietario: datos[i][1] });
  }
  return lista;
}

// ──────────────────────────────────────────────
// INQUILINOS
// ──────────────────────────────────────────────

/**
 * Crea un nuevo inquilino desde el sidebar.
 */
function crearInquilino(datos) {
  try {
    verificarRol('ASISTENTE');
    if (!datos.nombre || !datos.cedula) throw new Error('Nombre y Cédula son obligatorios.');

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('👥 INQUILINOS');
    var id = generarIdUnico('INQ', '👥 INQUILINOS');
    var ahora = new Date();

    hoja.appendRow([
      id,
      datos.nombre,
      datos.cedula,
      datos.telefono_whatsapp || '',
      datos.email || '',
      datos.empleador || '',
      datos.telefono_empleador || '',
      datos.referencia_nombre || '',
      datos.referencia_telefono || '',
      datos.activo || 'SI',
      ahora,
      '' // WA_LINK por fórmula
    ]);

    return { exito: true, id: id, mensaje: 'Inquilino ' + id + ' creado correctamente.' };
  } catch (e) {
    logError('crearInquilino', e);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Devuelve la lista de inquilinos activos para dropdowns.
 */
function listarInquilinos() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('👥 INQUILINOS');
  var datos = hoja.getDataRange().getValues();
  var lista = [];
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] && datos[i][9] === 'SI') {
      lista.push({ id: datos[i][0], nombre: datos[i][1], telefono: datos[i][3] });
    }
  }
  return lista;
}

// ──────────────────────────────────────────────
// CONTRATOS
// ──────────────────────────────────────────────

/**
 * Crea un nuevo contrato desde el sidebar.
 */
function crearContrato(datos) {
  try {
    verificarRol('ADMINISTRADOR');
    var requeridos = ['id_inquilino','id_inmueble','fecha_inicio','fecha_vencimiento','periodicidad','dia_cobro_1','canon_mensual'];
    requeridos.forEach(function(c) {
      if (!datos[c]) throw new Error('Campo requerido faltante: ' + c);
    });

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('📋 CONTRATOS');
    var id = generarIdUnico('CON', '📋 CONTRATOS');
    var ahora = new Date();

    hoja.appendRow([
      id,
      datos.id_inquilino,
      '', // VLOOKUP
      datos.id_inmueble,
      '', // VLOOKUP
      '', // VLOOKUP
      '', // VLOOKUP
      new Date(datos.fecha_inicio),
      new Date(datos.fecha_vencimiento),
      datos.periodicidad,
      parseInt(datos.dia_cobro_1),
      datos.periodicidad === 'QUINCENAL' ? parseInt(datos.dia_cobro_2 || 0) : '',
      parseInt(datos.dias_gracia || obtenerConfig('DIAS_GRACIA_DEFAULT') || 5),
      parseFloat(datos.canon_mensual),
      parseFloat(datos.deposito_recibido || 0),
      datos.tipo_mora || 'PORCENTAJE',
      parseFloat(datos.pct_mora || obtenerConfig('PCT_MORA_DEFAULT') || 0.03),
      parseFloat(datos.monto_mora_fijo || 0),
      datos.esquema_comision || 'HEREDA',
      parseFloat(datos.pct_comision || 0),
      parseFloat(datos.monto_fijo_comision || 0),
      datos.estado || 'VIGENTE',
      ahora
    ]);

    // Actualizar estado del inmueble a OCUPADO
    actualizarEstadoInmueble(datos.id_inmueble, 'OCUPADO');

    return { exito: true, id: id, mensaje: 'Contrato ' + id + ' creado correctamente.' };
  } catch (e) {
    logError('crearContrato', e);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Actualiza el estado de un inmueble.
 */
function actualizarEstadoInmueble(idInmueble, nuevoEstado) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🏢 INMUEBLES');
  var datos = hoja.getDataRange().getValues();
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === idInmueble) {
      hoja.getRange(i + 1, 21).setValue(nuevoEstado); // Col U = ESTADO
      return;
    }
  }
}

/**
 * Devuelve la lista de contratos vigentes para dropdowns.
 */
function listarContratos(soloVigentes) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📋 CONTRATOS');
  var datos = hoja.getDataRange().getValues();
  var lista = [];
  for (var i = 1; i < datos.length; i++) {
    if (!datos[i][0]) continue;
    if (soloVigentes && datos[i][21] !== 'VIGENTE') continue;
    lista.push({
      id: datos[i][0],
      inquilino: datos[i][2],
      inmueble: datos[i][4],
      canon: datos[i][13],
      estado: datos[i][21]
    });
  }
  return lista;
}

/**
 * Obtiene los datos de un contrato por su ID.
 */
function obtenerContrato(idContrato) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('📋 CONTRATOS');
  var datos = hoja.getDataRange().getValues();
  var enc = datos[0];
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === idContrato) {
      var obj = {};
      enc.forEach(function(col, j) { obj[col] = datos[i][j]; });
      return obj;
    }
  }
  return null;
}

// ──────────────────────────────────────────────
// CARGOS ADICIONALES
// ──────────────────────────────────────────────

/**
 * Crea un cargo adicional para un contrato.
 */
function crearCargoAdicional(datos) {
  try {
    verificarRol('ADMINISTRADOR');
    if (!datos.id_contrato || !datos.nombre_cargo || !datos.monto) {
      throw new Error('Contrato, Nombre del Cargo y Monto son obligatorios.');
    }

    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('➕ CARGOS_ADICIONALES');
    var id = generarIdUnico('CA', '➕ CARGOS_ADICIONALES');

    hoja.appendRow([
      id,
      datos.id_contrato,
      datos.nombre_cargo,
      parseFloat(datos.monto),
      datos.periodicidad || 'MENSUAL',
      datos.incluir_en_cxc || 'SI',
      datos.activo || 'SI',
      datos.fecha_desde ? new Date(datos.fecha_desde) : new Date(),
      datos.fecha_hasta ? new Date(datos.fecha_hasta) : ''
    ]);

    return { exito: true, id: id, mensaje: 'Cargo adicional ' + id + ' creado.' };
  } catch (e) {
    logError('crearCargoAdicional', e);
    return { exito: false, mensaje: e.message };
  }
}

/**
 * Devuelve los cargos adicionales activos de un contrato.
 */
function obtenerCargosAdicionalesContrato(idContrato) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('➕ CARGOS_ADICIONALES');
  var datos = hoja.getDataRange().getValues();
  var hoy = new Date();
  var lista = [];
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][1] === idContrato && datos[i][6] === 'SI') {
      var fechaHasta = datos[i][8];
      if (fechaHasta && new Date(fechaHasta) < hoy) continue;
      lista.push({
        id: datos[i][0],
        nombre: datos[i][2],
        monto: datos[i][3],
        periodicidad: datos[i][4],
        incluir_en_alquiler: datos[i][5]
      });
    }
  }
  return lista;
}
