#Requires -RunAsAdministrator
<#
  INSTALAR_IIS_LOCAL.ps1
  =======================
  Instala IIS con Classic ASP en Windows 10/11 y configura el
  Dashboard de Visitas como aplicacion local para pruebas.

  URL resultante:  http://localhost/DashVisitas/
#>

$NombreApp     = "DashVisitas"
$CarpetaFuente = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Instalador IIS + Classic ASP - Dashboard Visitas" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Carpeta fuente: $CarpetaFuente"
Write-Host "  URL local     : http://localhost/$NombreApp/"
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ── 1. Instalar IIS y Classic ASP ────────────────────────────────────────────
Write-Host "[1/5] Instalando IIS con Classic ASP (puede tardar 2-3 min)..." -ForegroundColor Yellow

$features = @(
    "IIS-WebServerRole",
    "IIS-WebServer",
    "IIS-CommonHttpFeatures",
    "IIS-DefaultDocument",
    "IIS-DirectoryBrowsing",
    "IIS-HttpErrors",
    "IIS-StaticContent",
    "IIS-HttpRedirect",
    "IIS-ApplicationDevelopment",
    "IIS-ASP",
    "IIS-ISAPIExtensions",
    "IIS-ISAPIFilter",
    "IIS-NetFxExtensibility45",
    "IIS-HealthAndDiagnostics",
    "IIS-HttpLogging",
    "IIS-Security",
    "IIS-RequestFiltering",
    "IIS-Performance",
    "IIS-HttpCompressionStatic",
    "IIS-ManagementConsole",
    "IIS-ManagementService"
)

foreach ($f in $features) {
    $result = Enable-WindowsOptionalFeature -Online -FeatureName $f -NoRestart -ErrorAction SilentlyContinue
    if ($result -and $result.RestartNeeded) {
        Write-Host "      AVISO: Se necesita reinicio (continuamos de todas formas)" -ForegroundColor Yellow
    }
}

Write-Host "      IIS instalado OK" -ForegroundColor Green

# ── 2. Cargar modulo de administracion IIS ───────────────────────────────────
Write-Host "[2/5] Cargando modulo WebAdministration..." -ForegroundColor Yellow
Import-Module WebAdministration -ErrorAction Stop
Write-Host "      OK" -ForegroundColor Green

# ── 3. Configurar Classic ASP ─────────────────────────────────────────────────
Write-Host "[3/5] Configurando Classic ASP..." -ForegroundColor Yellow

# Mostrar errores detallados en el navegador (util para desarrollo)
Set-WebConfigurationProperty -pspath "MACHINE/WEBROOT/APPHOST" `
    -filter "system.webServer/asp" `
    -name "scriptErrorSentToBrowser" -value "True"

# UTF-8 para caracteres en espanol
Set-WebConfigurationProperty -pspath "MACHINE/WEBROOT/APPHOST" `
    -filter "system.webServer/asp" `
    -name "codePage" -value 65001

# Lcid espanol
Set-WebConfigurationProperty -pspath "MACHINE/WEBROOT/APPHOST" `
    -filter "system.webServer/asp" `
    -name "lcid" -value 3082

Write-Host "      OK" -ForegroundColor Green

# ── 4. Crear Application Pool y aplicacion web ───────────────────────────────
Write-Host "[4/5] Creando aplicacion en IIS..." -ForegroundColor Yellow

$PoolName = "DashVisitasPool"

# Crear pool si no existe
if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    New-WebAppPool -Name $PoolName | Out-Null
}

# Configurar pool: pipeline Classic, sin .NET gestionado
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\$PoolName" -Name "managedPipelineMode"   -Value "Classic"
Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue

# Crear aplicacion virtual en Default Web Site
$appPath = "IIS:\Sites\Default Web Site\$NombreApp"

if (Test-Path $appPath) {
    Set-ItemProperty $appPath -Name "physicalPath"    -Value $CarpetaFuente
    Set-ItemProperty $appPath -Name "applicationPool" -Value $PoolName
    Write-Host "      Aplicacion actualizada." -ForegroundColor Gray
} else {
    New-WebApplication -Site "Default Web Site" `
                       -Name $NombreApp `
                       -PhysicalPath $CarpetaFuente `
                       -ApplicationPool $PoolName | Out-Null
    Write-Host "      Aplicacion creada." -ForegroundColor Green
}

# Pagina de inicio predeterminada
Set-WebConfigurationProperty `
    -pspath "IIS:\Sites\Default Web Site\$NombreApp" `
    -filter "system.webServer/defaultDocument/files" `
    -name "." -value @{value="index.asp"} `
    -ErrorAction SilentlyContinue

Write-Host "      OK" -ForegroundColor Green

# ── 5. Permisos y arranque ────────────────────────────────────────────────────
Write-Host "[5/5] Permisos y arranque de IIS..." -ForegroundColor Yellow

# Dar lectura a IIS sobre la carpeta (OneDrive puede tener restricciones)
$acl = Get-Acl $CarpetaFuente
foreach ($account in @("IUSR", "IIS_IUSRS")) {
    try {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $account, "ReadAndExecute",
            "ContainerInherit,ObjectInherit", "None", "Allow"
        )
        $acl.SetAccessRule($rule)
    } catch { }
}
Set-Acl -Path $CarpetaFuente -AclObject $acl

# Iniciar IIS
Start-Service W3SVC -ErrorAction SilentlyContinue
iisreset /noforce 2>$null | Out-Null

Write-Host "      OK" -ForegroundColor Green

# ── Resumen final ─────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  LISTO - IIS instalado y configurado" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Abre esto en tu navegador:"
Write-Host ""
Write-Host "    http://localhost/$NombreApp/" -ForegroundColor White
Write-Host ""
Write-Host "  Primero verifica las columnas de la tabla Empresa:"
Write-Host "    http://localhost/$NombreApp/api/empresa-cols.asp" -ForegroundColor Yellow
Write-Host "  (borrar ese archivo despues)"
Write-Host ""
Write-Host "  Si ves error 500 revisa:"
Write-Host "    Inicio > Administrador de IIS > Default Web Site"
Write-Host "    > $NombreApp > Handler Mappings > ASP debe estar activo"
Write-Host ""
Write-Host "  Para abrir el Administrador de IIS:"
Write-Host "    Win+R > inetmgr > Enter" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Pause
