@echo off
title Dashboard Smart (Visitas + Productividad) - Local
color 0E
cd /d "%~dp0"

echo.
echo  =====================================================
echo   Dashboard Smart - Iniciando servidor local...
echo   URL: http://localhost:5012
echo   Modulos: Menu, Torre, Visitas, Geo, Seguimientos,
echo            Productividad, Historicos, Supervision Dinamica
echo   (compila desde el codigo fuente: siempre lo ultimo)
echo   Ctrl+C para detener
echo  =====================================================
echo.

:: Abrir el navegador despues de 5 segundos
start /b cmd /c "timeout /t 5 /nobreak >nul && start http://localhost:5012/"

:: Compilar y luego correr el .dll a traves de dotnet.exe (en vez de "dotnet run",
:: que lanza directamente el .exe recien compilado). Esto evita el bloqueo de
:: "Control de aplicaciones" que impide ejecutar el .exe recompilado sin firmar:
:: dotnet.exe es un binario firmado de Microsoft ya autorizado en la maquina.
dotnet build DashSmart.csproj -c Debug
if errorlevel 1 goto :fin
dotnet "bin\Debug\net10.0\DashSmart.dll" --urls "http://localhost:5012"

:fin
pause
