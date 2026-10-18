// ============================================================
// Utils.gs — Funciones de utilidad general del sistema
// ============================================================

/**
 * Genera un ID único auto-incremental con prefijo.
 * Ejemplo: generarIdUnico('PROP', 'PROPIETARIOS') → 'PROP-001'
 */
function generarIdUnico(prefijo, nombreHoja) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName(nombreHoja);
  if (!hoja) throw new Error('Hoja no encontrada: ' + nombreHoja);

  var ultimaFila = hoja.getLastRow();
  var numero = 1;

  if (ultimaFila > 1) {
    var ids = hoja.getRange(2, 1, ultimaFila - 1, 1).getValues();
    // Recorrer de abajo hacia arriba buscando el último ID real
    for (var i = ids.length - 1; i >= 0; i--) {
      var id = ids[i][0];
      if (id && typeof id === 'string' && id.indexOf(prefijo + '-') === 0) {
        var partes = id.split('-');
        var n = parseInt(partes[partes.length - 1], 10);
        if (!isNaN(n) && n >= numero) numero = n + 1;
        break;
      }
    }
  }

  var digitos = numero > 999 ? 4 : 3;
  return prefijo + '-' + String(numero).padStart(digitos, '0');
}

/**
 * Verifica que el usuario activo tiene el rol requerido.
 * Lanza un error si no tiene permiso.
 */
function verificarRol(rolRequerido) {
  var email = Session.getActiveUser().getEmail();

  // Si la pestaña ROLES no existe aún (sistema sin inicializar),
  // permitir al propietario del script operar libremente.
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('🔒 ROLES');
  if (!hoja) {
    // Permitir si es el dueño del archivo
    var propietario = ss.getOwner() ? ss.getOwner().getEmail() : '';
    if (!propietario || email === propietario) return true;
    throw new Error('El sistema no está inicializado. Ejecute primero "crearEstructuraCompleta()" desde el editor de Apps Script.');
  }

  var datos = hoja.getDataRange().getValues();
  for (var i = 1; i < datos.length; i++) {
    var fila = datos[i];
    if (fila[0].toString().toLowerCase() === email.toLowerCase() && fila[3] === 'SI') {
      var rol = fila[2];
      if (rolRequerido === 'ADMINISTRADOR' && rol === 'ADMINISTRADOR') return true;
      if (rolRequerido === 'ASISTENTE' && (rol === 'ADMINISTRADOR' || rol === 'ASISTENTE')) return true;
      if (rolRequerido === 'LECTURA') return true;
      throw new Error('No tienes permiso para realizar esta operación. Se requiere rol: ' + rolRequerido);
    }
  }
  // Si no está en ROLES, verificar si es el propietario del script
  var propietario = Session.getEffectiveUser().getEmail();
  if (email === propietario) return true;
  throw new Error('Usuario no registrado en el sistema: ' + email);
}

/**
 * Obtiene el email del usuario activo.
 */
function obtenerEmailUsuario() {
  return Session.getActiveUser().getEmail();
}

/**
 * Formatea un número como moneda dominicana.
 * Ejemplo: 1234.5 → 'RD$ 1,234.50'
 */
function formatearMoneda(monto) {
  if (monto === null || monto === undefined || monto === '') return 'RD$ 0.00';
  var n = parseFloat(monto) || 0;
  return 'RD$ ' + n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Formatea una fecha como DD/MM/YYYY.
 */
function formatearFecha(fecha) {
  if (!fecha) return '';
  var d = new Date(fecha);
  if (isNaN(d.getTime())) return '';
  var dia = String(d.getDate()).padStart(2, '0');
  var mes = String(d.getMonth() + 1).padStart(2, '0');
  var anio = d.getFullYear();
  return dia + '/' + mes + '/' + anio;
}

/**
 * Devuelve el valor de una clave en CONFIG.
 */
function obtenerConfig(clave) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var hoja = ss.getSheetByName('⚙ CONFIG');
  if (!hoja) return null;
  var datos = hoja.getDataRange().getValues();
  for (var i = 1; i < datos.length; i++) {
    if (datos[i][0] === clave) return datos[i][1];
  }
  return null;
}

/**
 * Registra un error en CONFIG (clave ULTIMO_ERROR_CRON).
 */
function logError(contexto, error) {
  try {
    Logger.log('[ERROR] ' + contexto + ': ' + error.toString());
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var hoja = ss.getSheetByName('⚙ CONFIG');
    if (!hoja) return;
    var datos = hoja.getDataRange().getValues();
    for (var i = 1; i < datos.length; i++) {
      if (datos[i][0] === 'ULTIMO_ERROR_CRON') {
        hoja.getRange(i + 1, 2).setValue(new Date().toISOString() + ' | ' + contexto + ': ' + error.toString());
        return;
      }
    }
  } catch (e) {
    Logger.log('Error al logear error: ' + e.toString());
  }
}

/**
 * Muestra un toast de notificación al usuario.
 */
function mostrarToast(mensaje, titulo, duracion) {
  SpreadsheetApp.getActiveSpreadsheet().toast(mensaje, titulo || 'Sistema', duracion || 5);
}

/**
 * Muestra un mensaje de error al usuario.
 */
function mostrarError(mensaje) {
  SpreadsheetApp.getUi().alert('❌ Error', mensaje, SpreadsheetApp.getUi().ButtonSet.OK);
}

/**
 * Muestra un mensaje de éxito al usuario.
 */
function mostrarExito(mensaje) {
  SpreadsheetApp.getUi().alert('✅ Éxito', mensaje, SpreadsheetApp.getUi().ButtonSet.OK);
}
