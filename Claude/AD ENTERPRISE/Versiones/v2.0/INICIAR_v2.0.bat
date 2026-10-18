@echo off
title AD ALL DIGITAL ENTERPRISE — v2.0
color 1F
echo.
echo  ============================================================
echo   AD ALL DIGITAL ENTERPRISE  v2.0  -- Fanny Mercedes ^& Asoc.
echo  ============================================================
echo.

:: ── Obtener IP local de la maquina ─────────────────────────────────────────
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4" ^| findstr /v "169.254"') do (
    set "LOCAL_IP=%%a"
    goto :GOT_IP
)
:GOT_IP
:: Quitar espacio inicial
set "LOCAL_IP=%LOCAL_IP: =%"

echo  Puerto   : 5001   (v1.1 usa el 5000)
echo  Local    : http://localhost:5001
echo  Red LAN  : http://%LOCAL_IP%:5001
echo.
echo  Comparte la direccion de Red LAN con cualquier PC en la misma red.
echo  ============================================================
echo.

:: ── Abrir regla de firewall para puerto 5001 ────────────────────────────────
netsh advfirewall firewall show rule name="AD Enterprise v2.0 Puerto 5001" >nul 2>&1
if errorlevel 1 (
    echo  Agregando regla de firewall para puerto 5001...
    netsh advfirewall firewall add rule ^
        name="AD Enterprise v2.0 Puerto 5001" ^
        dir=in ^
        action=allow ^
        protocol=TCP ^
        localport=5001 ^
        profile=private,domain ^
        description="AD ALL DIGITAL ENTERPRISE v2.0" >nul 2>&1
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

:: ── Abrir navegador local despues de 4 segundos ─────────────────────────────
start "" cmd /c "timeout /t 4 /nobreak >nul && start http://localhost:5001"

:: ── Arrancar el servidor ────────────────────────────────────────────────────
echo  Iniciando servidor... (Ctrl+C para detener)
echo.
dotnet run --urls "http://0.0.0.0:5001"

pause
