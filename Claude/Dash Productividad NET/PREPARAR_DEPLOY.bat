@echo off
chcp 65001 >nul
title Preparar Deploy - Dashboard Productividad
cd /d "C:\Personales RM\Claude\Dash Productividad NET"

echo.
echo ============================================
echo   Preparando paquete - Dashboard Productividad
echo ============================================
echo.

if exist "DEPLOY_Productividad" rmdir /S /Q "DEPLOY_Productividad"
if exist "DEPLOY_Productividad.zip" del /Q "DEPLOY_Productividad.zip"
mkdir "DEPLOY_Productividad"

echo [1/4] Compilando en modo Release...
dotnet publish DashProductividad.csproj -c Release -o "DEPLOY_Productividad\app" --nologo
if errorlevel 1 (
    echo ERROR: Fallo la compilacion.
    pause & exit /b 1
)

echo [2/4] Copiando script de actualizacion...
copy "ACTUALIZAR_SERVIDOR.ps1" "DEPLOY_Productividad\" >nul

echo [3/4] Creando instrucciones...
(
echo ==========================================================
echo  Dashboard Productividad - AM COMERCIAL
echo  Generado: %DATE% %TIME%
echo ==========================================================
echo.
echo PASOS:
echo   1. Copiar carpeta DEPLOY_Productividad al servidor
echo   2. Abrir PowerShell como Administrador
echo   3. Ejecutar: PowerShell -ExecutionPolicy Bypass -File ACTUALIZAR_SERVIDOR.ps1
echo   4. Verificar: http://amco.fortidyndns.com:86/dashproductividad/
echo.
echo IIS:  Pool=DashProductividadPool  Dir=C:\inetpub\DashProductividad
echo ==========================================================
) > "DEPLOY_Productividad\LEEME.txt"

echo [4/4] Creando ZIP...
powershell -NoProfile -Command "Compress-Archive -Path 'DEPLOY_Productividad' -DestinationPath 'DEPLOY_Productividad.zip' -Force"

echo.
echo ============================================
echo   LISTO! Archivos en DEPLOY_Productividad\
echo   ZIP:  DEPLOY_Productividad.zip
echo ============================================
explorer "C:\Personales RM\Claude\Dash Productividad NET"
pause
