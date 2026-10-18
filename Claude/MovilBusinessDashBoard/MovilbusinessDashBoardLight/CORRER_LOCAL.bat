@echo off
title Dashboard Visitas (Light) - Local
color 0A
cd /d "%~dp0"

echo.
echo  =====================================================
echo   Dashboard Visitas - Iniciando servidor local...
echo   URL: http://localhost:5010
echo   Ctrl+C para detener
echo  =====================================================
echo.

:: Abrir el navegador despues de 5 segundos
start /b cmd /c "timeout /t 5 /nobreak >nul && start http://localhost:5010/"

:: Iniciar la aplicacion
dotnet run --project DashLight.csproj --urls "http://localhost:5010"

pause
