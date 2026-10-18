@echo off
title Dashboard Seguimientos Semanales (IEGT) - Local
color 0D
cd /d "%~dp0"

echo.
echo  =====================================================
echo   Dashboard Seguimientos Semanales - Iniciando...
echo   URL: http://localhost:5014
echo   Torre de Control - IEGT, informe y conclusiones
echo   Ctrl+C para detener
echo  =====================================================
echo.

start /b cmd /c "timeout /t 5 /nobreak >nul && start http://localhost:5014/"

dotnet run --project DashSeg.csproj --urls "http://localhost:5014"

pause
