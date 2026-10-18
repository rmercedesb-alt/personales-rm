@echo off
title Optimizador de Rutas v005 (.NET 8)
cd /d "%~dp0OptimizadorRutas"
start "" "http://localhost:8770"
dotnet run --configuration Release
pause
