' Lanza RESPALDAR_DRIVE.ps1 sin ventana (lo usa la tarea programada «Respaldo Personales RM a Drive»).
Set sh = CreateObject("WScript.Shell")
sh.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""C:\Personales RM\_Respaldo\RESPALDAR_DRIVE.ps1""", 0, False
