@echo off
title Instalar requisitos (Node.js y .NET 8 SDK)
color 0E
cls
echo.
echo  ============================================================
echo    INSTALAR REQUISITOS  -  Node.js y .NET 8 SDK
echo  ============================================================
echo.

REM -- Verificar permisos de administrador --
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  ERROR: Ejecuta este archivo como ADMINISTRADOR.
    echo  Haz clic derecho - Ejecutar como administrador.
    echo.
    pause
    exit /b 1
)

REM -- winget disponible? --
where winget >nul 2>&1
if %errorlevel% neq 0 (
    echo  Tu Windows no tiene "winget". Instala Node.js y .NET 8 SDK a mano:
    echo    Node.js LTS : https://nodejs.org/
    echo    .NET 8 SDK  : https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo  [1/2] Instalando Node.js LTS ...
winget install -e --id OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements

echo.
echo  [2/2] Instalando .NET 8 SDK ...
winget install -e --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements

echo.
echo  ============================================================
echo   Node.js y .NET 8 listos.
echo.
echo   RECUERDA: PostgreSQL 18 se instala A MANO con:
echo       contrasena postgres = kina5468   y   puerto 5432
echo       https://www.postgresql.org/download/windows/
echo.
echo   Luego ejecuta INSTALAR.bat como administrador.
echo  ============================================================
echo.
pause
