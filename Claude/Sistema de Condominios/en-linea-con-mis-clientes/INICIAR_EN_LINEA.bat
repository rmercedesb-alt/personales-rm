@echo off
title En linea con mis clientes - Fanny Mercedes ^& Asoc.
color 0A
cls
echo.
echo  ============================================================
echo    En linea con mis clientes  -  WhatsApp de cobranza
echo  ============================================================
echo.

cd /d "%~dp0"

REM -- Instalar dependencias la primera vez --
if not exist "node_modules" (
    echo  Instalando dependencias por primera vez (puede tardar varios minutos)...
    echo  Se descarga un navegador interno para WhatsApp, ten paciencia.
    call npm install
    echo.
)

echo  Iniciando el panel en http://localhost:5050 ...
echo  La primera vez deberas escanear el codigo QR desde el panel.
echo.
timeout /t 3 /nobreak >nul
start http://localhost:5050
node server.js
pause
