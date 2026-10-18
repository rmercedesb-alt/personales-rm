@echo off
title Preparar paquete V6 (ZIP) para el cliente
color 1F
cls
echo.
echo  ============================================================
echo    PREPARAR PAQUETE V6  ->  ZIP listo para llevar al cliente
echo  ============================================================
echo.

set "ROOT=%~dp0.."
set "STAGE=%TEMP%\InmueblesV6_Paquete"
set "ZIP=%ROOT%\InmueblesV6_Paquete.zip"

echo  Limpiando area temporal previa ...
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%"

echo  Copiando web-app-v6 (sin node_modules, bin, obj, dist) ...
robocopy "%ROOT%\web-app-v6" "%STAGE%\web-app-v6" /E /XD node_modules bin obj dist .vs .git >nul

echo  Quitando archivos sensibles/innecesarios del paquete ...
REM  credentials.json = clave privada de Google (NO debe ir al cliente)
del /s /q "%STAGE%\web-app-v6\credentials.json" >nul 2>&1
del /s /q "%STAGE%\web-app-v6\*.py"   >nul 2>&1
del /s /q "%STAGE%\web-app-v6\*.xlsx" >nul 2>&1
del /s /q "%STAGE%\web-app-v6\LOAD_*.sql" >nul 2>&1

echo  Copiando archivos del INSTALADOR ...
robocopy "%ROOT%\INSTALADOR" "%STAGE%\INSTALADOR" ACTUALIZAR_V6.bat CREAR_ACCESO_V6.bat APLICAR_ESQUEMA_V6.bat MANTENER_ENCENDIDA.bat ESQUEMA_ACTUALIZACIONES.sql RESPALDO_DRIVE_RCLONE.txt IMPRESORA_TICKET_80MM.txt LEEME_INSTALACION_V6.txt >nul

echo  Copiando changelog ...
copy /y "%ROOT%\MEJORAS_V6.txt" "%STAGE%\MEJORAS_V6.txt" >nul

echo  Comprimiendo ...
if exist "%ZIP%" del /q "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"

rmdir /s /q "%STAGE%"

echo.
echo  ============================================================
echo    LISTO. Paquete creado en:
echo        %ZIP%
echo.
echo    Llevalo al cliente y sigue LEEME_INSTALACION_V6.txt
echo  ============================================================
echo.
pause
