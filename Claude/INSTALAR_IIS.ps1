#Requires -RunAsAdministrator
<#
  INSTALAR_IIS.ps1
  ================
  Instala y configura el Dashboard Visitas en IIS.

  Ejecutar con clic derecho -> "Ejecutar con PowerShell"
  (o en PowerShell como Administrador)

  Lo que hace este script:
    1. Habilita IIS con los modulos necesarios
    2. Instala waitress (servidor WSGI de produccion)
    3. Descarga NSSM y crea el servicio Windows "DashboardVisitas"
    4. Crea el sitio IIS con proxy inverso al servicio Python
    5. Descarga e instala ARR + URL Rewrite si no estan presentes
#>

$ErrorActionPreference = 'Stop'
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Python     = 'C:\Python314\python.exe'
$SvcName    = 'DashboardVisitas'
$Port       = 5000
$IISSite    = 'Dashboard'
$IISPort    = 8080          # Puerto HTTP en IIS (cambiar a 80 si no hay otro sitio)

Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "  Dashboard Visitas - Configuracion IIS" -ForegroundColor Cyan
Write-Host "=================================================`n" -ForegroundColor Cyan

# ── 1. HABILITAR IIS ──────────────────────────────────────────────────────────
Write-Host "[1/6] Habilitando IIS..." -ForegroundColor Yellow
$features = @(
    'IIS-WebServerRole',
    'IIS-WebServer',
    'IIS-CommonHttpFeatures',
    'IIS-DefaultDocument',
    'IIS-HttpErrors',
    'IIS-StaticContent',
    'IIS-HttpRedirect',
    'IIS-HealthAndDiagnostics',
    'IIS-HttpLogging',
    'IIS-Security',
    'IIS-RequestFiltering',
    'IIS-Performance',
    'IIS-HttpCompressionStatic',
    'IIS-WebServerManagementTools',
    'IIS-ManagementConsole',
    'IIS-ApplicationDevelopment',
    'IIS-ASPNET45'
)
foreach ($f in $features) {
    $feat = Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction SilentlyContinue
    if ($feat -and $feat.State -ne 'Enabled') {
        Enable-WindowsOptionalFeature -Online -FeatureName $f -All -NoRestart | Out-Null
        Write-Host "   Habilitado: $f"
    }
}
Write-Host "   IIS listo." -ForegroundColor Green

# ── 2. INSTALAR WAITRESS ──────────────────────────────────────────────────────
Write-Host "[2/6] Instalando waitress..." -ForegroundColor Yellow
& $Python -m pip install waitress --quiet
Write-Host "   waitress instalado." -ForegroundColor Green

# ── 3. DESCARGAR ARR + URL REWRITE ───────────────────────────────────────────
Write-Host "[3/6] Verificando ARR y URL Rewrite..." -ForegroundColor Yellow

$arrKey = 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\Application Request Routing'
$rwKey  = 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite'

if (-not (Test-Path $arrKey)) {
    Write-Host "   Descargando ARR 3.0..." -ForegroundColor Yellow
    $arrMsi = "$env:TEMP\ARR_3.0.msi"
    Invoke-WebRequest -Uri 'https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi' `
                      -OutFile $arrMsi -UseBasicParsing
    Start-Process msiexec.exe -ArgumentList "/i `"$arrMsi`" /quiet /norestart" -Wait
    Write-Host "   ARR instalado." -ForegroundColor Green
} else {
    Write-Host "   ARR ya estaba instalado." -ForegroundColor Green
}

if (-not (Test-Path $rwKey)) {
    Write-Host "   Descargando URL Rewrite 2.1..." -ForegroundColor Yellow
    $rwMsi = "$env:TEMP\urlrewrite2.msi"
    Invoke-WebRequest -Uri 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' `
                      -OutFile $rwMsi -UseBasicParsing
    Start-Process msiexec.exe -ArgumentList "/i `"$rwMsi`" /quiet /norestart" -Wait
    Write-Host "   URL Rewrite instalado." -ForegroundColor Green
} else {
    Write-Host "   URL Rewrite ya estaba instalado." -ForegroundColor Green
}

# Habilitar proxy en ARR
$arrConfig = "$env:SystemRoot\System32\inetsrv\config\applicationHost.config"
[xml]$cfg  = Get-Content $arrConfig -ErrorAction SilentlyContinue
if ($cfg) {
    # proxy enabledByDefault via appcmd
    & "$env:SystemRoot\System32\inetsrv\appcmd.exe" set config -section:system.webServer/proxy /enabled:"True" 2>$null
}

# ── 4. DESCARGAR NSSM Y CREAR SERVICIO ───────────────────────────────────────
Write-Host "[4/6] Configurando servicio Windows..." -ForegroundColor Yellow

$NssmExe = "$ProjectDir\nssm.exe"
if (-not (Test-Path $NssmExe)) {
    Write-Host "   Descargando NSSM..." -ForegroundColor Yellow
    $nssmZip = "$env:TEMP\nssm.zip"
    Invoke-WebRequest -Uri 'https://nssm.cc/release/nssm-2.24.zip' -OutFile $nssmZip -UseBasicParsing
    Expand-Archive $nssmZip -DestinationPath "$env:TEMP\nssm-pkg" -Force
    Copy-Item "$env:TEMP\nssm-pkg\nssm-2.24\win64\nssm.exe" $NssmExe
    Remove-Item $nssmZip, "$env:TEMP\nssm-pkg" -Recurse -Force
}

# Eliminar servicio previo si existe
$existingSvc = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
if ($existingSvc) {
    Stop-Service -Name $SvcName -Force -ErrorAction SilentlyContinue
    & $NssmExe remove $SvcName confirm | Out-Null
    Start-Sleep -Seconds 2
}

# Crear servicio
& $NssmExe install $SvcName $Python "$ProjectDir\wsgi_run.py"
& $NssmExe set $SvcName AppDirectory $ProjectDir
& $NssmExe set $SvcName DisplayName  "Dashboard Visitas AM COMERCIAL"
& $NssmExe set $SvcName Description  "Servidor Flask/Waitress del Dashboard de Visitas"
& $NssmExe set $SvcName Start        SERVICE_AUTO_START
& $NssmExe set $SvcName AppStdout    "$ProjectDir\logs\dashboard_stdout.log"
& $NssmExe set $SvcName AppStderr    "$ProjectDir\logs\dashboard_stderr.log"
& $NssmExe set $SvcName AppRotateFiles 1
& $NssmExe set $SvcName AppRotateBytes 5000000

New-Item -ItemType Directory -Path "$ProjectDir\logs" -Force | Out-Null

Start-Service -Name $SvcName
Start-Sleep -Seconds 3

$svc = Get-Service -Name $SvcName
Write-Host "   Servicio '$SvcName': $($svc.Status)" -ForegroundColor Green

# ── 5. CREAR SITIO EN IIS ─────────────────────────────────────────────────────
Write-Host "[5/6] Creando sitio IIS '$IISSite'..." -ForegroundColor Yellow

Import-Module WebAdministration

# Crear carpeta virtual del sitio (web.config vivirá aqui)
$SitePath = "$ProjectDir\iis_site"
New-Item -ItemType Directory -Path $SitePath -Force | Out-Null
Copy-Item "$ProjectDir\web.config" "$SitePath\web.config" -Force

# Eliminar sitio previo si existe
if (Get-Website -Name $IISSite -ErrorAction SilentlyContinue) {
    Remove-Website -Name $IISSite
}

# Crear nuevo sitio
New-Website -Name $IISSite -Port $IISPort -PhysicalPath $SitePath -Force | Out-Null

# App pool: sin código gestionado (solo proxy)
$poolName = "${IISSite}Pool"
if (-not (Test-Path "IIS:\AppPools\$poolName")) {
    New-WebAppPool -Name $poolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$poolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\Sites\$IISSite"    -Name applicationPool -Value $poolName

Start-Website -Name $IISSite
Write-Host "   Sitio IIS '$IISSite' activo en puerto $IISPort." -ForegroundColor Green

# ── 6. REGLA DE FIREWALL ──────────────────────────────────────────────────────
Write-Host "[6/6] Abriendo puerto $IISPort en firewall..." -ForegroundColor Yellow
New-NetFirewallRule -DisplayName "Dashboard Visitas IIS" `
                    -Direction Inbound -Protocol TCP -LocalPort $IISPort `
                    -Action Allow -ErrorAction SilentlyContinue | Out-Null
Write-Host "   Puerto $IISPort abierto." -ForegroundColor Green

# ── RESUMEN ───────────────────────────────────────────────────────────────────
$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike '*Loopback*' } | Select-Object -First 1).IPAddress
Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "  INSTALACION COMPLETADA" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Dashboard disponible en:"
Write-Host "    Local:   http://localhost:$IISPort" -ForegroundColor White
Write-Host "    Red:     http://${ip}:$IISPort" -ForegroundColor White
Write-Host ""
Write-Host "  Servicio Windows: $SvcName"
Write-Host "  Sitio IIS:        $IISSite"
Write-Host "  Logs:             $ProjectDir\logs\"
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""
Pause
