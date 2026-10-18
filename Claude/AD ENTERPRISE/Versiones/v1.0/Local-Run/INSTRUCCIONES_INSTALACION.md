# Instrucciones de Instalación — AD ALL DIGITAL ENTERPRISE v1.0

## Requisitos previos

1. **Instalar .NET 8 SDK** (solo una vez):
   - Descargar desde: https://dotnet.microsoft.com/download/dotnet/8.0
   - Descargar el instalador de Windows x64 (SDK)
   - Ejecutar el instalador y seguir los pasos

2. **Archivo credentials.json** (Google API):
   - Ver sección "Configurar Google API" más abajo
   - Colocar el archivo `credentials.json` dentro de la carpeta `ADEnterprise\`

3. **Logo de la empresa**:
   - Colocar el archivo del logo como `ADEnterprise\wwwroot\images\logo-fm.png`

---

## Cómo iniciar el sistema

1. Hacer doble clic en **INICIAR.bat**
2. Esperar que aparezca el mensaje de inicio (unos segundos)
3. Abrir el navegador en: **http://localhost:5000**
4. Usuario inicial: `admin` — Contraseña: `Admin2024!`
   ⚠️ **Cambiar la contraseña del admin en el primer inicio**

---

## Acceso remoto desde internet (Cloudflare Tunnel — GRATIS)

Para que otras personas puedan entrar desde fuera de su red:

1. Instalar Cloudflare Tunnel:
   - Descargar `cloudflared.exe` desde: https://github.com/cloudflare/cloudflared/releases
   - Guardar el archivo en `C:\cloudflared\cloudflared.exe`

2. Ejecutar el túnel (mientras el sistema está corriendo):
   ```
   cloudflared tunnel --url http://localhost:5000
   ```

3. Cloudflare le dará una URL pública como:
   `https://algonombre.trycloudflare.com`
   
4. Comparta esa URL con sus usuarios — funciona desde cualquier lugar

---

## Configurar Google API (credentials.json)

1. Ir a: https://console.cloud.google.com
2. Crear proyecto nuevo: "AD Enterprise"
3. Activar la API: **Google Sheets API**
4. Crear credencial: **Service Account**
   - Nombre: "ad-enterprise-app"
   - Rol: Editor
5. Descargar el JSON de la cuenta de servicio
6. Renombrar el archivo a `credentials.json`
7. Copiarlo en la carpeta `ADEnterprise\`
8. **MUY IMPORTANTE:** En Google Sheets, compartir el spreadsheet
   con el correo de la cuenta de servicio (aparece en el JSON como `client_email`)

---

## Configurar ID del Spreadsheet

En el archivo `ADEnterprise\appsettings.json`:

```json
"Google": {
    "SpreadsheetId": "AQUI_VA_EL_ID_DEL_SPREADSHEET"
}
```

El ID está en la URL del Google Sheets:
`https://docs.google.com/spreadsheets/d/**[ESTE_ES_EL_ID]**/edit`

---

## Primer uso

1. Iniciar sesión con `admin` / `Admin2024!`
2. Ir a **Usuarios** → Cambiar contraseña del admin
3. Ir a **Mantenimiento → Compañías** → Crear las compañías
4. Ir a **Mantenimiento → Clientes** → Crear los clientes
5. Si tiene datos históricos: **Mantenimiento → Importar Datos**
6. ¡Listo para operar!

---

*AD ALL DIGITAL ENTERPRISE v1.0 — 2026*
