using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ADEnterprise.Helpers;

/// <summary>
/// Token de envío único (idempotencia) para formularios de transacción.
///
/// Flujo:
///   GET  → Generar()  crea un GUID, lo guarda en TempData y lo devuelve al view.
///   POST → Validar()  lee (y consume) el valor de TempData y lo compara con el
///          campo oculto enviado. Un segundo envío con el mismo GUID fallará porque
///          TempData ya fue consumido en el primer intento.
///
/// Esto previene:
///   • Doble clic por impaciencia del usuario.
///   • Uso del botón "Atrás" del navegador para re-enviar el formulario.
///   • Envíos duplicados por red lenta con reintentos del navegador.
/// </summary>
public static class FormTokenHelper
{
    /// <summary>
    /// Genera un token nuevo, lo guarda en TempData bajo la clave indicada
    /// y lo devuelve para renderizarlo como &lt;input type="hidden"&gt;.
    /// </summary>
    public static string Generar(ITempDataDictionary td, string clave)
    {
        var token = Guid.NewGuid().ToString("N");
        td[clave] = token;
        return token;
    }

    /// <summary>
    /// Valida el token recibido en el POST.
    /// Leer TempData consume el valor — un segundo intento falla automáticamente.
    /// Retorna true si el token es válido y el POST debe procesarse.
    /// </summary>
    public static bool Validar(ITempDataDictionary td, string clave, string? tokenEnviado)
    {
        var esperado = td[clave]?.ToString();   // lectura destruye el valor en TempData
        return !string.IsNullOrEmpty(tokenEnviado)
            && !string.IsNullOrEmpty(esperado)
            && tokenEnviado == esperado;
    }
}
