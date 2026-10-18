@echo off
title Mantener la PC siempre encendida (servidor Inmuebles)
color 0A
cls
echo.
echo  ============================================================
echo    MANTENER LA PC ENCENDIDA (para acceso remoto sin cortes)
echo    Evita reposo/hibernacion. Ejecutar UNA vez por cliente.
echo  ============================================================
echo.

REM -- Requiere administrador --
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  ERROR: Debes ejecutar este archivo como ADMINISTRADOR.
    echo  (clic derecho -^> Ejecutar como administrador)
    echo.
    pause
    exit /b 1
)

echo  [1/4] Desactivando la suspension (conectado y con bateria) ...
powercfg /change standby-timeout-ac 0
powercfg /change standby-timeout-dc 0

echo  [2/4] Desactivando la hibernacion ...
powercfg /change hibernate-timeout-ac 0
powercfg /change hibernate-timeout-dc 0
powercfg /hibernate off

echo  [3/4] Cerrar la tapa (laptop) = No hacer nada (conectado) ...
powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0
powercfg /setactive SCHEME_CURRENT

echo  [4/4] Evitar que la tarjeta de red se apague para ahorrar energia ...
powershell -NoProfile -Command ^
  "try { Get-NetAdapter -Physical | ForEach-Object { $n=$_.Name; Get-CimInstance -Namespace root\wmi -ClassName MSPower_DeviceEnable -ErrorAction SilentlyContinue } ; Get-CimInstance -ClassName Win32_NetworkAdapter -Filter 'PhysicalAdapter=TRUE' | Out-Null } catch {}" >nul 2>&1

echo.
echo  ============================================================
echo    LISTO. La PC ya no entrara en reposo.
echo    El monitor puede apagarse (no afecta el acceso remoto).
echo.
echo    Recomendado revisar manualmente UNA vez:
echo    Administrador de dispositivos -^> Adaptadores de red -^>
echo    (tu adaptador) -^> Propiedades -^> Administracion de energia
echo    -^> DESMARCAR "Permitir que el equipo apague este
echo       dispositivo para ahorrar energia".
echo  ============================================================
echo.
pause
