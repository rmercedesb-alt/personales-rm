@echo off
chcp 65001 >nul
title Preparar Paquete de Actualizacion - DashVisitas

cd /d "C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Dash NET"

echo.
echo ============================================
echo   Preparando paquete de actualizacion
echo   DashVisitas - AM COMERCIAL
echo ============================================
echo.

REM --- Limpiar carpeta de deploy anterior ---
if exist "DEPLOY_DashVisitas" (
    echo Limpiando paquete anterior...
    rmdir /S /Q "DEPLOY_DashVisitas"
)
if exist "DEPLOY_DashVisitas.zip" del /Q "DEPLOY_DashVisitas.zip"

mkdir "DEPLOY_DashVisitas"
mkdir "DEPLOY_DashVisitas\app"

REM --- Compilar en modo Release ---
echo [1/4] Compilando proyecto en modo Release...
echo.
dotnet publish DashVisitas.csproj -c Release -o "DEPLOY_DashVisitas\app" --nologo
if errorlevel 1 (
    echo.
    echo ============================================
    echo   ERROR: Fallo la compilacion.
    echo   Revisa los errores arriba y vuelve a intentar.
    echo ============================================
    pause
    exit /b 1
)

echo.
echo [2/4] Copiando script de actualizacion...
copy "ACTUALIZAR_SERVIDOR.ps1" "DEPLOY_DashVisitas\" >nul

REM --- Crear instrucciones LEEME.txt ---
echo [3/4] Creando instrucciones...
(
echo ==========================================================
echo  INSTRUCCIONES DE ACTUALIZACION - DashVisitas AM COMERCIAL
echo  Generado: %DATE% %TIME%
echo ==========================================================
echo.
echo CONTENIDO DE ESTE PAQUETE:
echo   app\                     Archivos compilados de la aplicacion
echo   ACTUALIZAR_SERVIDOR.ps1  Script de actualizacion para el servidor
echo   LEEME.txt                Este archivo
echo.
echo PASOS PARA ACTUALIZAR EL SERVIDOR:
echo.
echo   1. Copiar TODA esta carpeta al servidor
echo      ^(puede usar USB, red local, RDP, etc.^)
echo.
echo   2. En el servidor, abrir PowerShell como ADMINISTRADOR
echo.
echo   3. Ejecutar:
echo      PowerShell -ExecutionPolicy Bypass -File "ACTUALIZAR_SERVIDOR.ps1"
echo.
echo   4. Esperar que el script termine ^(dice "completada exitosamente"^)
echo.
echo   5. Abrir el navegador y verificar:
echo      http://amco.fortidyndns.com:86/movilbusinessdashboard/
echo.
echo CONFIGURACION DEL SERVIDOR:
echo   App Pool   : MovilBusinessDashboardPool
echo   Directorio : C:\inetpub\MovilBusinessDashboard
echo   Puerto IIS : 86
echo.
echo NOTA: El script protege automaticamente el appsettings.json
echo       del servidor ^(conexion a base de datos^) y NO lo sobreescribe.
echo ==========================================================
) > "DEPLOY_DashVisitas\LEEME.txt"

REM --- Crear ZIP ---
echo [4/4] Creando archivo ZIP...
powershell -NoProfile -Command "Compress-Archive -Path 'DEPLOY_DashVisitas' -DestinationPath 'DEPLOY_DashVisitas.zip' -Force"
if errorlevel 1 (
    echo   AVISO: No se pudo crear el ZIP ^(PowerShell error^)
    echo   La carpeta DEPLOY_DashVisitas\ esta lista igual.
) else (
    echo   ZIP creado: DEPLOY_DashVisitas.zip
)

echo.
echo ============================================
echo   PAQUETE LISTO!
echo.
echo   Carpeta : DEPLOY_DashVisitas\
echo   ZIP     : DEPLOY_DashVisitas.zip
echo.
echo   Copia cualquiera de los dos al servidor
echo   y ejecuta ACTUALIZAR_SERVIDOR.ps1
echo   como Administrador.
echo ============================================
echo.

REM --- Abrir carpeta en el explorador ---
explorer "C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\Dash NET"

pause
