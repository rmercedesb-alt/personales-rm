@echo off
chcp 65001 > nul
title Optimizador de Rutas - AM COMERCIAL

echo.
echo  ================================================
echo   Optimizador de Rutas de Visita - AM COMERCIAL
echo  ================================================
echo.

set PYTHON=C:\Python314\python.exe
set APPDIR=C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Optimizador de Ruta
set APP=%APPDIR%\optimizador_rutas.py
set PORT=8766

:: Incluir site-packages del usuario para asegurar que streamlit se encuentre
for /f "delims=" %%i in ('"%PYTHON%" -c "import site; print(site.getusersitepackages())"') do set USERPKG=%%i
set PYTHONPATH=%USERPKG%;%PYTHONPATH%

if not exist "%PYTHON%" (
    echo  ERROR: Python no encontrado en %PYTHON%
    pause & exit /b 1
)

if not exist "%APP%" (
    echo  ERROR: Script no encontrado en:
    echo  %APP%
    pause & exit /b 1
)

:: Verificar que streamlit esta instalado
"%PYTHON%" -c "import streamlit" >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo  Streamlit no encontrado. Instalando dependencias...
    "%PYTHON%" -m pip install streamlit pandas plotly pyodbc openpyxl requests
    echo.
)

echo  Verificando si el puerto %PORT% esta libre...
for /f "tokens=5" %%p in ('netstat -aon 2^>nul ^| findstr /R ":%PORT% "') do (
    echo  Liberando proceso %%p en puerto %PORT%...
    taskkill /f /pid %%p >nul 2>&1
)

echo  Abriendo navegador en 5 segundos...
start "" /min cmd /c "timeout /t 5 /nobreak > nul && start http://localhost:%PORT%"

echo  Iniciando servidor Streamlit...
echo  URL: http://localhost:%PORT%
echo.
echo  IMPORTANTE: No cierres esta ventana mientras uses la aplicacion.
echo  Para detenerla presiona Ctrl+C o cierra la ventana.
echo.

cd /d "%APPDIR%"
"%PYTHON%" -m streamlit run "%APP%" --server.port=%PORT% --server.headless=true --server.address=0.0.0.0 --browser.gatherUsageStats=false

echo.
echo  Aplicacion detenida.
pause
