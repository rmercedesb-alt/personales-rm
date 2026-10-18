<#
  CREAR_DELTA_IIS.ps1  —  Genera un instalable DELTA (solo archivos cambiados)
  ==========================================================================
  Se ejecuta en la PC de DESARROLLO. Publica la app y la COMPARA (por hash) contra
  el ultimo instalable FULL generado; empaqueta SOLO los archivos nuevos/cambiados.

  Resultado en Instalables\ :
     DashSmart_DELTA_v<version>_<fecha>\        (carpeta)
        app\                (solo los archivos cambiados)
        ACTUALIZAR_DELTA.ps1
        VERSION.txt · ARCHIVOS_CAMBIADOS.txt · LEEME.txt
     DashSmart_DELTA_v<version>_<fecha>.zip     (esto llevas al servidor)

  Cuando usar cual:
    - FULL  (CREAR_INSTALABLE_IIS.ps1): instalacion nueva o si el servidor esta muy atrasado.
    - DELTA (este): actualizacion rapida cuando el servidor ya tiene una version reciente.
      El baseline por defecto es el ULTIMO paquete FULL de Instalables\ (= lo que asumimos desplegado).
      Puedes forzar otro con:  .\CREAR_DELTA_IIS.ps1 -Baseline "C:\ruta\al\app_desplegado"
#>
param([string]$Baseline = "")

$ErrorActionPreference = "Stop"
$Proj = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Proj

# 1) Version
$csproj = Get-Content "$Proj\DashSmart.csproj" -Raw
if ($csproj -match "<Version>\s*([^<]+?)\s*</Version>") { $ver = $Matches[1].Trim() } else { $ver = "0.0.0" }
$stamp   = Get-Date -Format "yyyy-MM-dd_HHmm"
$outRoot = "$Proj\Instalables"
if (-not (Test-Path $outRoot)) { New-Item -ItemType Directory -Path $outRoot | Out-Null }

Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Cyan
Write-Host "   Instalable DELTA  -  Dashboard Smart  v$ver" -ForegroundColor Cyan
Write-Host "  ======================================================" -ForegroundColor Cyan

# 2) Publicar a temp
$pub = "$env:TEMP\dashsmart_delta_pub"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
Write-Host "   [1/4] dotnet publish -c Release ..." -ForegroundColor Yellow
$out = & dotnet publish "$Proj\DashSmart.csproj" -c Release -o $pub --nologo 2>&1
if ($LASTEXITCODE -ne 0) { $out | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }; Write-Host "  ERROR al publicar." -ForegroundColor Red; Pause; exit 1 }
Write-Host "         [OK] publicado." -ForegroundColor Green

# 3) Baseline = ultimo FULL (o el indicado)
if (-not $Baseline) {
    $cand = Get-ChildItem $outRoot -Directory -Filter "DashSmart_IIS_*" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Where-Object { Test-Path "$($_.FullName)\app\DashSmart.dll" }
    if ($cand) { $Baseline = "$($cand[0].FullName)\app" }
}
if (-not $Baseline -or -not (Test-Path "$Baseline\DashSmart.dll")) {
    Write-Host "  ERROR: no hay un instalable FULL de referencia (baseline)." -ForegroundColor Red
    Write-Host "         Genera primero un FULL con CREAR_INSTALABLE_IIS.ps1, o pasa -Baseline." -ForegroundColor Red
    Pause; exit 1
}
Write-Host "   [2/4] Comparando contra baseline:" -ForegroundColor Yellow
Write-Host "         $Baseline" -ForegroundColor Gray

# 4) Diff por hash SHA256
$changed = New-Object System.Collections.Generic.List[string]
Get-ChildItem $pub -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($pub.Length).TrimStart('\')
    if ($rel -ieq 'appsettings.json') { return }   # nunca sobrescribir la config del servidor
    $bf = Join-Path $Baseline $rel
    $add = $false
    if (-not (Test-Path $bf)) { $add = $true }
    elseif ((Get-FileHash $_.FullName -Algorithm SHA256).Hash -ne (Get-FileHash $bf -Algorithm SHA256).Hash) { $add = $true }
    if ($add) { [void]$changed.Add($rel) }
}
if ($changed.Count -eq 0) {
    Write-Host "         No hay archivos cambiados vs el baseline. No se genera delta." -ForegroundColor Yellow
    Pause; exit 0
}
Write-Host "         [OK] $($changed.Count) archivo(s) cambiado(s)." -ForegroundColor Green

# 5) Empaquetar delta
$pkgName = "DashSmart_DELTA_v${ver}_$stamp"
$pkgDir  = "$outRoot\$pkgName"
if (Test-Path $pkgDir)       { Remove-Item $pkgDir -Recurse -Force }
if (Test-Path "$pkgDir.zip") { Remove-Item "$pkgDir.zip" -Force }
New-Item -ItemType Directory -Path "$pkgDir\app" | Out-Null
Write-Host "   [3/4] Empaquetando solo lo cambiado ..." -ForegroundColor Yellow
foreach ($rel in $changed) {
    $dst = Join-Path "$pkgDir\app" $rel
    $dir = Split-Path $dst -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item (Join-Path $pub $rel) $dst -Force
}
Copy-Item "$Proj\deploy_iis\ACTUALIZAR_DELTA.ps1" "$pkgDir\ACTUALIZAR_DELTA.ps1" -Force
[System.IO.File]::WriteAllText("$pkgDir\VERSION.txt", $ver)
$changed | Set-Content "$pkgDir\ARCHIVOS_CAMBIADOS.txt" -Encoding UTF8
if (Test-Path "$Proj\CHANGELOG.md") { Copy-Item "$Proj\CHANGELOG.md" "$pkgDir\CHANGELOG.md" -Force }

$leeme = @"
==========================================================
 DELTA (parche) - MovilBusiness Dashboard Smart  v$ver
 Generado: $(Get-Date -Format 'yyyy-MM-dd HH:mm')  -  AM Comercial, S.A.S.
==========================================================

Este paquete contiene SOLO los $($changed.Count) archivo(s) que cambiaron
respecto a la version anterior. Es un PARCHE, no una instalacion completa.

REQUISITO: la version anterior debe estar ya instalada en el servidor.
           (Si es instalacion nueva o el servidor esta muy atrasado, usa el
            instalable FULL: DashSmart_IIS_v...\ACTUALIZAR_IIS.ps1)

COMO APLICAR:
  1. Copia/descomprime este paquete en el servidor.
  2. Click derecho en ACTUALIZAR_DELTA.ps1 -> Ejecutar con PowerShell (como Administrador).
     -> Copia solo los archivos cambiados y reinicia el sitio. Conserva tu appsettings.json.
  3. Verifica en:  http://<servidor>/movilbusinessdashboardSmart/version

Ver ARCHIVOS_CAMBIADOS.txt para el detalle de lo que trae este parche.
==========================================================
"@
$leeme | Set-Content "$pkgDir\LEEME.txt" -Encoding UTF8

Write-Host "   [4/4] Comprimiendo ZIP ..." -ForegroundColor Yellow
Compress-Archive -Path $pkgDir -DestinationPath "$pkgDir.zip" -Force
$kb = [math]::Round((Get-Item "$pkgDir.zip").Length / 1KB, 0)
Write-Host ""
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "   DELTA CREADO  -  v$ver  ($($changed.Count) archivos · $kb KB)" -ForegroundColor Green
Write-Host "  ======================================================" -ForegroundColor Green
Write-Host "   ZIP: $pkgDir.zip   <-- este llevas al servidor" -ForegroundColor White
Write-Host ""
