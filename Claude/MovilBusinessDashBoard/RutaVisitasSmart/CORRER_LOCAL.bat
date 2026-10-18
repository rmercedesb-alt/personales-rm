@echo off
cd /d "%~dp0"
if not exist node_modules (
  echo Instalando dependencias raiz...
  call npm install
)
if not exist server\node_modules (
  echo Instalando dependencias del servidor...
  call npm install --prefix server
)
if not exist client\node_modules (
  echo Instalando dependencias del cliente...
  call npm install --prefix client
)
echo Iniciando RutaVisitasSmart (API en :4000, app en :5173)...
call npm run dev
