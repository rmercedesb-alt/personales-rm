@echo off
title Optimizador de Rutas v002 (.NET 8)
echo.
echo  ===================================================
echo   Optimizador de Rutas de Visita - Version 002
echo   .NET 8 Blazor Server
echo  ===================================================
echo.
echo  Iniciando servidor...
echo  La aplicacion se abrira en: http://localhost:8767
echo.

cd /d "%~dp0OptimizadorRutas"
start "" "http://localhost:8767"
dotnet run --configuration Release --no-build

pause
