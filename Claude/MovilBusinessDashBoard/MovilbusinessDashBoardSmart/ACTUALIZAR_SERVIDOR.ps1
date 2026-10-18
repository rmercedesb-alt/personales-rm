# =============================================================
#  ACTUALIZAR_SERVIDOR.ps1
#  Ejecutar en el SERVIDOR como Administrador
#  Actualiza los archivos de DashVisitas en IIS
# =============================================================

$InstallDir = "C:\inetpub\MovilBusinessDashboard"
$PoolName   = "MovilBusinessDashboardPool"
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppSource  = Join-Path $ScriptDir "app"
$BackupDir  = Join-Path $InstallDir "_backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   Actualizacion DashVisitas - AM COMERCIAL" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# --- Verificar carpeta de fuente ---
if (-not (Test-Path $AppSource)) {
    Write-Host "ERROR: No se encontro la carpeta 'app\' junto a este script." -ForegroundColor Red
    Write-Host "       Asegurese de copiar TODO el contenido de la carpeta" -ForegroundColor Red
    Write-Host "       DEPLOY_DashVisitas al servidor y ejecutar desde alli." -ForegroundColor Red
    Write-Host ""
    Read-Host "Presione Enter para salir"
    exit 1
}

# --- Verificar/crear directorio de instalacion ---
if (-not (Test-Path $InstallDir)) {
    Write-Host "AVISO: No existe $InstallDir - Creando..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# --- Importar modulo IIS ---
Import-Module WebAdministration -ErrorAction SilentlyContinue

# =============================================================
# PASO 1: Detener App Pool
# =============================================================
Write-Host "[1/5] Deteniendo App Pool '$PoolName'..." -ForegroundColor Yellow
try {
    $poolState = (Get-WebAppPoolState -Name $PoolName -ErrorAction SilentlyContinue).Value
    if ($poolState -eq "Started") {
        Stop-WebAppPool -Name $PoolName
        Start-Sleep -Seconds 3
        Write-Host "      OK - App Pool detenido." -ForegroundColor Green
    } else {
        Write-Host "      App Pool ya estaba detenido (estado: $poolState)" -ForegroundColor Green
    }
} catch {
    Write-Host "      AVISO: $($_.Exception.Message)" -ForegroundColor Yellow
}

# =============================================================
# PASO 2: Guardar backup de appsettings.json del servidor
#         (contiene la cadena de conexion de produccion)
# =============================================================
$settingsOrig   = Join-Path $InstallDir "appsettings.json"
$settingsTmp    = Join-Path $env:TEMP   "appsettings_dash_backup.json"
$haySettingsOld = Test-Path $settingsOrig

Write-Host "[2/5] Protegiendo configuracion existente..." -ForegroundColor Yellow
if ($haySettingsOld) {
    Copy-Item $settingsOrig $settingsTmp -Force
    Write-Host "      OK - appsettings.json guardado temporalmente." -ForegroundColor Green
} else {
    Write-Host "      Primera instalacion - se usara el appsettings.json del paquete." -ForegroundColor Green
}

# =============================================================
# PASO 3: Hacer backup de archivos actuales
# =============================================================
Write-Host "[3/5] Haciendo backup de version anterior..." -ForegroundColor Yellow
try {
    if ((Get-ChildItem $InstallDir -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
        Get-ChildItem -Path $InstallDir -Exclude "_backup_*" |
            Copy-Item -Destination $BackupDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "      OK - Backup guardado en: $BackupDir" -ForegroundColor Green
    } else {
        Write-Host "      Sin archivos previos, se omite backup." -ForegroundColor Green
    }
} catch {
    Write-Host "      AVISO backup: $($_.Exception.Message)" -ForegroundColor Yellow
}

# =============================================================
# PASO 4: Copiar nuevos archivos
# =============================================================
Write-Host "[4/5] Copiando archivos nuevos..." -ForegroundColor Yellow
try {
    $count = 0
    Get-ChildItem -Path $AppSource -Recurse | ForEach-Object {
        $dest = $_.FullName.Replace($AppSource, $InstallDir)
        if ($_.PSIsContainer) {
            if (-not (Test-Path $dest)) {
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
            }
        } else {
            Copy-Item $_.FullName $dest -Force
            $count++
        }
    }
    Write-Host "      OK - $count archivos copiados." -ForegroundColor Green
} catch {
    Write-Host "      ERROR al copiar: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "      Intentando continuar de todos modos..." -ForegroundColor Yellow
}

# --- Restaurar appsettings.json original del servidor ---
if ($haySettingsOld -and (Test-Path $settingsTmp)) {
    Copy-Item $settingsTmp $settingsOrig -Force
    Remove-Item $settingsTmp -Force -ErrorAction SilentlyContinue
    Write-Host "      OK - Configuracion del servidor restaurada." -ForegroundColor Green
}

# =============================================================
# PASO 5: Iniciar App Pool
# =============================================================
Write-Host "[5/5] Iniciando App Pool '$PoolName'..." -ForegroundColor Yellow
try {
    Start-WebAppPool -Name $PoolName
    Start-Sleep -Seconds 3
    $estado = (Get-WebAppPoolState -Name $PoolName -ErrorAction SilentlyContinue).Value
    if ($estado -eq "Started") {
        Write-Host "      OK - App Pool corriendo." -ForegroundColor Green
    } else {
        Write-Host "      AVISO - Estado del pool: $estado" -ForegroundColor Yellow
    }
} catch {
    Write-Host "      ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "      Intente iniciar el pool manualmente en el IIS Manager." -ForegroundColor Yellow
}

# =============================================================
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   Actualizacion completada exitosamente!" -ForegroundColor Green
Write-Host ""
Write-Host "   URL del sistema:" -ForegroundColor White
Write-Host "   http://amco.fortidyndns.com:86/movilbusinessdashboard/" -ForegroundColor Cyan
Write-Host ""
Write-Host "   Si algo no funciona, el backup esta en:" -ForegroundColor White
Write-Host "   $BackupDir" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Read-Host "Presione Enter para cerrar"
