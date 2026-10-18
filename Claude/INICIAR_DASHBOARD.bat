@echo off
title Dashboard Visitas - MovilBusiness5STD
cd /d "%~dp0"
echo.
echo  ============================================
echo   Dashboard Visitas - MovilBusiness5STD
echo  ============================================
echo.
echo  Abre tu navegador en:  http://localhost:5000
echo.
echo  Para detener: presiona Ctrl+C
echo.
python dashboard_server.py
pause
