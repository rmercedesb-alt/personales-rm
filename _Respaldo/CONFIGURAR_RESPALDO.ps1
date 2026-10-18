<#
  CONFIGURAR_RESPALDO.ps1  —  se corre UNA vez, con el disco externo conectado.
  =============================================================================
  1. Elige el disco de respaldo (por defecto el único disco USB conectado) y le crea la carpeta «Respaldo Personales RM» con su marcador.
  2. Registra la tarea programada «Respaldo Personales RM» (al iniciar sesión y cada 5 minutos; solo actúa si el disco está conectado).
  Uso:  powershell -ExecutionPolicy Bypass -File "C:\Personales RM\_Respaldo\CONFIGURAR_RESPALDO.ps1" [-Letra D]
#>
param([string]$Letra = '')

$Carpeta = 'Respaldo Personales RM'; $Marca = '.respaldo_marker'
$Aqui = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Letra) {
    $usb = @(Get-Disk | Where-Object BusType -eq 'USB' | Get-Partition | Where-Object DriveLetter | ForEach-Object { $_.DriveLetter })
    if ($usb.Count -eq 1) { $Letra = $usb[0] }
    elseif ($usb.Count -gt 1) { Write-Host "Hay varios discos USB ($($usb -join ', ')). Vuelve a correr con -Letra X"; exit 1 }
    else { Write-Host "No encontré ningún disco USB conectado. Conéctalo y vuelve a correr este script."; exit 1 }
}
$Letra = $Letra.TrimEnd(':').ToUpper()
if (-not (Test-Path "${Letra}:\")) { Write-Host "La unidad ${Letra}: no existe."; exit 1 }

$dir = "${Letra}:\$Carpeta"
New-Item -ItemType Directory -Force $dir | Out-Null
Set-Content -Path (Join-Path $dir $Marca) -Value ("Disco de respaldo de C:\Personales RM`r`nConfigurado: {0}`r`nEquipo: {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm'), $env:COMPUTERNAME) -Encoding UTF8
Write-Host "Disco de respaldo: ${Letra}:  (carpeta: $dir)" -ForegroundColor Green

# Tarea programada (lanzada con un .vbs para que no parpadee ninguna ventana)
$vbs = Join-Path $Aqui 'RESPALDAR_OCULTO.vbs'
$accion = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument ('//B //Nologo "{0}"' -f $vbs)
$trigLogon = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigRep = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
$ajustes = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 6)
Register-ScheduledTask -TaskName 'Respaldo Personales RM' -Action $accion -Trigger @($trigLogon, $trigRep) -Settings $ajustes `
    -Description 'Copia lo nuevo de C:\Personales RM al disco externo cuando está conectado (al conectarlo y 1 vez al día).' -Force | Out-Null
Write-Host "Tarea programada «Respaldo Personales RM» registrada (se revisa cada 5 minutos)." -ForegroundColor Green
Write-Host "Para respaldar ya mismo: doble clic en RESPALDAR_AHORA.bat"
