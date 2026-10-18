@echo off
title Dashboard Visitas - Local
color 0A
cd /d "%~dp0"

echo.
echo  =====================================================
echo   Dashboard Visitas - Iniciando servidor local...
echo  =====================================================
echo.
echo  Espera 5 segundos y el navegador se abre solo.
echo  Para detenerlo: cierra esta ventana o presiona Ctrl+C
echo.

:: Abrir el navegador despues de 5 segundos
start /b cmd /c "timeout /t 5 /nobreak >nul && start http://localhost:5001/"

:: Iniciar la aplicacion
dotnet run --project DashVisitas.csproj --urls "http://localhost:5001"

pause
