#Requires -RunAsAdministrator
<#
  REPARAR_IIS.ps1  —  Configura IIS para MovilBusinessDashboard
  Ejecutar cuando el pool/app no fueron creados por INSTALAR_IIS.ps1
#>

$SitioIIS   = "Default Web Site"
$AppVirtual = "MovilBusinessDashboard"
$PoolName   = "MovilBusinessDashboardPool"
$InstallDir = "C:\inetpub\MovilBusinessDashboard"
$SourceDir  = Split-Path -Parent $MyInvocation.MyCommand.Path

$ErrorActionPreference = "Stop"

function Write-OK($msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green  }
function Write-Info($msg) { Write-Host "  [..] $msg" -ForegroundColor Gray   }
function Write-Fail($msg) { Write-Host "  [ERROR] $msg" -ForegroundColor Red; Pause; exit 1 }

Clear-Host
Write-Host ""
Write-Host "  =================================================" -ForegroundColor Cyan
Write-Host "   Reparar IIS - MovilBusinessDashboard" -ForegroundColor Cyan
Write-Host "  =================================================" -ForegroundColor Cyan

# Cargar modulo WebAdministration
try {
    Import-Module WebAdministration -ErrorAction Stop
} catch {
    Write-Fail "No se pudo cargar WebAdministration. Verifica que IIS este instalado."
}

# ── Verificar / publicar archivos ─────────────────────────────────────────────
if (-not (Test-Path "$InstallDir\web.config")) {
    Write-Info "No se encontraron archivos en '$InstallDir'. Compilando..."

    $publishTemp = "$env:TEMP\MBDash_pub"
    if (Test-Path $publishTemp) { Remove-Item $publishTemp -Recurse -Force }

    $out = & dotnet publish "$SourceDir\DashVisitas.csproj" -c Release -o $publishTemp --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        Write-Fail "Error al compilar."
    }

    if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }
    Copy-Item "$publishTemp\*" -Destination $InstallDir -Recurse -Force
    Remove-Item $publishTemp -Recurse -Force -ErrorAction SilentlyContinue
    Write-OK "Archivos publicados en '$InstallDir'."
} else {
    Write-OK "Archivos encontrados en '$InstallDir'."
}

# ── Application Pool ──────────────────────────────────────────────────────────
if (Test-Path "IIS:\AppPools\$PoolName") {
    Write-Info "Pool '$PoolName' ya existe. Actualizando configuracion..."
    try { Stop-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Seconds 2
} else {
    New-WebAppPool -Name $PoolName | Out-Null
    Write-OK "Application Pool '$PoolName' creado."
}

Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedPipelineMode"   -Value "Integrated"
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "startMode"             -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "autoStart"             -Value $true
Write-OK "Pool configurado (Sin codigo administrado / Integrado)."

# ── Aplicacion IIS ────────────────────────────────────────────────────────────
$appPath = "IIS:\Sites\$SitioIIS\$AppVirtual"

if (Test-Path $appPath) {
    Write-Info "Aplicacion existente. Actualizando..."
    Set-ItemProperty $appPath -Name "physicalPath"    -Value $InstallDir
    Set-ItemProperty $appPath -Name "applicationPool" -Value $PoolName
} else {
    New-WebApplication -Site $SitioIIS `
                       -Name $AppVirtual `
                       -PhysicalPath $InstallDir `
                       -ApplicationPool $PoolName | Out-Null
    Write-OK "Aplicacion '/$AppVirtual' creada en '$SitioIIS'."
}

# ── Permisos ──────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Path "$InstallDir\logs" -Force | Out-Null

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
try {
    $logRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "IIS AppPool\$PoolName", "Modify",
        "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $acl.SetAccessRule($logRule)
} catch {}
Set-Acl -Path $InstallDir -AclObject $acl
Write-OK "Permisos asignados."

# ── Iniciar y reiniciar ───────────────────────────────────────────────────────
Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
Write-Info "Ejecutando iisreset..."
iisreset /noforce | Out-Null
Write-OK "IIS reiniciado."

Start-Sleep -Seconds 3
$state = (Get-WebAppPoolState -Name $PoolName).Value
Write-OK "Estado del pool: $state"

$ip = (Get-NetIPAddress -AddressFamily IPv4 |
       Where-Object { $_.InterfaceAlias -notlike "*Loopback*" } |
       Select-Object -First 1).IPAddress

Write-Host ""
Write-Host "  =================================================" -ForegroundColor Green
Write-Host "   CONFIGURACION COMPLETADA" -ForegroundColor Green
Write-Host "  =================================================" -ForegroundColor Green
Write-Host ""
Write-Host "   Dashboard disponible en:"
Write-Host "     http://localhost:86/$AppVirtual/" -ForegroundColor White
Write-Host "     http://${ip}:86/$AppVirtual/"     -ForegroundColor White
Write-Host ""
Write-Host "   Si ves 502.5 / 500.30:" -ForegroundColor Yellow
Write-Host "     Verifica que .NET 8 Hosting Bundle este instalado" -ForegroundColor Yellow
Write-Host "     y que el App Pool tenga 'Sin codigo administrado'"  -ForegroundColor Yellow
Write-Host ""
Write-Host "   Logs: $InstallDir\logs\" -ForegroundColor Gray
Write-Host "  =================================================" -ForegroundColor Green
Write-Host ""

Start-Process "http://localhost:86/$AppVirtual/"
Pause
