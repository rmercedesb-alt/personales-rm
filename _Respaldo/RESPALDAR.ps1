<#
  RESPALDAR.ps1  —  Respaldo automático de  C:\Personales RM  al disco externo.
  ============================================================================
  Lo ejecuta SOLO la tarea programada «Respaldo Personales RM» (cada 5 minutos y al iniciar sesión). Cada vez:
    1. Busca el disco de respaldo (el que tiene la carpeta «Respaldo Personales RM\.respaldo_marker»).
    2. Si NO está conectado: no hace nada.
    3. Si está conectado y (a) acaba de conectarse, o (b) pasaron 24 horas o más desde el último respaldo, o (c) usas -Forzar:
       copia al disco TODO lo nuevo o modificado de C:\Personales RM.
  NO borra nada en el disco: lo que borres en la PC se conserva en el respaldo (es respaldo, no espejo).
  Se omiten carpetas que se regeneran solas: node_modules, bin, obj, __pycache__, .wwebjs_cache.
  Registro: C:\Personales RM\_Respaldo\logs\respaldo_AAAA-MM-DD_HHmm.log
#>
param([switch]$Forzar)

$Origen   = 'C:\Personales RM'
$Carpeta  = 'Respaldo Personales RM'
$Marca    = '.respaldo_marker'
$EstadoF  = Join-Path $Origen '_Respaldo\estado.json'
$LogDir   = Join-Path $Origen '_Respaldo\logs'
New-Item -ItemType Directory -Force $LogDir | Out-Null

# Una sola ejecución a la vez
$mutex = New-Object System.Threading.Mutex($false, 'Global\RespaldoPersonalesRM')
if (-not $mutex.WaitOne(0)) { exit }
try {
    function Leer-Estado { if (Test-Path $EstadoF) { try { return Get-Content $EstadoF -Raw | ConvertFrom-Json } catch {} }; return [pscustomobject]@{ conectado = $false; ultimo = $null; codigo = $null } }
    function Guardar-Estado($e) { $e | ConvertTo-Json | Set-Content $EstadoF -Encoding UTF8 }
    function Avisar($titulo, $texto) {
        try {
            Add-Type -AssemblyName System.Windows.Forms
            $n = New-Object System.Windows.Forms.NotifyIcon
            $n.Icon = [System.Drawing.SystemIcons]::Information; $n.Visible = $true
            $n.ShowBalloonTip(6000, $titulo, $texto, [System.Windows.Forms.ToolTipIcon]::Info)
            Start-Sleep -Seconds 7; $n.Dispose()
        } catch {}
    }

    # 1) ¿Está el disco de respaldo conectado?
    $disco = $null
    foreach ($d in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue | Where-Object { $_.Name.Length -eq 1 -and $_.Name -ne 'C' })) {
        if (Test-Path (Join-Path "$($d.Name):\" "$Carpeta\$Marca")) { $disco = "$($d.Name):"; break }
    }
    $estado = Leer-Estado
    if (-not $disco) {
        if ($estado.conectado) { $estado.conectado = $false; Guardar-Estado $estado }
        exit
    }

    # 2) ¿Toca respaldar?
    $acabaDeConectar = -not $estado.conectado
    $horas = if ($estado.ultimo) { (New-TimeSpan -Start ([datetime]$estado.ultimo) -End (Get-Date)).TotalHours } else { 9999 }
    if (-not ($Forzar -or $acabaDeConectar -or $horas -ge 24)) {
        if (-not $estado.conectado) { $estado.conectado = $true; Guardar-Estado $estado }
        exit
    }

    # 3) Copiar lo nuevo / modificado
    $destino = Join-Path "$disco\" "$Carpeta\Personales RM"
    $log = Join-Path $LogDir ("respaldo_{0}.log" -f (Get-Date -Format 'yyyy-MM-dd_HHmm'))
    New-Item -ItemType Directory -Force $destino | Out-Null
    $t0 = Get-Date
    robocopy $Origen $destino /E /COPY:DAT /DCOPY:DAT /MT:8 /R:1 /W:1 /XJ /NP /NDL `
        /XD node_modules bin obj __pycache__ .wwebjs_cache '$RECYCLE.BIN' 'System Volume Information' `
        /XF Thumbs.db '~$*' /LOG:$log | Out-Null
    $codigo = $LASTEXITCODE            # 0-7 = correcto (bit 1 = hubo archivos copiados); 8 o más = hubo errores
    $copiados = 0
    $linea = Select-String -Path $log -Pattern '^\s*(Files|Archivos)\s*:' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($linea) { $nums = [regex]::Matches($linea.Line, '\d+') | ForEach-Object { [int]$_.Value }; if ($nums.Count -ge 2) { $copiados = $nums[1] } }

    $estado = [pscustomobject]@{ conectado = $true; ultimo = (Get-Date).ToString('o'); codigo = $codigo; copiados = $copiados; segundos = [int]((Get-Date) - $t0).TotalSeconds; disco = $disco }
    Guardar-Estado $estado
    if ($codigo -lt 8) { Avisar 'Respaldo Personales RM' ("Respaldo completado en $disco — archivos nuevos o cambiados: $copiados") }
    else { Avisar 'Respaldo Personales RM' ("El respaldo terminó con errores (código $codigo). Revisa: $log") }
}
finally { $mutex.ReleaseMutex() }
