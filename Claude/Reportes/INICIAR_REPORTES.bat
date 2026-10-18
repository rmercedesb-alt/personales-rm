@echo off
title Sistema de Reporteria - MovilBusiness
cd /d "%~dp0"

echo ============================================
echo   Sistema de Reporteria - MovilBusiness
echo ============================================
echo.

REM Verificar Python
python --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python no esta instalado o no esta en el PATH.
    echo Instale Python desde https://python.org
    pause
    exit /b 1
)

REM Instalar dependencias si faltan
echo Verificando dependencias...
pip install -q -r requirements.txt

echo.
echo Iniciando servidor en http://127.0.0.1:5050
echo El navegador se abrira automaticamente...
echo.
echo [Cierre esta ventana para detener el servidor]
echo.

python app.py

pause
