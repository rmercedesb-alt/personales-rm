# Porfi Academy — Instrucciones del proyecto

Proyecto **independiente** del dashboard MovilBusiness (aunque la carpeta esté dentro de `MovilBusinessDashBoard`). Ignora las reglas y el stack del dashboard (PROGRESS.md de la raíz, bats, IIS, etc.) salvo que el usuario lo pida.

## Ubicación
Todo lo de este proyecto (código, documentos, notas, planes) se guarda en esta carpeta: `C:\Users\Rudy\OneDrive - AM COMERCIAL\Rudy\Claude\MovilBusinessDashBoard\Porfi Academy`. No crear archivos de Porfi Academy fuera de ella.

## Bitácora (PROGRESS.md)
1. Al iniciar una sesión sobre este proyecto, lee `PROGRESS.md` (en esta carpeta) antes de empezar.
2. **Cada vez que haya un cambio, decisión o avance, actualiza `PROGRESS.md`** (estado, decisiones, "dónde retomamos", bitácora con fecha) sin esperar a que el usuario lo pida.
3. Al cerrar o pausar, deja "Dónde retomamos" al día.

## Reglas
- Seguridad: las credenciales (API Key/Secret de Mux, Stripe, etc.) nunca van en el código ni en el repo ni se piden por chat. Se piden en la pantalla de configuración inicial de la app y se guardan de forma segura (variables de entorno / user-secrets / almacén cifrado).
- Mux: usar el environment **Desarrollo** para pruebas; Producción solo cuando el usuario lo decida.
- Idioma de trabajo con el usuario: español.
