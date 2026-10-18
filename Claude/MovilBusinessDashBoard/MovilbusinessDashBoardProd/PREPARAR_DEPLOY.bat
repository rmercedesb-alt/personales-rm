@echo off
chcp 65001 >nul
title Preparar Deploy - DashProd (Productividad)
cd /d "%~dp0"

echo.
echo ============================================
echo   Preparando paquete - Dashboard Productividad
echo   Proyecto: DashProd
echo ============================================
echo.

if exist "Instalables" rmdir /S /Q "Instalables"
if exist "Instalables.zip" del /Q "Instalables.zip"
mkdir "Instalables"

echo [1/3] Compilando en modo Release...
dotnet publish DashProd.csproj -c Release -o "Instalables" --nologo
if errorlevel 1 (
    echo ERROR: Fallo la compilacion.
    pause & exit /b 1
)

echo [2/3] Creando instrucciones...
(
echo ==========================================================
echo  Dashboard Productividad ^(DashProd^) - AM COMERCIAL
echo  Generado: %DATE% %TIME%
echo ==========================================================
echo.
echo PASOS PARA INSTALAR EN EL SERVIDOR:
echo.
echo   1. Copiar la carpeta Instalables\ al servidor
echo   2. En IIS, apuntar la app al directorio copiado
echo   3. Editar appsettings.json con la conexion correcta
echo      y el PathBase que corresponda
echo.
echo PathBase por defecto: /dashproductividadProd
echo ==========================================================
) > "Instalables\LEEME.txt"

echo [3/3] Creando ZIP...
powershell -NoProfile -Command "Compress-Archive -Path 'Instalables' -DestinationPath 'Instalables.zip' -Force"

echo.
echo ============================================
echo   LISTO!
echo   Carpeta : Instalables\
echo   ZIP     : Instalables.zip
echo ============================================
explorer "%~dp0"
pause
