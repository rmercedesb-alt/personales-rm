@echo off
rem Respalda YA a Google Drive (G:) una copia comprimida de C:\Personales RM.
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Personales RM\_Respaldo\RESPALDAR_DRIVE.ps1" -Forzar
echo.
echo Listo. Revisa G:\Mi unidad\Respaldos  y  C:\Personales RM\_Respaldo\logs
pause
