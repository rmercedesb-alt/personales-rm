#Requires -RunAsAdministrator
<#
  INSTALAR_EN_IIS.ps1
  ====================
  Ejecutar en el servidor IIS como Administrador.

  Crea la aplicacion /RMUtiles en IIS apuntando a esta carpeta.
  No requiere Python ni ningun servicio adicional.
  Solo necesita: IIS + Classic ASP (se habilita aqui si falta).
#>

$ErrorActionPreference = 'Stop'

# ── CONFIGURACION ─────────────────────────────────────────────────────────────
$SrcDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$DestDir  = 'C:\inetpub\wwwroot\RMUtiles'   # Carpeta en el servidor
$IISSite  = 'Default Web Site'              # Sitio IIS padre
$VirtApp  = 'RMUtiles'                      # Nombre en IIS
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "  Dashboard Visitas - Instalacion en IIS (ASP)" -ForegroundColor Cyan
Write-Host "=================================================`n" -ForegroundColor Cyan

# ── 1. HABILITAR CLASSIC ASP EN IIS ──────────────────────────────────────────
Write-Host "[1/4] Habilitando Classic ASP en IIS..." -ForegroundColor Yellow

$aspFeatures = @(
    'IIS-WebServerRole',
    'IIS-WebServer',
    'IIS-CommonHttpFeatures',
    'IIS-DefaultDocument',
    'IIS-HttpErrors',
    'IIS-StaticContent',
    'IIS-HealthAndDiagnostics',
    'IIS-HttpLogging',
    'IIS-Security',
    'IIS-RequestFiltering',
    'IIS-WebServerManagementTools',
    'IIS-ManagementConsole',
    'IIS-ASP'                         # <-- Classic ASP
)
foreach ($f in $aspFeatures) {
    $feat = Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction SilentlyContinue
    if ($feat -and $feat.State -ne 'Enabled') {
        Write-Host "   Habilitando: $f"
        Enable-WindowsOptionalFeature -Online -FeatureName $f -All -NoRestart | Out-Null
    }
}

# Habilitar envio de errores detallados en ASP (util para depurar)
$appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"
if (Test-Path $appcmd) {
    & $appcmd set config /section:asp /scriptErrorSentToBrowser:true 2>$null
    & $appcmd set config /section:asp /enableParentPaths:true        2>$null
}
Write-Host "   Classic ASP habilitado." -ForegroundColor Green

# ── 2. COPIAR ARCHIVOS ────────────────────────────────────────────────────────
Write-Host "`n[2/4] Copiando archivos a $DestDir..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $DestDir -Force | Out-Null

$files = @('dashboard.html', 'api_data.asp', 'web.config', '.env')
foreach ($f in $files) {
    $src = Join-Path $SrcDir $f
    if (Test-Path $src) {
        Copy-Item $src "$DestDir\$f" -Force
        Write-Host "   $f" -ForegroundColor Gray
    } else {
        Write-Host "   FALTA: $f" -ForegroundColor Red
    }
}
Write-Host "   Archivos copiados." -ForegroundColor Green

# ── 3. CREAR APLICACION VIRTUAL EN IIS ───────────────────────────────────────
Write-Host "`n[3/4] Creando aplicacion virtual /$VirtApp en IIS..." -ForegroundColor Yellow
Import-Module WebAdministration

# App Pool sin codigo gestionado (no .NET, solo ASP clasico)
$PoolName = "${VirtApp}AppPool"
if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    New-WebAppPool -Name $PoolName | Out-Null
    Write-Host "   App Pool '$PoolName' creado."
}
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name enable32BitAppOnWin64 -Value $false

# Quitar app virtual anterior si existe
$iisPath = "IIS:\Sites\$IISSite\$VirtApp"
if (Test-Path $iisPath) {
    Remove-WebApplication -Name $VirtApp -Site $IISSite -ErrorAction SilentlyContinue
    Write-Host "   Aplicacion previa eliminada."
}

# Crear la aplicacion virtual
New-WebApplication -Name $VirtApp `
                   -Site $IISSite `
                   -PhysicalPath $DestDir `
                   -ApplicationPool $PoolName | Out-Null

Write-Host "   Aplicacion '/$VirtApp' creada." -ForegroundColor Green

# ── 4. PERMISOS DE LECTURA ────────────────────────────────────────────────────
Write-Host "`n[4/4] Asignando permisos de lectura al App Pool..." -ForegroundColor Yellow
$acl  = Get-Acl $DestDir
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "IIS AppPool\$PoolName", 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'
)
$acl.SetAccessRule($rule)
Set-Acl $DestDir $acl
Write-Host "   Permisos asignados." -ForegroundColor Green

# ── RESUMEN ───────────────────────────────────────────────────────────────────
$ip   = (Get-NetIPAddress -AddressFamily IPv4 |
         Where-Object { $_.InterfaceAlias -notlike '*Loopback*' } |
         Select-Object -First 1).IPAddress
$bind = (Get-WebBinding -Name $IISSite | Select-Object -First 1).bindingInformation
$port = ($bind -split ':')[1]
$portStr = if ($port -eq '80') { '' } else { ":$port" }

Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "  INSTALACION COMPLETADA" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Dashboard disponible en:"
Write-Host "    Local : http://localhost${portStr}/$VirtApp" -ForegroundColor White
Write-Host "    Red   : http://${ip}${portStr}/$VirtApp"    -ForegroundColor White
Write-Host ""
Write-Host "  Archivos : $DestDir"
Write-Host "  IIS site : $IISSite > $VirtApp"
Write-Host "  App Pool : $PoolName"
Write-Host ""
Write-Host "  Si el sitio IIS padre no es '$IISSite',"
Write-Host "  edita la variable `$IISSite en este script."
Write-Host "=================================================" -ForegroundColor Cyan
Pause
