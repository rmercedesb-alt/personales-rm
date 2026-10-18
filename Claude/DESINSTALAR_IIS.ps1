#Requires -RunAsAdministrator
# Elimina el servicio y el sitio IIS del Dashboard Visitas

$SvcName = 'DashboardVisitas'
$IISSite = 'Dashboard'
$NssmExe = "$PSScriptRoot\nssm.exe"

Write-Host "Deteniendo y eliminando servicio '$SvcName'..." -ForegroundColor Yellow
Stop-Service -Name $SvcName -Force -ErrorAction SilentlyContinue
if (Test-Path $NssmExe) {
    & $NssmExe remove $SvcName confirm 2>$null
} else {
    sc.exe delete $SvcName 2>$null
}

Write-Host "Eliminando sitio IIS '$IISSite'..." -ForegroundColor Yellow
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (Get-Website -Name $IISSite -ErrorAction SilentlyContinue) {
    Stop-Website  -Name $IISSite -ErrorAction SilentlyContinue
    Remove-Website -Name $IISSite
}
Remove-WebAppPool -Name "${IISSite}Pool" -ErrorAction SilentlyContinue

Write-Host "Listo." -ForegroundColor Green
Pause
