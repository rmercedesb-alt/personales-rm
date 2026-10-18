# Reconstruye la carpeta app\ de ALL DIGITAL LICENSE (y el lanzador) a partir del codigo fuente.
# IMPORTANTE: cierra antes el programa (la ventana negra), o fallara al reemplazar app\.
# Uso:  powershell -ExecutionPolicy Bypass -File PUBLICAR.ps1
# NO toca la carpeta datos\ (llave cifrada + base de clientes).
$ErrorActionPreference = "Stop"
$src  = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $src
$app  = Join-Path $root "app"

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
& dotnet publish "$src\MfSoftLicense.csproj" -c Release -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw "Fallo dotnet publish" }
Remove-Item "$src\bin","$src\obj" -Recurse -Force -ErrorAction SilentlyContinue

$bat = @'
@echo off
title ALL DIGITAL LICENSE
color 1F
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo  Falta .NET 10 en esta computadora. Instala "ASP.NET Core Runtime 10" desde
  echo  https://dotnet.microsoft.com/download/dotnet/10.0  y vuelve a abrir este archivo.
  echo.
  pause
  exit /b 1
)
echo.
echo  =============================================
echo   ALL DIGITAL LICENSE  -  http://127.0.0.1:5077
echo   Deja esta ventana abierta mientras lo uses.
echo   Para cerrar el programa: Ctrl+C o cierra la ventana.
echo  =============================================
echo.
start "" /b cmd /c "timeout /t 3 /nobreak >nul & start http://127.0.0.1:5077/"
dotnet "app\MfSoftLicense.dll" --urls "http://127.0.0.1:5077" --datos "%~dp0datos"
pause
'@
Set-Content -Path (Join-Path $root "INICIAR_ALL_DIGITAL_LICENSE.bat") -Value $bat -Encoding ASCII
# El lanzador se llamaba antes INICIAR_MfSoft_License.bat: se retira para no dejar dos.
Remove-Item (Join-Path $root "INICIAR_MfSoft_License.bat") -Force -ErrorAction SilentlyContinue
Write-Host "Listo: $root"
