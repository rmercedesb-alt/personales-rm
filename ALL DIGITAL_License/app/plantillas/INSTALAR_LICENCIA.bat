@echo off
title Instalar licencia - Dashboard Smart
cd /d "%~dp0"
rem Pide permisos de Administrador y ejecuta INSTALAR_LICENCIA.ps1 (que esta en esta misma carpeta).
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0INSTALAR_LICENCIA.ps1\"'"
