@echo off
title Porfi Academy (local)
cd /d "%~dp0src\PorfiAcademy"
echo Abriendo http://localhost:5273 ...
start "" http://localhost:5273
dotnet run --launch-profile http
