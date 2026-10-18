@echo off
chcp 65001 >nul
title AM COMERCIAL - Iniciar Todos los Dashboards

echo.
echo ============================================================
echo   AM COMERCIAL - Iniciar Dashboards en Desarrollo
echo ============================================================
echo.
echo  Puerto 5001 ^> Dashboard Visitas   (http://localhost:5001)
echo  Puerto 5002 ^> Productividad       (http://localhost:5002)
echo.
echo  Menu principal: http://localhost:5001/menu
echo ============================================================
echo.

REM Cerrar procesos anteriores si estan corriendo
echo [1/2] Deteniendo procesos anteriores (si existen)...
taskkill /F /IM DashVisitas.exe     >nul 2>&1
taskkill /F /IM DashProductividad.exe >nul 2>&1
timeout /T 1 /NOBREAK >nul

REM Iniciar Dashboard Visitas en ventana separada
echo [2/2] Iniciando proyectos...
start "Dashboard Visitas (5001)" cmd /k "chcp 65001 >nul & cd /d "C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Dash NET" & echo [Visitas] Iniciando en http://localhost:5001 & dotnet run"

REM Esperar 2 segundos y luego iniciar Productividad
timeout /T 2 /NOBREAK >nul
start "Dashboard Productividad (5002)" cmd /k "chcp 65001 >nul & cd /d "C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Dash Productividad NET" & echo [Productividad] Iniciando en http://localhost:5002 & dotnet run"

echo.
echo  Ambos proyectos iniciando en ventanas separadas.
echo  Espera unos segundos y abre: http://localhost:5001/menu
echo.
timeout /T 5 /NOBREAK >nul
start "" "http://localhost:5001/menu"

echo  Listo! Cierra esta ventana cuando quieras.
pause
