@echo off
title Optimizador de Rutas v003 (.NET 8)
echo  Iniciando Version003...
cd /d "%~dp0OptimizadorRutas"
start "" "http://localhost:8768"
dotnet run --configuration Release --no-build
pause
