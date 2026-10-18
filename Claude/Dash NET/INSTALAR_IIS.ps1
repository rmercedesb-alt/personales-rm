#Requires -RunAsAdministrator
<#
  INSTALAR_IIS.ps1  —  Dashboard Visitas en IIS  (ASP.NET Core 8)
  =================================================================
  Este script hace TODO de forma automatica:
    1. Verifica / instala .NET 8 Hosting Bundle  (Runtime + Modulo IIS)
    2. Habilita IIS y los componentes necesarios
    3. Compila y publica la aplicacion
    4. Crea Application Pool y aplicacion en IIS
    5. Asigna permisos y reinicia IIS

  URL resultante:  http://<servidor>/DashVisitas/
#>

# ════════════════════════════════════════════════════════════
#   CONFIGURACION  (cambia solo si es necesario)
# ════════════════════════════════════════════════════════════
$SitioIIS   = "Default Web Site"                  # Sitio IIS donde se montara
$AppVirtual = "MovilBusinessDashboard"            # Ruta virtual -> /MovilBusinessDashboard/
$PoolName   = "MovilBusinessDashboardPool"        # Nombre del Application Pool
$InstallDir = "C:\inetpub\MovilBusinessDashboard" # Carpeta fisica de la app
$SourceDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
# ════════════════════════════════════════════════════════════

$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"

# ── Helpers de presentacion ───────────────────────────────────────────────────
function Write-Banner($msg) {
    Write-Host ""
    Write-Host "  $("=" * 54)" -ForegroundColor Cyan
    Write-Host "   $msg" -ForegroundColor Cyan
    Write-Host "  $("=" * 54)" -ForegroundColor Cyan
}
function Write-Step($n, $total, $msg) {
    Write-Host ""
    Write-Host "  [$n/$total] $msg" -ForegroundColor Yellow
}
function Write-OK($msg)   { Write-Host "        [OK] $msg" -ForegroundColor Green  }
function Write-Info($msg) { Write-Host "        [..] $msg" -ForegroundColor Gray   }
function Write-Warn($msg) { Write-Host "       [!] $msg"   -ForegroundColor Yellow }
function Write-Fail($msg) {
    Write-Host ""
    Write-Host "  [ERROR] $msg" -ForegroundColor Red
    Write-Host ""
    Pause
    exit 1
}

Clear-Host
Write-Banner "Instalador Dashboard Visitas - IIS (ASP.NET Core 8)"
Write-Host "   Sitio IIS  : $SitioIIS"
Write-Host "   Ruta web   : /$AppVirtual/"
Write-Host "   Carpeta    : $InstallDir"
Write-Host "   Pool       : $PoolName"
Write-Host "  $("=" * 54)" -ForegroundColor Cyan

# ── PASO 1: Verificar / instalar .NET 8 Hosting Bundle ───────────────────────
Write-Step 1 6 "Verificando .NET 8 Hosting Bundle..."

# Detectar si el ASP.NET Core Module esta instalado
$ancmKey  = "HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS 10.0 Express\ASP.NET Core Module V2"
$ancmKey2 = "HKLM:\SOFTWARE\Microsoft\IIS Extensions\ASP.NET Core Module V2"
$ancmOk   = (Test-Path $ancmKey) -or (Test-Path $ancmKey2)

# Detectar runtime de .NET 8
$runtimeOk = $false
try {
    $runtimes = & dotnet --list-runtimes 2>&1
    if ($runtimes -match "Microsoft\.AspNetCore\.App 8\.") { $runtimeOk = $true }
} catch {}

if ($runtimeOk -and $ancmOk) {
    Write-OK ".NET 8 Hosting Bundle ya instalado."
} else {
    if (-not $runtimeOk) { Write-Info ".NET 8 Runtime no encontrado." }
    if (-not $ancmOk)    { Write-Info "ASP.NET Core Module para IIS no encontrado." }
    Write-Info "Descargando .NET 8 Hosting Bundle (~100 MB)..."

    $bundlePath = "$env:TEMP\dotnet-hosting-win.exe"
    try {
        Invoke-WebRequest -Uri "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe" `
                          -OutFile $bundlePath -UseBasicParsing
    } catch {
        Write-Fail "No se pudo descargar el Hosting Bundle.`n  Descargalo manualmente desde:`n  https://dotnet.microsoft.com/download/dotnet/8.0`n  (Seccion 'Hosting Bundle' de Windows)"
    }

    Write-Info "Instalando Hosting Bundle (puede tardar 2-3 min)..."
    Start-Process -FilePath $bundlePath -ArgumentList "/quiet /norestart" -Wait
    Remove-Item $bundlePath -Force -ErrorAction SilentlyContinue

    # Recargar PATH
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path","User")

    Write-OK ".NET 8 Hosting Bundle instalado."
}

# Verificar que dotnet este disponible
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Fail "'dotnet' no esta en el PATH.`n  Reinicia y vuelve a correr el instalador."
}

# ── PASO 2: Habilitar IIS y componentes requeridos ────────────────────────────
Write-Step 2 6 "Habilitando IIS y componentes requeridos..."

$iisFeatures = @(
    "IIS-WebServerRole",
    "IIS-WebServer",
    "IIS-CommonHttpFeatures",
    "IIS-DefaultDocument",
    "IIS-HttpErrors",
    "IIS-StaticContent",
    "IIS-ApplicationDevelopment",
    "IIS-ISAPIExtensions",
    "IIS-ISAPIFilter",
    "IIS-NetFxExtensibility45",
    "IIS-HealthAndDiagnostics",
    "IIS-HttpLogging",
    "IIS-Security",
    "IIS-RequestFiltering",
    "IIS-Performance",
    "IIS-ManagementConsole"
)

foreach ($f in $iisFeatures) {
    Enable-WindowsOptionalFeature -Online -FeatureName $f -NoRestart `
        -ErrorAction SilentlyContinue | Out-Null
}

Write-OK "IIS habilitado."

# Cargar modulo de administracion
try {
    Import-Module WebAdministration -ErrorAction Stop
} catch {
    Write-Fail "No se pudo cargar el modulo WebAdministration.`n  Asegurate de que IIS esta instalado y reinicia si es necesario."
}

Write-OK "Modulo WebAdministration cargado."

# ── PASO 3: Compilar y publicar la aplicacion ─────────────────────────────────
Write-Step 3 6 "Compilando y publicando la aplicacion..."

$publishTemp = "$env:TEMP\DashVisitas_pub"
if (Test-Path $publishTemp) { Remove-Item $publishTemp -Recurse -Force }

Write-Info "dotnet publish -c Release..."
$output = & dotnet publish "$SourceDir\DashVisitas.csproj" `
              -c Release `
              -o $publishTemp `
              --nologo 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    $output | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Fail "Error al compilar. Revisa los mensajes de arriba."
}
Write-OK "Compilacion exitosa."

# Verificar que se genero web.config (creado automaticamente por dotnet publish)
if (-not (Test-Path "$publishTemp\web.config")) {
    Write-Fail "No se genero web.config. Algo fallo en el publish."
}

# ── PASO 4: Copiar archivos a la carpeta de IIS ───────────────────────────────
Write-Step 4 6 "Copiando archivos a '$InstallDir'..."

# Detener el pool si esta corriendo (para liberar archivos bloqueados)
if (Test-Path "IIS:\AppPools\$PoolName") {
    Write-Info "Deteniendo Application Pool anterior..."
    try { Stop-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Seconds 2
}

# Crear o limpiar directorio destino
if (Test-Path $InstallDir) {
    Get-ChildItem $InstallDir -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Copy-Item "$publishTemp\*" -Destination $InstallDir -Recurse -Force
Remove-Item $publishTemp -Recurse -Force -ErrorAction SilentlyContinue

Write-OK "Archivos copiados."

# ── PASO 5: Configurar Application Pool y aplicacion en IIS ──────────────────
Write-Step 5 6 "Configurando IIS..."

# -- Application Pool --
if (Test-Path "IIS:\AppPools\$PoolName") {
    Write-Info "Actualizando Application Pool existente..."
} else {
    New-WebAppPool -Name $PoolName | Out-Null
    Write-Info "Application Pool '$PoolName' creado."
}

# ASP.NET Core requiere:
#   - managedRuntimeVersion = ""  (Sin .NET CLR gestionado)
#   - managedPipelineMode   = "Integrated"
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedPipelineMode"   -Value "Integrated"
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "startMode"             -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "autoStart"             -Value $true

# Reinicio automatico ante fallos
$failure = @{
    resetInterval = [TimeSpan]"1.00:00:00"
    firstFailure  = "Restart"
    secondFailure = "Restart"
    thirdFailure  = "Restart"
}
Set-WebConfiguration -Filter "system.applicationHost/applicationPools/add[@name='$PoolName']/failure" `
    -Value $failure -ErrorAction SilentlyContinue

Write-OK "Application Pool configurado (No Managed Code / Integrated)."

# -- Aplicacion IIS --
$appIisPath = "IIS:\Sites\$SitioIIS\$AppVirtual"

if (Test-Path $appIisPath) {
    Write-Info "Aplicacion existente encontrada. Actualizando..."
    Set-ItemProperty $appIisPath -Name "physicalPath"    -Value $InstallDir
    Set-ItemProperty $appIisPath -Name "applicationPool" -Value $PoolName
} else {
    New-WebApplication -Site $SitioIIS `
                       -Name $AppVirtual `
                       -PhysicalPath $InstallDir `
                       -ApplicationPool $PoolName | Out-Null
    Write-OK "Aplicacion IIS '/$AppVirtual' creada."
}

# Pagina de inicio
try {
    Set-WebConfigurationProperty `
        -pspath "IIS:\Sites\$SitioIIS\$AppVirtual" `
        -filter "system.webServer/defaultDocument/files" `
        -name "." -value @{value="index.html"} -ErrorAction SilentlyContinue
} catch {}

Write-OK "Aplicacion IIS configurada."

# ── PASO 6: Permisos, reinicio IIS y resultado ────────────────────────────────
Write-Step 6 6 "Permisos, reinicio IIS y verificacion final..."

# Permisos de lectura/ejecucion para IIS
$acl = Get-Acl $InstallDir
foreach ($account in @("IUSR", "IIS_IUSRS")) {
    try {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $account, "ReadAndExecute",
            "ContainerInherit,ObjectInherit", "None", "Allow"
        )
        $acl.SetAccessRule($rule)
    } catch {}
}
# El pool necesita modificar la carpeta de logs
$appPoolIdentity = "IIS AppPool\$PoolName"
try {
    $logRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $appPoolIdentity, "Modify",
        "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $acl.SetAccessRule($logRule)
} catch {}
Set-Acl -Path $InstallDir -AclObject $acl

Write-OK "Permisos asignados."

# Crear carpeta de logs (stdoutLogFile en web.config)
New-Item -ItemType Directory -Path "$InstallDir\logs" -Force | Out-Null

# Iniciar pool y reiniciar IIS
Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
Start-Service W3SVC -ErrorAction SilentlyContinue
Write-Info "Ejecutando iisreset..."
$iisResult = iisreset /noforce 2>&1
Write-OK "IIS reiniciado."

# ── Verificacion final ────────────────────────────────────────────────────────
Start-Sleep -Seconds 3
$poolState = (Get-WebAppPoolState -Name $PoolName).Value
if ($poolState -eq "Started") {
    Write-OK "Application Pool '$PoolName' esta corriendo."
} else {
    Write-Warn "Pool en estado: $poolState  (puede tardar unos segundos en arrancar)"
}

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
       Where-Object { $_.InterfaceAlias -notlike "*Loopback*" } |
       Select-Object -First 1).IPAddress

Write-Host ""
Write-Host "  $("=" * 54)" -ForegroundColor Green
Write-Host "   INSTALACION COMPLETADA" -ForegroundColor Green
Write-Host "  $("=" * 54)" -ForegroundColor Green
Write-Host ""
Write-Host "   Dashboard disponible en:"
Write-Host "     http://localhost/$AppVirtual/"   -ForegroundColor White
Write-Host "     http://$ip/$AppVirtual/"         -ForegroundColor White
Write-Host ""
Write-Host "   Para administrar:"
Write-Host "     Win+R  ->  inetmgr  (Administrador de IIS)"
Write-Host "     Sitio  ->  $SitioIIS -> $AppVirtual"
Write-Host ""
Write-Host "   Si ves error 502 / 500.30:"
Write-Host "     Revisa que el .NET 8 Hosting Bundle este instalado" -ForegroundColor Yellow
Write-Host "     y que el App Pool tenga 'Sin codigo administrado'"  -ForegroundColor Yellow
Write-Host ""
Write-Host "   Logs de la app:"
Write-Host "     $InstallDir\logs\" -ForegroundColor Gray
Write-Host "     Visor de eventos -> Registros de Windows -> Aplicacion" -ForegroundColor Gray
Write-Host "  $("=" * 54)" -ForegroundColor Green
Write-Host ""

Start-Process "http://localhost/$AppVirtual/"
Pause
