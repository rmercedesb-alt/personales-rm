@echo off
title Instalar - En Linea con mis clientes
color 0A
cls
echo.
echo  ============================================================
echo    INSTALAR  "En Linea con mis clientes"  (WhatsApp)
echo  ============================================================
echo.
echo  Este modulo envia mensajes de WhatsApp leyendo la base de datos
echo  del sistema. Se puede instalar en la MISMA PC del sistema o en otra.
echo.

cd /d "%~dp0"

REM -- Node instalado? --
where node >nul 2>&1
if %errorlevel% neq 0 (
    echo  ERROR: Node.js no esta instalado. Descargalo (LTS) en https://nodejs.org/
    pause
    exit /b 1
)

REM -- Configurar donde esta la base de datos --
echo.
set /p MISMA="La base de datos esta en ESTA misma PC? (S/N): "
if /i "%MISMA%"=="N" (
    set /p DBIP="Escribe la IP del servidor de base de datos (ej. 192.168.1.50): "
    powershell -NoProfile -Command "(Get-Content '.env') -replace '^PGHOST=.*', 'PGHOST=%DBIP%' | Set-Content '.env' -Encoding ascii"
    echo  Configurado para conectar a la base de datos en %DBIP%
) else (
    powershell -NoProfile -Command "(Get-Content '.env') -replace '^PGHOST=.*', 'PGHOST=localhost' | Set-Content '.env' -Encoding ascii"
    echo  Configurado para base de datos local (localhost)
)

echo.
echo  [1/3] Instalando dependencias (puede tardar, descarga navegador interno) ...
call npm install

echo.
echo  [2/3] Limpiando sesion de WhatsApp anterior (pedira escanear el QR) ...
if exist "data\wa-session" rmdir /s /q "data\wa-session"

echo.
echo  [3/3] Creando acceso directo en el Escritorio ...
powershell -NoProfile -Command ^
  "$ws=New-Object -ComObject WScript.Shell;" ^
  "$node=(Get-Command node).Source;" ^
  "$s=$ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'En Linea con mis clientes.lnk'));" ^
  "$s.TargetPath=$node; $s.Arguments='server.js'; $s.WorkingDirectory='%~dp0';" ^
  "$s.IconLocation='%~dp0icono-inmuebles.ico';" ^
  "$s.Description='Modulo de WhatsApp para cobranza'; $s.Save()"

echo.
echo  ============================================================
echo    LISTO. En el Escritorio: "En Linea con mis clientes".
echo    Al abrirlo, escanea el codigo QR con el telefono dedicado.
echo  ============================================================
echo.
pause
