@echo off
title Reiniciar Frontend V4
color 0B

echo ============================================
echo   REINICIANDO FRONTEND - Sistema Inmuebles
echo ============================================
echo.

:: Matar proceso en puerto 5174 si existe
echo [1/3] Liberando puerto 5174...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr :5174 2^>nul') do (
    taskkill /PID %%a /F >nul 2>&1
)
timeout /t 2 /nobreak >nul

:: Verificar si el API esta corriendo en 5001
echo [2/3] Verificando API...
netstat -aon | findstr :5001 >nul 2>&1
if errorlevel 1 (
    echo       API no detectada - iniciando...
    if exist "C:\InmueblesV4-API\InmueblesAPI.exe" (
        start "API Inmuebles V4" cmd /k "title API Inmuebles V4 && color 0A && cd /d C:\InmueblesV4-API && .\InmueblesAPI.exe --urls http://0.0.0.0:5001"
        timeout /t 4 /nobreak >nul
    ) else (
        echo       AVISO: API no publicada. Ejecuta PUBLICAR_V4.bat primero.
    )
) else (
    echo       API ya esta corriendo OK.
)

:: Iniciar el servidor de desarrollo
echo [3/3] Iniciando servidor Vite...
echo.
cd /d "%~dp0inmuebles-web"
start "Frontend Inmuebles V4" cmd /k "title Frontend Inmuebles V4 && color 09 && npm run dev"

timeout /t 4 /nobreak >nul
start http://localhost:5174

echo.
echo  Listo! Presiona Ctrl+Shift+R en el navegador si no carga.
echo.
