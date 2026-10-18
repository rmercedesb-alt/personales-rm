# Crea los accesos directos en el Escritorio
param([string]$Root)

$desktop = [Environment]::GetFolderPath('Desktop')
$ws = New-Object -ComObject WScript.Shell

# 1) Sistema Inmuebles V5  -> INICIAR_V5.bat
$bat = Join-Path $Root 'web-app-v5\INICIAR_V5.bat'
$sc1 = $ws.CreateShortcut((Join-Path $desktop 'Sistema Inmuebles V5.lnk'))
$sc1.TargetPath = "$env:SystemRoot\System32\cmd.exe"
$sc1.Arguments  = "/c `"$bat`""
$sc1.WorkingDirectory = (Split-Path $bat)
$sc1.IconLocation = (Join-Path $Root 'web-app-v5\icono-inmuebles.ico')
$sc1.Description = "Sistema de Administracion de Inmuebles V5"
$sc1.Save()

# 2) En Linea con mis clientes  -> node server.js
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $node) { $node = 'C:\Program Files\nodejs\node.exe' }
$proj = Join-Path $Root 'en-linea-con-mis-clientes'
$sc2 = $ws.CreateShortcut((Join-Path $desktop 'En Linea con mis clientes.lnk'))
$sc2.TargetPath = $node
$sc2.Arguments  = 'server.js'
$sc2.WorkingDirectory = $proj
$sc2.IconLocation = "C:\Program Files\Google\Chrome\Application\chrome.exe,0"
$sc2.Description = "Modulo de WhatsApp para cobranza"
$sc2.Save()

Write-Output "Accesos directos creados en el Escritorio."
