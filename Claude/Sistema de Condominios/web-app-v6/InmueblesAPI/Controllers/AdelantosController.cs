using System.Security.Claims;
using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/adelantos")]
[Authorize]
public class AdelantosController : ControllerBase
{
    private readonly InmueblesService _svc;
    private readonly PagoService _pagos;
    public AdelantosController(InmueblesService svc, PagoService pagos)
    {
        _svc = svc;
        _pagos = pagos;
    }

    static readonly string[] MimesPermitidos = { "application/pdf", "image/jpeg", "image/jpg", "image/png" };
    const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<Adelanto>>>> Get([FromQuery] string? idPropietario)
    {
        var list = await _svc.GetAdelantosAsync(idPropietario);
        return Ok(new ApiResponse<List<Adelanto>>(true, null, list));
    }

    [HttpPost]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Adelanto>>> Crear([FromBody] CrearAdelantoRequest req)
    {
        var adelanto = await _svc.CrearAdelantoAsync(req);
        return Ok(new ApiResponse<Adelanto>(true, "Adelanto registrado", adelanto));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Adelanto>>> Actualizar(string id, [FromBody] ActualizarAdelantoRequest req)
    {
        try
        {
            var adelanto = await _svc.ActualizarAdelantoAsync(id, req);
            return Ok(new ApiResponse<Adelanto>(true, "Adelanto actualizado", adelanto));
        }
        catch (InvalidOperationException ex) { return BadRequest(new ApiResponse<Adelanto>(false, ex.Message, null)); }
    }

    [HttpPost("{id}/anular")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<Adelanto>>> Anular(string id, [FromBody] AnularAdelantoRequest req)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        try
        {
            var adelanto = await _svc.AnularAdelantoAsync(id, req.MotivoAnulacion, usuario);
            return Ok(new ApiResponse<Adelanto>(true, "Adelanto anulado", adelanto));
        }
        catch (InvalidOperationException ex) { return BadRequest(new ApiResponse<Adelanto>(false, ex.Message, null)); }
    }

    // Pago/devolución del adelanto por el propietario → ingreso a la caja activa
    [HttpPost("{id}/pagar")]
    public async Task<ActionResult<ApiResponse<CxCPago>>> Pagar(string id, [FromBody] PagarAdelantoRequest req)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        try
        {
            var pago = await _pagos.PagarAdelantoAsync(id, req.FormaPago, req.Referencia, usuario);
            return Ok(new ApiResponse<CxCPago>(true, "Pago de adelanto registrado en caja", pago));
        }
        catch (InvalidOperationException ex) { return BadRequest(new ApiResponse<CxCPago>(false, ex.Message, null)); }
    }

    // ─── Documentos adjuntos ──────────────────────────────────────────────────
    [HttpGet("{id}/documentos")]
    public async Task<ActionResult<ApiResponse<List<AdelantoDocumentoInfo>>>> ListarDocumentos(string id)
    {
        var docs = await _svc.GetAdelantoDocumentosAsync(id);
        var info = docs.Select(d => new AdelantoDocumentoInfo(
            d.Id, d.IdAdelanto, d.NombreArchivo, d.TipoMime, d.Tamano, d.Usuario, d.FechaSubida)).ToList();
        return Ok(new ApiResponse<List<AdelantoDocumentoInfo>>(true, null, info));
    }

    [HttpPost("{id}/documentos")]
    [Authorize(Roles = "ADMINISTRADOR")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<List<AdelantoDocumentoInfo>>>> SubirDocumentos(string id, [FromForm] List<IFormFile> archivos)
    {
        var usuario = User.FindFirst(ClaimTypes.Email)?.Value ?? "sistema";
        if (archivos == null || archivos.Count == 0)
            return BadRequest(new ApiResponse<List<AdelantoDocumentoInfo>>(false, "No se recibió ningún archivo.", null));

        var resultado = new List<AdelantoDocumentoInfo>();
        try
        {
            foreach (var archivo in archivos)
            {
                if (archivo.Length == 0) continue;
                if (archivo.Length > MaxBytes)
                    return BadRequest(new ApiResponse<List<AdelantoDocumentoInfo>>(false, $"El archivo '{archivo.FileName}' supera el límite de 5 MB.", null));
                if (!MimesPermitidos.Contains(archivo.ContentType))
                    return BadRequest(new ApiResponse<List<AdelantoDocumentoInfo>>(false, $"El archivo '{archivo.FileName}' no es PDF/JPG/PNG.", null));

                using var ms = new MemoryStream();
                await archivo.CopyToAsync(ms);
                var doc = await _svc.AgregarAdelantoDocumentoAsync(id, archivo.FileName, archivo.ContentType, ms.ToArray(), usuario);
                resultado.Add(new AdelantoDocumentoInfo(doc.Id, doc.IdAdelanto, doc.NombreArchivo, doc.TipoMime, doc.Tamano, doc.Usuario, doc.FechaSubida));
            }
            return Ok(new ApiResponse<List<AdelantoDocumentoInfo>>(true, "Documento(s) adjuntado(s)", resultado));
        }
        catch (InvalidOperationException ex) { return BadRequest(new ApiResponse<List<AdelantoDocumentoInfo>>(false, ex.Message, null)); }
    }

    [HttpGet("documentos/{docId}")]
    public async Task<IActionResult> DescargarDocumento(string docId)
    {
        var doc = await _svc.GetAdelantoDocumentoAsync(docId);
        if (doc == null) return NotFound();
        return File(doc.Contenido, string.IsNullOrEmpty(doc.TipoMime) ? "application/octet-stream" : doc.TipoMime, doc.NombreArchivo);
    }

    [HttpDelete("documentos/{docId}")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<string>>> EliminarDocumento(string docId)
    {
        try
        {
            await _svc.EliminarAdelantoDocumentoAsync(docId);
            return Ok(new ApiResponse<string>(true, "Documento eliminado", docId));
        }
        catch (InvalidOperationException ex) { return BadRequest(new ApiResponse<string>(false, ex.Message, null)); }
    }
}
