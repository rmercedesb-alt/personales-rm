# Porfi Academy — Bitácora de progreso

> Última actualización: 2026-10-11
> Regla: este archivo se actualiza cada vez que haya un cambio, decisión o avance en el proyecto.
> Todo lo de Porfi Academy vive en esta carpeta (`MovilBusinessDashBoard\Porfi Academy`). Es un proyecto **independiente** del dashboard MovilBusiness.

## Qué es
Plataforma web (PWA) de videos por suscripción, estilo "Netflix + curso por niveles", inspirada en https://www.topvelocity.net/. Es para un cliente. Nombre provisional: **Porfi Academy** (el nombre final se decide después).

Los miembros acceden con usuario y contraseña a videos avanzados (los básicos serán gratuitos). Para pasar al siguiente nivel:
1. Deben haber visto los videos del nivel anterior (seguimiento de visualización).
2. Deben subir un video de su propio avance, que el coach/admin revisa y aprueba.

Formas de pago/activación de la suscripción:
- Tarjeta de crédito (recurrente).
- Depósito a la cuenta del dueño (el admin confirma manualmente).
- PIN/código que el dueño entrega después de recibir el pago.

Requisito de seguridad: los videos no deben poder copiarse ni accederse con una dirección directa sin autorización.

## Decisiones tomadas
- **Construcción a la medida** (no turnkey tipo Uscreen/Vimeo OTT), porque el flujo de depósito + PIN y la revisión de avance por coach no encajan bien en plataformas turnkey.
- **Web-first / PWA instalable** primero. Apps nativas Android/iOS se evalúan después de validar el modelo.
- **Backend: ASP.NET Core 8 MVC + Identity** (mismo stack que ya usa el usuario). Frontend liviano (CSS propio, sin Bootstrap/jQuery); el bajo consumo en el celular depende sobre todo del streaming adaptativo (HLS).
- **Base de datos: SQLite** para el esqueleto (cero configuración, fácil de instalar). Cambiable a SQL Server más adelante si hace falta (cambio de proveedor EF). *Cambio respecto al plan inicial, que decía SQL Server.*
- **Carpeta de datos** (`DataDir`): en desarrollo `%LOCALAPPDATA%\PorfiAcademy` (fuera de OneDrive para no corromper SQLite); en producción `App_Data` o la ruta configurada en `DataDir`. Ahí viven la BD, las llaves de cifrado y `secrets.dat`.
- **Video: Mux** (HLS adaptativo + tokens de reproducción firmados y de corta duración). Mux no tiene plan gratuito permanente; se usa el environment **Desarrollo** para pruebas sin costo. DRM completo (Widevine/FairPlay) es de pago; al inicio se usa protección por token firmado.
- **Credenciales de Mux:** la app las pide en la pantalla de instalación (`/Setup`) y las guarda **cifradas** (ASP.NET Data Protection + DPAPI en Windows) en `secrets.dat`. Nunca en código, appsettings ni repo.
- **Seguridad de /Setup:** mientras la instalación no esté completa, todo redirige a `/Setup`. Solo se permite desde el propio servidor (localhost); desde otra ubicación exige el código `Setup:Code` configurado. Al completarse, `/Setup` deja de estar disponible.
- **Seguridad de credenciales del usuario:** los valores de Mux los maneja el usuario por su cuenta; no se pegan en el chat y Claude no abre el archivo `.env` descargado.

## Estructura del código (esqueleto, 2026-10-11)
`src/PorfiAcademy/`
- `Program.cs` — DataDir, Data Protection, Identity con roles, redirección a /Setup, migraciones automáticas.
- `Services/SecretStore.cs` — `ISecretStore` / `FileSecretStore` (credenciales cifradas), `DataPaths`.
- `Services/MuxService.cs` — prueba de conexión y listado de assets contra la API de Mux (Basic auth).
- `Controllers/SetupController.cs` — wizard de instalación (admin + Mux; verifica contra Mux, con opción «guardar sin verificar»).
- `Controllers/AdminController.cs` — panel admin (solo rol Admin): estado de Mux y lista de videos.
- `Controllers/MembersController.cs` — zona de miembros (placeholder, requiere login).
- `Views/` — layout rojo/negro estilo TopVelocity (mobile-first), landing, Setup, Admin, Members.
- Login/registro: páginas por defecto de Identity (hoy en inglés; pendiente traducir/estilizar).
- `INICIAR_PORFI_ACADEMY.bat` (raíz de esta carpeta) — arranca local en http://localhost:5273.
- Se añadió la entrada `porfi-academy` a `MovilBusinessDashBoard\.claude\launch.json` (para la vista previa de Claude).

## Verificado (2026-10-11)
Con datos de prueba falsos (ya borrados): redirección a /Setup, verificación contra Mux rechaza credenciales falsas con mensaje claro, instalación con «guardar sin verificar», login, `/Admin` protegido por rol, `secrets.dat` cifrado (no contiene texto plano), portada correcta en vista móvil. Los datos de prueba de `%LOCALAPPDATA%\PorfiAcademy` se eliminaron para que la instalación real parta limpia.

## Modelo de datos previsto (para pasadas siguientes)
Users, Plans, Subscriptions (origen: Stripe / Depósito / PIN), Courses > Levels > Videos (con MuxAssetId/PlaybackId), UserVideoProgress, LevelSubmissions (video de avance + estado Pendiente/Aprobado/Rechazado), AccessCodes (PINs), ManualDeposits.

## Estado actual
- [x] Investigación de mejores prácticas y modelo (híbrido Netflix + cursos + coach).
- [x] Decisiones de stack y alcance acordadas.
- [x] Cuenta de Mux creada (organización "Porfi Academy / Academia Porfi"). Existen los environments **Desarrollo** y **Producción**.
- [x] Carpeta del proyecto creada: `...\MovilBusinessDashBoard\Porfi Academy`.
- [x] Access token de Mux "PorfyAcademy" creado (permiso Video). El usuario tiene una foto del Secret y apareció `mux-access-token-PorfyAcademy.env` en esta carpeta (descarga). **Pendiente del usuario:** moverlo fuera de OneDrive a un lugar seguro después de instalar, y borrar la foto. Recomendado: revocar y crear un token nuevo si la foto estuvo en la nube.
- [x] Proyecto ASP.NET Core 8 creado y compila.
- [x] Landing page estilo TopVelocity (versión inicial, textos genéricos).
- [x] Login/registro (Identity por defecto).
- [x] Pantalla de instalación que pide la API Key de Mux.
- [x] **Instalación real completada por el usuario (2026-10-11)**: el wizard guardó admin + credenciales de Mux. **Confirmado: `/Admin` dice «conectado» con Mux (Desarrollo).** *(Original: ejecutar `INICIAR_PORFI_ACADEMY.bat`, abrir http://localhost:5273 y completar `/Setup` con su correo/contraseña de administrador y el Token ID + Secret de Mux (environment Desarrollo). Verificar que el panel `/Admin` diga «conectado».)*
- [x] Código de subida directa a Mux (`/Admin`), reproductor protegido (`/Members/Watch`) y firma JWT RS256 — escrito y compilando (2026-10-11). La firma se verificó con OpenSSL usando una clave de prueba.
- [ ] **Prueba de punta a punta con Mux real** (pendiente del usuario): crear la *signing key* en Mux (Desarrollo → Configuración → Claves de firma), pegarla en `/Admin`, subir un video corto y reproducirlo.

## Pasada de video protegido (2026-10-11) — cómo funciona
- Videos se crean en Mux con política **signed** (la URL pública no reproduce nada). Subida **directa** navegador → Mux (no pasa por nuestro servidor).
- La clave de firma (ID + clave privada) se pega en `/Admin` y se guarda **cifrada** en `secrets.dat` (mismo almacén que el token). Es distinta del token de API.
- Al abrir un video, el servidor firma 3 tokens JWT (video, miniatura, storyboard) que duran la duración del video + 1 hora. Reproductor: `@mux/mux-player` 3.14.0 (CDN jsdelivr).
- Marca de agua con el correo del usuario sobre el reproductor (desalienta compartir grabaciones).
- **Control de acceso:** *(reemplazado por el sistema de suscripciones, ver sección «Suscripciones y promociones»; el rol «Miembro» ya no se usa.)*
- `/Admin` («Videos y Mux») también permite eliminar videos de Mux.
- Datos desconocidos a confirmar en la prueba real: límites del environment Desarrollo de Mux (puede limitar duración/cantidad de videos); si Mux rechaza `playback_policy` en la creación del upload, el mensaje de error de Mux se muestra en pantalla (en ese caso probar `playback_policies`).

## Avances de la prueba real (2026-10-11)
- El usuario guardó la clave de firma y subió 2 videos de prueba ("Iguanas en Ocoa" 0:30, "Vinos Modernos" 0:34): ambos `ready` y con acceso «protegido» (subida directa y política signed funcionan con Mux real). Pendiente confirmar la reproducción con «Ver».
- Se corrigió el autocompletado del navegador en el formulario de clave de firma (ahora `textarea` oculto, `autocomplete=off`).
- **Navegación:** barra con botones «← Atrás» (historial, con respaldo a Inicio) y «⌂ Inicio» en todas las pantallas salvo la portada y /Setup (incluye las páginas de login/registro de Identity, que usan el mismo layout). Requisito del usuario: toda ventana nueva debe tener forma de volver/salir.

## Marca VeloX (2026-10-11)
- **Nombre de la página: VeloX** (logo: pelota de béisbol con estela + «VeloX»). «Porfi Academy» queda solo como nombre interno del proyecto/código (namespace `PorfiAcademy`, carpeta, `DataDir`); no se renombró para no romper nada.
- Archivos fuente de marca en esta carpeta: `Logo de la empresa.pdf` (imagen JPEG 1024×1024 sobre fondo azul marino) y `Nombre de la empresa.jpeg` (captura del mismo logo).
- Paleta tomada del logo: azul marino `#091020` (fondo), naranja `#F94D05` (pelota y X), crema `#FDF5DE` (texto «Velo»). Variables en `wwwroot/css/site.css`. El logo es crema → solo se ve bien sobre fondos oscuros, por eso el sitio es azul marino.
- Assets generados (fondo transparente) en `wwwroot/img/`: `logo.png` (pelota + VeloX, 810×196), `logo-ball.png` (solo pelota), `icon-192.png` / `icon-512.png` (para la PWA), y `wwwroot/favicon.ico`.
- Cabecera con el logo, título de pestaña «… - VeloX», pie «© 2026 VeloX», reproductor con acento naranja.
- Textos de la portada ajustados a béisbol («Entrenamiento de béisbol en video» / «Gana velocidad paso a paso»): son **provisionales**, falta el texto real del cliente.
- Login/registro de Identity: etiquetas de nuevo encima de los campos (CSS); siguen en inglés.
- Verificado en vista previa (escritorio y celular) con una copia temporal y datos falsos, ya eliminados.
- Si el cliente entrega el logo en vector (SVG/AI) o con fondo transparente, reemplazar `logo.png` para más nitidez.

## Suscripciones y promociones (2026-10-11) — prioridad pedida por el usuario para la demo
**Decisiones del usuario:** ir primero a suscripciones (para mostrar la demo con ellas); portada con carrusel promocional que rota cada 10 s (banners, fotos y cortes de video), todo terminando en suscribirse. **Decisión mía (reversible):** para ver *cualquier* video, incluso los gratis, hay que **crear cuenta** (así todo visitante pasa por el registro).

**Modelo de acceso** (`Services/AccessService.cs`): crear cuenta = plan gratuito (videos marcados «Gratis»); lo de pago exige suscripción vigente (o rol Admin). El acceso **vence solo**: se calcula contra `ExpiresAt`, no hay roles que quitar. Renovar antes de vencer **suma** los días al final; renovar una vencida empieza desde hoy. Suspender marca las suscripciones como revocadas. Tokens de reproducción ya emitidos duran la duración del video + 1 h.

**Tablas nuevas** (migración `AddSubscriptionsAndPromo`, se aplica sola al arrancar): `Plans` (con 3 planes de ejemplo Mensual 30 d / Trimestral 90 d / Anual 365 d, **precios «por definir»**), `Subscriptions`, `AccessCodes` (PINs), `SubscriptionRequests`, `PromoSlides`, `SiteSettings`, `VideoMetas` (gratis/de pago por video).

**Páginas públicas:** `/Subscribe` (planes, canje de PIN, datos de depósito, tarjeta «próximamente», formulario de solicitud: información / evaluación gratis / quiero suscribirme / ya hice mi depósito; con trampa anti-bots y límite por IP). Portada con carrusel (`wwwroot/js/carousel.js`): rota cada 10 s, flechas, puntos, barra de tiempo, pausa al pasar el mouse, desliza en celular, respeta «reducir movimiento», cuenta vistas y clics. Sin promociones propias muestra 3 de ejemplo.

**Centro de administración `/Manage`** (solo Admin; insignia con n.º de solicitudes nuevas en el menú): Resumen · Solicitudes (estados Nueva/Contactada/Aprobada/Rechazada, botón WhatsApp, «Aprobar y activar» crea la suscripción) · Suscripciones (dar días manualmente, suspender, estado Activo/Vencido/Suspendido/Gratis) · PINs (generar 1–50, formato `XXXX-XXXX-XXXX`, 12 caracteres sin I/O/0/1, un solo uso, vencimiento opcional, cancelar) · Planes (editar nombre/días/precio/orden/activo, crear) · Promociones (carrusel: subir imagen JPG/PNG/WebP ≤ 5 MB validada por firma real del archivo, fechas desde/hasta, orden, estadísticas) · Videos y Mux (`/Admin`: ahora con tipo de subida «curso protegido» o «corte promocional PÚBLICO», marcar video gratis/de pago, «Usar en carrusel») · Ajustes (datos de depósito, WhatsApp, correo).

**Seguridad:** canje de PIN atómico (si dos personas lo usan a la vez, solo una gana); 5 intentos fallidos de PIN bloquean 15 min; PINs no se muestran de nuevo salvo en la lista del admin; todo POST con antiforgery; imágenes del carrusel se guardan en la carpeta de datos con nombre GUID (no en wwwroot); destinos de botones del carrusel validados (solo rutas internas o http/https); cabeceras de proxy `X-Forwarded-Proto/Host` aceptadas (para túnel/hosting HTTPS) pero **no** `X-Forwarded-For`.

**Verificado (2026-10-11):** 18 pruebas automáticas de la lógica de acceso (vencimiento, suma de días, suspensión, PIN válido/usado/vencido/cancelado, carrera de 2 canjes simultáneos, bloqueo por intentos) → todas pasan. Prueba de punta a punta en navegador con datos falsos (ya borrados): visitante envía solicitud de depósito sin cuenta → admin ve insignia y la solicitud → se registra → admin aprueba → cliente ve «Suscripción Mensual activa hasta …»; PIN generado y canjeado por otro usuario escrito en minúsculas con espacios; usuario normal recibe «Acceso denegado» en todo `/Manage` y `/Admin`; carrusel rota exactamente cada 10 s; imagen subida visible y archivo falso rechazado; vistas/clics contados; vista móvil correcta.

**NO probado todavía (necesita Mux real):** reproducción de un video de pago con suscripción vencida/activa en vivo, subida de un *corte público* y «Usar en carrusel» con Mux real (la parte de Mux de los cortes públicos solo está verificada por código). Si Mux rechazara `playback_policy: ["public"]`, el error de Mux se muestra en pantalla.

**Ajustes tras la primera revisión del usuario (2026-10-11):**
- La portada ya no ofrece «Crear mi cuenta / Ver planes» a quien tiene sesión: el administrador ve «Panel de administración» (Ir a Administración / Ver mis cursos) y el cliente «Bienvenido de nuevo» (Ir a mis cursos / Mi suscripción). El visitante sin sesión sigue viendo «Crear mi cuenta». Aviso: el administrador veía los botones de visitante porque su navegador conservaba la sesión; para ver la portada como visitante: «Salir» o ventana privada.
- Entrar a `/Identity/Account/Register` o `/Login` con sesión iniciada redirige a `/?ya=1` con el aviso «Ya tienes la sesión iniciada como …» (antes dejaba crear otra cuenta).
- `/Manage/Subscriptions` muestra la columna **Tipo** (Administrador / Cliente); el administrador aparece como «Acceso total».
- Dónde viven los datos (desarrollo): `%LOCALAPPDATA%\PorfiAcademy` = `porfiacademy.db` (usuarios con contraseña **cifrada con hash**, roles, suscripciones, PINs, solicitudes, promociones, ajustes), `keys\` (llaves de cifrado), `secrets.dat` (credenciales de Mux cifradas), `uploads\promo\` (imágenes del carrusel). Quién es administrador = tiene el rol `Admin` (tablas `AspNetRoles` / `AspNetUserRoles`); los clientes no tienen rol y su acceso lo define la tabla `Subscriptions`. **Respaldar esa carpeta completa** (sin las llaves, la base y secrets.dat no se pueden leer).

## Banners del carrusel hechos con el video del cliente (2026-10-11)
- Fuente: `C:\Users\Rudy\Downloads\1er paso para tirar duro.mp4` (576×1024 vertical, 11,5 s, 24 fps; lanzador con guante naranja). Copia de respaldo en `Porfi Academy\Videos\1er paso para tirar duro.mp4`.
- Carpeta `Porfi Academy\Videos\`:
  - `Banners\` → 3 banners **1600×800 PNG sin pérdida** (postura inicial fotograma 0 / elevación de pierna fotograma 120 / brazo extendido fotograma 224).
  - `Banners\Para subir a la web\` → los mismos 3 en **JPG calidad 92, 4:4:4** (~185 KB c/u, cargan rápido en el celular; son los que conviene subir en `/Manage/Slides`).
  - `Fotogramas originales\` → los 3 fotogramas a resolución completa 576×1024 en PNG sin pérdida.
  - `Herramientas\crear_banners.py` → script para regenerar los banners (necesita Python con `opencv-python-headless`, `numpy`, `pillow`).
- Diseño: fondo de marca (azul marino + resplandor naranja + trazos de velocidad como la estela del logo), video nítido en panel inclinado como la cursiva del logo, sin texto dentro de la imagen (el título/botón los escribe la página abajo a la izquierda). Panel centrado en x≈1090 para que no se corte en celular (el carrusel recorta los lados).
- **Límite de calidad:** el video fuente es de solo 576 px de ancho; al ponerlo a 800 px de alto se amplía ~1,56×. Para banners más nítidos usar el archivo original del celular (1080×1920 o mayor) y volver a correr el script.
- **Pendiente del usuario:** subirlos en `/Manage/Slides` (Nueva diapositiva con imagen).

## Pendientes técnicos / próximas pasadas
1. ~~Subida de videos a Mux y reproducción~~ / ~~Playback firmado~~ (hecho y probado por el usuario con sus 2 videos; falta confirmar «Ver»).
2. ~~Suscripciones con vencimiento, PIN, depósito, solicitudes, carrusel~~ (hecho, ver arriba).
3. **Precios y textos reales:** definir precios de los planes (hoy «por definir»), datos de depósito y WhatsApp en `/Manage/Settings`, y textos/imágenes de promoción reales.
4. **Avisos por correo** al administrador cuando llega una solicitud y a los clientes antes de vencer (requiere cuenta de envío SMTP o servicio tipo Brevo/Resend). Hoy el aviso es la insignia en el menú.
5. Pago con **tarjeta** (Stripe u otro) → debe llamar a `AccessService.GrantAsync` con `SubscriptionSource.Card`.
6. Cursos > Niveles > Videos, panel admin de contenido (hoy los videos se listan planos).
7. Seguimiento de % visto y cola de revisión de videos de avance (desbloqueo de nivel).
8. Traducir las páginas de login/registro de Identity al español (hoy en inglés) y que el registro lleve a /Subscribe.
9. PWA instalable (manifest + service worker; íconos ya generados en `wwwroot/img`).
10. **Publicar para el cliente:** túnel Cloudflare para demos o servidor pequeño de pago; en hosting gratuito el disco se borra → mover `DataDir` a un disco persistente o base de datos externa; en producción Mux pasa a environment **Producción** (cobra por minuto).
11. Decidir si el registro sigue abierto o solo por invitación/PIN.
12. Registro de «último acceso» de cada usuario (hoy no se guarda).

## Dónde retomamos
El usuario debe **reiniciar la app** (cerrar la ventana de `INICIAR_PORFI_ACADEMY.bat` y abrirla de nuevo). Al arrancar crea las tablas nuevas en su base real (cambio solo aditivo; no toca usuarios ni credenciales). Luego: abrir `/Manage/Settings` (datos de depósito/WhatsApp), `/Manage/Plans` (precios), crear PIN de prueba, probar canje, crear una promoción con imagen, y subir un corte promocional en «Videos y Mux». Después seguimos con cursos/niveles o con la publicación para la demo.

## Archivos sensibles dentro de OneDrive (acción del usuario)
En esta carpeta (sincronizada a OneDrive) hay archivos con secretos descargados de Mux: `mux-access-token-PorfyAcademy.env` y `mux-signing-key-….pem` (clave privada de firma). Claude **no los abrió**. Ya están en `.gitignore`, pero **moverlos fuera de OneDrive** (gestor de contraseñas o carpeta privada). La app ya tiene copia cifrada en `secrets.dat`.

## Bitácora
- **2026-10-11:** Construido el sistema de suscripciones con vencimiento (planes, PINs, solicitudes, depósito aprobado a mano) y el carrusel promocional de la portada; centro de administración `/Manage`. 18 pruebas automáticas + prueba en navegador OK (ver sección «Suscripciones y promociones»). También: ajustes de proxy para publicar detrás de túnel HTTPS.
- **2026-10-11:** Rebranding a VeloX con el logo y colores de la empresa (ver sección «Marca VeloX»).
- **2026-10-11:** Conversación inicial: mejores prácticas, decisiones de arquitectura, creación de cuenta Mux y access token, creación de esta carpeta y de este archivo.
- **2026-10-11:** El usuario completó la instalación real; `/Admin` confirma Mux «conectado». Programada la pasada de video protegido (subida directa, firma JWT, reproductor, control de acceso por rol); compila y la firma fue verificada con OpenSSL. Pendiente prueba real con Mux.
- **2026-10-11:** Esqueleto técnico construido y verificado (ver secciones arriba). Datos de prueba eliminados. Añadidos `.gitignore` (incluye `*.env`) y `INICIAR_PORFI_ACADEMY.bat`.
