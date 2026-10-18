// ============================================================
// Code.gs — Punto de entrada principal del sistema
// ============================================================

/**
 * Se ejecuta al abrir el Sheet. Crea el menú personalizado.
 */
function onOpen() {
  var ui = SpreadsheetApp.getUi();
  ui.createMenu('⚙ Admin Inmuebles')
    .addItem('─── DATOS MAESTROS ───', 'menuSeparador')
    .addItem('Nuevo Propietario', 'abrirSidebarPropietario')
    .addItem('Nuevo Inmueble', 'abrirSidebarInmueble')
    .addItem('Nuevo Inquilino', 'abrirSidebarInquilino')
    .addItem('Nuevo Contrato', 'abrirSidebarContrato')
    .addSeparator()
    .addItem('─── COBRANZA ───', 'menuSeparador')
    .addItem('Registrar Pago', 'abrirSidebarPago')
    .addItem('Ver Estado de Cuenta de Inquilino', 'verEstadoCuentaInquilino')
    .addSeparator()
    .addItem('─── CAJA ───', 'menuSeparador')
    .addItem('Ver Sesión de Caja Activa', 'verSesionCajaActiva')
    .addItem('Aprobar Cuadre de Caja (solo Admin)', 'menuAprobarCuadreCaja')
    .addSeparator()
    .addItem('─── LIQUIDACIONES ───', 'menuSeparador')
    .addItem('Procesar Liquidación a Propietario (solo Admin)', 'menuProcesarLiquidacion')
    .addSeparator()
    .addItem('─── CARGA INICIAL ───', 'menuSeparador')
    .addItem('Cargar CxC Pendientes (migración)', 'menuCargarCxCPendientes')
    .addItem('Cargar Pagos Históricos sin Liquidar (migración)', 'menuCargarPagosSinLiquidar')
    .addSeparator()
    .addItem('─── REPORTES ───', 'menuSeparador')
    .addItem('Reporte de Cobranza del Período', 'generarReporteCobranza')
    .addItem('Reporte de Morosidad', 'generarReporteMorosidad')
    .addItem('Estado de Cuenta por Inquilino', 'generarEstadoCuentaReporte')
    .addItem('Reporte de Custodia', 'generarReporteCustodia')
    .addSeparator()
    .addItem('─── SISTEMA ───', 'menuSeparador')
    .addItem('Instalar/Actualizar Triggers Automáticos', 'instalarTriggers')
    .addItem('Configurar Carpeta Drive y Plantillas PDF', 'crearPlantillasEnDrive')
    .addItem('Verificar Integridad del Sistema', 'verificarIntegridadMenu')
    .addItem('Acerca de esta Beta', 'acercaDe')
    .addSeparator()
    .addItem('🚀 INICIALIZAR SISTEMA (primera vez)', 'crearEstructuraCompleta')
    .addToUi();

  try {
    actualizarDashboard();
  } catch (e) {
    Logger.log('Error al actualizar dashboard en onOpen: ' + e.toString());
  }
}

// Función placeholder para separadores de menú (no hace nada)
function menuSeparador() {}

/**
 * Trigger onEdit — valida ediciones en tiempo real.
 */
function onEdit(e) {
  try {
    validarEdicion(e);
  } catch (err) {
    Logger.log('Error en onEdit: ' + err.toString());
  }
}

/**
 * Instala/reinstala todos los triggers automáticos del sistema.
 */
function instalarTriggers() {
  try {
    verificarRol('ADMINISTRADOR');

    // Eliminar triggers existentes
    var triggers = ScriptApp.getProjectTriggers();
    for (var i = 0; i < triggers.length; i++) {
      ScriptApp.deleteTrigger(triggers[i]);
    }

    // Trigger onOpen
    ScriptApp.newTrigger('onOpen')
      .forSpreadsheet(SpreadsheetApp.getActiveSpreadsheet())
      .onOpen()
      .create();

    // Trigger onEdit
    ScriptApp.newTrigger('onEdit')
      .forSpreadsheet(SpreadsheetApp.getActiveSpreadsheet())
      .onEdit()
      .create();

    // Cron diario — hora configurada en CONFIG
    var horaConfig = parseInt(obtenerConfig('TRIGGER_CRON_HORA') || '0', 10);
    ScriptApp.newTrigger('cronDiario')
      .timeBased()
      .everyDays(1)
      .atHour(horaConfig)
      .create();

    // Recordatorio semanal — lunes a las 8 AM
    ScriptApp.newTrigger('generarRecordatoriosSemana')
      .timeBased()
      .onWeekDay(ScriptApp.WeekDay.MONDAY)
      .atHour(8)
      .create();

    mostrarExito('✅ Triggers instalados correctamente:\n' +
      '• onOpen\n• onEdit\n• cronDiario (cada día a las ' + horaConfig + ':00)\n• Recordatorio semanal (lunes 8 AM)');

  } catch (e) {
    logError('instalarTriggers', e);
    mostrarError(e.message);
  }
}

/**
 * Valida ediciones en celdas clave (validarEdicion).
 */
function validarEdicion(e) {
  if (!e || !e.range) return;
  var hoja = e.range.getSheet();
  var nombreHoja = hoja.getName();
  var col = e.range.getColumn();
  var fila = e.range.getRow();
  if (fila <= 1) return; // No validar encabezados

  // Auto-completar fecha de registro
  var columnasConFecha = {
    '👤 PROPIETARIOS': 13,
    '🏢 INMUEBLES': 22,
    '👥 INQUILINOS': 11,
    '📋 CONTRATOS': 23
  };
  var colFecha = columnasConFecha[nombreHoja];
  if (colFecha && col === 2 && e.value) {
    hoja.getRange(fila, colFecha).setValue(new Date());
  }
}

/**
 * Muestra información sobre el sistema.
 */
function acercaDe() {
  var version = obtenerConfig('VERSION_SISTEMA') || 'Beta 1.0';
  SpreadsheetApp.getUi().alert(
    '📋 Sistema de Administración de Inmuebles',
    'Versión: ' + version + '\n' +
    'Plataforma: Google Sheets + Apps Script\n' +
    'País: República Dominicana\n' +
    'Moneda: Pesos Dominicanos (DOP)\n\n' +
    'Este sistema es una beta funcional para validar\nel modelo de datos antes del sistema web v3.0.',
    SpreadsheetApp.getUi().ButtonSet.OK
  );
}

/**
 * Abre la pantalla de verificación de integridad.
 */
function verificarIntegridadMenu() {
  var resultado = verificarIntegridad();
  var html = HtmlService.createHtmlOutput(resultado)
    .setTitle('Verificación de Integridad')
    .setWidth(600)
    .setHeight(500);
  SpreadsheetApp.getUi().showModalDialog(html, 'Verificación de Integridad del Sistema');
}

// Wrappers de menú para sidebars
function abrirSidebarPropietario() { abrirSidebar('propietario'); }
function abrirSidebarInmueble()    { abrirSidebar('inmueble'); }
function abrirSidebarInquilino()   { abrirSidebar('inquilino'); }
function abrirSidebarContrato()    { abrirSidebar('contrato'); }
function abrirSidebarPago()        { abrirSidebar('pago'); }

function menuAprobarCuadreCaja() {
  try {
    var sesion = obtenerSesionCajaActiva();
    if (!sesion) { mostrarError('No hay sesión de caja activa hoy.'); return; }
    var ui = SpreadsheetApp.getUi();
    var resp = ui.alert('Aprobar Cuadre de Caja',
      'Sesión: ' + sesion.id + '\nTotal: ' + formatearMoneda(sesion.total) + '\nTransacciones: ' + sesion.cantidad +
      '\n\n¿Confirmar aprobación del cuadre?', ui.ButtonSet.YES_NO);
    if (resp === ui.Button.YES) aprobarCuadreCaja(sesion.id);
  } catch (e) { logError('menuAprobarCuadreCaja', e); mostrarError(e.message); }
}

function menuProcesarLiquidacion() {
  try {
    verificarRol('ADMINISTRADOR');
    abrirSidebar('liquidacion');
  } catch (e) { logError('menuProcesarLiquidacion', e); mostrarError(e.message); }
}

function menuCargarCxCPendientes() {
  try {
    verificarRol('ADMINISTRADOR');
    cargarCxCPendientes();
  } catch (e) { logError('menuCargarCxCPendientes', e); mostrarError(e.message); }
}

function menuCargarPagosSinLiquidar() {
  try {
    verificarRol('ADMINISTRADOR');
    cargarPagosSinLiquidar();
  } catch (e) { logError('menuCargarPagosSinLiquidar', e); mostrarError(e.message); }
}

function verEstadoCuentaInquilino() {
  try {
    abrirSidebar('estado_cuenta');
  } catch (e) { logError('verEstadoCuentaInquilino', e); mostrarError(e.message); }
}

function verSesionCajaActiva() {
  try {
    abrirSidebar('sesion_caja');
  } catch (e) { logError('verSesionCajaActiva', e); mostrarError(e.message); }
}
