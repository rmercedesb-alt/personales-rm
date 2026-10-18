<#
  CREAR_INSTALABLE_STANDALONE.ps1  —  Instalable IIS AUTONOMO (self-contained, win-x64)
  ====================================================================================
  Igual que CREAR_INSTALABLE_IIS.ps1 pero publica la app CON su propio runtime .NET
  adentro (self-contained). Ventaja: el servidor NO necesita tener .NET instalado
  (ni .NET 8 ni 10). Solo requiere el Modulo ASP.NET Core en IIS (lo instala cualquier
  Hosting Bundle). Ideal para clientes nuevos donde el runtime del servidor da problemas
  (HTTP 500.30 por version de .NET incompatible).

  Contrapartida: pesa mas (~115 MB) porque incluye el runtime.

  Produce en Instalables\:
     DashSmart_STANDALONE_v<version>_winx64_<fecha>\  (app + scripts) + su .zip
#>
param(
    # Opcional: ruta del license.lic YA emitido para un cliente (MfSoft License). Se valida y se
    # incluye junto a INSTALAR_IIS/ACTUALIZAR_IIS (los scripts lo toman solos) y el paquete lleva
    # el nombre del cliente. Sin este parametro el paquete sale sin licencia (generico).
    [string]$Licencia = ""
)
$ErrorActionPreference = "Stop"
$Proj = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Proj

$csproj = Get-Content "$Proj\DashSmart.csproj" -Raw
if ($csproj -match "<Version>\s*([^<]+?)\s*</Version>") { $ver = $Matches[1].Trim() } else { $ver = "0.0.0" }
$stamp   = Get-Date -Format "yyyy-MM-dd_HHmm"

$licSufijo = ""
if ($Licencia) {
    $Licencia = $Licencia.Trim('"')
    if (-not (Test-Path $Licencia)) { Write-Host "  ERROR: no existe la licencia '$Licencia'." -ForegroundColor Red; exit 1 }
    try {
        $lj = Get-Content $Licencia -Raw | ConvertFrom-Json
        $lp = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($lj.payload)) | ConvertFrom-Json
        if (-not $lj.signature -or -not $lp.expira) { throw "incompleta" }
    } catch { Write-Host "  ERROR: '$Licencia' no parece un license.lic valido." -ForegroundColor Red; exit 1 }
    $nom = ($lp.cliente -replace '[^A-Za-z0-9]+','_').Trim('_'); if (-not $nom) { $nom = "cliente" }
    $licSufijo = "_$nom"
    $nAct = @($lp.modulos.PSObject.Properties | Where-Object { $_.Value -eq $true }).Count
    Write-Host "   Licencia: $($lp.cliente) - vence $($lp.expira) - $nAct de 9 modulos" -ForegroundColor Cyan
    if ([datetime]$lp.expira -lt (Get-Date).Date) { Write-Host "   [!] ESA LICENCIA YA ESTA VENCIDA." -ForegroundColor Yellow }
}
$pkgName = "DashSmart_STANDALONE_v${ver}_winx64_$stamp$licSufijo"
$outRoot = "$Proj\Instalables"
$pkgDir  = "$outRoot\$pkgName"

Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Cyan
Write-Host "   Instalable AUTONOMO (self-contained)  -  v$ver  win-x64" -ForegroundColor Cyan
Write-Host "  ======================================================" -ForegroundColor Cyan

if (-not (Test-Path $outRoot)) { New-Item -ItemType Directory -Path $outRoot | Out-Null }
if (Test-Path $pkgDir)  { Remove-Item $pkgDir -Recurse -Force }
if (Test-Path "$pkgDir.zip") { Remove-Item "$pkgDir.zip" -Force }
New-Item -ItemType Directory -Path $pkgDir | Out-Null

# 1) Publicar SELF-CONTAINED win-x64
Write-Host "   [1/4] dotnet publish -c Release -r win-x64 --self-contained ..." -ForegroundColor Yellow
$pub = & dotnet publish "$Proj\DashSmart.csproj" -c Release -r win-x64 --self-contained true -o "$pkgDir\app" --nologo 2>&1
if ($LASTEXITCODE -ne 0) { $pub | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }; Write-Host "  ERROR al publicar." -ForegroundColor Red; Pause; exit 1 }
if (-not (Test-Path "$pkgDir\app\DashSmart.exe")) { Write-Host "  ERROR: no se genero DashSmart.exe (self-contained)." -ForegroundColor Red; Pause; exit 1 }
if (-not (Test-Path "$pkgDir\app\web.config"))     { Write-Host "  ERROR: no se genero web.config." -ForegroundColor Red; Pause; exit 1 }
Write-Host "         [OK] publicado (con runtime propio)." -ForegroundColor Green
# La licencia es POR CLIENTE: el paquete nunca debe traer una (el csproj ya la excluye; esto es un seguro extra).
if (Test-Path "$pkgDir\app\App_Data\license.lic") { Remove-Item "$pkgDir\app\App_Data\license.lic" -Force; Write-Host "         [!] Se encontro una license.lic en el publish y se ELIMINO del paquete." -ForegroundColor Yellow }
else { Write-Host "         [OK] la app publicada no lleva licencia embebida." -ForegroundColor Green }

# 2) Scripts + metadatos (los mismos INSTALAR/ACTUALIZAR sirven: copian app\ y manejan el Pool)
Write-Host "   [2/4] Copiando scripts y metadatos ..." -ForegroundColor Yellow
Copy-Item "$Proj\deploy_iis\INSTALAR_IIS.ps1"   "$pkgDir\INSTALAR_IIS.ps1"   -Force
Copy-Item "$Proj\deploy_iis\ACTUALIZAR_IIS.ps1" "$pkgDir\ACTUALIZAR_IIS.ps1" -Force
if ($Licencia) { Copy-Item $Licencia "$pkgDir\license.lic" -Force; Write-Host "         [OK] license.lic de '$($lp.cliente)' incluida junto a los scripts." -ForegroundColor Green }
[System.IO.File]::WriteAllText("$pkgDir\VERSION.txt", $ver)
if (Test-Path "$Proj\CHANGELOG.md") { Copy-Item "$Proj\CHANGELOG.md" "$pkgDir\CHANGELOG.md" -Force }

$leeme = @"
==========================================================
 MovilBusiness Dashboard Smart  -  version $ver  (AUTONOMO / self-contained, win-x64)
 Generado: $(Get-Date -Format 'yyyy-MM-dd HH:mm')  -  AM Comercial, S.A.S.
==========================================================

QUE ES:  Esta version incluye SU PROPIO runtime de .NET adentro.
         El servidor NO necesita tener instalado .NET 8 ni .NET 10.
         Solo requiere el "Modulo ASP.NET Core" en IIS (lo instala cualquier
         Hosting Bundle de .NET 8/9/10). Pesa mas (~115 MB) por llevar el runtime.

CUANDO USARLA:  cuando el instalable normal da HTTP 500.30 en un cliente nuevo
                por incompatibilidad de version de .NET del servidor.

--------------------------------------------------------------------
 INSTALACION NUEVA
--------------------------------------------------------------------
  1. Copia/descomprime esta carpeta en el servidor.
  2. Click derecho en INSTALAR_IIS.ps1 -> Ejecutar con PowerShell (como Administrador).
  3. Abre:  http://<servidor>/movilbusinessdashboardSmart/version   (debe decir $ver)

--------------------------------------------------------------------
 ACTUALIZAR una instalacion existente (a esta version)
--------------------------------------------------------------------
  1. Copia/descomprime este paquete en el servidor.
  2. Click derecho en ACTUALIZAR_IIS.ps1 -> Ejecutar con PowerShell (como Administrador).
     -> Reemplaza binarios y CONSERVA tu appsettings.json (conexion + PathBase)
        y tu licencia (App_Data\license.lic).

--------------------------------------------------------------------
 LICENCIA  (cada cliente tiene la suya; este paquete NO la incluye)
--------------------------------------------------------------------
  - El administrador del sistema te entrega un archivo license.lic para ESTE servidor.
  - Instalacion nueva: pon license.lic junto a INSTALAR_IIS.ps1 (lo toma solo) o pega su
    ruta cuando el script la pida. Tambien puedes copiarlo despues a:
        C:\inetpub\MovilBusinessDashboardSmart\App_Data\license.lic
  - Actualizacion: si el servidor ya tiene licencia, se CONSERVA; si no, el script la pide.
  - Para RENOVAR o cambiar modulos: el administrador te manda un license.lic nuevo y lo
    copias encima en App_Data (se detecta solo en ~10 min, sin reiniciar).
  - Sin licencia valida el sistema muestra "Sistema no disponible".

SI AUN ASI DA 500.30/500.31/500.19:
  - Falta el Modulo ASP.NET Core en IIS. Instala CUALQUIER "ASP.NET Core Hosting Bundle"
    (de https://dotnet.microsoft.com/download/dotnet) y reinicia IIS:
        net stop was /y   &&   net start w3svc
  - Para ver el error exacto: en web.config pon stdoutLogEnabled="true", crea la carpeta
    .\logs, recarga, y lee .\logs\stdout_*.log  (luego vuelve a poner "false").
==========================================================
"@
$leeme | Set-Content "$pkgDir\LEEME.txt" -Encoding UTF8
Write-Host "         [OK] scripts, VERSION.txt, CHANGELOG.md, LEEME.txt" -ForegroundColor Green

# 3) Comprimir
Write-Host "   [3/4] Comprimiendo ZIP ..." -ForegroundColor Yellow
Compress-Archive -Path $pkgDir -DestinationPath "$pkgDir.zip" -Force
$mb = [math]::Round((Get-Item "$pkgDir.zip").Length / 1MB, 1)
Write-Host "   [4/4] Listo." -ForegroundColor Yellow
Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "   INSTALABLE AUTONOMO CREADO  -  v$ver  ($mb MB)" -ForegroundColor Green
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "   ZIP: $pkgDir.zip   <-- este llevas al servidor" -ForegroundColor White
Write-Host ""
