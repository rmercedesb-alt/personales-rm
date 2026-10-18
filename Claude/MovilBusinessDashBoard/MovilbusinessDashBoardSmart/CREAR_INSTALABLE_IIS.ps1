<#
  CREAR_INSTALABLE_IIS.ps1  —  Genera el instalable IIS versionado
  ================================================================
  Se ejecuta en la PC de DESARROLLO (con el SDK .NET 8). Produce en Instalables\ :

     DashSmart_IIS_v<version>_<fecha>\        (carpeta lista)
        app\                (binarios publicados, incluye wwwroot y presentacion)
        INSTALAR_IIS.ps1    (instalacion nueva)
        ACTUALIZAR_IIS.ps1  (actualiza preservando appsettings.json)
        VERSION.txt
        CHANGELOG.md
        LEEME.txt
     DashSmart_IIS_v<version>_<fecha>.zip     (lo mismo comprimido -> esto llevas al servidor)

  La <version> sale de <Version> en DashSmart.csproj. Subela con cada release.
  Cada ZIP es una FOTO inmutable de esa version (sirve para instalar o para volver atras).
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

# 1) Leer version del csproj
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
$pkgName = "DashSmart_IIS_v${ver}_$stamp$licSufijo"
$outRoot = "$Proj\Instalables"
$pkgDir  = "$outRoot\$pkgName"

Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Cyan
Write-Host "   Instalable IIS  -  Dashboard Smart  v$ver" -ForegroundColor Cyan
Write-Host "  ======================================================" -ForegroundColor Cyan
Write-Host "   Paquete: $pkgName"

if (-not (Test-Path $outRoot)) { New-Item -ItemType Directory -Path $outRoot | Out-Null }
if (Test-Path $pkgDir)  { Remove-Item $pkgDir -Recurse -Force }
if (Test-Path "$pkgDir.zip") { Remove-Item "$pkgDir.zip" -Force }
New-Item -ItemType Directory -Path $pkgDir | Out-Null

# 2) Publicar la app (Release) dentro de \app
Write-Host ""
Write-Host "   [1/4] dotnet publish -c Release ..." -ForegroundColor Yellow
$pub = & dotnet publish "$Proj\DashSmart.csproj" -c Release -o "$pkgDir\app" --nologo 2>&1
if ($LASTEXITCODE -ne 0) { $pub | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }; Write-Host "  ERROR al publicar." -ForegroundColor Red; Pause; exit 1 }
if (-not (Test-Path "$pkgDir\app\web.config"))     { Write-Host "  ERROR: no se genero web.config." -ForegroundColor Red; Pause; exit 1 }
if (-not (Test-Path "$pkgDir\app\DashSmart.dll"))  { Write-Host "  ERROR: no se genero DashSmart.dll." -ForegroundColor Red; Pause; exit 1 }
Write-Host "         [OK] publicado." -ForegroundColor Green
# La licencia es POR CLIENTE: el paquete nunca debe traer una (el csproj ya la excluye; esto es un seguro extra).
if (Test-Path "$pkgDir\app\App_Data\license.lic") { Remove-Item "$pkgDir\app\App_Data\license.lic" -Force; Write-Host "         [!] Se encontro una license.lic en el publish y se ELIMINO del paquete." -ForegroundColor Yellow }
else { Write-Host "         [OK] la app publicada no lleva licencia embebida." -ForegroundColor Green }

# 3) Copiar scripts + metadatos
Write-Host "   [2/4] Copiando scripts y metadatos ..." -ForegroundColor Yellow
Copy-Item "$Proj\deploy_iis\INSTALAR_IIS.ps1"   "$pkgDir\INSTALAR_IIS.ps1"   -Force
Copy-Item "$Proj\deploy_iis\ACTUALIZAR_IIS.ps1" "$pkgDir\ACTUALIZAR_IIS.ps1" -Force
if ($Licencia) { Copy-Item $Licencia "$pkgDir\license.lic" -Force; Write-Host "         [OK] license.lic de '$($lp.cliente)' incluida junto a los scripts." -ForegroundColor Green }
[System.IO.File]::WriteAllText("$pkgDir\VERSION.txt", $ver)   # sin BOM
if (Test-Path "$Proj\CHANGELOG.md") { Copy-Item "$Proj\CHANGELOG.md" "$pkgDir\CHANGELOG.md" -Force }

$leeme = @"
==========================================================
 MovilBusiness Dashboard Smart  -  version $ver
 Generado: $(Get-Date -Format 'yyyy-MM-dd HH:mm')  -  AM Comercial, S.A.S.
==========================================================

CONTENIDO:  Menu + Visitas + Productividad + Torre de Control + Presentacion ejecutiva

REQUISITO EN EL SERVIDOR:  ASP.NET Core Hosting Bundle de .NET 10 o superior
                           (el build apunta a net10.0 con roll-forward solo HACIA ARRIBA;
                           YA NO corre en servidores que solo tengan .NET 8 o 9).
                           Si el servidor no tiene .NET 10, usa el instalable STANDALONE
                           en su lugar (trae su propio runtime, no depende del servidor).
                           Descarga: https://dotnet.microsoft.com/download/dotnet  (elige "Hosting Bundle").
                           Tras instalarlo, reinicia IIS:  net stop was /y  &&  net start w3svc

--------------------------------------------------------------------
 INSTALACION NUEVA
--------------------------------------------------------------------
  1. Copia esta carpeta al servidor (o descomprime el ZIP).
  2. Click derecho en INSTALAR_IIS.ps1  ->  Ejecutar con PowerShell (como Administrador).
  3. Al terminar abre:  http://<servidor>/movilbusinessdashboardSmart/
  4. Verifica la cadena de conexion en:
     C:\inetpub\MovilBusinessDashboardSmart\appsettings.json

--------------------------------------------------------------------
 ACTUALIZAR una instalacion existente (a esta version)
--------------------------------------------------------------------
  1. Copia/descomprime este paquete en el servidor.
  2. Click derecho en ACTUALIZAR_IIS.ps1  ->  Ejecutar con PowerShell (como Administrador).
     -> Reemplaza los binarios y CONSERVA tu appsettings.json (conexion + PathBase)
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

--------------------------------------------------------------------
 RUTAS
--------------------------------------------------------------------
  /                         Dashboard Visitas
  /productividad            Dashboard Productividad
  /torre                    Torre de Control
  /menu                     Menu principal
  /presentacion-torre.html  Presentacion ejecutiva
  /version                  Version desplegada (JSON) -> para confirmar la actualizacion

Ruta virtual IIS por defecto: /movilbusinessdashboardSmart
(se puede cambiar en la seccion CONFIGURACION de los scripts .ps1)
==========================================================
"@
$leeme | Set-Content "$pkgDir\LEEME.txt" -Encoding UTF8
Write-Host "         [OK] scripts, VERSION.txt, CHANGELOG.md, LEEME.txt" -ForegroundColor Green

# 4) Comprimir
Write-Host "   [3/4] Comprimiendo ZIP ..." -ForegroundColor Yellow
Compress-Archive -Path $pkgDir -DestinationPath "$pkgDir.zip" -Force
Write-Host "         [OK] $pkgName.zip" -ForegroundColor Green

$mb = [math]::Round((Get-Item "$pkgDir.zip").Length / 1MB, 1)
Write-Host "   [4/4] Listo." -ForegroundColor Yellow
Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "   INSTALABLE CREADO  -  v$ver  ($mb MB)" -ForegroundColor Green
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "   Carpeta: $pkgDir"
Write-Host "   ZIP    : $pkgDir.zip   <-- este llevas al servidor"
Write-Host ""
