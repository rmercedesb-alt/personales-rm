' Lanza RESPALDAR.ps1 sin mostrar ventana (lo usa la tarea programada «Respaldo Personales RM»).
Set sh = CreateObject("WScript.Shell")
sh.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""C:\Personales RM\_Respaldo\RESPALDAR.ps1""", 0, False
