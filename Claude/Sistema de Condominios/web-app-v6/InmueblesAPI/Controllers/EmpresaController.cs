using InmueblesAPI.Data;
using InmueblesAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/empresa")]
[Authorize]
public class EmpresaController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var e = await db.Empresas.FirstOrDefaultAsync();
        if (e == null)
            return Ok(new ApiResponse<EmpresaDto>(true, null,
                new EmpresaDto("", "", "", "", "", "", "", "")));

        return Ok(new ApiResponse<EmpresaDto>(true, null,
            new EmpresaDto(e.Nombre, e.Direccion, e.Direccion2,
                           e.Rnc, e.Telefono, e.Fax, e.Email, e.Logo)));
    }

    [HttpPut]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<IActionResult> Update([FromBody] ActualizarEmpresaRequest req)
    {
        var e = await db.Empresas.FirstOrDefaultAsync();
        if (e == null)
        {
            e = new Empresa();
            db.Empresas.Add(e);
        }

        e.Nombre     = req.Nombre;
        e.Direccion  = req.Direccion;
        e.Direccion2 = req.Direccion2;
        e.Rnc        = req.Rnc;
        e.Telefono   = req.Telefono;
        e.Fax        = req.Fax;
        e.Email      = req.Email;
        e.Logo       = req.Logo;
        e.FechaActualizacion = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(new ApiResponse<object>(true, "Empresa actualizada", null));
    }
}
