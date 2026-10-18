@echo off
title ALL DIGITAL LICENSE
color 1F
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo  Falta .NET 10 en esta computadora. Instala "ASP.NET Core Runtime 10" desde
  echo  https://dotnet.microsoft.com/download/dotnet/10.0  y vuelve a abrir este archivo.
  echo.
  pause
  exit /b 1
)
echo.
echo  =============================================
echo   ALL DIGITAL LICENSE  -  http://127.0.0.1:5077
echo   Deja esta ventana abierta mientras lo uses.
echo   Para cerrar el programa: Ctrl+C o cierra la ventana.
echo  =============================================
echo.
start "" /b cmd /c "timeout /t 3 /nobreak >nul & start http://127.0.0.1:5077/"
dotnet "app\MfSoftLicense.dll" --urls "http://127.0.0.1:5077" --datos "%~dp0datos"
pause
