@echo off
title Dashboard Productividad (Prod) - Local
color 0B
cd /d "%~dp0"

echo.
echo  =====================================================
echo   Dashboard Productividad - Iniciando servidor local...
echo   URL: http://localhost:5011
echo   Ctrl+C para detener
echo  =====================================================
echo.

:: Abrir el navegador despues de 5 segundos
start /b cmd /c "timeout /t 5 /nobreak >nul && start http://localhost:5011/"

:: Iniciar la aplicacion
dotnet run --project DashProd.csproj --urls "http://localhost:5011"

pause
