# =============================================================
#  ACTUALIZAR_SERVIDOR.ps1  —  Dashboard Productividad
#  Ejecutar en el SERVIDOR como Administrador
# =============================================================

$InstallDir = "C:\inetpub\DashProductividad"
$PoolName   = "DashProductividadPool"
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppSource  = Join-Path $ScriptDir "app"
$BackupDir  = Join-Path $InstallDir "_backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Dashboard Productividad - AM COMERCIAL"    -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $AppSource)) {
    Write-Host "ERROR: No se encontro la carpeta 'app\'" -ForegroundColor Red
    Read-Host "Presione Enter para salir"; exit 1
}

Import-Module WebAdministration -ErrorAction SilentlyContinue

# Si no existe el pool, crearlo
if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    Write-Host "Creando App Pool '$PoolName'..." -ForegroundColor Yellow
    New-WebAppPool -Name $PoolName | Out-Null
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedPipelineMode   -Value "Integrated"
    Write-Host "OK - App Pool creado." -ForegroundColor Green
}

# Si no existe la aplicacion virtual, crearla
$AppName = "DashProductividad"
$site    = "Default Web Site"
if (-not (Get-WebApplication -Site $site -Name $AppName -ErrorAction SilentlyContinue)) {
    Write-Host "Creando aplicacion IIS '/$AppName'..." -ForegroundColor Yellow
    if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }
    New-WebApplication -Site $site -Name $AppName -PhysicalPath $InstallDir -ApplicationPool $PoolName | Out-Null
    Write-Host "OK - Aplicacion IIS creada." -ForegroundColor Green
}

# [1] Detener pool
Write-Host "[1/5] Deteniendo App Pool..." -ForegroundColor Yellow
try {
    if ((Get-WebAppPoolState -Name $PoolName -ErrorAction SilentlyContinue).Value -eq "Started") {
        Stop-WebAppPool -Name $PoolName; Start-Sleep -Seconds 3
    }
    Write-Host "      OK" -ForegroundColor Green
} catch { Write-Host "      AVISO: $($_.Exception.Message)" -ForegroundColor Yellow }

# [2] Proteger appsettings.json
$settingsPath = Join-Path $InstallDir "appsettings.json"
$settingsTmp  = Join-Path $env:TEMP "appsettings_prod_backup.json"
$haySettings  = Test-Path $settingsPath
Write-Host "[2/5] Protegiendo configuracion..." -ForegroundColor Yellow
if ($haySettings) {
    Copy-Item $settingsPath $settingsTmp -Force
    Write-Host "      OK - appsettings.json guardado." -ForegroundColor Green
}

# [3] Backup
Write-Host "[3/5] Haciendo backup..." -ForegroundColor Yellow
try {
    if (Test-Path $InstallDir) {
        $items = Get-ChildItem $InstallDir -Exclude "_backup_*" -ErrorAction SilentlyContinue
        if ($items.Count -gt 0) {
            New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
            $items | Copy-Item -Destination $BackupDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "      OK - Backup en $BackupDir" -ForegroundColor Green
        }
    }
} catch { Write-Host "      AVISO: $($_.Exception.Message)" -ForegroundColor Yellow }

# [4] Copiar archivos
Write-Host "[4/5] Copiando archivos nuevos..." -ForegroundColor Yellow
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }
try {
    $cnt = 0
    Get-ChildItem -Path $AppSource -Recurse | ForEach-Object {
        $dest = $_.FullName.Replace($AppSource, $InstallDir)
        if ($_.PSIsContainer) {
            if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
        } else {
            Copy-Item $_.FullName $dest -Force; $cnt++
        }
    }
    Write-Host "      OK - $cnt archivos copiados." -ForegroundColor Green
} catch { Write-Host "      ERROR: $($_.Exception.Message)" -ForegroundColor Red }

# Restaurar appsettings.json del servidor
if ($haySettings -and (Test-Path $settingsTmp)) {
    Copy-Item $settingsTmp $settingsPath -Force
    Remove-Item $settingsTmp -Force -ErrorAction SilentlyContinue
    Write-Host "      OK - Configuracion restaurada." -ForegroundColor Green
}

# Permisos para IIS
$acl = Get-Acl $InstallDir
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "IIS_IUSRS", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($rule)
Set-Acl $InstallDir $acl -ErrorAction SilentlyContinue

# [5] Iniciar pool
Write-Host "[5/5] Iniciando App Pool..." -ForegroundColor Yellow
try {
    Start-WebAppPool -Name $PoolName; Start-Sleep -Seconds 3
    $state = (Get-WebAppPoolState -Name $PoolName -ErrorAction SilentlyContinue).Value
    Write-Host "      OK - Estado: $state" -ForegroundColor Green
} catch { Write-Host "      ERROR: $($_.Exception.Message)" -ForegroundColor Red }

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Instalacion completada!" -ForegroundColor Green
Write-Host ""
Write-Host "  URL: http://amco.fortidyndns.com:86/dashproductividad/" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Read-Host "Presione Enter para cerrar"
