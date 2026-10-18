@echo off
title AD ALL DIGITAL ENTERPRISE — v3.0
color 1F
echo.
echo  ============================================================
echo   AD ALL DIGITAL ENTERPRISE  v3.0  -- Fanny Mercedes ^& Asoc.
echo  ============================================================
echo.

:: ── Obtener IP local de la maquina ─────────────────────────────────────────
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4" ^| findstr /v "169.254"') do (
    set "LOCAL_IP=%%a"
    goto :GOT_IP
)
:GOT_IP
set "LOCAL_IP=%LOCAL_IP: =%"

echo  Puerto   : 5000
echo  Local    : http://localhost:5000
echo  Red LAN  : http://%LOCAL_IP%:5000
echo  Tailscale: http://100.84.75.124:5000
echo.
echo  Comparte la direccion de Red LAN o Tailscale con otros equipos.
echo  ============================================================
echo.

:: ── Abrir regla de firewall para puerto 5000 ────────────────────────────────
netsh advfirewall firewall show rule name="AD Enterprise v3.0 Puerto 5000" >nul 2>&1
if errorlevel 1 (
    echo  Agregando regla de firewall para puerto 5000...
    netsh advfirewall firewall add rule ^
        name="AD Enterprise v3.0 Puerto 5000" ^
        dir=in ^
        action=allow ^
        protocol=TCP ^
        localport=5000 ^
        profile=private,domain ^
        description="AD ALL DIGITAL ENTERPRISE v3.0" >nul 2>&1
    if errorlevel 1 (
        echo  AVISO: No se pudo agregar regla de firewall.
        echo  Si falla la conexion remota, ejecute este .bat como Administrador.
    ) else (
        echo  Firewall: regla creada OK.
    )
) else (
    echo  Firewall: regla ya existe OK.
)
echo.

:: ── Ir al proyecto ──────────────────────────────────────────────────────────
cd /d "%~dp0Local-Run\ADEnterprise"

:: ── Entorno Development (usa SpreadsheetId correcto) ────────────────────────
set ASPNETCORE_ENVIRONMENT=Development

:: ── Abrir navegador local despues de 4 segundos ─────────────────────────────
start "" cmd /c "timeout /t 4 /nobreak >nul && start http://localhost:5000"

:: ── Arrancar el servidor usando DLL precompilado (arranque rapido) ───────────
echo  Iniciando servidor... (Ctrl+C para detener)
echo.
dotnet "bin\Debug\net8.0\ADEnterprise.dll" ^
    --contentRoot "%~dp0Local-Run\ADEnterprise" ^
    --urls "http://0.0.0.0:5000"

pause
