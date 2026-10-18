// AD ALL DIGITAL ENTERPRISE — JavaScript principal

// Auto-cierre de alertas después de 5 segundos
document.addEventListener('DOMContentLoaded', function () {
    setTimeout(function () {
        document.querySelectorAll('.alert.fade.show').forEach(function (el) {
            var bsAlert = new bootstrap.Alert(el);
            bsAlert.close();
        });
    }, 5000);
});

// Confirmar antes de borrar / desactivar
function confirmarAccion(mensaje) {
    return confirm(mensaje || '¿Está seguro de que desea realizar esta acción?');
}

// Formatear número como moneda RD$
function formatearMoneda(valor) {
    return 'RD$ ' + parseFloat(valor || 0).toLocaleString('es-DO', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });
}

// Mostrar/ocultar campo de cheque según forma de pago
function toggleCampoReferencia() {
    var forma = document.getElementById('FormaPago');
    var campoRef = document.getElementById('campo-referencia');
    if (!forma || !campoRef) return;
    var necesita = ['Cheque', 'Transferencia'].includes(forma.value);
    campoRef.style.display = necesita ? 'block' : 'none';
    var input = campoRef.querySelector('input');
    if (input) input.required = necesita;
}

document.addEventListener('DOMContentLoaded', function () {
    var forma = document.getElementById('FormaPago');
    if (forma) {
        forma.addEventListener('change', toggleCampoReferencia);
        toggleCampoReferencia(); // ejecutar al cargar
    }
});
