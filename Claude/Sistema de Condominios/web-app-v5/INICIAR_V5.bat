@echo off
title Sistema Inmuebles V5.0
color 1F
cls
echo.
echo  ============================================================
echo    Sistema de Administracion de Inmuebles  v5.0
echo  ============================================================
echo.

REM -- Verificar que el API publicado exista --
if not exist "C:\InmueblesV5-API\InmueblesAPI.exe" (
    echo  AVISO: El API no esta publicado todavia.
    echo  Ejecuta primero: PUBLICAR_V5.bat
    echo.
    pause
    exit /b 1
)

REM -- Iniciar el API en puerto 5003 --
echo  [1/2] Iniciando API en http://localhost:5003 ...
start "API - Inmuebles V5 (NO CERRAR)" cmd /k "title API Inmuebles V5 && color 0A && cd /d C:\InmueblesV5-API && .\InmueblesAPI.exe --urls http://0.0.0.0:5003"

echo  Esperando que el API arranque...
timeout /t 4 /nobreak >nul

REM -- Iniciar el Frontend en puerto 5175 --
echo  [2/2] Iniciando Frontend en http://localhost:5175 ...
start "Frontend - Inmuebles V5 (NO CERRAR)" cmd /k "title Frontend Inmuebles V5 && color 0B && cd /d ""%~dp0inmuebles-web"" && npm run dev"

echo  Esperando que el frontend compile...
timeout /t 6 /nobreak >nul

REM -- Abrir navegador --
start http://localhost:5175

echo.
echo  ============================================================
echo   Sistema listo en: http://localhost:5175
echo.
echo   NO cierres las ventanas verdes (API) ni azules (Frontend)
echo  ============================================================
echo.
pause
