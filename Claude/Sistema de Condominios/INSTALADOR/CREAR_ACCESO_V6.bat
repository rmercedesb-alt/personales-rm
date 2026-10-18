@echo off
title Crear acceso directo "Sistema Inmuebles V6"
set "ROOT=%~dp0.."
set "DESTINO=%ROOT%\web-app-v6\INICIAR_V6.bat"

powershell -NoProfile -Command ^
  "$ws = New-Object -ComObject WScript.Shell;" ^
  "$lnk = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\\Sistema Inmuebles V6.lnk');" ^
  "$lnk.TargetPath = '%DESTINO%';" ^
  "$lnk.WorkingDirectory = '%ROOT%\web-app-v6';" ^
  "$lnk.Description = 'Sistema de Administracion de Inmuebles v6.0';" ^
  "$lnk.Save()"

echo  Acceso directo "Sistema Inmuebles V6" creado en el Escritorio.
pause
