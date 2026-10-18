@echo off
title Optimizador de Rutas v006 (.NET 8)
cd /d "%~dp0OptimizadorRutas"
start "" "http://localhost:8771"
dotnet run --configuration Release
pause
