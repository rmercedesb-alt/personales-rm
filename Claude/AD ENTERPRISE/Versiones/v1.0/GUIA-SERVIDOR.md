# Guía de Instalación en Servidor — AD ALL DIGITAL ENTERPRISE

## OPCIÓN A — PC en oficina + Tailscale VPN (RECOMENDADO)

### Requisitos
- PC/laptop con Windows 10/11 que quede SIEMPRE encendida
- Internet estable
- .NET 8 Runtime instalado

### Paso 1 — Instalar .NET 8 en el servidor
Descargar de: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
→ "Run apps" → ".NET Desktop Runtime 8.x" para Windows

### Paso 2 — Copiar los archivos
Copiar toda la carpeta `Local-Run` al servidor, ej:
  C:\AD-Enterprise\

Asegúrese que estén:
  - ADEnterprise\ (el proyecto)
  - INICIAR.bat
  - ADEnterprise\credentials.json  ← MUY IMPORTANTE

### Paso 3 — Instalar Tailscale (VPN gratuita)
1. Ir a https://tailscale.com/download → descargar para Windows
2. Instalar en el SERVIDOR (la PC de oficina)
3. Hacer login con Google o email
4. Anotar la IP de Tailscale del servidor (ej: 100.94.x.x)

5. En CADA equipo desde donde quiera acceder (casa, teléfono):
   - Instalar Tailscale también
   - Login con la MISMA cuenta
   - Listo — ya puede acceder a http://100.94.x.x:5000

### Paso 4 — Arrancar automáticamente con Windows
Para que el sistema arranque solo cuando se encienda el servidor:

1. Presionar Win+R → escribir: shell:startup
2. Crear acceso directo de INICIAR.bat en esa carpeta
3. Listo — el sistema arranca automáticamente

### Paso 5 — Firewall de Windows
Abrir puerto 5000 (si no funciona desde otros equipos):
1. Inicio → "Firewall de Windows con seguridad avanzada"
2. Reglas de entrada → Nueva regla
3. Puerto → TCP → 5000 → Permitir la conexión → Guardar

---

## OPCIÓN B — Servidor en la Nube (Railway.app)

### Costo: ~$5/mes USD
### Ventaja: No necesita VPN, accesible desde cualquier lugar

### Requisitos previos
- Cuenta en https://railway.app
- Cuenta en GitHub
- Docker o Dockerfile configurado

### El credentials.json en la nube
⚠️  NUNCA subir credentials.json a GitHub.
En Railway, configurar como variable de entorno:
  GOOGLE_CREDENTIALS_JSON = (contenido del archivo en base64)

### Pasos (cuando esté listo)
1. Publicar código en GitHub (privado)
2. En Railway: New Project → Deploy from GitHub
3. Configurar variables de entorno:
   - ASPNETCORE_ENVIRONMENT = Production
   - ASPNETCORE_URLS = http://0.0.0.0:$PORT
   - GOOGLE_CREDENTIALS_JSON = (base64 del credentials.json)
4. Railway asigna URL automáticamente: https://xxx.railway.app

---

## RESUMEN RÁPIDO

| Situación | Acceso |
|-----------|--------|
| En la oficina (misma red) | http://192.168.x.x:5000 |
| Fuera de la oficina + Tailscale activo | http://100.94.x.x:5000 |
| En la nube (Railway) | https://su-app.railway.app |

---

## Tailscale — Links de descarga
- Windows: https://tailscale.com/download/windows
- Android: Play Store → "Tailscale"
- iPhone: App Store → "Tailscale"
- Mac: https://tailscale.com/download/mac

La IP de Tailscale de su servidor siempre empieza con 100.x.x.x
y es la misma mientras tenga la misma cuenta, aunque cambie de red.
