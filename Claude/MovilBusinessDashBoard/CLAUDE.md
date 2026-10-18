# MovilBusiness Dashboard — Instrucciones del proyecto

## Bitácora compartida entre 2 cuentas (PROGRESS.md)
El usuario trabaja con **2 cuentas distintas** de Claude sobre este mismo proyecto. Desde el 2026-10-18 la carpeta vive en `C:\Personales RM\Claude\MovilBusinessDashBoard` (YA NO en OneDrive de la empresa); ambas cuentas deben abrir ESA misma carpeta en este equipo. Para no perder el hilo:

1. **Al INICIAR cualquier sesión/chat de este proyecto:** lee **`PROGRESS.md`** (en la raíz del proyecto) ANTES de empezar, para ponerte al día de qué se hizo, decisiones y pendientes.
2. **Al CERRAR o hacer una PAUSA de la conversación:** actualiza `PROGRESS.md` con:
   - qué se hizo en la sesión,
   - decisiones clave tomadas,
   - pendientes (incl. acciones de TI),
   - “dónde retomamos”,
   - y la fecha de última actualización.
3. **Etiqueta** cada actualización con fecha/hora. No puedes saber por identidad qué cuenta está activa; si el usuario lo dice, anótalo.
4. Este mecanismo se **actualiza al cerrar/pausar cada sesión** (no hay temporizador automático entre mensajes).

`PROGRESS.md` y este `CLAUDE.md` están en esa carpeta local → ambas cuentas los leen/escriben (ya no se sincronizan por OneDrive). La memoria interna de Claude (`~/.claude`) NO se sincroniza entre cuentas/máquinas; por eso el canal compartido son estos archivos del proyecto.

**Respaldos de `C:\Personales RM`:** (a) repositorio privado de GitHub `rmercedesb-alt/personales-rm` (solo código y documentos livianos; `.gitignore` excluye credenciales, `.lic`, `PROGRESS.md` —trae la cadena de conexión—, instaladores y datos pesados; los commits/push los corre el usuario en su terminal); (b) un zip por proyecto en `G:\Mi unidad\Respaldos\AAAA-MM-DD\` (Google Drive, tarea diaria «Respaldo Personales RM a Drive», incluye credenciales). Scripts en `C:\Personales RM\_Respaldo\`.

## Contexto rápido
.NET 8 MVC + SQL Server, sub-app IIS. Proyecto principal: `MovilbusinessDashBoardSmart` (local 5012, `CORRER_LOCAL.bat`). Usuario BD `Ventas` = **solo lectura**; TI crea las tablas `_RM`. Ver `PROGRESS.md` para el estado completo y versión vigente.

## Reglas del usuario
- **No versionar/empaquetar** hasta que el usuario lo pida; acumular en el CHANGELOG “Sin liberar”.
- **Pendientes del usuario (P1, P2, P3…):** viven al inicio de `PROGRESS.md` (sección «📌 PENDIENTES DEL USUARIO»). **Recuérdaselos SIEMPRE**: al iniciar cada sesión (en tu primera respuesta, en una línea por pendiente) y al cerrar/pausar, diciendo cuáles siguen abiertos. Solo se trabajan cuando él lo pida; al completar uno, márcalo `[x]` con fecha (no lo borres).

## Otro proyecto en esta misma carpeta: RutaVisitasSmart
`RutaVisitasSmart/` es una app **independiente** (Node/Express + React/Vite, no .NET)
para mantenimiento/consulta/reportes de `RutaVisitas`. Tiene su **propia bitácora**:
si el trabajo es sobre esa carpeta, lee y actualiza `RutaVisitasSmart/PROGRESS.md`
(no este archivo) — tiene su propio `RutaVisitasSmart/CLAUDE.md` con el mismo
mecanismo de sincronización entre las 2 cuentas.
