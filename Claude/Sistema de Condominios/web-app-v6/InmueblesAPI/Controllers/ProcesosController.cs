using InmueblesAPI.Models;
using InmueblesAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InmueblesAPI.Controllers;

[ApiController]
[Route("api/procesos")]
[Authorize]
public class ProcesosController : ControllerBase
{
    private readonly InmueblesService _svc;
    private readonly BackupService _backup;
    public ProcesosController(InmueblesService sheets, BackupService backup)
    {
        _svc = sheets;
        _backup = backup;
    }

    [HttpGet("estado")]
    public async Task<ActionResult<ApiResponse<ProcesosEstado>>> GetEstado()
    {
        var estado = await _svc.GetProcesosEstadoAsync();
        return Ok(new ApiResponse<ProcesosEstado>(true, null, estado));
    }

    [HttpPost("recalcular-mora")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<int>>> RecalcularMora()
    {
        var cantidad = await _svc.RecalcularMorasMasivoAsync();
        return Ok(new ApiResponse<int>(true, $"{cantidad} CxC actualizada(s) con su mora recalculada", cantidad));
    }

    [HttpPost("generar-cargos")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<int>>> GenerarCargos()
    {
        var cantidad = await _svc.GenerarCargosRecurrentesAsync();
        return Ok(new ApiResponse<int>(true, $"{cantidad} cargo(s) de alquiler generado(s)", cantidad));
    }

    // ─── Configuración: cuándo generar los cargos ─────────────────────────────
    [HttpGet("config-cargos")]
    public async Task<ActionResult<ApiResponse<string>>> GetConfigCargos()
    {
        var modo = await _svc.GetConfigAsync("GENERACION_CARGOS_MODO") ?? "INICIO_CICLO";
        return Ok(new ApiResponse<string>(true, null, modo));
    }

    [HttpPut("config-cargos")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<string>>> SetConfigCargos([FromBody] ConfigCargosRequest req)
    {
        var validos = new[] { "INICIO_CICLO", "FIN_CICLO", "DIA_DESPUES" };
        if (!validos.Contains(req.Modo))
            return BadRequest(new ApiResponse<string>(false, "Modo inválido", null));
        await _svc.SetConfigAsync("GENERACION_CARGOS_MODO", req.Modo);
        return Ok(new ApiResponse<string>(true, "Configuración guardada", req.Modo));
    }

    // ─── V6 #8: Respaldo ──────────────────────────────────────────────────────
    [HttpPost("backup")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<BackupResultado>>> RespaldarAhora()
    {
        var email = User.Identity?.Name ?? "administrador";
        var res = await _backup.EjecutarAsync($"manual ({email})");
        return Ok(new ApiResponse<BackupResultado>(res.Ok, res.Mensaje, res));
    }

    [HttpGet("config-backup")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<ConfigBackupRequest>>> GetConfigBackup()
    {
        var cfg = new ConfigBackupRequest(
            CarpetaLocal:  await _svc.GetConfigAsync("BACKUP_CARPETA_LOCAL")  ?? @"C:\InmueblesV6-Backups",
            ClienteNombre: await _svc.GetConfigAsync("BACKUP_CLIENTE_NOMBRE") ?? "",
            RetencionDias: int.TryParse(await _svc.GetConfigAsync("BACKUP_RETENCION_DIAS"), out var r) ? r : 8,
            DriveHabilitado: (await _svc.GetConfigAsync("BACKUP_DRIVE_HABILITADO")) == "true",
            RcloneRemoto:  await _svc.GetConfigAsync("BACKUP_RCLONE_REMOTO")   ?? ""
        );
        return Ok(new ApiResponse<ConfigBackupRequest>(true, null, cfg));
    }

    [HttpPut("config-backup")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public async Task<ActionResult<ApiResponse<string>>> SetConfigBackup([FromBody] ConfigBackupRequest req)
    {
        await _svc.SetConfigAsync("BACKUP_CARPETA_LOCAL",   req.CarpetaLocal ?? "");
        await _svc.SetConfigAsync("BACKUP_CLIENTE_NOMBRE",  req.ClienteNombre ?? "");
        await _svc.SetConfigAsync("BACKUP_RETENCION_DIAS",  Math.Max(1, req.RetencionDias).ToString());
        await _svc.SetConfigAsync("BACKUP_DRIVE_HABILITADO", req.DriveHabilitado ? "true" : "false");
        await _svc.SetConfigAsync("BACKUP_RCLONE_REMOTO",   req.RcloneRemoto ?? "");
        return Ok(new ApiResponse<string>(true, "Configuración de respaldo guardada", null));
    }
}

public record ConfigCargosRequest(string Modo);

public record ConfigBackupRequest(
    string CarpetaLocal,
    string ClienteNombre,
    int RetencionDias,
    bool DriveHabilitado,
    string RcloneRemoto
);
