@echo off
rem Respalda YA (aunque ya se haya respaldado hoy) lo nuevo de C:\Personales RM al disco externo. El disco debe estar conectado.
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Personales RM\_Respaldo\RESPALDAR.ps1" -Forzar
echo.
echo Listo. Revisa el aviso de Windows o la carpeta C:\Personales RM\_Respaldo\logs
pause
