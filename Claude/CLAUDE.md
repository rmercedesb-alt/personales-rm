# Personales RM — Instrucciones generales

Carpeta de trabajo: `C:\Personales RM\Claude` (desde el 2026-10-18; ya NO se trabaja en el OneDrive de la empresa). Aquí viven todos los proyectos, incluidos los de la compañía.

## Reglas generales
- **Responde siempre en español.**
- **Dashboard (MovilBusiness):** el proyecto vive en `MovilBusinessDashBoard/` y tiene su propio `CLAUDE.md` y `PROGRESS.md`. Antes de trabajar en él, lee `MovilBusinessDashBoard/CLAUDE.md` y `MovilBusinessDashBoard/PROGRESS.md` (sección «📌 PENDIENTES DEL USUARIO») y sigue sus reglas: no versionar/empaquetar hasta que el usuario lo pida (acumular en el CHANGELOG «Sin liberar»); BD `MovilBusiness5STD` con usuario `Ventas` = solo lectura (TI crea las tablas `_RM`).
- **Pendientes del usuario:** recuérdalos SIEMPRE, en una línea cada uno, al iniciar sesión y al cerrar/pausar (P2 informes Cobros/Depósitos/Fotos/Estudios, P3 presupuesto de Cobros, y los demás de `PROGRESS.md`). Solo se trabajan cuando el usuario lo pida.
- **Otros proyectos con reglas propias:** `RutaVisitasSmart/` y `Porfi Academy/` (dentro de `MovilBusinessDashBoard/`) tienen su `CLAUDE.md`; `Sistema de Condominios/` tiene reglas en la memoria (p. ej., en `web-app-v4` solo se editan archivos: el usuario corre `PUBLICAR_V4.bat`/`INICIAR_V4.bat`).

## Respaldos (no los rompas)
- **GitHub privado** `rmercedesb-alt/personales-rm` (repo en `C:\Personales RM`): solo código y documentos livianos. El `.gitignore` excluye credenciales, `.lic`, llave privada de licencias, `PROGRESS.md`, instaladores y datos pesados. **Los commits y push los corre el usuario en su terminal** (PowerShell 5.1: sin `&&`). Antes de sugerir un commit, verifica que no entren secretos.
- **Google Drive (G:)**: un zip por proyecto en `G:\Mi unidad\Respaldos\AAAA-MM-DD\` (tarea diaria «Respaldo Personales RM a Drive»; scripts en `C:\Personales RM\_Respaldo\`). Este sí incluye credenciales.
- Nunca borres nada del OneDrive de la empresa sin confirmación explícita del usuario.

## Rutas
- Usa siempre `C:\Personales RM\Claude\...`. Las carpetas hermanas de OneDrive (`...\OneDrive - AM COMERCIAL\Rudy\Junio 2026`, `Septiembre 2026`, etc.) no se migraron y los scripts de reuniones aún las leen/escriben ahí.
