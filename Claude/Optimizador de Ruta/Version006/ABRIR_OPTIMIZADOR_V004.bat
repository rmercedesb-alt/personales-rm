@echo off
title Optimizador de Rutas v004 (.NET 8)
cd /d "%~dp0OptimizadorRutas"
start "" "http://localhost:8769"
dotnet run --configuration Release
pause
