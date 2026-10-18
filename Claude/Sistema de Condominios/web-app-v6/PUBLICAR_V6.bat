@echo off
title Publicando API v6.0...
color 1F
cls
echo.
echo  ============================================================
echo    Publicar API - Solo ejecutar cuando hay cambios de codigo
echo    Version 6.0
echo  ============================================================
echo.

echo  Deteniendo API en ejecucion (si hay alguna)...
taskkill /F /IM InmueblesAPI.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo  Compilando y publicando API...
cd /d "%~dp0InmueblesAPI"
dotnet publish -o C:\InmueblesV6-API --nologo

if %errorlevel% neq 0 (
    echo.
    echo  ERROR al compilar. Verifica que .NET 8 SDK este instalado.
    pause
    exit /b 1
)

echo.
echo  OK - API publicado en C:\InmueblesV6-API
echo  Ahora puedes ejecutar INICIAR_V6.bat para arrancar el sistema.
echo.
pause
