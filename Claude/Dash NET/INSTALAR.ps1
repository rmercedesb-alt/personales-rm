#Requires -RunAsAdministrator
<#
  INSTALAR.ps1  —  Dashboard Visitas (ASP.NET Core 8)
  =====================================================
  Este script hace TODO de forma automatica:
    1. Verifica / instala .NET 8 Runtime
    2. Compila y publica la aplicacion
    3. Instala como Servicio de Windows (corre en segundo plano)
    4. Abre el firewall en el puerto elegido
    5. Muestra la URL final

  No se necesita IIS ni Python. El servicio arranca con Windows.
  URL resultante:  http://localhost:5000/
#>

# ════════════════════════════════════════════════════════════
#   CONFIGURACION  (cambia solo si es necesario)
# ════════════════════════════════════════════════════════════
$ServiceName = "DashVisitas"
$DisplayName = "Dashboard Visitas AM Comercial"
$Description = "Dashboard de visitas en vivo - AM COMERCIAL"
$InstallDir  = "C:\Program Files\DashVisitas"
$Port        = 5000
$SourceDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
# ════════════════════════════════════════════════════════════

$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"   # hace los downloads mas rapidos

function Write-Step($n, $total, $msg) {
    Write-Host ""
    Write-Host "  [$n/$total] $msg" -ForegroundColor Cyan
}
function Write-OK($msg)   { Write-Host "        OK  $msg" -ForegroundColor Green  }
function Write-Info($msg) { Write-Host "        ... $msg" -ForegroundColor Gray   }
function Write-Warn($msg) { Write-Host "       WARN $msg" -ForegroundColor Yellow }

Clear-Host
Write-Host ""
Write-Host "  =================================================" -ForegroundColor Cyan
Write-Host "   Instalador  -  Dashboard Visitas  (ASP.NET 8)  " -ForegroundColor Cyan
Write-Host "  =================================================" -ForegroundColor Cyan
Write-Host "   Servicio  : $ServiceName"
Write-Host "   Carpeta   : $InstallDir"
Write-Host "   Puerto    : $Port"
Write-Host "  =================================================" -ForegroundColor Cyan

# ── PASO 1: Verificar / instalar .NET 8 ──────────────────────────────────────
Write-Step 1 5 "Verificando .NET 8 ASP.NET Core Runtime..."

$dotnetOk = $false
try {
    $runtimes = & dotnet --list-runtimes 2>&1
    if ($runtimes -match "Microsoft\.AspNetCore\.App 8\.") { $dotnetOk = $true }
} catch {}

if ($dotnetOk) {
    Write-OK ".NET 8 ya esta instalado."
} else {
    Write-Info ".NET 8 no encontrado. Descargando Hosting Bundle (~100 MB)..."

    $bundleUrl  = "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe"
    $bundlePath = "$env:TEMP\dotnet-hosting-win.exe"

    try {
        Invoke-WebRequest -Uri $bundleUrl -OutFile $bundlePath -UseBasicParsing
        Write-Info "Instalando .NET 8 Hosting Bundle (puede tardar 2-3 min)..."
        Start-Process -FilePath $bundlePath -ArgumentList "/quiet /norestart" -Wait
        Remove-Item $bundlePath -Force -ErrorAction SilentlyContinue
        Write-OK ".NET 8 instalado correctamente."

        # Recargar PATH para que dotnet quede disponible
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
                    [System.Environment]::GetEnvironmentVariable("Path","User")
    } catch {
        Write-Host ""
        Write-Host "  ERROR: No se pudo descargar .NET 8." -ForegroundColor Red
        Write-Host "  Descargalo manualmente desde:" -ForegroundColor Yellow
        Write-Host "    https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor White
        Write-Host "  Instala el '.NET 8 Hosting Bundle' y vuelve a correr este script." -ForegroundColor Yellow
        Pause; exit 1
    }
}

# Verificar que dotnet este en PATH
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "  ERROR: 'dotnet' no esta en el PATH. Reinicia y vuelve a correr el instalador." -ForegroundColor Red
    Pause; exit 1
}

# ── PASO 2: Publicar la aplicacion ───────────────────────────────────────────
Write-Step 2 5 "Compilando y publicando la aplicacion..."

$publishDir = "$env:TEMP\DashVisitas_publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Info "dotnet publish -c Release..."
try {
    $result = & dotnet publish "$SourceDir\DashVisitas.csproj" `
                  -c Release `
                  -o $publishDir `
                  --nologo 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "  ERROR al compilar:" -ForegroundColor Red
        $result | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        Write-Host ""
        Write-Host "  Asegurate de que el proyecto compila antes de instalar." -ForegroundColor Yellow
        Pause; exit 1
    }
    Write-OK "Compilacion exitosa."
} catch {
    Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Pause; exit 1
}

# ── PASO 3: Copiar a carpeta de instalacion ───────────────────────────────────
Write-Step 3 5 "Instalando en '$InstallDir'..."

# Detener servicio anterior si existe
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -eq "Running") {
        Write-Info "Deteniendo servicio anterior..."
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    Write-Info "Eliminando servicio anterior..."
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Crear o limpiar carpeta de instalacion
if (Test-Path $InstallDir) {
    Remove-Item "$InstallDir\*" -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Copy-Item "$publishDir\*" -Destination $InstallDir -Recurse -Force
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-OK "Archivos copiados a '$InstallDir'."

# ── PASO 4: Instalar como Servicio de Windows ─────────────────────────────────
Write-Step 4 5 "Registrando Servicio de Windows..."

$exePath = "$InstallDir\DashVisitas.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "  ERROR: No se encontro '$exePath'." -ForegroundColor Red
    Write-Host "  Verifica que el proyecto se llama DashVisitas.csproj" -ForegroundColor Yellow
    Pause; exit 1
}

$binPath = "`"$exePath`" --urls `"http://0.0.0.0:$Port`""

sc.exe create $ServiceName `
    binPath= $binPath `
    DisplayName= $DisplayName `
    start= auto | Out-Null

sc.exe description $ServiceName $Description | Out-Null

# Configurar reinicio automatico en caso de fallo
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-OK "Servicio '$ServiceName' creado."

# ── PASO 5: Arrancar y abrir firewall ────────────────────────────────────────
Write-Step 5 5 "Iniciando servicio y configurando firewall..."

# Regla de firewall para el puerto
New-NetFirewallRule -DisplayName "DashVisitas Puerto $Port" `
    -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow `
    -ErrorAction SilentlyContinue | Out-Null

# Iniciar servicio
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3

$svc = Get-Service -Name $ServiceName
if ($svc.Status -eq "Running") {
    Write-OK "Servicio corriendo."
} else {
    Write-Warn "El servicio no arranco correctamente. Revisa los logs en:"
    Write-Warn "  Visor de eventos > Registros de Windows > Aplicacion"
}

# ── Resumen ───────────────────────────────────────────────────────────────────
$ip = (Get-NetIPAddress -AddressFamily IPv4 |
       Where-Object { $_.InterfaceAlias -notlike "*Loopback*" } |
       Select-Object -First 1).IPAddress

Write-Host ""
Write-Host "  =================================================" -ForegroundColor Green
Write-Host "   INSTALACION COMPLETADA" -ForegroundColor Green
Write-Host "  =================================================" -ForegroundColor Green
Write-Host ""
Write-Host "   Dashboard disponible en:"
Write-Host "     http://localhost:$Port/" -ForegroundColor White
Write-Host "     http://${ip}:$Port/"    -ForegroundColor White
Write-Host ""
Write-Host "   El servicio arranca automaticamente con Windows."
Write-Host ""
Write-Host "   Para administrar el servicio:"
Write-Host "     Inicio > Servicios > '$DisplayName'"
Write-Host "     o desde PowerShell:"
Write-Host "       Start-Service $ServiceName" -ForegroundColor Gray
Write-Host "       Stop-Service  $ServiceName" -ForegroundColor Gray
Write-Host "       Get-Service   $ServiceName" -ForegroundColor Gray
Write-Host ""
Write-Host "   Para desinstalar:"
Write-Host "     Ejecuta DESINSTALAR.ps1 como Administrador" -ForegroundColor Yellow
Write-Host "  =================================================" -ForegroundColor Green
Write-Host ""

# Abrir el navegador
Start-Process "http://localhost:$Port/"
Pause
