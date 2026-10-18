@echo off
title Sistema Inmuebles V6.0 - NO CERRAR ESTA VENTANA
color 1F
cls
echo.
echo  ============================================================
echo    Sistema de Administracion de Inmuebles  v6.0
echo  ============================================================
echo.

if not exist "C:\InmueblesV6-API\InmueblesAPI.exe" (
    echo  AVISO: El API no esta publicado todavia.
    echo  Ejecuta primero: PUBLICAR_V6.bat
    echo.
    pause
    exit /b 1
)

REM -- Cerrar cualquier API anterior para no chocar de puerto --
taskkill /F /IM InmueblesAPI.exe >nul 2>&1
timeout /t 1 /nobreak >nul

REM -- API en ventana MINIMIZADA (puerto 5005) --
echo  [1/2] Iniciando API en http://localhost:5005 (minimizada) ...
start "API Inmuebles V6 (NO CERRAR)" /min cmd /k "color 0A && cd /d C:\InmueblesV6-API && .\InmueblesAPI.exe --urls http://0.0.0.0:5005"

REM -- Abrir el navegador en segundo plano, cuando el frontend ya este listo --
start "Abrir navegador" /min cmd /c "timeout /t 9 >nul & start http://localhost:5176"

REM -- Frontend en ESTA MISMA ventana (no depende de abrir otra ventana) --
echo  [2/2] Iniciando Frontend en http://localhost:5176 ...
echo.
echo  ============================================================
echo   El sistema abrira solo en el navegador en unos segundos.
echo   NO cierres esta ventana mientras uses el sistema.
echo  ============================================================
echo.
cd /d "%~dp0inmuebles-web"
npm run dev
