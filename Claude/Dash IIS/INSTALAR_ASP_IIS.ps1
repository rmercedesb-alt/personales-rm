#Requires -RunAsAdministrator
<#
  INSTALAR_ASP_IIS.ps1
  =====================
  Registra la carpeta "Dash IIS" como aplicacion Classic ASP dentro de
  la aplicacion RMUtiles ya existente en IIS.

  URL resultante:  http://<servidor>/RMUtiles/DashVisitas/
#>

# ══════════════════════════════════════════════════════════
#  CONFIGURACION  (ajusta si es necesario)
# ══════════════════════════════════════════════════════════
$SitioIIS      = "Default Web Site"
$RutaRMUtiles  = "RMUtiles"
$NombreApp     = "DashVisitas"
$CarpetaFuente = Split-Path -Parent $MyInvocation.MyCommand.Path
# ══════════════════════════════════════════════════════════

$AppIISPath = "IIS:\Sites\$SitioIIS\$RutaRMUtiles\$NombreApp"
$WebPath    = "/$RutaRMUtiles/$NombreApp"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Instalador Dashboard Visitas - Classic ASP" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Sitio IIS  : $SitioIIS"
Write-Host "  Ruta web   : $WebPath"
Write-Host "  Carpeta    : $CarpetaFuente"
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Cargar modulo IIS
Write-Host "[1/6] Cargando modulo WebAdministration..." -ForegroundColor Yellow
Import-Module WebAdministration -ErrorAction Stop
Write-Host "      OK" -ForegroundColor Green

# 2. Habilitar Classic ASP
Write-Host "[2/6] Habilitando Classic ASP..." -ForegroundColor Yellow
try {
    Enable-WindowsOptionalFeature -Online -FeatureName "IIS-ASP" -NoRestart -ErrorAction SilentlyContinue | Out-Null
} catch {}

Set-WebConfigurationProperty -pspath "MACHINE/WEBROOT/APPHOST" `
    -filter "system.webServer/asp" -name "scriptErrorSentToBrowser" -value "True" `
    -ErrorAction SilentlyContinue

Set-WebConfigurationProperty -pspath "MACHINE/WEBROOT/APPHOST" `
    -filter "system.webServer/asp" -name "codePage" -value 65001 `
    -ErrorAction SilentlyContinue

Write-Host "      OK" -ForegroundColor Green

# 3. Crear Application Pool dedicado
Write-Host "[3/6] Configurando Application Pool..." -ForegroundColor Yellow
$PoolName = "DashVisitasPool"

if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    New-WebAppPool -Name $PoolName | Out-Null
    Write-Host "      Pool creado: $PoolName" -ForegroundColor Green
} else {
    Write-Host "      Pool ya existe: $PoolName" -ForegroundColor Gray
}

Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedPipelineMode"   -Value "Classic"
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "startMode"             -Value "AlwaysRunning"
Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
Write-Host "      OK - modo Classic pipeline" -ForegroundColor Green

# 4. Crear o actualizar la aplicacion web en IIS
Write-Host "[4/6] Registrando aplicacion en IIS ($WebPath)..." -ForegroundColor Yellow

if (Test-Path $AppIISPath) {
    Write-Host "      Aplicacion encontrada. Actualizando ruta fisica..." -ForegroundColor Gray
    Set-ItemProperty $AppIISPath -Name "physicalPath"    -Value $CarpetaFuente
    Set-ItemProperty $AppIISPath -Name "applicationPool" -Value $PoolName
} else {
    New-WebApplication -Site $SitioIIS `
                       -Name "$RutaRMUtiles/$NombreApp" `
                       -PhysicalPath $CarpetaFuente `
                       -ApplicationPool $PoolName | Out-Null
}
Write-Host "      OK" -ForegroundColor Green

# 5. Permisos de lectura
Write-Host "[5/6] Asignando permisos de lectura..." -ForegroundColor Yellow
$acl = Get-Acl $CarpetaFuente

foreach ($account in @("IUSR", "IIS_IUSRS")) {
    try {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $account, "ReadAndExecute",
            "ContainerInherit,ObjectInherit", "None", "Allow"
        )
        $acl.SetAccessRule($rule)
    } catch {}
}
Set-Acl -Path $CarpetaFuente -AclObject $acl
Write-Host "      OK" -ForegroundColor Green

# 6. Reiniciar IIS
Write-Host "[6/6] Reiniciando IIS..." -ForegroundColor Yellow
Start-Service W3SVC -ErrorAction SilentlyContinue
iisreset /noforce 2>$null | Out-Null
Write-Host "      OK" -ForegroundColor Green

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
       Where-Object { $_.InterfaceAlias -notlike "*Loopback*" } |
       Select-Object -First 1).IPAddress

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  INSTALACION COMPLETADA" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Accede al dashboard:"
Write-Host "    http://localhost$WebPath/"  -ForegroundColor White
Write-Host "    http://$ip$WebPath/"        -ForegroundColor White
Write-Host ""
Write-Host "  Verifica columnas de Empresa (borrar despues):"
Write-Host "    http://$ip$WebPath/api/empresa-cols.asp" -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Pause
