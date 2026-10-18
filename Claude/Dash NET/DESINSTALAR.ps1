#Requires -RunAsAdministrator
<#
  DESINSTALAR.ps1  —  Dashboard Visitas
  Detiene el servicio, lo elimina y borra los archivos instalados.
#>

$ServiceName = "DashVisitas"
$InstallDir  = "C:\Program Files\DashVisitas"
$Port        = 5000

Write-Host ""
Write-Host "  Desinstalando Dashboard Visitas..." -ForegroundColor Yellow
Write-Host ""

# Detener y eliminar servicio
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -eq "Running") {
        Write-Host "  Deteniendo servicio..." -ForegroundColor Gray
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    sc.exe delete $ServiceName | Out-Null
    Write-Host "  Servicio eliminado." -ForegroundColor Green
} else {
    Write-Host "  El servicio no estaba instalado." -ForegroundColor Gray
}

# Eliminar regla de firewall
Remove-NetFirewallRule -DisplayName "DashVisitas Puerto $Port" -ErrorAction SilentlyContinue
Write-Host "  Regla de firewall eliminada." -ForegroundColor Green

# Eliminar archivos
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "  Archivos eliminados de '$InstallDir'." -ForegroundColor Green
} else {
    Write-Host "  Carpeta '$InstallDir' no encontrada." -ForegroundColor Gray
}

Write-Host ""
Write-Host "  Desinstalacion completada." -ForegroundColor Green
Write-Host ""
Pause
