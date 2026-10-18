@echo off
title Dashboard Geolocalizaciones (Geo) - Local
color 0D
cd /d "%~dp0"

echo.
echo  =====================================================
echo   Dashboard Geolocalizaciones - Iniciando servidor local...
echo   URL: http://localhost:5013
echo   Mapa: ubicacion, ventas, oportunidades y rutas
echo   Ctrl+C para detener
echo  =====================================================
echo.

:: Abrir el navegador despues de 5 segundos
start /b cmd /c "timeout /t 5 /nobreak >nul && start http://localhost:5013/"

:: Iniciar la aplicacion
dotnet run --project DashGeo.csproj --urls "http://localhost:5013"

pause
