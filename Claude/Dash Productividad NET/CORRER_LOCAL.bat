@echo off
title Dashboard Productividad - Local
cd /d "C:\Personales RM\Claude\Dash Productividad NET"
echo.
echo ============================================
echo   Dashboard Productividad - Modo Local
echo   URL: http://localhost:5002
echo   Ctrl+C para detener
echo ============================================
echo.
start "" "http://localhost:5002"
dotnet run --project DashProductividad.csproj --urls "http://localhost:5002"
pause
