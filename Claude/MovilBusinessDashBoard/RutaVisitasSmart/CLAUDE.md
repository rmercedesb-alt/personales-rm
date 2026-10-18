# RutaVisitasSmart — Instrucciones del proyecto

Este proyecto es independiente de `MovilBusinessDashBoard*` (carpeta hermana, mismo
repo/OneDrive). Tiene su **propia bitácora**, separada de la de los dashboards .NET.

## Bitácora compartida entre 2 cuentas (PROGRESS.md)
El usuario trabaja con **2 cuentas distintas** de Claude sobre este proyecto (carpeta
en OneDrive, se sincroniza entre máquinas/cuentas). Para no perder el hilo:

1. **Al INICIAR cualquier sesión/chat en `RutaVisitasSmart/`:** lee
   **`RutaVisitasSmart/PROGRESS.md`** ANTES de empezar, para ponerte al día de qué se
   hizo, decisiones y pendientes.
2. **Al CERRAR o hacer una PAUSA de la conversación:** actualiza ese `PROGRESS.md` con
   qué se hizo, decisiones clave, pendientes (incl. acciones de TI), dónde se retoma,
   y la fecha de última actualización.
3. Este `CLAUDE.md` y `PROGRESS.md` viven en OneDrive → ambas cuentas los leen/escriben.
   La memoria interna de Claude (`~/.claude`) NO se sincroniza entre cuentas/máquinas;
   por eso el canal compartido son estos archivos del proyecto.

## Contexto rápido
Node + Express + TypeScript (API, solo lectura) + React + TypeScript + Vite + Leaflet.
Conecta directo a la misma BD que los dashboards .NET (`MovilBusiness5STD`, user
`Ventas`, solo lectura por convención de código). Correr con `CORRER_LOCAL.bat`
(API `:4000`, app `:5173`). Ver `PROGRESS.md` para el estado completo y `README.md`
para el modelo de datos y decisiones de arquitectura.

Desde 2026-08-22 la app pide login (usuario/contraseña contra `UsuarioSistema`,
misma tabla que los dashboards .NET — ver `server/src/auth.ts`). Logo/nombre de
empresa salen de la tabla `Empresa`; si no hay conexión, NO se muestra ningún
logo/nombre de respaldo (a propósito).

## Reglas del usuario
- La app **nunca** escribe directo a la BD de producción — cualquier cambio de ruta se
  entrega como Excel con script SQL para que TI lo aplique.
