using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DashVisitas.Services;

// Excluye UNA acción puntual de un [RequiereModulo] puesto a nivel de CLASE — para
// endpoints de infraestructura que viven dentro del controller de un módulo pero deben
// seguir disponibles sin importar si ESE módulo está activo. Caso real: ApiSupervisionController
// trae GET /apisuperv/branding, que el Menú llama para TODAS las tarjetas (reemplaza
// "IEGT" por el apodo configurado) — si Supervisión Dinámica estuviera apagada, sin esta
// excepción se rompería el branding de los OTROS 8 módulos también.
[AttributeUsage(AttributeTargets.Method)]
public class SinGateDeLicenciaAttribute : Attribute { }

// Gate de licencia por módulo — pedido explícito del usuario (2026-09-30): cada uno de
// los 9 módulos debe poder apagarse por licencia de forma independiente. Se aplica a
// nivel de CLASE en los controllers que son 100% de un solo módulo (ej. TorreController,
// ApiHistoricosController), o a nivel de MÉTODO en controllers compartidos que mezclan
// endpoints de infraestructura (siempre disponibles) con endpoints de un módulo
// específico (ej. ApiController.Data()/DataFecha(), que son del módulo "visitas").
//
// Ocultar la tarjeta en el Menú (ver /api/licencia + Menu/Index.cshtml) es solo
// cosmético — este filtro es el que de verdad bloquea el acceso si alguien entra
// directo por la URL del módulo apagado.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequiereModuloAttribute(string modulo) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor cad &&
            cad.MethodInfo.GetCustomAttributes(typeof(SinGateDeLicenciaAttribute), false).Length > 0)
        {
            await next();
            return;
        }
        var licencia = context.HttpContext.RequestServices.GetRequiredService<LicenseService>();
        if (!licencia.ModuloActivo(modulo))
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/html; charset=utf-8",
                Content = LicenseService.PaginaModuloNoDisponibleHtml(modulo)
            };
            return;
        }
        await next();
    }
}
