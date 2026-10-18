@echo off
title Sistema de Inmuebles RD
echo.
echo  =====================================================
echo   SISTEMA DE ADMINISTRACION DE INMUEBLES RD
echo  =====================================================
echo.

echo  Limpiando procesos anteriores...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000 "') do taskkill /f /pid %%a >nul 2>&1
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5173 "') do taskkill /f /pid %%a >nul 2>&1
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5174 "') do taskkill /f /pid %%a >nul 2>&1
timeout /t 2 >nul

echo  [1/2] Iniciando API (backend)...
start "API - Inmuebles" cmd /k "cd /d "%~dp0InmueblesAPI" && dotnet run --urls http://0.0.0.0:5000"

timeout /t 4 >nul

echo  [2/2] Iniciando Frontend...
start "WEB - Inmuebles" cmd /k "cd /d "%~dp0inmuebles-web" && npm run dev -- --port 5173 --strictPort"

echo.
echo  Esperando que el frontend este listo...

:WAIT
timeout /t 2 >nul
curl -s http://localhost:5173 >nul 2>&1
if errorlevel 1 goto WAIT

echo  Abriendo navegador...
start "" "http://localhost:5173"

exit
