#Requires -RunAsAdministrator
<#
  DEPLOY_SERVIDOR.ps1
  ===================
  Ejecutar en el SERVIDOR IIS como Administrador.

  - Copia los archivos del dashboard a C:\DashboardVisitas\
  - Instala waitress (servidor WSGI de produccion)
  - Crea/actualiza el servicio Windows "DashboardVisitas"
  - Crea la aplicacion virtual "RMUtiles" en IIS
  - Instala ARR + URL Rewrite si no estan presentes

  ANTES DE EJECUTAR:
    Asegurate de que Python 3.x este instalado en este servidor.
    Si no lo esta, descargalo de https://www.python.org/downloads/
    y marca "Add Python to PATH" durante la instalacion.
#>

$ErrorActionPreference = 'Stop'

# ── CONFIGURACION ─────────────────────────────────────────────────────────────
$AppDir    = 'C:\DashboardVisitas'        # Donde vivira el codigo Python
$SvcName   = 'DashboardVisitas'
$SvcPort   = 5000                         # Puerto interno Flask/waitress
$IISSite   = 'Default Web Site'          # Sitio IIS padre (cambiar si es otro)
$VirtApp   = 'RMUtiles'                  # Nombre de la aplicacion virtual en IIS
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "  Dashboard Visitas - Deploy en Servidor IIS" -ForegroundColor Cyan
Write-Host "=================================================`n" -ForegroundColor Cyan

# ── Detectar Python ───────────────────────────────────────────────────────────
$Python = (Get-Command python -ErrorAction SilentlyContinue)?.Source
if (-not $Python) {
    Write-Host "ERROR: Python no encontrado en PATH." -ForegroundColor Red
    Write-Host "Instala Python desde https://www.python.org/downloads/" -ForegroundColor Yellow
    Write-Host "Marca 'Add Python to PATH' durante la instalacion." -ForegroundColor Yellow
    Pause; exit 1
}
Write-Host "Python: $Python" -ForegroundColor Green

# ── 1. COPIAR ARCHIVOS AL SERVIDOR ────────────────────────────────────────────
Write-Host "`n[1/6] Copiando archivos a $AppDir..." -ForegroundColor Yellow
$FilesToCopy = @(
    'dashboard_server.py',
    'dashboard.html',
    'wsgi_run.py',
    '.env'
)
New-Item -ItemType Directory -Path $AppDir -Force | Out-Null
New-Item -ItemType Directory -Path "$AppDir\logs" -Force | Out-Null

$SrcDir = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($f in $FilesToCopy) {
    $src = Join-Path $SrcDir $f
    if (Test-Path $src) {
        Copy-Item $src "$AppDir\$f" -Force
        Write-Host "   Copiado: $f"
    } else {
        Write-Host "   AVISO: no encontrado: $f" -ForegroundColor Yellow
    }
}
Write-Host "   Archivos listos." -ForegroundColor Green

# ── 2. INSTALAR DEPENDENCIAS PYTHON ──────────────────────────────────────────
Write-Host "`n[2/6] Instalando dependencias Python..." -ForegroundColor Yellow
& $Python -m pip install flask waitress pyodbc --quiet --no-warn-script-location
Write-Host "   Dependencias instaladas." -ForegroundColor Green

# ── 3. ARR + URL REWRITE ──────────────────────────────────────────────────────
Write-Host "`n[3/6] Verificando ARR y URL Rewrite..." -ForegroundColor Yellow

function Install-MSI($url, $label) {
    $tmp = "$env:TEMP\$label.msi"
    Write-Host "   Descargando $label..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
    Start-Process msiexec.exe -ArgumentList "/i `"$tmp`" /quiet /norestart" -Wait
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    Write-Host "   $label instalado." -ForegroundColor Green
}

if (-not (Test-Path 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\Application Request Routing')) {
    Install-MSI 'https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi' 'ARR'
} else { Write-Host "   ARR: ya instalado." -ForegroundColor Green }

if (-not (Test-Path 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite')) {
    Install-MSI 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' 'URL Rewrite'
} else { Write-Host "   URL Rewrite: ya instalado." -ForegroundColor Green }

# Habilitar modo proxy en ARR
$appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"
if (Test-Path $appcmd) {
    & $appcmd set config -section:system.webServer/proxy /enabled:"True" 2>$null
    Write-Host "   Proxy ARR habilitado." -ForegroundColor Green
}

# ── 4. SERVICIO WINDOWS (NSSM) ────────────────────────────────────────────────
Write-Host "`n[4/6] Configurando servicio Windows..." -ForegroundColor Yellow
$NssmExe = "$AppDir\nssm.exe"
if (-not (Test-Path $NssmExe)) {
    Write-Host "   Descargando NSSM..."
    $z = "$env:TEMP\nssm.zip"
    Invoke-WebRequest 'https://nssm.cc/release/nssm-2.24.zip' -OutFile $z -UseBasicParsing
    Expand-Archive $z "$env:TEMP\nssm-pkg" -Force
    Copy-Item "$env:TEMP\nssm-pkg\nssm-2.24\win64\nssm.exe" $NssmExe
    Remove-Item $z, "$env:TEMP\nssm-pkg" -Recurse -Force
    Write-Host "   NSSM descargado." -ForegroundColor Green
}

# Eliminar servicio anterior si existe
$existing = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service $SvcName -Force -ErrorAction SilentlyContinue
    & $NssmExe remove $SvcName confirm 2>$null
    Start-Sleep -Seconds 2
}

# Crear servicio
& $NssmExe install     $SvcName $Python "$AppDir\wsgi_run.py"
& $NssmExe set         $SvcName AppDirectory   $AppDir
& $NssmExe set         $SvcName DisplayName    "Dashboard Visitas AM COMERCIAL"
& $NssmExe set         $SvcName Description    "Backend Flask/Waitress del Dashboard de Visitas"
& $NssmExe set         $SvcName Start          SERVICE_AUTO_START
& $NssmExe set         $SvcName AppStdout      "$AppDir\logs\stdout.log"
& $NssmExe set         $SvcName AppStderr      "$AppDir\logs\stderr.log"
& $NssmExe set         $SvcName AppRotateFiles 1
& $NssmExe set         $SvcName AppRotateBytes 5242880

Start-Service $SvcName
Start-Sleep   3
$s = Get-Service $SvcName
Write-Host "   Servicio '$SvcName': $($s.Status)" -ForegroundColor $(if ($s.Status -eq 'Running') { 'Green' } else { 'Red' })

# ── 5. VIRTUAL APPLICATION EN IIS ─────────────────────────────────────────────
Write-Host "`n[5/6] Creando aplicacion virtual IIS '/$VirtApp'..." -ForegroundColor Yellow
Import-Module WebAdministration

# Carpeta fisica de la app virtual (solo necesita el web.config)
$WebRoot = "C:\inetpub\wwwroot\$VirtApp"
New-Item -ItemType Directory -Path $WebRoot -Force | Out-Null

# web.config con proxy inverso
$WebConfig = @"
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="${VirtApp}Proxy" stopProcessing="true">
          <match url="(.*)" />
          <action type="Rewrite" url="http://127.0.0.1:${SvcPort}/{R:1}" />
        </rule>
      </rules>
    </rewrite>
    <defaultDocument><files><clear /></files></defaultDocument>
    <httpErrors existingResponse="PassThrough" />
  </system.webServer>
</configuration>
"@
Set-Content "$WebRoot\web.config" $WebConfig -Encoding UTF8

# App pool sin codigo gestionado
$PoolName = "${VirtApp}Pool"
if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    New-WebAppPool -Name $PoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name startMode             -Value 'AlwaysRunning'

# Crear/reemplazar la aplicacion virtual
$iisPath = "IIS:\Sites\$IISSite\$VirtApp"
if (Test-Path $iisPath) {
    Remove-WebApplication -Name $VirtApp -Site $IISSite -ErrorAction SilentlyContinue
}
New-WebApplication -Name $VirtApp -Site $IISSite -PhysicalPath $WebRoot -ApplicationPool $PoolName | Out-Null
Write-Host "   Aplicacion virtual '/$VirtApp' creada." -ForegroundColor Green

# ── 6. FIREWALL ───────────────────────────────────────────────────────────────
Write-Host "`n[6/6] Reglas de firewall..." -ForegroundColor Yellow
New-NetFirewallRule -DisplayName "Dashboard Visitas (backend)" `
    -Direction Inbound -Protocol TCP -LocalPort $SvcPort -Action Allow `
    -ErrorAction SilentlyContinue | Out-Null
Write-Host "   Firewall listo." -ForegroundColor Green

# ── RESUMEN ───────────────────────────────────────────────────────────────────
$ip    = (Get-NetIPAddress -AddressFamily IPv4 |
          Where-Object { $_.InterfaceAlias -notlike '*Loopback*' } |
          Select-Object -First 1).IPAddress
$bind  = (Get-WebBinding -Name $IISSite | Select-Object -First 1).bindingInformation
$iport = ($bind -split ':')[1]
$iport = if ($iport -eq '80') { '' } else { ":$iport" }

Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "  DEPLOY COMPLETADO" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Dashboard disponible en:"
Write-Host "    Local: http://localhost${iport}/$VirtApp" -ForegroundColor White
Write-Host "    Red:   http://${ip}${iport}/$VirtApp"    -ForegroundColor White
Write-Host ""
Write-Host "  Archivos Python: $AppDir"
Write-Host "  Web root IIS:    $WebRoot"
Write-Host "  Servicio:        $SvcName  (auto-inicio)"
Write-Host "  Logs:            $AppDir\logs\"
Write-Host "=================================================" -ForegroundColor Cyan
Pause
