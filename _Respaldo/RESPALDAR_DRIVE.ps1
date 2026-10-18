<#
  RESPALDAR_DRIVE.ps1  -  Copia comprimida de  C:\Personales RM  a Google Drive (unidad G:), UN ZIP POR PROYECTO.
  ==============================================================================================================
  Lo ejecuta la tarea programada «Respaldo Personales RM a Drive» (al iniciar sesión y cada hora; actúa 1 vez al día).
  Genera  G:\Mi unidad\Respaldos\AAAA-MM-DD\<proyecto>.zip  (un zip por carpeta de proyecto de Claude, más ALL DIGITAL_License, _Respaldo
  y «Claude - (archivos sueltos)»). Una carpeta que pese más de 1.5 GB sin comprimir se parte en un zip por subcarpeta
  (así cada zip queda bajo 2 GB y el Explorador de Windows lo puede abrir con doble clic).
  Los zips guardan la ruta relativa a C:\Personales RM, de modo que extraer todos dentro de C:\Personales RM reconstruye el árbol.
  Incluye credenciales (el Drive es privado; GitHub no las lleva). Omite: node_modules, bin, obj, __pycache__, .wwebjs_cache, wa-session, .git.
  Conserva las últimas 3 fechas. Si G: no está disponible, no hace nada y reintenta en la siguiente hora.
  Uso manual:  powershell -ExecutionPolicy Bypass -File RESPALDAR_DRIVE.ps1 -Forzar
#>
param([switch]$Forzar, [string]$Letra = 'G', [int]$Conservar = 3, [double]$LimiteGB = 1.5)

$Origen  = 'C:\Personales RM'
$Raiz    = "${Letra}:\Mi unidad\Respaldos"
$Estado  = Join-Path $Origen '_Respaldo\estado_drive.json'
$LogDir  = Join-Path $Origen '_Respaldo\logs'
$Omitir  = @('node_modules', 'bin', 'obj', '__pycache__', '.wwebjs_cache', 'wa-session', '.git', '$RECYCLE.BIN', 'System Volume Information')
New-Item -ItemType Directory -Force $LogDir | Out-Null
$log = Join-Path $LogDir ("drive_{0}.log" -f (Get-Date -Format 'yyyy-MM-dd_HHmm'))
function Log($m) { ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $m) | Add-Content -Path $log -Encoding UTF8 }

$mutex = New-Object System.Threading.Mutex($false, 'Global\RespaldoPersonalesRM_Drive')
if (-not $mutex.WaitOne(0)) { exit }
try {
    if (-not (Test-Path "${Letra}:\Mi unidad")) { exit }                       # Drive no disponible: reintenta luego
    $hoy = Get-Date -Format 'yyyy-MM-dd'
    $dest = Join-Path $Raiz $hoy
    $marca = Join-Path $dest '_COMPLETO.txt'
    if ((Test-Path $marca) -and -not $Forzar) { exit }                         # ya respaldado hoy
    if ($Forzar -and (Test-Path $dest)) { Remove-Item -LiteralPath $dest -Recurse -Force }   # rehace la copia de hoy desde cero
    New-Item -ItemType Directory -Force $dest | Out-Null
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $t0 = Get-Date; $script:tot = [pscustomobject]@{ zips = 0; archivos = 0; err = 0; mb = 0.0 }
    Log "Inicio. Destino=$dest"

    function Omitido($ruta) { return ($Omitir -contains (Split-Path $ruta -Leaf)) }
    function Es-Enlace($ruta) { return [bool]((Get-Item -LiteralPath $ruta -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) }
    function Tam-Carpeta($dir) {
        $t = 0L; $pila = New-Object System.Collections.Generic.Stack[string]; $pila.Push($dir)
        while ($pila.Count -gt 0) {
            $d = $pila.Pop()
            try { foreach ($f in [IO.Directory]::GetFiles($d)) { $t += (New-Object IO.FileInfo $f).Length }
                  foreach ($s in [IO.Directory]::GetDirectories($d)) { if (-not (Omitido $s) -and -not (Es-Enlace $s)) { $pila.Push($s) } } } catch {}
        }
        return $t
    }
    # Crea un zip con: los archivos directos de $dir (si $sueltos) o todo el contenido de $dir (si no)
    function Empacar($dir, $nombre, [switch]$soloSueltos, [switch]$sinZipsRaiz) {
        $tmp = Join-Path $env:TEMP ("pers_rm_{0}.zip" -f [guid]::NewGuid().ToString('N'))
        $n = 0; $zip = [System.IO.Compression.ZipFile]::Open($tmp, 'Create')
        try {
            $pila = New-Object System.Collections.Generic.Stack[string]; $pila.Push($dir); $primero = $true
            while ($pila.Count -gt 0) {
                $d = $pila.Pop()
                try { $files = [IO.Directory]::GetFiles($d); $subs = [IO.Directory]::GetDirectories($d) } catch { $script:tot.err++; Log "ERR dir $d : $($_.Exception.Message)"; continue }
                if (-not ($soloSueltos -and -not $primero)) {
                    foreach ($s in $subs) { if (-not $soloSueltos -and -not (Omitido $s) -and -not (Es-Enlace $s)) { $pila.Push($s) } }
                }
                foreach ($f in $files) {
                    $nom = Split-Path $f -Leaf
                    if ($nom -eq 'Thumbs.db' -or $nom.StartsWith('~$')) { continue }
                    $rel = $f.Substring($Origen.Length + 1).Replace('\', '/')
                    if ($rel -like '_Respaldo/logs/*') { continue }
                    if ($sinZipsRaiz -and $d -eq $dir -and $nom.EndsWith('.zip')) { continue }   # instaladores: se copian tal cual, sin recomprimir
                    try { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f, $rel, [System.IO.Compression.CompressionLevel]::Optimal); $n++ }
                    catch { $script:tot.err++; Log "ERR archivo $f : $($_.Exception.Message)" }
                }
                $primero = $false
            }
        } finally { $zip.Dispose() }
        if ($n -eq 0) { Remove-Item -LiteralPath $tmp -Force; return }
        $mb = [math]::Round((Get-Item $tmp).Length / 1MB, 1)
        $final = Join-Path $dest ((($nombre -replace '[\\/:*?"<>|]', '_')) + '.zip')
        Copy-Item -LiteralPath $tmp -Destination "$final.part" -Force
        Move-Item -LiteralPath "$final.part" -Destination $final -Force
        Remove-Item -LiteralPath $tmp -Force
        $script:tot.zips++; $script:tot.archivos += $n; $script:tot.mb += $mb
        Log ("OK  {0}  ({1} archivos, {2} MB)" -f (Split-Path $final -Leaf), $n, $mb)
    }
    # Un proyecto: si es grande se parte por subcarpetas
    function Proyecto($dir, $nombre) {
        if ((Split-Path $dir -Leaf) -eq 'Instalables') {
            # Carpeta de instaladores: los .zip ya están comprimidos -> se copian tal cual a una subcarpeta; el resto va en un solo zip
            Empacar $dir "$nombre (sin los instaladores)" -sinZipsRaiz
            $paq = Join-Path $dest ("$nombre - instaladores .zip")
            New-Item -ItemType Directory -Force $paq | Out-Null
            foreach ($z in [IO.Directory]::GetFiles($dir, '*.zip')) { Copy-Item -LiteralPath $z -Destination $paq -Force; $script:tot.archivos++; $script:tot.mb += [math]::Round((New-Object IO.FileInfo $z).Length / 1MB, 1) }
            Log "OK  carpeta '$nombre - instaladores .zip' (instaladores copiados sin recomprimir)"
            return
        }
        $gb = (Tam-Carpeta $dir) / 1GB
        if ($gb -le $LimiteGB) { Empacar $dir $nombre; return }
        Log ("Parto '{0}' ({1:N1} GB sin comprimir) por subcarpetas" -f $nombre, $gb)
        Empacar $dir "$nombre - (archivos sueltos)" -soloSueltos
        foreach ($s in [IO.Directory]::GetDirectories($dir)) {
            if ((Omitido $s) -or (Es-Enlace $s)) { continue }
            Proyecto $s ("$nombre - " + (Split-Path $s -Leaf))
        }
    }

    # Raíz de C:\Personales RM: archivos sueltos + cada carpeta
    Empacar $Origen '(raiz)' -soloSueltos
    foreach ($c in [IO.Directory]::GetDirectories($Origen)) {
        if ((Omitido $c) -or (Es-Enlace $c)) { continue }
        $nombre = Split-Path $c -Leaf
        if ($nombre -eq 'Claude') {
            Empacar $c 'Claude - (archivos sueltos)' -soloSueltos
            foreach ($p in [IO.Directory]::GetDirectories($c)) {
                if ((Omitido $p) -or (Es-Enlace $p)) { continue }
                Proyecto $p ("Claude - " + (Split-Path $p -Leaf))
            }
        } else { Proyecto $c $nombre }
    }

    $seg = [int]((Get-Date) - $t0).TotalSeconds
    "Respaldo completo $(Get-Date -Format 's'). Zips: $($script:tot.zips)  Archivos: $($script:tot.archivos)  MB: $($script:tot.mb)  Errores: $($script:tot.err)" | Set-Content $marca -Encoding UTF8
    # Rotación: conservar solo las últimas N fechas (carpetas AAAA-MM-DD)
    Get-ChildItem -LiteralPath $Raiz -Directory | Where-Object Name -match '^\d{4}-\d{2}-\d{2}$' | Sort-Object Name -Descending | Select-Object -Skip $Conservar |
        ForEach-Object { Log "Rotación: elimino carpeta $($_.Name)"; Remove-Item -LiteralPath $_.FullName -Recurse -Force }
    [pscustomobject]@{ ultimo = (Get-Date).ToString('o'); zips = $script:tot.zips; archivos = $script:tot.archivos; mb = $script:tot.mb; errores = $script:tot.err; segundos = $seg; carpeta = $dest } | ConvertTo-Json | Set-Content $Estado -Encoding UTF8
    Log "Fin OK en $seg s"
}
finally { $mutex.ReleaseMutex() }
