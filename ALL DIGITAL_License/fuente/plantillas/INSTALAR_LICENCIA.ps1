<#
  INSTALAR_LICENCIA.ps1  -  Instala el license.lic que viene junto a este script
  ==============================================================================
  Para el sistema Dashboard Smart (IIS). Se ejecuta como Administrador (el .bat lo pide solo).
    - Busca donde esta instalado el sistema (IIS) o usa la ruta por defecto.
    - Si la licencia esta atada a un servidor, verifica que sea ESTE servidor (si no, no la instala).
    - Respalda la licencia anterior y copia la nueva a App_Data\license.lic.
    - Aplica el cambio (reinicio opcional de la aplicacion) y muestra el estado final.
  Parametros (opcionales): -Carpeta <ruta>   -SinPausa   -NoReiniciar   -Forzar
  NOTA: archivo solo ASCII a proposito (PowerShell 5.1 lee mal los acentos sin BOM).
#>
param(
    [string]$Carpeta = "",
    [switch]$SinPausa,
    [switch]$NoReiniciar,
    [switch]$Forzar
)
$ErrorActionPreference = "Stop"

function Banner($m){ Write-Host ""; Write-Host "  $('='*60)" -ForegroundColor Cyan; Write-Host "   $m" -ForegroundColor Cyan; Write-Host "  $('='*60)" -ForegroundColor Cyan }
function OK($m){ Write-Host "   [OK] $m" -ForegroundColor Green }
function Inf($m){ Write-Host "   [..] $m" -ForegroundColor Gray }
function Warn($m){ Write-Host "   [!]  $m" -ForegroundColor Yellow }
function Err($m){ Write-Host ""; Write-Host "   [ERROR] $m" -ForegroundColor Red }
function Salir($code){ if (-not $SinPausa) { Write-Host ""; Read-Host "Presione Enter para cerrar" | Out-Null }; exit $code }
function Norm($s){ return (($s -replace '[^0-9A-Za-z]','').ToUpper()) }
function PreguntaSN($texto){ if ($SinPausa) { return $false }; $r = Read-Host "   $texto (S/N)"; return ($r -match '^[sSyY]') }

Banner "Instalar licencia - Dashboard Smart"

# 1) Leer y validar el license.lic que viene junto al script
$lic = Join-Path $PSScriptRoot "license.lic"
if (-not (Test-Path $lic)) { Err "No se encontro license.lic junto a este script. Descomprima TODO el paquete y vuelva a ejecutar."; Salir 1 }
try {
    $j = Get-Content $lic -Raw | ConvertFrom-Json
    $p = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($j.payload)) | ConvertFrom-Json
    if (-not $j.signature -or -not $p.expira) { throw "incompleta" }
} catch { Err "El archivo license.lic no es valido (esta danado o incompleto). Pida uno nuevo al administrador del sistema."; Salir 1 }

$licHuella = ""
if ($p.PSObject.Properties.Name -contains "huella" -and $p.huella) { $licHuella = [string]$p.huella }
$nMod = @($p.modulos.PSObject.Properties | Where-Object { $_.Value -eq $true }).Count
Write-Host ""
Write-Host "   Cliente       : $($p.cliente)"
Write-Host "   Vence         : $($p.expira)"
Write-Host "   Modulos       : $nMod de $(@($p.modulos.PSObject.Properties).Count)"
if ($licHuella) { Write-Host "   Atada a       : servidor $licHuella" } else { Write-Host "   Atada a       : ningun servidor (funciona en cualquiera)" }
try { if ([datetime]$p.expira -lt (Get-Date).Date) { Warn "OJO: esta licencia ya esta VENCIDA." } } catch {}

# 2) Ubicar la instalacion del sistema
$pool = ""
if (-not $Carpeta) {
    try {
        Import-Module WebAdministration -ErrorAction Stop
        $app = Get-WebApplication | Where-Object { $_.PhysicalPath -and (Test-Path (Join-Path ([Environment]::ExpandEnvironmentVariables($_.PhysicalPath)) "DashSmart.dll")) } | Select-Object -First 1
        if ($app) { $Carpeta = [Environment]::ExpandEnvironmentVariables($app.PhysicalPath); $pool = $app.applicationPool }
    } catch {}
}
if (-not $Carpeta -and (Test-Path "C:\inetpub\MovilBusinessDashboardSmart\DashSmart.dll")) { $Carpeta = "C:\inetpub\MovilBusinessDashboardSmart" }
if (-not $Carpeta) {
    if ($SinPausa) { Err "No pude ubicar la instalacion del sistema (use -Carpeta)."; Salir 4 }
    Write-Host ""
    $Carpeta = (Read-Host "   No encontre la instalacion. Escriba la carpeta donde esta instalado el sistema").Trim().Trim('"')
}
if (-not $Carpeta -or -not (Test-Path (Join-Path $Carpeta "DashSmart.dll"))) { Err "'$Carpeta' no parece una instalacion del sistema (falta DashSmart.dll)."; Salir 4 }
Write-Host ""
Inf "Instalacion del sistema: $Carpeta"

# 3) Huella de ESTE servidor (la guarda el sistema en App_Data\huella_servidor.txt al arrancar)
$srvHuella = ""
$hf = Join-Path $Carpeta "App_Data\huella_servidor.txt"
if (Test-Path $hf) { $m = [regex]::Match((Get-Content $hf -Raw), '[0-9A-Fa-f]{4}(-[0-9A-Fa-f]{4}){4}'); if ($m.Success) { $srvHuella = $m.Value.ToUpper() } }
if ($srvHuella) { Inf "Huella de este servidor: $srvHuella" }

if ($licHuella) {
    if ($srvHuella) {
        if ((Norm $licHuella) -ne (Norm $srvHuella)) {
            Err "Esta licencia fue emitida para OTRO servidor y NO se instalo."
            Write-Host "        Licencia atada a : $licHuella" -ForegroundColor Red
            Write-Host "        Este servidor    : $srvHuella" -ForegroundColor Red
            Write-Host "        Entregue la huella de este servidor al administrador del sistema para que le emita la licencia correcta." -ForegroundColor Yellow
            Salir 2
        }
        OK "La licencia corresponde a este servidor."
    } else {
        Warn "La licencia esta atada a un servidor pero no pude leer la huella de este (el sistema la crea al arrancar)."
        if (-not $Forzar -and -not (PreguntaSN "Instalarla de todos modos? (si no corresponde a este servidor el sistema quedara bloqueado)")) { Warn "No se instalo. Abra el sistema una vez y vuelva a ejecutar."; Salir 3 }
    }
}

# 4) Estado ANTES (para saber si el sistema ya esta bloqueado)
$pathBase = ""
try { $pathBase = (Get-Content (Join-Path $Carpeta "appsettings.json") -Raw | ConvertFrom-Json).PathBase } catch {}
function EstadoValido {
    try { $r = Invoke-RestMethod "http://localhost$pathBase/api/licencia" -TimeoutSec 8; if ($r.valida -eq $true) { return $r } } catch {}
    return $null
}
$antes = EstadoValido

# 5) Respaldar la licencia anterior y copiar la nueva
$ad = Join-Path $Carpeta "App_Data"
New-Item -ItemType Directory -Path $ad -Force | Out-Null
$dest = Join-Path $ad "license.lic"
if (Test-Path $dest) {
    $bak = Join-Path $ad ("license_anterior_{0}.lic" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
    Copy-Item $dest $bak -Force
    Inf "Licencia anterior respaldada: $(Split-Path $bak -Leaf)"
}
Copy-Item $lic $dest -Force
OK "Licencia instalada en $dest"

# 6) Aplicar el cambio. El sistema relee la licencia solo cada ~10 min; reiniciar la aplicacion lo aplica YA
#    (los usuarios conectados tendran que volver a iniciar sesion). Si el sistema estaba bloqueado, se reinicia solo.
if ($pool -and -not $NoReiniciar) {
    $reiniciar = $false
    if (-not $antes) { $reiniciar = $true }
    else { $reiniciar = PreguntaSN "El sistema esta funcionando. Reiniciar la aplicacion para aplicar la licencia YA? (los usuarios deberan volver a iniciar sesion; si responde N se aplica sola en ~10 min)" }
    if ($reiniciar) { try { Restart-WebAppPool -Name $pool; Start-Sleep -Seconds 5; OK "Aplicacion reiniciada." } catch { Warn "No pude reiniciar el pool '$pool': $($_.Exception.Message)" } }
    else { Inf "Se aplicara sola en unos 10 minutos." }
} elseif (-not $pool) { Inf "Se aplicara sola en unos 10 minutos (o reinicie la aplicacion en IIS para aplicarla ya)." }

$despues = EstadoValido
Banner "RESULTADO"
if ($despues) {
    $act = @($despues.modulos.PSObject.Properties | Where-Object { $_.Value -eq $true }).Count
    Write-Host "   Licencia VALIDA - vence $($despues.expira) ($($despues.diasRestantes) dias) - $act de 9 modulos activos" -ForegroundColor Green
} else {
    Warn "El sistema todavia no reporta la licencia como valida (puede tardar hasta ~10 min en aplicarla, o el sistema aun no responde)."
    Warn "Si pasados 10 minutos sigue 'Sistema no disponible', avise al administrador del sistema."
}
if ($srvHuella) { Write-Host "   ID de este servidor: $srvHuella" -ForegroundColor Cyan }
Salir 0
