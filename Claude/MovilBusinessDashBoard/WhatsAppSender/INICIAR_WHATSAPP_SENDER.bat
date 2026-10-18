@echo off
REM Levanta el sidecar WhatsAppSender (Baileys) en este equipo/servidor.
REM Primera vez: correr "npm install" en esta carpeta antes de este .bat.
REM Debe quedar corriendo SIEMPRE en el servidor (igual que el Dashboard) --
REM para produccion, registrar como Servicio de Windows (nssm o node-windows).
cd /d "%~dp0"
node server.js
pause
