@echo off
title AD ALL DIGITAL ENTERPRISE v1.1
color 1F
cls
echo.
echo  ================================================
echo    AD ALL DIGITAL ENTERPRISE  v1.1
echo    Fanny Mercedes ^& Asoc.
echo  ================================================
echo.

:: ── Detectar IP local para mostrarla al usuario ──────────────
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4" ^| findstr /v "127.0.0.1"') do (
    set "IP=%%a"
    goto :found
)
:found
set "IP=%IP: =%"

echo  Iniciando el sistema...
echo  Una vez iniciado, puede acceder desde:
echo.
echo     Este equipo   :  http://localhost:5000
echo     Red local     :  http://%IP%:5000
echo     Via VPN       :  Use la IP de Tailscale de este equipo
echo.
echo  Para detener el sistema presione CTRL+C
echo  ================================================
echo.

cd /d "%~dp0ADEnterprise"

:: ASPNETCORE_ENVIRONMENT=Production activa appsettings.Production.json
set ASPNETCORE_ENVIRONMENT=Production

dotnet run --urls "http://0.0.0.0:5000"

echo.
echo  ================================================
echo   Servidor detenido correctamente.
echo  ================================================
echo.
pause
