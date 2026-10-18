#Requires -RunAsAdministrator
<#
  AGREGAR_HTTPS_IIS.ps1
  ======================
  Agrega un certificado SSL autofirmado al sitio IIS y habilita HTTPS.
  Los navegadores mostraran una advertencia (es normal para certificados internos).
  El usuario la acepta una vez y ya no vuelve a aparecer en ese dispositivo.
#>

$IISSite = 'Default Web Site'   # Cambiar si tu sitio tiene otro nombre

Import-Module WebAdministration

# ── 1. Crear certificado autofirmado ─────────────────────────────────────────
Write-Host "Creando certificado SSL autofirmado..." -ForegroundColor Yellow

$ip       = (Get-NetIPAddress -AddressFamily IPv4 |
             Where-Object { $_.InterfaceAlias -notlike '*Loopback*' } |
             Select-Object -First 1).IPAddress
$hostname = $env:COMPUTERNAME

$cert = New-SelfSignedCertificate `
    -DnsName $hostname, $ip, "localhost" `
    -CertStoreLocation "cert:\LocalMachine\My" `
    -NotAfter (Get-Date).AddYears(5) `
    -FriendlyName "Dashboard Visitas IIS"

Write-Host "   Certificado creado: $($cert.Thumbprint)" -ForegroundColor Green

# ── 2. Agregar binding HTTPS en el sitio IIS ──────────────────────────────────
Write-Host "Agregando binding HTTPS (puerto 443)..." -ForegroundColor Yellow

# Quitar binding HTTPS previo si existe
Get-WebBinding -Name $IISSite -Protocol https -ErrorAction SilentlyContinue |
    Remove-WebBinding -ErrorAction SilentlyContinue

# Agregar nuevo binding HTTPS
New-WebBinding -Name $IISSite -Protocol https -Port 443 -IPAddress "*"

# Asignar el certificado al binding
$bind = Get-WebBinding -Name $IISSite -Protocol https
$bind.AddSslCertificate($cert.Thumbprint, "My")

Write-Host "   HTTPS habilitado en puerto 443." -ForegroundColor Green

# ── 3. Abrir puerto 443 en el firewall ────────────────────────────────────────
Write-Host "Abriendo puerto 443 en firewall..." -ForegroundColor Yellow
New-NetFirewallRule -DisplayName "IIS HTTPS Dashboard" `
    -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow `
    -ErrorAction SilentlyContinue | Out-Null
Write-Host "   Puerto 443 abierto." -ForegroundColor Green

# ── Resumen ───────────────────────────────────────────────────────────────────
Write-Host "`n=================================================" -ForegroundColor Cyan
Write-Host "  HTTPS CONFIGURADO" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Accede desde el celular con:"
Write-Host "    https://$ip/RMUtiles" -ForegroundColor White
Write-Host ""
Write-Host "  El navegador mostrara una advertencia de"
Write-Host "  certificado la PRIMERA vez. Es normal."
Write-Host "  Toca 'Avanzado' -> 'Continuar de todas formas'"
Write-Host "  y no volvera a aparecer en ese dispositivo."
Write-Host "=================================================" -ForegroundColor Cyan
Pause
